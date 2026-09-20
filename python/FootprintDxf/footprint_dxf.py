#!/usr/bin/env python3
# ============================================================
# FootprintDxf (Python / ZOS-API)
# ============================================================
# Traces field/pupil footprints onto sequential surfaces and
# exports DXF (+ optional PNG). Twin of the C# User Extension.
# Dialogs are -nodialog only. Self-test is geometry-only.
#
# Flags match C#: -file -out -rays -rimrays -surfaces -includeimage
#   -fields -wave -rim -perfield -global -aperture -nopng
#   -quiet -nodialog -selftest
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from typing import List, Optional, Set, Tuple

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import bootstrap_zosapi, connect_zos, discover_zos_root, parse_flag_token

FILE_PATH = None
OUT_PATH = None
RAYS = 11
RIM_RAYS = 64
SURFACES = "all"
INCLUDE_IMAGE = False
FIELDS = "all"
WAVE = "primary"
RIM = False
PER_FIELD = False
GLOBAL = False
APERTURE = False
NO_PNG = False
QUIET = False
NO_DIALOG = False
SELF_TEST = False
EXPLICIT: Set[str] = set()


def say(s):
    print(s, flush=True)


def parse_int(s, keep):
    try:
        return int(s)
    except Exception:
        say("WARNING: {!r} not int - keeping {}".format(s, keep)); return keep


def parse_args(argv):
    global FILE_PATH, OUT_PATH, RAYS, RIM_RAYS, SURFACES, INCLUDE_IMAGE, FIELDS, WAVE
    global RIM, PER_FIELD, GLOBAL, APERTURE, NO_PNG, QUIET, NO_DIALOG, SELF_TEST
    i = 0
    while i < len(argv):
        raw = argv[i]
        if raw.startswith("-") or raw.startswith("/"):
            a = parse_flag_token(raw)
            def next_tok():
                nonlocal i
                if i + 1 < len(argv):
                    i += 1
                    return argv[i]
                return None
            mapping = {
                "out": ("OUT_PATH", True),
                "file": ("FILE_PATH", True),
                "rays": ("RAYS", True),
                "rimrays": ("RIM_RAYS", True),
                "surfaces": ("SURFACES", True),
                "fields": ("FIELDS", True),
                "wave": ("WAVE", True),
            }
            if a in mapping:
                name, takes = mapping[a]
                val = next_tok() if takes else True
                if a == "rays":
                    globals()[name] = parse_int(val, RAYS)
                elif a == "rimrays":
                    globals()[name] = parse_int(val, RIM_RAYS)
                else:
                    globals()[name] = val
                EXPLICIT.add(a)
            elif a == "includeimage":
                INCLUDE_IMAGE = True; EXPLICIT.add(a)
            elif a == "rim":
                RIM = True; EXPLICIT.add(a)
            elif a == "perfield":
                PER_FIELD = True; EXPLICIT.add(a)
            elif a == "global":
                GLOBAL = True; EXPLICIT.add(a)
            elif a == "aperture":
                APERTURE = True; EXPLICIT.add(a)
            elif a == "nopng":
                NO_PNG = True; EXPLICIT.add(a)
            elif a == "quiet":
                QUIET = True; EXPLICIT.add(a)
            elif a == "nodialog":
                NO_DIALOG = True
            elif a == "selftest":
                SELF_TEST = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1
    if RAYS < 3:
        RAYS = 3
    if RAYS % 2 == 0:
        RAYS += 1
    if "rimrays" in EXPLICIT:
        RIM_RAYS = max(16, min(1024, RIM_RAYS))


def convex_hull(points: List[Tuple[float, float]]) -> List[Tuple[float, float]]:
    """Andrew's monotone chain."""
    pts = sorted(set(points))
    if len(pts) <= 1:
        return pts
    def cross(o, a, b):
        return (a[0]-o[0])*(b[1]-o[1]) - (a[1]-o[1])*(b[0]-o[0])
    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def write_dxf(path: str, layers) -> None:
    """Minimal DXF: layers of closed LWPOLYLINE entities."""
    lines = ["0", "SECTION", "2", "HEADER", "0", "ENDSEC",
             "0", "SECTION", "2", "TABLES", "0", "ENDSEC",
             "0", "SECTION", "2", "ENTITIES"]
    for layer, polys in layers:
        for poly in polys:
            if len(poly) < 2:
                continue
            lines += ["0", "LWPOLYLINE", "8", layer, "90", str(len(poly)), "70", "1"]
            for x, y in poly:
                lines += ["10", "{:.6f}".format(x), "20", "{:.6f}".format(y)]
    lines += ["0", "ENDSEC", "0", "EOF"]
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def write_png(path: str, layers, title: str) -> None:
    from PIL import Image, ImageDraw, ImageFont
    all_pts = [p for _, polys in layers for poly in polys for p in poly]
    if not all_pts:
        return
    xs = [p[0] for p in all_pts]; ys = [p[1] for p in all_pts]
    minx, maxx = min(xs), max(xs); miny, maxy = min(ys), max(ys)
    if maxx - minx < 1e-9: maxx += 1; minx -= 1
    if maxy - miny < 1e-9: maxy += 1; miny -= 1
    W, H, margin = 1000, 800, 40
    scale = min((W-2*margin)/(maxx-minx), (H-2*margin)/(maxy-miny))
    def Map(p):
        return (margin + (p[0]-minx)*scale, H - margin - (p[1]-miny)*scale)
    img = Image.new("RGB", (W, H), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    colors = [(0,0,200),(0,160,0),(200,0,0),(0,160,160),(160,0,160),(160,120,0)]
    for i, (layer, polys) in enumerate(layers):
        c = colors[i % len(colors)]
        for poly in polys:
            if len(poly) >= 2:
                draw.line([Map(p) for p in poly] + [Map(poly[0])], fill=c, width=2)
    try:
        font = ImageFont.load_default()
    except Exception:
        font = None
    draw.text((10, 10), title, fill=(0, 0, 0), font=font)
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    img.save(path, "PNG")


def self_test() -> int:
    """Geometry-only self-test (no OpticStudio)."""
    square = [(0,0),(1,0),(1,1),(0,1),(0.5,0.5)]
    h = convex_hull(square)
    ok = len(h) == 4
    say("self-test convex_hull: " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 2


def resolve_surfaces(sys_, ZOSAPI):
    lde = sys_.LDE
    img = lde.NumberOfSurfaces - 1
    if SURFACES.strip().lower() == "all":
        surfs = list(range(1, img + (1 if INCLUDE_IMAGE else 0)))
    else:
        surfs = []
        for part in SURFACES.replace(";", ",").split(","):
            part = part.strip()
            if not part:
                continue
            if "-" in part:
                a, b = part.split("-", 1)
                surfs.extend(range(int(a), int(b) + 1))
            else:
                surfs.append(int(part))
    return [s for s in surfs if 1 <= s <= img]


def resolve_fields(sys_):
    nf = int(sys_.SystemData.Fields.NumberOfFields)
    if FIELDS.strip().lower() == "all":
        return list(range(1, nf + 1))
    out = []
    for part in FIELDS.replace(";", ",").split(","):
        part = part.strip()
        if part:
            out.append(int(part))
    return out


def resolve_wave(sys_):
    wls = sys_.SystemData.Wavelengths
    if WAVE.strip().lower() == "all":
        return list(range(1, int(wls.NumberOfWavelengths) + 1))
    if WAVE.strip().lower() == "primary":
        for w in range(1, int(wls.NumberOfWavelengths) + 1):
            if wls.GetWavelength(w).IsPrimary:
                return [w]
        return [1]
    return [int(WAVE)]


def trace_footprint(sys_, ZOSAPI, surf, field, wave) -> List[Tuple[float, float]]:
    """Trace a pupil grid (or rim) to (x,y) at surface."""
    fields = sys_.SystemData.Fields
    f = fields.GetField(field)
    max_r = 1e-10
    for i in range(1, fields.NumberOfFields + 1):
        ff = fields.GetField(i)
        max_r = max(max_r, math.sqrt(ff.X*ff.X + ff.Y*ff.Y))
    hx, hy = f.X / max_r, f.Y / max_r
    pts = []
    if RIM:
        n = RIM_RAYS
        rays = [(math.cos(2*math.pi*k/n), math.sin(2*math.pi*k/n)) for k in range(n)]
    else:
        n = RAYS
        rays = []
        for iy in range(n):
            for ix in range(n):
                px = -1 + 2*ix/(n-1)
                py = -1 + 2*iy/(n-1)
                if px*px + py*py <= 1.0001:
                    rays.append((px, py))
    trace = sys_.Tools.OpenBatchRayTrace()
    try:
        data = trace.CreateNormUnpol(len(rays), ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
        for px, py in rays:
            data.AddRay(wave, hx, hy, px, py, getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None"))
        trace.RunAndWaitForCompletion()
        data.StartReadingResults()
        while True:
            try:
                result = data.ReadNextResult()
                if isinstance(result, tuple):
                    vals = list(result)
                    if isinstance(vals[0], bool):
                        if not vals[0]:
                            break
                        vals = vals[1:]
                    err = int(vals[1])
                    x, y = float(vals[3]), float(vals[4])
                    if err == 0:
                        pts.append((x, y))
                else:
                    break
            except Exception:
                break
    finally:
        trace.Close()
    return pts


def run(session):
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        raise RuntimeError("sequential system required")

    surfs = resolve_surfaces(sys_, ZOSAPI)
    fields = resolve_fields(sys_)
    waves = resolve_wave(sys_)
    say("FootprintDxf: surfaces={} fields={} waves={}".format(surfs, fields, waves))

    layers = []
    for surf in surfs:
        try:
            if app.TerminateRequested:
                say("Terminated."); return
        except Exception:
            pass
        polys = []
        if PER_FIELD:
            for fi in fields:
                for w in waves:
                    pts = trace_footprint(sys_, ZOSAPI, surf, fi, w)
                    if pts:
                        polys.append(convex_hull(pts))
        else:
            all_pts = []
            for fi in fields:
                for w in waves:
                    all_pts.extend(trace_footprint(sys_, ZOSAPI, surf, fi, w))
            if all_pts:
                polys.append(convex_hull(all_pts))
        layers.append(("SURF_{}".format(surf), polys))

    out = OUT_PATH
    if not out:
        src = FILE_PATH or (sys_.SystemFile or "")
        if src:
            out = os.path.join(os.path.dirname(src),
                               os.path.splitext(os.path.basename(src))[0] + "_footprint.dxf")
        else:
            out = os.path.join(str(app.ZemaxDataDir), "footprint.dxf")
    if not out.lower().endswith(".dxf"):
        out = out + ".dxf"
    write_dxf(out, layers)
    say("DXF: " + out)
    if not NO_PNG:
        png = os.path.splitext(out)[0] + ".png"
        write_png(png, layers, os.path.basename(out))
        say("PNG: " + png)


def main(argv=None) -> int:
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex)); return 1
    if SELF_TEST:
        return self_test()
    try:
        zos_root = discover_zos_root()
    except Exception as ex:
        print("FATAL: failed to locate an OpticStudio installation.  " + str(ex)); return 1
    session = None
    try:
        ZOSAPI = bootstrap_zosapi(zos_root, say=say)
        session = connect_zos(ZOSAPI, FILE_PATH, say=say, zos_root=zos_root)
        run(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex)); traceback.print_exc(); return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

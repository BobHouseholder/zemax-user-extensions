#!/usr/bin/env python3
# ============================================================
# RayExtentEnvelope (Python / ZOS-API)
# ============================================================
# Builds a keep-out / ray-extent envelope from rim rays and
# exports PNG (and a simple facet mesh as STL; STEP/OCC path
# is C#-primary — see README). Twin of the C# User Extension.
#
# Flags: -file -out -png -step -rimrays -surfaces -width -height
#        -quiet -nodialog -clap/-noclap/-rayextent -vertexz
#        -envelopeonly/-nolenses -lenses
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from typing import List, Optional, Tuple

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import bootstrap_zosapi, connect_zos, discover_zos_root, parse_flag_token

FILE_PATH = None
OUT_PATH = None
WANT_PNG = True
WANT_STEP = False
EXPLICIT_OUTPUTS = False
RIM_RAYS = 64
SURFACES = "auto"
WIDTH = 1200
HEIGHT = 800
QUIET = False
NO_DIALOG = False
NO_CLAP = True  # default ray-extent (not clear aperture)
USE_VERTEX_Z = False
ENVELOPE_ONLY = False


def say(s):
    print(s, flush=True)


def parse_int(s, keep):
    try:
        return int(s)
    except Exception:
        return keep


def parse_args(argv):
    global FILE_PATH, OUT_PATH, WANT_PNG, WANT_STEP, EXPLICIT_OUTPUTS, RIM_RAYS, SURFACES
    global WIDTH, HEIGHT, QUIET, NO_DIALOG, NO_CLAP, USE_VERTEX_Z, ENVELOPE_ONLY
    saw_png = saw_step = False
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
            if a == "file":
                FILE_PATH = next_tok()
            elif a == "out":
                OUT_PATH = next_tok()
            elif a == "png":
                saw_png = True
            elif a == "step":
                saw_step = True
            elif a == "rimrays":
                RIM_RAYS = parse_int(next_tok(), RIM_RAYS)
            elif a == "surfaces":
                SURFACES = next_tok() or "auto"
            elif a == "width":
                WIDTH = parse_int(next_tok(), WIDTH)
            elif a == "height":
                HEIGHT = parse_int(next_tok(), HEIGHT)
            elif a == "quiet":
                QUIET = True
            elif a == "nodialog":
                NO_DIALOG = True
            elif a == "clap":
                NO_CLAP = False
            elif a in ("noclap", "rayextent"):
                NO_CLAP = True
            elif a == "vertexz":
                USE_VERTEX_Z = True
            elif a in ("envelopeonly", "nolenses"):
                ENVELOPE_ONLY = True
            elif a == "lenses":
                ENVELOPE_ONLY = False
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1
    if saw_png or saw_step:
        EXPLICIT_OUTPUTS = True
        WANT_PNG = saw_png
        WANT_STEP = saw_step
    RIM_RAYS = max(16, min(256, RIM_RAYS))
    WIDTH = max(200, WIDTH)
    HEIGHT = max(200, HEIGHT)


def get_frame(lde, surf):
    try:
        result = lde.GetGlobalMatrix(surf)
        if isinstance(result, tuple):
            vals = list(result)
            if isinstance(vals[0], bool):
                vals = vals[1:]
            return [float(v) for v in vals[:12]]
    except Exception:
        pass
    try:
        import clr
        from System import Double
        refs = [clr.Reference[Double]() for _ in range(12)]
        lde.GetGlobalMatrix(surf, *refs)
        return [float(r.Value) for r in refs]
    except Exception:
        return None


def to_global(frame, x, y, z):
    r11,r12,r13,r21,r22,r23,r31,r32,r33,ox,oy,oz = frame
    return (
        r11*x + r12*y + r13*z + ox,
        r21*x + r22*y + r23*z + oy,
        r31*x + r32*y + r33*z + oz,
    )


def resolve_surfaces(sys_):
    lde = sys_.LDE
    img = lde.NumberOfSurfaces - 1
    if SURFACES.strip().lower() == "auto":
        # optical surfaces between stop-ish and image
        return list(range(1, img))
    out = []
    for part in SURFACES.replace(";", ",").split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.extend(range(int(a), int(b) + 1))
        else:
            out.append(int(part))
    return [s for s in out if 1 <= s < img]


def rim_points_at_surface(sys_, ZOSAPI, surf, wave, field=1) -> List[Tuple[float, float, float]]:
    fields = sys_.SystemData.Fields
    f = fields.GetField(field)
    max_r = 1e-10
    for i in range(1, fields.NumberOfFields + 1):
        ff = fields.GetField(i)
        max_r = max(max_r, math.sqrt(ff.X*ff.X + ff.Y*ff.Y))
    hx, hy = f.X / max_r, f.Y / max_r
    n = RIM_RAYS
    rays = [(math.cos(2*math.pi*k/n), math.sin(2*math.pi*k/n)) for k in range(n)]
    pts = []
    frame = get_frame(sys_.LDE, surf)
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
                if not isinstance(result, tuple):
                    break
                vals = list(result)
                if isinstance(vals[0], bool):
                    if not vals[0]:
                        break
                    vals = vals[1:]
                err = int(vals[1])
                x, y, z = float(vals[3]), float(vals[4]), float(vals[5])
                if err == 0:
                    if frame:
                        pts.append(to_global(frame, x, y, z if not USE_VERTEX_Z else 0.0))
                    else:
                        pts.append((x, y, z))
            except Exception:
                break
    finally:
        trace.Close()
    return pts


def write_png_layout(path, envelopes, title):
    from PIL import Image, ImageDraw, ImageFont
    # Project YZ
    all_pts = [(p[2], p[1]) for ring in envelopes for p in ring]
    if not all_pts:
        return
    xs = [p[0] for p in all_pts]; ys = [p[1] for p in all_pts]
    minx, maxx = min(xs), max(xs); miny, maxy = min(ys), max(ys)
    if maxx <= minx: maxx += 1; minx -= 1
    if maxy <= miny: maxy += 1; miny -= 1
    W, H, margin = WIDTH, HEIGHT, 50
    scale = min((W-2*margin)/(maxx-minx), (H-2*margin)/(maxy-miny))
    def Map(p):
        return (margin + (p[0]-minx)*scale, H - margin - (p[1]-miny)*scale)
    img = Image.new("RGB", (W, H), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    for ring in envelopes:
        proj = [Map((p[2], p[1])) for p in ring]
        if len(proj) >= 2:
            draw.line(proj + [proj[0]], fill=(200, 0, 0), width=2)
    try:
        font = ImageFont.load_default()
    except Exception:
        font = None
    draw.text((10, 10), title, fill=(0, 0, 0), font=font)
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    img.save(path, "PNG")


def write_stl_facets(path, envelopes):
    """Simple faceted BREP substitute: loft consecutive rim rings into an STL."""
    if len(envelopes) < 2:
        say("NOTE: need >=2 surfaces for STL loft; skipped.")
        return
    tris = []
    for a, b in zip(envelopes, envelopes[1:]):
        n = min(len(a), len(b))
        if n < 3:
            continue
        for i in range(n):
            j = (i + 1) % n
            tris.append((a[i], a[j], b[j]))
            tris.append((a[i], b[j], b[i]))
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="ascii") as f:
        f.write("solid RayExtentEnvelope\n")
        for t in tris:
            # naive normal
            ax, ay, az = t[0]; bx, by, bz = t[1]; cx, cy, cz = t[2]
            ux, uy, uz = bx-ax, by-ay, bz-az
            vx, vy, vz = cx-ax, cy-ay, cz-az
            nx = uy*vz - uz*vy; ny = uz*vx - ux*vz; nz = ux*vy - uy*vx
            f.write("  facet normal {:.6e} {:.6e} {:.6e}\n".format(nx, ny, nz))
            f.write("    outer loop\n")
            for p in t:
                f.write("      vertex {:.6e} {:.6e} {:.6e}\n".format(*p))
            f.write("    endloop\n  endfacet\n")
        f.write("endsolid RayExtentEnvelope\n")
    say("STL (facet loft, not AP214 STEP): " + path)


def run(session):
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        raise RuntimeError("sequential system required")

    wave = 1
    wls = sys_.SystemData.Wavelengths
    for w in range(1, int(wls.NumberOfWavelengths) + 1):
        if wls.GetWavelength(w).IsPrimary:
            wave = w
            break

    surfs = resolve_surfaces(sys_)
    say("RayExtentEnvelope: surfaces={} rimrays={} mode={}".format(
        surfs, RIM_RAYS, "ray-extent" if NO_CLAP else "clear-aperture"))

    envelopes = []
    for surf in surfs:
        try:
            if app.TerminateRequested:
                say("Terminated."); return
        except Exception:
            pass
        # Merge all fields' rim points at this surface
        pts = []
        nf = int(sys_.SystemData.Fields.NumberOfFields)
        for fi in range(1, nf + 1):
            pts.extend(rim_points_at_surface(sys_, ZOSAPI, surf, wave, fi))
        if pts:
            # Order by angle around centroid in YZ for a closed ring
            cx = sum(p[0] for p in pts)/len(pts)
            cy = sum(p[1] for p in pts)/len(pts)
            cz = sum(p[2] for p in pts)/len(pts)
            pts.sort(key=lambda p: math.atan2(p[1]-cy, p[2]-cz))
            envelopes.append(pts)
            say("  surf {}: {} rim points".format(surf, len(pts)))

    out = OUT_PATH
    if not out:
        src = FILE_PATH or (sys_.SystemFile or "")
        if src:
            out = os.path.join(os.path.dirname(src),
                               os.path.splitext(os.path.basename(src))[0] + "_envelope")
        else:
            out = os.path.join(str(app.ZemaxDataDir), "envelope")
    base = out
    if base.lower().endswith((".png", ".step", ".stp", ".stl")):
        base = os.path.splitext(base)[0]

    if WANT_PNG or not EXPLICIT_OUTPUTS:
        png = base + ".png"
        write_png_layout(png, envelopes, os.path.basename(base))
        say("PNG: " + png)
    if WANT_STEP:
        # Full OCC/AP214 STEP is C#-primary; write STL loft as a usable stand-in
        stl = base + ".stl"
        write_stl_facets(stl, envelopes)
        say("NOTE: Python twin writes STL facet loft instead of OCC AP214 STEP (see README).")


def main(argv=None) -> int:
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex)); return 1
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

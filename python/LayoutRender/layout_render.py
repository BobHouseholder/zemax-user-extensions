#!/usr/bin/env python3
# ============================================================
# LayoutRender (Python / ZOS-API) - what this program does
# ============================================================
# Draws a side-view (Y-Z) picture of your sequential lens and
# saves it as a PNG — with no layout window open. Twin of the
# C# User Extension (Pillow instead of System.Drawing).
#
# Flags: -file -out -rays -width -height -noorient -quiet -nodialog
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import (  # noqa: E402
    bootstrap_zosapi,
    connect_zos,
    discover_zos_root,
    parse_flag_token,
)

FILE_PATH: Optional[str] = None
OUT_PATH: Optional[str] = None
RAYS = 7
WIDTH = 1400
HEIGHT = 900
NO_ORIENT = False
QUIET = False

FIELD_COLORS = [
    (0, 0, 220), (0, 160, 0), (220, 0, 0), (0, 170, 170),
    (190, 0, 190), (180, 150, 0), (90, 90, 90), (255, 128, 0),
]


def say(line: str) -> None:
    print(line, flush=True)


def fmt(template: str, *args) -> str:
    return template.format(*args)


def parse_int(s: Optional[str], keep: int) -> int:
    if s is None:
        return keep
    try:
        return int(s)
    except ValueError:
        say("WARNING: {!r} is not a valid integer - keeping {}.".format(s, keep))
        return keep


def parse_args(argv: List[str]) -> None:
    global FILE_PATH, OUT_PATH, RAYS, WIDTH, HEIGHT, NO_ORIENT, QUIET
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
            elif a == "rays":
                RAYS = parse_int(next_tok(), RAYS)
            elif a == "width":
                WIDTH = parse_int(next_tok(), WIDTH)
            elif a == "height":
                HEIGHT = parse_int(next_tok(), HEIGHT)
            elif a == "noorient":
                NO_ORIENT = True
            elif a in ("quiet", "nodialog"):
                if a == "quiet":
                    QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1
    if RAYS < 2:
        RAYS = 2
    if WIDTH < 200:
        WIDTH = 200
    if HEIGHT < 200:
        HEIGHT = 200


@dataclass
class Frame:
    R: List[List[float]] = field(default_factory=lambda: [[0.0]*3 for _ in range(3)])
    X: float = 0.0
    Y: float = 0.0
    Z: float = 0.0
    Valid: bool = False

    def to_global(self, x: float, y: float, z: float) -> Tuple[float, float, float]:
        R = self.R
        return (
            R[0][0]*x + R[0][1]*y + R[0][2]*z + self.X,
            R[1][0]*x + R[1][1]*y + R[1][2]*z + self.Y,
            R[2][0]*x + R[2][1]*y + R[2][2]*z + self.Z,
        )


@dataclass
class SurfInfo:
    Index: int = 0
    Type: object = None
    Radius: float = float("inf")
    Conic: float = 0.0
    SemiDiameter: float = 0.0
    Pars: List[float] = field(default_factory=lambda: [0.0]*9)
    EffMedium: str = ""
    Frame: Frame = field(default_factory=Frame)
    Section: Optional[List[Tuple[float, float]]] = None


def get_frame(lde, surf: int) -> Frame:
    """Ask OpticStudio for this surface's global matrix."""
    fr = Frame()
    try:
        result = lde.GetGlobalMatrix(surf)
        if isinstance(result, tuple) and len(result) >= 13:
            # (ok, r11..r33, x, y, z) or similar
            vals = list(result)
            if isinstance(vals[0], bool):
                fr.Valid = vals[0]
                vals = vals[1:]
            else:
                fr.Valid = True
            r11,r12,r13,r21,r22,r23,r31,r32,r33,x,y,z = [float(v) for v in vals[:12]]
            fr.R = [[r11,r12,r13],[r21,r22,r23],[r31,r32,r33]]
            fr.X, fr.Y, fr.Z = x, y, z
            return fr
    except Exception:
        pass
    try:
        import clr
        from System import Double, Boolean
        refs = [clr.Reference[Double]() for _ in range(12)]
        ok = bool(lde.GetGlobalMatrix(surf, *refs))
        fr.Valid = ok
        vals = [float(r.Value) for r in refs]
        fr.R = [[vals[0],vals[1],vals[2]],[vals[3],vals[4],vals[5]],[vals[6],vals[7],vals[8]]]
        fr.X, fr.Y, fr.Z = vals[9], vals[10], vals[11]
    except Exception:
        fr.Valid = False
    return fr


def sag(s: SurfInfo, y: float, ZOSAPI) -> float:
    """How far the surface bulges at height y (sag)."""
    ST = ZOSAPI.Editors.LDE.SurfaceType
    if s.Type == ST.Tilted:
        return y * s.Pars[2]
    if s.Type in (ST.Paraxial, getattr(ST, "ParaxialXY", ST.Paraxial)):
        return 0.0
    z = 0.0
    if abs(s.Radius) > 1e10 or s.Radius == 0:
        z = 0.0
    else:
        c = 1.0 / s.Radius
        disc = 1 - (1 + s.Conic) * c * c * y * y
        if disc < 0:
            return float("nan")
        z = c * y * y / (1 + math.sqrt(disc))
    if s.Type == ST.EvenAspheric:
        y2 = y * y
        term = y2
        for p in range(1, 9):
            z += s.Pars[p] * term
            term *= y2
    elif s.Type == ST.OddAsphere:
        ay = abs(y)
        term = ay
        for p in range(1, 9):
            z += s.Pars[p] * term
            term *= ay
    return z


def is_axial_system(surfs: List[SurfInfo], ZOSAPI) -> bool:
    ST = ZOSAPI.Editors.LDE.SurfaceType
    pos_tol, dir_tol = 1e-6, 1e-9
    for s in surfs:
        if s.Type in (ST.CoordinateBreak, ST.Tilted):
            return False
        if not s.Frame.Valid:
            continue
        pos_tol = max(pos_tol, 1e-9 * abs(s.Frame.Z))
        if abs(s.Frame.X) > pos_tol or abs(s.Frame.Y) > pos_tol:
            return False
        if abs(s.Frame.R[0][2]) > dir_tol or abs(s.Frame.R[1][2]) > dir_tol:
            return False
    return True


def read_next_ray(data):
    """Read one batch-ray result; return None at end."""
    try:
        result = data.ReadNextResult()
        if isinstance(result, tuple):
            # (ok, rayNum, err, vig, x,y,z,l,m,n,l2,m2,n2,opd,inten)
            if isinstance(result[0], bool):
                if not result[0]:
                    return None
                return result[1:]
            return result
        if not result:
            return None
    except Exception:
        pass
    try:
        import clr
        from System import Int32, Double
        refs_i = [clr.Reference[Int32]() for _ in range(3)]
        refs_d = [clr.Reference[Double]() for _ in range(11)]
        ok = bool(data.ReadNextResult(*(refs_i + refs_d)))
        if not ok:
            return None
        return [r.Value for r in refs_i] + [r.Value for r in refs_d]
    except Exception:
        return None


def draw_png(surf_lines, edge_lines, fans, out_path: str, title: str) -> None:
    """Paint layout primitives to a PNG via Pillow."""
    from PIL import Image, ImageDraw, ImageFont

    min_x = min_y = float("inf")
    max_x = max_y = float("-inf")
    for line in list(surf_lines) + list(edge_lines) + [f[0] for f in fans]:
        for px, py in line:
            min_x = min(min_x, px); max_x = max(max_x, px)
            min_y = min(min_y, py); max_y = max(max_y, py)
    if min_x >= max_x:
        min_x -= 1; max_x += 1
    if min_y >= max_y:
        min_y -= 1; max_y += 1

    W, H, margin, footer = WIDTH, HEIGHT, 60, 50
    scale = min((W - 2*margin)/(max_x-min_x), (H - 2*margin - footer)/(max_y-min_y))
    ox = margin - min_x*scale + (W - 2*margin - (max_x-min_x)*scale)/2
    oy = H - margin - footer + min_y*scale - (H - 2*margin - footer - (max_y-min_y)*scale)/2

    def Map(p):
        return (ox + p[0]*scale, oy - p[1]*scale)

    img = Image.new("RGB", (W, H), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    for pts, field in fans:
        if len(pts) < 2:
            continue
        color = FIELD_COLORS[field % len(FIELD_COLORS)]
        mapped = [Map(p) for p in pts]
        draw.line(mapped, fill=color, width=1)
    for line in surf_lines + edge_lines:
        if len(line) >= 2:
            draw.line([Map(p) for p in line], fill=(0, 0, 0), width=2)

    span_v = max_x - min_x
    bar = 10 ** math.floor(math.log10(span_v * 0.25)) if span_v > 0 else 1.0
    if span_v * 0.25 / bar >= 5:
        bar *= 5
    elif span_v * 0.25 / bar >= 2:
        bar *= 2
    bx0, by = margin, H - footer + 8
    draw.line([(bx0, by), (bx0 + bar*scale, by)], fill=(0,0,0), width=2)
    draw.line([(bx0, by-4), (bx0, by+4)], fill=(0,0,0), width=2)
    draw.line([(bx0+bar*scale, by-4), (bx0+bar*scale, by+4)], fill=(0,0,0), width=2)
    try:
        font = ImageFont.load_default()
    except Exception:
        font = None
    draw.text((bx0 + bar*scale + 8, by - 10), "{0:.4g} lens units".format(bar), fill=(0,0,0), font=font)
    draw.text((margin, H - footer + 24), title + "  -  Y-Z layout (LayoutRender Python twin)", fill=(0,0,0), font=font)

    parent = os.path.dirname(out_path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    img.save(out_path, "PNG")


def render_system(session) -> None:
    """Build glass/ray polylines and paint them into a PNG."""
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        raise RuntimeError("this extension requires a sequential system")

    lde = sys_.LDE
    img_idx = lde.NumberOfSurfaces - 1
    say("Rendering: " + (sys_.SystemFile or "(untitled)"))
    ST = ZOSAPI.Editors.LDE.SurfaceType

    surfs: List[SurfInfo] = []
    prev_medium = ""
    for i in range(1, img_idx + 1):
        row = lde.GetSurfaceAt(i)
        s = SurfInfo(Index=i, Type=row.Type)
        try:
            s.Radius = float(row.Radius)
        except Exception:
            s.Radius = float("inf")
        try:
            s.Conic = float(row.Conic)
        except Exception:
            s.Conic = 0.0
        if abs(s.Conic) > 1e10:
            s.Conic = 0.0
        try:
            s.SemiDiameter = float(row.SemiDiameter)
        except Exception:
            s.SemiDiameter = 0.0
        for p in range(1, 9):
            try:
                col = getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p))
                s.Pars[p] = float(row.GetSurfaceCell(col).DoubleValue)
            except Exception:
                s.Pars[p] = 0.0
        mat = (row.Material or "").strip()
        if mat == "-" or (s.Type == ST.CoordinateBreak and not mat):
            s.EffMedium = prev_medium
        else:
            s.EffMedium = "" if mat.upper() == "MIRROR" else mat
        prev_medium = s.EffMedium
        s.Frame = get_frame(lde, i)
        surfs.append(s)

    for s in surfs:
        if s.Type == ST.CoordinateBreak or not s.Frame.Valid:
            continue
        y_cen, y_half = 0.0, s.SemiDiameter
        try:
            ad = lde.GetSurfaceAt(s.Index).ApertureData
            st = ad.CurrentTypeSettings
            at = ad.CurrentType
            SAT = ZOSAPI.Editors.LDE.SurfaceApertureTypes
            if at == SAT.RectangularAperture:
                y_cen = float(st.ApertureYDecenter); y_half = float(st.YHalfWidth)
            elif at == SAT.CircularAperture:
                y_cen = float(st.ApertureYDecenter); y_half = float(st.MaximumRadius)
            elif at == SAT.EllipticalAperture:
                y_cen = float(st.ApertureYDecenter); y_half = float(st.YHalfWidth)
        except Exception:
            pass
        if y_half < 1e-9:
            continue
        pts = []
        n_steps = 40
        for k in range(n_steps + 1):
            y = y_cen - y_half + 2.0 * y_half * k / n_steps
            z = sag(s, y, ZOSAPI)
            if math.isnan(z):
                continue
            gx, gy, gz = s.Frame.to_global(0, y, z)
            pts.append((gz, gy))
        if len(pts) >= 2:
            s.Section = pts

    fields = sys_.SystemData.Fields
    nf = int(fields.NumberOfFields)
    max_r = 1e-10
    for i in range(1, nf + 1):
        f = fields.GetField(i)
        max_r = max(max_r, math.sqrt(f.X * f.X + f.Y * f.Y))
    wave = 1
    try:
        wls = sys_.SystemData.Wavelengths
        for w in range(1, int(wls.NumberOfWavelengths) + 1):
            if wls.GetWavelength(w).IsPrimary:
                wave = w
                break
    except Exception:
        pass

    rays_per_fan = RAYS
    n_rays = nf * rays_per_fan
    ray_pts = [[[] for _ in range(rays_per_fan)] for _ in range(nf)]
    ray_alive = [[True] * rays_per_fan for _ in range(nf)]

    for surf in range(1, img_idx + 1):
        try:
            if app.TerminateRequested:
                say("Terminated by user - no image written.")
                return
        except Exception:
            pass
        frame = surfs[surf - 1].Frame
        trace = sys_.Tools.OpenBatchRayTrace()
        try:
            data = trace.CreateNormUnpol(n_rays, ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
            for fi in range(nf):
                f = fields.GetField(fi + 1)
                hx, hy = f.X / max_r, f.Y / max_r
                for r in range(rays_per_fan):
                    py = 0 if rays_per_fan == 1 else -1.0 + 2.0 * r / (rays_per_fan - 1)
                    data.AddRay(wave, hx, hy, 0, py, getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None"))
            trace.RunAndWaitForCompletion()
            data.StartReadingResults()
            idx = 0
            while True:
                row = read_next_ray(data)
                if row is None:
                    break
                err_code = int(row[1])
                x, y, z = float(row[3]), float(row[4]), float(row[5])
                fi, r = idx // rays_per_fan, idx % rays_per_fan
                idx += 1
                if fi >= nf:
                    break
                if not ray_alive[fi][r]:
                    continue
                if err_code != 0:
                    ray_alive[fi][r] = False
                    continue
                if frame.Valid:
                    gx, gy, gz = frame.to_global(x, y, z)
                    ray_pts[fi][r].append((gz, gy))
        finally:
            trace.Close()

    surf_lines = [s.Section for s in surfs if s.Section]
    edge_lines = []
    for i in range(len(surfs) - 1):
        a = surfs[i]
        if not a.EffMedium or not a.Section:
            continue
        for j in range(i + 1, len(surfs)):
            if surfs[j].Section is None:
                continue
            edge_lines.append([a.Section[0], surfs[j].Section[0]])
            edge_lines.append([a.Section[-1], surfs[j].Section[-1]])
            break

    fans = []
    for fi in range(nf):
        for r in range(rays_per_fan):
            if len(ray_pts[fi][r]) >= 2:
                fans.append((ray_pts[fi][r], fi))

    all_pts = [p for f, _ in fans for p in f]
    if not NO_ORIENT and not is_axial_system(surfs, ZOSAPI) and len(all_pts) > 4:
        mz = sum(p[0] for p in all_pts) / len(all_pts)
        my = sum(p[1] for p in all_pts) / len(all_pts)
        szz = syy = szy = 0.0
        for p in all_pts:
            szz += (p[0]-mz)**2; syy += (p[1]-my)**2; szy += (p[0]-mz)*(p[1]-my)
        ang = 0.5 * math.atan2(2*szy, szz - syy)
        if abs(ang) > 0.02:
            ca, sa = math.cos(-ang), math.sin(-ang)
            def Rot(p):
                return (mz + (p[0]-mz)*ca - (p[1]-my)*sa, my + (p[0]-mz)*sa + (p[1]-my)*ca)
            for pts, _ in fans:
                for i in range(len(pts)):
                    pts[i] = Rot(pts[i])
            for s in surfs:
                if s.Section:
                    s.Section = [Rot(p) for p in s.Section]
            for line in edge_lines:
                for i in range(len(line)):
                    line[i] = Rot(line[i])
            say(fmt("Auto-oriented layout by {0:.1f} degrees to level the beam axis.", ang * 180 / math.pi))

    out_path = OUT_PATH
    if not out_path:
        src = FILE_PATH or (sys_.SystemFile or "")
        if not src:
            out_path = os.path.join(str(app.ZemaxDataDir), "layout.png")
        else:
            out_path = os.path.join(
                os.path.dirname(src),
                os.path.splitext(os.path.basename(src))[0] + "_layout.png",
            )
    title = os.path.basename(sys_.SystemFile or "(untitled)")
    draw_png(surf_lines, edge_lines, fans, out_path, title)
    say("Layout written to: " + out_path)
    try:
        app.ProgressMessage = "Done. Layout written to " + os.path.basename(out_path)
    except Exception:
        pass


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
        render_system(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex)); traceback.print_exc(); return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# ============================================================
# ReverseSystem (Python / ZOS-API) - what this program does
# ============================================================
# Flips the whole sequential lens end-for-end so light goes the
# other way. Handles coordinate breaks and negative thicknesses
# better than OpticStudio's built-in Reverse Elements.
# Twin of the C# User Extension.
#
# Flags: -file -out -save -inplace -apply -keepconj -refocus -keepaperture
#        -georeport -rayaim -quiet -nodialog
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import List, Optional

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

SAVE_COPY = False
KEEP_CONJUGATES = False
REFOCUS = False
KEEP_APERTURE = False
GEO_REPORT = False
RAY_AIM = False
QUIET = False
IN_PLACE = False  # reverse live primary only with -inplace/-apply
FILE_PATH: Optional[str] = None
OUT_PATH: Optional[str] = None
REPORT: List[str] = []


@dataclass
class RowSnap:
    old_index: int = 0
    type: object = None
    type_name: str = ""
    radius: float = float("inf")
    conic: float = 0.0
    thickness: float = 0.0
    material: str = ""
    comment: str = ""
    pars: List[float] = field(default_factory=lambda: [0.0] * 9)
    is_stop: bool = False
    semi_diameter: float = 0.0
    sd_automatic: bool = True


def say(line: str) -> None:
    print(line, flush=True)
    REPORT.append(line)


def fmt(t, *a):
    return t.format(*a)


def parse_args(argv):
    global SAVE_COPY, KEEP_CONJUGATES, REFOCUS, KEEP_APERTURE, GEO_REPORT
    global RAY_AIM, QUIET, IN_PLACE, FILE_PATH, OUT_PATH
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
            if a == "save":
                SAVE_COPY = True
            elif a in ("inplace", "apply"):
                IN_PLACE = True  # reverse live primary (dangerous)
            elif a == "keepconj":
                KEEP_CONJUGATES = True
            elif a == "refocus":
                REFOCUS = True
            elif a == "keepaperture":
                KEEP_APERTURE = True
            elif a == "georeport":
                GEO_REPORT = True
            elif a == "rayaim":
                RAY_AIM = True
            elif a == "file":
                FILE_PATH = next_tok()
            elif a == "out":
                OUT_PATH = next_tok()
            elif a in ("quiet", "nodialog"):
                if a == "quiet":
                    QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1


def is_supported(t, ST) -> bool:
    return t in (
        ST.Standard, ST.CoordinateBreak, ST.EvenAspheric, ST.OddAsphere,
        ST.Tilted, ST.Paraxial,
    )


def params_used(t, ST):
    if t == ST.CoordinateBreak:
        return range(1, 7)
    if t in (ST.EvenAspheric, ST.OddAsphere):
        return range(1, 9)
    if t == ST.Tilted:
        return range(1, 3)
    if t == ST.Paraxial:
        return range(1, 3)
    return []


def get_par(row, p, ZOSAPI):
    col = getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p))
    return row.GetSurfaceCell(col)


def get_global_matrix(lde, i):
    try:
        result = lde.GetGlobalMatrix(i)
        if isinstance(result, tuple):
            vals = list(result)
            if isinstance(vals[0], bool):
                ok = vals[0]
                vals = vals[1:]
            else:
                ok = True
            return ok, [float(v) for v in vals[:12]]
    except Exception:
        pass
    try:
        import clr
        from System import Double
        refs = [clr.Reference[Double]() for _ in range(12)]
        ok = bool(lde.GetGlobalMatrix(i, *refs))
        return ok, [float(r.Value) for r in refs]
    except Exception:
        return False, [0.0] * 12


def op_val(sys_, ZOSAPI, name, *args):
    t = getattr(ZOSAPI.Editors.MFE.MeritOperandType, name)
    return float(sys_.MFE.GetOperandValue(t, *args))


def snapshot(sys_, ZOSAPI):
    out = {}
    try:
        out["MF"] = float(sys_.MFE.GetMeritFunctionValue())
    except Exception:
        out["MF"] = float("nan")
    for n in ("EFFL", "TOTR", "PMAG"):
        try:
            out[n] = op_val(sys_, ZOSAPI, n, 0, 0, 0, 0, 0, 0, 0, 0)
        except Exception:
            out[n] = float("nan")
    return out


def run_on_system(session) -> None:
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    ST = ZOSAPI.Editors.LDE.SurfaceType
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        say("This extension requires a sequential system.")
        return

    lde = sys_.LDE

    # ---------------------------------------------------------------
    # Safety gate (plain words) — mirror of C#:
    # Do not reverse the attached PrimarySystem in place by default.
    # Require -inplace/-apply, or -save/-out to reverse a CopySystem
    # and write a file. -georeport is read-only and skips this gate.
    # ---------------------------------------------------------------
    standalone = bool(FILE_PATH)
    want_file = SAVE_COPY or bool(OUT_PATH)
    built_on_copy = False
    if not GEO_REPORT:
        if not standalone and not IN_PLACE and not want_file:
            say("FATAL: ReverseSystem refuses to reverse the attached PrimarySystem in place.")
            say("  Pass -save and/or -out <path> to reverse a CopySystem and write a file,")
            say("  or pass -inplace / -apply to reverse the live open lens on purpose (dangerous).")
            raise RuntimeError("refusing attach+mutate without -inplace/-apply or -save/-out")
        if not IN_PLACE and want_file and not standalone:
            copy = sys_.CopySystem()
            if copy is None:
                raise RuntimeError("CopySystem() returned null; pass -inplace to mutate PrimarySystem.")
            sys_ = copy
            session.TheSystem = copy
            built_on_copy = True
            say("Working on CopySystem() clone (open PrimarySystem left unchanged).")
        elif IN_PLACE and not standalone:
            say("WARNING: -inplace/-apply: reversing the live PrimarySystem in place.")

    img_idx = lde.NumberOfSurfaces - 1
    say("")
    say("=== Reverse System ===")
    say("Lens file : " + (sys_.SystemFile or "(untitled)"))
    say(fmt("Surfaces  : {0} (OBJ=0 .. IMA={1})", lde.NumberOfSurfaces, img_idx))
    say("Conjugates: " + (
        "object/image gaps kept in place (-keepconj)"
        if KEEP_CONJUGATES else "true reversal (conjugate states swap)"
    ))

    if img_idx < 2:
        say("Nothing to reverse (no surfaces between object and image).")
        return

    if GEO_REPORT:
        say("")
        say("Global surface geometry (vertex, local X axis, local Z axis):")
        say("  #   type               vertex (x,y,z)                    xAxis (x,y,z)                  zAxis (x,y,z)")
        for i in range(img_idx + 1):
            ok, v = get_global_matrix(lde, i)
            r11, r12, r13, r21, r22, r23, r31, r32, r33, x, y, z = v
            say(fmt(
                "  {0:<3} {1:<18} {2:10.5f} {3:10.5f} {4:10.5f}   {5:8.5f} {6:8.5f} {7:8.5f}   {8:8.5f} {9:8.5f} {10:8.5f}{11}",
                i, lde.GetSurfaceAt(i).TypeName, x, y, z, r11, r21, r31, r13, r23, r33,
                "" if ok else "  (!)",
            ))
        try:
            app.ProgressMessage = "Done. Geometry report only - no changes made."
        except Exception:
            pass
        return

    problems = []
    for i in range(1, img_idx):
        row = lde.GetSurfaceAt(i)
        if not is_supported(row.Type, ST):
            problems.append(fmt("  surface {0}: unsupported type '{1}'", i, row.TypeName))
    if sys_.MCE.NumberOfConfigurations > 1:
        stale = []
        for i in range(1, sys_.MCE.NumberOfOperands + 1):
            op = sys_.MCE.GetOperandAt(i)
            tn = str(op.Type)
            if any(k in tn for k in ("THIC", "GLSS", "CRVT", "CONN", "SDIA", "COTN", "IGNR", "PRAM", "STPS")):
                stale.append(fmt("  MCE operand {0} ({1}) references a surface number", i, op.TypeName))
        if stale:
            problems.extend(stale)
        else:
            say("WARNING: multi-configuration system; only configuration-independent data is affected by the reversal.")
    if problems:
        say("Cannot reverse this system:")
        for p in problems:
            say(p)
        say("Supported: Standard, Coordinate Break, Even/Odd Asphere, Tilted, Paraxial.")
        return

    before = snapshot(sys_, ZOSAPI)

    # Freeze solves
    frozen = 0
    freeze_cols = [ZOSAPI.Editors.LDE.SurfaceColumn.Radius,
                   ZOSAPI.Editors.LDE.SurfaceColumn.Thickness,
                   ZOSAPI.Editors.LDE.SurfaceColumn.Material,
                   ZOSAPI.Editors.LDE.SurfaceColumn.Conic]
    for p in range(1, 9):
        freeze_cols.append(getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p)))
    for i in range(1, img_idx):
        row = lde.GetSurfaceAt(i)
        for col in freeze_cols:
            try:
                cell = row.GetSurfaceCell(col)
                st = cell.Solve
                if st not in (ZOSAPI.Editors.SolveType.Fixed, getattr(ZOSAPI.Editors.SolveType, "None"),
                              ZOSAPI.Editors.SolveType.Automatic):
                    if cell.MakeSolveFixed():
                        frozen += 1
            except Exception:
                pass
    if frozen:
        say(fmt("Froze {0} solve(s) (pickups/variables) to their current values.", frozen))

    # Snapshot rows
    snaps = [None] * (img_idx + 1)
    for i in range(img_idx + 1):
        row = lde.GetSurfaceAt(i)
        s = RowSnap(
            old_index=i, type=row.Type, type_name=row.TypeName,
            thickness=float(row.Thickness),
            material=(row.Material or "").strip(),
            comment=row.Comment or "",
            is_stop=bool(row.IsStop),
        )
        try:
            s.radius = float(row.Radius)
        except Exception:
            s.radius = float("inf")
        try:
            s.conic = float(row.Conic)
        except Exception:
            s.conic = 0.0
        if abs(s.conic) > 1e10 or math.isnan(s.conic):
            s.conic = 0.0
        for p in params_used(s.type, ST):
            cell = get_par(row, p, ZOSAPI)
            try:
                s.pars[p] = float(cell.DoubleValue)
            except Exception:
                try:
                    s.pars[p] = float(cell.IntegerValue)
                except Exception:
                    s.pars[p] = 0.0
        try:
            s.semi_diameter = float(row.SemiDiameter)
        except Exception:
            s.semi_diameter = 0.0
        try:
            s.sd_automatic = row.SemiDiameterCell.Solve == ZOSAPI.Editors.SolveType.Automatic
        except Exception:
            s.sd_automatic = True
        snaps[i] = s

    eff = [""] * (img_idx + 1)
    is_mirror = [False] * (img_idx + 1)
    mirror_count = 0
    for i in range(img_idx + 1):
        m = snaps[i].material
        is_mirror[i] = m.upper() == "MIRROR"
        if is_mirror[i] and 1 <= i < img_idx:
            mirror_count += 1
        if m == "-" or is_mirror[i] or (snaps[i].type == ST.CoordinateBreak and not m):
            eff[i] = eff[i - 1] if i > 0 else ""
        else:
            eff[i] = m
    mirror_sign = -1.0 if (mirror_count % 2 == 1) else 1.0
    if mirror_count:
        say(fmt("Reflective system: {0} mirror surface(s) ({1}) - interior gap signs {2}.",
                mirror_count, "odd" if mirror_count % 2 else "even",
                "flip" if mirror_sign < 0 else "are preserved"))

    old_stop = int(lde.StopSurface)
    primary_wave = 1
    try:
        wls = sys_.SystemData.Wavelengths
        for w in range(1, int(wls.NumberOfWavelengths) + 1):
            if wls.GetWavelength(w).IsPrimary:
                primary_wave = w
                break
    except Exception:
        pass

    stop_clear_sd = 0.0
    if 1 <= old_stop < img_idx:
        stop_clear_sd = snaps[old_stop].semi_diameter
        try:
            r1 = abs(op_val(sys_, ZOSAPI, "PARY", old_stop, primary_wave, 0, 0, 0, 1, 0, 0))
            r2 = abs(op_val(sys_, ZOSAPI, "PARX", old_stop, primary_wave, 0, 0, 1, 0, 0, 0))
            traced = max(r1, r2)
            if traced > 1e-12 and not math.isnan(traced):
                stop_clear_sd = traced
        except Exception:
            pass

    obj_collimated = snaps[0].thickness > 1e10
    img_collimated = False
    focus_from_last = mirror_sign * snaps[img_idx - 1].thickness
    sys_len = sum(abs(snaps[i].thickness) for i in range(1, img_idx) if abs(snaps[i].thickness) < 1e8)
    collim_limit = max(50.0 * sys_len, 500.0)
    try:
        img_collimated = bool(sys_.SystemData.Aperture.AFocalImageSpace)
    except Exception:
        pass
    if not img_collimated:
        try:
            yI = op_val(sys_, ZOSAPI, "REAY", img_idx, primary_wave, 0, 0, 0, 1, 0, 0)
            mY = op_val(sys_, ZOSAPI, "REAB", img_idx, primary_wave, 0, 0, 0, 1, 0, 0)
            nY = op_val(sys_, ZOSAPI, "REAC", img_idx, primary_wave, 0, 0, 0, 1, 0, 0)
            xI = op_val(sys_, ZOSAPI, "REAX", img_idx, primary_wave, 0, 0, 1, 0, 0, 0)
            lX = op_val(sys_, ZOSAPI, "REAA", img_idx, primary_wave, 0, 0, 1, 0, 0, 0)
            nX = op_val(sys_, ZOSAPI, "REAC", img_idx, primary_wave, 0, 0, 1, 0, 0, 0)
            uY = mY / nY if abs(nY) > 1e-14 else 0
            uX = lX / nX if abs(nX) > 1e-14 else 0
            last_gap = snaps[img_idx - 1].thickness
            focY = mirror_sign * (last_gap - yI / uY) if abs(uY) > 1e-14 else float("inf")
            focX = mirror_sign * (last_gap - xI / uX) if abs(uX) > 1e-14 else float("inf")
            colY = abs(focY) > collim_limit
            colX = abs(focX) > collim_limit
            say("")
            say(fmt("Image-space real ray fans: x-fan {0}; y-fan {1}",
                    "collimated" if colX else fmt("focus {0:.6g}", focX),
                    "collimated" if colY else fmt("focus {0:.6g}", focY)))
            if abs(yI) >= abs(xI):
                img_collimated, focus_from_last = colY, focY
            else:
                img_collimated, focus_from_last = colX, focX
        except Exception:
            pass

    say("")
    say("Object space (original): " + (
        "collimated (object at infinity)" if obj_collimated
        else fmt("point source {0:.6g} before the first surface", snaps[0].thickness)))
    say("Image space  (original): " + (
        "COLLIMATED (dominant real-ray fan)" if img_collimated
        else fmt("converges to a focus {0:.6g} after the last surface", focus_from_last)))

    odd_mirrors = mirror_sign < 0
    new_type = [None] * img_idx
    new_radius = [0.0] * img_idx
    new_conic = [0.0] * img_idx
    new_pars = [None] * img_idx
    new_comment = [""] * img_idx
    new_thick = [0.0] * img_idx
    new_eff = [""] * img_idx
    src_of = [0] * img_idx

    for k in range(1, img_idx):
        src = snaps[img_idx - k]
        src_of[k] = src.old_index
        new_type[k] = src.type
        new_comment[k] = src.comment
        new_conic[k] = src.conic
        new_radius[k] = src.radius if (odd_mirrors or abs(src.radius) > 1e10 or src.radius == 0) else -src.radius
        pars = src.pars[:]
        if src.type == ST.CoordinateBreak:
            if odd_mirrors:
                pars[1] = -pars[1]
                pars[4] = -pars[4]
            else:
                pars[1] = -pars[1]
                pars[2] = -pars[2]
                pars[5] = -pars[5]
            pars[6] = 1 if pars[6] == 0 else 0
        elif src.type in (ST.EvenAspheric, ST.OddAsphere):
            if not odd_mirrors:
                for p in range(1, 9):
                    pars[p] = -pars[p]
        elif src.type == ST.Tilted:
            if odd_mirrors:
                pars[2] = -pars[2]
            else:
                pars[1] = -pars[1]
                pars[2] = -pars[2]
        new_pars[k] = pars
        new_thick[k] = (mirror_sign * snaps[img_idx - 1 - k].thickness) if k <= img_idx - 2 else snaps[img_idx - 1].thickness
        new_eff[k] = eff[img_idx - 1 - k] if k <= img_idx - 2 else eff[img_idx - 1]

    if not KEEP_CONJUGATES:
        new_t0 = 1e18 if img_collimated else focus_from_last
        lde.GetSurfaceAt(0).Thickness = new_t0
        new_thick[img_idx - 1] = (
            snaps[img_idx - 1].thickness if obj_collimated else mirror_sign * snaps[0].thickness
        )
        say("")
        say("Reversed object space  : " + (
            "collimated (object set to infinity)" if img_collimated
            else fmt("point source {0:.6g} before the first surface", new_t0)))
        say("Reversed image space   : " + (
            fmt("collimated; image plane is an evaluation plane {0:.6g} after the last surface",
                new_thick[img_idx - 1]) if obj_collimated
            else fmt("focuses {0:.6g} after the last surface (the original object location)",
                     new_thick[img_idx - 1])))

    write_errors = []
    for k in range(1, img_idx):
        row = lde.GetSurfaceAt(k)
        try:
            if row.Type != new_type[k]:
                try:
                    row.Conic = 0
                except Exception:
                    pass
                if not row.ChangeType(row.GetSurfaceTypeSettings(new_type[k])):
                    raise RuntimeError("ChangeType to " + str(new_type[k]) + " failed")
            is_cb = new_type[k] == ST.CoordinateBreak
            if not is_cb:
                try:
                    row.Radius = new_radius[k]
                except Exception:
                    pass
                try:
                    row.Conic = new_conic[k]
                except Exception:
                    pass
            row.Thickness = new_thick[k]
            row.Comment = new_comment[k]
            for p in params_used(new_type[k], ST):
                cell = get_par(row, p, ZOSAPI)
                try:
                    cell.DoubleValue = new_pars[k][p]
                except Exception:
                    try:
                        cell.IntegerValue = int(round(new_pars[k][p]))
                    except Exception as ex:
                        write_errors.append(fmt("row {0} Par{1}: {2}", k, p, ex))
            if not is_cb:
                src_snap = snaps[src_of[k]]
                if src_snap.sd_automatic:
                    try:
                        sd = row.SemiDiameterCell
                        if sd.Solve != ZOSAPI.Editors.SolveType.Automatic:
                            sd.SetSolveData(sd.CreateSolveType(ZOSAPI.Editors.SolveType.Automatic))
                    except Exception:
                        pass
                else:
                    try:
                        row.SemiDiameter = src_snap.semi_diameter
                    except Exception:
                        pass
                want = "MIRROR" if is_mirror[src_of[k]] else new_eff[k]
                current = (row.Material or "").strip()
                if current.lower() != want.lower():
                    row.Material = want
        except Exception as ex:
            write_errors.append(fmt("row {0}: {1}", k, ex))

    if 1 <= old_stop < img_idx:
        new_stop = img_idx - old_stop
        tries = 0
        while (new_stop < len(new_type) and new_type[new_stop] == ST.CoordinateBreak
               and new_stop > 1 and tries < img_idx):
            new_stop -= 1
            tries += 1
        try:
            lde.GetSurfaceAt(new_stop).IsStop = True
            say(fmt("Stop surface: {0} -> {1}", old_stop, new_stop))
        except Exception as ex:
            write_errors.append("stop: " + str(ex))

        if not KEEP_APERTURE and stop_clear_sd > 1e-12:
            try:
                aperture = sys_.SystemData.Aperture
                aperture.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.FloatByStopSize
                # Pin stop clear SD
                try:
                    stop_row = lde.GetSurfaceAt(new_stop)
                    stop_row.SemiDiameter = stop_clear_sd
                except Exception:
                    pass
                say(fmt("Aperture set to Float By Stop Size (stop clear SD {0:.6g})", stop_clear_sd))
            except Exception as ex:
                write_errors.append("aperture: " + str(ex))

    if RAY_AIM:
        try:
            sys_.SystemData.RayAiming.RayAiming = ZOSAPI.SystemData.RayAimingMethod.Paraxial
            say("Ray aiming set to Paraxial.")
        except Exception as ex:
            write_errors.append("rayaim: " + str(ex))

    if REFOCUS:
        try:
            qf = sys_.Tools.OpenQuickFocus()
            try:
                qf.RunAndWaitForCompletion()
            finally:
                qf.Close()
            say("Quick Focus applied (-refocus).")
        except Exception as ex:
            write_errors.append("refocus: " + str(ex))

    after = snapshot(sys_, ZOSAPI)
    say("")
    say("Metrics before -> after:")
    for k in sorted(set(before) | set(after)):
        say(fmt("  {0}: {1:.6g} -> {2:.6g}", k, before.get(k, float("nan")), after.get(k, float("nan"))))

    for e in write_errors:
        say("WARNING: " + e)

    saved_to = ""
    if SAVE_COPY or OUT_PATH or built_on_copy:
        path = OUT_PATH
        if not path:
            src = sys_.SystemFile or "system.zmx"
            path = os.path.join(
                os.path.dirname(src) or ".",
                os.path.splitext(os.path.basename(src))[0] + "_reversed.zmx",
            )
        path = os.path.abspath(path)
        parent = os.path.dirname(path)
        if parent:
            os.makedirs(parent, exist_ok=True)
        sys_.SaveAs(path)
        saved_to = path
        say("Saved: " + path)

    # Report file
    report_path = ""
    if saved_to:
        report_path = os.path.splitext(saved_to)[0] + "_reverse_report.txt"
    elif sys_.SystemFile:
        report_path = os.path.join(
            os.path.dirname(sys_.SystemFile),
            os.path.splitext(os.path.basename(sys_.SystemFile))[0] + "_reverse_report.txt",
        )
    if report_path:
        with open(report_path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(REPORT) + "\n")
        say("Report: " + report_path)

    try:
        app.ProgressMessage = "Done. System reversed."
        app.ProgressPercent = 100
    except Exception:
        pass

    if built_on_copy:
        try:
            sys_.Close(False)
        except Exception:
            pass


def main(argv=None) -> int:
    global REPORT
    REPORT = []
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
        try:
            session.app.ShowChangesInUI = True
        except Exception:
            pass
        run_on_system(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex)); traceback.print_exc(); return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

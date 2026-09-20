#!/usr/bin/env python3
# ============================================================
# AthermalScan (Python / ZOS-API) - core scan/report CLI
# ============================================================
# Sweeps temperature (optional pressure), applies OpticStudio\'s
# thermal model transiently, restores the system, and reports
# focus shift / EFFL / RMS. Dialogs are -nodialog only.
# Twin of the C# User Extension (reports: text+CSV+simple chart).
#
# Flags: -tmin -tmax -steps -track -pressure -vacuum -psweep
#        -temp0 -press0 -freezesolves -dump -nodialog -dialog
#        -out -outdir -file -quiet
# ============================================================

from __future__ import annotations

import csv
import math
import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import bootstrap_zosapi, connect_zos, discover_zos_root, parse_flag_token

TMIN, TMAX, STEPS = -20.0, 60.0, 9
TRACK = 0.0
OUT_PREFIX = None
OUT_DIR = None
FILE_PATH = None
QUIET = False
PRESSURE = None  # None = design
PRESSURE_END = None
TEMP0 = None
PRESS0 = None
FREEZE_SOLVES = False
DUMP_AT = None
NO_DIALOG = False
FORCE_DIALOG = False
REPORT: List[str] = []
EDGE_FALLBACK: Set[int] = set()


@dataclass
class RowSnap:
    radius: float = float("inf")
    thickness: float = 0.0
    conic: float = 0.0
    semi_dia: float = 0.0
    mech_semi_dia: float = 0.0
    pars: List[float] = field(default_factory=lambda: [0.0]*9)
    type: object = None
    material: str = ""
    mount_tce: float = 0.0
    alpha_radius: float = 0.0
    alpha_thick: float = 0.0
    is_glass: bool = False


def say(s: str) -> None:
    print(s, flush=True)
    REPORT.append(s)


def fmt(t, *a):
    return t.format(*a)


def parse_double(s, keep):
    try:
        return float(s)
    except Exception:
        say("WARNING: {!r} not a number - keeping {}".format(s, keep)); return keep


def parse_int(s, keep):
    try:
        return int(s)
    except Exception:
        say("WARNING: {!r} not an int - keeping {}".format(s, keep)); return keep


def parse_args(argv):
    global TMIN, TMAX, STEPS, TRACK, OUT_PREFIX, OUT_DIR, FILE_PATH, QUIET
    global PRESSURE, PRESSURE_END, TEMP0, PRESS0, FREEZE_SOLVES, DUMP_AT, NO_DIALOG, FORCE_DIALOG
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
            if a == "tmin": TMIN = parse_double(next_tok(), TMIN)
            elif a == "tmax": TMAX = parse_double(next_tok(), TMAX)
            elif a == "steps": STEPS = parse_int(next_tok(), STEPS)
            elif a == "track": TRACK = parse_double(next_tok(), TRACK)
            elif a == "pressure": PRESSURE = parse_double(next_tok(), 1.0)
            elif a == "vacuum": PRESSURE = 0.0
            elif a == "psweep":
                parts = (next_tok() or "").split(":")
                if len(parts) == 2:
                    PRESSURE = parse_double(parts[0], 1.0)
                    PRESSURE_END = parse_double(parts[1], 0.0)
                else:
                    say("WARNING: -psweep expects P1:P2 - ignoring")
            elif a == "temp0": TEMP0 = parse_double(next_tok(), 20.0)
            elif a == "press0": PRESS0 = parse_double(next_tok(), 1.0)
            elif a == "freezesolves": FREEZE_SOLVES = True
            elif a == "dump": DUMP_AT = parse_double(next_tok(), 20.0)
            elif a == "nodialog": NO_DIALOG = True
            elif a == "dialog": FORCE_DIALOG = True
            elif a == "out": OUT_PREFIX = next_tok()
            elif a == "outdir": OUT_DIR = next_tok()
            elif a == "file": FILE_PATH = next_tok()
            elif a == "quiet": QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1
    if STEPS < 3:
        STEPS = 3
    for name in ("PRESSURE", "PRESSURE_END", "PRESS0"):
        v = globals()[name]
        if v is not None and v < 0:
            say("WARNING: negative pressure - clamping to 0")
            globals()[name] = 0.0


def op(sys_, ZOSAPI, name, p1=0, p2=0, h1=0, h2=0, p3=0, p4=0):
    t = getattr(ZOSAPI.Editors.MFE.MeritOperandType, name)
    return float(sys_.MFE.GetOperandValue(t, p1, p2, h1, h2, p3, p4, 0, 0))


def convention(p_atm):
    if p_atm <= 1e-12:
        return "ABSOLUTE (vacuum) - at P = 0 the air reference is unity"
    return fmt("RELATIVE to air at {0:.3f} atm", p_atm)


def check_no_env_operands(sys_, ZOSAPI):
    found = []
    try:
        mce = sys_.MCE
        for r in range(1, mce.NumberOfOperands + 1):
            op_ = mce.GetOperandAt(r)
            tn = str(op_.Type)
            if "TEMP" in tn or "PRES" in tn:
                found.append(fmt("row {0}: {1}", r, op_.Type))
    except Exception:
        return
    if found:
        raise RuntimeError(
            "the multi-configuration editor already defines the environment ("
            + ", ".join(found) + "). Analyse through multi-config workflow instead."
        )


def writable_columns(ZOSAPI):
    cols = [ZOSAPI.Editors.LDE.SurfaceColumn.Radius, ZOSAPI.Editors.LDE.SurfaceColumn.Thickness]
    for p in range(1, 9):
        cols.append(getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p)))
    return cols


def find_computing_solves(lde, img_idx, ZOSAPI):
    found = []
    for col in writable_columns(ZOSAPI):
        for i in range(1, img_idx):
            try:
                st = lde.GetSurfaceAt(i).GetSurfaceCell(col).Solve
            except Exception:
                continue
            if st in (getattr(ZOSAPI.Editors.SolveType, "None"), ZOSAPI.Editors.SolveType.Fixed,
                      ZOSAPI.Editors.SolveType.Variable, ZOSAPI.Editors.SolveType.Automatic):
                continue
            found.append(fmt("surface {0} {1} ({2})", i, col, st))
    return found


def check_solves(lde, img_idx, ZOSAPI):
    if not FREEZE_SOLVES:
        offenders = find_computing_solves(lde, img_idx, ZOSAPI)
        if offenders:
            raise RuntimeError(
                "value-computing solves sit on cells this scan must write - "
                + "; ".join(offenders[:8])
                + ". Remove them, or re-run with -freezesolves."
            )
        return
    frozen = 0
    for col in writable_columns(ZOSAPI):
        for i in range(1, img_idx):
            try:
                cell = lde.GetSurfaceAt(i).GetSurfaceCell(col)
                st = cell.Solve
            except Exception:
                continue
            if st in (getattr(ZOSAPI.Editors.SolveType, "None"), ZOSAPI.Editors.SolveType.Fixed,
                      ZOSAPI.Editors.SolveType.Variable, ZOSAPI.Editors.SolveType.Automatic):
                continue
            try:
                if cell.MakeSolveFixed():
                    frozen += 1
            except Exception:
                pass
    if frozen:
        say(fmt("Froze {0} value-computing solve(s). NOT undone by restore - do not save unless intended.", frozen))


def sag_snap(s: RowSnap, h: float, e_r: float, ST):
    even = s.type == ST.EvenAspheric
    odd = s.type == ST.OddAsphere
    if s.type != ST.Standard and not even and not odd:
        return 0.0, False
    z = 0.0
    R = s.radius * e_r
    if not (math.isinf(R) or abs(R) > 1e10 or R == 0):
        c = 1.0 / R
        u = 1 - (1 + s.conic) * c * c * h * h
        if u < 0:
            return 0.0, False
        z = c * h * h / (1 + math.sqrt(u))
    if even or odd:
        for p in range(1, 9):
            if s.pars[p] == 0:
                continue
            powr = 2 * p if even else p
            z += s.pars[p] * (e_r ** (1 - powr)) * (h ** powr)
    ok = not (math.isnan(z) or math.isinf(z))
    return z, ok


def edge_expanded(snaps, i, img_idx, dT, ST):
    s = snaps[i - 1]
    nxt = snaps[i] if i <= img_idx - 2 else None
    h0 = s.semi_dia if s.semi_dia > 0 else s.mech_semi_dia
    if not (h0 > 0) or math.isnan(h0) or math.isinf(h0):
        return 0.0, False
    zA0, o1 = sag_snap(s, h0, 1.0, ST)
    zB0, o2 = (0.0, True) if nxt is None else sag_snap(nxt, h0, 1.0, ST)
    eRa = 1 + s.alpha_radius * 1e-6 * dT
    eRb = 1.0 if nxt is None else 1 + nxt.alpha_radius * 1e-6 * dT
    zA1, o3 = sag_snap(s, h0, eRa, ST)
    zB1, o4 = (0.0, True) if nxt is None else sag_snap(nxt, h0, eRb, ST)
    if not (o1 and o2 and o3 and o4):
        return 0.0, False
    edge0 = s.thickness + zB0 - zA0
    edge1 = edge0 * (1 + s.alpha_thick * 1e-6 * dT)
    t = edge1 - zB1 + zA1
    if math.isnan(t) or math.isinf(t):
        return 0.0, False
    return t, True


def apply_temperature(sys_, snaps, img_idx, dT, ZOSAPI):
    ST = ZOSAPI.Editors.LDE.SurfaceType
    lde = sys_.LDE
    for i in range(1, img_idx):
        s = snaps[i - 1]
        row = lde.GetSurfaceAt(i)
        eT = 1 + s.alpha_thick * 1e-6 * dT
        eR = 1 + s.alpha_radius * 1e-6 * dT
        if s.is_glass:
            row.Thickness = s.thickness * eT
        else:
            t, ok = edge_expanded(snaps, i, img_idx, dT, ST)
            if not ok:
                t = s.thickness * eT
                EDGE_FALLBACK.add(i)
            row.Thickness = t
        if s.type != ST.CoordinateBreak:
            if not (abs(s.radius) > 1e10 or s.radius == 0):
                try:
                    row.Radius = s.radius * eR
                except Exception:
                    pass
            if s.type in (ST.EvenAspheric, ST.OddAsphere):
                for p in range(1, 9):
                    if s.pars[p] == 0:
                        continue
                    powr = 2 * p if s.type == ST.EvenAspheric else p
                    try:
                        col = getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p))
                        row.GetSurfaceCell(col).DoubleValue = s.pars[p] * (eR ** (1 - powr))
                    except Exception:
                        pass


def restore_system(sys_, env, snaps, img_idx, t0, p0, t_raw, p_raw, adjust0, primary_wave, ZOSAPI):
    check = float("nan")
    try:
        apply_temperature(sys_, snaps, img_idx, 0, ZOSAPI)
    except Exception as ex:
        say("WARNING: could not restore the prescription: " + str(ex))
    try:
        env.Temperature = t0
        env.Pressure = p0
        check = op(sys_, ZOSAPI, "EFFL", 0, primary_wave)
    except Exception as ex:
        say("WARNING: restoration check failed: " + str(ex))
    try:
        env.Temperature = t_raw
        env.Pressure = p_raw
        env.AdjustIndexToEnvironment = adjust0
    except Exception as ex:
        say("WARNING: could not restore the environment: " + str(ex))
    return check


def marginal_focus(sys_, img_idx, wave, last_gap, ZOSAPI):
    try:
        y = op(sys_, ZOSAPI, "REAY", img_idx, wave, 0, 0, 0, 1, 0, 0)
        m = op(sys_, ZOSAPI, "REAB", img_idx, wave, 0, 0, 0, 1, 0, 0)
        n = op(sys_, ZOSAPI, "REAC", img_idx, wave, 0, 0, 0, 1, 0, 0)
        u = m / n if abs(n) > 1e-14 else 0
        return last_gap - y / u if abs(u) > 1e-14 else float("nan")
    except Exception:
        return float("nan")


def out_paths(sys_, app):
    """Resolve report/csv/png prefix from -out / -outdir."""
    stem = "athermal"
    src = FILE_PATH or (sys_.SystemFile or "")
    if src:
        stem = os.path.splitext(os.path.basename(src))[0] + "_athermal"
    directory = OUT_DIR
    prefix = OUT_PREFIX
    if prefix:
        if os.path.dirname(prefix):
            directory = os.path.dirname(prefix) or directory
            stem = os.path.basename(prefix)
        else:
            stem = prefix
    if not directory:
        directory = os.path.dirname(src) if src else str(getattr(app, "ZemaxDataDir", "."))
    os.makedirs(directory, exist_ok=True)
    base = os.path.join(directory, stem)
    return base + ".txt", base + ".csv", base + ".png"


def write_chart(path, temps, dz, rms_f, rms_r, efl):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except Exception:
        say("NOTE: Pillow unavailable - chart skipped")
        return
    W, H, m = 900, 700, 50
    img = Image.new("RGB", (W, H), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    panels = [
        ("Focus shift", dz, (0, 0, 200)),
        ("RMS fixed / refoc", None, None),
        ("EFL", efl, (0, 140, 0)),
    ]
    # simple focus panel
    def panel(y0, h, ys, color, title):
        finite = [v for v in ys if not (math.isnan(v) or math.isinf(v))]
        if not finite:
            return
        ymin, ymax = min(finite), max(finite)
        if abs(ymax - ymin) < 1e-15:
            ymax += 1; ymin -= 1
        draw.rectangle([m, y0, W - m, y0 + h], outline=(0, 0, 0))
        pts = []
        for i, t in enumerate(temps):
            v = ys[i]
            if math.isnan(v) or math.isinf(v):
                continue
            x = m + (t - temps[0]) / (temps[-1] - temps[0] + 1e-15) * (W - 2 * m)
            y = y0 + h - (v - ymin) / (ymax - ymin) * h
            pts.append((x, y))
        if len(pts) >= 2:
            draw.line(pts, fill=color, width=2)
        draw.text((m + 4, y0 + 4), title, fill=(0, 0, 0))
    panel(m, 180, dz, (0, 0, 200), "Focus shift (lens units)")
    panel(m + 200, 180, rms_f, (200, 0, 0), "RMS spot fixed")
    panel(m + 400, 180, efl, (0, 140, 0), "EFL")
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    img.save(path, "PNG")


def analyze(session):
    global EDGE_FALLBACK
    EDGE_FALLBACK = set()
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    ST = ZOSAPI.Editors.LDE.SurfaceType
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        raise RuntimeError("this extension requires a sequential system")
    if FORCE_DIALOG and not NO_DIALOG:
        say("NOTE: Python twin has no settings dialog; using CLI flags (-nodialog).")

    lde = sys_.LDE
    img_idx = lde.NumberOfSurfaces - 1
    env = sys_.SystemData.Environment
    primary_wave = 1
    wls = sys_.SystemData.Wavelengths
    for w in range(1, int(wls.NumberOfWavelengths) + 1):
        if wls.GetWavelength(w).IsPrimary:
            primary_wave = w
            break

    say("=== Athermal Scan ===")
    say("Lens file : " + (sys_.SystemFile or "(untitled)"))
    if abs(TMAX - TMIN) < 1e-9:
        raise RuntimeError("-tmin and -tmax must differ")
    check_no_env_operands(sys_, ZOSAPI)
    check_solves(lde, img_idx, ZOSAPI)

    t_raw = float(env.Temperature)
    p_raw = float(env.Pressure)
    adjust0 = bool(env.AdjustIndexToEnvironment)
    t0, p0 = t_raw, p_raw
    if not adjust0:
        if TEMP0 is None:
            raise RuntimeError(
                "'Adjust Index Data To Environment' is OFF; re-run with -temp0 <C> "
                "(plus -pressure/-vacuum if needed)."
            )
        t0 = TEMP0
        p0 = PRESS0 if PRESS0 is not None else (PRESSURE if PRESSURE is not None else 1.0)
    else:
        if TEMP0 is not None:
            t0 = TEMP0
        if PRESS0 is not None:
            p0 = PRESS0

    p_start = PRESSURE if PRESSURE is not None else p0
    p_end = PRESSURE_END if PRESSURE_END is not None else p_start
    p_varies = abs(p_end - p_start) > 1e-12

    say(fmt("Design environment: {0:.1f} C, {1:.3f} atm", t0, p0))
    say(fmt("Scan              : {0:.0f}..{1:.0f} C, {2} steps, {3}",
            TMIN, TMAX, STEPS,
            fmt("pressure {0:.3f} -> {1:.3f} atm", p_start, p_end) if p_varies
            else fmt("pressure {0:.3f} atm", p_start)))
    say("Index convention  : " + convention(p_start))

    # Snapshot
    snaps = [None] * img_idx
    glass_names = set()
    for i in range(1, img_idx):
        row = lde.GetSurfaceAt(i)
        s = RowSnap(type=row.Type)
        try:
            s.radius = float(row.Radius)
        except Exception:
            s.radius = float("inf")
        s.thickness = float(row.Thickness)
        try:
            s.conic = float(row.Conic)
        except Exception:
            s.conic = 0.0
        try:
            s.semi_dia = float(row.SemiDiameter)
        except Exception:
            pass
        try:
            s.mech_semi_dia = float(row.MechanicalSemiDiameter)
        except Exception:
            pass
        mat = (row.Material or "").strip()
        s.material = mat
        s.is_glass = bool(mat) and mat != "-" and mat.upper() != "MIRROR"
        if s.is_glass:
            glass_names.add(mat)
        try:
            s.mount_tce = float(row.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.TCE).DoubleValue)
        except Exception:
            s.mount_tce = 0.0
        for p in range(1, 9):
            try:
                col = getattr(ZOSAPI.Editors.LDE.SurfaceColumn, "Par" + str(p))
                s.pars[p] = float(row.GetSurfaceCell(col).DoubleValue)
            except Exception:
                s.pars[p] = 0.0
        snaps[i - 1] = s
    if not glass_names:
        raise RuntimeError("no glass surfaces found - nothing to athermalize")

    glass_tce: Dict[str, float] = {}
    mat_tool = sys_.Tools.OpenMaterialsCatalog()
    try:
        catalogs = [c for c in sys_.SystemData.MaterialCatalogs.GetCatalogsInUse() if c and str(c).strip()]
        for cat in catalogs:
            try:
                mat_tool.SelectedCatalog = cat
                names = list(mat_tool.GetAllMaterials())
            except Exception:
                continue
            for nm in names:
                if nm not in glass_names or nm in glass_tce:
                    continue
                mat_tool.SelectedMaterial = nm
                glass_tce[nm] = float(mat_tool.TCE)
    finally:
        mat_tool.Close()
    for g in glass_names:
        if g not in glass_tce:
            say("WARNING: glass '{0}' not in catalogs - assuming TCE=0".format(g))
            glass_tce[g] = 0.0

    for i, s in enumerate(snaps):
        if s is None:
            continue
        if s.is_glass:
            s.alpha_thick = glass_tce[s.material]
            s.alpha_radius = glass_tce[s.material]
        else:
            s.alpha_thick = s.mount_tce
            s.alpha_radius = glass_tce[snaps[i - 1].material] if (i > 0 and snaps[i - 1] and snaps[i - 1].is_glass) else s.mount_tce

    if DUMP_AT is not None:
        env.AdjustIndexToEnvironment = True
        try:
            env.Temperature = t0
            env.Pressure = p0
            apply_temperature(sys_, snaps, img_idx, DUMP_AT - t0, ZOSAPI)
            env.Temperature = DUMP_AT
            say(fmt("PRESCRIPTION AT {0:.4f} C  (dT = {1:+.4f})", DUMP_AT, DUMP_AT - t0))
            say("  surf                radius             thickness   material")
            for i in range(1, img_idx):
                row = lde.GetSurfaceAt(i)
                try:
                    r = float(row.Radius)
                except Exception:
                    r = float("inf")
                say(fmt("  {0:4d}   {1:20.14g}   {2:18.14g}   {3}", i, r, float(row.Thickness), snaps[i - 1].material))
        finally:
            restore_system(sys_, env, snaps, img_idx, t0, p0, t_raw, p_raw, adjust0, primary_wave, ZOSAPI)
        return

    n = STEPS
    temps = [TMIN + (TMAX - TMIN) * i / (n - 1) for i in range(n)]
    press = [p_start + (p_end - p_start) * i / (n - 1) for i in range(n)]
    focus_shift = [float("nan")] * n
    rms_fixed = [float("nan")] * n
    rms_refoc = [float("nan")] * n
    efl = [float("nan")] * n

    env.AdjustIndexToEnvironment = True
    try:
        env.Temperature = t0
        env.Pressure = p0
        efl0 = op(sys_, ZOSAPI, "EFFL", 0, primary_wave)
        last_gap = snaps[img_idx - 1].thickness if img_idx >= 1 else 0
        focus0 = marginal_focus(sys_, img_idx, primary_wave, last_gap, ZOSAPI)
        say(fmt("Baseline EFFL={0:.6g}  focus={1:.6g}", efl0, focus0))

        for i, T in enumerate(temps):
            try:
                if app.TerminateRequested:
                    say("Terminated by user."); break
            except Exception:
                pass
            dT = T - t0
            apply_temperature(sys_, snaps, img_idx, dT, ZOSAPI)
            env.Temperature = T
            env.Pressure = press[i]
            efl[i] = op(sys_, ZOSAPI, "EFFL", 0, primary_wave)
            foc = marginal_focus(sys_, img_idx, primary_wave, float(lde.GetSurfaceAt(img_idx - 1).Thickness), ZOSAPI)
            focus_shift[i] = foc - focus0 if not (math.isnan(foc) or math.isnan(focus0)) else float("nan")
            try:
                rms_fixed[i] = op(sys_, ZOSAPI, "RSCE", 0, primary_wave, 0, 0, 0, 0)
            except Exception:
                try:
                    rms_fixed[i] = op(sys_, ZOSAPI, "RSCH", 0, primary_wave)
                except Exception:
                    rms_fixed[i] = float("nan")
            # quick refocus estimate: put focus shift into last thickness temporarily
            try:
                row = lde.GetSurfaceAt(img_idx - 1)
                t_save = float(row.Thickness)
                if not math.isnan(focus_shift[i]):
                    row.Thickness = t_save - focus_shift[i]
                try:
                    rms_refoc[i] = op(sys_, ZOSAPI, "RSCE", 0, primary_wave, 0, 0, 0, 0)
                except Exception:
                    rms_refoc[i] = float("nan")
                row.Thickness = t_save
            except Exception:
                rms_refoc[i] = float("nan")
            say(fmt("  T={0:7.2f} C  P={1:.3f}  dFocus={2:.6g}  EFL={3:.6g}  RMSfix={4:.6g}",
                    T, press[i], focus_shift[i], efl[i], rms_fixed[i]))
            try:
                app.ProgressPercent = 10 + 80.0 * (i + 1) / n
            except Exception:
                pass
    finally:
        efl_check = restore_system(sys_, env, snaps, img_idx, t0, p0, t_raw, p_raw, adjust0, primary_wave, ZOSAPI)
        if not math.isnan(efl_check) and not math.isnan(efl0):
            if abs(efl_check - efl0) > 1e-4 * max(abs(efl0), 1):
                say(fmt("WARNING: restore EFFL check drifted ({0:.6g} vs {1:.6g})", efl_check, efl0))

    if EDGE_FALLBACK:
        say("NOTE: centre-scaled fallback on surface(s) " + ", ".join(str(x) for x in sorted(EDGE_FALLBACK)))

    txt, csv_path, png = out_paths(sys_, app)
    with open(txt, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(REPORT) + "\n")
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["T_C", "P_atm", "dFocus", "EFL", "RMS_fixed", "RMS_refoc"])
        for i in range(n):
            w.writerow([temps[i], press[i], focus_shift[i], efl[i], rms_fixed[i], rms_refoc[i]])
    write_chart(png, temps, focus_shift, rms_fixed, rms_refoc, efl)
    say("Report: " + txt)
    say("CSV   : " + csv_path)
    say("Chart : " + png)
    try:
        app.ProgressMessage = "Done. Athermal scan complete."
        app.ProgressPercent = 100
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
        analyze(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex)); traceback.print_exc(); return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

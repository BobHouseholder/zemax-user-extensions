#!/usr/bin/env python3
# ============================================================
# ElementLeaveOneOut (Python / ZOS-API) - what this program does
# ============================================================
# Imagine a stack of lenses. We want to know which lens we need
# the least. So we try this game for each lens:
#   1) Take that lens out.
#   2) Tweak the leftover lenses a little (local optimize).
#   3) Check the "report card" score (merit function).
# The report card number going UP a little means "we miss that
# lens a little." Going UP a lot means "we really needed it."
# We keep the try where the report card got worse the LEAST
# (or even got better), and save a new .zmx with that lens gone.
#
# Also: glass and air gaps are not allowed to go negative or
# paper-thin. If the report card has no thickness rules yet,
# we add them (glass at least 1 mm, air at least 0.5 mm).
#
# This is the public Python (pythonnet + ZOS-API) twin of the
# C# OpticStudio user extension ElementLeaveOneOut.
#
# Flags (same names as the C# tool):
#   -file <zmx>  -save <path>  -out <dir>  -cycles K  -top N
#   -rank power|loo  -report [path]  -nodialog  -quiet
# ============================================================

from __future__ import annotations

import csv
import math
import os
import sys
import traceback
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional

# --- command-line switches (filled by parse_args) ---
FILE_PATH: Optional[str] = None
SAVE_PATH: Optional[str] = None
OUT_DIR: Optional[str] = None
REPORT_PATH: Optional[str] = None  # None=off; ""=default name; else path
CYCLES: int = 30
TOP_N: int = 0
RANK_POWER_ONLY: bool = False
QUIET: bool = False

REPORT_LINES: List[str] = []

# Thickness fences (same numbers as the C# extension).
GLASS_MIN_CT = 1.0
GLASS_MAX_CT = 1000.0
AIR_MIN_CT = 0.5
AIR_MAX_CT = 1000.0
THICKNESS_BOUND_WEIGHT = 100.0

KNOWN_INSTALL = r"C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01"


@dataclass
class ElementInfo:
    index: int
    front: int
    rear: int
    label: str
    abs_power: float
    is_mirror: bool


@dataclass
class TrialResult:
    element: ElementInfo
    mf_after: float = float("nan")
    delta: float = float("nan")
    error: str = ""
    ok: bool = False


def say(line: str) -> None:
    """Print a line and remember it for the text report."""
    print(line, flush=True)
    REPORT_LINES.append(line)


def fmt(template: str, *args) -> str:
    return template.format(*args)


# ---------------------------------------------------------------------------
# Finding OpticStudio DLLs
# ---------------------------------------------------------------------------
# OpticStudio installs a folder of .NET libraries. Python needs that folder
# so pythonnet can load them. Look order:
#   1) ZEMAX_ROOT environment variable
#   2) Known 2026 R1.01 install path
#   3) Any Ansys Zemax OpticStudio* under Program Files
# ---------------------------------------------------------------------------

def has_zos_dlls(folder: str) -> bool:
    if not folder or not os.path.isdir(folder):
        return False
    need = ("ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll")
    return all(os.path.isfile(os.path.join(folder, n)) for n in need)


def discover_zos_root() -> str:
    env = (os.environ.get("ZEMAX_ROOT") or "").strip().strip('"')
    if env and has_zos_dlls(env):
        return env.rstrip("\\/")
    if has_zos_dlls(KNOWN_INSTALL):
        return KNOWN_INSTALL

    candidates: List[str] = []
    for pf_key in ("ProgramFiles", "ProgramFiles(x86)"):
        pf = os.environ.get(pf_key) or (
            r"C:\Program Files" if pf_key == "ProgramFiles" else r"C:\Program Files (x86)"
        )
        if not os.path.isdir(pf):
            continue
        try:
            for name in os.listdir(pf):
                low = name.lower().replace(" ", "")
                if "opticstudio" in low or ("zemax" in low and "optic" in low):
                    full = os.path.join(pf, name)
                    if has_zos_dlls(full):
                        candidates.append(full)
        except OSError:
            continue

    if candidates:
        candidates = sorted(set(candidates), reverse=True)
        for c in candidates:
            if "2026" in c:
                return c
        return candidates[0]

    raise RuntimeError(
        "could not find OpticStudio ZOS-API DLLs. Set ZEMAX_ROOT to the install "
        "folder that contains ZOSAPI.dll (tried {!r}).".format(KNOWN_INSTALL)
    )


def bootstrap_zosapi(zos_root: str):
    """
    Load OpticStudio .NET libraries into this Python process.

    Why: Python cannot talk to OpticStudio alone. pythonnet (clr) lets Python
    call .NET. We load ZOSAPI_NetHelper, initialize that install folder, then
    load ZOSAPI + interfaces.
    """
    import clr  # type: ignore

    if zos_root not in sys.path:
        sys.path.append(zos_root)
    os.environ["PATH"] = zos_root + os.pathsep + os.environ.get("PATH", "")

    clr.AddReference(os.path.join(zos_root, "ZOSAPI_NetHelper"))
    import ZOSAPI_NetHelper  # type: ignore

    ok = bool(ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(zos_root))
    if not ok:
        ok = bool(ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
    if not ok:
        raise RuntimeError("ZOSAPI_NetHelper failed to Initialize at " + zos_root)

    resolved = ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory()
    say("Found OpticStudio at: " + str(resolved or zos_root))

    clr.AddReference(os.path.join(zos_root, "ZOSAPI"))
    clr.AddReference(os.path.join(zos_root, "ZOSAPI_Interfaces"))
    import ZOSAPI  # type: ignore

    return ZOSAPI


# ---------------------------------------------------------------------------
# Command line
# ---------------------------------------------------------------------------

def parse_args(argv: List[str]) -> None:
    """Read -file / -out / ... and fill the global switches."""
    global FILE_PATH, SAVE_PATH, OUT_DIR, REPORT_PATH, CYCLES, TOP_N
    global RANK_POWER_ONLY, QUIET

    i = 0
    while i < len(argv):
        raw = argv[i]
        if raw.startswith("-") or raw.startswith("/"):
            a = raw.lstrip("-/").lower()

            def next_tok() -> Optional[str]:
                nonlocal i
                if i + 1 < len(argv):
                    i += 1
                    return argv[i]
                return None

            if a == "file":
                FILE_PATH = next_tok()
            elif a == "save":
                SAVE_PATH = next_tok()
            elif a == "out":
                OUT_DIR = next_tok()
            elif a == "report":
                if i + 1 < len(argv) and not (
                    argv[i + 1].startswith("-") or argv[i + 1].startswith("/")
                ):
                    REPORT_PATH = next_tok()
                else:
                    REPORT_PATH = ""
            elif a == "cycles":
                CYCLES = parse_int(next_tok(), CYCLES)
            elif a == "top":
                TOP_N = parse_int(next_tok(), TOP_N)
            elif a == "rank":
                v = (next_tok() or "loo").lower()
                if v == "power":
                    RANK_POWER_ONLY = True
                elif v == "loo":
                    RANK_POWER_ONLY = False
                else:
                    raise RuntimeError("unknown -rank value (use power|loo)")
            elif a == "nodialog":
                pass
            elif a == "quiet":
                QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1


def parse_int(s: Optional[str], keep: int) -> int:
    if s is None:
        return keep
    try:
        return int(s)
    except ValueError:
        return keep


def csv_escape(s: Optional[str]) -> str:
    if not s:
        return ""
    if any(ch in s for ch in ',"\n'):
        return '"' + s.replace('"', '""') + '"'
    return s


# ---------------------------------------------------------------------------
# Entry + connection
# ---------------------------------------------------------------------------

def main(argv: Optional[List[str]] = None) -> int:
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex))
        return 1

    try:
        zos_root = discover_zos_root()
    except Exception as ex:
        print("FATAL: failed to locate an OpticStudio installation.  " + str(ex))
        return 1

    try:
        ZOSAPI = bootstrap_zosapi(zos_root)
        run(ZOSAPI)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex))
        traceback.print_exc()
        return 1


def run(ZOSAPI) -> None:
    """
    Start OpticStudio, open the lens file (or attach to the open one),
    then run leave-one-out. Close OpticStudio if we started it ourselves.
    """
    standalone = bool(FILE_PATH)
    app = None

    if standalone:
        connection = ZOSAPI.ZOSAPI_Connection()
        app = connection.CreateNewApplication()
        if app is None or app.PrimarySystem is None or not app.IsValidLicenseForAPI:
            raise RuntimeError("could not start a standalone OpticStudio instance")
        TheSystem = app.PrimarySystem
        if (not TheSystem.LoadFile(FILE_PATH, False)) or TheSystem.LDE.NumberOfSurfaces < 3:
            try:
                app.CloseApplication()
            except Exception:
                pass
            raise RuntimeError("failed to load " + str(FILE_PATH))
        say("Loaded: " + str(FILE_PATH))
    else:
        connection = ZOSAPI.ZOSAPI_Connection()
        app = None
        try:
            app = connection.ConnectToApplication()
        except Exception:
            app = None
        if app is None:
            try:
                app = connection.ConnectAsExtension(0)
            except Exception:
                app = None
        if app is None or app.PrimarySystem is None:
            raise RuntimeError(
                "could not connect to OpticStudio "
                "(start Interactive Extension, or pass -file <zmx>)"
            )
        if not app.IsValidLicenseForAPI:
            raise RuntimeError("license is not valid for ZOS-API: " + str(app.LicenseStatus))
        TheSystem = app.PrimarySystem
        say("Connected to OpticStudio (mode: " + str(app.Mode) + ")")

    try:
        try:
            app.ShowChangesInUI = True
        except Exception:
            pass
        run_on_system(ZOSAPI, app, TheSystem)
    finally:
        if standalone and app is not None:
            try:
                app.CloseApplication()
            except Exception:
                pass


def run_on_system(ZOSAPI, app, TheSystem) -> None:
    """
    Big picture:
    1) Ensure merit function + thickness rules exist.
    2) Record baseline score.
    3) Find removable lenses/mirrors.
    4) Try removing each; local DLS; record score change.
    5) Keep the friendliest removal and save that .zmx.
    """
    say("=== ElementLeaveOneOut ===")
    say("Method: leave-one-out deletion + local DLS reopt on existing MF")

    mfe = TheSystem.MFE
    if mfe is None or mfe.NumberOfOperands < 1 or not has_positive_weight(mfe):
        say(
            "Merit function empty/unweighted - building default RMS spot MF "
            "(Optimization Wizard defaults)."
        )
        seed_baseline_mf(ZOSAPI, TheSystem)
        mfe = TheSystem.MFE

    ensure_thickness_constraints(ZOSAPI, TheSystem)
    mfe = TheSystem.MFE
    mf0 = float(mfe.CalculateMeritFunction())
    effl0 = safe_effl(ZOSAPI, TheSystem)
    say(fmt("Baseline MF: {:.8g}", mf0))
    say(fmt("Baseline EFFL: {:.8g}", effl0))

    elements = enumerate_elements(TheSystem)
    if len(elements) < 1:
        raise RuntimeError("no removable lens/mirror elements found")
    say(fmt("Removable elements: {}", len(elements)))
    for e in elements:
        say(
            fmt(
                "  E{}: surfaces {}-{}  |power|~{:.4g}  {}  {}",
                e.index,
                e.front,
                e.rear,
                e.abs_power,
                "mirror" if e.is_mirror else "lens",
                e.label,
            )
        )

    work_dir = OUT_DIR
    if not work_dir:
        src = FILE_PATH or (TheSystem.SystemFile or "")
        if src:
            work_dir = str(Path(src).parent / "_ElementLeaveOneOut")
        else:
            work_dir = str(Path(os.environ.get("TEMP", ".")) / "ElementLeaveOneOut")
    Path(work_dir).mkdir(parents=True, exist_ok=True)
    base_copy = str(Path(work_dir) / "_baseline_loo.zmx")
    TheSystem.SaveAs(base_copy)
    say("Baseline copy: " + base_copy)

    candidates = sorted(elements, key=lambda e: e.abs_power)
    if TOP_N > 0 and TOP_N < len(candidates):
        candidates = candidates[:TOP_N]
        say(fmt("Prefilter: testing {} weakest-|power| element(s)", len(candidates)))

    trials: List[TrialResult] = []
    if RANK_POWER_ONLY:
        trials.append(run_trial(ZOSAPI, app, TheSystem, base_copy, candidates[0], mf0, effl0))
    else:
        n = 0
        for el in candidates:
            n += 1
            try:
                if app.TerminateRequested:
                    say("Terminate requested.")
                    break
            except Exception:
                pass
            try:
                app.ProgressPercent = int(100.0 * n / max(len(candidates), 1))
                app.ProgressMessage = fmt("LOO E{} ({}/{})", el.index, n, len(candidates))
            except Exception:
                pass
            say("")
            say(fmt("--- Trial E{} surfaces {}-{} ---", el.index, el.front, el.rear))
            trials.append(run_trial(ZOSAPI, app, TheSystem, base_copy, el, mf0, effl0))

    ok_trials = sorted([t for t in trials if t.ok], key=lambda t: t.delta)
    say("")
    say("=== LOO RESULTS ===")
    say(fmt("{:<6} {:<12} {:>14} {:>14} {}", "Elem", "Surfaces", "MF_after", "Delta", "Note"))
    for t in sorted(trials, key=lambda t: t.element.index):
        surf = "{}-{}".format(t.element.front, t.element.rear)
        if not t.ok:
            say(fmt("E{:<5} {:<12} {:>14} {:>14} {}", t.element.index, surf, "FAIL", "", t.error))
        else:
            note = "improved/equal" if t.delta <= 0 else ""
            say(
                fmt(
                    "E{:<5} {:<12} {:>14.8g} {:>14.8g} {}",
                    t.element.index,
                    surf,
                    t.mf_after,
                    t.delta,
                    note,
                )
            )

    if not ok_trials:
        raise RuntimeError("all LOO trials failed")

    best = ok_trials[0]
    say("")
    say(
        fmt(
            "Winner: E{} surfaces {}-{}  MF {:.8g} -> {:.8g}  (delta {:.8g})",
            best.element.index,
            best.element.front,
            best.element.rear,
            mf0,
            best.mf_after,
            best.delta,
        )
    )

    reload_system(TheSystem, base_copy)
    apply_deletion_and_mf(ZOSAPI, TheSystem, best.element, effl0)
    ensure_optimization_variables(ZOSAPI, TheSystem)
    mf_final = local_reopt(ZOSAPI, TheSystem, app)
    say(fmt("Final MF after winner reopt: {:.8g}", mf_final))

    save_path = SAVE_PATH
    if not save_path:
        src = FILE_PATH or (TheSystem.SystemFile or "")
        stem = Path(src).stem if src else "system"
        save_path = str(Path(work_dir) / (stem + "_minus1.zmx"))
    TheSystem.SaveAs(save_path)
    say("Saved reduced system: " + save_path)

    if REPORT_PATH is not None:
        report_path = (
            str(Path(work_dir) / "ElementLeaveOneOut_report.txt")
            if REPORT_PATH == ""
            else REPORT_PATH
        )
        Path(report_path).write_text("\n".join(REPORT_LINES) + "\n", encoding="utf-8")
        say("Report: " + report_path)

    csv_path = str(Path(work_dir) / "ElementLeaveOneOut_trials.csv")
    with open(csv_path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(
            [
                "element",
                "front",
                "rear",
                "label",
                "abs_power",
                "mf0",
                "mf_after",
                "delta",
                "ok",
                "error",
            ]
        )
        for t in trials:
            w.writerow(
                [
                    t.element.index,
                    t.element.front,
                    t.element.rear,
                    csv_escape(t.element.label),
                    fmt("{:.8g}", t.element.abs_power),
                    fmt("{:.8g}", mf0),
                    fmt("{:.8g}", t.mf_after) if t.ok else "",
                    fmt("{:.8g}", t.delta) if t.ok else "",
                    "1" if t.ok else "0",
                    csv_escape(t.error or ""),
                ]
            )
    say("CSV: " + csv_path)
    say(
        fmt(
            "RETURN mf0={:.8g} mf_after={:.8g} delta={:.8g} save={}",
            mf0,
            mf_final,
            mf_final - mf0,
            save_path,
        )
    )


def run_trial(ZOSAPI, app, TheSystem, base_copy, el, mf0, effl0) -> TrialResult:
    """One try: reload baseline, remove ONE element, fix MF, DLS, score."""
    tr = TrialResult(element=el)
    try:
        reload_system(TheSystem, base_copy)
        apply_deletion_and_mf(ZOSAPI, TheSystem, el, effl0)
        ensure_optimization_variables(ZOSAPI, TheSystem)
        mf = local_reopt(ZOSAPI, TheSystem, app)
        tr.mf_after = mf
        tr.delta = mf - mf0
        tr.ok = True
        say(fmt("  MF after reopt: {:.8g}  (delta {:.8g})", mf, tr.delta))
    except Exception as ex:
        tr.ok = False
        tr.error = str(ex)
        say("  FAIL: " + str(ex))
    return tr


def reload_system(TheSystem, path: str) -> None:
    """Load the saved untouched copy so each try starts clean."""
    if (not TheSystem.LoadFile(path, False)) or TheSystem.LDE.NumberOfSurfaces < 3:
        raise RuntimeError("reload failed: " + path)


def safe_effl(ZOSAPI, TheSystem) -> float:
    """Ask for effective focal length. NaN means unknown."""
    try:
        return float(
            TheSystem.MFE.GetOperandValue(
                ZOSAPI.Editors.MFE.MeritOperandType.EFFL,
                0, 1, 0, 0, 0, 0, 0, 0,
            )
        )
    except Exception:
        return float("nan")


def enumerate_elements(TheSystem) -> List[ElementInfo]:
    """
    Walk surfaces and group glass/mirror into elements.
    An element is a clump of surfaces that stick together as one lens or mirror.
    Empty air gaps are skipped (spaces between parts).
    """
    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    out: List[ElementInfo] = []
    s = 1
    idx = 0
    while s < n - 1:
        mat = (lde.GetSurfaceAt(s).Material or "").strip()
        if len(mat) == 0:
            s += 1
            continue
        front = s
        mirror = mat.upper() == "MIRROR"
        mats = [mat]
        s += 1
        while s < n - 1:
            m2 = (lde.GetSurfaceAt(s).Material or "").strip()
            if len(m2) == 0:
                break
            mats.append(m2)
            if m2.upper() == "MIRROR":
                mirror = True
            s += 1
        rear = s
        if rear >= n - 1:
            break
        power = 0.0
        for i in range(front, rear):
            R = float(lde.GetSurfaceAt(i).Radius)
            if abs(R) < 1e-12 or math.isinf(R):
                continue
            mi = (lde.GetSurfaceAt(i).Material or "").strip()
            k = 2.0 if mi.upper() == "MIRROR" else 0.5
            power += k / R
        idx += 1
        out.append(
            ElementInfo(
                index=idx,
                front=front,
                rear=rear,
                abs_power=abs(power),
                is_mirror=mirror,
                label="+".join(mats),
            )
        )
        s = rear + 1
    return out


def apply_deletion_and_mf(ZOSAPI, TheSystem, el: ElementInfo, effl0: float) -> None:
    """Delete element, remap MF, keep EFFL, refresh thickness rules."""
    remap_and_clean_mf(ZOSAPI, TheSystem, el.front, el.rear)
    delete_element(TheSystem, el.front, el.rear)
    ensure_effl_anchor(ZOSAPI, TheSystem, effl0)
    ensure_thickness_constraints(ZOSAPI, TheSystem)


def delete_element(TheSystem, front: int, rear: int) -> None:
    """
    Remove this lens's surfaces. Pour its total thickness into the surface
    in front so overall length stays honest.
    """
    lde = TheSystem.LDE
    absorb = 0.0
    for i in range(front, rear + 1):
        absorb += float(lde.GetSurfaceAt(i).Thickness)
    if front > 0:
        prev = lde.GetSurfaceAt(front - 1)
        prev.Thickness = float(prev.Thickness) + absorb
    for i in range(rear, front - 1, -1):
        lde.RemoveSurfaceAt(i)


def remap_and_clean_mf(ZOSAPI, TheSystem, front: int, rear: int) -> None:
    """
    Report-card rows name surfaces by number. After deletes, renumber:
      - drop rows that pointed at deleted surfaces
      - subtract removed count from larger surface numbers
    """
    mfe = TheSystem.MFE
    removed = rear - front + 1
    MeritColumn = ZOSAPI.Editors.MFE.MeritColumn
    for row in range(int(mfe.NumberOfOperands), 0, -1):
        try:
            op = mfe.GetOperandAt(row)
        except Exception:
            continue
        p1 = read_surf_param(op, MeritColumn.Param1)
        p2 = read_surf_param(op, MeritColumn.Param2)
        hit = (front <= p1 <= rear) or (front <= p2 <= rear)
        if hit:
            try:
                mfe.RemoveOperandAt(row)
            except Exception:
                try:
                    op.Weight = 0
                except Exception:
                    pass
            continue
        if p1 > rear:
            write_surf_param(op, MeritColumn.Param1, p1 - removed)
        if p2 > rear:
            write_surf_param(op, MeritColumn.Param2, p2 - removed)


def read_surf_param(op, col) -> int:
    try:
        return int(round(float(op.GetOperandCell(col).DoubleValue)))
    except Exception:
        return -1


def write_surf_param(op, col, value: int) -> None:
    try:
        op.GetOperandCell(col).DoubleValue = float(value)
    except Exception:
        pass


def has_thickness_boundary_operands(mfe) -> bool:
    if mfe is None:
        return False
    keys = ("MNCT", "MXCT", "MNCA", "MXCA", "MNEG", "MNEA", "CTGT", "CTLT")
    for i in range(1, int(mfe.NumberOfOperands) + 1):
        try:
            op = mfe.GetOperandAt(i)
            if float(op.Weight) <= 0:
                continue
            t = str(op.Type)
            if any(k.lower() in t.lower() for k in keys):
                return True
        except Exception:
            pass
    return False


def ensure_thickness_constraints(ZOSAPI, TheSystem) -> None:
    """
    Add MNCT/MXCT fences if missing (glass >= 1 mm, air >= 0.5 mm).
    If already present, bump their weights so DLS listens.
    """
    mfe = TheSystem.MFE
    if mfe is None:
        return
    if has_thickness_boundary_operands(mfe):
        strengthen_thickness_bound_weights(mfe)
        say("  Thickness constraints already present in MF (weights reinforced).")
        return

    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    added = 0
    MeritOperandType = ZOSAPI.Editors.MFE.MeritOperandType
    for i in range(1, n - 1):
        mat = ""
        try:
            mat = (lde.GetSurfaceAt(i).Material or "").strip()
        except Exception:
            pass
        is_glass = len(mat) > 0 and mat.upper() != "MIRROR"
        min_t = GLASS_MIN_CT if is_glass else AIR_MIN_CT
        max_t = GLASS_MAX_CT if is_glass else AIR_MAX_CT
        if add_thickness_bound(ZOSAPI, mfe, MeritOperandType.MNCT, i, min_t):
            added += 1
        if add_thickness_bound(ZOSAPI, mfe, MeritOperandType.MXCT, i, max_t):
            added += 1
    say(
        fmt(
            "  Added {} glass/air thickness bounds (MNCT/MXCT; glass>={:.4g} air>={:.4g}).",
            added,
            GLASS_MIN_CT,
            AIR_MIN_CT,
        )
    )


def add_thickness_bound(ZOSAPI, mfe, op_type, surf: int, target: float) -> bool:
    try:
        mfe.AddOperand()
        op = mfe.GetOperandAt(mfe.NumberOfOperands)
        op.ChangeType(op_type)
        MeritColumn = ZOSAPI.Editors.MFE.MeritColumn
        try:
            op.GetOperandCell(MeritColumn.Param1).DoubleValue = float(surf)
        except Exception:
            pass
        try:
            op.GetOperandCell(MeritColumn.Param2).DoubleValue = float(surf)
        except Exception:
            pass
        op.Target = float(target)
        op.Weight = float(THICKNESS_BOUND_WEIGHT)
        return True
    except Exception as ex:
        say(fmt("  WARNING: could not add {} on S{}: {}", op_type, surf, ex))
        return False


def strengthen_thickness_bound_weights(mfe) -> None:
    keys = ("MNCT", "MXCT", "MNCA", "MXCA", "MNEG", "MNEA", "CTGT", "CTLT")
    for i in range(1, int(mfe.NumberOfOperands) + 1):
        try:
            op = mfe.GetOperandAt(i)
            t = str(op.Type)
            if not any(k.lower() in t.lower() for k in keys):
                continue
            if float(op.Weight) < THICKNESS_BOUND_WEIGHT:
                op.Weight = float(THICKNESS_BOUND_WEIGHT)
        except Exception:
            pass


def clamp_negative_thicknesses(TheSystem) -> None:
    """
    Seatbelt for NEGATIVE gaps only.
    Thin-but-positive air/glass can be real design; forcing those up wrecks
    the lens. Soft MNCT/MXCT already nudge during DLS; here we only undo th < 0.
    """
    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    fixed = 0
    for i in range(1, n - 1):
        try:
            s = lde.GetSurfaceAt(i)
            th = float(s.Thickness)
            if not (th < 0):
                continue
            mat = (s.Material or "").strip()
            is_glass = len(mat) > 0 and mat.upper() != "MIRROR"
            is_refocus_gap = i == (n - 2)
            floor = 1e-3 if is_refocus_gap else (GLASS_MIN_CT if is_glass else AIR_MIN_CT)
            s.Thickness = float(floor)
            fixed += 1
        except Exception:
            pass
    if fixed > 0:
        say(fmt("  Clamped {} negative thickness(es) back to a legal floor.", fixed))


def ensure_effl_anchor(ZOSAPI, TheSystem, effl0: float) -> None:
    """Keep focal length near baseline; insert EFFL operand if missing."""
    if math.isnan(effl0) or abs(effl0) < 1e-12:
        return
    mfe = TheSystem.MFE
    has_effl = False
    for i in range(1, int(mfe.NumberOfOperands) + 1):
        try:
            op = mfe.GetOperandAt(i)
            if "EFFL" in str(op.Type).upper():
                has_effl = True
                op.Target = float(effl0)
                if float(op.Weight) <= 0:
                    op.Weight = 1.0
        except Exception:
            pass
    if not has_effl:
        try:
            mfe.AddOperand()
            op = mfe.GetOperandAt(mfe.NumberOfOperands)
            op.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EFFL)
            op.Target = float(effl0)
            op.Weight = 1.0
            say(fmt("  Inserted EFFL anchor target={:.8g}", effl0))
        except Exception as ex:
            say("  WARNING: could not insert EFFL anchor: " + str(ex))


def local_reopt(ZOSAPI, TheSystem, app) -> float:
    """Local DLS reopt, then clamp negatives, then quick-focus."""
    opt = TheSystem.Tools.OpenLocalOptimization()
    try:
        if int(opt.Variables) < 1:
            say("  Reopt skipped: no variables")
            clamp_negative_thicknesses(TheSystem)
            run_quick_focus(ZOSAPI, TheSystem)
            return float(TheSystem.MFE.CalculateMeritFunction())

        opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
        opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
        before = float(opt.InitialMeritFunction)
        opt.RunAndWaitForCompletion()
        after = float(opt.CurrentMeritFunction)
        say(fmt("  DLS vars={}: {:.8g} -> {:.8g}", int(opt.Variables), before, after))

        extra = max(0, (CYCLES // 10) - 1)
        for _k in range(extra):
            try:
                if app.TerminateRequested:
                    break
            except Exception:
                pass
            opt.RunAndWaitForCompletion()
            after = float(opt.CurrentMeritFunction)

        clamp_negative_thicknesses(TheSystem)
        run_quick_focus(ZOSAPI, TheSystem)
        return float(TheSystem.MFE.CalculateMeritFunction())
    finally:
        opt.Close()


def has_positive_weight(mfe) -> bool:
    if mfe is None:
        return False
    for i in range(1, int(mfe.NumberOfOperands) + 1):
        try:
            if float(mfe.GetOperandAt(i).Weight) > 0:
                return True
        except Exception:
            pass
    return False


def seed_baseline_mf(ZOSAPI, TheSystem) -> None:
    """Default RMS Spot / Centroid / GQ MF via SEQOptimizationWizard2."""
    mfe = TheSystem.MFE
    try:
        wiz = mfe.SEQOptimizationWizard2
        if wiz is None:
            raise RuntimeError("SEQOptimizationWizard2 unavailable")

        wiz.ResetSettings()
        wiz.Criterion = ZOSAPI.Wizards.CriterionTypes.Spot
        wiz.Type = ZOSAPI.Wizards.OptimizationTypes.RMS
        wiz.Reference = ZOSAPI.Wizards.ReferenceTypes.Centroid
        wiz.UseGaussianQuadrature = True
        wiz.UseRectangularArray = False
        wiz.UseAllFields = True
        wiz.AssumeAxialSymmetry = True
        wiz.IgnoreLateralColor = False
        wiz.AddFavoriteOperands = False
        wiz.UseGlassBoundaryValues = True
        wiz.UseAirBoundaryValues = True
        wiz.OptimizeForBestNominalPerformance = True
        wiz.OptimizeForManufacturingYield = False
        wiz.UseMaximumDistortion = False

        say("  Applying SEQOptimizationWizard2 (RMS Spot / Centroid / GQ)...")
        wiz.Apply()
        say(fmt("  Default MF operands: {}", int(mfe.NumberOfOperands)))
        if mfe.NumberOfOperands < 1 or not has_positive_weight(mfe):
            raise RuntimeError("wizard Apply left MFE empty")
    except Exception as ex:
        say("  Wizard2 failed (" + str(ex) + ") - trying SEQOptimizationWizard...")
        wiz1 = mfe.SEQOptimizationWizard
        if wiz1 is None:
            raise RuntimeError("no sequential optimization wizard available") from ex
        wiz1.ResetSettings()
        try:
            wiz1.IsAssumeAxialSymmetryUsed = True
        except Exception:
            pass
        try:
            wiz1.IsGlassUsed = True
        except Exception:
            pass
        try:
            wiz1.IsAirUsed = True
        except Exception:
            pass
        wiz1.Apply()
        say(fmt("  Default MF operands (v1 wizard): {}", int(mfe.NumberOfOperands)))
        if mfe.NumberOfOperands < 1 or not has_positive_weight(mfe):
            raise RuntimeError("v1 wizard Apply left MFE empty: " + str(ex)) from ex


def ensure_optimization_variables(ZOSAPI, TheSystem) -> None:
    """Free radii/thicknesses (except ends), then free the refocus gap."""
    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    SurfaceColumn = ZOSAPI.Editors.LDE.SurfaceColumn
    for i in range(1, n - 1):
        s = lde.GetSurfaceAt(i)
        try:
            try:
                s.RadiusCell.MakeSolveVariable()
            except Exception:
                try:
                    s.GetSurfaceCell(SurfaceColumn.Radius).MakeSolveVariable()
                except Exception:
                    pass
            try:
                s.ThicknessCell.MakeSolveVariable()
            except Exception:
                try:
                    s.GetSurfaceCell(SurfaceColumn.Thickness).MakeSolveVariable()
                except Exception:
                    pass
        except Exception:
            pass
    ensure_refocus_variable(ZOSAPI, TheSystem)


def ensure_refocus_variable(ZOSAPI, TheSystem) -> None:
    """
    Thickness on the surface right before the image is the focus knob.
    After deleting a lens that distance usually must move — force it variable.
    """
    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    if n < 3:
        return
    focus_surf = n - 2
    try:
        s = lde.GetSurfaceAt(focus_surf)
        try:
            s.ThicknessCell.MakeSolveVariable()
        except Exception:
            try:
                s.GetSurfaceCell(
                    ZOSAPI.Editors.LDE.SurfaceColumn.Thickness
                ).MakeSolveVariable()
            except Exception as ex:
                say(
                    "  WARNING: could not free refocus thickness on S"
                    + str(focus_surf)
                    + ": "
                    + str(ex)
                )
                return
        say(fmt("  Refocus variable: thickness of S{} (gap to image).", focus_surf))
    except Exception as ex:
        say("  WARNING: EnsureRefocusVariable failed: " + str(ex))


def run_quick_focus(ZOSAPI, TheSystem) -> None:
    """Prefer native Quick Focus; else DLS on focus-gap thickness only."""
    ensure_refocus_variable(ZOSAPI, TheSystem)
    if try_native_quick_focus(ZOSAPI, TheSystem):
        return
    refocus_by_focus_gap_only(ZOSAPI, TheSystem)


def try_native_quick_focus(ZOSAPI, TheSystem) -> bool:
    qf = None
    try:
        qf = TheSystem.Tools.OpenQuickFocus()
        if qf is None:
            return False
        try:
            qf.UseCentroid = True
        except Exception:
            pass
        try:
            qf.Criterion = ZOSAPI.Tools.General.QuickFocusCriterion.SpotSizeRadial
        except Exception:
            pass
        qf.RunAndWaitForCompletion()
        say("  Quick Focus done (native tool).")
        return True
    except Exception as ex:
        say("  Native Quick Focus failed (" + str(ex) + ") — using focus-gap DLS.")
        return False
    finally:
        try:
            if qf is not None:
                qf.Close()
        except Exception:
            pass


def refocus_by_focus_gap_only(ZOSAPI, TheSystem) -> None:
    """Lock other knobs; free only gap-to-image; DLS; restore variables."""
    lde = TheSystem.LDE
    n = int(lde.NumberOfSurfaces)
    if n < 3:
        return
    focus_surf = n - 2

    for i in range(1, n - 1):
        try:
            s = lde.GetSurfaceAt(i)
            try:
                s.RadiusCell.MakeSolveFixed()
            except Exception:
                pass
            try:
                s.ThicknessCell.MakeSolveFixed()
            except Exception:
                pass
        except Exception:
            pass

    try:
        fs = lde.GetSurfaceAt(focus_surf)
        ok = False
        try:
            ok = bool(fs.ThicknessCell.MakeSolveVariable())
        except Exception:
            ok = False
        if not ok:
            try:
                fs.GetSurfaceCell(
                    ZOSAPI.Editors.LDE.SurfaceColumn.Thickness
                ).MakeSolveVariable()
            except Exception:
                pass
    except Exception as ex:
        say("  WARNING: could not free focus gap for refocus DLS: " + str(ex))
        ensure_optimization_variables(ZOSAPI, TheSystem)
        return

    opt = TheSystem.Tools.OpenLocalOptimization()
    if opt is None:
        say("  Refocus DLS skipped: local optimizer would not open.")
        ensure_optimization_variables(ZOSAPI, TheSystem)
        return
    try:
        if int(opt.Variables) < 1:
            say("  Refocus DLS skipped: focus gap still not variable.")
            return
        opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
        opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
        before = float(opt.InitialMeritFunction)
        opt.RunAndWaitForCompletion()
        after = float(opt.CurrentMeritFunction)
        say(
            fmt(
                "  Refocus DLS (focus gap S{} only): {:.8g} -> {:.8g}",
                focus_surf,
                before,
                after,
            )
        )
    except Exception as ex:
        say("  WARNING: refocus DLS failed: " + str(ex))
    finally:
        try:
            opt.Close()
        except Exception:
            pass
        ensure_optimization_variables(ZOSAPI, TheSystem)


if __name__ == "__main__":
    sys.exit(main())

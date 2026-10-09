#!/usr/bin/env python3
# ============================================================
# StartPointFinder (Python / ZOS-API) - what this program does
# ============================================================
# You give it a folder full of lens files that are "cousins":
# same family, same number of lenses in the same order (for
# example ten Double Gauss camera lenses). Each one was made for
# a slightly different job: a different focal length, a
# different f-number, a different field of view.
#
# The program:
#   1) Reads every .zmx in the folder and writes down its basic
#      "first-order" numbers: focal length (EFL), f-number,
#      half field of view, and length (track). A file whose glass
#      does not resolve (catalog not installed) or whose numbers
#      are absurd (EFL 1e10, F/0.001) is skipped with the reason.
#      Byte-identical files (and identical prescriptions) count once.
#      Files in inches, cm or m are converted to mm in memory (the
#      file itself is never changed), so every number is in mm.
#      Zoom files (more than one configuration) are skipped.
#   2) Keeps only the files that share one lens layout AND one
#      object distance (infinite, or the same finite distance) and
#      skips the odd ones out, with a count per reason. -layout
#      picks another family; the runner-up groups are listed.
#   3) Shrinks or grows every kept lens to the same focal length
#      (1 unit), so their shapes can be compared fairly.
#   4) Takes the job YOU ask for: F/# and half field of view (and
#      optionally focal length). Default = the middle value of the
#      inputs. Values outside the inputs' range are refused (or
#      clamped with -clamp). Distance between jobs is measured in
#      the folder's own spread (log2 F/# range, field range);
#      on-axis-only designs get a penalty for field requests.
#   5) Builds every start it can make at that job (every input
#      design, and a blend of the Standard-surface inputs), sizes
#      each to the job and scores it with the same report card
#      WITHOUT optimizing ("the screen"). Default -start best then
#      optimizes only the 3 best-screened starts (-top N, or -top all
#      to optimize every start; if none of them passes the checks it
#      goes on down the list until one does) and keeps the winner that
#      passes the checks; -start nearest / blend force one. Optional -hammer SEC
#      gives the 3 best optimized lenses that pass the checks a longer
#      Hammer polish each, then keeps the best again.
#      Designs with Even Asphere / Zernike / Paraxial surfaces are
#      rebuilt from their own file (scaled; asphere terms fixed)
#      and are never averaged into the blend.
#   6) Saves a NEW .zmx plus CSV/JSON/report and a side-view PNG,
#      with all candidates ranked and the runtime.
# The input files are only read. They are never saved over.
#
# This is the public Python (pythonnet + ZOS-API) twin of the C#
# OpticStudio user extension StartPointFinder.
#
# Flags (same names as the C# tool):
#   -dir <folder>  -out <dir>  -fno N  -hfov DEG  -efl F  -clamp
#   -start best|nearest|blend  -layout <signature>
#   -weight near|soft|equal  -glass nearest|majority  -passes K
#   -hammer SEC  -top N|all  -compare reopt|refocus|none  -min N
#   -force  -ask  -nopng  -nodialog  -quiet
# Every lens that is checked must also trace its pupil-edge rays at
# every field (7 real rays): a lens whose edge rays miss a surface
# fails the checks like any other failure.
# No -dir: a folder picker opens, then a small window asks for
# F/# and field (tkinter). The Python twin always runs its own
# OpticStudio (standalone), so it never touches an open session.
# With a window (no -dir, or -ask) every error or refusal also pops
# up a message, and when the folder already holds a result it asks:
# Replace / New folder / Cancel.
#
# Exit codes: 0 ok, 1 error, 2 refused (bad inputs / target
# outside the envelope / outputs exist without -force),
# 3 result written but it failed the envelope check.
# ============================================================

from __future__ import annotations

import csv
import hashlib
import json
import math
import os
import shutil
import statistics
import subprocess
import sys
import time
import traceback
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import (  # noqa: E402
    bootstrap_zosapi,
    discover_zos_root,
    parse_flag_token,
)

TOOL = "StartPointFinder"

# --- command-line switches (filled by parse_args) ---
DIR_PATH: Optional[str] = None
OUT_DIR: Optional[str] = None
TARGET_EFL: Optional[float] = None
TARGET_FNO: Optional[float] = None
TARGET_FOV: Optional[float] = None  # half field of view, degrees
CLAMP: bool = False
START_MODE: str = "best"  # best = lowest MF among candidates that pass the checks (default); nearest; blend
LAYOUT: str = ""  # -layout: pick this layout signature instead of the biggest group
WEIGHT_MODE: str = "near"  # near = closest inputs dominate; soft = old gentle weights
ASK: bool = False  # show the F/# + field window even with -dir
FOLDER_FROM_DIALOG: bool = False
GLASS_MODE: str = "nearest"  # nearest = glass closest to the average nd/vd
PASSES: int = 3  # how many automatic DLS runs (stops early when stuck)
HAMMER_SEC: int = 0  # optional Hammer time in seconds per polished lens (0 = off)
TOP_N: int = 3  # -top: in -start best, optimize only this many best-screened starts (0 = all)
HAMMER_KEEP = 3  # -hammer polishes this many of the best optimized lenses that pass the checks
HAMMER_MAX = 600  # largest Hammer time (s) per lens, for -hammer and the F/# window alike
BIG_FOLDER = 200  # more .zmx files than this: warn (and ask, with a window) before reading
COMPARE: str = "reopt"  # how to score the inputs at the target point
MIN_GROUP: int = 2  # need at least this many matching designs
FORCE: bool = False
NO_PNG: bool = False
NO_DIALOG: bool = False
QUIET: bool = False

REPORT_LINES: List[str] = []
RUN_OUTS: Optional[Dict[str, str]] = None  # this run's output paths (so a failed run can tidy up)
RUN_WROTE_RESULT = False  # did this run save a result/start .zmx yet?

# Steering by F/# and field (see norm_dist): NEAR_EPS softens an exact match;
# FAR_DIST = farther than this from every input means "warn"; ON_AXIS_PENALTY
# is added (in quadrature) for an on-axis-only design when field is requested.
NEAR_EPS = 0.1
FAR_DIST = 0.5
ON_AXIS_PENALTY = 1.0
# Distance scales from the kept group's spread (set_scales fills these).
FNO_SCALE = 1.0
FIELD_SCALE = 10.0
FIELD_MAX = 0.0
AVAIL_CATS: set = set()  # glass catalogs installed on this PC (norm_cat spelling)

# Output file names (all go into the -out folder).
OUT_NAMES = {
    "result": TOOL + "_result.zmx",
    "start": TOOL + "_start.zmx",
    "csv": TOOL + "_firstorder.csv",
    "json": TOOL + "_summary.json",
    "report": TOOL + "_report.txt",
    "png": TOOL + "_layout.png",
}


class ToolExit(Exception):
    # Stop the tool with a known exit code (2 = refused, 3 = result outside envelope).
    # user_cancel = the user pressed Cancel; that needs no error pop-up.
    def __init__(self, code: int, message: str, user_cancel: bool = False) -> None:
        super().__init__(message)
        self.code = code
        self.user_cancel = user_cancel


@dataclass
class SurfRx:
    """One surface written down as plain numbers (curvature = 1/radius)."""

    curv: float
    thick: float
    glass: str
    catalog: str
    conic: float
    type: str = ""  # "" = Standard; otherwise OpticStudio's type name
    pars: Optional[List[float]] = None  # Par1..Par8 of a non-Standard surface (duplicate check)


@dataclass
class Design:
    """Everything we learned from one input .zmx file."""

    name: str
    path: str
    ok: bool = False
    reason: str = ""
    cat: str = ""  # short skip category for the summary counts
    hash: str = ""  # SHA-256 of the file bytes (duplicate check)
    file_catalogs: List[str] = field(default_factory=list)  # GCAT line of the file
    obj_dist: float = float("inf")  # object distance (infinity = far away)
    obj_rep: float = float("inf")  # object distance of its group (finite only)
    is_special: bool = False  # has Even Asphere / Zernike / Paraxial surfaces
    special_note: str = ""  # e.g. "S4 Even Asphere, S5 Even Asphere"
    blend_member: bool = False  # averaged into the blend (Standard surfaces only)
    on_axis: bool = False  # on-axis-only design in a folder that has field designs
    nsurf: int = 0
    stop: int = 0
    signature: str = ""
    surfs: List[SurfRx] = field(default_factory=list)
    efl: float = float("nan")
    fno: float = float("nan")
    hfov: float = float("nan")
    track: float = float("nan")
    weight: float = 0.0  # blend weight while building the blend
    blend_weight: float = 0.0  # closeness weight a blend would use (shown in the CSV)
    dist: float = float("nan")  # distance to the request in (F/#, field) space
    glass_nd_vd: Dict[int, Tuple[float, float]] = field(default_factory=dict)
    mf_refocus: float = float("nan")
    mf_reopt: float = float("nan")
    spot_reopt_um: float = float("nan")
    reopt_pass: bool = True  # did its optimized version pass the result checks?
    note: str = ""
    units: str = "mm"  # the file's own lens units (numbers above are always mm)
    ray_aim: int = 0  # the file's ray aiming: 0 off, 1 paraxial, 2 real

    @property
    def track_ratio(self) -> float:
        return self.track / self.efl if self.efl else float("nan")


@dataclass
class Cand:
    """One start we tried at the request: an input design or the blend."""

    kind: str = "input"  # "input" or "blend"
    d: Optional[Design] = None  # the input (None for the blend)
    label: str = ""
    mf_start: float = float("nan")
    mf: float = float("nan")
    spot: float = float("nan")
    optimized: bool = False
    passed: bool = True  # optimized lens passed the same checks as the final result
    fail: str = ""  # short reason when it did not
    note: str = ""
    screen_rank: int = 0  # 1 = lowest report-card score before optimizing ("the screen")
    edge_ok: Optional[bool] = None  # did its pupil-edge rays trace at every field? (None = not checked)
    # Hammer polish (only with -hammer): the DLS numbers are kept so the report can show both.
    mf_dls: float = float("nan")
    spot_dls: float = float("nan")
    hammered: bool = False  # True = the Hammer result is the one this candidate now stands for
    ham_note: str = ""
    opt_path: str = ""  # temp copy of its DLS lens (only kept for -hammer)
    start_path: str = ""  # temp copy of its start (only kept for -hammer)


def say(line: str) -> None:
    """Print a line and remember it for the text report."""
    print(line, flush=True)
    REPORT_LINES.append(line)


def finite(x: float) -> bool:
    """True if x is a normal number (not NaN, not infinity)."""
    return isinstance(x, (int, float)) and math.isfinite(x)


def plain_sum(values) -> float:
    """
    Left-to-right float sum, like C#'s Enumerable.Sum. Python 3.12+ sum() uses
    compensated summation, which differs in the last bits and would make the
    blend (and so its DLS result) differ from the C# build.
    """
    total = 0.0
    for v in values:
        total += v
    return total


def quant(x: float, scale: float) -> float:
    """
    Round to a fixed grid (x * scale, round half to even, / scale). Distances and
    derived field angles go through this so the C# and Python builds get the very
    same numbers: log/atan can differ in the last bit between runtimes, and DLS
    turns a last-bit difference in the blend into a different local minimum.
    """
    return float(round(x * scale)) / scale if finite(x) else x


def g(x: float, digits: int = 6) -> str:
    """Short number text for tables (blank-safe for NaN)."""
    if not finite(x):
        return "nan"
    return "{:.{}g}".format(x, digits)


# ---------------------------------------------------------------------------
# Command line
# ---------------------------------------------------------------------------

def parse_args(argv: List[str]) -> None:
    """Read -dir / -out / ... and fill the global switches."""
    global DIR_PATH, OUT_DIR, TARGET_EFL, TARGET_FNO, TARGET_FOV, CLAMP, START_MODE, ASK, LAYOUT
    global WEIGHT_MODE, GLASS_MODE, PASSES, HAMMER_SEC, COMPARE, MIN_GROUP, TOP_N
    global FORCE, NO_PNG, NO_DIALOG, QUIET

    i = 0
    while i < len(argv):
        raw = argv[i]
        if raw.startswith("-") or raw.startswith("/"):
            a = parse_flag_token(raw)

            def next_tok() -> str:
                nonlocal i
                if i + 1 >= len(argv):
                    raise RuntimeError("flag " + raw + " needs a value")
                i += 1
                return argv[i]

            if a == "dir":
                DIR_PATH = next_tok()
            elif a == "out":
                OUT_DIR = next_tok()
            elif a == "efl":
                TARGET_EFL = parse_float(next_tok(), raw)
            elif a == "fno":
                TARGET_FNO = parse_float(next_tok(), raw)
            elif a in ("hfov", "fov"):  # -fov kept as an old name
                TARGET_FOV = parse_float(next_tok(), raw)
            elif a == "clamp":
                CLAMP = True
            elif a == "start":
                START_MODE = pick(next_tok(), ("best", "nearest", "blend"), raw)
            elif a == "layout":
                LAYOUT = next_tok().strip()
            elif a == "weight":
                WEIGHT_MODE = pick(next_tok(), ("near", "soft", "equal"), raw)
            elif a == "ask":
                ASK = True
            elif a == "glass":
                GLASS_MODE = pick(next_tok(), ("nearest", "majority"), raw)
            elif a == "passes":
                PASSES = whole_num(next_tok(), raw, 1, 100)
            elif a == "hammer":
                HAMMER_SEC = whole_num(next_tok(), raw, 0, HAMMER_MAX)
            elif a == "top":
                # "-top all" = optimize every start (the v2 behavior); "-top N" = only the N best-screened.
                tok = next_tok()
                TOP_N = 0 if tok.strip().lower() == "all" else whole_num(tok, raw, 1, 100000)
            elif a == "compare":
                COMPARE = pick(next_tok(), ("reopt", "refocus", "none"), raw)
            elif a == "min":
                MIN_GROUP = whole_num(next_tok(), raw, 2, 100000)
            elif a == "force":
                FORCE = True
            elif a == "nopng":
                NO_PNG = True
            elif a == "nodialog":
                NO_DIALOG = True
            elif a == "quiet":
                QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        elif not DIR_PATH:
            DIR_PATH = raw
        i += 1


def parse_float(s: str, flag: str) -> float:
    """A normal number (nan, inf and 1e999 are refused: they would only cause trouble later)."""
    try:
        v = float(s)
    except ValueError:
        v = float("nan")
    if not finite(v):
        raise RuntimeError("flag " + flag + " needs a number, got " + repr(s))
    return v


def whole_num(s: str, flag: str, lo: int, hi: int) -> int:
    """A whole number from lo to hi ("2.9" or "1e10" is refused instead of being cut)."""
    t = (s or "").strip()
    body = t[1:] if t[:1] in "+-" else t
    if body.isascii() and body.isdigit() and lo <= int(t) <= hi:
        return int(t)
    raise RuntimeError("flag {} needs a whole number from {} to {}, got {!r}".format(flag, lo, hi, s))


def interactive() -> bool:
    """Pop-ups are for people: no -dir (folder picker) or -ask, never with -nodialog."""
    return not NO_DIALOG and (not DIR_PATH or ASK)


def pop_up(text: str, is_error: bool) -> None:
    """Show a message box when someone is watching; the console line is always printed too."""
    if not interactive():
        return
    try:
        import tkinter
        from tkinter import messagebox
        root = tkinter.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        (messagebox.showerror if is_error else messagebox.showwarning)(TOOL, text, parent=root)
        root.destroy()
    except Exception:
        pass  # a pop-up that cannot show must never crash the tool


def report_hint() -> str:
    """' Details: <report path>' when a report was written, so the pop-up says where to look."""
    if RUN_OUTS and os.path.exists(RUN_OUTS["report"]):
        return "\n\nDetails: " + RUN_OUTS["report"]
    return ""


def tidy_failed_run() -> None:
    """
    A run that stopped with an error or a refusal must not leave a result behind
    that blocks the next run (or looks finished when it is not): delete the
    result/start .zmx this run wrote, and any temp lenses. The report stays.
    """
    if not RUN_OUTS:
        return
    if RUN_WROTE_RESULT:
        try_delete(RUN_OUTS["result"])
        try_delete(RUN_OUTS["start"])
        print("Removed this run's unfinished result files.")
    delete_temp_lenses(os.path.dirname(RUN_OUTS["result"]))


def pick(value: str, allowed: Tuple[str, ...], flag: str) -> str:
    v = value.lower()
    if v not in allowed:
        raise RuntimeError("flag " + flag + " must be one of " + "|".join(allowed))
    return v


def choose_folder() -> str:
    """Use -dir if given; otherwise pop up a folder picker (unless -nodialog)."""
    global FOLDER_FROM_DIALOG
    if DIR_PATH:
        return DIR_PATH
    if NO_DIALOG:
        raise ToolExit(2, "no -dir given and -nodialog is set (nothing to read)")
    try:
        import tkinter
        from tkinter import filedialog

        root = tkinter.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        picked = filedialog.askdirectory(
            title="StartPointFinder: pick the folder that holds the starting designs"
        )
        root.destroy()
    except Exception as ex:
        raise ToolExit(2, "folder dialog failed (" + str(ex) + "); pass -dir <folder>")
    if not picked:
        raise ToolExit(2, "no folder picked", True)
    FOLDER_FROM_DIALOG = True
    return picked


# ---------------------------------------------------------------------------
# Entry
# ---------------------------------------------------------------------------

def main(argv: Optional[List[str]] = None) -> int:
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex))
        pop_up("StartPointFinder could not start:\n\n" + str(ex), True)
        return 1

    try:
        zos_root = discover_zos_root()
    except Exception as ex:
        print("FATAL: failed to locate an OpticStudio installation.  " + str(ex))
        pop_up("StartPointFinder: failed to locate an OpticStudio installation.  " + str(ex), True)
        return 1

    app = None
    try:
        folder = choose_folder()
        ZOSAPI = bootstrap_zosapi(zos_root, say=say)
        # Standalone OpticStudio (no window): same as the C# -dir path.
        app = ZOSAPI.ZOSAPI_Connection().CreateNewApplication()
        if app is None or app.PrimarySystem is None:
            raise RuntimeError("could not start a standalone OpticStudio instance")
        if not app.IsValidLicenseForAPI:
            raise RuntimeError("the license is not valid for ZOS-API (status: " + str(app.LicenseStatus) + ")")
        return run(ZOSAPI, app.PrimarySystem, folder)
    except ToolExit as tex:
        say("FATAL: " + str(tex))
        # Exit 3 keeps its result (it was written on purpose); any other stop tidies up.
        if tex.code != 3:
            tidy_failed_run()
        if not tex.user_cancel:
            if tex.code == 3:
                pop_up("The result was saved, but it FAILED the final check:\n\n" + str(tex) + report_hint(), False)
            else:
                pop_up("StartPointFinder stopped (exit {}):\n\n{}{}".format(tex.code, tex, report_hint()), True)
        return int(tex.code)
    except Exception as ex:
        say("FATAL: " + str(ex))
        traceback.print_exc()
        tidy_failed_run()
        pop_up("StartPointFinder hit an error:\n\n" + str(ex) + report_hint(), True)
        return 1
    finally:
        if app is not None:
            try:
                app.CloseApplication()
            except Exception:
                pass


# ---------------------------------------------------------------------------
# The main recipe
# ---------------------------------------------------------------------------

def run(Z, S, folder: str) -> int:
    """
    Big picture:
    1) Read every design in the folder (skip duplicates).
    2) Keep the biggest group with one shared lens layout and object distance.
    3) Work out the envelope (smallest / middle / largest first-order numbers).
    4) Pick and check the target point.
    5) Closeness + blend recipe.
    6) Try every candidate start, keep the best (or the forced one), check, write files.
    """
    global AVAIL_CATS, RUN_OUTS
    clock = time.monotonic()
    folder = os.path.abspath(folder)
    if not os.path.isdir(folder):
        raise ToolExit(2, "folder not found: " + folder)
    every = sorted(
        (str(p) for p in Path(folder).iterdir()
         if p.is_file() and p.suffix.lower() == ".zmx"),
        key=lambda p: os.path.basename(p).upper(),  # same order as C# OrdinalIgnoreCase
    )
    # Our own earlier outputs (StartPointFinder_result.zmx, temp lenses...) are not inputs:
    # reading them would let an old result steer the new one.
    files = [f for f in every if not is_own_output(f)]
    say("=== " + TOOL + " ===")
    say("Folder: " + folder)
    if len(files) < len(every):
        say("Ignored {} earlier {} output file(s) in the folder (they are results, not inputs).".format(
            len(every) - len(files), TOOL))
    if not files:
        raise ToolExit(2, "no .zmx files in " + folder)
    say("Found {} .zmx file(s).".format(len(files)))
    if len(files) > BIG_FOLDER:
        # Every file is opened, and every kept one is built and scored once ("the screen").
        mins = len(files) * 1.5 / 60.0
        say("  Large folder: {} files. Reading and screening take roughly {:.0f} min before any optimizing.".format(
            len(files), mins))
        if interactive() and not ask_yes_no("The folder has {} lens files. Reading and screening them all takes "
                                            "roughly {:.0f} minutes.\n\nContinue?".format(len(files), mins)):
            raise ToolExit(2, "cancelled: large folder", True)

    out_dir = os.path.abspath(OUT_DIR or os.path.join(folder, "_" + TOOL))
    out_dir = guard_outputs(out_dir, folder, files)
    outs = out_paths(out_dir)
    RUN_OUTS = outs  # from here on a failed run tidies up after itself

    # 1) read (the installed catalog list lets a skip reason name a missing catalog)
    AVAIL_CATS = installed_catalogs(S)
    glass_cache: Dict[Tuple[str, str], Tuple[float, float]] = {}
    designs = [read_design(Z, S, f, glass_cache) for f in files]
    # Identical files (or identical prescriptions) count once.
    mark_duplicates(designs)

    # 2) group by layout and object distance
    group, others = pick_group(designs)
    for d in designs:
        if not d.ok:
            say("  SKIP {}: {}".format(d.name, d.reason))
    summary = skip_summary(designs)
    say("Usable: {} of {} file(s){}".format(len(group), len(designs), "; skipped: " + summary if summary else ""))
    for o in others:
        say("  " + o)
    if len(group) < MIN_GROUP:
        write_tables(outs, designs, None, None, None, None, None, None)
        say("REFUSED: not enough usable designs share one layout.")
        write_report(outs)
        raise ToolExit(2, "only {} of {} file(s) usable in one layout group (need {}){}".format(
            len(group), len(designs), MIN_GROUP, "; skipped: " + summary if summary else ""))
    say("Kept {} design(s) with layout {}".format(len(group), group_label(group[0])))

    # 3) envelope (and the distance scales that come from it)
    env = envelope(group)
    set_scales(env)
    for d in group:
        d.on_axis = on_axis_only(d.hfov)
    say("First-order envelope (min / median / max):")
    for key, label in (("efl", "EFL"), ("fno", "F/#"), ("hfov", "half-FOV deg"),
                       ("track_ratio", "track/EFL")):
        e = env[key]
        # 5 digits, so 0.9999995 shows as 1.
        say("  {:<13} {:>10} {:>10} {:>10}".format(label, g(e[0], 5), g(e[1], 5), g(e[2], 5)))
    finite_obj = [d.obj_dist for d in group if finite(d.obj_dist)]
    obj = statistics.median(finite_obj) if finite_obj else float("inf")
    if finite(obj):
        say("Object distance: held at {} for every candidate (all kept designs share it).".format(g(obj)))

    # 4) target: ask in a small window when interactive, then check it against the envelope
    if not NO_DIALOG and (FOLDER_FROM_DIALOG or ASK):
        ask_targets(group, env)
    target = pick_target(env)
    target["obj"] = obj
    say("Target: EFL {} mm  F/{}  half-FOV {} deg".format(
        g(target["efl"]), g(target["fno"]), g(target["hfov"])))
    # Every candidate is scored with ray aiming off (like a new lens), so a lens rebuilt
    # from its own file and a lens built from numbers are scored alike. Say so when
    # some inputs had it on: the user may want to switch it back on afterward.
    aimed = [d for d in group if d.ray_aim != 0]
    if aimed:
        say("  Ray aiming: off for every candidate ({} of {} inputs use {}); turn it on in OpticStudio afterward if your design needs it.".format(
            len(aimed), len(group), ray_aim_name(aimed[0].ray_aim)))
    pair = pair_check(group, target["fno"], target["hfov"])
    say("  " + pair["text"])
    if pair["warn"]:
        say("  WARNING: this F/# + field pair is extrapolation in 2D (each value is inside its own range, "
            "but no input design was made for this combination).")

    # Blend members: Standard surfaces only (aspheres are rebuilt from their own file),
    # and for a field request no on-axis-only design: its shape was never asked to
    # handle field, so it should not move the average. It stays a candidate.
    field_ask = target["hfov"] > 1e-6
    for d in group:
        d.blend_member = not d.is_special and not (d.on_axis and field_ask)
    members = [d for d in group if d.blend_member]
    if len(members) < len(group):
        left = [d for d in group if not d.blend_member]
        say("Not averaged into the blend: {}".format("; ".join(
            "{} [{}] (own file)".format(d.name, d.special_note) if d.is_special else "{} (on-axis only)".format(d.name)
            for d in left)))

    # 5) closeness weights; the blend recipe uses Standard-surface, field designs only
    blend_ok = len(members) >= 2
    mode = START_MODE
    if mode == "blend" and not blend_ok:
        say("  -start blend needs 2+ blend members (Standard-surface; no on-axis-only design for a field request; have {}); using nearest instead.".format(len(members)))
        mode = "nearest"
    set_weights(group, members, target)
    say_weights(group, pair, mode)
    bounds = thickness_bounds(group)
    catalogs = sorted({s.catalog for d in group for s in d.surfs if s.catalog}, key=str.lower)
    blend_rx = None
    if blend_ok:
        blend_rx, glass_notes = start_shape(members)
        for note in glass_notes:
            say("  blend " + note)
    else:
        say("  blend n/a: only {} blend member(s) in the group.".format(len(members)))

    # 6) candidates: each is sized to the target, refocused and scored ("the screen").
    # Then the chosen ones are optimized with the same report card. The keeper is saved as it goes.
    os.makedirs(out_dir, exist_ok=True)
    x = {"target": target, "env": env, "bounds": bounds, "catalogs": catalogs, "stop": group[0].stop,
         "blend_count": len(members), "outs": outs, "tmp": os.path.join(out_dir, TOOL + "_candidate.tmp.zmx"),
         "out_dir": out_dir, "mode": mode, "pair": pair, "chosen": None, "nopt": 0}
    cands: List[Cand] = []
    reopt = COMPARE == "reopt"
    # The top-N screen only applies to -start best with the full re-optimized comparison.
    screen = mode == "best" and reopt and TOP_N > 0
    # Temp lenses must go on every way out (error, Ctrl+C), not only on success.
    try:
        if screen:
            # Step A: build and score every start WITHOUT optimizing (fast: no DLS at all).
            say("Screening every start at the request (sized + refocused, no optimization):")
            for d in group:
                c = evaluate(Z, S, x, "input", d, None, False)
                d.mf_refocus = c.mf_start
                cands.append(c)
            if blend_ok:
                cands.append(evaluate(Z, S, x, "blend", None, blend_rx, False))
            set_screen_ranks(cands)
            picked = {id(c) for c in cands if 0 < c.screen_rank <= TOP_N}
            for c in sorted(cands, key=lambda c: c.screen_rank):
                say("  #{:<2} {:<34} start MF {:>12}{}{}".format(
                    c.screen_rank, c.label, g(c.mf_start), "   -> optimize" if id(c) in picked else "",
                    "   (" + c.note + ")" if c.note else ""))
            say("Optimizing the {} best-screened start(s) of {} (-top {}; -top all optimizes every start):".format(
                len(picked), len(cands), TOP_N))
            # Step B: optimize only the picked starts, best-screened first. Each is rebuilt exactly
            # as in the screen (same numbers), so its result is the same as when every start was optimized.
            # Safety net: if none of the top N passes the checks, keep going down the screen
            # list one start at a time until one passes (or every start has been tried).
            fallback_said = False
            for i in sorted(range(len(cands)), key=lambda i: cands[i].screen_rank):
                c = cands[i]
                if id(c) not in picked:
                    if any(o.optimized and o.passed for o in cands):
                        if c.d is not None:
                            c.d.note = "not optimized (screen rank {} of {}; -top {})".format(c.screen_rank, len(cands), TOP_N)
                        continue
                    if not fallback_said:
                        say("  None of the top {} passed the checks; optimizing the next screened start(s) until one passes:".format(TOP_N))
                        fallback_said = True
                rank = c.screen_rank
                c = evaluate(Z, S, x, c.kind, c.d, blend_rx if c.kind == "blend" else None, True)
                c.screen_rank = rank
                cands[i] = c
                if c.d is not None:
                    c.d.mf_reopt, c.d.spot_reopt_um, c.d.reopt_pass = c.mf, c.spot, c.passed
                say("  #{:<2} {:<34} start MF {:>12} -> MF {:>12}  spot {:>8} um{}".format(
                    rank, c.label, g(c.mf_start), g(c.mf), g(c.spot, 4),
                    "   FAILS CHECK: " + c.fail if c.optimized and not c.passed else ""))
        else:
            if COMPARE != "none":
                say("Scoring every kept input at the request ({}):".format(COMPARE))
                for d in group:
                    c = evaluate(Z, S, x, "input", d, None, reopt)
                    d.mf_refocus = c.mf_start
                    if reopt:
                        d.mf_reopt, d.spot_reopt_um, d.reopt_pass = c.mf, c.spot, c.passed
                        cands.append(c)
                    say("  {:<28} refocus MF {:>12}   reopt MF {:>12}   reopt spot {:>8} um{}{}".format(
                        d.name, g(d.mf_refocus), g(d.mf_reopt), g(d.spot_reopt_um, 4),
                        "   (own file, scaled)" if d.is_special else "",
                        "   FAILS CHECK: " + c.fail if c.optimized and not c.passed else ""))
            # Inputs not optimized above: the nearest input is still a candidate.
            if not reopt and pair["nearest"] is not None and mode != "blend":
                cands.append(evaluate(Z, S, x, "input", pair["nearest"], None, True))
            if blend_ok and (mode != "nearest" or COMPARE != "none"):
                cands.append(evaluate(Z, S, x, "blend", None, blend_rx, True))
            set_screen_ranks(cands)
        try_delete(x["tmp"])
        chosen: Optional[Cand] = x["chosen"]
        if chosen is None:
            raise ToolExit(1, "no candidate start could be built and optimized (see the lines above)")

        # Optional Hammer polish: the best few lenses that pass the checks (or just the forced
        # keeper for -start nearest/blend), then keep the best again. It writes the keeper's files.
        if HAMMER_SEC > 0:
            chosen = hammer_polish(Z, S, x, cands, chosen)
    finally:
        try_delete(x["tmp"])
        cleanup_cand_files(cands)
    S.LoadFile(outs["result"], False)
    mf_result = chosen.mf
    mf_start = chosen.mf_start
    say("Chosen start: {}. MF {} before optimization, {} after ({})".format(
        chosen.label, g(mf_start), g(mf_result), outs["start"]))
    result_fo = measure(Z, S, target["obj"])
    checks = validate(Z, S, result_fo, target, env, bounds, mf_result)
    say("Saved: " + outs["result"])
    png_ok = False
    if not NO_PNG:
        png_ok = draw_layout_png(Z, S, outs["png"], target, mf_result, chosen.label)

    # Ranked list of every optimized candidate: ones that pass the checks first, then by score.
    ranked = ranked_cands(cands)
    say("Candidates at the request (same report card, each optimized), best first:")
    for r, c in enumerate(ranked):
        tags = ["screen #{}".format(c.screen_rank)] if c.screen_rank else []
        if c is chosen:
            tags.append("CHOSEN")
        if c.d is not None and c.d is pair["nearest"]:
            tags.append("nearest")
        if c.d is not None and c.d.is_special:
            tags.append("own file")
        if c.d is not None and c.d.on_axis:
            tags.append("on-axis input")
        if c.ham_note:
            tags.append(c.ham_note)
        if not c.passed:
            tags.append("fails check: " + c.fail)
        say("  {:>2}. {:<34} start MF {:>12} -> MF {:>12}  spot {:>8} um{}".format(
            r + 1, c.label, g(c.mf_start), g(c.mf), g(c.spot, 4), "  [" + ", ".join(tags) + "]" if tags else ""))
    skipped_screen = [c for c in cands if not c.optimized and c.screen_rank]
    if screen and skipped_screen:
        say("  Not optimized (outside the top {} of the screen): {}".format(TOP_N, ", ".join(
            "#{} {}".format(c.screen_rank, c.label) for c in sorted(skipped_screen, key=lambda c: c.screen_rank))))
    # Say plainly when "best" stepped over a lower score because that lens failed the checks.
    if mode == "best":
        skipped = sum(1 for c in ranked if not c.passed and finite(c.mf) and c.mf < chosen.mf)
        if not chosen.passed:
            say("  No candidate passed the checks; kept the lowest score (see the check below).")
        elif skipped > 0:
            say("  Passed over {} lower-scoring candidate(s) that failed the checks.".format(skipped))
    # Blend and nearest are compared on their DLS scores (before any Hammer), like the inputs table.
    near_mf = next((dls_mf(c) for c in cands if c.optimized and c.d is not None and c.d is pair["nearest"]),
                   float("nan"))
    blend_mf = next((dls_mf(c) for c in cands if c.kind == "blend" and c.optimized), float("nan"))
    secs = time.monotonic() - clock

    write_tables(outs, designs, env, target, mf_start, mf_result, result_fo, checks, pair, blend_mf,
                 cands, chosen, secs)
    best_name, best_mf = best_input(group)
    say("")
    say("=== RESULT ===")
    say("Chosen start: {} (-start {})".format(chosen.label, mode))
    say("Result MF {} (mean RMS spot {} um)  vs best input at target ({}) MF {}".format(
        g(mf_result), g(result_fo["spot_um"], 4), best_name or "n/a", g(best_mf)))
    if finite(best_mf) and finite(mf_result):
        say(versus_text("the best input re-sized to the target", mf_result, best_mf))
    # Blend vs nearest-design start, both optimized with the same report card.
    if finite(near_mf) and finite(blend_mf) and pair["nearest"] is not None:
        if abs(blend_mf / near_mf - 1.0) < 5e-4:
            verdict = "they match"
        else:
            verdict = "{}, blend {:+.1f}%".format("blend wins" if blend_mf < near_mf else "nearest wins",
                                                  100.0 * (blend_mf / near_mf - 1.0))
        say("Blend vs nearest-design start ({}): {} (blend MF {}, nearest MF {}).".format(
            pair["nearest"].name, verdict, g(blend_mf), g(near_mf)))
    say("Envelope check: " + ("PASS" if checks["pass"] else "FAIL"))
    for item in checks["items"]:
        say("  [{}] {}".format("ok" if item[1] else "XX", item[0]))
    say("Runtime: {:.0f} s ({} optimization(s))".format(secs, x["nopt"]))
    say("Files: " + out_dir)
    write_report(outs)
    if not QUIET and not DIR_PATH and png_ok and os.name == "nt":
        open_file(outs["png"])
    say("RETURN start={} chosen={} nearest={} mf_start={} mf_result={} best_input_mf={} nearest_mf={} blend_mf={} "
        "pair_warn={} envelope={} secs={:.0f} top={} nopt={} screen_rank={} hammer={} save={}".format(
            mode, chosen.label, pair["nearest"].name if pair["nearest"] else "n/a", g(mf_start, 8), g(mf_result, 8),
            g(best_mf, 8), g(near_mf, 8), g(blend_mf, 8),
            "yes" if pair["warn"] else "no", "PASS" if checks["pass"] else "FAIL", secs,
            TOP_N if screen else "all", x["nopt"], chosen.screen_rank, HAMMER_SEC, outs["result"]))
    if not checks["pass"]:
        raise ToolExit(3, "result was written but failed the envelope check (see report)")
    return 0


def versus_text(what: str, mf: float, reference: float) -> str:
    """'matches' / 'is better than' / 'is worse than', with the percentage."""
    rel = mf / reference - 1.0
    if abs(rel) < 5e-4:
        return "Result matches " + what + "."
    return "Result is {} than {} ({:+.1f}%).".format("better" if rel < 0 else "worse", what, 100.0 * rel)


def evaluate(Z, S, x, kind: str, d: Optional[Design], rx, optimize_it: bool) -> Cand:
    """
    Try one candidate start at the request: build it, refocus, score it; with
    optimize_it, save it as a temp start, polish it, and keep it if it is the one
    this mode wants (best = passes the checks, then lowest MF so far). The keeper's start and result are
    written right away, so nothing has to be rebuilt at the end.
    """
    global RUN_WROTE_RESULT
    c = Cand(kind=kind, d=d, label="blend of {} designs".format(x["blend_count"]) if kind == "blend" else d.name)
    try:
        t = x["target"]
        if kind == "blend":
            build_system(Z, S, rx, x["stop"], t, x["catalogs"], "StartPointFinder blended start")
        elif d.is_special:
            build_native(Z, S, d, t, "StartPointFinder start = input " + d.name + " (own file, scaled)")
        else:
            build_system(Z, S, scaled_rx(d), d.stop, t, x["catalogs"],
                         "StartPointFinder start = input " + d.name + " (scaled)")
        build_merit(Z, S, t, x["env"], x["bounds"])
        quick_focus(Z, S)
        c.mf_start = merit(S)
        if not optimize_it:
            return c
        S.SaveAs(x["tmp"])
        make_variables(Z, S, x["stop"] if kind == "blend" else d.stop)
        c.mf = optimize(Z, S, PASSES, 0)
        c.spot = mean_spot_um(Z, S)
        c.optimized = True
        x["nopt"] += 1
        # Give every optimized candidate the same honest checks as the final result.
        # A low score is not enough: the optimizer can "win" by making rays miss a
        # surface or by bending an asphere so its edge no longer exists.
        chk = validate(Z, S, measure(Z, S, t["obj"]), t, x["env"], x["bounds"], c.mf)
        c.passed = chk["pass"]
        c.edge_ok = chk["edge_ok"]
        c.fail = "; ".join(short_check(text) for text, ok in chk["items"] if not ok)
        c.mf_dls, c.spot_dls = c.mf, c.spot
        if HAMMER_SEC > 0:
            # -hammer polishes several candidates later, so keep a temp copy of each
            # optimized lens and its start (deleted again before the tool ends).
            stem = os.path.join(x["out_dir"], "{}_cand{:02d}".format(TOOL, x["nopt"]))
            c.opt_path, c.start_path = stem + ".tmp.zmx", stem + "_start.tmp.zmx"
            S.SaveAs(c.opt_path)
            shutil.copyfile(x["tmp"], c.start_path)
        chosen = x["chosen"]
        if x["mode"] == "best":
            # best = a lens that passes beats one that fails; then the lower score wins.
            keep = finite(c.mf) and (chosen is None or better(c, chosen))
        elif x["mode"] == "blend":
            keep = kind == "blend"
        else:
            keep = kind == "input" and d is x["pair"]["nearest"]
        if keep:
            RUN_WROTE_RESULT = True  # set first: a half-written pair is tidied up too
            S.SaveAs(x["outs"]["result"])
            shutil.copyfile(x["tmp"], x["outs"]["start"])
            x["chosen"] = c
    except Exception as ex:
        c.note = "failed: " + str(ex)
        say("  candidate {} failed: {}".format(c.label, ex))
    return c


def better(a: Cand, b: Cand) -> bool:
    """Is candidate a a better keeper than b? Passing the checks counts first, then the score."""
    return (a.passed and not b.passed) or (a.passed == b.passed and a.mf < b.mf)


def ranked_cands(cands) -> List[Cand]:
    """Optimized candidates in report order: passing ones first, each part by score."""
    return sorted([c for c in (cands or []) if c.optimized],
                  key=lambda c: (0 if c.passed else 1, c.mf if finite(c.mf) else float("inf")))


def set_screen_ranks(cands: List[Cand]) -> None:
    """
    Number the starts by their report-card score before optimizing (1 = lowest).
    A start whose score could not be computed (rays fail) goes to the end.
    Ties keep their reading order, so the C# and Python builds rank the same way.
    """
    order = sorted(range(len(cands)), key=lambda i: (
        cands[i].mf_start if finite(cands[i].mf_start) else float("inf"), i))
    for r, i in enumerate(order):
        cands[i].screen_rank = r + 1


def dls_mf(c: Cand) -> float:
    """A candidate's score after DLS only (before any Hammer polish)."""
    return c.mf_dls if finite(c.mf_dls) else c.mf


def hammer_polish(Z, S, x, cands: List[Cand], chosen: Cand) -> Cand:
    """
    Hammer = a longer "shake it and walk downhill again" search that stops after
    HAMMER_SEC seconds of wall time (so results vary a little from PC to PC).
    -start best: polish the best HAMMER_KEEP optimized lenses that pass the checks
    (the keeper alone if none pass), check each again, and keep the best.
    -start nearest/blend: polish only the forced keeper.
    A polished lens that now FAILS the checks is not used when its DLS version passed.
    Writes the keeper's result and start files and returns the (possibly new) keeper.
    """
    global RUN_WROTE_RESULT
    if x["mode"] == "best":
        polish = [c for c in ranked_cands(cands) if c.passed][:HAMMER_KEEP] or [chosen]
    else:
        polish = [chosen]
    t = x["target"]
    say("Hammer polish ({} s each, wall-time limited) on {} lens(es):".format(HAMMER_SEC, len(polish)))
    for c in polish:
        if not c.opt_path or not os.path.exists(c.opt_path):
            continue
        S.LoadFile(c.opt_path, False)
        mf_h = optimize(Z, S, 0, HAMMER_SEC)
        fo = measure(Z, S, t["obj"])
        chk = validate(Z, S, fo, t, x["env"], x["bounds"], mf_h)
        fail_h = "; ".join(short_check(text) for text, ok in chk["items"] if not ok)
        # Use the polished lens when it passes and is not worse than the DLS lens (or the
        # DLS lens failed anyway), or when neither version passes and it scores lower.
        worse = finite(c.mf) and mf_h > c.mf
        use = finite(mf_h) and ((chk["pass"] and (not c.passed or not worse)) or (not c.passed and mf_h < c.mf))
        if use:
            ham_path = c.opt_path.replace(".tmp.zmx", "_ham.tmp.zmx")
            S.SaveAs(ham_path)
            c.opt_path = ham_path
            c.mf, c.spot, c.passed, c.fail, c.edge_ok = mf_h, fo["spot_um"], chk["pass"], fail_h, chk["edge_ok"]
            c.hammered = True
            c.ham_note = "Hammer {} -> {}".format(g(c.mf_dls), g(mf_h))
        elif not finite(mf_h):
            c.ham_note = "Hammer gave no usable score; kept DLS"
        elif chk["pass"] and worse:
            c.ham_note = "Hammer result {} is worse than DLS; kept DLS".format(g(mf_h))
        else:
            c.ham_note = "Hammer result {} failed checks ({}); kept DLS".format(g(mf_h), fail_h)
        say("  {:<34} DLS MF {:>12} -> Hammer MF {:>12}  spot {:>8} um  {}".format(
            c.label, g(c.mf_dls), g(mf_h), g(fo["spot_um"], 4),
            "used" if use else "NOT used (" + c.ham_note + ")"))
    # Pick the keeper again among every optimized candidate (polished ones now carry Hammer numbers).
    if x["mode"] == "best":
        for c in ranked_cands(cands):
            if finite(c.mf) and better(c, chosen):
                chosen = c
    if chosen.opt_path and os.path.exists(chosen.opt_path):
        RUN_WROTE_RESULT = True
        shutil.copyfile(chosen.opt_path, x["outs"]["result"])
        shutil.copyfile(chosen.start_path, x["outs"]["start"])
    x["chosen"] = chosen
    return chosen


def cleanup_cand_files(cands: List[Cand]) -> None:
    """Delete the per-candidate temp lenses that -hammer needed."""
    for c in cands:
        for pth in (c.opt_path, c.start_path, c.opt_path.replace("_ham.tmp.zmx", ".tmp.zmx")):
            if pth:
                try_delete(pth)


def short_check(item: str) -> str:
    """
    The short name of a failed check: its words up to the first number or
    comparison, so "thinnest glass edge -0.3 > 0" becomes "thinnest glass edge".
    """
    i = item.find(" (")
    if i > 0:
        item = item[:i]
    keep = []
    for w in item.split(" "):
        if w in ("vs", "inside", ">=", ">", "<", "<="):
            break
        try:
            float(w)
            break
        except ValueError:
            pass
        keep.append(w)
    if len(keep) > 1 and keep[-1] == "and":
        keep.pop()  # "finite and < 1e8"
    return " ".join(keep) if keep else item


def scaled_rx(d: Design) -> List[SurfRx]:
    """An input's own recipe at focal length 1 (curvature x EFL, thickness / EFL)."""
    return [SurfRx(s.curv * d.efl, s.thick / d.efl, s.glass, s.catalog, s.conic) for s in d.surfs]


def try_delete(path: str) -> None:
    """Remove a temp file (and a same-named side file OpticStudio may write)."""
    folder, stem = os.path.dirname(path), os.path.splitext(os.path.basename(path))[0]
    try:
        for name in os.listdir(folder):
            if name == os.path.basename(path) or name.startswith(stem + "."):
                try:
                    os.remove(os.path.join(folder, name))
                except OSError:
                    pass
    except OSError:
        pass


def open_file(path: str) -> None:
    """Open a picture with Windows' default viewer (explorer handles the file type)."""
    try:
        os.startfile(path)  # type: ignore[attr-defined]
    except Exception:
        try:
            subprocess.Popen(["explorer.exe", path])
        except Exception as ex:
            say("  Could not open {}: {}".format(path, ex))


def write_report(outs: Dict[str, str]) -> None:
    """Save every line we printed into the text report."""
    os.makedirs(os.path.dirname(outs["report"]), exist_ok=True)
    with open(outs["report"], "w", encoding="utf-8") as fh:
        fh.write("\n".join(REPORT_LINES) + "\n")


def out_paths(out_dir: str) -> Dict[str, str]:
    """The 6 output files of a run in one folder."""
    return {k: os.path.join(out_dir, v) for k, v in OUT_NAMES.items()}


def guard_outputs(out_dir: str, folder: str, inputs: List[str]) -> str:
    """
    Decide where this run writes, before anything is read:
    - never into the input folder itself (the results would be read as inputs next time);
    - never over an input file;
    - an earlier RESULT there is only replaced with -force, or after asking (window):
      Replace / New folder (_StartPointFinder_2, _3, ...) / Cancel;
    - leftovers of a run that never finished (report or tables without a result, temp
      lenses) never block: they are cleared, like every old output we are about to rewrite,
      so the folder never mixes files from two runs.
    Returns the output folder to use.
    """
    if same_path(out_dir, folder):
        raise ToolExit(2, "the output folder is the input folder (" + out_dir
                       + "); the results would be read as inputs next time. Pick another -out folder.")
    low_inputs = {os.path.normcase(os.path.abspath(p)) for p in inputs}
    for path in out_paths(out_dir).values():
        if os.path.normcase(path) in low_inputs:
            raise ToolExit(2, "output would overwrite an input: " + path)
    if os.path.exists(out_paths(out_dir)["result"]) and not FORCE:
        if not interactive():
            raise ToolExit(2, "a result already exists (pass -force to replace it): " + out_paths(out_dir)["result"])
        fresh = next_free_dir(out_dir)
        answer = ask_replace(out_dir, fresh)
        if answer == "replace":
            say("Replacing the earlier result in " + out_dir + " (you chose Replace).")
        elif answer == "new":
            out_dir = fresh
            say("Writing to a new folder: " + out_dir + " (you chose New folder).")
        else:
            raise ToolExit(2, "cancelled: a result already exists in " + out_dir, True)
    # Clear our own old files here (only the 6 output names and our temp lenses).
    cleared = 0
    for p in out_paths(out_dir).values():
        if os.path.exists(p):
            try_delete(p)
            cleared += 1
    cleared += delete_temp_lenses(out_dir)
    if cleared:
        say("Cleared {} old output file(s) in {}.".format(cleared, out_dir))
    return out_dir


def ask_replace(out_dir: str, fresh: str) -> str:
    """
    The question when the folder already holds a result: "replace", "new" (folder)
    or "cancel". A tiny window with plain button names; Enter = New folder (safe).
    """
    try:
        import tkinter as tk
    except Exception:
        return "cancel"
    answer = {"v": "cancel"}
    root = tk.Tk()
    root.title(TOOL + ": earlier result found")
    root.attributes("-topmost", True)
    root.resizable(False, False)
    tk.Label(root, justify="left", wraplength=480, text=(
        "This folder already holds a StartPointFinder result:\n" + out_dir
        + "\n\nReplace it, or write this run to a new folder (" + os.path.basename(fresh) + ")?")).pack(
        padx=12, pady=12, anchor="w")
    row = tk.Frame(root)
    row.pack(padx=12, pady=(0, 12), anchor="e")

    def choose(v: str) -> None:
        answer["v"] = v
        root.destroy()

    tk.Button(row, text="Replace", width=12, command=lambda: choose("replace")).pack(side="left", padx=4)
    new_btn = tk.Button(row, text="New folder", width=12, command=lambda: choose("new"))
    new_btn.pack(side="left", padx=4)
    tk.Button(row, text="Cancel", width=12, command=lambda: choose("cancel")).pack(side="left", padx=4)
    root.bind("<Return>", lambda e: choose("new"))
    root.bind("<Escape>", lambda e: choose("cancel"))
    root.protocol("WM_DELETE_WINDOW", lambda: choose("cancel"))
    new_btn.focus_set()
    root.mainloop()
    return answer["v"]


def ask_yes_no(text: str) -> bool:
    """A plain Yes/No question box (tkinter)."""
    try:
        import tkinter
        from tkinter import messagebox
        root = tkinter.Tk()
        root.withdraw()
        root.attributes("-topmost", True)
        ok = messagebox.askyesno(TOOL, text, parent=root)
        root.destroy()
        return bool(ok)
    except Exception:
        return True  # no window possible: carry on, the console already says it


def next_free_dir(out_dir: str) -> str:
    """The first of dir_2, dir_3, ... that has no result in it yet."""
    base = out_dir.rstrip("\\/")
    k = 2
    while os.path.exists(out_paths(base + "_" + str(k))["result"]):
        k += 1
    return base + "_" + str(k)


def same_path(a: str, b: str) -> bool:
    """Same folder? (full paths, any case, with or without a trailing slash)"""
    return os.path.normcase(os.path.abspath(a)).rstrip("\\/") == os.path.normcase(os.path.abspath(b)).rstrip("\\/")


def is_own_output(path: str) -> bool:
    """A file this tool writes (result, start, temp lenses), not a design someone made."""
    n = os.path.basename(path).lower()
    if not n.startswith(TOOL.lower() + "_"):
        return False
    return n.endswith(".tmp.zmx") or n in {v.lower() for v in OUT_NAMES.values()}


def delete_temp_lenses(folder: str) -> int:
    """Delete our temp lenses (StartPointFinder_*.tmp.zmx and their side files) in a folder."""
    k = 0
    try:
        for name in os.listdir(folder):
            low = name.lower()
            if low.startswith(TOOL.lower() + "_") and low.endswith(".tmp.zmx"):
                try_delete(os.path.join(folder, name))
                k += 1
    except OSError:
        pass
    return k


def ray_aim_name(r: int) -> str:
    """Ray aiming in words (0 off, 1 paraxial, 2 real)."""
    return {1: "paraxial", 2: "real"}.get(r, "off")


# ---------------------------------------------------------------------------
# Step 1: read one design
# ---------------------------------------------------------------------------

def read_design(Z, S, path: str, glass_cache) -> Design:
    """
    Open one file (read only) and write down its shape and first-order numbers.
    Anything we cannot use gets a reason and a short category (for the
    "skipped: 3 finite object, ..." summary).
    """
    d = Design(name=os.path.basename(path), path=path)
    try:
        with open(path, "rb") as fh:
            raw = fh.read()
        d.hash = hashlib.sha256(raw).hexdigest()
        d.file_catalogs = gcat_list(raw)
        if not S.LoadFile(path, False):
            return reject(d, "could not load", "could not load")
        if S.Mode != Z.SystemType.Sequential:
            return reject(d, "not a Sequential system", "not sequential")
        # A zoom lens (several configurations) is several lenses in one file; we would
        # only see whichever one was active when it was saved, so it is skipped.
        n_conf = int(S.MCE.NumberOfConfigurations)
        if n_conf > 1:
            return reject(d, "has {} configurations (zoom or multi-setup files are not supported)".format(n_conf),
                          "multi-configuration")
        # Inches, cm or m: convert the copy in memory to mm (OpticStudio's own unit
        # conversion; the file on disk is not touched), so every number below is mm.
        d.units = unit_name(Z, S.SystemData.Units.LensUnits)
        if d.units != "mm":
            to_millimeters(Z, S)
        try:
            d.ray_aim = int(S.SystemData.RayAiming.RayAiming)
        except Exception:
            d.ray_aim = 0
        lde = S.LDE
        n = lde.NumberOfSurfaces
        d.nsurf = n
        if n < 3:
            return reject(d, "has no lens surfaces between object and image", "no lens surfaces")
        d.stop = int(lde.StopSurface)
        # Infinite object (objectives) or a finite one; a finite one must match the group's.
        t0 = float(lde.GetSurfaceAt(0).Thickness)
        d.obj_dist = t0 if finite(t0) and abs(t0) < 1e9 else float("inf")
        ST = Z.Editors.LDE.SurfaceType
        mask, special = [], []
        for i in range(1, n - 1):
            s = lde.GetSurfaceAt(i)
            std = s.Type == ST.Standard
            allowed = std or s.Type in (ST.EvenAspheric, ST.ZernikeStandardSag, ST.Paraxial)
            if not allowed:
                return reject(d, "surface {} is {} (Standard, Even Asphere, Zernike Standard Sag and Paraxial are "
                                 "supported)".format(i, s.TypeName), "unsupported surface type")
            if not std:
                special.append("S{} {}".format(i, s.TypeName))
            mat = (s.Material or "").strip()
            if mat.upper() == "MIRROR":
                return reject(d, "surface {} is a mirror (refractive objectives only)".format(i), "mirror")
            cat = ""
            if mat:
                try:
                    cat = (s.MaterialCatalog or "").strip()
                except Exception:
                    cat = ""
                if not cat:
                    return reject(d, "surface {} glass '{}' has no catalog (model glass not supported)".format(i, mat),
                                  "model glass")
            r = s.Radius
            curv = 0.0 if (not finite(r) or abs(r) > 1e10 or r == 0) else 1.0 / r
            conic = s.Conic if finite(s.Conic) else 0.0
            d.surfs.append(SurfRx(curv, float(s.Thickness), mat, cat, float(conic),
                                  "" if std else str(s.TypeName), None if std else read_pars(Z, s)))
            mask.append("G" if mat else "A")
            if mat:
                # A glass that does not resolve comes back as air (nd 1, Vd 0): say which catalog is missing.
                nd, vd = glass_index(Z, S, cat, mat, glass_cache)
                d.glass_nd_vd[i] = (nd, vd)
                if not (nd > 1.0001 and vd > 0):
                    # Only blame a catalog when we could read the list of installed ones.
                    missing = [c for c in d.file_catalogs if c not in AVAIL_CATS] if AVAIL_CATS else []
                    if missing:
                        return reject(d, "surface {} glass '{}' does not resolve: catalog {} is not installed".format(
                            i, mat, ", ".join(missing)), "missing glass catalog")
                    return reject(d, "surface {} glass '{}' does not resolve in catalog {}".format(i, mat, cat),
                                  "unresolved glass")
        d.is_special = bool(special)
        d.special_note = ", ".join(special)
        # Layout fingerprint: surface count, stop position, glass/air pattern.
        d.signature = "{}surf/stop{}/{}".format(n, d.stop, "".join(mask))
        d.efl = op(Z, S, "EFFL", 0, primary_wave(S))
        d.fno = op(Z, S, "ISFN")
        d.track = op(Z, S, "TOTR")
        if not finite(d.efl) or d.efl <= 0:
            return reject(d, "EFL is not a positive number ({})".format(g(d.efl)), "EFL not positive")
        # Absurd first-order data means the file is not what it looks like (e.g. glass gone to air).
        if not (d.efl < 1e6) or not (0.05 <= d.fno <= 1000) or not finite(d.track):
            return reject(d, "first-order data is not physical (EFL {}, F/# {}, track {})".format(
                g(d.efl), g(d.fno), g(d.track)), "unphysical first order")
        d.hfov = half_fov(Z, S, d.efl, d.obj_dist)
        if not finite(d.hfov):
            return reject(d, "field type {} is not supported here".format(S.SystemData.Fields.GetFieldType()),
                          "field type")
        d.ok = True
        if d.units != "mm":
            say("  {}: lens units {}, converted to mm for this run (file unchanged).".format(d.name, d.units))
        return d
    except Exception as ex:
        return reject(d, "read error: " + str(ex), "read error")


def reject(d: Design, reason: str, cat: str) -> Design:
    """Mark a design skipped with a reason (long) and a category (short)."""
    d.ok, d.reason, d.cat = False, reason, cat
    return d


def unit_name(Z, u) -> str:
    """Lens units as short text: "mm", "cm", "in", "m"."""
    U = Z.SystemData.ZemaxSystemUnits
    return {int(U.Millimeters): "mm", int(U.Centimeters): "cm", int(U.Inches): "in"}.get(int(u), "m")


def to_millimeters(Z, S) -> None:
    """
    Convert the lens in memory to millimeters with OpticStudio's Scale Lens tool
    ("scale by units": every length, asphere term and aperture is converted, and
    the lens units become mm). Raises when it does not end up in mm.
    """
    sc = S.Tools.OpenScale()
    if sc is None:
        raise RuntimeError("could not open the Scale Lens tool to convert units")
    try:
        sc.ScaleByUnits = True
        sc.ScaleToUnit = Z.Tools.General.ScaleToUnits.Millimeters
        sc.RunAndWaitForCompletion()
    finally:
        sc.Close()
    if unit_name(Z, S.SystemData.Units.LensUnits) != "mm":
        raise RuntimeError("could not convert the lens units to mm")


def read_pars(Z, s) -> List[float]:
    """Par1..Par8 of a surface (even-asphere terms on Even Asphere / Zernike Standard Sag)."""
    vals = []
    for k in range(1, 9):
        try:
            vals.append(float(s.GetSurfaceCell(getattr(Z.Editors.LDE.SurfaceColumn, "Par{}".format(k))).DoubleValue))
        except Exception:
            vals.append(float("nan"))
    return vals


def asphere_terms(Z, s) -> Optional[List[float]]:
    """Even-asphere terms of a surface, or None for a surface without them."""
    ST = Z.Editors.LDE.SurfaceType
    return read_pars(Z, s) if s.Type in (ST.EvenAspheric, ST.ZernikeStandardSag) else None


def norm_cat(c: str) -> str:
    """Catalog name in one spelling: upper case, no '.AGF'."""
    u = (c or "").strip().upper()
    return u[:-4] if u.endswith(".AGF") else u


def decode_zmx(b: bytes) -> str:
    """.zmx text: UTF-16 (with or without BOM), UTF-8 with BOM, else Latin-1."""
    if b[:2] == b"\xff\xfe":
        return b[2:].decode("utf-16-le", errors="replace")
    if b[:2] == b"\xfe\xff":
        return b[2:].decode("utf-16-be", errors="replace")
    if b[:3] == b"\xef\xbb\xbf":
        return b[3:].decode("utf-8", errors="replace")
    if len(b) >= 2 and b[1] == 0:
        return b.decode("utf-16-le", errors="replace")
    return b.decode("latin-1")


def gcat_list(raw: bytes) -> List[str]:
    """The glass catalogs the file asks for (its 'GCAT' line)."""
    for line in decode_zmx(raw).split("\n"):
        line = line.strip()
        if line[:5].upper() in ("GCAT ", "GCAT\t"):
            return [norm_cat(c) for c in line[5:].split()]
    return []


def installed_catalogs(S) -> set:
    """Glass catalogs installed on this PC (names in norm_cat spelling)."""
    try:
        return {norm_cat(str(c)) for c in S.SystemData.MaterialCatalogs.GetAvailableCatalogs()}
    except Exception:
        return set()


def mark_duplicates(designs: List[Design]) -> None:
    """
    Byte-identical files count once; so do files with the same lens data
    (same surfaces, glasses, aperture and field) saved with other settings.
    """
    seen: Dict[str, str] = {}
    for d in designs:
        if not d.hash:
            continue
        if d.hash in seen:
            reject(d, "duplicate of {} (identical file content)".format(seen[d.hash]), "duplicate file")
        else:
            seen[d.hash] = d.name
    keys: Dict[str, str] = {}
    for d in designs:
        if not d.ok:
            continue
        key = rx_key(d)
        if key in keys:
            reject(d, "same prescription as {} (files differ outside the lens data)".format(keys[key]),
                   "same prescription")
        else:
            keys[key] = d.name


def g9(x: float) -> str:
    """9 significant digits in C# "G9" spelling (so both twins build the same key)."""
    if not finite(x):
        return "nan"
    return "{:.9g}".format(x).replace("e", "E")


def rx_key(d: Design) -> str:
    """Lens data written as text at 9 significant digits (equal text = same lens)."""
    parts = [d.signature, g9(d.obj_dist), g9(d.efl), g9(d.fno), g9(d.hfov)]
    for s in d.surfs:
        item = ";".join([s.type, g9(s.curv), g9(s.thick), s.glass.upper(), g9(s.conic)])
        if s.pars is not None:
            item += "".join(";" + g9(p) for p in s.pars)
        parts.append(item)
    return "|".join(parts)


def skip_summary(designs: List[Design]) -> str:
    """'3 finite object, 2 duplicate file' - skip counts by category, first seen first."""
    counts: Dict[str, int] = {}
    for d in designs:
        if not d.ok:
            c = d.cat or "other"
            counts[c] = counts.get(c, 0) + 1
    return ", ".join("{} {}".format(n, c) for c, n in counts.items())


def op(Z, S, name: str, i1: int = 0, i2: int = 0, *dbl: float) -> float:
    """Ask OpticStudio for one merit-function number (EFFL, ISFN, ...) without editing the MF."""
    vals = list(dbl) + [0.0] * (6 - len(dbl))
    t = getattr(Z.Editors.MFE.MeritOperandType, name)
    try:
        return float(S.MFE.GetOperandValue(t, i1, i2, *vals))
    except Exception:
        return float("nan")


def primary_wave(S) -> int:
    """Number of the main (primary) wavelength; EFL is measured there."""
    try:
        wls = S.SystemData.Wavelengths
        for k in range(1, wls.NumberOfWavelengths + 1):
            if wls.GetWavelength(k).IsPrimary:
                return k
    except Exception:
        pass
    return 1


def half_fov(Z, S, efl: float, obj: float) -> float:
    """
    Biggest field angle in degrees. Image-height fields become angles via atan(h / EFL);
    object-height fields (finite object) via atan(h / (object distance + entrance pupil position)).
    """
    fields = S.SystemData.Fields
    ftype = fields.GetFieldType()
    biggest = 0.0
    for k in range(1, fields.NumberOfFields + 1):
        f = fields.GetField(k)
        biggest = max(biggest, math.sqrt(f.X * f.X + f.Y * f.Y))
    if ftype == Z.SystemData.FieldType.Angle:
        return biggest
    if ftype in (Z.SystemData.FieldType.ParaxialImageHeight, Z.SystemData.FieldType.RealImageHeight):
        return quant(math.atan(biggest / efl) * 180.0 / math.pi, 1e10)
    if ftype == Z.SystemData.FieldType.ObjectHeight and finite(obj):
        enpp = op(Z, S, "ENPP")
        L = obj + (enpp if finite(enpp) else 0.0)
        if L > 0:
            return quant(math.atan(biggest / L) * 180.0 / math.pi, 1e10)
    return float("nan")


def glass_index(Z, S, catalog: str, name: str, cache) -> Tuple[float, float]:
    """Look up nd (how much it bends light) and Vd (how much it splits colors)."""
    key = (catalog.upper(), name.upper())
    if key in cache:
        return cache[key]
    nd = vd = float("nan")
    mc = None
    try:
        mc = S.Tools.OpenMaterialsCatalog()
        mc.SelectedCatalog = catalog
        mc.SelectedMaterial = name
        nd, vd = float(mc.Nd), float(mc.Vd)
    except Exception:
        pass
    finally:
        if mc is not None:
            try:
                mc.Close()
            except Exception:
                pass
    cache[key] = (nd, vd)
    return nd, vd


# ---------------------------------------------------------------------------
# Step 2 + 3: group and envelope
# ---------------------------------------------------------------------------

def pick_group(designs: List[Design]) -> Tuple[List[Design], List[str]]:
    """
    Keep the biggest family that shares one layout fingerprint AND one object
    distance (infinite, or finite values within 0.1%); -layout picks the
    fingerprint instead. Everyone else is skipped; other groups are listed.
    """
    others: List[str] = []
    ok = [d for d in designs if d.ok]
    if not ok:
        return [], others
    reps: List[float] = []
    for d in ok:
        if not finite(d.obj_dist):
            d.obj_rep = float("inf")
            continue
        r = next((i for i, v in enumerate(reps) if same_obj(v, d.obj_dist)), -1)
        if r < 0:
            reps.append(d.obj_dist)
            r = len(reps) - 1
        d.obj_rep = reps[r]

    def key(d):
        return (d.signature, d.obj_rep)

    order: List[tuple] = []
    buckets: Dict[tuple, List[Design]] = {}
    for d in ok:
        if key(d) not in buckets:
            order.append(key(d))
            buckets[key(d)] = []
        buckets[key(d)].append(d)
    pool = order
    if LAYOUT:
        pool = [k for k in order if buckets[k][0].signature.lower() == LAYOUT.lower()]
        if not pool:
            raise ToolExit(2, "-layout {} matches no usable group; groups here: {}".format(
                LAYOUT, "; ".join("{} ({})".format(group_label(buckets[k][0]), len(buckets[k])) for k in order)))
    # Biggest group wins; on a tie, the group seen first (sorted file order) wins.
    best = sorted(pool, key=lambda k: (-len(buckets[k]), order.index(k)))[0]
    main = buckets[best]
    m0 = main[0]
    for d in ok:
        if key(d) == best:
            continue
        if d.signature == m0.signature:
            reject(d, "object distance {} differs from the main group's {} (a finite object must match within "
                      "0.1%)".format(obj_text(d.obj_dist), obj_text(m0.obj_rep)), "object distance mismatch")
        else:
            reject(d, "layout {} does not match the main group {}".format(d.signature, m0.signature),
                   "layout mismatch")
    rest = sorted([k for k in order if k != best], key=lambda k: (-len(buckets[k]), order.index(k)))[:5]
    for k in rest:
        b = buckets[k]
        names = ", ".join(d.name for d in b[:4]) + (", ..." if len(b) > 4 else "")
        others.append("other group: {}: {} design(s) ({}); pick it with -layout {}".format(
            group_label(b[0]), len(b), names, b[0].signature))
    return main, others


def same_obj(a: float, b: float) -> bool:
    """Two finite object distances count as the same within 0.1% (tiny absolute floor)."""
    return abs(a - b) <= max(1e-3 * abs(a), 1e-6)


def obj_text(v: float) -> str:
    return g(v) if finite(v) else "infinity"


def group_label(d: Design) -> str:
    return d.signature + ", object at " + obj_text(d.obj_rep)


def envelope(group: List[Design]) -> Dict[str, Tuple[float, float, float]]:
    """Smallest, middle (median), and largest value of each first-order number."""
    env = {}
    for key in ("efl", "fno", "hfov", "track_ratio"):
        vals = [getattr(d, key) for d in group]
        env[key] = (min(vals), statistics.median(vals), max(vals))
    return env


def pick_target(env) -> Dict[str, float]:
    """Median by default. A user value outside min..max is refused (or clamped with -clamp)."""
    target = {}
    for key, user, label in (("efl", TARGET_EFL, "-efl"), ("fno", TARGET_FNO, "-fno"),
                             ("hfov", TARGET_FOV, "-hfov")):
        lo, med, hi = env[key]
        if user is None:
            target[key] = med
            continue
        # Round-off slack (1e-5 relative) so "-fno 1" counts as inside a range that starts at 0.9999995.
        if in_range(user, env[key]):
            target[key] = min(max(user, lo), hi)
        elif CLAMP:
            target[key] = min(max(user, lo), hi)
            say("  {} {} is outside {}..{}; clamped to {}".format(label, g(user), g(lo, 5), g(hi, 5),
                                                                   g(target[key], 5)))
        else:
            raise ToolExit(2, "{} {} is outside the input envelope {}..{} (use -clamp to clamp)".format(
                label, g(user), g(lo, 5), g(hi, 5)))
    if target["fno"] <= 0 or target["efl"] <= 0:
        raise ToolExit(2, "target EFL and F/# must be positive")
    return target


def in_range(u: float, e) -> bool:
    return e[0] - (1e-5 * abs(e[0]) + 1e-6) <= u <= e[2] + (1e-5 * abs(e[2]) + 1e-6)


# ---------------------------------------------------------------------------
# Steering by F/# and field
# ---------------------------------------------------------------------------

def set_scales(env) -> None:
    """
    Distance scales from the group's spread: log2(Fmax/Fmin) for F/#, fmax-fmin
    for field. An axis with no spread uses 1 (F/#) or 10 deg (field); the request
    is in range, so that axis then adds nothing.
    """
    global FNO_SCALE, FIELD_SCALE, FIELD_MAX
    # math.log(x, 2.0) (= ln x / ln 2) rather than log2, so the weights match the C# build bit for bit.
    sf = math.log(env["fno"][2] / env["fno"][0], 2.0) if env["fno"][0] > 0 else float("nan")
    FNO_SCALE = sf if finite(sf) and sf > 1e-6 else 1.0
    sv = env["hfov"][2] - env["hfov"][0]
    FIELD_SCALE = sv if finite(sv) and sv > 1e-6 else 10.0
    FIELD_MAX = env["hfov"][2]


def on_axis_only(hfov: float) -> bool:
    return hfov <= 1e-6 and FIELD_MAX > 1e-6


def raw_dist(fno_a: float, hfov_a: float, fno_b: float, hfov_b: float) -> float:
    """
    Distance between two (F/#, half-field) pairs, each axis in the folder's own spread:
      d = sqrt( (log2(Fa/Fb) / log2(Fmax/Fmin))^2 + ((field_a - field_b) / (fmax - fmin))^2 )
    """
    df = math.log(fno_a / fno_b, 2.0) / FNO_SCALE if fno_a > 0 and fno_b > 0 else 4.0
    dv = (hfov_a - hfov_b) / FIELD_SCALE
    return math.sqrt(df * df + dv * dv)


def norm_dist(fno_d: float, hfov_d: float, fno_t: float, hfov_t: float) -> float:
    """
    Distance from a design (first pair) to a request (second pair). An on-axis-only
    design (half-field 0) in a folder that has field designs gets +1 (in quadrature)
    for a field request: its shape was never asked to handle field.
    """
    d = raw_dist(fno_d, hfov_d, fno_t, hfov_t)
    if on_axis_only(hfov_d) and hfov_t > 1e-6:
        d = math.sqrt(d * d + ON_AXIS_PENALTY * ON_AXIS_PENALTY)
    return d


def set_weights(group: List[Design], members: List[Design], target) -> None:
    """
    How much each blend member counts in the average (always adds up to 1).
      near  (default): w = 1 / (d^2 + 0.1^2)   inverse-square, the closest inputs dominate
      soft  (old):     w = 1 / (0.25 + d)      gentle, everyone keeps a fair share
      equal:           w = 1
    Every kept design gets a distance; only members (Standard surfaces) get a weight.
    """
    for d in group:
        d.dist = quant(norm_dist(d.fno, d.hfov, target["fno"], target["hfov"]), 1e12)
        d.weight = 0.0
        d.blend_weight = 0.0
    for d in members:
        if WEIGHT_MODE == "equal":
            d.weight = 1.0
        elif WEIGHT_MODE == "soft":
            d.weight = 1.0 / (0.25 + d.dist)
        else:
            d.weight = 1.0 / (d.dist * d.dist + NEAR_EPS * NEAR_EPS)
    total = plain_sum(d.weight for d in members)
    for d in members:
        d.weight = d.weight / total if total > 0 else 0.0
        d.blend_weight = d.weight


def say_weights(group: List[Design], pair, mode: str) -> None:
    """Print every kept design by distance, with its blend weight ('--' = not in the blend)."""
    say("Closeness (weight {}; distance = log2 F/# over {} and field over {} deg{}). Start mode: {}".format(
        WEIGHT_MODE, g(FNO_SCALE, 4), g(FIELD_SCALE, 4),
        ", on-axis-only designs +1 in quadrature when a field is asked" if FIELD_MAX > 1e-6 else "", mode))
    for d in sorted(group, key=lambda d: (d.dist, -d.blend_weight)):
        tags = (("   <- nearest" if d is pair["nearest"] else "") + ("   [on-axis only]" if d.on_axis else "")
                + ("   [own file, not in blend]" if d.is_special else "" if d.blend_member else "   [not in blend]"))
        say("  {:>7}  d={:<6}  F/{:<8} {:>8} deg  {}{}".format(
            "{:.1f}%".format(100.0 * d.blend_weight) if d.blend_member else "--",
            g(d.dist, 3), g(d.fno, 4), g(d.hfov, 4), d.name, tags))


def pair_check(group: List[Design], fno: float, hfov: float) -> Dict:
    """
    Where does the requested pair sit among the inputs' pairs?
    Nearest input (same distance as the weights) and whether the pair is inside
    the convex hull of the inputs' points (same scaled axes). A request equal to
    an input is inside by definition. Outside the hull, or farther than FAR_DIST
    from every input = 2D extrapolation, so we warn.
    """
    nearest, best, exact = None, float("inf"), False
    for d in group:
        dist = norm_dist(d.fno, d.hfov, fno, hfov)
        if dist < best:
            nearest, best = d, dist
        if raw_dist(d.fno, d.hfov, fno, hfov) < 1e-9:
            exact = True
    pts = [(math.log(d.fno, 2.0) / FNO_SCALE, d.hfov / FIELD_SCALE) for d in group if d.fno > 0]
    inside = exact or (fno > 0 and inside_hull(pts, math.log(fno, 2.0) / FNO_SCALE, hfov / FIELD_SCALE))
    warn = (not exact) and ((not inside) or best > FAR_DIST)
    text = ("Request F/{} at {} deg: nearest input {} (F/{}, {} deg) at distance {}; "
            "inside the inputs' F/#-field hull: {}{}").format(
        g(fno, 4), g(hfov, 4), nearest.name if nearest else "n/a",
        g(nearest.fno, 4) if nearest else "nan", g(nearest.hfov, 4) if nearest else "nan",
        g(best, 3), "yes" if inside else "no", " (matches an input exactly)" if exact else "")
    return {"nearest": nearest, "dist": best, "inside_hull": inside, "exact": exact, "warn": warn, "text": text}


def inside_hull(pts: List[Tuple[float, float]], x: float, y: float) -> bool:
    """
    Point-in-convex-hull test in 2D (hull by the monotone-chain method).
    Repeated points are dropped first. A hull that collapses to a line or a
    point only "contains" points on it.
    """
    tol = 1e-9

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    P: List[Tuple[float, float]] = []
    for q in sorted(pts):
        if not P or abs(P[-1][0] - q[0]) > tol or abs(P[-1][1] - q[1]) > tol:
            P.append(q)
    if not P:
        return False
    hull: List[Tuple[float, float]] = []
    # Lower edge left to right, then upper edge right to left (counter-clockwise).
    for q in P:
        while len(hull) >= 2 and cross(hull[-2], hull[-1], q) <= tol:
            hull.pop()
        hull.append(q)
    lower = len(hull) + 1
    for q in reversed(P[:-1]):
        while len(hull) >= lower and cross(hull[-2], hull[-1], q) <= tol:
            hull.pop()
        hull.append(q)
    if len(hull) > 1:
        hull.pop()  # last point repeats the first
    r = (x, y)
    if len(hull) == 1:
        return abs(hull[0][0] - x) < 1e-6 and abs(hull[0][1] - y) < 1e-6
    if len(hull) == 2:
        # Degenerate (all inputs on one line): on the segment only.
        a, b = hull
        len2 = (b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2
        if len2 < 1e-18:
            return abs(a[0] - x) < 1e-6 and abs(a[1] - y) < 1e-6
        t = ((x - a[0]) * (b[0] - a[0]) + (y - a[1]) * (b[1] - a[1])) / len2
        return abs(cross(a, b, r)) < 1e-6 and -1e-9 <= t <= 1 + 1e-9
    return all(cross(hull[i], hull[(i + 1) % len(hull)], r) >= -1e-9 for i in range(len(hull)))


def ask_targets(group: List[Design], env) -> None:
    """
    The "what job?" window (interactive use): F/#, half field and EFL, prefilled
    with the folder's medians and showing each min..max. Out-of-range values are
    refused here unless "clamp" is ticked; a 2D-extrapolation pair asks "continue?".
    It also takes the optional Hammer time (same as -hammer SEC).
    Cancel = exit 2. The answers go into the same globals as the command-line flags.
    """
    global TARGET_FNO, TARGET_FOV, TARGET_EFL, CLAMP, START_MODE, HAMMER_SEC
    try:
        import tkinter as tk
        from tkinter import messagebox, simpledialog
    except Exception as ex:
        raise ToolExit(2, "F/# and field window failed (" + str(ex) + "); pass -fno/-hfov and -nodialog")

    names = ("F/# (image space)", "Half field of view (deg)", "Focal length EFL (mm)")
    spans = (env["fno"], env["hfov"], env["efl"])
    given = (TARGET_FNO, TARGET_FOV, TARGET_EFL)
    prefill = ["{:.10g}".format(given[i] if given[i] is not None else spans[i][1]) for i in range(3)]
    hammer_prefill = str(HAMMER_SEC)  # -hammer value, so it shows up here and can be changed

    class TargetDialog(simpledialog.Dialog):
        def body(self, master):
            tk.Label(master, justify="left", wraplength=520, text=(
                "{} designs share one layout. Type the job you want; leave a box as it is "
                "to use the folder's median.").format(len(group))).grid(row=0, column=0, columnspan=3, sticky="w")
            # One row per number: name, box (prefilled), and the folder's range.
            self.boxes = []
            for i in range(3):
                tk.Label(master, text=names[i]).grid(row=i + 1, column=0, sticky="w", pady=2)
                box = tk.Entry(master, width=12)
                box.insert(0, prefill[i])
                box.grid(row=i + 1, column=1, sticky="w")
                self.boxes.append(box)
                tk.Label(master, text="inputs {} .. {} (median {})".format(
                    g(spans[i][0], 5), g(spans[i][2], 5), g(spans[i][1], 5))).grid(row=i + 1, column=2, sticky="w")
            self.clamp = tk.BooleanVar(value=CLAMP)
            tk.Checkbutton(master, variable=self.clamp, text=(
                "Clamp out-of-range values to the nearest edge (otherwise they are refused)")).grid(
                row=4, column=0, columnspan=3, sticky="w")
            self.start = tk.StringVar(value=START_MODE)
            tk.Label(master, text="Start from").grid(row=5, column=0, sticky="w")
            frm = tk.Frame(master)
            frm.grid(row=5, column=1, columnspan=2, sticky="w")
            tk.Radiobutton(frm, text="best of all candidates (default)", variable=self.start,
                           value="best").pack(side="left")
            tk.Radiobutton(frm, text="nearest input design", variable=self.start,
                           value="nearest").pack(side="left")
            tk.Radiobutton(frm, text="blend of the inputs (closest count most)", variable=self.start,
                           value="blend").pack(side="left")
            # Optional Hammer polish, in whole seconds (0 = off).
            tk.Label(master, text="Hammer polish (seconds, 0 = off)").grid(row=6, column=0, sticky="w", pady=2)
            self.hammer = tk.Entry(master, width=12)
            self.hammer.insert(0, hammer_prefill)
            self.hammer.grid(row=6, column=1, sticky="w")
            tk.Label(master, text="polishes the {} best passing lenses; 0 .. {}".format(
                HAMMER_KEEP, HAMMER_MAX)).grid(row=6, column=2, sticky="w")
            # The inputs' own pairs, so the user can see where designs exist.
            lst = tk.Listbox(master, width=80, height=min(12, len(group)), font=("Courier", 9))
            for d in sorted(group, key=lambda d: (d.fno, d.hfov)):
                lst.insert("end", "F/{:<7} {:>7} deg  EFL {:<9} {}".format(
                    g(d.fno, 4), g(d.hfov, 4), g(d.efl, 5), d.name))
            lst.grid(row=7, column=0, columnspan=3, sticky="we", pady=4)
            return self.boxes[0]

        def validate(self):
            # Read each box; an untouched box means "exactly the median".
            picked = []
            for i in range(3):
                txt = self.boxes[i].get().strip()
                if txt == prefill[i]:
                    v = given[i] if given[i] is not None else spans[i][1]
                else:
                    try:
                        v = float(txt.replace(",", "."))
                        if not finite(v):  # "nan" and "inf" parse, but are not numbers we can use
                            raise ValueError(txt)
                    except ValueError:
                        messagebox.showerror(TOOL, "{}: '{}' is not a number.".format(names[i], txt), parent=self)
                        return False
                lo, _, hi = spans[i]
                inside = in_range(v, spans[i])
                if not inside and not self.clamp.get():
                    messagebox.showerror(TOOL, "{} = {} is outside the folder's range {} .. {}.\n"
                                         "Type a value inside it, or tick Clamp.".format(names[i], g(v), g(lo, 5),
                                                                                         g(hi, 5)),
                                         parent=self)
                    return False
                picked.append(min(max(v, lo), hi))
            if picked[0] <= 0 or picked[2] <= 0:
                messagebox.showerror(TOOL, "F/# and EFL must be positive.", parent=self)
                return False
            # Hammer box: an untouched box keeps the -hammer value as given (already
            # checked); anything typed must be a whole number of seconds from 0 to the cap.
            h_txt = self.hammer.get().strip()
            hammer_sec = HAMMER_SEC
            if h_txt != hammer_prefill:
                if not (h_txt.isascii() and h_txt.isdigit()) or int(h_txt) > HAMMER_MAX:
                    messagebox.showerror(TOOL, "Hammer polish: '{}' is not a whole number of seconds from 0 to {} "
                                         "(0 = off).".format(h_txt, HAMMER_MAX), parent=self)
                    return False
                hammer_sec = int(h_txt)
            # 2D check: each value may be in range while the pair is not.
            pc = pair_check(group, picked[0], picked[1])
            if pc["warn"] and not messagebox.askyesno(TOOL, pc["text"] + "\n\nNo input was made for this F/# + "
                                                      "field combination, so the start is an extrapolation. "
                                                      "Continue?", parent=self):
                return False
            self.picked = picked
            self.hammer_sec = hammer_sec
            return True

        def apply(self):
            self.result = (self.picked, self.clamp.get(), self.start.get(), self.hammer_sec)

    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)
    dlg = TargetDialog(root, title="StartPointFinder: F/# and field of view")
    root.destroy()
    if dlg.result is None:
        raise ToolExit(2, "cancelled in the F/# and field window", True)
    (TARGET_FNO, TARGET_FOV, TARGET_EFL), CLAMP, START_MODE, HAMMER_SEC = dlg.result  # same path as -hammer SEC
    say("Window: F/{}  half-FOV {} deg  EFL {}  start {}{}{}".format(
        g(TARGET_FNO), g(TARGET_FOV), g(TARGET_EFL), START_MODE, "  (clamp on)" if CLAMP else "",
        "  hammer {} s".format(HAMMER_SEC) if HAMMER_SEC > 0 else ""))


def thickness_bounds(group: List[Design]) -> Dict[str, float]:
    """
    Fences for the optimizer, measured from the inputs at unit focal length:
    glass no thinner than 80% of the thinnest input glass, no thicker than
    120% of the thickest; air gaps the same idea (with a tiny floor).
    """
    glass, air = [], []
    for d in group:
        for s in d.surfs[:-1]:  # last gap is the focus distance; it gets its own rule
            (glass if s.glass else air).append(s.thick / d.efl)
    bfl = [d.surfs[-1].thick / d.efl for d in group]
    # A layout with no glass at all (e.g. only Paraxial surfaces) has no glass fence:
    # use a wide one rather than failing on an empty list.
    if not glass:
        glass = [0.01, 0.5]
    return {
        "glass_min": 0.8 * min(glass),
        "glass_max": 1.2 * max(glass),
        "air_min": max(0.002, 0.8 * min(air)) if air else 0.002,
        "air_max": 1.2 * max(air + bfl),
    }


# ---------------------------------------------------------------------------
# Step 5: build the start shape
# ---------------------------------------------------------------------------

def start_shape(group: List[Design]) -> Tuple[List[SurfRx], List[str]]:
    """
    Weighted average of the shapes (blend members only, closeness weights).
    Each input is first scaled to focal length 1, so a
    curvature becomes curv*EFL and a thickness becomes thick/EFL. Then we take
    the weighted mean, surface by surface. Glass cannot be averaged, so we
    pick a real glass that the inputs used at that spot.
    """
    nsurf = len(group[0].surfs)
    out: List[SurfRx] = []
    notes: List[str] = []
    for j in range(nsurf):
        c = plain_sum(d.weight * d.surfs[j].curv * d.efl for d in group)
        t = plain_sum(d.weight * d.surfs[j].thick / d.efl for d in group)
        k = plain_sum(d.weight * d.surfs[j].conic for d in group)
        glass, cat = "", ""
        if group[0].surfs[j].glass:
            glass, cat, note = choose_glass(group, j)
            notes.append("S{} glass: {}".format(j + 1, note))
        out.append(SurfRx(c, t, glass, cat, k))
    return out, notes


def choose_glass(group: List[Design], j: int) -> Tuple[str, str, str]:
    """Pick one input glass for surface j (closest to the average nd/Vd, or most used)."""
    surf_no = j + 1
    cands = []
    for d in group:
        s = d.surfs[j]
        nd, vd = d.glass_nd_vd.get(surf_no, (float("nan"), float("nan")))
        cands.append((s.glass, s.catalog, nd, vd, d.weight))
    good = [c for c in cands if finite(c[2]) and finite(c[3])]
    if good:
        wsum = plain_sum(c[4] for c in good)
        nd_bar = plain_sum(c[2] * c[4] for c in good) / wsum
        vd_bar = plain_sum(c[3] * c[4] for c in good) / wsum
    else:
        nd_bar = vd_bar = float("nan")

    def dist(c):
        # 0.02 in nd and 5 in Vd count as "one step" apart.
        if not (finite(c[2]) and finite(c[3]) and finite(nd_bar)):
            return 1e9
        return math.sqrt(((c[2] - nd_bar) / 0.02) ** 2 + ((c[3] - vd_bar) / 5.0) ** 2)

    if GLASS_MODE == "majority" or not good:
        votes: Dict[Tuple[str, str], float] = {}
        for c in cands:
            votes[(c[0], c[1])] = votes.get((c[0], c[1]), 0.0) + c[4]
        top = max(votes.values())
        tied = [c for c in cands if abs(votes[(c[0], c[1])] - top) < 1e-12]
        pick_c = min(tied, key=dist)
    else:
        pick_c = min(cands, key=dist)
    note = "{} ({}) nd={} Vd={}; weighted mean nd={} Vd={}".format(
        pick_c[0], pick_c[1], g(pick_c[2], 5), g(pick_c[3], 4), g(nd_bar, 5), g(vd_bar, 4))
    return pick_c[0], pick_c[1], note


def build_system(Z, S, rx: List[SurfRx], stop: int, target, catalogs: List[str], title: str) -> None:
    """
    Make a brand-new lens from the normalized recipe (focal length 1),
    sized to the target: entrance pupil = EFL / F#, angle fields 0, 0.7, 1 x FOV,
    F d C visible wavelengths, the group's object distance. Then nudge the size so
    EFL is exact and put the image plane at paraxial focus.
    """
    S.New(False)
    sd = S.SystemData
    # Say mm out loud: a new lens takes the user's default units, which could be inches.
    sd.Units.LensUnits = Z.SystemData.ZemaxSystemUnits.Millimeters
    same_settings(Z, S, target)
    efl_t = target["efl"]
    sd.Aperture.ApertureType = Z.SystemData.ZemaxApertureType.EntrancePupilDiameter
    sd.Aperture.ApertureValue = efl_t / target["fno"]
    sd.Fields.SetFieldType(Z.SystemData.FieldType.Angle)
    sd.Fields.GetField(1).Y = 0.0
    if target["hfov"] > 1e-6:
        sd.Fields.AddField(0.0, 0.7 * target["hfov"], 1.0)
        sd.Fields.AddField(0.0, target["hfov"], 1.0)
    sd.Wavelengths.SelectWavelengthPreset(Z.SystemData.WavelengthPreset.FdC_Visible)
    for cat in catalogs:
        try:
            if not sd.MaterialCatalogs.IsCatalogInUse(cat):
                sd.MaterialCatalogs.AddCatalog(cat)
        except Exception:
            pass
    try:
        sd.TitleNotes.Title = title
    except Exception:
        pass
    lde = S.LDE
    while lde.NumberOfSurfaces < len(rx) + 2:
        lde.InsertNewSurfaceAt(lde.NumberOfSurfaces - 1)
    for i, s in enumerate(rx, start=1):
        surf = lde.GetSurfaceAt(i)
        c = s.curv / efl_t  # back from "focal length 1" to real size
        surf.Radius = (1.0 / c) if abs(c) > 1e-12 else float("inf")
        surf.Thickness = s.thick * efl_t
        surf.Conic = s.conic
        surf.Material = s.glass
    lde.GetSurfaceAt(stop).IsStop = True
    if finite(target["obj"]):
        lde.GetSurfaceAt(0).Thickness = target["obj"]

    # Glass and wavelengths change EFL a little; one pure re-scale fixes it exactly.
    for _ in range(2):
        efl = op(Z, S, "EFFL", 0, primary_wave(S))
        if not finite(efl) or efl <= 0:
            break
        k = efl_t / efl
        if abs(k - 1.0) < 1e-9:
            break
        for i in range(1, lde.NumberOfSurfaces - 1):
            surf = lde.GetSurfaceAt(i)
            if finite(surf.Radius) and abs(surf.Radius) < 1e10:
                surf.Radius = surf.Radius * k
            surf.Thickness = surf.Thickness * k
    paraxial_focus(Z, S)


def build_native(Z, S, d: Design, target, title: str) -> None:
    """
    Start from a design's OWN file (for Even Asphere / Zernike / Paraxial
    surfaces, which a plain recipe cannot carry): open it (read only), drop its
    variables, extra configurations and merit rows, scale it with OpticStudio's
    Scale Lens tool (asphere terms scale too) to the target EFL, then give it
    the same pupil, fields, wavelengths and object distance as every other
    candidate, fix the EFL exactly and focus paraxially.
    """
    if not S.LoadFile(d.path, False):
        raise RuntimeError("could not reload " + d.name)
    try:
        if S.MCE.NumberOfConfigurations > 1:
            S.MCE.MakeSingleConfiguration()
    except Exception:
        pass
    if unit_name(Z, S.SystemData.Units.LensUnits) != "mm":
        to_millimeters(Z, S)  # d.efl is in mm
    try:
        S.Tools.RemoveAllVariables()
    except Exception:
        pass
    try:
        S.MFE.DeleteAllRows()
    except Exception:
        pass
    scale_by(S, target["efl"] / d.efl)
    retarget(Z, S, target)
    try:
        S.SystemData.TitleNotes.Title = title
    except Exception:
        pass
    # New wavelengths shift EFL a little; a pure re-scale fixes it exactly.
    for _ in range(2):
        efl = op(Z, S, "EFFL", 0, primary_wave(S))
        if not finite(efl) or efl <= 0:
            break
        k = target["efl"] / efl
        if abs(k - 1.0) < 1e-9:
            break
        scale_by(S, k)
        retarget(Z, S, target)
    paraxial_focus(Z, S)


def scale_by(S, k: float) -> None:
    """Scale the whole lens by k (OpticStudio's Scale Lens tool)."""
    sc = S.Tools.OpenScale()
    if sc is None:
        raise RuntimeError("could not open the Scale Lens tool")
    try:
        sc.ScaleByFactor = True
        sc.ScaleFactor = k
        sc.RunAndWaitForCompletion()
    finally:
        sc.Close()


def retarget(Z, S, target) -> None:
    """
    Same job as every candidate: EPD = EFL / F#, angle fields 0 / 0.7 / 1 x FOV
    (no vignetting factors), F d C wavelengths, held object distance.
    """
    sd = S.SystemData
    sd.Aperture.ApertureType = Z.SystemData.ZemaxApertureType.EntrancePupilDiameter
    sd.Aperture.ApertureValue = target["efl"] / target["fno"]
    f = sd.Fields
    f.SetFieldType(Z.SystemData.FieldType.Angle)
    while f.NumberOfFields > 1:
        f.RemoveField(f.NumberOfFields)
    f1 = f.GetField(1)
    f1.X, f1.Y, f1.Weight = 0.0, 0.0, 1.0
    if target["hfov"] > 1e-6:
        f.AddField(0.0, 0.7 * target["hfov"], 1.0)
        f.AddField(0.0, target["hfov"], 1.0)
    try:
        f.ClearVignetting()
    except Exception:
        pass
    sd.Wavelengths.SelectWavelengthPreset(Z.SystemData.WavelengthPreset.FdC_Visible)
    if finite(target["obj"]):
        S.LDE.GetSurfaceAt(0).Thickness = target["obj"]
    same_settings(Z, S, target)


def same_settings(Z, S, target) -> None:
    """
    The same system settings for every candidate, whether it was built from numbers
    or rebuilt from its own file, so their scores compare fairly: ray aiming off,
    an evenly lit pupil (no apodization), and 20 C / 1 atm with the glass
    index not adjusted for temperature (OpticStudio's defaults for a new lens).
    """
    sd = S.SystemData
    try:
        sd.RayAiming.RayAiming = Z.SystemData.RayAimingMethod.Off
    except Exception:
        pass
    try:
        sd.Aperture.ApodizationType = Z.SystemData.ZemaxApodizationType.Uniform
    except Exception:
        pass
    try:
        sd.Environment.AdjustIndexToEnvironment = False
        sd.Environment.Temperature = 20.0
        sd.Environment.Pressure = 1.0
    except Exception:
        pass


def paraxial_focus(Z, S) -> None:
    """Put the image where the paraxial edge ray crosses the axis, then freeze that number."""
    lde = S.LDE
    cell = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2).ThicknessCell
    try:
        solve = cell.CreateSolveType(Z.Editors.SolveType.MarginalRayHeight)
        cell.SetSolveData(solve)  # height 0 at pupil zone 0 = paraxial focus
        _ = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2).Thickness
        cell.MakeSolveFixed()  # keep the number, drop the solve
    except Exception as ex:
        say("  WARNING: paraxial focus solve failed: " + str(ex))


def quick_focus(Z, S) -> None:
    """Slide the image plane to the smallest RMS spot (OpticStudio Quick Focus)."""
    qf = None
    try:
        qf = S.Tools.OpenQuickFocus()
        if qf is None:
            return
        try:
            qf.Criterion = Z.Tools.General.QuickFocusCriterion.SpotSizeRadial
            qf.UseCentroid = True
        except Exception:
            pass
        qf.RunAndWaitForCompletion()
    except Exception:
        pass
    finally:
        if qf is not None:
            try:
                qf.Close()
            except Exception:
                pass


# ---------------------------------------------------------------------------
# Merit function ("report card") + optimization
# ---------------------------------------------------------------------------

def build_merit(Z, S, target, env, bounds) -> None:
    """
    The report card ("merit function": one number, smaller = better lens):
    OpticStudio's default RMS spot size (how wide the blur dot is, measured
    around its own center = "centroid", with rays placed on a smart pattern of
    rings and arms = "Gaussian quadrature"), with glass/air thickness fences
    taken from the inputs, plus
      EFFL  = focal length must equal the target (keeps the size right),
      TOTR  = total track (lens length); OPLT / OPGT = "keep it less than / greater
              than", so the track stays between the shortest and longest input (scaled),
      ISFN  = image F/#, shown with weight 0 (only displayed: the F/# is already
              held by the fixed pupil size plus the fixed EFL).
    """
    efl_t = target["efl"]
    mfe = S.MFE
    wiz = mfe.SEQOptimizationWizard2
    wiz.ResetSettings()
    wiz.Criterion = Z.Wizards.CriterionTypes.Spot
    wiz.Type = Z.Wizards.OptimizationTypes.RMS
    wiz.Reference = Z.Wizards.ReferenceTypes.Centroid
    wiz.UseGaussianQuadrature = True
    wiz.UseAllFields = True
    wiz.AssumeAxialSymmetry = True
    wiz.AddFavoriteOperands = False
    wiz.UseGlassBoundaryValues = True
    wiz.GlassMin = bounds["glass_min"] * efl_t
    wiz.GlassMax = bounds["glass_max"] * efl_t
    wiz.GlassEdgeThickness = 0.5 * bounds["glass_min"] * efl_t
    wiz.UseAirBoundaryValues = True
    wiz.AirMin = bounds["air_min"] * efl_t
    wiz.AirMax = bounds["air_max"] * efl_t
    wiz.AirEdgeThickness = 0.0
    wiz.OptimizeForBestNominalPerformance = True
    wiz.OptimizeForManufacturingYield = False
    wiz.Apply()

    MO = Z.Editors.MFE.MeritOperandType
    add_row(Z, mfe, MO.EFFL, target=efl_t, weight=1.0, ints=(0, primary_wave(S)))
    add_row(Z, mfe, MO.ISFN, target=target["fno"], weight=0.0)
    track_row = add_row(Z, mfe, MO.TOTR, target=0.0, weight=0.0)
    add_row(Z, mfe, MO.OPLT, target=env["track_ratio"][2] * efl_t, weight=1.0, ints=(track_row, 0))
    add_row(Z, mfe, MO.OPGT, target=env["track_ratio"][0] * efl_t, weight=1.0, ints=(track_row, 0))


def add_row(Z, mfe, optype, target: float, weight: float, ints=(0, 0)) -> int:
    """Add one line to the report card and return its row number."""
    mfe.AddOperand()
    row = mfe.NumberOfOperands
    o = mfe.GetOperandAt(row)
    o.ChangeType(optype)
    for col, val in zip((Z.Editors.MFE.MeritColumn.Param1, Z.Editors.MFE.MeritColumn.Param2), ints):
        if val:
            cell = o.GetOperandCell(col)
            try:
                cell.IntegerValue = int(val)
            except Exception:
                cell.DoubleValue = float(val)
    o.Target = target
    o.Weight = weight
    return row


def merit(S) -> float:
    try:
        return float(S.MFE.CalculateMeritFunction())
    except Exception:
        return float("nan")


def make_variables(Z, S, stop: int) -> None:
    """
    Let the optimizer move every radius (not the flat stop) and every thickness.
    Asphere terms of own-file starts stay fixed.
    """
    lde = S.LDE
    for i in range(1, lde.NumberOfSurfaces - 1):
        s = lde.GetSurfaceAt(i)
        if i != stop:
            try:
                s.RadiusCell.MakeSolveVariable()
            except Exception:
                pass
        try:
            s.ThicknessCell.MakeSolveVariable()
        except Exception:
            pass


def optimize(Z, S, passes: int, hammer_sec: int) -> float:
    """
    Damped least squares (DLS) = "walk downhill" on the report card.
    We run it up to `passes` times (0 = Hammer only) and stop early when it barely
    improves. Optional Hammer = a longer "shake and walk downhill" search, time-limited.
    """
    if passes > 0:
        opt = S.Tools.OpenLocalOptimization()
        if opt is None:
            raise RuntimeError("could not open local optimization")
        try:
            opt.Algorithm = Z.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
            opt.Cycles = Z.Tools.Optimization.OptimizationCycles.Automatic
            last = float(opt.InitialMeritFunction)
            for k in range(passes):
                opt.RunAndWaitForCompletion()
                cur = float(opt.CurrentMeritFunction)
                if not finite(cur) or last - cur < 1e-3 * max(abs(last), 1e-12):
                    break
                last = cur
        finally:
            opt.Close()
    if hammer_sec > 0:
        ham = S.Tools.OpenHammerOptimization()
        if ham is not None:
            try:
                ham.Algorithm = Z.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
                try:
                    ham.NumberOfCores = ham.MaxCores
                except Exception:
                    pass
                before = float(ham.InitialMeritFunction)
                ham.RunAndWaitWithTimeout(hammer_sec)
                try:
                    ham.Cancel()
                    ham.WaitForCompletion()
                except Exception:
                    pass
                say("  Hammer {} s: {} -> {}".format(hammer_sec, g(before), g(float(ham.CurrentMeritFunction))))
            finally:
                ham.Close()
    return merit(S)


# ---------------------------------------------------------------------------
# Measure, compare, validate
# ---------------------------------------------------------------------------

def mean_spot_um(Z, S) -> float:
    """Plain-language score: average RMS spot radius over the fields, in microns (polychromatic)."""
    fields = S.SystemData.Fields
    fmax = max([abs(fields.GetField(k).Y) for k in range(1, fields.NumberOfFields + 1)] + [1e-12])
    vals = []
    for k in range(1, fields.NumberOfFields + 1):
        hy = fields.GetField(k).Y / fmax if fmax > 1e-9 else 0.0
        v = op(Z, S, "RSCE", 4, 0, 0.0, hy)  # 4 rings, wave 0 = all wavelengths
        if finite(v):
            vals.append(v * 1000.0)
    return plain_sum(vals) / len(vals) if vals else float("nan")


def measure(Z, S, obj: float) -> Dict[str, float]:
    """First-order numbers of whatever lens is loaded now."""
    efl = op(Z, S, "EFFL", 0, primary_wave(S))
    return {
        "spot_um": mean_spot_um(Z, S),
        "efl": efl,
        "fno": op(Z, S, "ISFN"),
        "hfov": half_fov(Z, S, efl, obj) if finite(efl) and efl > 0 else float("nan"),
        "track": op(Z, S, "TOTR"),
        "track_ratio": op(Z, S, "TOTR") / efl if finite(efl) and efl else float("nan"),
    }


def best_input(group: List[Design]) -> Tuple[str, float]:
    """
    The input with the lowest score at the target (reopt score if we have it),
    counting only inputs whose optimized lens passed the checks when any did.
    """
    best = ("", float("nan"))
    # Inputs whose optimized lens passed the checks count first; if none did, all count.
    any_pass = any(d.reopt_pass and finite(d.mf_reopt) for d in group)
    # With the top-N screen some inputs are never optimized; their refocus-only score
    # is not comparable with optimized scores, so they only count if nothing was optimized.
    any_opt = any(finite(d.mf_reopt) for d in group)
    for d in group:
        if any_pass and not d.reopt_pass:
            continue
        if any_opt and not finite(d.mf_reopt):
            continue
        v = d.mf_reopt if finite(d.mf_reopt) else d.mf_refocus
        if finite(v) and (not finite(best[1]) or v < best[1]):
            best = (d.name, v)
    return best


def sag(c: float, k: float, y: float) -> float:
    """How far a curved surface bulges at height y (standard conic sag formula)."""
    arg = 1.0 - (1.0 + k) * c * c * y * y
    if arg < 0:
        return float("nan")
    return c * y * y / (1.0 + math.sqrt(arg))


def sag_at(c: float, k: float, pars: Optional[List[float]], y: float) -> float:
    """
    Sag with even-asphere terms added (pars = Par1..Par8: a1 r^2 + a2 r^4 + ...).
    Zernike terms are left out (good enough for edge checks and the picture).
    """
    z = sag(c, k, y)
    if pars is not None and finite(z):
        for j in range(8):
            if finite(pars[j]):
                z += pars[j] * y ** (2 * (j + 1))
    return z


def validate(Z, S, fo, target, env, bounds, mf) -> Dict:
    """
    Check the result honestly:
      EFL and F/# within 1% of the target, field equal to the target,
      track/EFL inside the inputs' range (2% slack),
      every glass at least 95% of the glass fence in the middle,
      edges of glass not paper-thin, no air gaps that collide,
      and a sane report-card score.
    """
    items = []
    efl_t, fno_t = target["efl"], target["fno"]
    items.append(("EFL {} vs target {} (1%)".format(g(fo["efl"]), g(efl_t)),
                  finite(fo["efl"]) and abs(fo["efl"] / efl_t - 1) <= 0.01))
    items.append(("F/# {} vs target {} (1%)".format(g(fo["fno"]), g(fno_t)),
                  finite(fo["fno"]) and abs(fo["fno"] / fno_t - 1) <= 0.01))
    items.append(("half-FOV {} vs target {}".format(g(fo["hfov"]), g(target["hfov"])),
                  finite(fo["hfov"]) and abs(fo["hfov"] - target["hfov"]) <= 1e-6 + 1e-3 * target["hfov"]))
    lo, _, hi = env["track_ratio"]
    items.append(("track/EFL {} inside {}..{} (2% slack)".format(g(fo["track_ratio"]), g(lo), g(hi)),
                  finite(fo["track_ratio"]) and lo * 0.98 <= fo["track_ratio"] <= hi * 1.02))
    lde = S.LDE
    n = lde.NumberOfSurfaces
    min_glass = min_glass_edge = min_air_edge = float("inf")
    n_glass = n_air = 0
    bad_sag: List[str] = []
    bad_sd: List[str] = []
    terms: Dict[int, Optional[List[float]]] = {}

    def pars_of(i):
        if i not in terms:
            terms[i] = asphere_terms(Z, lde.GetSurfaceAt(i))
        return terms[i]

    # A strong conic has no sag past y = 1 / (|c| sqrt(1 + k)); say where that cuts a clear aperture.
    for i in range(1, n - 1):
        s = lde.GetSurfaceAt(i)
        cv = 0.0 if not finite(s.Radius) or abs(s.Radius) > 1e10 or s.Radius == 0 else 1.0 / s.Radius
        # No semi-diameter at all means OpticStudio could not trace the aperture there.
        if not finite(s.SemiDiameter):
            bad_sd.append("S{}".format(i))
            continue
        if not finite(sag(cv, s.Conic, s.SemiDiameter)):
            bad_sag.append("S{} (beyond y={} of semi-diameter {})".format(
                i, g(1.0 / (abs(cv) * math.sqrt(1.0 + s.Conic)), 4), g(s.SemiDiameter, 4)))
    for i in range(1, n - 2):
        a, b = lde.GetSurfaceAt(i), lde.GetSurfaceAt(i + 1)
        ca = 0.0 if not finite(a.Radius) or abs(a.Radius) > 1e10 or a.Radius == 0 else 1.0 / a.Radius
        cb = 0.0 if not finite(b.Radius) or abs(b.Radius) > 1e10 or b.Radius == 0 else 1.0 / b.Radius
        is_glass = bool((a.Material or "").strip())
        # Glass edge is at the bigger of the two faces; air gap edge where both faces exist.
        y = max(a.SemiDiameter, b.SemiDiameter) if is_glass else min(a.SemiDiameter, b.SemiDiameter)
        # The smaller face runs flat past its own clear aperture (as OpticStudio draws it).
        edge = (a.Thickness + sag_at(cb, b.Conic, pars_of(i + 1), min(y, b.SemiDiameter))
                - sag_at(ca, a.Conic, pars_of(i), min(y, a.SemiDiameter)))
        if is_glass:
            n_glass += 1
            min_glass = min(min_glass, a.Thickness)
        else:
            n_air += 1
        if not finite(edge):
            continue  # undefined sag: reported by its own check below
        if is_glass:
            min_glass_edge = min(min_glass_edge, edge)
        else:
            min_air_edge = min(min_air_edge, edge)
    gmin = bounds["glass_min"] * efl_t
    # No glass or no inner air gap (e.g. a cemented doublet) = nothing to check, said plainly.
    if n_glass == 0:
        items.append(("glass thickness: n/a (no glass elements)", True))
    else:
        items.append(("thinnest glass center {} >= 95% of fence {}".format(g(min_glass), g(gmin)),
                      min_glass >= 0.95 * gmin))
        if finite(min_glass_edge):
            items.append(("thinnest glass edge {} > 0".format(g(min_glass_edge)), min_glass_edge > 0))
        else:
            items.append(("thinnest glass edge: not computable (see the sag check)", True))
    if n_air == 0:
        items.append(("air gaps do not collide: n/a (no internal air gaps)", True))
    elif not finite(min_air_edge):
        items.append(("air gaps do not collide: not computable (see the sag check)", True))
    else:
        # Edges that touch within 1e-4 x EFL count as touching, not colliding
        # (the optimizer holds the edge-gap boundary only approximately).
        touch = 1e-4 * efl_t
        items.append(("air gaps do not collide (min edge gap {}, tolerance {})".format(g(min_air_edge), g(touch, 3)),
                      min_air_edge >= -touch))
    if bad_sd:
        items.append(("clear aperture not computable at " + ", ".join(bad_sd)
                      + " (OpticStudio returned no semi-diameter; rays likely fail there)", False))
    if not bad_sag:
        items.append(("every surface sag is defined across its clear aperture", True))
    else:
        items.append(("surface sag undefined at the clear aperture of " + ", ".join(bad_sag)
                      + " (conic too strong for the semi-diameter)", False))
    items.append(("merit function finite and < 1e8 ({})".format(g(mf)), finite(mf) and mf < 1e8))
    # Pupil-edge rays: the spot rows of the report card can all trace while a ray at
    # the very edge of the pupil still misses a surface (seen on hard cases).
    edge = edge_ray_check(Z, S, target["hfov"] > 1e-6)
    items.append((edge["text"], edge["ok"] is not False))
    return {"pass": all(ok for _, ok in items), "items": items, "edge_ok": edge["ok"],
            "min_glass_ct": min_glass, "min_glass_edge": min_glass_edge, "min_air_edge": min_air_edge}


# The 7 pupil-edge rays (normalized field Hy, pupil Px, Py), same set as the re-optimization
# study: top of the pupil on axis, top/bottom at 0.7 and full field, and the side (sagittal)
# edge at 0.7 and full field. An on-axis-only request traces just the first one.
EDGE_RAYS = [(0.0, 0.0, 1.0), (0.7, 0.0, 1.0), (0.7, 0.0, -1.0), (1.0, 0.0, 1.0), (1.0, 0.0, -1.0),
             (1.0, 1.0, 0.0), (0.7, 1.0, 0.0)]


def edge_ray_check(Z, S, has_field: bool) -> Dict:
    """
    Trace each pupil-edge ray to the image with a temporary REAY row (weight 0, so
    the score itself does not change). When a ray misses a surface, OpticStudio
    cannot compute the report card at all, which is how a failed ray shows up here.
    The rows are removed again, so the saved lens keeps its own report card.
    Returns ok = True / False, or None when the report card already fails without
    the extra rays (then the merit-function check above has already failed).
    """
    rays = EDGE_RAYS if has_field else EDGE_RAYS[:1]
    base = merit(S)
    if not (finite(base) and base < 1e8):
        return {"ok": None, "text": "edge rays: not checked (the report card itself cannot be computed)"}
    mfe = S.MFE
    C = Z.Editors.MFE.MeritColumn
    img = S.LDE.NumberOfSurfaces - 1
    wave = primary_wave(S)
    failed = []
    for hy, px, py in rays:
        mfe.AddOperand()
        row = mfe.NumberOfOperands
        try:
            o = mfe.GetOperandAt(row)
            o.ChangeType(Z.Editors.MFE.MeritOperandType.REAY)
            o.GetOperandCell(C.Param1).IntegerValue = img
            o.GetOperandCell(C.Param2).IntegerValue = wave
            o.GetOperandCell(C.Param4).DoubleValue = hy
            o.GetOperandCell(C.Param5).DoubleValue = px
            o.GetOperandCell(C.Param6).DoubleValue = py
            o.Weight = 0.0
            m = merit(S)
        finally:
            mfe.RemoveOperandAt(row)  # the temporary row never stays, even after an error
        if not (finite(m) and m < 1e8):
            failed.append("Hy {} P({}, {})".format(g(hy), g(px), g(py)))
    merit(S)  # recompute with the original rows only
    if failed:
        return {"ok": False, "text": "edge rays trace at every field ({} of {} fail: {})".format(
            len(failed), len(rays), ", ".join(failed))}
    return {"ok": True, "text": "edge rays trace at every field ({} of {} pupil-edge rays)".format(len(rays), len(rays))}


# ---------------------------------------------------------------------------
# Output files
# ---------------------------------------------------------------------------

def write_tables(outs, designs, env, target, mf_start, mf_result, result_fo, checks, pair=None,
                 blend_mf=float("nan"), cands=None, chosen=None, secs=float("nan")) -> None:
    """Write the first-order CSV (inputs vs result) and the JSON summary."""
    os.makedirs(os.path.dirname(outs["csv"]), exist_ok=True)
    cols = ["role", "name", "status", "reason", "efl", "fno", "hfov_deg", "track",
            "track_over_efl", "weight", "dist_norm", "mf_scaled_refocus", "mf_scaled_reopt", "spot_um",
            "obj_dist", "surfaces", "blend_member", "on_axis"]
    blank4 = ["", "", "", ""]
    rows = []
    for d in designs:
        rows.append(["input", d.name, "kept" if d.ok else "skipped", d.note if d.ok else d.reason,
                     g(d.efl, 8), g(d.fno, 6), g(d.hfov, 6), g(d.track, 8), g(d.track_ratio, 6),
                     g(d.blend_weight, 4) if d.ok else "", g(d.dist, 4) if d.ok else "",
                     g(d.mf_refocus, 8) if d.ok else "",
                     g(d.mf_reopt, 8) if d.ok else "", g(d.spot_reopt_um, 5) if d.ok else "",
                     obj_csv(d.obj_dist), d.special_note,
                     ("yes" if d.blend_member else "no") if d.ok else "",
                     ("yes" if d.on_axis else "no") if d.ok else ""])
    if env:
        for idx, role in enumerate(("envelope_min", "envelope_median", "envelope_max")):
            rows.append([role, "", "", "", g(env["efl"][idx], 8), g(env["fno"][idx], 6),
                         g(env["hfov"][idx], 6), "", g(env["track_ratio"][idx], 6), "", "", "", "", ""] + blank4)
    if target:
        rows.append(["target", "", "", "", g(target["efl"], 8), g(target["fno"], 6),
                     g(target["hfov"], 6), "", "", "", "", "", "", "", obj_csv(target.get("obj", float("inf"))),
                     "", "", ""])
    for c in (cands or []):
        if c.kind == "blend":
            rows.append(["candidate_blend", c.label, "optimized" if c.optimized else "failed", c.note,
                         "", "", "", "", "", "", "", g(c.mf_start, 8), g(c.mf, 8), g(c.spot, 5)] + blank4)
    if result_fo:
        rows.append(["start", OUT_NAMES["start"], START_MODE, chosen.label if chosen else "", "", "", "", "", "",
                     "", "", g(mf_start, 8), "", ""] + blank4)
        rows.append(["result", OUT_NAMES["result"], "PASS" if checks["pass"] else "FAIL",
                     "start: " + chosen.label if chosen else "",
                     g(result_fo["efl"], 8), g(result_fo["fno"], 6), g(result_fo["hfov"], 6),
                     g(result_fo["track"], 8), g(result_fo["track_ratio"], 6), "", "", "",
                     g(mf_result, 8), g(result_fo["spot_um"], 5)] + blank4)
    with open(outs["csv"], "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(cols)
        w.writerows(rows)
    ranked = ranked_cands(cands)
    summary = {
        "tool": TOOL,
        "inputs": [{"name": d.name, "kept": d.ok, "reason": d.reason, "skip_category": d.cat,
                    "signature": d.signature, "efl": d.efl, "fno": d.fno, "hfov_deg": d.hfov, "track": d.track,
                    "object_distance": d.obj_dist, "lens_units": d.units, "surfaces": d.special_note,
                    "blend_member": d.ok and d.blend_member, "on_axis": d.ok and d.on_axis,
                    "weight": d.blend_weight, "dist_norm": d.dist, "mf_scaled_refocus": d.mf_refocus,
                    "mf_scaled_reopt": d.mf_reopt, "spot_scaled_reopt_um": d.spot_reopt_um,
                    "note": d.note} for d in designs],
        "envelope": env,
        "distance_scales": None if not env else {
            "log2_fno_range": FNO_SCALE, "field_range_deg": FIELD_SCALE,
            "on_axis_penalty": ON_AXIS_PENALTY if FIELD_MAX > 1e-6 else 0.0},
        "target": None if not target else {
            "efl": target["efl"], "fno": target["fno"], "hfov": target["hfov"],
            "object_distance": target.get("obj", float("inf"))},
        "request": None if not pair else {
            "nearest": pair["nearest"].name if pair["nearest"] else "", "nearest_dist": pair["dist"],
            "inside_hull": pair["inside_hull"], "exact_match": pair["exact"], "warn_2d": pair["warn"],
            "start": START_MODE,
            "nearest_mf_scaled_reopt": pair["nearest"].mf_reopt if pair["nearest"] else float("nan"),
            "blend_mf": blend_mf},
        "candidates": None if cands is None else [
            {"label": c.label, "kind": c.kind, "screen_rank": c.screen_rank, "mf_start": c.mf_start,
             "mf": c.mf, "spot_um": c.spot, "passes_check": c.passed, "fail": c.fail,
             "edge_rays_ok": c.edge_ok, "mf_dls": c.mf_dls, "spot_dls_um": c.spot_dls,
             "hammered": c.hammered, "hammer_note": c.ham_note, "chosen": c is chosen} for c in ranked],
        # The screen: every start's score before optimizing, lowest first, and whether it was optimized.
        "screen": None if cands is None else [
            {"rank": c.screen_rank, "label": c.label, "kind": c.kind, "mf_start": c.mf_start,
             "optimized": c.optimized, "note": c.note}
            for c in sorted(cands, key=lambda c: c.screen_rank or 10 ** 6)],
        "chosen": chosen.label if chosen else None,
        "runtime_s": round(secs, 1) if finite(secs) else None,
        "mf_start": mf_start,
        "mf_result": mf_result, "result_first_order": result_fo,
        "envelope_check": None if not checks else {
            "pass": checks["pass"], "items": [[t, ok] for t, ok in checks["items"]]},
        "settings": {"start": START_MODE, "layout": LAYOUT, "weight": WEIGHT_MODE, "glass": GLASS_MODE,
                     "passes": PASSES, "hammer_sec": HAMMER_SEC, "hammer_keep": HAMMER_KEEP,
                     "top": "all" if TOP_N <= 0 else TOP_N, "compare": COMPARE, "min_group": MIN_GROUP},
    }

    def clean(o):
        # JSON has no NaN or infinity; write null instead.
        if isinstance(o, float) and not math.isfinite(o):
            return None
        if isinstance(o, dict):
            return {k: clean(v) for k, v in o.items()}
        if isinstance(o, (list, tuple)):
            return [clean(v) for v in o]
        return o

    with open(outs["json"], "w", encoding="utf-8") as fh:
        json.dump(clean(summary), fh, indent=1)
    say("CSV: " + outs["csv"])


def obj_csv(v: float) -> str:
    return g(v, 8) if finite(v) else "inf"


def draw_layout_png(Z, S, path: str, target, mf: float, start_label: str = "") -> bool:
    """
    Draw a side view (Y-Z) of the result: each surface as a curve, glass edges,
    and a fan of real rays for each field. REAY / REAZ = where a real ray hits a
    surface (height / along the axis, local to that surface); GLCZ = where that
    surface sits along the axis, so the pieces line up in one picture.
    Needs matplotlib; if it is missing we skip the picture and say so.
    """
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except Exception:
        say("  PNG skipped: matplotlib is not installed")
        return False
    lde = S.LDE
    n = lde.NumberOfSurfaces
    zs = [op(Z, S, "GLCZ", i) for i in range(n)]
    zs[0] = 0.0 if not finite(zs[0]) else zs[0]
    fig, ax = plt.subplots(figsize=(14, 6), dpi=100)
    edges = {}
    for i in range(1, n):
        s = lde.GetSurfaceAt(i)
        c = 0.0 if not finite(s.Radius) or abs(s.Radius) > 1e10 or s.Radius == 0 else 1.0 / s.Radius
        h = s.SemiDiameter
        pars = asphere_terms(Z, s)
        ys = [-h + 2 * h * k / 60 for k in range(61)]
        pts = [(zs[i] + sag_at(c, s.Conic, pars, y), y) for y in ys]
        pts = [p for p in pts if finite(p[0])]
        if pts:
            ax.plot([p[0] for p in pts], [p[1] for p in pts], color="black", lw=1.2)
            edges[i] = (pts[0], pts[-1])
    for i in range(1, n - 1):
        if (lde.GetSurfaceAt(i).Material or "").strip() and i in edges and i + 1 in edges:
            for e in (0, 1):
                ax.plot([edges[i][e][0], edges[i + 1][e][0]],
                        [edges[i][e][1], edges[i + 1][e][1]], color="black", lw=1.2)
    colors = ["tab:blue", "tab:green", "tab:red"]
    wave = primary_wave(S)
    fields = S.SystemData.Fields
    fmax = max([abs(fields.GetField(k).Y) for k in range(1, fields.NumberOfFields + 1)] + [1e-12])
    for fi in range(1, fields.NumberOfFields + 1):
        hy = fields.GetField(fi).Y / fmax if fmax > 1e-9 else 0.0
        for py in (-1.0, -0.66, -0.33, 0.0, 0.33, 0.66, 1.0):
            zz, yy = [], []
            for i in range(1, n):
                y = op(Z, S, "REAY", i, wave, 0.0, hy, 0.0, py)
                z = op(Z, S, "REAZ", i, wave, 0.0, hy, 0.0, py)
                if finite(y) and finite(z):
                    zz.append(zs[i] + z)
                    yy.append(y)
            if len(zz) > 1:
                ax.plot(zz, yy, color=colors[(fi - 1) % 3], lw=0.6)
    ax.set_aspect("equal")
    ax.set_xlabel("z (mm)")
    ax.set_ylabel("y (mm)")
    ax.set_title("StartPointFinder result  EFL {}  F/{}  half-FOV {} deg  MF {}  start: {}".format(
        g(target["efl"], 4), g(target["fno"], 3), g(target["hfov"], 3), g(mf, 4), start_label))
    fig.tight_layout()
    fig.savefig(path)
    plt.close(fig)
    say("PNG: " + path)
    return True


if __name__ == "__main__":
    sys.exit(main())

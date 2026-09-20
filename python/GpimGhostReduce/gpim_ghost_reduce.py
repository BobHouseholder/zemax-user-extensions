#!/usr/bin/env python3
# ============================================================
# GpimGhostReduce (Python / ZOS-API)
# ============================================================
# Adds GPIM ghost operands for the worst ghost pairs, optionally
# runs a local DLS. Twin of the C# User Extension.
# Dialogs are -nodialog only (no WinForms port).
#
# Flags: -file -save -top N -weight W -balance B -mode image|pupil|both
#        -optimize -cycles K -nodialog -quiet
# ============================================================

from __future__ import annotations

import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import List, Optional, Set

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import bootstrap_zosapi, connect_zos, discover_zos_root, parse_flag_token

TOP_N = 5
WEIGHT = 1.0
BALANCE = 1.0
MODE = "image"  # image|pupil|both
OPTIMIZE = False
CYCLES = 0
SAVE_PATH: Optional[str] = None
FILE_PATH: Optional[str] = None
NO_DIALOG = False
QUIET = False
EXPLICIT: Set[str] = set()
REPORT: List[str] = []


@dataclass
class GhostHit:
    s1: int
    s2: int
    mode: int  # 1=image, 0=pupil
    wfb: int = 0
    wsb: int = 0
    score: float = 0.0


def say(s: str) -> None:
    print(s, flush=True)
    REPORT.append(s)


def fmt(t, *a):
    return t.format(*a)


def parse_args(argv):
    global TOP_N, WEIGHT, BALANCE, MODE, OPTIMIZE, CYCLES, SAVE_PATH, FILE_PATH, NO_DIALOG, QUIET
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
            if a == "top":
                TOP_N = int(next_tok()); EXPLICIT.add("top")
            elif a == "weight":
                WEIGHT = float(next_tok()); EXPLICIT.add("weight")
            elif a == "balance":
                BALANCE = float(next_tok()); EXPLICIT.add("balance")
            elif a == "mode":
                MODE = (next_tok() or "image").lower(); EXPLICIT.add("mode")
            elif a == "optimize":
                OPTIMIZE = True; EXPLICIT.add("optimize")
            elif a == "cycles":
                CYCLES = int(next_tok()); EXPLICIT.add("cycles")
            elif a == "save":
                SAVE_PATH = next_tok()
            elif a == "file":
                FILE_PATH = next_tok()
            elif a == "nodialog":
                NO_DIALOG = True
            elif a == "quiet":
                QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1
    if MODE not in ("image", "pupil", "both", "0", "1"):
        raise RuntimeError("unknown -mode (use image|pupil|both)")
    if TOP_N < 1:
        TOP_N = 1
    if WEIGHT <= 0:
        WEIGHT = 1.0


def modes_list():
    m = MODE
    if m in ("image", "1"):
        return [1]
    if m in ("pupil", "0"):
        return [0]
    return [1, 0]


def learn_gpim_map(op, ZOSAPI):
    """Discover GPIM column indices by probing (same approach as C#)."""
    # Typical GPIM layout: Surf1, Surf2, Mode, ... — use known defaults when probe fails
    return {"surf1": 2, "surf2": 3, "mode": 4, "wfb": 5, "wsb": 6}


def ensure_scratch_gpim(mfe, ZOSAPI):
    """Ensure at least one GPIM operand exists so we can clone its cell map."""
    MOT = ZOSAPI.Editors.MFE.MeritOperandType
    for i in range(1, mfe.NumberOfOperands + 1):
        if mfe.GetOperandAt(i).Type == MOT.GPIM:
            return i
    row = mfe.AddOperand()
    row.ChangeType(MOT.GPIM)
    return mfe.NumberOfOperands


def count_weighted(mfe) -> int:
    n = 0
    for i in range(1, mfe.NumberOfOperands + 1):
        try:
            if float(mfe.GetOperandAt(i).Weight) > 0:
                n += 1
        except Exception:
            pass
    return n


def find_existing(mfe, mode, s1, s2, ZOSAPI, colmap) -> int:
    MOT = ZOSAPI.Editors.MFE.MeritOperandType
    for i in range(1, mfe.NumberOfOperands + 1):
        op = mfe.GetOperandAt(i)
        if op.Type != MOT.GPIM:
            continue
        try:
            if (int(op.GetOperandCell(colmap["surf1"]).IntegerValue) == s1
                    and int(op.GetOperandCell(colmap["surf2"]).IntegerValue) == s2
                    and int(op.GetOperandCell(colmap["mode"]).IntegerValue) == mode):
                return i
        except Exception:
            pass
    return -1


def read_worst(op, colmap):
    try:
        wfb = int(op.GetOperandCell(colmap["wfb"]).IntegerValue)
        wsb = int(op.GetOperandCell(colmap["wsb"]).IntegerValue)
        return wfb, wsb
    except Exception:
        return 0, 0


def apply(session) -> None:
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        raise RuntimeError("this extension requires a sequential system")

    say("=== GpimGhostReduce ===")
    say("Lens: " + (sys_.SystemFile or "(untitled)"))
    say(fmt("mode={0} top={1} weight={2:g} balance={3:g} optimize={4}",
            MODE, TOP_N, WEIGHT, BALANCE, OPTIMIZE))

    mfe = sys_.MFE
    m0 = float(mfe.GetMeritFunctionValue()) if mfe.NumberOfOperands > 0 else 0.0
    say(fmt("Baseline MF: {0:.6g}", m0))

    scratch = ensure_scratch_gpim(mfe, ZOSAPI)
    colmap = learn_gpim_map(mfe.GetOperandAt(scratch), ZOSAPI)
    MOT = ZOSAPI.Editors.MFE.MeritOperandType

    hits: List[GhostHit] = []
    for mode in modes_list():
        # Seed a GPIM with mode; OpticStudio fills worst faces into WFB/WSB
        op = mfe.GetOperandAt(scratch)
        try:
            op.GetOperandCell(colmap["mode"]).IntegerValue = mode
            op.GetOperandCell(colmap["surf1"]).IntegerValue = 0
            op.GetOperandCell(colmap["surf2"]).IntegerValue = 0
        except Exception:
            pass
        # Force update
        try:
            _ = float(mfe.GetMeritFunctionValue())
        except Exception:
            pass
        # Collect top ghosts by repeatedly reading WFB/WSB and locking them out via new operands
        seen = set()
        for _ in range(TOP_N * 3):
            try:
                if app.TerminateRequested:
                    say("Terminated by user.")
                    return
            except Exception:
                pass
            op = mfe.GetOperandAt(scratch)
            wfb, wsb = read_worst(op, colmap)
            if wfb <= 0 or wsb <= 0:
                break
            key = (mode, min(wfb, wsb), max(wfb, wsb))
            if key in seen:
                break
            seen.add(key)
            s1, s2 = min(wfb, wsb), max(wfb, wsb)
            # Score: use current operand value if available
            try:
                score = abs(float(op.Value))
            except Exception:
                score = 0.0
            hits.append(GhostHit(s1=s1, s2=s2, mode=mode, wfb=wfb, wsb=wsb, score=score))
            # Insert a locking GPIM so the next worst surfaces appear
            if find_existing(mfe, mode, s1, s2, ZOSAPI, colmap) < 0:
                row = mfe.AddOperand()
                row.ChangeType(MOT.GPIM)
                try:
                    row.GetOperandCell(colmap["surf1"]).IntegerValue = s1
                    row.GetOperandCell(colmap["surf2"]).IntegerValue = s2
                    row.GetOperandCell(colmap["mode"]).IntegerValue = mode
                    row.Weight = 0.0  # probe only
                    row.Comment = fmt("probe ghost {0}/{1} mode={2}", s1, s2, mode)
                except Exception as ex:
                    say("WARNING: could not write GPIM probe: " + str(ex))
            try:
                _ = float(mfe.GetMeritFunctionValue())
            except Exception:
                pass

    # Rank and keep top N overall
    hits.sort(key=lambda h: -h.score)
    chosen = hits[:TOP_N]
    say(fmt("Selected {0} ghost pair(s):", len(chosen)))
    for h in chosen:
        say(fmt("  surfaces {0}/{1}  mode={2}  score={3:.6g}", h.s1, h.s2, h.mode, h.score))

    # Assign weights relative to baseline MF
    weighted = count_weighted(mfe)
    for h in chosen:
        row_i = find_existing(mfe, h.mode, h.s1, h.s2, ZOSAPI, colmap)
        if row_i < 0:
            row = mfe.AddOperand()
            row.ChangeType(MOT.GPIM)
            row_i = mfe.NumberOfOperands
            op = mfe.GetOperandAt(row_i)
            op.GetOperandCell(colmap["surf1"]).IntegerValue = h.s1
            op.GetOperandCell(colmap["surf2"]).IntegerValue = h.s2
            op.GetOperandCell(colmap["mode"]).IntegerValue = h.mode
        else:
            op = mfe.GetOperandAt(row_i)
        w = WEIGHT
        if m0 > 0 and BALANCE > 0:
            try:
                val = abs(float(op.Value)) or 1.0
                w = WEIGHT * BALANCE * (m0 / max(weighted, 1)) / val
            except Exception:
                pass
        op.Weight = w
        op.Comment = fmt("GpimGhostReduce {0}/{1}", h.s1, h.s2)
        say(fmt("  weighted GPIM row {0}: W={1:.6g}", row_i, w))

    if OPTIMIZE:
        say("Running local DLS...")
        opt = sys_.Tools.OpenLocalOptimization()
        try:
            opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
            if CYCLES <= 0:
                opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
            else:
                try:
                    opt.NumberOfCycles = CYCLES
                except Exception:
                    opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Fixed_10_Cycles
            opt.RunAndWaitForCompletion()
        finally:
            opt.Close()
        say(fmt("MF after opt: {0:.6g}", float(mfe.GetMeritFunctionValue())))

    if SAVE_PATH:
        full = os.path.abspath(SAVE_PATH)
        parent = os.path.dirname(full)
        if parent:
            os.makedirs(parent, exist_ok=True)
        sys_.SaveAs(full)
        say("Saved: " + full)

    try:
        app.ProgressMessage = "Done. GPIM ghosts reduced."
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
        if not NO_DIALOG and not FILE_PATH:
            say("NOTE: Python twin has no settings dialog; pass flags or -nodialog.")
        ZOSAPI = bootstrap_zosapi(zos_root, say=say)
        session = connect_zos(ZOSAPI, FILE_PATH, say=say, zos_root=zos_root)
        apply(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex)); traceback.print_exc(); return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

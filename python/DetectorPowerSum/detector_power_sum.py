#!/usr/bin/env python3
# ============================================================
# DetectorPowerSum (Python / ZOS-API) - what this program does
# ============================================================
# Adds up how much light power landed on every Detector Rectangle
# in a non-sequential system, then prints each detector and the
# grand total. Uses GetDetectorData(..., 0, 0) — the same "sum of
# all pixels" number you see as Total Power in the Detector Viewer
# (and the NSDD operand). Optionally runs a ray trace first.
# Does not change the lens design. Twin of the C# User Extension.
#
# Flags (same names as the C# tool):
#   -file <zmx>  -out <path>  -trace  -nosplit  -noscatter  -nopol
#   -all  -quiet  -nodialog
# ============================================================

from __future__ import annotations

import os
import sys
import traceback
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

FILE_PATH: Optional[str] = None
OUT_PATH: Optional[str] = None
DO_TRACE = False
SPLIT = True
SCATTER = True
POL = True
ALL_DETS = False
QUIET = False


def say(line: str) -> None:
    print(line, flush=True)


def fmt(template: str, *args) -> str:
    return template.format(*args)


def parse_args(argv: List[str]) -> None:
    """Read -file / -out / -trace / ... and fill the global switches."""
    global FILE_PATH, OUT_PATH, DO_TRACE, SPLIT, SCATTER, POL, ALL_DETS, QUIET
    i = 0
    while i < len(argv):
        raw = argv[i]
        if raw.startswith("-") or raw.startswith("/"):
            a = parse_flag_token(raw)

            def next_tok() -> Optional[str]:
                nonlocal i
                if i + 1 < len(argv):
                    i += 1
                    return argv[i]
                return None

            if a == "file":
                FILE_PATH = next_tok()
            elif a == "out":
                OUT_PATH = next_tok()
            elif a == "trace":
                DO_TRACE = True
            elif a == "nosplit":
                SPLIT = False
            elif a == "noscatter":
                SCATTER = False
            elif a == "nopol":
                POL = False
            elif a == "all":
                ALL_DETS = True
            elif a in ("quiet", "nodialog"):
                if a == "quiet":
                    QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1


def sum_detectors(session) -> None:
    """Read total power from each Detector Rectangle and print the grand total."""
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    nce = sys_.NCE
    if nce.NumberOfObjects < 1:
        raise RuntimeError(
            "the system has no non-sequential objects (NSC mode or an NSC group is required)"
        )

    src = FILE_PATH or (sys_.SystemFile or "")

    if DO_TRACE:
        try:
            app.ProgressMessage = "Tracing rays..."
        except Exception:
            pass
        trace = sys_.Tools.OpenNSCRayTrace()
        try:
            try:
                trace.ClearDetectors(0)
            except Exception:
                pass
            trace.SplitNSCRays = SPLIT
            trace.ScatterNSCRays = SCATTER
            trace.UsePolarization = POL
            trace.IgnoreErrors = True
            trace.RunAndWaitForCompletion()
        finally:
            trace.Close()

    # Units: radiometric vs photometric
    src_units = sys_.SystemData.Units.SourceUnits
    lumens = ZOSAPI.SystemData.ZemaxSourceUnits.Lumens
    joules = ZOSAPI.SystemData.ZemaxSourceUnits.Joules
    if src_units == lumens:
        unit, kind = "lm", "photometric"
    elif src_units == joules:
        unit, kind = "J", "radiant energy"
    else:
        unit, kind = "W", "radiometric"

    report: List[str] = [
        "Detector Power Sum",
        "System : " + (src if src else "(untitled)"),
        fmt("Units  : {0} ({1}, from the system source-units setting)", unit, kind),
    ]
    if DO_TRACE:
        report.append(
            fmt(
                "Trace  : run before reading (split={0}, scatter={1}, polarization={2})",
                SPLIT,
                SCATTER,
                POL,
            )
        )
    report.append("")
    report.append("obj  type                    pixels      hits        power           comment")
    report.append("---  ----------------------  ----------  ----------  --------------  -------")

    rect_sum = 0.0
    other_sum = 0.0
    rect_count = 0
    other_count = 0
    terminated = False
    nobj = int(nce.NumberOfObjects)

    for i in range(1, nobj + 1):
        try:
            if app.TerminateRequested:
                terminated = True
                report.append("(terminated by user - the sums below are partial)")
                break
        except Exception:
            pass
        try:
            app.ProgressPercent = 10 + 85.0 * i / nobj
        except Exception:
            pass

        row = nce.GetObjectAt(i)
        is_rect = row.Type == ZOSAPI.Editors.NCE.ObjectType.DetectorRectangle

        # GetDetectorDimensions returns (ok, ur, uc) via pythonnet out-params
        try:
            ok_dim, ur, uc = nce.GetDetectorDimensions(i)
            # Some pythonnet builds return bool only and mutate byref; try both
            if isinstance(ok_dim, bool) and not isinstance(ur, (int, float)):
                # signature may be GetDetectorDimensions(i, out ur, out uc) -> bool
                ur_box = [0]
                uc_box = [0]
                # Fall through using reflection-style call
                ok_dim = False
                ur = uc = 0
        except TypeError:
            # Classic out-parameter pattern via clr
            ur = uc = 0
            ok_dim = False

        # Prefer explicit out-parameter call
        ur_u = 0
        uc_u = 0
        try:
            # pythonnet often returns (bool, uint, uint) for out params
            result = nce.GetDetectorDimensions(i)
            if isinstance(result, tuple) and len(result) >= 3:
                ok_dim, ur_u, uc_u = result[0], int(result[1]), int(result[2])
            elif isinstance(result, bool):
                ok_dim = result
                # Try System.UInt32 byref via a second approach below
                ur_u = uc_u = 0
            else:
                ok_dim = False
        except Exception:
            ok_dim = False
            ur_u = uc_u = 0

        # Fallback: try with clr ByRef if needed
        if not ok_dim or ur_u <= 0:
            try:
                import clr  # type: ignore
                from System import UInt32  # type: ignore

                ur_ref = clr.Reference[UInt32]()
                uc_ref = clr.Reference[UInt32]()
                ok_dim = bool(nce.GetDetectorDimensions(i, ur_ref, uc_ref))
                ur_u = int(ur_ref.Value)
                uc_u = int(uc_ref.Value)
            except Exception:
                pass

        is_detector = bool(ok_dim) and ur_u > 0 and uc_u > 0
        if not is_rect and not (ALL_DETS and is_detector):
            continue

        power = 0.0
        hits = 0.0
        # Pixel (0,0) means "sum of all pixels" total power (API quirk).
        ok = False
        try:
            result = nce.GetDetectorData(i, 0, 0)
            if isinstance(result, tuple):
                ok, power = bool(result[0]), float(result[1])
            else:
                ok = bool(result)
        except TypeError:
            try:
                import clr  # type: ignore
                from System import Double  # type: ignore

                p_ref = clr.Reference[Double]()
                ok = bool(nce.GetDetectorData(i, 0, 0, p_ref))
                power = float(p_ref.Value)
            except Exception:
                ok = False
                power = 0.0
        except Exception:
            ok = False
            power = 0.0

        try:
            result = nce.GetDetectorData(i, -3, 0)
            if isinstance(result, tuple):
                hits = float(result[1])
            else:
                import clr  # type: ignore
                from System import Double  # type: ignore

                h_ref = clr.Reference[Double]()
                nce.GetDetectorData(i, -3, 0, h_ref)
                hits = float(h_ref.Value)
        except Exception:
            hits = 0.0

        type_name = getattr(row, "TypeName", None) or "Detector"
        comment = (getattr(row, "Comment", None) or "").strip()
        power_s = fmt("{0:.8g}", power) if ok else "n/a"
        report.append(
            fmt(
                "{0:3d}  {1:<22}  {2:4d} x {3:<4d}  {4:10.6g}  {5:>14}  {6}{7}",
                i,
                type_name,
                ur_u,
                uc_u,
                hits,
                power_s,
                comment,
                "" if is_rect else "  [non-rectangle]",
            )
        )
        if not ok:
            power = 0.0
            report.append(
                fmt(
                    "     (obj {0}: this detector type does not report total flux via GetDetectorData - excluded from the sum)",
                    i,
                )
            )

        if is_rect:
            rect_sum += power
            rect_count += 1
        elif ok:
            other_sum += power
            other_count += 1

    report.append("")
    if rect_count == 0 and other_count == 0 and not terminated:
        report.append(
            "No detector objects found in the NCE."
            if ALL_DETS
            else "No Detector Rectangle objects found in the NCE (pass -all to include other detector types)."
        )
    else:
        report.append(
            fmt(
                "TOTAL POWER, {0} Detector Rectangle(s) : {1:.8g} {2} ({3})",
                rect_count,
                rect_sum,
                unit,
                kind,
            )
        )
        if ALL_DETS and other_count > 0:
            report.append(
                fmt(
                    "Sub-total, {0} other detector(s)       : {1:.8g} {2}",
                    other_count,
                    other_sum,
                    unit,
                )
            )
            report.append(
                fmt(
                    "TOTAL POWER, all {0} detector(s)       : {1:.8g} {2}",
                    rect_count + other_count,
                    rect_sum + other_sum,
                    unit,
                )
            )
        if not DO_TRACE:
            report.append(
                "(detector data as accumulated in the file/session - pass -trace to re-trace first)"
            )

    for line in report:
        say(line)

    out_path = OUT_PATH
    plugin = False
    try:
        plugin = app.Mode == ZOSAPI.ZOSAPI_Mode.Plugin
    except Exception:
        pass
    if not out_path and plugin:
        if not src:
            out_path = os.path.join(str(app.ZemaxDataDir), "DetectorPowerSum.txt")
        else:
            out_path = os.path.join(
                os.path.dirname(src),
                os.path.splitext(os.path.basename(src))[0] + "_power_sum.txt",
            )
    if out_path:
        parent = os.path.dirname(out_path)
        if parent:
            os.makedirs(parent, exist_ok=True)
        with open(out_path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(report) + "\n")
        say("Report : " + out_path)

    try:
        app.ProgressPercent = 100
        if terminated:
            app.ProgressMessage = "Terminated - partial sums reported."
        elif rect_count + other_count == 0:
            app.ProgressMessage = "No detectors found."
        else:
            app.ProgressMessage = fmt(
                "Total power: {0:.8g} {1} over {2} detector(s)",
                rect_sum + other_sum,
                unit,
                rect_count + other_count,
            )
    except Exception:
        pass


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

    session = None
    try:
        ZOSAPI = bootstrap_zosapi(zos_root, say=say)
        session = connect_zos(ZOSAPI, FILE_PATH, say=say, zos_root=zos_root)
        sum_detectors(session)
        return 0
    except Exception as ex:
        print("FATAL: " + str(ex))
        traceback.print_exc()
        return 1
    finally:
        if session is not None:
            session.close()


if __name__ == "__main__":
    sys.exit(main())

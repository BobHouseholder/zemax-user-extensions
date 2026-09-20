#!/usr/bin/env python3
# ============================================================
# DetectorDump (Python / ZOS-API) - what this program does
# ============================================================
# Non-sequential systems can have many detectors (light meters).
# This tool dumps EVERY detector in one go: native detector files,
# CSV pixel grids, false-color PNG heatmaps, and a summary table.
# The lens is not redesigned — we only read detectors (optional
# ray-trace first). Twin of the C# User Extension.
#
# Flags (same names as the C# tool):
#   -file <zmx>  -dir <folder>  -trace  -nosplit  -noscatter  -nopol
#   -data N  -log  -nocsv  -nopng  -nonative  -quiet  -nodialog
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

from _zos_bootstrap import (  # noqa: E402
    bootstrap_zosapi,
    call_out,
    connect_zos,
    discover_zos_root,
    parse_flag_token,
)

FILE_PATH: Optional[str] = None
OUT_DIR: Optional[str] = None
DO_TRACE = False
SPLIT = True
SCATTER = True
POL = True
DATA_CODE = 0
DO_CSV = True
DO_PNG = True
DO_NATIVE = True
LOG_SCALE = False
QUIET = False


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
    """Read -file / -dir / -trace / ... and fill the global switches."""
    global FILE_PATH, OUT_DIR, DO_TRACE, SPLIT, SCATTER, POL
    global DATA_CODE, DO_CSV, DO_PNG, DO_NATIVE, LOG_SCALE, QUIET
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
            elif a == "dir":
                OUT_DIR = next_tok()
            elif a == "trace":
                DO_TRACE = True
            elif a == "nosplit":
                SPLIT = False
            elif a == "noscatter":
                SCATTER = False
            elif a == "nopol":
                POL = False
            elif a == "data":
                DATA_CODE = parse_int(next_tok(), DATA_CODE)
            elif a == "log":
                LOG_SCALE = True
            elif a == "nocsv":
                DO_CSV = False
            elif a == "nopng":
                DO_PNG = False
            elif a == "nonative":
                DO_NATIVE = False
            elif a in ("quiet", "nodialog"):
                if a == "quiet":
                    QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        else:
            if not FILE_PATH:
                FILE_PATH = raw
        i += 1


def sanitize(s: str) -> str:
    """Make a safe file-name fragment from a detector label."""
    out = []
    for c in s:
        out.append(c if (c.isalnum() or c in "-_") else "_")
    r = "".join(out).strip("_")
    if len(r) > 40:
        r = r[:40]
    return r if r else "detector"


def get_detector_dims(nce, i: int) -> Tuple[bool, int, int]:
    """Return (ok, rows, cols) for detector object i."""
    try:
        result = nce.GetDetectorDimensions(i)
        if isinstance(result, tuple) and len(result) >= 3:
            return bool(result[0]), int(result[1]), int(result[2])
    except Exception:
        pass
    try:
        import clr  # type: ignore
        from System import UInt32  # type: ignore

        ur = clr.Reference[UInt32]()
        uc = clr.Reference[UInt32]()
        ok = bool(nce.GetDetectorDimensions(i, ur, uc))
        return ok, int(ur.Value), int(uc.Value)
    except Exception:
        return False, 0, 0


def get_detector_scalar(nce, i: int, pixel: int, data: int) -> float:
    """Read one GetDetectorData scalar (handles pythonnet out-params)."""
    try:
        result = nce.GetDetectorData(i, pixel, data)
        if isinstance(result, tuple):
            return float(result[1])
    except Exception:
        pass
    try:
        import clr  # type: ignore
        from System import Double  # type: ignore

        ref = clr.Reference[Double]()
        nce.GetDetectorData(i, pixel, data, ref)
        return float(ref.Value)
    except Exception:
        return 0.0


def grid_to_list(grid) -> Tuple[int, int, List[List[float]]]:
    """Convert a .NET 2D array or nested sequence to Python lists."""
    if grid is None:
        return 0, 0, []
    try:
        # .NET rectangular array
        nr = grid.GetLength(0)
        nc = grid.GetLength(1)
        rows = []
        for r in range(nr):
            rows.append([float(grid[r, c]) for c in range(nc)])
        return nr, nc, rows
    except Exception:
        pass
    try:
        rows = [[float(v) for v in row] for row in grid]
        return len(rows), (len(rows[0]) if rows else 0), rows
    except Exception:
        return 0, 0, []


def write_csv(rows: List[List[float]], path: str) -> None:
    """Write the pixel values as a CSV spreadsheet."""
    lines = []
    for row in rows:
        lines.append(",".join("{:.9g}".format(v) for v in row))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def lut(t: float) -> Tuple[int, int, int]:
    """Map 0..1 to a heat color (blue cold → red hot)."""
    t = max(0.0, min(1.0, t))
    ks = [0, 0.166, 0.333, 0.5, 0.666, 0.833, 1.0]
    cs = [
        (0, 0, 0),
        (0, 0, 255),
        (0, 255, 255),
        (0, 255, 0),
        (255, 255, 0),
        (255, 0, 0),
        (255, 255, 255),
    ]
    for k in range(6):
        if t <= ks[k + 1]:
            f = (t - ks[k]) / (ks[k + 1] - ks[k])
            return (
                int(cs[k][0] + f * (cs[k + 1][0] - cs[k][0])),
                int(cs[k][1] + f * (cs[k + 1][1] - cs[k][1])),
                int(cs[k][2] + f * (cs[k + 1][2] - cs[k][2])),
            )
    return (255, 255, 255)


def write_heatmap(rows: List[List[float]], path: str, caption: str) -> None:
    """Paint a false-color PNG heatmap of the pixel grid (Pillow)."""
    from PIL import Image, ImageDraw, ImageFont

    nr = len(rows)
    nc = len(rows[0]) if rows else 0
    peak = 0.0
    for row in rows:
        for v in row:
            if v > peak:
                peak = v
    if peak <= 0:
        peak = 1.0

    cell = max(1, 512 // max(nr, nc, 1))
    W = nc * cell
    H = nr * cell
    cap = 24
    floor = peak * 1e-4
    img = Image.new("RGB", (W, H + cap), (0, 0, 0))
    draw = ImageDraw.Draw(img)
    for r in range(nr):
        for c in range(nc):
            v = rows[r][c]
            if LOG_SCALE:
                t = 0.0 if v <= floor else math.log10(v / floor) / 4.0
            else:
                t = v / peak
            color = lut(t)
            y0 = (nr - 1 - r) * cell
            draw.rectangle([c * cell, y0, c * cell + cell - 1, y0 + cell - 1], fill=color)
    try:
        font = ImageFont.load_default()
    except Exception:
        font = None
    draw.text((2, H + 4), caption, fill=(255, 255, 255), font=font)
    img.save(path, "PNG")


def dump(session) -> None:
    """Walk every detector and write native + CSV + PNG + summary."""
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    nce = sys_.NCE
    if nce.NumberOfObjects < 1:
        raise RuntimeError(
            "the system has no non-sequential objects (NSC mode or an NSC group is required)"
        )

    src = FILE_PATH or (sys_.SystemFile or "")
    out_dir = OUT_DIR
    if not out_dir:
        if not src:
            out_dir = os.path.join(str(app.ZemaxDataDir), "DetectorDump")
        else:
            out_dir = os.path.join(
                os.path.dirname(src),
                os.path.splitext(os.path.basename(src))[0] + "_detectors",
            )
    os.makedirs(out_dir, exist_ok=True)
    say("System : " + (src if src else "(untitled)"))
    say("Output : " + out_dir)

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
        say(
            fmt(
                "Trace  : done (split={0}, scatter={1}, polarization={2})",
                SPLIT,
                SCATTER,
                POL,
            )
        )

    summary = [
        "obj  type                    pixels      total flux      peak value        hit px  comment",
        "---  ----------------------  ----------  --------------  ----------------  ------  -------",
    ]
    found = 0
    exported = 0
    nobj = int(nce.NumberOfObjects)

    for i in range(1, nobj + 1):
        try:
            if app.TerminateRequested:
                say("Terminated by user - export is partial.")
                summary.append("(terminated by user - export is partial)")
                break
        except Exception:
            pass
        try:
            app.ProgressPercent = 10 + 85.0 * i / nobj
            app.ProgressMessage = fmt(
                "Exporting detector object {0}/{1}...", i, nobj
            )
        except Exception:
            pass

        ok, rows_n, cols_n = get_detector_dims(nce, i)
        if not ok or rows_n <= 0 or cols_n <= 0:
            continue
        found += 1

        row = nce.GetObjectAt(i)
        type_name = getattr(row, "TypeName", None) or "Detector"
        comment = (getattr(row, "Comment", None) or "").strip()
        base_name = fmt(
            "obj{0:02d}_{1}", i, sanitize(comment if comment else type_name)
        )

        total_flux = get_detector_scalar(nce, i, 0, 0)

        grid = None
        try:
            grid = nce.GetAllDetectorDataSafe(i, DATA_CODE)
        except Exception:
            grid = None
        is_polar = "polar" in type_name.lower()
        if (grid is None) and is_polar:
            try:
                grid = nce.GetAllPolarDetectorDataSafe(
                    i, ZOSAPI.Editors.NCE.PolarDetectorDataType.Power
                )
            except Exception:
                grid = None

        nr, nc, rows = grid_to_list(grid)
        peak = 0.0
        hits = 0
        for r in rows:
            for v in r:
                if v > peak:
                    peak = v
                if v != 0:
                    hits += 1

        summary.append(
            fmt(
                "{0:3d}  {1:<22}  {2:4d} x {3:<4d}  {4:14.6g}  {5:16.6g}  {6:6d}  {7}",
                i,
                type_name,
                rows_n,
                cols_n,
                total_flux,
                peak,
                hits,
                comment,
            )
        )

        if DO_NATIVE:
            low = type_name.lower()
            if "color" in low:
                ext = ".DDC"
            elif is_polar:
                ext = ".DDP"
            elif "volume" in low:
                ext = ".DDV"
            else:
                ext = ".DDR"
            try:
                if nce.SaveDetector(i, os.path.join(out_dir, base_name + ext)):
                    say(fmt("  obj {0}: saved native {1}", i, base_name + ext))
                else:
                    say(
                        fmt(
                            "  obj {0}: native save not supported for this detector",
                            i,
                        )
                    )
            except Exception as ex:
                say(fmt("  obj {0}: native save failed: {1}", i, ex))

        if nr == 0 or nc == 0:
            say(fmt("  obj {0}: no pixel grid available (CSV/PNG skipped)", i))
            continue

        if DO_CSV:
            csv_path = os.path.join(out_dir, base_name + ".csv")
            write_csv(rows, csv_path)
            say(
                fmt(
                    "  obj {0}: wrote {1} ({2}x{3} pixels)",
                    i,
                    os.path.basename(csv_path),
                    nr,
                    nc,
                )
            )
        if DO_PNG:
            png_path = os.path.join(out_dir, base_name + ".png")
            write_heatmap(
                rows,
                png_path,
                fmt(
                    "obj {0}  {1}  {2}   total={3:.5g}  peak={4:.5g}",
                    i,
                    type_name,
                    comment,
                    total_flux,
                    peak,
                ),
            )
            say(fmt("  obj {0}: wrote {1}", i, os.path.basename(png_path)))
        exported += 1

    data_label = (
        "flux" if DATA_CODE == 0 else "irradiance" if DATA_CODE == 1 else "intensity"
    )
    summary.append("")
    summary.append(
        fmt(
            "{0} detector(s) found, {1} with pixel data exported. Data code {2} ({3}).",
            found,
            exported,
            DATA_CODE,
            data_label,
        )
    )
    summary_path = os.path.join(out_dir, "detectors_summary.txt")
    with open(summary_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(summary) + "\n")

    say("")
    for line in summary:
        say(line)
    if found == 0:
        say("NOTE: no detectors with data found. Did you run a trace? (pass -trace)")
    try:
        app.ProgressMessage = fmt(
            "Done. {0} detector(s) found, {1} exported to {2}", found, exported, out_dir
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
        dump(session)
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

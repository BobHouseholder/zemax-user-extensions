#!/usr/bin/env python3
# ============================================================
# EquivalentGlassFinder (Python / ZOS-API)
# ============================================================
# Find cheaper/stock glasses that behave almost the same (nd, Vd,
# dPgF distance). Optionally swap them in and reoptimize.
# Twin of the C# User Extension. Adds -file for standalone
# (C# is Interactive Extension only).
#
# Flags: -file -catalog -includeobsolete -report -reopt -save
#        -top N -wnd -wvd -wpgf -quiet -nodialog -out
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from dataclasses import dataclass
from typing import Dict, List, Optional

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

TARGET_CATALOG: Optional[str] = None
INCLUDE_OBSOLETE = False
REPORT_ONLY = False
REOPTIMIZE = False
SAVE_COPY = False
TOP_N = 3
WEIGHT_ND = 100.0
WEIGHT_VD = 1.0
WEIGHT_PGF = 500.0
QUIET = False
FILE_PATH: Optional[str] = None
OUT_PATH: Optional[str] = None
REPORT_LINES: List[str] = []


@dataclass
class GlassInfo:
    name: str
    catalog: str
    nd: float
    vd: float
    dpgf: float
    status: str
    exclude_substitution: bool


def say(line: str) -> None:
    print(line, flush=True)
    REPORT_LINES.append(line)


def fmt(template: str, *args) -> str:
    return template.format(*args)


def parse_int(s, keep):
    try:
        return int(s)
    except Exception:
        say("WARNING: {!r} is not a valid integer - keeping {}.".format(s, keep))
        return keep


def parse_double(s, keep):
    try:
        return float(s)
    except Exception:
        say("WARNING: {!r} is not a valid number - keeping {}.".format(s, keep))
        return keep


def parse_args(argv):
    global TARGET_CATALOG, INCLUDE_OBSOLETE, REPORT_ONLY, REOPTIMIZE, SAVE_COPY
    global TOP_N, WEIGHT_ND, WEIGHT_VD, WEIGHT_PGF, QUIET, FILE_PATH, OUT_PATH
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
            if a == "catalog":
                TARGET_CATALOG = next_tok()
            elif a == "includeobsolete":
                INCLUDE_OBSOLETE = True
            elif a == "report":
                REPORT_ONLY = True
            elif a == "reopt":
                REOPTIMIZE = True
            elif a == "save":
                SAVE_COPY = True
            elif a == "top":
                TOP_N = parse_int(next_tok(), TOP_N)
            elif a == "wnd":
                WEIGHT_ND = parse_double(next_tok(), WEIGHT_ND)
            elif a == "wvd":
                WEIGHT_VD = parse_double(next_tok(), WEIGHT_VD)
            elif a == "wpgf":
                WEIGHT_PGF = parse_double(next_tok(), WEIGHT_PGF)
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


def distance(a: GlassInfo, b: GlassInfo) -> float:
    dn = WEIGHT_ND * (a.nd - b.nd)
    dv = WEIGHT_VD * (a.vd - b.vd)
    dp = WEIGHT_PGF * (a.dpgf - b.dpgf)
    return math.sqrt(dn * dn + dv * dv + dp * dp)


def snapshot(sys_, ZOSAPI):
    """Grab a few performance numbers for before/after."""
    out = {}
    mfe = sys_.MFE
    try:
        out["MF"] = float(mfe.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.ANNO, 0, 0, 0, 0, 0, 0, 0, 0))
    except Exception:
        try:
            out["MF"] = float(mfe.GetMeritFunctionValue())
        except Exception:
            out["MF"] = float("nan")
    for name, op in (("EFFL", "EFFL"), ("TOTR", "TOTR"), ("PMAG", "PMAG")):
        try:
            t = getattr(ZOSAPI.Editors.MFE.MeritOperandType, op)
            out[name] = float(mfe.GetOperandValue(t, 0, 0, 0, 0, 0, 0, 0, 0))
        except Exception:
            out[name] = float("nan")
    return out


def print_metrics(title, before, after=None):
    say("")
    say(title)
    keys = sorted(set(before) | set(after or {}))
    for k in keys:
        b = before.get(k, float("nan"))
        if after is None:
            say(fmt("  {0}: {1:.6g}", k, b))
        else:
            a = after.get(k, float("nan"))
            say(fmt("  {0}: {1:.6g} -> {2:.6g}", k, b, a))


def derive_path(app, sys_, suffix: str) -> str:
    if OUT_PATH:
        base = OUT_PATH
        if os.path.isdir(base) or base.endswith(("/", "\\")):
            stem = os.path.splitext(os.path.basename(sys_.SystemFile or "system"))[0]
            return os.path.join(base, stem + suffix)
        return base if suffix in base else base + suffix
    src = sys_.SystemFile or ""
    if src:
        return os.path.join(os.path.dirname(src), os.path.splitext(os.path.basename(src))[0] + suffix)
    return os.path.join(str(getattr(app, "ZemaxDataDir", ".")), "EquivalentGlass" + suffix)


def write_report(app, sys_) -> str:
    path = derive_path(app, sys_, "_glass_report.txt")
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(REPORT_LINES) + "\n")
    say("Report: " + path)
    return path


def run_on_system(session) -> None:
    app = session.app
    ZOSAPI = session.ZOSAPI
    sys_ = session.TheSystem
    if sys_.Mode != ZOSAPI.SystemType.Sequential:
        say("This extension requires a sequential system.")
        return

    say("")
    say("=== Equivalent Glass Finder ===")
    say("Lens file : " + (sys_.SystemFile or "(untitled)"))
    say("Mode      : " + ("report only" if REPORT_ONLY else "apply best match"))
    say("Target    : " + (TARGET_CATALOG or "(catalogs in use, obsolete glasses only)"))
    say("")

    lde = sys_.LDE
    surfaces_by_glass: Dict[str, List[int]] = {}
    for i in range(lde.NumberOfSurfaces):
        mat = (lde.GetSurfaceAt(i).Material or "").strip()
        if not mat or mat.upper() == "MIRROR":
            continue
        surfaces_by_glass.setdefault(mat, []).append(i)
    if not surfaces_by_glass:
        say("No glass surfaces found - nothing to do.")
        return
    say(fmt("Found {0} distinct glass(es): {1}", len(surfaces_by_glass), ", ".join(surfaces_by_glass)))

    before = snapshot(sys_, ZOSAPI)

    catalogs_in_use = list(sys_.SystemData.MaterialCatalogs.GetCatalogsInUse())
    lookup = []
    for c in catalogs_in_use:
        if c and c.strip() and c not in lookup:
            lookup.append(c)
    if TARGET_CATALOG and TARGET_CATALOG not in lookup:
        lookup.append(TARGET_CATALOG)

    glass_data: Dict[str, GlassInfo] = {}
    candidates: List[GlassInfo] = []
    mat_tool = sys_.Tools.OpenMaterialsCatalog()
    try:
        for cat_idx, cat_name in enumerate(lookup, 1):
            try:
                app.ProgressMessage = "Reading catalog " + cat_name + "..."
            except Exception:
                pass
            try:
                mat_tool.SelectedCatalog = cat_name
                names = list(mat_tool.GetAllMaterials())
            except Exception:
                say("WARNING: could not read catalog '{0}' - skipped.".format(cat_name))
                continue
            is_cand = (not TARGET_CATALOG) or cat_name.lower() == TARGET_CATALOG.lower()
            for n in names:
                try:
                    if app.TerminateRequested:
                        say("Cancelled by user.")
                        return
                except Exception:
                    pass
                mat_tool.SelectedMaterial = n
                gi = GlassInfo(
                    name=n,
                    catalog=cat_name,
                    nd=float(mat_tool.Nd),
                    vd=float(mat_tool.Vd),
                    dpgf=float(mat_tool.dPgF),
                    status=str(mat_tool.MaterialStatus),
                    exclude_substitution=bool(mat_tool.ExcludeSubstitution),
                )
                if n not in glass_data:
                    glass_data[n] = gi
                if is_cand:
                    candidates.append(gi)
    finally:
        mat_tool.Close()

    say("")
    say(fmt(
        "Distance metric: sqrt( ({0:g}*dNd)^2 + ({1:g}*dVd)^2 + ({2:g}*dPgF)^2 )",
        WEIGHT_ND, WEIGHT_VD, WEIGHT_PGF,
    ))

    replacements: Dict[str, GlassInfo] = {}
    for current, surfs in sorted(surfaces_by_glass.items(), key=lambda kv: kv[1][0]):
        say("")
        say(fmt("--- {0}  (surface{1} {2}) ---",
                current, "s" if len(surfs) > 1 else "", ", ".join(str(s) for s in surfs)))
        cur = glass_data.get(current)
        if cur is None:
            say("    Not found in any catalog in use (model/custom glass?) - skipped.")
            continue
        say(fmt("    current: nd={0:.5f}  vd={1:.2f}  dPgF={2:+.4f}  status={3}  [{4}]",
                cur.nd, cur.vd, cur.dpgf, cur.status, cur.catalog))
        obsolete_only = not TARGET_CATALOG
        if obsolete_only and cur.status != "Obsolete":
            say("    Status OK - left unchanged (pass -catalog NAME to convert all glasses).")
            continue
        ranked = []
        for c in candidates:
            if c.exclude_substitution:
                continue
            if not INCLUDE_OBSOLETE and c.status == "Obsolete":
                continue
            if c.name.lower() == current.lower() and c.status == "Obsolete":
                continue
            ranked.append((distance(cur, c), c))
        ranked.sort(key=lambda x: x[0])
        ranked = ranked[: max(1, TOP_N)]
        if not ranked:
            say("    No candidates available - skipped.")
            continue
        for i, (d, g) in enumerate(ranked):
            mark = "-> " if i == 0 else "   "
            say(fmt("    {0}{1:<12} [{2}]  nd={3:.5f}  vd={4:.2f}  dPgF={5:+.4f}  status={6:<9}  dist={7:.3f}",
                    mark, g.name, g.catalog, g.nd, g.vd, g.dpgf, g.status, d))
        best = ranked[0][1]
        if best.name.lower() == current.lower():
            say("    Best match is the current glass - no change needed.")
        else:
            replacements[current] = best

    say("")
    if not replacements:
        say("No replacements to apply.")
        print_metrics("BEFORE (unchanged)", before)
        write_report(app, sys_)
        return
    if REPORT_ONLY:
        say(fmt("Report-only mode: {0} replacement(s) suggested but NOT applied.", len(replacements)))
        print_metrics("BEFORE (unchanged)", before)
        write_report(app, sys_)
        return

    for old, new in replacements.items():
        try:
            if app.TerminateRequested:
                say("Terminated by user - remaining replacements skipped.")
                break
        except Exception:
            pass
        for si in surfaces_by_glass[old]:
            lde.GetSurfaceAt(si).Material = new.name
        say(fmt("Applied: {0} -> {1} [{2}]", old, new.name, new.catalog))

    after = snapshot(sys_, ZOSAPI)
    if REOPTIMIZE:
        say("Running local DLS reoptimize...")
        try:
            opt = sys_.Tools.OpenLocalOptimization()
            try:
                opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
                opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
                opt.RunAndWaitForCompletion()
            finally:
                opt.Close()
            after = snapshot(sys_, ZOSAPI)
        except Exception as ex:
            say("WARNING: reoptimize failed: " + str(ex))

    print_metrics("BEFORE / AFTER", before, after)
    if SAVE_COPY:
        path = derive_path(app, sys_, "_equiv.zmx")
        parent = os.path.dirname(path)
        if parent:
            os.makedirs(parent, exist_ok=True)
        sys_.SaveAs(os.path.abspath(path))
        say("Saved: " + path)
    write_report(app, sys_)


def main(argv=None) -> int:
    global REPORT_LINES
    REPORT_LINES = []
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

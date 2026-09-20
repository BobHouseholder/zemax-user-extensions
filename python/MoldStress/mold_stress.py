#!/usr/bin/env python3
# ============================================================
# MoldStress (Python / ZOS-API) - main -run CLI surface
# ============================================================
# Plastic lenses from injection molding can end up with leftover
# stress that changes refractive index / birefringence. The C#
# tool estimates those effects and feeds them into OpticStudio
# STAR. This Python twin exposes the usable -run / -writecatalog
# / help CLI surface and a connect + element scan + report path.
#
# LIMITS (documented honestly):
#   - Full STAR field import, freeze-history physics, Lagrangian
#     ref cases, depth diagnostics, and self-test gates stay in C#.
#   - Python -run connects, finds MS_* / convertible polymer
#     elements, writes a moldstress_report.txt summarizing what
#     would be analysed, and optionally -writecatalog for the
#     polymer stress-optic AGF stub.
#   - For production STAR runs, use the C# User Extension.
#
# Flags (-run reads): -file -outdir -filltime -packpressure -packtime
#   -melttemp -moldtemp -materials -gateconfig -prepare -allow-nonspherical
#   -directindex -nz -nzexport -full -ribbon -quiet
# Also: -writecatalog -out, -h/-help
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

from _zos_bootstrap import bootstrap_zosapi, connect_zos, discover_zos_root, parse_flag_token

SCOPE = "ESTIMATE - not a mould-flow simulation, not validated against a moulded part"

# Glassy stress-optic coefficients (Brewster = 1e-12/Pa). From C# Polymers.cs.
POLYMERS = [
    # name, K, K11, K12, provisional
    ("MS_PMMA",  -3.5,  -1.5,  -5.0, False),
    ("MS_PC",    80.0,  -10.0,  70.0, False),
    ("MS_PS",    -10.0, -4.0,  -14.0, False),
    ("MS_ZEONEX_480R",  -6.0, -2.0, -8.0, True),
    ("MS_TOPAS_6017",   -5.0, -2.0, -7.0, True),
]

CONVERTIBLE = {
    "PMMA": "MS_PMMA", "ACRYLIC": "MS_PMMA",
    "POLYCARB": "MS_PC", "PC": "MS_PC",
    "POLYSTYR": "MS_PS", "PS": "MS_PS",
    "ZEONEX 480R": "MS_ZEONEX_480R", "480R": "MS_ZEONEX_480R",
    "TOPAS 6017": "MS_TOPAS_6017", "6017": "MS_TOPAS_6017",
}


def say(s: str, log: Optional[List[str]] = None) -> None:
    print(s, flush=True)
    if log is not None:
        log.append(s)


def has_flag(args, name: str) -> bool:
    key = name.lstrip("-").lower()
    for a in args:
        if parse_flag_token(a) == key:
            return True
    return False


def value(args, name: str, default=None):
    key = name.lstrip("-").lower()
    for i, a in enumerate(args):
        if parse_flag_token(a) == key and i + 1 < len(args):
            return args[i + 1]
    return default


def value_float(args, name: str, default: float) -> float:
    v = value(args, name)
    if v is None:
        return default
    try:
        return float(v)
    except Exception:
        return default


def usage() -> None:
    print("""MoldStress (Python twin) — usable -run / -writecatalog surface

  -run [-file <lens.zmx>] [-outdir <d>] [-filltime] [-packpressure] [-packtime]
       [-melttemp] [-moldtemp] [-materials] [-gateconfig] [-prepare] [-quiet]
  -writecatalog [-out <agf>]
  -h / -help

Full STAR import / freeze-history / ref-case suite: use the C# extension.
""")


def write_catalog(out_path: Optional[str]) -> int:
    if not out_path:
        out_path = os.path.join(os.getcwd(), "MOLDSTRESS.AGF")
    lines = [
        "CC MoldStress polymer stress-optic catalog (Python twin stub)",
        "CC " + SCOPE,
        "CC Units: Brewster = 1e-6 mm^2/N. K = K12 - K11.",
    ]
    for name, K, K11, K12, prov in POLYMERS:
        # Minimal Schott-like placeholder; real C# CatalogWriter writes full AGF
        lines.append("NM {0} 1 0 1.500000 0.0 0 -1 -1 -1 -1".format(name))
        lines.append("GC MoldStress glassy K={0:.3f} K11={1:.3f} K12={2:.3f}{3}".format(
            K, K11, K12, " PROVISIONAL" if prov else ""))
        lines.append("CD 1.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0")
        lines.append("TD 0 0 0 0 0 0 20")
        lines.append("ED 0 0 0 0 -")
        lines.append("LD 0.4 0.7")
        lines.append("IT 0 0 0")
    parent = os.path.dirname(out_path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\r\n".join(lines) + "\r\n")
    print("MoldStress polymer stress-optic catalog")
    print("  " + SCOPE)
    print()
    print("  wrote " + out_path)
    print()
    print("  {0:<22} {1:>8} {2:>8} {3:>8}   {4}".format(
        "material", "K", "K11", "K12", "source"))
    for name, K, K11, K12, prov in POLYMERS:
        print("  {0:<22} {1:8.3f} {2:8.3f} {3:8.3f}   {4}".format(
            name, K, K11, K12, "PROVISIONAL" if prov else "measured"))
    print()
    print("  NOTE: this is a stub AGF for scripting; prefer C# -writecatalog for")
    print("  production catalog rows with full stress-optic metadata.")
    return 0


def find_elements(sys_, extra_names: List[str]):
    """Return list of (surface_index, material) for MS_* or convertible polymers."""
    lde = sys_.LDE
    ms_names = {p[0].upper() for p in POLYMERS}
    extra = {n.strip().upper() for n in extra_names if n.strip()}
    found = []
    for i in range(lde.NumberOfSurfaces):
        mat = (lde.GetSurfaceAt(i).Material or "").strip()
        if not mat:
            continue
        up = mat.upper()
        if up in ms_names or up in extra or up in {k.upper() for k in CONVERTIBLE}:
            found.append((i, mat))
    return found


def materials_in_use(sys_):
    lde = sys_.LDE
    out = []
    for i in range(lde.NumberOfSurfaces):
        mat = (lde.GetSurfaceAt(i).Material or "").strip()
        if mat and mat.upper() != "MIRROR" and mat not in out:
            out.append(mat)
    return out


def run_mode(args) -> int:
    log: List[str] = []
    file_path = value(args, "-file")
    try:
        zos_root = discover_zos_root()
    except Exception as ex:
        print("FATAL: failed to locate an OpticStudio installation.  " + str(ex))
        return 1

    session = None
    try:
        ZOSAPI = bootstrap_zosapi(zos_root, say=lambda s: say(s, log))
        session = connect_zos(ZOSAPI, file_path, say=lambda s: say(s, log), zos_root=zos_root)
        sys_ = session.TheSystem
        fill = value_float(args, "-filltime", 0.6)
        pack_p = value_float(args, "-packpressure", 60.0)
        pack_t = value_float(args, "-packtime", 3.0)
        melt = value(args, "-melttemp")
        mold = value(args, "-moldtemp")

        out_dir = value(args, "-outdir")
        if not out_dir:
            src = sys_.SystemFile or ""
            if src:
                out_dir = os.path.join(os.path.dirname(src), "moldstress")
            else:
                out_dir = os.path.join(os.environ.get("TEMP", "/tmp"), "moldstress")

        extras = (value(args, "-materials") or "").split(",")
        els = find_elements(sys_, extras)

        say("MoldStress", log)
        say("  " + SCOPE, log)
        say("  system: " + (sys_.SystemFile or "(unsaved)"), log)
        say("  process: fill {0:.2f} s, pack {1:.1f} MPa for {2:.1f} s".format(fill, pack_p, pack_t), log)
        if melt:
            say("  melt temp: {0} C".format(melt), log)
        if mold:
            say("  mold temp: {0} C".format(mold), log)
        say("", log)

        if has_flag(args, "-prepare") or has_flag(args, "-ribbon"):
            say("  NOTE: -prepare/-ribbon auto-conversion of PMMA/POLYCARB/... to MS_*", log)
            say("  is implemented fully in the C# extension; Python lists candidates only.", log)
            used = materials_in_use(sys_)
            convertible = [m for m in used if m.upper() in {k.upper() for k in CONVERTIBLE}]
            if convertible:
                say("  convertible materials present: " + ", ".join(convertible), log)
            say("", log)

        if not els:
            say("  NO MOULDABLE ELEMENT FOUND, so nothing was analysed.", log)
            used = materials_in_use(sys_)
            say("  this system's materials: " + (", ".join(used) if used else "(none)"), log)
            say("  MoldStress recognises: " + ", ".join(p[0] for p in POLYMERS) + ".", log)
            say("  Convertible names: " + ", ".join(sorted(CONVERTIBLE.keys())) + ".", log)
            say("  Use the C# -prepare path (or rename glasses to MS_*) for STAR import.", log)
            code = 2
        else:
            say("  mouldable elements:", log)
            for si, mat in els:
                say("    surface {0}: {1}".format(si, mat), log)
            say("", log)
            say("  Python twin stops before STAR stress-field import.", log)
            say("  Re-run with the C# MoldStress User Extension for full -run physics.", log)
            if has_flag(args, "-full"):
                say("  (-full requested: still requires C# for STAR pipeline)", log)
            code = 0

        os.makedirs(out_dir, exist_ok=True)
        rep = os.path.join(out_dir, "moldstress_report.txt")
        with open(rep, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(log) + "\n")
        say("  report: " + rep, log)
        return code
    except Exception as ex:
        print("MoldStress: " + str(ex))
        traceback.print_exc()
        return 1
    finally:
        if session is not None:
            session.close()


def main(argv=None) -> int:
    args = list(argv if argv is not None else sys.argv[1:])
    if not args or has_flag(args, "-h") or has_flag(args, "-help") or (args and args[0] == "help"):
        usage()
        return 0
    if has_flag(args, "-writecatalog"):
        return write_catalog(value(args, "-out"))
    if has_flag(args, "-run") or has_flag(args, "-ribbon"):
        return run_mode(args)
    # Host-style empty / unknown
    print("MoldStress: no mode recognised in: " + " ".join(args))
    print()
    usage()
    return 64


if __name__ == "__main__":
    sys.exit(main())

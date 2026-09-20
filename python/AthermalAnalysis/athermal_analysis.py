#!/usr/bin/env python3
# ============================================================
# AthermalAnalysis (Python / ZOS-API)
# ============================================================
# C# AthermalAnalysis is a User Analysis window that reuses
# AthermalScan physics. The Python twin has no OpticStudio
# analysis window host — it is a thin wrapper that runs the
# same scan/report CLI as python/AthermalScan (always -nodialog).
#
# Prefer: python/AthermalScan/athermal_scan.py
# ============================================================

from __future__ import annotations

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_SCAN = os.path.join(os.path.dirname(_HERE), "AthermalScan")
if _SCAN not in sys.path:
    sys.path.insert(0, _SCAN)

from athermal_scan import main as scan_main  # noqa: E402


def main(argv=None) -> int:
    args = list(argv if argv is not None else sys.argv[1:])
    # Force no dialog; User Analysis window is C#-only
    if not any(a.lstrip("-/").lower() == "nodialog" for a in args):
        args = ["-nodialog"] + args
    print(
        "NOTE: Python AthermalAnalysis has no dockable analysis window; "
        "running shared AthermalScan CLI (same physics).",
        flush=True,
    )
    return scan_main(args)


if __name__ == "__main__":
    sys.exit(main())

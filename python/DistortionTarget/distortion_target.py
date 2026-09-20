#!/usr/bin/env python3
# ============================================================
# DistortionTarget (Python / ZOS-API) - what this program does
# ============================================================
# Builds a chrome-on-glass dot target in non-sequential mode:
# a glass plate with a square grid of chrome dots (Edmund Optics
# 15963 defaults). Uses an Array object so we do not place
# thousands of dots one-by-one. Twin of the C# User Extension.
# Dialogs are -nodialog only (no WinForms port).
#
# Flags: -n -pitch -dot -plate -thick -material -coating -film
#        -drawlimit -rig -save -file -nodialog -quiet
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from typing import List, Optional, Set

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

N = 199
PITCH = 0.500
DOT_DIA = 0.250
PLATE = 100.0
PLATE_T = 1.50
MATERIAL = "B270"
COATING = "CHROME_OD3"
FILM = 0.0001
DRAW_LIMIT = 2000
RIG = False
SAVE_PATH: Optional[str] = None
FILE_PATH: Optional[str] = None
NO_DIALOG = False
QUIET = False
EXPLICIT: Set[str] = set()


def span() -> float:
    return (N - 1) * PITCH


def outer_edge() -> float:
    return span() / 2.0 + DOT_DIA / 2.0


def clearance() -> float:
    return PLATE / 2.0 - outer_edge()


def chrome_fraction() -> float:
    return math.pi * (DOT_DIA / 2.0) ** 2 / (PITCH * PITCH)


def say(line: str) -> None:
    print(line, flush=True)


def parse_args(argv: List[str]) -> None:
    """Read -n / -pitch / -file / ... and fill the global switches."""
    global N, PITCH, DOT_DIA, PLATE, PLATE_T, MATERIAL, COATING, FILM
    global DRAW_LIMIT, RIG, SAVE_PATH, FILE_PATH, NO_DIALOG, QUIET
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

            if a == "n":
                N = int(next_tok())
                EXPLICIT.add("n")
            elif a == "pitch":
                PITCH = float(next_tok())
                EXPLICIT.add("pitch")
            elif a == "dot":
                DOT_DIA = float(next_tok())
                EXPLICIT.add("dot")
            elif a == "plate":
                PLATE = float(next_tok())
                EXPLICIT.add("plate")
            elif a == "thick":
                PLATE_T = float(next_tok())
                EXPLICIT.add("thick")
            elif a == "material":
                MATERIAL = next_tok() or MATERIAL
                EXPLICIT.add("material")
            elif a == "coating":
                COATING = next_tok() or COATING
                EXPLICIT.add("coating")
            elif a == "film":
                FILM = float(next_tok())
                EXPLICIT.add("film")
            elif a == "drawlimit":
                DRAW_LIMIT = int(next_tok())
                EXPLICIT.add("drawlimit")
            elif a == "rig":
                RIG = True
                EXPLICIT.add("rig")
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
        i += 1


def validate() -> None:
    """Refuse a geometry that cannot be built rather than building a wrong one."""
    if N < 2:
        raise RuntimeError("dots per side must be at least 2")
    if PITCH <= 0 or DOT_DIA <= 0:
        raise RuntimeError("pitch and dot diameter must be positive")
    if DOT_DIA >= PITCH:
        raise RuntimeError(
            "dots of {0} mm on a {1} mm pitch would touch or overlap".format(DOT_DIA, PITCH)
        )
    if PLATE_T <= 0:
        raise RuntimeError("substrate thickness must be positive")
    if FILM <= 0:
        raise RuntimeError("chrome film thickness must be positive")
    if outer_edge() >= PLATE / 2.0:
        raise RuntimeError(
            "{0} x {0} dots on a {1} mm pitch span {2:.3f} mm centre-to-centre, "
            "putting the outermost dot edge at {3:.3f} mm — outside a {4:.3f} mm "
            "plate. Reduce the count, the pitch, or enlarge the substrate.".format(
                N, PITCH, span(), outer_edge(), PLATE
            )
        )


def set_par(row, n: int, value: float, ZOSAPI) -> None:
    """Set NSC object parameter number n (respect Integer vs Double cells)."""
    col = getattr(ZOSAPI.Editors.NCE.ObjectColumn, "Par" + str(n))
    cell = row.GetObjectCell(col)
    if cell.DataType == ZOSAPI.Editors.CellDataType.Integer:
        if value != math.floor(value):
            raise RuntimeError("Par{0} is an integer cell; refusing to truncate {1}".format(n, value))
        cell.IntegerValue = int(value)
    else:
        cell.DoubleValue = float(value)


def new_object(nce, index: int, obj_type, ZOSAPI):
    """Insert a new NSC object of the given type at an index."""
    if index > nce.NumberOfObjects + 1:
        raise RuntimeError(
            "cannot insert at {0}; editor holds {1}".format(index, nce.NumberOfObjects)
        )
    row = nce.InsertNewObjectAt(index)
    if row is None:
        raise RuntimeError("InsertNewObjectAt({0}) returned null".format(index))
    row.ChangeType(row.GetObjectTypeSettings(obj_type))
    return row


def summary() -> str:
    lines = [
        "built {0} x {0} = {1:,} dots, {2} mm pitch, {3} mm dots".format(
            N, N * N, PITCH, DOT_DIA
        ),
        "  pattern span {0:.3f} mm, outermost dot edge {1:.3f} mm, clearance to face {2:.3f} mm".format(
            span(), outer_edge(), clearance()
        ),
        "  chrome fill {0:.4f}, open {1:.4f}".format(chrome_fraction(), 1.0 - chrome_fraction()),
        (
            "  odd count: one dot lands on the optical axis"
            if N % 2 == 1
            else "  EVEN count: no dot on axis, the grid straddles it"
        ),
        "  draw limit {0:,} of {1:,} — drawing only, no traced result depends on it".format(
            DRAW_LIMIT, N * N
        ),
        "  radiometry needs ray splitting ON: without it OpticStudio applies "
        "no coating, so the chrome neither blocks nor reflects",
    ]
    return "\n".join(lines)


def add_rig(nce, ZOSAPI) -> None:
    """Add source / detector around the target if -rig."""
    OT = ZOSAPI.Editors.NCE.ObjectType
    src = new_object(nce, nce.NumberOfObjects + 1, OT.SourceRectangle, ZOSAPI)
    src.Comment = "collimated beam"
    half = min(45.0, span() / 2.0 - PITCH)
    for n, v in [(1, 20), (2, 1000000), (3, 1.0), (4, 0), (5, 0), (6, half), (7, half), (8, 0), (9, 0), (10, 0)]:
        set_par(src, n, v, ZOSAPI)
    src.ZPosition = -10.0

    det = new_object(nce, nce.NumberOfObjects + 1, OT.DetectorRectangle, ZOSAPI)
    det.Comment = "transmitted power"
    for n, v in [(1, PLATE / 2.0), (2, PLATE / 2.0), (3, 100), (4, 100), (5, 0)]:
        set_par(det, n, v, ZOSAPI)
    det.ZPosition = PLATE_T + 10.0


def build(session) -> None:
    """Create the glass plate + chrome dots Array in the NSC editor."""
    app = session.app
    ZOSAPI = session.ZOSAPI
    sysm = session.TheSystem
    OT = ZOSAPI.Editors.NCE.ObjectType

    # Fresh NSC system (same as C#)
    sysm.New(False)
    sysm.MakeNonSequential()
    sysm.SystemData.Units.LensUnits = ZOSAPI.SystemData.ZemaxSystemUnits.Millimeters
    nce = sysm.NCE

    glass = new_object(nce, 1, OT.RectangularVolume, ZOSAPI)
    glass.Comment = "substrate {0:.2f} sq x {1:.2f}".format(PLATE, PLATE_T)
    glass.Material = MATERIAL
    set_par(glass, 1, PLATE / 2.0, ZOSAPI)
    set_par(glass, 2, PLATE / 2.0, ZOSAPI)
    set_par(glass, 3, PLATE_T, ZOSAPI)
    set_par(glass, 4, PLATE / 2.0, ZOSAPI)
    set_par(glass, 5, PLATE / 2.0, ZOSAPI)
    glass.ZPosition = 0.0

    # Thin cylinder volume (flat discs silently ignore coatings)
    dot = new_object(nce, 2, OT.CylinderVolume, ZOSAPI)
    dot.Comment = "chrome dot (array parent)"
    set_par(dot, 1, DOT_DIA / 2.0, ZOSAPI)
    set_par(dot, 2, FILM, ZOSAPI)
    set_par(dot, 3, DOT_DIA / 2.0, ZOSAPI)
    dot.ZPosition = -FILM

    face = dot.CoatScatterData.GetFaceData(1)
    face.Coating = COATING
    if face.Coating != COATING:
        raise RuntimeError(
            "coating '{0}' did not take — it reads '{1}'. Is it in COATING.DAT?".format(
                COATING, face.Coating
            )
        )

    dot.TypeData.RaysIgnoreObject = ZOSAPI.Editors.NCE.RaysIgnoreObjectType.Always
    dot.DrawData.DoNotDrawObject = True

    arr = new_object(nce, 3, OT.Array, ZOSAPI)
    arr.Comment = "{0} x {0} dot array".format(N)
    for n, v in [
        (1, 2),
        (2, N),
        (3, N),
        (4, 1),
        (5, PITCH),
        (6, PITCH),
        (7, 0.0),
        (8, 1),
        (9, 0),
        (10, 0),
        (11, 0),
        (12, 1),
        (13, 0),
        (14, 0),
        (15, 0),
        (16, 1),
        (20, DRAW_LIMIT),
    ]:
        set_par(arr, n, v, ZOSAPI)
    arr.XPosition = -span() / 2.0
    arr.YPosition = -span() / 2.0
    arr.ZPosition = 0.0

    if RIG:
        add_rig(nce, ZOSAPI)

    if SAVE_PATH:
        full = os.path.abspath(SAVE_PATH)
        parent = os.path.dirname(full)
        if parent:
            os.makedirs(parent, exist_ok=True)
        sysm.SaveAs(full)
        if not os.path.isfile(full):
            raise RuntimeError("SaveAs reported no error but wrote no file at " + full)
        say("saved " + full)

    say(summary())
    try:
        app.ProgressMessage = "Done. Distortion target built."
        app.ProgressPercent = 100
    except Exception:
        pass


def main(argv: Optional[List[str]] = None) -> int:
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex))
        return 1

    # Prefer standalone when -file or (-save with -nodialog)
    standalone_file = FILE_PATH
    if not standalone_file and SAVE_PATH and NO_DIALOG:
        standalone_file = None  # still CreateNewApplication with empty system

    try:
        zos_root = discover_zos_root()
    except Exception as ex:
        print("FATAL: failed to locate an OpticStudio installation.  " + str(ex))
        return 1

    session = None
    try:
        validate()
        ZOSAPI = bootstrap_zosapi(zos_root, say=say)
        from _zos_bootstrap import ZosSession
        if FILE_PATH:
            session = connect_zos(ZOSAPI, FILE_PATH, say=say, zos_root=zos_root)
        elif SAVE_PATH or NO_DIALOG:
            # Fresh standalone app (new NSC system built in-process)
            connection = ZOSAPI.ZOSAPI_Connection()
            app = connection.CreateNewApplication()
            if app is None or app.PrimarySystem is None:
                raise RuntimeError("could not start a standalone OpticStudio instance")
            if not app.IsValidLicenseForAPI:
                raise RuntimeError("license is not valid for ZOS-API: " + str(app.LicenseStatus))
            session = ZosSession(ZOSAPI, app, app.PrimarySystem, True, zos_root)
            say("Started standalone OpticStudio (new system)")
        else:
            session = connect_zos(ZOSAPI, None, say=say, zos_root=zos_root)
            if not NO_DIALOG:
                say("NOTE: Python twin has no settings dialog; using CLI defaults / flags (-nodialog).")
        build(session)
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

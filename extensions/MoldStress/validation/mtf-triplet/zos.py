"""Shared ZOS-API bootstrap for the MoldStress MTF test, 2026-08-29."""
import glob, os


def find_zosapi():
    for root in sorted(glob.glob(r"C:\Program Files\Ansys Zemax OpticStudio*"),
                       reverse=True):
        for dirpath, dirnames, filenames in os.walk(root):
            if "ZOSAPI.dll" in filenames:
                return dirpath
            if dirpath.count(os.sep) - root.count(os.sep) >= 3:
                dirnames[:] = []
    raise RuntimeError("no ZOSAPI.dll found")


import clr  # noqa: E402
_z = find_zosapi()
clr.AddReference(os.path.join(_z, "ZOSAPI_NetHelper.dll"))
import ZOSAPI_NetHelper  # noqa: E402
ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize()
clr.AddReference(os.path.join(_z, "ZOSAPI.dll"))
clr.AddReference(os.path.join(_z, "ZOSAPI_Interfaces.dll"))
import ZOSAPI  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def connect():
    conn = ZOSAPI.ZOSAPI_Connection()
    app = conn.CreateNewApplication()
    if app is None or not app.IsValidLicenseForAPI:
        raise RuntimeError("no standalone ZOS-API licence")
    return app

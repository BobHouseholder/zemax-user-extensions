"""Redo the through-focus scan wide enough to bracket BOTH the true minimum
and the place the focus solve moved to.

The first scan ran +/-60 um and both curves were still falling at the left
edge, so its reported minimum was the edge of the window, not a minimum - and
the solve's -325 um landed off the plot entirely. A minimum found at the end of
a scan range is a statement about the range.
"""
import glob, json, os, re
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms2")
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
E = ZOSAPI.Editors.MFE.MeritOperandType
FIELDS = [0.0, 0.7, 1.0]
STEPS = [(-600 + 15 * i) / 1000.0 for i in range(61)]      # -600 .. +300 um

IDX = {}
for p in sorted(glob.glob(os.path.join(MS, "moldstress_s*_index.txt"))):
    IDX[int(re.search(r"_s(\d+)_index", p).group(1))] = p

app = connect()
s = app.PrimarySystem
res = json.load(open(os.path.join(HERE, "results2.json")))
LAST = res["last_surface"]


def scan(with_star):
    assert s.LoadFile(BASE, False)
    row = s.LDE.GetSurfaceAt(LAST)
    row.ThicknessCell.MakeSolveFixed()
    t0 = float(row.Thickness)
    if with_star:
        for surf, path in IDX.items():
            di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
            di.SetDataIsLocal()
            di.FEAData.ImportDirectIndex_1(path)
            di.Fits.Refit()
            di.Fits.GRINStep = 0.50
    mf, curve = s.MFE, []
    for dz in STEPS:
        row.Thickness = t0 + dz
        curve.append({"d": dz,
                      "rwre": [float(mf.GetOperandValue(
                          E.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0)) for h in FIELDS]})
    return t0, curve


for tag, star in (("tf_base", False), ("tf_mould", True)):
    t0, curve = scan(star)
    res[tag] = {"t0": t0, "curve": curve}
    b = min(curve, key=lambda c: c["rwre"][0])
    edge = abs(b["d"] - STEPS[0]) < 1e-9 or abs(b["d"] - STEPS[-1]) < 1e-9
    print("%-9s best axis %+.0f um -> %.4f waves   %s"
          % (tag, b["d"] * 1000, b["rwre"][0],
             "AT THE RANGE EDGE - widen" if edge else "interior minimum"))

shift = (res["poly_solve_mould"]["bfl"] - res["poly_solve_base"]["bfl"]) * 1000
print("solve moved the plane %+.0f um; scan covers %+.0f .. %+.0f um"
      % (shift, STEPS[0] * 1000, STEPS[-1] * 1000))
assert STEPS[0] * 1000 <= shift <= STEPS[-1] * 1000, "solve position off-scan"

with open(os.path.join(HERE, "results2.json"), "w") as fh:
    json.dump(res, fh)
app.CloseApplication()
print("results2.json updated")

"""Is the -0.295 mm focal shift the FIELD, or the FIT of the field?

The exported cloud has dn = 0 on axis and +/-1.094e-4 at the rim of a 5.41 mm
part in a 2.47 mm wall. Treated as a smooth radial gradient that is a GRIN
power of order 2e-5 /mm - about 0.06% of system power, where the measurement
says 0.98%. Either my estimate is wrong or the fit has near-axis curvature the
data does not.

The fit is an MBA (multilevel B-spline, MBAMaxLevel 8) - a LOCAL interpolant,
which is free to wiggle between samples. GetFittedIndex reads it back, so the
fit and the data can be compared directly instead of argued about.
"""
import math, os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
E = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()

di = s.LDE.GetSurfaceAt(1).STARData.DirectIndex
di.SetDataIsLocal()
di.FEAData.ImportDirectIndex_1(os.path.join(MS, "moldstress_s1_index.txt"))
di.Fits.Refit()
di.Fits.GRINStep = 0.5
print("IndexDataType after import:", s.LDE.GetSurfaceAt(1).STARData.IndexDataType)

fr = di.Fits.FitResultsIndex
for q in ("Points", "FitAccuracy", "FitAverage", "FitFill", "FitLevels",
          "FitNorm", "FitPV", "FitRMS"):
    try:
        print("   %-12s %s" % (q, getattr(fr, q)))
    except Exception as e:
        print("   %-12s err %s" % (q, e))

# --- the exported cloud, as data ------------------------------------------
rows = []
for line in open(os.path.join(MS, "moldstress_s1_index.txt")):
    p = line.split()
    if len(p) >= 4:
        rows.append((float(p[0]), float(p[1]), float(p[2]), float(p[3])))
zs = sorted(set(round(r[2], 6) for r in rows))
print("\nexported z planes:", ["%.4f" % z for z in zs])
ND = 1.4917

print("\nDATA: dn vs r on each z plane (worst over azimuth)")
for z in zs:
    pl = [r for r in rows if abs(r[2] - z) < 1e-6]
    byr = {}
    for x, y, _, n in pl:
        rr = round(math.hypot(x, y), 4)
        byr.setdefault(rr, []).append(n - ND)
    rr = sorted(byr)
    show = [rr[0]] + [rr[len(rr) * k // 5] for k in range(1, 5)] + [rr[-1]]
    print("  z %7.4f : " % z + "  ".join(
        "r%.2f %+.2e" % (q, max(byr[q], key=abs)) for q in show))

# --- the FIT, read back on the same lines ---------------------------------
print("\nFIT: GetFittedIndex along r (y=0) on each z plane")
sig = None
for z in zs:
    vals = []
    for k in range(11):
        r = 5.413 * k / 10.0
        try:
            v = di.Fits.GetFittedIndex(r, 0.0, z)
            sig = "GetFittedIndex(x,y,z)"
        except Exception as e:
            sig = "failed: %s" % e
            v = float("nan")
        vals.append(v)
    print("  z %7.4f : " % z + " ".join(
        ("%+.2e" % (v - ND)) if v == v else "  nan  " for v in vals))
    if sig and sig.startswith("failed"):
        print("   ", sig)
        break

app.CloseApplication()
print("done")

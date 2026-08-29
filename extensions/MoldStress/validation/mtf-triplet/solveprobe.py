"""What actually moved? Four arms on one system, changing ONE thing at a time.

The tool reported baseline 0.0412 -> 0.6888 waves. The through-focus scan says
that at a FIXED image plane the moulded lens reads 0.084 against the baseline's
0.080. Those cannot both be descriptions of the same change, so one of them is
about the image plane rather than the optics. This finds out which.
"""
import os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
IDX = {1: "moldstress_s1_index.txt", 3: "moldstress_s3_index.txt",
       6: "moldstress_s6_index.txt"}
E = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem


def read(tag):
    mf = s.MFE
    row = s.LDE.GetSurfaceAt(7)
    print("%-34s BFL %8.4f  EFFL %8.4f  RWRE(w1) %8.4f  RWRE(w2) %8.4f" % (
        tag, row.Thickness,
        mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 2, 0, 0, 0, 0, 0, 0)))


def star():
    for surf, fn in IDX.items():
        di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(os.path.join(MS, fn))
        di.Fits.Refit()
        di.Fits.GRINStep = 0.50


print("=== arm 1: solve LEFT ALONE (exactly what the tool's -run does) ===")
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
print("  surface 7 thickness solve:", s.LDE.GetSurfaceAt(7).ThicknessCell.GetSolveData().Type)
read("baseline, solve active")
star()
read("moulded, solve active")

print()
print("=== arm 2: solve FIXED before loading (image plane pinned) ===")
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
read("baseline, plane pinned")
star()
read("moulded, plane pinned")

print()
print("=== what the index files actually contain (surface 1) ===")
rows = [l.split() for l in open(os.path.join(MS, IDX[1]))]
rows = [(float(a), float(b), float(c), float(d)) for a, b, c, d in rows]
ns = [r[3] for r in rows]
axis = [r for r in rows if abs(r[0]) < 1e-9 and abs(r[1]) < 1e-9]
print("  points %d   n min %.9f  max %.9f  span %.3e"
      % (len(rows), min(ns), max(ns), max(ns) - min(ns)))
print("  MS_PMMA Nd 1.4917 -> dn range %+.3e .. %+.3e"
      % (min(ns) - 1.4917, max(ns) - 1.4917))
print("  on-axis (x=y=0) samples: %d, n %.9f .. %.9f"
      % (len(axis), min(a[3] for a in axis), max(a[3] for a in axis)))
rmax = max((r[0] ** 2 + r[1] ** 2) ** 0.5 for r in rows)
rim = [r for r in rows if (r[0] ** 2 + r[1] ** 2) ** 0.5 > 0.98 * rmax]
print("  rim   (r>0.98 rmax) samples: %d, n %.9f .. %.9f"
      % (len(rim), min(a[3] for a in rim), max(a[3] for a in rim)))
app.CloseApplication()
print("done")

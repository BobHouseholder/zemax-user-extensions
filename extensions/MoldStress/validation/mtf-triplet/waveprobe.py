"""The NULL cloud is a no-op at wave 2 and NOT at wave 1. That is a
wavelength-handling signature, not an integration one (the step sweep moved
nothing). Enumerate what the DirectIndex object actually exposes, then measure
every wavelength.
"""
import os
from zos import ZOSAPI, connect, HERE

TMP = os.path.join(HERE, "ctl")
MS = os.path.join(HERE, "ms")
SURF = {1: "s1", 3: "s3", 6: "s6"}
E = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()

star = s.LDE.GetSurfaceAt(1).STARData
di = star.DirectIndex
print("STARData      :", [q for q in dir(star) if not q.startswith(("_", "get_", "set_"))])
print()
print("DirectIndex   :", [q for q in dir(di) if not q.startswith(("_", "get_", "set_"))])
print()
print("DI.FEAData    :", [q for q in dir(di.FEAData) if not q.startswith(("_", "get_", "set_"))])
print()
print("DI.Fits       :", [q for q in dir(di.Fits) if not q.startswith(("_", "get_", "set_"))])
print()

mf = s.MFE
wl = s.SystemData.Wavelengths


def row(tag):
    vals = []
    for w in (1, 2, 3):
        vals.append(mf.GetOperandValue(E.RWRE, 4, w, 0, 0, 0, 0, 0, 0))
    print("%-30s EFFL %8.4f   RWRE  w1 %.4f  w2 %.4f  w3 %.4f"
          % (tag, mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0), *vals))
    return vals


print("wavelengths:", [round(wl.GetWavelength(i).Wavelength, 5) for i in (1, 2, 3)])
row("no data")

for surf in SURF:
    d = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
    d.SetDataIsLocal()
    d.FEAData.ImportDirectIndex_1(os.path.join(TMP, "null_%s.txt" % SURF[surf]))
    d.Fits.Refit()
    d.Fits.GRINStep = 0.5
row("NULL cloud (n == Nd exactly)")

# Does the object carry a working wavelength the tool never sets?
for name in ("WorkingWavelength", "SetWorkingWavelength", "Wavelength",
             "ReferenceWavelength"):
    if hasattr(di, name):
        print("  DirectIndex has", name, "->", getattr(di, name))
    if hasattr(di.FEAData, name):
        print("  DI.FEAData has", name, "->", getattr(di.FEAData, name))
    if hasattr(di.Fits, name):
        print("  DI.Fits has", name, "->", getattr(di.Fits, name))

# What index does the system actually use inside element 1 now?
try:
    print("\nindex readback via IndexOfRefraction:")
    for w in (1, 2, 3):
        print("   wave %d  surf1 material index %.6f"
              % (w, s.LDE.GetSurfaceAt(1).GetSurfaceCell(
                  ZOSAPI.Editors.LDE.SurfaceColumn.Par1).DoubleValue))
        break
except Exception as e:
    print("readback n/a:", e)

app.CloseApplication()
print("done")

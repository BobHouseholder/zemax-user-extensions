"""STARData exposes IndexDataType, and DI.Fits exposes Settings. If either can
say "this cloud is a DELTA", the export should write dn and the material keeps
its own dispersion - which is exactly the defect the NULL cloud just exposed.

An earlier probe in this project concluded absolute was correct because a delta
file "killed tracing". That test was run with whatever IndexDataType defaults
to. If the type is switchable, that conclusion was about the default, not about
the format.
"""
import os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
TMP = os.path.join(HERE, "ctl")
SURF = {1: ("s1", 1.4917), 3: ("s3", 1.5905), 6: ("s6", 1.4917)}
E = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
star = s.LDE.GetSurfaceAt(1).STARData

print("IndexDataType now :", star.IndexDataType, type(star.IndexDataType))
try:
    et = star.IndexDataType.GetType()
    print("enum type         :", et.FullName)
    for v in __import__("System").Enum.GetNames(et):
        print("   option:", v)
except Exception as e:
    print("enum enumeration failed:", e)

fs = s.LDE.GetSurfaceAt(1).STARData.DirectIndex.Fits.Settings
print()
print("DI.Fits.Settings  :", [q for q in dir(fs) if not q.startswith(("_", "get_", "set_"))])
for q in [q for q in dir(fs) if not q.startswith(("_", "get_", "set_"))]:
    try:
        v = getattr(fs, q)
        if not callable(v):
            print("   %-28s %s" % (q, v))
    except Exception:
        pass

print()
print("FitResultsIndex   :", [q for q in dir(
    s.LDE.GetSurfaceAt(1).STARData.DirectIndex.Fits.FitResultsIndex)
    if not q.startswith(("_", "get_", "set_"))])

# --- write delta files and try the delta type, if one exists --------------
os.makedirs(TMP, exist_ok=True)
delta = {}
for surf, (tag, nd) in SURF.items():
    dst = os.path.join(TMP, "delta_%s.txt" % tag)
    with open(os.path.join(MS, "moldstress_%s_index.txt" % tag)) as fh, \
            open(dst, "w") as g:
        for line in fh:
            p = line.split()
            if len(p) >= 4:
                g.write("%s %s %s %.9E\n" % (p[0], p[1], p[2], float(p[3]) - nd))
    delta[surf] = dst
print("\ndelta files written")

mf = s.MFE


def show(tag):
    print("%-34s EFFL %8.4f  RWRE w1 %.4f  w2 %.4f  w3 %.4f" % (
        tag, mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 2, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 3, 0, 0, 0, 0, 0, 0)))


show("no data")
import System
names = list(System.Enum.GetNames(star.IndexDataType.GetType()))
for nm in names:
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
    s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
    ok = True
    for surf in SURF:
        sd = s.LDE.GetSurfaceAt(surf).STARData
        try:
            sd.IndexDataType = System.Enum.Parse(sd.IndexDataType.GetType(), nm)
        except Exception as e:
            print("  cannot set", nm, e)
            ok = False
            break
        d = sd.DirectIndex
        d.SetDataIsLocal()
        src = delta[surf] if "delta" in nm.lower() or "diff" in nm.lower() \
            else os.path.join(MS, "moldstress_%s_index.txt" % SURF[surf][0])
        d.FEAData.ImportDirectIndex_1(src)
        d.Fits.Refit()
        d.Fits.GRINStep = 0.5
    if ok:
        show("IndexDataType=%s" % nm)

app.CloseApplication()
print("done")

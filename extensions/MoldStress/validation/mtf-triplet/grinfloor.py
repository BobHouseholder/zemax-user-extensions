"""Is the NULL cloud's 0.0412 -> 0.0968 wave step integration error?

If it is, it must SHRINK as the GRIN step shrinks, and vanish in the limit.
If it does not move with step size, it is something else and the label would
be wrong. The uniform cloud is the right probe because its true answer is
known exactly: zero change.
"""
import os
from zos import ZOSAPI, connect, HERE

TMP = os.path.join(HERE, "ctl")
MS = os.path.join(HERE, "ms")
SURF = {1: "s1", 3: "s3", 6: "s6"}
E = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem


def run(files, step, tag):
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
    s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
    mf = s.MFE
    if files:
        for surf, path in files.items():
            di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
            di.SetDataIsLocal()
            di.FEAData.ImportDirectIndex_1(path)
            di.Fits.Refit()
            di.Fits.GRINStep = step
    print("%-22s step %5.3f   EFFL %8.4f   RWRE(w1) %.4f   RWRE(w2) %.4f" % (
        tag, step,
        mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0),
        mf.GetOperandValue(E.RWRE, 4, 2, 0, 0, 0, 0, 0, 0)))


null = {k: os.path.join(TMP, "null_%s.txt" % v) for k, v in SURF.items()}
full = {k: os.path.join(MS, "moldstress_%s_index.txt" % v) for k, v in SURF.items()}

run(None, 0.0, "no data (truth)")
print()
for st in (1.0, 0.5, 0.25, 0.10, 0.05, 0.02):
    run(null, st, "NULL cloud")
print()
for st in (0.5, 0.10, 0.02):
    run(full, st, "FULL cloud")
app.CloseApplication()
print("done")

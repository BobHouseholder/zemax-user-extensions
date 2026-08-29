"""Through-focus RWRE scan of baseline and moulded, on the SAME focus grid.

QuickFocus is a tool with its own search; trusting it on both arms and then
reporting that the perturbed system beat the unperturbed one would be trusting
the instrument over the physics. This scans the image distance explicitly, so
best focus is read off a curve rather than taken on faith - and the two curves
are directly comparable because they share a grid.
"""
import json, os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
IDX = {1: "moldstress_s1_index.txt", 3: "moldstress_s3_index.txt",
       6: "moldstress_s6_index.txt"}
FIELDS = [0.0, 0.7, 1.0]
STEPS = [i * 0.005 for i in range(-10, 11)]        # +/-50 um in 5 um steps

app = connect()
s = app.PrimarySystem
res = json.load(open(os.path.join(HERE, "results.json")))


def scan(with_star):
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
    lde = s.LDE
    row = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2)
    row.ThicknessCell.MakeSolveFixed()
    t0 = float(row.Thickness)
    if with_star:
        for surf, fn in IDX.items():
            di = lde.GetSurfaceAt(surf).STARData.DirectIndex
            di.SetDataIsLocal()
            di.FEAData.ImportDirectIndex_1(os.path.join(MS, fn))
            di.Fits.Refit()
            di.Fits.GRINStep = 0.50
    mf = s.MFE
    curve = []
    for d in STEPS:
        row.Thickness = t0 + d
        vals = [float(mf.GetOperandValue(
            ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0))
            for h in FIELDS]
        curve.append({"d": d, "rwre": vals})
    return t0, curve


for tag, star in (("tf_base", False), ("tf_mould", True)):
    t0, curve = scan(star)
    res[tag] = {"t0": t0, "curve": curve}
    best = min(curve, key=lambda c: c["rwre"][0])
    bestall = min(curve, key=lambda c: sum(v * v for v in c["rwre"]))
    print("%-9s t0 %.4f | axis best at %+.3f mm -> %.4f waves | "
          "all-field best at %+.3f -> %s" % (
              tag, t0, best["d"], best["rwre"][0], bestall["d"],
              ["%.4f" % v for v in bestall["rwre"]]))
    print("           curve:", " ".join("%.3f" % c["rwre"][0] for c in curve))

with open(os.path.join(HERE, "results.json"), "w") as fh:
    json.dump(res, fh)
app.CloseApplication()
print("done")

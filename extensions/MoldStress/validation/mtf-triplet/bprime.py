"""State B' - the BASELINE refocused, by the identical QuickFocus criterion
used on the moulded system.

Why this run exists: refocusing the moulded lens took it to 0.0193 waves on
axis, BETTER than the 0.0801 the baseline read. A perturbation cannot improve
a system, so the baseline was not at its own best focus - its image plane came
from a paraxial marginal-ray-height solve. Comparing best-focus-moulded to
paraxial-focus-baseline would credit moulding with a refocus the DESIGN was
owed. Both sides get the same treatment here.
"""
import json, os
from zos import ZOSAPI, connect, HERE

FIELDS = [0.0, 0.7, 1.0]
app = connect()
s = app.PrimarySystem
res = json.load(open(os.path.join(HERE, "results.json")))

assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
lde = s.LDE
img_prev = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2)
img_prev.ThicknessCell.MakeSolveFixed()
bfl0 = float(img_prev.Thickness)

qf = s.Tools.OpenQuickFocus()
qf.Criterion = ZOSAPI.Tools.General.QuickFocusCriterion.RMSWavefront
qf.UseCentroid = True
qf.RunAndWaitForCompletion()
qf.Close()
bfl1 = float(img_prev.Thickness)
print("baseline back focus %.4f -> %.4f mm (%+.4f)" % (bfl0, bfl1, bfl1 - bfl0))

mf = s.MFE
sc = {"rwre": [float(mf.GetOperandValue(
          ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0))
          for h in FIELDS],
      "rsre": [float(mf.GetOperandValue(
          ZOSAPI.Editors.MFE.MeritOperandType.RSRE, 4, 2, 0.0, h, 0, 0, 0, 0))
          for h in FIELDS],
      "effl": float(mf.GetOperandValue(
          ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 2, 0, 0, 0, 0, 0, 0))}

an = s.Analyses.New_FftMtf()
st = an.GetSettings()
st.MaximumFrequency = 100.0
st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
an.ApplyAndWaitForCompletion()
r = an.GetResults()
series = []
for k in range(r.NumberOfDataSeries):
    ds = r.GetDataSeries(k)
    x = [float(v) for v in ds.XData.Data]
    y = ds.YData.Data
    series.append({"freq": x,
                   "tan": [float(y.GetValue(i, 0)) for i in range(len(x))],
                   "sag": [float(y.GetValue(i, 1)) for i in range(len(x))]})
an.Close()

res["Bp"] = {"label": "baseline, refocused", "scalars": sc, "mtf": series,
             "bfl_before": bfl0, "bfl_after": bfl1}
print("B' RWRE", ["%.4f" % v for v in sc["rwre"]])
print("B' RSRE", ["%.5f" % v for v in sc["rsre"]])
print("B' EFFL %.4f" % sc["effl"])

with open(os.path.join(HERE, "results.json"), "w") as fh:
    json.dump(res, fh)
app.CloseApplication()
print("done")

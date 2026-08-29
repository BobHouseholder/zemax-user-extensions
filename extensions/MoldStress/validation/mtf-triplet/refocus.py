"""Two questions the first pass left open, both of which change what the
headline MEANS.

1. The moulded system's EFFL moved 30.010 -> 29.715 mm, a 1% focal shift. Some
   of the MTF collapse is therefore DEFOCUS, which a user refocuses out in a
   second. State D re-solves best focus and re-measures: what survives a
   refocus is the part that is actually lost.

2. A->B looked free at wave 2 (d-line) while the tool's own report showed
   0.2307 -> 0.0412 waves at wave 1. The MS_* rows are a two-coefficient nd/vd
   fit, so the substitution is expected to move the ENDS of the band, not its
   middle. Measured per wavelength here rather than asserted.
"""
import json, os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
IDX = {1: "moldstress_s1_index.txt", 3: "moldstress_s3_index.txt",
       6: "moldstress_s6_index.txt"}
FIELDS = [(0.0, "0.0 deg"), (0.7, "6.3 deg"), (1.0, "9.0 deg")]

app = connect()
s = app.PrimarySystem
res = json.load(open(os.path.join(HERE, "results.json")))


def mtf():
    an = s.Analyses.New_FftMtf()
    st = an.GetSettings()
    st.MaximumFrequency = 100.0
    st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
    an.ApplyAndWaitForCompletion()
    r = an.GetResults()
    out = []
    for k in range(r.NumberOfDataSeries):
        ds = r.GetDataSeries(k)
        x = [float(v) for v in ds.XData.Data]
        y = ds.YData.Data
        out.append({"freq": x,
                    "tan": [float(y.GetValue(i, 0)) for i in range(len(x))],
                    "sag": [float(y.GetValue(i, 1)) for i in range(len(x))]})
    an.Close()
    return out


def per_wave():
    mf, d = s.MFE, {}
    for w in (1, 2, 3):
        d[w] = [float(mf.GetOperandValue(
            ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, w, 0.0, hy, 0, 0, 0, 0))
            for hy, _ in FIELDS]
    return d


def scalars():
    mf = s.MFE
    return {"rwre": [float(mf.GetOperandValue(
                ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 2, 0.0, hy, 0, 0, 0, 0))
                for hy, _ in FIELDS],
            "rsre": [float(mf.GetOperandValue(
                ZOSAPI.Editors.MFE.MeritOperandType.RSRE, 4, 2, 0.0, hy, 0, 0, 0, 0))
                for hy, _ in FIELDS],
            "effl": float(mf.GetOperandValue(
                ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 2, 0, 0, 0, 0, 0, 0))}


# ---- question 2: where the substitution actually costs -------------------
assert s.LoadFile(os.path.join(HERE, "plastic-triplet.zmx"), False)
res["A"]["per_wave"] = per_wave()
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
res["B"]["per_wave"] = per_wave()
wl = s.SystemData.Wavelengths
res["waves"] = [float(wl.GetWavelength(i).Wavelength) for i in (1, 2, 3)]
print("wavelengths um:", res["waves"])
for w in (1, 2, 3):
    print("  wave %d  A %s   B %s" % (
        w, ["%.4f" % v for v in res["A"]["per_wave"][w]],
        ["%.4f" % v for v in res["B"]["per_wave"][w]]))

# ---- question 1: refocus -------------------------------------------------
lde = s.LDE
img_prev = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2)
cell = img_prev.ThicknessCell
try:
    cell.MakeSolveFixed()          # the MRA solve is paraxial and GRIN-blind
except Exception as e:
    print("solve clear:", e)
bfl_before = float(img_prev.Thickness)

for surf, fn in IDX.items():
    di = lde.GetSurfaceAt(surf).STARData.DirectIndex
    di.SetDataIsLocal()
    di.FEAData.ImportDirectIndex_1(os.path.join(MS, fn))
    di.Fits.Refit()
    di.Fits.GRINStep = 0.50
print("STAR reloaded; back focus before refocus %.4f mm" % bfl_before)

qf = s.Tools.OpenQuickFocus()
qf.Criterion = ZOSAPI.Tools.General.QuickFocusCriterion.RMSWavefront
qf.UseCentroid = True
qf.RunAndWaitForCompletion()
qf.Close()
bfl_after = float(img_prev.Thickness)
print("back focus after refocus  %.4f mm  (shift %+.4f mm)"
      % (bfl_after, bfl_after - bfl_before))

res["D"] = {"label": "with moulding, refocused",
            "scalars": scalars(), "mtf": mtf(),
            "bfl_before": bfl_before, "bfl_after": bfl_after}
print("D RWRE", ["%.4f" % v for v in res["D"]["scalars"]["rwre"]])
print("D RSRE", ["%.5f" % v for v in res["D"]["scalars"]["rsre"]])

with open(os.path.join(HERE, "results.json"), "w") as fh:
    json.dump(res, fh)
app.CloseApplication()
print("done")

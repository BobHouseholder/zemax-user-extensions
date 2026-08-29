"""The final measurement set, with the controls the earlier passes showed are
necessary.

Six states. The three-way split matters because two DIFFERENT things were
found to move the headline, and only one of them is moulding:

  poly_solve_base / poly_solve_mould
        image plane on the file's own marginal-ray-height solve, all three
        wavelengths. This is what MoldStress -run reports.
  poly_pin_base / poly_pin_null / poly_pin_mould
        image plane PINNED. NULL is a uniform cloud (n == Nd everywhere) -
        physically a no-op, so any change it shows is the pipeline, not the
        part.
  mono_pin_base / mono_pin_mould
        pinned AND at the d-line, the one wavelength where the NULL control
        is an exact no-op. This is the trustworthy before/after.
"""
import json, os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
TMP = os.path.join(HERE, "ctl")
SURF = {1: "s1", 3: "s3", 6: "s6"}
E = ZOSAPI.Editors.MFE.MeritOperandType
FIELDS = [0.0, 0.7, 1.0]

app = connect()
s = app.PrimarySystem
res = json.load(open(os.path.join(HERE, "results.json")))


def load(pin, mono=False):
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
    if pin:
        s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
    if mono:
        # The FftMtf settings object on this build exposes no Wavelength
        # member, so the single wavelength is made in the SYSTEM instead -
        # unambiguous, and it makes every operand monochromatic too.
        wl = s.SystemData.Wavelengths
        for i in (3, 1):
            wl.RemoveWavelength(i)
        assert wl.NumberOfWavelengths == 1
        assert abs(wl.GetWavelength(1).Wavelength - 0.5875618) < 1e-6


def star(which):
    for surf, tag in SURF.items():
        p = (os.path.join(MS, "moldstress_%s_index.txt" % tag) if which == "full"
             else os.path.join(TMP, "null_%s.txt" % tag))
        di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(p)
        di.Fits.Refit()
        di.Fits.GRINStep = 0.50


def mtf(mono):
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


def scal():
    mf = s.MFE
    wd = 2 if s.SystemData.Wavelengths.NumberOfWavelengths > 1 else 1
    return {"bfl": float(s.LDE.GetSurfaceAt(7).Thickness),
            "effl": float(mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0)),
            "rwre_w2": [float(mf.GetOperandValue(
                E.RWRE, 4, wd, 0.0, h, 0, 0, 0, 0)) for h in FIELDS],
            "rwre_axis": [float(mf.GetOperandValue(
                E.RWRE, 4, w, 0, 0, 0, 0, 0, 0))
                for w in range(1, s.SystemData.Wavelengths.NumberOfWavelengths + 1)],
            "rsre": [float(mf.GetOperandValue(
                E.RSRE, 4, wd, 0.0, h, 0, 0, 0, 0)) for h in FIELDS]}


plan = [
    ("poly_solve_base",  False, None,   False),
    ("poly_solve_mould", False, "full", False),
    ("poly_pin_base",    True,  None,   False),
    ("poly_pin_null",    True,  "null", False),
    ("poly_pin_mould",   True,  "full", False),
    ("mono_pin_base",    True,  None,   True),
    ("mono_pin_null",    True,  "null", True),
    ("mono_pin_mould",   True,  "full", True),
]

for tag, pin, which, mono in plan:
    load(pin, mono)
    if which:
        star(which)
    d = scal()
    d["mtf"] = mtf(mono)
    d["mono"] = mono
    res[tag] = d
    print("%-17s BFL %7.4f  EFFL %8.4f  RWRE(w2) %s" % (
        tag, d["bfl"], d["effl"], ["%.4f" % v for v in d["rwre_w2"]]))

with open(os.path.join(HERE, "results.json"), "w") as fh:
    json.dump(res, fh)
app.CloseApplication()
print("wrote results.json")

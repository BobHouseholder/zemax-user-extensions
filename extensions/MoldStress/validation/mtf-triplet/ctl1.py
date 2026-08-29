"""Re-measure article 1's per-wavelength control and WRITE IT TO A FILE.

report.py carried these three triples as literals typed out of a terminal,
which is the one thing the report format is supposed to forbid. This emits
ctl1.json and report.py reads it, so the page and the measurement cannot drift.
It also re-checks the numbers that were hardcoded.
"""
import json, os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
TMP = os.path.join(HERE, "ctl")
SURF = {1: "s1", 3: "s3", 6: "s6"}
E = ZOSAPI.Editors.MFE.MeritOperandType
WAS = {"no_data": [0.0412, 0.0801, 0.1080],
       "null": [0.0968, 0.0801, 0.0717],
       "full": [0.1011, 0.0836, 0.0852]}

app = connect()
s = app.PrimarySystem
out = {}
for tag, which in (("no_data", None), ("null", "null"), ("full", "full")):
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"),
                      False)
    s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
    if which:
        for surf, tg in SURF.items():
            p = (os.path.join(MS, "moldstress_%s_index.txt" % tg)
                 if which == "full" else os.path.join(TMP, "null_%s.txt" % tg))
            di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
            di.SetDataIsLocal()
            di.FEAData.ImportDirectIndex_1(p)
            di.Fits.Refit()
            di.Fits.GRINStep = 0.50
    mf = s.MFE
    out[tag] = [float(mf.GetOperandValue(E.RWRE, 4, w, 0, 0, 0, 0, 0, 0))
                for w in (1, 2, 3)]
    out[tag + "_effl"] = float(
        mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0))
    agree = all(abs(a - b) < 5e-5 for a, b in zip(out[tag], WAS[tag]))
    print("%-8s %s   hardcoded %s   %s"
          % (tag, ["%.6f" % v for v in out[tag]],
             ["%.4f" % v for v in WAS[tag]],
             "agrees" if agree else "DISAGREES with what was published"))

d = abs(out["no_data"][1] - out["null"][1])
print("\nd-line no_data vs null: %.9f vs %.9f, difference %.2e waves"
      % (out["no_data"][1], out["null"][1], d))
print("that is %s - 'exact' was an overclaim; 'identical to 5 decimals' is not"
      % ("not bit-identical" if d else "bit-identical"))
out["dline_null_delta"] = d

with open(os.path.join(HERE, "ctl1.json"), "w") as fh:
    json.dump(out, fh, indent=1)
app.CloseApplication()
print("wrote ctl1.json")

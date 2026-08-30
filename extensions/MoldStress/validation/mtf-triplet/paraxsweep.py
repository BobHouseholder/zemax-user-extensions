"""ITEM 2, part 2. Turn "non-linear" into a response curve with a threshold.

paraxctl.py settled the open question: a SYNTHETIC analytic index field
n(r) = n0 + a*r^2, whose paraxial power change is exactly -2*a*t and therefore
exactly linear in a, comes back from STAR with

    a/10  ->  0.0206 of its closed form
    a     ->  0.5173
    10a   ->  0.9097

so tenth/full = 0.0040 where first order demands 0.100 - and the REAL moulding
field on the same lens gives 0.0049. The non-linearity is not a property of the
moulding field, the catalogue or the physics: STAR reproduces it on an input
that is linear by construction.

That is the answer, but "non-linear" is not yet actionable. The shape of
meas/pred - 0.02, 0.52, 0.91 as the field grows - looks like a FLOOR: index
variation below some size is largely discarded by the fit, above it is carried.
If so there is a threshold, it can be measured, and the tool can say whether a
user's field is above it.

Sweeps the amplitude over six decades and reports meas/pred at each. Also
checks the GRIN step, since that is the other producer-side constant in this
path and a response that moves with it would mean the integration rather than
the fit (gates section B: sweep the constants inside the producer).

Writes paraxsweep.json.
"""
import json
import os
import shutil

import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
SYN = os.path.join(HERE, "sweep")
WAVE_D = 2
FRONTS = [1, 3, 5]
out = {}

P = json.load(open(os.path.join(HERE, "paraxctl.json")))
A_REF = {int(f): P["real_field_quadratic_fit"][f]["fits"]["0.25"]["a"] for f in ("1", "3", "5")}
N0 = {int(f): P["real_field_quadratic_fit"][f]["n0"] for f in ("1", "3", "5")}
HEIGHT = {int(k): v for k, v in P["ray_heights"].items()}
EFFL0 = P["effl_zemax"]
ELEM_T = {1: 4.0, 3: 1.2, 5: 4.0}

if os.path.isdir(SYN):
    shutil.rmtree(SYN)
os.makedirs(SYN)
POINTS = {f: np.loadtxt(os.path.join(REAL, "moldstress_s%d_index.txt" % f)) for f in FRONTS}


def predict(scale):
    tot = 0.0
    for f in FRONTS:
        phi = -2.0 * (A_REF[f] * scale) * ELEM_T[f]
        tot += -(EFFL0 ** 2) * (HEIGHT[f] / 1.0) * phi
    return tot


app = connect()
s = app.PrimarySystem


def load(scale, grinstep=0.50):
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    if scale is None:
        return s.MFE.GetOperandValue(E.EFFL, 0, WAVE_D, 0, 0, 0, 0, 0, 0)
    d = os.path.join(SYN, "s%+0.6e" % scale)
    os.makedirs(d, exist_ok=True)
    for f in FRONTS:
        p = POINTS[f].copy()
        r2 = p[:, 0] ** 2 + p[:, 1] ** 2
        p[:, 3] = N0[f] + A_REF[f] * scale * r2
        pp = os.path.join(d, "s%d.txt" % f)
        np.savetxt(pp, p, fmt="%.9E", delimiter=" ")
        di = s.LDE.GetSurfaceAt(f).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(pp)
        di.Fits.Refit()
        di.Fits.GRINStep = grinstep
    return s.MFE.GetOperandValue(E.EFFL, 0, WAVE_D, 0, 0, 0, 0, 0, 0)


base = load(None)
print("baseline EFFL = %.6f mm" % base)
print()
print("SYNTHETIC AMPLITUDE SWEEP - the input is EXACTLY linear in the scale")
print("%12s %13s %14s %14s %10s %12s" %
      ("scale", "peak dn", "dEFFL meas", "dEFFL pred", "meas/pred", "d(dEFFL)/d(s)"))
rows = {}
prev = None
SCALES = [0.0, 1e-3, 3e-3, 1e-2, 3e-2, 0.1, 0.3, 1.0, 3.0, 10.0, 30.0, 100.0, 1000.0]
for sc in SCALES:
    ef = load(sc)
    dm, dp = ef - base, predict(sc)
    # peak dn of the synthetic field on element 1
    r2max = (POINTS[1][:, 0] ** 2 + POINTS[1][:, 1] ** 2).max()
    peak_dn = abs(A_REF[1] * sc * r2max)
    slope = ""
    if prev is not None and abs(sc - prev[0]) > 0:
        slope = "%12.4f" % ((dm - prev[1]) / (predict(sc) - predict(prev[0]))
                            if abs(predict(sc) - predict(prev[0])) > 1e-15 else float("nan"))
    rows["%g" % sc] = dict(effl=ef, d_meas=dm, d_pred=dp, peak_dn=peak_dn,
                           ratio=(dm / dp) if abs(dp) > 1e-15 else None)
    print("%12g %13.3e %14.6f %14.6f %10s %12s"
          % (sc, peak_dn, dm, dp,
             ("%.4f" % (dm / dp)) if abs(dp) > 1e-15 else "-", slope))
    prev = (sc, dm)
out["sweep"] = rows
out["predict_at_1"] = predict(1.0)

print()
print("GRIN STEP - is this the FIT or the INTEGRATION?")
print("%10s %14s %14s %10s" % ("GRIN step", "dEFFL @ s=1", "dEFFL @ s=10", "ratio"))
gs = {}
for step in (1.0, 0.5, 0.25, 0.1, 0.05):
    e1 = load(1.0, grinstep=step) - base
    e10 = load(10.0, grinstep=step) - base
    gs["%g" % step] = dict(s1=e1, s10=e10)
    print("%10.2f %14.6f %14.6f %10.4f" % (step, e1, e10, e10 / e1 if e1 else float("nan")))
out["grin_step"] = gs

json.dump(out, open(os.path.join(HERE, "paraxsweep.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote paraxsweep.json")
print("done")

"""ITEM 1, part 5. The extraction recipe.

Established so far:

  SHIPPED  GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0) does not measure
           retardance. It reads 0.5-1.0 waves on three fields whose retardance
           is exactly zero, it is not rotation invariant (the same stress state
           rotated 45 deg reads 0.062 rad against 4.260 on surface 5, a factor
           of 69), and it does not scale with stress (814x too high at
           0.02 MPa, 0.16x at 200 MPa). On each ring it takes three values -
           0, +d, d-pi - with span exactly pi and exact zeros at theta = 0,
           +-90, 180 deg. That is an ANGLE, not a phase.

  arg4=1   IS a real integrated retardance: exact to five decimals against the
           closed form from 0.02 to 10 MPa, i.e. up to pi radians, then
           saturates. But it is NOT rotation invariant either - it reads
           1.92486 for sxx = S and 0.012 for the same state rotated 45 deg.

So arg3/arg4 look like an ANALYSER ORIENTATION, and arg4=1 happened to align
with the uniaxial arm's principal axis. Retardance itself is a property of the
medium and must not depend on that orientation, so the correct extraction is
presumably an extremum over it.

This sweeps the orientation and asks:

  1. does max over orientation recover the SAME value for sxx=S and for the
     same state rotated 45 deg? (rotation invariance - the property that
     defines a real retardance)
  2. does it stay 0 for the three zero-retardance fields?
  3. does it reproduce the closed form for uniaxial AND for pure shear, whose
     retardance is 2x?

S is chosen so every arm stays below pi, where arg4=1 was shown to be exact -
testing a recipe in the regime where its ingredient is already known to
saturate would confound two things.

Writes retorient.json.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
WRK = os.path.join(HERE, "orientarms")
LAM_D = 0.5875618e-3
SURF = 1
KDIFF, CT, RF, RB = 4.5e-6, 4.00000, 11.00271, -83.76109
S = 5.0                      # uniaxial 0.962 rad, shear 1.925 rad - both < pi
out = {}
os.makedirs(WRK, exist_ok=True)


def sag(R, r):
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


T0 = CT - sag(RF, np.array([0.0]))[0] + sag(RB, np.array([0.0]))[0]
CF1 = 2 * math.pi * KDIFF * S * T0 / LAM_D          # uniaxial, at the axis

ARMS = [
    ("null",        (0, 0, 0, 0),       0.0),
    ("hydrostatic", (1, 1, 1, 0),       0.0),
    ("biaxial",     (1, 1, 0, 0),       0.0),
    ("axial",       (0, 0, 1, 0),       0.0),
    ("uniaxial",    (1, 0, 0, 0),       1.0),
    ("shear",       (0, 0, 0, 1),       2.0),
    ("rot45",       (0.5, 0.5, 0, 0.5), 1.0),
    ("rot30",       (0.75, 0.25, 0, 0.4330127), 1.0),
]

SP = np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % SURF))


def write_arm(tag, c):
    p = SP.copy()
    o = np.zeros(len(p))
    p[:, 3] = o + c[0] * S
    p[:, 4] = o + c[1] * S
    p[:, 5] = o + c[2] * S
    p[:, 6] = o + c[3] * S
    p[:, 7] = o
    p[:, 8] = o
    pp = os.path.join(WRK, "%s.txt" % tag)
    np.savetxt(pp, p, fmt="%.9E", delimiter=" ")
    return pp


app = connect()
s = app.PrimarySystem


def load(path):
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    st = s.LDE.GetSurfaceAt(SURF).STARData.Stress
    try:
        st.FEAData.UnloadData()
    except Exception:
        pass
    st.SetDataIsLocal()
    st.SetWorkingWavelength(1)
    code = st.FEAData.ImportStress_1(path)
    st.Fits.Refit()
    st.Fits.ApplyStress()
    return st, int(code)


def peak(st, a3, a4):
    m = st.Fits.GetRetardanceMap(8, 0, 1, a3, a4, 0.0, 0.0)
    if m is None or len(m) == 0:
        return float("nan")
    return float(np.max(np.abs([float(q.Retardance) for q in m])))


PHI = np.arange(0.0, 180.0, 7.5)
print("=" * 78)
print("ORIENTATION SWEEP, surface %d, S = %.1f N/mm2" % (SURF, S))
print("closed form for the uniaxial arm at the axis: %.5f rad "
      "(path %.5f mm, lambda_d %.7f um)" % (CF1, T0, LAM_D * 1e3))
print("=" * 78)
print()
print("%-12s %11s %11s %11s %9s %9s" %
      ("arm", "closed rad", "arg4=1", "max over phi", "phi@max", "max/cf"))

res = {}
for tag, c, mult in ARMS:
    st, code = load(write_arm(tag, c))
    cf = CF1 * mult
    fixed = peak(st, 1.0, 1.0)
    vals = []
    for ph in PHI:
        vals.append(peak(st, math.cos(math.radians(ph)), math.sin(math.radians(ph))))
    vals = np.array(vals)
    i = int(np.nanargmax(vals))
    res[tag] = dict(closed=cf, arg4_1=fixed, max_over_phi=float(vals[i]),
                    phi_at_max=float(PHI[i]), curve=[float(v) for v in vals],
                    code=code, mult=mult)
    print("%-12s %11.5f %11.5f %11.5f %9.1f %9s"
          % (tag, cf, fixed, vals[i], PHI[i],
             ("%.4f" % (vals[i] / cf)) if cf > 1e-12 else "-"))
out["arms"] = res
out["phi_deg"] = [float(p) for p in PHI]
out["closed_uniaxial"] = CF1

print()
print("ROTATION INVARIANCE - the property that defines a retardance:")
u = res["uniaxial"]["max_over_phi"]
for tag in ("rot45", "rot30", "shear"):
    v = res[tag]["max_over_phi"]
    print("   %-10s max-over-phi %.5f   vs uniaxial %.5f   ratio %.4f  (expect %.1f)"
          % (tag, v, u, v / u if u else float("nan"), res[tag]["mult"]))
print()
print("and arg4=1 alone, for comparison:")
for tag in ("rot45", "rot30", "shear"):
    print("   %-10s arg4=1 %.5f   vs uniaxial %.5f   ratio %.4f"
          % (tag, res[tag]["arg4_1"], res["uniaxial"]["arg4_1"],
             res[tag]["arg4_1"] / res["uniaxial"]["arg4_1"]))

print()
print("the orientation curve for a few arms (rad vs phi deg):")
for tag in ("uniaxial", "rot45", "shear", "null"):
    print("   %-10s" % tag, " ".join("%6.3f" % v for v in res[tag]["curve"]))

json.dump(out, open(os.path.join(HERE, "retorient.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retorient.json")
print("done")

"""ITEM 1, part 6. Is there ANY route that returns a rotation-invariant
retardance?

GetRetardanceMap's arg3/arg4 turned out not to be a continuous orientation -
0.5 and 1.0 give identical answers, so they are flag-like - and with both set
the call is exact for uniaxial-along-x and reads ~0 for the SAME stress state
rotated 45 deg. Sweeping "orientation" therefore does not recover anything; it
just wanders between a correct branch and a broken one.

Retardance is a property of the medium. It cannot depend on how the stress
happens to be oriented in x-y. So the test that decides whether ANY usable
route exists is rotation invariance, applied to the one route that returns a
SCALAR local quantity:

    GetPointRetardanceList -> Retardance, established as local birefringence in
    rad/mm at the d-line (0.481215 rad/mm measured against 0.480847 closed
    form for sxx = 10 N/mm2, implied lambda 0.5875618 um to seven figures).

Expected local value per mm, at the d-line:

    2*pi*(K11-K12)*S*mult/lambda_d,   mult = 0,0,0,0,1,2,1,1 for the arms below

If that is right for shear (2x) and for rot45 and rot30 (1x, equal to
uniaxial), the tool has a usable route and the remedy is to integrate it. If it
is not, then no route here measures retardance for a general stress state and
the honest remedy is to REFUSE the number rather than to swap an argument.

Writes retpoint.json.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
WRK = os.path.join(HERE, "ptarms")
LAM_D = 0.5875618e-3
SURF = 1
KDIFF = 4.5e-6
S = 5.0
out = {}
os.makedirs(WRK, exist_ok=True)

ARMS = [
    ("null",        (0, 0, 0, 0),                 0.0),
    ("hydrostatic", (1, 1, 1, 0),                 0.0),
    ("biaxial",     (1, 1, 0, 0),                 0.0),
    ("axial",       (0, 0, 1, 0),                 0.0),
    ("uniaxial",    (1, 0, 0, 0),                 1.0),
    ("shear",       (0, 0, 0, 1),                 2.0),
    ("rot45",       (0.5, 0.5, 0, 0.5),           1.0),
    ("rot30",       (0.75, 0.25, 0, 0.4330127),   1.0),
    ("uniaxial_y",  (0, 1, 0, 0),                 1.0),
]

SP = np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % SURF))
app = connect()
s = app.PrimarySystem


def load(tag, c):
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
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    st = s.LDE.GetSurfaceAt(SURF).STARData.Stress
    try:
        st.FEAData.UnloadData()
    except Exception:
        pass
    st.SetDataIsLocal()
    st.SetWorkingWavelength(1)
    code = int(st.FEAData.ImportStress_1(pp))
    st.Fits.Refit()
    st.Fits.ApplyStress()
    return st, code


PER_MM = 2 * math.pi * KDIFF * S / LAM_D
print("=" * 78)
print("GetPointRetardanceList AS A LOCAL SCALAR, surface %d, S = %.1f N/mm2" % (SURF, S))
print("=" * 78)
print("expected local birefringence phase, d-line: %.6f rad/mm per unit multiplier"
      % PER_MM)
print()
print("%-13s %6s %11s %11s %11s %9s %9s" %
      ("arm", "mult", "expect", "median", "max", "med/exp", "invariant"))
rows = {}
uni = None
for tag, c, mult in ARMS:
    st, code = load(tag, c)
    try:
        pl = st.Fits.GetPointRetardanceList(8, 0, 1)
        v = np.array([float(q.Retardance) for q in pl])
    except Exception as ex:
        print("%-13s  raised %s" % (tag, ex))
        rows[tag] = dict(err=str(ex))
        continue
    exp = PER_MM * mult
    med, mx = float(np.median(v)), float(np.max(np.abs(v)))
    if tag == "uniaxial":
        uni = med
    rows[tag] = dict(mult=mult, expect=exp, median=med, max=mx, n=len(v), code=code)
    print("%-13s %6.1f %11.6f %11.6f %11.6f %9s %9s"
          % (tag, mult, exp, med, mx,
             ("%.4f" % (med / exp)) if exp > 1e-12 else "-",
             ("%.4f" % (med / uni)) if uni else "-"))
out["arms"] = rows
out["per_mm_unit"] = PER_MM

print()
print("ROTATION INVARIANCE (all of these are the SAME physical state, rotated):")
if uni:
    for tag in ("uniaxial", "uniaxial_y", "rot45", "rot30"):
        if tag in rows and "median" in rows[tag]:
            print("   %-12s median %.6f rad/mm   ratio to uniaxial-x %.4f  (must be 1.0000)"
                  % (tag, rows[tag]["median"], rows[tag]["median"] / uni))
    if "shear" in rows and "median" in rows["shear"]:
        print("   %-12s median %.6f rad/mm   ratio to uniaxial-x %.4f  (must be 2.0000)"
              % ("shear", rows["shear"]["median"], rows["shear"]["median"] / uni))
    print()
    print("ZERO-RETARDANCE ARMS (must all be 0):")
    for tag in ("null", "hydrostatic", "biaxial", "axial"):
        if tag in rows and "median" in rows[tag]:
            print("   %-12s median %.6f rad/mm   max %.6f   (%.2f%% of uniaxial)"
                  % (tag, rows[tag]["median"], rows[tag]["max"],
                     100 * rows[tag]["median"] / uni))

json.dump(out, open(os.path.join(HERE, "retpoint.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retpoint.json")
print("done")

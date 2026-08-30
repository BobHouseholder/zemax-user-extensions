"""ITEM 1, part 4. The fix, and six closed forms to test it against.

What retdump.py established:

  * GetRetardanceMap(..., arg4 = 0.0) - what Runner.cs ships - returns SEVERAL
    values at each (x,y), and they are exactly pi apart: at r = 0.3930 it
    returns -2.703674, +0.437919, +0.437919, -2.703674, 0.000000. max|.| over
    that is not a spatial peak of anything.
  * its Z column tracks the BACK surface sag over a 0.06 mm range, so it is not
    a projection through the element either.
  * GetPointRetardanceList returns 0.481215 rad essentially constant over the
    whole volume of a UNIFORM field - a per-unit-length quantity. Solving
    2*pi*dn*L/lambda = 0.481215 with L = 1 mm gives lambda = 0.58755 um, the
    d-line to five figures. So it is local birefringence in rad/mm AT THE
    d-LINE, whatever the working wavelength is set to.
  * setting arg4 = 1.0 returned -1.924859 rad where the closed form for the
    integrated retardance at the d-line is 1.92339 rad. 0.08%.

So the hypothesis is that arg4 selects integrated-vs-local, and the tool ships
the wrong one. One number agreeing once is not evidence. This tests it against
SIX closed forms, three of which are non-trivial zeros:

  null          all zero                     -> 0
  hydrostatic   sxx=syy=szz=S                -> 0   (no transverse difference)
  biaxial       sxx=syy=S, szz=0             -> 0   (von Mises = S, so this
                                                     separates correct tensor
                                                     handling from a scalar)
  axial         szz=S only                   -> 0
  uniaxial      sxx=S                        -> kdiff*S*t(r)
  shear         sxy=S                        -> kdiff*2S*t(r)   (principal
                                                     stresses +-S)
  rot45         sxx=syy=S/2, sxy=S/2         -> kdiff*S*t(r), IDENTICAL to
                                                     uniaxial - rotation
                                                     invariance

and then a stress LADDER on the uniaxial arm over four decades, because a fix
that works at one magnitude and not another is not a fix.

Writes retfix.json.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
WRK = os.path.join(HERE, "fixarms")
LAM_D = 0.5875618e-3          # mm - what the point list turned out to use
ELEMS = {
    1: dict(mat="MS_PMMA", kdiff=4.5e-6, ct=4.00000, rf=11.00271, rb=-83.76109),
    3: dict(mat="MS_POLYSTYR", kdiff=1.0e-5, ct=1.20000, rf=-13.97735, rb=9.00465),
    5: dict(mat="MS_PMMA", kdiff=4.5e-6, ct=4.00000, rf=24.77427, rb=-11.70778),
}
SHIPPED = (8, 0, 1, 1.0, 0.0, 0.0, 0.0)
FIXED = (8, 0, 1, 1.0, 1.0, 0.0, 0.0)
S = 10.0
out = {}
os.makedirs(WRK, exist_ok=True)


def banner(t):
    print()
    print("=" * 78)
    print(t)
    print("=" * 78)


def sag(R, r):
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


def path(f, r):
    e = ELEMS[f]
    return e["ct"] - sag(e["rf"], r) + sag(e["rb"], r)


def peak_path(f):
    e = ELEMS[f]
    rmax = np.hypot(SP[f][:, 0], SP[f][:, 1]).max()
    rg = np.linspace(0, rmax, 4001)
    t = path(f, rg)
    i = int(np.argmax(t))
    return float(t[i]), float(rg[i])


SP = {f: np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % f)) for f in ELEMS}

# ------------------------------------------------------------------- arms
# each entry: name -> (sxx, syy, szz, sxy, expected dn multiplier of kdiff*S)
ARMS = [
    ("null",        (0, 0, 0, 0),                 0.0),
    ("hydrostatic", (1, 1, 1, 0),                 0.0),
    ("biaxial",     (1, 1, 0, 0),                 0.0),
    ("axial",       (0, 0, 1, 0),                 0.0),
    ("uniaxial",    (1, 0, 0, 0),                 1.0),
    ("shear",       (0, 0, 0, 1),                 2.0),
    ("rot45",       (0.5, 0.5, 0, 0.5),           1.0),
]


def write_arm(tag, comps, scale=S):
    d = os.path.join(WRK, tag)
    os.makedirs(d, exist_ok=True)
    paths = {}
    for f in ELEMS:
        p = SP[f].copy()
        o = np.zeros(len(p))
        p[:, 3] = o + comps[0] * scale
        p[:, 4] = o + comps[1] * scale
        p[:, 5] = o + comps[2] * scale
        p[:, 6] = o + comps[3] * scale
        p[:, 7] = o
        p[:, 8] = o
        pp = os.path.join(d, "a_s%d.txt" % f)
        np.savetxt(pp, p, fmt="%.9E", delimiter=" ")
        paths[f] = pp
    return paths


app = connect()
s = app.PrimarySystem


def load(paths, workwave=1):
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    codes = {}
    for f, pth in paths.items():
        st = s.LDE.GetSurfaceAt(f).STARData.Stress
        try:
            st.FEAData.UnloadData()
        except Exception:
            pass
        st.SetDataIsLocal()
        st.SetWorkingWavelength(workwave)
        codes[f] = (int(st.FEAData.ImportStress_1(pth)),
                    int(st.FEAData.NumberOfDataPoints))
        st.Fits.Refit()
        st.Fits.ApplyStress()
    return codes


def peak(f, args):
    st = s.LDE.GetSurfaceAt(f).STARData.Stress
    try:
        m = st.Fits.GetRetardanceMap(*args)
    except Exception as ex:
        return None, str(ex), None
    if m is None or len(m) == 0:
        return None, "empty", None
    r = np.array([float(q.Retardance) for q in m])
    x = np.array([float(q.X) for q in m])
    y = np.array([float(q.Y) for q in m])
    i = int(np.argmax(np.abs(r)))
    return float(abs(r[i])), None, float(np.hypot(x[i], y[i]))


banner("A. SEVEN TENSOR ARMS, SHIPPED CALL vs arg4 = 1.0")
print("uniform fields at S = %.1f N/mm2; closed form uses lambda_d = %.7f um" %
      (S, LAM_D * 1e3))
print()
print("%-12s %-4s %11s %11s %9s %11s %9s" %
      ("arm", "surf", "closed rad", "shipped", "ship/cf", "arg4=1", "fix/cf"))
arm_rows = {}
for tag, comps, mult in ARMS:
    paths = write_arm(tag, comps)
    load(paths)
    for f in ELEMS:
        tpk, rpk = peak_path(f)
        cf = 2 * math.pi * ELEMS[f]["kdiff"] * S * mult * tpk / LAM_D
        a, ea, ra = peak(f, SHIPPED)
        b, eb, rb_ = peak(f, FIXED)
        arm_rows.setdefault(tag, {})[str(f)] = dict(
            closed=cf, shipped=a, shipped_err=ea, shipped_r=ra,
            fixed=b, fixed_err=eb, fixed_r=rb_, mult=mult, t_peak=tpk, r_peak=rpk)
        def rat(v):
            if v is None:
                return float("nan")
            return v / cf if cf > 1e-12 else v      # for cf = 0 report the raw
        print("%-12s %-4d %11.5f %11s %9.4f %11s %9.4f"
              % (tag, f, cf,
                 ("%.5f" % a) if a is not None else ea,
                 rat(a), ("%.5f" % b) if b is not None else eb, rat(b)))
out["arms"] = arm_rows

banner("B. STRESS LADDER, UNIAXIAL, SURFACE 1")
print("%9s %12s %12s %9s %12s %9s" %
      ("S MPa", "closed rad", "shipped", "ship/cf", "arg4=1", "fix/cf"))
lad = {}
for Sv in (0.0, 0.02, 0.1, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 200.0):
    paths = write_arm("lad", (1, 0, 0, 0), scale=Sv)
    load(paths)
    tpk, _ = peak_path(1)
    cf = 2 * math.pi * ELEMS[1]["kdiff"] * Sv * tpk / LAM_D
    a, ea, _ = peak(1, SHIPPED)
    b, eb, _ = peak(1, FIXED)
    lad["%.4f" % Sv] = dict(closed=cf, shipped=a, fixed=b)
    print("%9.2f %12.5f %12s %9.4f %12s %9.4f"
          % (Sv, cf, ("%.5f" % a) if a is not None else ea,
             (a / cf) if (a is not None and cf > 1e-12) else float("nan"),
             ("%.5f" % b) if b is not None else eb,
             (b / cf) if (b is not None and cf > 1e-12) else float("nan")))
out["ladder"] = lad

banner("C. WHICH WAVELENGTH DOES THE FIXED CALL REPORT AT?")
print("Runner.cs converts the peak to nm with wavelength 1 (%.6f um)."
      % float(s.SystemData.Wavelengths.GetWavelength(1).Wavelength))
print("If the map is fixed at the d-line, that conversion is wrong by the ratio.")
print()
paths = write_arm("wave", (1, 0, 0, 0))
wv = {}
for w in (1, 2, 3):
    load(paths, workwave=w)
    b, eb, _ = peak(1, FIXED)
    lam = float(s.SystemData.Wavelengths.GetWavelength(w).Wavelength) * 1e-3
    tpk, _ = peak_path(1)
    cf_w = 2 * math.pi * ELEMS[1]["kdiff"] * S * tpk / lam
    cf_d = 2 * math.pi * ELEMS[1]["kdiff"] * S * tpk / LAM_D
    wv[str(w)] = dict(lam_um=lam * 1e3, measured=b, cf_at_this_wave=cf_w, cf_at_d=cf_d)
    print("  working wavelength %d = %.6f um -> measured %.5f rad; "
          "closed form at THAT wavelength %.5f, at the d-line %.5f"
          % (w, lam * 1e3, b if b else float("nan"), cf_w, cf_d))
out["wavelength"] = wv

banner("D. THE REAL MOULDING FIELD, BOTH WAYS")
real = {f: os.path.join(REAL, "moldstress_s%d_stress.txt" % f) for f in ELEMS}
load(real)
rr = {}
for f in ELEMS:
    a, ea, ra = peak(f, SHIPPED)
    b, eb, rb_ = peak(f, FIXED)
    rr[str(f)] = dict(shipped=a, fixed=b, shipped_r=ra, fixed_r=rb_)
    print("  surface %d:  shipped %s waves (r=%s)   arg4=1 %s waves (r=%s)"
          % (f,
             ("%.5f" % (a / (2 * math.pi))) if a else ea,
             ("%.3f" % ra) if ra is not None else "-",
             ("%.5f" % (b / (2 * math.pi))) if b else eb,
             ("%.3f" % rb_) if rb_ is not None else "-"))
out["real_field"] = rr

json.dump(out, open(os.path.join(HERE, "retfix.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retfix.json")
print("done")

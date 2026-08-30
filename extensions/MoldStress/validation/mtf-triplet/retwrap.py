"""ITEM 1, part 2. Is the retardance map WRAPPED, and where does it stop being
a measurement?

retctl.py found that a stress-FREE element returns peak |retardance| of exactly
pi or 2*pi, that a hydrostatic field (10 MPa, zero birefringence by symmetry)
returns the same, and that 1 / 10 / 100 MPa uniaxial return 0.464 / 0.430 /
0.982 waves where the closed form says 0.037 / 0.370 / 3.704. Non-monotonic,
saturating near one wave.

The hypothesis is that pt.Retardance is a phase WRAPPED into [-2pi, 2pi], so
max|R| over the map saturates at 2*pi as soon as any point in the field exceeds
a wave - and on a null field lands on the wrap boundary.

A hypothesis that only explains failures is worth little. This tests the other
direction too: at low enough stress NOTHING can wrap, and there the map must
reproduce the closed form exactly. That is the arm that says the instrument
works inside a regime and names the regime's edge.

  LADDER      S = 0.02 .. 200 MPa uniaxial on surface 1 (PMMA, centre-peaked,
              so the closed-form peak sits at r = 0 where the map definitely
              samples - no edge question mixed in).
  MATCHED     every arm read at the SAME map point, the one nearest r = 0,
              as well as at its own argmax. Gates section A: two variants read
              at their own extrema are not the same measurement, and here the
              argmax MOVES between arms.
  IMPORT      the import code and accepted point count for every arm, because
              "STAR returned junk on a zero field" and "STAR never received the
              zero field" are different findings and only one is about STAR.

Writes retwrap.json.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
LAD = os.path.join(HERE, "ladder")
SURF = 1
KDIFF = 4.5e-6            # MS_PMMA, K11-K12, from the generated AGF
CT = 4.00000
RF, RB = 11.00271, -83.76109
out = {}


def banner(t):
    print()
    print("=" * 78)
    print(t)
    print("=" * 78)


def sag(R, r):
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


def path(r):
    return CT - sag(RF, r) + sag(RB, r)


SPTS = np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % SURF))
os.makedirs(LAD, exist_ok=True)


def write_uniaxial(S):
    p = SPTS.copy()
    o = np.zeros(len(p))
    p[:, 3] = o + S      # sxx
    p[:, 4] = o          # syy
    p[:, 5] = o          # szz
    p[:, 6] = o          # sxy
    p[:, 7] = o
    p[:, 8] = o
    path_ = os.path.join(LAD, "lad_%09.4f.txt" % S)
    np.savetxt(path_, p, fmt="%.9E", delimiter=" ")
    return path_


app = connect()
s = app.PrimarySystem
LAM_UM = None


def load_uniaxial(S):
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    st = s.LDE.GetSurfaceAt(SURF).STARData.Stress
    try:
        st.FEAData.UnloadData()
    except Exception:
        pass
    st.SetDataIsLocal()
    st.SetWorkingWavelength(1)
    code = st.FEAData.ImportStress_1(write_uniaxial(S))
    n = st.FEAData.NumberOfDataPoints
    st.Fits.Refit()
    st.Fits.ApplyStress()
    return st, int(code), int(n)


def read_map(st):
    m = st.Fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0)
    if m is None or len(m) == 0:
        return None
    x = np.array([float(p.X) for p in m])
    y = np.array([float(p.Y) for p in m])
    r = np.array([float(p.Retardance) for p in m])
    return x, y, r


# ------------------------------------------------ what does a point carry?
banner("A. WHAT A MAP POINT ACTUALLY CARRIES")
st, code, n = load_uniaxial(10.0)
LAM_UM = float(s.SystemData.Wavelengths.GetWavelength(1).Wavelength)
print("working wavelength (index 1) = %.6f um" % LAM_UM)
print("import code %d, %d points accepted" % (code, n))
m = st.Fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0)
pt = m[0]
print("point type:", type(pt))
props = [p for p in dir(pt) if not p.startswith("_") and p[0].isupper()]
for p in props:
    try:
        print("   %-22s %s" % (p, getattr(pt, p)))
    except Exception as ex:
        print("   %-22s <%s>" % (p, ex))
out["point_properties"] = props

print()
print("GetPointRetardanceList(8,0,1):")
try:
    pl = st.Fits.GetPointRetardanceList(8, 0, 1)
    print("   %d points; type %s" % (len(pl), type(pl[0])))
    q = pl[0]
    for p in [x for x in dir(q) if not x.startswith("_") and x[0].isupper()]:
        try:
            print("      %-22s %s" % (p, getattr(q, p)))
        except Exception as ex:
            print("      %-22s <%s>" % (p, ex))
except Exception as ex:
    print("   raised:", ex)

# --------------------------------------------------------------- the ladder
banner("B. THE STRESS LADDER")
print("closed form: peak = (K11-K12)*S*t(0)/lambda, at r = 0")
print("             t(0) = %.5f mm, lambda = %.6f um, K11-K12 = %.3e"
      % (path(np.array([0.0]))[0], LAM_UM, KDIFF))
print()
print("%9s %5s %7s %11s %11s %11s %9s %9s %7s"
      % ("S MPa", "code", "npts", "closed wv", "own-max wv", "at r~0 wv",
         "own/cf", "r@max", "%at wrap"))

ladder = {}
S_LIST = [0.0, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0,
          20.0, 50.0, 100.0, 200.0]
for S in S_LIST:
    st, code, n = load_uniaxial(S)
    got = read_map(st)
    if got is None:
        print("%9.2f %5d %7d   EMPTY MAP" % (S, code, n))
        ladder["%.4f" % S] = dict(code=code, npts=n, empty=True)
        continue
    x, y, r = got
    rr = np.hypot(x, y)
    cf_waves = KDIFF * S * float(path(np.array([0.0]))[0]) / (LAM_UM * 1e-3)
    i = int(np.argmax(np.abs(r)))
    j = int(np.argmin(rr))                      # MATCHED point, nearest r = 0
    own = abs(r[i]) / (2 * math.pi)
    at0 = abs(r[j]) / (2 * math.pi)
    # how much of the map is sitting on a wrap boundary?
    near_wrap = float(np.mean(
        (np.abs(np.abs(r) - math.pi) < 1e-4) | (np.abs(np.abs(r) - 2 * math.pi) < 1e-4)))
    ladder["%.4f" % S] = dict(
        code=code, npts=n, mappts=len(r), closed_waves=cf_waves,
        own_max_waves=own, at_axis_waves=at0, r_at_max=float(rr[i]),
        r_at_matched=float(rr[j]), frac_on_wrap=near_wrap,
        raw_min=float(r.min()), raw_max=float(r.max()))
    print("%9.2f %5d %7d %11.5f %11.5f %11.5f %9.4f %9.4f %7.1f%%"
          % (S, code, n, cf_waves, own, at0,
             (own / cf_waves) if cf_waves > 1e-12 else float("nan"),
             rr[i], 100 * near_wrap))
out["ladder"] = ladder
out["lambda_um"] = LAM_UM
out["closed_form_t0_mm"] = float(path(np.array([0.0]))[0])

# ------------------------------------------- is the low-stress arm EXACT?
banner("C. THE VALID REGIME")
ok = [(float(k), v) for k, v in ladder.items()
      if not v.get("empty") and v["closed_waves"] > 1e-9]
ok.sort()
print("ratio of the MATCHED (r~0) reading to the closed form:")
for S, v in ok:
    ratio = v["at_axis_waves"] / v["closed_waves"]
    flag = "  <- agrees" if abs(ratio - 1.0) < 0.02 else ""
    print("   S = %8.2f MPa   closed %8.5f wv   read %8.5f wv   ratio %8.4f%s"
          % (S, v["closed_waves"], v["at_axis_waves"], ratio, flag))
good = [S for S, v in ok if abs(v["at_axis_waves"] / v["closed_waves"] - 1.0) < 0.02]
out["agreeing_stresses_MPa"] = good
if good:
    print()
    print("agrees within 2%% up to S = %.2f MPa, i.e. a peak retardance of %.4f waves"
          % (max(good), ladder["%.4f" % max(good)]["closed_waves"]))

json.dump(out, open(os.path.join(HERE, "retwrap.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retwrap.json")
print("done")

"""ITEM 1, part 3. Stop inferring what the map IS and read it.

The ladder showed three things that do not fit one story:

  - GetRetardanceMap reads exactly 0.00000 waves at the axis at EVERY stress,
    including 0.5 MPa where the closed form says 0.01851 waves;
  - its max sits at the outermost sampled radius at every stress and equals
    pi MINUS the closed form, to about 2% (0.5 - own_max is exactly linear in
    S and matches the closed form: 0.00076/0.00074, 0.00377/0.00370,
    0.00754/0.00741, 0.01868/0.01851 waves);
  - GetPointRetardanceList reports 0.481215 rad at the SAME axial point where
    the map reports 0.

Two routes into the same fitted stress field disagreeing at the same
coordinate is an instrument question, and the cheapest way to settle it is to
dump both and look at the structure rather than fit a story to three numbers.

Uniform uniaxial 10 MPa on surface 1, where retardance is known everywhere:
    R(x,y,z) = 2*pi * (K11-K12) * S * L / lambda
with L whatever path STAR is integrating. Solving for L at every point tells us
what STAR thinks the path is, and L is the thing all three oddities are about.

Writes retdump.json and dumps the raw arrays.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
SURF = 1
S = 10.0
KDIFF = 4.5e-6
CT, RF, RB = 4.00000, 11.00271, -83.76109
out = {}


def sag(R, r):
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False)
s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
LAM = float(s.SystemData.Wavelengths.GetWavelength(1).Wavelength) * 1e-3   # mm

p = np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % SURF))
p[:, 3] = S
p[:, 4:9] = 0.0
tmp = os.path.join(HERE, "dump_stress.txt")
np.savetxt(tmp, p, fmt="%.9E", delimiter=" ")

st = s.LDE.GetSurfaceAt(SURF).STARData.Stress
try:
    st.FEAData.UnloadData()
except Exception:
    pass
st.SetDataIsLocal()
st.SetWorkingWavelength(1)
code = st.FEAData.ImportStress_1(tmp)
npts = st.FEAData.NumberOfDataPoints
st.Fits.Refit()
st.Fits.ApplyStress()
print("import code %d, %d points, lambda %.6f mm" % (code, npts, LAM))
print("uniform uniaxial sxx = %.1f N/mm2 -> dn = %.4e" % (S, KDIFF * S))
print("so retardance R(rad) = 2*pi*dn*L/lambda  =>  L(mm) = R*lambda/(2*pi*dn)")
DN = KDIFF * S
print()

# ------------------------------------------------------------------ the map
m = st.Fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0)
M = np.array([[float(q.X), float(q.Y), float(q.Z), float(q.Retardance)] for q in m])
np.savetxt(os.path.join(HERE, "dump_map.txt"), M, fmt="%.9E")
r = np.hypot(M[:, 0], M[:, 1])
L_implied = M[:, 3] * LAM / (2 * math.pi * DN)
geom = CT - sag(RF, r) + sag(RB, r)

print("=" * 78)
print("GetRetardanceMap(8,0,1, 1,0,0,0):  %d points" % len(M))
print("=" * 78)
print("  X range   %+.4f .. %+.4f" % (M[:, 0].min(), M[:, 0].max()))
print("  Y range   %+.4f .. %+.4f" % (M[:, 1].min(), M[:, 1].max()))
print("  Z range   %+.4f .. %+.4f   (%d distinct)"
      % (M[:, 2].min(), M[:, 2].max(), len(np.unique(np.round(M[:, 2], 9)))))
print("  r range   %+.4f .. %+.4f" % (r.min(), r.max()))
print("  R range   %+.6f .. %+.6f rad" % (M[:, 3].min(), M[:, 3].max()))
print()
print("  %8s %9s %9s %13s %11s %11s" %
      ("r", "Z", "R rad", "R waves", "L implied", "L geometric"))
order = np.argsort(r)
for i in list(order[:6]) + list(order[len(order) // 2 - 3:len(order) // 2 + 3]) + list(order[-6:]):
    print("  %8.4f %9.4f %9.6f %13.6f %11.5f %11.5f"
          % (r[i], M[i, 2], M[i, 3], M[i, 3] / (2 * math.pi),
             L_implied[i], geom[i]))
out["map"] = dict(n=len(M), zmin=float(M[:, 2].min()), zmax=float(M[:, 2].max()),
                  rmin=float(r.min()), rmax=float(r.max()),
                  Rmin=float(M[:, 3].min()), Rmax=float(M[:, 3].max()))

# does pi - R fit the geometric path?
alt = (math.pi - np.abs(M[:, 3])) * LAM / (2 * math.pi * DN)
print()
print("  IS THE MAP REPORTING (pi - R)?  compare L implied by (pi-|R|):")
print("  %8s %11s %11s %9s" % ("r", "L from pi-R", "L geometric", "ratio"))
for i in list(order[:4]) + list(order[-4:]):
    print("  %8.4f %11.5f %11.5f %9.4f"
          % (r[i], alt[i], geom[i], alt[i] / geom[i] if geom[i] else float("nan")))
resid = np.abs(alt - geom)
print("  median |L(pi-R) - L_geom| = %.5f mm over %d points  (element CT %.3f mm)"
      % (float(np.median(resid)), len(M), CT))
out["pi_minus_R_median_path_error_mm"] = float(np.median(resid))

# --------------------------------------------------------------- point list
pl = st.Fits.GetPointRetardanceList(8, 0, 1)
P = np.array([[float(q.X), float(q.Y), float(q.Z), float(q.Retardance), float(q.Index)]
              for q in pl])
np.savetxt(os.path.join(HERE, "dump_pointlist.txt"), P, fmt="%.9E")
rp = np.hypot(P[:, 0], P[:, 1])
Lp = P[:, 3] * LAM / (2 * math.pi * DN)
geomp = CT - sag(RF, rp) + sag(RB, rp)
print()
print("=" * 78)
print("GetPointRetardanceList(8,0,1):  %d points" % len(P))
print("=" * 78)
print("  Z range   %+.4f .. %+.4f   (%d distinct)"
      % (P[:, 2].min(), P[:, 2].max(), len(np.unique(np.round(P[:, 2], 9)))))
print("  r range   %+.4f .. %+.4f" % (rp.min(), rp.max()))
print("  R range   %+.6f .. %+.6f rad" % (P[:, 3].min(), P[:, 3].max()))
print()
print("  %8s %9s %9s %13s %11s %11s %11s" %
      ("r", "Z", "R rad", "R waves", "L implied", "L geometric", "index"))
op = np.argsort(rp)
for i in list(op[:6]) + list(op[len(op) // 2 - 3:len(op) // 2 + 3]) + list(op[-6:]):
    print("  %8.4f %9.4f %9.6f %13.6f %11.5f %11.5f %11.7f"
          % (rp[i], P[i, 2], P[i, 3], P[i, 3] / (2 * math.pi),
             Lp[i], geomp[i], P[i, 4]))
out["pointlist"] = dict(n=len(P), Rmin=float(P[:, 3].min()), Rmax=float(P[:, 3].max()),
                        zmin=float(P[:, 2].min()), zmax=float(P[:, 2].max()))

# is the point list's implied path proportional to something recognisable?
print()
print("  L implied / L geometric over the point list:")
ratio = Lp / np.where(geomp > 1e-9, geomp, np.nan)
print("     min %.5f  median %.5f  max %.5f"
      % (np.nanmin(ratio), np.nanmedian(ratio), np.nanmax(ratio)))
print("  L implied / Z:")
rz = Lp / np.where(np.abs(P[:, 2]) > 1e-9, P[:, 2], np.nan)
print("     min %.5f  median %.5f  max %.5f"
      % (np.nanmin(rz), np.nanmedian(rz), np.nanmax(rz)))
out["pointlist_L_over_geom_median"] = float(np.nanmedian(ratio))
out["pointlist_L_over_Z_median"] = float(np.nanmedian(rz))

# ----------------------------------------- GetRetardance, argument by argument
print()
print("=" * 78)
print("GetRetardance(a,b,c,d,e,f,g) - what moves it?")
print("=" * 78)
base = [0.0, 0.0, 2.0, 0.0, 0.0, 1.0, 1.0]
print("  base call %s -> %.6f rad" % (base, st.Fits.GetRetardance(*base)))
probe = {}
for pos, cands in ((0, [0.0, 1.0, 2.0]), (1, [0.0, 1.0]), (2, [0.0, 1.0, 2.0, 4.0]),
                   (3, [0.0, 1.0]), (4, [0.0, 1.0]), (5, [0.0, 1.0]),
                   (6, [0.486133, 0.5, 1.0, 2.0])):
    row = []
    for c in cands:
        a = list(base)
        a[pos] = c
        try:
            v = float(st.Fits.GetRetardance(*a))
        except Exception as ex:
            v = str(ex)
        row.append((c, v))
    probe[str(pos)] = row
    print("  arg %d: %s" % (pos, ", ".join(
        "%.4g->%s" % (c, ("%.6f" % v) if isinstance(v, float) else "ERR")
        for c, v in row)))
out["get_retardance_probe"] = probe

json.dump(out, open(os.path.join(HERE, "retdump.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retdump.json, dump_map.txt, dump_pointlist.txt")
print("done")

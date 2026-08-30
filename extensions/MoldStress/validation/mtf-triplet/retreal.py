"""ITEM 1, final. The real moulding field through the route that passed the
controls, and what the published 585x ratio actually is.

GetPointRetardanceList is the only route that survived: exact zeros on the null
and hydrostatic arms, rotation-invariant to 1.0000 across four orientations of
one stress state, and 1.9976 for pure shear where theory demands 2. It reports
LOCAL birefringence in rad/mm at the d-line, so retardance needs the path, and
the longest path gives a bound rather than a peak.

The denominator is re-measured on the same footing: RMS wavefront at a PINNED
image plane. The index half of this study established that the file's focus
solve chases a paraxial shift real rays do not follow, and an unpinned
denominator would import that artifact straight into the ratio.

Writes retreal.json.
"""
import json
import math
import os

import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
LAM_D_MM = 0.5875618e-3
SURFS = [1, 3, 5]
# centre thickness and the two radii, from the LDE
GEO = {
    1: dict(ct=4.00000, rf=11.00271, rb=-83.76109, sd=5.67565),
    3: dict(ct=1.20000, rf=-13.97735, rb=9.00465, sd=2.64022),
    5: dict(ct=4.00000, rf=24.77427, rb=-11.70778, sd=4.27419),
}
out = {}


def sag(R, r):
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


def max_path(f):
    g = GEO[f]
    r = np.linspace(0.0, g["sd"], 2001)
    t = g["ct"] - sag(g["rf"], r) + sag(g["rb"], r)
    i = int(np.argmax(t))
    return float(t[i]), float(r[i])


app = connect()
s = app.PrimarySystem
NS = IMGPREV = None


def fresh():
    global NS, IMGPREV
    assert s.LoadFile(BASE, False)
    NS = s.LDE.NumberOfSurfaces
    IMGPREV = NS - 2
    s.LDE.GetSurfaceAt(IMGPREV).ThicknessCell.MakeSolveFixed()


def wfe(wave=1):
    return s.MFE.GetOperandValue(E.RWRE, 4, wave, 0, 0, 0, 0, 0, 0)


def load_stress():
    got = {}
    for f in SURFS:
        pth = os.path.join(REAL, "moldstress_s%d_stress.txt" % f)
        st = s.LDE.GetSurfaceAt(f).STARData.Stress
        try:
            st.FEAData.UnloadData()
        except Exception:
            pass
        st.SetDataIsLocal()
        st.SetWorkingWavelength(1)
        code = int(st.FEAData.ImportStress_1(pth))
        n = int(st.FEAData.NumberOfDataPoints)
        st.Fits.Refit()
        st.Fits.ApplyStress()
        got[f] = (code, n)
    return got


print("=" * 78)
print("A. THE REAL FIELD THROUGH THE ROUTE THAT PASSED THE CONTROLS")
print("=" * 78)
fresh()
base = wfe()
plane0 = float(s.LDE.GetSurfaceAt(IMGPREV).Thickness)
got = load_stress()
plane1 = float(s.LDE.GetSurfaceAt(IMGPREV).Thickness)
loaded = wfe()

print("%-5s %8s %14s %10s %9s %13s %11s" %
      ("surf", "points", "biref rad/mm", "max path", "r@path", "bound waves", "bound nm"))
per = {}
best = (0.0, None)
for f in SURFS:
    st = s.LDE.GetSurfaceAt(f).STARData.Stress
    pl = st.Fits.GetPointRetardanceList(8, 0, 1)
    v = np.abs([float(q.Retardance) for q in pl])
    bl = float(v.max())
    tmax, rat = max_path(f)
    waves = bl * tmax / (2 * math.pi)
    nm = waves * LAM_D_MM * 1e6
    per[str(f)] = dict(biref_rad_per_mm=bl, npts=len(v), max_path_mm=tmax,
                       r_at_path=rat, bound_waves=waves, bound_nm=nm,
                       imported=got[f])
    if waves > best[0]:
        best = (waves, f)
    print("%-5d %8d %14.6f %10.4f %9.3f %13.5f %11.1f"
          % (f, len(v), bl, tmax, rat, waves, nm))
out["per_surface"] = per
peak_waves, peak_surf = best
print()
print("PEAK RETARDANCE BOUND: at most %.5f waves (%.1f nm at the d-line) on surface %d"
      % (peak_waves, peak_waves * LAM_D_MM * 1e6, peak_surf))
out["peak_bound_waves"] = peak_waves
out["peak_surface"] = peak_surf

print()
print("=" * 78)
print("B. WHAT THE OLD CALL SAID ABOUT THE SAME FIELD")
print("=" * 78)
old = {}
for f in SURFS:
    st = s.LDE.GetSurfaceAt(f).STARData.Stress
    m = st.Fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0)
    v = np.abs([float(q.Retardance) for q in m])
    old[str(f)] = float(v.max()) / (2 * math.pi)
    print("  surface %d: old %.5f waves   new bound %.5f waves   old/new %.2f"
          % (f, old[str(f)], per[str(f)]["bound_waves"],
             old[str(f)] / per[str(f)]["bound_waves"]))
out["old_call_waves"] = old
old_peak = max(old.values())
print("  the headline would have been %.5f waves; the bound is %.5f waves"
      % (old_peak, peak_waves))

print()
print("=" * 78)
print("C. THE DENOMINATOR - RMS WAVEFRONT AT A PINNED PLANE")
print("=" * 78)
d_abs = loaded - base
print("  baseline           %.6f waves" % base)
print("  stress applied     %.6f waves" % loaded)
print("  change             %+.6f waves  (%+.3f%%)" % (d_abs, 100 * d_abs / base))
print("  image plane        %.6f -> %.6f mm  (%s)"
      % (plane0, plane1, "pinned" if abs(plane1 - plane0) < 1e-9 else "MOVED"))
out.update(wfe_base=base, wfe_loaded=loaded, wfe_delta=d_abs,
           plane_move_mm=plane1 - plane0)

print()
print("=" * 78)
print("D. THE RATIO")
print("=" * 78)
print("  published:   0.41 waves / +0.5%%  ->  585x")
print("  old call:    %.5f / %.6f = %.0fx" % (old_peak, abs(d_abs), old_peak / abs(d_abs)))
print("  bound:       %.5f / %.6f = %.0fx" % (peak_waves, abs(d_abs), peak_waves / abs(d_abs)))
out["ratio_old"] = old_peak / abs(d_abs)
out["ratio_bound"] = peak_waves / abs(d_abs)

json.dump(out, open(os.path.join(HERE, "retreal.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retreal.json")
print("done")

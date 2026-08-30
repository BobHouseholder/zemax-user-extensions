"""ITEM 3. What did STAR actually read back?

Every STAR defect this project has found was found BY HAND, one at a time:

  * the direct-index route applies one index at EVERY wavelength and discards
    the material's dispersion (2026-08-29);
  * GetRetardanceMap returns pi on a stress-FREE element and is an angle, not a
    phase (2026-08-29);
  * the paraxial response to a direct-index cloud has a noise floor below a peak
    dn of ~1e-6, where it changes SIGN (2026-08-29).

Not one of them would be caught by any automated check in this repo, because
they all live in the gap between what the tool WRITES and what OpticStudio does
with it. The self-tests check the physics and need no session; the reference
cases check the model against literature. Nothing checks the interface.

This does. It writes a field whose correct read-back is known, reads it back
through STAR's own accessors, and compares. It is a round trip, so it needs no
reference case and no published number - the input IS the expected output.

FOUR ARMS, each catching a different way the interface can lie:

  A  INDEX IDENTITY   write n = n0 exactly; the fitted index must come back n0
                      at every wavelength. Catches a route that alters what it
                      was given.
  B  INDEX RAMP       write n = n0 + a*r^2 with a known a; the fit must return
                      the same a. Catches smoothing, truncation and the noise
                      floor - and the floor is why this runs at TWO amplitudes.
  C  DISPERSION       write a uniform index and compare the RAY-TRACED optical
                      path against the unloaded element at three wavelengths.
                      Measured: loading a uniform index collapses F, d and C
                      onto the d-line path exactly - the route REPLACES the
                      index and the material's dispersion is discarded. This is
                      the 2026-08-29 defect, now a standing measurement.
                      Do NOT write this arm with INDX: INDX cannot see the STAR
                      contribution at all, so such a check can never fail.
  D  STRESS -> BIREF  write a uniform uniaxial stress; the local birefringence
                      must come back (K11-K12)*sigma at the d-line. Catches the
                      catalogue not being attached and a missing BD record.
                      It does NOT catch a stress that was imported and never
                      applied - measured, see the note at the arm.

Exits non-zero if any arm disagrees. Needs a standalone OpticStudio licence.
"""
import json
import math
import os
import sys

import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
WRK = os.path.join(HERE, "roundtrip")
LAM_D_MM = 0.5875618e-3
SURF = 1
KDIFF_PMMA = 4.5e-6          # K11-K12 for MS_PMMA, from the generated AGF
os.makedirs(WRK, exist_ok=True)

# --poison breaks each arm's SUBJECT - not its probe - and the run must then
# fail. Without it "10 claims, 0 FAILED" is a sentence, not evidence: three of
# these arms were rewritten this session precisely because they were passing or
# failing for reasons unconnected to STAR. Run both ways; the difference is the
# proof that the check discriminates.
POISON = "--poison" in sys.argv

fails, out = [], {}


def claim(name, ok, got=""):
    print("%s %-58s %s" % ("ok  " if ok else "FAIL", name, got))
    if not ok:
        fails.append(name)


PTS_I = np.loadtxt(os.path.join(REAL, "moldstress_s%d_index.txt" % SURF))
PTS_S = np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % SURF))
N0 = float(np.median(PTS_I[np.hypot(PTS_I[:, 0], PTS_I[:, 1]) < 0.1, 3]))

app = connect()
s = app.PrimarySystem


def load_index(n_of_r2):
    """Write an index cloud on the real point set and import it."""
    p = PTS_I.copy()
    r2 = p[:, 0] ** 2 + p[:, 1] ** 2
    p[:, 3] = n_of_r2(r2)
    path = os.path.join(WRK, "idx.txt")
    np.savetxt(path, p, fmt="%.12E", delimiter=" ")
    assert s.LoadFile(BASE, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    di = s.LDE.GetSurfaceAt(SURF).STARData.DirectIndex
    di.SetDataIsLocal()
    di.FEAData.ImportDirectIndex_1(path)
    di.Fits.Refit()
    di.Fits.GRINStep = 0.50
    return di


def dense_slice():
    """A depth where the cloud actually HAS points, and the radius it covers.

    THREE of this arm-set's first-run failures were probes sampling where the
    quantity is not defined, and the worst was silent: z=2.0 was picked by eye
    as "mid-element" and the cloud has ZERO points within +-0.15 of it, so the
    ramp arm was fitting pure extrapolation and reporting it as a fit defect.
    A uniform field extrapolates to itself, which is why the identity arm
    passed on the same bad probe and raised no alarm.
    """
    z = PTS_I[:, 2]
    edges = np.linspace(z.min(), z.max(), 17)
    k = int(np.argmax(np.histogram(z, edges)[0]))
    z0 = 0.5 * (edges[k] + edges[k + 1])
    half = 0.5 * (edges[1] - edges[0])
    m = np.abs(z - z0) < half
    return float(z0), float(np.hypot(PTS_I[m, 0], PTS_I[m, 1]).max()), int(m.sum())


def in_domain(x, y, z, tol=0.25):
    """Is there real data near this probe point? Guards every probe below."""
    d = np.sqrt((PTS_I[:, 0] - x) ** 2 + (PTS_I[:, 1] - y) ** 2
                + (PTS_I[:, 2] - z) ** 2)
    return bool(d.min() < tol)


def fitted_index(di, x, y, z):
    try:
        return float(di.Fits.GetFittedIndex(x, y, z))
    except Exception as ex:
        return float("nan")


print("=" * 78)
print("A. INDEX IDENTITY - a uniform field must come back unchanged")
print("=" * 78)
di = load_index(lambda r2: np.full(len(r2), N0 + (1e-3 if POISON else 0.0)))
Z0, RMAX, NSL = dense_slice()
print("    probing at z = %.3f, where the cloud has %d points out to r = %.3f"
      % (Z0, NSL, RMAX))
probes = [(0.0, 0.0, Z0), (0.25 * RMAX, 0.0, Z0), (0.0, 0.5 * RMAX, Z0),
          (0.5 * RMAX, 0.5 * RMAX, Z0)]
outside = [q for q in probes if not in_domain(*q)]
claim("every identity probe sits inside the data", not outside,
      "%d of %d outside" % (len(outside), len(probes)))
worst = 0.0
for (x, y, z) in probes:
    got = fitted_index(di, x, y, z)
    worst = max(worst, abs(got - N0))
    print("    (%.1f, %.1f, %.1f) -> %.9f" % (x, y, z, got))
claim("a uniform index field round-trips", worst < 1e-7,
      "worst |read - written| = %.2e over %d probes" % (worst, len(probes)))
out["identity_worst"] = worst

print()
print("=" * 78)
print("B. INDEX RAMP - a known quadratic must come back with the same curvature")
print("=" * 78)
ramp = {}
for a in (1.0e-5, 1.0e-7):        # above and near the measured noise floor
    di = load_index(lambda r2, a=a: N0 + (2.0 if POISON else 1.0) * a * r2)
    # In the dense slice, over the radius THAT SLICE covers - not the cloud's
    # global maximum radius, which is reached only at other depths.
    rr = np.linspace(0.0, 0.90 * RMAX, 9)
    keep = np.array([in_domain(float(r), 0.0, Z0) for r in rr])
    rr = rr[keep]
    vals = np.array([fitted_index(di, float(r), 0.0, Z0) for r in rr])
    A = np.vstack([np.ones(len(rr)), rr ** 2]).T
    coef, *_ = np.linalg.lstsq(A, vals, rcond=None)
    got = float(coef[1])
    ratio = got / a
    ramp["%.0e" % a] = dict(written=a, read=got, ratio=ratio)
    resid = float(np.max(np.abs(vals - (N0 + a * rr ** 2)))) / a
    ramp["%.0e" % a]["resid_pts"] = resid
    print("    a = %.1e written -> %.4e read   ratio %.4f   (%d probes, "
          "worst point error %.3f of a*r^2)" % (a, got, ratio, len(rr), resid))
claim("the ramp probes stayed inside the data", len(rr) >= 6,
      "%d probes survived the domain guard" % len(rr))
# GATED AT 10%, AND THE MEASURED VALUE IS 2.5%. The bound is loose on purpose:
# 1.0246 is the vendor fit's accuracy on a quadratic, not a defect, and I have
# no basis for asserting it should be tighter. What this arm is for is noticing
# if it MOVES. The three values this ratio took while the probe was wrong -
# 0.6450 (z=2.0, where the cloud has no points at all), 0.7775 (same, wider
# span), 1.0246 (dense slice, in-domain) - are the reason the domain guard
# above exists and the reason a tight bound here would have been read as a
# vendor defect three separate times.
claim("a 1e-5 ramp round-trips to 10%", abs(ramp["1e-05"]["ratio"] - 1.0) < 0.10,
      "ratio %.4f (worst point error %.3f of a*r^2)"
      % (ramp["1e-05"]["ratio"], ramp["1e-05"]["resid_pts"]))
claim("the fit is LINEAR in amplitude over two decades",
      abs(ramp["1e-05"]["ratio"] - ramp["1e-07"]["ratio"]) < 1e-3,
      "ratios %.4f and %.4f" % (ramp["1e-05"]["ratio"], ramp["1e-07"]["ratio"]))
# The small-amplitude arm is a REPORTER, not a gate: the noise floor is a
# measured property of the vendor's fit and this records where it bites.
print("    NOTE the 1e-7 ratio is reported, not gated - the fit's noise floor")
print("         was measured 2026-08-29 to change SIGN below a peak dn of ~1e-6")
out["ramp"] = ramp

print()
print("=" * 78)
print("C. DISPERSION - does the route REPLACE the index or PERTURB it?")
print("=" * 78)
# TWO EARLIER VERSIONS OF THIS ARM MEASURED THE WRONG THING, and the second
# failure is the finding this arm now exists to record.
#
#   v1 read INDX on one loaded field and asserted its spread was zero. INDX
#      carries MS_PMMA's own Sellmeier dispersion whether or not STAR
#      contributed anything, so the arm measured the MATERIAL and reported it
#      as the route.
#   v2 read INDX on two fields 1e-2 apart, to difference the material out.
#      THE DIFFERENCE WAS EXACTLY ZERO AT ALL THREE WAVELENGTHS: INDX does not
#      see the STAR contribution at all. That is a trap with teeth - a check on
#      the index route written with INDX cannot fail, and would have read as a
#      clean pass forever.
#
# So the probe has to be something the RAY actually experiences. OPTH is the
# accumulated optical path to a surface, so a uniform index change inside the
# element moves it by (dn x geometric path) and nothing else moves at all.
DELTA = 1.0e-2
WAVES = (1, 2, 3)
lam = [float(s.SystemData.Wavelengths.GetWavelength(w).Wavelength) for w in WAVES]


def opth():
    return [float(s.MFE.GetOperandValue(E.OPTH, SURF + 1, w, 0, 0, 0, 0, 0, 0))
            for w in WAVES]


assert s.LoadFile(BASE, False)
s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
o_unloaded = opth()
load_index(lambda r2: np.full(len(r2), N0))
o_base = opth()
if not POISON:
    load_index(lambda r2: np.full(len(r2), N0 + DELTA))
o_delta = opth()

print("    %-12s F %.9f  d %.9f  C %.9f" % (("unloaded",) + tuple(o_unloaded)))
print("    %-12s F %.9f  d %.9f  C %.9f" % (("STAR n0",) + tuple(o_base)))
print("    %-12s F %.9f  d %.9f  C %.9f" % (("STAR n0+d",) + tuple(o_delta)))

d_star = [o_delta[i] - o_base[i] for i in range(3)]
d_load = [o_base[i] - o_unloaded[i] for i in range(3)]
print("    write %+.0e   -> dOPTH  F %+.3e  d %+.3e  C %+.3e" % ((DELTA,) + tuple(d_star)))
print("    load n0 vs off -> dOPTH  F %+.3e  d %+.3e  C %+.3e" % tuple(d_load))
out["opth_dstar"], out["opth_dload"] = d_star, d_load

# 1. The trace must see STAR at all, or nothing below means anything.
claim("the RAY TRACE responds to the STAR index field",
      max(abs(x) for x in d_star) > 1e-9,
      "largest dOPTH for a %.0e write = %.3e mm" % (DELTA, max(abs(x) for x in d_star)))

# 2. STAR's own contribution is one number at every wavelength - that is what
#    "monochromatic" means here, and it is a property of the CONTRIBUTION, not
#    of the element. Measured as the spread of the response across F-d-C.
sp = max(d_star) - min(d_star)
mstar = max(abs(x) for x in d_star)
# Guarded on a NON-ZERO response: an arm that saw nothing at all would
# otherwise report a spread of zero and read as the cleanest pass on the page.
claim("STAR's contribution is applied identically at every wavelength",
      mstar > 1e-9 and sp < 1e-6 * mstar + 1e-12,
      "spread %.3e mm on a response of %.3e mm" % (sp, mstar))

# 3. REPLACE or PERTURB. Writing the d-line index itself and comparing against
#    the unloaded element separates them cleanly: a route that REPLACES leaves
#    the d-line alone and moves F and C by the material's own dispersion; one
#    that PERTURBS leaves all three alone.
mag = max(abs(x) for x in d_load)
if mag < 1e-9:
    verdict = "PERTURBS - the element keeps its own dispersion"
elif abs(d_load[1]) < 0.05 * mag:
    verdict = "REPLACES - the material's dispersion is DISCARDED"
else:
    verdict = "NEITHER cleanly - the d-line moved too"
print("    verdict: the direct-index route %s" % verdict)
out["star_mode"] = verdict
claim("the index route has a coherent wavelength behaviour",
      not verdict.startswith("NEITHER"), verdict)

print()
print("=" * 78)
print("D. STRESS -> BIREFRINGENCE - does the catalogue coupling round-trip?")
print("=" * 78)
S_MPA = 10.0
p = PTS_S.copy()
# Poison the WRITTEN STRESS, which is this arm's subject. The first poison
# attempt skipped ApplyStress() instead and THE ARM STILL PASSED, exactly:
# GetPointRetardanceList reads the FIT, not the applied state, so a stress that
# was imported and never applied reads back as though it had been. That is the
# same family as GetRetardanceMap returning pi on a stress-free element, and it
# means ApplyStress() cannot be verified from the retardance list at all.
p[:, 3] = S_MPA * (2.0 if POISON else 1.0)
p[:, 4:9] = 0.0
spath = os.path.join(WRK, "stress.txt")
np.savetxt(spath, p, fmt="%.9E", delimiter=" ")
assert s.LoadFile(BASE, False)
s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
st = s.LDE.GetSurfaceAt(SURF).STARData.Stress
try:
    st.FEAData.UnloadData()
except Exception:
    pass
st.SetDataIsLocal()
st.SetWorkingWavelength(1)
code = int(st.FEAData.ImportStress_1(spath))
npts = int(st.FEAData.NumberOfDataPoints)
st.Fits.Refit()
st.Fits.ApplyStress()
pl = st.Fits.GetPointRetardanceList(8, 0, 1)
got = float(np.max(np.abs([float(q.Retardance) for q in pl])))
# rad per mm at the d-line. The first version of this line carried a
# spurious *1e-3 left over from an earlier form and read 1000x low - the
# round trip was exact and the EXPECTATION was wrong, which is the failure
# mode a round-trip check is least able to tell you about by itself.
want = 2 * math.pi * KDIFF_PMMA * S_MPA / LAM_D_MM
print("    import code %d, %d points accepted" % (code, npts))
print("    written  sigma_xx = %.1f N/mm2, K11-K12 = %.3e" % (S_MPA, KDIFF_PMMA))
print("    expected %.6f rad/mm at the d-line" % want)
print("    read     %.6f rad/mm over %d points" % (got, len(pl)))
claim("the import accepted every point", code == 0 and npts == len(PTS_S),
      "%d of %d" % (npts, len(PTS_S)))
claim("stress -> birefringence round-trips to 1%",
      want > 0 and abs(got - want) / want < 0.01,
      "read/expected = %.4f" % (got / want if want else float("nan")))
out["biref_written"] = want
out["biref_read"] = got

json.dump(out, open(os.path.join(HERE, "starroundtrip.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("%d claims across 4 arms, %d FAILED%s"
      % (10, len(fails), "   [POISONED RUN - failures are the point]" if POISON else ""))
if POISON and not fails:
    print("THE POISONED RUN PASSED. The check does not discriminate.")
    sys.exit(2)
for f in fails:
    print("  FAILED:", f)
print("wrote starroundtrip.json")
sys.exit(1 if fails else 0)

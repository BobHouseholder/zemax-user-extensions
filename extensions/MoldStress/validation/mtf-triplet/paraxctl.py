"""ITEM 2. Settle the paraxial EFFL anomaly with a POSITIVE CONTROL on STAR.

The open question: loading the moulding index field moves paraxial EFFL by
-0.987%, which is ~18x larger than a smooth reading of the field supports, and
scaling the field by 0.1 moves EFFL by 0.065 of the full amount where first
order demands exactly 0.100. The non-linearity even FLIPPED SIGN across the
catalogue fix (0.155 -> 0.065) on a field that moved by under 0.51%.

Chasing the mechanism inside the real field has not worked. So instead: feed
STAR a field whose paraxial answer is known in closed form, on the SAME point
set, through the SAME import path, and ask whether it comes back right.

    n(r) = n_mat + a * r^2

A thin slab of thickness t with that profile adds optical path a*t*r^2, which
is a thin lens of power

    phi_add = -2 * a * t                                              (1)

independent of n_mat. Inserted where the paraxial marginal ray height is y, it
changes system power by (y/y1)*phi_add, so

    dEFFL = -F^2 * (y/y1) * phi_add = 2*a*t*F^2*(y/y1)                (2)

TWO INDEPENDENT PREDICTIONS, because one closed form agreeing with itself is
not a check (verify-the-artifact step 2 / gates step 0):

  METHOD A  my own paraxial y-nu trace through the prescription, using (2).
            VALIDATED FIRST by reproducing Zemax's own EFFL - if my trace
            cannot reproduce the unperturbed focal length it cannot be trusted
            for the perturbed one.
  METHOD B  Zemax's own paraxial machinery: apply the SAME added power (1) as
            a curvature change dc = phi_add/(n'-n) on the element's front
            surface, and read EFFL. No GRIN, no STAR, no fit.

If STAR reproduces A and B on the synthetic field, the anomaly is in the DATA
or in the fit of the real field, and STAR's paraxial handling is exonerated.
If STAR misses on an analytic quadratic too, the finding is STAR's.

THE LINEARITY ARM IS THE SHARPEST PART AND NEEDS NO CLOSED FORM AT ALL. The
synthetic field is exactly linear in `a` by construction, so scaling a by 0.1
MUST move EFFL by 0.100 of the full amount. The real field gives 0.065. If the
synthetic field is linear and the real one is not, the non-linearity lives in
the field's SHAPE, not in STAR's response to a field.

Writes: paraxctl.json, and synthetic clouds under syn/.
"""
import json
import os
import shutil

import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")             # the real moulding export
SYN = os.path.join(HERE, "syn")
WAVE_D = 2                                   # 1=F 0.486, 2=d 0.588, 3=C 0.656
FRONTS = [1, 3, 5]                           # front surface of each element

out = {}


def banner(t):
    print()
    print("=" * 78)
    print(t)
    print("=" * 78)


# ============================================================== connect
app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False), "could not load " + BASE
NSURF = s.LDE.NumberOfSurfaces
IMG = NSURF - 1
IMGPREV = NSURF - 2


def effl(wave=WAVE_D):
    return s.MFE.GetOperandValue(E.EFFL, 0, wave, 0, 0, 0, 0, 0, 0)


def indx(surf, wave=WAVE_D):
    return s.MFE.GetOperandValue(E.INDX, surf, wave, 0, 0, 0, 0, 0, 0)


def pin_image_plane():
    """Every EFFL here is read with the image plane FIXED.

    EFFL does not depend on the image plane, but a live marginal-ray-height
    solve makes the system re-solve on every edit, and a scan helper that
    leaves ambient state live is precisely the defect that produced a +275 um
    reading where the truth was 19 um (gates skill, section A). Pin it once,
    explicitly, for every arm.
    """
    cell = s.LDE.GetSurfaceAt(IMGPREV).ThicknessCell
    prev = cell.GetSolveData()
    cell.MakeSolveFixed()
    return prev


pin_image_plane()

# ============================================== A. prescription + my own trace
banner("A. PRESCRIPTION, AND A PARAXIAL TRACE VALIDATED AGAINST ZEMAX")

pres = []
for i in range(NSURF):
    row = s.LDE.GetSurfaceAt(i)
    pres.append(dict(i=i, R=float(row.Radius), t=float(row.Thickness),
                     mat=(row.Material or ""), sd=float(row.SemiDiameter),
                     n_after=float(indx(i))))
print("%-4s %-12s %12s %10s %10s %9s" %
      ("surf", "material", "radius", "thick", "semi-dia", "n_after"))
for p in pres:
    print("%-4d %-12s %12.5f %10.5f %10.5f %9.6f" %
          (p["i"], p["mat"], p["R"], p["t"], p["sd"], p["n_after"]))

zemax_effl = effl()
print("\nZemax EFFL (wave %d) = %.6f mm" % (WAVE_D, zemax_effl))


def ynu_trace(dc=None):
    """Paraxial y-nu trace. dc maps surface index -> extra curvature."""
    dc = dc or {}
    y1 = 1.0
    y, w = y1, 0.0                      # w = n*u, reduced angle; entering parallel
    heights = {}
    for k in range(1, IMG):             # object at 0 is infinity; image is last
        p = pres[k]
        c = (1.0 / p["R"]) if abs(p["R"]) > 1e-12 and np.isfinite(p["R"]) else 0.0
        c += dc.get(k, 0.0)
        n_before = pres[k - 1]["n_after"]
        n_after = p["n_after"]
        heights[k] = y
        w = w - y * c * (n_after - n_before)
        y = y + p["t"] * w / n_after
    return y1, w, heights                # w is n*u in image space, n = 1


y1, w_out, HEIGHT = ynu_trace()
my_effl = -y1 / w_out
print("my y-nu  EFFL          = %.6f mm" % my_effl)
rel = abs(my_effl - zemax_effl) / abs(zemax_effl)
print("agreement              = %.3e relative   %s" %
      (rel, "OK - trace validated" if rel < 1e-6 else "*** TRACE NOT VALIDATED"))
out["effl_zemax"] = zemax_effl
out["effl_mytrace"] = my_effl
out["trace_validated"] = bool(rel < 1e-6)
if rel >= 1e-6:
    print("REFUSING to use method A: my own instrument does not reproduce the")
    print("unperturbed focal length, so its perturbed answer means nothing.")

print("\nmarginal ray heights (y1 = 1.0):")
for k in sorted(HEIGHT):
    print("   surf %d  y = %+.6f%s" %
          (k, HEIGHT[k], "   <- element front" if k in FRONTS else ""))
out["ray_heights"] = {str(k): HEIGHT[k] for k in HEIGHT}

# element axial thickness = thickness of its front surface
ELEM_T = {f: pres[f]["t"] for f in FRONTS}
print("\nelement centre thicknesses:", {k: round(v, 5) for k, v in ELEM_T.items()})

# ==================================== B. what the REAL field's curvature implies
banner("B. THE REAL FIELD'S NEAR-AXIS CURVATURE, AND WHAT IT PREDICTS")
print("A quadratic fit has to be taken in the regime where the reference is")
print("DEFINED - paraxial means r -> 0. Fitting over the whole aperture answers")
print("a different question. So fit over several radii and show the spread.")
print()

real_fit = {}
FRACS = [0.25, 0.5, 0.75, 1.0]
for f in FRONTS:
    d = np.loadtxt(os.path.join(REAL, "moldstress_s%d_index.txt" % f))
    r = np.hypot(d[:, 0], d[:, 1])
    n = d[:, 3]
    rmax = r.max()
    n0 = float(np.median(n[r < 0.02 * rmax])) if (r < 0.02 * rmax).any() else float(n[r.argmin()])
    row = {}
    print("  element at surface %d   %d points, r <= %.4f mm, n span %.3e"
          % (f, len(d), rmax, n.max() - n.min()))
    for fr in FRACS:
        m = r <= fr * rmax
        if m.sum() < 20:
            continue
        # n = n0 + a r^2, least squares in r^2 with the constant free
        A = np.vstack([np.ones(m.sum()), r[m] ** 2]).T
        coef, *_ = np.linalg.lstsq(A, n[m], rcond=None)
        a = float(coef[1])
        resid = float(np.sqrt(np.mean((A @ coef - n[m]) ** 2)))
        row["%.2f" % fr] = dict(a=a, npts=int(m.sum()), rms_resid=resid,
                                rmax=float(fr * rmax))
        print("     r <= %.2f rmax (%4d pts): a = %+.4e /mm^2   fit rms %.2e"
              % (fr, m.sum(), a, resid))
    real_fit[str(f)] = dict(n0=n0, rmax=float(rmax), fits=row)
out["real_field_quadratic_fit"] = real_fit


def predict_deffl(a_by_surface):
    """Method A: closed form, eq (2), summed over elements."""
    tot = 0.0
    for f, a in a_by_surface.items():
        phi = -2.0 * a * ELEM_T[f]
        tot += -(zemax_effl ** 2) * (HEIGHT[f] / y1) * phi
    return tot


a_quarter = {f: real_fit[str(f)]["fits"]["0.25"]["a"] for f in FRONTS}
a_full = {f: real_fit[str(f)]["fits"]["1.00"]["a"] for f in FRONTS}
print()
print("closed-form dEFFL from the near-axis (r<=0.25 rmax) fit: %+.5f mm  (%+.4f%%)"
      % (predict_deffl(a_quarter), 100 * predict_deffl(a_quarter) / zemax_effl))
print("closed-form dEFFL from a whole-aperture fit            : %+.5f mm  (%+.4f%%)"
      % (predict_deffl(a_full), 100 * predict_deffl(a_full) / zemax_effl))
print("MEASURED with the real field loaded (README, this lens) :          -0.987%")
out["predicted_deffl_nearaxis"] = predict_deffl(a_quarter)
out["predicted_deffl_fullap"] = predict_deffl(a_full)

# ================================================ C. the synthetic control
banner("C. SYNTHETIC ANALYTIC FIELD - THE POSITIVE CONTROL")

if os.path.isdir(SYN):
    shutil.rmtree(SYN)
os.makedirs(SYN)

POINTS = {}
for f in FRONTS:
    POINTS[f] = np.loadtxt(os.path.join(REAL, "moldstress_s%d_index.txt" % f))


def write_synth(tag, a_by_surface):
    """Same (x,y,z) points as the real export; only the index column changes.

    Reusing the real point set is deliberate: it holds the grid, the point
    count, the aperture and the z-extent fixed, so the only thing that differs
    between the real arm and this one is the SHAPE of the field.
    """
    d = os.path.join(SYN, tag)
    os.makedirs(d, exist_ok=True)
    paths = {}
    for f in FRONTS:
        p = POINTS[f].copy()
        r2 = p[:, 0] ** 2 + p[:, 1] ** 2
        n0 = real_fit[str(f)]["n0"]
        p[:, 3] = n0 + a_by_surface[f] * r2
        path = os.path.join(d, "syn_s%d_index.txt" % f)
        np.savetxt(path, p, fmt="%.9E", delimiter=" ")
        paths[f] = path
    return paths


def load_and_effl(paths):
    """Load index clouds and read EFFL. Returns (effl, per-surface point counts)."""
    assert s.LoadFile(BASE, False)
    pin_image_plane()
    counts = {}
    for f, path in (paths or {}).items():
        di = s.LDE.GetSurfaceAt(f).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(path)
        di.Fits.Refit()
        di.Fits.GRINStep = 0.50
        try:
            counts[f] = int(di.FEAData.NumberOfDataPoints)
        except Exception:
            counts[f] = -1
    return effl(), counts


base_effl, _ = load_and_effl(None)
print("baseline EFFL, nothing loaded: %.6f mm" % base_effl)
out["effl_baseline"] = base_effl

# scale the synthetic field so its dEFFL is the same ORDER as the real one,
# which keeps the comparison inside the same regime rather than testing STAR
# on a perturbation a thousand times bigger.
A_REF = a_quarter

arms = [
    ("null", {f: 0.0 for f in FRONTS}),
    ("tenth", {f: A_REF[f] * 0.1 for f in FRONTS}),
    ("full", dict(A_REF)),
    ("ten_x", {f: A_REF[f] * 10.0 for f in FRONTS}),
]

syn_res = {}
print()
print("%-8s %14s %14s %14s %12s" %
      ("arm", "EFFL mm", "dEFFL mm", "predicted mm", "meas/pred"))
for tag, a_by in arms:
    paths = write_synth(tag, a_by)
    ef, counts = load_and_effl(paths)
    d_meas = ef - base_effl
    d_pred = predict_deffl(a_by)
    ratio = (d_meas / d_pred) if abs(d_pred) > 1e-12 else float("nan")
    syn_res[tag] = dict(a=a_by, effl=ef, d_meas=d_meas, d_pred=d_pred,
                        ratio=ratio, counts=counts)
    print("%-8s %14.6f %+14.6f %+14.6f %12.4f" %
          (tag, ef, d_meas, d_pred, ratio))

# THE LINEARITY ARM. No closed form involved - the synthetic field is exactly
# linear in `a` by construction, so this ratio MUST be 0.100.
lin = (syn_res["tenth"]["d_meas"] / syn_res["full"]["d_meas"]
       if abs(syn_res["full"]["d_meas"]) > 1e-12 else float("nan"))
lin10 = (syn_res["ten_x"]["d_meas"] / syn_res["full"]["d_meas"]
         if abs(syn_res["full"]["d_meas"]) > 1e-12 else float("nan"))
print()
print("SYNTHETIC LINEARITY   tenth/full = %.4f   (first order demands 0.1000)" % lin)
print("                      ten_x/full = %.4f   (first order demands 10.000)" % lin10)
out["synthetic"] = syn_res
out["synthetic_linearity_tenth_over_full"] = lin
out["synthetic_linearity_tenx_over_full"] = lin10

# ================================ D. METHOD B - Zemax's own paraxial machinery
banner("D. METHOD B - THE SAME ADDED POWER AS A CURVATURE CHANGE")
print("No GRIN and no STAR: put phi_add = -2*a*t on the element's front surface")
print("as dc = phi_add/(n'-n) and let Zemax compute EFFL its own way.")
print()
assert s.LoadFile(BASE, False)
pin_image_plane()
for f in FRONTS:
    n_before = pres[f - 1]["n_after"]
    n_after = pres[f]["n_after"]
    phi = -2.0 * A_REF[f] * ELEM_T[f]
    dc = phi / (n_after - n_before)
    row = s.LDE.GetSurfaceAt(f)
    c0 = 1.0 / row.Radius if abs(row.Radius) > 1e-12 else 0.0
    row.Radius = 1.0 / (c0 + dc) if abs(c0 + dc) > 1e-30 else 1e30
methodB = effl()
print("EFFL with equivalent curvature change: %.6f mm" % methodB)
print("dEFFL method B                       : %+.6f mm" % (methodB - base_effl))
print("dEFFL method A (closed form)         : %+.6f mm" % predict_deffl(A_REF))
print("dEFFL STAR synthetic 'full' arm      : %+.6f mm" % syn_res["full"]["d_meas"])
out["method_b_effl"] = methodB
out["method_b_deffl"] = methodB - base_effl

# =============================== E. the REAL field, for a matched comparison
banner("E. THE REAL FIELD ON THE SAME FOOTING")
real_paths = {f: os.path.join(REAL, "moldstress_s%d_index.txt" % f) for f in FRONTS}
ef_real, counts_real = load_and_effl(real_paths)
d_real = ef_real - base_effl
print("EFFL with the real moulding field: %.6f mm   dEFFL %+.6f mm  (%+.4f%%)"
      % (ef_real, d_real, 100 * d_real / base_effl))
print("points accepted per surface:", counts_real)

# a tenth-scaled REAL field, written through the same path as the synthetic
# arms so the two linearity numbers are measured identically
d = os.path.join(SYN, "real_tenth")
os.makedirs(d, exist_ok=True)
tenth_paths = {}
for f in FRONTS:
    p = POINTS[f].copy()
    n0 = real_fit[str(f)]["n0"]
    p[:, 3] = n0 + (p[:, 3] - n0) * 0.1
    path = os.path.join(d, "syn_s%d_index.txt" % f)
    np.savetxt(path, p, fmt="%.9E", delimiter=" ")
    tenth_paths[f] = path
ef_rt, _ = load_and_effl(tenth_paths)
d_rt = ef_rt - base_effl
real_lin = d_rt / d_real if abs(d_real) > 1e-12 else float("nan")
print("EFFL with the real field scaled 0.1 : %.6f mm   dEFFL %+.6f mm" % (ef_rt, d_rt))
print()
print("REAL LINEARITY        tenth/full = %.4f   (first order demands 0.1000)" % real_lin)
print("SYNTHETIC LINEARITY   tenth/full = %.4f" % lin)
out["real_effl"] = ef_real
out["real_deffl"] = d_real
out["real_linearity_tenth_over_full"] = real_lin

json.dump(out, open(os.path.join(HERE, "paraxctl.json"), "w"), indent=1)
app.CloseApplication()
print()
print("wrote paraxctl.json")
print("done")

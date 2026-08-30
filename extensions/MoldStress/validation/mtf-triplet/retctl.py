"""ITEM 1. The polarisation half of MoldStress, under controls.

The index half ran clean, exited 0, and printed +1572% that was really 0.003
waves. Four defects fell out of it in one day and every one was found by a
CONTROL, not by reading code. The retardance half has one data point and no
controls: "peak retardance 0.41 waves, 585x the wavefront change", now a
shipped warning threshold, measured once on one lens.

So: feed STAR stress fields whose retardance is known in closed form.

For a ray along z through a uniform UNIAXIAL stress sigma_xx = S, the two
transverse principal indices differ by

    dn = (K11 - K12) * S                                              (1)

and the retardance is that times the local axial path,

    OPD(r) = dn * t(r),   t(r) = CT - sag(R_front, r) + sag(R_back, r) (2)

K11 and K12 are read back from the AGF the tool GENERATED and Zemax actually
reads, not from Polymers.cs - the catalogue is the artifact under test here
(verify-the-artifact step 2), and it was wrong about dispersion as recently as
this morning.

FIVE ARMS, and the interesting ones are not the null:

  null         all zeros            -> retardance exactly 0. Trivial.
  hydrostatic  sxx=syy=szz=S        -> retardance exactly 0 DESPITE 10 MPa of
                                      stress. A non-trivial input with a zero
                                      answer is a far stronger control than a
                                      zero input, and it is the one that can
                                      catch a tensor mishandled into a scalar.
  uniaxial S   sxx=S                -> peak = (1)x(2), at a KNOWN LOCATION:
                                      the CENTRE for the biconvex elements 1
                                      and 3, and the APERTURE EDGE for the
                                      biconcave element 2, because its axial
                                      path grows with r. That edge case tests
                                      whether the shipped 217-point map
                                      reaches the edge at all.
  uniaxial S/10, S*10               -> must scale exactly 0.1 and 10.

And the shipped call has six undocumented arguments. Runner.cs passes
(8, 0, 1, 1.0, 0.0, 0.0, 0.0) with a comment explaining only the first. If one
of the others is a wavelength or a propagation direction, the shipped peak is
quoted over a domain nobody chose. Decoded here by sweeping.

Writes: retctl.json, stress fields under ctlstress/.
"""
import json
import math
import os
import shutil

import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
CTL = os.path.join(HERE, "ctlstress")
AGF = r"C:\Users\Shadow\Documents\Zemax\Glasscat\MOLDSTRESS.AGF"

# element front surface -> (back surface, material, CT, R_front, R_back)
ELEMS = {
    1: dict(back=2, mat="MS_PMMA", ct=4.00000, rf=11.00271, rb=-83.76109),
    3: dict(back=4, mat="MS_POLYSTYR", ct=1.20000, rf=-13.97735, rb=9.00465),
    5: dict(back=6, mat="MS_PMMA", ct=4.00000, rf=24.77427, rb=-11.70778),
}
S_REF = 10.0                      # N/mm^2 uniaxial, same order as the real field
out = {}


def banner(t):
    print()
    print("=" * 78)
    print(t)
    print("=" * 78)


# =================================================== K11/K12 from the CATALOGUE
banner("0. PHOTOELASTIC CONSTANTS, READ BACK FROM THE GENERATED CATALOGUE")
KD = {}
cur = None
for line in open(AGF, encoding="utf-8", errors="replace"):
    if line.startswith("NM "):
        cur = line.split()[1]
    elif line.startswith("BD ") and cur:
        p = line.split()
        lam, k, k11, k12 = float(p[1]), float(p[2]), float(p[3]), float(p[4])
        KD[cur] = dict(lam=lam, k=k, k11=k11, k12=k12, kdiff=(k11 - k12) * 1e-6)
for m in sorted(set(e["mat"] for e in ELEMS.values())):
    d = KD[m]
    print("  %-14s BD lambda %.3f um   K %+.4f  K11 %+.4f  K12 %+.4f"
          % (m, d["lam"], d["k"], d["k11"], d["k12"]))
    print("  %-14s K11-K12 = %+.4f Br -> dn = %.3e per N/mm^2"
          % ("", d["k11"] - d["k12"], d["kdiff"]))
    assert abs((d["k12"] - d["k11"]) - d["k"]) < 1e-9, "K != K12-K11 in the AGF"
out["catalogue_BD"] = {m: KD[m] for m in set(e["mat"] for e in ELEMS.values())}


def sag(R, r):
    if not np.isfinite(R) or abs(R) < 1e-12:
        return np.zeros_like(r)
    q = 1.0 - (r / R) ** 2
    q = np.clip(q, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


def axial_path(f, r):
    e = ELEMS[f]
    return e["ct"] - sag(e["rf"], r) + sag(e["rb"], r)


# ============================================== write the control stress fields
banner("1. CONTROL STRESS FIELDS")
if os.path.isdir(CTL):
    shutil.rmtree(CTL)
os.makedirs(CTL)

SPTS = {f: np.loadtxt(os.path.join(REAL, "moldstress_s%d_stress.txt" % f))
        for f in ELEMS}
for f, d in SPTS.items():
    r = np.hypot(d[:, 0], d[:, 1])
    print("  surface %d: %d stress points, r <= %.4f mm, z %.4f..%.4f"
          % (f, len(d), r.max(), d[:, 2].min(), d[:, 2].max()))


def write_stress(tag, fn):
    """fn(f, x, y, z) -> (sxx, syy, szz, sxy, sxz, syz), each an array."""
    d = os.path.join(CTL, tag)
    os.makedirs(d, exist_ok=True)
    paths = {}
    for f in ELEMS:
        p = SPTS[f].copy()
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        sxx, syy, szz, sxy, sxz, syz = fn(f, x, y, z)
        p[:, 3], p[:, 4], p[:, 5] = sxx, syy, szz
        p[:, 6], p[:, 7], p[:, 8] = sxy, sxz, syz
        path = os.path.join(d, "ctl_s%d_stress.txt" % f)
        np.savetxt(path, p, fmt="%.9E", delimiter=" ")
        paths[f] = path
    return paths


def const_field(sxx=0.0, syy=0.0, szz=0.0, sxy=0.0):
    def fn(f, x, y, z):
        o = np.zeros_like(x)
        return (o + sxx, o + syy, o + szz, o + sxy, o, o)
    return fn


ARMS = {
    "null":        const_field(),
    "hydrostatic": const_field(sxx=S_REF, syy=S_REF, szz=S_REF),
    "uni_tenth":   const_field(sxx=S_REF * 0.1),
    "uni":         const_field(sxx=S_REF),
    "uni_tenx":    const_field(sxx=S_REF * 10.0),
}
ARM_PATHS = {tag: write_stress(tag, fn) for tag, fn in ARMS.items()}
ARM_PATHS["real"] = {f: os.path.join(REAL, "moldstress_s%d_stress.txt" % f)
                     for f in ELEMS}
print("  wrote %d control arms" % len(ARMS))

# ------------------------------------------------- closed-form expectations
banner("2. CLOSED-FORM EXPECTATION FOR THE UNIAXIAL ARM")
EXPECT = {}
for f, e in ELEMS.items():
    kd = KD[e["mat"]]["kdiff"]
    dn = kd * S_REF
    rgrid = np.linspace(0.0, np.hypot(SPTS[f][:, 0], SPTS[f][:, 1]).max(), 4001)
    t = axial_path(f, rgrid)
    i = int(np.argmax(t))
    EXPECT[f] = dict(dn=dn, opd_mm=float(dn * t[i]), r_peak=float(rgrid[i]),
                     t_peak=float(t[i]), t_centre=float(axial_path(f, np.array([0.0]))[0]),
                     edge_peaked=bool(i > len(rgrid) // 2))
    print("  surface %d (%s): dn = %.3e, path peaks %.4f mm at r = %.4f mm  [%s]"
          % (f, e["mat"], dn, t[i], rgrid[i],
             "EDGE-PEAKED - biconcave" if EXPECT[f]["edge_peaked"] else "centre-peaked"))
    for lam_um in (0.486, 0.588):
        w = EXPECT[f]["opd_mm"] / (lam_um * 1e-3)
        print("        expected peak %.5f waves = %.4f rad  at lambda %.3f um"
              % (w, 2 * math.pi * w, lam_um))
out["closed_form"] = EXPECT

# ========================================================== connect and load
app = connect()
s = app.PrimarySystem


def load_arm(paths):
    """Load a stress arm exactly the way Runner.cs does in -full mode."""
    assert s.LoadFile(BASE, False)
    # pin the image plane - every arm read on the same footing
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    got = {}
    for f, path in paths.items():
        st = s.LDE.GetSurfaceAt(f).STARData.Stress
        try:
            st.FEAData.UnloadData()
        except Exception:
            pass
        st.SetDataIsLocal()
        st.SetWorkingWavelength(1)
        code = st.FEAData.ImportStress_1(path)
        n = st.FEAData.NumberOfDataPoints
        st.Fits.Refit()
        st.Fits.ApplyStress()
        got[f] = (int(code), int(n))
    return got


def map_peak(f, args=(8, 0, 1, 1.0, 0.0, 0.0, 0.0)):
    """Peak |retardance| off the map, WITH the argmax location and a bracket.

    PeakRetardance() in Runner.cs returns max|R| and the sample count and
    nothing else. A peak with no location cannot be checked against a closed
    form, and a peak with no bracket is a lower bound (gates step 2).
    """
    st = s.LDE.GetSurfaceAt(f).STARData.Stress
    try:
        m = st.Fits.GetRetardanceMap(*args)
    except Exception as ex:
        return dict(err=str(ex), n=0)
    if m is None:
        return dict(err="None", n=0)
    n = len(m)
    if n == 0:
        return dict(err="empty", n=0)
    xs, ys, rs = [], [], []
    for pt in m:
        xs.append(float(pt.X)); ys.append(float(pt.Y))
        rs.append(float(pt.Retardance))
    xs, ys, rs = np.array(xs), np.array(ys), np.array(rs)
    rr = np.hypot(xs, ys)
    i = int(np.argmax(np.abs(rs)))
    rad_max = float(rr.max())
    # "on the edge" = in the outermost 5% of the sampled radius
    on_edge = bool(rr[i] > 0.95 * rad_max) if rad_max > 0 else False
    return dict(n=n, peak=float(rs[i]), peak_abs=float(abs(rs[i])),
                r_at_peak=float(rr[i]), x=float(xs[i]), y=float(ys[i]),
                r_sampled_max=rad_max, r_sampled_min=float(rr.min()),
                on_sample_edge=on_edge,
                span=float(np.abs(rs).max() - np.abs(rs).min()))


# ============================================= 3. decode the shipped arguments
banner("3. WHAT ARE THE SEVEN ARGUMENTS?")
load_arm(ARM_PATHS["uni"])
base_args = [8, 0, 1, 1.0, 0.0, 0.0, 0.0]
print("shipped call (8,0,1,1,0,0,0) on surface 1:")
print("   ", map_peak(1))

dec = {}
for pos, cands in ((0, [1, 2, 4, 6, 8, 10, 12, 16, 20]),
                   (1, [0, 1, 2, 3]),
                   (2, [0, 1, 2, 3]),
                   (3, [0.0, 0.486, 0.5, 1.0, 2.0]),
                   (4, [0.0, 0.5, 1.0]),
                   (5, [0.0, 0.5, 1.0]),
                   (6, [0.0, 0.5, 1.0])):
    print("\n  argument %d:" % pos)
    rows = []
    for c in cands:
        a = list(base_args)
        a[pos] = c
        r = map_peak(1, tuple(a))
        rows.append((c, r))
        print("     %-8s -> n=%-6s peak=%s" %
              (c, r.get("n"),
               ("%.6f rad" % r["peak"]) if r.get("n") else r.get("err", "-")))
    dec[str(pos)] = [(c, {k: v for k, v in r.items()}) for c, r in rows]
out["argument_sweep"] = dec

# GetRetardance(7 doubles) - guess (x,y,z, l,m,n, wavelength) and test it
print("\n  GetRetardance(x,y,z,l,m,n,lambda?) against the closed form:")
st1 = s.LDE.GetSurfaceAt(1).STARData.Stress
gr = {}
for lam in (0.0, 0.486, 0.588, 1.0):
    try:
        v = st1.Fits.GetRetardance(0.0, 0.0, 0.0, 0.0, 0.0, 1.0, lam)
        gr["%.3f" % lam] = float(v)
        w486 = EXPECT[1]["opd_mm"] / 0.486e-3
        w588 = EXPECT[1]["opd_mm"] / 0.588e-3
        print("     7th arg %.3f -> %.6f rad   (closed form: %.4f rad @0.486, "
              "%.4f rad @0.588)"
              % (lam, v, 2 * math.pi * w486, 2 * math.pi * w588))
    except Exception as ex:
        gr["%.3f" % lam] = str(ex)
        print("     7th arg %.3f -> raised %s" % (lam, ex))
out["get_retardance_probe"] = gr

# =============================================== 4. the arms, shipped settings
banner("4. THE CONTROL ARMS THROUGH THE SHIPPED CALL")
print("%-12s %-4s %8s %12s %12s %10s %8s" %
      ("arm", "surf", "points", "peak rad", "peak waves", "r@peak", "on edge"))
arm_res = {}
for tag in ("null", "hydrostatic", "uni_tenth", "uni", "uni_tenx", "real"):
    got = load_arm(ARM_PATHS[tag])
    per = {}
    for f in ELEMS:
        r = map_peak(f)
        per[str(f)] = dict(r, imported=got[f])
        if r.get("n"):
            lam_um = s.SystemData.Wavelengths.GetWavelength(1).Wavelength
            print("%-12s %-4d %8d %12.6f %12.5f %10.4f %8s"
                  % (tag, f, r["n"], r["peak"], r["peak_abs"] / (2 * math.pi),
                     r["r_at_peak"], "YES" if r["on_sample_edge"] else "no"))
        else:
            print("%-12s %-4d %8s %12s" % (tag, f, r.get("n"), r.get("err", "-")))
    arm_res[tag] = per
out["arms"] = arm_res

# ================================================== 5. the density sweep
banner("5. DENSITY SWEEP - THE CONSTANT NOBODY HAS VARIED")
print("Runner.cs hardcodes density 8 and notes only that 16 'returns nothing at")
print("all rather than refusing'. A fixed sampling constant inside the producer")
print("is a sampled domain like any other (gates section B).")
print()
load_arm(ARM_PATHS["uni"])
dens = {}
print("%-6s %-4s %8s %12s %12s %10s" %
      ("dens", "surf", "points", "peak rad", "vs closed", "r@peak"))
for d in range(0, 21):
    for f in ELEMS:
        r = map_peak(f, (d, 0, 1, 1.0, 0.0, 0.0, 0.0))
        dens.setdefault(str(d), {})[str(f)] = r
        if r.get("n"):
            lam = s.SystemData.Wavelengths.GetWavelength(1).Wavelength * 1e-3
            cf = 2 * math.pi * EXPECT[f]["opd_mm"] / lam
            print("%-6d %-4d %8d %12.6f %12.4f %10.4f"
                  % (d, f, r["n"], r["peak"], abs(r["peak"]) / abs(cf) if cf else float("nan"),
                     r["r_at_peak"]))
        else:
            print("%-6d %-4d %8s %12s" % (d, f, r.get("n", 0), r.get("err", "-")))
out["density_sweep"] = dens

json.dump(out, open(os.path.join(HERE, "retctl.json"), "w"), indent=1, default=str)
app.CloseApplication()
print()
print("wrote retctl.json")
print("done")

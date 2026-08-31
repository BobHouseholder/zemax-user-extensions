"""ITEM 3. What actually differs between the two pin orders?

The finding (2026-08-29): an RMS wavefront read at the design plane depends on
WHEN the marginal-ray-height solve is killed, by 0.008160 waves, with the final
thickness identical to the nanometre and byte-identical stress data.

  A  pin the solve, THEN load          plane never moves    -0.007359 waves
  B  load, let the solve move, pin back  plane moves -193 um  +0.000802 waves

Enough to flip the sign of the reported moulding effect. `pinorder.py` checked
the obvious suspect - automatic semi-diameters - and refuted it: they differ in
the sixth decimal, six orders of magnitude too small to matter. It checked
nothing else, so the mechanism has been open since.

THIS SCRIPT STOPS GUESSING AND ENUMERATES THE STATE. It fingerprints everything
that could differ - every surface parameter at full precision, the vignetting
factors, aperture and apodization, ray aiming, the first-order quantities, the
pupil, and the loaded STAR field itself - then prints ONLY the entries that
disagree.

THE CONTROL IS THE POINT. Order A is run TWICE. A-vs-A' must come back empty:
that proves the fingerprint is deterministic and that anything appearing in
A-vs-B is the pin order and not run-to-run noise. Without it, any difference
found here would be uninterpretable.
"""
import json
import os

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\plastic-cooke-MoldStress.zmx"
STRESS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\moldstress"
SURFS = [1, 3, 5]

app = connect()
s = app.PrimarySystem


def op(name, *a):
    """A merit operand by name, or None if this build has no such operand."""
    try:
        return float(s.MFE.GetOperandValue(getattr(E, name), *(list(a) + [0] * (8 - len(a)))))
    except Exception:
        return None


def wfe():
    return float(s.MFE.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0))


def load():
    for f in SURFS:
        st = s.LDE.GetSurfaceAt(f).STARData.Stress
        try:
            st.FEAData.UnloadData()
        except Exception:
            pass
        st.SetDataIsLocal()
        st.SetWorkingWavelength(1)
        st.FEAData.ImportStress_1(os.path.join(STRESS, "moldstress_s%d_stress.txt" % f))
        st.Fits.Refit()
        st.Fits.ApplyStress()


def fingerprint():
    """Everything that could plausibly differ, at FULL precision.

    repr() not round(): the whole finding is that two states look identical at
    the precision anyone prints, so this deliberately prints more than is
    readable and lets the diff pick.
    """
    fp = {}
    ns = s.LDE.NumberOfSurfaces
    for i in range(ns):
        su = s.LDE.GetSurfaceAt(i)
        for attr in ("Radius", "Thickness", "SemiDiameter", "Conic"):
            try:
                fp["surf%d.%s" % (i, attr)] = repr(float(getattr(su, attr)))
            except Exception:
                pass
        try:
            fp["surf%d.Material" % i] = str(su.Material or "")
        except Exception:
            pass
        # the aperture ON the surface, which is not the semi-diameter
        try:
            ta = su.ApertureData.CurrentTypeSettings
            fp["surf%d.ApertureType" % i] = str(su.ApertureData.CurrentType)
        except Exception:
            pass

    flds = s.SystemData.Fields
    fp["field.count"] = flds.NumberOfFields
    for k in range(1, flds.NumberOfFields + 1):
        f = flds.GetField(k)
        for attr in ("X", "Y", "Weight", "VDX", "VDY", "VCX", "VCY", "VAN"):
            try:
                fp["field%d.%s" % (k, attr)] = repr(float(getattr(f, attr)))
            except Exception:
                pass

    ap = s.SystemData.Aperture
    for attr in ("ApertureValue", "ApodizationFactor", "ApertureType",
                 "ApodizationType", "AFocalImageSpace", "SemiDiameterMargin"):
        try:
            v = getattr(ap, attr)
            fp["aperture.%s" % attr] = repr(float(v)) if isinstance(v, (int, float)) else str(v)
        except Exception:
            pass
    for attr in ("RayAiming", "UseRayAiming"):
        try:
            fp["aperture.%s" % attr] = str(getattr(ap, attr))
        except Exception:
            pass

    # first-order and pupil quantities - the wavefront's own references
    for name, args in (("EFFL", (1,)), ("ENPP", (1,)), ("EXPP", (1,)),
                       ("EXPD", (1,)), ("ISFN", (1,)), ("TOTR", ()),
                       ("PIMH", (1,)), ("WFNO", ())):
        v = op(name, *args)
        if v is not None:
            fp["op.%s" % name] = repr(v)

    # real-ray intercepts: chief and marginal, on axis and at full field
    for tag, (hy, py) in (("chief_axis", (0.0, 0.0)), ("marg_axis", (0.0, 1.0)),
                          ("chief_full", (1.0, 0.0)), ("marg_full", (1.0, 1.0))):
        v = op("REAY", s.LDE.NumberOfSurfaces - 1, 1, 0, hy, 0, py)
        if v is not None:
            fp["ray.%s" % tag] = repr(v)

    # the loaded STAR field itself - if this differs, the input differs
    for f in SURFS:
        try:
            st = s.LDE.GetSurfaceAt(f).STARData.Stress
            fp["star%d.points" % f] = str(int(st.FEAData.NumberOfDataPoints))
            pl = st.Fits.GetPointRetardanceList(8, 0, 1)
            peak = max(abs(float(q.Retardance)) for q in pl) if pl else 0.0
            fp["star%d.peak_retardance" % f] = repr(peak)
            fp["star%d.list_len" % f] = str(len(pl) if pl else 0)
        except Exception as ex:
            fp["star%d.error" % f] = str(ex)[:60]

    fp["wfe"] = repr(wfe())
    return fp


def run(pin_first, tag):
    assert s.LoadFile(LENS, False)
    ip = s.LDE.NumberOfSurfaces - 2
    cell = s.LDE.GetSurfaceAt(ip).ThicknessCell
    design = float(s.LDE.GetSurfaceAt(ip).Thickness)
    if pin_first:
        cell.MakeSolveFixed()
    base = wfe()
    load()
    moved = float(s.LDE.GetSurfaceAt(ip).Thickness)
    if not pin_first:
        cell.MakeSolveFixed()
        s.LDE.GetSurfaceAt(ip).Thickness = design
    fp = fingerprint()
    fp["_meta.design"] = repr(design)
    fp["_meta.moved"] = repr(moved)
    fp["_meta.baseline"] = repr(base)
    fp["_meta.change"] = repr(float(fp["wfe"]) - base)
    print("  %-26s design %.9f  after-load %.9f  change %+.6f waves"
          % (tag, design, moved, float(fp["_meta.change"])))
    return fp


def diff(a, b, la, lb):
    keys = sorted(set(a) | set(b))
    out = [(k, a.get(k, "<absent>"), b.get(k, "<absent>"))
           for k in keys if a.get(k) != b.get(k)]
    return out


print("=" * 78)
print("Running order A twice (the CONTROL), then order B")
print("=" * 78)
A1 = run(True, "A  pin first")
A2 = run(True, "A' pin first (control)")
B = run(False, "B  pin after loading")

print()
print("=" * 78)
print("CONTROL — A vs A' must be EMPTY, or nothing below is interpretable")
print("=" * 78)
ctrl = diff(A1, A2, "A", "A'")
if not ctrl:
    print("  clean: %d fingerprint entries, all identical across two runs of the "
          "same order" % len(A1))
else:
    print("  %d ENTRIES DIFFER BETWEEN TWO RUNS OF THE SAME ORDER:" % len(ctrl))
    for k, x, y in ctrl:
        print("    %-34s %s  vs  %s" % (k, x, y))
    print("  -> the fingerprint is not deterministic; the comparison below is noise")

print()
print("=" * 78)
print("THE FINDING — A vs B, over %d fingerprint entries" % len(A1))
print("=" * 78)
d = diff(A1, B, "A", "B")
if not d:
    print("  NOTHING DIFFERS. Two states with identical fingerprints and different")
    print("  wavefronts means the difference is in state this fingerprint does not")
    print("  reach - widen it.")
for k, x, y in d:
    star = "  <<<" if k in ("wfe", "_meta.change") else ""
    print("    %-34s A=%-24s B=%-24s%s" % (k, x[:24], y[:24], star))

json.dump({"A": A1, "A_control": A2, "B": B,
           "control_diffs": ctrl, "diffs": d},
          open(os.path.join(HERE, "pinorder2.json"), "w"), indent=1)
print()
print("%d entries fingerprinted; %d differ between orders, %d between control runs"
      % (len(A1), len(d), len(ctrl)))
print("wrote pinorder2.json")
app.CloseApplication()

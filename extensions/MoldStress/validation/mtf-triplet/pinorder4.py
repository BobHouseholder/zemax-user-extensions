"""ITEM 3. Is it the semi-diameters after all?

Where this stands. The anomaly is real and reproducible: pinning the focus solve
BEFORE loading gives -0.007359 waves, pinning it AFTER gives +0.000802, on
byte-identical stress data and a byte-identical lens file, and the two differ by
0.008160158 waves - enough to flip the sign of the reported moulding effect.

`pinorder3.py` refuted the idea that the probe was to blame: the difference
survives a paraxial operand and a real-ray trace unchanged. Two things differ in
the final state, both semi-diameters:

    surf6  4.842202 vs 4.842207   delta 5e-6 mm
    surf7  8.449104 vs 8.449232   delta 1.28e-4 mm      <- the image surface

The 2026-08-29 note dismissed these as "six orders of magnitude too small to
matter" and looked elsewhere. THAT WAS AN ESTIMATE BY EYE, NOT A MEASUREMENT,
and it is the only surviving suspect - so this measures it instead of judging it.

THE EXPERIMENT IS A SWAP. Reach each order's final state, then force the OTHER
order's semi-diameters onto the two surfaces that differ and re-read the
wavefront. If the wavefront follows the semi-diameters, they are the mechanism
and the dismissal was wrong. If it does not move, they are cleared by
measurement rather than by assertion, and the mechanism is elsewhere.

A control runs first: force each order's OWN values back onto it. That must
leave the wavefront where it was, or the act of setting a semi-diameter is
itself changing something and no swap below can be read.
"""
import json
import os

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\plastic-cooke-MoldStress.zmx"
STRESS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\moldstress"
SURFS = [1, 3, 5]
WATCH = [6, 7]          # the only two that differ between the orders

app = connect()
s = app.PrimarySystem


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


def sd(i):
    return float(s.LDE.GetSurfaceAt(i).SemiDiameter)


def force_sd(i, v):
    """Pin the semi-diameter and set it. Automatic semis are recomputed on the
    next trace, so the cell has to be made fixed or the assignment is undone."""
    su = s.LDE.GetSurfaceAt(i)
    try:
        su.SemiDiameterCell.MakeSolveFixed()
    except Exception:
        pass
    su.SemiDiameter = v


def reach(pin_first):
    assert s.LoadFile(LENS, False)
    ip = s.LDE.NumberOfSurfaces - 2
    cell = s.LDE.GetSurfaceAt(ip).ThicknessCell
    design = float(s.LDE.GetSurfaceAt(ip).Thickness)
    if pin_first:
        cell.MakeSolveFixed()
    wfe()                       # the baseline read, as both harnesses do
    load()
    if not pin_first:
        cell.MakeSolveFixed()
        s.LDE.GetSurfaceAt(ip).Thickness = design
    return {i: sd(i) for i in WATCH}


print("=" * 78)
print("NATIVE STATES")
print("=" * 78)
sdA = reach(True)
wA = wfe()
print("  A  pin first        wfe %.9f   surf6 %.9f  surf7 %.9f" % (wA, sdA[6], sdA[7]))
sdB = reach(False)
wB = wfe()
print("  B  pin after load   wfe %.9f   surf6 %.9f  surf7 %.9f" % (wB, sdB[6], sdB[7]))
delta = wB - wA
print("  delta %+.9f waves" % delta)

print()
print("=" * 78)
print("CONTROL — force each order's OWN values back onto it; nothing may move")
print("=" * 78)
ctrl = {}
for tag, pin, own in (("A", True, sdA), ("B", False, sdB)):
    reach(pin)
    before = wfe()
    for i in WATCH:
        force_sd(i, own[i])
    after = wfe()
    ctrl[tag] = after - before
    print("  %s  %.9f -> %.9f   moved %+.2e  %s"
          % (tag, before, after, after - before,
             "clean" if abs(after - before) < 1e-9 else "SETTING A SEMI-DIAMETER IS NOT NEUTRAL"))
ctrl_ok = all(abs(v) < 1e-9 for v in ctrl.values())

print()
print("=" * 78)
print("THE SWAP — force the OTHER order's semi-diameters and re-read")
print("=" * 78)
res = {}
for tag, pin, other, native, target in (("A<-B", True, sdB, wA, wB),
                                        ("B<-A", False, sdA, wB, wA)):
    reach(pin)
    before = wfe()
    for i in WATCH:
        force_sd(i, other[i])
    after = wfe()
    res[tag] = {"before": before, "after": after, "moved": after - before,
                "target": target, "closed": abs(after - target)}
    frac = (after - before) / (target - before) if abs(target - before) > 1e-12 else float("nan")
    print("  %-5s %.9f -> %.9f   moved %+.9f of the %+.9f needed  (%.1f%%)"
          % (tag, before, after, after - before, target - before, 100 * frac))

print()
print("=" * 78)
print("VERDICT")
print("=" * 78)
if not ctrl_ok:
    print("  UNREADABLE: setting a semi-diameter to its own value moved the wavefront,")
    print("  so the swap cannot be attributed to the value rather than the act.")
else:
    moved = max(abs(res[k]["moved"]) for k in res)
    need = abs(delta)
    frac = moved / need if need else 0.0
    print("  control clean: re-setting a semi-diameter to its own value moves nothing")
    print("  swap moves at most %.9f waves of the %.9f that separates the orders (%.1f%%)"
          % (moved, need, 100 * frac))
    if frac > 0.8:
        print("  -> THE SEMI-DIAMETERS ARE THE MECHANISM. The 2026-08-29 dismissal was")
        print("     an estimate by eye and it was wrong.")
    elif frac < 0.05:
        print("  -> THE SEMI-DIAMETERS ARE CLEARED, by measurement rather than by eye.")
        print("     They differ, and forcing them across does not move the wavefront.")
        print("     The mechanism is elsewhere and the only named suspect is now gone.")
    else:
        print("  -> PARTIAL: they carry some of it. Neither a cause nor a coincidence.")

json.dump({"wA": wA, "wB": wB, "delta": delta, "sdA": sdA, "sdB": sdB,
           "control": ctrl, "swap": res}, open(os.path.join(HERE, "pinorder4.json"), "w"),
          indent=1, default=str)
print()
print("wrote pinorder4.json")
app.CloseApplication()

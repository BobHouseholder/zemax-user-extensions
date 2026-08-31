"""ITEM 3, and the probe that found it was destroying the thing it measured.

`pinorder2.py` fingerprinted 105 pieces of state and reported the two pin orders
agreeing to 7e-13 waves - i.e. no anomaly. `pinorder.py`, re-run unmodified
minutes later on the same machine, the same byte-identical stress data and the
same byte-identical lens file, still reproduced it exactly: -0.007359 against
+0.000802, delta 0.008160 waves.

THE DIFFERENCE IS THE INSTRUMENT. pinorder.py reads RWRE immediately after
ApplyStress(). pinorder2.py evaluates about fifteen merit operands - EFFL, EXPP,
ISFN, four REAY ray traces - BEFORE it reads RWRE, and every one of those forces
a trace. The fingerprint erased the effect it was built to explain, and reported
the erasure as "no anomaly".

So the anomaly lives in state that is STALE immediately after ApplyStress() and
is refreshed by the next thing that traces a ray. This script measures exactly
that: the wavefront is read at four stages per order, with nothing between the
load and the first read.

  w0  immediately after ApplyStress()
  w1  read again, nothing in between        - is the value even stable?
  w2  after ONE paraxial operand (EFFL)
  w3  after ONE real-ray operand (REAY)

The stage at which the two orders converge names what is stale.
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


def wfe():
    return float(s.MFE.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0))


def effl():
    return float(s.MFE.GetOperandValue(E.EFFL, 1, 0, 0, 0, 0, 0, 0, 0))


def reay():
    return float(s.MFE.GetOperandValue(E.REAY, s.LDE.NumberOfSurfaces - 1, 1,
                                       0, 0.0, 0, 1.0, 0, 0))


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


def semis():
    return [round(float(s.LDE.GetSurfaceAt(i).SemiDiameter), 6)
            for i in range(s.LDE.NumberOfSurfaces)]


def run(pin_first, tag):
    assert s.LoadFile(LENS, False)
    ip = s.LDE.NumberOfSurfaces - 2
    cell = s.LDE.GetSurfaceAt(ip).ThicknessCell
    design = float(s.LDE.GetSurfaceAt(ip).Thickness)
    if pin_first:
        cell.MakeSolveFixed()
    base = wfe()
    load()
    if not pin_first:
        cell.MakeSolveFixed()
        s.LDE.GetSurfaceAt(ip).Thickness = design

    r = {"tag": tag, "baseline": base}
    r["sd0"] = semis()
    r["w0"] = wfe()                      # nothing between the load and this
    r["w1"] = wfe()                      # again, with nothing in between
    r["sd1"] = semis()
    r["effl"] = effl()                   # one paraxial operand
    r["w2"] = wfe()
    r["sd2"] = semis()
    r["reay"] = reay()                   # one real-ray operand
    r["w3"] = wfe()
    r["sd3"] = semis()
    print("  %-24s w0 %.9f  w1 %.9f  w2 %.9f  w3 %.9f"
          % (tag, r["w0"], r["w1"], r["w2"], r["w3"]))
    return r


print("=" * 78)
print("The wavefront at four stages, with NOTHING between the load and w0")
print("=" * 78)
A = run(True, "A  pin first")
B = run(False, "B  pin after load")

print()
print("=" * 78)
print("WHERE DO THE TWO ORDERS CONVERGE?")
print("=" * 78)
conv = None
for k, what in (("w0", "immediately after ApplyStress()"),
                ("w1", "read a second time, nothing in between"),
                ("w2", "after ONE paraxial operand (EFFL)"),
                ("w3", "after ONE real-ray operand (REAY)")):
    d = B[k] - A[k]
    agree = abs(d) < 1e-9
    if agree and conv is None:
        conv = k
    print("  %-3s %-42s A %.9f  B %.9f  delta %+.9f  %s"
          % (k, what, A[k], B[k], d, "AGREE" if agree else "DIFFER"))

print()
if conv is None:
    print("  The two orders NEVER converge across these four stages - the difference")
    print("  is not erased by a trace, and the instrument hypothesis is REFUTED.")
else:
    print("  They converge at %s: whatever RWRE reads is STALE until then." % conv)
    print("  That is why a fingerprint which probes first sees no anomaly - the")
    print("  probing IS the refresh.")

print()
print("=" * 78)
print("AND WHAT MOVED WHEN THEY CONVERGED?")
print("=" * 78)
for stage in ("sd0", "sd1", "sd2", "sd3"):
    same = A[stage] == B[stage]
    print("  %-4s semi-diameters identical across orders: %s" % (stage, same))
    if not same:
        for i, (x, y) in enumerate(zip(A[stage], B[stage])):
            if x != y:
                print("        surf%d  A=%.6f  B=%.6f  delta %+.6f" % (i, x, y, y - x))

json.dump({"A": A, "B": B, "converge_at": conv},
          open(os.path.join(HERE, "pinorder3.json"), "w"), indent=1)
print()
print("wrote pinorder3.json")
app.CloseApplication()

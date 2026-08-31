"""ITEM 3, resolved. Which ACT moves it, and on which surface?

`pinorder4.py`'s control was supposed to be a formality and it is the whole
finding. In state A - pin the focus solve BEFORE loading - forcing the
semi-diameters to THEIR OWN, UNCHANGED VALUES moved the wavefront the entire
0.008160158 waves, onto state B's answer. In state B the same operation moves
nothing.

So it is not the VALUE. The values differ only in the sixth decimal, which is
what the 2026-08-29 note correctly observed and then wrongly concluded from. It
is the ACT: something in state A is unresolved, and resolving it produces B.

Note what does NOT resolve it - `pinorder3.py` showed a paraxial operand and a
real-ray trace both leave the difference untouched. So this is not "the system
was not updated".

This script separates act from value, and surface from surface:

  1  baseline in state A, nothing done
  2  MakeSolveFixed on surf6's semi-diameter ONLY, no assignment
  3  MakeSolveFixed on surf7's semi-diameter ONLY, no assignment
  4  assign surf7's own value with NO MakeSolveFixed
  5  MakeSolveFixed on both, no assignment

Each arm is a fresh reach of state A, so the arms cannot contaminate each other.
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


def reach_A():
    """State A: pin the focus solve BEFORE loading."""
    assert s.LoadFile(LENS, False)
    ip = s.LDE.NumberOfSurfaces - 2
    s.LDE.GetSurfaceAt(ip).ThicknessCell.MakeSolveFixed()
    wfe()
    load()


TARGET_B = 0.132978429      # state B's answer, from pinorder3/4
arms = []


def arm(name, action):
    reach_A()
    before = wfe()
    action()
    after = wfe()
    moved = after - before
    frac = moved / (TARGET_B - before) if abs(TARGET_B - before) > 1e-12 else 0.0
    arms.append({"arm": name, "before": before, "after": after,
                 "moved": moved, "frac_of_gap": frac})
    print("  %-46s %.9f -> %.9f  %+.9f  (%.0f%% of the gap)"
          % (name, before, after, moved, 100 * frac))


def pin(i):
    s.LDE.GetSurfaceAt(i).SemiDiameterCell.MakeSolveFixed()


print("=" * 78)
print("STATE A, then one act per arm — each arm reaches A fresh")
print("=" * 78)
arm("1  nothing", lambda: None)
arm("2  MakeSolveFixed on surf6 semi-dia, no assignment", lambda: pin(6))
arm("3  MakeSolveFixed on surf7 semi-dia, no assignment", lambda: pin(7))


def assign_only():
    su = s.LDE.GetSurfaceAt(7)
    su.SemiDiameter = float(su.SemiDiameter)   # its own value, cell left automatic


arm("4  assign surf7 its OWN value, cell left automatic", assign_only)
arm("5  MakeSolveFixed on BOTH, no assignment", lambda: (pin(6), pin(7)))

print()
print("=" * 78)
print("VERDICT")
print("=" * 78)
null = arms[0]["moved"]
print("  control arm (nothing done) moves %+.2e - %s"
      % (null, "clean" if abs(null) < 1e-9 else "NOT CLEAN, the rest is unreadable"))
if abs(null) < 1e-9:
    for a in arms[1:]:
        verdict = ("CARRIES IT" if a["frac_of_gap"] > 0.8 else
                   "no effect" if abs(a["moved"]) < 1e-9 else "partial")
        print("  %-46s %s" % (a["arm"], verdict))
    movers = [a for a in arms[1:] if a["frac_of_gap"] > 0.8]
    if movers:
        print()
        print("  MECHANISM: %s" % movers[0]["arm"][3:])
        print("  The wavefront in state A is computed against an aperture that is not")
        print("  the one SemiDiameter reads back. Resolving the cell publishes it, and")
        print("  the answer becomes state B's. The values were never the difference -")
        print("  they agree to the sixth decimal, which is exactly why comparing the")
        print("  VALUES cleared a suspect that was guilty.")

json.dump(arms, open(os.path.join(HERE, "pinorder5.json"), "w"), indent=1)
print()
print("wrote pinorder5.json")
app.CloseApplication()

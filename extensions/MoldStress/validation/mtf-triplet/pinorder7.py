"""Does RefreshAfterStarLoad actually refresh? Or did I ship a no-op?

Runner.cs now calls a helper that assigns SURFACE 1's thickness to its own value
before the first measurement, on the strength of pinorder6 arm 1 - where
"surface 2 Thickness = its own value" carried 100% of the gap.

The rebuilt tool then produced numbers BIT-IDENTICAL to the previous build,
including the `movedWfe` reading the fix was aimed at. Two explanations fit and
only one is acceptable:

  (a) this code path already wrote to the LDE somewhere between ApplyStress()
      and the read, so it was never stale and the helper is harmless insurance;
  (b) the helper does nothing - surface 1 is not surface 2, and an assignment
      there is rejected, optimised away, or lands on a solve-driven cell.

(b) would mean shipping a no-op under a comment claiming a measured mechanism,
which is worse than shipping nothing. This tests the helper's EXACT operation,
on the exact surface it uses, in state A where the gap is known to exist.
"""
import json
import os

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\plastic-cooke-MoldStress.zmx"
STRESS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\moldstress"
SURFS = [1, 3, 5]
TARGET_B = 0.132978429

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
    assert s.LoadFile(LENS, False)
    s.LDE.GetSurfaceAt(s.LDE.NumberOfSurfaces - 2).ThicknessCell.MakeSolveFixed()
    wfe()
    load()


out = []


def arm(name, action):
    reach_A()
    before = wfe()
    action()
    after = wfe()
    moved = after - before
    frac = moved / (TARGET_B - before) if abs(TARGET_B - before) > 1e-12 else 0.0
    out.append({"arm": name, "moved": moved, "frac": frac})
    print("  %-46s %+.9f  (%3.0f%% of the gap)" % (name, moved, 100 * frac))


def own_thk(i):
    su = s.LDE.GetSurfaceAt(i)
    su.Thickness = su.Thickness      # EXACTLY what the helper does


print("=" * 78)
print("The helper's exact operation, per surface")
print("=" * 78)
arm("control: nothing", lambda: None)
for i in (1, 2, 3, 4, 5):
    arm("surface %d Thickness = its own value" % i, lambda i=i: own_thk(i))

print()
print("=" * 78)
print("VERDICT")
print("=" * 78)
ctrl, s1 = out[0], out[1]
if abs(ctrl["moved"]) > 1e-9:
    print("  control not clean; unreadable")
elif s1["frac"] > 0.8:
    print("  SURFACE 1 REFRESHES. The helper does what its comment claims, and the")
    print("  tool's numbers were bit-identical because this path already wrote to")
    print("  the LDE before the read - the helper is insurance, not a correction.")
else:
    print("  SURFACE 1 DOES NOT REFRESH (%+.2e, %.0f%% of the gap)."
          % (s1["moved"], 100 * s1["frac"]))
    works = [a for a in out[1:] if a["frac"] > 0.8]
    print("  THE HELPER AS SHIPPED IS A NO-OP and its comment overclaims.")
    if works:
        print("  Surfaces that DO refresh: %s"
              % ", ".join(a["arm"].split()[1] for a in works))
        print("  -> point the helper at one of those and re-verify.")
    else:
        print("  NO surface refreshes via a thickness self-assignment here, so the")
        print("  pinorder6 result does not generalise and the helper needs a")
        print("  different operation entirely.")

json.dump(out, open(os.path.join(HERE, "pinorder7.json"), "w"), indent=1)
print()
print("wrote pinorder7.json")
app.CloseApplication()

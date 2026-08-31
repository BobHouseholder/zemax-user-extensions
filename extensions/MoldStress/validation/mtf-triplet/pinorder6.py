"""ITEM 3, the last discriminator: is it the APERTURE, or is it ANY write?

`pinorder5.py` found that in state A every arm touching the lens data editor
moves the wavefront the full 0.008160158 waves onto state B's answer - including
arm 4, which assigned a surface its OWN unchanged value with the cell left
automatic. The control, which does nothing, moves exactly zero. And
`pinorder3.py` had already shown that merit-operand READS - a paraxial EFFL, a
real-ray REAY - move nothing at all.

Reads do not refresh; writes do. But every write tested so far was to a
SEMI-DIAMETER, so "the aperture is stale" and "any write refreshes" both fit.
They are different claims and only one is true.

This separates them by writing to things that have nothing to do with the
aperture: a lens thickness set to its own value, a radius set to its own value,
and a comment string. If those also carry the full gap, the aperture is
incidental and the mechanism is that RWRE after ApplyStress() is served from
state that only a WRITE invalidates.
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
    ip = s.LDE.NumberOfSurfaces - 2
    s.LDE.GetSurfaceAt(ip).ThicknessCell.MakeSolveFixed()
    wfe()
    load()


arms = []


def arm(name, action):
    reach_A()
    before = wfe()
    try:
        action()
        err = ""
    except Exception as ex:
        err = str(ex)[:50]
    after = wfe()
    moved = after - before
    frac = moved / (TARGET_B - before) if abs(TARGET_B - before) > 1e-12 else 0.0
    arms.append({"arm": name, "moved": moved, "frac": frac, "error": err})
    print("  %-48s %+.9f  (%3.0f%% of the gap)%s"
          % (name, moved, 100 * frac, ("  [" + err + "]") if err else ""))


def own_thickness(i):
    su = s.LDE.GetSurfaceAt(i)
    su.Thickness = float(su.Thickness)


def own_radius(i):
    su = s.LDE.GetSurfaceAt(i)
    su.Radius = float(su.Radius)


def set_comment():
    s.LDE.GetSurfaceAt(2).Comment = "refresh probe"


print("=" * 78)
print("Writes that have NOTHING to do with the aperture")
print("=" * 78)
arm("0  control: nothing", lambda: None)
arm("1  surface 2 Thickness = its own value", lambda: own_thickness(2))
arm("2  surface 4 Radius = its own value", lambda: own_radius(4))
arm("3  surface 2 Comment = a string", set_comment)

print()
print("=" * 78)
print("VERDICT")
print("=" * 78)
ctrl = arms[0]
if abs(ctrl["moved"]) > 1e-9:
    print("  control is not clean; unreadable")
else:
    carried = [a for a in arms[1:] if a["frac"] > 0.8]
    inert = [a for a in arms[1:] if abs(a["moved"]) < 1e-9]
    print("  control clean (moves 0). %d of %d non-aperture writes carry the FULL gap;"
          % (len(carried), len(arms) - 1))
    print("  %d move nothing." % len(inert))
    print()
    if carried:
        print("  MECHANISM, stated as narrowly as the evidence allows:")
        print("    After ApplyStress(), RWRE is served from state that a merit-operand")
        print("    READ does not invalidate. ANY WRITE to the lens data editor does -")
        print("    a thickness or a radius set to its own value, or a comment string,")
        print("    each moves the wavefront the entire 0.008160158 waves.")
        print()
        print("    So the aperture was never the cause. The two pin orders differ")
        print("    because order B WRITES (it restores the thickness) and order A")
        print("    never writes after loading. B's +0.000802 is the refreshed value;")
        print("    A's -0.007359 is stale, and the tool has been right all along.")
    else:
        print("  No non-aperture write carries it: the aperture IS specifically")
        print("  implicated, and the semi-diameter finding stands as the mechanism.")

json.dump(arms, open(os.path.join(HERE, "pinorder6.json"), "w"), indent=1)
print()
print("wrote pinorder6.json")
app.CloseApplication()

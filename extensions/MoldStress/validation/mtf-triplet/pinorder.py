"""Why does pinning the image plane BEFORE loading give a different wavefront
than pinning it AFTER?

The tool and my harness both measure "at the design plane" and disagree:
+0.000802 waves against -0.007359, on byte-identical inputs, both reproducible.
The only structural difference is WHEN the marginal-ray-height solve is killed.

  TOOL      baseline with the solve LIVE -> load stress -> the solve moves the
            plane -80.8 um -> MakeSolveFixed and force Thickness = planeDesign
            -> read.
  HARNESS   MakeSolveFixed FIRST -> baseline -> load stress (plane cannot move)
            -> read.

Both end with thickness = planeDesign. If they still differ, something OTHER
than the thickness changed while the plane was allowed to move - and the
suspect is the AUTOMATIC SEMI-DIAMETERS, which are computed from ray heights
and do not go back when the thickness does. A different aperture is a different
wavefront, at the same plane.

Prints both paths side by side with every semi-diameter, so the answer is data
rather than another story.
"""
import os

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\plastic-cooke-MoldStress.zmx"
STRESS = r"C:\Users\Shadow\Documents\Zemax\Samples\MoldStress\moldstress"
SURFS = [1, 3, 5]

app = connect()
s = app.PrimarySystem


def wfe():
    return s.MFE.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0)


def semis():
    return [round(float(s.LDE.GetSurfaceAt(i).SemiDiameter), 6)
            for i in range(s.LDE.NumberOfSurfaces)]


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


def run(pin_first, tag):
    assert s.LoadFile(LENS, False)
    n = s.LDE.NumberOfSurfaces
    ip = n - 2
    cell = s.LDE.GetSurfaceAt(ip).ThicknessCell
    design = float(s.LDE.GetSurfaceAt(ip).Thickness)
    sd0 = semis()
    if pin_first:
        cell.MakeSolveFixed()
    base = wfe()
    load()
    moved = float(s.LDE.GetSurfaceAt(ip).Thickness)
    sd_after_load = semis()
    if not pin_first:
        cell.MakeSolveFixed()
        s.LDE.GetSurfaceAt(ip).Thickness = design
    got = wfe()
    print("--- %s" % tag)
    print("    design plane      %.6f mm" % design)
    print("    after loading     %.6f mm  (moved %+.1f um)"
          % (moved, (moved - design) * 1000.0))
    print("    final thickness   %.6f mm" % float(s.LDE.GetSurfaceAt(ip).Thickness))
    print("    baseline          %.6f waves" % base)
    print("    loaded            %.6f waves" % got)
    print("    change            %+.6f waves" % (got - base))
    print("    semi-dia at load  %s" % sd0)
    print("    semi-dia after    %s" % sd_after_load)
    print("    semi-dia final    %s" % semis())
    return base, got, sd0, semis()


b1, g1, s1a, s1b = run(True, "HARNESS: pin BEFORE loading")
print()
b2, g2, s2a, s2b = run(False, "TOOL: load, let the solve move, then pin back")
print()
print("=" * 70)
print("baseline agrees : %s (%.6f vs %.6f)" % (abs(b1 - b2) < 1e-9, b1, b2))
print("loaded differs  : %.6f vs %.6f   delta %.6f waves" % (g1, g2, g2 - g1))
same_sd = (s1b == s2b)
print("final semi-diameters identical: %s" % same_sd)
if not same_sd:
    print("  harness:", s1b)
    print("  tool   :", s2b)
    print("  -> the aperture, not the plane, is what differs")
else:
    print("  -> semi-diameters are NOT the mechanism; look elsewhere")
app.CloseApplication()
print("done")

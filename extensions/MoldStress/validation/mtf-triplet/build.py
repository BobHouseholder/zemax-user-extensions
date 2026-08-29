"""Build the test article: a spherical plastic Cooke triplet, PMMA / POLYSTYR /
PMMA. EFL 30 mm, F/4.5, +/-9 deg, F-d-C.

Deliberate choices:
  * ORDINARY catalogue names (PMMA, POLYSTYR) so MoldStress's own conversion
    path is what runs - this is a test of the shipped tool, not of a lens
    hand-built in MS_* glasses.
  * SPHERICAL surfaces, so the non-spherical gate does not fire and the run is
    the ordinary path a user would take.
  * Thicknesses bounded to MOULDABLE values (glass 2.0-5.0 mm, air >= 0.5 mm)
    rather than fixed or free. Free ran away to negative thickness; fixed left
    the optimiser nothing to trade and it parked three airspaces on the same
    value with a 0.44 merit function.
  * Global optimisation, not DLS from one seed. A plastic triplet at n=1.49/
    1.59 is a different structure from the glass Cooke it is seeded from, and
    local descent kept the seed's topology.
  * Mechanical semi-diameters 1.0 mm larger than optical, i.e. a mounting
    flange - every moulded lens has one, and the export sizes the part from it.
"""
import os
from zos import ZOSAPI, connect, HERE

app = connect()
s = app.PrimarySystem
s.New(False)
s.SystemData.MaterialCatalogs.AddCatalog("MISC")

EFL, FNO, HFOV = 30.0, 4.5, 9.0
ap = s.SystemData.Aperture
ap.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter
ap.ApertureValue = EFL / FNO

s.SystemData.Wavelengths.SelectWavelengthPreset(
    ZOSAPI.SystemData.WavelengthPreset.FdC_Visible)

f = s.SystemData.Fields
f.SetFieldType(ZOSAPI.SystemData.FieldType.Angle)
while f.NumberOfFields > 1:
    f.RemoveField(f.NumberOfFields)
f.GetField(1).Y = 0.0
f.AddField(0.0, 0.7 * HFOV, 1.0)
f.AddField(0.0, HFOV, 1.0)

lde = s.LDE
while lde.NumberOfSurfaces < 9:
    lde.InsertNewSurfaceAt(1)

# 0 OBJ | 1-2 PMMA + | 3-4 POLYSTYR - | 5 STOP | 6-7 PMMA + | 8 IMA
seed = [
    (1,  13.21,  3.00, "PMMA"),
    (2, -261.5,  3.60, ""),
    (3, -13.33,  2.00, "POLYSTYR"),
    (4,  12.18,  2.86, ""),
    (5,  0.0,    1.78, ""),
    (6,  47.81,  3.00, "PMMA"),
    (7, -11.04, 25.00, ""),
]
for i, r, t, m in seed:
    row = lde.GetSurfaceAt(i)
    row.Radius = r
    row.Thickness = t
    row.Material = m
lde.GetSurfaceAt(0).Thickness = 1.0e10
lde.GetSurfaceAt(5).IsStop = True

# Back focus on a marginal-ray-height solve, so it can never go negative and
# the image plane follows the design instead of being optimised into it.
cell = lde.GetSurfaceAt(7).ThicknessCell
sv = cell.CreateSolveType(ZOSAPI.Editors.SolveType.MarginalRayHeight)
sv._S_MarginalRayHeight.Height = 0.0
sv._S_MarginalRayHeight.Pupil = 0.0
cell.SetSolveData(sv)

# --- merit function -------------------------------------------------------
mfe = s.MFE
wiz = mfe.SEQOptimizationWizard
wiz.Type = 0            # RMS
wiz.Data = 1            # wavefront
wiz.Reference = 0       # centroid
wiz.Ring = 2
wiz.Arm = 0
wiz.IsGQ = True
wiz.IsAirUsed = True
wiz.AirMin = 0.50
wiz.AirMax = 14.00
wiz.AirEdge = 0.50
wiz.IsGlassUsed = True
wiz.GlassMin = 2.00
wiz.GlassMax = 5.00
wiz.GlassEdge = 0.80
wiz.OverallWeight = 1.0
wiz.Apply()
print("merit operands after wizard:", mfe.NumberOfOperands)

op = mfe.InsertNewOperandAt(1)
op.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EFFL)
op.Target = EFL
op.Weight = 5.0

# --- variables: curvatures AND thicknesses (bounded above) ----------------
for i in (1, 2, 3, 4, 6, 7):
    lde.GetSurfaceAt(i).RadiusCell.MakeSolveVariable()
for i in (1, 2, 3, 4, 5, 6):
    lde.GetSurfaceAt(i).ThicknessCell.MakeSolveVariable()

print("start merit %.6g" % mfe.CalculateMeritFunction())

lo = s.Tools.OpenLocalOptimization()
lo.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
lo.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
lo.NumberOfCores = 8
lo.RunAndWaitForCompletion()
print("after DLS    %.6g" % lo.CurrentMeritFunction)
lo.Close()

gl = s.Tools.OpenGlobalOptimization()
gl.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
gl.NumberOfCores = 8
gl.NumberToSave = ZOSAPI.Tools.Optimization.OptimizationSaveCount.Save_10
gl.RunAndWaitWithTimeout(150.0)
gl.Cancel()
gl.WaitForCompletion()
gl.Close()
print("after Global %.6g" % mfe.CalculateMeritFunction())

lo = s.Tools.OpenLocalOptimization()
lo.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
lo.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
lo.NumberOfCores = 8
lo.RunAndWaitForCompletion()
print("after DLS2   %.6g" % lo.CurrentMeritFunction)
lo.Close()

ham = s.Tools.OpenHammerOptimization()
ham.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
ham.NumberOfCores = 8
ham.RunAndWaitWithTimeout(90.0)
ham.Cancel()
ham.WaitForCompletion()
ham.Close()
print("after Hammer %.6g" % mfe.CalculateMeritFunction())

# --- freeze: remove variables, add the moulding flange --------------------
for i in range(1, lde.NumberOfSurfaces - 1):
    row = lde.GetSurfaceAt(i)
    try:
        row.RadiusCell.MakeSolveFixed()
    except Exception:
        pass
    if i != 7:
        try:
            row.ThicknessCell.MakeSolveFixed()
        except Exception:
            pass

for i in (1, 2, 3, 4, 6, 7):
    row = lde.GetSurfaceAt(i)
    row.MechanicalSemiDiameter = row.SemiDiameter + 1.0

out = os.path.join(HERE, "plastic-triplet.zmx")
s.SaveAs(out)
print("saved", out, os.path.exists(out))

print("\n--- prescription ---")
for i in range(0, lde.NumberOfSurfaces):
    row = lde.GetSurfaceAt(i)
    print("%2d  R %12.4f  t %9.4f  %-10s  sd %7.3f  mech %7.3f" % (
        i, row.Radius, row.Thickness, row.Material or "-",
        row.SemiDiameter, row.MechanicalSemiDiameter))

mf = s.MFE
for t, lbl in ((ZOSAPI.Editors.MFE.MeritOperandType.EFFL, "EFFL"),
               (ZOSAPI.Editors.MFE.MeritOperandType.TOTR, "TOTR"),
               (ZOSAPI.Editors.MFE.MeritOperandType.ISFN, "ISFN")):
    print("%-5s %.4f" % (lbl, mf.GetOperandValue(t, 0, 1, 0, 0, 0, 0, 0, 0)))
for hy, lbl in ((0.0, "0.0 deg"), (0.7, "6.3 deg"), (1.0, "9.0 deg")):
    print("RWRE Hy=%.1f (%-8s) %.6f waves" % (hy, lbl, mf.GetOperandValue(
        ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 2, 0.0, hy, 0, 0, 0, 0)))

# FFT MTF of the as-designed lens - is this actually an imaging system?
an = s.Analyses.New_FftMtf()
st = an.GetSettings()
st.MaximumFrequency = 100.0
st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
an.ApplyAndWaitForCompletion()
r = an.GetResults()
print("\nFFT MTF series:", r.NumberOfDataSeries)
for k in range(r.NumberOfDataSeries):
    ds = r.GetDataSeries(k)
    x = list(ds.XData.Data)
    y = ds.YData.Data
    for target in (20.0, 40.0):
        j = min(range(len(x)), key=lambda q: abs(x[q] - target))
        print("  series %d  %5.1f lp/mm   T %.4f  S %.4f" % (
            k, x[j], y.GetValue(j, 0), y.GetValue(j, 1)))
an.Close()
app.CloseApplication()
print("done")

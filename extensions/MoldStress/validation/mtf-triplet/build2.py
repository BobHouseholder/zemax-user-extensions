"""Rebuild the test article as a genuine plastic Cooke triplet.

The first attempt used GLOBAL optimisation and left the Cooke basin entirely:
it came out positive/negative/NEGATIVE with a meniscus middle element, an
unmountable 0.50 mm airgap and a 62.9 deg surface slope. Better MTF, not a
manufacturable lens, and not the form it claimed to be.

This one is a transcription of the shipped glass sample:
  Samples/Sequential/Objectives/Cooke 40 degree field.zmx
scaled 0.8 (EFL 50 -> 40), SK16 -> PMMA and F2 -> POLYSTYR, stop kept on the
back of the negative element where the sample puts it.

Three things keep the FORM rather than hoping for it:
  * LOCAL optimisation only. No global, no hammer - both hop basins, and
    hopping basins is exactly what broke the first attempt.
  * Explicit curvature-SIGN constraints on all six surfaces, matching the
    glass reference surface for surface (+ - / - + / + -). A Cooke triplet
    with the middle element's signs flipped is not a Cooke triplet.
  * Moulding bounds as merit operands, not as an afterthought: centre and edge
    thickness per element, minimum air centre and edge, and a curvature
    magnitude cap that holds every surface slope under ~42 deg.

Wavelengths are F-d-C rather than the sample's 0.48/0.55/0.65 for one specific
reason: MoldStress writes the d-line index, so the null control is only exact
at 0.5876 um. Keeping d in the set keeps that control available.
"""
import math, os
from zos import ZOSAPI, connect, HERE

GLASS = os.path.expanduser(
    r"~\Documents\Zemax\Samples\Sequential\Objectives"
    r"\Cooke 40 degree field.zmx")
SCALE = 0.8                       # EFL 50 -> 40
EFL, FNO, HFOV = 40.0, 5.0, 20.0

app = connect()
s = app.PrimarySystem

# ---- read the reference -------------------------------------------------
assert s.LoadFile(GLASS, False), GLASS
ref = []
for i in range(s.LDE.NumberOfSurfaces):
    q = s.LDE.GetSurfaceAt(i)
    ref.append({"R": q.Radius, "t": q.Thickness,
                "mat": (q.Material or "").strip(), "stop": q.IsStop})
print("glass reference, %d surfaces" % len(ref))
for i, r in enumerate(ref):
    print("  %2d  R %10.3f  t %8.3f  %-6s %s"
          % (i, r["R"], r["t"], r["mat"], "STOP" if r["stop"] else ""))

SWAP = {"SK16": "PMMA", "F2": "POLYSTYR"}

# ---- build the plastic transcription ------------------------------------
s.New(False)
s.SystemData.MaterialCatalogs.AddCatalog("MISC")
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
while lde.NumberOfSurfaces < len(ref):
    lde.InsertNewSurfaceAt(1)

# PMMA is n 1.4917 against SK16's 1.620, so the positive elements need more
# curvature for the same power: R scales by (n_g - 1)/(n_p - 1) ~ 0.79. Seeding
# with that lands DLS near a solution instead of asking it to travel.
IDX = {"PMMA": (0.620 / 0.4917), "POLYSTYR": (0.633 / 0.5905)}
for i, r in enumerate(ref):
    row = lde.GetSurfaceAt(i)
    mat = SWAP.get(r["mat"], r["mat"])
    k = 1.0
    if i > 0 and ref[i]["mat"] in SWAP:
        k = 1.0 / IDX[SWAP[ref[i]["mat"]]]
    elif i > 0 and ref[i - 1]["mat"] in SWAP:
        k = 1.0 / IDX[SWAP[ref[i - 1]["mat"]]]
    row.Radius = r["R"] * SCALE * k if abs(r["R"]) < 1e9 else r["R"]
    row.Thickness = r["t"] * SCALE if r["t"] < 1e9 else r["t"]
    row.Material = mat
    if r["stop"]:
        row.IsStop = True
lde.GetSurfaceAt(0).Thickness = 1.0e10

# element surface groups, read back from the built system
ELS = [(1, 2), (3, 4), (5, 6)]
# the sample's own signs, surface by surface: this is the form being preserved
SIGNS = {1: +1, 2: -1, 3: -1, 4: +1, 5: +1, 6: -1}
for k, v in SIGNS.items():
    assert (ref[k]["R"] > 0) == (v > 0), "sign table disagrees with the sample"

# E2 scaled to 0.80 mm, which no moulder will run; open it to a floor of 1.4
lde.GetSurfaceAt(3).Thickness = max(lde.GetSurfaceAt(3).Thickness, 1.6)

# back focus on a marginal-ray-height solve
cell = lde.GetSurfaceAt(6).ThicknessCell
sv = cell.CreateSolveType(ZOSAPI.Editors.SolveType.MarginalRayHeight)
sv._S_MarginalRayHeight.Height = 0.0
sv._S_MarginalRayHeight.Pupil = 0.0
cell.SetSolveData(sv)

# ---- merit function ------------------------------------------------------
mfe = s.MFE
wiz = mfe.SEQOptimizationWizard
wiz.Type = 0
wiz.Data = 1            # RMS wavefront
wiz.Reference = 0
wiz.Ring = 3
wiz.Arm = 0
wiz.IsGQ = True
wiz.Apply()
print("wizard operands:", mfe.NumberOfOperands)

MC = ZOSAPI.Editors.MFE.MeritColumn
MT = ZOSAPI.Editors.MFE.MeritOperandType
row_at = [1]


def addop(t, target, weight, **params):
    op = mfe.InsertNewOperandAt(row_at[0])
    row_at[0] += 1
    op.ChangeType(t)
    for name, val in params.items():
        op.GetOperandCell(getattr(MC, name)).IntegerValue = val
    op.Target = target
    op.Weight = weight
    return op


# focal length first - everything else is a boundary
addop(MT.EFFL, EFL, 10.0, Param2=1)

# --- moulding bounds ------------------------------------------------------
# centre thickness per element: the positives 2.0-5.0, the negative 1.4-3.0
for (a, b), lo, hi in ((ELS[0], 2.0, 5.0), (ELS[1], 1.4, 3.0),
                       (ELS[2], 2.0, 5.0)):
    addop(MT.MNCG, lo, 3.0, Param1=a, Param2=a)
    addop(MT.MXCG, hi, 3.0, Param1=a, Param2=a)
    addop(MT.MNEG, 1.0, 3.0, Param1=a, Param2=a)   # edge thickness >= 1.0 mm
# air: centre >= 1.5 mm, edge >= 1.2 mm, and not absurdly long
for a in (2, 4):
    addop(MT.MNCA, 1.5, 3.0, Param1=a, Param2=a)
    addop(MT.MXCA, 12.0, 1.0, Param1=a, Param2=a)
    addop(MT.MNEA, 1.2, 3.0, Param1=a, Param2=a)

# --- form: curvature signs, and a slope cap -------------------------------
# |c| <= 1/(1.5 * sd) keeps every surface slope under asin(1/1.5) = 42 deg.
# sd is not known before the trace, so the cap uses the reference's own scaled
# semi-diameters, which is what the form is being held to anyway.
SD = {1: 9.5, 2: 9.5, 3: 5.0, 4: 5.0, 5: 7.5, 6: 7.5}
for surf, sgn in SIGNS.items():
    cmax = 1.0 / (1.5 * SD[surf] * SCALE)
    if sgn > 0:
        addop(MT.CVGT, 1.0 / 400.0, 4.0, Param1=surf)   # stays positive
        addop(MT.CVLT, cmax, 4.0, Param1=surf)          # not too steep
    else:
        addop(MT.CVLT, -1.0 / 400.0, 4.0, Param1=surf)  # stays negative
        addop(MT.CVGT, -cmax, 4.0, Param1=surf)
print("operands after bounds:", mfe.NumberOfOperands)

# ---- variables -----------------------------------------------------------
for i in (1, 2, 3, 4, 5, 6):
    lde.GetSurfaceAt(i).RadiusCell.MakeSolveVariable()
for i in (1, 2, 3, 4, 5):          # 3 glass CTs + 2 airspaces
    lde.GetSurfaceAt(i).ThicknessCell.MakeSolveVariable()

print("start merit %.6g" % mfe.CalculateMeritFunction())
for cycle in range(3):
    lo = s.Tools.OpenLocalOptimization()
    lo.Algorithm = \
        ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
    lo.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
    lo.NumberOfCores = 8
    lo.RunAndWaitForCompletion()
    print("  DLS pass %d -> %.6g" % (cycle + 1, lo.CurrentMeritFunction))
    lo.Close()

# ---- freeze, flange, save ------------------------------------------------
for i in range(1, lde.NumberOfSurfaces - 1):
    row = lde.GetSurfaceAt(i)
    try:
        row.RadiusCell.MakeSolveFixed()
    except Exception:
        pass
    if i != 6:
        try:
            row.ThicknessCell.MakeSolveFixed()
        except Exception:
            pass
for i in (1, 2, 3, 4, 5, 6):
    row = lde.GetSurfaceAt(i)
    row.MechanicalSemiDiameter = row.SemiDiameter + 1.0

out = os.path.join(HERE, "plastic-cooke.zmx")
s.SaveAs(out)
print("saved", out, os.path.exists(out))

print("\n--- prescription ---")
for i in range(lde.NumberOfSurfaces):
    row = lde.GetSurfaceAt(i)
    print("%2d  R %11.4f  t %9.4f  %-9s sd %7.3f  mech %7.3f %s"
          % (i, row.Radius, row.Thickness, row.Material or "-",
             row.SemiDiameter, row.MechanicalSemiDiameter,
             "STOP" if row.IsStop else ""))

mf = s.MFE
print("\nEFFL %.4f   TOTR %.4f   ISFN %.4f" % (
    mf.GetOperandValue(MT.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
    mf.GetOperandValue(MT.TOTR, 0, 2, 0, 0, 0, 0, 0, 0),
    mf.GetOperandValue(MT.ISFN, 0, 2, 0, 0, 0, 0, 0, 0)))
for hy, lbl in ((0.0, "0 deg"), (0.7, "14 deg"), (1.0, "20 deg")):
    print("RWRE %-7s %.6f waves" % (lbl, mf.GetOperandValue(
        MT.RWRE, 4, 2, 0.0, hy, 0, 0, 0, 0)))

an = s.Analyses.New_FftMtf()
st = an.GetSettings()
st.MaximumFrequency = 100.0
st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
an.ApplyAndWaitForCompletion()
r = an.GetResults()
print("\nFFT MTF, %d series" % (r.NumberOfDataSeries if r else -1))
if r:
    for k in range(r.NumberOfDataSeries):
        ds = r.GetDataSeries(k)
        x = list(ds.XData.Data)
        y = ds.YData.Data
        for tgt in (20.0, 40.0):
            j = min(range(len(x)), key=lambda q: abs(x[q] - tgt))
            print("  field %d  %5.1f lp/mm   T %.4f  S %.4f"
                  % (k, x[j], y.GetValue(j, 0), y.GetValue(j, 1)))
an.Close()
app.CloseApplication()
print("done")

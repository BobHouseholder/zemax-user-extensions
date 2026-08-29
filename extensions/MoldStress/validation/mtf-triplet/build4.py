"""Final test article, and the control that says what plastic cost.

Two lenses, identical in every way except the glasses:
  plastic-cooke.zmx   PMMA / POLYSTYR / PMMA
  glass-cooke.zmx     SK16 / F2 / SK16   (the sample's own glasses)

Same form (the sample's curvature signs, stop on the back of the negative
element), same spec, same moulding bounds, same optimiser. The difference
between them is the index and dispersion of the materials and nothing else,
which is the only way to say honestly what going all-plastic costs.

Change from build3: the positive elements' thickness ceiling drops 5.0 -> 4.0
mm. Both were sitting exactly on the 5 mm bound, and a 5 mm centre thickness on
an 11.6 mm part is a long cool, high shrink, and the worst case for exactly the
frozen-in effects this article exists to measure.
"""
import os
from zos import ZOSAPI, connect, HERE

GLASS = os.path.expanduser(
    r"~\Documents\Zemax\Samples\Sequential\Objectives"
    r"\Cooke 40 degree field.zmx")
SCALE, EFL, FNO, HFOV = 0.8, 40.0, 5.6, 12.0
ELS = [(1, 2), (3, 4), (5, 6)]
SIGNS = {1: +1, 2: -1, 3: -1, 4: +1, 5: +1, 6: -1}
CT_MAX_POS = 4.0
MC = ZOSAPI.Editors.MFE.MeritColumn
MT = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem
assert s.LoadFile(GLASS, False)
ref = [{"R": q.Radius, "t": q.Thickness, "mat": (q.Material or "").strip(),
        "stop": q.IsStop}
       for q in (s.LDE.GetSurfaceAt(i) for i in range(s.LDE.NumberOfSurfaces))]
NREF = {"SK16": 0.620, "F2": 0.633}


def build(swap):
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
    NEW = {"PMMA": 0.4917, "POLYSTYR": 0.5905, "SK16": 0.620, "F2": 0.633}
    for i, r in enumerate(ref):
        row = lde.GetSurfaceAt(i)
        k = 1.0
        for src in (ref[i]["mat"], ref[i - 1]["mat"] if i else ""):
            if src in swap:
                k = NEW[swap[src]] / NREF[src]
                break
        row.Radius = r["R"] * SCALE * k if abs(r["R"]) < 1e9 else r["R"]
        row.Thickness = r["t"] * SCALE if r["t"] < 1e9 else r["t"]
        row.Material = swap.get(r["mat"], r["mat"])
        if r["stop"]:
            row.IsStop = True
    lde.GetSurfaceAt(0).Thickness = 1.0e10
    lde.GetSurfaceAt(3).Thickness = max(lde.GetSurfaceAt(3).Thickness, 1.6)
    cell = lde.GetSurfaceAt(6).ThicknessCell
    sv = cell.CreateSolveType(ZOSAPI.Editors.SolveType.MarginalRayHeight)
    sv._S_MarginalRayHeight.Height = 0.0
    sv._S_MarginalRayHeight.Pupil = 0.0
    cell.SetSolveData(sv)
    return lde


def merit(caps):
    mfe = s.MFE
    w = mfe.SEQOptimizationWizard
    w.Type, w.Data, w.Reference = 0, 1, 0
    w.Ring, w.Arm, w.IsGQ = 3, 0, True
    w.Apply()
    at = [1]

    def op(t, target, weight, **p):
        o = mfe.InsertNewOperandAt(at[0])
        at[0] += 1
        o.ChangeType(t)
        for n, v in p.items():
            o.GetOperandCell(getattr(MC, n)).IntegerValue = v
        o.Target, o.Weight = target, weight

    op(MT.EFFL, EFL, 10.0, Param2=1)
    for (a, b), lo, hi in ((ELS[0], 2.0, CT_MAX_POS), (ELS[1], 1.2, 3.0),
                           (ELS[2], 2.0, CT_MAX_POS)):
        op(MT.MNCG, lo, 3.0, Param1=a, Param2=a)
        op(MT.MXCG, hi, 3.0, Param1=a, Param2=a)
        op(MT.MNEG, 1.0, 3.0, Param1=a, Param2=a)
    for a in (2, 4):
        op(MT.MNCA, 1.5, 3.0, Param1=a, Param2=a)
        op(MT.MXCA, 12.0, 1.0, Param1=a, Param2=a)
        op(MT.MNEA, 1.2, 3.0, Param1=a, Param2=a)
    for surf, sgn in SIGNS.items():
        cmax = 1.0 / (1.5 * caps[surf])
        if sgn > 0:
            op(MT.CVGT, 1.0 / 400.0, 4.0, Param1=surf)
            op(MT.CVLT, cmax, 4.0, Param1=surf)
        else:
            op(MT.CVLT, -1.0 / 400.0, 4.0, Param1=surf)
            op(MT.CVGT, -cmax, 4.0, Param1=surf)


def vary(lde):
    for i in (1, 2, 3, 4, 5, 6):
        lde.GetSurfaceAt(i).RadiusCell.MakeSolveVariable()
    for i in (1, 2, 3, 4, 5):
        lde.GetSurfaceAt(i).ThicknessCell.MakeSolveVariable()


def dls(n):
    v = None
    for _ in range(n):
        lo = s.Tools.OpenLocalOptimization()
        lo.Algorithm = \
            ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares
        lo.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic
        lo.NumberOfCores = 8
        lo.RunAndWaitForCompletion()
        v = lo.CurrentMeritFunction
        lo.Close()
    return v


def make(swap, save, label):
    lde = build(swap)
    caps = {i: (9.5 if i < 3 else 5.0 if i < 5 else 7.5) * SCALE
            for i in SIGNS}
    merit(caps)
    vary(lde)
    dls(2)
    caps = {i: max(1.0, lde.GetSurfaceAt(i).SemiDiameter) for i in SIGNS}
    merit(caps)
    vary(lde)
    m = dls(3)

    mf = s.MFE
    rwre = [mf.GetOperandValue(MT.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0)
            for h in (0.0, 0.7, 1.0)]
    an = s.Analyses.New_FftMtf()
    st = an.GetSettings()
    st.MaximumFrequency = 100.0
    st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
    an.ApplyAndWaitForCompletion()
    r = an.GetResults()
    mtf = {}
    for k in range(r.NumberOfDataSeries):
        ds = r.GetDataSeries(k)
        x = list(ds.XData.Data)
        y = ds.YData.Data
        for fq in (20.0, 40.0):
            j = min(range(len(x)), key=lambda q: abs(x[q] - fq))
            mtf[(k, fq)] = (y.GetValue(j, 0), y.GetValue(j, 1))
    an.Close()

    print("%-9s merit %-10.5g EFFL %.3f  RWRE %s" % (
        label, m, mf.GetOperandValue(MT.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
        " ".join("%.3f" % v for v in rwre)))
    for k, nm in enumerate(("0 deg", "8.4 deg", "12 deg")):
        print("          %-8s MTF20 T %.3f S %.3f   MTF40 T %.3f S %.3f"
              % (nm, mtf[(k, 20.0)][0], mtf[(k, 20.0)][1],
                 mtf[(k, 40.0)][0], mtf[(k, 40.0)][1]))
    print("          CT %s   air %s" % (
        " ".join("%.3f" % lde.GetSurfaceAt(a).Thickness for a, b in ELS),
        " ".join("%.3f" % lde.GetSurfaceAt(a).Thickness for a in (2, 4))))

    for i in range(1, lde.NumberOfSurfaces - 1):
        try:
            lde.GetSurfaceAt(i).RadiusCell.MakeSolveFixed()
        except Exception:
            pass
        if i != 6:
            try:
                lde.GetSurfaceAt(i).ThicknessCell.MakeSolveFixed()
            except Exception:
                pass
    for i in (1, 2, 3, 4, 5, 6):
        row = lde.GetSurfaceAt(i)
        row.MechanicalSemiDiameter = row.SemiDiameter + 1.0
    s.SaveAs(os.path.join(HERE, save))
    print("          saved", save)


print("EFL %.0f mm, F/%.1f, +/-%.0f deg, F-d-C; Cooke form held by sign "
      "constraints" % (EFL, FNO, HFOV))
make({"SK16": "PMMA", "F2": "POLYSTYR"}, "plastic-cooke.zmx", "PLASTIC")
make({}, "glass-cooke.zmx", "GLASS")
app.CloseApplication()
print("done")

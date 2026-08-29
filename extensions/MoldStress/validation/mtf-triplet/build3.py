"""The plastic Cooke, with the slope cap derived from the BUILT lens and the
spec swept to find where this form actually works in plastic.

Two fixes over build2:

  * The curvature cap was computed from the glass reference's semi-diameters
    scaled by 0.8, i.e. 7.6 mm on surface 1 - but the built lens's surface 1 is
    6.365 mm. The cap was therefore ~19% tighter than the slope rule needs, and
    surface 1 came out sitting exactly on it. It is now a two-pass: optimise,
    read the real semi-diameters, re-cap from those, optimise again.

  * SK16 -> PMMA drops the index from 1.620 to 1.4917, which costs aberration
    the glass version does not pay. Rather than assert what an all-plastic
    Cooke can do, sweep F/# and field and measure it.

Everything else is held: the sample's surface-by-surface curvature signs, the
stop on the back of the negative element, and the moulding bounds.
"""
import os
from zos import ZOSAPI, connect, HERE

GLASS = os.path.expanduser(
    r"~\Documents\Zemax\Samples\Sequential\Objectives"
    r"\Cooke 40 degree field.zmx")
SCALE, EFL = 0.8, 40.0
SWAP = {"SK16": "PMMA", "F2": "POLYSTYR"}
ELS = [(1, 2), (3, 4), (5, 6)]
SIGNS = {1: +1, 2: -1, 3: -1, 4: +1, 5: +1, 6: -1}
MC = ZOSAPI.Editors.MFE.MeritColumn
MT = ZOSAPI.Editors.MFE.MeritOperandType

app = connect()
s = app.PrimarySystem
assert s.LoadFile(GLASS, False)
ref = [{"R": q.Radius, "t": q.Thickness, "mat": (q.Material or "").strip(),
        "stop": q.IsStop}
       for q in (s.LDE.GetSurfaceAt(i) for i in range(s.LDE.NumberOfSurfaces))]


def build(fno, hfov):
    s.New(False)
    s.SystemData.MaterialCatalogs.AddCatalog("MISC")
    ap = s.SystemData.Aperture
    ap.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter
    ap.ApertureValue = EFL / fno
    s.SystemData.Wavelengths.SelectWavelengthPreset(
        ZOSAPI.SystemData.WavelengthPreset.FdC_Visible)
    f = s.SystemData.Fields
    f.SetFieldType(ZOSAPI.SystemData.FieldType.Angle)
    while f.NumberOfFields > 1:
        f.RemoveField(f.NumberOfFields)
    f.GetField(1).Y = 0.0
    f.AddField(0.0, 0.7 * hfov, 1.0)
    f.AddField(0.0, hfov, 1.0)

    lde = s.LDE
    while lde.NumberOfSurfaces < len(ref):
        lde.InsertNewSurfaceAt(1)
    IDX = {"PMMA": 0.620 / 0.4917, "POLYSTYR": 0.633 / 0.5905}
    for i, r in enumerate(ref):
        row = lde.GetSurfaceAt(i)
        k = 1.0
        if i > 0 and ref[i]["mat"] in SWAP:
            k = 1.0 / IDX[SWAP[ref[i]["mat"]]]
        elif i > 0 and ref[i - 1]["mat"] in SWAP:
            k = 1.0 / IDX[SWAP[ref[i - 1]["mat"]]]
        row.Radius = r["R"] * SCALE * k if abs(r["R"]) < 1e9 else r["R"]
        row.Thickness = r["t"] * SCALE if r["t"] < 1e9 else r["t"]
        row.Material = SWAP.get(r["mat"], r["mat"])
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
    wiz = mfe.SEQOptimizationWizard
    wiz.Type, wiz.Data, wiz.Reference = 0, 1, 0
    wiz.Ring, wiz.Arm, wiz.IsGQ = 3, 0, True
    wiz.Apply()
    at = [1]

    def op(t, target, weight, **params):
        o = mfe.InsertNewOperandAt(at[0])
        at[0] += 1
        o.ChangeType(t)
        for n, v in params.items():
            o.GetOperandCell(getattr(MC, n)).IntegerValue = v
        o.Target, o.Weight = target, weight

    op(MT.EFFL, EFL, 10.0, Param2=1)
    for (a, b), lo, hi in ((ELS[0], 2.0, 5.0), (ELS[1], 1.2, 3.0),
                           (ELS[2], 2.0, 5.0)):
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


def dls(n=3):
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


def mtf_at(freqs=(20.0, 40.0)):
    an = s.Analyses.New_FftMtf()
    st = an.GetSettings()
    st.MaximumFrequency = 100.0
    st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
    an.ApplyAndWaitForCompletion()
    r = an.GetResults()
    out = {}
    if r and r.NumberOfDataSeries:
        for k in range(r.NumberOfDataSeries):
            ds = r.GetDataSeries(k)
            x = list(ds.XData.Data)
            y = ds.YData.Data
            for fq in freqs:
                j = min(range(len(x)), key=lambda q: abs(x[q] - fq))
                out[(k, fq)] = (y.GetValue(j, 0), y.GetValue(j, 1))
    an.Close()
    return out


def run(fno, hfov, save=None):
    lde = build(fno, hfov)
    caps = {i: 9.5 * SCALE if i < 3 else (5.0 * SCALE if i < 5 else 7.5 * SCALE)
            for i in SIGNS}
    merit(caps)
    vary(lde)
    dls(2)
    # pass 2: re-cap from the semi-diameters the lens actually has
    caps = {i: max(1.0, lde.GetSurfaceAt(i).SemiDiameter) for i in SIGNS}
    merit(caps)
    vary(lde)
    m = dls(3)

    mf = s.MFE
    res = {"fno": fno, "hfov": hfov, "merit": m,
           "effl": mf.GetOperandValue(MT.EFFL, 0, 2, 0, 0, 0, 0, 0, 0),
           "rwre": [mf.GetOperandValue(MT.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0)
                    for h in (0.0, 0.7, 1.0)],
           "mtf": mtf_at()}
    m40 = [res["mtf"][(k, 40.0)] for k in range(3)]
    res["worst40"] = min(min(a, b) for a, b in m40)
    res["axis40"] = m40[0][0]
    print("  F/%-4.1f  %4.1f deg   merit %-9.5g  RWRE %s   "
          "MTF40 axis %.3f  worst %.3f"
          % (fno, hfov, m, " ".join("%.3f" % v for v in res["rwre"]),
             res["axis40"], res["worst40"]))
    if save:
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
        print("    saved", save)
    return res


print("plastic Cooke, spec sweep (form and moulding bounds held fixed)")
specs = [(5.0, 20.0), (5.0, 16.0), (5.6, 20.0), (5.6, 16.0),
         (6.3, 20.0), (5.0, 12.0), (5.6, 12.0), (8.0, 20.0)]
out = [run(a, b) for a, b in specs]

best = max(out, key=lambda r: r["worst40"])
print("\nbest by worst-field MTF at 40 lp/mm: F/%.1f  %.1f deg  (worst %.3f)"
      % (best["fno"], best["hfov"], best["worst40"]))
run(best["fno"], best["hfov"], save="plastic-cooke.zmx")
app.CloseApplication()
print("done")

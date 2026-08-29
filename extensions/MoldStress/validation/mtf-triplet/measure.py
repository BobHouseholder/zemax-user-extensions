"""Measure the three states of the plastic triplet and dump them to JSON.

  A  as designed   plastic-triplet.zmx            PMMA / POLYSTYR (MISC)
  B  baseline      plastic-triplet-MoldStress.zmx MS_* glasses, no STAR data
  C  moulded       B + MoldStress direct-index data on surfaces 1, 3, 6

B is the honest before-picture for the moulding question: A->B is the cost of
the catalogue substitution, which is not a moulding effect and must not be
folded into one. Both are reported.
"""
import json, os, time
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
IDX = {1: os.path.join(MS, "moldstress_s1_index.txt"),
       3: os.path.join(MS, "moldstress_s3_index.txt"),
       6: os.path.join(MS, "moldstress_s6_index.txt")}
GRIN_STEP = 0.50                      # what the tool itself set, from CT/10
FIELDS = [(0.0, "0.0 deg"), (0.7, "6.3 deg"), (1.0, "9.0 deg")]

app = connect()
s = app.PrimarySystem
out = {}


def mtf(tag):
    t0 = time.time()
    an = s.Analyses.New_FftMtf()
    st = an.GetSettings()
    st.MaximumFrequency = 100.0
    st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
    an.ApplyAndWaitForCompletion()
    r = an.GetResults()
    series = []
    if r is not None and r.NumberOfDataSeries > 0:
        for k in range(r.NumberOfDataSeries):
            ds = r.GetDataSeries(k)
            x = [float(v) for v in ds.XData.Data]
            y = ds.YData.Data
            series.append({
                "freq": x,
                "tan": [float(y.GetValue(i, 0)) for i in range(len(x))],
                "sag": [float(y.GetValue(i, 1)) for i in range(len(x))],
            })
    an.Close()
    print("   %-10s MTF %d series in %.1f s" % (tag, len(series), time.time() - t0))
    return series


def scalars():
    mf = s.MFE
    d = {"rwre": [], "rsre": []}
    for hy, _ in FIELDS:
        d["rwre"].append(float(mf.GetOperandValue(
            ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 2, 0.0, hy, 0, 0, 0, 0)))
        d["rsre"].append(float(mf.GetOperandValue(
            ZOSAPI.Editors.MFE.MeritOperandType.RSRE, 4, 2, 0.0, hy, 0, 0, 0, 0)))
    d["effl"] = float(mf.GetOperandValue(
        ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 2, 0, 0, 0, 0, 0, 0))
    return d


def prescription():
    lde, rows = s.LDE, []
    for i in range(lde.NumberOfSurfaces):
        r = lde.GetSurfaceAt(i)
        rows.append({"i": i, "R": float(r.Radius), "t": float(r.Thickness),
                     "mat": r.Material or "", "sd": float(r.SemiDiameter),
                     "mech": float(r.MechanicalSemiDiameter),
                     "conic": float(r.Conic)})
    return rows


def layout_rays():
    """Real meridional fans, every field, traced surface by surface."""
    lde = s.LDE
    nlast = lde.NumberOfSurfaces - 1
    OPD = getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None_",
                  getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None", None))
    pys = [-1.0, -0.7, -0.35, 0.0, 0.35, 0.7, 1.0]
    fans = {}
    for fi, (hy, lbl) in enumerate(FIELDS):
        pts = [[] for _ in pys]
        for surf in range(1, nlast + 1):
            tool = s.Tools.OpenBatchRayTrace()
            data = tool.CreateNormUnpol(len(pys),
                                        ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
            data.ClearData()
            for py in pys:
                data.AddRay(2, 0.0, hy, 0.0, py, OPD)
            tool.RunAndWaitForCompletion()
            data.StartReadingResults()
            while True:
                res = data.ReadNextResult()
                if not res[0]:
                    break
                rn, err, vig = res[1], res[2], res[3]
                x, y, z = res[4], res[5], res[6]
                if err == 0:
                    pts[rn - 1].append((surf, float(y), float(z)))
            tool.Close()
        fans[lbl] = pts
    return fans


def load_star():
    n = 0
    for surf, path in IDX.items():
        di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(path)
        pts = di.FEAData.NumberOfDataPoints
        di.Fits.Refit()
        di.Fits.GRINStep = GRIN_STEP
        print("   surface %d: %d index points, GRIN step %.2f mm"
              % (surf, pts, di.Fits.GRINStep))
        n += pts
    return n


# ---------------- A: as designed ------------------------------------------
print("A as designed")
assert s.LoadFile(os.path.join(HERE, "plastic-triplet.zmx"), False)
out["A"] = {"label": "as designed (PMMA / POLYSTYR)",
            "scalars": scalars(), "mtf": mtf("A"), "presc": prescription()}

# ---------------- B: baseline ---------------------------------------------
print("B baseline (MS_* substituted, no moulding data)")
assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
out["B"] = {"label": "baseline (MS_* glasses, no moulding)",
            "scalars": scalars(), "mtf": mtf("B"), "presc": prescription()}
out["rays"] = layout_rays()

# ---------------- C: moulded ----------------------------------------------
print("C moulded (STAR direct index loaded)")
out["C_points"] = load_star()
out["C"] = {"label": "with moulding (STAR direct index)",
            "scalars": scalars(), "mtf": mtf("C")}

with open(os.path.join(HERE, "results.json"), "w") as fh:
    json.dump(out, fh)
print("wrote results.json")
app.CloseApplication()
print("done")

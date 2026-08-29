"""The whole measurement suite, on the plastic COOKE article, in one pass.

Same states and the same controls as the first article, with the element
surfaces discovered from the index files MoldStress wrote rather than
hard-coded - the Cooke's elements are at 1-2 / 3-4 / 5-6, the rejected design's
were at 1-2 / 3-4 / 6-7, and hard-coding that was one edit waiting to be
forgotten.

  A                 as designed, PMMA / POLYSTYR from MISC
  poly_solve_*      image plane on the file's own focus solve   <- what -run reports
  poly_pin_*        image plane pinned; base / NULL / moulding
  mono_pin_*        pinned and d-line only; the trustworthy comparison
  tf_*              through focus on a shared grid
  ctl               per-wavelength: no data / NULL cloud / moulding
"""
import glob, json, os, re, sys, time
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, sys.argv[1] if len(sys.argv) > 1 else "ms2")
CTL = os.path.join(HERE, "ctl2")
os.makedirs(CTL, exist_ok=True)
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
ORIG = os.path.join(HERE, "plastic-cooke.zmx")
GRIN = 0.50
E = ZOSAPI.Editors.MFE.MeritOperandType
FIELDS = [0.0, 0.7, 1.0]
STEPS = [i * 0.005 for i in range(-12, 13)]

# --- which surfaces carry data, and each one's base index -----------------
IDX = {}
for p in sorted(glob.glob(os.path.join(MS, "moldstress_s*_index.txt"))):
    IDX[int(re.search(r"_s(\d+)_index", p).group(1))] = p
print("index files:", {k: os.path.basename(v) for k, v in IDX.items()})

app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False)
ND = {}
for surf in IDX:
    ND[surf] = s.MFE.GetOperandValue(E.INDX, surf, 2, 0, 0, 0, 0, 0, 0)
print("base indices (d-line):", {k: round(v, 4) for k, v in ND.items()})

# a NULL cloud per surface: same points, index replaced by that element's own
# Nd, so it is physically a no-op and anything it moves is the pipeline
NULL = {}
for surf, src in IDX.items():
    dst = os.path.join(CTL, "null_s%d.txt" % surf)
    with open(src) as fh, open(dst, "w") as g:
        for line in fh:
            q = line.split()
            if len(q) >= 4:
                g.write("%s %s %s %.9E\n" % (q[0], q[1], q[2], ND[surf]))
    NULL[surf] = dst
print("null clouds written")

LAST = s.LDE.NumberOfSurfaces - 2      # last surface before the image
OUT = {"surfaces": list(IDX), "nd": ND, "last_surface": LAST}


def load(pin, mono=False, orig=False):
    assert s.LoadFile(ORIG if orig else BASE, False)
    if pin:
        s.LDE.GetSurfaceAt(LAST).ThicknessCell.MakeSolveFixed()
    if mono:
        wl = s.SystemData.Wavelengths
        for i in (3, 1):
            wl.RemoveWavelength(i)
        assert wl.NumberOfWavelengths == 1
        assert abs(wl.GetWavelength(1).Wavelength - 0.5875618) < 1e-6


def star(which):
    src = IDX if which == "full" else NULL
    for surf, path in src.items():
        di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
        di.SetDataIsLocal()
        di.FEAData.ImportDirectIndex_1(path)
        di.Fits.Refit()
        di.Fits.GRINStep = GRIN


def mtf():
    an = s.Analyses.New_FftMtf()
    st = an.GetSettings()
    st.MaximumFrequency = 100.0
    st.SampleSize = ZOSAPI.Analysis.SampleSizes.S_64x64
    an.ApplyAndWaitForCompletion()
    r = an.GetResults()
    out = []
    for k in range(r.NumberOfDataSeries):
        ds = r.GetDataSeries(k)
        x = [float(v) for v in ds.XData.Data]
        y = ds.YData.Data
        out.append({"freq": x,
                    "tan": [float(y.GetValue(i, 0)) for i in range(len(x))],
                    "sag": [float(y.GetValue(i, 1)) for i in range(len(x))]})
    an.Close()
    return out


def scal():
    mf = s.MFE
    nw = s.SystemData.Wavelengths.NumberOfWavelengths
    wd = 2 if nw > 1 else 1
    return {"bfl": float(s.LDE.GetSurfaceAt(LAST).Thickness),
            "effl": float(mf.GetOperandValue(E.EFFL, 0, wd, 0, 0, 0, 0, 0, 0)),
            "rwre_w2": [float(mf.GetOperandValue(
                E.RWRE, 4, wd, 0.0, h, 0, 0, 0, 0)) for h in FIELDS],
            "rsre": [float(mf.GetOperandValue(
                E.RSRE, 4, wd, 0.0, h, 0, 0, 0, 0)) for h in FIELDS]}


def presc():
    lde = s.LDE
    return [{"i": i, "R": float(lde.GetSurfaceAt(i).Radius),
             "t": float(lde.GetSurfaceAt(i).Thickness),
             "mat": lde.GetSurfaceAt(i).Material or "",
             "sd": float(lde.GetSurfaceAt(i).SemiDiameter),
             "mech": float(lde.GetSurfaceAt(i).MechanicalSemiDiameter),
             "conic": float(lde.GetSurfaceAt(i).Conic),
             "stop": bool(lde.GetSurfaceAt(i).IsStop)}
            for i in range(lde.NumberOfSurfaces)]


# ---- as designed ---------------------------------------------------------
load(False, orig=True)
OUT["A"] = {"label": "as designed (PMMA / POLYSTYR)", "scalars": scal(),
            "mtf": mtf(), "presc": presc()}
print("A  RWRE", ["%.4f" % v for v in OUT["A"]["scalars"]["rwre_w2"]])

# ---- the eight states ----------------------------------------------------
plan = [("poly_solve_base", False, None, False),
        ("poly_solve_mould", False, "full", False),
        ("poly_pin_base", True, None, False),
        ("poly_pin_null", True, "null", False),
        ("poly_pin_mould", True, "full", False),
        ("mono_pin_base", True, None, True),
        ("mono_pin_null", True, "null", True),
        ("mono_pin_mould", True, "full", True)]
for tag, pin, which, mono in plan:
    t0 = time.time()
    load(pin, mono)
    if which:
        star(which)
    d = scal()
    d["mtf"] = mtf()
    d["mono"] = mono
    OUT[tag] = d
    print("%-17s BFL %7.4f  EFFL %8.4f  RWRE %s   (%.0f s)"
          % (tag, d["bfl"], d["effl"],
             ["%.4f" % v for v in d["rwre_w2"]], time.time() - t0))
OUT["presc"] = OUT["poly_pin_base"] and presc()

# ---- per-wavelength control ---------------------------------------------
ctl = {}
for tag, which in (("no_data", None), ("null", "null"), ("full", "full")):
    load(True)
    if which:
        star(which)
    mf = s.MFE
    ctl[tag] = [float(mf.GetOperandValue(E.RWRE, 4, w, 0, 0, 0, 0, 0, 0))
                for w in (1, 2, 3)]
    ctl[tag + "_effl"] = float(mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0))
    print("ctl %-8s RWRE w1 %.4f  w2 %.4f  w3 %.4f   EFFL %.4f"
          % (tag, ctl[tag][0], ctl[tag][1], ctl[tag][2], ctl[tag + "_effl"]))
OUT["ctl"] = ctl
OUT["waves"] = [float(s.SystemData.Wavelengths.GetWavelength(i).Wavelength)
                for i in (1, 2, 3)]

# ---- through focus, shared grid -----------------------------------------
for tag, which in (("tf_base", None), ("tf_mould", "full")):
    load(True)
    if which:
        star(which)
    row = s.LDE.GetSurfaceAt(LAST)
    t0 = float(row.Thickness)
    mf, curve = s.MFE, []
    for dz in STEPS:
        row.Thickness = t0 + dz
        curve.append({"d": dz,
                      "rwre": [float(mf.GetOperandValue(
                          E.RWRE, 4, 2, 0.0, h, 0, 0, 0, 0)) for h in FIELDS]})
    OUT[tag] = {"t0": t0, "curve": curve}
    b = min(curve, key=lambda c: c["rwre"][0])
    print("%-9s best axis %+.3f mm -> %.4f waves"
          % (tag, b["d"], b["rwre"][0]))

# ---- layout rays ---------------------------------------------------------
load(True)
OPD = getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None_",
              getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None", None))
pys = [-1.0, -0.66, -0.33, 0.0, 0.33, 0.66, 1.0]
fld = s.SystemData.Fields
rays = {}
for k in range(1, fld.NumberOfFields + 1):
    hy = fld.GetField(k).Y / fld.GetField(fld.NumberOfFields).Y \
        if fld.GetField(fld.NumberOfFields).Y else 0.0
    pts = [[] for _ in pys]
    for surf in range(1, s.LDE.NumberOfSurfaces):
        tool = s.Tools.OpenBatchRayTrace()
        data = tool.CreateNormUnpol(len(pys),
                                    ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
        data.ClearData()
        for py in pys:
            data.AddRay(2, 0.0, hy, 0.0, py, OPD)
        tool.RunAndWaitForCompletion()
        data.StartReadingResults()
        while True:
            r = data.ReadNextResult()
            if not r[0]:
                break
            if r[2] == 0:
                pts[r[1] - 1].append((surf, float(r[5]), float(r[6])))
        tool.Close()
    rays["%.1f deg" % fld.GetField(k).Y] = pts
OUT["rays"] = rays
OUT["field_labels"] = list(rays)
print("ray fans:", {k: sum(len(p) for p in v) for k, v in rays.items()})

with open(os.path.join(HERE, "results2.json"), "w") as fh:
    json.dump(OUT, fh)
app.CloseApplication()
print("wrote results2.json")

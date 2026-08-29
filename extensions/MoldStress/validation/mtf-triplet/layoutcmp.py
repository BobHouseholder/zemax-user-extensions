"""Three layouts on one page, all normalised by focal length so the FORM is
comparable regardless of size: the shipped glass Cooke, the design that was
rejected, and its replacement.
"""
import json, math, os
from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENSES = [
    (os.path.expanduser(r"~\Documents\Zemax\Samples\Sequential\Objectives"
                        r"\Cooke 40 degree field.zmx"),
     "glass Cooke triplet, as shipped", "SK16 / F2 / SK16"),
    (os.path.join(HERE, "plastic-triplet.zmx"),
     "rejected: global optimisation left the Cooke basin", "PMMA / POLYSTYR / PMMA"),
    (os.path.join(HERE, "plastic-cooke.zmx"),
     "replacement: the glass form, transcribed and bounded", "PMMA / POLYSTYR / PMMA"),
]

app = connect()
s = app.PrimarySystem
out = []
for path, label, mats in LENSES:
    assert s.LoadFile(path, False), path
    lde = s.LDE
    n = lde.NumberOfSurfaces
    rows = [{"i": i, "R": lde.GetSurfaceAt(i).Radius,
             "t": lde.GetSurfaceAt(i).Thickness,
             "mat": (lde.GetSurfaceAt(i).Material or "").strip(),
             "sd": lde.GetSurfaceAt(i).SemiDiameter,
             "k": lde.GetSurfaceAt(i).Conic,
             "stop": lde.GetSurfaceAt(i).IsStop} for i in range(n)]
    efl = s.MFE.GetOperandValue(E.EFFL, 0, 1, 0, 0, 0, 0, 0, 0)
    fld = s.SystemData.Fields
    hfov = max(fld.GetField(k).Y for k in range(1, fld.NumberOfFields + 1))
    nf = fld.NumberOfFields

    OPD = getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None_",
                  getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None", None))
    pys = [-1.0, -0.66, -0.33, 0.0, 0.33, 0.66, 1.0]
    fans = []
    for k in range(1, nf + 1):
        hy = fld.GetField(k).Y / hfov if hfov else 0.0
        pts = [[] for _ in pys]
        for surf in range(1, n):
            tool = s.Tools.OpenBatchRayTrace()
            data = tool.CreateNormUnpol(len(pys),
                                        ZOSAPI.Tools.RayTrace.RaysType.Real,
                                        surf)
            data.ClearData()
            for py in pys:
                data.AddRay(1, 0.0, hy, 0.0, py, OPD)
            tool.RunAndWaitForCompletion()
            data.StartReadingResults()
            while True:
                r = data.ReadNextResult()
                if not r[0]:
                    break
                if r[2] == 0:
                    pts[r[1] - 1].append((surf, float(r[5]), float(r[6])))
            tool.Close()
        fans.append({"deg": fld.GetField(k).Y, "rays": pts})
    out.append({"label": label, "mats": mats, "efl": efl, "hfov": hfov,
                "rows": rows, "fans": fans,
                "file": os.path.basename(path)})
    print("traced %-34s EFL %.2f  %d fields" % (out[-1]["file"], efl, nf))

with open(os.path.join(HERE, "layouts.json"), "w") as fh:
    json.dump(out, fh)
app.CloseApplication()
print("wrote layouts.json")

"""Form and mouldability metrics for a set of lenses -> form.json.

The report reads this file. Retyping these numbers out of a terminal into
prose is how a report drifts from the thing it describes.
"""
import json, math, os
from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
LENSES = [
    (os.path.expanduser(r"~\Documents\Zemax\Samples\Sequential\Objectives"
                        r"\Cooke 40 degree field.zmx"),
     "glass Cooke triplet, as shipped", "reference"),
    (os.path.join(HERE, "plastic-triplet.zmx"),
     "rejected plastic design", "rejected"),
    (os.path.join(HERE, "plastic-cooke.zmx"),
     "plastic Cooke triplet", "article"),
    (os.path.join(HERE, "glass-cooke.zmx"),
     "same form in the sample's own glasses", "control"),
]
# what "mouldable" is being held to, stated once and applied uniformly
LIM = {"ct_min": 1.0, "ct_max": 5.0, "et_min": 0.8,
       "ct_et_lo": 0.35, "ct_et_hi": 3.0, "slope_max": 50.0,
       "air_min": 0.8, "air_edge_min": 0.8}


def sag(r, R, k=0.0):
    if R == 0 or abs(R) > 1e9:
        return 0.0
    c = 1.0 / R
    q = 1.0 - (1.0 + k) * c * c * r * r
    return float("nan") if q < 0 else c * r * r / (1.0 + math.sqrt(q))


app = connect()
s = app.PrimarySystem
out = []
for path, label, kind in LENSES:
    assert s.LoadFile(path, False), path
    lde, mf = s.LDE, s.MFE
    n = lde.NumberOfSurfaces
    rows = [{"i": i, "R": lde.GetSurfaceAt(i).Radius,
             "t": lde.GetSurfaceAt(i).Thickness,
             "mat": (lde.GetSurfaceAt(i).Material or "").strip(),
             "sd": lde.GetSurfaceAt(i).SemiDiameter,
             "k": lde.GetSurfaceAt(i).Conic,
             "stop": lde.GetSurfaceAt(i).IsStop} for i in range(n)]
    els, i = [], 1
    while i < n - 1:
        if rows[i]["mat"]:
            j = i
            while j < n - 1 and rows[j]["mat"]:
                j += 1
            els.append((i, j))
            i = j
        else:
            i += 1

    fld = s.SystemData.Fields
    d = {"label": label, "kind": kind, "file": os.path.basename(path),
         "efl": mf.GetOperandValue(E.EFFL, 0, 1, 0, 0, 0, 0, 0, 0),
         "fno": mf.GetOperandValue(E.ISFN, 0, 1, 0, 0, 0, 0, 0, 0),
         "track": mf.GetOperandValue(E.TOTR, 0, 1, 0, 0, 0, 0, 0, 0),
         "hfov": max(fld.GetField(k).Y
                     for k in range(1, fld.NumberOfFields + 1)),
         "stop": next((r["i"] for r in rows if r["stop"]), None),
         "els": [], "air": [], "ok": True}
    d["stop_is_back_of_e2"] = (d["stop"] == els[1][1]) if len(els) > 1 else False

    for k, (a, b) in enumerate(els, 1):
        R1, R2 = rows[a]["R"], rows[b]["R"]
        nd = mf.GetOperandValue(E.INDX, a, 1, 0, 0, 0, 0, 0, 0)
        ct = rows[a]["t"]
        sd = max(rows[a]["sd"], rows[b]["sd"])
        s1 = sag(min(sd, .999 * abs(R1)), R1, rows[a]["k"])
        s2 = sag(min(sd, .999 * abs(R2)), R2, rows[b]["k"])
        et = ct + s2 - s1
        p = (nd - 1.0) * (1.0 / R1 - 1.0 / R2)
        slope = max(math.degrees(math.asin(min(1.0, rows[a]["sd"] / abs(R1)))),
                    math.degrees(math.asin(min(1.0, rows[b]["sd"] / abs(R2)))))
        fails = []
        if ct < LIM["ct_min"]:
            fails.append("CT below %.1f mm" % LIM["ct_min"])
        if ct > LIM["ct_max"] + 1e-6:
            fails.append("CT above %.1f mm" % LIM["ct_max"])
        if et < LIM["et_min"]:
            fails.append("edge below %.1f mm" % LIM["et_min"])
        if not (LIM["ct_et_lo"] <= ct / et <= LIM["ct_et_hi"]):
            fails.append("CT/ET outside %.2f-%.1f"
                         % (LIM["ct_et_lo"], LIM["ct_et_hi"]))
        if slope > LIM["slope_max"]:
            fails.append("slope above %.0f deg" % LIM["slope_max"])
        if fails:
            d["ok"] = False
        d["els"].append({
            "n": k, "surfaces": [a, b], "mat": rows[a]["mat"], "nd": nd,
            "R1": R1, "R2": R2, "ct": ct, "et": et, "sd": sd,
            "power": p, "f": (1.0 / p if p else None),
            "shape_q": ((R2 + R1) / (R2 - R1)) if (R2 - R1) else None,
            "slope_deg": slope, "fails": fails})

    for k in range(len(els) - 1):
        b, f2 = els[k][1], els[k + 1][0]
        tair = sum(rows[j]["t"] for j in range(b, f2))
        sd = max(rows[b]["sd"], rows[f2]["sd"])
        edge = tair + sag(min(sd, .999 * abs(rows[f2]["R"])), rows[f2]["R"],
                          rows[f2]["k"]) \
            - sag(min(sd, .999 * abs(rows[b]["R"])), rows[b]["R"], rows[b]["k"])
        bad = tair < LIM["air_min"] or edge < LIM["air_edge_min"]
        if bad:
            d["ok"] = False
        d["air"].append({"between": [k + 1, k + 2], "centre": tair,
                         "edge": edge, "fails": bad})
    out.append(d)
    print("%-38s EFL %6.2f  F/%.2f  %4.1f deg  powers %s  %s"
          % (d["label"], d["efl"], d["fno"], d["hfov"],
             " ".join("+" if e["power"] > 0 else "-" for e in d["els"]),
             "mouldable" if d["ok"] else "FAILS a moulding check"))

with open(os.path.join(HERE, "form.json"), "w") as fh:
    json.dump({"limits": LIM, "lenses": out}, fh, indent=1)
app.CloseApplication()
print("wrote form.json")

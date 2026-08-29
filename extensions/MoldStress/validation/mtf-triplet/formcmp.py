"""Dump the FORM of a lens - the things that make it a Cooke triplet or not,
and the things that make it mouldable or not. Runs on any .zmx.

Form:      element powers and their signs, Coddington shape factors, where the
           stop sits, how the airspaces are distributed.
Moulding:  centre and edge thickness and their ratio, edge airgaps, and the
           steepest surface slope (asin(semi-diameter / |R|)), which is what
           decides whether a part will release from the tool.
"""
import math, os, sys
from zos import ZOSAPI, connect

E = ZOSAPI.Editors.MFE.MeritOperandType


def sag(r, R, k=0.0):
    if R == 0 or abs(R) > 1e9:
        return 0.0
    c = 1.0 / R
    q = 1.0 - (1.0 + k) * c * c * r * r
    if q < 0:
        return float("nan")
    return c * r * r / (1.0 + math.sqrt(q))


def report(app, path, label):
    s = app.PrimarySystem
    assert s.LoadFile(path, False), path
    lde, mf = s.LDE, s.MFE
    n = lde.NumberOfSurfaces
    rows = []
    for i in range(n):
        q = lde.GetSurfaceAt(i)
        rows.append({"i": i, "R": q.Radius, "t": q.Thickness,
                     "mat": (q.Material or "").strip(), "sd": q.SemiDiameter,
                     "k": q.Conic, "stop": q.IsStop})

    # element grouping: a run of surfaces carrying glass
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

    efl = mf.GetOperandValue(E.EFFL, 0, 1, 0, 0, 0, 0, 0, 0)
    isfn = mf.GetOperandValue(E.ISFN, 0, 1, 0, 0, 0, 0, 0, 0)
    totr = mf.GetOperandValue(E.TOTR, 0, 1, 0, 0, 0, 0, 0, 0)
    fld = s.SystemData.Fields
    hfov = max(fld.GetField(k).Y for k in range(1, fld.NumberOfFields + 1))
    wl = s.SystemData.Wavelengths
    waves = [wl.GetWavelength(k).Wavelength
             for k in range(1, wl.NumberOfWavelengths + 1)]
    stop = next((r["i"] for r in rows if r["stop"]), None)

    print("=" * 78)
    print("%s   %s" % (label, os.path.basename(path)))
    print("  EFL %.3f mm   F/%.2f   HFOV %.1f deg   track %.2f mm   "
          "stop at surface %s" % (efl, isfn, hfov, totr, stop))
    print("  wavelengths %s um" % ", ".join("%.4f" % w for w in waves))
    print()
    print("  el  surf   material     nd     Vd      R1        R2      "
          "shape q   f_el     power")
    tot_pow = 0.0
    forms = []
    for k, (a, b) in enumerate(els, 1):
        R1, R2 = rows[a]["R"], rows[b]["R"]
        mat = rows[a]["mat"]
        nd = s.SystemData.MaterialCatalogs  # placeholder; index read below
        try:
            g = lde.GetSurfaceAt(a).GetSurfaceCell(
                ZOSAPI.Editors.LDE.SurfaceColumn.Material)
            del g
        except Exception:
            pass
        nd = lde.GetSurfaceAt(a).IndexData if hasattr(
            lde.GetSurfaceAt(a), "IndexData") else None
        # index at wavelength 1 via the INDX operand
        ndv = mf.GetOperandValue(E.INDX, a, 1, 0, 0, 0, 0, 0, 0)
        nF = mf.GetOperandValue(E.INDX, a, 1, 0, 0, 0, 0, 0, 0)
        # thin-lens power with the actual index
        ct = rows[a]["t"]
        p = (ndv - 1.0) * (1.0 / R1 - 1.0 / R2) if R1 and R2 else 0.0
        f = 1.0 / p if p else float("inf")
        q = ((R2 + R1) / (R2 - R1)) if (R2 - R1) else float("nan")
        tot_pow += p
        forms.append((k, a, b, mat, R1, R2, q, f, p, ct))
        print("  %2d  %2d-%-2d  %-11s %6.4f  %5s %9.3f %9.3f  %+7.3f  "
              "%+8.2f  %+8.5f" % (k, a, b, mat, ndv, "-", R1, R2, q, f, p))
    print("  sum of thin-lens element powers %+.5f  (system %+.5f)"
          % (tot_pow, 1.0 / efl))
    print()
    print("  MOULDABILITY")
    print("  el  CT      ET      CT/ET   sd      steepest slope   "
          "sag front / back")
    ok = True
    for (k, a, b, mat, R1, R2, q, f, p, ct) in forms:
        sd = max(rows[a]["sd"], rows[b]["sd"])
        s1 = sag(min(sd, .999 * abs(R1)), R1, rows[a]["k"])
        s2 = sag(min(sd, .999 * abs(R2)), R2, rows[b]["k"])
        et = ct + s2 - s1
        sl = max(math.degrees(math.asin(min(1.0, rows[a]["sd"] / abs(R1)))),
                 math.degrees(math.asin(min(1.0, rows[b]["sd"] / abs(R2)))))
        flag = ""
        if et < 0.8:
            flag += " ET<0.8"; ok = False
        if ct < 1.0:
            flag += " CT<1.0"; ok = False
        if ct > 5.0:
            flag += " CT>5.0"; ok = False
        if not (0.35 <= ct / et <= 3.0):
            flag += " CT/ET"; ok = False
        if sl > 50.0:
            flag += " slope>50deg"; ok = False
        print("  %2d  %6.3f  %6.3f  %6.2f  %6.3f  %8.1f deg      "
              "%+.3f / %+.3f%s" % (k, ct, et, ct / et, sd, sl, s1, s2, flag))
    print()
    print("  AIRSPACES  (centre / at the edge of the larger aperture)")
    for k in range(len(els) - 1):
        b = els[k][1]
        f2 = els[k + 1][0]
        tair = sum(rows[j]["t"] for j in range(b, f2))
        sd = max(rows[b]["sd"], rows[f2]["sd"])
        sb = sag(min(sd, .999 * abs(rows[b]["R"])), rows[b]["R"], rows[b]["k"])
        sf = sag(min(sd, .999 * abs(rows[f2]["R"])), rows[f2]["R"], rows[f2]["k"])
        edge = tair + sf - sb
        flag = "" if edge >= 0.8 and tair >= 0.8 else "   <-- too tight to mount"
        print("    %d-%d   %6.3f / %6.3f mm%s" % (k + 1, k + 2, tair, edge, flag))
        if flag:
            ok = False
    print()
    print("  verdict: %s" % ("mouldable on every check above" if ok
                             else "FAILS at least one moulding check"))
    return {"efl": efl, "isfn": isfn, "hfov": hfov, "forms": forms}


if __name__ == "__main__":
    app = connect()
    HERE = os.path.dirname(os.path.abspath(__file__))
    GLASS = os.path.expanduser(
        r"~\Documents\Zemax\Samples\Sequential\Objectives"
        r"\Cooke 40 degree field.zmx")
    report(app, GLASS, "GLASS REFERENCE")
    for p in sys.argv[1:]:
        report(app, os.path.join(HERE, p), "PLASTIC")
    app.CloseApplication()

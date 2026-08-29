"""Null control on the STAR direct-index route.

Claim under test: the -0.295 mm (-0.98%) EFFL shift the moulded system shows is
too big for the field that produced it. A radial gradient of +/-1.094e-4 over a
5.4 mm semi-diameter in a 2.47 mm wall is a GRIN power of order 2e-5 /mm, i.e.
about 0.06% of system power - roughly 18x short of 0.98%.

Three arms, each a file with the SAME geometry and sampling as the real one,
differing only in the fourth column:

  NULL   every point exactly Nd            -> a correct pipeline moves nothing
  TENTH  dn scaled by 0.1                  -> a physical shift scales with dn
  FULL   the real file                     -> the measurement

NULL is the one that matters. If a perfectly uniform index cloud moves EFFL,
the shift is the import/fit machinery, not the moulding.
"""
import os
from zos import ZOSAPI, connect, HERE

MS = os.path.join(HERE, "ms")
SRC = {1: ("moldstress_s1_index.txt", 1.4917),
       3: ("moldstress_s3_index.txt", 1.5905),
       6: ("moldstress_s6_index.txt", 1.4917)}
E = ZOSAPI.Editors.MFE.MeritOperandType
TMP = os.path.join(HERE, "ctl")
os.makedirs(TMP, exist_ok=True)


def make(scale, tag):
    """Rewrite each index file with dn multiplied by `scale`."""
    out = {}
    for surf, (fn, nd) in SRC.items():
        dst = os.path.join(TMP, "%s_s%d.txt" % (tag, surf))
        with open(os.path.join(MS, fn)) as fh, open(dst, "w") as g:
            for line in fh:
                p = line.split()
                if len(p) < 4:
                    continue
                g.write("%s %s %s %.9E\n"
                        % (p[0], p[1], p[2], nd + scale * (float(p[3]) - nd)))
        out[surf] = dst
    return out


app = connect()
s = app.PrimarySystem


def arm(tag, files):
    assert s.LoadFile(os.path.join(HERE, "plastic-triplet-MoldStress.zmx"), False)
    s.LDE.GetSurfaceAt(7).ThicknessCell.MakeSolveFixed()
    mf = s.MFE
    e0 = mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0)
    if files:
        for surf, path in files.items():
            di = s.LDE.GetSurfaceAt(surf).STARData.DirectIndex
            di.SetDataIsLocal()
            di.FEAData.ImportDirectIndex_1(path)
            di.Fits.Refit()
            di.Fits.GRINStep = 0.50
    e1 = mf.GetOperandValue(E.EFFL, 0, 2, 0, 0, 0, 0, 0, 0)
    w1 = mf.GetOperandValue(E.RWRE, 4, 1, 0, 0, 0, 0, 0, 0)
    print("%-8s EFFL %8.4f -> %8.4f  (%+.4f mm, %+.3f%%)   RWRE(w1) %.4f"
          % (tag, e0, e1, e1 - e0, 100.0 * (e1 - e0) / e0, w1))
    return e1 - e0


arm("none", None)
d0 = arm("NULL", make(0.0, "null"))
d1 = arm("TENTH", make(0.1, "tenth"))
d2 = arm("FULL", {k: os.path.join(MS, v[0]) for k, v in SRC.items()})

print()
print("NULL shift %+.4f mm - a uniform cloud should move NOTHING" % d0)
if abs(d2) > 1e-9:
    print("TENTH/FULL ratio %.3f (a physical, dn-linear shift gives 0.100)"
          % (d1 / d2))
    print("NULL/FULL ratio  %.3f (an artifact-free pipeline gives 0.000)"
          % (d0 / d2))
app.CloseApplication()
print("done")

"""Exec-summary figures for the retardance controls and the paraxial control.

Panel A  2D layout with real traced rays, three fields, and the retardance
         bound per element written on the element it belongs to - the point
         being that the peak moved to a DIFFERENT element.
Panel B  the stress ladder: what the shipped call reported against the closed
         form, over four decades.
Panel C  the seven tensor control arms, shipped vs the route that passes them.
Panel D  the paraxial amplitude response, showing the noise floor.
Panel E  before/after optical performance on the pinned plane - through-focus
         RMS wavefront with and without the moulding stress applied.

Writes figret/*.png.
"""
import json
import math
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

from zos import ZOSAPI, connect, HERE

E = ZOSAPI.Editors.MFE.MeritOperandType
BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
REAL = os.path.join(HERE, "ms6")
FIG = os.path.join(HERE, "figret")
os.makedirs(FIG, exist_ok=True)
SURFACE = "#fcfcfb"
plt.rcParams.update({"font.size": 9, "figure.dpi": 160,
                     "figure.facecolor": SURFACE, "axes.facecolor": SURFACE})
BLUE, ORANGE, GREEN, GREY = "#2a78d6", "#eb6834", "#1baf7a", "#8a8a86"

RET = json.load(open(os.path.join(HERE, "retreal.json")))
FIX = json.load(open(os.path.join(HERE, "retfix.json")))
SWP = json.load(open(os.path.join(HERE, "paraxsweep.json")))
PCT = json.load(open(os.path.join(HERE, "paraxctl.json")))

app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False)
NS = s.LDE.NumberOfSurfaces
IMGPREV = NS - 2
s.LDE.GetSurfaceAt(IMGPREV).ThicknessCell.MakeSolveFixed()
ROWS = [dict(i=i,
             R=float(s.LDE.GetSurfaceAt(i).Radius),
             t=float(s.LDE.GetSurfaceAt(i).Thickness),
             mat=(s.LDE.GetSurfaceAt(i).Material or ""),
             sd=float(s.LDE.GetSurfaceAt(i).SemiDiameter))
        for i in range(NS)]


def sag(r, R):
    if R == 0 or abs(R) > 1e9 or not np.isfinite(R):
        return np.zeros_like(r)
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


# ---- real ray trace for the layout ---------------------------------------
rt = s.Tools.OpenBatchRayTrace()
NF, NP = 3, 11
# `None` is a Python keyword, so the enum member has to come off the type by name
OPD_NONE = getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None")
zacc = {}
acc = 0.0
for r in ROWS[1:NS - 1]:
    zacc[r["i"]] = acc
    acc += r["t"]
zacc[NS - 1] = acc

# read the traced rays surface by surface
seg = {}
for surf in range(1, NS):
    hit = rt.CreateNormUnpol(NF * NP, ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
    for fi in range(NF):
        hy = [0.0, 0.7, 1.0][fi]
        for pi in range(NP):
            py = -1.0 + 2.0 * pi / (NP - 1.0)
            hit.AddRay(2, 0.0, hy, 0.0, py, OPD_NONE)
    # RunAndWaitForCompletion lives on the BATCH TOOL, not on the ray data
    rt.RunAndWaitForCompletion()
    hit.StartReadingResults()
    k = 0
    while True:
        ok, rayno, err, vig, x, y, z, l, m, n, l2, m2, n2, op, ii = hit.ReadNextResult()
        if not ok:
            break
        if err == 0:
            seg.setdefault(rayno - 1, []).append(
                (zacc.get(surf, acc) + (z if surf < NS - 1 else 0.0), y))
        k += 1
    # the ray-data object has no Close(); the BATCH TOOL owns the lifetime
    try:
        rt.ClearData()
    except Exception:
        pass
rt.Close()

fig = plt.figure(figsize=(11.5, 12.2))
gs = fig.add_gridspec(4, 2, height_ratios=[1.15, 1.0, 1.0, 1.0], hspace=0.52, wspace=0.26)

# ================================================================== PANEL A
axA = fig.add_subplot(gs[0, :])
i = 1
while i < NS - 1:
    if ROWS[i]["mat"]:
        j = i + 1
        sd = max(ROWS[i]["sd"], ROWS[j]["sd"])
        rr = np.linspace(-sd, sd, 240)
        z1 = zacc[i] + sag(np.abs(rr), ROWS[i]["R"])
        z2 = zacc[j] + sag(np.abs(rr), ROWS[j]["R"])
        axA.fill(np.concatenate([z1, z2[::-1]]), np.concatenate([rr, rr[::-1]]),
                 color="#dfe9f6", ec="#5b7fa8", lw=0.9, zorder=2)
        i = j + 1
    else:
        i += 1
for rayno, pts in seg.items():
    fi = rayno // NP
    p = np.array(pts)
    axA.plot(p[:, 0], p[:, 1], lw=0.55, color=[BLUE, ORANGE, GREEN][fi],
             alpha=0.75, zorder=3)
axA.axvline(acc, color=GREY, lw=1.1, ls="--", zorder=1)
axA.text(acc, -8.6, " image\n (pinned)", fontsize=7.5, color=GREY, va="bottom")
lab = {1: (1, 2), 3: (3, 4), 5: (5, 6)}
# the three elements sit within 16 mm of each other on a ~50 mm axis, so the
# labels have to be staggered in z AND y or they overlap into unreadability
place = {1: (-1.5, 9.2), 3: (13.0, 6.6), 5: (26.5, 9.2)}
for f, (a, b) in lab.items():
    w = RET["per_surface"][str(f)]["bound_waves"]
    bl = RET["per_surface"][str(f)]["biref_rad_per_mm"]
    zc = 0.5 * (zacc[a] + zacc[b])
    ytop = max(ROWS[a]["sd"], ROWS[b]["sd"])
    peak = (f == int(RET["peak_surface"]))
    tx, ty = place[f]
    axA.annotate("el %d: %.3f waves\n%.2f rad/mm" % ((f + 1) // 2, w, bl),
                 xy=(zc, ytop + 0.2), xytext=(tx, ty),
                 ha="center", fontsize=7.4, zorder=6,
                 color="#b3261e" if peak else "#333",
                 fontweight="bold" if peak else "normal",
                 arrowprops=dict(arrowstyle="-", lw=0.7,
                                 color="#b3261e" if peak else "#999"),
                 bbox=dict(fc="#fff3f1" if peak else "#f4f4f2",
                           ec="#b3261e" if peak else "#ccc", lw=0.8, pad=0.3))
axA.set_title("A.  Plastic Cooke triplet, real rays at 0 / 6.3 / 9$^\\circ$ — retardance BOUND per element\n"
              "the corrected route puts the peak on the biconcave middle element; the old call put it on element 1",
              fontsize=9.5, loc="left")
axA.set_xlabel("z (mm)")
axA.set_ylabel("height (mm)")
axA.set_ylim(-9.6, 11.6)
axA.set_aspect("equal", adjustable="box")

# ================================================================== PANEL B
axB = fig.add_subplot(gs[1, 0])
lad = FIX["ladder"]
S = sorted(float(k) for k in lad if float(k) > 0)
cf = [lad["%.4f" % v]["closed"] / (2 * math.pi) for v in S]
sh = [lad["%.4f" % v]["shipped"] / (2 * math.pi) for v in S]
axB.loglog(S, cf, "o-", color=GREEN, lw=1.6, ms=4, label="closed form")
axB.loglog(S, sh, "s-", color=ORANGE, lw=1.6, ms=4, label="what the tool printed")
axB.axhline(0.5, color=GREY, lw=0.8, ls=":")
axB.text(0.025, 0.53, "$\\pi$", fontsize=8, color=GREY)
axB.set_xlabel("uniform uniaxial stress (N/mm$^2$)")
axB.set_ylabel("peak retardance (waves)")
axB.set_title("B.  It did not scale with stress\n814$\\times$ high at 0.02, 0.16$\\times$ at 200",
              fontsize=9.5, loc="left")
axB.legend(fontsize=7.5, frameon=False)
axB.grid(alpha=0.25, which="both")

# ================================================================== PANEL C
axC = fig.add_subplot(gs[1, 1])
arms = ["null", "hydrostatic", "biaxial", "axial", "uniaxial", "shear", "rot45"]
lbl = ["null", "hydro", "biaxial", "axial", "uniax", "shear", "rot45"]
shipped = [FIX["arms"][a]["1"]["shipped"] / (2 * math.pi) for a in arms]
closed = [FIX["arms"][a]["1"]["closed"] / (2 * math.pi) for a in arms]
x = np.arange(len(arms))
axC.bar(x - 0.21, closed, 0.42, color=GREEN, label="true (closed form)")
axC.bar(x + 0.21, shipped, 0.42, color=ORANGE, label="what the tool printed")
for k in range(4):
    axC.annotate("", xy=(x[k] + 0.21, shipped[k]), xytext=(x[k] + 0.21, shipped[k] + 0.12),
                 arrowprops=dict(arrowstyle="-", color="#b3261e", lw=1.0))
    axC.text(x[k] + 0.21, shipped[k] + 0.14, "true = 0", fontsize=6.4,
             color="#b3261e", ha="center", rotation=90, va="bottom")
axC.set_xticks(x)
axC.set_xticklabels(lbl, fontsize=7.6, rotation=20)
axC.set_ylabel("peak retardance (waves)")
axC.set_ylim(0, 1.35)
axC.set_title("C.  Four fields with ZERO retardance\nread 0.31–0.70 waves, element 1, 10 N/mm$^2$",
              fontsize=9.5, loc="left")
axC.legend(fontsize=7.5, frameon=False, loc="upper left")
axC.grid(alpha=0.25, axis="y")

# ================================================================== PANEL D
axD = fig.add_subplot(gs[2, 0])
sw = SWP["sweep"]
sc = sorted(float(k) for k in sw if float(k) > 0)
dn = [sw["%g" % v]["peak_dn"] for v in sc]
rat = [sw["%g" % v]["ratio"] for v in sc]
pos = [(d, r) for d, r in zip(dn, rat) if r is not None and r > 0]
neg = [(d, r) for d, r in zip(dn, rat) if r is not None and r <= 0]
axD.semilogx([p[0] for p in pos], [p[1] for p in pos], "o-", color=BLUE, lw=1.5, ms=4)
if neg:
    axD.semilogx([p[0] for p in neg], [p[1] for p in neg], "x", color="#b3261e", ms=8,
                 mew=2, label="WRONG SIGN")
axD.axhline(1.0, color=GREEN, lw=1.2, ls="--", label="exact")
axD.axvline(4.825e-6, color=ORANGE, lw=1.1, ls=":")
axD.text(5.4e-6, -0.55, " the real field's\n smooth component", fontsize=6.8, color=ORANGE)
axD.set_xlabel("peak $\\Delta n$ of the synthetic analytic field")
axD.set_ylabel("measured / closed form")
axD.set_ylim(-1.1, 3.5)
axD.set_title("D.  STAR's response to an EXACTLY LINEAR input\nnoise below $\\Delta n\\approx10^{-6}$, settles at 0.82–0.91 above $10^{-5}$",
              fontsize=9.5, loc="left")
axD.legend(fontsize=7.5, frameon=False, loc="upper left")
axD.grid(alpha=0.25, which="both")

# ================================================================== PANEL E
axE = fig.add_subplot(gs[2, 1])
labels = ["synthetic\n(analytic, exactly linear)", "real moulding\nfield"]
vals = [PCT["synthetic_linearity_tenth_over_full"], PCT["real_linearity_tenth_over_full"]]
axE.bar([0, 1], vals, 0.5, color=[BLUE, ORANGE])
axE.axhline(0.1, color=GREEN, lw=1.4, ls="--")
axE.text(1.45, 0.101, "first order\ndemands 0.100", fontsize=7.4, color=GREEN, va="bottom", ha="right")
for i2, v in enumerate(vals):
    axE.text(i2, v + 0.004, "%.4f" % v, ha="center", fontsize=8.4, fontweight="bold")
axE.set_xticks([0, 1])
axE.set_xticklabels(labels, fontsize=7.6)
axE.set_ylabel("tenth / full")
axE.set_ylim(0, 0.125)
axE.set_title("E.  The non-linearity is STAR's, not the field's\nan input linear BY CONSTRUCTION shows the same defect",
              fontsize=9.5, loc="left")
axE.grid(alpha=0.25, axis="y")

# ================================================================== PANEL F
axF = fig.add_subplot(gs[3, :])
per = RET["per_surface"]
sf = ["1", "3", "5"]
old = [RET["old_call_waves"][k] for k in sf]
new = [per[k]["bound_waves"] for k in sf]
x = np.arange(3)
axF.bar(x - 0.2, old, 0.4, color=ORANGE, label="what the tool printed")
axF.bar(x + 0.2, new, 0.4, color=GREEN, label="corrected bound")
axF.set_xticks(x)
axF.set_xticklabels(["element 1\nPMMA, biconvex", "element 2\nPOLYSTYR, biconcave",
                     "element 3\nPMMA, biconvex"], fontsize=8)
axF.set_ylabel("peak retardance (waves)")
axF.annotate("the tool named THIS\nthe worst element", xy=(0 - 0.2, old[0]),
             xytext=(0.35, 1.18), fontsize=7.8, color="#b3261e",
             arrowprops=dict(arrowstyle="->", color="#b3261e", lw=1.1))
axF.annotate("it is actually THIS one", xy=(1 + 0.2, new[1]), xytext=(1.55, 1.30),
             fontsize=7.8, color="#1b7a4a",
             arrowprops=dict(arrowstyle="->", color="#1b7a4a", lw=1.1))
axF.set_title("F.  The real moulding field — the correction changes which element you would redesign\n"
              "RMS wavefront moved only 0.132177 $\\to$ 0.124818 waves at a pinned plane, so the ratio is 176$\\times$, not the withdrawn 585$\\times$",
              fontsize=9.5, loc="left")
axF.legend(fontsize=7.8, frameon=False)
axF.grid(alpha=0.25, axis="y")

fig.suptitle("MoldStress — the polarisation half under controls, and the paraxial anomaly settled",
             fontsize=12, y=0.995, x=0.012, ha="left", fontweight="bold")
fig.savefig(os.path.join(FIG, "summary.png"), bbox_inches="tight", facecolor=SURFACE)
print("wrote", os.path.join(FIG, "summary.png"))
app.CloseApplication()
print("done")

"""Exec summary for the accuracy batch - uncertainty bands, depth-model
adjudication, and the STAR round-trip check.

Panel A  2D layout with real traced rays, three fields, and where the peak
         retardance lands - on the ONE element whose coefficient has no
         published interval.
Panel B  before / after MoldStress on the pinned image plane, with the
         retardance beside it, because that is the 1513x point.
Panel C  item 1 - how wide each polymer's retardance answer is, given the
         interval its own source states. Two of five state none.
Panel D  item 2 - the two depth models, adjudicated.
Panel E  item 3 - the headline round-trip measurement: loading a uniform index
         collapses F, d and C onto the d-line path.
Panel F  item 3 - the check discriminates: clean vs poisoned.

Writes figret/batch.png.
"""
import json
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

from zos import ZOSAPI, connect, HERE

BASE = os.path.join(HERE, "plastic-cooke-MoldStress.zmx")
FIG = os.path.join(HERE, "figret")
os.makedirs(FIG, exist_ok=True)
SURFACE = "#fcfcfb"
plt.rcParams.update({"font.size": 9, "figure.dpi": 160,
                     "figure.facecolor": SURFACE, "axes.facecolor": SURFACE})
BLUE, ORANGE, GREEN, GREY, RED = "#2a78d6", "#eb6834", "#1baf7a", "#8a8a86", "#b3261e"

RT = json.load(open(os.path.join(HERE, "starroundtrip.json")))

# From gui_run.txt - the run through the OpticStudio GUI on 2026-08-29, which is
# the only route these numbers have ever been confirmed by. Deliberately NOT
# recomputed here: a figure that recomputes its own numbers only ever agrees
# with itself, and the 176x caption this batch retracted was born that way.
WF_ORIG, WF_BASE, WF_MS = 0.174136, 0.132177, 0.132978
RET_WAVES, RATIO = 1.2125, 1513

app = connect()
s = app.PrimarySystem
assert s.LoadFile(BASE, False)
NS = s.LDE.NumberOfSurfaces
s.LDE.GetSurfaceAt(NS - 2).ThicknessCell.MakeSolveFixed()
ROWS = [dict(i=i, R=float(s.LDE.GetSurfaceAt(i).Radius),
             t=float(s.LDE.GetSurfaceAt(i).Thickness),
             mat=(s.LDE.GetSurfaceAt(i).Material or ""),
             sd=float(s.LDE.GetSurfaceAt(i).SemiDiameter)) for i in range(NS)]


def sag(r, R):
    if R == 0 or abs(R) > 1e9 or not np.isfinite(R):
        return np.zeros_like(r)
    q = np.clip(1.0 - (r / R) ** 2, 0.0, None)
    return (r ** 2 / R) / (1.0 + np.sqrt(q))


rt = s.Tools.OpenBatchRayTrace()
NF, NP = 3, 11
OPD_NONE = getattr(ZOSAPI.Tools.RayTrace.OPDMode, "None")
zacc, acc = {}, 0.0
for r in ROWS[1:NS - 1]:
    zacc[r["i"]] = acc
    acc += r["t"]
zacc[NS - 1] = acc
seg = {}
for surf in range(1, NS):
    hit = rt.CreateNormUnpol(NF * NP, ZOSAPI.Tools.RayTrace.RaysType.Real, surf)
    for fi in range(NF):
        for pi in range(NP):
            hit.AddRay(2, 0.0, [0.0, 0.7, 1.0][fi], 0.0,
                       -1.0 + 2.0 * pi / (NP - 1.0), OPD_NONE)
    rt.RunAndWaitForCompletion()
    hit.StartReadingResults()
    while True:
        ok, rayno, err, vig, x, y, z, l, m, n, l2, m2, n2, op, ii = hit.ReadNextResult()
        if not ok:
            break
        if err == 0:
            seg.setdefault(rayno - 1, []).append(
                (zacc.get(surf, acc) + (z if surf < NS - 1 else 0.0), y))
    try:
        rt.ClearData()
    except Exception:
        pass
rt.Close()
app.CloseApplication()

fig = plt.figure(figsize=(12.0, 13.6))
gs = fig.add_gridspec(4, 2, height_ratios=[1.15, 1.0, 1.0, 1.0],
                      hspace=0.62, wspace=0.27)

# ================================================================== PANEL A
axA = fig.add_subplot(gs[0, :])
i = 1
while i < NS - 1:
    if ROWS[i]["mat"]:
        j = i + 1
        sd = max(ROWS[i]["sd"], ROWS[j]["sd"])
        rr = np.linspace(-sd, sd, 240)
        axA.fill(np.concatenate([zacc[i] + sag(np.abs(rr), ROWS[i]["R"]),
                                 (zacc[j] + sag(np.abs(rr), ROWS[j]["R"]))[::-1]]),
                 np.concatenate([rr, rr[::-1]]),
                 color="#dfe9f6", ec="#5b7fa8", lw=0.9, zorder=2)
        i = j + 1
    else:
        i += 1
for rayno, pts in seg.items():
    p = np.array(pts)
    axA.plot(p[:, 0], p[:, 1], lw=0.55, color=[BLUE, ORANGE, GREEN][rayno // NP],
             alpha=0.75, zorder=3)
axA.axvline(acc, color=GREY, lw=1.1, ls="--", zorder=1)
axA.text(acc, -8.6, " image\n (pinned)", fontsize=7.5, color=GREY, va="bottom")
notes = {1: ("el 1  MS_PMMA", "interval 3.07x wide", -1.5, 9.6),
         3: ("el 2  MS_POLYSTYR", "PEAK 1.2125 waves\ninterval UNQUANTIFIED", 13.0, 7.6),
         5: ("el 3  MS_PMMA", "interval 3.07x wide", 27.0, 9.6)}
for f, (nm, sub, tx, ty) in notes.items():
    peak = (f == 3)
    zc = 0.5 * (zacc[f] + zacc[f + 1])
    axA.annotate("%s\n%s" % (nm, sub),
                 xy=(zc, max(ROWS[f]["sd"], ROWS[f + 1]["sd"]) + 0.2),
                 xytext=(tx, ty), ha="center", fontsize=7.4, zorder=6,
                 color=RED if peak else "#333",
                 fontweight="bold" if peak else "normal",
                 arrowprops=dict(arrowstyle="-", lw=0.7,
                                 color=RED if peak else "#999"),
                 bbox=dict(fc="#fff3f1" if peak else "#f4f4f2",
                           ec=RED if peak else "#ccc", lw=0.8, pad=0.3))
axA.set_title("A.  Plastic Cooke triplet, real rays at 0 / 6.3 / 9$^\\circ$ - where the answer is least certain\n"
              "the peak retardance lands on the ONE element whose coefficient source states no interval at all",
              fontsize=9.5, loc="left")
axA.set_xlabel("z (mm)")
axA.set_ylabel("height (mm)")
axA.set_ylim(-9.6, 12.2)
axA.set_aspect("equal", adjustable="box")
axA.grid(alpha=0.18)

# ================================================================== PANEL B
axB = fig.add_subplot(gs[1, 0])
axB.bar([0, 1, 2], [WF_ORIG, WF_BASE, WF_MS], 0.55, color=[GREY, BLUE, ORANGE])
for k, v in enumerate([WF_ORIG, WF_BASE, WF_MS]):
    axB.text(k, v + 0.005, "%.6f" % v, ha="center", fontsize=8.0, fontweight="bold")
axB.set_xticks([0, 1, 2])
axB.set_xticklabels(["original\nmaterials", "baseline\npolymers, no stress",
                     "with moulding\neffects"], fontsize=7.6)
axB.set_ylabel("RMS wavefront (waves)")
axB.set_ylim(0, 0.225)
axB.annotate("+0.000802 waves\n(+0.6%)", xy=(2, WF_MS), xytext=(1.55, 0.185),
             fontsize=7.8, color=ORANGE, ha="center",
             arrowprops=dict(arrowstyle="->", color=ORANGE, lw=1.0))
axB.set_title("B.  Before / after MoldStress, SAME image plane\nso this is optics and not refocusing",
              fontsize=9.5, loc="left")
axB.grid(alpha=0.25, axis="y")

axB2 = fig.add_subplot(gs[1, 1])
axB2.bar([0, 1], [WF_MS - WF_BASE, RET_WAVES], 0.5, color=[ORANGE, RED])
axB2.set_yscale("log")
axB2.set_xticks([0, 1])
axB2.set_xticklabels(["wavefront change\n0.000802 waves",
                      "peak retardance\n1.2125 waves"], fontsize=7.8)
axB2.set_ylabel("waves (log scale)")
axB2.set_title("B2.  The wavefront number understates it %d$\\times$\nfor a polarisation-sensitive system the retardance IS the result" % RATIO,
               fontsize=9.5, loc="left")
axB2.grid(alpha=0.25, axis="y")

# ================================================================== PANEL C
axC = fig.add_subplot(gs[2, 0])
# hi/lo of each stated interval, read off Polymers.cs rather than retyped:
# PMMA [-4.6,-1.5], POLYCARB [72,82], TOPAS [-9,-8]; two publish none.
pol = [("PMMA", 4.6 / 1.5, True), ("POLYCARB", 82.0 / 72.0, True),
       ("COC_TOPAS", 9.0 / 8.0, True), ("POLYSTYR", 0.0, False),
       ("COP_ZEONEX", 0.0, False)]
x = np.arange(len(pol))
axC.bar(x, [p[1] for p in pol], 0.55,
        color=[BLUE if p[2] else "#efefec" for p in pol],
        edgecolor=[BLUE if p[2] else RED for p in pol],
        hatch=[None, None, None, "//", "//"])
for k, p in enumerate(pol):
    axC.text(k, (p[1] + 0.07) if p[2] else 0.12,
             ("%.2fx" % p[1]) if p[2] else "no interval\npublished",
             ha="center", fontsize=7.5, fontweight="bold",
             color="#333" if p[2] else RED)
axC.axhline(1.0, color=GREY, lw=1.0, ls="--")
axC.set_xticks(x)
axC.set_xticklabels([p[0] for p in pol], fontsize=7.2, rotation=18)
axC.set_ylabel("width of the answer (hi / lo)")
axC.set_ylim(0, 3.8)
axC.set_title("C.  Item 1 - how wide is the retardance answer?\npropagated from each source's OWN stated interval; 2 of 5 state none",
              fontsize=9.5, loc="left")
axC.grid(alpha=0.25, axis="y")

# ================================================================== PANEL D
axD = fig.add_subplot(gs[2, 1])
axD.bar([0, 1], [0.511, 0.970], 0.5, color=[BLUE, ORANGE])
for k, v in enumerate([0.511, 0.970]):
    axD.text(k, v + 0.025, "%.3f" % v, ha="center", fontsize=8.4, fontweight="bold")
axD.set_xticks([0, 1])
axD.set_xticklabels(["RMS over the 58 of 81\ndepths carrying signal",
                     "worst single depth"], fontsize=7.6)
axD.set_ylabel("fractional shape difference")
axD.set_ylim(0, 1.35)
axD.text(0.5, 1.16,
         "mid-plane excluded BY PHYSICS: Eulerian 2.2e-40 vs Lagrangian 3.1e-08 is\n"
         "the assumption the port exists to replace, and it dominated v1 of this metric",
         ha="center", fontsize=6.9, color=GREY, style="italic")
axD.set_title("D.  Item 2 - the two depth models, adjudicated\na ~50% shape disagreement nothing was reporting",
              fontsize=9.5, loc="left")
axD.grid(alpha=0.25, axis="y")

# ================================================================== PANEL E
axE = fig.add_subplot(gs[3, 0])
lam = [0.486133, 0.587562, 0.656273]
unl = [5.990880301, 5.966800350, 5.956615491]
ld = [unl[1] + RT["opth_dload"][k] for k in range(3)]
axE.plot(lam, [(v - unl[1]) * 1e3 for v in unl], "o-", color=BLUE, lw=1.6, ms=6,
         label="unloaded - the material keeps its dispersion")
axE.plot(lam, [(v - unl[1]) * 1e3 for v in ld], "s--", color=RED, lw=1.6, ms=6,
         label="STAR uniform index loaded - collapsed")
for k, tag in enumerate(["F", "d", "C"]):
    axE.annotate(tag, (lam[k], (unl[k] - unl[1]) * 1e3), textcoords="offset points",
                 xytext=(0, 9), ha="center", fontsize=8.5, color=BLUE,
                 fontweight="bold")
axE.axhline(0, color=GREY, lw=0.9, ls=":")
axE.set_xlabel("wavelength ($\\mu$m)")
axE.set_ylabel("optical path re. d-line ($\\mu$m)")
axE.legend(fontsize=7.2, frameon=False, loc="upper right")
axE.set_title("E.  Item 3 - the headline round-trip measurement\nloading a uniform index collapses F, d and C onto the d-line path EXACTLY",
              fontsize=9.5, loc="left")
axE.grid(alpha=0.25)

# ================================================================== PANEL F
axF = fig.add_subplot(gs[3, 1])
axF.bar([0, 1], [0, 5], 0.5, color=[GREEN, RED])
axF.text(0, 0.18, "0 of 10 fail", ha="center", fontsize=8.4,
         fontweight="bold", color=GREEN)
axF.text(1, 5.18, "5 of 10 fail", ha="center", fontsize=8.4,
         fontweight="bold", color=RED)
axF.set_xticks([0, 1])
axF.set_xticklabels(["clean run", "--poison\neach arm's SUBJECT broken"], fontsize=7.8)
axF.set_ylabel("claims failing")
axF.set_ylim(0, 7.2)
axF.text(0.5, 6.3,
         "the first poison SKIPPED ApplyStress() and the arm STILL passed to 1.0000:\n"
         "GetPointRetardanceList reads the fit, not the applied state",
         ha="center", fontsize=6.9, color=GREY, style="italic")
axF.set_title("F.  Item 3 - the check discriminates\nwithout this, \"0 failed\" is a sentence and not evidence",
              fontsize=9.5, loc="left")
axF.grid(alpha=0.25, axis="y")

fig.suptitle("MoldStress - the accuracy batch: stated intervals propagated, the depth models adjudicated, the STAR interface checked",
             fontsize=12, y=0.997, x=0.012, ha="left", fontweight="bold")
fig.savefig(os.path.join(FIG, "batch.png"), bbox_inches="tight", facecolor=SURFACE)
print("wrote", os.path.join(FIG, "batch.png"))

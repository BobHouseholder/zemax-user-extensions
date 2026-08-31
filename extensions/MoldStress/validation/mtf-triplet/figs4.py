"""Exec summary for item 3 - the pin-order anomaly, solved.

Panel A  2D layout with real traced rays, the two reads marked on it.
Panel B  before / after MoldStress at the pinned plane, both modes.
Panel C  the four stages: the difference survives every READ.
Panel D  the acts: what moves the reading, and what does not.

Every number is read from the pinorder*.json the experiments wrote.
Writes figret/item3.png.
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
BLUE, ORANGE, GREEN, PINK = "#0072B2", "#E69F00", "#009E73", "#CC79A7"
GREY, RED, INK = "#8a8a86", "#b3261e", "#222"

P3 = json.load(open(os.path.join(HERE, "pinorder3.json")))
P5 = json.load(open(os.path.join(HERE, "pinorder5.json")))
P6 = json.load(open(os.path.join(HERE, "pinorder6.json")))
P7 = json.load(open(os.path.join(HERE, "pinorder7.json")))
STALE, FRESH = P3["A"]["w0"], P3["B"]["w0"]
GAP = FRESH - STALE
BASELINE = P3["A"]["baseline"]

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
        ok, rn, err, vig, x, y, z, l, m, n, l2, m2, n2, op, ii = hit.ReadNextResult()
        if not ok:
            break
        if err == 0:
            seg.setdefault(rn - 1, []).append(
                (zacc.get(surf, acc) + (z if surf < NS - 1 else 0.0), y))
    try:
        rt.ClearData()
    except Exception:
        pass
rt.Close()
app.CloseApplication()

fig = plt.figure(figsize=(12.0, 10.6))
gs = fig.add_gridspec(3, 2, height_ratios=[1.18, 1.0, 1.0], hspace=0.60, wspace=0.26)

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
for rn, pts in seg.items():
    p = np.array(pts)
    axA.plot(p[:, 0], p[:, 1], lw=0.55, color=[BLUE, ORANGE, GREEN][rn // NP],
             alpha=0.75, zorder=3)
axA.axvline(acc, color=GREY, lw=1.2, ls="--", zorder=1)
axA.annotate("the design plane —\nBOTH reads happen here",
             xy=(acc, 3.0), xytext=(acc - 14.5, 9.0), fontsize=8, color=INK,
             ha="center", arrowprops=dict(arrowstyle="->", color=GREY, lw=1.0),
             bbox=dict(fc="#f4f4f2", ec="#ccc", lw=0.8, pad=0.35))
axA.text(1.0, -9.4, "same lens · same stress bytes · same thickness to the nanometre\n"
                   "two answers %.6f and %.6f waves, %.6f apart"
         % (STALE, FRESH, GAP), fontsize=8.4, color=RED, fontweight="bold")
axA.set_title("A.  Plastic Cooke triplet, real rays at 0 / 6.3 / 9$^\\circ$ — the anomaly was never in the optics",
              fontsize=9.5, loc="left")
axA.set_xlabel("z (mm)")
axA.set_ylabel("height (mm)")
axA.set_ylim(-10.2, 11.6)
axA.set_aspect("equal", adjustable="box")
axA.grid(alpha=0.18)

# ================================================================== PANEL B
axB = fig.add_subplot(gs[1, 0])
vals = [BASELINE, STALE, FRESH]
axB.bar([0, 1, 2], vals, 0.55, color=[BLUE, RED, GREEN])
for k, v in enumerate(vals):
    axB.text(k, v + 0.003, "%.6f" % v, ha="center", fontsize=8.4, fontweight="bold")
axB.axhline(BASELINE, color=GREY, lw=1.0, ls=":")
axB.set_xticks([0, 1, 2])
axB.set_xticklabels(["baseline\nbefore MoldStress",
                     "STALE read\npin first, never write",
                     "REFRESHED read\nthe tool's number"], fontsize=7.6)
axB.set_ylabel("RMS wavefront (waves)")
axB.set_ylim(0, max(vals) * 1.22)
axB.text(1, STALE * 0.45, "%+.6f\nwaves" % (STALE - BASELINE), ha="center",
         fontsize=8, fontweight="bold", color="white")
axB.text(2, FRESH * 0.45, "%+.6f\nwaves" % (FRESH - BASELINE), ha="center",
         fontsize=8, fontweight="bold", color="white")
axB.set_title("B.  Before / after MoldStress at the pinned plane\nthe stale read flips the SIGN of the effect",
              fontsize=9.5, loc="left")
axB.grid(alpha=0.25, axis="y")

# ================================================================== PANEL C
axC = fig.add_subplot(gs[1, 1])
stages = ["w0\nafter\nApplyStress", "w1\nread\nagain", "w2\nafter\nEFFL", "w3\nafter\nREAY"]
a = [P3["A"][k] for k in ("w0", "w1", "w2", "w3")]
b = [P3["B"][k] for k in ("w0", "w1", "w2", "w3")]
x = np.arange(4)
axC.plot(x, a, "o-", color=RED, lw=2, ms=7, label="A  pin first (never writes)")
axC.plot(x, b, "s-", color=GREEN, lw=2, ms=7, label="B  pin after (writes)")
axC.fill_between(x, a, b, color=RED, alpha=0.10)
axC.set_xticks(x)
axC.set_xticklabels(stages, fontsize=7.2)
axC.set_ylabel("RMS wavefront (waves)")
axC.legend(fontsize=7.4, frameon=False, loc="center right")
axC.set_title("C.  Every READ leaves the gap untouched\na paraxial operand and a real-ray trace change nothing",
              fontsize=9.5, loc="left")
axC.grid(alpha=0.25)

# ================================================================== PANEL D
axD = fig.add_subplot(gs[2, :])
acts = [("nothing (control)", 0.0, False)]
for a_ in P6[1:]:
    acts.append((a_["arm"].split("  ", 1)[-1], a_["frac"], True))
acts.append(("surf7 semi-diameter, pinned", P5[2]["frac_of_gap"], True))
acts.append(("surf1 thickness = itself\n(what the fix does)", P7[1]["frac"], True))
lab = [a_[0] for a_ in acts]
frac = [100 * a_[1] for a_ in acts]
cols = [GREY] + [GREEN] * (len(acts) - 1)
axD.barh(np.arange(len(acts)), frac, 0.6, color=cols)
for k, v in enumerate(frac):
    axD.text(v + 1.5, k, "%.0f%%" % v, va="center", fontsize=8.2,
             fontweight="bold", color=INK)
axD.set_yticks(np.arange(len(acts)))
axD.set_yticklabels(lab, fontsize=7.6)
axD.invert_yaxis()
axD.set_xlim(0, 118)
axD.set_xlabel("share of the 0.008160158-wave gap the act closes")
axD.set_title("D.  What moves the reading — a comment string moves it as completely as an aperture does\n"
              "reads never refresh; ANY write to the lens data editor always does",
              fontsize=9.5, loc="left")
axD.grid(alpha=0.25, axis="x")

fig.suptitle("MoldStress — the pin-order anomaly was a STALE READ, and the tool's number was the right one",
             fontsize=12, y=0.997, x=0.012, ha="left", fontweight="bold")
fig.savefig(os.path.join(FIG, "item3.png"), bbox_inches="tight", facecolor=SURFACE)
print("wrote", os.path.join(FIG, "item3.png"))

"""The three-layout comparison figure, normalised by focal length."""
import json, math, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
FIG = os.path.join(HERE, "fig")
os.makedirs(FIG, exist_ok=True)
D = json.load(open(os.path.join(HERE, "layouts.json")))
FC = ["#2a78d6", "#eb6834", "#1baf7a"]
SURFACE = "#fcfcfb"
plt.rcParams.update({"font.size": 9, "axes.grid": False, "figure.dpi": 150,
                     "figure.facecolor": SURFACE, "axes.facecolor": SURFACE})


def sag(r, R, k=0.0):
    if R == 0 or abs(R) > 1e9:
        return np.zeros_like(r)
    c = 1.0 / R
    q = np.clip(1.0 - (1.0 + k) * c * c * r * r, 0.0, None)
    return c * r * r / (1.0 + np.sqrt(q))


fig, axes = plt.subplots(3, 1, figsize=(9.6, 9.4))
for ax, d in zip(axes, D):
    rows, efl = d["rows"], d["efl"]
    n = len(rows)
    vz, acc = {}, 0.0
    for r in rows:
        if r["i"] == 0:
            continue
        vz[r["i"]] = acc
        acc += r["t"]

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

    for k, (a, b) in enumerate(els):
        ra, rb = rows[a], rows[b]
        sd = max(ra["sd"], rb["sd"])
        rmf = min(sd, .995 * abs(ra["R"])) if ra["R"] else sd
        rmb = min(sd, .995 * abs(rb["R"])) if rb["R"] else sd
        xf = np.linspace(-rmf, rmf, 241)
        xb = np.linspace(-rmb, rmb, 241)
        zf = (vz[a] + sag(xf, ra["R"], ra["k"])) / efl
        zb = (vz[b] + sag(xb, rb["R"], rb["k"])) / efl
        col = "#dfe4ea" if k != 1 else "#eee0d2"
        ax.fill(np.concatenate([zf, zb[::-1]]),
                np.concatenate([xf, xb[::-1]]) / efl, col, alpha=.9, zorder=2)
        ax.plot(zf, xf / efl, "k-", lw=.9, zorder=3)
        ax.plot(zb, xb / efl, "k-", lw=.9, zorder=3)
        for sgn in (1, -1):
            q = -1 if sgn > 0 else 0
            ax.plot([zf[q], zb[q]], [sgn * rmf / efl, sgn * rmb / efl],
                    "k-", lw=.9, zorder=3)

    st = next((r for r in rows if r["stop"] and r["i"] not in
               [x for e in els for x in e]), None)
    stop_surf = next((r["i"] for r in rows if r["stop"]), None)
    if stop_surf is not None:
        sr = rows[stop_surf]
        z = (vz[stop_surf] + sag(np.array([sr["sd"]]), sr["R"], sr["k"])[0]) / efl
        h = sr["sd"] / efl
        for sgn in (1, -1):
            ax.plot([z, z], [sgn * h, sgn * (h + .045)], color="#b4531a",
                    lw=2.4, zorder=5)
        ax.text(z, h + .062, "stop", ha="center", fontsize=7.5,
                color="#b4531a")

    zi = vz[n - 1] / efl
    ymax = max(r["sd"] for r in rows[1:n - 1]) / efl
    ax.plot([zi, zi], [-1.15 * ymax, 1.15 * ymax], "k-", lw=1.6)

    for fi, fan in enumerate(d["fans"]):
        for ray in fan["rays"]:
            if len(ray) < 2:
                continue
            ax.plot([(vz[int(sv)] + z) / efl for sv, y, z in ray],
                    [y / efl for sv, y, z in ray],
                    color=FC[fi % 3], lw=.55, alpha=.85, zorder=4)
        ax.plot([], [], color=FC[fi % 3], lw=1.3,
                label="%.1f$^\\circ$" % fan["deg"])

    ax.set_title("%s\n%s  ·  EFL %.1f mm  ·  %s"
                 % (d["label"], d["mats"], efl, d["file"]),
                 fontsize=8.8, loc="left")
    ax.set_aspect("equal")
    ax.set_xlabel("z / EFL")
    ax.set_ylabel("y / EFL")
    ax.legend(loc="upper left", fontsize=7, framealpha=.9, ncol=3)
    ax.set_ylim(-.42, .42)
    ax.set_xlim(-.05, max(zi + .06, 1.05))
    ax.grid(alpha=.18)

fig.suptitle("Same form, normalised by focal length", fontsize=10.5, y=.997)
fig.tight_layout(rect=(0, 0, 1, .985))
fig.savefig(os.path.join(FIG, "form.png"), bbox_inches="tight")
print("fig/form.png")

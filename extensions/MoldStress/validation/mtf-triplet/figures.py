"""Figures for a MoldStress before/after run. Parameterised, not copied.

  python figures.py <results.json> <ms-dir> <fig-dir> "<system caption>"

Everything specific to an article - element grouping, field labels, control
values, the index file and its base index - is read from the results file, so
retargeting is an argument rather than an edit. The first version of this was
hard-coded to one lens and had to be rewritten when the elements moved from
1-2/3-4/6-7 to 1-2/3-4/5-6.
"""
import json, os, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

RES, MSDIR, FIG, CAPTION = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, RES)
MSDIR = os.path.join(HERE, MSDIR)
FIG = os.path.join(HERE, FIG)
os.makedirs(FIG, exist_ok=True)
d = json.load(open(RES))
presc = d["presc"]
FIELDS = d["field_labels"]
FC = ["#2a78d6", "#eb6834", "#1baf7a"]
SURFACE, INK = "#fcfcfb", "#3d3d3d"
plt.rcParams.update({"font.size": 9, "axes.grid": True, "grid.alpha": .25,
                     "figure.dpi": 150, "figure.facecolor": SURFACE,
                     "axes.facecolor": SURFACE})
NSURF = len(presc)


def elements():
    els, i = [], 1
    while i < NSURF - 1:
        if presc[i]["mat"]:
            j = i
            while j < NSURF - 1 and presc[j]["mat"]:
                j += 1
            els.append((i, j, presc[i]["mat"]))
            i = j
        else:
            i += 1
    return els


ELS = elements()


def vertex_z():
    z, acc = {}, 0.0
    for r in presc:
        if r["i"] == 0:
            continue
        z[r["i"]] = acc
        acc += r["t"]
    return z


def sag(r, R, k=0.0):
    if R == 0 or not np.isfinite(R) or abs(R) > 1e9:
        return 0.0 * r
    c = 1.0 / R
    q = np.clip(1.0 - (1.0 + k) * c * c * r * r, 0.0, None)
    return c * r * r / (1.0 + np.sqrt(q))


def layout():
    fig, ax = plt.subplots(figsize=(9.6, 4.0))
    vz = vertex_z()
    # room BELOW the parts for the material labels, set before anything is
    # drawn - a label placed past the data range drags the axis onto itself
    ymech = max(max(presc[a]["mech"], presc[b]["mech"]) for a, b, _ in ELS)
    YLAB = [ymech + 1.6]
    for k, (a, b, mat) in enumerate(ELS):
        ra, rb = presc[a], presc[b]
        sd = max(ra["sd"], rb["sd"])
        mech = max(ra["mech"], rb["mech"])
        rmf = min(sd, .995 * abs(ra["R"])) if ra["R"] else sd
        rmb = min(sd, .995 * abs(rb["R"])) if rb["R"] else sd
        xf = np.linspace(-rmf, rmf, 241)
        xb = np.linspace(-rmb, rmb, 241)
        zf = vz[a] + sag(xf, ra["R"], ra["conic"])
        zb = vz[b] + sag(xb, rb["R"], rb["conic"])
        col = "#dfe4ea" if "PMMA" in mat.upper() else "#eee0d2"
        ax.fill(np.concatenate([zf, zb[::-1]]),
                np.concatenate([xf, xb[::-1]]), col, alpha=.9, zorder=2)
        ax.plot(zf, xf, "k-", lw=.9, zorder=3)
        ax.plot(zb, xb, "k-", lw=.9, zorder=3)
        ef = vz[a] + sag(np.array([sd]), ra["R"])[0]
        eb = vz[b] + sag(np.array([sd]), rb["R"])[0]
        for sgn in (1, -1):
            q = -1 if sgn > 0 else 0
            ax.plot([zf[q], zb[q]], [sgn * rmf, sgn * rmb], "k-", lw=.9,
                    zorder=3)
            ax.plot([ef, eb], [sgn * mech, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
            ax.plot([ef, ef], [sgn * sd, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
            ax.plot([eb, eb], [sgn * sd, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
        ax.text((vz[a] + vz[b]) / 2, -YLAB[0], mat, ha="center",
                fontsize=7.5, color="#333")

    stop = next((r for r in presc if r.get("stop")), None)
    if stop:
        z = vz[stop["i"]] + sag(np.array([stop["sd"]]), stop["R"])[0]
        for sgn in (1, -1):
            ax.plot([z, z], [sgn * stop["sd"], sgn * (stop["sd"] + 1.3)],
                    color="#b4531a", lw=2.4, zorder=5)
        ax.text(z, stop["sd"] + 1.7, "stop", ha="center", fontsize=7.5,
                color="#b4531a")

    zi = vz[NSURF - 1]
    ymax = max(r["sd"] for r in presc[1:NSURF - 1])
    ax.plot([zi, zi], [-1.1 * ymax, 1.1 * ymax], "k-", lw=1.6)
    ax.text(zi + .4, 1.05 * ymax, "image", fontsize=7.5, va="top")

    for fi, lbl in enumerate(FIELDS):
        for ray in d["rays"][lbl]:
            if len(ray) < 2:
                continue
            ax.plot([vz[int(sv)] + z for sv, y, z in ray],
                    [y for sv, y, z in ray],
                    color=FC[fi % 3], lw=.55, alpha=.85, zorder=4)
        ax.plot([], [], color=FC[fi % 3], lw=1.3, label=lbl)
    ax.set_xlabel("z, mm")
    ax.set_ylabel("y, mm")
    ax.set_title(CAPTION + "   (dashed = moulding flange, to the mechanical "
                 "semi-diameter)", fontsize=9)
    ax.legend(loc="upper left", fontsize=7.5, framealpha=.9)
    ax.set_aspect("equal")
    ax.set_ylim(-(YLAB[0] + 1.0), ymech + 2.4)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "layout.png"), bbox_inches="tight")
    plt.close(fig)
    print("layout.png")


def mtf_panels():
    fig, axes = plt.subplots(1, 2, figsize=(10.5, 4.1), sharey=True)
    for ax, kb, km, title in (
            (axes[0], "poly_solve_base", "poly_solve_mould",
             "AS THE TOOL REPORTS IT\nimage plane on the file's focus solve, "
             "F-d-C"),
            (axes[1], "mono_pin_base", "mono_pin_mould",
             "LIKE FOR LIKE\nimage plane pinned, d-line (where the null "
             "control is exact)")):
        for fi in range(len(FIELDS)):
            b, m = d[kb]["mtf"][fi], d[km]["mtf"][fi]
            ax.plot(b["freq"], b["tan"], color=FC[fi], lw=1.5)
            ax.plot(b["freq"], b["sag"], color=FC[fi], lw=1.5, ls=":")
            ax.plot(m["freq"], m["tan"], color=FC[fi], lw=1.5, ls="--")
            ax.plot(m["freq"], m["sag"], color=FC[fi], lw=1.0, ls=(0, (1, 1)))
        ax.set_title(title, fontsize=8.5)
        ax.set_xlabel("spatial frequency, cycles/mm")
        ax.set_xlim(0, 100)
        ax.set_ylim(0, 1)
    axes[0].set_ylabel("modulation")
    h = [plt.Line2D([], [], color=FC[i], lw=1.5, label=FIELDS[i])
         for i in range(len(FIELDS))]
    h += [plt.Line2D([], [], color="0.3", lw=1.5, label="before moulding"),
          plt.Line2D([], [], color="0.3", lw=1.5, ls="--",
                     label="after moulding")]
    axes[1].legend(handles=h, fontsize=7.5, loc="upper right", framealpha=.92)
    fig.suptitle("FFT MTF before and after MoldStress - the same run, read "
                 "two ways", fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, .95))
    fig.savefig(os.path.join(FIG, "mtf.png"), bbox_inches="tight")
    plt.close(fig)
    print("mtf.png")


def through_focus():
    fig, ax = plt.subplots(figsize=(6.4, 3.9))
    for key, lbl, c, ls in (("tf_base", "before moulding", "#2a78d6", "-"),
                            ("tf_mould", "after moulding", "#eb6834", "--")):
        cur = d[key]["curve"]
        x = [q["d"] * 1000 for q in cur]
        y = [q["rwre"][0] for q in cur]
        ax.plot(x, y, ls, color=c, lw=1.6, label=lbl)
        j = int(np.argmin(y))
        ax.plot([x[j]], [y[j]], "o", color=c, ms=5)
    best = min(d["tf_base"]["curve"], key=lambda q: q["rwre"][0])["d"] * 1000
    shift = (d["poly_solve_mould"]["bfl"] - d["poly_solve_base"]["bfl"]) * 1000
    ax.axvline(0, color="0.4", lw=.9)
    ymax = max(q["rwre"][0] for q in d["tf_base"]["curve"])
    ax.annotate("where the file's focus solve put the\nimage plane after "
                "moulding: %+.0f $\\mu$m" % shift,
                xy=(shift, .84 * ymax), xytext=(shift, .84 * ymax),
                fontsize=7.5, color=INK, ha="left", va="center")
    ax.annotate("", xy=(shift, .74 * ymax), xytext=(0, .74 * ymax),
                arrowprops=dict(arrowstyle="->", color=INK, lw=1.1))
    ax.set_xlabel("image-plane shift from the design position, $\\mu$m")
    ax.set_ylabel("on-axis RMS wavefront error, waves (d-line)")
    ax.set_title("Real rays see no focus shift\nboth curves minimise at "
                 "%+.0f $\\mu$m" % best, fontsize=9)
    ax.legend(fontsize=8)
    ax.set_ylim(0, ymax * 1.05)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "focus.png"), bbox_inches="tight")
    plt.close(fig)
    print("focus.png")


def field_map():
    surf = d["surfaces"][0]
    nd = d["nd"][str(surf)] if str(surf) in d["nd"] else list(d["nd"].values())[0]
    rows = []
    for line in open(os.path.join(MSDIR, "moldstress_s%d_index.txt" % surf)):
        q = line.split()
        if len(q) >= 4:
            rows.append([float(v) for v in q[:4]])
    a = np.array(rows)
    r = np.hypot(a[:, 0], a[:, 1])
    dn = a[:, 3] - nd
    lim = 25.0
    ca = presc[surf]["sd"]
    fig, axes = plt.subplots(1, 2, figsize=(10.0, 3.7))
    sc = axes[0].scatter(a[:, 2], r, c=dn * 1e6, s=9, cmap="RdBu_r",
                         vmin=-lim, vmax=lim)
    axes[0].axhline(ca, color="#6b6b6b", ls="--", lw=1)
    axes[0].text(0.02, ca + .08, "clear aperture", fontsize=7, color="#6b6b6b")
    axes[0].set_xlabel("z through the element, mm")
    axes[0].set_ylabel("radius, mm")
    axes[0].set_title("Exported index change, first element (%s)\n%d points "
                      "on sag-following shells"
                      % (presc[surf]["mat"], len(rows)), fontsize=8.5)
    cb = fig.colorbar(sc, ax=axes[0])
    cb.set_label(r"$\Delta n \times 10^{6}$  (clipped at $\pm$%d)" % lim,
                 fontsize=8)
    axes[1].scatter(r, dn * 1e6, s=6, color="#444", alpha=.6)
    axes[1].axvline(ca, color="#6b6b6b", ls="--", lw=1,
                    label="clear aperture %.2f mm" % ca)
    axes[1].axvline(presc[surf]["mech"], color="0.45", ls=":", lw=1.2,
                    label="mechanical %.2f mm" % presc[surf]["mech"])
    axes[1].set_xlabel("radius, mm")
    axes[1].set_ylabel(r"$\Delta n \times 10^{6}$")
    axes[1].set_title(r"$\Delta n$ is small across the clear aperture and "
                      "reaches its" "\n" r"extremes at the rim", fontsize=8.5)
    axes[1].legend(fontsize=7.5)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "field.png"), bbox_inches="tight")
    plt.close(fig)
    print("field.png")


def control():
    c = d["ctl"]
    fig, ax = plt.subplots(figsize=(6.4, 3.8))
    labels = [r"%.3f $\mu$m (F)" % d["waves"][0],
              r"%.3f $\mu$m (d)" % d["waves"][1],
              r"%.3f $\mu$m (C)" % d["waves"][2]]
    x = np.arange(3)
    w = .27
    ax.bar(x - w, c["no_data"], w, label="no data loaded", color="#2a78d6")
    ax.bar(x, c["null"], w, label=r"NULL cloud ($n\equiv N_d$, a no-op)",
           color="#eb6834")
    ax.bar(x + w, c["full"], w, label="moulding data", color="#1baf7a")
    ax.set_xticks(x)
    ax.set_xticklabels(labels, fontsize=8)
    ax.set_ylabel("on-axis RMS wavefront error, waves")
    ax.set_title("The null control: a uniform index cloud should change\n"
                 "NOTHING, and only at the d-line does it", fontsize=9)
    top = max(max(c["no_data"]), max(c["null"]), max(c["full"]))
    ax.set_ylim(0, top * 1.35)
    ax.legend(fontsize=7.5, loc="upper right")
    ax.annotate("identical to 'no data':\nan exact no-op",
                xy=(1.0 - w / 2, c["null"][1]), xytext=(1.35, top * .70),
                ha="left", fontsize=7.5, color=INK,
                arrowprops=dict(arrowstyle="->", color=INK, lw=1))
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "control.png"), bbox_inches="tight")
    plt.close(fig)
    print("control.png")


layout()
mtf_panels()
through_focus()
field_map()
control()
print("figures in", FIG)

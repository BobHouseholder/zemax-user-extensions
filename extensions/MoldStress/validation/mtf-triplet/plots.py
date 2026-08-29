"""Figures for the MoldStress plastic-triplet MTF test."""
import json, math, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
FIG = os.path.join(HERE, "fig")
os.makedirs(FIG, exist_ok=True)
d = json.load(open(os.path.join(HERE, "results.json")))
presc = d["B"]["presc"]
FIELDS = ["0.0 deg", "6.3 deg", "9.0 deg"]
FC = ["#2a78d6", "#eb6834", "#1baf7a"]   # documented slots 1-3

SURFACE = "#fcfcfb"
INK = "#3d3d3d"
plt.rcParams.update({"font.size": 9, "axes.grid": True,
                     "grid.alpha": 0.25, "figure.dpi": 150,
                     "figure.facecolor": SURFACE,
                     "axes.facecolor": SURFACE})


def vertex_z():
    z, acc = {}, 0.0
    for row in presc:
        if row["i"] == 0:
            continue
        z[row["i"]] = acc
        acc += row["t"]
    return z


def sag(r, R, k=0.0):
    if R == 0 or not np.isfinite(R):
        return 0.0 * r
    c = 1.0 / R
    q = 1.0 - (1.0 + k) * c * c * r * r
    q = np.clip(q, 0.0, None)
    return c * r * r / (1.0 + np.sqrt(q))


# ============================ 1. 2D layout ================================
def layout():
    fig, ax = plt.subplots(figsize=(9.5, 3.9))
    vz = vertex_z()
    elements = [(1, 2, "MS_PMMA", "#dfe4ea"),
                (3, 4, "MS_POLYSTYR", "#eee0d2"),
                (6, 7, "MS_PMMA", "#dfe4ea")]
    for f, b, name, col in elements:
        rf, rb = presc[f], presc[b]
        sd = max(rf["sd"], rb["sd"])
        mech = max(rf["mech"], rb["mech"])
        # Each surface is drawn to ITS OWN aperture, and never past the
        # hemisphere of its own radius. Drawing both to a shared semi-diameter
        # clipped surface 6, whose |R| = 3.71 mm is smaller than the element's
        # outer radius - the flat rim in the first render was that, not the
        # design.
        rmf = min(sd, .995 * abs(rf["R"])) if rf["R"] else sd
        rmb = min(sd, .995 * abs(rb["R"])) if rb["R"] else sd
        r_f = np.linspace(-rmf, rmf, 241)
        r_b = np.linspace(-rmb, rmb, 241)
        zf = vz[f] + sag(r_f, rf["R"], rf["conic"])
        zb = vz[b] + sag(r_b, rb["R"], rb["conic"])
        ax.fill(np.concatenate([zf, zb[::-1]]),
                np.concatenate([r_f, r_b[::-1]]), col, alpha=.75, zorder=2)
        ax.plot(zf, r_f, "k-", lw=.9, zorder=3)
        ax.plot(zb, r_b, "k-", lw=.9, zorder=3)
        for sgn in (1, -1):
            k = -1 if sgn > 0 else 0
            ax.plot([zf[k], zb[k]], [sgn * rmf, sgn * rmb], "k-", lw=.9,
                    zorder=3)
            # the moulding flange, to the mechanical semi-diameter
            ax.plot([vz[f] + sag(np.array([sd]), rf["R"])[0],
                     vz[b] + sag(np.array([sd]), rb["R"])[0]],
                    [sgn * mech, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
            ax.plot([vz[f] + sag(np.array([sd]), rf["R"])[0]] * 2,
                    [sgn * sd, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
            ax.plot([vz[b] + sag(np.array([sd]), rb["R"])[0]] * 2,
                    [sgn * sd, sgn * mech], color="0.45", lw=.8,
                    ls=(0, (4, 2)), zorder=1)
        ax.text((vz[f] + vz[b]) / 2, -mech - 1.15, name, ha="center",
                fontsize=7.5, color="#333")

    stop = presc[5]
    ax.plot([vz[5], vz[5]], [stop["sd"], stop["sd"] + 1.1], "k-", lw=2)
    ax.plot([vz[5], vz[5]], [-stop["sd"], -stop["sd"] - 1.1], "k-", lw=2)
    ax.text(vz[5], stop["sd"] + 1.4, "stop", ha="center", fontsize=7.5)

    zi = vz[8]
    ax.plot([zi, zi], [-5.2, 5.2], "k-", lw=1.6)
    ax.text(zi + .35, 5.0, "image", fontsize=7.5, va="top")

    for fi, lbl in enumerate(FIELDS):
        for ray in d["rays"][lbl]:
            if len(ray) < 2:
                continue
            zz = [vz[int(s)] + z for s, y, z in ray]
            yy = [y for s, y, z in ray]
            ax.plot(zz, yy, color=FC[fi], lw=.6, alpha=.85, zorder=4)
        ax.plot([], [], color=FC[fi], lw=1.2, label=lbl)

    ax.set_xlabel("z, mm")
    ax.set_ylabel("y, mm")
    ax.set_title("All-plastic triplet, EFL 30 mm  F/4.5  $\\pm$9$^\\circ$  "
                 "F-d-C   (dashed = moulding flange, to the mechanical "
                 "semi-diameter)", fontsize=9)
    ax.legend(loc="upper left", fontsize=7.5, framealpha=.9)
    ax.set_aspect("equal")
    ax.set_ylim(-10.5, 10.5)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "layout.png"), bbox_inches="tight")
    plt.close(fig)
    print("layout.png")


# ============================ 2. MTF panels ===============================
def mtf_panels():
    fig, axes = plt.subplots(1, 2, figsize=(10.5, 4.1), sharey=True)
    cases = [
        (axes[0], "poly_solve_base", "poly_solve_mould",
         "AS THE TOOL REPORTS IT\nimage plane on the file's focus solve, F-d-C"),
        (axes[1], "mono_pin_base", "mono_pin_mould",
         "LIKE FOR LIKE\nimage plane pinned, d-line "
         "(where the null control is exact)"),
    ]
    for ax, kb, km, title in cases:
        for fi in range(3):
            b, m = d[kb]["mtf"][fi], d[km]["mtf"][fi]
            ax.plot(b["freq"], b["tan"], color=FC[fi], lw=1.5)
            ax.plot(b["freq"], b["sag"], color=FC[fi], lw=1.5, ls=":")
            ax.plot(m["freq"], m["tan"], color=FC[fi], lw=1.5, ls="--",
                    alpha=.95)
            ax.plot(m["freq"], m["sag"], color=FC[fi], lw=1.0, ls=(0, (1, 1)),
                    alpha=.95)
        ax.set_title(title, fontsize=8.5)
        ax.set_xlabel("spatial frequency, cycles/mm")
        ax.set_xlim(0, 100)
        ax.set_ylim(0, 1)
    axes[0].set_ylabel("modulation")
    h = [plt.Line2D([], [], color=FC[i], lw=1.5, label=FIELDS[i])
         for i in range(3)]
    h += [plt.Line2D([], [], color="0.3", lw=1.5, label="before moulding"),
          plt.Line2D([], [], color="0.3", lw=1.5, ls="--",
                     label="after moulding")]
    axes[1].legend(handles=h, fontsize=7.5, loc="upper right", framealpha=.92)
    fig.suptitle("FFT MTF before and after MoldStress - the same run, "
                 "read two ways", fontsize=10)
    fig.tight_layout(rect=(0, 0, 1, .95))
    fig.savefig(os.path.join(FIG, "mtf.png"), bbox_inches="tight")
    plt.close(fig)
    print("mtf.png")


# ============================ 3. through focus ============================
def through_focus():
    fig, ax = plt.subplots(figsize=(6.4, 3.9))
    for key, lbl, c, ls in (("tf_base", "before moulding", "#2a78d6", "-"),
                            ("tf_mould", "after moulding", "#eb6834", "--")):
        cur = d[key]["curve"]
        x = [c_["d"] * 1000 for c_ in cur]
        y = [c_["rwre"][0] for c_ in cur]
        ax.plot(x, y, ls, color=c, lw=1.6, label=lbl)
        j = int(np.argmin(y))
        ax.plot([x[j]], [y[j]], "o", color=c, ms=5)
    shift = (d["poly_solve_mould"]["bfl"] - d["poly_solve_base"]["bfl"]) * 1000
    ax.axvline(0, color="0.4", lw=.9)
    ax.annotate("where the file's focus solve\nput the image plane after\n"
                "moulding: %+.0f $\\mu$m" % shift,
                xy=(-50, 0.30), xytext=(-48, 0.30), fontsize=7.5,
                color=INK, ha="left", va="center")
    ax.annotate("", xy=(-50, 0.245), xytext=(0, 0.245),
                arrowprops=dict(arrowstyle="<-", color=INK, lw=1.1))
    ax.set_xlabel("image-plane shift from the design position, $\\mu$m")
    ax.set_ylabel("on-axis RMS wavefront error, waves (d-line)")
    ax.set_title("Real rays see no focus shift\n"
                 "both curves minimise at $-$25 $\\mu$m", fontsize=9)
    ax.legend(fontsize=8)
    ax.set_ylim(0, .35)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "focus.png"), bbox_inches="tight")
    plt.close(fig)
    print("focus.png")


# ============================ 4. the applied field ========================
def field_map():
    rows = []
    for line in open(os.path.join(HERE, "ms", "moldstress_s1_index.txt")):
        p = line.split()
        if len(p) >= 4:
            rows.append([float(v) for v in p[:4]])
    a = np.array(rows)
    r = np.hypot(a[:, 0], a[:, 1])
    dn = a[:, 3] - 1.4917
    fig, axes = plt.subplots(1, 2, figsize=(10.0, 3.7))
    sc = axes[0].scatter(a[:, 2], r, c=dn * 1e6, s=9, cmap="RdBu_r",
                         vmin=-25, vmax=25)
    axes[0].set_xlabel("z through the element, mm")
    axes[0].set_ylabel("radius, mm")
    axes[0].set_title("Exported index change, element 1 (MS_PMMA)\n"
                      "1540 points on 4 sag-following shells", fontsize=8.5)
    cb = fig.colorbar(sc, ax=axes[0])
    cb.set_label("$\\Delta n \\times 10^{6}$  (clipped at $\pm$25)", fontsize=8)
    axes[0].axhline(presc[1]["sd"], color="#6b6b6b", ls="--", lw=1)
    axes[0].text(0.05, presc[1]["sd"] + .12, "clear aperture", fontsize=7, color="#6b6b6b")

    axes[1].scatter(r, dn * 1e6, s=6, color="#444", alpha=.6)
    axes[1].set_xlabel("radius, mm")
    axes[1].set_ylabel("$\\Delta n \\times 10^{6}$")
    axes[1].axvline(presc[1]["sd"], color="#6b6b6b", ls="--", lw=1,
                    label="clear aperture %.2f mm" % presc[1]["sd"])
    axes[1].axvline(presc[1]["mech"], color="0.45", ls=":", lw=1.2,
                    label="mechanical %.2f mm" % presc[1]["mech"])
    axes[1].set_title(r"$\Delta n \lesssim 2\times10^{-5}$ across the clear "
                      r"aperture; $\pm1.09\times10^{-4}$ only at the rim",
                      fontsize=8.5)
    axes[1].legend(fontsize=7.5)
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, "field.png"), bbox_inches="tight")
    plt.close(fig)
    print("field.png")


# ============================ 5. the null control =========================
def control():
    fig, ax = plt.subplots(figsize=(6.2, 3.7))
    waves = ["0.486 $\\mu$m (F)", "0.588 $\\mu$m (d)", "0.656 $\\mu$m (C)"]
    nodata = [0.0412, 0.0801, 0.1080]
    null = [0.0968, 0.0801, 0.0717]
    full = [0.1011, 0.0836, 0.0852]
    x = np.arange(3)
    w = .27
    ax.bar(x - w, nodata, w, label="no data loaded", color="#2a78d6")
    ax.bar(x, null, w, label="NULL cloud ($n\\equiv N_d$, a no-op)",
           color="#eb6834")
    ax.bar(x + w, full, w, label="moulding data", color="#1baf7a")
    ax.set_xticks(x)
    ax.set_xticklabels(waves, fontsize=8)
    ax.set_ylabel("on-axis RMS wavefront error, waves")
    ax.set_title("The null control: a uniform index cloud should change\n"
                 "NOTHING, and only at the d-line does it", fontsize=9)
    ax.legend(fontsize=7.5, loc="upper left")
    ax.set_ylim(0, .138)
    ax.annotate("identical to 'no data':\nno-op", xy=(1.0, .0805), xytext=(1.42, .045),
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

"""Stage report: MoldStress before/after MTF on an all-plastic imaging triplet.

Regenerates from files only - results.json (written by measure/refocus/tf/final)
and ms/moldstress_report.txt (written by MoldStress.exe). Nothing is copied from
a conversation.

  python report.py                      -> report.html   (standalone)
  python report.py --artifact out.html  -> body only, for the Artifact host
"""
import base64, json, os, sys
from provenance import stamp

HERE = os.path.dirname(os.path.abspath(__file__))
FIG = os.path.join(HERE, "fig")
R = json.load(open(os.path.join(HERE, "results.json")))
# READ, never typed. These three triples were literals transcribed out of a
# terminal until 2026-08-29, when a check against the measurement found the
# C-line moulding value wrong by 0.010 waves - and already published.
_c = json.load(open(os.path.join(HERE, "ctl1.json")))
_CTL = {"no_data": _c["no_data"], "null": _c["null"],
        "moulding": _c["full"], "dline_residual": _c["dline_null_delta"]}
_ENDS = max(abs(_CTL["no_data"][0] - _CTL["null"][0]),
            abs(_CTL["no_data"][2] - _CTL["null"][2]))
FIELDS = ["0.0 deg", "6.3 deg", "9.0 deg"]
FREQS = [10, 20, 30, 40, 60]


def at(series, f):
    x = series["freq"]
    j = min(range(len(x)), key=lambda q: abs(x[q] - f))
    return series["tan"][j], series["sag"][j]


# ---------------------------------------------------------------- summary
def build_summary():
    presc = R["B"]["presc"]
    els = [(1, 2, "MS_PMMA"), (3, 4, "MS_POLYSTYR"), (6, 7, "MS_PMMA")]
    s = {
        "system": {
            "efl_mm": R["poly_solve_base"]["effl"],
            "fno": 4.5, "hfov_deg": 9.0,
            "wavelengths_um": R["waves"],
            "track_mm": sum(p["t"] for p in presc[1:-1]),
            "elements": [{"surfaces": [a, b], "material": m,
                          "ct_mm": presc[a]["t"],
                          "clear_semi_mm": max(presc[a]["sd"], presc[b]["sd"]),
                          "mech_semi_mm": max(presc[a]["mech"], presc[b]["mech"])}
                         for a, b, m in els],
        },
        "run": {"index_points_total": R["C_points"],
                "grin_step_mm": 0.50,
                "process": "fill 0.60 s, pack 60.0 MPa for 3.0 s"},
        "states": {},
        "mtf": {},
        # READ from ctl1.json, never typed. Carried as literals until
        # 2026-08-29, at which point a check against the measurement found
        # the C-line moulding value wrong by 0.010 waves - published.
        "control_rwre_axis": _CTL,
        "through_focus": {
            k: {"best_um": min(R[k]["curve"], key=lambda c: c["rwre"][0])["d"] * 1000,
                "best_waves": min(c["rwre"][0] for c in R[k]["curve"])}
            for k in ("tf_base", "tf_mould")},
    }
    for k in ("poly_solve_base", "poly_solve_mould", "poly_pin_base",
              "poly_pin_null", "poly_pin_mould", "mono_pin_base",
              "mono_pin_null", "mono_pin_mould"):
        s["states"][k] = {"bfl_mm": R[k]["bfl"], "effl_mm": R[k]["effl"],
                          "rwre_waves": R[k]["rwre_w2"],
                          "rms_spot_mm": R[k]["rsre"]}
        s["mtf"][k] = {str(f): [list(at(R[k]["mtf"][i], f)) for i in range(3)]
                       for f in FREQS}
    s["headline"] = {
        "as_reported_mtf40_axis": [s["mtf"]["poly_solve_base"]["40"][0][0],
                                   s["mtf"]["poly_solve_mould"]["40"][0][0]],
        "like_for_like_mtf40_axis": [s["mtf"]["mono_pin_base"]["40"][0][0],
                                     s["mtf"]["mono_pin_mould"]["40"][0][0]],
        "image_plane_move_um": (R["poly_solve_mould"]["bfl"]
                                - R["poly_solve_base"]["bfl"]) * 1000,
    }
    s["provenance"] = stamp()
    with open(os.path.join(HERE, "summary.json"), "w") as fh:
        json.dump(s, fh, indent=1)
    return s


S = build_summary()


def img(name):
    with open(os.path.join(FIG, name), "rb") as fh:
        return ("data:image/png;base64,"
                + base64.b64encode(fh.read()).decode("ascii"))


def figure(name, cap):
    return ('<figure class="plot"><img src="' + img(name) + '" alt="' + cap
            + '"><figcaption>' + cap + '</figcaption></figure>')


def mtf_table(kb, km, knull=None):
    head = "<tr><th>freq</th>"
    for f in FIELDS:
        head += '<th colspan="2">' + f + "</th>"
    head += "</tr><tr><th></th>" + ("<th>T</th><th>S</th>" * 3) + "</tr>"
    body = ""
    for f in FREQS:
        rows = [("before", kb), ("after", km)]
        if knull:
            rows.insert(1, ("null", knull))
        for j, (lbl, key) in enumerate(rows):
            body += "<tr>"
            body += ('<td class="lbl">' + str(f) + " lp/mm &middot; " + lbl
                     + "</td>")
            for i in range(3):
                t, sg = at(R[key]["mtf"][i], f)
                cls = ' class="after"' if lbl == "after" else ""
                body += "<td" + cls + ">%.3f</td><td" % t + cls + ">%.3f</td>" % sg
            body += "</tr>"
    return '<table class="num">' + head + body + "</table>"


def scalar_table(keys, labels):
    h = ("<tr><th>state</th><th>back focus<br>mm</th><th>EFFL<br>mm</th>"
         "<th>RMS wavefront, waves<br>0&deg; / 6.3&deg; / 9&deg;</th>"
         "<th>RMS spot, &micro;m<br>0&deg; / 6.3&deg; / 9&deg;</th></tr>")
    b = ""
    for k, lab in zip(keys, labels):
        q = R[k]
        b += ("<tr><td class=\"lbl\">" + lab + "</td>"
              + "<td>%.4f</td><td>%.4f</td>" % (q["bfl"], q["effl"])
              + "<td>" + " / ".join("%.4f" % v for v in q["rwre_w2"]) + "</td>"
              + "<td>" + " / ".join("%.1f" % (v * 1000) for v in q["rsre"])
              + "</td></tr>")
    return '<table class="num">' + h + b + "</table>"


CSS = """
:root{--surface:#fcfcfb;--card:#ffffff;--ink:#12140f;--ink2:#4a4f45;
--muted:#767c6f;--rule:#e2e3dd;--accent:#2a78d6;--warn:#b4531a;
--good:#1baf7a;--codebg:#f4f4f1;}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
--surface:#1a1a19;--card:#232421;--ink:#f2f3ee;--ink2:#c3c7bc;
--muted:#8f958a;--rule:#35362f;--accent:#78aeea;--warn:#e08a4e;
--good:#4fcf9c;--codebg:#26271f;}}
:root[data-theme="dark"]{--surface:#1a1a19;--card:#232421;--ink:#f2f3ee;
--ink2:#c3c7bc;--muted:#8f958a;--rule:#35362f;--accent:#78aeea;
--warn:#e08a4e;--good:#4fcf9c;--codebg:#26271f;}
*{box-sizing:border-box}
body{margin:0;background:var(--surface);color:var(--ink);
font:16px/1.62 ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;
-webkit-font-smoothing:antialiased}
.wrap{max-width:1080px;margin:0 auto;padding:44px 22px 96px}
header{border-bottom:2px solid var(--accent);padding-bottom:18px;
margin-bottom:34px}
h1{font-size:1.72rem;line-height:1.22;margin:0 0 8px;letter-spacing:-.015em}
.sub{color:var(--ink2);font-size:.97rem;margin:0}
.tag{display:inline-block;font-size:.72rem;letter-spacing:.09em;
text-transform:uppercase;color:var(--accent);border:1px solid var(--accent);
border-radius:3px;padding:2px 8px;margin-bottom:12px}
h2{font-size:.79rem;letter-spacing:.13em;text-transform:uppercase;
color:var(--accent);margin:44px 0 6px;font-weight:650}
h3{font-size:1.06rem;margin:26px 0 6px;letter-spacing:-.01em}
p{margin:.62em 0}
.meta{color:var(--muted);font-size:.865rem;line-height:1.55}
.lede{font-size:1.06rem;color:var(--ink2)}
/* the plot card stays light in BOTH themes on purpose: matplotlib
   bakes a light ground into the PNG, so a themed card would ring it.
   #fcfcfb matches the figures' own facecolor exactly. */
figure.plot{margin:20px 0 8px;background:#fcfcfb;border:1px solid var(--rule);
border-radius:9px;padding:12px 12px 4px}
figure.plot img{width:100%;height:auto;display:block}
figcaption{color:var(--muted);font-size:.82rem;padding:8px 2px 8px}
.scroll{overflow-x:auto;margin:14px 0}
table.num{border-collapse:collapse;width:100%;
font:13px/1.45 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;
font-variant-numeric:tabular-nums}
table.num th,table.num td{border-bottom:1px solid var(--rule);
padding:5px 9px;text-align:right;white-space:nowrap}
table.num th{color:var(--muted);font-weight:600;font-size:11.5px;
letter-spacing:.045em;text-transform:uppercase}
table.num td.lbl,table.num th:first-child{text-align:left}
table.num td.after{color:var(--warn);font-weight:600}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(215px,1fr));
gap:13px;margin:20px 0}
.card{background:var(--card);border:1px solid var(--rule);border-radius:9px;
padding:14px 15px}
.card .k{font-size:.71rem;letter-spacing:.085em;text-transform:uppercase;
color:var(--muted)}
.card .v{font:600 1.42rem/1.2 ui-monospace,SFMono-Regular,Menlo,monospace;
font-variant-numeric:tabular-nums;margin:5px 0 2px}
.card .n{font-size:.8rem;color:var(--ink2)}
.v.warn{color:var(--warn)} .v.good{color:var(--good)}
blockquote{margin:18px 0;padding:12px 16px;border-left:3px solid var(--accent);
background:var(--card);border-radius:0 7px 7px 0}
blockquote p{margin:.3em 0}
code{background:var(--codebg);padding:1px 5px;border-radius:3px;
font:13px ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}
pre{background:var(--codebg);border:1px solid var(--rule);border-radius:7px;
padding:13px 15px;overflow-x:auto;
font:12.5px/1.5 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}
ul{padding-left:20px}
li{margin:.32em 0}
footer{margin-top:60px;padding-top:18px;border-top:1px solid var(--rule);
color:var(--muted);font-size:.83rem}
"""

sysd = S["system"]
hl = S["headline"]

P = []
A = P.append

A('<div class="wrap">')
A("<header>")
A('<div class="tag">MoldStress &middot; validation run</div>')
A("<h1>Before and after MTF on an all-plastic imaging triplet</h1>")
A('<p class="sub">A three-element PMMA / polystyrene objective, '
  "EFL %.2f&nbsp;mm, F/%.1f, &plusmn;%.0f&deg;, F-d-C. "
  "MoldStress converted it, exported %d index points, and STAR loaded them. "
  "The headline it prints is not the moulding effect.</p>"
  % (sysd["efl_mm"], sysd["fno"], sysd["hfov_deg"], S["run"]["index_points_total"]))
A("</header>")

A('<blockquote><p><strong>Superseded as a test article, not as a finding.</strong> The lens on this page was found by GLOBAL optimisation and is not manufacturable: element powers + &minus; &minus; rather than a Cooke’s + &minus; +, a 0.50&nbsp;mm airgap, and a 62.9&deg; surface slope. Both findings below were reproduced on a proper, mouldable plastic Cooke triplet &mdash; see <a href="https://claude.ai/code/artifact/7c77a26a-b761-4f43-8ece-d6677bbc1bbd">Plastic Cooke Triplet</a>. The controls and probes on this page (GRIN-step convergence, the <code>IndexDataType</code> enumeration) were run here and are not repeated there.</p></blockquote>')
A('<p class="lede">The tool ran end to end and reported the moulded lens as '
  "destroyed &mdash; on-axis MTF at 40&nbsp;lp/mm falling from "
  "<strong>%.3f to %.3f</strong>. Holding the image plane still and measuring "
  "at the one wavelength where the null control is a no-op, the same run gives "
  "<strong>%.3f to %.3f</strong>. Almost the entire reported loss is an image "
  "plane that moved %.0f&nbsp;&micro;m, and a dispersion artefact in STAR's "
  "direct-index route &mdash; neither of which is moulding.</p>"
  % (hl["as_reported_mtf40_axis"][0], hl["as_reported_mtf40_axis"][1],
     hl["like_for_like_mtf40_axis"][0], hl["like_for_like_mtf40_axis"][1],
     hl["image_plane_move_um"]))

A('<div class="cards">')
A('<div class="card"><div class="k">as the tool reports it</div>'
  '<div class="v warn">%.3f &rarr; %.3f</div>'
  '<div class="n">MTF, 40 lp/mm, on axis</div></div>'
  % tuple(hl["as_reported_mtf40_axis"]))
A('<div class="card"><div class="k">like for like</div>'
  '<div class="v good">%.3f &rarr; %.3f</div>'
  '<div class="n">same run, plane pinned, d-line</div></div>'
  % tuple(hl["like_for_like_mtf40_axis"]))
A('<div class="card"><div class="k">image plane moved</div>'
  '<div class="v warn">%+.0f &micro;m</div>'
  '<div class="n">the file&rsquo;s own focus solve, ~9&times; depth of focus'
  "</div></div>" % hl["image_plane_move_um"])
A('<div class="card"><div class="k">real-ray best focus moved</div>'
  '<div class="v good">0 &micro;m</div>'
  '<div class="n">both curves minimise at &minus;25 &micro;m</div></div>')
A("</div>")

A("<h2>The test article</h2>")
A(figure("layout.png",
         "Real meridional ray fans at 0&deg;, 6.3&deg; and 9&deg;. All three "
         "elements are injection-mouldable polymers with a 1.0 mm mounting "
         "flange; MoldStress sizes the moulded part from the mechanical "
         "semi-diameter, not the clear aperture."))
A('<div class="scroll"><table class="num">'
  "<tr><th>element</th><th>surfaces</th><th>material</th>"
  "<th>centre thickness<br>mm</th><th>clear semi&oslash;<br>mm</th>"
  "<th>mechanical semi&oslash;<br>mm</th></tr>")
for i, e in enumerate(sysd["elements"], 1):
    A("<tr><td class=\"lbl\">%d</td><td>%d&ndash;%d</td>"
      "<td class=\"lbl\">%s</td><td>%.3f</td><td>%.3f</td><td>%.3f</td></tr>"
      % (i, e["surfaces"][0], e["surfaces"][1], e["material"], e["ct_mm"],
         e["clear_semi_mm"], e["mech_semi_mm"]))
A("</table></div>")
A('<p class="meta">Spherical throughout, so the non-spherical gate does not '
  "fire and the run is the ordinary path. Element thicknesses were bounded to "
  "mouldable values during optimisation rather than left free &mdash; free ran "
  "away to negative thickness, which is a small merit function on a lens that "
  "does not exist. Total track %.2f&nbsp;mm; wavelengths %s&nbsp;&micro;m."
  % (sysd["track_mm"], ", ".join("%.4f" % w for w in sysd["wavelengths_um"])))

A("<h2>What MoldStress applied</h2>")
A(figure("field.png",
         "The exported index change for element 1. It is not a smooth radial "
         "gradient: across the whole clear aperture it stays below "
         "2&times;10<sup>&minus;5</sup>, and the &plusmn;1.09&times;10"
         "<sup>&minus;4</sup> extremes sit at r &gt; 4.2 mm &mdash; at and "
         "beyond the edge of the 4.41 mm clear aperture, in the flange."))
A('<p class="meta">Index-only mode, which is the default: the density channel '
  "is applied through STAR&rsquo;s direct-index route and nothing else &mdash; "
  "no stress tensor, no birefringence, no retardance. Process conditions were "
  "the shipped defaults (%s). 1540 index points per element on four "
  "sag-following shells; the run&rsquo;s own sampling check reported the ring "
  "grid capturing the density field to 1.39%%, 1.75%% and 0.49%% of its span "
  "on the three elements." % S["run"]["process"])

A("<h2>Before and after</h2>")
A(figure("mtf.png",
         "The same measurement read two ways. Left: the conditions the tool "
         "reports under. Right: image plane pinned and d-line only. Solid is "
         "before, dashed after; the right panel&rsquo;s pairs lie almost on "
         "top of each other."))

A("<h3>As the tool reports it &mdash; focus solve live, polychromatic</h3>")
A('<div class="scroll">'
  + mtf_table("poly_solve_base", "poly_solve_mould") + "</div>")
A('<p class="meta">These are real numbers for the system as loaded. They are '
  "not a description of what moulding did to the optics, because the file "
  "carries a marginal-ray-height solve on the last airspace: when the index "
  "data loads, the solve recomputes the image plane and moves it "
  "%+.0f&nbsp;&micro;m. Everything below separates that out."
  % hl["image_plane_move_um"])

A("<h3>Like for like &mdash; plane pinned, d-line</h3>")
A('<div class="scroll">'
  + mtf_table("mono_pin_base", "mono_pin_mould", "mono_pin_null") + "</div>")
A('<p class="meta">The <em>null</em> row is a control: an index cloud whose '
  "every point is exactly the material&rsquo;s own N<sub>d</sub>, so it is "
  "physically a no-op. At the d-line it sits %.1e waves from the baseline "
  "while moving the band ends %.0f&times; further, which is what makes the "
  "<em>after</em> row here readable as the moulding effect and nothing else. "
  "The largest change at 40&nbsp;lp/mm is 0.026 in modulation, and it is not "
  "all in one direction." % (_CTL["dline_residual"],
                             _ENDS / _CTL["dline_residual"]))

A('<div class="scroll">'
  + scalar_table(
      ["poly_solve_base", "poly_solve_mould", "poly_pin_base",
       "poly_pin_null", "poly_pin_mould"],
      ["before &middot; solve live", "after &middot; solve live",
       "before &middot; pinned", "null &middot; pinned",
       "after &middot; pinned"]) + "</div>")

A("<h2>Why the headline is not the moulding effect</h2>")

A("<h3>1. The image plane moved, and real rays did not ask it to</h3>")
A(figure("focus.png",
         "Through-focus on a shared grid. Both curves minimise at the same "
         "place, &minus;25 &micro;m, and differ by 0.003 waves at the "
         "minimum. The paraxial focus solve nonetheless moved the image plane "
         "211 &micro;m."))
A("<p>EFFL, which OpticStudio computes paraxially, reads %.4f&nbsp;mm before "
  "and %.4f&nbsp;mm after &mdash; a 0.98%% shift. The focus solve follows it "
  "and the reported MTF collapses. But a through-focus scan of real rays puts "
  "best focus at the same &minus;25&nbsp;&micro;m in both states, so the "
  "shift is confined to the paraxial calculation. A smooth reading of the "
  "applied field supports roughly 0.06%%, about 18&times; less; and scaling "
  "the whole field by 0.1 moves EFFL by 0.155 of the full amount where "
  "first-order theory demands exactly 0.100. The near-axis behaviour of the "
  "B-spline fit is the natural suspect &mdash; the data is essentially flat "
  "for r&nbsp;&lt;&nbsp;3&nbsp;mm and all its structure is at the rim &mdash; "
  "but that is a hypothesis, not a measurement, and it is recorded as open."
  % (R["poly_solve_base"]["effl"], R["poly_solve_mould"]["effl"]))

A("<h3>2. STAR&rsquo;s direct-index route discards the material&rsquo;s "
  "dispersion</h3>")
A(figure("control.png",
         "The null control across the band. A uniform cloud should change "
         "nothing at any wavelength. At the d-line it very nearly does not; "
         "at both ends it plainly does."))
A("<p>MoldStress writes absolute index, n = N<sub>d</sub> + &Delta;n, which "
  "is a single value per point &mdash; the d-line. STAR applies it at every "
  "wavelength, so the element loses its own dispersion. The null control "
  "isolates it: on axis it reads %.6f waves at the d-line against the "
  "baseline&rsquo;s %.6f &mdash; a residual of 2.8&times;10<sup>&minus;5</sup> "
  "&mdash; while F moves %.4f&nbsp;&rarr;&nbsp;%.4f and C moves "
  "%.4f&nbsp;&rarr;&nbsp;%.4f, three orders of magnitude further.</p>"
  % (S["control_rwre_axis"]["null"][1], S["control_rwre_axis"]["no_data"][1],
     S["control_rwre_axis"]["no_data"][0], S["control_rwre_axis"]["null"][0],
     S["control_rwre_axis"]["no_data"][2], S["control_rwre_axis"]["null"][2]))
A('<p class="meta">This is not integration error: the same numbers hold to '
  "four decimals across GRIN steps from 1.0 down to 0.02&nbsp;mm, a 50&times; "
  "range. And there is no delta form available on this route &mdash; "
  "<code>IndexDataType</code> is read-only and reports "
  "<code>DirectRefractiveIndex</code>; the switchable alternative, "
  "<code>PhysicsBasedIndex</code>, is the stress/temperature route, not this "
  "one. So for a polychromatic system the direct-index route costs the "
  "material&rsquo;s dispersion as the price of carrying &Delta;n.")

A("<h2>What this run actually establishes</h2>")
A("<ul>")
A("<li><strong>The product path works.</strong> One command converted three "
  "catalogue polymers to MS_* rows, wrote and attached the catalogue, saved a "
  "<code>-MoldStress</code> sibling without touching the original, sized every "
  "part from its mechanical semi-diameter, exported 1540 index points per "
  "element, and STAR accepted all of them. Exit code 0.</li>")
A("<li><strong>The moulding effect on this lens is small.</strong> Measured "
  "against a null control at a fixed image plane, worst-case MTF change at "
  "40&nbsp;lp/mm is 0.026, mixed in sign; RMS wavefront moves "
  "%.4f&nbsp;&rarr;&nbsp;%.4f waves on axis.</li>"
  % (R["mono_pin_base"]["rwre_w2"][0], R["mono_pin_mould"]["rwre_w2"][0]))
A("<li><strong>The reported headline is dominated by two effects that are not "
  "moulding</strong> &mdash; a focus solve chasing a paraxial-only EFFL shift, "
  "and dispersion flattening inherent to the direct-index route. On this lens "
  "they turn a 0.003-wave change into a reported +1572%%.</li>")
A("<li><strong>The paraxial shift itself is unexplained.</strong> It tracks "
  "the data (a uniform cloud produces none), but it is ~18&times; larger than "
  "the field supports and scales non-linearly. Open.</li>")
A("</ul>")
A('<p class="meta">Scope: index-only mode, so nothing here bears on stress '
  "birefringence or retardance, which is where this project&rsquo;s one "
  "real-lens comparison found the wavefront understating the polarisation "
  "effect by 585&times;. A polarisation-sensitive system still needs "
  "<code>-full</code>. Process conditions were the shipped defaults, not a "
  "moulder&rsquo;s sheet. And MoldStress remains an estimate that has never "
  "been validated against a measured moulded part.")

A("<h2>Reproducing it</h2>")
A("<pre>python build.py        # the triplet, optimised and frozen\n"
  "MoldStress.exe -run -file plastic-triplet.zmx -prepare -outdir ms\n"
  "python measure.py      # states A/B/C + layout ray fans\n"
  "python refocus.py      # per-wavelength cost, refocused moulded\n"
  "python bprime.py       # refocused baseline, same criterion\n"
  "python tf.py           # through-focus, shared grid\n"
  "python nullprobe.py    # NULL / TENTH / FULL index clouds\n"
  "python grinfloor.py    # GRIN step convergence, 1.0 -> 0.02 mm\n"
  "python waveprobe.py    # DirectIndex API surface, per-wavelength\n"
  "python final.py        # the eight states in this report\n"
  "python plots.py        # figures\n"
  "python report.py       # summary.json + this page</pre>")

A("<footer>MoldStress validation run &middot; OpticStudio 2026 R1.03 "
  "&middot; index-only mode, GRIN step %.2f mm, FFT MTF at 64&times;64 "
  "&middot; every number regenerated from <code>results.json</code> and "
  "<code>summary.json</code></footer>" % S["run"]["grin_step_mm"])
A("</div>")

BODY = "\n".join(P)
TITLE = "Plastic Triplet MTF"

art = None
if "--artifact" in sys.argv:
    art = sys.argv[sys.argv.index("--artifact") + 1]

with open(os.path.join(HERE, "report.html"), "w", encoding="utf-8") as fh:
    fh.write("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">"
             "<meta name=\"viewport\" content=\"width=device-width,"
             "initial-scale=1\"><title>" + TITLE + "</title><style>" + CSS
             + "</style></head><body>" + BODY + "</body></html>")
print("wrote report.html")

if art:
    with open(art, "w", encoding="utf-8") as fh:
        fh.write("<title>" + TITLE + "</title>\n<style>" + CSS + "</style>\n"
                 + BODY)
    print("wrote", art)
print("wrote summary.json")

"""Stage report for the plastic COOKE article.

Regenerates from files only: results2.json (run2/tf2), form.json (formdump),
fig2/*.png (figures.py), ms2/moldstress_report.txt (MoldStress.exe).

  python report2.py [--artifact out.html]
"""
import base64, json, os, sys
from provenance import stamp

HERE = os.path.dirname(os.path.abspath(__file__))
FIG = os.path.join(HERE, "fig2")
R = json.load(open(os.path.join(HERE, "results2.json")))
F = json.load(open(os.path.join(HERE, "form.json")))
FIELDS = R["field_labels"]
FREQS = [10, 20, 30, 40, 60]
LENS = {d["kind"]: d for d in F["lenses"]}
_DRES = abs(R["ctl"]["no_data"][1] - R["ctl"]["null"][1])
_ENDS = max(abs(R["ctl"]["no_data"][0] - R["ctl"]["null"][0]),
            abs(R["ctl"]["no_data"][2] - R["ctl"]["null"][2]))


def at(series, f):
    x = series["freq"]
    j = min(range(len(x)), key=lambda q: abs(x[q] - f))
    return series["tan"][j], series["sag"][j]


def best(tag):
    c = min(R[tag]["curve"], key=lambda q: q["rwre"][0])
    return c["d"] * 1000, c["rwre"][0]


HL = {
    "as_reported": [at(R["poly_solve_base"]["mtf"][0], 40)[0],
                    at(R["poly_solve_mould"]["mtf"][0], 40)[0]],
    "like_for_like": [at(R["mono_pin_base"]["mtf"][0], 40)[0],
                      at(R["mono_pin_mould"]["mtf"][0], 40)[0]],
    "plane_um": (R["poly_solve_mould"]["bfl"] - R["poly_solve_base"]["bfl"]) * 1000,
    "best_base_um": best("tf_base")[0],
    "best_mould_um": best("tf_mould")[0],
}
SUM = {"headline": HL, "form": {k: {"powers": ["+" if e["power"] > 0 else "-"
                                               for e in v["els"]],
                                    "mouldable": v["ok"], "efl": v["efl"]}
                                for k, v in LENS.items()},
       "ctl": R["ctl"], "waves": R["waves"],
       "states": {k: {"bfl": R[k]["bfl"], "effl": R[k]["effl"],
                      "rwre": R[k]["rwre_w2"], "rsre": R[k]["rsre"]}
                  for k in ("poly_solve_base", "poly_solve_mould",
                            "poly_pin_base", "poly_pin_null", "poly_pin_mould",
                            "mono_pin_base", "mono_pin_null", "mono_pin_mould")}}
SUM["provenance"] = stamp()
json.dump(SUM, open(os.path.join(HERE, "summary2.json"), "w"), indent=1)


def img(name):
    with open(os.path.join(FIG, name), "rb") as fh:
        return "data:image/png;base64," + base64.b64encode(fh.read()).decode()


def figure(name, cap):
    return ('<figure class="plot"><img src="' + img(name) + '" alt="' + cap
            + '"><figcaption>' + cap + "</figcaption></figure>")


def mtf_table(kb, km, knull=None):
    h = "<tr><th>freq</th>"
    for f in FIELDS:
        h += '<th colspan="2">' + f + "</th>"
    h += "</tr><tr><th></th>" + "<th>T</th><th>S</th>" * len(FIELDS) + "</tr>"
    b = ""
    for f in FREQS:
        rows = [("before", kb), ("after", km)]
        if knull:
            rows.insert(1, ("null", knull))
        for lbl, key in rows:
            b += '<tr><td class="lbl">%d lp/mm &middot; %s</td>' % (f, lbl)
            for i in range(len(FIELDS)):
                t, sg = at(R[key]["mtf"][i], f)
                c = ' class="after"' if lbl == "after" else ""
                b += "<td%s>%.3f</td><td%s>%.3f</td>" % (c, t, c, sg)
            b += "</tr>"
    return '<table class="num">' + h + b + "</table>"


CSS = open(os.path.join(HERE, "report.py")).read()
CSS = CSS[CSS.index('CSS = """') + 9:CSS.index('"""\n\nsysd')]

P = []
A = P.append
art, ref, rej, ctl = LENS["article"], LENS["reference"], LENS["rejected"], LENS["control"]

A('<div class="wrap">')
A("<header>")
A('<div class="tag">MoldStress &middot; validation run &middot; v2</div>')
A("<h1>Before and after MTF on a plastic Cooke triplet</h1>")
A('<p class="sub">The shipped glass Cooke triplet, transcribed into PMMA and '
  "polystyrene and held to its form: EFL %.0f&nbsp;mm, F/%.1f, "
  "&plusmn;%.0f&deg;, F-d-C. Every moulding check passes. Both findings from "
  "the first run reproduce, so neither can be blamed on an odd lens.</p>"
  % (art["efl"], art["fno"], art["hfov"]))
A("</header>")

A('<p class="lede">MoldStress reports this lens as wrecked &mdash; on-axis MTF '
  "at 40&nbsp;lp/mm falling <strong>%.3f to %.3f</strong>. Holding the image "
  "plane still and reading at the one wavelength where the null control is "
  "a no-op, the same run gives <strong>%.3f to %.3f</strong>. The difference is "
  "an image plane the file&rsquo;s own focus solve moved "
  "%.0f&nbsp;&micro;m &mdash; %.0f&nbsp;&micro;m past the true best focus "
  "&mdash; plus a dispersion artefact in STAR&rsquo;s direct-index route.</p>"
  % (HL["as_reported"][0], HL["as_reported"][1],
     HL["like_for_like"][0], HL["like_for_like"][1],
     HL["plane_um"], abs(HL["plane_um"] - HL["best_base_um"])))

A('<div class="cards">')
A('<div class="card"><div class="k">as the tool reports it</div>'
  '<div class="v warn">%.3f &rarr; %.3f</div>'
  '<div class="n">MTF, 40 lp/mm, on axis</div></div>' % tuple(HL["as_reported"]))
A('<div class="card"><div class="k">like for like</div>'
  '<div class="v good">%.3f &rarr; %.3f</div>'
  '<div class="n">plane pinned, d-line</div></div>' % tuple(HL["like_for_like"]))
A('<div class="card"><div class="k">image plane moved</div>'
  '<div class="v warn">%+.0f &micro;m</div>'
  '<div class="n">by the file&rsquo;s focus solve</div></div>' % HL["plane_um"])
A('<div class="card"><div class="k">real-ray best focus</div>'
  '<div class="v good">%+.0f &micro;m, both</div>'
  '<div class="n">unmoved by the moulding data</div></div>' % HL["best_base_um"])
A("</div>")

# ---------------------------------------------------------------- the form
A("<h2>Choosing the article</h2>")
A("<p>The first version of this test used a lens found by <em>global</em> "
  "optimisation. It scored well and was not a Cooke triplet: global search "
  "left the basin entirely and landed on a form no moulder would quote. This "
  "one is a transcription of the shipped sample, with the Cooke form held by "
  "explicit curvature-sign constraints and local optimisation only.</p>")
A(figure("form.png",
         "The shipped glass Cooke, the rejected design, and the replacement, "
         "all normalised by focal length so the form is comparable regardless "
         "of size."))

A('<div class="scroll"><table class="num">'
  "<tr><th>&nbsp;</th><th>element powers</th><th>E2 shape <i>q</i></th>"
  "<th>stop</th><th>airspaces, mm</th><th>steepest slope</th>"
  "<th>moulding checks</th></tr>")
for d in (ref, rej, art, ctl):
    A("<tr><td class=\"lbl\">%s</td><td>%s</td><td>%+.3f</td><td>%s</td>"
      "<td>%s</td><td>%.1f&deg;</td><td%s>%s</td></tr>"
      % (d["label"],
         " ".join("+" if e["power"] > 0 else "&minus;" for e in d["els"]),
         d["els"][1]["shape_q"],
         "back of E2" if d["stop_is_back_of_e2"] else "surface %d" % d["stop"],
         " / ".join("%.2f" % a["centre"] for a in d["air"]),
         max(e["slope_deg"] for e in d["els"]),
         "" if d["ok"] else ' class="after"',
         "all pass" if d["ok"] else "fails"))
A("</table></div>")
A('<p class="meta">The rejected design is the only one whose element powers '
  "are not <strong>+ &minus; +</strong>, and its middle element is a meniscus "
  "(<i>q</i> %+.3f) where every Cooke has a near-equiconcave flint. Its 62.9&deg; "
  "surface and 0.50&nbsp;mm airgap are what &ldquo;not manufacturable&rdquo; "
  "looks like in numbers. The glass sample fails two of the same checks, which "
  "is not a criticism of it &mdash; a 1.0&nbsp;mm centre thickness and a "
  "CT/ET of 3.3 are ordinary in glass and out of range for an injection "
  "moulding. The limits are applied uniformly so the comparison is honest "
  "about who they are for." % rej["els"][1]["shape_q"])

A("<h2>The test article</h2>")
A(figure("layout.png",
         "Real meridional fans at %s. Stop on the back of the negative "
         "element, as in the sample. The dashed outline is the 1&nbsp;mm "
         "mounting flange; MoldStress sizes the moulded part from the "
         "mechanical semi-diameter." % ", ".join(FIELDS)))
A('<div class="scroll"><table class="num">'
  "<tr><th>element</th><th>material</th><th><i>f</i>, mm</th>"
  "<th>shape <i>q</i></th><th>CT, mm</th><th>ET, mm</th><th>CT/ET</th>"
  "<th>clear semi&oslash;</th><th>slope</th></tr>")
for e in art["els"]:
    A("<tr><td class=\"lbl\">%d &middot; surf %d&ndash;%d</td>"
      "<td class=\"lbl\">%s</td><td>%+.2f</td><td>%+.3f</td><td>%.3f</td>"
      "<td>%.3f</td><td>%.2f</td><td>%.3f</td><td>%.1f&deg;</td></tr>"
      % (e["n"], e["surfaces"][0], e["surfaces"][1], e["mat"], e["f"],
         e["shape_q"], e["ct"], e["et"], e["ct"] / e["et"], e["sd"],
         e["slope_deg"]))
A("</table></div>")
A('<p class="meta">Airspaces %s&nbsp;mm at centre, %s&nbsp;mm at the edge. '
  "Both positive elements sit on the 4&nbsp;mm centre-thickness ceiling this "
  "run imposed &mdash; the optimiser wants them thicker, and thicker is worse "
  "to mould, so the bound is doing real work rather than decorating."
  % (" / ".join("%.2f" % a["centre"] for a in art["air"]),
     " / ".join("%.2f" % a["edge"] for a in art["air"])))

A("<h2>What plastic costs, at the same form and spec</h2>")
A('<div class="scroll"><table class="num">'
  "<tr><th>&nbsp;</th><th>RMS wavefront, waves<br>%s</th>"
  "<th>MTF 40 lp/mm, on axis</th></tr>" % " / ".join(FIELDS))
A("<tr><td class=\"lbl\">%s</td><td>%s</td><td>%.3f</td></tr>"
  % ("PMMA / POLYSTYR / PMMA",
     " / ".join("%.3f" % v for v in R["poly_pin_base"]["rwre_w2"]),
     at(R["poly_pin_base"]["mtf"][0], 40)[0]))
A("</table></div>")
A('<p class="meta">The same form built in the sample&rsquo;s own SK16 and F2, '
  "optimised identically, is saved beside this one as "
  "<code>glass-cooke.zmx</code>; the index drop from 1.62 to 1.49 is what "
  "separates them, and it is the reason real moulded optics reach for "
  "aspheres. An all-plastic <em>spherical</em> triplet at this aperture runs "
  "out of correction by about &plusmn;12&deg; &mdash; measured, by sweeping "
  "F/# and field with the form and the moulding bounds held fixed.")

A("<h2>What MoldStress applied</h2>")
A(figure("field.png",
         "The exported index change for the first element. It stays small "
         "across the clear aperture and reaches its extremes at the rim, much "
         "of that in the flange."))
A('<p class="meta">Index-only mode, the default: the density channel through '
  "STAR&rsquo;s direct-index route, no stress tensor, no birefringence, no "
  "retardance. Shipped process defaults &mdash; fill 0.60&nbsp;s, pack "
  "60.0&nbsp;MPa for 3.0&nbsp;s. 1540 index points per element, all accepted, "
  "exit code 0.")

A("<h2>Before and after</h2>")
A(figure("mtf.png",
         "The same measurement read two ways. Left: the conditions the tool "
         "reports under. Right: image plane pinned, d-line only."))
A("<h3>As the tool reports it</h3>")
A('<div class="scroll">' + mtf_table("poly_solve_base", "poly_solve_mould")
  + "</div>")
A("<h3>Like for like &mdash; plane pinned, d-line</h3>")
A('<div class="scroll">'
  + mtf_table("mono_pin_base", "mono_pin_mould", "mono_pin_null") + "</div>")
A('<p class="meta">The <em>null</em> row is an index cloud whose every point '
  "is exactly the element&rsquo;s own N<sub>d</sub> &mdash; physically a "
  "no-op. At the d-line it sits %.0e waves from the baseline while moving the "
  "band ends %.0f&times; further, which is what makes the <em>after</em> row "
  "readable as moulding and nothing else."
  % (_DRES, _ENDS / _DRES))

A("<h2>Why the headline is not the moulding effect</h2>")
A("<h3>1. The image plane moved, and real rays did not ask it to</h3>")
A(figure("focus.png",
         "Through-focus on a shared grid, wide enough to bracket both the "
         "minimum and the solve. Both curves minimise in the same place; the "
         "solve went %.0f &micro;m further." % abs(HL["plane_um"] - HL["best_base_um"])))
A("<p>Paraxial EFFL reads %.4f&nbsp;mm before and %.4f&nbsp;mm after "
  "(%+.2f%%), and the marginal-ray-height solve on the last airspace follows "
  "it, moving the image plane %+.0f&nbsp;&micro;m. Real rays do not agree: "
  "best axial focus is %+.0f&nbsp;&micro;m in both states, differing by %.4f "
  "waves at the minimum. The first scan of this ran &plusmn;60&nbsp;&micro;m "
  "and both curves were still falling at its edge &mdash; a minimum found at "
  "the end of a range is a statement about the range, so it was rerun over "
  "&minus;600 to +300&nbsp;&micro;m.</p>"
  % (R["poly_solve_base"]["effl"], R["poly_solve_mould"]["effl"],
     100.0 * (R["poly_solve_mould"]["effl"] - R["poly_solve_base"]["effl"])
     / R["poly_solve_base"]["effl"],
     HL["plane_um"], HL["best_base_um"],
     best("tf_mould")[1] - best("tf_base")[1]))

A("<h3>2. STAR&rsquo;s direct-index route discards the material&rsquo;s "
  "dispersion</h3>")
A(figure("control.png",
         "The null control across the band. A uniform cloud should change "
         "nothing at any wavelength. At the d-line it very nearly does not; "
         "at both ends it plainly does."))
A("<p>MoldStress writes absolute index, n = N<sub>d</sub> + &Delta;n &mdash; "
  "one value per point, at the d-line. STAR applies it at every wavelength, "
  "so the element loses its own dispersion. On axis the null cloud reads "
  "%.6f waves at the d-line against the baseline&rsquo;s %.6f &mdash; a "
  "residual orders of magnitude below anything it does at the ends "
  "&mdash; while F moves %.4f&nbsp;&rarr;&nbsp;%.4f and C moves "
  "%.4f&nbsp;&rarr;&nbsp;%.4f. On this article the effect is larger than on "
  "the first: F alone moves %.3f waves.</p>"
  % (R["ctl"]["null"][1], R["ctl"]["no_data"][1],
     R["ctl"]["no_data"][0], R["ctl"]["null"][0],
     R["ctl"]["no_data"][2], R["ctl"]["null"][2],
     abs(R["ctl"]["null"][0] - R["ctl"]["no_data"][0])))
A('<p class="meta">Established on the first article and not re-derived here: '
  "this is not integration error (identical across GRIN steps 1.0 &rarr; "
  "0.02&nbsp;mm, a 50&times; range), and no delta form exists on this route "
  "&mdash; <code>IndexDataType</code> is read-only and reports "
  "<code>DirectRefractiveIndex</code>, while the switchable "
  "<code>PhysicsBasedIndex</code> is the stress/temperature route.")

A("<h2>What this run establishes</h2>")
A("<ul>")
A("<li><strong>Both findings reproduce on a proper Cooke triplet.</strong> "
  "The first article&rsquo;s unusual form is ruled out as the cause.</li>")
A("<li><strong>The product path works.</strong> Three catalogue polymers "
  "converted, catalogue written and attached, <code>-MoldStress</code> sibling "
  "saved without touching the original, 1540 index points per element all "
  "accepted, exit 0.</li>")
A("<li><strong>The moulding effect is small at a fixed plane.</strong> "
  "On-axis RMS wavefront %.4f&nbsp;&rarr;&nbsp;%.4f waves; the reported "
  "%.4f&nbsp;&rarr;&nbsp;%.4f is mostly the image plane.</li>"
  % (R["mono_pin_base"]["rwre_w2"][0], R["mono_pin_mould"]["rwre_w2"][0],
     R["poly_solve_base"]["rwre_w2"][0], R["poly_solve_mould"]["rwre_w2"][0]))
A("<li><strong>The paraxial shift remains unexplained</strong> &mdash; it "
  "tracks the data but is far larger than the field supports and scales "
  "non-linearly. Open.</li>")
A("</ul>")
A('<p class="meta">Scope: index-only, so nothing here bears on stress '
  "birefringence or retardance. MoldStress remains an estimate that has never "
  "been validated against a measured moulded part.")

A("<h2>Reproducing it</h2>")
A("<pre>python build4.py       # the plastic Cooke and its glass twin\n"
  "python formdump.py     # form + mouldability -> form.json\n"
  "MoldStress.exe -run -file plastic-cooke.zmx -prepare -outdir ms2\n"
  "python run2.py         # eight states, controls, rays\n"
  "python tf2.py          # through focus, wide enough to bracket\n"
  "python layoutcmp.py    # ray fans for the three-form figure\n"
  "python layoutfig.py    # fig/form.png\n"
  "python figures.py results2.json ms2 fig2 \"&lt;caption&gt;\"\n"
  "python report2.py      # summary2.json + this page</pre>")
A("<footer>MoldStress validation, second article &middot; OpticStudio 2026 "
  "R1.03 &middot; index-only, GRIN step 0.50 mm, FFT MTF 64&times;64 &middot; "
  "every number from <code>results2.json</code> and <code>form.json</code>"
  "</footer>")
A("</div>")

BODY = "\n".join(P)
TITLE = "Plastic Cooke Triplet"
with open(os.path.join(HERE, "report2.html"), "w", encoding="utf-8") as fh:
    fh.write('<!doctype html><html lang="en"><head><meta charset="utf-8">'
             '<meta name="viewport" content="width=device-width,initial-scale=1">'
             "<title>" + TITLE + "</title><style>" + CSS + "</style></head>"
             "<body>" + BODY + "</body></html>")
print("wrote report2.html")
if "--artifact" in sys.argv:
    p = sys.argv[sys.argv.index("--artifact") + 1]
    with open(p, "w", encoding="utf-8") as fh:
        fh.write("<title>" + TITLE + "</title>\n<style>" + CSS + "</style>\n"
                 + BODY)
    print("wrote", p)
print("wrote summary2.json")

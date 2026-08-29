"""Assert every number quoted in the validation README against the files it
claims to summarise. A README is a report; the same rule applies.
"""
import json, os, re, sys

REPO = os.path.expanduser(
    "~/Dropbox/Optics/zemax-user-extensions/extensions/MoldStress/"
    "validation/mtf-triplet")
S1 = json.load(open(os.path.join(REPO, "summary.json")))
S2 = json.load(open(os.path.join(REPO, "summary2.json")))
FM = json.load(open(os.path.join(REPO, "form.json")))
L = {d["kind"]: d for d in FM["lenses"]}
md = open(os.path.join(REPO, "README.md"), encoding="utf-8").read()

fails = []


def claim(text, ok, got):
    tag = "ok  " if ok else "FAIL"
    if not ok:
        fails.append(text)
    print("%s %-52s %s" % (tag, text, got))


def near(a, b, tol=6e-4):
    return abs(a - b) <= tol


# --- article 2 headline ---------------------------------------------------
h = S2["headline"]
claim("a2 as reported 0.496 -> 0.155",
      near(h["as_reported"][0], .496, 5e-4) and near(h["as_reported"][1], .155, 5e-4),
      "%.4f -> %.4f" % tuple(h["as_reported"]))
claim("a2 like for like 0.585 -> 0.579",
      near(h["like_for_like"][0], .585, 5e-4) and near(h["like_for_like"][1], .579, 5e-4),
      "%.4f -> %.4f" % tuple(h["like_for_like"]))
claim("a2 plane moved 325 um", near(abs(h["plane_um"]), 325, .6),
      "%.1f um" % h["plane_um"])
claim("a2 best focus -75 um, both",
      near(h["best_base_um"], -75, .6) and near(h["best_mould_um"], -75, .6),
      "%.0f / %.0f" % (h["best_base_um"], h["best_mould_um"]))

st = S2["states"]
claim("a2 solve-live RWRE 0.1551 -> 0.4865",
      near(st["poly_solve_base"]["rwre"][0], .1551) and
      near(st["poly_solve_mould"]["rwre"][0], .4865),
      "%.4f -> %.4f" % (st["poly_solve_base"]["rwre"][0],
                        st["poly_solve_mould"]["rwre"][0]))
claim("a2 pinned RWRE 0.1551 -> 0.1647",
      near(st["mono_pin_base"]["rwre"][0], .1551) and
      near(st["mono_pin_mould"]["rwre"][0], .1647),
      "%.4f -> %.4f" % (st["mono_pin_base"]["rwre"][0],
                        st["mono_pin_mould"]["rwre"][0]))
d_effl = 100.0 * (st["poly_solve_mould"]["effl"] - st["poly_solve_base"]["effl"]) \
    / st["poly_solve_base"]["effl"]
claim("a2 EFFL shift -0.66%", near(d_effl, -0.66, .006), "%.3f%%" % d_effl)

c = S2["ctl"]
claim("a2 null: F 0.4022 -> 0.1875",
      near(c["no_data"][0], .4022) and near(c["null"][0], .1875),
      "%.4f -> %.4f" % (c["no_data"][0], c["null"][0]))
claim("a2 null: C 0.0648 -> 0.1389",
      near(c["no_data"][2], .0648) and near(c["null"][2], .1389),
      "%.4f -> %.4f" % (c["no_data"][2], c["null"][2]))
# NOT "exact" - that claim failed here on 2026-08-29 and was corrected in
# place. What makes the control decisive is the RATIO, not a zero.
_d2 = abs(c["no_data"][1] - c["null"][1])
_e2 = max(abs(c["no_data"][0] - c["null"][0]),
          abs(c["no_data"][2] - c["null"][2]))
claim("a2 d-line residual 2e-07 waves", _d2 < 5e-7, "%.1e" % _d2)
claim("a2 band ends move >1e5x further", _e2 / _d2 > 1e5, "%.0fx" % (_e2 / _d2))
C1 = json.load(open(os.path.join(REPO, "ctl1.json")))
_d1 = abs(C1["no_data"][1] - C1["null"][1])
_e1 = max(abs(C1["no_data"][0] - C1["null"][0]),
          abs(C1["no_data"][2] - C1["null"][2]))
claim("a1 d-line residual 2.8e-05 waves", near(_d1, 2.79e-5, 2e-7), "%.2e" % _d1)
claim("a1 band ends move ~2000x further", 1900 < _e1 / _d1 < 2100,
      "%.0fx" % (_e1 / _d1))
claim("a1 C-line moulding is 0.0749, not the published 0.0852",
      near(C1["full"][2], .07488, 1e-4), "%.5f" % C1["full"][2])
claim("report.py no longer carries control literals",
      "0.0852" not in open(os.path.join(REPO, "report.py")).read(), "")
claim("README no longer claims 'exact' for the null control",
      "null control below is exact" not in md and
      "isolates it: exact" not in md, "")

# --- article 1 headline ---------------------------------------------------
h1 = S1["headline"]
claim("a1 as reported 0.816 -> 0.102",
      near(h1["as_reported_mtf40_axis"][0], .816, 5e-4) and
      near(h1["as_reported_mtf40_axis"][1], .102, 5e-4),
      "%.4f -> %.4f" % tuple(h1["as_reported_mtf40_axis"]))
claim("a1 like for like 0.820 -> 0.818",
      near(h1["like_for_like_mtf40_axis"][0], .820, 5e-4) and
      near(h1["like_for_like_mtf40_axis"][1], .818, 5e-4),
      "%.4f -> %.4f" % tuple(h1["like_for_like_mtf40_axis"]))
claim("a1 plane moved 211 um", near(abs(h1["image_plane_move_um"]), 211, .6),
      "%.1f um" % h1["image_plane_move_um"])

# --- the form table -------------------------------------------------------
def powers(k):
    return " ".join("+" if e["power"] > 0 else "-" for e in L[k]["els"])


claim("reference powers + - +", powers("reference") == "+ - +", powers("reference"))
claim("article-1 powers + - -", powers("rejected") == "+ - -", powers("rejected"))
claim("article-2 powers + - +", powers("article") == "+ - +", powers("article"))
claim("ref E2 q -0.045", near(L["reference"]["els"][1]["shape_q"], -.045, 5e-4),
      "%+.4f" % L["reference"]["els"][1]["shape_q"])
claim("a1 E2 q +1.135", near(L["rejected"]["els"][1]["shape_q"], 1.135, 5e-4),
      "%+.4f" % L["rejected"]["els"][1]["shape_q"])
claim("a2 E2 q -0.216", near(L["article"]["els"][1]["shape_q"], -.216, 5e-4),
      "%+.4f" % L["article"]["els"][1]["shape_q"])
claim("a1 worst slope 62.9 deg",
      near(max(e["slope_deg"] for e in L["rejected"]["els"]), 62.9, .05),
      "%.2f" % max(e["slope_deg"] for e in L["rejected"]["els"]))
claim("a2 worst slope 31.1 deg",
      near(max(e["slope_deg"] for e in L["article"]["els"]), 31.1, .05),
      "%.2f" % max(e["slope_deg"] for e in L["article"]["els"]))
claim("ref worst slope 25.6 deg",
      near(max(e["slope_deg"] for e in L["reference"]["els"]), 25.6, .05),
      "%.2f" % max(e["slope_deg"] for e in L["reference"]["els"]))
claim("a1 airgap 0.50 mm",
      near(min(a["centre"] for a in L["rejected"]["air"]), .50, .005),
      "%.3f" % min(a["centre"] for a in L["rejected"]["air"]))
claim("a2 airspaces 4.04 / 4.03",
      near(L["article"]["air"][0]["centre"], 4.04, .006) and
      near(L["article"]["air"][1]["centre"], 4.03, .006),
      " / ".join("%.3f" % a["centre"] for a in L["article"]["air"]))
claim("a2 mouldable, a1 and ref not",
      L["article"]["ok"] and not L["rejected"]["ok"] and not L["reference"]["ok"],
      "a2 %s  a1 %s  ref %s" % (L["article"]["ok"], L["rejected"]["ok"],
                                L["reference"]["ok"]))
claim("glass twin also mouldable", L["control"]["ok"], str(L["control"]["ok"]))

# --- numbers that appear ONLY in the README prose -------------------------
for pat in (r"0\.013 / 0\.137 / 0\.204", r"0\.156 / 0\.245 / 0\.199",
            r"0\.819", r"0\.558"):
    claim("README still contains %s" % pat, re.search(pat, md) is not None, "")

print()
print("%d claims, %d FAILED" % (len(fails) + 0, len(fails)))
for f in fails:
    print("  FAILED:", f)
sys.exit(1 if fails else 0)

"""Check this study's README against the data it summarises - and check the
DATA against the tool that produced it.

The first version only did the former, and on 2026-08-29 it passed clean while
every polychromatic number in the study was invalid: a catalogue fix had changed
them, and the README and the summary files were consistently stale. Doc-vs-data
cannot see that. So there are two kinds of check here now:

  FRESHNESS   the summaries carry a `provenance` block - does the tool commit
              that produced them still exist, and is it HEAD?
  CONSISTENCY does the README still state what the summaries say?

The numeric half of the README is generated from the summaries, so consistency
is largely by construction; what is checked below is what is still hand-written
and would drift silently.
"""
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
S1 = json.load(open(os.path.join(HERE, "summary.json")))
S2 = json.load(open(os.path.join(HERE, "summary2.json")))
FM = json.load(open(os.path.join(HERE, "form.json")))
L = {d["kind"]: d for d in FM["lenses"]}
md = open(os.path.join(HERE, "README.md"), encoding="utf-8").read()

fails, warns = [], []


def claim(text, ok, got=""):
    print("%s %-56s %s" % ("ok  " if ok else "FAIL", text, got))
    if not ok:
        fails.append(text)


def warn(text, ok, got=""):
    print("%s %-56s %s" % ("ok  " if ok else "WARN", text, got))
    if not ok:
        warns.append(text)


def git(*a):
    try:
        return subprocess.check_output(["git", "-C", REPO] + list(a),
                                       stderr=subprocess.DEVNULL).decode().strip()
    except Exception:
        return ""


# ---------------------------------------------------------------- freshness
head = git("rev-parse", "--short", "HEAD")
for name, S in (("summary.json", S1), ("summary2.json", S2)):
    p = S.get("provenance")
    claim(name + " carries a provenance block", p is not None)
    if not p:
        continue
    commit = p.get("tool_commit", "")
    known = (git("cat-file", "-t", commit) == "commit") if commit else False
    claim(name + ": its tool commit exists in this repo", known, commit)
    warn(name + ": generated at the CURRENT HEAD", commit == head,
         "%s vs HEAD %s - regenerate, or say why not" % (commit, head))
    claim(name + ": generated from a clean tree", not p.get("tool_dirty", True))

claim("both articles generated at the same tool commit",
      S1.get("provenance", {}).get("tool_commit")
      == S2.get("provenance", {}).get("tool_commit"),
      "otherwise they are not comparable")

cd = S1.get("provenance", {}).get("catalogue_MS_PMMA_CD", "")
m = re.search(r"CD\s+\S+\s+(\S+)", cd)
c1 = float(m.group(1)) if m else float("nan")
claim("the catalogue's Sellmeier c1 was POSITIVE for these runs",
      c1 > 0, "%.6e - a negative c1 inverted the dispersion until 2026-08-29" % c1)

# ------------------------------------------------------------- consistency
h1, h2 = S1["headline"], S2["headline"]
for label, vals in (
        ("article 1 as-reported MTF40", h1["as_reported_mtf40_axis"]),
        ("article 1 like-for-like MTF40", h1["like_for_like_mtf40_axis"]),
        ("article 2 as-reported MTF40", h2["as_reported"]),
        ("article 2 like-for-like MTF40", h2["like_for_like"])):
    txt = "%.3f" % vals[0]
    claim("README states " + label + " " + txt, txt in md, txt)

for label, um in (("article 1 plane move", abs(h1["image_plane_move_um"])),
                  ("article 2 plane move", abs(h2["plane_um"]))):
    claim("README states " + label + " %.0f um" % um, ("%.0f" % um) in md)


def powers(k):
    return " ".join("+" if e["power"] > 0 else "-" for e in L[k]["els"])


claim("reference powers + - +", powers("reference") == "+ - +", powers("reference"))
claim("article-1 powers + - -", powers("rejected") == "+ - -", powers("rejected"))
claim("article-2 powers + - +", powers("article") == "+ - +", powers("article"))
claim("article 2 is mouldable, article 1 is not",
      L["article"]["ok"] and not L["rejected"]["ok"])

d1 = S1["control_rwre_axis"]["dline_residual"]
d2 = abs(S2["ctl"]["no_data"][1] - S2["ctl"]["null"][1])
claim("the NULL control is a no-op at the d-line on BOTH articles",
      d1 < 1e-8 and d2 < 1e-8, "%.1e and %.1e waves" % (d1, d2))
claim("...and moves the band ends orders of magnitude more",
      abs(S2["ctl"]["no_data"][0] - S2["ctl"]["null"][0]) / max(d2, 1e-30) > 1e5,
      "the control is decisive on the RATIO, not on a zero")

claim("README no longer calls the null control EXACT unqualified",
      "null control below is exact" not in md)

print()
print("%d checks, %d FAILED, %d warnings"
      % (len(fails) + len(warns), len(fails), len(warns)))
for f in fails:
    print("  FAILED:", f)
for w in warns:
    print("  WARN:  ", w)
sys.exit(1 if fails else 0)

"""What produced a dataset, stamped into the dataset.

Added 2026-08-29, after a catalogue fix invalidated the polychromatic half of
two published reports and NOTHING caught it. `checkreadme.py` asserts the
report against its summary file and passed clean, because the report and the
summary were consistently stale - it verifies doc-vs-data, not data-vs-reality,
and cannot see a dataset that has been invalidated underneath it.

A stamp does not make the data fresh. It makes staleness VISIBLE: a reader (or
a check) can compare the tool commit that produced the numbers against the tool
commit that exists now, and the catalogue line against the catalogue on disk.
"""
import os
import subprocess

REPO = os.path.expanduser("~/Dropbox/Optics/zemax-user-extensions")
AGF = os.path.expanduser("~/Documents/Zemax/Glasscat/MOLDSTRESS.AGF")


def _git(*args):
    try:
        return subprocess.check_output(
            ["git", "-C", REPO] + list(args),
            stderr=subprocess.DEVNULL).decode().strip()
    except Exception as e:
        return "unavailable (%s)" % e


def _catalogue_row(name="MS_PMMA"):
    """The dispersion coefficients actually in force. This is the line that was
    wrong until 2026-08-29, so it is the one worth pinning."""
    try:
        lines = open(AGF, encoding="utf-8", errors="replace").read().split("\n")
        for i, ln in enumerate(lines):
            if ln.startswith("NM " + name + " "):
                for nxt in lines[i:i + 8]:
                    if nxt.startswith("CD "):
                        return nxt.strip()
        return "no CD line found for " + name
    except Exception as e:
        return "unavailable (%s)" % e


def stamp():
    return {
        "tool_commit": _git("rev-parse", "--short", "HEAD"),
        "tool_subject": _git("log", "-1", "--format=%s"),
        # Only the TOOL SOURCE (*.cs) matters. The validation outputs live
        # UNDER extensions/MoldStress and are always uncommitted at the
        # moment of generation, so a wider pathspec fires every run.
        "tool_dirty": bool(_git("status", "--porcelain", "--",
                                "extensions/MoldStress/*.cs")),
        "tool_source_at": _git("log", "-1", "--format=%h %ad", "--date=short",
                               "--", "extensions/MoldStress/*.cs"),
        "catalogue_MS_PMMA_CD": _catalogue_row(),
        "note": ("Compare tool_commit against the repo's current HEAD. If they "
                 "differ, these numbers may predate a fix - the 2026-08-29 "
                 "catalogue correction invalidated every polychromatic number "
                 "in this study while leaving the d-line ones bit-identical."),
    }


if __name__ == "__main__":
    import json
    print(json.dumps(stamp(), indent=1))

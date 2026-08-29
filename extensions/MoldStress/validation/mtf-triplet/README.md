# MTF before and after, on a plastic triplet

Run 2026-08-29, OpticStudio 2026 R1.03. The question was the simple one — what
does MoldStress do to the MTF of a real imaging system made of plastic — and the
answer is that **the number `-run` prints is mostly not the moulding effect**,
on a lens whose image plane sits on a solve.

There are **two articles**, because the first one was not manufacturable and had
to be replaced. Both findings hold on both, which is the point of keeping the
first one around.

## The findings

| | on-axis MTF @ 40 lp/mm | on-axis RMS wavefront |
|---|---|---|
| **article 2**, as `-run` reports it | 0.496 → **0.155** | 0.1551 → 0.4865 waves |
| **article 2**, like for like | 0.585 → **0.579** | 0.1551 → 0.1647 waves |
| **article 1**, as `-run` reports it | 0.816 → **0.102** | 0.0412 → 0.6888 waves |
| **article 1**, like for like | 0.820 → **0.818** | 0.0801 → 0.0836 waves |

"Like for like" means the image plane held still and the measurement taken at
the d-line, the one wavelength where the null control below is a no-op.

Two causes, neither of them moulding:

1. **The file's marginal-ray-height solve moves the image plane** — 211 µm on
   article 1, 325 µm on article 2 — following a paraxial `EFFL` shift (−0.98%
   and −0.66%). Real rays do not: on a through-focus scan over a shared grid,
   both states minimise in the same place (−25 µm article 1, −75 µm article 2),
   differing by 0.003 and 0.005 waves at the minimum. On article 2 the solve
   went **250 µm past** the true best focus.
   **Why the paraxial number moves at all is open.** It tracks the data — a
   uniform cloud moves it −0.0001 mm — but on article 1 it is ~18× larger than
   a smooth reading of the field supports, and scaling the field by 0.1 moves it
   0.155 of the full amount where first order demands 0.100.

2. **STAR's `DirectRefractiveIndex` route applies one index at every
   wavelength.** `StarFiles` writes absolute `Nd + dn` — the d-line — so the
   element loses its own dispersion. The NULL control isolates it: at the d-line
   it sits 2.8e-5 waves (article 1) and 2e-7 waves (article 2) from the
   baseline, while moving the band ends 2,000x and 1,300,000x further
   (article 2: F 0.4022 → 0.1875, C 0.0648 → 0.1389). Not exact — that was
   an overclaim, caught 2026-08-29 by asserting this README against the data.
   Not integration error — identical to four decimals across GRIN steps
   1.0 → 0.02 mm. No delta form exists on this route: `IndexDataType` is
   read-only and reports `DirectRefractiveIndex`; `PhysicsBasedIndex` is the
   stress/temperature route.

Scope: index-only mode, so none of this bears on stress birefringence or
retardance. Process conditions were the shipped defaults.

## The two articles, and why the first was replaced

**Article 1** (`plastic-triplet.zmx`, EFL 30, F/4.5, ±9°) was found by **global
optimisation**, which left the Cooke basin entirely. It scored well and was not
manufacturable:

| | glass Cooke sample | article 1 | article 2 |
|---|---|---|---|
| element powers | + − + | **+ − −** | + − + |
| E2 shape factor *q* | −0.045 | **+1.135** | −0.216 |
| stop | back of E2 | 14.5 mm behind E2 | back of E2 |
| airspaces, mm | 6.01 / 4.75 | **0.50** / 14.50 | 4.04 / 4.03 |
| steepest surface slope | 25.6° | **62.9°** | 31.1° |
| moulding checks | fails 2 (it is glass) | fails 3 | **all pass** |

**Article 2** (`plastic-cooke.zmx`, EFL 40, F/5.6, ±12°) is a transcription of
`Samples/Sequential/Objectives/Cooke 40 degree field.zmx`, scaled 0.8, with
SK16 → PMMA and F2 → POLYSTYR. Three things hold the form rather than hoping
for it:

- **Local optimisation only.** Global and hammer both hop basins, and hopping
  basins is exactly what produced article 1.
- **Explicit curvature-sign constraints** on all six surfaces, matching the
  sample surface for surface (+ − / − + / + −).
- **Moulding bounds as merit operands** — centre and edge thickness per element,
  minimum air centre and edge, and a curvature cap holding every surface slope
  under ~42°.

The glass sample fails two of the same moulding checks. That is not a criticism
of it: a 1.0 mm centre thickness and a CT/ET of 3.3 are ordinary in glass and
out of range for an injection moulding. The limits are applied uniformly so the
table is honest about who they are for.

`glass-cooke.zmx` is the same form in the sample's own glasses at article 2's
spec, so the index penalty of going all-plastic is isolated rather than asserted:
RMS wavefront 0.013 / 0.137 / 0.204 waves against the plastic's
0.156 / 0.245 / 0.199, and MTF at 40 lp/mm on axis 0.819 against 0.558. An
all-plastic *spherical* triplet at this aperture runs out of correction by about
±12°, measured by sweeping F/# and field with the form and bounds held fixed
(`build3.py`).

## The controls, and why each exists

- **NULL cloud** — every point exactly the element's own `Nd`, so physically a
  no-op. Anything it changes is the pipeline, not the part. This separated
  finding 2 from "moulding degrades the lens".
- **TENTH cloud** — the field scaled by 0.1. A first-order effect must scale
  linearly; this one does not.
- **GRIN step sweep**, 1.0 → 0.02 mm. Rules out integration error, which was the
  first (wrong) label put on finding 2.
- **Through-focus on a shared grid** rather than QuickFocus on each arm.
  QuickFocus moved the two arms by different amounts under its own all-field
  criterion, which briefly made the moulded lens look BETTER than the baseline.
  A perturbation cannot improve a system; the shared grid showed both arms
  minimise in the same place.
- **A scan range wide enough to bracket the answer.** Article 2's first
  through-focus ran ±60 µm and both curves were still falling at the edge — a
  minimum found at the end of a range is a statement about the range. Rerun over
  −600 to +300 µm, which also brackets the solve's −325 µm.
- **The same form in glass** — so "plastic costs this much" is a measurement.

## Reproducing

`build.py` and `build4.py` need no inputs; everything downstream reads what the
step before it wrote. Requires pythonnet and a standalone ZOS-API licence.

```
# article 2 - the plastic Cooke, and its glass twin
python build4.py
python formdump.py
MoldStress.exe -run -file plastic-cooke.zmx -prepare -outdir ms2
python run2.py
python tf2.py
python layoutcmp.py && python layoutfig.py
python figures.py results2.json ms2 fig2 "<caption>"
python report2.py

# article 1 - kept for the probes that were only run there
python build.py
MoldStress.exe -run -file plastic-triplet.zmx -prepare -outdir ms
python measure.py && python refocus.py && python bprime.py && python tf.py
python nullprobe.py && python grinfloor.py && python waveprobe.py
python typeprobe.py && python solveprobe.py && python fitfidelity.py
python final.py && python plots.py && python report.py
```

`summary.json`, `summary2.json`, `form.json` and both `moldstress_report*.txt`
are committed so the numbers above can be checked without a licence.

`figures.py` is parameterised on the results file; `plots.py` is its
hard-coded predecessor, kept because article 1's figures were made with it. The
rewrite happened because article 1's elements sat at surfaces 1-2/3-4/6-7 and
article 2's at 1-2/3-4/5-6, and that was one edit waiting to be forgotten.

## A correction, kept because the method is the reusable part

`checkreadme.py` asserts every number above against `summary*.json` and
`form.json`. Running it the first time failed two claims:

- **The NULL cloud is not an *exact* no-op at the d-line.** It is 2.8e-5 waves
  (article 1) and 2e-7 (article 2) away. Both print as identical at four
  decimals, which is why "exact" got written. The control is decisive because
  those residuals are 2,000x and 1,300,000x smaller than what the cloud does at
  the band ends — a ratio, not a zero.
- **One published number was wrong.** `report.py` carried the three control
  triples as literals typed out of a terminal, and the C-line moulding value
  read 0.0852 where the measurement is 0.0749. It had already been published in
  that state. `ctl1.py` now re-measures them into `ctl1.json`, and both the
  figure and the report read that file.

The general rule is in the report format already — patch the run script to emit
a summary file rather than copying numbers out of the chat — and this is what
happens when the report obeys it and the *figure* and the *README* do not.

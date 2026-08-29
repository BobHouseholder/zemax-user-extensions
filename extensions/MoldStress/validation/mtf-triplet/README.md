# MTF before and after, on an all-plastic triplet

Run 2026-08-29, OpticStudio 2026 R1.03. The question was the simple one — what
does MoldStress do to the MTF of a real imaging system made of plastic — and the
answer turned out to be that **the number `-run` prints is mostly not the
moulding effect**, on a lens whose image plane sits on a solve.

## What it found

| | on-axis MTF @ 40 lp/mm | on-axis RMS wavefront |
|---|---|---|
| as `-run` reports it | 0.816 → **0.102** | 0.0412 → 0.6888 waves (+1572%) |
| like for like | 0.820 → **0.818** | 0.0801 → 0.0836 waves |

"Like for like" means the image plane held still and the measurement taken at the
d-line, which is the one wavelength where the null control below is exact. Worst
change at 40 lp/mm over all three fields is 0.026, and it is not all in one
direction.

The gap between the two rows is two things, neither of them moulding:

1. **A 211 µm image-plane move.** The lens carries a marginal-ray-height solve on
   the last airspace. Paraxial `EFFL` reads 30.0096 → 29.7147 mm once the index
   data loads, and the solve follows it. A through-focus scan of REAL rays on a
   shared 5 µm grid puts best focus at −25 µm in both states, differing by 0.003
   waves at the minimum — the shift is confined to the paraxial calculation.
   **Why the paraxial number moves at all is open.** It tracks the data (a
   uniform cloud moves it −0.0001 mm), but it is ~18× larger than the field
   supports and scaling the field by 0.1 moves it 0.155 of the full amount where
   first order demands 0.100.

2. **STAR's `DirectRefractiveIndex` route applies one index at every
   wavelength.** `StarFiles` writes absolute `Nd + dn` — the d-line — so the
   element loses its own dispersion. The NULL control isolates it: on axis it is
   exact at the d-line (0.0801 → 0.0801) and moves both ends (F 0.0412 → 0.0968,
   C 0.1080 → 0.0717). Not integration error: identical to four decimals across
   GRIN steps 1.0 → 0.02 mm. No delta form exists on this route — `IndexDataType`
   is read-only and reports `DirectRefractiveIndex`; `PhysicsBasedIndex` is the
   stress/temperature route.

Scope: index-only mode, so none of this bears on stress birefringence or
retardance. Process conditions were the shipped defaults.

## The controls, and why each exists

- **NULL cloud** — every point exactly `Nd`, so physically a no-op. Anything it
  changes is the pipeline, not the part. This is what separated finding 2 from
  "moulding degrades the lens".
- **TENTH cloud** — the field scaled by 0.1. A first-order effect must scale
  linearly; this one does not.
- **GRIN step sweep**, 1.0 → 0.02 mm. Rules out integration error, which was the
  first (wrong) label put on finding 2.
- **Through-focus on a shared grid** rather than QuickFocus on each arm.
  QuickFocus moved the two arms by different amounts (−4.5 vs −21.7 µm) under its
  own all-field criterion, which briefly made the moulded lens look BETTER than
  the baseline. A perturbation cannot improve a system; the shared grid showed
  both arms actually minimise in the same place.

## Reproducing

`build.py` needs no inputs; everything downstream reads what the step before it
wrote. Requires pythonnet and a standalone ZOS-API licence.

```
python build.py        # the triplet, optimised and frozen
MoldStress.exe -run -file plastic-triplet.zmx -prepare -outdir ms
python measure.py      # states A/B/C + layout ray fans
python refocus.py      # per-wavelength substitution cost, refocused moulded
python bprime.py       # refocused baseline, same criterion
python tf.py           # through-focus, shared grid
python nullprobe.py    # NULL / TENTH / FULL index clouds
python grinfloor.py    # GRIN step convergence
python waveprobe.py    # DirectIndex API surface, per-wavelength
python typeprobe.py    # IndexDataType enum, delta-form attempt
python solveprobe.py   # solve active vs pinned, one change at a time
python fitfidelity.py  # fit vs data, and what the cloud actually contains
python final.py        # the eight states in the report
python plots.py        # figures into fig/
python report.py       # summary.json + report.html
```

`summary.json` and `moldstress_report.txt` are committed so the numbers above can
be checked without a licence.

The test article is deliberately spherical with bounded, mouldable thicknesses.
Letting the optimiser choose thicknesses freely ran it to negative thickness — a
small merit function on a lens that does not exist — and fixing them left it
nothing to trade, so it parked three airspaces on the same value.

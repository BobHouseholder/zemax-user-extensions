# Validation sources for MoldStress

The model currently rests on **one** reference case (Polymers 2024, 16(2), 168 —
a TOPAS 6017 plate), and that reference has two known weaknesses: its headline
ratio had to be withdrawn because it was formed across two instruments, and its
depth profile is read off 0.2 mm slabs whose sampling definition the paper does
not state. One reference with two open questions is thin for a tool meant to
ship. This file collects candidates, with what each can and cannot settle.

Ranked by usefulness, not by how easy they were to find.

---

## 1. Chang, Yu, Chiu, Yang, Lai & Wang — *Simulations and Verifications of True 3D Optical Parts by Injection Molding Process*

CoreTech System (Moldex3D) + National Tsing Hua University.
<https://www.moldex3d.com/assets/2011/09/SMGG5H7.pdf> — open, read in full.

**This is the best available check and it is better than the current reference
case in three ways: it is a LENS not a plate, the material is a cyclo-olefin, and
its depth data comes from ONE self-consistent method.**

| | |
|---|---|
| part | plano-convex spherical lens, diameter 32 mm, thickness 2 mm, gate thickness 0.8 mm, edge gate on the Y axis |
| material | **ZEONEX 480R** — a COP, same family as the COC the model has measured constants for |
| melt / mould | **275 °C / 124 °C** |
| holding pressure | 98.10 MPa |
| injection speed | 22 mm/s |
| cooling time | 60 s, ejection at 127 °C |

### What it can settle

**(a) The in-plane clause, directly.** Fig. 7 plots residual birefringence
measured along the filling path, **stated measurement error < 10%**:

| distance from gate | birefringence |
|---|---|
| 0 mm | ~3.7 × 10⁻⁵ |
| 3 mm | ~2.5 × 10⁻⁵ |
| 6 mm | ~1 × 10⁻⁵ |
| 12 mm | ~0.2 × 10⁻⁵ |
| ~24 mm | small secondary rise, ~0.4 × 10⁻⁵ |
| 30 mm | ~0 |

Peaks at the gate and decays to nothing — the shape the current model gets
wrong under every envelope configuration, now measurable on a second part.

**(b) The depth clause, by LAYER REMOVAL — one method, no cross-instrument
ratio.** Successive 0.1 mm layers were diamond-turned off and the fringe order
recounted (Table 2):

| removed | fringe order removed |
|---|---|
| 0.1 mm | **27.9%** |
| 0.2 mm | 30.8% |
| 0.3 mm | 43.9% |
| 0.4 mm | **46.2%** |

So the outer **0.1 mm of a 2 mm part — 5% of the thickness — carries 28% of the
retardance**, and the outer 0.4 mm carries 46%. That is a cumulative
distribution, which is a far stronger constraint than a two-point ratio and has
no sampling ambiguity: the quantity removed is the quantity measured.

**(c) The channel split.** The paper states flow-induced birefringence accounts
for **92%** against **8%** thermal for COC moulded lenses, citing Wang & Lai.
That is a direct check on the model's two-channel decomposition, and it is
consistent with the finding that a hot mould produces little thermal stress.

**(d) A second peak.** Both the simulation and the layer-removal experiment show
birefringence peaking near the wall AND again near the mid-thickness, the second
peak appearing between 0.2 and 0.3 mm of removal and attributed to packing flow
through the narrowing channel.

### What it cannot settle, and one caution

It reports **fringe order**, converted via `Δn = λN/h` at 589.3 nm — a
**gapwise-average**, not a point value, so it constrains the integral rather than
the profile shape directly. And **the paper's own simulation under-predicts**:
maximum fringe count 5 observed against 4 predicted. A model agreeing with
Moldex3D here is agreeing with something already known to be ~20% low.

**Internal inconsistency to be aware of:** the body text says the lens curvature
is 70 mm; Fig. 1 labels it **75 mm**. Take the geometry from whichever the
comparison is sensitive to, and say which was used.

---

## 2. Isayev et al. — residual stress and birefringence in moulded PC and PS

*Residual stresses and birefringence in injection molding of amorphous polymers:
simulation and comparison with experiment*, J. Polym. Sci. B (2006);
gapwise distributions measured with a polarising microscope at five radial
locations on centre-gated discs.

The classic dataset, and the one that establishes the flow/thermal decomposition
the model uses. **Paywalled** — characterised here from abstracts only, so it is
a lead to obtain, not a source that has been read.

## 3. Wimberger-Friedl — optical assessment of orientation, stress and density

*Prog. Polym. Sci.* (1994), a review of what is actually measurable in moulded
amorphous polymers and how the contributions separate. Paywalled.

## 4. Handbook of Plastic Optics, ch. 4 — *Metrology of Injection Molded Optics*

Wiley. Would establish what magnitudes are normal and what instruments resolve —
useful for judging whether a predicted 241 nm retardance is remarkable or
routine. Paywalled.

## 5. Wang & Lai — *Study of Residual Birefringence in Injection Molded Lenses*

ANTEC 2007, pp. 2494–2498, and the 2008 follow-up *Experimental Verifications of
CAE Predictions on Birefringence of Injection Molded Lenses*, ANTEC 2008,
pp. 421–425. These are the primary source behind the 92%/8% split quoted above.
Worth obtaining, since source 1 cites rather than reproduces them.

---

## What to do with these

The layer-removal cumulative distribution in source 1 is the single most useful
datum, because it is immune to the sampling question that still hangs over the
current reference case. Reproducing it needs the model to integrate its own
profile over successive outer layers and report the cumulative fraction — which
neither the Eulerian nor the Lagrangian path currently does, and which is a
better-posed target than the two-point ratio either is being judged on.


---

## REFUTED FALSIFIER — the 480R melt coefficient, 2026-08-18

Case 2's in-plane peak over-predicts by ~8x once its inputs are sourced. The only
borrowed *scale* in the chain is `C_melt`, taken from TOPAS 6017 for a grade sold
as low-birefringence, so this was registered with a number and a direction:

> If `C_melt(480R)` is about an eighth of `C_melt(TOPAS 6017)` — roughly 125 Br —
> case 2's peak clause passes and both shape clauses stay passing. If a measured
> 480R melt coefficient lands anywhere near 1000 Br, the over-prediction is a
> model error and this attribution is dead.

**It is dead.** Inoue et al., *Dynamic Birefringence of Amorphous Polyolefins II:
Measurements on Polymers Containing Five-Membered Ring in Main Chain*,
[Polymer Journal (1995)](https://www.nature.com/articles/pj1995122):

- `C_R` in the rubbery plateau for five amorphous polyolefins is **~1.7 × 10⁻⁹
  Pa⁻¹ = 1700 Br**, all positive and all close together;
- explicitly **"relatively insensitive to the details of molecular structure for
  this kind of polyolefins"**;
- and ROMP-made cyclic olefin polymers — which is exactly what Zeonex/Zeonor are
  — show the **larger** `C_R` values of the set, attributed to the optical
  anisotropy of the cyclic units and a less flexible main chain.

So the plausible 480R melt coefficient is **~1700 Br, larger than the 1000 Br the
model borrows**, not an eighth of it. The substitution cannot explain the
over-prediction and, if anything, understates it.

**Two things follow.**

**The ~8x on case 2 is a MODEL error.** Every material and process input on that
case is now either sourced or shown to be a safe borrowing, and each correction
made the gap larger rather than smaller. There is no input left to blame.

**And the borrowing turns out to be the safest link in the chain, not the
weakest.** `C_R` being structure-insensitive across polyolefins is the opposite
of what "low-birefringence grade" suggested. Zeon's low-birefringence claim
refers to the **photoelastic** constant — 5.0 × 10⁻¹² Pa⁻¹, ultra-low — which
governs the *thermal* channel, not the melt orientation that sets the in-plane
peak. Reading a melt property off an optical-grade marketing claim was the error;
the two coefficients are three orders apart and answer different questions.


---

# Second sweep, 2026-08-18

The first list was assembled to find a second reference CASE. This sweep asked a
different question - what can be checked WITHOUT another full case - because the
tool now has two cases and one outstanding failure (case 2's in-plane peak at
12.87x), and because case 1 passing its criterion makes independent magnitude
anchors more valuable than another geometry.

**Access, stated up front because it decides how much weight each item carries.**
Items marked READ were fetched in full. Items marked SNIPPET are characterised
from search results only - MDPI, ScienceDirect, the E3S PDF and the NTU thesis
CDN all returned 403 to direct fetches. A SNIPPET item is a lead, not evidence,
and nothing below is written into the model on one.

## A. Magnitude anchors that need no new reference case

These are the most immediately useful thing found, because they test the tool's
OUTPUT against what the industry treats as normal, and they need no process data.

**Optical-grade retardance is specified at 10-20 nm, and a well-optimised disc
measures under 18 nm.** (SNIPPET.) A disc-substrate specification quoted as
"preferably less than 20 nm, more preferably less than 15 nm, most preferably
less than 10 nm" for a 0.6 mm substrate, and a separate statement that a
well-optimised process holds under 18 nm over the data band of an optical disc.

**Against that, MoldStress predicted a peak retardance of 241 nm** on the one
moulded element of the stock Double Gauss - twelve to twenty-four times an
optical-grade specification. That does NOT convict the model: the element was
never designed to be moulded, has no gate design, and took the tool's default
process. But it is the first external number that says the tool's output is in
the "would be rejected" band rather than the "routine" band, and it points the
same way as case 2's 12.87x. **A cheap and decisive test exists**: run MoldStress
on a part whose measured retardance is published as being INSIDE spec, and see
whether it predicts something inside spec. If it predicts 200 nm for a part
measured at 15 nm, the over-prediction is general rather than specific to case 2,
which would move the diagnosis off case 2's unsourced flow inputs entirely.

**Melt fracture starts around 0.1 MPa wall shear stress.** (SNIPPET, and
consistent across two independent hits.) The onset shear stress for melt fracture
is of order 10^6 dyn/cm^2 = 0.1 MPa, measured in capillary extrusion.

Case 2's fill field computes **tau_wall = 1.67 MPa**, about 17x that. Extrusion
and injection filling are not the same regime - injection fills transiently at
far higher rates and routinely exceeds extrusion limits - so this is not proof.
It is, however, an independent second signal pointing at the same two unsourced
inputs (the cavity's share of the shot, and the 12.6 mm flow width I chose), and
it is the reason those inputs should be resolved before any more model work is
done on case 2's magnitude.

## B. A contradiction the model has inherited and must not generalise

**For polycarbonate, thermal birefringence is COMPARABLE to flow-induced
birefringence.** (SNIPPET, from the Isayev body of work.) MoldStress carries a
**92% flow / 8% thermal** split, quoted in this file from Chang et al. citing
Wang & Lai - **and that figure is for COC lenses specifically.**

This matters because the tool ships provisional PMMA / PC / PS entries. A 92/8
split is a statement about a material and a process window, not a property of
injection moulding, and applying it to PC would be wrong by roughly an order of
magnitude in the thermal channel. **Nothing in the code asserts 92/8** - the two
channels are computed independently - but the README quotes it as corroboration,
and that corroboration must stay scoped to COC. Flagged rather than fixed,
because the model does not currently use the number.

**Sign conventions on the photoelastic coefficient are inconsistent between
sources** - one gives polycarbonate as -78 x 10^-12 Pa^-1, another describes PC
as positive and PS as negative. Any future PC/PS entry needs its sign taken from
the same source as its magnitude, with the convention stated.

## C. Shape corroboration for the clause that already passes

**The published qualitative pattern for a moulded lens is: very strong
birefringence near the gate, high at the lens edge, moderate over a large area on
the gate side, low on the side opposite the gate.** (SNIPPET.) That is the shape
MoldStress produces and the in-plane shape clause passes on both cases - case 1
falls to 46.5% at the far edge, case 2 to 14%. Weak evidence, because it is
qualitative, but it is independent of both reference papers and it agrees.

## D. New leads, none of them read

- **Lai & Wang, *Study of process parameters on optical qualities for
  injection-molded plastic lenses*, Applied Optics (2008).** A JOURNAL version of
  the ANTEC work that source 5 lists, and therefore easier to obtain and cite
  than the conference papers. This is the primary source behind the 92/8 split.
- **Isayev's gapwise datasets for PS and PC** - free and constrained quenching of
  PS and PMMA strips with the gapwise distribution of thermal birefringence
  measured. This is the right shape of data for the THERMAL channel, which has
  never been tested against a measured profile; case 1 only tests it by nulling.
- **NTU thesis, *Birefringent Effects in Plastic Optics*.** Open-access landing
  page on `dr.ntu.edu.sg`; its CDN rejected the fetch. A thesis is likely to
  carry full process conditions AND gapwise profiles, which is the combination
  every published paper so far has been missing half of.
- **Multi-Objective Optimization of an Injection Molding Process for an Alvarez
  Freeform Lens (Polymers 2025, 17, 2453).** Open access, MDPI blocked the fetch;
  try the PMC mirror. Couples mould-flow to optical analysis, so it may carry
  both a process spec and an optical consequence.
- **Injection-COMPRESSION moulding birefringence, simulation and experiment.**
  ICM is the process actually used for precision optics, and MoldStress models
  pure injection. Worth knowing how far apart they are before the tool is offered
  for lens work.

## What this sweep did NOT find

**No public Cross-WLF or PVT constants for ZEONEX 480R.** Datasheets give Tg, nd,
density and flow indices but not the rheology the fill solve needs, so 480R's
rheological constants remain borrowed and marked. The Moldex3D and Moldflow
material databases hold them and are not public.

**No published maximum-shear-stress limit for optical grades.** Mould-flow
packages carry a recommended per-grade limit, which would turn case 2's 1.67 MPa
from a smell into a pass/fail. It needs the database, not the literature.

**And no source resolves case 2's two unsourced inputs** - the cavity count and
the flow width. Chang et al. state the injection speed and screw diameter but not
how the shot divides between cavity and runner, and not the gate width. Those are
still my choices, and they are still the reason case 2's magnitude clause is not
currently a test of the birefringence model.

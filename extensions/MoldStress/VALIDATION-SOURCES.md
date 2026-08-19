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


---

# Third sweep, 2026-08-18 - five parallel searches

Run as five independent searches: gapwise profiles, an in-spec part, material
constants, fountain-flow evidence, and the case-2 paper family. READ means the
full text was fetched; SNIPPET means abstract or search result only.

**The headline is not a new source. It is that the mechanism this model was
rebuilt on today is better supported as KINEMATICS than as the cause of the skin
orientation MAGNITUDE, and that a shear-only model reproduces the same profile
shape.** That does not undo the rebuild - it changes what the rebuild's passing
clause is evidence FOR.

## 1. The fountain-deposition premise: confirmed transport, contested dominance

**Confirmed, by direct visualisation.** Schmidt (1974) tracer work showed melt
originally at the inlet centreline deformed into a V and ending at the part
SURFACE - core-to-wall transport, observed. White (1974) repeated it in real
polymer melts; Coyle, Blake & Macosko (1987) built an apparatus for it and found
shear-thinning barely changes the kinematics. This half of the mechanism is solid.
(All SNIPPET, but multiple independent confirmations.)

**Contested, and by the best source found.** Flaman, PhD thesis, TU Eindhoven
(1990), `https://pure.tue.nl/ws/files/3456221/339750.pdf` - READ IN FULL.

  - **Its model contains NO fountain-flow term at all** - a 1-D lubrication
    shear-flow Leonov model in the Isayev-Hieber (1980) lineage - and it still
    "reproduces the shape of the measured birefringence profile satisfactorily,
    including the location of the maxima at the wall", across variations in melt
    temperature, flow rate and packing pressure. It fails on absolute MAGNITUDE
    only, and the author attributes that to pressure-dependence of viscosity and
    of the stress-optical coefficient - never to a missing elongational term.
    **So a skin-peaked profile does not by itself discriminate between front
    deposition and wall shear. Both produce one.**
  - **The outermost layer is optically inaccessible.** Section 6.4.3: within
    ~60 um of the surface "it was not possible to distinguish different values".
    The peak that this literature calls the "skin peak" therefore sits at
    z/H ~ 0.75-0.8 - a SUB-SURFACE shear-zone maximum - with birefringence
    decreasing again in the last measurable step toward the true wall.
  - A third maximum, distinct from both, is attributed to PACKING-stage flow.

**Wimberger-Friedl (Philips), PC discs (SNIPPET, several papers):** the surface
maximum "cannot be explained by stresses due to classical fountain flow"; the
dominant contribution is transient deviatoric stress from compression of the
vitrifying polymer plus wall adhesion. Without packing, a sub-surface maximum
appears instead and is attributed to SHEAR during filling.

**Blake's envelope and Tadmor (1974) are theory that has not been tested.**
Blake's `z*(s)` and the 1/sqrt(3) dividing streamline are an analytical Newtonian
free-surface result; no experimental measurement of the crossover location
against 1/sqrt(3) was found. Tadmor is cited by every review as the originating
HYPOTHESIS; no comparison of its predicted through-thickness profile against
measured birefringence by Tadmor was found. Mavridis/Hrymak/Vlachopoulos (1988)
is FEM, not experiment.

### What this costs this model, stated in numbers

MoldStress case 1 samples its "surface" at 97.5% of the half-wall, which on a
1.5 mm plate is **18.8 um from the wall**. The Lagrangian depth shape puts its
peak at 93% - **52.5 um from the wall**. Both sit INSIDE the ~60 um band Flaman
reports as unresolvable, and the peak Flaman does measure would land at z/H~0.78,
i.e. **165 um in**, which the current criterion ("beyond 75%") only just passes.

So today's result - depth peak 53% -> 93%, criterion MET - is a pass against a
clause whose sampling point may be inside the region its own reference cannot
resolve. **The pass is not withdrawn** (it is a different paper, a different
material and a different instrument from Flaman's, and case 1's own 0.2 mm slab
question was already open) **but it is downgraded from "the model now gets the
depth profile right" to "the model now peaks in the outer quarter, and the data
that would discriminate 78% from 93% does not exist in the sources we have."**

Two consequences worth acting on, neither done:
  - The depth criterion would be better posed on a band average over a resolvable
    depth range than on a point at 97.5%, which is what the 0.2 mm slab question
    was already hinting at.
  - Case 1's Eulerian peak at 53% (352 um in) is clearly too deep, and the
    Lagrangian at 93% may be too shallow. Flaman's ~0.78 sits between them.

## 2. The thermal channel finally has reference data

**Wimberger-Friedl, PhD thesis, TU Eindhoven (1991),**
`https://pure.tue.nl/ws/files/1962727/364279.pdf` - READ IN FULL. This is the
primary source behind the paywalled 1993/94 J. Polym. Sci. series.

It gives thermal-only gapwise profiles under BOTH boundary conditions, on PC:
  - **free quench** (no wall constraint): sign-reversing, +5..+9e-4 at the core to
    **-15..-20e-4 at the surface**;
  - **constrained quench** (wall-adhered, i.e. the moulding condition): a
    **nearly flat plateau**, +5..+8e-4, no peak at either end;
  - **injection moulded PC, full process data**: a sharp flow peak in a thin
    surface layer (~20e-4) over a flat thermal core plateau (~5e-4), with the
    thermal contribution stated to be **more than twice** the thickness-averaged
    flow contribution.
  - PC constants from the same source: stress-optical **5.5 GPa^-1 above Tg**,
    **0.1 GPa^-1 below**, Tg = 139 + 0.38p degC.
  - A causal control: insulating one mould half with 0.1 mm Teflon, suppressing
    solid-layer growth during filling, makes the surface maximum on that side
    DISAPPEAR - which supports a filling-stage origin for it.

This is the first data that can test the thermal channel against a measured
profile instead of against a null. Numbers are read off scanned figures, so
+-10-15%.

## 3. Case 2's wall shear stress now has a limit to fail against

Generic moulding-industry maxima, READ in full from a training reference
(`https://krusetraining.com/wp-content/uploads/2018/01/List-Of-Materials-Shear-Rates.pdf`):
**PC 0.50 MPa, PMMA 0.40, PS 0.25, SAN 0.30, PC/ABS 0.40, PSU 0.50**, all at a
40,000 s^-1 shear-rate ceiling. No COC/COP entry exists.

**Case 2 computes tau_wall = 1.67 MPa - 3.3x to 6.7x every amorphous limit in
that table. Case 1's 0.297 MPa is inside it.** The second sweep recorded this
number as unavailable outside the mould-flow databases; that was wrong, and it is
now the third independent signal that case 2's flow inputs are wrong rather than
its birefringence physics.

## 4. Material constants, including one that threatens a constant in use

**The PC sign disagreement is a CONVENTION artifact, resolved.** The -78e-12 Pa^-1
value comes from Cambridge DoITPoMS (READ), which writes the law as
`n_Q - n_P = C(sigma_P - sigma_Q)` - index order reversed against stress order.
Sources using matched order report the same physics as **+78 to +82 Br**. Every
source agrees the chain-parallel index is the higher one. Same magnitude, sign set
by pairing convention; state the convention beside any value adopted.

Melt (rubbery) coefficients found: **PC +3 to +4e-9 Pa^-1**; **PMMA -30 Br**, tiny
and sign-changing at 144 degC; **PS -4.65 to -4.8e-9 Pa^-1**, negative because
phenyl-ring anisotropy dominates the backbone.

**And a discrepancy that is not resolved: TOPAS 5013 is reported at -700 Br,
NEGATIVE**, against Inoue's +1700 Br for amorphous polyolefins and the +1000 Br
this model uses for TOPAS 6017. 5013 is a different grade (lower cyclic content,
lower Tg), so the sign may be composition-dependent - but until that is settled,
**no COC grade other than 6017 should be given a borrowed melt coefficient**, and
the aliasing feature makes exactly that borrowing easy to do by accident.

## 5. Two candidate cases with measured values AND full process data

**US 6,183,830 B1** (READ) - 120 mm x 0.6 mm DVD substrate, centre-gated radial
flow, which is a geometry neither current case exercises. Complete recipe:
injection rate 250 cm3/s, injection pressure ~147 MPa, melt 340 degC, mould
115 degC, cooling 5 s. Measured birefringence (ADR-2000) at 30 and 50 mm radii:
**57-59 nm** for the standard examples, and **195-200 nm** for a high-Mv PC
comparative example.

That second figure matters for calibration: a REAL moulded PC disc reaches ~200 nm
and Delta n ~3.3e-4. The second sweep's alarm that MoldStress's 241 nm on the
Double Gauss element was "12-24x above optical-grade spec" was measured against a
best-in-class DISC SPEC, not against what ordinary moulded parts do. **The
magnitudes this model produces sit inside the range real parts exhibit.** That
weakens the case for a general over-prediction and strengthens the case that
case 2's specific comparison is what is wrong - its published 3.7e-5 is at the
very bottom of everything found in three sweeps.

**US 6,506,870 B1** (READ) - the in-spec part: 10-12 nm measured against a stated
&lt;=20 nm DVD spec, 0.6 mm thick, melt 370/390 degC, mould 120 degC. Missing
injection speed, packing pressure and gate design, so it anchors but cannot be
reproduced.

**Hu & Xue, Scientific Reports 15:15451 (2025)** (READ, open access) - PMMA
aspheric lens, named grade, full DOE, real photoelastic measurement, but reports
MPa residual stress rather than nm retardance.

## 6. Case 2's paper family: two gaps, one closed and one still open

**The cavity count and gate width are NOT STATED anywhere reachable.** Chang et
al.'s Table 1 was read in full: it gives clamping force 550 kN, screw diameter
22 mm, stroke 70 mm, max pressure 259 MPa, max flow 190 cc/s and a 27 g shot
weight - all machine ratings. The ANTEC 2007/2008 papers that might carry the
mould layout are paywalled SPE proceedings. A third-party synopsis weakly suggests
a single cavity; that is not strong enough to set a boundary condition on.

**That table also refuted an argument this repo was making** - see the retraction
in `RefCase2.cs`. The 27 g is the machine's maximum shot, reproduced exactly by
pi/4 * 22^2 * 70 = 26,609 mm3 at 1.01 g/cm3, not the shot used.

**The 92/8 split traces to Wang & Lai (2007)**, two conference papers, neither
reachable, and the material is stated as "COC" while the lens is ZEONEX 480R,
which is a **COP**. The authors use the terms loosely. Whether the split was
measured or simulated could not be determined. Its scope is narrower than this
file previously recorded, and it should not be quoted for any other material.
Separately corroborating a small thermal share for this grade: annealing the lens
12 h at 125 degC left the fringe count essentially unchanged (~6.5 before and
after) - a different experiment, not the source of 92/8, and not to be conflated.

**A trend dataset exists on the SAME lens**: Lai & Wang, Applied Optics 47(12),
2017-2027 (2008) - an 8-run DOE varying melt 247.5-280 degC, injection speed
19.8-22.4 mm/s, mould 111.6-136.4 degC and holding pressure 88.29-107.91 MPa,
with Chang's 98.10 MPa near the midpoint. That would turn case 2 from one point
into a trend, which is the single largest available upgrade to this validation.
Paywalled; per-run numbers not in the accessible synopsis.

## 7. Still not found, after three sweeps

- Cross-WLF and Tait constants for any cyclo-olefin grade. Now confirmed WHY:
  Bienia et al. moulded Zeonex E48R and state the constants came from Moldflow's
  library without publishing them, while a companion PC paper publishes its own
  in full. The disclosure is possible; it simply is not done for these grades.
- Plateau modulus and terminal relaxation times for COC/COP - only a qualitative
  result that COC's entanglement molecular weight is 3-4x COP's.
- A COC/COP-specific maximum shear stress.
- Case 2's cavity count and gate width.
- Any measurement isolating the front-elongation contribution from the wall-shear
  contribution - which is precisely the experiment that would settle section 1.

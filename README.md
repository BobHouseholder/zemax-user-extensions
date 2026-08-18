# Zemax OpticStudio User Extensions

ZOS-API user extensions for Ansys Zemax OpticStudio, built and validated against
OpticStudio 2026 R1.01. Each extension is a self-contained C# (.NET Framework 4.8)
console application. Compiled executables deploy to `{Zemax Data}\ZOS-API\Extensions\`
and appear under **Programming > User Extensions** in the OpticStudio ribbon; they can
also be run from a shell against a session waiting in
**Programming > Interactive Extension** mode.

Ribbon (GUI) runs report progress and results through OpticStudio's extension
progress display, honor the **Terminate** button in their long-running loops,
and auto-open their report/image outputs when finished, since the console
window closes with the process (pass `-quiet` to disable the auto-open).
Tools that modify the system show their edits live in the editors.

## Extensions

### EquivalentGlassFinder

Solves the community feature request
["Equivalent Glass" Feature Proposal](https://community.zemax.com/got-a-question-7/equivalent-glass-feature-proposal-881):
for every glass in the loaded system it finds the closest available material by
weighted (nd, vd, dPgF) distance, reports ranked candidates, applies the best match,
and prints before/after performance (EFFL, merit function, RMS spot per field).
By default it replaces only obsolete glasses from the catalogs in use; pass
`-catalog NAME` to convert an entire design to another vendor's nearest equivalents.

Options: `-catalog NAME`, `-includeObsolete`, `-report`, `-reopt`, `-save`,
`-top N`, `-wnd/-wvd/-wpgf` (distance weights), `-quiet`.

### ReverseSystem

Reverses any sequential system in place - refractive or reflective, including
systems with coordinate breaks, negative-thickness virtual propagation, fold
mirrors, and double-pass Mangin elements, which the built-in Reverse Elements
tool cannot handle (see community threads
[How to flip the whole optical system](https://community.zemax.com/got-a-question-7/how-to-flip-the-whole-optical-system-1367),
[Reverse elements erases materials](https://community.zemax.com/got-a-question-7/reverse-elements-erases-materials-3682)).

The reversed system is derived as the mirror image traversed backwards
(`rev(op) = M.op^-1.M`): radii and polynomial sag terms negate, gaps reverse order
keeping their signs, materials ride with their gaps, and coordinate breaks negate
decenter X/Y and tilt-Z, keep tilt X/Y, and flip the order flag. Additionally:

- all solves/pickups are frozen to their values first so they cannot corrupt the rewrite
- surface apertures travel with their surfaces
- the system aperture converts to Float By Stop Size with the physical stop
  semi-diameter preserved (paraxial marginal at the primary wavelength), so the
  reversed trace is the same physical bundle
- conjugate states swap for a true reversal: real marginal ray fans (x and y,
  analysed separately with astigmatism detection) classify the original image space
  as collimated or converging, and the reversed object/image spaces and the
  afocal-image-space flag are set to match
- reflective systems are fully supported: MIRROR markers travel with their
  surfaces, interior gap signs multiply by (-1)^(mirror count), and for odd
  mirror counts the reversal operator becomes conjugation by the y-flip mirror
  (radii/conic/sag terms kept, coordinate-break rule (-dx,+dy,+tx,-ty,+tz)) so
  the reversed light still enters along +z; validated by exact double-reversal
  identity on 10 mirror systems (Cassegrains, catadioptrics, folds, off-axis
  and Yolo telescopes, and a double-pass Mangin mirror)
- unsupported surface types and multi-configuration systems with
  surface-referencing MCE operands are refused with explicit messages rather
  than silently corrupted

Options: `-save`, `-keepconj`, `-refocus`, `-rayaim`, `-keepaperture`,
`-georeport`, `-file <path>` (headless batch mode), `-out <path>`, `-quiet`.

Validated by exact double-reversal identity (LDE prescription and RMS spot values
restore digit-for-digit) on 8 refractive coordinate-break sample systems and 10
reflective test systems, by numeric global-geometry mirror-congruence checks via
`-georeport`, and by loading every reversed file in the OpticStudio GUI and
comparing its native layout against the original.

### LayoutRender

Solves a long-standing ZOS-API gap: layout windows cannot be saved as images from
the API (see [Feature Request: Layout Window Exports](https://community.zemax.com/got-a-question-7/feature-request-layout-window-exports-2244)
and [How do I output the image of an analysis in ZOS-API?](https://community.zemax.com/got-a-question-7/how-do-i-output-the-image-of-an-analysis-in-zos-api-1011) -
the ZPL EXPORTJPG workaround only works in interactive mode). LayoutRender draws
the 2D Y-Z layout headlessly and writes a PNG: surface cross-sections are sampled
from the sag equations and mapped to global coordinates via GetGlobalMatrix
(coordinate breaks and tilts handled naturally), lens elements are closed over
glass gaps, and per-field colour-coded ray fans are traced with the batch ray
tracer and terminated where rays fail. The drawing is auto-oriented: a
principal-component fit of the traced ray points rotates the view so folded and
tilted systems (fold mirrors, Yolo telescopes, off-axis designs) render along
their dominant optical axis instead of a skewed Y-Z projection. Purely axial
systems (no coordinate breaks or tilts, all vertices on the z axis) are never
rotated — their beam axis is already level and the multi-field fan would bias
the fit — and `-noorient` forces the rotation off entirely. Decentered
surface apertures (circular, rectangular, elliptical) are drawn as sections at
their true offset positions. Works in extension mode against the open system or
fully standalone for batch/scripted use.

Options: `-out <path.png>`, `-rays N` (default 7), `-width W -height H`,
`-noorient`, `-file <path>` (headless batch mode), `-quiet`.

### DetectorDump

Batch-exports EVERY detector in a non-sequential system in one command,
answering the recurring community ask that saving data from many detectors is
"tedious to manually save one by one", plus the related request to save detector
viewer graphics via the API ([thread 1534](https://community.zemax.com/zos-api-12/how-to-save-detector-viewer-graphical-plot-into-image-file-by-zos-api-1534)).
For each detector it writes the native detector file (.DDR/.DDC/.DDP/.DDV via
`SaveDetector`), a CSV pixel grid, and a false-colour PNG heatmap, and prints a
summary table (pixels, total flux, peak, hit count). Optionally runs the NSC
ray trace first.

Options: `-dir <folder>`, `-trace` (with `-nosplit`/`-noscatter`/`-nopol`),
`-data N` (0 flux / 1 irradiance / 2 intensity), `-log` (logarithmic heatmap
scale spanning four decades, for high-dynamic-range detectors where a linear
scale hides everything but the peak), `-nocsv`/`-nopng`/`-nonative`,
`-file <path>` (headless batch mode), `-quiet`.

### AthermalScan

One-command passive athermalization analysis for a uniform-environment system,
replacing the manual TEMP/PRES multi-configuration setup (community thread
[athermal design](https://community.zemax.com/got-a-question-7/athermal-design-3623)).
Applies OpticStudio's thermal model transiently (indices via the environment,
radii/thicknesses/asphere terms expanded with the glass catalog TCE, air gaps
with the LDE TCE mount column), sweeps temperature — optionally with pressure,
for an altitude or vacuum soak — fully restores the system including on error,
and reports: focus shift / EFFL / RMS (fixed and refocused) vs T, the
diffraction depth of focus and fixed-plane athermal temperature range, the
required housing CTE with a ranked table of real housing materials (including
negative-CTE ALLVAR) and their usable ranges, an exact bimetallic mount length
solution, a per-glass opto-thermal table (n, measured dn/dT, TCE, thermal glass
constant x_f), approximate per-element thermal defocus shares, and a two-panel
PNG chart. Validated against thin-lens theory on an f/4 germanium singlet at
10 µm: measured dz/dT = -0.013274 against -f·x_f = -0.013292 lens units/K
(f = 99.954, x_f = +132.98e-6/K), agreeing to 0.14%. Note that this case is
insensitive to the edge-thickness model below — its rear face is plano, its
image plane flat and its back gap TCE 0, so every sag term vanishes; the edge
model is measured separately on the Cooke triplet.

**Index convention.** OpticStudio always traces *relative* index — air at the
system temperature and pressure is exactly 1.0 — so the system pressure alone
decides whether the reported n, dn/dT and x_f are relative-to-air or absolute
(vacuum). The difference in dn/dT is n·|dn_air/dT|, ~1.4e-6/K at n = 1.5 and
1 atm, which is the whole value for a low-dn/dT crown and can flip the sign of
x_f. The convention in force is printed in the report; `-vacuum` / `-pressure`
/ `-psweep` select it explicitly, and the pressure term in the focus shift is
reported separately from dz/dT. Absolute-index catalogs such as those written
by CryoGlass need `-vacuum`.

**Refuses rather than guessing** when the environment isn't the scan's to own:
`TEMP`/`PRES` operands in the multi-configuration editor (those govern every
operand after them, even in a single configuration, so a group would keep its
own pressure and index reference); value-computing solves on the radii,
thicknesses or parameters it must write (a marginal ray height solve on the
last thickness auto-refocuses and would report a focus shift of zero — pass
`-freezesolves` to freeze them instead); and a file with *Adjust Index Data To
Environment* switched off, where OpticStudio pins index data to 20 °C / 1 atm
and the stored temperature and pressure are not the design environment — pass
`-temp0` (and `-pressure`) to declare it.

**Non-glass gaps expand along the edge**, matching Make Thermal's pickup solves
exactly. The edge length runs from the rim of one surface to the rim of the
next, expands with the mount TCE, and is transferred back onto the centre
thickness — so the sag change of both bounding surfaces feeds into the gap and a
TCE of 0 still moves a gap when the adjacent radii move.

Two details are taken from measurement rather than from manual §2.1.1.4.4.2,
which is wrong about both. The edge is measured at the **clear** semi-diameter,
not the mechanical one — changing a mechanical semi-diameter from 14 to 20 with
the clear one held at 12 moves OpticStudio's answer by exactly nothing. And
there is **no contact-point walk**: the manual describes the mount contact point
migrating radially with a clamp to keep it on the lens, but modelling that
leaves a ~0.85 µm residual, while evaluating both sags at the same unexpanded
height reproduces OpticStudio to the last displayed digit.

Verified against `TEMP` + thermal-pickup ground truth on a two-singlet system at
ΔT = 50 K, across curved/plano faces, mount TCE 23.6 and 0, and two mechanical
semi-diameters — air gaps agree to all 14 significant figures in every case.

It changes answers, not just digits. On the Cooke triplet in air, whose air gaps
carry TCE 0, dz/dT goes from +0.000260 under naive centre scaling to +0.000296,
and the required housing CTE from 4.32 to 4.92e-6/K — 14%, which redistributes
about 5 mm of length between the two metals of the bimetallic mount.

Still short of Make Thermal: semi-diameters are not expanded, and length
parameters outside the even/odd asphere terms (toroidal and biconic radii,
Zernike normalisation radii) are not scaled. A gap bounded by a surface whose
sag the tool cannot evaluate falls back to centre scaling and is named in the
report rather than silently differing.

**Outputs.** `_report.html` is the one to read — self-contained, chart as inline
SVG so it scales and prints, warnings as callouts, index convention as a header
badge — and it is the only file auto-opened after a ribbon run. `_sweep.csv`
(one row per temperature, full round-trip precision) and `_summary.json`
(everything else: environments, dz/dT, pressure terms, per-glass and housing
tables, restoration check, warnings) exist so runs can be diffed against each
other; the text transcript never supported that. `_report.txt` and `_chart.png`
are still written for anything that already consumed them.

**Ribbon runs get a settings window.** OpticStudio launches a user extension with
no command line and offers no way to supply one, so with no arguments in Plugin
mode AthermalScan asks: sweep range and steps, the design temperature and
pressure (prefilled from the system, and called out in amber when the file has
*Adjust Index Data To Environment* off, since its stored values then mean
nothing), the analysis pressure, mount track and solve handling. The last run is
remembered in `%APPDATA%\AthermalScan\lastrun.txt`. `-nodialog` suppresses it for
scripted no-argument runs; `-dialog` forces it elsewhere.

Design point and scan environment are separate: `-temp0 T` / `-press0 P` declare
what the prescription was measured in, `-pressure` / `-vacuum` / `-psweep` say
what to analyse it at. "Built in air, flown in vacuum" is `-press0 1 -vacuum`,
and the resulting focus step is reported as its own PRESSURE TERM line — at
every scan pressure that differs from the design pressure — separately from
`dz/dT`.

Options: `-tmin/-tmax/-steps`, `-track L` (mount length), `-pressure P`,
`-vacuum`, `-psweep P1:P2` (paired T/P soak), `-temp0 T`, `-press0 P`,
`-freezesolves`, `-out <prefix>`, `-outdir <dir>`, `-file <path>` (headless batch mode),
`-quiet`.

### MoldStress

Estimates the refractive-index change and stress birefringence that injection
moulding leaves in the plastic elements of a sequential system, and applies both
through OpticStudio's STAR module so the change in optical performance can be
read directly.

**ESTIMATE — not a mould-flow simulation, and not validated against a moulded
part.** That label is on every artifact the tool writes. It is held against two
published reference cases and **fails a clause on each** — see below. Moldex3D's Optics
add-on and Autodesk Moldflow Insight solve this properly and export into optical
design software; MoldStress exists for the designer who has OpticStudio and STAR
and no mould-flow seat.

Nothing is asked of the user that the design already contains. The cavity
profile `h(r)` comes from the surface sag equations, so the filling solve needs
no mesh: for a rotationally symmetric element it is a one-dimensional radial
integral. A single edge gate at +Y sized off the local wall (a ring gate above
12 mm semi-diameter) and a parting plane at the rim are chosen by default, and
any of it is overridable per element.

The chain is four stages, each held against a closed form by `-selftest` before
the next is allowed to depend on it:

- **A1** Hele–Shaw pressure and shear field, Cross-WLF viscosity, Tait equation
  of state — checked against Poiseuille flow and the analytic log law for
  converging radial flow;
- **A2** freeze history, solved numerically across the full wall — checked
  against the erf isotherm near the wall, *where that closed form is valid*. It
  is a semi-infinite result and overstates the core freeze time by 10.8× on a
  2 mm wall, which is why the numerics are the model and the closed form is the
  control;
- **A3** three channels, kept apart because they are physically apart: flow
  orientation through a **viscoelastic memory integral** (single Maxwell mode,
  λ = η/G, from melt arrival to end of flow), thermal residual stress with force
  and moment balance imposed, and density through Lorentz–Lorenz;
- **A4** assembly. STAR accepts a *stress tensor*, not birefringence, and applies
  the catalog's K11 and K12 itself — so frozen orientation, which is not a stress
  in the finished part, is converted to the equivalent stress
  `σ = Δn / (K11 − K12)` with its principal axis along the local flow.

**A polymer catalog is a prerequisite, not an extra.** No polymer OpticStudio
ships carries a `BD` record: across all 51 installed catalogs there are 578 of
them and every one is on a glass. Without it STAR does not refuse the stress data
through the ZOS-API — it accepts zero points, returns success, and reports
retardance exactly zero, which is indistinguishable from a well-moulded part.
`-writecatalog` writes the missing constants; they are marked PROVISIONAL
everywhere because they are representative of the polymer family rather than
measured for a grade.

Two behaviours worth knowing, both measured rather than documented anywhere:
`DirectIndex` and `Stress` are **mutually exclusive per surface** (loading the
index onto a stressed surface silently empties the retardance map), which is why
the density term rides in the stress tensor as a hydrostatic component; and
`GetRetardanceMap`'s first argument is a sampling selector, not a point count.

**The depth distribution of the flow channel comes from a Lagrangian particle
model, and that is the default.** The Eulerian channel computes, for each depth,
the stress an element would build up having sat there since t=0 - and under that
assumption the retained orientation must peak between wall and core, because
build-up and retention run in opposite directions in reduced time. Measurements
peak at the skin, because in fountain flow the skin never sat at the wall: it was
sheared in the hot core, carried to the wall by the advancing front and quenched
on arrival. The particle model carries that history; the shape it produces is
solved per station on the local gap and applied to the Eulerian per-station
magnitude, normalised so that no thickness-averaged quantity can move. It costs
tens of seconds per build. `-eulerian-depth` turns it off.

**Fountain flow is ON by default.** Material reaching the cavity wall got there
through the melt front, turning through roughly a right angle and stretching on
the way; that strain is imposed once at deposition and then relaxes, so the skin
keeps nearly all of it and the core loses nearly all of it. Its magnitude comes
from the front kinematics — a Maxwell fluid extended at v_front/(h/2) for one
gap-crossing time — not from a chosen strain.

It was gated off for part of its history, because enabling it then made both
criteria worse. The viscosity-weighted shear rate inverted that: shear alone now
correctly gives a fast-freezing skin almost no orientation, so **deposition at the
front is the only thing left that can orient one**. With both channels the
in-plane peak goes from 0.62× to 1.16× of the published value and the depth ratio
from 0.31 to 0.82, on measured constants with no fitted parameter between them.
(Re-measured 2026-08-18 at `-nz 161`; the pair quoted here before the
freeze-history fix, 0.26×/0.90× and 0.02/0.76, was taken on the bad grid.)
Disable with `-fountain 0` to recover the shear-only model.

### CryoGlass

Generates OpticStudio glass catalogs from the NASA GSFC **CHARMS** cryogenic
refractive-index dataset (Leviton & Frey temperature-dependent Sellmeier
fits — absolute n(λ,T) measured to ~1e-4/1e-5 class accuracy, ~20–300 K).
OpticStudio's catalog dn/dT model is a room-temperature-anchored
perturbation that degrades at cryogenic temperatures; CHARMS is the measured
ground truth there, but OpticStudio has no native support and the ZOS-API
cannot override index computation — so CryoGlass freezes the CHARMS model at
a working temperature T0, where it IS a three-term Sellmeier, and writes an
`.AGF` with **exact** Sellmeier1 coefficients plus a locally-fitted Schott
thermal model (fit error reported per glass) valid near T0. Materials so
far: Si (1.1–5.6 µm) and Ge (1.9–5.5 µm), both 20–300 K, from the free NTRS
full texts.

A built-in self-test checks the evaluator against the papers' own published
measured-index tables before every run and refuses on disagreement, so a
coefficient transcription error can never silently reach a design.
Out-of-range requests are refused by name — CHARMS stops at ~5.6 µm (LWIR is
not covered) and below 20 K; the tool never extrapolates. Generated indices
are ABSOLUTE (vacuum): set the system environment to the working temperature
at 0 atm. CHARMS carries no thermal-expansion data, so TCE is written as 0
with a warning — source it separately before AthermalScan-style analyses.

Validated against the source papers' full published tables, H.H. Li 1980,
OpticStudio's built-in infrared catalog, and the traced index across
50-295 K - see [extensions/CryoGlass/VALIDATION.md](extensions/CryoGlass/VALIDATION.md).

Options: `-temp T` (Kelvin; pure generation, no OpticStudio needed),
`-range T1:T2:N` (catalog set for STOP sweeps), `-materials "SI,GE"`,
`-fitbox K`, `-out <agf>`, `-file <zmx>` (read the lens's environment
temperature), `-selftest`, `-quiet`. Ribbon runs read the open system's
environment temperature and generate beside the lens file.


## Validation, and what currently fails

Two published reference cases, run with `-refcase` and `-refcase2`. Both exit
non-zero unless every clause holds, and **both currently fail** - the tool is
usable as an estimate and is not validated as a predictor.

Numbers below are read from the binary at `-nz 161`, not carried in prose.
How they were arrived at, including every mechanism tried and rejected, is in
[`extensions/MoldStress/VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md).
Candidate sources for further checks are in
[`extensions/MoldStress/VALIDATION-SOURCES.md`](extensions/MoldStress/VALIDATION-SOURCES.md).

### Case 1 - TOPAS 6017S-04 plate

100 x 100 x 1.5 mm, film gate on one edge, polarimetry at 594 nm, 280 C melt /
150 C mould / 71.3 MPa. Material constants from Kim, Yoon & Kornfield,
*Key Eng. Mater.* **326-328** (2006) 183.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 1.398e-4 against a published 1.2e-4 - **1.16x** | within a factor of 2 | PASS |
| in-plane shape | maximum at the gate, 46.5% of it at the far edge | must decay from the gate | PASS |
| gate null | peak moves x=0 -> x=100 mm when the gate moves | must track the gate | PASS |
| depth ratio | **3.45** surface/deep against a published 2.78 | [1.39, 5.56] | PASS |
| depth peak position | maximum at **93%** of the half-wall | beyond 75% | PASS |
| depth null (flow) | denominator collapses to exactly 0 with the freeze order mirrored | must respond | PASS |
| depth null (thermal) | CTE=0 collapses to flow-only, and the channel is material | must collapse | PASS |

**`-refcase` now reports the registered criterion as MET.** It did not before
2026-08-18; the depth ratio was 0.82 and the peak sat at 53% of the half-wall.
What changed is the depth history, not a constant - see below. `-eulerian-depth`
restores the previous behaviour exactly, including both failures.

Converged from **nz=41** since the freeze-history fix, and the depth ratio is
flat at 3.43-3.46 across nz 41/81/161/321.

### Case 2 - ZEONEX 480R plano-convex lens

32 mm diameter, 2 mm centre thickness, 0.8 mm edge gate, 275 C / 124 C,
98.10 MPa. Chang, Yu, Chiu, Yang, Lai & Wang (CoreTech / NTHU). A better check
than case 1 in three ways - it is a lens rather than a plate, the material is a
cyclo-olefin, and its depth data comes from one self-consistent method
(successive 0.1 mm layers turned off, fringe order recounted).

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 4.763e-4 against a published 3.7e-5 - **12.87x** | within a factor of 2 | **FAIL** |
| in-plane shape | maximum at the gate, 14% of it at the far edge | must decay from the gate | PASS |
| layer removal | 3 of 4 cumulative points within 10 points | 3 of 4 | PASS |

### The two open failures, and what is known about each

**The depth profile peaks mid-wall on both cases; the measurements peak at the
skin.** Located 2026-08-18 with the model's own stored intermediates rather than
a recomputation. The memory factor itself peaks at 60% of the half-wall:

| depth | reduced time at freeze | memory | tau (MPa) | dn_flow |
|---|---|---|---|---|
| 100% (wall) | 0.000 | 0.0000 | 1.67 | 1.49e-4 |
| 80% | 1.096 | 0.1603 | 1.34 | 5.54e-4 |
| **60%** | 4.521 | **0.4447** | 1.00 | **9.66e-4** |
| 40% | 9.716 | 0.4156 | 0.67 | 5.88e-4 |
| 0% (core) | 16.989 | 0.2273 | 0.00 | 9.83e-6 |

Memory is the product of two monotone factors running in opposite directions -
build-up needs reduced time, retention is destroyed by it - so the product must
peak somewhere between wall and core. At the wall a layer freezes before it can
build anything; at the core it builds fully and then relaxes.

**The flow channel's depth shape comes from the Lagrangian particle model, and
this is the default since 2026-08-18.** `-eulerian-depth` turns it off and
restores the previous behaviour exactly. It takes the depth SHAPE from `Lagrangian.cs` and applies it to
the Eulerian per-station magnitude, normalised to mean 1 over the wall - so each
station's thickness average is multiplied by 1 and cannot move. Every clause that
reads a thickness average is invariant by construction, asserted at runtime
rather than hoped for; only the depth clauses can respond. In-plane numbers come
back bit-identical either way on both cases.

The shape is solved **per station on the local gap**. It depends on the station
only through the gap, so it is solved at a few gap ratios spanning the part and
interpolated between them, using the same similarity the Eulerian channel already
applies - depths scale with the gap, times with its square. A uniform gap
collapses to a single solve, so the plate case costs exactly what it did.

| | case 1 depth ratio | case 1 peak position | case 2 layer removal |
|---|---|---|---|
| `-eulerian-depth` (was the default) | 0.82 **FAIL** | 53% **FAIL** | 3 of 4 **PASS** |
| Lagrangian, one shape per part | 3.45 PASS | 93% PASS | 2 of 4 **FAIL** |
| **Lagrangian, per-station (default)** | **3.45 PASS** | **93% PASS** | **3 of 4 PASS** |

On the default, **case 1 meets the registered criterion** and case 2 has only its
in-plane peak outstanding.

**It costs time, and that is the honest price of the default.** The shape is a
particle solve per gap node, so builds that took under a second take tens of
seconds: `-selftest` runs in about 2m45 where it used to be near-instant, and a
reference case takes 2-3 minutes. Nothing is cached between builds yet, which is
the obvious place to get it back.

The middle row is why the per-station solve exists. A single part-wide shape
makes `DnFlow[i,k] = A_i * phi[k]`, so the normalised depth profile is identical
at every station - right for a plate, wrong for a lens whose gap varies 2.5x.
Case 2's layer-removal clause is evaluated at the 0.8 mm gate region, and a shape
computed on the 2.0 mm centre thickness put 43.0% of the retardance in the first
0.1 mm against a measured 27.9%. On the local gap it reads 32.6%.

**What was swept, and what the sweeps cost.** Grid: phi at the wall 2.268 to
2.326 across nz 41/81/161/321 with the depth ratio flat at 3.43-3.46. Particles:
the first default of 4000 was carrying about 6% - phi goes 2.466 / 2.628 / 2.637
and 6.908 / 6.489 / 6.430 at 4000 / 16000 / 64000 - so the default is now 16000,
where it is converged to ~1%. Gap nodes: at 4000 particles the node count looked
to matter by 11%, and at 16000 that falls to 1-3% and goes non-monotone, so most
of it was particle noise; 6 nodes ships. Both are settable with
`-shape-particles` and `-shape-nodes`. A reference case takes 2-3 minutes with
the shape on.

**Two self-tests had to move, and neither was a numerical regression.** Both
were written for the Eulerian decomposition that the port replaces, so both are
now pinned to `-eulerian-depth`, where the property they assert is true and still
worth guarding. "The fountain is the same at the gate and the far edge" assumed
the fountain is a separable additive term at a fixed depth; under the port it is
folded into the station's thickness average and redistributed by a shape. "Shear
birefringence vanishes at the mid-plane" holds because an Eulerian element there
has sat at zero shear stress since t=0 - give the material a path and the element
now at the mid-plane arrived from somewhere with nonzero shear, so it carries
orientation. Asserting that on the default path would be asserting the assumption
the port exists to remove.

The default path gained two checks of its own: the port must leave the thickness
average alone (it matches to 1.2e-16) and it must actually move the skin value
(7.37e-5 -> 8.45e-4), so the first cannot pass on a port that did nothing. 57
self-tests pass, 0 fail.

**One clause cannot test the interpolation, and says so.** Case 2's layer removal
samples s = 0, which is the minimum gap and therefore the first interpolation
node exactly - so it returns identical numbers for every node count by
construction. The per-station phi rows printed beside it are what actually
exercise the interpolation.

**The underlying Eulerian defect is still not a term that can be corrected.** It is what an Eulerian channel
computes when it assumes every layer sat at its final depth since t=0. The
measured profile peaks at the skin because the skin's orientation was never
built locally - it was sheared in the hot core, carried to the wall by the
advancing front and quenched on arrival (Mavridis, Hrymak & Vlachopoulos,
*J. Rheol.* **32**(6) 639, 1988). `Lagrangian.cs` carries that history properly
and is not yet the shipped path.

Eight configurations of additional terms have been measured against this and
rejected; tripling the deposition term raises the wall from 1.49e-4 to 4.47e-4
and **leaves the peak at 60%**. The log records each one so they are not
proposed again.

**Case 2's 12.87x is a model error, not a bad input - but its magnitude clause
is not currently a clean test.** Every material and process input has been
sourced or shown to be a safe borrowing, and each correction made the gap
larger. The one registered falsifier - that ZEONEX 480R might have a melt
stress-optical coefficient an eighth of the borrowed value - **is dead**: Inoue
et al., [*Polymer Journal* (1995)](https://www.nature.com/articles/pj1995122),
measure ~1700 Br for amorphous polyolefins and state it is insensitive to
molecular structure, with ROMP cyclic olefins at the *high* end. Zeon's
low-birefringence claim is about the **photoelastic** constant, which governs the
thermal channel; the two coefficients are three orders apart.

The caution: the model **over-retains** rather than over-stresses (case 1
retention 0.235 against a measured 0.202; case 2 0.143 against 0.0111), and the
wall shear stress it computes, 1.67 MPa, is above the melt-fracture threshold.
Both inputs that set it - the cavity's share of the shot, and a 12.6 mm flow
width - are unsourced choices.

## Building

Requires the .NET SDK and an OpticStudio installation. `ZemaxPaths.props` (in the
sibling `repo/` clone, or create your own) points `ZEMAX_ROOT` at the install
directory; the ZOSAPI assemblies are referenced with `Private=false` and resolved
at runtime by `ZOSAPI_NetHelper`.

```
dotnet build extensions\ReverseSystem\ReverseSystem.csproj --configuration Release
dotnet build extensions\EquivalentGlassFinder\EquivalentGlassFinder.csproj --configuration Release
```

Copy the built `.exe` files to `{Zemax Data}\ZOS-API\Extensions\`.

## Licence

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Bob Householder.

That covers the source in this repository and nothing else. The extensions
**link against Ansys ZOS-API assemblies**, which are part of an OpticStudio
installation, are not included here, and are not covered by this licence. A
build output therefore carries Ansys components alongside MIT-licensed code —
so building and using the extensions is straightforward, but redistributing a
compiled `.exe` is a question about Ansys's terms, not about this one.

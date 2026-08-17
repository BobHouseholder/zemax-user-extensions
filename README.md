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
part.** That label is on every artifact the tool writes. Moldex3D's Optics
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
in-plane peak goes from 0.26× to 0.90× of the published value and the depth ratio
from 0.02 to 0.76, on measured constants with no fitted parameter between them.
Disable with `-fountain 0` to recover the shear-only model.

## Validation, and what currently fails

Against a published injection-moulded TOPAS 6017S-04 plate (100 × 100 × 1.5 mm,
film gate on one edge, polarimetry at 594 nm), on material constants measured by
Kim, Yoon & Kornfield, *Key Eng. Mater.* **326–328** (2006) 183. Run it with
`-refcase`, which exits non-zero unless every clause holds.

> **A GRID BUG INVALIDATED EVERY NUMBER BELOW UNTIL 2026-08-17, AND IT IS FIXED.**
> `FreezeHistory` sampled the cooling curve with `step % 50` into 240 slots, so the
> recorded window was `240 x 50 x dt` — and with `dt = 0.2*dz^2/alpha` and
> `dz ~ 1/n`, **that window shrank as 1/n^2**: ~0.08 s at nz=81 and ~0.005 s at
> nz=321, against a centre that does not freeze until ~3 s. The memory integral
> integrates along that grid, so at fine grids it saw almost none of the cooling
> and the shear channel collapsed. Fixed by single-pass dynamic decimation. The
> comment above that line had described the correct adaptive algorithm; the code
> implemented a different one.
>
> **The "criterion is MET" reading obtained at nz=321 before the fix was an
> artifact of this bug.** On the fixed history the depth ratio is 0.82 and
> `-refcase` exits 2. The failing flow null is what flagged it.

**Convergence, re-taken 2026-08-17 after the fix.** Default configuration:

| nz | in-plane | depth | flow null | verdict |
|---|---|---|---|---|
| 41 | 1.16x | 0.82 | PASS | NOT met |
| 81 | 1.16x | 0.82 | PASS | NOT met |
| 161 | 1.16x | 0.82 | PASS | NOT met |
| 321 | 1.17x | 0.82 | PASS | NOT met |

Flat to three figures across an 8x grid range, where before the fix it drifted
1.17 -> 1.07 -> 0.99 -> 0.95 and 0.89 -> 1.16 -> 1.36 -> 1.43. **nz=41 now gives
the converged answer**, so the expensive `-nz 321` default and the `nz=481`
ceiling are both moot. Shear-only converges too: 0.61x/0.62x/0.62x at nz
41/81/161, against 0.52x/0.26x/0.09x before.

**The in-plane criterion passes at 1.16x; the depth criterion fails at 0.82.**

| Clause | Result | Bar |
|---|---|---|
Measured 2026-08-17 at `-nz 321`, on the paper's own process conditions
(280 °C melt, 150 °C mould, 71.3 MPa) — see the correction note below.

| Clause | Result | Bar |
|---|---|---|
| in-plane peak | **0.95×** the published 1.2 × 10⁻⁴ | within a factor of 2 — **passes** |
| in-plane shape | maximum at the gate, falling only to **97.4%** at the far edge | must decay from the gate — **passes, but see below** |
| gate null | maximum moves from x = 0 to x = 100 mm when the gate moves | must track the gate — **passes** |
| depth ratio | **0.99** surface/deep against a published **2.78** | [1.39, 5.56] — **fails** |
| depth peak position | maximum at **89%** of the half-wall | beyond 75% — **passes** |
| depth null | **1.35 vs 0.81** after being rebuilt — see below | must invert — **passes** |

**Two rows moved on 2026-08-17, in opposite directions, and neither move was
the point of the change.** Depth peak position went 68% → 89% and now passes.
In-plane shape went 70.6% → 97.4% at the far edge, i.e. the predicted field is
now nearly FLAT across the plate where the reference falls roughly linearly to
zero. It still passes, and that is a criticism of the clause rather than a
defence of the model: "maximum on the gate side" is satisfied by a 2.6% tilt.
A shape clause that a nearly-flat field passes is not measuring shape.

**The depth null now works — it was the null that was broken, not the channel.**
Corrected 2026-08-17, third attempt. Earlier that day this README claimed the
depth channel was "not driven by the freeze history at all", on the evidence of
a null that would not move and a 1% response to a 30 °C mould change. **That
claim was wrong, and a positive control refuted it:** scaling every freeze time
by 100 moves the ratio 0.810 → 0.812, but scaling by 0.01 moves it to 0.908.
The channel responds to freeze times being SHORTENED and is insensitive to their
being LENGTHENED — and that insensitivity is correct physics, because once a
layer has vitrified reduced time stops accumulating and a later nominal freeze
time adds nothing to the integral.

The old null inverted `t → tMax − t`, which **lengthens** the freeze time at both
sampling depths (0.002 → 6.9 s at the wall, 0.85 → 6.05 s at 47%). It perturbed
exclusively in the direction the model is provably deaf to. It was a
rearrangement pointing the wrong way — the third version of this null that could
not fail, after an `Array.Reverse` on a mid-plane-symmetric profile.

The null now mirrors the depth axis of the **temperature history** as well as the
freeze times (|z| → h/2 − |z|), giving the wall the core's thermal history. That
inverts the driver the memory integral actually reads, rather than a derived
label. It discriminates: **1.35 vs 0.81.**

A hypothesis checked and killed on the way: the memory integral's clamp was the
obvious suspect, since a clamped quantity is deaf to its inputs. It is
instrumented now and **does not bind — 0 of 39,700 evaluations saturated,
largest raw value 0.2.**

**Where the depth deficit actually is.** The skin signal comes entirely from
fountain deposition, and it saturates:

| fountain strain | depth ratio |
|---|---|
| 0 (shear only) | **0.02** |
| 1 (shipped default) | 0.81 |
| 3 | 1.03 |

The shear channel contributes essentially **nothing** at the wall. That is
self-consistent rather than a bug: shear stress is largest at the wall, but a
layer that vitrifies on contact never deforms, so σ relaxes toward τ and never
gets there. The physical answer is the one Mavridis, Hrymak & Vlachopoulos
(*J. Rheol.* **32**(6) 639, 1988) give — skin material was not at the wall when
it was deformed. It was oriented in the hot core and carried to the wall by
fountain flow, then quenched. The model has that mechanism but treats it as a
locally computed strain that then relaxes, which is why tripling it buys only
27%.

**That change was implemented and it is measurably worse.** Available as
`-frontmode carried`, not the default. Measured at nz=81:

| | in-plane peak | depth ratio | depth null |
|---|---|---|---|
| extensional (default) | **1.07×** passes | 0.81 fails | passes |
| melt orientation carried | **4.57×** fails | 1.09 fails | **fails** |

The diagnosis of the *cap* stands: the extensional form cannot exceed the
plateau modulus (`eEff → 1` as Wi → ∞, so σ ≤ G = 2.8 × 10⁵ Pa), while the
melt's own wall shear stress here is ~5 × 10⁵ Pa, so the cap was binding on the
wrong quantity. **The implementation is what failed.** `2·τ_wall = dp/ds·(h/2)`
has no z-dependence, so it lifts every depth by the same amount and leaves
`exp(−ξ)` as the only thing separating skin from core. That inflated the
thickness average fourfold, moved the ratio by 0.28, and flattened the profile
enough to kill the null again.

What it exposes is the real missing piece: **not every depth is front-deposited.**
Material near the mid-plane is the core stream and is never swept to the wall,
so the deposition term needs a weight that falls off inward.

### The deposition weight, and what it revealed

That weight is in the literature and is parameter-free. **Blake's
maximum-residence envelope**, in M. C. Altan, *A Review of Fiber-Reinforced
Injection Molding: Flow Kinematics and Particle Orientation*, J. Thermoplastic
Composite Materials **3** (Oct 1990) 275, §2.4.4; pathlines measured by Coyle,
Blake & Macosko, *AIChE J.* **33**(7) 1168 (1987). Sorting particles by whether
they ever reached the front gives a dividing height of `1/√3` and an envelope
`x1m = (3/2)(1 − x3m²)`, i.e. a support boundary

    z*(s) = sqrt(1 − (2/3)·s/L)      in units of the half gap

Material inside `z*` never passed through the front and receives no deposition.
Implemented as `-deposition-support`. **It is not the default, and the reason is
the interesting part.** Measured at nz=81:

| | in-plane peak | depth @ gate (criterion) | depth @ s/L 0.1–0.5 |
|---|---|---|---|
| without envelope | **1.07×** passes | 0.81 fails | 0.81 |
| with envelope | **0.28×** fails | 0.02 fails | **2.52** vs published 2.78 |

**At every interior station the depth ratio is 2.52 against a published 2.78** —
9% low, inside the band, with no fitted parameter. The depth *shape* problem is
essentially solved by a kinematic result taken off the shelf.

Two things break, and both are diagnostic rather than incidental:

1. **The in-plane peak collapses, 1.07× → 0.28×**, because the core value falls
   1.39 × 10⁻⁴ → 4.48 × 10⁻⁵ once the core stops receiving deposition. That is
   the finding: **the in-plane agreement at 1.07× was propped up by depositing
   fountain orientation into the core, where the literature says none is
   deposited.** Removing it exposes that the shear channel under-predicts the
   core by about 3×. That is a better-located problem than "the skin is 10× low",
   and it is in the channel that has always been the model's weakest.
2. **The registered criterion samples at s = 0, and `z*(0) = 1`** — the gate is
   the one station where the envelope admits no deposited material at all. The
   boundary crosses the criterion's own surface sampling point (0.975 of the
   half-wall) at s/L = 0.075, so only the first ~7% of the flow length is
   affected. The reference paper measured its depth profiles at positions A, B
   and C and publishes coordinates for none of them, so **the criterion's station
   has NOT been moved to suit** — the ratio is reported across stations beside it
   instead. This is the fifth time a number on this model has turned on a
   sampling definition before it turned on physics.

### The in-plane peak is now the maximum of the profile

Corrected 2026-08-17. The clause reads *"predicted peak within a factor of 2 of
1.2 × 10⁻⁴"*, and the code took `avg[0]` — the same thing only if the maximum
sits at the gate. That held for every model this case had run until Blake's
envelope arrived, and `z*(0) = 1` admits no deposited material at the gate edge
exactly, so `avg[0]` collapsed to the shear-only value and the clause fell
1.07× → 0.28×. **That was the criterion reading the one station where the
kinematics is singular, not the model losing its peak.**

Taking the actual maximum is the literal reading and does not weaken anything:
clause (b) still separately requires that maximum to lie on the gate side and to
decay toward the far edge. The gate-edge value is printed beside it. The change
is **inert in the default configuration** — the maximum is at s = 0 there, so
1.07× is unchanged.

Under the envelope the peak recovers to **0.59×, inside the factor of 2**. But it
now fails a different clause, and this one is physical rather than a sampling
artefact:

| envelope | in-plane peak | peak location | far-edge value |
|---|---|---|---|
| off | 1.07× passes | s = 0 mm | 76.1% — decays, **passes** |
| on | 0.59× passes | **s = 92 mm** | **129.3% — rises, FAILS** |

**Blake's envelope makes the fountain-deposited layer thicken along the flow, so
predicted birefringence RISES with distance from the gate. The reference says it
falls roughly linearly to zero.** This is exactly the gate-versus-far-field
tension flagged before the envelope was implemented, when it looked like it might
be confined to the first 7% of flow length. It is not: it inverts the whole
along-flow profile.

So the envelope trades a depth-shape success for an along-flow-shape failure, and
it stays opt-in. What it has genuinely established is that the depth ratio and
the along-flow decay are coupled through one term, and no single scaling of that
term satisfies both.

### An along-flow decay for the deposition — implemented, measured, not adopted

`-deposition-decay` scales the front deposition by the shear window available to
the melt **feeding** the front at that station (the memory bracket at the
mid-plane, the core stream the front draws from). The argument is not a new one:
it is the same expression that already gives the shear channel its gate-to-edge
decay — at the far edge the melt arrives as filling ends, the window is
identically zero, and there is no orientation to deposit.

It does produce decay, and it is still wrong. Measured at nz=81 with the
envelope on:

| | in-plane peak | peak location | far edge | depth @ s/L 0.1–0.5 |
|---|---|---|---|---|
| envelope only | 0.59× | s = 92 mm | 129% — rises | **4.08** |
| envelope + decay | **0.31×** | s = 87 mm | 0% — falls | 1.56 |

The far-edge rise is fixed, but the profile is now a **hump**: it climbs from the
gate to a maximum at 87% of the flow length and then collapses to zero. The
reference falls roughly linearly *from the gate*. And both magnitudes get worse —
in-plane 0.59× → 0.31×, depth 4.08 → 1.56.

The mechanism is visible in the two terms. Blake's support **grows** with
distance (z* falls from 1 to 0.577) while this window factor **falls** slowly and
then crashes at the very end. Their product peaks near s/L = 0.87. No scaling of
either fixes that — a monotone decay from the gate needs the magnitude to fall
faster than the support grows, everywhere, and this factor does not.

**It also exposed a weak clause.** *"Maximum on the gate side"* is implemented as
an endpoint comparison, `profile[0] > profile[last]`, so a profile peaking at 87%
of the length **passes** it. It passed here while the shape was plainly wrong.
The peak location is now printed beside the verdict so the gap is visible; the
registered clause itself is left alone rather than quietly tightened.

### No magnitude term can rescue the envelope — this is a proof, not a measurement

The deposited layer's thickness fraction is `f(s) = 1 − z*(s) = 1 − √(1 − ⅔·s/L)`,
and **`f(0) = 0` exactly**. The thickness-averaged deposition at the gate is
therefore zero for *any* magnitude term `M(s)`, because `M` multiplies a layer of
zero thickness.

That forecloses the whole search. The in-plane clause needs the maximum on the
gate side; with deposition contributing nothing there, the gate value is pinned
at the shear-only value, **0.26× of published**. Any `M` large enough to reach the
factor-of-2 bar (0.5×) necessarily puts the maximum somewhere `f > 0` — away from
the gate — failing the shape clause. **The two clauses are mutually exclusive
under a hard envelope support, independently of `M`.**

Confirmed numerically, with a control separating the deposition term from overall
grid drift:

| grid | shear-only at gate | envelope at gate | deposition at gate |
|---|---|---|---|
| nz=41 | 6.233e-5 | 6.781e-5 | **5.48e-6** |
| nz=81 | 3.071e-5 | 3.348e-5 | **2.77e-6** |
| nz=161 | 1.037e-5 | 1.176e-5 | **1.39e-6** |

The deposition at the gate halves with every grid doubling — converging to zero as
the analysis requires. Its nonzero value on coarse grids is a **one-node
discretisation artifact**: `z*(0) = 1` makes the support a measure-zero set that
the grid nonetheless resolves with a single node.

**What must change is the support, not the magnitude.** And there is a reason to
doubt `z*(0) = 1` physically: Blake's envelope classifies material by whether it
was *transported* to the wall from upstream. It says nothing about the material
that **constituted the initial front** at the gate, which was itself
fountain-processed and laid onto the wall there. `z*(0) = 1` is a statement about
transport history, not evidence that gate-wall material never saw a front.

**A separate defect the control exposed, and it is not about the envelope:** the
shear-only gate value is itself strongly grid-dependent — 6.233e-5 → 3.071e-5 →
1.037e-5, still falling steeply at nz=161. **The in-plane number is not converged
with the fountain off.** The convergence sweep behind the shipped `-nz 321`
default was run with the fountain ON, so it never covered this configuration.
Any future work on the shear channel at the gate needs its own sweep first.

### The depth criterion now uses both channels

Corrected 2026-08-17. The depth clause compared `DnFlow` alone against a profile
the source measured in the **xz and yz planes** — out of plane, on slabs cut from
the plate and viewed edge-on — where the thermal residual stress contributes in
full. Isayev (*J. Polym. Sci. B*, 2006) has the thermal part dominating the core
outright. Comparing one channel against a two-channel measurement is a
measurement-definition error of the same class as the withdrawn 5.56.

**The in-plane clause is deliberately unchanged.** Thermal stress is equibiaxial
in plane (σxx = σyy, σzz = 0), so it contributes exactly **zero** to the in-plane
difference that clause measures. Adding it there would be adding a term that
vanishes in the geometry it is measured in. In-plane stays 1.07×.

| | depth @ gate | depth @ s/L 0.1–0.5 | published |
|---|---|---|---|
| flow only, no envelope | 0.81 | 0.81 | 2.78 (yz) / 4.67 (xz) |
| **flow+thermal**, no envelope | **1.16** | 1.16 | |
| flow only + envelope | 0.02 | 2.52 | |
| **flow+thermal + envelope** | 1.08 | **4.08** | |

With both corrections applied, the interior-station ratio is **4.08 — between the
two planes the source actually measured** (2.78 yz, 4.67 xz), and 13% below the
cross-plane value. That is the closest this model has come, on a corrected
reference and a literature deposition weight, with no fitted parameter.

**It cost the null, and the null has been rebuilt — fourth version.** Correcting
the clause dropped the freeze-order null to 1.19 vs 1.16, just under the bar.
Nothing about the perturbation got worse: the thermal channel is nearly flat
through the thickness, so adding it to numerator and denominator alike drags any
ratio toward 1 and compresses whatever the null was resolving. **A single null on
a summed quantity is always diluted by whichever channel is flatter, and a bigger
kick does not fix that.** So the control is decomposed the same way the
measurement is:

| control | what it perturbs | requirement | result |
|---|---|---|---|
| **(i) flow** | mirror the temperature history | flow-only ratio must move >50% | 1.349 vs 0.812 — **passes** (2.090 vs 0.018 with envelope) |
| **(ii) thermal** | CTE = 0 | total must collapse **exactly** onto flow-only, **and** differ materially when on | rel err 0.0, contribution 43% — **passes** |

Clause (ii) has two halves that fail in opposite directions, deliberately. The
collapse identity alone would hold **trivially if the thermal term were never
added at all**, so a check built only on it would pass when the feature is
absent. The second half requires the channel to move the reported ratio.

And the identity check carries its own positive control: the same comparison is
fed a pair it must reject (thermal-ON total against flow-only) and required to
report a difference. It reads 3.5 × 10⁻¹, so the tolerance is not swallowing
everything.

The `s/L = 1.0` column reads infinity: at the far edge the deep sample sits
inside `z*` and the shear channel is identically zero there, so the denominator
is exactly 0. Reported rather than suppressed — it is a real degeneracy of
sampling a ratio at a station where the denominator vanishes.

**What the failure means.** The predicted profile is now core-weighted where the
real part is skin-peaked. That is not a missing magnitude — it is the balance
between the two channels through the thickness. Shear correctly gives a
fast-freezing skin almost nothing, and fountain deposition supplies what the skin
has, so the depth shape is set by how those two trade off, which is the open
question. The in-plane peak, by contrast, is 0.95× of the published value on
measured constants with **no fitted parameter between the two channels.**

**Numbers here are the default: `-refcase` now runs at nz=321.**  They are still drifting.

> **The sweep below was taken at the OLD process conditions (290 °C / 120 °C /
> 60 MPa) and its VALUES are superseded** by the 2026-08-17 correction — at
> 150 °C the pair is 0.95× / 0.99 at nz=321 and 0.81 depth at nz=81. Convergence
> expires when the model changes. The GRID at which it converged plausibly
> carries over, and that is all the sweep is still quoted for.

Neither
registered number is converged at the shipped default of 81: peak 1.01× / depth
0.74 at nz=81, 0.90× / 0.91 at 161, 0.85× / 0.98 at 321. The trend is monotone
and decelerating, and extrapolates to roughly 0.83× and 1.0. **nz=641 does not
complete** — the freeze solver is explicit, so cost grows as the cube of the node
count and the step cap is reached before the core freezes. So convergence here is
demonstrated by trend, not by brute force, and `-refcase` warns if you drop below it. `-run` now runs the physics at nz=321 too and exports a small **wall-clustered** subset of those same nodes, so a converged model no longer forces an unmanageable file. Depths are placed quadratically in distance from the wall — dense at the skin, where all the structure is, sparse in the core — and taken as actual physics nodes, so nothing is interpolated. `-nzexport` is an upper bound rather than a count: near the wall several requested depths collapse onto the same node and are de-duplicated, so 41 requested yields 19 distinct.

**The deficit is located and it is not a shape problem.** At the surface the
model gives ~1.0 × 10⁻⁴ against a published 10 × 10⁻⁴ — a factor of ten low —
while at the published 0.4 mm depth it gives ~1.1 × 10⁻⁴ against 1.8 × 10⁻⁴,
which is within a factor of two. The core is roughly right and the skin is short
by an order of magnitude. Sampling depth is not the explanation: sweeping the
surface sampling point from 19 µm to 0.1 µm moves the value by under 1%.

**Three candidate explanations for the depth deficit have been eliminated by
measurement rather than argument:** the melt stress-optical coefficient (the
depth ratio is invariant under any scaling of it), the fountain term's magnitude
(the physically correct version is *smaller* than the arbitrary one it replaced,
and the profile needs more skin weighting, not less), and grid resolution. What
remains is the shear channel's behaviour in the skin.

Options: `-writecatalog [-out <agf>]`, `-gates`, `-run`, `-refcase`, `-selftest`,
plus `-file <zmx>` (headless batch mode), `-gateconfig <file>`, `-outdir <dir>`,
`-filltime`, `-packpressure`, `-packtime`, `-melttemp`, `-moldtemp`,
`-materials A,B`, `-directindex`.

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

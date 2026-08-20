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

### DistortionTarget

Builds a chrome-on-glass dot distortion target in non-sequential mode: a glass
plate carrying a square grid of chrome dots, replicated by an **Array** object
rather than placed as individual objects, which is what makes ~40,000 dots
affordable at all. Defaults reproduce [Edmund Optics
15963](https://www.edmundoptics.com/p/100-x-100mm-05mm-spacing-glass-distortion-target/15963/)
— 100 x 100 x 1.5 mm soda-lime, 0.250 mm dots on a 0.500 mm pitch, reflective
first-surface chromium — and every default is a configuration that was built and
traced against acceptance criteria fixed before the trace, not a guess. A ribbon
run gets no command line, so the parameters are exposed in a settings dialog.

Options: `-n <int>` (dots per side), `-pitch <mm>`, `-dot <mm>` (dot diameter),
`-plate <mm>`, `-thick <mm>`, `-material <name>`, `-coating <name>`,
`-film <mm>` (chrome film thickness), `-drawlimit <int>`, `-rig` (also add a
collimated source and detector), `-save <path>`, `-file <path>`, `-nodialog`.

The dialog seeds itself from the last run, so precedence is **explicit flag >
last run > built-in default**: `-n 150` opens the dialog showing 150, not
whatever was built last time.

The dialog recomputes the derived geometry on every keystroke — span, outermost
dot edge, clearance to the face, chrome fill fraction, whether a dot lands on
axis — and **refuses to build while the corner dots hang over the edge of the
plate**. That guard is there because the failure is invisible in the inputs and
obvious in the outputs: reading the vendor's "pattern size 100 x 100" as the grid
span gives 201 dots, whose outermost edge lands at 50.125 mm on a 100 mm plate.
"201, 0.5, 100" looks perfectly reasonable; "clearance -0.125 mm" does not.

Three things it does that are not obvious, each of which is a silent failure in
the ZOS-API rather than an error:

- **The dot is a thin Cylinder Volume, not a flat disc.** Assigning a `Coating`
  to a single-face flat object — Annulus, Ellipse, Rectangle, CylinderPipe — is
  silently ignored: no exception, and the property still reads `None` afterwards,
  for any coating name including stock ones. Only multi-face solids and
  PolygonObject accept one. A flat dot blocks light but carries no reflectance at
  all, which is precisely wrong for a part sold on its reflective chrome. The
  coating is read back after being set, and the build fails if it did not take.
- **Face 1 is the front face** (0 = Side Faces, 2 = Back Face). Coating the wrong
  face is exactly as silent as not coating at all.
- **Parameter cells are typed.** The Array object's counts and draw limit are
  Integer cells, and `DoubleValue` throws on them — from the getter as well as the
  setter, so the type cannot be discovered by reading the cell first. Every write
  dispatches on `cell.DataType`.

**Dot visibility and render speed.** The layout will not show you all the dots,
and that is deliberate — two separate mechanisms are at work, neither of which
affects a single traced result:

- **`Draw Limit`** (Array parameter 20, verified off the cell header) caps how
  many array elements are *drawn*. OpticStudio's own default is 500; this
  extension uses 2000. At the default target that means the layout renders 2000
  of 39,601 dots, so the field looks sparse and the corners look empty. Set
  `-drawlimit` to `n^2` (39601 for the stock target) to draw every one.
- **The parent dot is hidden.** Object 2 carries *Do Not Draw*, because it is a
  template the Array replicates rather than a dot in its own right. It also
  carries *Rays Ignore This Object*, so it is not traced either — without that it
  would double-count one dot.

Raising the limit to the full field is slow, and heavier than the same grid drawn
as flat discs would be: each dot is a solid **Cylinder Volume**, forced by the
coating limitation above. Drawing 39,601 solids is a different proposition from
drawing 39,601 discs. Prefer the wireframe
**NSC 3D Layout** over **NSC Shaded Model** at high limits, raise it only when you
actually want the render, and put it back afterwards.

Timings are deliberately not quoted here: the ZOS-API runs headless with no
graphics context — `ToFile` on a layout analysis writes a text stub whatever
extension you give it — so the render can only be produced and timed in the GUI,
and it has not been measured.

Radiometry with this target requires **ray splitting on**. Without it OpticStudio
applies no coating at all, so the chrome neither blocks nor reflects and the plate
reads as bare glass.

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

#### MoldStress validation, and what currently fails

Four published reference cases, each with a criterion registered BEFORE it was
first run. Numbers below are read from the binary, not carried in prose.

| | what it tests | verdict |
|---|---|---|
| `-refcase` | moulded plate, flow + thermal | **criterion MET** |
| `-refcase2` | moulded lens, curved, layer-removal depth data | NOT met - one clause |
| `-refquench` | free quench, the THERMAL channel alone | **criterion MET** |
| `-refplate` | moulded plate, flow and thermal SEPARATED by the author | NOT met - 6 of 8 |

How each was arrived at, and every mechanism tried and rejected, is in
[`VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md). Candidate sources
and three literature sweeps are in
[`VALIDATION-SOURCES.md`](extensions/MoldStress/VALIDATION-SOURCES.md).

#### Case 1 - TOPAS 6017S-04 plate

100 x 100 x 1.5 mm, film gate on one edge, 280 C / 150 C / 71.3 MPa. Constants
from Kim, Yoon & Kornfield, *Key Eng. Mater.* **326-328** (2006) 183. Polymers
2024 16(2) 168.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 1.392e-4 against a published 1.2e-4 - **1.16x** | within 2x | PASS |
| in-plane shape | maximum at the gate, 47.3% of it at the far edge | must decay | PASS |
| gate null | peak moves x=0 -> x=100 mm when the gate moves | must track | PASS |
| depth ratio | **3.44** against a published 2.78 | [1.39, 5.56] | PASS |
| depth peak | maximum at 95% of the half-wall | beyond 75% | PASS |
| depth null (flow) | mirrored freeze order drives the deep value to exactly 0 | must respond | PASS |
| depth null (thermal) | CTE=0 collapses to flow-only, channel material (3.44 vs 2.84) | must collapse | PASS |

Converged at **nz=41**: depth ratio 3.43 / 3.46 / 3.45 / 3.43 and peak 1.16x /
1.16x / 1.16x / 1.17x at nz 41 / 81 / 161 / 321. nz=21 is NOT converged, so 41 is
a floor rather than the smallest grid that passed.

**Channel split, printed beside the clause because a ratio built from a SIGNED sum
cannot be attributed from the ratio alone:** at the surface sampling depth flow is
3.169e-4 and thermal +4.42e-5 (14%); at the deep point 1.116e-4 and -6.66e-6 (6%).
That brackets the 8% thermal share published for this material class. It was 26%
until the post-vitrification increment was restricted to free parts - see below.

#### Case 2 - ZEONEX 480R plano-convex lens

32 mm diameter, 2 mm centre thickness, 0.8 mm edge gate, 275 C / 124 C, 98.10 MPa.
Chang, Yu, Chiu, Yang, Lai & Wang (CoreTech / NTHU). Chosen because it is a lens
rather than a plate, a different material family, and its depth data comes from
successive 0.1 mm layers turned off with the fringe order recounted - so the
quantity removed IS the quantity measured.

| Clause | Result | Bar | |
|---|---|---|---|
| in-plane peak | 4.434e-4 against a published **3.68e-3** - **0.12x** | within 2x | **FAIL (LOW)** |
| in-plane shape | maximum at the gate, 14% of it at the far edge | must decay | PASS |
| layer removal | 32.4 / 44.7 / 48.9 / 50.0% against 27.9 / 30.8 / 43.9 / 46.2% | 3 of 4 within 10 pts | PASS |

**The registered reference was wrong by a factor of 100, and correcting it
INVERTS this failure.** Chang et al. Fig. 7's y-axis is labelled x10^-5 and its
peak reads 3.7 - the source of the old 3.7e-5. That label is a typo. The same
paper gives dn = lambda*N/h, states a maximum observed fringe count of N = 5, and
has a 0.80 mm gate, so 589.3e-9 x 5 / 0.8e-3 = **3.68e-3** - matching the plotted
3.7 to two significant figures. Two cross-checks from the companion paper agree
in order: 6.5 fringes over the 2 mm centre is 1.9e-3, and its removal axis runs
to 2.21e-3. Nothing in either paper supports 1e-5.

The model reads 4.434e-4, so it is about **8x LOW** where it had been recorded as
12x HIGH. **The correction does not rescue the clause - it still fails, in the
other direction - which is why it can be trusted.** Withdrawn along with the old
direction: "the model over-predicts"; the suspicion that a melt-fracture-level
wall shear stress was CAUSING an over-prediction; and the reading that 480R's
borrowed melt coefficient being larger than 1000 Br would make matters worse. It
would now help.

**The fill time is now sourced** at 0.50 s (Lai & Wang Fig. 5c), replacing a
0.109 s derivation that assumed the whole screw output entered this one cavity.
tau_wall falls 1.67 -> 1.05 MPa, within ~20% of that paper's own simulated
0.75-0.89 MPa peak, so the melt-fracture concern largely dissolves on sourced
numbers. The gate width remains a choice of mine.

**The sampling thickness in the reference is pinned, not open.** Eq. (9)'s text
calls Fig. 7 the "gap wise average residual birefringence" - the same quantity
this model averages - and solving that equation for the thickness each axis
reading would require settles both at once, assuming nothing: the x10^-5 label
needs h = 79.6 mm, forty times the lens's centre thickness, while x10^-3 needs
0.796 mm, the 0.80 mm gate land to within 0.5%. So the 5x deficit is real and
none of the five candidates explains it - not the flow inputs, the relaxation
time, the retained fraction, the depth port's normalisation, or the conversion
thickness.

#### Case 3 - free quench, the THERMAL channel alone

Bisphenol-A polycarbonate, 2 mm sheet, 160 C -> 60 C bath. Wimberger-Friedl, PhD
thesis, TU Eindhoven (1991) ch. 3.2, open access. Cases 1 and 2 are mouldings, so
every number in them is flow and thermal together; this channel had only ever
been tested by NULLING it. A quench has no flow, and the thermal construction
reads only the freeze history - so no gate, flow rate or fill time reaches it.

| Clause | Result | Bar | |
|---|---|---|---|
| sign reversal | core +5.38e-4, surface -1.42e-3 | must reverse | PASS |
| direction | core tension, surface compression | as published | PASS |
| zero crossing | z/d **0.649** (published 0.5-0.8) | [0.40, 0.90] | PASS |
| shape ratio | \|surface\|/\|core\| **2.64** (published 1.7-4.0) | [1.0, 8.0] | PASS |
| magnitude | \|surface\| 1.42e-3 against a published 1.75e-3 | within 3x | PASS |
| null | CTE=0 collapses the profile to exactly 0 | must collapse | PASS |
| control on the null | CTE restored gives 3.20e-3 | must not be dead | PASS |

Published figures are read off scanned figure axes at +-10-15%, so every band is
set wide for that. The magnitude clause was registered as deliberately weak
BEFORE running, because the source attributes much of the quench birefringence to
frozen-in orientation above Tg, which this channel does not model.

**All seven passed first run, and the case immediately found a failure they
cannot see.** The source reports the zero crossing moving OUTWARD as initial
temperature rises, z/d ~0.3 to ~0.85, naming Ti the dominant control. The model
moves it 0.645 -> 0.656 - right direction, span 51x too small. Printed as an
unscored trend diagnostic rather than folded into the verdict, because
registering a clause after seeing its result is moving the bar.

#### Case 4 - moulded PC plate, with the channels separated by the author

80 x 35 x 2 mm polycarbonate plate, melt 320 C, mould 30-120 C, 25.4 cm3/s, no
packing stage. Wimberger-Friedl (1991) ch. 3.3 - the injection-moulding half of
the same open-access thesis that supplies case 3.

**This is the first case that can test the SPLIT between the two channels**, and
it can because the author took the part apart: residual stress by layer removal
(*not exceeding 1 MPa*, below 1e-4), frozen-in thermal orientation (5e-4 average,
*more than twice* the flow contribution), and flow orientation at the surface,
attributed to elongation at the melt front. This model has the first and the
third and not the second, so its like-for-like counterpart is the 1 MPa bound and
NOT the 5e-4 plateau.

The fill time is sourced twice and the two agree: 5600 mm3 at 25.4 cm3/s is
0.220 s, and the measured cavity-pressure trace peaks at about 0.35 s.

| Clause | Result | Bar | |
|---|---|---|---|
| thermal stress bound | peak **7.44 MPa** (interior 2.83) | <= 3 MPa | **FAIL** |
| thermal dn bound | peak **5.36e-4** | <= 3e-4 | **FAIL** |
| total magnitude | 4.03e-4 against a measured 6.0e-4, ratio **0.67** | [0.15, 1.00] | PASS |
| flow peak position | \|z/d\| **0.888** (published ~0.95) | >= 0.70 | PASS |
| Tm trend, peak moves out | 0.875 -> 0.888 -> 0.888 at 30/60/90 C | must rise | PASS |
| Tm trend, stress rises as Tm falls | 13.8 MPa at 30 C vs 4.98 at 90 C | must fall | PASS |
| null | CTE=0 collapses the thermal stress to exactly 0 | must collapse | PASS |
| control on the null | CTE restored gives 7.44 MPa | must not be dead | PASS |

The total-magnitude clause is **one-sided on purpose**: over-predicting is a
FAILURE here, which no other case in this project does. The model is missing the
mechanism the source says supplies more than half of what the instrument sees, so
reaching the measurement would mean compensating with the wrong physics rather
than agreeing. 0.67 and below is the direction that makes sense.

**The two failures are the result this case was built to get.** The thermal
channel over-predicts residual stress in a moulding by three to eight times, and
the mechanism is a boundary condition the source names: with wall adhesion
*"stresses are not equilibrated within the polymer ... When the polymer is
released, the tensile stresses will be relieved so that no residual stresses
remain"*. This model imposes free-plate force and moment balance at every
increment while the part is still adhered to the cavity. That reopens the
constrained-then-released branch refuted earlier - it was refuted against the
POST-vitrification increment, where zero is genuinely wrong, and was never tried
on the during-solidification stage where this stress is built.

Two numerical defects, handled differently and deliberately. **The boundary node
is an artefact and is excluded**: it read -66 MPa, and refining the grid settles
which it is - -25.4 / -53.1 / -66.0 MPa at nz 41 / 81 / 161 while the interior
converges to 2.8. A physical stress converges. Case 3 already excludes the same
node for the same stated reason. **The peak statistic is grid-noisy and is NOT
fixed**: the clause reads 3.07 / 10.42 / 7.44 MPa across the same grids, and a
grid-robust interior companion is printed beside it and explicitly not scored,
because changing a clause after watching it fail is moving the bar.

The plate is rectangular and this solver's cavity is a disc, so the case uses the
equal-VOLUME disc: Q and the fill time come out at the sourced values exactly and
the path length carries the error (59.7 mm against 80). `-semidia 40` runs the
alternative that gets the length right and Q 1.8x too high.

#### The thermal channel, as of 2026-08-18

**Stress accumulates INCREMENTALLY** - a layer is stress-free above Tg, becomes
elastic when it vitrifies, and every later cooling increment re-equilibrates over
the layers solid at that moment, so the total is force- and moment-balanced
without imposing it. The previous construction read the temperature profile at
one instant and removed its mean and linear parts; that is capped, since the
profile then is near a similarity solution and its crossing cannot move. On case
3 the incremental form took the surface from -9.5e-4 to -1.42e-3 and the ratio
from 1.76 to 2.64. `-snapshot` restores the old one.

**The post-vitrification increment is for FREE parts only.** Completing the
cooling after every layer is solid is essential to a quench - case 3's core reads
7.6e-7 without it - and wrong for a moulding, where the part is still adhered to
the cavity and cannot relieve in-plane, so the denied contraction is identical
for every layer and cancels at ejection. Including it put case 1's thermal share
at 26% of flow against a published 8%; excluding it gives 14%.

That cancellation was implemented as a constrained-then-released branch and
MEASURED: it returns identically zero, which refuted it as a general construction
and simultaneously justified the exclusion. Recorded in `memory/rejected.md`.

**Cooling after EJECTION contributes exactly zero, and that is a proof.** The
part is then entirely glassy - nothing vitrifies, no sub-Tg relaxation, one
modulus - and a linear-elastic body taken through a cycle that starts and ends
uniform has no residual stress. The condition is mould < Tg, which this tool
already ENFORCES (`FreezeHistory` refuses anything else), so the result is
unconditional rather than a property of the cases run. A warning for the
mould-above-Tg case was written and removed as dead code: with case 1 set to a
200 C mould it never printed, because the run fails earlier. The self-test
asserts the precondition instead, in both directions.

#### Open

- **Case 2's in-plane peak, ~3.6x LOW.** ~~12.87x high~~ - **retracted
  2026-08-18**: Fig. 7's y-axis in the source is mislabelled by 100x, and the
  paper's own dn = lambda\*N/h settles it without circularity (the printed axis
  needs a 79.6 mm sampling thickness on a 2 mm part; the corrected one needs
  0.796 mm, its stated 0.80 mm gate land). The DIRECTION of the failure inverted,
  so "the model over-predicts", melt-fracture shear stress as its cause, and
  "case 2 over-retains orientation" are all withdrawn and must not be requoted.
  Seven candidates were measured for the remainder and six refuted; the
  normal-stress term worked and lifted the ceiling from 0.49 to 1.35 of the gate,
  so for the first time the target is reachable. What is left is the retained
  fraction being LOW exactly where tau is HIGH.
- **The thermal channel's boundary condition in a moulding** - case 4 measures it
  at three to eight times the published residual stress, and the constrained-
  then-released branch is reopened for the during-solidification stage.
- **Case 3's Ti trend** - the elastic stress is dominated by a Ti-independent
  term, so the dependence must live in frozen-in ORIENTATION above Tg, which is a
  second thermal channel rather than a correction to this one.
- **The depth criterion may sample inside its reference's resolution.** Case 1
  samples 18.8 um from the wall and the ported shape peaks 52.5 um in; Flaman
  (1990) reports birefringence unresolvable within ~60 um of the surface, with the
  measured peak at ~165 um. The pass means "peaks in the outer quarter", not "gets
  the profile right".
- **Fountain-flow deposition is contested as the cause of skin orientation.** The
  transport is directly visualised, but a shear-only model reproduces the same
  profile shape, so a skin-peaked profile does not discriminate between the two.
- **Case 2 is not grid-converged** (first layer 34.5 / 33.4 / 32.4% at nz 41 / 81
  / 161) and stays at 161.
- **PMMA is the least trustworthy material row** - its stress-optical coefficient
  changes sign near 144 C while the model carries one constant across a range that
  straddles it.
- **Needs Bob:** click the ribbon entry once in the OpticStudio GUI. Everything
  here has run headless.


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
dotnet build extensions\DistortionTarget\DistortionTarget.csproj --configuration Release
```

Every project deploys itself. `ZemaxPaths.props` carries a `DeployToZemax` target
that runs after each build and copies the `.exe` and its `.exe.config` (which holds
the binding redirects) into the folder OpticStudio reads. The destination comes from
`HKCU\Software\Zemax@ZemaxRoot` — the same key Ansys's own ZOS-API boilerplate reads,
and the one OpticStudio rewrites when the data folder changes in preferences, so it
cannot pick the wrong tree on a machine where Documents is redirected to OneDrive.

Default destination is `{Zemax Data}\ZOS-API\Extensions\`. A project that is not a
user extension says so itself — `AthermalAnalysis` sets
`<ZemaxDeployKind>User Analysis</ZemaxDeployKind>` and lands in
`{Zemax Data}\ZOS-API\User Analysis\` instead. Build with `-p:ZemaxDeploy=false` to
skip deployment, or `-p:ZEMAX_DATA="C:\...\Zemax"` to target another data folder;
a destination that does not exist fails the build rather than passing quietly.

A newly added extension appears after **Programming > Refresh List**. User analyses
have no such button — restart OpticStudio for a new one. Replacing an add-in that is
already listed takes effect on its next run, with no refresh either way.

Ansys ships no deploy step of its own: the project template behind
**Programming > C#** leaves `OutputPath` at `bin\Release\` and its `AfterBuild`
target empty, so the copy is manual by their design.

## Licence

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Bob Householder.

That covers the source in this repository and nothing else. The extensions
**link against Ansys ZOS-API assemblies**, which are part of an OpticStudio
installation, are not included here, and are not covered by this licence. A
build output therefore carries Ansys components alongside MIT-licensed code —
so building and using the extensions is straightforward, but redistributing a
compiled `.exe` is a question about Ansys's terms, not about this one.

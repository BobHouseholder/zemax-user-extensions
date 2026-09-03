# Zemax OpticStudio User Extensions

ZOS-API user extensions for Ansys Zemax OpticStudio, built and validated against
OpticStudio 2026 R1.01. Each extension is a self-contained C# (.NET Framework 4.8)
console application. Compiled executables deploy to `{Zemax Data}\ZOS-API\Extensions\`
and appear under **Programming > User Extensions** in the OpticStudio ribbon; they can
also be run from a shell against a session waiting in
**Programming > Interactive Extension** mode.

Ribbon (GUI) runs report progress and results through OpticStudio's extension
progress display, and auto-open their report/image outputs when finished, since
the console window closes with the process (pass `-quiet` to disable the
auto-open). Tools that modify the system show their edits live in the editors.

**Terminate is honoured by five of the ten.** AthermalScan, DetectorDump,
EquivalentGlassFinder, LayoutRender and GpimGhostReduce poll `TerminateRequested` inside their
loops, so Cancel stops the run at the next iteration. CryoGlass,
DistortionTarget, MoldStress, ReverseSystem and the AthermalAnalysis window do
not reference it at all — pressing Cancel there does nothing and the run goes to
completion. That gap matters most on DistortionTarget and MoldStress, the two
longest-running of the set. OpticStudio's own template checks the flag once,
before your code runs, which is why checking it is not the same as honouring it.

## Extensions

### GpimGhostReduce

Implements the sequential half of
[Stray Light Analysis with Ghost Focus Generator](https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator)
(Sean Lin / Wilson Chen): rank double-bounce **image ghosts** (and optionally pupil
ghosts) with the `GPIM` operand, then append `GPIM` rows — target 0, existing merit
function left intact — so a later optimize pushes the ghost focus off the image plane
instead of sitting on it. OpticStudio defines GPIM as \(1/|z_{\mathrm{ghost}}-z_{\mathrm{image}}|\),
which is why the article targets zero.

It does **not** replace Ghost Focus Generator + Geometric Image Analysis. Those still
confirm the peak irradiance drop on the saved double-bounce file; this extension only
does the operand / optional DLS step. It also does not apply coatings or run NSC stray
light. Image ghosts are the default because that is what the article prioritises.

A ribbon run gets a settings dialog (last run remembered in
`%APPDATA%\GpimGhostReduce\lastrun.txt`). `Top N = 0` inserts one GPIM with
Surf1=Surf2=−1 so OpticStudio keeps tracking whichever pair is currently worst.

Options: `-mode image|pupil|both`, `-top N`, `-weight W`, `-optimize`, `-cycles K`
(0 = automatic DLS), `-nodialog`, `-file <zmx>`, `-save <zmx>`.

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

Reverses any sequential system in place — refractive or reflective, including
coordinate breaks, negative-thickness virtual propagation, fold mirrors and
double-pass Mangin elements, none of which the built-in Reverse Elements tool
handles (community threads:
[flip the whole system](https://community.zemax.com/got-a-question-7/how-to-flip-the-whole-optical-system-1367),
[Reverse elements erases materials](https://community.zemax.com/got-a-question-7/reverse-elements-erases-materials-3682)).

The reversed system is the mirror image traversed backwards (`rev(op) = M.op^-1.M`):
radii and polynomial sag terms negate, gaps reverse order keeping their signs,
materials ride with their gaps, and coordinate breaks negate decenter X/Y and tilt-Z,
keep tilt X/Y and flip the order flag. Beyond that:

- solves and pickups are frozen to their values first, so they cannot corrupt the
  rewrite; surface apertures travel with their surfaces
- the system aperture converts to Float By Stop Size with the physical stop
  semi-diameter preserved, so the reversed trace is the same physical bundle
- conjugates swap properly: real marginal ray fans in x and y, analysed separately
  with astigmatism detection, classify the original image space as collimated or
  converging, and the reversed spaces and afocal flag are set to match
- **reflective systems are fully supported** — MIRROR markers travel with their
  surfaces, interior gap signs multiply by (−1)^(mirror count), and for odd counts
  the operator becomes conjugation by the y-flip mirror, so reversed light still
  enters along +z
- unsupported surface types and multi-configuration systems with surface-referencing
  MCE operands are **refused with explicit messages**, not silently corrupted

Options: `-save`, `-keepconj`, `-refocus`, `-rayaim`, `-keepaperture`, `-georeport`,
`-file <path>`, `-out <path>`, `-quiet`.

Validated by exact double-reversal identity — LDE prescription and RMS spot restore
digit-for-digit — on 8 refractive coordinate-break systems and 10 reflective ones
(Cassegrains, catadioptrics, folds, off-axis and Yolo telescopes, a double-pass
Mangin), by global-geometry mirror-congruence checks via `-georeport`, and by loading
every reversed file in the GUI against the original.

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

Builds a chrome-on-glass dot distortion target in non-sequential mode: a glass plate
carrying a square grid of chrome dots, replicated by an **Array** object rather than
placed individually, which is what makes ~40,000 dots affordable. Defaults reproduce
[Edmund Optics 15963](https://www.edmundoptics.com/p/100-x-100mm-05mm-spacing-glass-distortion-target/15963/)
— 100 x 100 x 1.5 mm soda-lime, 0.250 mm dots on 0.500 mm pitch, reflective
first-surface chromium — and each was built and traced against criteria fixed before
the trace. A ribbon run gets a settings dialog, seeded from the last run, so
precedence is **explicit flag > last run > default**.

Options: `-n <int>`, `-pitch <mm>`, `-dot <mm>`, `-plate <mm>`, `-thick <mm>`,
`-material <name>`, `-coating <name>`, `-film <mm>`, `-drawlimit <int>`, `-rig`
(adds a collimated source and detector), `-save <path>`, `-file <path>`, `-nodialog`.

The dialog recomputes span, outermost dot edge, clearance, chrome fill and on-axis
occupancy on every keystroke, and **refuses to build while the corner dots overhang
the plate**. That guard exists because the failure is invisible in the inputs: reading
the vendor's "pattern size 100 x 100" as the grid span gives 201 dots whose outer edge
lands at 50.125 mm on a 100 mm plate. "201, 0.5, 100" looks reasonable; "clearance
−0.125 mm" does not.

Three ZOS-API behaviours here fail **silently** rather than raising:

- **The dot is a thin Cylinder Volume, not a flat disc.** Assigning a `Coating` to a
  single-face flat object (Annulus, Ellipse, Rectangle, CylinderPipe) is ignored — no
  exception, and the property still reads `None` afterwards, for any coating name.
  Only multi-face solids and PolygonObject accept one. A flat dot would block light
  while carrying no reflectance, precisely wrong for a part sold on its chrome. The
  coating is read back after being set and the build fails if it did not take.
- **Face 1 is the front face** (0 = sides, 2 = back). Coating the wrong one is exactly
  as silent as not coating at all.
- **Parameter cells are typed.** The Array's counts and draw limit are Integer cells
  and `DoubleValue` throws on them — from the getter too, so the type cannot be
  discovered by reading first. Every write dispatches on `cell.DataType`.

**The layout will not show you all the dots**, deliberately, and neither mechanism
affects a traced result. `Draw Limit` (Array parameter 20) caps how many elements are
*drawn* — OpticStudio defaults to 500, this uses 2000, so the stock target renders
2000 of 39,601 and the corners look empty; set `-drawlimit 39601` to draw them all.
And the parent dot carries *Do Not Draw* plus *Rays Ignore This Object*, because it is
a template the Array replicates, not a dot — without the latter it would double-count.

Raising the limit is slow: each dot is a solid Cylinder Volume, forced by the coating
limitation above, and 39,601 solids is a different proposition from 39,601 discs.
Prefer the wireframe **NSC 3D Layout** over **NSC Shaded Model**, and put the limit
back afterwards. Timings are not quoted because the ZOS-API is headless — `ToFile` on
a layout writes a text stub whatever extension you give it — so the render can only be
timed in the GUI and has not been.

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

One-command passive athermalization for a uniform-environment system, replacing the
manual TEMP/PRES multi-configuration setup (community thread
[athermal design](https://community.zemax.com/got-a-question-7/athermal-design-3623)).
It applies OpticStudio's thermal model transiently — indices via the environment,
radii/thicknesses/asphere terms via the glass catalog TCE, air gaps via the LDE mount
column — sweeps temperature and optionally pressure, restores the system even on
error, and reports focus shift / EFFL / RMS against T, the diffraction depth of focus
and athermal range, a ranked housing-material table (including negative-CTE ALLVAR),
an exact bimetallic mount length, a per-glass opto-thermal table, per-element defocus
shares, and a chart.

Validated against thin-lens theory on an f/4 germanium singlet at 10 µm:
dz/dT = −0.013274 against −f·x_f = −0.013292 lens units/K, **0.14%**. That case is
insensitive to the edge model below — plano rear face, flat image, back gap TCE 0, so
every sag term vanishes — so the edge model is measured separately on a Cooke triplet.

**Index convention.** OpticStudio traces *relative* index, so system pressure alone
decides whether reported n, dn/dT and x_f are relative-to-air or absolute. The
difference in dn/dT is n·|dn_air/dT|, ~1.4e-6/K at n = 1.5 and 1 atm — the whole value
for a low-dn/dT crown, enough to flip the sign of x_f. The convention in force is
printed; `-vacuum` / `-pressure` / `-psweep` select it. Absolute-index catalogs such as
CryoGlass's need `-vacuum`.

**Refuses rather than guessing** when the environment is not the scan's to own:
`TEMP`/`PRES` operands in the multi-configuration editor; value-computing solves on
the radii, thicknesses or parameters it must write (a marginal-ray-height solve
auto-refocuses and would report zero focus shift — `-freezesolves` freezes them); and
a file with *Adjust Index Data To Environment* off, where the stored temperature and
pressure are not the design environment — declare it with `-temp0` / `-pressure`.

**Non-glass gaps expand along the edge**, matching Make Thermal's pickup solves
exactly: the edge runs rim to rim, expands with the mount TCE, and is transferred back
onto the centre thickness, so a TCE of 0 still moves a gap when the adjacent radii do.
Two details come from measurement, not from manual §2.1.1.4.4.2, which is wrong about
both: the edge is measured at the **clear** semi-diameter (changing a mechanical one
from 14 to 20 with the clear held at 12 moves nothing), and there is **no
contact-point walk** — modelling the manual's migrating contact point leaves a ~0.85 µm
residual, while evaluating both sags at the same unexpanded height reproduces
OpticStudio to the last displayed digit. Verified at ΔT = 50 K across curved/plano
faces, mount TCE 23.6 and 0, and two mechanical semi-diameters: **air gaps agree to all
14 significant figures**.

It changes answers, not digits. On the Cooke triplet, whose air gaps carry TCE 0,
dz/dT goes +0.000260 → +0.000296 and the required housing CTE 4.32 → 4.92e-6/K — 14%,
redistributing about 5 mm between the two metals of the bimetallic mount.

Still short of Make Thermal: semi-diameters are not expanded, and length parameters
outside the even/odd asphere terms (toroidal and biconic radii, Zernike normalisation
radii) are not scaled. A gap bounded by a surface whose sag cannot be evaluated falls
back to centre scaling and is **named in the report** rather than silently differing.

**Outputs.** `_report.html` is the one to read — self-contained, inline SVG chart,
warnings as callouts — and the only file auto-opened after a ribbon run. `_sweep.csv`
and `_summary.json` exist so runs can be diffed; `_report.txt` and `_chart.png` remain
for anything already consuming them.

**Ribbon runs get a settings window**, because OpticStudio launches an extension with
no command line: sweep range and steps, design temperature and pressure (prefilled,
and flagged amber when *Adjust Index Data To Environment* is off), analysis pressure,
mount track and solve handling. Last run is remembered in
`%APPDATA%\AthermalScan\lastrun.txt`. `-nodialog` suppresses it, `-dialog` forces it.

Design point and scan environment are separate: `-temp0` / `-press0` declare what the
prescription was measured in, `-pressure` / `-vacuum` / `-psweep` what to analyse it
at. "Built in air, flown in vacuum" is `-press0 1 -vacuum`, and the resulting focus
step is reported as its own PRESSURE TERM line, separately from `dz/dT`.

Options: `-tmin/-tmax/-steps`, `-track L`, `-pressure P`, `-vacuum`, `-psweep P1:P2`,
`-temp0 T`, `-press0 P`, `-freezesolves`, `-out <prefix>`, `-outdir <dir>`,
`-file <path>`, `-quiet`.

### MoldStress

Estimates the refractive-index change and stress birefringence that injection moulding
leaves in the plastic elements of a sequential system, and applies both through
OpticStudio's STAR module so the change in optical performance can be read directly.

**REQUIRES AN OPTICSTUDIO ENTERPRISE LICENCE.** Ansys's own help states it plainly:
"To use the tools inside the STAR tab, you must have an Ansys Zemax OpticStudio
Enterprise-level license." STAR is how this tool delivers its result, so without
Enterprise it computes but cannot apply anything. That prerequisite went
undocumented here until 2026-08-20 and it narrows the audience considerably — the
tier below Enterprise cannot use this at all.

It also qualifies the premise below. An Enterprise seat is the top tier, quote-only,
well above the ~$5k–15k of Standard/Professional/Premium — so the user this was
written for has already bought Zemax's most expensive licence. And Moldex3D now
states it "allows users to directly export injection molding results to Ansys
Zemax" (Moldex3D 2025), so the mould-flow-to-Zemax path is no longer absent. The
real gap is narrower than "no mould-flow seat": an Enterprise owner, at concept
stage, before a moulder is engaged.

**ESTIMATE — not a mould-flow simulation, and not validated against a moulded part.**
That label is on every artifact it writes. It is held against four published reference
cases and does not clear all of them. Moldex3D's Optics add-on and Autodesk Moldflow
Insight solve this properly; MoldStress exists for the designer with OpticStudio and
STAR and no mould-flow seat.

Nothing is asked that the design does not already contain: the cavity profile comes
from the surface sag, so the fill solve needs no mesh, and a single edge gate at +Y
(ring gate above 12 mm semi-diameter) with a parting plane at the rim are defaults,
overridable per element. The sag reads the base radius, the conic, and even or odd
aspheric terms; surface types whose parameters it cannot interpret are refused rather
than silently flattened to a sphere.

Four stages, each held against a closed form by `-selftest` before the next depends
on it:

- **A1** Hele–Shaw pressure and shear, Cross-WLF viscosity, Tait EOS — against
  Poiseuille flow and the analytic log law for converging radial flow.
- **A2** freeze history across the full wall — against the erf isotherm *where that
  closed form is valid*. It is semi-infinite and overstates the core freeze time by
  10.8× on a 2 mm wall, which is why the numerics are the model and it is the control.
- **A3** three channels kept apart because they are physically apart: flow orientation
  through a viscoelastic memory integral (single Maxwell mode), thermal residual stress
  with force and moment balance imposed, and density through Lorentz–Lorenz.
- **A4** assembly. STAR accepts a *stress tensor* and applies the catalog's K11/K12
  itself, so frozen orientation — not a stress in the finished part — is converted to
  `σ = Δn / (K11 − K12)` with its principal axis along the local flow.

**A polymer catalog is a prerequisite.** No polymer OpticStudio ships carries a `BD`
record, and without one STAR does not refuse the data — it accepts zero points, returns
success and reports retardance exactly zero, indistinguishable from a well-moulded
part. `-writecatalog` writes them; **four of the five are PROVISIONAL**, representative
of a family rather than measured for a grade.

The depth distribution comes from a **Lagrangian particle model** (`-eulerian-depth`
turns it off), because the skin never sat at the wall — it was sheared in the hot core
and carried there by the front. **Fountain deposition is ON by default**, though its
source has since withdrawn the attribution that justified it; it is kept for measured
reasons rather than that one, and `-fountain 0` recovers the shear-only model.
Mechanism notes, catalog details and both retractions:
[`VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md).

#### MoldStress validation, and what currently fails

Four published reference cases, each with a criterion registered BEFORE it was
first run. Numbers below are read from the binary, not carried in prose.

| | what it tests | verdict at the shipped grid |
|---|---|---|
| `-refcase` | moulded plate, flow + thermal | **criterion MET**, grid-stable |
| `-refcase2` | moulded lens, layer-removal depth data | NOT met — in-plane peak ~3.6x low |
| `-refquench` | free quench, the THERMAL channel alone | **criterion MET** |
| `-refplate` | flow and thermal SEPARATED by the author | MET, 3 of 8 clauses non-discriminating |

**The time grid was the hidden axis.** Case 1's verdict used to flip — MET at
nz=41, NOT met at nz=161 — because the recorded cooling history was fixed at
`nt = 240` and did not refine with `nz`, so a sweep in `nz` alone was blind to it.
Exposed as `-nt` and raised to 960 on 2026-08-20:

| depth ratio | nt=240 | nt=480 | nt=960 |
|---|---|---|---|
| nz=41 | 3.43 MET | 3.42 MET | 3.42 MET |
| nz=81 | 2.24 MET | 3.27 MET | 3.33 MET |
| nz=161 | 2.80 **FAIL** | 3.32 MET | 3.38 MET |

At the new default the ratio is flat to 1.5% and MET at every grid. It costs
~10% runtime. It also moved published numbers — case 3's shape ratio 2.64 → 3.07 —
and those are corrected rather than kept: a number that changes when the grid is
made adequate was never the model's answer.

How each was arrived at, and every mechanism tried and rejected, is in
[`VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md). Candidate sources
and three literature sweeps are in
[`VALIDATION-SOURCES.md`](extensions/MoldStress/VALIDATION-SOURCES.md).

#### When this tool's answer is worth acting on

Worth acting on for a rotationally symmetric element close to a plate or a shallow
spherical lens, in a cyclo-olefin whose constants are measured rather than borrowed,
with a known gate and a mould temperature safely below Tg — and then as an
order-of-magnitude estimate of stress birefringence, not a number to set a tolerance
against.

**Not** worth acting on for: a **toroidal, biconic or Zernike** surface, whose shape
this solver cannot read at all; a **non-circular outline**, approximated as a disc; a material whose photoelastic constants are
catalogue-generic (four of the five are); a part whose dominant moulding risk is
**warpage or sink**, which is not modelled at all.

Since 2026-08-21 the run reports the two quantities under separate headings and ends
on the **polarisation** one, because they answer different questions and the last
number printed is the one people quote. It also states the ratio, warns whenever
retardance exceeds the wavefront change (a derived boundary, not a chosen threshold),
and says so explicitly when the birefringence could not be read at all while stress
WAS applied, which is the case where a real wavefront number stands alone and reads
as the result.

**THE 585× THAT STOOD HERE IS WITHDRAWN, 2026-08-29, AND SO IS THE 0.41 WAVES IT WAS
BUILT ON.** Its numerator came from `GetRetardanceMap`, which controls have now shown
does not return a retardance at all — see the Open list below. The lens it was
measured on is not available to re-run, so this is a retraction and not a correction:
the figure was computed by a route that fails six closed-form controls, and no claim
is made here about what that lens would read today. On the validation triplet the
ratio is **1513×** — a retardance bound of 1.2125 waves against 0.000802 waves of
RMS wavefront change. The qualitative claim, that the scalar understates a
polarisation-sensitive system by orders of magnitude, survives and is if anything
stronger. The specific number did not.

**AND THE FIRST REPLACEMENT FOR IT WAS ALSO WRONG.** For a few hours on
2026-08-29 this paragraph read **176×**, from 1.29522 waves against 0.007359.
Both halves were wrong, in opposite directions, so they partly cancelled and the
result looked unremarkable. Running the tool through the GUI is what caught it:

- the **numerator** was measured over an aperture the element does not have.
  Surfaces 3–4 have semi-diameters 2.640 and 2.391, and light must clear both, so
  the longest path is 1.729 mm at r = 2.391 — not the 1.847 mm at r = 2.640 that a
  scratch script computed from surface 3 alone. The tool had it right; the check
  written to verify the tool did not.
- the **denominator** was wrong in SIGN. The harness read the moulded wavefront as
  0.124818 waves against a 0.132177 baseline — moulding stress *improving* a lens
  by 5.6%, which should have been suspicious on its face and was not.

The tool's figure is now confirmed by three independent routes: a ribbon-deployed
binary attached to a live GUI session, the same binary standalone with `-file`,
and a scratch probe that reproduces either answer on demand — see the pin-order
entry in the Open list, because the mechanism is not what it looks like.

#### Questions this extension has already answered, and where

Four times in this project's history a conclusion was written that a file already
on disk contradicted — twice about what to buy, once about a mechanism's
reachability, once about which mechanism would close a failing case. **Read the
file that owns the question before writing a conclusion in its domain.**

| standing question | the file that owns it |
|---|---|
| Can the optical-memory / C_t rewrite reach its targets, and on what input? | `ct-reachability.py` |
| What is the measured τ(T) for PC, and where is it valid? | `tau-measured-pc.py` |
| Has this source been assessed, bought, priced or closed? | `VALIDATION-SOURCES.md` |
| What does each reference case measure, and what does it currently read? | `VALIDATION-LOG.md` |
| Why is a constant the value it is, and how well sourced? | the `KSource` / `CMeltSource` strings in `Polymers.cs` |

#### Open

- **The `-run` headline is not the moulding effect on a lens whose image plane is
  on a solve, and the direct-index route costs the material's dispersion.** Both
  found 2026-08-29 on a purpose-built all-plastic triplet (PMMA / POLYSTYR / PMMA,
  EFL 30 mm, F/4.5, ±9°, F-d-C; spherical, mouldable thicknesses, 1 mm flange). The
  run itself was clean — three surfaces converted, 1540 index points per element,
  every point accepted, exit 0 — and it reported **+1572%** wavefront, on-axis FFT
  MTF at 40 lp/mm falling 0.816 → 0.102. Measured against controls, the moulding
  effect is **0.0801 → 0.0836 waves** and worst-case ΔMTF at 40 lp/mm is **0.026,
  mixed in sign**. Two separable causes, neither of them moulding:
  1. **The file's marginal-ray-height solve moved the image plane 211 µm.** Paraxial
     EFFL reads 30.0096 → 29.7147 mm (−0.98%) with the index data loaded, and the
     solve follows it. **Real rays do not**: a through-focus scan on a shared 5 µm
     grid puts best focus at −25 µm in BOTH states, differing by 0.003 waves at the
     minimum. Pinning the plane collapses the reported change to nothing.
     ~~*The paraxial shift itself is unexplained and is the open item.*~~
     **ANSWERED 2026-08-29 BY A POSITIVE CONTROL ON STAR, and the answer is that
     the shift is not physics.** Chasing the mechanism inside the real field had
     not worked in three attempts, so instead STAR was fed a field whose paraxial
     answer is known in closed form — `n(r) = n₀ + a·r²`, on the same point set,
     through the same import path. A thin slab of that profile adds power
     `φ = −2at`, exactly linear in `a` by construction. STAR returns it
     **non-linearly, and by the same amount the real field does**:

     | | tenth/full | first order demands |
     |---|---|---|
     | synthetic analytic field | **0.0040** | 0.100 |
     | real moulding field | **0.0049** | 0.100 |

     So the non-linearity belongs to STAR's ingestion of a direct-index cloud,
     not to the moulding field, not to the catalogue and not to the physics. The
     earlier sign flip across the catalogue fix (0.155 → 0.065) needed no
     material explanation: it is what a response sitting in noise does.

     **The mechanism is a noise floor plus a GRIN-step dependence, both
     measured.** Sweeping the synthetic amplitude over six decades, `measured /
     closed form` runs 0.22, **−0.79**, 0.21, **−0.13**, 0.02, 3.20 below a peak
     Δn of ~1e-6 — it changes SIGN, so below that amplitude the response is
     noise rather than a small answer — and then settles at 0.82–0.91 from
     Δn ≈ 1e-5 upward and stays there for two decades. The real field's smooth
     quadratic component peaks at Δn = 4.8e-6, inside the transition. And the
     answer moves with the GRIN integration step, a producer-side constant the
     tool sets by heuristic: at that amplitude ΔEFFL is −0.0292 mm at step 1.0
     against −0.0032 mm at 0.50, a factor of **9**.
     **That last point retracts a convergence claim of this project's own.**
     "Identical to four decimals across GRIN steps 1.0 → 0.02, a 50× range" was
     established for the *index-shift metric on a NULL cloud* and is quoted
     below in exactly those terms — it does not hold for paraxial EFFL on a real
     field, and a convergence result belongs to the quantity it was measured on.
     Scripts: `validation/mtf-triplet/paraxctl.py`, `paraxsweep.py`.
     A separate instrument check: my own paraxial y-nu trace reproduces Zemax's
     unperturbed EFFL to 1.8e-16 relative, so the closed forms above are not
     resting on an unvalidated trace.
     **What is still open** is narrower and is Ansys's rather than ours: why the
     fit discards small index variation, and where exactly the usable floor sits
     for a general cloud.
     **RE-DERIVED 2026-08-29 AFTER THE CATALOGUE FIX, and the catalogue is ruled
     out.** The whole three-arm probe was re-run at `265e826`, because its original
     evidence was taken against the inverted-dispersion rows. The shift is
     unchanged to three digits: FULL moves EFFL −0.987% against −0.983% before,
     and the NULL arm still moves nothing. The exported field is materially the
     same: I claimed byte-identical and CHECKED, and it is not — the corrected
     dispersion moves ray heights slightly, so the automatic semi-diameters and
     hence the export grid move with them (up to 12.6 um radially on element 3,
     0.2% of its radius). But the Δn VALUES change by at most 1.6e-7, which is
     0.02%, 0.09% and 0.51% of each element's field span. The anomaly acts on
     the same field to within half a percent.
     **AND THE TOOL NOW MEASURES THIS ON EVERY RUN**, since `e4b4110`, so the
     question is asked of the user's own lens rather than quoted at them from
     these two. `-run` scans real-ray best focus before and after — both at the
     d-line, both centred on the design plane — and prints the real shift beside
     the solve's. On the Cooke: −75 µm before, −75 µm after, against the solve's
     −294 µm, i.e. the solve chases a paraxial shift real rays do not follow at
     all (0%).
  2. **STAR's DirectRefractiveIndex route applies one index at every wavelength.**
     StarFiles writes absolute `Nd + dn`, i.e. the d-line, so the element loses its
     own dispersion. Isolated by a NULL cloud (`n = Nd` everywhere, physically a

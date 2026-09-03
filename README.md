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
and [How do I output the image of an analysis in ZOS-API?](https://community.zemax.com/got-a-question-7/how-to-output-the-image-of-an-analysis-in-zos-api-1011) -
the ZPL EXPORTJPG workaround only works in interactive mode). LayoutRender draws
the 2D Y-Z layout headlessly and writes a PNG: surface cross-sections are sampled
from the sag equations and mapped to global coordinates via GetGlobalMatrix
(coordinate breaks and tilts handled naturally), lens elements are closed over
glass gaps, and per-field colour-coded ray fans are traced with the batch ray
tracer and terminated where rays fail. The drawing is auto-oriented: a
principal-component fit of the traced ray points rotates the view so folded and
til ted systems (fold mirrors, Yolo telescopes, off-axis designs) render along
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

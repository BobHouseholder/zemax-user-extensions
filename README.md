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

**Terminate is honoured by four of the nine.** AthermalScan, DetectorDump,
EquivalentGlassFinder and LayoutRender poll `TerminateRequested` inside their
loops, so Cancel stops the run at the next iteration. CryoGlass,
DistortionTarget, MoldStress, ReverseSystem and the AthermalAnalysis window do
not reference it at all — pressing Cancel there does nothing and the run goes to
completion. That gap matters most on DistortionTarget and MoldStress, the two
longest-running of the set. OpticStudio's own template checks the flag once,
before your code runs, which is why checking it is not the same as honouring it.

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
overridable per element. **Only base radius is read — aspheres get a spherical proxy.**

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

**Not** worth acting on for: an **aspheric or toroidal** surface, where only the base
radius is read and a spherical proxy is silently substituted; a **non-circular
outline**, approximated as a disc; a material whose photoelastic constants are
catalogue-generic (four of the five are); a part whose dominant moulding risk is
**warpage or sink**, which is not modelled at all; or on the strength of the final RMS
wavefront delta rather than the per-element **peak retardance** — on the one real lens
tested those differ by 585x.

#### Open

- **`nt = 240` is fixed and does not refine with `nz`**, so a convergence sweep in
  `nz` alone proves nothing. This is the root cause of case 1's grid-dependent
  verdict and it invalidates most convergence claims until re-taken in the
  (`nz`, `nt`) plane.
- **Only base radius is read** — no conic, no aspheric terms. Aspheres get a
  spherical proxy, silently.
- **Case 2 is ~3.6x low on its in-plane peak** and not grid-converged.
- **No frozen-in thermal ORIENTATION channel.** Two independent cases implicate it;
  it is the largest known physics gap. The optical-memory machinery for it is built
  and tested but not wired — see `ct-reachability.py` and `tau-measured-pc.py`.
- **The flow law is a recognised but dated simplification** (shear-stress-driven with
  a Maxwell memory, Kamal & Tan-era) against a field that has used full viscoelastic
  tensors since Baaijens 1991. Packing-stage flow orientation is not modelled.
- **PMMA is the least trustworthy row** in the material table — its stress-optical
  coefficient changes sign near 144 C while one constant is carried across it.
- **Needs Bob:** click the ribbon entry once in the GUI. Everything here ran headless.

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

## Releases

A built zip is committed at [`dist/zemax-user-extensions-2026R1.03.zip`](dist/) so the
add-ins install without a .NET SDK. Extract it, read `INSTALL.txt`, and drag the
`ZOS-API` folder onto your Zemax **data** folder — not into `Extensions`, where an
Ansys extension zip goes but this one does not: it carries two destinations, because
one of the nine is a User Analysis. (Ansys ships its own CODE V Converter as a zip
the same way, which is why the format is this one.)

Each zip holds only our `.exe` and `.exe.config` files, `INSTALL.txt`, and a
`manifest.txt` naming the source commit, the OpticStudio release compiled against and
a SHA-256 per file. `tools\pack.ps1` builds it and refuses a dirty tree, an Ansys
binary, or an executable carrying a build-machine path.

**Re-run the packer whenever the binaries change** — a stale zip looks exactly like a
fresh one. Check `Source commit` in `manifest.txt` against this repository's history.

Three caveats: the binaries are **unsigned** (Ansys signs theirs; Windows may block
ours — `INSTALL.txt` gives the `Unblock-File` line); they were **compiled against one
OpticStudio release**, and while the ZOS-API assemblies resolve against your own
installation at run time, a withdrawn member can still fail; and **redistribution
terms are Ansys's to define**, not this repository's. Building from source avoids all
three — see [Building](#building).

## Licence

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Bob Householder.

That covers the source in this repository and nothing else. The extensions
**link against Ansys ZOS-API assemblies**, which are part of an OpticStudio
installation, are not included here, and are not covered by this licence. A
build output therefore carries Ansys components alongside MIT-licensed code —
so building and using the extensions is straightforward, but redistributing a
compiled `.exe` is a question about Ansys's terms, not about this one.

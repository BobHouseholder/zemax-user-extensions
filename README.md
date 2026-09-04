# Zemax OpticStudio User Extensions

ZOS-API add-ins for Ansys Zemax OpticStudio 2026 R1.01. Each is a self-contained
C# (.NET Framework 4.8) console app. Build output deploys to
`{Zemax Data}\ZOS-API\Extensions\` (or `User Analysis\` for AthermalAnalysis)
and appears under **Programming > User Extensions**. They also run from a shell
against **Programming > Interactive Extension**. Per-tool details live in
`extensions/<Name>/README.md`.

Ribbon runs report through OpticStudio's progress display and auto-open
report/image outputs (`-quiet` disables that). Tools that edit the system show
the edits live.

**Terminate is honoured by six of the eleven.** AthermalScan, DetectorDump,
EquivalentGlassFinder, LayoutRender, GpimGhostReduce and FootprintDxf poll
`TerminateRequested` inside their loops. CryoGlass, DistortionTarget, MoldStress,
ReverseSystem and the AthermalAnalysis window do not — Cancel does nothing there. That gap matters
most on DistortionTarget and MoldStress. OpticStudio's template checks the flag
once before your code runs, which is why checking it is not the same as honouring
it.

## Extensions

### GpimGhostReduce

Sequential half of
[Stray Light Analysis with Ghost Focus Generator](https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator):
rank double-bounce image ghosts (optional pupil ghosts) with `GPIM`, then append
`GPIM` rows (target 0) to the **existing** MFE so a later DLS push the ghost
focus off the image. Does not replace Ghost Focus Generator + Geometric Image
Analysis, apply coatings, or run NSC stray light.

`TopN=0` is auto: keep pairs covering ~80% of total GPIM, drop anything under
10% of the worst hit, cap 8 (`-top N` overrides). Default `-balance 1` scales
new weights so ghost pull matches existing MF performance. Empty/unweighted MF
skips DLS. Ribbon settings are remembered in `%APPDATA%\GpimGhostReduce\lastrun.txt`.

Options: `-mode image|pupil|both`, `-top N`, `-balance B`, `-weight W`,
`-optimize`, `-cycles K` (0 = automatic DLS), `-nodialog`, `-file <zmx>`,
`-save <zmx>`.

### EquivalentGlassFinder

Community request
["Equivalent Glass" Feature Proposal](https://community.zemax.com/got-a-question-7/equivalent-glass-feature-proposal-881):
closest catalog glass by weighted (nd, vd, dPgF), ranked candidates, optional
swap, before/after EFFL / MF / RMS. Default is obsolete glasses in catalogs in
use; `-catalog NAME` converts the whole design to that vendor.

Options: `-catalog NAME`, `-includeObsolete`, `-report`, `-reopt`, `-save`,
`-top N`, `-wnd/-wvd/-wpgf`, `-quiet`.

### ReverseSystem

Reverses a sequential system in place — refractive or reflective, including
coordinate breaks, negative-thickness virtual gaps, folds and double-pass Mangin
elements, which built-in Reverse Elements does not
([flip the whole system](https://community.zemax.com/got-a-question-7/how-to-flip-the-whole-optical-system-1367),
[Reverse elements erases materials](https://community.zemax.com/got-a-question-7/reverse-elements-erases-materials-3682)).

Radii and polynomial sag negate; gaps reverse order keeping signs; materials ride
with gaps; coordinate breaks negate decenter X/Y and tilt-Z. Solves/pickups
freeze first. System aperture becomes Float By Stop Size. Conjugates swap from
real marginal-ray fans. Odd mirror counts conjugate by the y-flip so reversed
light still enters along +z. Unsupported surface types and surface-referencing
MCE operands are **refused**, not silently corrupted.

Validated by exact double-reversal identity (LDE + RMS) on 8 refractive
coordinate-break systems and 10 reflective ones.

Options: `-save`, `-keepconj`, `-refocus`, `-rayaim`, `-keepaperture`,
`-georeport`, `-file <path>`, `-out <path>`, `-quiet`.

### LayoutRender

Headless 2D Y-Z layout PNG — the ZOS-API cannot save layout windows
([layout exports](https://community.zemax.com/got-a-question-7/feature-request-layout-window-exports-2244)).
Sag sampled and mapped via `GetGlobalMatrix`; glass gaps closed; per-field ray
fans from the batch tracer. A PCA of traced points orients folded/tilted systems;
purely axial systems are never rotated (`-noorient` forces that off). Decentered
apertures are drawn at their true offsets.

Options: `-out <path.png>`, `-rays N` (default 7), `-width W -height H`,
`-noorient`, `-file <path>`, `-quiet`.


### FootprintDxf

Exports the envelope of beam footprints on sequential surfaces to a CAD DXF
(R12 ASCII). Forum:
[export beam footprints to CAD/DXF](https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991).
Pupil-grid batch trace → local (x,y) hits → convex hull → one closed
POLYLINE per surface layer. LayoutRender (layout PNG) and DetectorDump (NSC
detectors) do not replace this. System is not modified.

Options: `-out <path.dxf>`, `-rays N` (default 21), `-surfaces all|1,3|1-6`,
`-includeimage`, `-fields all|1,2`, `-wave primary|all`, `-rim`, `-file`,
`-quiet`, `-nodialog`.

### DistortionTarget

Chrome-on-glass dot target in NSC: a plate plus an **Array** of chrome dots
(~40k affordable). Defaults match
[Edmund Optics 15963](https://www.edmundoptics.com/p/100-x-100mm-05mm-spacing-glass-distortion-target/15963/).
Ribbon dialog (explicit flag > last run > default) refuses builds whose corner
dots overhang the plate.

Three silent ZOS-API traps: a coating on a single-face flat is ignored (dots are
thin Cylinder Volumes; Face 1 is the front; the coating is read back); Array
count cells are Integer (`DoubleValue` throws); `Draw Limit` caps *drawn*
replicas only (default here 2000 of 39601). Radiometry needs **ray splitting on**.

Options: `-n`, `-pitch`, `-dot`, `-plate`, `-thick`, `-material`, `-coating`,
`-film`, `-drawlimit`, `-rig`, `-save`, `-file`, `-nodialog`.

### DetectorDump

Exports every NSC detector in one pass: native `.DDR/.DDC/.DDP/.DDV`, CSV pixel
grid, false-colour PNG, plus a flux/peak/hit table. Optional NSC trace first.

Options: `-dir <folder>`, `-trace` (`-nosplit`/`-noscatter`/`-nopol`),
`-data N` (0 flux / 1 irradiance / 2 intensity), `-log`, `-nocsv`/`-nopng`/`-nonative`,
`-file <path>`, `-quiet`.

### AthermalScan

Passive athermalization for a uniform-environment system, replacing a manual
TEMP/PRES multi-config
([athermal design](https://community.zemax.com/got-a-question-7/athermal-design-3623)).
Applies OpticStudio's thermal model transiently, sweeps T (optional P), restores
the system even on error, and reports focus shift / EFFL / RMS, diffraction DOF,
ranked housing materials, bimetallic mount length, per-glass opto-thermal table
and a chart.

Refuses rather than guessing when TEMP/PRES already live in the MCE, when
value-computing solves sit on radii/thicknesses it must write (`-freezesolves`
freezes them), or when *Adjust Index Data To Environment* is off without
`-temp0`/`-pressure`. Absolute-index catalogs (CryoGlass) need `-vacuum`.
Non-glass gaps expand along the **clear** semi-diameter edge — Make Thermal's
pickup model, including TCE 0 moving a gap when adjacent radii change. Air gaps
on a Cooke triplet agree with OpticStudio to 14 significant figures at ΔT = 50 K.
Semi-diameters and non-asphere length parameters are still not scaled.

`-outdir` is honoured even when `-out` is also set (the folder + the `-out`
stem). Ribbon settings in `%APPDATA%\AthermalScan\lastrun.txt`. Companion
**AthermalAnalysis** is the User Analysis window for the same sweep.

Options: `-tmin/-tmax/-steps`, `-track L`, `-pressure P`, `-vacuum`,
`-psweep P1:P2`, `-temp0 T`, `-press0 P`, `-freezesolves`, `-out <prefix>`,
`-outdir <dir>`, `-file <path>`, `-quiet`, `-nodialog`, `-dialog`.

### MoldStress

Estimates moulded Δn and stress birefringence in sequential plastic elements and
applies both through STAR. **Requires OpticStudio Enterprise** — without STAR it
computes but cannot apply. It is an **estimate**, not a mould-flow run and not
validated against a moulded part. Moldex3D / Moldflow solve this properly; this
exists for an Enterprise seat at concept stage with no mould-flow licence.

Cavity geometry comes from sag (radius, conic, even/odd asphere); unreadable
surface types are refused. A polymer catalog with `BD` records is required
(`-writecatalog`; four of five entries are provisional). Default: edge gate at
+Y (ring above 12 mm), parting at the rim.

Four published ref cases; `-refcase2` does not meet its criterion. The 585× and
176× retardance/wavefront ratios previously quoted here are **withdrawn** —
`GetRetardanceMap` is not retardance. Diary and both retractions:
[`VALIDATION-LOG.md`](extensions/MoldStress/VALIDATION-LOG.md).

Exit codes on `-run`: **0** every element applied, **66** mixed, **65** nothing
applied, 64 usage. Worth acting on for a rotationally symmetric plate-like or
shallow spherical element in a measured cyclo-olefin; not for toroidal/biconic/
Zernike surfaces, non-circular outlines, warpage or sink.

Options: see [extensions/MoldStress](extensions/MoldStress) (`-run`, `-full`,
`-selftest`, `-writecatalog`, `-refcase` / `-refcase2` / `-refquench` / `-refplate`).

### CryoGlass

NASA GSFC **CHARMS** cryogenic n(λ,T) (Leviton & Frey Sellmeier, ~20–300 K,
Si 1.1–5.6 µm and Ge 1.9–5.5 µm) frozen at working temperature T0 into an `.AGF`
with exact Sellmeier1 coefficients plus a local Schott thermal fit. OpticStudio
cannot override index computation; the catalog is the workaround. Indices are
**absolute (vacuum)** — set pressure 0. TCE is written 0 (CHARMS has none).

Self-test vs the papers' measured tables runs before every generation and
refuses on disagreement. Out-of-range λ/T is refused; nothing is extrapolated.
CHARMS options, validation and Building/Releases/Licence:
[docs/catalog.md](docs/catalog.md),
[VALIDATION.md](extensions/CryoGlass/VALIDATION.md).

Options: `-temp T` (K; no OpticStudio needed), `-range T1:T2:N`,
`-materials "SI,GE"`, `-fitbox K`, `-out <agf>`, `-file <zmx>`, `-selftest`,
`-quiet`.

## Building

Needs the .NET SDK and an OpticStudio install. `ZemaxPaths.props` sets
`ZEMAX_ROOT`; ZOSAPI is `Private=false` and resolved at run time.

```
Get-ChildItem extensions -Filter *.csproj -Recurse -Depth 1 |
    ForEach-Object { dotnet build $_.FullName --configuration Release }
```

That is ten User Extensions plus the AthermalAnalysis User Analysis. csproj
defaults stay **x64**. An x86 ribbon listing (needed on OpticStudio 2026 R1.01
here) is an override, not a project edit:

```
... dotnet build $_.FullName --configuration Release -p:PlatformTarget=x86
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack.ps1 -x86
```

`DeployToZemax` copies `.exe` + `.exe.config` to the Zemax data folder after
each build (`HKCU\Software\Zemax@ZemaxRoot`). A new extension may need
**Programming > Refresh List and an OpticStudio restart** — Refresh List alone
did not list an x64 `GpimGhostReduce` until an x86 rebuild plus restart. User
analyses always need a restart. Replacing an already-listed add-in takes effect
on the next run.

## Releases

A zip at [`dist/`](dist/) installs without a SDK: extract, read `INSTALL.txt`,
drag the `ZOS-API` folder onto the Zemax **data** folder (not into `Extensions`
— one of the eleven is a User Analysis). Unsigned; `INSTALL.txt` has `Unblock-File`.
`tools\pack.ps1` refuses a dirty tree, an Ansys binary, or a build-machine path.
Re-pack whenever binaries change. Redistribution of compiled `.exe` files is
Ansys's terms, not this MIT licence.

## Licence

MIT — [LICENSE](LICENSE). Copyright (c) 2026 Bob Householder. Covers this
repository's source only. The extensions link against Ansys ZOS-API assemblies,
which are not included and are not under this licence.

# Zemax OpticStudio User Extensions

ZOS-API user extensions for Ansys Zemax OpticStudio, built and validated against
OpticStudio 2026 R1.01. Each extension is a self-contained C# (.NET Framework 4.8)
console application. Compiled executables deploy to `{Zemax Data}\ZOS-API\Extensions\`
and appear under **Programming > User Extensions** in the OpticStudio ribbon; they can
also be run from a shell against a session waiting in
**Programming > Interactive Extension** mode.

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
`-top N`, `-wnd/-wvd/-wpgf` (distance weights).

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
`-georeport`, `-file <path>` (headless batch mode), `-out <path>`.

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
`-noorient`, `-file <path>` (headless batch mode).

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
`-file <path>` (headless batch mode).

### AthermalScan

One-command passive athermalization analysis, replacing the manual TEMP/PRES
multi-configuration workflow (community threads
[athermal design](https://community.zemax.com/got-a-question-7/athermal-design-3623),
[groups under different temperatures](https://community.zemax.com/got-a-question-7/how-to-model-a-system-with-groups-under-different-temperatures-and-pressures-2670)).
Applies OpticStudio's thermal model transiently (indices via the environment,
radii/thicknesses/asphere terms expanded with the glass catalog TCE, air gaps
with the LDE TCE mount column), sweeps temperature, fully restores the system,
and reports: focus shift / EFFL / RMS (fixed and refocused) vs T, the
diffraction depth of focus and fixed-plane athermal temperature range, the
required housing CTE with a ranked table of real housing materials (including
negative-CTE ALLVAR) and their usable ranges, an exact bimetallic mount length
solution, a per-glass opto-thermal table (n, measured dn/dT, TCE, thermal glass
constant x_f), approximate per-element thermal defocus shares, and a two-panel
PNG chart. Validated against thin-lens theory on a germanium singlet
(dz/dT = -f*x_f within 2%).

Options: `-tmin/-tmax/-steps`, `-track L` (mount length), `-out <prefix>`,
`-file <path>` (headless batch mode).

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

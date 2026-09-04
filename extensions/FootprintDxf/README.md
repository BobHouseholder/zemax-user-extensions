# FootprintDxf

ZOS-API User Extension. Exports the **envelope of beam footprints** on sequential
surfaces to a CAD DXF file (R12 / AC1009 ASCII).

Forum ask:
[How can I export beam footprints to a CAD or DXF file](https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991)
(Harvey.Spencer) — OpticStudio has no built-in path from a footprint diagram to
mech CAD.

**LayoutRender** only writes a 2D layout PNG. **DetectorDump** only dumps NSC
detectors. Neither replaces this. There is no ZOS-API DXF export; the DXF is
written as text. A **PNG preview** of the same polylines is written beside the
DXF by default (skip with `-nopng`) and auto-opened in Plugin mode like
LayoutRender.

## What it does

For each selected surface (default: optical surfaces `1 .. image-1`):

1. Batch-trace a pupil **grid** plus a **dense pupil rim** of real rays for every
   chosen field and wavelength (`OpenBatchRayTrace` / `CreateNormUnpol`, same
   pattern as LayoutRender). The rim is always included in the main hit cloud
   (default count `max(128, Rays×8)`, override with `-rimrays N`, clamp 16..1024).
   A second near-edge ring at radius 0.99 uses the same angular count.
2. Collect intercept `(x, y)` in the **local surface coordinate system** for rays
   that hit (error and vignette codes ignored).
3. Compute the 2D convex hull (Andrew’s monotone chain) of **grid ∪ rim** hits.
   That hull is the footprint envelope. With a dense rim, circular/elliptical
   footprints keep many hull verts instead of a chunky ~12–16-gon from the grid
   alone. The grid still supplies corner hits for vignetted / non-circular
   apertures.
4. Optionally also write separate pupil-rim polylines as `RIM_…` layers (`-rim`),
   ordered by angle around the centroid (not re-hulled).

One DXF **LAYER** per surface (`SURF_N`, or the surface Comment when set). One
closed `POLYLINE` + `VERTEX` + `SEQEND` per hull (ancient-CAD friendly; not
LWPOLYLINE). Coordinates are local XY in OpticStudio **lens units** (usually mm;
`$INSUNITS` is set to millimetres). The same envelopes are also drawn to a PNG
beside the DXF (white background, equal aspect, ACI-ish colours, layer legend).
The optical system is **never modified**.

Empty hull (no hits) → WARNING and that surface is skipped; other surfaces still
write.

Sequential systems only — NSC is refused with a clear message.

## Ribbon

Settings dialog when launched with no options / Plugin mode. Last run remembered
in `%APPDATA%\FootprintDxf\lastrun.txt`. Cancel leaves the system untouched.
Progress via `ProgressMessage` / `ProgressPercent`; `TerminateRequested` is
honoured in the ray loops.

The rim checkbox means “also write separate `RIM_…` layers”; the dense rim is
always used for the main SURF hull either way. The dialog has a rim-rays field.
A PNG preview is written beside the DXF by default (`-nopng` to skip).

## Build

```
dotnet build extensions\FootprintDxf\FootprintDxf.csproj --configuration Release
```

Then **Programming > Refresh List** (restart may be required on first deploy).

## Options

| Flag | Meaning |
|------|---------|
| `-out <path.dxf>` | Output path (default: `<lens>_footprints.dxf` beside the lens) |
| `-file <zmx>` | Standalone: load file (no dialog) |
| `-rays N` | Pupil grid density, odd (default 21) |
| `-rimrays N` | Dense rim sample count (default `max(128, Rays×8)`; clamp 16..1024). Always merged into the main hull |
| `-surfaces all\|1,3,5\|1-6\|Comment` | Surfaces (default `all` = 1..image-1) |
| `-includeimage` | Also include the image surface when `-surfaces all` |
| `-fields all\|1,2` | Fields (default all) |
| `-wave primary\|all` | Wavelengths (default all) |
| `-rim` | Also write separate pupil-rim polylines as `RIM_…` layers |
| `-nopng` | Skip writing the PNG preview beside the DXF |
| `-quiet` | Do not auto-open DXF/PNG after a ribbon run (files still written) |
| `-nodialog` | Skip settings dialog in plugin mode |
| `-selftest` | Convex-hull + ring-order self-check only (no OpticStudio) |

## How to run

```
FootprintDxf.exe -file C:\designs\cooke.zmx -out C:\designs\cooke_footprints.dxf -rays 21
```

Or open the lens in OpticStudio and run **Programming > User Extensions >
FootprintDxf**.

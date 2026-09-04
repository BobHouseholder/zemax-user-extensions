# FootprintDxf

ZOS-API User Extension. Exports the **envelope of beam footprints** on sequential
surfaces to a CAD DXF file (R12 / AC1009 ASCII).

Forum ask:
[How can I export beam footprints to a CAD or DXF file](https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991)
(Harvey.Spencer) — OpticStudio has no built-in path from a footprint diagram to
mech CAD.

**LayoutRender** only writes a 2D layout PNG. **DetectorDump** only dumps NSC
detectors. Neither replaces this. There is no ZOS-API DXF export; the file is
written as text.

## What it does

For each selected surface (default: optical surfaces `1 .. image-1`):

1. Batch-trace a pupil grid of real rays for every chosen field and wavelength
   (`OpenBatchRayTrace` / `CreateNormUnpol`, same pattern as LayoutRender).
2. Collect intercept `(x, y)` in the **local surface coordinate system** for rays
   that hit (error and vignette codes ignored).
3. Compute the 2D convex hull (Andrew’s monotone chain). That hull is the
   footprint envelope.
4. Optionally also write denser pupil-rim samples as `RIM_…` layers (`-rim`).

One DXF **LAYER** per surface (`SURF_N`, or the surface Comment when set). One
closed `POLYLINE` + `VERTEX` + `SEQEND` per hull (ancient-CAD friendly; not
LWPOLYLINE). Coordinates are local XY in OpticStudio **lens units** (usually mm;
`$INSUNITS` is set to millimetres). The optical system is **never modified**.

Empty hull (no hits) → WARNING and that surface is skipped; other surfaces still
write.

Sequential systems only — NSC is refused with a clear message.

## Ribbon

Settings dialog when launched with no options / Plugin mode. Last run remembered
in `%APPDATA%\FootprintDxf\lastrun.txt`. Cancel leaves the system untouched.
Progress via `ProgressMessage` / `ProgressPercent`; `TerminateRequested` is
honoured in the ray loops.

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
| `-surfaces all\|1,3,5\|1-6\|Comment` | Surfaces (default `all` = 1..image-1) |
| `-includeimage` | Also include the image surface when `-surfaces all` |
| `-fields all\|1,2` | Fields (default all) |
| `-wave primary\|all` | Wavelengths (default all) |
| `-rim` | Also write denser pupil-rim polylines |
| `-quiet` | Do not auto-open the DXF after a ribbon run |
| `-nodialog` | Skip settings dialog in plugin mode |
| `-selftest` | Convex-hull self-check only (no OpticStudio) |

## How to run

```
FootprintDxf.exe -file C:\designs\cooke.zmx -out C:\designs\cooke_footprints.dxf -rays 21
```

Or open the lens in OpticStudio and run **Programming > User Extensions >
FootprintDxf**.

# RayExtentEnvelope

ZOS-API User Extension. Shows the **maximum radial extent of rays** through a
sequential system - extreme fields times a pupil-rim sample set - as:

1. **PNG** - 2D Y-Z outline of glass elements and the stop, plus the max-ray
   radial envelope (+R / -R vs global Z)
2. **STEP** - faceted BREP solids of revolution for each glass element, plus a
   solid-of-revolution of the radial envelope ("max ray cone")

The optical system is **never modified** and **never saved**.

## Method

- Extreme fields = every field whose radial magnitude equals the system max
  field (primary wavelength only).
- Pupil rim at r=1 (default 48 samples; `-rimrays N`, clamp 16..256). No dense
  interior pupil grid is used for drawing.
- At each envelope station, batch-trace those rays, map hits with
  `GetGlobalMatrix`, then set
  **R = max(ray_envelope_r, clear_aperture/CLAP, field_height_at_that_plane)**
  so vignette cannot pinch the keep-out below mechanical CA (e.g. phone/image
  plane). Paraxial/phone stations with floating DIAM inherit CLAP from the
  previous drawn glass surface.
- **Drawn:** glass spans (non-empty material) and the stop aperture. **Skipped:**
  CoordinateBreaks and dummy flat air surfaces with no optical power/material.

STEP generation is pure C# (AP214 `FACETED_BREP`); no Python helper.

## Default stations (`-surfaces auto`)

1. **Object (surface 0)** is included when the object plane is finite (finite
   conjugates) so the keep-out starts at the object. Infinite-conjugate object
   planes (Z ~ 1e10) are skipped.
2. Drawn optical surfaces (glass + stop) through **AutoLastStation**.
3. **AutoLastStation** = last Paraxial/ParaxialXY before the formal image when
   unused surfaces follow it (post-image flare / dummy air) — e.g. Concept-24
   through S9 (phone), excluding S10+S11. Otherwise the formal image surface
   (typical Cooke-style objectives).

Override with `-surfaces all` or an explicit list/range (`0,2,4` or `0-9`).
Surface 0 is allowed in explicit lists.

## Options

| Flag | Meaning |
|------|---------|
| `-file <zmx>` | Standalone: load this lens |
| `-out <base\|dir>` | Output base path or directory (writes `<base>.png` / `.step`) |
| `-png` / `-step` | If either is set, only those outputs are written; otherwise both |
| `-rimrays N` | Pupil rim samples (default 48) |
| `-surfaces auto\|all\|0,2,4\|0-9` | Envelope stations (default `auto`; see above) |
| `-width W` `-height H` | PNG size (default 1400x900) |
| `-nodialog` | Accepted (Phase 1 has no settings dialog) |
| `-quiet` | Do not auto-open outputs after a ribbon run |

## Build / deploy

```bat
dotnet build -c Release -p:ZemaxDeploy=false
```

Copy `bin\Release\RayExtentEnvelope.exe` (+ `.config` if present) to
`{ZemaxRoot}\ZOS-API\Extensions\` and Unblock. Or build without
`-p:ZemaxDeploy=false` to auto-deploy via `ZemaxPaths.props`.

## Smoke (Cooke)

```bat
copy /Y "...\Samples\Sequential\Objectives\Cooke 40 degree field.zmx" C:\Temp\Cooke_RayExtent.zmx
RayExtentEnvelope.exe -file C:\Temp\Cooke_RayExtent.zmx -out C:\Temp\Cooke_RayExtent -nodialog -quiet
```

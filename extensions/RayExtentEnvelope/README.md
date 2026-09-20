# RayExtentEnvelope

ZOS-API User Extension. Shows the **maximum radial extent of rays** through a
sequential system - extreme fields times a pupil-rim sample set - as:

1. **PNG** - 2D Y-Z outline of glass elements and the stop, plus the max-ray
   radial envelope (+R / -R vs global Z)
2. **STEP** - one assembly with named products:
   - `KEEP_OUT` — ray-extent envelope (max-ray cone)
   - `L1`, `L2`, `L3`, … — **full MEMA** lens solids (MechanicalSemiDiameter blank;
     optical faces to CLAP, flange to MEMA). **Not** old CLAP-stub `LENS_*` naming.
   Default = KEEP_OUT + MEMA lenses together. Pass `-envelopeOnly` for KEEP_OUT only.
   Frame is **object-at-0** (finite object shifted to Z=0), matching Concept-24
   `KEEP_OUT_plus_L1L2L3` STEPs.

The optical system is **never modified** and **never saved**.

## Method

- Extreme fields = every field whose radial magnitude equals the system max
  field (primary wavelength only).
- Pupil rim at r=1 (default 48 samples; `-rimrays N`, clamp 16..256). No dense
  interior pupil grid is used for drawing.
- At each envelope station, batch-trace those rays, map hits with
  `GetGlobalMatrix`, then set **R = max(ray_envelope_r, field_height)** by
  default (no CLAP/Semi floor — matches ray-extent keep-outs such as Concept-24
  phone ~0.256). Station **Z uses the surface rim** (global Z of the max-R ray
  hit, or `Frame.Z + Sag(R)` when no hit) so the polyline follows the asphere
  rim rather than pinching through glass at the vertex. R is **not** floored
  to MEMA.
  - **`-clap`** restores the old CA floor:
    `R = max(ray_envelope_r, clear_aperture/CLAP, field_height)`.
  - **`-vertexZ`** uses vertex `Frame.Z` for station Z instead of rim Z.
  - **`-noclap`** / **`-rayExtent`** are aliases for the default no-CLAP mode
    (kept so older scripts still parse).
  Paraxial/phone stations with floating DIAM still report inherited CLAP in the
  log when `-clap` is used.
- **Drawn:** glass spans (non-empty material) and the stop aperture. **Skipped:**
  CoordinateBreaks and dummy flat air surfaces with no optical power/material.

STEP generation prefers an OCC post-process (`tools/stl_to_rhino_step.py`: sew → named solids / keep-out shell by max Z-span → `ShapeUpgrade_UnifySameDomain` → `STEPControl_Controller.Init` + AP214IS/MM/`write.surfacecurve.mode=0`, XCAF product names). Default export is KEEP_OUT + MEMA `L*` solids. Requires Python with `OCP`/`cadquery-ocp` on PATH (or `RAYEXTENT_PYTHON` / `RAYEXTENT_STL_TO_STEP`). Falls back to pure-C# `FACETED_BREP` if OCC is unavailable (Rhino opens that dialect empty).

PNG includes a labeled **scale bar in mm** (footer, not clipped).

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
| `-clap` | Floor Rmax to CLAP/Semi: `max(rayR, CLAP, fieldH)` (default is no CLAP floor) |
| `-noclap` / `-rayExtent` | Aliases for default no-CLAP mode (`max(rayR, fieldH)`) |
| `-vertexZ` | Station Z = `Frame.Z` (vertex) instead of rim Z |
| `-envelopeOnly` / `-nolenses` | STEP = `KEEP_OUT` only (omit MEMA `L*` solids) |
| `-lenses` | Include MEMA `L1`/`L2`/… solids (default; explicit) |
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

## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same CLI flags where practical)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/RayExtentEnvelope/`](../../python/RayExtentEnvelope/)
- Shared connect helpers: [`python/_zos_bootstrap.py`](../../python/_zos_bootstrap.py)

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

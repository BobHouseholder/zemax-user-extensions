# StartPointFinder

ZOS-API User Extension. Builds a new **intermediate starting design** from a
folder of similar sequential imaging objectives (for example several Double
Gauss lenses) for the **F/# and field of view you ask for**, inside the
first-order envelope those inputs cover, and polishes it with OpticStudio's
optimizer. **By default (`-start best`) it builds every start it can make at
your request, scores each one before optimizing ("the screen"), optimizes the
3 best-screened starts and keeps the one with the lowest merit function that
also passes the result checks**: the starts are each input design scaled to
the request, plus a blend of the inputs. `-top all` optimizes every start (the
behavior before the screen was added); `-start nearest` or `-start blend`
force one kind. Input files are only read; nothing is saved
over them.

**Why best is the default:** it compares every start at your request under the
same merit function and keeps the winner.
On a 10-design Double Gauss test set it beat the nearest-design start at every
request tried (merit function at F/2.5 10 deg: 0.00053 vs 0.00084 for nearest;
F/3 14 deg: 0.00068 vs 0.00074; F/4 16 deg: 0.00051 vs 0.00065; F/4.5 6 deg:
0.00022 vs 0.00026). It also avoids the trap where the input closest in F/# and
field is a poor shape for the request. Each optimized candidate must also pass
the result checks before it can win, so a lens with a low merit but rays
missing a surface is skipped.

**Why only the top 3 are optimized:** on the Double Gauss test set the winner came
from the 3 starts with the lowest merit before optimization at all 5
requests tried, with the same result as optimizing every start, and
optimizing 3 instead of 11 starts cut the optimization time by 70 to 80%.
If none of the top 3 passes the checks, the safety net described below goes
on down the list. `-top N` changes the number; `-top all`
optimizes every start.

**Sequential, refractive objectives.** Standard surfaces are fully supported.
Even Asphere, Zernike Standard Sag and Paraxial surfaces are accepted as
candidates (rebuilt from their own file) but never averaged into the blend.
A finite object distance is accepted when every kept design shares it.
Anything else in the folder is skipped with a reason (see "What gets skipped").

## What it does

1. **Read** every `*.zmx` in the chosen folder (top level only). For each one it
   records the prescription (curvature, thickness, glass, conic per surface),
   the object distance, and the first-order numbers: EFL, image-space F/#
   (`ISFN`), half field of view (angle fields as-is, image-height fields as
   `atan(h / EFL)`, object-height fields with a finite object as
   `atan(h / (object distance + ENPP))`), and total track (`TOTR`).
   - A glass that does not resolve is refused **by name**: OpticStudio quietly
     turns a glass from a catalog that is not installed into air (EFL 1e10,
     F/0.001). The tool reads the file's `GCAT` line, compares it with the
     installed catalogs and says e.g. *"surface 2 glass 'POLYCARB' does not
     resolve: catalog PLASTICPREFERRED is not installed"*.
   - Absurd first-order data (EFL >= 1e6, F/# outside 0.05..1000, non-finite
     track) is refused as "first-order data is not physical".
   - **Duplicates**: byte-identical files (SHA-256) count once; so do files
     with the same lens data (same surfaces, glasses, aperture and field at 9
     significant digits) saved with other settings. Each skipped copy names the
     file it duplicates.
2. **Group** by layout fingerprint (surface count + stop surface + glass/air
   pattern, for a Double Gauss `13surf/stop6/GAGGAAGGAGA`) **and object
   distance** (infinite, or finite values within 0.1%). The biggest group is
   kept; `-layout <fingerprint>` picks another one (an unknown fingerprint is
   refused with the list of groups). The console prints a summary such as
   `Usable: 10 of 18 file(s); skipped: 6 mirror, 2 layout mismatch` and up to
   five runner-up groups with their `-layout` value. Fewer than `-min`
   (default 2) usable designs is refused (exit 2) with the same counts.
3. **Envelope**: min / median / max of EFL, F/#, half-FOV, and track/EFL over the
   kept group (shown at 5 significant digits). A shared finite object distance
   is held for every candidate.
4. **Request (F/# and field)**: `-fno N` and `-hfov DEG` (half field, degrees;
   `-fov` still works) are the main inputs; `-efl F` is optional. Anything not
   given is the folder's median. From the ribbon (or with `-ask`) a small
   window opens after the folder is read, prefilled with the medians and
   showing each min..max plus every input's (F/#, field) pair; it also offers
   Clamp and "start from best (default) / nearest / blend". `-nodialog` never
   opens it.
   - A value outside min..max is **refused** (exit 2), or clamped to the
     nearest edge with `-clamp` (or the window's Clamp box). Round-off slack is
     1e-5 relative, so `-fno 1` is inside a range that starts at 0.9999995.
   - **2D check**: each value can be in range while the *pair* is not. The tool
     finds the nearest input and whether the request lies inside the convex
     hull of the inputs' pairs (same scaled axes as the distance). A request
     equal to an input is always inside. Outside the hull, or farther than 0.5
     from every input, prints a WARNING (the window asks "continue?"). It is a
     warning, not a refusal.
5. **Distance and weights**. Each axis is measured in the folder's own spread:

   `d = sqrt( (log2(F_input / F_request) / log2(F_max / F_min))^2 + ((field_input - field_request) / (field_max - field_min))^2 )`

   An axis with no spread uses 1 (F/#) or 10 deg (field); the request is in
   range, so it adds nothing. An **on-axis-only** input (half-field 0) in a
   folder that has field designs gets +1 in quadrature for a field request, so
   it no longer looks close to a narrow-field request (marked
   `[on-axis only]`).
   - `-weight near` (default): `w = 1 / (d^2 + 0.1^2)`, normalized to sum 1.
   - `-weight soft`: `w = 1 / (0.25 + d)`.
   - `-weight equal`: all blend members count the same.

   Weights apply to the **blend members only** (Standard-surface designs); the
   others show `--`. For a field request, on-axis-only designs (half-field 0
   in a folder that has field designs) are not blend members either: their
   shape was never asked to handle field, so it must not move the average
   (they stay candidates, marked `[on-axis only]`). For the blend every member is scaled to EFL = 1
   (curvature x EFL, thickness / EFL) and the shapes are averaged surface by
   surface. Glass is not averaged: `-glass nearest` (default) picks the input
   glass closest to the weighted mean nd/Vd, `-glass majority` the most-used one.
6. **Candidates**. Every candidate gets the same treatment: sized to the
   request (entrance pupil = EFL / F#, angle fields 0 / 0.7 / 1.0 x half-FOV,
   F d C wavelengths, held object distance, pure re-scale so EFL is exact,
   paraxial focus), Quick Focus, the same merit function, then DLS.
   - **Standard-surface input**: rebuilt from its recipe, scaled to the request.
   - **Input with Even Asphere / Zernike / Paraxial surfaces**: its own file is
     opened (read only), variables, extra configurations and merit rows are
     dropped, OpticStudio's Scale Lens tool scales it (asphere terms scale
     too), then it is given the same pupil, fields, wavelengths and object
     distance. Radii and thicknesses are variables; asphere terms stay fixed.
   - **Blend** of the blend members (Standard surfaces; no on-axis-only
     design for a field request). Needs 2+; otherwise "blend n/a", and
     `-start blend` falls back to nearest with a note.

   **The screen (`-start best`, default `-top 3`).** Every start is first
   sized, refocused and scored with the merit function *without* optimizing.
   The starts are ranked by that score (rank 1 = lowest; a start whose score
   cannot be computed goes last) and only the best `N` are optimized
   (the blend too, if it ranks in). **Safety net:** if none of those `N`
   passes the checks, the next starts in screen order are optimized one at a
   time until one passes (or every start has been tried), so the screen never
   turns a passing result into a failed one. The ranking is printed and saved in the
   report and in `_summary.json` (`screen`, plus `screen_rank` for each
   optimized candidate); inputs that were not optimized say so in the CSV.
   `-top all` optimizes every start. The screen is skipped for `-start
   nearest|blend` and for `-compare refocus|none`.

   Every optimized candidate gets the same checks as the final result (step
   9), so a low merit with rays missing a surface or an asphere edge that no
   longer exists cannot win. `-start best` keeps the lowest merit among the
   candidates that pass (the lowest overall only if none pass, said in the
   report); failed candidates are listed last with the check they failed.
   `-start nearest` keeps the nearest input; `-start blend` keeps the blend. The keeper's start and result are
   saved as soon as it is found, so nothing is rebuilt at the end. All
   optimized candidates are printed **ranked**, with their screen rank, start
   MF, final MF and mean RMS spot. With `-compare refocus|none` the inputs are only refocused
   (or not scored) and the candidates are the nearest input and the blend.
7. **Merit function** (identical for every candidate): OpticStudio default RMS
   spot (centroid, Gaussian quadrature) with glass/air boundary values taken
   from the inputs (glass 0.8x thinnest .. 1.2x thickest input glass, air the
   same idea, all scaled to the target EFL), plus `EFFL` = target, `TOTR` held
   between the shortest and longest input track/EFL (`OPLT`/`OPGT`), and
   `ISFN` shown at weight 0.
8. **Optimize**: every radius (except the stop) and every thickness is a
   variable. DLS, up to `-passes` automatic runs (default 3, stops early when it
   gains less than 0.1%). Optional `-hammer SEC` then gives the **3 best
   optimized lenses that pass the checks** a Hammer run of `SEC` seconds each
   (the keeper alone if none pass, or for `-start nearest|blend`), checks each
   polished lens again and keeps the best. A polished lens that fails the
   checks is not used if its DLS version passed. **Hammer stops on wall time,
   so results vary slightly from PC to PC** (CPU speed and core count) and
   between runs; without `-hammer` the result is repeatable. In testing a
   20 s Hammer on the top 3 lowered the merit by 25 to 37% on the Double Gauss
   set (about 60 s more). The gain depends on the folder (some gain
   nothing) and the result on the PC, so it stays off by default.
9. **Validate** the result and write files. The check passes only if EFL and
   F/# are within 1% of target, the field equals the target, track/EFL is
   inside the input range (2% slack), the thinnest glass center is at least 95%
   of the glass fence, glass edges are positive, air gaps do not collide
   (edges touching within 1e-4 x EFL count as touching; "n/a" when there is no
   internal air gap, e.g. a cemented doublet), every surface has a computable
   semi-diameter and a defined sag across it, the merit function is
   finite and below 1e8, and the **pupil-edge rays trace at every field**:
   7 real rays (top of the pupil on axis; top and bottom at 0.7 and full
   field; the side edge at 0.7 and full field; on-axis requests trace the
   first one only) must reach the image. The merit function's own spot rays
   can all trace while an edge ray misses a surface, so this check catches
   lenses that would otherwise pass. It is traced with temporary merit rows
   that are removed again, and adds almost no time. It applies to every
   optimized candidate too, so such a lens cannot win. A failed check still saves the result but exits **3**.
   The report compares the result with the best input ("matches" within 0.05%)
   and prints the runtime and number of optimizations.

## Outputs

Written to `-out <dir>` (default `<folder>\_StartPointFinder\`). Existing outputs
are refused unless `-force`; an output path equal to an input is always refused.

| File | Contents |
|------|----------|
| `StartPointFinder_result.zmx` | optimized intermediate design (the chosen candidate) |
| `StartPointFinder_start.zmx` | the chosen start, sized and refocused, before optimization |
| `StartPointFinder_firstorder.csv` | inputs (kept/skipped + reason, blend weight, `dist_norm`, object distance, non-Standard surfaces, blend member, on-axis) vs envelope vs target vs blend candidate vs result, with merit scores and mean RMS spot (um) |
| `StartPointFinder_summary.json` | the same data plus settings (incl. `top`, `hammer_sec`), distance scales, the `request` block, the `screen` ranking, the ranked `candidates` (with `screen_rank`, `edge_rays_ok`, DLS and Hammer scores), `chosen`, `runtime_s` and the envelope check list |
| `StartPointFinder_report.txt` | console log |
| `StartPointFinder_layout.png` | Y-Z side view of the result with real ray fans (even-asphere terms drawn; Zernike terms not) |

The console ends with one machine-readable line:
`RETURN start=best|nearest|blend chosen=<label> nearest=<file> mf_start=... mf_result=... best_input_mf=... nearest_mf=... blend_mf=... pair_warn=yes|no envelope=PASS|FAIL secs=N top=N|all nopt=N screen_rank=N hammer=SEC save=...`

## What gets skipped

Each skip has a reason and a category; the categories are counted in the
`Usable: ... skipped: ...` line and in the refusal message.

- Non-Sequential files, files that fail to load
- Surfaces other than Standard / Even Asphere / Zernike Standard Sag / Paraxial
  (coordinate breaks, odd aspheres, ...), mirrors
- Glass without a catalog (model glass); glass that does not resolve (the
  missing catalog is named)
- Unphysical first-order data; EFL not positive
- Field types other than angle / image height / object height with a finite object
- Duplicate files and duplicate prescriptions
- A layout fingerprint or object distance that does not match the kept group

## Options

| Flag | Meaning |
|------|---------|
| `-dir <folder>` | Folder of starting designs. Without it a folder picker opens |
| `-out <dir>` | Output folder (default `<folder>\_StartPointFinder`) |
| `-fno N` / `-hfov DEG` | Requested F/# and half field (degrees; `-fov` is the old name). Default: median of inputs |
| `-efl F` | Requested EFL (optional, default median) |
| `-clamp` | Clamp out-of-envelope values instead of refusing |
| `-start best\|nearest\|blend` | Keep the best candidate (default), the nearest input, or the blend |
| `-layout <fingerprint>` | Use this layout group instead of the biggest (e.g. `13surf/stop6/GAGGAAGGAGA`) |
| `-weight near\|soft\|equal` | Closeness weights for the blend (default `near`) |
| `-ask` | Open the F/# and field window even with `-dir` |
| `-glass nearest\|majority` | Glass pick per position in the blend (default `nearest`) |
| `-passes K` | Automatic DLS runs per candidate (default 3) |
| `-hammer SEC` | Optional Hammer time per lens, on the 3 best optimized lenses that pass the checks (default 0 = off). Stops on wall time, so results vary slightly by PC |
| `-top N\|all` | `-start best`: optimize only the `N` best-screened starts (default 3); `all` = every start |
| `-compare reopt\|refocus\|none` | How inputs are scored at the target (default `reopt` = every input is a candidate) |
| `-min N` | Minimum usable designs in the group (default 2, never below 2) |
| `-force` | Replace existing outputs |
| `-nopng` | Skip the layout picture |
| `-nodialog` | Never open the folder picker, the F/# window or the "open result?" prompt |
| `-quiet` | No PNG auto-open and no "open result?" prompt after a ribbon run |

Exit codes: **0** ok, **1** error, **2** refused (no/bad folder, too few
usable designs, unknown `-layout`, target outside envelope, outputs exist),
**3** result written but it failed the envelope check.

## Ribbon vs command line

- `-dir` given: starts its own standalone OpticStudio (`CreateNewApplication`),
  same as the other tools' `-file` path.
- No `-dir` (ribbon): opens a folder picker, then the F/# and field window
  (Cancel = exit 2), attaches to the running OpticStudio and does all work in
  a **new extra system** (`CreateNewSystem`), so the lens you have open is not
  touched. If attach fails it falls back to standalone.
- At the end of a ribbon run the PNG opens (explorer fallback) and a prompt
  asks **"Open the result in the main OpticStudio window now?"** (default
  **No**; it warns when the open lens has unsaved changes). Yes loads
  `StartPointFinder_result.zmx` into the main window.
- Terminate is polled between files, between DLS passes and between
  candidates. A single automatic DLS run cannot be interrupted mid-run.

## Limits (read before trusting a result)

- Averaging normalized curvatures only makes sense for close relatives. The
  tool checks layout, not "shape family".
- The result is a **starting point**. `-start best` costs one quick score per
  start plus 3 DLS runs (`-top 3`); `-top all` costs one DLS run per input plus
  one for the blend (N + 1 runs).
- The screen can miss: a start that scores badly before optimizing can still
  end up best after DLS. The safety net only steps in when none of the top
  starts passes the checks; it does not look for a passing start that would
  have optimized lower. Use `-top all` (or a larger `-top`) when the request is
  far from the folder's median or the result looks poor.
- DLS from these starts is very sensitive: nudging the request by 1e-6 can
  move a re-optimized merit by 10% or more (a different local minimum). Treat
  differences of that size as noise.
- The distance scales come from the folder's own spread; they do not know how
  hard a given F/# + field combination is for the layout.
- Wavelengths are not taken from the inputs: everything is evaluated at F d C
  (visible), for every candidate.
- Asphere terms of own-file starts are not optimized; vignetting factors are
  cleared; Zernike terms are not included in the edge checks or the picture.
- `-layout` selects a fingerprint; when one fingerprint has designs at two
  object distances, the bigger object group is used.

## Build

```bat
dotnet build -c Release
```

Deploys to `{Zemax Data}\ZOS-API\Extensions\` (`-p:ZemaxDeploy=false` to skip),
then press **Programming > Refresh List**.

## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same algorithm and flags)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/StartPointFinder/`](../../python/StartPointFinder/)
- Entry point: `python/StartPointFinder/start_point_finder.py`

The Python twin always runs standalone (it never attaches to an open session,
so it has no "open result?" prompt); the folder picker there is tkinter.

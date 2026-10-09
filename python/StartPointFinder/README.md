# StartPointFinder (Python / ZOS-API)

Public **Python** port of the C# OpticStudio user extension
[`extensions/StartPointFinder`](../../extensions/StartPointFinder/). Same
algorithm: read a folder of similar sequential objectives (skipping
duplicates, unresolved glass and unphysical files, with a reason and a count
per reason), keep the biggest group that shares one layout and object
distance, then build every start at the F/# and field you ask for - each input
scaled to the request and a blend of the Standard-surface inputs - score each
with the same merit function before optimizing, optimize the 3 best-scored
starts (`-top N`; `-top all` = every start; if none of them passes the
checks, the next starts in screen order are tried until one passes), check each
optimized lens like the final result (including a pupil-edge ray trace at every field), and keep the
best one that passes (default `-start best`; `-start nearest|blend` force
one). Optional `-hammer SEC` polishes the 3 best passing lenses and keeps the
best. Writes a new `.zmx` plus CSV / JSON / text
report (all candidates ranked, runtime) and a layout PNG. Inputs are only read.
Files in inches / cm / m are converted to mm in memory (never on disk); zoom
(multi-configuration) files are skipped; all numbers and outputs are in mm.

This is a **standalone script** (pythonnet + ZOS-API). It is **not** a ribbon
User Extension and is **not** bundled in `tools/pack.ps1` / the dist zip. The
full algorithm description, merit function, validation rules and limits are in
the [C# README](../../extensions/StartPointFinder/README.md).

## Requirements

- Ansys Zemax OpticStudio **2026 R1.01** (or another install with ZOS-API DLLs)
- Python **3.12** with **pythonnet 3.1**
- **matplotlib** for the layout PNG (optional; without it the PNG is skipped)
- **tkinter** for the folder picker (optional; pass `-dir` instead)
- Valid OpticStudio license for the API

Connect helpers come from [`python/_zos_bootstrap.py`](../_zos_bootstrap.py)
(`discover_zos_root` / `bootstrap_zosapi`), same as the other Python twins.
DLL lookup order: `ZEMAX_ROOT`, the known 2026 R1.01 install path, then any
`Ansys Zemax OpticStudio*` folder under Program Files.

## How to run

```bat
set ZEMAX_ROOT=C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01

python python\StartPointFinder\start_point_finder.py ^
  -dir C:\path\to\my_double_gauss_folder ^
  -out C:\path\to\finder_out
```

No `-dir`: a folder picker opens, then a small tkinter window asks for F/#,
half field and EFL (prefilled with the folder's medians, showing each min..max
and every input's F/# + field), plus a Hammer polish box (seconds, 0 = off,
prefilled from `-hammer`, 0 to 600). `-nodialog` skips both; `-ask` opens the window
even with `-dir`. With a window (no `-dir`, or `-ask`) every error or refusal
also pops up a message, and an earlier result in the output folder gets a
**Replace / New folder / Cancel** question (`-force` replaces without asking;
without a window an earlier result is refused, exit 2). A run that stops with an
error deletes the result it had written, so it never blocks the next run.
Requesting F/# and field on the command line:

```bat
python start_point_finder.py -dir C:\designs -fno 2.8 -hfov 12
python start_point_finder.py -dir C:\designs -fno 4 -hfov 16 -start blend
python start_point_finder.py -dir C:\designs -layout 13surf/stop6/GAGGAAGGAGA
```

A value outside the inputs' min..max range is refused (exit 2); add `-clamp`
to clamp it to the nearest edge instead. A pair that sits outside the inputs'
F/# + field hull (or far from every input) prints a 2D-extrapolation WARNING.
`-start best` is the default because it compares every start under the same
merit function and keeps the winner that passes the result checks. Only the 3
starts with the lowest merit before optimizing are optimized (on the Double
Gauss test set the winner came from those 3 at every request tried, at 20 to
30% of the optimization time;
if none of them passes the checks, the next starts are tried);
use `-top all` for a request far from the folder's median. Distances use the folder's own spread (log2 F/# range and
field range), and on-axis-only inputs get a +1 penalty for field requests
and are then left out of the blend;
see the C# README for the formulas, the duplicate/glass checks, the
finite-object rule and the aspheric (own-file) candidates.

`smoke_example.bat` in this folder runs one smoke on a folder you pass in
(it always passes `-force`, so it replaces its own earlier output).

### Flags (match the C# extension)

| Flag | Meaning |
|------|---------|
| `-dir <folder>` | Folder of starting designs (else folder picker) |
| `-out <dir>` | Output folder (default `<folder>\_StartPointFinder`) |
| `-fno N` / `-hfov DEG` | Requested F/# and half field (deg; `-fov` = old name); default median |
| `-efl F` | Requested EFL in mm (optional, default median) |
| `-clamp` | Clamp out-of-envelope values instead of refusing |
| `-start best\|nearest\|blend` | Keep the best candidate (default), the nearest input, or the blend |
| `-layout <fingerprint>` | Use this layout group instead of the biggest |
| `-weight near\|soft\|equal` | Closeness weights for the blend (default `near`) |
| `-ask` | Open the F/# and field window even with `-dir` |
| `-glass nearest\|majority` | Glass pick per position (default `nearest`) |
| `-passes K` | Automatic DLS runs (whole number 1..100, default 3) |
| `-hammer SEC` | Optional Hammer time per lens on the 3 best optimized lenses that pass the checks (whole seconds 0..600, default 0). Hammer stops on wall time, so results vary slightly by PC |
| `-top N\|all` | `-start best`: optimize only the `N` best-scored starts (whole number, default 3); `all` = every start |
| `-compare reopt\|refocus\|none` | Input scoring at the target (default `reopt` = every input is a candidate) |
| `-min N` | Minimum usable designs in the group (default 2) |
| `-force` | Replace an earlier result without asking |
| `-nopng` | Skip the layout picture |
| `-nodialog` | Never open the folder picker or the F/# window |
| `-quiet` | Do not auto-open the PNG after a picker run |

Exit codes: **0** ok, **1** error (including "no candidate start could be
built"), **2** refused or cancelled (also: a result exists without `-force`,
`-out` = the input folder), **3** result written but failed the envelope check.

### Supported / C#-only

Everything above is supported. C#-only: attaching to a running OpticStudio
from the ribbon, the ribbon progress bar / Terminate button, and the
end-of-run "open the result in the main window?" prompt. The Python twin
always starts its own standalone OpticStudio, so there is no session to load
the result into; open `StartPointFinder_result.zmx` yourself.

# ElementLeaveOneOut (Python / ZOS-API)

Public **Python** port of the C# OpticStudio user extension
[`extensions/ElementLeaveOneOut`](../../extensions/ElementLeaveOneOut/).
Same leave-one-out algorithm: remove each removable lens/mirror, adapt the
merit function, local DLS reoptimize, score by ΔMF, save the friendliest
reduced `.zmx`.

This is a **standalone script** (pythonnet + ZOS-API). It is **not** a ribbon
User Extension and is **not** bundled in `tools/pack.ps1` / the dist zip.

## Requirements

- Ansys Zemax OpticStudio **2026 R1.01** (or another install with ZOS-API DLLs)
- Python **3.12** with **pythonnet 3.1.0**
- Valid OpticStudio license for the API

## Finding the OpticStudio DLLs

The script looks for `ZOSAPI.dll`, `ZOSAPI_Interfaces.dll`, and
`ZOSAPI_NetHelper.dll` in this order:

1. Environment variable **`ZEMAX_ROOT`** (install folder that contains those DLLs)
2. Known path `C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01`
3. Any `Ansys Zemax OpticStudio*` folder under Program Files

Example:

```bat
set ZEMAX_ROOT=C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01
```

## How to run

Prefer **standalone** `CreateNewApplication` (same as the C# `-file` path), not
Interactive Extension:

```bat
set PY=C:\Users\bob\AppData\Local\Programs\Python\Python312\python.exe

"%PY%" python\ElementLeaveOneOut\element_leave_one_out.py ^
  -file C:\path\to\sample.zmx ^
  -out C:\GIT\zue-smoke-out\ElementLeaveOneOut\python_smoke ^
  -report ^
  -cycles 30
```

From this folder:

```bat
python element_leave_one_out.py -file C:\path\to\sample.zmx -out C:\temp\loo_out -report
```

### Flags (match the C# extension)

| Flag | Meaning |
|------|---------|
| `-file <zmx>` | Standalone load |
| `-save <path>` | Output reduced system |
| `-out <dir>` | Working directory for copies / report / CSV |
| `-cycles K` | Soft DLS effort (0 = Automatic; default 30) |
| `-top N` | Only LOO-test N weakest-\|power\| elements (0 = all) |
| `-rank power` | Skip LOO; remove weakest-\|power\| only |
| `-report [path]` | Write text report |
| `-quiet` | Accepted (no auto-open) |
| `-nodialog` | Accepted no-op |

## Outputs

Under `-out` (or a sibling `_ElementLeaveOneOut` folder):

- `_baseline_loo.zmx` — untouched copy used for each trial reload
- `<stem>_minus1.zmx` — winner (or path from `-save`)
- `ElementLeaveOneOut_report.txt` — if `-report`
- `ElementLeaveOneOut_trials.csv` — per-element scores

## Algorithm

Faithful port of the C# extension:

1. Connect standalone; load `-file`.
2. Baseline MF0 + EFFL; if MFE empty/unweighted → `SEQOptimizationWizard2`
   default RMS Spot / Centroid / GQ with glass/air boundary values.
3. Ensure MNCT/MXCT thickness constraints (glass ≥1 mm, air ≥0.5 mm; reinforce
   weights to 100).
4. Enumerate removable elements (contiguous glass/mirror runs).
5. Optional `-top N` weakest-\|power\| prefilter; `-rank power` skips LOO.
6. Per candidate: reload → RemapAndCleanMf → DeleteElement (absorb CT into
   previous thickness) → EFFL anchor → thickness constraints →
   EnsureRefocusVariable → local DLS → ClampNegativeThicknesses → score
   ΔMF = MF_after − MF0.
7. Winner = min ΔMF; save `*_minus1.zmx`; write report/CSV.

## C# sibling

Ribbon / deployed User Extension (C#): see
[`extensions/ElementLeaveOneOut/README.md`](../../extensions/ElementLeaveOneOut/README.md).

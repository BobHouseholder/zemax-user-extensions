# ElementLeaveOneOut

ZOS-API User Extension. Ranks each removable **lens or mirror** by a
**leave-one-out** deletion test: remove the element, adapt the existing merit
function, locally reoptimize (DLS), and score by the change in merit function.
Writes a new `.zmx` with the winning element removed.

## Algorithm

Chosen public/practical method (OpticStudio-native):

1. Evaluate baseline merit function `MF0` and EFFL.
2. Enumerate glass/mirror elements (contiguous material runs + rear face).
3. If the MFE is empty/unweighted, builds OpticStudio **default RMS Spot** MF via `SEQOptimizationWizard2` (Spot/RMS/Centroid/GQ + glass/air boundary values). If the MF lacks thickness bounds, adds MNCT/MXCT (glass ≥1 mm, air ≥0.5 mm).\n\nOptional `-top N`: only test the N weakest-|power| elements (crude |Ï†| proxy).
4. For each candidate: reload baseline â†’ delete element (absorb CT into previous
   thickness) â†’ remap/remove MFE operands that referenced deleted surfaces â†’
   ensure an **EFFL** operand targets baseline EFFL â†’ local DLS â†’ record `MF_i`.
5. Keep the candidate with minimum `(MF_i - MF0)`.
6. Save `<stem>_minus1.zmx`.

This matches the common design practice of deleting a weak element and
reoptimizing (see e.g. opticalensdesign.com optimization notes). Differentiable
deletion (Zhang et al., CVPR 2026) and Cheng AO 2003 power/symmetry criteria
informed the optional power prefilter only â€” they are not required at runtime.

## Options

| Flag | Meaning |
|------|---------|
| `-file <zmx>` | Standalone load |
| `-save <path>` | Output reduced system |
| `-out <dir>` | Working directory for copies / report / CSV |
| `-cycles K` | Soft DLS effort (0 = Automatic; default 30) |
| `-top N` | Only LOO-test N weakest-\|power\| elements (0 = all) |
| `-rank power` | Skip LOO; remove weakest-\|power\| only |
| `-report [path]` | Write text report |
| `-quiet` | Do not auto-open report |
| `-nodialog` | Accepted (no ribbon dialog in v1) |

## Build

```bat
dotnet build -c Release -p:ZemaxDeploy=false
```

Deploy to `{Zemax Data}\ZOS-API\Extensions\`.




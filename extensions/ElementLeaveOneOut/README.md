# ElementLeaveOneOut

ZOS-API User Extension. Ranks each removable **lens or mirror** by a
**leave-one-out** deletion test: remove the element, adapt the existing merit
function, locally reoptimize (DLS), and score by the change in merit function.
Writes a new `.zmx` with the winning element removed.

**Sequential systems only.** Non-sequential (NSC) files are refused immediately
with a `FATAL` message and exit code **2** (same spirit as ReverseSystem /
Footprint / EGF).

## Algorithm

Chosen public/practical method (OpticStudio-native):

1. Evaluate baseline merit function `MF0` and EFFL.
2. Enumerate glass/mirror elements (contiguous material runs + rear face).
3. If the MFE is empty/unweighted, builds OpticStudio **default RMS Spot** MF via `SEQOptimizationWizard2` (Spot/RMS/Centroid/GQ + glass/air boundary values). If the MF lacks thickness bounds, adds MNCT/MXCT (glass ≥1 mm, air ≥0.5 mm).\n\nOptional `-top N`: only test the N weakest-|power| elements (crude |Ï†| proxy).
4. For each candidate: reload baseline → delete element (absorb CT into previous
   thickness) → remap/remove MFE operands that referenced deleted surfaces
   (Param1/Param2, plus other columns whose header is a surface slot, plus a
   short type-aware list such as TRAC Param6) → if any remaining operand still
   names a deleted surface, **refuse that trial** (fail-closed; no silent bad
   ranking) → ensure an **EFFL** operand targets baseline EFFL → local DLS →
   record `MF_i`.
5. Keep the candidate with minimum `(MF_i - MF0)` among **valid** trials only.
6. Save `<stem>_minus1.zmx` only when at least one trial is ok (else fail-closed, exit 2).

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
| `-allowbadmf` | Override baseline MF health gate (warns hard; default is refuse) |

If the baseline merit function is already absurd (≥1e8) — common when a file has weights but rays are failing — the tool tries **one** Optimization Wizard reseed, then re-checks the health gate. It still refuses afterward unless you pass `-allowbadmf`.

**Fail-closed:** if every LOO trial is rejected/failed, the tool does **not** write
`*_minus1.zmx` and exits with code **2**. Broken trials (non-finite MF/delta,
MF ≥ 1e8, or MF ≥ 1e6×MF0) are excluded from the winner. After seed + baseline MF0,
a non-finite / ≤0 / ≥1e8 baseline also refuses (exit 2) unless `-allowbadmf`.
A trial whose merit function still names a deleted surface after remap is also
rejected (CODE_REVIEW H3) so a stale constraint cannot win the ranking.

## Build

```bat
dotnet build -c Release -p:ZemaxDeploy=false
```

Deploy to `{Zemax Data}\ZOS-API\Extensions\`.




## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same algorithm and CLI flags)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/ElementLeaveOneOut/`](../../python/ElementLeaveOneOut/)
- Entry point: `python/ElementLeaveOneOut/element_leave_one_out.py`

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

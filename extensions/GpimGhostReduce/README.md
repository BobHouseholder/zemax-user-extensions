# GpimGhostReduce

ZOS-API User Extension. Sequential half of
[Stray Light Analysis with Ghost Focus Generator](https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator)
(Sean Lin / Wilson Chen).

**Never replaces the file’s merit function.** Full-scan every double-bounce pair
with `GPIM`, then append only the ghosts that matter — target 0 — with weights
scaled so ghost pull matches existing MF performance (`balance = 1` → equal).
OpticStudio defines GPIM as `1/|z_ghost − z_image|`, which is why the target is
zero.

This does **not** replace Ghost Focus Generator + Geometric Image Analysis, coatings,
or NSC stray light. Image ghosts are the default.

## How the scan picks operands

1. Snapshot the existing MFE (operand count, weighted-row count, MF value). Scratch
   `GPIM` used for the scan is weight 0 and is removed afterwards.
2. Force `CalculateMeritFunction` on every Surf1 > Surf2 pair (Mode 1 image, and
   optionally Mode 0 pupil).
3. Keep pairs until they cover ~80% of total GPIM, dropping anything under 10% of
   the worst hit. Cap is 8, or `-top N`.
4. Scale each new row so `Σ w·GPIM² = (balance · MF_existing)²`. Use `-weight W` to
   force a raw weight instead.
5. Local DLS runs only when the file already has weighted operands. An empty sample
   MF is left alone so optimize cannot rebuild the lens around ghosts.

## Ribbon

Settings dialog; last run remembered in `%APPDATA%\\GpimGhostReduce\\lastrun.txt`.
Cancel is honoured (`TerminateRequested`).

## Build

```
dotnet build extensions\\GpimGhostReduce\\GpimGhostReduce.csproj --configuration Release
```

OpticStudio 2026 R1 MFE rows are `IMFERow` / `GetOperandCell(MeritColumn)`. Then
**Programming > Refresh List**. Sequential systems only. Pass `-file <zmx>` for
standalone, `-nodialog` to skip the window.

Options: `-mode image|pupil|both`, `-top N` (0 = auto), `-balance B` (default 1),
`-weight W` (raw override), `-optimize`, `-cycles K` (0 = automatic DLS),
`-nodialog`, `-file <zmx>`, `-save <zmx>`.

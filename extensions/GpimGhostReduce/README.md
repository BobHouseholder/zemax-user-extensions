# GpimGhostReduce

ZOS-API User Extension. Sequential half of
[Stray Light Analysis with Ghost Focus Generator](https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator)
(Sean Lin / Wilson Chen).

Rank double-bounce **image ghosts** (and optionally pupil ghosts) with the `GPIM`
operand, then append `GPIM` rows — target 0, existing merit function left intact —
so a later optimize pushes the ghost focus off the image plane. OpticStudio defines
GPIM as `1/|z_ghost − z_image|`, which is why the article targets zero.

This does **not** replace Ghost Focus Generator + Geometric Image Analysis, coatings,
or NSC stray light. Image ghosts are the default.

## Ribbon

Settings dialog; last run remembered in `%APPDATA%\\GpimGhostReduce\\lastrun.txt`.
`Top N = 0` inserts one GPIM with Surf1=Surf2=−1 so OpticStudio tracks the current
worst pair. Cancel is honoured (`TerminateRequested`).

## Build

```
dotnet build extensions\\GpimGhostReduce\\GpimGhostReduce.csproj --configuration Release
```

Then **Programming > Refresh List**. Sequential systems only. Pass `-file <zmx>` for
standalone, `-nodialog` to skip the window.

Options: `-mode image|pupil|both`, `-top N`, `-weight W`, `-optimize`, `-cycles K`
(0 = automatic DLS), `-nodialog`, `-file <zmx>`, `-save <zmx>`.

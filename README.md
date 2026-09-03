# Zemax OpticStudio User Extensions

ZOS-API user extensions for Ansys Zemax OpticStudio, built and validated against
OpticStudio 2026 R1.01. Each extension is a self-contained C# (.NET Framework 4.8)
console application. Compiled executables deploy to `{Zemax Data}\ZOS-API\Extensions\`
and appear under **Programming > User Extensions** in the OpticStudio ribbon; they can
also be run from a shell against a session waiting in
**Programming > Interactive Extension** mode.

Ribbon (GUI) runs report progress and results through OpticStudio's extension
progress display, and auto-open their report/image outputs when finished, since
the console window closes with the process (pass `-quiet` to disable the
auto-open). Tools that modify the system show their edits live in the editors.

**Terminate is honoured by five of the ten.** AthermalScan, DetectorDump,
EquivalentGlassFinder, LayoutRender and GpimGhostReduce poll `TerminateRequested` inside their
loops, so Cancel stops the run at the next iteration. CryoGlass,
DistortionTarget, MoldStress, ReverseSystem and the AthermalAnalysis window do
not reference it at all — pressing Cancel there does nothing and the run goes to
completion. That gap matters most on DistortionTarget and MoldStress, the two
longest-running of the set. OpticStudio's own template checks the flag once,
before your code runs, which is why checking it is not the same as honouring it.

## Extensions

### GpimGhostReduce

Implements the sequential half of
[Stray Light Analysis with Ghost Focus Generator](https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator)
(Sean Lin / Wilson Chen): rank double-bounce **image ghosts** (and optionally pupil
ghosts) with the `GPIM` operand, then append `GPIM` rows — target 0, existing merit
function left intact — so a later optimize pushes the ghost focus off the image plane
instead of sitting on it. OpticStudio defines GPIM as \(1/|z_{\mathrm{ghost}}-z_{\mathrm{image}}|\),
which is why the article targets zero.

It does **not** replace Ghost Focus Generator + Geometric Image Analysis. Those still
confirm the peak irradiance drop on the saved double-bounce file; this extension only
does the operand / optional DLS step. It also does not apply coatings or run NSC stray
light. Image ghosts are the default because that is what the article prioritises.

A ribbon run gets a settings dialog (last run remembered in
`%APPDATA%\GpimGhostReduce\lastrun.txt`). `Top N = 0` inserts one GPIM with
Surf1=Surf2=−1 so OpticStudio keeps tracking whichever pair is currently worst.

Options: `-mode image|pupil|both`, `-top N`, `-weight W`, `-optimize`, `-cycles K`
(0 = automatic DLS), `-nodialog`, `-file <zmx>`, `-save <zmx>`.

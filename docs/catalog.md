# Building, releases, licence

Continuation of the root README: complete CryoGlass section, then Building, Releases, and Licence.

### GpimGhostReduce

See [extensions/GpimGhostReduce/README.md](../extensions/GpimGhostReduce/README.md) for the
authoritative description. `TopN=0` is auto (cap 8, keep pairs covering ~80% of total
GPIM, drop under 10% of the worst). Default `-balance 1` scales new GPIM weights so
ghost pull matches existing MF performance. Existing MFE is never deleted.

### CryoGlass

Generates OpticStudio glass catalogs from the NASA GSFC **CHARMS** cryogenic
refractive-index dataset (Leviton & Frey temperature-dependent Sellmeier
fits — absolute n(λ,T) measured to ~1e-4/1e-5 class accuracy, ~20–300 K).
OpticStudio's catalog dn/dT model is a room-temperature-anchored
perturbation that degrades at cryogenic temperatures; CHARMS is the measured
ground truth there, but OpticStudio has no native support and the ZOS-API
cannot override index computation — so CryoGlass freezes the CHARMS model at
a working temperature T0, where it IS a three-term Sellmeier, and writes an
`.AGF` with **exact** Sellmeier1 coefficients plus a locally-fitted Schott
thermal model (fit error reported per glass) valid near T0. Materials so
far: Si (1.1–5.6 µm) and Ge (1.9–5.5 µm), both 20–300 K, from the free NTRS
full texts.

A built-in self-test checks the evaluator against the papers' own published
measured-index tables before every run and refuses on disagreement, so a
coefficient transcription error can never silently reach a design.
Out-of-range requests are refused by name — CHARMS stops at ~5.6 µm (LWIR is
not covered) and below 20 K; the tool never extrapolates. Generated indices
are ABSOLUTE (vacuum): set the system environment to the working temperature
at 0 atm. CHARMS carries no thermal-expansion data, so TCE is written as 0
with a warning — source it separately before AthermalScan-style analyses.

Validated against the source papers' full published tables, H.H. Li 1980,
OpticStudio's built-in infrared catalog, and the traced index across
50-295 K - see [extensions/CryoGlass/VALIDATION.md](../extensions/CryoGlass/VALIDATION.md).

Options: `-temp T` (Kelvin; pure generation, no OpticStudio needed),
`-range T1:T2:N` (catalog set for STOP sweeps), `-materials "SI,GE"`,
`-fitbox K`, `-out <agf>`, `-file <zmx>` (read the lens's environment
temperature), `-selftest`, `-quiet`. Ribbon runs read the open system's
environment temperature and generate beside the lens file.


## Building

Requires the .NET SDK and an OpticStudio installation. `ZemaxPaths.props` (in the
sibling `repo/` clone, or create your own) points `ZEMAX_ROOT` at the install
directory; the ZOSAPI assemblies are referenced with `Private=false` and resolved
at runtime by `ZOSAPI_NetHelper`.

Build every `extensions/*/*.csproj` (nine User Extensions plus the AthermalAnalysis
User Analysis). Do not list a subset of names: a fourth example that is missing from
the copy-paste is an extension that never gets built.

```
Get-ChildItem extensions -Filter *.csproj -Recurse -Depth 1 |
    ForEach-Object { dotnet build $_.FullName --configuration Release }
```

Default `PlatformTarget` in each csproj stays x64. For an x86 build, pass an override
without editing the projects:

```
Get-ChildItem extensions -Filter *.csproj -Recurse -Depth 1 |
    ForEach-Object { dotnet build $_.FullName --configuration Release -p:PlatformTarget=x86 }
```

Then `tools\\pack.ps1` (x64, default) or `tools\\pack.ps1 -x86`.

Every project deploys itself. `ZemaxPaths.props` carries a `DeployToZemax` target
that runs after each build and copies the `.exe` and its `.exe.config` (which holds
the binding redirects) into the folder OpticStudio reads. The destination comes from
`HKCU\\Software\\Zemax@ZemaxRoot` — the same key Ansys's own ZOS-API boilerplate reads,
and the one OpticStudio rewrites when the data folder changes in preferences, so it
cannot pick the wrong tree on a machine where Documents is redirected to OneDrive.

Default destination is `{Zemax Data}\\ZOS-API\\Extensions\\`. A project that is not a
user extension says so itself — `AthermalAnalysis` sets
`<ZemaxDeployKind>User Analysis</ZemaxDeployKind>` and lands in
`{Zemax Data}\\ZOS-API\\User Analysis\\` instead. Build with `-p:ZemaxDeploy=false` to
skip deployment, or `-p:ZEMAX_DATA="C:\\...\\Zemax"` to target another data folder;
a destination that does not exist fails the build rather than passing quietly.

A newly added extension appears after **Programming > Refresh List**. User analyses
have no such button — restart OpticStudio for a new one. On some machines (observed
Windows ARM, OpticStudio 2026) Refresh List is not enough and OpticStudio must be
restarted as well. Replacing an add-in that is already listed takes effect on its
next run, with no refresh either way.

Ansys ships no deploy step of its own: the project template behind
**Programming > C#** leaves `OutputPath` at `bin\\Release\\` and its `AfterBuild`
target empty, so the copy is manual by their design.

## Releases

A built zip is committed at [`dist/zemax-user-extensions-2026R1.03.zip`](../dist/) so the
add-ins install without a .NET SDK. Extract it, read `INSTALL.txt`, and drag the
`ZOS-API` folder onto your Zemax **data** folder — not into `Extensions`, where an
Ansys extension zip goes but this one does not: it carries two destinations, because
one of the ten is a User Analysis. (Ansys ships its own CODE V Converter as a zip
the same way, which is why the format is this one.)

Each zip holds only our `.exe` and `.exe.config` files, `INSTALL.txt`, and a
`manifest.txt` naming the source commit, the OpticStudio release compiled against and
a SHA-256 per file. `tools\\pack.ps1` builds it and refuses a dirty tree, an Ansys
binary, or an executable carrying a build-machine path.

**Re-run the packer whenever the binaries change** — a stale zip looks exactly like a
fresh one. Check `Source commit` in `manifest.txt` against this repository's history.

Three caveats: the binaries are **unsigned** (Ansys signs theirs; Windows may block
ours — `INSTALL.txt` gives the `Unblock-File` line); they were **compiled against one
OpticStudio release**, and while the ZOS-API assemblies resolve against your own
installation at run time, a withdrawn member can still fail; and **redistribution
terms are Ansys's to define**, not this repository's. Building from source avoids all
three — see [Building](#building).

## Licence

MIT — see [LICENSE](../LICENSE). Copyright (c) 2026 Bob Householder.

That covers the source in this repository and nothing else. The extensions
**link against Ansys ZOS-API assemblies**, which are part of an OpticStudio
installation, are not included here, and are not covered by this licence. A
build output therefore carries Ansys components alongside MIT-licensed code —
so building and using the extensions is straightforward, but redistributing a
compiled `.exe` is a question about Ansys's terms, not about this one.

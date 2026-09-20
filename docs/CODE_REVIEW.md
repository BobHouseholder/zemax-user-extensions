# Full-repo code review — zemax-user-extensions

**Repo:** BobHouseholder/zemax-user-extensions  
**HEAD reviewed:** `32bd838` (`Stop tracking python __pycache__ bytecode artifacts.`) on `main`  
**Date:** 2026-09-19 (America/Bogota)  
**Scope:** entire tree under `extensions/*`, `python/*`, `tools/pack.ps1`, `ZemaxPaths.props`, `.gitignore`, READMEs — read from source, not invented.  
**Not reviewed line-by-line:** MoldStress physics (~15k LOC C# under `extensions/MoldStress/`, plus `validation/mtf-triplet/`); sampled Session/Runner/Program, Polymers, CatalogWriter, and the intentional thin Python twin.

---

## Executive summary

The C# User Extensions are generally careful about ZOS-API JIT/load ordering (`ZemaxLocator`), standalone `CloseApplication`, and several tools have strong fail-closed / restore patterns (ElementLeaveOneOut, AthermalScan restore, CryoGlass self-test). Ship hygiene in `pack.ps1` / `ZemaxPaths.props` (dirty-tree gate, Ansys binary ban, PDB path scan) is unusually solid for a public add-in repo.

The highest risks are **destructive in-session mutations with weak defaults**: DistortionTarget calls `New(false)` on the live primary system; EquivalentGlassFinder **defaults to apply** glass swaps; ReverseSystem / ElementLeaveOneOut / GpimGhostReduce rewrite the open LDE/MFE when launched from the ribbon. Several **Python twins are thin or flag-incomplete** relative to C# (MoldStress intentional; FootprintDxf `-global`/`-aperture` accepted but unused; RayExtentEnvelope STEP→STL stand-in). ElementLeaveOneOut (C#+Python) is the real gold-standard twin; most other twins share `_zos_bootstrap` but ELOO still **duplicates** bootstrap locally. No secrets found; personal machine paths leak in Python READMEs / smoke bat.

---

## Critical

### C1 — DistortionTarget wipes the live OpticStudio system
- **Location:** `extensions/DistortionTarget/Program.cs` (`Build`, ~211–212); same in `python/DistortionTarget/distortion_target.py` (~229–230).
- **What:** Unconditionally calls `sysm.New(false)` then `MakeNonSequential()` on `app.PrimarySystem`.
- **Why:** Ribbon / Interactive Extension attach uses the user’s open file. `New(false)` discards the current prescription with no confirmation, no CopySystem, and no “save first” gate. A mis-click from Programming > User Extensions destroys unsaved work.
- **Suggested fix:** Build into a `CopySystem()` / new standalone only; or require `-save` / explicit confirm dialog before `New`; never call `New` on an attached live primary without opt-in. Prefer creating a new untitled system via CreateNewApplication when `-file`/`-save` imply standalone (partially present) and refuse attach+build unless `-force`/`-replace`.

### C2 — EquivalentGlassFinder defaults to mutating materials in place
- **Location:** `extensions/EquivalentGlassFinder/Program.cs` (`Options.ReportOnly = false`; apply loop ~360–374); `python/EquivalentGlassFinder/equivalent_glass_finder.py` (`REPORT_ONLY = False`).
- **What:** Without `-report`, best-match glasses are written straight onto LDE materials of the open system.
- **Why:** Ribbon launch with no flags = silent glass substitution on a live design. Undo is OpticStudio-only; no CopySystem / no default report-only.
- **Suggested fix:** Default to report-only; require `-apply` (or dialog checkbox) to mutate. Always offer SaveAs copy before apply. Mirror in Python.

---

## High

### H1 — ReverseSystem rewrites the open LDE with no undo / no default save
- **Location:** `extensions/ReverseSystem/Program.cs` (`RunOnSystem` mutates live LDE; save only if `-save` or `-file`, ~795–812); Python twin same pattern.
- **What:** Full sequential reversal freezes solves and rewrites surfaces on `PrimarySystem`. `SaveCopy` defaults false for ribbon runs.
- **Why:** Correctness of reversal is strong (MCE surface-op blockers, mirror-sign, conjugate analysis), but a Cancel-less, Terminate-less run leaves a permanently reversed open file if the user then hits Save in OpticStudio. Documented Cancel gap; still a high operational hazard.
- **Suggested fix:** Work on `CopySystem()` then optional replace/SaveAs; or force `-save` copy before mutation; poll `TerminateRequested`; confirm dialog when attach mode.

### H2 — ElementLeaveOneOut has no Sequential mode gate
- **Location:** `extensions/ElementLeaveOneOut/Program.cs` (`RunOnSystem`); `python/ElementLeaveOneOut/element_leave_one_out.py`.
- **What:** Uses LDE/MFE/SEQOptimizationWizard with no `sys.Mode != Sequential` check (unlike ReverseSystem, Footprint, EGF, Gpim, LayoutRender, Athermal*).
- **Why:** On an NSC system this will fail oddly or corrupt assumptions (surface enumeration, thickness bounds, wizard). Fail-open into wrong API surface.
- **Suggested fix:** Refuse early with a clear message if not Sequential (both C# and Python).

### H3 — ElementLeaveOneOut MF remapping only touches Param1/Param2
- **Location:** `RemapAndCleanMf` in C# (~625–651) and Python twin.
- **What:** After deletion, only MeritColumn Param1/Param2 surface integers are remapped or zeroed; other surface-bearing columns / operand types are ignored.
- **Why:** Many MF operands encode surfaces outside P1/P2 (or use Hx/Hy/Wave slots differently). Stale surface indices → wrong constraints → silent bad LOO ranking / bad saved `*_minus1.zmx`.
- **Suggested fix:** Type-aware remap table per `MeritOperandType`, or fail-closed when any remaining operand still references deleted surfaces after remap; expand tests on MF with REAY/TRAC/CONF/etc.

### H4 — Python FootprintDxf accepts `-global` / `-aperture` but never applies them
- **Location:** `python/FootprintDxf/footprint_dxf.py` — flags set GLOBAL/APERTURE (~98–101); `run()` never reads them; no `GetGlobalMatrix` path (C# has `GlobalApertureHelpers` / `FootprintExport`).
- **What:** Twin advertises flag parity with C#; DXF stays in local surface XY even when `-global` is passed.
- **Why:** CAD users trusting the Python CLI for assembly frames get wrong coordinates without error.
- **Suggested fix:** Implement global transform parity, or refuse with “Python twin does not support -global/-aperture; use C#” when those flags are set.

### H5 — Large C#↔Python parity gaps (beyond intentional MoldStress)
- **Evidence (LOC approx.):** FootprintDxf py~377 vs cs~2100; RayExtentEnvelope py~357 vs cs~1810; GpimGhostReduce py~337 vs cs~928; MoldStress py~271 vs cs~15k (documented thin — OK if callers never assume STAR parity).
- **What:** RayExtentEnvelope Python writes STL facet loft when `-step` requested (`ray_extent_envelope.py` ~327–331). AthermalAnalysis Python is a 40-line wrapper to AthermalScan (documented — OK). MoldStress Python `-run` only inventories elements / stub AGF.
- **Why:** README “every User Extension should have a Python twin” invites assuming behavioral parity; thin twins without hard refusals on unsupported modes are a correctness trap.
- **Suggested fix:** Per-twin capability matrix in each README; exit non-zero when unsupported flags imply C#-only features (`-step` AP214, `-full` STAR, `-global`, dialogs).

### H6 — Cancel / Terminate not honoured on several mutators
- **Location:** Documented in root `README.md` / `pack.ps1` INSTALL.txt: CryoGlass, DistortionTarget, MoldStress, ReverseSystem, AthermalAnalysis window.
- **What:** Mutating or long-running tools ignore `TerminateRequested` (ReverseSystem, DistortionTarget especially).
- **Why:** User cannot abort mid-reversal / mid-build; worsens C1/H1.
- **Suggested fix:** Poll Terminate in write loops; on terminate, restore from snapshot/CopySystem.

---

## Medium

### M1 — ElementLeaveOneOut Python still embeds its own ZOS bootstrap
- **Location:** `python/ElementLeaveOneOut/element_leave_one_out.py` (`KNOWN_INSTALL`, `discover_zos_root`, `bootstrap_zosapi` ~63–186) vs shared `python/_zos_bootstrap.py`.
- **What:** Gold-standard twin does **not** `import _zos_bootstrap`; other twins do. Bootstrap comment in `_zos_bootstrap.py` says it was extracted from ELOO — ELOO was not updated.
- **Why:** Drift risk (already: locator heuristics diverge from C# `ZemaxLocator` version sorting).
- **Suggested fix:** Delete duplicated helpers; call `connect_zos` / `bootstrap_zosapi` from `_zos_bootstrap`.

### M2 — C# `ZemaxLocator.HasZosApi` only checks `ZOSAPI.dll`
- **Location:** `extensions/Shared/ZemaxLocator.cs` (~166–169) vs Python `has_zos_dlls` requiring NetHelper + Interfaces.
- **What:** A half-broken install folder can be selected; Initialize then fails later (mitigated by try/next candidate).
- **Suggested fix:** Align with Python’s three-DLL check.

### M3 — AthermalScan extension mutates live prescription (Analysis uses clone)
- **Location:** `Program.cs` calls `Analyze(app, app.PrimarySystem)`; `AnalysisProgram` uses `live.CopySystem()`. Restore in `finally` (`Program.Analyze.cs` ~287–395, `RestoreSystem`).
- **What:** Extension path is restore-guarded and Terminate-aware in the sweep loop — good — but any restore bug / killed process still leaves thermal scale / env dirty (Analysis path is safer).
- **Suggested fix:** Prefer CopySystem for the ribbon extension too, or harden restore self-test.

### M4 — MoldStress Python stub AGF uses placeholder n=1.500000
- **Location:** `python/MoldStress/mold_stress.py` `write_catalog` (~113–122).
- **What:** Writes Schott-like NM rows with fake index while real stress-optic data lives in C# `CatalogWriter`.
- **Why:** Documented as stub, but a user who loads the AGF into a design gets wrong optics with “MS_*” names that look official.
- **Suggested fix:** Refuse `-writecatalog` unless `-stub` flag; or write comments-only / empty catalog and point to C#.

### M5 — GpimGhostReduce appends MF + optional DLS on live system
- **Location:** `extensions/GpimGhostReduce/Program.cs` (append GPIM; optional optimize; `-save` optional).
- **What:** Does not delete old rows (good) but still mutates MF / variables on attach. Python twin similar, no dialog.
- **Why:** Unexpected MF growth on ribbon run; mitigated by append-only design.
- **Suggested fix:** Default report-only scan; `-apply` to append; CopySystem for optimize trials.

### M6 — SaveAs / report paths overwrite without existence checks
- **Location:** Widespread (`ElementLeaveOneOut` SaveAs, EGF, ReverseSystem, Detector* WriteAllLines, etc.). DistortionTarget / Gpim verify SaveAs produced a file (good pattern).
- **What:** Silent overwrite of prior outputs beside the lens.
- **Suggested fix:** Optional `-force`; otherwise refuse if target exists; always use absolute paths (already noted for Distortion SaveAs).

### M7 — Personal / studio paths in public Python docs
- **Location:** Every `python/*/README.md` (`set PY=C:\Users\bob\...`); `python/ElementLeaveOneOut/smoke_example.bat` (`BobStudio`, `C:\Users\bob\...`, `C:\GIT\zue-smoke-out\...`, “never a live client design”).
- **What:** Not credentials, but machine layout + “client design” wording in a public repo.
- **Suggested fix:** Use `%LOCALAPPDATA%\Programs\Python\...` or `py -3`; scrub BobStudio/client phrasing.

### M8 — Child-level comment standing uneven
- **Location:** ElementLeaveOneOut / ReverseSystem / MoldStress / ZemaxLocator are exemplary; `DetectorPowerSum/Program.cs` header has a broken indented comment splice (~15–16); several shorter tools have file banners but thinner per-method comments.
- **Suggested fix:** Fix DetectorPowerSum banner; bring Detector*/CryoGlass method comments up to ELOO standard where logic is non-obvious.

### M9 — pack.ps1 is C#-only (expected) but zip count comments can drift
- **Location:** `tools/pack.ps1` (“THIRTEEN add-ins”); README Terminate counts.
- **What:** Python intentionally excluded — good. Comment arithmetic must stay synced when tools are added.
- **Suggested fix:** Derive expected count from csproj scan (already iterates projects).

---

## Low

### L1 — `ZOSAPI_NetHelper` referenced with `<Private>true</Private>`
- **Location:** all extension `.csproj` files.
- **What:** Copies Ansys helper into `bin/`; `pack.ps1` correctly refuses shipping `ZOSAPI*`. DeployToZemax copies only if missing.
- **Why:** Fine if pack guards hold; local bin can confuse redistributors who zip `bin/` by hand.
- **Suggested fix:** Keep pack guards; document “never zip bin/”.

### L2 — Process.Start opens reports after ribbon runs
- **Location:** multiple `OpenOutputs` helpers; MoldStress Runner; RayExtentEnvelope StepWriter launches Python.
- **What:** Opens local paths only — low risk; StepWriter should keep args trusted.
- **Suggested fix:** Keep Quiet gate; never pass user strings as shell scripts.

### L3 — `__pycache__` present on disk but gitignored
- **Location:** `python/*/__pycache__/` after local runs; `.gitignore` has `__pycache__/` / `*.pyc`; HEAD commit stopped tracking them.
- **Why:** Hygiene OK for git; clutter for reviewers.
- **Suggested fix:** Optional clean in contributor docs.

### L4 — ElementLeaveOneOut thickness absorb includes rear gap
- **Location:** `DeleteElement` front..rear inclusive.
- **What:** Likely intentional to preserve total track; worth a unit/regression note if edge cases (mirror, CB) mis-absorb.
- **Suggested fix:** Document invariant; add fixture tests.

### L5 — Tracked release zip under `dist/`
- **Location:** `dist/zemax-user-extensions-2026R1.01.zip` (intentionally tracked per `.gitignore` exception).
- **Why:** Fine for Releases workflow; large binary in git.
- **Suggested fix:** Prefer GitHub Releases only long-term (already documented).

### L6 — Security: no secrets / tokens found
- **Scan:** no API keys, passwords, or private keys in source. MIT license / public paths only.
- Residual risk is destructive OpticStudio automation (covered above), not network exfiltration.

---

## Cross-cutting themes

1. **Attach-mode mutation defaults** — Several tools treat the open primary system as a scratchpad. Fail-closed and restore exist in places (ELOO, AthermalScan), but DistortionTarget / EGF / ReverseSystem need safer defaults.
2. **Twin honesty** — MoldStress / AthermalAnalysis Python are honestly thin. Footprint / RayExtent / Gpim look like “full” CLIs but omit major C# behaviors without hard errors.
3. **Bootstrap consolidation** — `_zos_bootstrap.py` is the right shared layer; ELOO should consume it; C# locator is stronger (file-version sort, NoInlining story).
4. **ZOS-API load discipline** — `ZemaxLocator.TryInitialize` / Main-without-ZOSAPI-types pattern is consistently applied and is a real reliability win (WER hang avoidance).
5. **Ship pipeline** — Dirty tree + Ansys DLL + PDB path guards in `pack.ps1` + `DebugType=none` in Release are exemplary public-release hygiene.
6. **Comment standing** — Plain-language banners are a house style strength; uneven below the gold-standard tools.

---

## What’s solid

- **ElementLeaveOneOut (C#+Python):** fail-closed baseline MF, trial MF rejection, thickness fences, EFFL anchor, wizard reseed, CSV always written, CloseApplication in finally — best practice template for the repo.
- **Shared `ZemaxLocator`:** documented JIT/NoInlining rationale; PrimarySystem-null attach rejection; license check.
- **`python/_zos_bootstrap.py`:** CreateNewApplication vs attach, license gate, session.close only if standalone.
- **AthermalAnalysis:** CopySystem clone + close; early LaunchLog; mode confinement — safer than the extension path.
- **AthermalScan:** environment operand / solve guards; afocal refuse; restore-in-finally; Terminate in sweep.
- **CryoGlass:** self-test vs CHARMS before write; refuse out-of-range; absolute-index honesty.
- **DistortionTarget geometry validation:** overhang / pitch checks and chrome coating read-back (silent coating ignore on flats) are excellent correctness work — undermined only by `New(false)`.
- **ReverseSystem validation:** unsupported types + MCE surface operands blocked before rewrite.
- **DetectorPowerSum / DetectorDump:** read-only (aside from optional NSC trace); clear NSC focus.
- **pack.ps1 / ZemaxPaths.props:** reproducible packs, OneDrive/registry deploy notes, no Ansys redistribution.

---

## Suggested follow-up PR order

1. **Safety defaults:** DistortionTarget never `New` on live primary; EGF default `-report`; ReverseSystem CopySystem or forced SaveAs — unblock Critical/High attach hazards.
2. **ElementLeaveOneOut gates:** Sequential mode check + broader MF remap / fail-closed on unmapped surface refs (C#+Python together).
3. **Python twin honesty:** Footprint refuse or implement `-global`/`-aperture`; RayExtent refuse real STEP; capability matrices in READMEs.
4. **Bootstrap cleanup:** ELOO imports `_zos_bootstrap`; align `HasZosApi` with three-DLL check.
5. **Terminate + restore:** ReverseSystem / DistortionTarget / MoldStress poll Cancel; snapshot restore on abort.
6. **Doc hygiene:** scrub `C:\Users\bob` / BobStudio / client wording from python READMEs and smoke bat.
7. **MoldStress (separate epic):** only after safety/parity PRs — deepen Python or formally mark C#-only STAR in root README capability table.

---

## Coverage notes

| Area | Depth |
|------|--------|
| ElementLeaveOneOut C#+Python | Deep |
| `_zos_bootstrap`, `ZemaxLocator`, pack.ps1, ZemaxPaths | Deep |
| ReverseSystem, DistortionTarget, EGF, Gpim, Athermal*, Footprint, RayExtent | Deep / targeted |
| Detector*, LayoutRender, CryoGlass | Medium (patterns + headers + key paths) |
| MoldStress C# physics / validation suite | High-level only |
| Per-tool README accuracy vs flags | Spot-checked |


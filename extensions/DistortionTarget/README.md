# DistortionTarget

ZOS-API User Extension (C#). See top-level README for options.


## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same CLI flags where practical)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/DistortionTarget/`](../../python/DistortionTarget/)
- Shared connect helpers: [`python/_zos_bootstrap.py`](../../python/_zos_bootstrap.py)

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

## Safety defaults (PR1)

Addresses [docs/CODE_REVIEW.md](../../docs/CODE_REVIEW.md) **C1**.

**Default:** DistortionTarget will **not** call `New(false)` on an attached live `PrimarySystem`.

| Mode | Behaviour |
|------|-----------|
| Standalone (`CreateNewApplication`, typically `-file` or `-save`+`-nodialog`) | May `New(false)` a fresh system, then build. |
| Attach + `-save` / `-out` `<path>` | `CopySystem()` → `New` on the **copy** → build → `SaveAs` path. Open lens untouched. |
| Attach + `-force` / `-replace` | Explicitly wipe live `PrimarySystem` with `New(false)` (dangerous; also a dialog checkbox). |
| Attach with none of the above | **FATAL** — refuses to erase the open lens. |

Ribbon dialog: check **Replace open system (-force)** only when you really mean to wipe the live system.

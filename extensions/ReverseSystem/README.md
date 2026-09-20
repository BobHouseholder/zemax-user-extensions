# ReverseSystem

ZOS-API User Extension (C#). See top-level README for options.


## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same CLI flags where practical)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/ReverseSystem/`](../../python/ReverseSystem/)
- Shared connect helpers: [`python/_zos_bootstrap.py`](../../python/_zos_bootstrap.py)

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

## Safety defaults (PR1)

Addresses [docs/CODE_REVIEW.md](../../docs/CODE_REVIEW.md) **H1**.

**Default:** does **not** reverse the attached `PrimarySystem` in place.

| Flag | Effect |
|------|--------|
| `-save` and/or `-out` `<path>` | Reverse a `CopySystem()` clone, then `SaveAs` (open lens unchanged). |
| `-inplace` / `-apply` | Reverse the **live** primary in place (dangerous — documented). |
| `-georeport` | Read-only geometry dump; skips the mutation gate. |
| *(attach, no flags)* | **FATAL** — refuses in-place reverse. |

Long write loops poll `TerminateRequested` so Cancel can stop mid-write.

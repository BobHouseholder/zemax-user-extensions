# EquivalentGlassFinder

ZOS-API User Extension (C#). See top-level README for options.


## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same CLI flags where practical)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/EquivalentGlassFinder/`](../../python/EquivalentGlassFinder/)
- Shared connect helpers: [`python/_zos_bootstrap.py`](../../python/_zos_bootstrap.py)

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

## Safety defaults (PR1)

Addresses [docs/CODE_REVIEW.md](../../docs/CODE_REVIEW.md) **C2**.

**Default:** `ReportOnly = true`. Ribbon with **no flags** only reports candidate glasses — it does **not** change materials.

| Flag | Effect |
|------|--------|
| *(none)* / `-report` | Report only (default). |
| `-apply` | Write best matches onto the live LDE. |
| `-save` | After apply, also `SaveAs` a copy next to the lens. |
| `-reopt` | Optional local DLS after apply. |

# ReverseSystem

ZOS-API User Extension (C#). See top-level README for options.


## Python (ZOS-API)

A standalone **Python** port (pythonnet + ZOS-API, same CLI flags where practical)
lives outside this Extensions folder so `tools/pack.ps1` never ships it:

- Script + run notes: [`python/ReverseSystem/`](../../python/ReverseSystem/)
- Shared connect helpers: [`python/_zos_bootstrap.py`](../../python/_zos_bootstrap.py)

Prefer that tree for scripting / headless smoke. This folder remains the C#
User Extension (ribbon deploy).

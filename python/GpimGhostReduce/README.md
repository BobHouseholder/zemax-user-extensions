# GpimGhostReduce (Python / ZOS-API)

Public **Python** port of the C# OpticStudio user extension
[`extensions/GpimGhostReduce`](../../extensions/GpimGhostReduce/).
Adds weighted GPIM ghost operands; optional local DLS.

This is a **standalone script** (pythonnet + ZOS-API). It is **not** a ribbon
User Extension and is **not** bundled in `tools/pack.ps1` / the dist zip.

## Requirements

- Ansys Zemax OpticStudio **2026 R1.01** (or another install with ZOS-API DLLs)
- Python **3.12+** with **pythonnet** (and **Pillow** where PNG output is used)
- Valid OpticStudio license for the API

## Finding the OpticStudio DLLs

Shared helper [`python/_zos_bootstrap.py`](../_zos_bootstrap.py) looks for
`ZOSAPI.dll`, `ZOSAPI_Interfaces.dll`, and `ZOSAPI_NetHelper.dll` in this order:

1. Environment variable **`ZEMAX_ROOT`**
2. Known path `C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01`
3. Any `Ansys Zemax OpticStudio*` folder under Program Files

```bat
set ZEMAX_ROOT=C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01
```

## How to run

Prefer **standalone** `CreateNewApplication` (pass `-file`):

```bat
set PY=C:\Users\bob\AppData\Local\Programs\Python\Python312\python.exe

"%PY%" python\GpimGhostReduce\gpim_ghost_reduce.py ^
  -file C:\path\to\sample.zmx ^
  -quiet
```

From this folder:

```bat
python gpim_ghost_reduce.py -file C:\path\to\sample.zmx
```

### Flags

`-file <zmx> -save <path> -top N -weight W -balance B -mode image|pupil|both -optimize -cycles K -nodialog -quiet`

(`-top` default here is 5, not C# auto. `-top 0` is refused.)

## Supported / C#-only

**Supported:** sequential GPIM probe via WFB/WSB, append weighted rows, `-mode image|pupil|both`, `-top N>=1`, `-weight` / `-balance`, optional local DLS (`-optimize -cycles`), `-save`.

**C# only:** settings dialog / `%APPDATA%` last-run; **`-top 0` auto** (80% cover / 10% floor / cap 8) — **FATAL** if passed; full Surf1>Surf2 pair scan; scratch-GPIM cleanup after the scan. Empty-MF “skip DLS” guard is C#-stronger.

## C# sibling

Ribbon / deployed User Extension (C#): see
[`extensions/GpimGhostReduce/`](../../extensions/GpimGhostReduce/).

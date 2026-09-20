# RayExtentEnvelope (Python / ZOS-API)

Public **Python** port of the C# OpticStudio user extension
[`extensions/RayExtentEnvelope`](../../extensions/RayExtentEnvelope/).
Rim-ray keep-out envelope PNG (+ STL loft; STEP is C#-primary).

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

"%PY%" python\RayExtentEnvelope\ray_extent_envelope.py ^
  -file C:\path\to\sample.zmx ^
  -quiet
```

From this folder:

```bat
python ray_extent_envelope.py -file C:\path\to\sample.zmx
```

### Flags

`-file -out -png -step -rimrays -surfaces -width -height -quiet -nodialog -clap/-noclap -vertexz -envelopeonly/-lenses`

(Match the C# tool where practical; see C# sibling README for full semantics.)

## C# sibling

Ribbon / deployed User Extension (C#): see
[`extensions/RayExtentEnvelope/`](../../extensions/RayExtentEnvelope/).

## STEP / OCC note

The C# tool can post-process to AP214 STEP via OCC (`tools/stl_to_rhino_step.py`).
The Python twin writes a **PNG** layout and, with `-step`, an **STL facet loft**
stand-in. Prefer the C# path for Rhino-ready STEP solids.

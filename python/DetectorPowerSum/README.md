# DetectorPowerSum (Python / ZOS-API)

Public **Python** port of the C# OpticStudio user extension
[`extensions/DetectorPowerSum`](../../extensions/DetectorPowerSum/).
Sums Detector Rectangle total power (optional NSC trace).

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

"%PY%" python\DetectorPowerSum\detector_power_sum.py ^
  -file C:\path\to\sample.zmx ^
  -quiet
```

From this folder:

```bat
python detector_power_sum.py -file C:\path\to\sample.zmx
```

### Flags

`-file <zmx> -out <path> -trace -nosplit -noscatter -nopol -all -quiet`

(Match the C# tool where practical; see C# sibling README for full semantics.)

## C# sibling

Ribbon / deployed User Extension (C#): see
[`extensions/DetectorPowerSum/`](../../extensions/DetectorPowerSum/).

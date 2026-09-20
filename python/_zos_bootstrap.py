#!/usr/bin/env python3
# ============================================================
# Shared ZOS-API bootstrap for python/* twins
# ============================================================
# Finds OpticStudio DLLs and starts (or attaches to) a ZOS-API
# session. Extracted from ElementLeaveOneOut so every twin can
# import the same connect helpers without copy-paste drift
# (ELOO now imports this module too).
#
# Prefer CreateNewApplication + -file for standalone / headless
# runs. Interactive Extension attach is the fallback when no
# -file is given.
# ============================================================

from __future__ import annotations

import os
import sys
from dataclasses import dataclass
from typing import Any, Optional, Tuple

KNOWN_INSTALL = r"C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01"


def has_zos_dlls(folder: str) -> bool:
    """True if folder looks like an OpticStudio install with ZOS-API DLLs."""
    if not folder or not os.path.isdir(folder):
        return False
    need = ("ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll")
    return all(os.path.isfile(os.path.join(folder, n)) for n in need)


def discover_zos_root() -> str:
    """
    Locate the OpticStudio install folder that holds ZOSAPI.dll.

    Look order:
      1) ZEMAX_ROOT environment variable
      2) Known 2026 R1.01 install path
      3) Any Ansys Zemax OpticStudio* under Program Files
    """
    env = (os.environ.get("ZEMAX_ROOT") or "").strip().strip('"')
    if env and has_zos_dlls(env):
        return env.rstrip("\\/")
    if has_zos_dlls(KNOWN_INSTALL):
        return KNOWN_INSTALL

    candidates = []
    for pf_key in ("ProgramFiles", "ProgramFiles(x86)"):
        pf = os.environ.get(pf_key) or (
            r"C:\Program Files" if pf_key == "ProgramFiles" else r"C:\Program Files (x86)"
        )
        if not os.path.isdir(pf):
            continue
        try:
            for name in os.listdir(pf):
                low = name.lower().replace(" ", "")
                if "opticstudio" in low or ("zemax" in low and "optic" in low):
                    full = os.path.join(pf, name)
                    if has_zos_dlls(full):
                        candidates.append(full)
        except OSError:
            continue

    if candidates:
        candidates = sorted(set(candidates), reverse=True)
        for c in candidates:
            if "2026" in c:
                return c
        return candidates[0]

    raise RuntimeError(
        "could not find OpticStudio ZOS-API DLLs. Set ZEMAX_ROOT to the install "
        "folder that contains ZOSAPI.dll (tried {!r}).".format(KNOWN_INSTALL)
    )


def bootstrap_zosapi(zos_root: Optional[str] = None, say=None):
    """
    Load OpticStudio .NET libraries into this Python process via pythonnet.

    Returns the imported ZOSAPI module. Optionally logs the resolved path
    through `say` (callable) or print.
    """
    import clr  # type: ignore

    if zos_root is None:
        zos_root = discover_zos_root()
    log = say if callable(say) else (lambda s: print(s, flush=True))

    if zos_root not in sys.path:
        sys.path.append(zos_root)
    os.environ["PATH"] = zos_root + os.pathsep + os.environ.get("PATH", "")

    clr.AddReference(os.path.join(zos_root, "ZOSAPI_NetHelper"))
    import ZOSAPI_NetHelper  # type: ignore

    ok = bool(ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(zos_root))
    if not ok:
        ok = bool(ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
    if not ok:
        raise RuntimeError("ZOSAPI_NetHelper failed to Initialize at " + zos_root)

    resolved = ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory()
    log("Found OpticStudio at: " + str(resolved or zos_root))

    clr.AddReference(os.path.join(zos_root, "ZOSAPI"))
    clr.AddReference(os.path.join(zos_root, "ZOSAPI_Interfaces"))
    import ZOSAPI  # type: ignore

    return ZOSAPI


@dataclass
class ZosSession:
    """A live ZOS-API application plus whether we started it ourselves."""

    ZOSAPI: Any
    app: Any
    TheSystem: Any
    standalone: bool
    zos_root: str

    def close(self) -> None:
        """Close OpticStudio only if this process created it."""
        if self.standalone and self.app is not None:
            try:
                self.app.CloseApplication()
            except Exception:
                pass


def connect_zos(
    ZOSAPI=None,
    file_path: Optional[str] = None,
    *,
    require_license: bool = True,
    say=None,
    zos_root: Optional[str] = None,
) -> ZosSession:
    """
    Start standalone OpticStudio (-file) or attach to Interactive Extension.

    Prefer CreateNewApplication when file_path is set. Otherwise try
    ConnectToApplication / ConnectAsExtension.
    """
    log = say if callable(say) else (lambda s: print(s, flush=True))
    if zos_root is None:
        zos_root = discover_zos_root()
    if ZOSAPI is None:
        ZOSAPI = bootstrap_zosapi(zos_root, say=log)

    standalone = bool(file_path)
    connection = ZOSAPI.ZOSAPI_Connection()
    app = None

    if standalone:
        app = connection.CreateNewApplication()
        if app is None or app.PrimarySystem is None:
            raise RuntimeError("could not start a standalone OpticStudio instance")
        if require_license and not app.IsValidLicenseForAPI:
            raise RuntimeError(
                "standalone instance started but license is not valid for ZOS-API: "
                + str(getattr(app, "LicenseStatus", "?"))
            )
        TheSystem = app.PrimarySystem
        if not TheSystem.LoadFile(file_path, False):
            try:
                app.CloseApplication()
            except Exception:
                pass
            raise RuntimeError("failed to load " + str(file_path))
        log("Loaded: " + str(file_path))
    else:
        try:
            app = connection.ConnectToApplication()
        except Exception:
            app = None
        if app is None:
            try:
                app = connection.ConnectAsExtension(0)
            except Exception:
                app = None
        if app is None or app.PrimarySystem is None:
            raise RuntimeError(
                "could not connect to OpticStudio "
                "(start Interactive Extension, or pass -file <zmx>)"
            )
        if require_license and not app.IsValidLicenseForAPI:
            raise RuntimeError(
                "license is not valid for ZOS-API: " + str(app.LicenseStatus)
            )
        TheSystem = app.PrimarySystem
        log("Connected to OpticStudio (mode: " + str(app.Mode) + ")")

    return ZosSession(
        ZOSAPI=ZOSAPI,
        app=app,
        TheSystem=TheSystem,
        standalone=standalone,
        zos_root=zos_root,
    )


def ensure_python_path() -> None:
    """Put python/ on sys.path so `import _zos_bootstrap` works from subfolders."""
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)


def parse_flag_token(raw: str) -> str:
    """Normalize '-Foo' / '/foo' to lowercase key 'foo'."""
    return raw.lstrip("-/").lower()


def next_arg(argv, i) -> Tuple[Optional[str], int]:
    """Return (value, new_index) consuming argv[i+1] if present."""
    if i + 1 < len(argv):
        return argv[i + 1], i + 1
    return None, i


def call_out(method, *args, out_types=None):
    """
    Call a .NET method that uses 'out' parameters via pythonnet.

    Tries the tuple-return style first; falls back to clr.Reference.
    out_types: list of .NET types for ByRef args (e.g. [Double, Double]).
    Returns (ok_or_result, *out_values) when possible, else raw result.
    """
    try:
        result = method(*args)
        if isinstance(result, tuple):
            return result
        if out_types is None:
            return result
    except TypeError:
        result = None

    if out_types is None:
        return result

    import clr  # type: ignore

    refs = [clr.Reference[t]() for t in out_types]
    ok = method(*(list(args) + refs))
    vals = [r.Value for r in refs]
    if isinstance(ok, bool):
        return (ok,) + tuple(vals)
    return (ok,) + tuple(vals)

#!/usr/bin/env python3
# ============================================================
# CryoGlass (Python / ZOS-API) - what this program does
# ============================================================
# Some glasses are used very cold (space / cryo). NASA CHARMS
# measured how their index changes with temperature. OpticStudio
# cannot plug those formulas in directly, so we bake a frozen
# glass catalog (.AGF) at your working temperature T0.
# Twin of the C# User Extension (CHARMS Si/Ge only).
#
# Flags (same names as the C# tool):
#   -temp T  -range T1:T2:N  -materials "SI,GE"  -fitbox K
#   -out <agf>  -file <zmx>  -selftest  -quiet  -nodialog
# ============================================================

from __future__ import annotations

import math
import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import List, Optional, Sequence

_HERE = os.path.dirname(os.path.abspath(__file__))
_PYROOT = os.path.dirname(_HERE)
if _PYROOT not in sys.path:
    sys.path.insert(0, _PYROOT)

from _zos_bootstrap import (  # noqa: E402
    bootstrap_zosapi,
    connect_zos,
    discover_zos_root,
    parse_flag_token,
)

TEMP_K = float("nan")
RANGE_SPEC: Optional[str] = None
MATERIALS = ""
FIT_BOX = 25
OUT_PATH: Optional[str] = None
FILE_PATH: Optional[str] = None
SELFTEST_ONLY = False
QUIET = False


@dataclass
class CharmsMaterial:
    name: str
    description: str
    lambda_min_um: float
    lambda_max_um: float
    tmin_k: float
    tmax_k: float
    accuracy_abs: float
    source: str
    S: List[List[float]]
    L: List[List[float]]
    self_test: List[List[float]] = field(default_factory=list)


# Published CHARMS coefficients from open papers — we do not invent numbers.
MATERIALS_DATA: List[CharmsMaterial] = [
    CharmsMaterial(
        name="SI_CHARMS",
        description="Silicon (single crystal), CHARMS TSM fit",
        lambda_min_um=1.1,
        lambda_max_um=5.6,
        tmin_k=20,
        tmax_k=300,
        accuracy_abs=1e-4,
        source="Frey, Leviton & Madison, Proc. SPIE 6273, 62732J (2006), Table 5; NTRS 20070021411",
        S=[
            [10.4907, -2.08020e-04, 4.21694e-06, -5.82298e-09, 3.44688e-12],
            [-1346.61, 29.1664, -0.278724, 1.05939e-03, -1.35089e-06],
            [4.42827e07, -1.76213e06, -7.61575e04, 678.414, 103.243],
        ],
        L=[
            [0.299713, -1.14234e-05, 1.67134e-07, -2.51049e-10, 2.32484e-14],
            [-3.51710e03, 42.3892, -0.357957, 1.17504e-03, -1.13212e-06],
            [1.71400e06, -1.44984e05, -6.90744e03, -39.3699, 23.5770],
        ],
        self_test=[
            [1.1, 30.0, 3.51113],
            [1.5, 100.0, 3.45609],
            [2.5, 295.0, 3.44011],
            [3.0, 150.0, 3.41240],
            [2.0, 50.0, 3.42508],
            [3.0, 295.0, 3.43293],
        ],
    ),
    CharmsMaterial(
        name="GE_CHARMS",
        description="Germanium (single crystal), CHARMS TSM fit",
        lambda_min_um=1.9,
        lambda_max_um=5.5,
        tmin_k=20,
        tmax_k=300,
        accuracy_abs=1e-4,
        source="Frey, Leviton & Madison, Proc. SPIE 6273, 62732J (2006), Table 10; NTRS 20070021411",
        S=[
            [13.9723, 2.52809e-03, -5.02195e-06, 2.22604e-08, -4.86238e-12],
            [0.452096, -3.09197e-03, 2.16895e-05, -6.02290e-08, 4.12038e-11],
            [751.447, -14.2843, -0.238093, 2.96047e-03, -7.73454e-06],
        ],
        L=[
            [0.386367, 2.01871e-04, -5.93448e-07, -2.27923e-10, 5.37423e-12],
            [1.08843, 1.16510e-03, -4.97284e-06, 1.12357e-08, 9.40201e-12],
            [-2893.19, -0.967948, -0.527016, 6.49364e-03, -1.95162e-05],
        ],
        self_test=[
            [2.0, 30.0, 4.01922],
            [2.5, 100.0, 3.99502],
            [3.5, 150.0, 3.97985],
            [4.0, 200.0, 3.98949],
            [5.5, 295.0, 4.01404],
            [5.0, 60.0, 3.94349],
        ],
    ),
]


def find_material(name: str) -> Optional[CharmsMaterial]:
    """Look up a CHARMS material by short or full name."""
    key = name.strip()
    for m in MATERIALS_DATA:
        if m.name.lower() == key.lower():
            return m
        short = m.name.replace("_CHARMS", "")
        if short.lower() == key.lower():
            return m
    return None


def poly(c: Sequence[float], t: float) -> float:
    """Evaluate a0 + a1*t + a2*t^2 + ..."""
    v = 0.0
    for j in range(len(c) - 1, -1, -1):
        v = v * t + c[j]
    return v


def s_at(m: CharmsMaterial, i: int, t_k: float) -> float:
    return poly(m.S[i], t_k)


def l_at(m: CharmsMaterial, i: int, t_k: float) -> float:
    return poly(m.L[i], t_k)


def index_abs(m: CharmsMaterial, lambda_um: float, t_k: float) -> float:
    """Absolute (vacuum) refractive index at wavelength (um) and T (Kelvin)."""
    l2 = lambda_um * lambda_um
    ssum = 0.0
    for i in range(3):
        li = l_at(m, i, t_k)
        ssum += s_at(m, i, t_k) * l2 / (l2 - li * li)
    return math.sqrt(1.0 + ssum)


def self_test_material(m: CharmsMaterial, do_print: bool = True) -> bool:
    """Verify the evaluator against the paper's measured-index tables."""
    worst = 0.0
    ok = True
    for row in m.self_test:
        n = index_abs(m, row[0], row[1])
        err = abs(n - row[2])
        worst = max(worst, err)
        if err > 5e-4:
            ok = False
        if do_print:
            print(
                "  {0:<10} lam={1:4.1f} um T={2:5.1f} K  paper {3:.5f}  model {4:.5f}  |d|={5:.1E}".format(
                    m.name, row[0], row[1], row[2], n, err
                ),
                flush=True,
            )
    if do_print:
        print(
            "  {0}: worst |dn| vs published table = {1:.1E}  ({2})".format(
                m.name,
                worst,
                "PASS" if ok else "FAIL - refusing to generate catalogs",
            ),
            flush=True,
        )
    return ok


def n_air(lam_um: float, t_c: float, p_atm: float) -> float:
    """OpticStudio air-index model (Kohlrausch/Edlen form)."""
    s2 = 1.0 / (lam_um * lam_um)
    nref = 1.0 + 1e-8 * (6432.8 + 2949810.0 / (146.0 - s2) + 25540.0 / (41.0 - s2))
    return 1.0 + (nref - 1.0) * p_atm / (1.0 + (t_c - 15.0) * 3.4785e-3)


def solve5(a: List[List[float]], b: List[float]) -> List[float]:
    """Solve a 5×5 linear system (Gaussian elimination with pivoting)."""
    n = 5
    m = [row[:] for row in a]
    x = b[:]
    for c in range(n):
        p = c
        for r in range(c + 1, n):
            if abs(m[r][c]) > abs(m[p][c]):
                p = r
        if abs(m[p][c]) < 1e-30:
            x[c] = 0.0
            continue
        if p != c:
            m[c], m[p] = m[p], m[c]
            x[c], x[p] = x[p], x[c]
        for r in range(c + 1, n):
            f = m[r][c] / m[c][c]
            for k in range(c, n):
                m[r][k] -= f * m[c][k]
            x[r] -= f * x[c]
    for c in range(n - 1, -1, -1):
        if abs(m[c][c]) < 1e-30:
            x[c] = 0.0
            continue
        for k in range(c + 1, n):
            x[c] -= m[c][k] * x[k]
        x[c] /= m[c][c]
    return x


def convert_relative(m: CharmsMaterial, t0_k: float):
    """Turn CHARMS absolute n into OpticStudio relative-to-air coefficients at T0."""
    t_c = t0_k - 273.15
    lam_mid = 0.5 * (m.lambda_min_um + m.lambda_max_um)
    c = n_air(lam_mid, t_c, 1.0)
    c2 = c * c
    k = [0.0] * 4
    for i in range(3):
        k[i] = s_at(m, i, t0_k) / c2
    k[3] = (1.0 - c2) / c2
    l2s = [l_at(m, i, t0_k) ** 2 for i in range(3)]
    worst = 0.0
    for p in range(61):
        lam = m.lambda_min_um + (m.lambda_max_um - m.lambda_min_um) * p / 60.0
        lam2 = lam * lam
        ssum = k[3] * lam2 / (lam2 + 1e-9)
        for i in range(3):
            ssum += k[i] * lam2 / (lam2 - l2s[i])
        n_model = math.sqrt(1.0 + ssum)
        n_true = index_abs(m, lam, t0_k) / n_air(lam, t_c, 1.0)
        worst = max(worst, abs(n_model - n_true))
    return k, worst


def fit_schott_local(m: CharmsMaterial, t0: float, t_lo: float, t_hi: float):
    """Least-squares fit of the Schott thermal model to the TSM surface."""
    lams = [
        m.lambda_min_um + (m.lambda_max_um - m.lambda_min_um) * i / 8.0 for i in range(9)
    ]
    temps = [t_lo + (t_hi - t_lo) * i / 10.0 for i in range(11)]
    ata = [[0.0] * 5 for _ in range(5)]
    atb = [0.0] * 5
    for lam in lams:
        n0 = index_abs(m, lam, t0)
        scale = (n0 * n0 - 1.0) / (2.0 * n0)
        il2 = 1.0 / (lam * lam)
        for t in temps:
            dT = t - t0
            if abs(dT) < 1e-12:
                continue
            dn = index_abs(m, lam, t) - n0
            row = [
                scale * dT,
                scale * dT * dT,
                scale * dT * dT * dT,
                scale * dT * il2,
                scale * dT * dT * il2,
            ]
            for a in range(5):
                atb[a] += row[a] * dn
                for b in range(5):
                    ata[a][b] += row[a] * row[b]
    x = solve5(ata, atb)
    worst = 0.0
    for lam in lams:
        n0 = index_abs(m, lam, t0)
        scale = (n0 * n0 - 1.0) / (2.0 * n0)
        il2 = 1.0 / (lam * lam)
        for t in temps:
            dT = t - t0
            model = scale * (
                x[0] * dT
                + x[1] * dT * dT
                + x[2] * dT * dT * dT
                + (x[3] * dT + x[4] * dT * dT) * il2
            )
            worst = max(worst, abs(model - (index_abs(m, lam, t) - n0)))
    return x, worst


def write_agf(
    path: str, mats: Sequence[CharmsMaterial], t0_k: float, fit_half_width_k: int
) -> List[str]:
    """Write the frozen .AGF file at temperature T0 for these materials."""
    report: List[str] = []
    lines = [
        "CC CryoGlass-generated catalog from NASA GSFC CHARMS TSM fits",
        "CC Working temperature {0:.2f} K ({1:.2f} C); indices are ABSOLUTE (vacuum)".format(
            t0_k, t0_k - 273.15
        ),
        "CC Set the system environment to the working temperature at 0 atm pressure",
        "CC TCE is NOT provided by CHARMS - the 0 written here must be replaced from a",
        "CC separate source before thermal-expansion-sensitive analyses",
    ]
    for m in mats:
        t_lo = max(m.tmin_k, t0_k - fit_half_width_k)
        t_hi = min(m.tmax_k, t0_k + fit_half_width_k)
        name = "{0}_{1:.0f}K".format(m.name, t0_k)
        td, worst_fit = fit_schott_local(m, t0_k, t_lo, t_hi)
        cd, worst_conv = convert_relative(m, t0_k)
        report.append(
            "{0}: exact Sellmeier5 (relative-to-air at {1:.2f} K) residual |dn|={2:.1E}; "
            "local dn/dT fit over {3:.0f}-{4:.0f} K worst |dn|={5:.1E}".format(
                name, t0_k, worst_conv, t_lo, t_hi, worst_fit
            )
        )
        n_mid = index_abs(m, 0.5 * (m.lambda_min_um + m.lambda_max_um), t0_k)
        lines.append("NM {0} 11 0 {1:.6f} 0.0 0 -1 -1 -1 -1".format(name, n_mid))
        lines.append(
            "GC CHARMS index at {0:.2f} K (relative-to-air convention); {1}; "
            "valid {2:.3f}-{3:.3f} um, fit box {4:.0f}-{5:.0f} K".format(
                t0_k, m.source, m.lambda_min_um, m.lambda_max_um, t_lo, t_hi
            )
        )
        lines.append(
            "CD {0:.9E} {1:.9E} {2:.9E} {3:.9E} {4:.9E} {5:.9E} {6:.9E} {7:.9E} {8:.9E} {9:.9E}".format(
                cd[0],
                l_at(m, 0, t0_k) ** 2,
                cd[1],
                l_at(m, 1, t0_k) ** 2,
                cd[2],
                l_at(m, 2, t0_k) ** 2,
                cd[3],
                -1e-9,
                0.0,
                0.0,
            )
        )
        lines.append(
            "TD {0:.6E} {1:.6E} {2:.6E} {3:.6E} {4:.6E} {5:.6E} {6:.3f}".format(
                td[0], td[1], td[2], td[3], td[4], 0.0, t0_k - 273.15
            )
        )
        lines.append("ED 0.000000E+000 0.000000E+000 0 0 -")
        lines.append(
            "LD {0:.4f} {1:.4f}".format(m.lambda_min_um, m.lambda_max_um)
        )
        lines.append("IT 0 0 0")
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\r\n".join(lines) + "\r\n")
    return report


def say(line: str) -> None:
    print(line, flush=True)


def parse_double(s: Optional[str], keep: float) -> float:
    if s is None:
        return keep
    try:
        return float(s)
    except ValueError:
        say("WARNING: {!r} is not a valid number - keeping {}.".format(s, keep))
        return keep


def parse_args(argv: List[str]) -> None:
    """Read -temp / -range / -materials / ... and fill the global switches."""
    global TEMP_K, RANGE_SPEC, MATERIALS, FIT_BOX, OUT_PATH, FILE_PATH
    global SELFTEST_ONLY, QUIET
    i = 0
    while i < len(argv):
        raw = argv[i]
        if raw.startswith("-") or raw.startswith("/"):
            a = parse_flag_token(raw)

            def next_tok() -> Optional[str]:
                nonlocal i
                if i + 1 < len(argv):
                    i += 1
                    return argv[i]
                return None

            if a == "temp":
                TEMP_K = parse_double(next_tok(), TEMP_K)
            elif a == "range":
                RANGE_SPEC = next_tok()
            elif a == "materials":
                MATERIALS = next_tok() or ""
            elif a == "fitbox":
                FIT_BOX = int(parse_double(next_tok(), FIT_BOX))
            elif a == "out":
                OUT_PATH = next_tok()
            elif a == "file":
                FILE_PATH = next_tok()
            elif a == "selftest":
                SELFTEST_ONLY = True
            elif a in ("quiet", "nodialog"):
                if a == "quiet":
                    QUIET = True
            else:
                raise RuntimeError("unknown flag " + raw)
        i += 1
    if FIT_BOX < 5:
        FIT_BOX = 5


def selected_materials() -> List[CharmsMaterial]:
    """Which glasses the user asked for (or all of them)."""
    if not MATERIALS.strip():
        return list(MATERIALS_DATA)
    out: List[CharmsMaterial] = []
    for part in MATERIALS.replace(";", ",").split(","):
        part = part.strip()
        if not part:
            continue
        m = find_material(part)
        if m is None:
            say(
                "WARNING: unknown material {!r} - available: {}".format(
                    part, ", ".join(x.name for x in MATERIALS_DATA)
                )
            )
        else:
            out.append(m)
    return out


def temperatures() -> List[float]:
    """Working temperature(s) T0 to freeze catalogs at."""
    temps: List[float] = []
    if RANGE_SPEC is not None:
        p = RANGE_SPEC.split(":")
        if len(p) == 3:
            t1 = parse_double(p[0], float("nan"))
            t2 = parse_double(p[1], float("nan"))
            n = int(parse_double(p[2], 0))
            if not math.isnan(t1) and not math.isnan(t2) and n >= 2:
                for i in range(n):
                    temps.append(t1 + (t2 - t1) * i / (n - 1))
        if not temps:
            raise RuntimeError("could not parse -range (expected T1:T2:N in Kelvin)")
    else:
        temps.append(TEMP_K)
    return temps


def generate(directory: str, mats: List[CharmsMaterial], temps: List[float]) -> None:
    """Write .AGF for each material × temperature."""
    global OUT_PATH
    for t0 in temps:
        usable = [m for m in mats if m.tmin_k <= t0 <= m.tmax_k]
        for m in mats:
            if m not in usable:
                say(
                    "REFUSED: {0} is not measured at {1:.0f} K (valid {2:.0f}-{3:.0f} K) - not extrapolating.".format(
                        m.name, t0, m.tmin_k, m.tmax_k
                    )
                )
        if not usable:
            say("no materials usable at {0:.0f} K - nothing generated.".format(t0))
            continue
        if OUT_PATH and len(temps) == 1:
            path = OUT_PATH
        else:
            path = os.path.join(directory, "CHARMS_{0:.0f}K.AGF".format(t0))
        report = write_agf(path, usable, t0, FIT_BOX)
        say("catalog written: " + path)
        for line in report:
            say("  " + line)
        say("  NOTE: absolute (vacuum) indices at the working temperature; set the system")
        say("  environment to that temperature at 0 atm. TCE=0 - source thermal expansion")
        say("  separately before AthermalScan-style analyses.")


def self_test_all() -> bool:
    say("self-test: evaluator vs published measured-index tables")
    ok = True
    for m in MATERIALS_DATA:
        ok = self_test_material(m, True) and ok
    return ok


def main(argv: Optional[List[str]] = None) -> int:
    global TEMP_K
    try:
        parse_args(list(argv if argv is not None else sys.argv[1:]))
    except Exception as ex:
        print("FATAL: " + str(ex))
        return 1

    try:
        if not self_test_all():
            return 2
        if SELFTEST_ONLY:
            return 0

        need_zos = math.isnan(TEMP_K) and RANGE_SPEC is None
        if not need_zos and FILE_PATH is None:
            generate(os.getcwd(), selected_materials(), temperatures())
            return 0

        # Need OpticStudio: either for T0 from environment, or -file load
        try:
            zos_root = discover_zos_root()
        except Exception as ex:
            print("FATAL: failed to locate an OpticStudio installation.  " + str(ex))
            return 1

        session = None
        try:
            ZOSAPI = bootstrap_zosapi(zos_root, say=say)
            session = connect_zos(ZOSAPI, FILE_PATH, say=say, zos_root=zos_root)
            sys_ = session.TheSystem
            env = sys_.SystemData.Environment
            t_k = TEMP_K if not math.isnan(TEMP_K) else float(env.Temperature) + 273.15
            say(
                "working temperature: {0:.2f} K ({1:.2f} C){2}".format(
                    t_k,
                    t_k - 273.15,
                    "" if not math.isnan(TEMP_K) else " (from system environment)",
                )
            )
            if math.isnan(TEMP_K) and abs(float(env.Pressure)) > 1e-9:
                say("WARNING: system environment pressure is not 0 atm - CHARMS catalogs carry")
            say("absolute (vacuum) indices; set pressure to 0 for consistent tracing.")
            directory = (
                os.path.dirname(sys_.SystemFile)
                if sys_.SystemFile
                else os.path.expanduser("~")
            )
            TEMP_K = t_k
            generate(directory or os.getcwd(), selected_materials(), temperatures())
            return 0
        finally:
            if session is not None:
                session.close()
    except Exception as ex:
        print("FATAL: " + str(ex))
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())

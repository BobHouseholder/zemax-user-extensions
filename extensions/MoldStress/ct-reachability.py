"""
C_t OPTICAL-MEMORY REACHABILITY CHECK                              2026-08-19
=============================================================================

Run BEFORE building the optical-memory rewrite, because this project has once
already swept six candidate fixes inside a box smaller than its target.

THE QUESTION. Can ONE retardation-time law tau(T), driving the source's own
optical-memory function, satisfy the three things the rewrite is meant to fix?

    C_t = C_g + C_m * [1 - exp(-(t/tau)^beta)]      Eq (4), beta = 0.72 for PC
    R. Wimberger-Friedl, Int. Polym. Process. 11(4) 373 (1996)

THE METHOD. A stress applied for a duration D leaves a frozen-in birefringence
proportional to how much C GREW while it acted, so the fraction retained of the
full melt response is

    f = 1 - exp(-(D/tau)^beta)    =>    D/tau = (-ln(1-f))^(1/beta)

For each target f is known (a published measurement over a ceiling computed
from the source's own equations) and D is known (from the process), so tau is
DETERMINED - not fitted. The check then asks where the model's EXISTING
relaxation law, lambda = eta0(T)/G with eta0 from Cross-WLF, takes that value,
and compares it with the temperature at which the mechanism actually acts.

THE RESULT, and it is the reason the rewrite was not built on this evidence:

    target                            f req'd   D (s)   tau req'd  lambda hits
                                                                   it at
    A  case 4 surface max (pressure)   0.0151   10.00      3333 s   177.4 C
    B  case 4 core plateau, 0.5 MPa    0.2500    3.00      16.93 s  218.8 C
    B  case 4 core plateau, 1.0 MPa    0.1250    3.00      49.16 s  207.5 C
    B  case 4 core plateau, 2.0 MPa    0.0625    3.00      134.9 s  198.4 C
    C  case 2 in-plane peak (flow)     0.5148    0.50     0.7842 s  231.7 C

    A acts at ~145 C (a layer vitrifying, i.e. AT Tg)   -> off by +32 C
    B acts at ~150 C (just above Tg)                    -> off by +48 to +69 C
    C acts at ~275 C (the melt during filling)          -> off by -43 C

REACHABLE IN ARCHITECTURE, BLOCKED ON AN INPUT.

  1. The two PC targets agree with each other and both point the SAME way: the
     model's lambda near Tg is far too long. At 145 C lambda is 7.5e7 s where A
     needs 3.3e3 s - about four orders of magnitude. A and B are mutually
     consistent, since a tau(T) falling 25-200x over 5 C is ordinary WLF
     behaviour near Tg. So one tau(T) can serve both.

  2. But it CANNOT be this model's lambda(T). That lambda is a melt viscosity
     divided by a plateau modulus, evaluated far below the range it was fitted
     in. The optical retardation time near Tg is a different quantity, and this
     check measures how different: ~2e4 x at Tg.

  3. Case C is a different polymer (ZEONEX 480R, not PC) so its -43 C does not
     contradict 1 and 2 - each material carries its own tau(T). It does say the
     same rewrite will not fix case 2 for free.

SO THE MISSING INPUT IS tau(T) NEAR Tg, and the source names where it lives:
its refs [10] Progr. Polym. Sci. 20, 369 (1995) and [27] Wimberger-Friedl &
de Bruin, Rheol. Acta 30, 419 (1991). Building C_t on the existing lambda would
produce a channel wrong by four orders of magnitude at the temperature that
matters most, which is worse than not having it.

A NOTE ON THE ARITHMETIC, because it cost a wrong answer first time. The WLF
form INVERTS below the Vogel temperature D2 - A2 (93.4 C for PC): the
denominator changes sign and eta0 collapses instead of diverging. Bracketing
the inversion from 50 C therefore put the low end in the inverted region and
made every target read "out of range". The shipped model guards this correctly
- `if (a2 + dT <= 1e-9) return 1e12;` in FillField.CrossWlf - the defect was in
this script alone.
"""
import math

BETA = 0.72

PC = dict(D1=1.5e13, D2_K=418.15, A1=26.0, A2=51.6, G=2.0e5, Tg=145.0, Cm=4000e-12)
R480 = dict(D1=8.0e12, D2_K=411.15, A1=27.0, A2=51.6, G=2.8e5, Tg=138.0, Cm=1700e-12)


def retention_to_ratio(f, beta=BETA):
    """D/tau required to retain fraction f of the full melt response."""
    if not (0.0 < f < 1.0):
        return float('nan')
    return (-math.log(1.0 - f)) ** (1.0 / beta)


def eta0(T_C, D1, D2_K, A1, A2):
    """Cross-WLF zero-shear viscosity, the model's own law, with the model's
    own below-Vogel guard."""
    u = (T_C + 273.15) - D2_K
    if A2 + u <= 1e-9:
        return 1e12
    return D1 * math.exp(-A1 * u / (A2 + u))


def lam(T_C, m):
    return eta0(T_C, m['D1'], m['D2_K'], m['A1'], m['A2']) / m['G']


def temp_for_lambda(target_tau, m, hi=400.0):
    """Invert lambda(T) = target_tau. Brackets ABOVE the Vogel temperature."""
    lo = (m['D2_K'] - m['A2']) - 273.15 + 10.0
    f = lambda T: math.log(lam(T, m)) - math.log(target_tau)
    if f(lo) * f(hi) > 0:
        return None
    for _ in range(200):
        mid = 0.5 * (lo + hi)
        if f(lo) * f(mid) <= 0:
            hi = mid
        else:
            lo = mid
    return 0.5 * (lo + hi)


def main():
    print(__doc__)
    targets = []

    # (A) case 4 surface maximum - pressure-induced, source Eq (8) ceiling.
    p_MPa, nu = 80.0, 0.37                      # Fig. 9 change-over peak; PC
    dev = p_MPa * (1 - 2 * nu) / (1 - nu)
    targets.append(("A  case 4 surface max (pressure)",
                    20e-4 / (PC['Cm'] * dev * 1e6), 10.0, PC, PC['Tg']))

    # (B) case 4 core plateau - thermal orientation above Tg. The stress it
    # acts on is not given by the source, so it is swept rather than assumed.
    for sig in (0.5, 1.0, 2.0):
        targets.append(("B  case 4 core plateau, %.1f MPa" % sig,
                        5e-4 / (PC['Cm'] * sig * 1e6), 3.0, PC, PC['Tg'] + 5))

    # (C) case 2 in-plane peak - flow orientation, short by 3.6x on 0.143.
    targets.append(("C  case 2 in-plane peak (flow)",
                    0.143 * 3.6, 0.50, R480, 275.0))

    print("  %-34s %8s %7s %11s %11s %9s" %
          ("target", "f req'd", "D (s)", "tau req'd", "lambda at", "delta"))
    print("  " + "-" * 84)
    for label, f, D, m, T_act in targets:
        if not (0.0 < f < 1.0):
            print("  %-34s %8.4f   UNREACHABLE: needs more than the full melt "
                  "response" % (label, f))
            continue
        tau = D / retention_to_ratio(f)
        T_lam = temp_for_lambda(tau, m)
        print("  %-34s %8.4f %7.2f %11.4g %11s %9s" %
              (label, f, D, tau,
               ("%.1f C" % T_lam) if T_lam else "out of range",
               ("%+.0f C" % (T_lam - T_act)) if T_lam else "-"))

    print()
    print("  lambda(Tg) for PC   = %.3g s   (target A needs 3.3e3 s)"
          % lam(PC['Tg'], PC))
    print("  ratio               = %.3g x too long"
          % (lam(PC['Tg'], PC) / 3333.0))


if __name__ == '__main__':
    main()

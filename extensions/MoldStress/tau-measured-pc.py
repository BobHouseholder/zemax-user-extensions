"""
The MEASURED tau(T) for PC, from Wimberger-Friedl & de Bruin, Rheol. Acta 30
(1991) = chapter 2.2 of the open-access thesis already on disk.

    beta      = 0.72 +- 0.01   (temperature INDEPENDENT, KWW fit, thesis p.52)
    tau(140C) = 166 s          (thesis p.53)
    WLF       : C1 = 9.0, C2 = 31.2 C, T0 = 140 C   (thesis p.51 Fig.5, p.52)
    C_g       = 1.0e-10 /Pa    (set in the fit; 0.89-1.2e-10 measured 20-120 C)
    C_m       = 4.8-5.5e-9 /Pa (shear creep just above Tg)

Stated validity floor, thesis p.52: below about 148 C the measured shift factors
fall BELOW the WLF line, because the volumetric glass transition is passed and
the excess free volume gives SHORTER relaxation times than equilibrium WLF
predicts. So WLF extrapolated below ~148 C over-estimates tau.
"""
import math

TAU0, T0, C1, C2, BETA = 166.0, 140.0, 9.0, 31.2, 0.72


def tau(T_C):
    return TAU0 * 10.0 ** (-C1 * (T_C - T0) / (C2 + (T_C - T0)))


def eta0_model(T_C):                       # the model's Cross-WLF, PC row
    u = (T_C + 273.15) - 418.15
    if 51.6 + u <= 1e-9:
        return 1e12
    return 1.5e13 * math.exp(-26.0 * u / (51.6 + u))


def lam_model(T_C):
    return eta0_model(T_C) / 2.0e5


print(__doc__)
print("  %6s %14s %16s %12s" % ("T (C)", "tau measured", "lambda model", "ratio"))
print("  " + "-" * 52)
for T in (130, 135, 140, 145, 148, 150, 160, 175):
    tm, lm = tau(T), lam_model(T)
    flag = "  <- below the stated 148 C floor" if T < 148 else ""
    print("  %6.0f %14.4g %16.4g %12.3g%s" % (T, tm, lm, lm / tm, flag))

print()
print("  AGAINST THE REACHABILITY CHECK (commit 1b309de):")
print("    target A needed tau = 3333 s at ~145 C")
print("    measured tau(145 C) = %.2f s" % tau(145))
print("    -> the measurement is %.0fx SHORTER than the single-tau requirement,"
      % (3333.0 / tau(145)))
print("       and the model's lambda is %.2eX LONGER than the measurement."
      % (lam_model(145) / tau(145)))
print()
print("    So the single-tau simplification in that check is superseded. With a")
print("    real tau(T) the retention must be integrated over the vitrification")
print("    window, where tau rises by orders of magnitude as the layer cools -")
print("    which is what reduced time is for, and the model already has it.")
print()
D = 10.0
print("  Retention f = 1 - exp(-(D/tau)^beta) for a D = %.0f s packing window:" % D)
for T in (140, 145, 148, 150, 155):
    f = 1.0 - math.exp(-((D / tau(T)) ** BETA))
    print("    T = %3.0f C   tau = %8.3f s   f = %.4f" % (T, tau(T), f))

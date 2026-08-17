using System;
using System.Collections.Generic;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// Material data for the moulding estimate.
    ///
    /// Every stress-optical constant carries its source and the units it was
    /// published in. That is not decoration: the published literature quotes
    /// quantities of the same NAME in both 1e-10 and 1e-12 per pascal, two orders
    /// of magnitude apart, so a number without its source and its unit cannot be
    /// checked and must not be shipped.
    ///
    /// KEY DISTINCTION, and the most expensive thing to get wrong here.
    /// There are TWO stress-optical coefficients per polymer:
    ///   Cglass - the glassy (photoelastic) coefficient, which acts on real
    ///            residual stress in the finished part. This is what belongs in
    ///            an OpticStudio catalog as K11/K12, because that is what STAR
    ///            multiplies an imported stress tensor by.
    ///   Cmelt  - the melt/rubbery coefficient, which relates FROZEN-IN
    ///            ORIENTATION to birefringence. It is 2-3 orders of magnitude
    ///            larger, and it never belongs in the catalog: orientation is not
    ///            a stress in the finished part.
    /// Using one where the other belongs is a factor of several hundred and still
    /// produces a plausible-looking map.
    /// </summary>
    internal sealed class Polymer
    {
        public string Name;                 // catalog name written to the AGF
        public string Description;

        // --- optical -------------------------------------------------------
        public double Nd;                   // index at d-line
        public double Vd;                   // Abbe number
        public double WavelengthUm;         // wavelength the coefficients apply at

        // --- stress-optical, GLASSY, in Brewster (1e-12 /Pa == 1e-6 mm^2/N) --
        // Zemax writes K, K11, K12 in 1e-6 mm^2/N, which is numerically the same
        // unit, and defines K = K12 - K11 (they are the additive inverses of the
        // photoelastic coefficients). See OpticStudio User Manual p.1410.
        public double KGlassBrewster;
        public double K11Brewster;          // parallel to stress
        public string KSource;
        public bool Provisional;            // true => not measured for this grade

        // --- stress-optical, MELT, in Brewster ------------------------------
        public double CMeltBrewster;
        public string CMeltSource;

        // --- thermal / rheological -----------------------------------------
        public double TgC;                  // glass transition, degC
        public double MeltTempC;            // typical melt temperature, degC
        public double MoldTempC;            // typical mould wall temperature, degC
        public double DiffusivityMm2PerS;   // thermal diffusivity
        public double CtePerK;              // linear thermal expansion, glassy
        public double ModulusMPa;           // Young's modulus, glassy
        public double PoissonRatio;
        public double DensityGPerCm3;

        /// <summary>
        /// Plateau (rubbery) modulus of the melt, Pa. With the Maxwell relation
        /// lambda = eta / G it sets the relaxation time, and therefore how much of
        /// the shear history a layer still remembers when it freezes.
        ///
        /// Derived rather than assumed, from a MEASURED entanglement molecular
        /// weight: G_N0 = rho*R*T/Me. For TOPAS COC, Me = 16-18 kDa (time-
        /// temperature superposition plus TMA on Topas 5013 and 5014CL, Chinese
        /// Journal of Polymer Bulletin 2024, doi 10.14028/j.cnki.1003-3726.2024.24.255),
        /// giving 2.8e5 Pa at 290 C - against the generic 2.0e5 Pa this used to
        /// carry, i.e. 1.4x, not the factor of ~7 the depth deficit would need.
        ///
        /// So the last unmeasured constant in the chain is now measured, and it
        /// does NOT explain the depth profile. Per-polymer values below are set
        /// where Me is known and left at the generic figure where it is not.
        /// </summary>
        public double MeltModulusPa = 2.0e5;

        /// <summary>
        /// Shallow copy with the processing temperatures overridden.
        ///
        /// MeltTempC and MoldTempC on the table entries are TYPICAL processing
        /// conditions for the grade, which is what a user moulding their own part
        /// wants as a default. A reference case reproducing a published
        /// experiment needs THAT experiment's conditions, and must not get them
        /// by editing the shared default every other caller reads.
        /// </summary>
        public Polymer WithProcessTemps(double meltTempC, double moldTempC)
        {
            var q = (Polymer)MemberwiseClone();
            q.MeltTempC = meltTempC;
            q.MoldTempC = moldTempC;
            return q;
        }

        /// <summary>
        /// Shallow copy with the thermal expansion zeroed, which switches the
        /// THERMAL birefringence channel off and leaves the flow channel alone.
        /// Used as a positive control on the two-channel depth comparison: with
        /// CTE = 0 the total must collapse EXACTLY onto the flow-only numbers.
        /// </summary>
        public Polymer WithZeroCte()
        {
            var q = (Polymer)MemberwiseClone();
            q.CtePerK = 0.0;
            return q;
        }

        // --- Cross-WLF (SI: Pa.s, Pa, K) ------------------------------------
        public double CrossN;               // power-law index
        public double CrossTauStarPa;       // tau*
        public double WlfD1PaS, WlfD2K, WlfD3KPerPa, WlfA1, WlfA2K;

        public double K12Brewster { get { return K11Brewster + KGlassBrewster; } }
    }

    internal static class Polymers
    {
        /// <summary>
        /// The shipped set. Values are PROVISIONAL where marked: they are
        /// representative of the polymer family, not measured for the specific
        /// grade, and the tool says so on every run and in the catalog header.
        /// The reason they can ship at all is that the criterion this feeds is a
        /// factor-of-two agreement against a published case, not a tolerance.
        /// </summary>
        public static readonly Polymer[] All = new[]
        {
            new Polymer {
                Name = "MS_PMMA", Description = "Poly(methyl methacrylate), generic optical grade",
                Nd = 1.4917, Vd = 57.4, WavelengthUm = 0.5876,
                KGlassBrewster = -4.5, K11Brewster = 2.3,
                KSource = "glassy stress-optic coefficient, PMMA fibre measurements, -4.5 to -1.5e-12 /Pa (Aston, polymer optical fibre); most negative value taken",
                Provisional = true,
                CMeltBrewster = -1200.0,
                CMeltSource = "melt-state stress-optical coefficient for PMMA, order 1e-9 /Pa from rheo-optical literature",
                TgC = 105, MeltTempC = 250, MoldTempC = 70,
                DiffusivityMm2PerS = 0.11, CtePerK = 70e-6, ModulusMPa = 3200, PoissonRatio = 0.37,
                DensityGPerCm3 = 1.19,
                CrossN = 0.25, CrossTauStarPa = 1.0e5,
                WlfD1PaS = 3.0e12, WlfD2K = 378.15, WlfD3KPerPa = 0.0, WlfA1 = 28.0, WlfA2K = 51.6,
            },
            new Polymer {
                Name = "MS_POLYCARB", Description = "Bisphenol-A polycarbonate, optical grade",
                Nd = 1.5855, Vd = 29.9, WavelengthUm = 0.5876,
                KGlassBrewster = 72.0, K11Brewster = -24.0,
                KSource = "glassy photoelastic constant of PC, ~72e-12 /Pa, standard value in the photoelasticity literature",
                Provisional = true,
                CMeltBrewster = 4000.0,
                CMeltSource = "melt stress-optical coefficient of PC, order 4e-9 /Pa; note vendor-adjacent sources quote ~90e-10 /Pa for 'the' coefficient without stating the regime",
                TgC = 145, MeltTempC = 300, MoldTempC = 100,
                DiffusivityMm2PerS = 0.13, CtePerK = 65e-6, ModulusMPa = 2400, PoissonRatio = 0.37,
                DensityGPerCm3 = 1.20,
                CrossN = 0.18, CrossTauStarPa = 1.5e5,
                WlfD1PaS = 1.5e13, WlfD2K = 418.15, WlfD3KPerPa = 0.0, WlfA1 = 26.0, WlfA2K = 51.6,
            },
            new Polymer {
                Name = "MS_POLYSTYR", Description = "Polystyrene, optical grade",
                Nd = 1.5905, Vd = 30.9, WavelengthUm = 0.5876,
                KGlassBrewster = -10.0, K11Brewster = 5.0,
                KSource = "glassy stress-optic coefficient of PS, order -10e-12 /Pa; sign is negative and is known to invert with frequency near Tg",
                Provisional = true,
                CMeltBrewster = -4800.0,
                CMeltSource = "melt stress-optical coefficient of PS, order -4.8e-9 /Pa, rheo-optical literature",
                TgC = 100, MeltTempC = 230, MoldTempC = 50,
                DiffusivityMm2PerS = 0.09, CtePerK = 70e-6, ModulusMPa = 3300, PoissonRatio = 0.35,
                DensityGPerCm3 = 1.05,
                CrossN = 0.28, CrossTauStarPa = 3.0e4,
                WlfD1PaS = 3.5e11, WlfD2K = 373.15, WlfD3KPerPa = 0.0, WlfA1 = 25.0, WlfA2K = 51.6,
            },
            new Polymer {
                Name = "MS_COC_TOPAS6017", Description = "Cyclic olefin copolymer, TOPAS 6017-class",
                Nd = 1.5300, Vd = 56.0, WavelengthUm = 0.5876,
                // MEASURED, 2026-08-15. Kim, Yoon & Kornfield, "Measurement of
                // Stress-Optical Coefficients of COC's with Different
                // Composition", Key Engineering Materials 326-328 (2006) 183:
                // glassy -8 to -9 Br, melt +920 to +1160 Br, for COCs of 60-70
                // mol% norbornene. Corroborated for TOPAS 5013 by Korea-Australia
                // Rheology Journal (2012), which reports a melt extreme of
                // +1.0e-9 /Pa = +1000 Br.
                //
                // This REPLACES a provisional +5.0 Br glassy value that was wrong
                // in SIGN as well as magnitude. The measured difference K12 - K11
                // is what is quoted; the split between K11 and K12 individually is
                // not measured and is assumed in N-BK7's proportion.
                KGlassBrewster = -8.5, K11Brewster = 2.43,
                KSource = "MEASURED: Kim, Yoon & Kornfield, Key Eng. Mater. 326-328 (2006) 183 - glassy -8 to -9 Br; midpoint taken. K11/K12 split assumed",
                Provisional = false,
                CMeltBrewster = 1000.0,
                CMeltSource = "MEASURED: same source, melt +920 to +1160 Br; corroborated at +1.0e-9 /Pa for TOPAS 5013, Korea-Australia Rheol. J. (2012)",
                MeltModulusPa = 2.8e5,
                TgC = 178, MeltTempC = 290, MoldTempC = 120,
                DiffusivityMm2PerS = 0.10, CtePerK = 60e-6, ModulusMPa = 3000, PoissonRatio = 0.36,
                DensityGPerCm3 = 1.02,
                CrossN = 0.29, CrossTauStarPa = 4.0e4,
                WlfD1PaS = 8.0e12, WlfD2K = 451.15, WlfD3KPerPa = 0.0, WlfA1 = 27.0, WlfA2K = 51.6,
            },
        };

        public static Polymer ByName(string name)
        {
            var p = All.FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null)
                throw new ArgumentException(
                    "unknown polymer '" + name + "'; known: " +
                    string.Join(", ", All.Select(x => x.Name)));
            return p;
        }

        /// <summary>
        /// Refuses a material that cannot be written honestly. Every entry must
        /// carry a source string, K must equal K12 - K11 by construction, and the
        /// melt coefficient must be larger in magnitude than the glassy one - if
        /// it is not, the two have been swapped, which is the error this whole
        /// class exists to prevent.
        /// </summary>
        public static List<string> Validate()
        {
            var errs = new List<string>();
            foreach (var p in All)
            {
                if (string.IsNullOrWhiteSpace(p.KSource))
                    errs.Add(p.Name + ": no source for the glassy coefficient");
                if (string.IsNullOrWhiteSpace(p.CMeltSource))
                    errs.Add(p.Name + ": no source for the melt coefficient");
                if (Math.Abs(p.K12Brewster - p.K11Brewster - p.KGlassBrewster) > 1e-9)
                    errs.Add(p.Name + ": K != K12 - K11");
                if (Math.Abs(p.CMeltBrewster) <= Math.Abs(p.KGlassBrewster))
                    errs.Add(p.Name + ": |Cmelt| <= |Cglass| - the coefficients look swapped");
                // A same-sign check used to live here, on the reasoning that a
                // sign disagreement meant the two had been swapped. MEASUREMENT
                // REFUTED IT: Kim, Yoon & Kornfield report COC at -8 to -9 Br
                // glassy and +920 to +1160 Br in the melt - genuinely opposite,
                // and polystyrene is documented to invert through Tg as well. The
                // guard would have REFUSED the correct data, which is the worst
                // thing a guard can do. Magnitude separation is the real check and
                // it stays.
                if (p.TgC >= p.MeltTempC) errs.Add(p.Name + ": Tg is not below the melt temperature");
                if (p.MoldTempC >= p.TgC) errs.Add(p.Name + ": mould wall is not below Tg");
            }
            return errs;
        }
    }
}

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

        /// <summary>
        /// Set when a published value for this constant CONFLICTS with the one
        /// carried here, in sign or by more than a factor of two. Null when the
        /// constant is simply measured, and null when it is merely BORROWED -
        /// borrowing is recorded in CMeltSource and is a different, milder thing.
        ///
        /// It exists because a contested constant is invisible at the point of
        /// use: a number with a source string beside it looks settled whether or
        /// not anyone disagrees with it, and the aliasing feature makes borrowing
        /// one onto another grade a single command-line flag.
        /// </summary>
        public string CMeltContested;

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
                CMeltBrewster = -30.0,
                CMeltSource = "MEASURED: Wimberger-Friedl, Rheol. Acta 30 (1991) 329-340, read via US Patent 9720155 Table 1 - PMMA -30 Br at 20 C above Tg. That is the SAME convention and the SAME paper this model's polycarbonate entry uses (+3000~4000 Br, carried here as +4000), so the two are now consistent",
                CMeltContested = "CORRECTED 2026-08-18, from an unsourced -1200 Br to a sourced "
                    + "-30 Br - a factor of forty. The old source string read 'order 1e-9 /Pa from "
                    + "rheo-optical literature' and named no paper; -30 Br is 3e-11 /Pa, two orders "
                    + "below what that asserted. WHAT REMAINS GENUINELY OPEN, and it is a property "
                    + "of PMMA rather than of this entry: its stress-optical coefficient CHANGES "
                    + "SIGN near 144 C (Wimberger-Friedl 1991, 'the peculiar rheo-optical behaviour "
                    + "of bisphenol A-polycarbonate and polymethylmethacrylate'). This model carries "
                    + "ONE constant while integrating orientation from a 250 C melt down through a "
                    + "105 C Tg, a range that straddles the inversion, so no single number is right "
                    + "across it. -30 Br is used because frozen-in orientation is locked at "
                    + "VITRIFICATION, not at peak melt, and 20 C above Tg is that regime - which is "
                    + "also why the '20 C above Tg' convention is the one materials data for this "
                    + "purpose is reported in. PMMA remains the least trustworthy entry in this "
                    + "table, and a temperature-resolved C(T) would replace the constant rather "
                    + "than re-tune it.",
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
                CMeltSource = "MEASURED: Kim, Yoon & Kornfield, Key Eng. Mater. 326-328 (2006) 183, melt +920 to +1160 Br. A corroboration from TOPAS 5013 was claimed here until 2026-08-18 and was WITHDRAWN - see CMeltContested. This value now rests on its own measurement alone, which is what it always should have done",
                CMeltContested = "RESOLVED 2026-08-18, and the conflict was real rather than a "
                    + "convention artifact. US Patent 9720155 Table 1 was read verbatim: it lists "
                    + "TOPAS 5013 at -700 Br, NEGATIVE, measured 20 C above Tg (so genuinely the "
                    + "rubbery coefficient), citing Min & Yoon (2012), under the plain definition "
                    + "dn = C.dsigma. The table is internally consistent and standard - it gives "
                    + "BPA-PC +3000~4000 Br and PMMA -30 Br, both from Wimberger-Friedl (1991) and "
                    + "both independently corroborated - so a reversed pairing convention does NOT "
                    + "explain the sign. THIS ENTRY'S OLD CLAIM THAT 5013 CORROBORATES IT AT "
                    + "+1.0e-9 /Pa IS WITHDRAWN AS FALSE. What survives: 6017's own measurement is "
                    + "untouched, and the two need not agree, because TOPAS is an ethylene-NORBORNENE "
                    + "copolymer whose C_R composition-dependence is reported as UNCLEAR (Dynamic "
                    + "birefringence of cyclic olefin copolymers - for ethylene-cyclododecene C_R "
                    + "falls with cyclic content; for ethylene-norbornene no systematic trend was "
                    + "found). 5013 and 6017 differ in norbornene content - 5013 Tg ~134 C against "
                    + "6017's 178 C - so a sign difference between grades of this family is not "
                    + "excluded by anything measured. DO NOT borrow this value onto another COC "
                    + "grade.",
                MeltModulusPa = 2.8e5,
                TgC = 178, MeltTempC = 290, MoldTempC = 120,
                DiffusivityMm2PerS = 0.10, CtePerK = 60e-6, ModulusMPa = 3000, PoissonRatio = 0.36,
                DensityGPerCm3 = 1.02,
                CrossN = 0.29, CrossTauStarPa = 4.0e4,
                WlfD1PaS = 8.0e12, WlfD2K = 451.15, WlfD3KPerPa = 0.0, WlfA1 = 27.0, WlfA2K = 51.6,
            },
            new Polymer {
                Name = "MS_COP_ZEONEX480R",
                Description = "Zeon ZEONEX 480R cyclo-olefin POLYMER, optical moulding grade",
                // MEASURED for this grade, from the vendor datasheet (corroborated
                // across UL Prospector, MatWeb and Material Data Center):
                Nd = 1.525, Vd = 56.0, WavelengthUm = 0.5876,
                TgC = 138,                       // <-- 40 K below TOPAS 6017's 178
                MeltTempC = 275, MoldTempC = 124,
                // BORROWED from the measured TOPAS 6017 COC - same cyclo-olefin
                // family, different grade and different polymerisation route (COP
                // by ring-opening metathesis, COC by chain copolymerisation).
                // These are the numbers to replace first if this grade matters.
                KGlassBrewster = -8.5, K11Brewster = 2.43,
                KSource = "BORROWED from TOPAS 6017 (Kim, Yoon & Kornfield 2006). A photoelastic coefficient of 5.0e-12 /Pa is quoted for 'Zeonor 480R' in a USPTO document that could NOT be retrieved to verify - not used, recorded as a lead only. NOTE this GLASSY constant is what Zeon's low-birefringence marketing refers to ('ultra-low photoelastic constant'), and it drives the THERMAL channel, not the melt orientation that sets the in-plane peak",
                Provisional = true,
                CMeltBrewster = 1700.0,
                CMeltSource = "REBASED 2026-08-18, then CORRECTED the same day from +1000 to +1700 Br. The rebase moved the JUSTIFICATION off TOPAS and onto Inoue but LEFT TOPAS'S NUMBER IN PLACE - so the entry cited one source and carried another's value, which is the cross-family borrowing the rebase existed to remove. The value is now Inoue's: it now rests on Inoue et al., Polymer Journal (1995), which measured C_R ~1700 Br, positive, for amorphous polyolefins with a five-membered ring in the MAIN CHAIN - i.e. ROMP-made cyclic olefin POLYMERS, which is exactly what ZEONEX is. That is the same family as this grade, where the old justification borrowed across families from a COC",
                CMeltContested = "NARROWED 2026-08-18. The earlier worry was that a reported "
                    + "-700 Br for TOPAS 5013 would refute Inoue's structure-insensitivity and "
                    + "destroy this borrowing. Reading US Patent 9720155 Table 1 confirms the -700 "
                    + "is real, but it does NOT reach this entry: Inoue's set is ROMP polymers with "
                    + "a five-membered ring in the main chain (COP - Zeonex, Zeonor), while TOPAS is "
                    + "an ethylene-norbornene chain copolymer (COC). They are different families, "
                    + "and the structure-insensitivity claim was only ever made about the first. So "
                    + "this grade being a COP is what supports it, and routing the justification "
                    + "through a COC was the actual defect. WHAT REMAINS OPEN: no melt coefficient "
                    + "has been measured for 480R itself, and Inoue's family value is +1700 Br "
                    + "against the +1000 used here - a factor of 1.7 that would make case 2's "
                    + "in-plane over-prediction WORSE, not better.",
                MeltModulusPa = 2.8e5,
                DiffusivityMm2PerS = 0.10, CtePerK = 60e-6, ModulusMPa = 2100, PoissonRatio = 0.36,
                DensityGPerCm3 = 1.01,
                CrossN = 0.29, CrossTauStarPa = 4.0e4,
                WlfD1PaS = 8.0e12, WlfD2K = 411.15, WlfD3KPerPa = 0.0, WlfA1 = 27.0, WlfA2K = 51.6,
            },
        };

        /// <summary>
        /// Alias map from a REAL catalogue material name to one of this table's
        /// entries, populated from `-materials NAME=POLYMER`.
        ///
        /// Why this exists: every entry here is named MS_*, and no real lens file
        /// uses those names. Without aliasing the tool can only run on systems
        /// built from its own catalogue, which is to say on nothing a user
        /// actually has. `-materials E48R` alone was worse than useless - it made
        /// FindElements match the surface and then ByName threw.
        ///
        /// An alias is a BORROWING of constants, not an identification, and every
        /// caller that resolves one is expected to say so in its output.
        /// </summary>
        public static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolve through the alias map; returns null if unknown.</summary>
        public static string AliasTarget(string name)
        {
            string t;
            return Aliases.TryGetValue((name ?? "").Trim(), out t) ? t : null;
        }

        public static Polymer ByName(string name)
        {
            string alias = AliasTarget(name);
            if (alias != null) name = alias;

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
        /// <summary>
        /// EVERY COEFFICIENT AGAINST ITS PUBLISHED VALUE - a check that reads a
        /// NUMBER, not a comparison.
        ///
        /// Written 2026-08-18 after PMMA's melt coefficient was found to be 40x
        /// its only sourced value, unsourced, and to have survived every existing
        /// test. The reason it survived is instructive: the material tests were
        /// ORDINAL. "Short-cycle COC exceeds normal-cycle PMMA" passed before and
        /// after the correction - the ratio moved from about 1.9x to 76.8x while
        /// staying on the same side of the inequality. A test that only asks
        /// which of two numbers is larger cannot see an error in either of them.
        ///
        /// THE EXPECTED VALUES BELOW ARE THE PUBLISHED ONES, not the ones in the
        /// table above. That is the whole point and it is what makes this fail:
        /// if someone edits a constant, this test breaks until they also change
        /// the published figure beside it, which they cannot honestly do without
        /// a source. Comparing the table against itself would pass forever.
        ///
        /// Bands are deliberately generous - they guard the ORDER and the SIGN,
        /// which is where the real errors have been, not the third digit.
        /// </summary>
        /// <summary>
        /// The one place the band comparison lives, so the checks and their
        /// controls cannot diverge.
        /// </summary>
        private static bool InPublishedBand(double got, double lo, double hi)
        {
            return got >= lo && got <= hi;
        }

        public static void SelfCheckValues()
        {
            // material, quantity, published centre, low, high, citation
            var expect = new[]
            {
                new object[] { "MS_PMMA", "C_melt", -30.0, -90.0, -10.0,
                    "Wimberger-Friedl, Rheol. Acta 30 (1991) 329, via US9720155 Table 1, 20 C above Tg" },
                new object[] { "MS_PMMA", "K_glass", -4.5, -6.0, -1.0,
                    "PMMA optical-fibre measurements (Aston), -4.5 to -1.5e-12 /Pa" },
                new object[] { "MS_POLYCARB", "C_melt", 3500.0, 2500.0, 4500.0,
                    "US9720155 Table 1 / Wimberger-Friedl 1991: BPA-PC +3000~4000 Br at 20 C above Tg" },
                new object[] { "MS_POLYCARB", "K_glass", 78.0, 60.0, 95.0,
                    "BPA-PC photoelastic ~78-82 Br, matched-order convention (n_P-n_Q = C(sig_P-sig_Q))" },
                new object[] { "MS_POLYSTYR", "C_melt", -4725.0, -5500.0, -4000.0,
                    "Venerus et al., J. Rheol. 43(3) 795 (1999): PS melt -4.65 to -4.8e-9 /Pa" },
                new object[] { "MS_COC_TOPAS6017", "C_melt", 1040.0, 850.0, 1250.0,
                    "MEASURED: Kim, Yoon & Kornfield, Key Eng. Mater. 326-328 (2006) 183, +920 to +1160 Br" },
                new object[] { "MS_COC_TOPAS6017", "K_glass", -8.5, -10.0, -7.0,
                    "MEASURED: same source, glassy -8 to -9 Br" },
                // Borrowed, so this is checked against the FAMILY value it is
                // justified by, not against the grade it was copied from - which
                // would be circular. Inoue's +1700 Br sits outside the band's
                // centre on purpose: the band records that the number in use is
                // low against the family, which is a known open item.
                new object[] { "MS_COP_ZEONEX480R", "C_melt", 1700.0, 900.0, 2500.0,
                    "Inoue et al., Polymer J. (1995): ROMP cyclic olefin polymers ~+1700 Br - the family this grade belongs to. Still not measured for 480R itself" },
            };

            foreach (var e in expect)
            {
                var pm = ByName((string)e[0]);
                string q = (string)e[1];
                double got = q == "C_melt" ? pm.CMeltBrewster : pm.KGlassBrewster;
                double lo = (double)e[3], hi = (double)e[4];
                double pub = (double)e[2];

                // Sign first and separately. A sign error is the failure mode this
                // table has actually produced twice, and a magnitude band that
                // brackets zero would hide one.
                SelfTest.Check(e[0] + " " + q + " has the published SIGN",
                    Math.Sign(got) == Math.Sign(pub),
                    string.Format("{0:F1} Br vs published {1:F1} Br", got, pub));

                SelfTest.Check(e[0] + " " + q + " is within its published band",
                    InPublishedBand(got, lo, hi),
                    string.Format("{0:F1} Br, band [{1:F1}, {2:F1}] - {3}", got, lo, hi, e[5]));
            }

            // CONTROLS, BOTH DIRECTIONS, THROUGH THE SAME FUNCTION THE LOOP USES.
            // The first version of these re-wrote the comparison as a literal
            // expression, which tests that a number is outside an interval - not
            // that this check would have caught anything. Two copies of one rule
            // is how they drift apart; InPublishedBand is now called by the loop
            // above and by both controls, so a defect in it fails here.
            SelfTest.Check("the band rejects PMMA's old unsourced -1200 Br (control)",
                !InPublishedBand(-1200.0, -90.0, -10.0),
                "-1200 Br must fall outside [-90, -10]");

            SelfTest.Check("the band accepts the published -30 Br (control)",
                InPublishedBand(-30.0, -90.0, -10.0),
                "-30 Br must fall inside [-90, -10]");

            // And the sign test must discriminate too, or a sign flip passes.
            SelfTest.Check("the sign test rejects a flipped value (control)",
                Math.Sign(30.0) != Math.Sign(-30.0),
                "+30 Br must not satisfy a published -30 Br");
        }

        /// <summary>
        /// The contested-constant guard, tested rather than asserted.
        ///
        /// A warning that has never been observed to fire is an untested remedy,
        /// and this one fires on a code path (aliasing a real lens material) that
        /// no reference case exercises. So the flag is checked directly, in BOTH
        /// directions: the two cyclo-olefins must carry it, and a material whose
        /// melt coefficient nobody disputes must NOT - otherwise the field could
        /// be set on everything and the check would pass while meaning nothing.
        /// </summary>
        /// <summary>
        /// Post-ejection cooling is provably zero only when the part leaves the
        /// mould fully vitrified. Checked in BOTH directions, because a guard
        /// that never fires and a guard that always fires look identical from a
        /// green suite.
        /// </summary>
        public static void SelfCheckEjection()
        {
            foreach (var name in new[] { "MS_COC_TOPAS6017", "MS_COP_ZEONEX480R", "MS_POLYCARB" })
            {
                var p = ByName(name);
                SelfTest.Check(name + " default mould is below Tg (post-ejection provably zero)",
                    p.MoldTempC < p.TgC,
                    string.Format("mould {0:F0} C, Tg {1:F0} C", p.MoldTempC, p.TgC));
            }

            // THE INVARIANT THE PROOF RESTS ON: a mould at or above Tg must be
            // REFUSED, not merely warned about. That refusal is what makes
            // "post-ejection cooling contributes zero" unconditional rather than
            // a property of the cases we happen to run. Exercised for real -
            // FreezeHistory.Build is called and must throw.
            var hot = ByName("MS_COC_TOPAS6017").WithProcessTemps(290.0, 200.0);
            bool refused = false;
            try
            {
                FreezeHistory.Build(1.5, hot,
                    new Process { FillTimeS = 1.0, PackTimeS = 1.0 }, 21, 210);
            }
            catch (ArgumentException) { refused = true; }
            SelfTest.Check("a mould at or above Tg is REFUSED, not accepted",
                refused,
                string.Format("mould {0:F0} C vs Tg {1:F0} C - the refusal is what makes "
                              + "post-ejection cooling provably zero", hot.MoldTempC, hot.TgC));

            // ... and the same call must SUCCEED below Tg, or the test above
            // passes because the call throws for some unrelated reason.
            bool accepted = true;
            try
            {
                FreezeHistory.Build(1.5, ByName("MS_COC_TOPAS6017").WithProcessTemps(280.0, 150.0),
                    new Process { FillTimeS = 1.0, PackTimeS = 1.0 }, 21, 210);
            }
            catch (ArgumentException) { accepted = false; }
            SelfTest.Check("a mould below Tg is accepted (control)",
                accepted, "mould 150 C vs Tg 178 C must build normally");
        }

        public static void SelfCheckContested()
        {
            foreach (var name in new[] { "MS_COC_TOPAS6017", "MS_COP_ZEONEX480R" })
            {
                var p = ByName(name);
                SelfTest.Check("melt coefficient of " + name + " is marked contested",
                    !string.IsNullOrEmpty(p.CMeltContested),
                    p.CMeltBrewster.ToString("F0") + " Br");
                SelfTest.Check("the contest on " + name + " names the conflicting value",
                    p.CMeltContested != null && p.CMeltContested.Contains("-700"),
                    "must name the -700 Br report so it can be checked");
            }

            // CONTROL: the field must discriminate. PC's melt coefficient is
            // corroborated (+3 to +4e-9 /Pa, Wimberger-Friedl 1991) and nothing
            // found disputes it, so it must come back clean. If this ever fails,
            // the flag has been applied indiscriminately and means nothing.
            // PMMA's melt coefficient was -1200 Br and unsourced until 2026-08-18,
            // forty times the only sourced value. Pinned against the source so an
            // edit back to the old order of magnitude fails loudly. The bound is
            // generous (a factor of 3) because the underlying quantity really is
            // temperature-sensitive - it is guarding the ORDER, not the digit.
            var pmma = ByName("MS_PMMA");
            SelfTest.Check("PMMA melt coefficient is the sourced order, not the old -1200 Br",
                Math.Abs(pmma.CMeltBrewster) > 10.0 && Math.Abs(pmma.CMeltBrewster) < 90.0,
                pmma.CMeltBrewster.ToString("F0") + " Br (source: -30 Br, 20 C above Tg)");

            // NAMED EXPLICITLY, via ByName so a rename THROWS rather than
            // skipping. The first version searched for a name containing "PC";
            // the entry is called MS_POLYCARB, so it matched nothing, the control
            // silently did not run, and the suite still reported all-pass - a
            // control that iterates zero times, which is the exact failure this
            // control was written to catch, committed inside the control itself.
            var pc = ByName("MS_POLYCARB");
            SelfTest.Check("an uncontested material is NOT flagged (control)",
                string.IsNullOrEmpty(pc.CMeltContested),
                pc.Name + " at " + pc.CMeltBrewster.ToString("F0") + " Br");
        }

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

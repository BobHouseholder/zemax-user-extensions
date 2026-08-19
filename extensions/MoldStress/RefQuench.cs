using System;
using System.Globalization;

namespace MoldStress
{
    /// <summary>
    /// THIRD REFERENCE CASE - a FREE QUENCH, which tests the THERMAL channel on
    /// its own for the first time.
    ///
    /// WHY THIS CASE EXISTS. Cases 1 and 2 are mouldings, so every number they
    /// produce is flow and thermal together. The thermal channel has therefore
    /// only ever been tested by NULLING it - set CTE to zero, watch the answer
    /// collapse - which proves it is wired up and contributes, not that it is
    /// right. A quench has no flow at all, so it isolates one channel.
    ///
    /// It is also structurally cheap to test, and that is not a coincidence:
    /// `Channels.ThermalProfile` reads only the freeze history's TrefC and the
    /// depth grid. It never touches the fill field. So this case needs no gate,
    /// no flow rate and no fill time - none of the unsourced inputs that make
    /// case 2's magnitude clause untestable can reach it.
    ///
    /// SOURCE. R. Wimberger-Friedl, "Orientation, Stress and Density
    /// Distributions in Injection-Moulded Amorphous Polymers Determined by
    /// Optical Techniques", PhD thesis, TU Eindhoven (1991), DOI
    /// 10.6100/IR364279, chapter 3.2 - open access, read in full.
    /// Bisphenol-A polycarbonate, 2 mm sheet, quenched from Ti to a bath at Tc.
    ///
    /// PUBLISHED PROFILE (Ti = 160 C, Tc = 60 C), delta-n against z/d where
    /// z/d = 0 is the mid-plane and 1 is the surface:
    ///   core     +5 to +9  e-4   (tension - the last material to solidify)
    ///   surface  -15 to -20 e-4  (compression - frozen first, then squeezed)
    ///   zero crossing at z/d ~ 0.5-0.8, moving out as Ti rises
    ///
    /// THE NUMBERS ARE READ OFF SCANNED FIGURE AXES. The thesis publishes no
    /// table, so these are +-10-15% graphical estimates. Every band below is set
    /// wide enough for that and says so; a tight band on a hand-read figure
    /// would be false precision.
    ///
    /// CRITERION, REGISTERED 2026-08-18 BEFORE THE CASE WAS RUN ONCE, and not
    /// adjustable afterwards:
    ///   (a) SIGN REVERSAL. The thermal profile must change sign between the
    ///       mid-plane and the surface. This is the whole shape of the result
    ///       and no free parameter in this model can produce it by accident -
    ///       it comes from force and moment balance or it does not appear.
    ///   (b) DIRECTION. Core in TENSION (positive delta-n for a positive
    ///       photoelastic coefficient), surface in COMPRESSION.
    ///   (c) ZERO CROSSING at z/d in [0.40, 0.90]. Published 0.5-0.8, widened
    ///       for the graphical read and for the spread the paper shows across
    ///       initial temperatures. Note a parabolic self-equilibrating profile
    ///       crosses at 1/sqrt(3) = 0.577, inside the published range - so this
    ///       clause is checking the balance, not curve-fitting.
    ///   (d) SHAPE RATIO |surface| / |core| in [1.0, 8.0]. Published 1.7-4.0
    ///       (-15/9 to -20/5); a factor of two either side.
    ///   (e) MAGNITUDE. |surface| within a FACTOR OF 3 of 17.5e-4, i.e.
    ///       [5.8e-4, 52.5e-4]. Deliberately the loosest clause - see below.
    ///   (f) NULL. With CTE = 0 the entire profile must be identically zero.
    ///   (g) CONTROL ON THE NULL. With CTE restored it must be non-zero, or (f)
    ///       passes on a channel that is dead rather than on one that responds.
    ///
    /// WHAT THIS CASE CANNOT SETTLE, stated before running so it cannot be
    /// reached for afterwards as an excuse. The thesis attributes a large part
    /// of the quench birefringence to frozen-in ORIENTATION from tensile
    /// cooling stresses above Tg - not to classical elastic residual stress.
    /// This model computes only the elastic stress and multiplies it by the
    /// photoelastic coefficient. So the SHAPE clauses (a)-(d) are the real test
    /// and the MAGNITUDE clause (e) is weak by construction: passing (e) would
    /// be pleasant, failing it would implicate a missing mechanism rather than a
    /// wrong constant. If (a)-(d) pass and (e) fails, the honest reading is that
    /// the channel gets the structure right and is missing the orientational
    /// half - which is exactly what the source says is there.
    /// </summary>
    internal static class RefQuench
    {
        public const double PublishedCoreDn = 7.0e-4;        // +5 to +9 e-4
        public const double PublishedSurfaceDn = -17.5e-4;   // -15 to -20 e-4
        public const double CrossingLo = 0.40, CrossingHi = 0.90;
        public const double RatioLo = 1.0, RatioHi = 8.0;
        public const double MagnitudeFactor = 3.0;

        public const double ThicknessMm = 2.0;
        public const double InitialTempC = 160.0;
        public const double BathTempC = 60.0;

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            Action<string> say = Console.WriteLine;

            // Refuse a flag this mode does not read. The helper existed for one
            // build without being CALLED anywhere - a guard that cannot fire,
            // which is the same defect it was written to prevent.
            int badForMode = Program.RejectFlagsNotReadBy(
                args, new[] { "-nz", "-ti", "-tc", "-snapshot" }, "-refquench");
            if (badForMode != 0) return badForMode;

            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;

            // Ti and Tc are exposed because the SOURCE reports a trend in them -
            // the zero crossing moves outward as Ti rises, z/d ~ 0.3 at low Ti to
            // ~0.85 at high Ti - and a case that can only be run at one condition
            // cannot test a trend. The registered clauses above are evaluated at
            // the registered 160/60; these flags are for the trend diagnostic.
            Incremental = !Program.Has(args, "-snapshot");
            say("");
            double ti = Program.Value(args, "-ti", InitialTempC);
            double tc = Program.Value(args, "-tc", BathTempC);
            var p = Polymers.ByName("MS_POLYCARB").WithProcessTemps(ti, tc);
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 0.0, PackTimeS = 0.0 };

            say("MoldStress - third reference case: FREE QUENCH (thermal channel alone)");
            say("  " + Program.ScopeLabel);
            say(string.Format(ci,
                "  bisphenol-A polycarbonate, {0:F1} mm sheet, {1:F0} C -> {2:F0} C bath",
                ThicknessMm, ti, tc));
            say("  Wimberger-Friedl, PhD thesis, TU Eindhoven (1991), ch. 3.2");
            say(string.Format(ci, "  grid: nz {0}, thermal construction: {1}", nz,
                Incremental ? "INCREMENTAL (front sweeps)" : "snapshot (single instant)"));
            say("");

            double[] dn = Profile(p, proc, nz);
            var freeze = FreezeHistory.Build(ThicknessMm, p, proc, nz, 10 * nz);
            double half = 0.5 * ThicknessMm;

            // Sampled at the mid-plane and just inside the surface. Not AT the
            // surface: the outermost node is the boundary itself, where the
            // profile is evaluated on a one-sided stencil.
            int kMid = nz / 2;
            int kSurf = nz - 2;
            double core = dn[kMid], surf = dn[kSurf];

            say("  depth profile of the THERMAL channel:");
            say("     z/d      delta-n");
            for (int f = 0; f <= 10; f += 1)
            {
                int k = kMid + (int)Math.Round((nz - 1 - kMid) * f / 10.0);
                if (k > nz - 1) k = nz - 1;
                say(string.Format(ci, "    {0:F2}     {1:E3}", Math.Abs(freeze.Z[k]) / half, dn[k]));
            }
            say("");

            // ---- (a) and (b) -------------------------------------------------
            bool reverses = Math.Sign(core) != Math.Sign(surf) && core != 0.0 && surf != 0.0;
            bool direction = core > 0.0 && surf < 0.0;
            say(string.Format(ci,
                "  (a) core {0:E3}, surface {1:E3} - sign reversal  =>  {2}",
                core, surf, reverses ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (b) core in tension and surface in compression  =>  {0}",
                direction ? "PASS" : "FAIL"));

            // ---- (c) zero crossing ------------------------------------------
            double crossing = double.NaN;
            for (int k = kMid; k < nz - 1; k++)
            {
                if (dn[k] == 0.0) { crossing = Math.Abs(freeze.Z[k]) / half; break; }
                if (Math.Sign(dn[k]) != Math.Sign(dn[k + 1]))
                {
                    double z0 = Math.Abs(freeze.Z[k]) / half, z1 = Math.Abs(freeze.Z[k + 1]) / half;
                    double t = Math.Abs(dn[k]) / Math.Max(Math.Abs(dn[k]) + Math.Abs(dn[k + 1]), 1e-30);
                    crossing = z0 + t * (z1 - z0);
                    break;
                }
            }
            bool crossOk = !double.IsNaN(crossing) && crossing >= CrossingLo && crossing <= CrossingHi;
            say(string.Format(ci,
                "  (c) zero crossing at z/d {0:F3}, published 0.5-0.8, band [{1:F2}, {2:F2}]  =>  {3}",
                crossing, CrossingLo, CrossingHi, crossOk ? "PASS" : "FAIL"));

            // ---- (d) shape ratio --------------------------------------------
            double ratio = Math.Abs(core) > 0 ? Math.Abs(surf) / Math.Abs(core) : double.PositiveInfinity;
            bool ratioOk = ratio >= RatioLo && ratio <= RatioHi;
            say(string.Format(ci,
                "  (d) |surface|/|core| {0:F2}, published 1.7-4.0, band [{1:F1}, {2:F1}]  =>  {3}",
                ratio, RatioLo, RatioHi, ratioOk ? "PASS" : "FAIL"));

            // ---- (e) magnitude ----------------------------------------------
            double lo = Math.Abs(PublishedSurfaceDn) / MagnitudeFactor;
            double hi = Math.Abs(PublishedSurfaceDn) * MagnitudeFactor;
            bool magOk = Math.Abs(surf) >= lo && Math.Abs(surf) <= hi;
            say(string.Format(ci,
                "  (e) |surface| {0:E3} against published {1:E3}, factor of {2:F0} band [{3:E2}, {4:E2}]  =>  {5}",
                Math.Abs(surf), Math.Abs(PublishedSurfaceDn), MagnitudeFactor, lo, hi,
                magOk ? "PASS" : "FAIL"));
            say("      (weak by construction - the source attributes much of the quench");
            say("       birefringence to frozen-in ORIENTATION above Tg, which this");
            say("       channel does not model. Registered as weak before running.)");

            // ---- (f) null and (g) its control --------------------------------
            double[] dnNull = Profile(p.WithZeroCte(), proc, nz);
            double biggestNull = 0.0;
            for (int k = 0; k < nz; k++) biggestNull = Math.Max(biggestNull, Math.Abs(dnNull[k]));
            bool nullOk = biggestNull <= 1e-15;

            double biggestLive = 0.0;
            for (int k = 0; k < nz; k++) biggestLive = Math.Max(biggestLive, Math.Abs(dn[k]));
            bool controlOk = biggestLive > 1e-12;

            say(string.Format(ci,
                "  (f) null: CTE = 0 collapses the profile, largest |delta-n| {0:E3}  =>  {1}",
                biggestNull, nullOk ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (g) control on the null: CTE restored gives {0:E3}  =>  {1}",
                biggestLive, controlOk ? "PASS" : "FAIL"));

            // ---- TREND DIAGNOSTIC, NOT PART OF THE REGISTERED VERDICT --------
            //
            // The source reports the zero crossing moving OUTWARD as the initial
            // temperature rises - z/d ~0.3 at low Ti to ~0.85 at high Ti, and it
            // names Ti as the dominant control parameter, more so than the bath
            // temperature. That is a TREND, and the registered criterion is a
            // single condition, so the criterion cannot see it either way.
            //
            // It is printed and NOT scored. Registering a clause after seeing its
            // result is moving the bar, which this project does not do. It is
            // recorded here, in the README and on the goal board as an open
            // failure, so that a future criterion can register it BEFORE the next
            // change to this channel.
            if (Math.Abs(ti - InitialTempC) < 1e-9)
            {
                say("");
                say("  TREND DIAGNOSTIC (not scored - see the note in this file):");
                say("    Ti      crossing z/d");
                double first = double.NaN, last = double.NaN;
                foreach (double tiTry in new[] { 150.0, 160.0, 170.0, 180.0 })
                {
                    double[] d2 = Profile(
                        Polymers.ByName("MS_POLYCARB").WithProcessTemps(tiTry, tc), proc, nz);
                    var fz2 = FreezeHistory.Build(ThicknessMm,
                        Polymers.ByName("MS_POLYCARB").WithProcessTemps(tiTry, tc), proc, nz, 10 * nz);
                    double x = double.NaN;
                    for (int k = nz / 2; k < nz - 1; k++)
                        if (Math.Sign(d2[k]) != Math.Sign(d2[k + 1]))
                        {
                            double z0 = Math.Abs(fz2.Z[k]) / half, z1 = Math.Abs(fz2.Z[k + 1]) / half;
                            double t = Math.Abs(d2[k]) / Math.Max(Math.Abs(d2[k]) + Math.Abs(d2[k + 1]), 1e-30);
                            x = z0 + t * (z1 - z0); break;
                        }
                    if (double.IsNaN(first)) first = x;
                    last = x;
                    say(string.Format(ci, "    {0,3:F0} C   {1:F3}", tiTry, x));
                }
                say(string.Format(ci,
                    "    published: crossing moves OUTWARD with Ti, ~0.3 -> ~0.85."));
                say(string.Format(ci,
                    "    model:     {0:F3} -> {1:F3}, i.e. {2} and {3:F0}x too small a span.",
                    first, last, last > first ? "outward" : "INWARD (wrong direction)",
                    0.55 / Math.Max(Math.Abs(last - first), 1e-9)));
                say("    THIS IS A REAL FAILURE the registered criterion cannot see,");
                say("    and the incremental construction did NOT fix it - which is a");
                say("    sharper result than the original miss.");
                say("    The snapshot construction was nearly scale-invariant, so its");
                say("    crossing could not move. The incremental one CAN move it - with");
                say("    the post-vitrification cooling excluded it spans 0.375 -> 0.874,");
                say("    matching the published 0.3 -> 0.85 almost exactly. Including that");
                say("    cooling, which the values demand, flattens it again, because");
                say("    every layer then cools from about Tg to the bath REGARDLESS OF Ti.");
                say("    So the elastic stress is dominated by a Ti-independent term, and");
                say("    the Ti-dependence must live in the mechanism the source names and");
                say("    this channel lacks: frozen-in ORIENTATION from stresses ABOVE Tg,");
                say("    where time-above-Tg is exactly what Ti controls. That is a second");
                say("    thermal channel, not a correction to this one.");
            }

            bool met = reverses && direction && crossOk && ratioOk && magOk && nullOk && controlOk;
            say("");
            say("  VERDICT: " + (met
                ? "the registered criterion is MET"
                : "the registered criterion is NOT met"));
            return met ? 0 : 2;
        }

        /// <summary>
        /// The thermal channel alone: the freeze solve's freeze-off temperature
        /// profile, through force and moment balance, times the photoelastic
        /// coefficient. No fill field is constructed, because ThermalProfile
        /// does not read one.
        /// </summary>
        /// <summary>Which thermal construction the case is exercising.</summary>
        private static bool Incremental;

        private static double[] Profile(Polymer p, Process proc, int nz)
        {
            var freeze = FreezeHistory.Build(ThicknessMm, p, proc, nz, 10 * nz);
            double eOver1MinusNu = p.ModulusMPa / (1.0 - p.PoissonRatio);
            double[] sigma = Incremental
                ? Channels.ThermalProfileIncremental(
                      freeze.Z, freeze.TimeGridS, freeze.TempHistoryC,
                      p.TgC, p.CtePerK, eOver1MinusNu, p.MoldTempC)
                : Channels.ThermalProfile(
                      freeze.TrefC, freeze.Z, eOver1MinusNu * p.CtePerK);

            var dn = new double[nz];
            for (int k = 0; k < nz; k++) dn[k] = p.KGlassBrewster * 1e-6 * sigma[k];
            return dn;
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MoldStress
{
    /// <summary>
    /// The registered reference case, run as a falsifier rather than as a demo.
    ///
    /// CASE (registered 2026-08-15, before any prediction existed):
    ///   TOPAS 6017S-04 cyclic olefin copolymer, 100 x 100 x 1.5 mm plate,
    ///   film gate along one edge, polarimetry at 594 nm.
    ///   In-plane birefringence peaks at 1.2e-4 AT THE GATE and falls roughly
    ///   linearly to zero at the far edge.
    ///   Through-thickness: 10e-4 at the surface near the gate against 1.8e-4 in
    ///   the core.
    ///   Source: Polymers 2024, 16(2), 168, open access.
    ///
    /// CRITERION, fixed at intake and not adjustable here:
    ///   (a) predicted peak within a FACTOR OF 2 of 1.2e-4, computed over the
    ///       traced footprint - which for a polarimeter reading through a plate
    ///       is the THICKNESS AVERAGE of the in-plane birefringence, since that
    ///       is the quantity the instrument integrates.
    ///   (b) the maximum must fall on the GATE side.
    ///   (b) NULL: move the gate to the opposite edge and the maximum must move
    ///       with it. A map that does not rotate makes the gate model decorative.
    ///
    /// PROCESS CONDITIONS, declared before the run and not tuned afterwards:
    ///   fill 1.0 s, pack 60 MPa for 3 s, melt and mould temperatures taken from
    ///   the polymer's own defaults (290 C / 120 C). Nothing in this file was
    ///   changed after seeing an answer.
    /// </summary>
    internal static class RefCase
    {
        public const double PublishedPeakDn = 1.2e-4;
        public const double PublishedSurfaceDn = 10.0e-4;
        public const double PublishedCoreDn = 1.8e-4;
        public const double FactorBar = 2.0;

        public static int Run(string[] args)
        {
            var log = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); log.AppendLine(s); };
            var ci = CultureInfo.InvariantCulture;

            var p = Polymers.ByName("MS_COC_TOPAS6017");
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 60.0, PackTimeS = 3.0 };

            say("MoldStress - registered reference case");
            say("  " + Program.ScopeLabel);
            say("  TOPAS 6017S-04, 100 x 100 x 1.5 mm plate, film gate on one edge");
            say(string.Format(ci, "  process: fill {0:F1} s, pack {1:F0} MPa for {2:F0} s, " +
                "melt {3:F0} C, mould {4:F0} C",
                proc.FillTimeS, proc.PackPressureMPa, proc.PackTimeS, p.MeltTempC, p.MoldTempC));
            say("");

            double[] gateProfile = null, farProfile = null;
            double gatePeak = 0, farPeak = 0;
            double surfaceDn = 0, coreDn = 0;

            foreach (double azimuth in new[] { 0.0, 180.0 })
            {
                var e = new MouldedElement
                {
                    FrontSurface = 1, BackSurface = 2, Material = p.Name,
                    CentreThicknessMm = 1.5, SemiDiameterMm = 50.0,
                    FrontRadiusMm = 0, BackRadiusMm = 0,
                };
                e.EdgeThicknessMm = e.ThicknessAt(e.SemiDiameterMm);
                e.Gate = new GateSpec
                {
                    Kind = GateKind.FilmEdge, AzimuthDeg = azimuth,
                    WidthMm = 100.0, ThicknessMm = 0.9, IsDefault = false,
                };
                e.PartingLineZMm = Gating.DefaultPartingLineZ(e);

                var fill = FillField.Build(e, p, proc, 101);
                var freeze = FreezeHistory.Build(e.CentreThicknessMm, p, proc, 41);
                var ch = Channels.Build(e, p, proc, fill, freeze);

                // Thickness average of |dn| at each station along the flow - what
                // a polarimeter reading through the plate actually integrates.
                int ns = ch.S.Length, nz = freeze.NodeCount;
                var avg = new double[ns];
                for (int i = 0; i < ns; i++)
                {
                    double sum = 0;
                    for (int k = 0; k < nz; k++) sum += Math.Abs(ch.DnFlow[i, k]);
                    avg[i] = sum / nz;
                }

                if (azimuth == 0.0)
                {
                    gateProfile = avg;
                    gatePeak = avg[0];
                    for (int k = 0; k < nz; k++)
                    {
                        double v = Math.Abs(ch.DnFlow[0, k]);
                        if (Math.Abs(freeze.Z[k]) > 0.45 * e.CentreThicknessMm)
                            surfaceDn = Math.Max(surfaceDn, v);
                    }
                    coreDn = Math.Abs(ch.DnFlow[0, nz / 2]);
                    // the core is exactly on the mid-plane where shear vanishes,
                    // so report the mid-third average instead - that is what a
                    // prism coupler sampling the core sees.
                    double s2 = 0; int c2 = 0;
                    for (int k = 0; k < nz; k++)
                        if (Math.Abs(freeze.Z[k]) < 0.17 * e.CentreThicknessMm)
                        { s2 += Math.Abs(ch.DnFlow[0, k]); c2++; }
                    coreDn = c2 > 0 ? s2 / c2 : coreDn;

                    say("  distance from gate    thickness-averaged |dn|");
                    for (int i = 0; i < ns; i += ns / 10)
                        say(string.Format(ci, "    {0,6:F1} mm            {1:E3}", ch.S[i], avg[i]));
                    say(string.Format(ci, "    {0,6:F1} mm            {1:E3}",
                        ch.S[ns - 1], avg[ns - 1]));
                    say("");
                }
                else
                {
                    farProfile = avg;
                    farPeak = avg[0];
                }
            }

            // --- (a) the number ------------------------------------------------
            double ratio = gatePeak / PublishedPeakDn;
            bool withinFactor = ratio <= FactorBar && ratio >= 1.0 / FactorBar;
            say(string.Format(ci, "  (a) predicted peak {0:E3} against published {1:E3} - ratio {2:F2}x",
                gatePeak, PublishedPeakDn, ratio));
            say(string.Format(ci, "      criterion: within a factor of {0:F0}  =>  {1}",
                FactorBar, withinFactor ? "PASS" : "FAIL"));

            // --- (a) the shape -------------------------------------------------
            int nsG = gateProfile.Length;
            bool decays = gateProfile[0] > gateProfile[nsG - 1];
            double decayRatio = gateProfile[nsG - 1] / Math.Max(gateProfile[0], 1e-30);
            say(string.Format(ci,
                "      maximum on the gate side, falling to {0:P1} of it at the far edge  =>  {1}",
                decayRatio, decays ? "PASS" : "FAIL"));

            // --- through-thickness shape --------------------------------------
            double pubRatio = PublishedSurfaceDn / PublishedCoreDn;
            double gotRatio = surfaceDn / Math.Max(coreDn, 1e-30);
            say(string.Format(ci,
                "      surface/core {0:F2} against published {1:F2} (diagnostic, not gated)",
                gotRatio, pubRatio));

            // --- (b) the null --------------------------------------------------
            // Both runs are symmetric plates, so the profile against distance from
            // the gate must be IDENTICAL while the profile in part coordinates
            // must reverse. The test is that the peak follows the gate.
            //
            // AND IT CANNOT DISCRIMINATE IF THE PROFILE IS FLAT. A null that
            // compares two identical flat fields passes for the same reason a
            // broken instrument passes: there is nothing in the treatment for it
            // to detect. Reported as INCONCLUSIVE rather than as a pass.
            //
            // Comparing PEAK MAGNITUDES is also too weak - by symmetry they are
            // equal whether or not anything moved. The test that means something
            // is in PART coordinates: where on the plate does the maximum sit?
            bool hasStructure = decayRatio < 0.99;
            int nGrid = gateProfile.Length;
            int argMaxGate0 = 0, argMaxGate180 = 0;
            for (int i = 0; i < nGrid; i++)
            {
                if (gateProfile[i] > gateProfile[argMaxGate0]) argMaxGate0 = i;
                if (farProfile[i] > farProfile[argMaxGate180]) argMaxGate180 = i;
            }
            // Gate at azimuth 0 enters at x = 0; gate at 180 enters at x = 100,
            // so its distance-from-gate axis runs the other way across the part.
            double xMax0 = 100.0 * argMaxGate0 / (nGrid - 1.0);
            double xMax180 = 100.0 - 100.0 * argMaxGate180 / (nGrid - 1.0);
            bool moved = Math.Abs(xMax180 - xMax0) > 50.0;

            say("");
            say(string.Format(ci,
                "  (b) null: gate moved to the opposite edge, peak {0:E3} vs {1:E3}",
                farPeak, gatePeak));
            if (!hasStructure)
                say("      INCONCLUSIVE - the predicted field has no spatial structure, so " +
                    "moving the gate cannot move anything. This null cannot fail here.");
            else
                say(string.Format(ci,
                    "      maximum sits at x = {0:F0} mm with the gate at 0 deg and x = {1:F0} mm " +
                    "with it at 180 deg  =>  {2}", xMax0, xMax180, moved ? "PASS" : "FAIL"));
            bool nullMoves = hasStructure && moved;

            // --- (a) non-triviality -------------------------------------------
            say("");
            say("  the 10x-over-repeat-spread clause does not bite here: the estimator is");
            say("  deterministic, so the repeat spread is exactly zero and any non-zero");
            say("  result clears it. Recorded as a criterion that cannot fail rather than");
            say("  as one that passed.");

            bool pass = withinFactor && decays && nullMoves;
            say("");
            say("  VERDICT: " + (pass ? "the registered criterion is MET"
                                      : "the registered criterion is NOT met"));

            string outPath = Program.Value(args, "-out")
                ?? Path.Combine(Path.GetTempPath(), "moldstress_refcase.txt");
            File.WriteAllText(outPath, log.ToString());
            Console.WriteLine("  written to " + outPath);
            return pass ? 0 : 2;
        }
    }
}

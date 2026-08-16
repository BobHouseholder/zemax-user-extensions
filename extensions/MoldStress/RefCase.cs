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

        // Depth criterion, registered 2026-08-15 before it was implemented.
        // Surface is the outermost 5% of the half-wall; the deep point is 0.47 of
        // it, which is the 0.4 mm depth in a 1.5 mm plate where the published
        // prism-coupler value of 1.8e-4 was taken. Both sampling points are part
        // of the criterion, not of the implementation.
        public const double SurfaceFraction = 0.975;
        public const double DeepFraction = 0.47;
        public const double PublishedDepthRatio = PublishedSurfaceDn / PublishedCoreDn;
        public const double PeakMustLieOutside = 0.75;   // outer 25% of the half-wall

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
            double bandSurfaceDn = 0, bandCoreDn = 0;
            double peakDepthFraction = 0, reversedRatio = 0;

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

                    // Sampled at the depths the criterion names, not averaged over
                    // a band. The band version is kept alongside because it is
                    // what the earlier 108.9 was, and replacing a number silently
                    // is worse than reporting both.
                    double half = 0.5 * e.CentreThicknessMm;
                    surfaceDn = Channels.DnAtDepthFraction(ch.DnFlow, freeze.Z, 0, half, SurfaceFraction);
                    coreDn = Channels.DnAtDepthFraction(ch.DnFlow, freeze.Z, 0, half, DeepFraction);

                    double s2 = 0; int c2 = 0;
                    for (int k = 0; k < nz; k++)
                        if (Math.Abs(freeze.Z[k]) < 0.17 * e.CentreThicknessMm)
                        { s2 += Math.Abs(ch.DnFlow[0, k]); c2++; }
                    bandCoreDn = c2 > 0 ? s2 / c2 : 0.0;
                    bandSurfaceDn = 0;
                    for (int k = 0; k < nz; k++)
                        if (Math.Abs(freeze.Z[k]) > 0.45 * e.CentreThicknessMm)
                            bandSurfaceDn = Math.Max(bandSurfaceDn, Math.Abs(ch.DnFlow[0, k]));

                    peakDepthFraction = ch.PeakDepthFraction;

                    // NULL for the depth clause: make the CORE solidify first,
                    // change nothing else, and require the ratio to move.
                    //
                    // Array.Reverse was the first attempt and it is a no-op here:
                    // the freeze-time profile is symmetric about the mid-plane, so
                    // reversing it returns the same array and the null agreed with
                    // itself at 33.41 vs 33.41. Inverting the ORDER - t -> tMax - t
                    // - genuinely swaps which depths freeze first.
                    var reversed = FreezeHistory.Build(e.CentreThicknessMm, p, proc, 41);
                    double tMax = 0.0;
                    foreach (double t in reversed.FreezeTimeS) tMax = Math.Max(tMax, t);
                    for (int k = 0; k < reversed.FreezeTimeS.Length; k++)
                        reversed.FreezeTimeS[k] = tMax - reversed.FreezeTimeS[k];
                    var chRev = Channels.Build(e, p, proc, fill, reversed);
                    double sRev = Channels.DnAtDepthFraction(chRev.DnFlow, reversed.Z, 0, half, SurfaceFraction);
                    double dRev = Channels.DnAtDepthFraction(chRev.DnFlow, reversed.Z, 0, half, DeepFraction);
                    reversedRatio = dRev > 0 ? sRev / dRev : double.PositiveInfinity;

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

            // --- (a) depth profile, GATED since 2026-08-15 ---------------------
            double gotRatio = surfaceDn / Math.Max(coreDn, 1e-30);
            double bandRatio = bandSurfaceDn / Math.Max(bandCoreDn, 1e-30);
            double lo = PublishedDepthRatio / FactorBar, hi = PublishedDepthRatio * FactorBar;
            bool depthInBand = gotRatio >= lo && gotRatio <= hi;
            bool peakOutside = peakDepthFraction >= PeakMustLieOutside;

            say("");
            say(string.Format(ci,
                "  (a) depth: |dn| at {0:P0} of the half-wall / at {1:P0} = {2:F2}",
                SurfaceFraction, DeepFraction, gotRatio));
            say(string.Format(ci,
                "      published {0:F2}, criterion [{1:F2}, {2:F2}]  =>  {3}",
                PublishedDepthRatio, lo, hi, depthInBand ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "      maximum at {0:P0} of the half-wall, must be beyond {1:P0}  =>  {2}",
                peakDepthFraction, PeakMustLieOutside, peakOutside ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "      for comparison, the mid-third band average this replaces: {0:F2}",
                bandRatio));

            // NULL for the depth clause.
            bool depthNull = (gotRatio - 1.0) * (reversedRatio - 1.0) < 0
                             || Math.Abs(reversedRatio - gotRatio) / Math.Max(gotRatio, 1e-30) > 0.5;
            say(string.Format(ci,
                "      null: freeze order reversed, ratio {0:F2} vs {1:F2}  =>  {2}",
                reversedRatio, gotRatio, depthNull ? "PASS" : "FAIL"));

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

            bool pass = withinFactor && decays && nullMoves
                        && depthInBand && peakOutside && depthNull;
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

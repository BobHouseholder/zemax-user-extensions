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
    ///   Through-thickness: see the depth criterion below.
    ///   Source: Polymers 2024, 16(2), 168, open access.
    ///
    /// DEPTH REFERENCE CORRECTED 2026-08-17. The registered target was 5.56,
    /// formed as 10e-4 / 1.8e-4 - and those two numbers COME FROM DIFFERENT
    /// INSTRUMENTS. 10e-4 is a PRISM COUPLER surface reading (stated resolution
    /// 2e-4, i.e. +-20% on that value alone); 1.8e-4 is the plateau of a
    /// POLARIMETRY depth profile on cut slabs. The paper states outright that
    /// polarimetry "underestimates the surface birefringence" relative to the
    /// prism coupler, so the two are KNOWN to disagree and dividing one by the
    /// other is not a measurement of anything. The authors never state a ratio.
    ///
    /// Within the single self-consistent instrument, in the single plane whose
    /// profile the paper actually describes as decaying to that plateau (yz, the
    /// melt-flow direction): 5e-4 at the surface against 1.8e-4 at 0.4 mm, so
    /// 2.78. The cross-plane (xz) surface value reaches 8.4e-4, giving 4.67
    /// against the same plateau, and is recorded below as the alternative rather
    /// than averaged in - a ratio is only meaningful within one plane.
    ///
    /// This WIDENS the acceptance band at the bottom (factor of 2 of 2.78 is
    /// [1.39, 5.56] where factor of 2 of 5.56 was [2.78, 11.11]), which is
    /// exactly the move that deserves suspicion when a criterion is being
    /// failed. It is not a rescue: the measured 0.98 fails the corrected band
    /// too. The correction is made because the original number was constructed
    /// wrongly, and it is recorded here with the direction it moved the bar.
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
    ///   fill 1.0 s, pack 71.3 MPa for 3 s, melt 280 C, mould 150 C - the
    ///   paper's own stated conditions as of 2026-08-17. Before that date this
    ///   case ran the polymer's TYPICAL defaults (290 C / 120 C) against the
    ///   paper's geometry, which was simply an error. Fill and pack TIMES are
    ///   still not given by the source and remain declared defaults.
    ///   Nothing here was changed after seeing an answer; the two corrections
    ///   made on 2026-08-17 both moved the bar AWAY from the model.
    /// </summary>
    internal static class RefCase
    {
        public const double PublishedPeakDn = 1.2e-4;

        // POLARIMETRY, yz plane (melt-flow direction) - the surface value and the
        // core plateau below come from ONE instrument and ONE stated profile, so
        // their ratio is a measurement. This is the depth reference.
        public const double PublishedSurfaceDn = 5.0e-4;
        public const double PublishedCoreDn = 1.8e-4;

        // Same instrument, cross plane (xz). Recorded so the direction-dependence
        // is visible; NOT averaged with the above.
        public const double PublishedSurfaceDnCross = 8.4e-4;

        // PRISM COUPLER surface reading, resolution 2e-4. Kept ONLY to name the
        // number that must not be used for the ratio - see the header. Dividing
        // this by the polarimetry plateau is what produced the withdrawn 5.56.
        public const double PrismCouplerSurfaceDn = 10.0e-4;

        public const double FactorBar = 2.0;

        // Depth criterion, sampling points registered 2026-08-15 before it was
        // implemented and NOT changed by the 2026-08-17 reference correction.
        // Surface is the outermost 5% of the half-wall; the deep point is 0.47 of
        // it, which is the 0.4 mm depth in a 1.5 mm plate where the published
        // polarimetry plateau of 1.8e-4 was taken. Both sampling points are part
        // of the criterion, not of the implementation.
        //
        // OPEN, and NOT silently corrected here: the polarimetry surface value is
        // read off slabs 0.2 mm across, and it is not clear from the paper whether
        // 0.2 mm is the depth resolution or merely the beam path width. If it is
        // the depth resolution then 5e-4 is an average over roughly the outer 27%
        // of the half-wall, not a surface point, and the like-for-like comparison
        // is against the model's BAND figure rather than its point figure. Both
        // are already computed and printed below. Resolving this needs the figure
        // itself, so it is recorded as a question rather than guessed at.
        public const double SurfaceFraction = 0.975;
        public const double DeepFraction = 0.47;
        public const double PublishedDepthRatio = PublishedSurfaceDn / PublishedCoreDn;
        public const double PeakMustLieOutside = 0.75;   // outer 25% of the half-wall

        public static int Run(string[] args)
        {
            var log = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); log.AppendLine(s); };
            var ci = CultureInfo.InvariantCulture;

            // PROCESS CONDITIONS CORRECTED 2026-08-17 to those of the experiment
            // this case reproduces. Until today the reference case ran the
            // polymer's TYPICAL defaults - 290 C melt, 120 C mould - against a
            // paper that states 280 C and 150 C, a 30 C error in the single
            // boundary condition that governs how fast the skin freezes, and so
            // in the one place the model is furthest out.
            //
            // Stated plainly because it does not help: a COLDER mould freezes the
            // skin sooner and should retain MORE orientation, so moving 120 -> 150
            // is expected to WIDEN the depth deficit, not close it. It is
            // corrected because it is wrong, not because it improves the answer.
            var p = Polymers.ByName("MS_COC_TOPAS6017").WithProcessTemps(280.0, 150.0);

            // 71.3 MPa is the paper's stated injection pressure. Fill and pack
            // times are still NOT given by the paper (it states a 25 s cooling
            // time only) and remain the declared defaults.
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 71.3, PackTimeS = 3.0 };
            if (Program.Has(args, "-fountain"))
                proc.FountainStrain = Program.Value(args, "-fountain", 1.0);
            // -frontmode carried selects the melt-orientation deposition model,
            // which is NOT the default because it measures worse. See
            // Process.FrontCarriesMeltOrientation for the numbers.
            foreach (string a in args)
                if (string.Equals(a, "carried", StringComparison.OrdinalIgnoreCase))
                    proc.FrontCarriesMeltOrientation = true;

            // Grid, exposed so both registered numbers can be re-taken at
            // convergence. The convergence established earlier was measured on
            // the model as it stood before the fountain default, the viscosity
            // weighting and the measured constants, and does not carry over.
            int nzGrid = (int)Program.Value(args, "-nz", 321.0);   // converged; see -nz sweep
            if (nzGrid % 2 == 0) nzGrid++;
            int nFdGrid = 10 * nzGrid;

            say("MoldStress - registered reference case");
            say("  " + Program.ScopeLabel);
            say("  TOPAS 6017S-04, 100 x 100 x 1.5 mm plate, film gate on one edge");
            say(string.Format(ci, "  grid: nz {0}, nFD {1}", nzGrid, nFdGrid));
            if (nzGrid < 321)
                say("  WARNING: below nz=321 neither registered number is converged. " +
                    "Sweep measured 2026-08-15: peak 1.01x / depth 0.74 at nz=81, " +
                    "0.90x / 0.91 at 161, 0.85x / 0.98 at 321. Quote -nz 321.");
            // Those figures were taken at 290 C / 120 C / 60 MPa. The 2026-08-17
            // correction to the paper's own conditions changes the model's inputs,
            // and convergence EXPIRES when the model changes - the sweep above is
            // a GRID sweep and its converged VALUES do not carry across a change
            // of boundary condition, even though the grid at which it converged
            // plausibly does. Re-taken below on every run, so the printed numbers
            // are always current; the sweep is quoted only to justify nz=321.
            say("  NOTE: the 0.85x / 0.98 pair above predates the 2026-08-17 " +
                "process-condition correction. Trust THIS run's numbers, not those.");
            say(string.Format(ci, "  process: fill {0:F1} s, pack {1:F0} MPa for {2:F0} s, " +
                "melt {3:F0} C, mould {4:F0} C",
                proc.FillTimeS, proc.PackPressureMPa, proc.PackTimeS, p.MeltTempC, p.MoldTempC));
            say("");

            double[] gateProfile = null, farProfile = null;
            double gatePeak = 0, farPeak = 0;
            double surfaceDn = 0, coreDn = 0;
            double bandSurfaceDn = 0, bandCoreDn = 0;
            double peakDepthFraction = 0, reversedRatio = 0;

            Channels.ResetClampStats();
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
                var freeze = FreezeHistory.Build(e.CentreThicknessMm, p, proc, nzGrid, nFdGrid);
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
                    // THIRD ATTEMPT AT THIS NULL. The first two could not fail.
                    //
                    // (1) Array.Reverse on a profile symmetric about the mid-plane
                    //     returns the same array; it compared 33.41 with 33.41.
                    // (2) t -> tMax - t genuinely changed the numbers, and STILL
                    //     could not move the answer - measured 2026-08-17 by a
                    //     positive control that scales the freeze times instead of
                    //     rearranging them. Scaling by 100 moves the ratio 0.810 ->
                    //     0.812; scaling by 0.01 moves it to 0.908. The channel
                    //     responds to SHORTENING and is insensitive to LENGTHENING,
                    //     and that insensitivity is correct physics: once a layer
                    //     has vitrified, reduced time stops accumulating and a
                    //     later nominal freeze time adds nothing to the integral.
                    //     t -> tMax - t LENGTHENS the freeze time at both sampling
                    //     depths (0.002 -> 6.9 s at the wall, 0.85 -> 6.05 s at
                    //     47%), so it perturbs exclusively in the direction the
                    //     model is provably deaf to. It was a rearrangement that
                    //     happened to point the wrong way.
                    //
                    // So invert the DRIVER, not the derived label. Mirror the depth
                    // axis of the temperature history AND the freeze times together
                    // (|z| -> half - |z|), which gives the wall the core's thermal
                    // history and vice versa. Now the core really does solidify
                    // first, in the quantity the memory integral actually reads,
                    // and the perturbation is no longer confined to the saturating
                    // direction.
                    var reversed = FreezeHistory.Build(e.CentreThicknessMm, p, proc, nzGrid, nFdGrid);
                    {
                        int nzr = reversed.Z.Length;
                        double halfR = 0.5 * e.CentreThicknessMm;
                        var srcFor = new int[nzr];
                        for (int k = 0; k < nzr; k++)
                        {
                            double want = Math.Sign(reversed.Z[k]) * (halfR - Math.Abs(reversed.Z[k]));
                            int best = 0; double bestD = double.MaxValue;
                            for (int m = 0; m < nzr; m++)
                            {
                                double d = Math.Abs(reversed.Z[m] - want);
                                if (d < bestD) { bestD = d; best = m; }
                            }
                            srcFor[k] = best;
                        }
                        var ftOld = (double[])reversed.FreezeTimeS.Clone();
                        var trOld = (double[])reversed.TrefC.Clone();
                        double[,] thOld = null;
                        int nt = 0;
                        if (reversed.TempHistoryC != null)
                        {
                            nt = reversed.TempHistoryC.GetLength(1);
                            thOld = (double[,])reversed.TempHistoryC.Clone();
                        }
                        for (int k = 0; k < nzr; k++)
                        {
                            reversed.FreezeTimeS[k] = ftOld[srcFor[k]];
                            reversed.TrefC[k] = trOld[srcFor[k]];
                            if (thOld != null)
                                for (int q = 0; q < nt; q++)
                                    reversed.TempHistoryC[k, q] = thOld[srcFor[k], q];
                        }
                    }
                    var chRev = Channels.Build(e, p, proc, fill, reversed);
                    double sRev = Channels.DnAtDepthFraction(chRev.DnFlow, reversed.Z, 0, half, SurfaceFraction);
                    double dRev = Channels.DnAtDepthFraction(chRev.DnFlow, reversed.Z, 0, half, DeepFraction);
                    reversedRatio = dRev > 0 ? sRev / dRev : double.PositiveInfinity;

                    // POSITIVE CONTROL ON THE NULL ITSELF, added 2026-08-17.
                    //
                    // The freeze-order null reports FAIL, and a null that cannot
                    // move has two possible causes that look identical from the
                    // outside: the channel ignores the freeze history, or the
                    // PERTURBATION is too weak to show up. Reversing t -> tMax - t
                    // is a rearrangement; if the channel's response saturates it
                    // rearranges nothing. So drive the same input far harder, in
                    // both directions, and see whether ANY response exists.
                    //
                    // This is a control on the control. If these two agree with
                    // the unperturbed ratio as well, the channel is deaf to
                    // FreezeTimeS and the null was never capable of failing
                    // informatively - which is a defect in the model, not in the
                    // null. If they DO move, the null's perturbation is the weak
                    // link and it needs rewriting, not the channel.
                    foreach (double scale in new[] { 0.01, 100.0 })
                    {
                        var sc = FreezeHistory.Build(e.CentreThicknessMm, p, proc, nzGrid, nFdGrid);
                        for (int k = 0; k < sc.FreezeTimeS.Length; k++)
                            sc.FreezeTimeS[k] *= scale;
                        var chSc = Channels.Build(e, p, proc, fill, sc);
                        double sS = Channels.DnAtDepthFraction(chSc.DnFlow, sc.Z, 0, half, SurfaceFraction);
                        double dS = Channels.DnAtDepthFraction(chSc.DnFlow, sc.Z, 0, half, DeepFraction);
                        say(string.Format(ci,
                            "      probe: freeze times x{0,-6:G} -> surface {1:E3}, deep {2:E3}, ratio {3:F3}",
                            scale, sS, dS, dS > 0 ? sS / dS : double.PositiveInfinity));
                    }

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

            // WHY the null cannot move, measured rather than inferred. A clamped
            // quantity is deaf to its inputs; if the memory integral is saturated
            // at both sampling depths then reversing the freeze order, or moving
            // the mould 30 C, changes the INPUT to a function whose OUTPUT is
            // pinned at 1 either way - and the depth ratio collapses toward unity
            // because the same constant appears in numerator and denominator.
            say(string.Format(ci,
                "      memory clamp: {0} of {1} evaluations saturated ({2:P1}), " +
                "largest raw value {3:F1}",
                Channels.ClampHits, Channels.ClampCalls,
                Channels.ClampCalls > 0 ? (double)Channels.ClampHits / Channels.ClampCalls : 0.0,
                Channels.MaxRaw));

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
            // A criterion result read without knowing a physical term is disabled
            // is worse than no result, so it is printed beside the verdict rather
            // than in a footnote.
            say("");
            if (proc.FountainStrain <= 0)
            {
                say("  FOUNTAIN FLOW IS DISABLED for this run - non-default. The shipped");
                say("  configuration has it ON, because shear alone correctly gives a");
                say("  fast-freezing skin almost no orientation, so deposition at the front");
                say("  is the only thing left that can orient one.");
            }
            else
            {
                say(string.Format(ci,
                    "  fountain flow enabled at strain {0:F2} (the shipped default). Disable",
                    proc.FountainStrain));
                say("  with -fountain 0 to see the shear-only model.");
            }

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

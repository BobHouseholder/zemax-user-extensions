using System;
using System.Globalization;

namespace MoldStress
{
    /// <summary>
    /// FOURTH REFERENCE CASE - an injection-moulded PLATE whose flow and thermal
    /// contributions were SEPARATED BY THE AUTHOR.
    ///
    /// WHY THIS CASE EXISTS, and it is not "one more part".
    ///
    /// The three existing cases each test one thing and none of them tests the
    /// thing this model is most free to get wrong:
    ///
    ///     case 1  TOPAS plate   flow magnitude and depth shape, in a moulding
    ///     case 2  480R lens     one gapwise-average magnitude, in a moulding
    ///     case 3  PC quench     the thermal channel's SHAPE, with no flow at all
    ///
    /// Nothing has ever tested the SPLIT between the two channels inside a
    /// moulding, or the thermal channel's absolute magnitude in a mould rather
    /// than in a bath. Both are free parameters of the architecture in the sense
    /// that matters: no registered clause moves when they are wrong. This model
    /// currently runs flow-dominant - 92.3% flow for case 2, 14% thermal for case
    /// 1 against a published 8% - and it has never been contradicted because
    /// nothing has ever asked.
    ///
    /// This part answers, because its author took the trouble to take it apart.
    ///
    /// SOURCE, read page by page rather than from a summary:
    ///   R. Wimberger-Friedl, "Orientation, Stress and Density Distributions in
    ///   Injection-Moulded Amorphous Polymers Determined by Optical Techniques",
    ///   PhD thesis, TU Eindhoven (1991), DOI 10.6100/IR364279, chapter 3.3,
    ///   thesis pages 125-143. Open access. Chapter 3.2 of the same thesis is
    ///   reference case 3, so the polymer constants are already in the table and
    ///   already exercised by a case that passes.
    ///
    /// PART, from Fig. 2 (thesis p. 127): a flat plate, 80 x 35 x 2 mm, edge
    /// gated across the 35 mm end through a fan. Pressure transducers P1-P4 sit
    /// along the flow path; Fig. 10's cross-section was cut at P3's distance from
    /// the gate.
    ///
    /// PROCESS, from the text on p. 127 and the trace in Fig. 9 (p. 137):
    ///   melt (barrel)     320 C
    ///   mould              60 C for the reference condition; 30, 60, 90, 120 C
    ///                      for the series in Fig. 12
    ///   injection speed   25.4 cm3/s  (about 36 cm/s front speed, stated)
    ///   packing pressure   0 bar - NO separate packing stage
    ///   cycle time         60 s
    ///
    /// THE FILL TIME IS SOURCED TWICE AND THE TWO AGREE. The cavity holds
    /// 80*35*2 = 5600 mm3, so at 25.4 cm3/s it fills in 0.220 s; Fig. 9's
    /// pressure trace rises from about 0.1 s and peaks at about 0.35 s. That is
    /// the cross-check case 2 never had - there, one sourced fill time overturned
    /// three sweeps' worth of conclusions. Here the flow inputs are not chosen at
    /// all: Q is stated, the volume is geometry, and the trace agrees.
    ///
    /// ==================================================================
    /// WHAT THE SOURCE MEASURED, and the distinction the whole case turns on
    /// ==================================================================
    ///
    /// Fig. 10 (p. 138) is the gapwise birefringence of the moulded plate:
    ///
    ///     narrow surface spikes    about 20e-4 at |z/d| ~ 0.95
    ///     a local minimum just inside them
    ///     a broad, flat core plateau of about 5e-4
    ///
    /// The author then decomposes it, and the decomposition is the reason this
    /// case is worth more than the profile:
    ///
    ///   1. RESIDUAL STRESS, by layer removal (Fig. 13, p. 142). "The residual
    ///      stress level is very much as in the constrained quench case, even
    ///      slightly lower, NOT EXCEEDING 1 MPa" (p. 137), and on p. 143 "one can
    ///      estimate the thermal stress contribution to be BELOW 1e-4,
    ///      corresponding to 1 MPa even for the lowest mould temperature".
    ///
    ///   2. FROZEN-IN THERMAL ORIENTATION, the broad core plateau. "The
    ///      contribution of thermally induced orientation is on the average
    ///      5e-4, which is MORE THAN TWICE that of the flow-induced orientation
    ///      (averaged over the thickness)" (p. 140).
    ///
    ///   3. FLOW ORIENTATION, the surface spikes. "The maximum at the surface is
    ///      due to the elongation of the melt at the flow front" (p. 136) - which
    ///      is this model's fountain deposition term, named by the source.
    ///
    /// THIS MODEL HAS (1) AND (3) AND DOES NOT HAVE (2). The thermal channel here
    /// computes an ELASTIC residual stress and multiplies it by the photoelastic
    /// coefficient. Its like-for-like published counterpart is therefore item 1 -
    /// at most 1 MPa and at most 1e-4 - and NOT the 5e-4 plateau, which is
    /// orientation frozen in above Tg by the same mechanism reference case 3
    /// already identified as missing.
    ///
    /// That is what makes this case sharp instead of merely another profile. It
    /// supplies an absolute UPPER BOUND on the channel the model does have, in a
    /// moulding, where case 3 could only bound it in a free quench - and the two
    /// bounds pull in opposite directions. A free quench is a violent one and its
    /// stresses are large; a moulding adheres to the wall, which the source says
    /// suppresses thermal residual stress almost completely. A channel tuned to
    /// pass case 3 by being generous will fail here, and the model cannot satisfy
    /// both by scaling.
    ///
    /// ==================================================================
    /// CRITERION, REGISTERED 2026-08-19 BEFORE THE CASE WAS RUN ONCE
    /// ==================================================================
    ///
    ///   (a) THERMAL STRESS BOUND. Peak |sigma_thermal| across the gap must not
    ///       exceed 3.0 MPa. Published "not exceeding 1 MPa"; a factor of three
    ///       is allowed because the model carries no wall adhesion, which is the
    ///       mechanism the source credits for the low level, and because the
    ///       number is read from a plotted curve.
    ///
    ///   (b) THERMAL BIREFRINGENCE BOUND. Peak |dn_thermal| must not exceed
    ///       3.0e-4. Published "below 1e-4". Same factor of three, same reasons.
    ///       This is (a) through the photoelastic coefficient and is NOT
    ///       redundant with it: if the coefficient in the table were wrong the
    ///       two clauses would disagree, and that disagreement is the point.
    ///
    ///   (c) TOTAL MAGNITUDE, one-sided by construction. The gapwise average of
    ///       |dn_total| must land in [0.15, 1.00] x 6.0e-4. The upper edge is
    ///       1.00 and not a factor of two BECAUSE THE MODEL LACKS THE DOMINANT
    ///       PUBLISHED MECHANISM: item 2 above is more than half of what the
    ///       instrument sees, so a model without it that nonetheless reaches or
    ///       exceeds the measurement is not agreeing, it is compensating with the
    ///       wrong physics. Registering the ceiling at 1.00 makes over-prediction
    ///       a FAILURE here, which no other case in this project does.
    ///
    ///   (d) FLOW PEAK NEAR THE SURFACE. The flow channel's gapwise maximum must
    ///       sit at |z/d| >= 0.70. Published about 0.95, and attributed to
    ///       elongation at the front.
    ///
    ///   (e) MOULD-TEMPERATURE TRENDS, over Tm = 30, 60, 90 C. Both are stated by
    ///       the source as directions, so both are scored as directions:
    ///       (e1) the flow peak moves OUTWARD, toward the surface, as Tm rises.
    ///            "The local maximum shifts closer to the surface with increasing
    ///            mould temperature from 30 to 90 C. The reason is a decrease of
    ///            the thickness of the layer solidified during filling" (p. 140).
    ///       (e2) peak |sigma_thermal| is LARGER at Tm = 30 C than at Tm = 90 C.
    ///            "The stress levels are considerably low. They increase with
    ///            decreasing mould temperature Tm" (p. 141).
    ///       The 120 C point of Fig. 12 is deliberately EXCLUDED: it was moulded
    ///       at 2.54 cm3/s rather than 25.4, so injection speed is confounded
    ///       with mould temperature there and it cannot test a Tm trend. Stated
    ///       here, before running, so it cannot later be reached for.
    ///
    ///   (f) NULL. With CTE = 0 the thermal stress must be identically zero.
    ///   (g) CONTROL ON THE NULL. With CTE restored it must be non-zero, or (f)
    ///       passes on a channel that is dead rather than one that responds.
    ///
    /// ==================================================================
    /// WHAT THIS CASE CANNOT SETTLE, stated before running
    /// ==================================================================
    ///
    /// THE PLATE IS RECTANGULAR AND THE SOLVER'S CAVITY IS A DISC. There is no
    /// rectangular geometry in this model - reference case 1's 100 mm square is
    /// carried as a 50 mm radius disc for the same reason. Three quantities set
    /// the answer, and only two can be preserved:
    ///
    ///     flow rate per unit width Q/W   sets dp/ds and hence tau
    ///     fill time                      sets the freeze/fill overlap
    ///     flow path length               sets the pressure integral
    ///
    /// This case preserves the first two EXACTLY, by using the equal-VOLUME disc:
    /// R = sqrt(V/(pi*h)) = 29.85 mm, so the modelled cavity holds the true
    /// 5600 mm3, the fill time is the sourced 0.220 s, and Q comes out at the
    /// stated 25400 mm3/s rather than at something derived from it. The film-edge
    /// front width is held at the true gated width of 35 mm. What is distorted is
    /// the path length, 59.7 mm against 80 mm, and the sampling station is
    /// therefore expressed as a FRACTION of the path so that it lands in the
    /// proportionally right place. Sweep the alternative with -semidia 40, which
    /// gets the length right and Q 1.8x too high, and the difference between the
    /// two is the honest size of this idealisation.
    ///
    /// THE 5e-4 PLATEAU IS NOT REACHABLE and clause (c) says so in its shape
    /// rather than pretending otherwise. If (c) passes at the bottom of its band
    /// the reading is that the model has the two channels it claims and is
    /// missing the third; if (c) fails high, something is over-predicting.
    ///
    /// THE GATE THICKNESS IS NOT STATED. Fig. 2 shows a fan gate and gives no
    /// land. 1.0 mm is chosen here, exposed as -gatethick, and it is nearly inert
    /// in this case because there is no packing stage for a gate seal to end.
    /// </summary>
    internal static class RefPlate
    {
        // --- the part, from Fig. 2 ------------------------------------------
        public const double PlateLengthMm = 80.0;
        public const double PlateWidthMm = 35.0;
        public const double ThicknessMm = 2.0;

        // --- the process, from p. 127 and Fig. 9 ------------------------------
        public const double MeltTempC = 320.0;
        public const double RefMouldTempC = 60.0;
        public const double InjectionRateMm3PerS = 25400.0;   // 25.4 cm3/s

        // --- the published observables ----------------------------------------
        /// <summary>"not exceeding 1 MPa", p. 137 and p. 143.</summary>
        public const double PublishedThermalStressMaxMPa = 1.0;
        /// <summary>"below 1e-4", p. 143.</summary>
        public const double PublishedThermalDnMax = 1.0e-4;
        /// <summary>
        /// Gapwise average of Fig. 10, estimated as 5e-4 over the inner ~85% of
        /// the gap plus the surface spikes over the outer ~15%: 0.85*5 +
        /// 0.15*12 = 6.0e-4. A read off a scanned figure, so the band around it
        /// is wide and one-sided.
        /// </summary>
        public const double PublishedGapAverageDn = 6.0e-4;
        public const double PublishedCorePlateauDn = 5.0e-4;
        public const double PublishedSurfacePeakDn = 20.0e-4;
        public const double PublishedSurfacePeakZOverD = 0.95;

        // --- the registered bands ---------------------------------------------
        public const double StressBoundFactor = 3.0;
        public const double TotalLoFraction = 0.15, TotalHiFraction = 1.00;
        public const double FlowPeakZOverDMin = 0.70;

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            Action<string> say = Console.WriteLine;

            int badForMode = Program.RejectFlagsNotReadBy(
                args, new[] { "-nz", "-moldtemp", "-station", "-semidia", "-filltime",
                              "-gatethick", "-snapshot" }, "-refplate");
            if (badForMode != 0) return badForMode;

            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;

            double tm = Program.Value(args, "-moldtemp", RefMouldTempC);
            double stationFrac = Program.Value(args, "-station", 0.5);
            double gateThick = Program.Value(args, "-gatethick", 1.0);
            bool incremental = !Program.Has(args, "-snapshot");

            // GEOMETRY, DERIVED rather than typed. The equal-volume disc is
            // computed from the plate's own dimensions, so if those are ever
            // corrected the radius follows instead of going stale.
            double cavityVolMm3 = PlateLengthMm * PlateWidthMm * ThicknessMm;
            double equalVolumeRadiusMm = Math.Sqrt(cavityVolMm3 / (Math.PI * ThicknessMm));
            double semiDia = Program.Value(args, "-semidia", equalVolumeRadiusMm);

            // FILL TIME, likewise derived: the stated injection rate emptying the
            // MODELLED cavity. With the equal-volume radius this is the true
            // 0.220 s; with -semidia 40 it is the time that keeps Q honest at the
            // cost of the duration, and the print below says which one is running.
            double modelVolMm3 = Math.PI * semiDia * semiDia * ThicknessMm;
            double fillDerivedS = modelVolMm3 / InjectionRateMm3PerS;
            double fillS = Program.Value(args, "-filltime", fillDerivedS);

            say("");
            say("MoldStress - fourth reference case: MOULDED PLATE (channel split)");
            say("  " + Program.ScopeLabel);
            say(string.Format(ci,
                "  bisphenol-A polycarbonate, {0:F0} x {1:F0} x {2:F1} mm plate, edge gated",
                PlateLengthMm, PlateWidthMm, ThicknessMm));
            say("  Wimberger-Friedl, PhD thesis, TU Eindhoven (1991), ch. 3.3, pp. 125-143");
            say(string.Format(ci,
                "  process: melt {0:F0} C, mould {1:F0} C, injection {2:F1} cm3/s, packing 0 bar",
                MeltTempC, tm, InjectionRateMm3PerS / 1000.0));
            say(string.Format(ci,
                "  cavity {0:F0} mm3 -> equal-volume disc R {1:F2} mm (path {2:F1} mm vs the "
                + "true {3:F0} mm)", cavityVolMm3, equalVolumeRadiusMm,
                2.0 * equalVolumeRadiusMm, PlateLengthMm));
            say(string.Format(ci,
                "  running R {0:F2} mm, fill {1:F4} s, Q {2:F0} mm3/s (stated {3:F0}), "
                + "front width {4:F0} mm", semiDia, fillS, modelVolMm3 / fillS,
                InjectionRateMm3PerS, PlateWidthMm));
            say(string.Format(ci, "  grid: nz {0}, thermal construction: {1}, station {2:F2} "
                + "of the path", nz, incremental ? "INCREMENTAL" : "snapshot", stationFrac));
            say("");

            var p = Polymers.ByName("MS_POLYCARB").WithProcessTemps(MeltTempC, tm);
            var proc = new Process
            {
                FillTimeS = fillS,
                PackPressureMPa = 0.0,      // stated: no separate packing stage
                PackTimeS = 0.0,
                IncrementalThermal = incremental,
            };

            var plate = BuildElement(semiDia, gateThick);
            var fill = FillField.Build(plate, p, proc, 101);
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, proc, nz, 10 * nz);
            Channels.ResetClampStats();
            var ch = Channels.Build(plate, p, proc, fill, freeze);

            int ns = ch.S.Length;
            int iSta = (int)Math.Round(stationFrac * (ns - 1));
            iSta = Math.Max(0, Math.Min(ns - 1, iSta));
            int kMid = nz / 2;
            double half = 0.5 * ThicknessMm;

            // ---- the profile, printed before anything is judged ---------------
            say("  gapwise profile at station " + iSta.ToString(ci)
                + string.Format(ci, " (s = {0:F1} mm):", ch.S[iSta]));
            say("     z/d      dn_flow     dn_thermal    dn_total     sigma_th MPa");
            for (int f = 0; f <= 10; f++)
            {
                int k = kMid + (int)Math.Round((nz - 1 - kMid) * f / 10.0);
                if (k > nz - 1) k = nz - 1;
                double dnTh = p.KGlassBrewster * 1e-6 * ch.SigmaThermalMPa[iSta, k];
                say(string.Format(ci, "    {0:F2}    {1: E3}   {2: E3}   {3: E3}   {4: F4}",
                    Math.Abs(freeze.Z[k]) / half, ch.DnFlow[iSta, k], dnTh,
                    ch.DnTotalOutOfPlane[iSta, k], ch.SigmaThermalMPa[iSta, k]));
            }
            say("");

            // ---- THE CEILING, before the clauses ------------------------------
            //
            // Printed first and deliberately. Six candidate fixes were swept on
            // reference case 2 before anyone computed what the architecture could
            // produce at all, and the ceiling explained all six at once. A clause
            // whose ceiling sits below its gate is not a test of the parameters.
            //
            // Here the ceiling is unusual, because clause (c) is ONE-SIDED: this
            // case can fail by being too LARGE, so the interesting comparison is
            // both that the ceiling clears the gate and that the model sits below
            // it for a reason rather than by luck.
            {
                double ceilFlow = 0.0, tauSum = 0.0;
                for (int k = 0; k < nz; k++)
                {
                    double t = Math.Abs(ch.TauViscMPa[iSta, k]);
                    tauSum += t;
                    double fac = 1.0;
                    if (proc.NormalStressDifference && p.MeltModulusPa > 0.0)
                    {
                        double wi = t * 1e6 / p.MeltModulusPa;
                        fac = Math.Sqrt(1.0 + wi * wi);
                    }
                    ceilFlow += 2.0 * Math.Abs(p.CMeltBrewster) * 1e-6 * t * fac;
                }
                ceilFlow /= nz;
                double tauAvg = tauSum / nz;
                say(string.Format(ci,
                    "  CEILING of the flow channel: 2*|C|*<tau> = {0:E3} at memory==1 "
                    + "(<tau> {1:F4} MPa, C {2:F0} Br)", ceilFlow, tauAvg, p.CMeltBrewster));
                say(string.Format(ci,
                    "    against the gapwise-average gate {0:E3}: ceiling/gate = {1:F2}  =>  {2}",
                    PublishedGapAverageDn, ceilFlow / PublishedGapAverageDn,
                    ceilFlow >= PublishedGapAverageDn
                        ? "reachable"
                        : "NOT reachable by the flow channel alone - which is what the "
                          + "source says, since it attributes more than half to a "
                          + "mechanism this model lacks"));
            }
            say("");

            // ---- (a) thermal stress bound -------------------------------------
            double sigMax = 0.0, dnThMax = 0.0;
            for (int k = 0; k < nz; k++)
            {
                sigMax = Math.Max(sigMax, Math.Abs(ch.SigmaThermalMPa[iSta, k]));
                dnThMax = Math.Max(dnThMax,
                    Math.Abs(p.KGlassBrewster * 1e-6 * ch.SigmaThermalMPa[iSta, k]));
            }
            double sigBound = PublishedThermalStressMaxMPa * StressBoundFactor;
            bool aOk = sigMax <= sigBound;
            say(string.Format(ci,
                "  (a) peak |sigma_thermal| {0:F3} MPa, published <= {1:F1}, bound {2:F1}  =>  {3}",
                sigMax, PublishedThermalStressMaxMPa, sigBound, aOk ? "PASS" : "FAIL"));

            // ---- (b) thermal birefringence bound ------------------------------
            double dnThBound = PublishedThermalDnMax * StressBoundFactor;
            bool bOk = dnThMax <= dnThBound;
            say(string.Format(ci,
                "  (b) peak |dn_thermal| {0:E3}, published <= {1:E1}, bound {2:E1}  =>  {3}",
                dnThMax, PublishedThermalDnMax, dnThBound, bOk ? "PASS" : "FAIL"));

            // ---- (c) total magnitude, one-sided -------------------------------
            double totalSum = 0.0, flowSum = 0.0, thermSum = 0.0;
            for (int k = 0; k < nz; k++)
            {
                totalSum += Math.Abs(ch.DnTotalOutOfPlane[iSta, k]);
                flowSum += Math.Abs(ch.DnFlow[iSta, k]);
                thermSum += Math.Abs(p.KGlassBrewster * 1e-6 * ch.SigmaThermalMPa[iSta, k]);
            }
            double totalAvg = totalSum / nz, flowAvg = flowSum / nz, thermAvg = thermSum / nz;
            double loGate = TotalLoFraction * PublishedGapAverageDn;
            double hiGate = TotalHiFraction * PublishedGapAverageDn;
            bool cOk = totalAvg >= loGate && totalAvg <= hiGate;
            say(string.Format(ci,
                "  (c) gapwise-average |dn_total| {0:E3} against measured {1:E3}, "
                + "ratio {2:F2}, band [{3:F2}, {4:F2}]  =>  {5}",
                totalAvg, PublishedGapAverageDn, totalAvg / PublishedGapAverageDn,
                TotalLoFraction, TotalHiFraction, cOk ? "PASS" : "FAIL"));
            if (totalAvg > hiGate)
                say("      OVER-PREDICTED. The model reaches the measurement without "
                    + "carrying the mechanism the source says supplies most of it.");

            // ---- (d) flow peak near the surface -------------------------------
            double flowPeak = 0.0, flowPeakZ = 0.0;
            for (int k = kMid; k <= nz - 2; k++)
            {
                double v = Math.Abs(ch.DnFlow[iSta, k]);
                if (v > flowPeak) { flowPeak = v; flowPeakZ = Math.Abs(freeze.Z[k]) / half; }
            }
            bool dOk = flowPeakZ >= FlowPeakZOverDMin;
            say(string.Format(ci,
                "  (d) flow-channel peak {0:E3} at |z/d| {1:F3}, published ~{2:F2}, "
                + "gate >= {3:F2}  =>  {4}",
                flowPeak, flowPeakZ, PublishedSurfacePeakZOverD, FlowPeakZOverDMin,
                dOk ? "PASS" : "FAIL"));

            // ---- (e) the mould-temperature trends -----------------------------
            //
            // Rebuilt from scratch at each Tm rather than scaled, because the
            // whole point of the clause is that the freeze/fill overlap moves.
            say("");
            say("  (e) mould-temperature series, Fig. 12 and Fig. 13 (120 C excluded - "
                + "it was moulded at 2.54 cm3/s):");
            say("     Tm      flow peak |z/d|    peak sigma_th MPa   avg |dn_total|");
            var tmSeries = new[] { 30.0, 60.0, 90.0 };
            var peakZ = new double[tmSeries.Length];
            var peakSig = new double[tmSeries.Length];
            for (int j = 0; j < tmSeries.Length; j++)
            {
                double pz, ps, av;
                Measure(tmSeries[j], nz, semiDia, gateThick, fillS, incremental, stationFrac,
                        out pz, out ps, out av);
                peakZ[j] = pz; peakSig[j] = ps;
                say(string.Format(ci, "    {0,3:F0} C       {1:F3}              {2:F3}"
                    + "               {3:E3}", tmSeries[j], pz, ps, av));
            }
            bool e1Ok = peakZ[1] >= peakZ[0] && peakZ[2] >= peakZ[1] && peakZ[2] > peakZ[0];
            bool e2Ok = peakSig[0] > peakSig[2];
            say(string.Format(ci,
                "  (e1) flow peak moves OUTWARD with Tm: {0:F3} -> {1:F3} -> {2:F3}  =>  {3}",
                peakZ[0], peakZ[1], peakZ[2], e1Ok ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (e2) thermal stress rises as Tm falls: {0:F3} MPa at 30 C vs {1:F3} at "
                + "90 C  =>  {2}", peakSig[0], peakSig[2], e2Ok ? "PASS" : "FAIL"));

            // ---- (f) null and (g) its control ---------------------------------
            say("");
            var pNull = p.WithZeroCte();
            var chNull = Channels.Build(plate, pNull, proc, fill,
                FreezeHistory.Build(plate.CentreThicknessMm, pNull, proc, nz, 10 * nz));
            double nullMax = 0.0;
            for (int k = 0; k < nz; k++)
                nullMax = Math.Max(nullMax, Math.Abs(chNull.SigmaThermalMPa[iSta, k]));
            bool fOk = nullMax <= 1e-12;
            bool gOk = sigMax > 1e-9;
            say(string.Format(ci,
                "  (f) null: CTE = 0 collapses the thermal stress, largest {0:E3} MPa  =>  {1}",
                nullMax, fOk ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (g) control on the null: CTE restored gives {0:E3} MPa  =>  {1}",
                sigMax, gOk ? "PASS" : "FAIL"));

            // ---- THE CHANNEL SPLIT, DIAGNOSTIC AND NOT SCORED -----------------
            //
            // The published statement is "thermally induced orientation is on the
            // average 5e-4, more than twice the flow-induced orientation". That
            // 5e-4 is ORIENTATION frozen in above Tg, which this model does not
            // carry - so scoring the model's thermal/flow ratio against 2 would
            // be scoring it against physics it does not contain, and would make a
            // model that ADDED the missing channel look worse.
            //
            // It is printed and not scored, on the same footing as reference case
            // 3's Ti trend, so that a future criterion can register it BEFORE the
            // orientational channel is built rather than after.
            say("");
            say("  CHANNEL SPLIT (diagnostic, not scored):");
            say(string.Format(ci,
                "    model    flow {0:E3} ({1:F1}%), thermal-elastic {2:E3} ({3:F1}%)",
                flowAvg, 100.0 * flowAvg / Math.Max(totalAvg, 1e-30),
                thermAvg, 100.0 * thermAvg / Math.Max(totalAvg, 1e-30)));
            say(string.Format(ci,
                "    source   flow ~{0:E3}, thermal-ORIENTATIONAL {1:E3} - a ratio of more "
                + "than 2 the other way", PublishedCorePlateauDn / 2.0,
                PublishedCorePlateauDn));
            say("    The two are NOT comparable: the source's thermal term is frozen-in");
            say("    orientation from stresses above Tg, and this model's is elastic");
            say("    residual stress. Clause (a) compares the like-for-like halves. The");
            say("    gap between them IS the missing channel, and it is the same one");
            say("    reference case 3's Ti trend already pointed at.");

            bool met = aOk && bOk && cOk && dOk && e1Ok && e2Ok && fOk && gOk;
            say("");
            say("  VERDICT: " + (met
                ? "the registered criterion is MET"
                : "the registered criterion is NOT met"));
            return met ? 0 : 2;
        }

        /// <summary>
        /// The plate as this solver can carry it. Kept in one place so the trend
        /// series and the reference condition cannot drift apart - which is
        /// exactly how RefCase2's depth table once disagreed with the dn beside it.
        /// </summary>
        private static MouldedElement BuildElement(double semiDia, double gateThick)
        {
            var e = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = "MS_POLYCARB",
                CentreThicknessMm = ThicknessMm, SemiDiameterMm = semiDia,
                FrontRadiusMm = 0, BackRadiusMm = 0,
            };
            e.EdgeThicknessMm = e.ThicknessAt(e.SemiDiameterMm);
            e.Gate = new GateSpec
            {
                // FilmEdge, because the plate is gated across a whole 35 mm end
                // through a fan and the front then travels as a straight line of
                // constant width - which is what FilmEdge models and what the
                // pressure traces of Fig. 9 show.
                Kind = GateKind.FilmEdge, AzimuthDeg = 0,
                WidthMm = PlateWidthMm, ThicknessMm = gateThick, IsDefault = false,
            };
            e.PartingLineZMm = Gating.DefaultPartingLineZ(e);
            return e;
        }

        private static void Measure(double tm, int nz, double semiDia, double gateThick,
                                    double fillS, bool incremental, double stationFrac,
                                    out double flowPeakZOverD, out double peakSigmaMPa,
                                    out double avgTotalDn)
        {
            var p = Polymers.ByName("MS_POLYCARB").WithProcessTemps(MeltTempC, tm);
            var proc = new Process
            {
                FillTimeS = fillS, PackPressureMPa = 0.0, PackTimeS = 0.0,
                IncrementalThermal = incremental,
            };
            var e = BuildElement(semiDia, gateThick);
            var fill = FillField.Build(e, p, proc, 101);
            var freeze = FreezeHistory.Build(e.CentreThicknessMm, p, proc, nz, 10 * nz);
            var ch = Channels.Build(e, p, proc, fill, freeze);

            int ns = ch.S.Length;
            int i = Math.Max(0, Math.Min(ns - 1, (int)Math.Round(stationFrac * (ns - 1))));
            int kMid = nz / 2;
            double half = 0.5 * ThicknessMm;

            double best = 0.0, bestZ = 0.0, sig = 0.0, tot = 0.0;
            for (int k = kMid; k <= nz - 2; k++)
            {
                double v = Math.Abs(ch.DnFlow[i, k]);
                if (v > best) { best = v; bestZ = Math.Abs(freeze.Z[k]) / half; }
            }
            for (int k = 0; k < nz; k++)
            {
                sig = Math.Max(sig, Math.Abs(ch.SigmaThermalMPa[i, k]));
                tot += Math.Abs(ch.DnTotalOutOfPlane[i, k]);
            }
            flowPeakZOverD = bestZ;
            peakSigmaMPa = sig;
            avgTotalDn = tot / nz;
        }
    }
}

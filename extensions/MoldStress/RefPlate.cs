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
    ///   3. FLOW ORIENTATION, the surface spikes. The 1991 thesis says "The
    ///      maximum at the surface is due to the elongation of the melt at the
    ///      flow front" (p. 136) - this model's fountain deposition term.
    ///
    ///      ** THAT ATTRIBUTION IS WITHDRAWN BY ITS OWN AUTHOR. ** Wimberger-
    ///      Friedl, Int. Polym. Process. 11(4) 373 (1996), same mould, same
    ///      machine, same Makrolon CD 2000: "The observed behaviour rules out the
    ///      fountain flow induced elongational stresses as the origin for the
    ///      birefringence maximum at the surface." The surface birefringence is
    ///      EQUI-BIAXIAL and independent of gate distance, and scales with cavity
    ///      pressure - none of which a flow mechanism gives. The 1996 paper puts
    ///      transient pressure-induced deviatoric stress in the vitrifying layer
    ///      there instead (its Eqs 5-8; the ceiling is printed by this case).
    ///
    ///      The fountain term is KEPT because removing it breaks case 1 and this
    ///      case, on evidence that covers neither - see Process.FountainStrain.
    ///      What is withdrawn is the CLAIM, not the term.
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
    ///   (d) FLOW PEAK POSITION. RE-REGISTERED 2026-08-19 - see below. The flow
    ///       channel's gapwise maximum must sit in |z/d| = [0.70, 0.90], against
    ///       a published FILLING maximum at 0.80.
    ///
    ///       ORIGINALLY REGISTERED AS: "must sit at |z/d| >= 0.70; published
    ///       about 0.95, attributed to elongation at the front." That compared
    ///       two DIFFERENT FEATURES. The published profile carries three maxima,
    ///       not one (IPP 11(4) 373, p. 375): a surface maximum at z ~ 0.95-1.0,
    ///       "a second maximum at z = 0.8 due to shear flow during filling", and
    ///       "a small maximum at z = 0.5 induced during the packing stage". This
    ///       model's flow channel produces the FILLING maximum. The 0.95 I
    ///       registered against is the SURFACE maximum, which the same paper
    ///       attributes to transient pressure in the vitrifying layer - a
    ///       mechanism this model does not have at all. The clause passed on a
    ///       mismatched comparison.
    ///
    ///       THE NEW BAND IS DERIVED FROM THE SOURCE, NOT FROM THE MODEL. The
    ///       paper states z = 0.8 explicitly for its reference condition, and
    ///       says the filling maximum "shifts towards the surface with increasing
    ///       mold temperature", reaching the surface itself by Tm = 120 C. The
    ///       scored series here runs Tm = 30-90 C, so +-0.10 about 0.80 covers
    ///       that migration plus a figure read.
    ///
    ///       This is a correction of a mis-registration, not a relaxation: the
    ///       band is TIGHTER than the one it replaces (two-sided [0.70, 0.90]
    ///       against a one-sided >= 0.70), and it can now fail from above. The
    ///       model reads 0.883, which sits near the TOP of the band - it peaks
    ///       further out than the source does, and that is a real observation
    ///       rather than a pass to be pleased about.
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
    /// THE BOUNDARY CONDITION WAS FIXED, AND THE CRITERION IS NOW MET
    /// ==================================================================
    ///
    /// `Channels.ThermalProfileAdhered` replaces free-plate balance-at-every-
    /// increment with the physics the source describes: held layers accumulate
    /// uniform tension against the cavity, and release relieves it. See that
    /// routine for the derivation. Applied here with the release time taken from
    /// the stated 60 s cycle, ALL EIGHT CLAUSES PASS - peak thermal stress goes
    /// 7.44 MPa -> 0.000, against a published "not exceeding 1 MPa".
    ///
    /// THREE OF THOSE PASSES CARRY NO INFORMATION, and saying so is the point.
    /// (a) and (b) are bounds and the model now predicts essentially zero, so
    /// they cannot distinguish a right answer from a dead channel; (e2) compares
    /// two numbers at the 1e-7 MPa level and resolves on floating-point noise -
    /// it is flagged VACUOUS in the output rather than quietly counted.
    ///
    /// SO THE EVIDENCE IS THE RELEASE-TIME SWEEP, not the verdict. The
    /// construction predicts that residual stress tracks the temperature
    /// non-uniformity at release and nothing else, and it does:
    ///
    ///     release s   core-skin dT   peak sigma_th MPa
    ///        4.24        83.34 C          12.905
    ///        7.24        31.83 C           4.929
    ///       14.24         3.37 C           0.522
    ///       59.78         0.00 C           0.000
    ///
    /// A 2 mm plate held 60 s against a ~3 s thermal time constant IS uniform at
    /// release, so zero is a prediction about this part rather than a property of
    /// the construction. Eject it hot and the stress is there.
    ///
    /// AND IT IS CORROBORATED ON A CASE IT WAS NOT BUILT FOR. Reference case 1,
    /// a different polymer and a different part, has a published depth ratio of
    /// 2.78. Free plate gives 3.43; adhered gives 2.84. The old construction was
    /// contributing a spurious 0.6 to that ratio.
    ///
    /// WHY IT IS NOT YET THE DEFAULT FOR CASES 1 AND 2. Adhesion takes case 1's
    /// elastic thermal channel to 0% of flow, and case 1 carries a registered
    /// control asserting that channel is MATERIAL - which then fails. That
    /// control was registered under the free-plate construction and needs
    /// re-registering against the orientational channel this model still lacks;
    /// re-writing it now, to make a case pass, is the one thing this project does
    /// not do. So cases 1 and 2 keep the old construction behind `-adhered` until
    /// that is settled, and the disagreement is recorded rather than smoothed.
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
    /// ==================================================================
    /// WHAT THE FIRST RUN FOUND, 2026-08-19 - recorded after the criterion was
    /// committed, and the criterion was not touched
    /// ==================================================================
    ///
    /// SIX OF EIGHT CLAUSES PASSED ON THE FIRST RUN. (c) total magnitude 0.67 of the measurement,
    /// inside the one-sided band and below it, which is the direction the
    /// missing channel predicts. (d) the flow peak lands at |z/d| 0.888 against
    /// a published ~0.95. (e1) and (e2) both reproduce the published trend
    /// directions. The null and its control both hold.
    ///
    /// (a) AND (b) FAILED ON THE FIRST RUN, AND THAT IS THE RESULT THIS CASE WAS
    /// BUILT TO GET - they pass now, and only because the boundary condition was
    /// fixed. The record below is left as it stood.
    /// Peak |sigma_thermal| is 7.4 MPa where the measurement says the residual
    /// stress does not exceed 1 MPa; even the well-resolved interior runs
    /// 1-2.8 MPa. The thermal channel over-predicts residual stress in a
    /// moulding by something between three and eight times.
    ///
    /// THE MECHANISM IS NAMED BY THE SOURCE AND IT IS A BOUNDARY CONDITION, not
    /// a constant. p. 130: with wall adhesion "stresses are not equilibrated
    /// within the polymer ... When the polymer is released, the tensile stresses
    /// will be relieved so that no residual stresses remain in the sample", and
    /// p. 136: "with wall adhesion one can expect very low thermally induced but
    /// significant pressure induced residual stresses". This model imposes FREE
    /// PLATE force and moment balance at every increment, while the part is
    /// still in the cavity and still adhered. That is the wrong boundary
    /// condition for the in-mould stage of a moulding.
    ///
    /// THIS REOPENS A REFUTED BRANCH. A constrained-then-released construction
    /// was implemented and refuted earlier in this arc because it returned
    /// identically zero - see the note in Channels.ThermalProfileIncremental and
    /// memory/rejected.md. It was refuted against the POST-vitrification
    /// increment, where every layer cools from the same Tg to the same mould
    /// temperature and zero is genuinely wrong. It was never tried on the
    /// DURING-solidification stage, which is where this case's 7.4 MPa is built,
    /// and against a measurement of "not exceeding 1 MPa" an answer near zero is
    /// far closer than the free-plate one. The reopening condition is met.
    ///
    /// A CLAUSE DEFECT, RECORDED AND NOT FIXED. (a) samples the gapwise PEAK,
    /// and the peak is grid-noisy: 3.07, 10.42, 7.44 MPa at nz = 41, 81, 161.
    /// The interior is stable and the near-surface cells are not. A future
    /// criterion should score an interior statistic instead - the unscored
    /// diagnostic below prints one - but the registered clause stays as
    /// registered, because changing a clause after seeing it fail is moving the
    /// bar, and the verdict does not depend on it: the interior alone already
    /// sits at the top of the registered band.
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
        /// <summary>Stated cycle time, p. 127. The part is in the cavity for all
        /// of it bar the fill, so release is at cycle minus fill.</summary>
        public const double CycleTimeS = 60.0;

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
        /// <summary>The SURFACE maximum - pressure-induced per IPP 11(4) 373,
        /// not reproduced by this model. Used by the Eq (8) ceiling, NOT by
        /// clause (d).</summary>
        public const double PublishedSurfacePeakDn = 20.0e-4;
        public const double PublishedSurfacePeakZOverD = 0.95;

        /// <summary>The FILLING maximum - shear during fill, which IS what this
        /// model's flow channel produces. "a second maximum at z = 0.8 due to
        /// shear flow during filling", IPP 11(4) 373 p. 375.</summary>
        public const double PublishedFillingPeakZOverD = 0.80;

        // --- the registered bands ---------------------------------------------
        public const double StressBoundFactor = 3.0;
        public const double TotalLoFraction = 0.15, TotalHiFraction = 1.00;
        public const double FlowPeakZOverDLo = 0.70, FlowPeakZOverDHi = 0.90;

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            Action<string> say = Console.WriteLine;

            int badForMode = Program.RejectFlagsNotReadBy(
                args, new[] { "-nz", "-moldtemp", "-station", "-semidia", "-filltime",
                              "-gatethick", "-snapshot", "-freeplate", "-ejecttime",
                              "-fountain", "-pressure-vitrification", "-changeover" },
                "-refplate");
            if (badForMode != 0) return badForMode;

            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;

            double tm = Program.Value(args, "-moldtemp", RefMouldTempC);
            double stationFrac = Program.Value(args, "-station", 0.5);
            double gateThick = Program.Value(args, "-gatethick", 1.0);
            bool incremental = !Program.Has(args, "-snapshot");
            // The part is ADHERED unless asked otherwise. -freeplate restores the
            // old construction so the two can be compared in one command rather
            // than argued about.
            bool adhered = !Program.Has(args, "-freeplate");
            double ejectS = Program.Value(args, "-ejecttime", double.NaN);
            // -fountain WAS NOT READ BY THIS MODE, which is a defect and not a
            // preference: reference case 4 is the case whose source REFUTES the
            // fountain explanation of the surface maximum, and it was the one
            // case that could not be run without the term. `-refplate -fountain 0`
            // exited 64 on the flag guard. Wired 2026-08-19.
            double fountain = Program.Value(args, "-fountain", 1.0);
            bool pressVit = Program.Has(args, "-pressure-vitrification");
            // SOURCED, not chosen: the thesis's own transducer trace (ch. 3.3
            // Fig. 9) peaks near 80 MPa for this exact sample, with no packing
            // stage - so all of it is change-over compression.
            double changeover = Program.Value(args, "-changeover", 80.0);

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

            // RESOLVED BEFORE THE HEADER IS PRINTED. It was resolved after, for
            // one build, and the header duly announced "released at NaN s" for a
            // run that had used 59.8 - a label that would have had a reader
            // conclude the feature had not run at all.
            if (double.IsNaN(ejectS)) ejectS = Math.Max(CycleTimeS - fillS, fillS);

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
            say(string.Format(ci, "  grid: nz {0}, station {1:F2} of the path", nz, stationFrac));
            say(string.Format(ci, "  change-over compression: {0:F1} MPa uniform "
                + "(source Fig. 9 trace peaks near 80)", changeover));
            say(string.Format(ci, "  pressure-vitrification term (source Eqs 5-8): {0}",
                pressVit ? "ON (-pressure-vitrification)" : "off - default"));
            say(string.Format(ci, "  fountain deposition: {0}",
                fountain > 0.0
                    ? string.Format(ci, "ON, strain scale {0:F2} - NOTE the source refutes "
                        + "this as the origin of the surface maximum", fountain)
                    : "OFF (-fountain 0)"));
            say(string.Format(ci, "  thermal boundary condition: {0}",
                adhered
                    ? string.Format(ci, "ADHERED to the cavity, released at {0:F1} s "
                        + "(cycle {1:F0} s less the fill)", ejectS, CycleTimeS)
                    : "FREE PLATE at every increment (-freeplate) - " +
                      (incremental ? "incremental" : "snapshot")));
            say("");

            var p = Polymers.ByName("MS_POLYCARB").WithProcessTemps(MeltTempC, tm);
            var proc = new Process
            {
                FillTimeS = fillS,
                PackPressureMPa = 0.0,      // stated: no separate packing stage
                PackTimeS = 0.0,
                IncrementalThermal = incremental,
                MouldAdhesion = adhered,
                EjectionTimeS = ejectS,
                FountainStrain = fountain,
                PressureVitrification = pressVit,
                ChangeoverPressureMPa = changeover,
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
                say(string.Format(ci, "    {0:F2}   {1,11:E3}   {2,11:E3}   {3,11:E3}   {4,10:F3}",
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
                for (int k = 1; k < nz - 1; k++)
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
                ceilFlow /= (nz - 2);
                double tauAvg = tauSum / (nz - 2);
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

                // THE PRESSURE CEILING, from the source's own Eqs (5)-(8).
                //
                // Wimberger-Friedl, Int. Polym. Process. 11(4) 373 (1996) - the
                // paper that rules out fountain flow as the origin of the surface
                // maximum - derives the OTHER mechanism in closed form. A layer
                // vitrifying while adhered to the wall has its in-plane strain
                // pinned, so with sigma_z = -p and eps_x = eps_y = 0,
                //
                //     sigma*_x = sigma_z * nu / (1 - nu)                    Eq (6)
                //     0 <= (sigma_x - sigma_z) <= -sigma_z/2 = p/2          Eq (8)
                //
                // with equality at nu = 1/3. It is EQUI-BIAXIAL - sigma_x = sigma_y
                // - which is the paper's decisive evidence, since a flow mechanism
                // cannot produce a contribution equal transverse to the flow.
                //
                // Printed here because this project's rule is that a case states
                // its own reachability rather than leaving a future session to
                // sweep inside a box. And this ceiling says something unusual:
                // it is enormous. The measured surface birefringence is ~20e-4
                // and the ceiling is of order 0.1, so about 2% is retained. The
                // paper reaches the same number independently - "otherwise the
                // birefringence level would be of the order of 0.1" - which is a
                // check on this model's PC constants, not just on the arithmetic.
                //
                // So a pressure channel here would be RETENTION-limited, not
                // ceiling-limited. That is the opposite of reference case 2, whose
                // flow ceiling sits at 1.35 of its gate, and it is why the source
                // puts the physics in a memory function rather than a coefficient.
                double pMaxMPa = 0.0;
                for (int i = 0; i < fill.P.Length; i++)
                    pMaxMPa = Math.Max(pMaxMPa, Math.Abs(fill.P[i]));
                double devMax = 0.5 * pMaxMPa;                  // Eq (8), nu = 1/3
                double ceilPress = Math.Abs(p.CMeltBrewster) * 1e-6 * devMax;
                say(string.Format(ci,
                    "  CEILING of a PRESSURE channel, source Eq (8): (sigma_x - sigma_z) "
                    + "<= p/2 = {0:F2} MPa", devMax));
                say(string.Format(ci,
                    "    -> |C_melt|*p/2 = {0:E3}, against a measured surface {1:E3}: "
                    + "ceiling/measured = {2:F0}x, i.e. {3:F2}% retained",
                    ceilPress, PublishedSurfacePeakDn,
                    ceilPress / PublishedSurfacePeakDn,
                    100.0 * PublishedSurfacePeakDn / Math.Max(ceilPress, 1e-30)));
                say(string.Format(ci,
                    "    model cavity pressure peak {0:F1} MPa = {1:F1} change-over + {2:F1} "
                    + "fill drop", pMaxMPa, changeover, pMaxMPa - changeover));
                say(string.Format(ci,
                    "    THE TWO ARE NOT SIMULTANEOUS and adding them OVERSHOOTS: the "
                    + "source's trace"));
                say(string.Format(ci,
                    "    peaks near 80 MPa, not {0:F1}. The fill gradient collapses as flow "
                    + "stops at", pMaxMPa));
                say("    change-over, so a layer vitrifying under compression sees the");
                say("    change-over pressure ALONE. The pressure term reads fill.P and");
                say("    therefore reads high by that fill drop - recorded, not yet fixed,");
                say("    because it belongs with the term rather than with the pressure field.");
                say("    EQUI-BIAXIAL by construction - sigma_x = sigma_y - which is the");
                say("    source's decisive evidence and something this model cannot produce:");
                say("    it carries one scalar dn per station and depth, slow axis along flow.");
            }
            say("");

            // ---- (a) thermal stress bound -------------------------------------
            // THE OUTERMOST NODE IS EXCLUDED FROM EVERY GAPWISE SCAN BELOW, and
            // this is a convention that PREDATES this case rather than a
            // concession invented to pass it: reference case 3 samples kSurf =
            // nz - 2 with the written reason "the outermost node is the boundary
            // itself, where the profile is evaluated on a one-sided stencil".
            //
            // It is also a measurement, not an appeal to precedent. On the first
            // run of this case the boundary node read -66 MPa against an interior
            // maximum of 2.8, so the grid was refined to ask which it was:
            //
            //     nz          41       81      161
            //     boundary  -25.4    -53.1    -66.0   MPa
            //     interior    2.4      2.7      2.8   MPa   (z/d <= 0.90)
            //
            // A physical stress converges under refinement. This one grows
            // without bound and roughly with the node spacing, which is the
            // signature of a one-sided difference taken across the last cell.
            // The interior converges. So the boundary node is an artefact of the
            // discretisation and reporting it as the model's answer would have
            // published a number that has no limit.
            double sigMax = 0.0, dnThMax = 0.0;
            for (int k = 1; k < nz - 1; k++)
            {
                sigMax = Math.Max(sigMax, Math.Abs(ch.SigmaThermalMPa[iSta, k]));
                dnThMax = Math.Max(dnThMax,
                    Math.Abs(p.KGlassBrewster * 1e-6 * ch.SigmaThermalMPa[iSta, k]));
            }
            // GRID-ROBUST COMPANION, PRINTED AND NOT SCORED. The peak above is
            // taken over every interior node, and the last few cells are where
            // the one-sided stencil is worst - the value moves 3.07 / 10.42 /
            // 7.44 MPa at nz 41 / 81 / 161 while the region inside |z/d| = 0.9
            // is stable to a few percent. So the peak is a poorly conditioned
            // statistic and the interior maximum is the honest one.
            //
            // It is NOT substituted for the registered clause. The clause was
            // committed before the run and swapping its statistic after seeing it
            // fail would be moving the bar - and it would change nothing, since
            // the interior maximum is itself at the top of the registered band.
            double sigInterior = 0.0;
            for (int k = 1; k < nz - 1; k++)
            {
                if (Math.Abs(freeze.Z[k]) / half > 0.90) continue;
                sigInterior = Math.Max(sigInterior, Math.Abs(ch.SigmaThermalMPa[iSta, k]));
            }

            say("  near-surface detail (where the stencil is one-sided):");
            for (int k = nz - 1; k >= nz - 9 && k >= 0; k--)
                say(string.Format(ci, "    z/d {0:F4}   sigma_th {1,10:F3} MPa{2}",
                    Math.Abs(freeze.Z[k]) / half, ch.SigmaThermalMPa[iSta, k],
                    k == nz - 1 ? "   <- boundary node, excluded from every scan" : ""));
            say("");

            double sigBound = PublishedThermalStressMaxMPa * StressBoundFactor;
            bool aOk = sigMax <= sigBound;
            say(string.Format(ci,
                "  (a) peak |sigma_thermal| {0:F3} MPa, published <= {1:F1}, bound {2:F1}  =>  {3}",
                sigMax, PublishedThermalStressMaxMPa, sigBound, aOk ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "      grid-robust companion (not scored): interior max over |z/d| <= 0.90 "
                + "is {0:F3} MPa, i.e. {1:F1}x the published bound",
                sigInterior, sigInterior / PublishedThermalStressMaxMPa));

            // ---- (b) thermal birefringence bound ------------------------------
            double dnThBound = PublishedThermalDnMax * StressBoundFactor;
            bool bOk = dnThMax <= dnThBound;
            say(string.Format(ci,
                "  (b) peak |dn_thermal| {0:E3}, published <= {1:E1}, bound {2:E1}  =>  {3}",
                dnThMax, PublishedThermalDnMax, dnThBound, bOk ? "PASS" : "FAIL"));

            // ---- (c) total magnitude, one-sided -------------------------------
            double totalSum = 0.0, flowSum = 0.0, thermSum = 0.0;
            for (int k = 1; k < nz - 1; k++)
            {
                totalSum += Math.Abs(ch.DnTotalOutOfPlane[iSta, k]);
                flowSum += Math.Abs(ch.DnFlow[iSta, k]);
                thermSum += Math.Abs(p.KGlassBrewster * 1e-6 * ch.SigmaThermalMPa[iSta, k]);
            }
            int nInterior = nz - 2;
            double totalAvg = totalSum / nInterior, flowAvg = flowSum / nInterior,
                   thermAvg = thermSum / nInterior;
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
            double flowPeak = 0.0;
            for (int k = kMid; k <= nz - 2; k++)   // nz-1 is the boundary node
                flowPeak = Math.Max(flowPeak, Math.Abs(ch.DnFlow[iSta, k]));
            double flowPeakZ = FlowPeakZOverD(ch.DnFlow, freeze.Z, iSta, nz, half);
            bool dOk = flowPeakZ >= FlowPeakZOverDLo && flowPeakZ <= FlowPeakZOverDHi;
            say(string.Format(ci,
                "  (d) flow-channel peak {0:E3} at |z/d| {1:F3}, against the published "
                + "FILLING maximum {2:F2}, band [{3:F2}, {4:F2}]  =>  {5}",
                flowPeak, flowPeakZ, PublishedFillingPeakZOverD,
                FlowPeakZOverDLo, FlowPeakZOverDHi, dOk ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "      NOT compared against the SURFACE maximum at {0:F2} - that is a "
                + "different feature", PublishedSurfacePeakZOverD));
            say("      from a different mechanism (transient pressure in the vitrifying");
            say("      layer), which this model does not carry. Re-registered 2026-08-19");
            say("      after IPP 11(4) 373 showed the old comparison crossed features.");

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
                Measure(tmSeries[j], nz, semiDia, gateThick, fillS, incremental, adhered,
                        ejectS, fountain, pressVit, changeover, stationFrac, out pz, out ps, out av);
                peakZ[j] = pz; peakSig[j] = ps;
                say(string.Format(ci, "    {0,3:F0} C       {1:F3}              {2:F3}"
                    + "               {3:E3}", tmSeries[j], pz, ps, av));
            }
            bool e1Ok = peakZ[1] >= peakZ[0] && peakZ[2] >= peakZ[1] && peakZ[2] > peakZ[0];
            // IS (e2) MEASURING ANYTHING? Under the adhered boundary condition
            // this part's thermal stress is zero to about 1e-7 MPa, and a
            // strict > comparison between two numbers that size resolves on
            // floating-point noise. It would then print PASS while carrying no
            // information, which is worse than a FAIL.
            //
            // The registered clause is NOT changed - it stands as committed, and
            // tightening a bar after watching it pass is still moving it. The
            // vacuity is reported beside the verdict instead, and recorded for
            // the next registration.
            bool e2Ok = peakSig[0] > peakSig[2];
            bool e2Vacuous = Math.Max(peakSig[0], peakSig[2]) < 0.01;
            say(string.Format(ci,
                "  (e1) flow peak moves OUTWARD with Tm: {0:F3} -> {1:F3} -> {2:F3}  =>  {3}",
                peakZ[0], peakZ[1], peakZ[2], e1Ok ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (e2) thermal stress rises as Tm falls: {0:F3} MPa at 30 C vs {1:F3} at "
                + "90 C  =>  {2}", peakSig[0], peakSig[2], e2Ok ? "PASS" : "FAIL"));
            if (e2Vacuous)
            {
                say("       VACUOUS - both arms are below 0.01 MPa, so this clause is");
                say("       resolving on floating-point noise and its verdict carries no");
                say("       information. See the release-time sweep below for a test of");
                say("       the same channel that does bite.");
            }

            // ---- (f) null and (g) its control ---------------------------------
            say("");
            var pNull = p.WithZeroCte();
            // The SAME freeze history. Zeroing the CTE cannot change the
            // conduction solve - FreezeHistory reads diffusivity, Tg and the two
            // process temperatures and never touches thermal expansion - so
            // rebuilding it here solved the identical problem a second time.
            var chNull = Channels.Build(plate, pNull, proc, fill, freeze);
            double nullMax = 0.0;
            for (int k = 1; k < nz - 1; k++)
                nullMax = Math.Max(nullMax, Math.Abs(chNull.SigmaThermalMPa[iSta, k]));
            bool fOk = nullMax <= 1e-12;
            bool gOk = sigMax > 1e-9;
            say(string.Format(ci,
                "  (f) null: CTE = 0 collapses the thermal stress, largest {0:E3} MPa  =>  {1}",
                nullMax, fOk ? "PASS" : "FAIL"));
            say(string.Format(ci,
                "  (g) control on the null: CTE restored gives {0:E3} MPa  =>  {1}",
                sigMax, gOk ? "PASS" : "FAIL"));

            // ---- RELEASE-TIME SWEEP: can the adhered channel respond at all? --
            //
            // THIS IS THE CONTROL ON A ZERO. Under adhesion this part's residual
            // thermal stress comes out at ~1e-7 MPa, and a channel that returns
            // zero is indistinguishable from a channel that is DEAD unless it is
            // shown to move when the physics says it should. Clauses (a), (b) and
            // (e2) all pass here without discriminating anything, so on their own
            // they are not evidence.
            //
            // The construction's own prediction is specific and cheap to test:
            // the residual is set by the temperature NON-UNIFORMITY at release
            // and by nothing else, so ejecting the part early - while the core is
            // still hot - must produce a large stress, and holding it until the
            // wall temperature has reached the core must produce none. If the
            // sweep is flat, the mechanism is not implemented, it is just absent.
            if (adhered)
            {
                say("");
                say("  RELEASE-TIME SWEEP (control on the zero - the mechanism's own");
                say("  prediction is that residual stress tracks the non-uniformity at release):");
                say("     release s    core-skin dT at release    peak sigma_th MPa");
                double tFreeze = freeze.CentreFreezeTimeS;
                var times = new[] { tFreeze, tFreeze + 1.0, tFreeze + 3.0, tFreeze + 10.0,
                                    tFreeze + 30.0, ejectS };
                double first = double.NaN, last = double.NaN;
                // The freeze history does NOT depend on the release time - it is
                // built from the melt and wall temperatures alone - so it is
                // solved once and reused. Rebuilding it inside the loop cost six
                // full conduction solves per run and changed nothing.
                foreach (double te in times)
                {
                    var tRel = freeze.TempProfileAtC(te, p, proc);
                    var sg = Channels.ThermalProfileAdhered(freeze.Z, tRel, p.TgC, p.CtePerK,
                                                            eOver1MinusNuOf(p));
                    double mx = 0.0;
                    for (int k = 1; k < nz - 1; k++) mx = Math.Max(mx, Math.Abs(sg[k]));
                    double dT = tRel[nz / 2] - tRel[1];
                    if (double.IsNaN(first)) first = mx;
                    last = mx;
                    say(string.Format(ci, "     {0,8:F2}    {1,18:F2}    {2,17:F3}", te, dT, mx));
                }
                say(string.Format(ci,
                    "    span {0:F3} -> {1:F3} MPa. The channel {2}.",
                    first, last,
                    first > 100.0 * Math.Max(last, 1e-9)
                        ? "RESPONDS - ejecting hot builds stress, holding to uniformity removes it"
                        : "DOES NOT RESPOND, so the zeros above are a dead channel, not a result"));
                say("    So the ~0 at the registered release time is a PREDICTION about this");
                say("    part - 2 mm held for 60 s against a ~3 s thermal time constant - and");
                say("    not a property of the construction.");
            }

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
        /// <summary>
        /// Where the gapwise flow peak actually sits, to SUB-NODE accuracy.
        ///
        /// WHY: the peak used to be reported as a node index, so its resolution
        /// was the grid spacing - 0.05 in z/d at nz=41. The published shift with
        /// mould temperature is smaller than that, so at nz=41 all three arms of
        /// clause (e1) returned exactly 0.900 and the clause FAILED for want of
        /// resolution rather than for want of the effect. At nz=161 it passed but
        /// with two arms tied on the same node, which is the same defect wearing
        /// a pass. An instrument whose arms cannot differ is not weak evidence,
        /// it is none.
        ///
        /// A parabola through the peak node and its two neighbours puts the
        /// vertex between nodes, which is a strictly better estimator of the same
        /// quantity - it does not move the bar, it stops quantising the reading.
        /// Falls back to the node itself when the three points are not concave,
        /// which is the honest answer rather than an extrapolation.
        ///
        /// Used by the reported clause AND by the trend series, from one place,
        /// because a diagnostic that recomputes what the clause computed is how
        /// this project has previously ended up with two disagreeing columns.
        /// </summary>
        private static double FlowPeakZOverD(double[,] dnFlow, double[] z, int station,
                                             int nz, double half)
        {
            int kMid = nz / 2, kBest = kMid;
            double best = -1.0;
            for (int k = kMid; k <= nz - 2; k++)      // nz-1 is the boundary node
            {
                double v = Math.Abs(dnFlow[station, k]);
                if (v > best) { best = v; kBest = k; }
            }
            double zPeak = Math.Abs(z[kBest]) / half;
            if (kBest > 0 && kBest < nz - 1)
            {
                double y0 = Math.Abs(dnFlow[station, kBest - 1]);
                double y1 = Math.Abs(dnFlow[station, kBest]);
                double y2 = Math.Abs(dnFlow[station, kBest + 1]);
                double denom = y0 - 2.0 * y1 + y2;
                if (denom < 0.0)                       // concave: a real maximum
                {
                    double delta = 0.5 * (y0 - y2) / denom;
                    if (Math.Abs(delta) <= 1.0)
                    {
                        double dz = Math.Abs(z[kBest + 1] - z[kBest]) / half;
                        zPeak += delta * dz * Math.Sign(z[kBest] == 0 ? 1 : z[kBest]);
                        zPeak = Math.Abs(zPeak);
                    }
                }
            }
            return zPeak;
        }

        private static double eOver1MinusNuOf(Polymer p)
        {
            return p.ModulusMPa / (1.0 - p.PoissonRatio);
        }

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
                                    double fillS, bool incremental, bool adhered, double ejectS,
                                    double fountain, bool pressVit, double changeover,
                                    double stationFrac,
                                    out double flowPeakZOverD, out double peakSigmaMPa,
                                    out double avgTotalDn)
        {
            var p = Polymers.ByName("MS_POLYCARB").WithProcessTemps(MeltTempC, tm);
            var proc = new Process
            {
                FillTimeS = fillS, PackPressureMPa = 0.0, PackTimeS = 0.0,
                IncrementalThermal = incremental,
                MouldAdhesion = adhered, EjectionTimeS = ejectS,
                FountainStrain = fountain,
                PressureVitrification = pressVit,
                ChangeoverPressureMPa = changeover,
            };
            var e = BuildElement(semiDia, gateThick);
            var fill = FillField.Build(e, p, proc, 101);
            var freeze = FreezeHistory.Build(e.CentreThicknessMm, p, proc, nz, 10 * nz);
            var ch = Channels.Build(e, p, proc, fill, freeze);

            int ns = ch.S.Length;
            int i = Math.Max(0, Math.Min(ns - 1, (int)Math.Round(stationFrac * (ns - 1))));
            double half = 0.5 * ThicknessMm;

            double sig = 0.0, tot = 0.0;
            double bestZ = FlowPeakZOverD(ch.DnFlow, freeze.Z, i, nz, half);
            for (int k = 1; k < nz - 1; k++)
            {
                sig = Math.Max(sig, Math.Abs(ch.SigmaThermalMPa[i, k]));
                tot += Math.Abs(ch.DnTotalOutOfPlane[i, k]);
            }
            flowPeakZOverD = bestZ;
            peakSigmaMPa = sig;
            avgTotalDn = tot / (nz - 2);
        }
    }
}

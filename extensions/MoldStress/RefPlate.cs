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

        /// <summary>
        /// THE MEASURED CAVITY-PRESSURE TRACE for this exact sample, digitised
        /// from the source's own transducer recording: Wimberger-Friedl 1991
        /// thesis, ch. 3.3 Fig. 9, p. 137 - 25.4 cm3/s, mould 60 C, barrel
        /// 320 C, NO packing stage, so the whole pulse is change-over
        /// compression.
        ///
        /// AXIS CALIBRATION: y ticks at 0/20/40/60/80 MPa, x ticks at
        /// 0/0.5/1.0/1.5 s. The screw-position trace xs descends to baseline at
        /// t ~ 0.4 s, which independently marks the end of filling and agrees
        /// with the 0.220 s fill time derived from 5600 mm3 at 25.4 cm3/s plus
        /// the ~0.1 s before pressure registers.
        ///
        /// THE FOUR TRANSDUCERS ARE NOT RESOLVED SEPARATELY, and the text says
        /// why they need not be: "After the completion of the filling all
        /// pressure curves lie on top of each other" (p. 136). Before that they
        /// differ, but the peak is where the frozen-in contribution is decided
        /// and there they are within a few MPa of one another.
        ///
        /// UNCERTAINTY, stated per region rather than implied by the digit count:
        ///   peak height      82 MPa, +-4    (the text quotes ~80)
        ///   peak position    0.37 s, +-0.02
        ///   rise            0.25-0.37 s, the LEAST reliable part of the read
        ///                   because it is nearly vertical on the page
        ///   decay           +-10% in pressure
        ///   TAIL            3 MPa, and this is the dominant uncertainty - see
        ///                   below and TailPressureMPa
        ///
        /// THE TAIL IS THE WHOLE BALL GAME, which a first pass got wrong by
        /// treating it as a truncation artefact. After ~0.9 s the traces settle
        /// a few MPa ABOVE the zero line and stay there to 1.9 s, which is
        /// physically ordinary - the cavity stays pressed until the part shrinks
        /// off the wall. It matters out of all proportion because a stress that
        /// never returns to zero keeps the fully saturated stress-optical
        /// coefficient: 0.2 MPa of deviatoric residual is worth 1.1e-3, half the
        /// measured surface birefringence. So the tail is EXPOSED as a swept
        /// parameter rather than digitised to a single number, and the case
        /// prints the sensitivity instead of a point value.
        /// </summary>
        public static readonly double[] PressureTraceS =
            { 0.00, 0.10, 0.20, 0.25, 0.28, 0.30, 0.32, 0.34, 0.36, 0.37,
              0.39, 0.42, 0.45, 0.50, 0.55, 0.60, 0.70, 0.80, 0.90, 1.00,
              1.20, 1.50, 1.90 };
        public static readonly double[] PressureTraceMPa =
            { 0.0,  0.0,  0.0,  1.0,  3.0,  8.0, 20.0, 45.0, 70.0, 82.0,
             78.0, 66.0, 52.0, 36.0, 24.0, 16.0,  9.0,  6.0,  4.5,  4.0,
              3.5,  3.0,  3.0 };

        /// <summary>Where the trace settles after the pulse, MPa. Swept, because
        /// the frozen-in result is more sensitive to this than to the 82 MPa peak
        /// and it is the hardest thing to read off the scan.</summary>
        public const double TailPressureMPa = 3.0;

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
                              "-fountain", "-pressure-vitrification", "-changeover", "-nt" },
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
            int ntSamples = (int)Program.Value(args, "-nt", 960.0);
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
                TimeSamples = ntSamples,
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
                        ejectS, fountain, pressVit, changeover, ntSamples, stationFrac, out pz, out ps, out av);
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

            // ---- OPTICAL MEMORY: the reachability RE-CHECK, on measured tau --
            //
            // The earlier check (ct-reachability.py, 1b309de) assumed ONE tau per
            // target and inverted the retention for it. That was a bound, not a
            // calculation, and the measured tau(T) overturned it: retention runs
            // 0.12 at 140 C to 0.99 at 150 C, so it is set by WHERE IN THE
            // TRANSITION a layer sits while the stress acts, not by a single
            // number. This computes it properly, along each layer's own cooling
            // curve, with the measured constants.
            //
            // Printed rather than wired into a channel. The primitives are tested
            // against the source's own numbers, but nothing here has been shown
            // to reproduce a measurement yet, and a channel switched on before
            // that is a fit waiting to happen.
            if (p.HasOpticalMemory)
            {
                say("");
                say("  OPTICAL MEMORY re-check, on MEASURED tau(T) - not yet a channel:");
                say(string.Format(ci,
                    "    beta {0:F2}, tau({1:F0} C) {2:F0} s, WLF C1 {3:F1} / C2 {4:F1}, "
                    + "C_g {5:F0} Br, C_m {6:F0} Br",
                    p.OpticalBeta, p.OpticalTau0TempC, p.OpticalTau0S,
                    p.OpticalWlfC1, p.OpticalWlfC2K,
                    p.OpticalCgBrewster, p.OpticalCmBrewster));
                say("     z/d    freeze s     tau(T_frz) s        xi     retained    C(xi) Br");
                var row = new double[freeze.TimeGridS.Length];
                for (int f = 0; f <= 10; f++)
                {
                    int k = kMid + (int)Math.Round((nz - 1 - kMid) * f / 10.0);
                    if (k > nz - 1) k = nz - 1;
                    for (int j = 0; j < row.Length; j++) row[j] = ch.Z != null
                        ? freeze.TempHistoryC[k, j] : 0.0;
                    double xi = Channels.OpticalReducedTime(
                        freeze.TimeGridS, row, p, freeze.FreezeTimeS[k]);
                    double frac = Channels.OpticalRetainedFraction(xi, p);
                    double cxi = Channels.OpticalCoefficientBrewster(xi, p);
                    say(string.Format(ci,
                        "    {0:F2}  {1,10:F3}  {2,15:E3}  {3,9:E2}  {4,9:F4}  {5,10:F0}",
                        Math.Abs(freeze.Z[k]) / half, freeze.FreezeTimeS[k],
                        p.OpticalTauS(freeze.TrefC[k]), xi, frac, cxi));
                }
                say("");
                say("    READ THIS BEFORE USING THE COLUMN ABOVE. Retention saturates at");
                say("    1.0000 for every layer, and that is an ARTEFACT of two things,");
                say("    both of which the naive integral gets wrong:");
                say("");
                say(string.Format(ci,
                    "    (1) tau(T) IS FITTED OVER {0:F0}-150 C AND THIS INTEGRATES FROM "
                    + "{1:F0} C.", 135.0, p.MeltTempC));
                say("        xi = INT dt/tau is dominated by the hot phase, where the WLF");
                say("        extrapolation returns a vanishing tau far outside its range -");
                say("        so xi reaches 1e4-1e5 and the exponential saturates. The");
                say("        source's own stated floor is the mirror of this: below 148 C");
                say("        it over-estimates tau. Neither end of the fit is safe here.");
                say("");
                say("    (2) C(xi) IS A BUILD-UP, NOT A RETENTION. It answers how much");
                say("        birefringence DEVELOPS under a stress that persists, which is");
                say("        only half of Eq (3). The frozen-in part needs the full");
                say("        convolution over a stress history that RISES AND FALLS -");
                say("        negative increments contribute negatively - and this model");
                say("        carries cavity pressure as a single number, not as sigma(t).");
                say("        The author is explicit: the polymer 'will creep under the high");
                say("        stresses so that PART of the momentary birefringence will be");
                say("        frozen in', and puts the residual at C_g times a residual");
                say("        stress below 10 MPa - about 10e-4, against a measured 20e-4.");
                say("");
                say(string.Format(ci,
                    "    Taken literally the saturated column would give the pressure term "
                    + "{0:F0}x", (p.OpticalCgBrewster + p.OpticalCmBrewster) * 1e-6 * 33.0
                        / PublishedSurfacePeakDn));
                say("    the measured surface value, which is the same over-prediction the");
                say("    pressure channel already shows and NOT a fix for it.");
                say("");
                say("    A layer retains 1 - exp(-xi^beta) of the RUBBERY response and the");
                say("    glassy part always. Compare C(xi) against this table's two limits:");
                say(string.Format(ci,
                    "      C_g alone = {0:F0} Br   C_g + C_m = {1:F0} Br   "
                    + "(the model currently switches between {2:F0} and {3:F0})",
                    p.OpticalCgBrewster, p.OpticalCgBrewster + p.OpticalCmBrewster,
                    p.KGlassBrewster, p.CMeltBrewster));
            }

            // ---- IS THE FREEZE SOLVE TOO FAST AT THE WALL? -------------------
            //
            // The sigma(t) convolution returned exactly zero for the outermost
            // layers because they vitrify before the pressure arrives. That is
            // either physics or a wrong freeze solve, and this asks which by two
            // independent routes:
            //
            //   (i) against the CLOSED FORM the model already carries as a
            //       control, FreezeHistory.ErfFreezeTime - the short-time
            //       similarity solution for a half-space quenched at its face;
            //
            //  (ii) against the SOURCE's own statement. The 1996 paper says the
            //       birefringence maximum due to filling marks "the thickness of
            //       the solidified layer at the end of filling" (p. 375, p. 138),
            //       and places that maximum at z/d = 0.8. On a 2 mm plate that is
            //       a 0.2 mm skin at the end of a 0.220 s fill.
            //
            // Neither route is this model's own arithmetic, which is the point.
            {
                say("");
                say("  FREEZE SOLVE AT THE WALL - two independent checks:");
                say("     z/d     depth mm   model s    erf closed form s     ratio");
                foreach (double zd in new[] { 0.98, 0.95, 0.90, 0.85, 0.80, 0.70 })
                {
                    int k = (int)Math.Round(kMid + (nz - 1 - kMid) * zd);
                    if (k > nz - 1) k = nz - 1;
                    double depth = half - Math.Abs(freeze.Z[k]);
                    double tErf = FreezeHistory.ErfFreezeTime(depth, p, proc);
                    double tMod = freeze.FreezeTimeS[k];
                    say(string.Format(ci, "    {0:F2}   {1,9:F4}  {2,9:F4}   {3,17:F4}   {4,7:F2}",
                        Math.Abs(freeze.Z[k]) / half, depth, tMod, tErf,
                        tErf > 0 ? tMod / tErf : double.NaN));
                }

                // The solidified thickness at the END OF FILL, which is the
                // quantity the source states independently.
                double frozenAtFill = 0.0;
                for (int k = nz - 1; k >= kMid; k--)
                    if (freeze.FreezeTimeS[k] <= fillS)
                        frozenAtFill = Math.Max(frozenAtFill, half - Math.Abs(freeze.Z[k]));
                double zdFront = (half - frozenAtFill) / half;
                say("");
                say(string.Format(ci,
                    "    solidified layer at the end of fill ({0:F3} s): {1:F3} mm, "
                    + "i.e. the front", fillS, frozenAtFill));
                say(string.Format(ci,
                    "    sits at z/d {0:F2}. The SOURCE puts its filling maximum at z/d 0.80 "
                    + "and calls", zdFront));
                say(string.Format(ci,
                    "    that the solidified thickness, i.e. {0:F3} mm - so this model "
                    + "freezes a", 0.20));
                say(string.Format(ci,
                    "    layer {0:F2}x {1} than the source implies.",
                    frozenAtFill > 0 ? 0.20 / frozenAtFill : double.NaN,
                    frozenAtFill < 0.20 ? "THINNER" : "thicker"));
                say("    Note the DIRECTION: the candidate under test was 'the freeze solve");
                say("    is too FAST at the wall'. Both checks say the opposite - it tracks");
                say("    its own closed form to within 6% and freezes a layer 2x too THIN.");
                say("");
                say("    AND THAT REFUTES THE CANDIDATE TWICE OVER. Correcting the solve");
                say("    toward the source would freeze MORE of the skin before change-over,");
                say("    not less - z/d 0.80 would vitrify inside the fill instead of at");
                say("    0.86 s - so the layers reading zero would get deeper. Whatever");
                say("    explains the surface maximum, it is not that this solve freezes the");
                say("    skin too early.");
            }

            // ---- sigma(t): THE Eq (3) CONVOLUTION ON THE MEASURED TRACE ------
            //
            // The previous diagnostic showed the retained-fraction picture
            // saturating and said why: C(xi) is a build-up, and the frozen part
            // needs the full convolution over a stress history that rises AND
            // falls. This runs that convolution on the source's own transducer
            // trace, so nothing here is modelled - the pressure is measured, the
            // deviatoric conversion is the source's Eq (8), and the optical
            // constants are measured in its ref [27].
            if (p.HasOpticalMemory)
            {
                double devFac = (1.0 - 2.0 * p.PoissonRatio) / (1.0 - p.PoissonRatio);
                var devMPa = new double[PressureTraceMPa.Length];
                for (int j = 0; j < devMPa.Length; j++)
                    devMPa[j] = PressureTraceMPa[j] * devFac;

                say("");
                say("  sigma(t) CONVOLUTION on the MEASURED pressure trace (source Fig. 9):");
                // DERIVED, not indexed. This read PressureTraceMPa[5] and printed
                // "peak 8 MPa" the moment the trace was re-digitised and index 5
                // stopped being the peak - a hardcoded position surviving the
                // change it should have tracked.
                int kPk = 0;
                for (int j2 = 1; j2 < PressureTraceMPa.Length; j2++)
                    if (PressureTraceMPa[j2] > PressureTraceMPa[kPk]) kPk = j2;
                say(string.Format(ci,
                    "    peak {0:F0} MPa cavity at {1:F2} s -> {2:F1} MPa deviatoric "
                    + "(Eq 8 factor {3:F3} at nu = {4:F2}), tail {5:F1} MPa",
                    PressureTraceMPa[kPk], PressureTraceS[kPk], devMPa[kPk], devFac,
                    p.PoissonRatio, TailPressureMPa));
                say("     z/d    freeze s     dn_frozen     vs measured 20e-4");
                var rowT = new double[freeze.TimeGridS.Length];
                double dnSurf = 0.0, dnCore = 0.0;
                for (int f = 0; f <= 10; f++)
                {
                    int k = kMid + (int)Math.Round((nz - 1 - kMid) * f / 10.0);
                    if (k > nz - 1) k = nz - 1;
                    for (int j = 0; j < rowT.Length; j++) rowT[j] = freeze.TempHistoryC[k, j];
                    double dnp = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, PressureTraceS, devMPa, p,
                        freeze.FreezeTimeS[k]);
                    double zd = Math.Abs(freeze.Z[k]) / half;
                    if (f == 0) dnCore = dnp;
                    if (f >= 9) dnSurf = Math.Max(dnSurf, dnp);
                    say(string.Format(ci, "    {0:F2}  {1,10:F3}  {2,12:E3}  {3,14:F2}x",
                        zd, freeze.FreezeTimeS[k], dnp, dnp / PublishedSurfacePeakDn));
                }
                // TAIL SENSITIVITY, swept rather than asserted. The frozen-in
                // birefringence of a stress that never returns to zero keeps the
                // saturated coefficient, so the settled tail dominates the core
                // and competes with the peak at depth. This prints what the
                // answer does across the readable range instead of quoting one
                // number off a scan.
                say("");
                say("  TAIL SENSITIVITY - the dominant uncertainty in the digitisation:");
                say("     tail MPa      core dn      surface dn    surface/core");
                foreach (double tail in new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 6.0 })
                {
                    var devT = new double[devMPa.Length];
                    for (int j2 = 0; j2 < devT.Length; j2++) devT[j2] = devMPa[j2];
                    // everything at or after 0.9 s settles to the swept value
                    for (int j2 = 0; j2 < PressureTraceS.Length; j2++)
                        if (PressureTraceS[j2] >= 0.90) devT[j2] = tail * devFac;

                    for (int j2 = 0; j2 < rowT.Length; j2++)
                        rowT[j2] = freeze.TempHistoryC[kMid, j2];
                    double cCore = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, PressureTraceS, devT, p,
                        freeze.FreezeTimeS[kMid]);

                    int kS = (int)Math.Round(kMid + (nz - 1 - kMid) * 0.8);
                    for (int j2 = 0; j2 < rowT.Length; j2++)
                        rowT[j2] = freeze.TempHistoryC[kS, j2];
                    double cSurf = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, PressureTraceS, devT, p,
                        freeze.FreezeTimeS[kS]);

                    say(string.Format(ci, "     {0,7:F1}   {1,11:E3}   {2,12:E3}   {3,12:F1}",
                        tail, cCore, cSurf,
                        Math.Abs(cCore) > 1e-20 ? cSurf / cCore : double.NaN));
                }
                // ---- CANDIDATE 2: DOES THE PRESSURE ARRIVE EARLIER? -----------
                //
                // The skin reads zero because it vitrifies at 0.206 s and the
                // digitised trace shows nothing until 0.25 s - a gap of 0.044 s,
                // which is the same order as the read error on a rise that is
                // nearly vertical on the page. So this shifts the whole trace
                // earlier and asks how far it has to move before the skin lights
                // up, then compares that against the uncertainty I actually
                // stated: peak position 0.37 +- 0.02 s.
                //
                // The test is not "can a shift fix it" - any large enough shift
                // will. It is whether the REQUIRED shift fits inside the error
                // bar that was written down before this question was asked.
                say("");
                say("  CANDIDATE 2: trace shifted earlier, against a stated +-0.02 s:");
                say("     shift s    onset s    dn at z/d 0.95    dn at z/d 0.90");
                int k95 = (int)Math.Round(kMid + (nz - 1 - kMid) * 0.95);
                int k90 = (int)Math.Round(kMid + (nz - 1 - kMid) * 0.90);
                foreach (double shift in new[] { 0.00, -0.02, -0.04, -0.06, -0.10, -0.15 })
                {
                    var tShift = new double[PressureTraceS.Length];
                    for (int j2 = 0; j2 < tShift.Length; j2++)
                        tShift[j2] = Math.Max(PressureTraceS[j2] + shift, 0.0);
                    // onset = first time carrying any pressure at all
                    double onset = double.NaN;
                    for (int j2 = 0; j2 < tShift.Length; j2++)
                        if (PressureTraceMPa[j2] > 0.0) { onset = tShift[j2]; break; }

                    for (int j2 = 0; j2 < rowT.Length; j2++)
                        rowT[j2] = freeze.TempHistoryC[k95, j2];
                    double d95 = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, tShift, devMPa, p, freeze.FreezeTimeS[k95]);
                    for (int j2 = 0; j2 < rowT.Length; j2++)
                        rowT[j2] = freeze.TempHistoryC[k90, j2];
                    double d90 = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, tShift, devMPa, p, freeze.FreezeTimeS[k90]);
                    say(string.Format(ci, "    {0,7:F2}   {1,8:F3}   {2,15:E3}   {3,15:E3}",
                        shift, onset, d95, d90));
                }
                say(string.Format(ci,
                    "    z/d 0.95 freezes at {0:F3} s, z/d 0.90 at {1:F3} s.",
                    freeze.FreezeTimeS[k95], freeze.FreezeTimeS[k90]));
                say("    A shift inside the stated +-0.02 s does NOT reach the 0.95 layer,");
                say("    which freezes at 0.05 s - it would need about -0.20 s, ten times the");
                say("    error bar and a quarter of the whole fill. So candidate 2 cannot");
                say("    rescue the OUTERMOST layers within its own uncertainty. It does");
                say("    move the 0.90 layer, which sits right at the edge of the read.");
                say("");
                // ---- CANDIDATE 3: FOUNTAIN TRANSPORT, NOT FOUNTAIN STRESS -----
                //
                // Candidates 1 and 2 both failed for the same underlying reason:
                // the outermost skin vitrifies at 0.051 s, a quarter of the way
                // into the fill, so at that station the cavity is not yet full
                // and there is no cavity pressure to reach it at any shift.
                //
                // But that assumed the skin has been sitting at the wall since
                // t = 0, and in fountain flow it has not. The material at the
                // wall at station s was carried there from the core of the melt
                // when the FRONT passed s, so its clock starts at t_arrive(s),
                // not at zero. The model already computes that arrival time -
                // tFill * s / pathLength - for the fountain deposition term.
                //
                // THE DECISIVE TEST IS NOT WHETHER THIS HELPS. It is whether it
                // produces a GATE-DISTANCE DEPENDENCE, because the 1996 paper
                // reports the surface birefringence "equal for both distances
                // from the gate" and used exactly that independence to rule out
                // fountain flow as the stress source. If transport reintroduces a
                // strong dependence, it is refuted by the same observation.
                say("");
                say("  CANDIDATE 3: fountain TRANSPORT - the skin's clock starts when the");
                say("  front passes, not at t = 0. Tested against the source's own");
                say("  gate-distance independence:");
                say("     s/L    t_arrive s   skin freeze s   vs onset 0.25 s      dn");
                double zSkin = 0.95;
                int kSk = (int)Math.Round(kMid + (nz - 1 - kMid) * zSkin);
                double tFrzLocal = freeze.FreezeTimeS[kSk];
                for (int j2 = 0; j2 < rowT.Length; j2++)
                    rowT[j2] = freeze.TempHistoryC[kSk, j2];
                double first = double.NaN, last = double.NaN;
                foreach (double sFrac in new[] { 0.1, 0.3, 0.5, 0.7, 0.9, 1.0 })
                {
                    double tArr = fillS * sFrac;
                    // The layer's own history is unchanged - it still takes
                    // tFrzLocal to vitrify after being laid down - but the whole
                    // clock is offset, so the pressure trace must be shifted the
                    // other way to stay in the layer's frame.
                    var tRel = new double[PressureTraceS.Length];
                    for (int q = 0; q < tRel.Length; q++)
                        tRel[q] = PressureTraceS[q] - tArr;
                    double dnT = Channels.FrozenBirefringence(
                        freeze.TimeGridS, rowT, tRel, devMPa, p, tFrzLocal);
                    if (double.IsNaN(first)) first = dnT;
                    last = dnT;
                    say(string.Format(ci,
                        "    {0:F1}   {1,10:F3}   {2,13:F3}   {3,15}   {4,9:E3}",
                        sFrac, tArr, tArr + tFrzLocal,
                        (tArr + tFrzLocal) > 0.25 ? "AFTER onset" : "before onset", dnT));
                }
                say(string.Format(ci,
                    "    span {0:E3} -> {1:E3} across the flow path.", first, last));
                say("    The source measures the surface value EQUAL at both its distances");
                say("    from the gate, and used that independence to rule fountain flow out");
                say("    as the stress source. This produces the MOST EXTREME possible");
                say("    dependence - identically zero across 90% of the plate and nonzero");
                say("    only at the very last station - so it is refuted by the same");
                say("    observation, and refuted harder than the mechanism it replaced.");
                say("");
                say("  ALL THREE CANDIDATES REFUTED, and together they say something the");
                say("  individual refutations do not:");
                say("");
                say("    The outermost skin vitrifies at 0.051 s, before the cavity at its");
                say("    own station is full. No pressure history can act on it, because at");
                say("    that instant there is no cavity pressure anywhere near it - and");
                say("    that holds however the trace is read, however the clock is offset,");
                say("    and MORE strongly if the freeze solve is corrected toward the");
                say("    source, since that freezes the skin sooner still.");
                say("");
                say("    So a pressure mechanism cannot produce a maximum in the OUTERMOST");
                say("    material. Either the measured maximum sits deeper than z/d 0.95 -");
                say("    the layer-removal steps are 50 um, one grid step in z/d here, so");
                say("    the two are not distinguishable in the source - or the outermost");
                say("    material carries orientation from something that acted before the");
                say("    cavity was full, which is the filling flow, which is where this");
                say("    model already puts it.");
                say("");
                say("    AND THE OUTERMOST LAYERS NOW READ EXACTLY ZERO, which is a result");
                say("    rather than a rounding. z/d 0.90 vitrifies at 0.206 s and the");
                say("    digitised trace shows no pressure until 0.25 s, so the skin freezes");
                say("    BEFORE change-over and cannot carry a pressure contribution at all.");
                say("    The measurement puts its maximum at z/d ~0.95. Three ways out, none");
                say("    yet tested: the freeze solve is too fast at the wall; pressure");
                say("    reaches this station earlier than the trace was read; or the skin is");
                say("    material deposited at the front later in the fill, which is the");
                say("    fountain transport the 1996 paper keeps while rejecting its stress.");
                say("");
                say("    The SHAPE survives the whole range - surface always above core -");
                say("    but the core level does not: it is proportional to the tail and");
                say("    spans four orders across a band the scan cannot resolve. So the");
                say("    surface peak is a result and the core level is not.");
                say("");
                say(string.Format(ci,
                    "    surface/core ratio {0:F1} - the mechanism is surface-peaked without "
                    + "tuning,", dnCore != 0.0 ? Math.Abs(dnSurf / dnCore) : double.NaN));
                say("    because the pulse sits at the near-surface layers' own");
                say("    vitrification and is long gone before the core freezes, so there");
                say("    its rise and fall cancel. That shape is the observation the 1996");
                say("    paper could not get from fountain flow, and nothing here is fitted.");
            }

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
                                    double fountain, bool pressVit, double changeover, int ntSamples,
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
                TimeSamples = ntSamples,
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

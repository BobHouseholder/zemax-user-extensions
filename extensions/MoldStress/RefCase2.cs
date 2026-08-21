using System;
using System.Globalization;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// SECOND registered reference case - a moulded LENS, not a plate.
    ///
    /// WHY A SECOND CASE. Every number this model has ever been judged on came
    /// from ONE part: a 1.5 mm TOPAS plate. A model tuned to one part that fails a
    /// second part is a model that was tuned, and with one case there is no way to
    /// tell the difference. This case is deliberately different in the three ways
    /// that matter - it is a LENS with curvature, it is a different polymer grade,
    /// and its depth data comes from a different measurement principle.
    ///
    /// SOURCE, read in full rather than from an abstract:
    ///   Chang, Yu, Chiu, Yang, Lai and Wang, "Simulations and Verifications of
    ///   True 3D Optical Parts by Injection Molding Process", CoreTech System
    ///   (Moldex3D) and National Tsing Hua University.
    ///   https://www.moldex3d.com/assets/2011/09/SMGG5H7.pdf
    ///
    /// PART: plano-convex spherical lens, 32 mm diameter, 2 mm centre thickness,
    /// edge gate of 0.8 mm land. ZEONEX 480R, a cyclo-olefin POLYMER.
    ///
    /// PROCESS, from the paper's Table 1: melt 275 C, mould 124 C, holding
    /// pressure 98.10 MPa, injection speed 22 mm/s, cooling 60 s.
    ///
    /// GEOMETRY CAVEAT, stated because the source contradicts itself: the body
    /// text says the curvature is 70 mm and Figure 1 labels it 75 mm. This case
    /// uses 75 mm, from the figure, and the sensitivity is reported so the choice
    /// can be checked rather than trusted.
    ///
    /// MATERIAL CAVEAT: ZEONEX 480R's stress-optic and rheological constants are
    /// BORROWED from the measured TOPAS 6017 COC. Same family, different grade.
    /// Results here are indicative for a cyclo-olefin, not for 480R.
    ///
    /// THE CLAUSES, and why the third is the valuable one:
    ///
    ///   (a) IN-PLANE. Measured birefringence along the filling path, the paper's
    ///       Figure 7, with a stated measurement error below 10%: 3.7e-5 at the
    ///       gate falling to essentially zero by 30 mm. Predicted peak within a
    ///       factor of 2, and the maximum on the gate side.
    ///
    ///   (b) LAYER REMOVAL - a CUMULATIVE distribution from ONE self-consistent
    ///       method. Successive outer layers were diamond-turned off and the
    ///       fringe order recounted (the paper's Table 2):
    ///
    ///           removed   0.1 mm   0.2 mm   0.3 mm   0.4 mm
    ///           fringe %   27.9     30.8     43.9     46.2
    ///
    ///       This is worth more than any two-point ratio. The quantity removed IS
    ///       the quantity measured, so there is no cross-instrument division of
    ///       the kind that produced the withdrawn 5.56, and no sampling-definition
    ///       ambiguity of the kind still open on the plate's 0.2 mm slabs. Four
    ///       points constrain the SHAPE of the profile, not just its ends.
    ///
    /// WHAT THIS CASE CANNOT SETTLE. The measurement is a gapwise AVERAGE
    /// (fringe order over the thickness), so it constrains the integral rather
    /// than the profile pointwise. And the source's own simulation UNDER-predicts:
    /// 5 fringes observed against 4 predicted. Agreeing with it closely is
    /// agreeing with something already known to be about 20% low.
    /// </summary>
    /// WHAT THE IN-PLANE PEAK CLAUSE CURRENTLY TESTS - read this before trying to
    /// close its 13x, 2026-08-18.
    ///
    /// Of this case's three clauses, TWO ARE SCALE-FREE and both pass: layer
    /// removal is a normalised cumulative fraction, and in-plane shape is a
    /// normalised decay along the flow. The in-plane peak is the ONLY magnitude
    /// measure, and it is the only failure. So the case says: shape right,
    /// magnitude 13x high.
    ///
    /// Magnitude is 2 * C_melt * tau * memory, and the two factors were separated:
    ///
    ///     quantity    case 1   case 2   note
    ///     memory       0.235    0.143   model output; case 1 agrees with its
    ///                                   measurement to 16%, so retention is sound
    ///     tau_wall     0.297    1.670   MPa, from the flow solve - 5.6x
    ///
    /// TAU IS THE WHOLE OF IT, AND TAU IS UNPHYSICAL HERE. 1.67 MPa sits above the
    /// melt-fracture threshold for polymer melts (~0.1-0.5 MPa); case 1's 0.297 MPa
    /// is inside the normal moulding range. A melt would not flow smoothly at all
    /// in the regime this solve puts case 2 in.
    ///
    /// AND BOTH INPUTS THAT SET IT ARE UNSOURCED:
    ///   Q  8410 mm3/s, from a screw rate that assumes the WHOLE screw output
    ///      enters this one cavity. A sprue and a runner demonstrably exist - the
    ///      paper places a gate - so some of that output goes somewhere else, and
    ///      this Q is an UPPER BOUND rather than an estimate.
    ///   W  12.6 mm, one eighth of the circumference, CHOSEN HERE. The paper places
    ///      a gate on the Y axis and never states its width.
    ///
    /// RETRACTED 2026-08-18, in place. This block used to argue the same
    /// conclusion from a different fact: "the paper shots 27 g for a 0.92 g part,
    /// a ratio of 29 that a single-cavity sprue and runner does not explain."
    /// THAT WAS A MISREAD. The 27 g is the MACHINE'S MAXIMUM SHOT CAPACITY, not
    /// the shot used for this lens. It is reproduced exactly by the full screw
    /// stroke also given in the same table - pi/4 * 22^2 * 70 mm = 26,609 mm3,
    /// which at ZEONEX 480R's 1.01 g/cm3 is 26.9 g - and it sits among clamping
    /// force 550 kN, maximum pressure 259 MPa and maximum flow 190 cc/s, which
    /// are plainly machine ratings. The paper never states the shot it used.
    ///
    /// WHERE THE ERROR CAME FROM, because that is the reusable part: a number was
    /// taken from a table headed with the process conditions and read as a process
    /// setting, without asking which rows of that table describe the MACHINE and
    /// which describe the RUN. A machine rating and a process setting look
    /// identical once quoted - both are a number with units next to a part - and
    /// the tell was available in the same table, since a stated screw stroke times
    /// a stated screw diameter reproduces the "shot weight" to three figures. When
    /// a source gives both a capacity and a geometry, multiply them out: if they
    /// agree, the number is a rating.
    ///
    /// The conclusion is unchanged and now rests only on what survives - that a
    /// runner exists and Q therefore cannot all be reaching the cavity. The 29x
    /// ratio is withdrawn and must not be requoted.
    ///
    /// ============================================================
    /// THE REGISTERED REFERENCE WAS WRONG BY A FACTOR OF 100. SETTLED
    /// 2026-08-18 BY READING FIG. 7 DIRECTLY.
    ///
    /// Fig. 7's y-axis is labelled "Birefringence (x 10^-5)" and its peak reads
    /// 3.7, which is where the registered 3.7e-5 came from. THE LABEL IS A TYPO
    /// FOR x10^-3, and the paper refutes it with its own arithmetic:
    ///
    ///   Eq. (9) of that paper is dn = lambda*N/h.
    ///   Its text states the maximum fringe count observed under the circular
    ///   polariscope is N = 5 (4 from simulation).
    ///   The gate thickness is h = 0.80 mm - confirmed independently by the
    ///   layer-removal experiment, where 0.4 mm is described as "half of total
    ///   thickness at the gate area".
    ///   So dn = 589.3e-9 * 5 / 0.8e-3 = 3.68e-3.
    ///
    /// Fig. 7's peak reads 3.7. Against 3.68e-3 that agrees to two significant
    /// figures; against 3.7e-5 it is out by 100x. A label can be mistyped; the
    /// paper's own equation, its own stated fringe count and its own gate
    /// thickness cannot all three be wrong in the same direction.
    ///
    /// SETTLED WITHOUT CIRCULARITY, 2026-08-18. The argument above assumes the
    /// gate thickness in order to correct the axis, and the sampling thickness
    /// was then "confirmed" from the corrected axis - which is circular and was
    /// caught before it was relied on. The non-circular form solves Eq. (9) for
    /// the thickness each axis reading would REQUIRE, assuming nothing about h:
    ///
    ///   axis x10^-5 as labelled  ->  h = lambda*N/dn = 79.6 mm
    ///   axis x10^-3              ->  h =                0.796 mm
    ///
    /// 79.6 mm is FORTY TIMES this lens's 2 mm centre thickness and a hundred
    /// times its gate land; there is no dimension in the part, the mould or the
    /// runner anywhere near it. 0.796 mm is the 0.80 mm gate land to within 0.5%.
    /// The label is refuted by its own equation before h is chosen at all, and h
    /// then follows rather than being assumed.
    ///
    /// So the sampling thickness is NOT an open input. Eq. (9)'s text calls Fig. 7
    /// "the gap wise average residual birefringence", which is the same quantity
    /// this model averages through the thickness, and h = 0.80 mm at the peak is
    /// over-determined by three independent statements in the paper. The earlier
    /// note here treating h as the term that decides the clause is WITHDRAWN: it
    /// decides nothing, because it is pinned.
    ///
    /// THE CONSEQUENCE IS THAT THE 5x DEFICIT IS REAL AND UNEXPLAINED. It is not
    /// the flow inputs (the fill time is sourced and tau matches the paper's own
    /// simulation), not the relaxation time (an 8x change buys 15%), not the
    /// retained fraction (its limit still leaves 2x), not the depth port's
    /// normalisation (the particle model predicts the same magnitude), and not
    /// the conversion thickness (pinned here). What remains is the model's
    /// structure: it never has high stress and high retention at the same depth.
    ///
    /// TWO INDEPENDENT CROSS-CHECKS, both from the companion paper (Lai & Wang,
    /// Appl. Opt. 47(12) 2017): a fringe order of 6.5 over the 2 mm centre is
    /// 1.9e-3, and Fig. 14's removal axis runs to 2.21e-3 at 50% removal. Same
    /// order of magnitude. Nothing anywhere in either paper supports 1e-5.
    ///
    /// THE CORRECTION DOES NOT RESCUE THIS CLAUSE, WHICH IS WHY IT CAN BE
    /// TRUSTED. It inverts the failure rather than removing it: the model reads
    /// 4.434e-4 against a corrected 3.68e-3, so it is about 8x LOW where it had
    /// been recorded as 12x HIGH. The bar is a factor of 2 either way and is not
    /// touched. Every conclusion drawn from the old direction is withdrawn -
    /// including "the model over-predicts", the suspicion of the melt-fracture
    /// shear stress as a cause of over-prediction, and the reading that 480R's
    /// borrowed melt coefficient being LARGER than 1000 Br would make things
    /// worse. It would now make them better.
    /// ============================================================
    ///
    /// So the in-plane peak clause is NOT presently a test of the birefringence
    /// model. It is a test of two flow inputs nobody has sourced, and it should
    /// not be "fixed" by tuning the model. It becomes evaluable when the gate
    /// width and the cavity count are known - and Q is a weak lever in any case,
    /// since Cross gives tau ~ Q^0.29.
    ///
    /// The finding that IS about the model: case 2's depth profile peaks at 60% of
    /// the half-wall (dn_flow 9.66e-4 there against 1.49e-4 at the wall), the same
    /// mid-depth peak the Eulerian channel shows on case 1. Case 1's in-plane
    /// magnitude passes anyway because its thickness average happens to land near
    /// the measurement; case 2's does not. Both carry the same defect and only one
    /// is caught by its magnitude clause.
    internal static class RefCase2
    {
        // CORRECTED 2026-08-18, from 3.7e-5, after reading Fig. 7 directly. The
        // FIGURE'S AXIS LABEL IS WRONG BY A FACTOR OF 100 - see the header.
        public const double PublishedInPlanePeakDn = 3.68e-3;
        public const double FactorBar = 2.0;

        // Cumulative fraction of the through-thickness retardance held in the
        // outer t mm, from the paper's Table 2.
        public static readonly double[] RemovalDepthMm = { 0.1, 0.2, 0.3, 0.4 };
        public static readonly double[] RemovedFraction = { 0.279, 0.308, 0.439, 0.462 };

        /// <summary>
        /// Every flag -refcase2 READS. Public so the self-test can hold both arms
        /// of the guard against it without an OpticStudio session.
        ///
        /// KEEP THIS IN STEP WITH THE READS BELOW, in both directions. A flag
        /// missing from the list makes a legitimate run fail loudly, which is
        /// annoying and self-correcting. A flag listed here but never read is the
        /// original defect wearing the guard's uniform: the run proceeds, the
        /// flag does nothing, and the number that comes back looks like it
        /// answered the question that was asked.
        /// </summary>
        internal static readonly string[] ReadsFlags =
        {
            "-complementary", "-curvature", "-deposition-decay", "-deposition-support",
            "-eulerian-depth", "-filltime", "-fountain", "-gatewidth",
            "-incremental-thermal", "-lagrangian-depth", "-lambdascale", "-narrowing",
            "-normal-stress", "-nt", "-nz", "-packfrac", "-packing-orientation",
            "-packpressure", "-packtime", "-relax-below-tg", "-shape-nodes",
            "-shape-particles", "-shape-steps", "-snapshot", "-thinned-lambda",
        };

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            Action<string> say2 = t => Console.WriteLine(t);

            // REFUSE A FLAG THIS MODE DOES NOT READ. Wired 2026-08-21 alongside
            // -refcase. Note what is NOT in the list and used to be swallowed:
            // -adhered, which this mode has never implemented, so every run of
            // `-refcase2 -adhered` reported a free-plate result under an adhered
            // heading. That is the -packtime sweep failure again, one mode over.
            int badForMode = Program.RejectFlagsNotReadBy(args, ReadsFlags, "-refcase2");
            if (badForMode != 0) return badForMode;
            Console.WriteLine("MoldStress - reference case 2: a moulded LENS (ZEONEX 480R)");
            Console.WriteLine("  " + Program.ScopeLabel);
            Console.WriteLine("  source: Chang et al., CoreTech/NTHU, Moldex3D verification study");
            Console.WriteLine("  480R: Tg and nd from the datasheet; stress-optic constants still BORROWED from TOPAS 6017");
            Console.WriteLine();

            // Its OWN entry now, rather than an alias onto TOPAS. What is sourced
            // for 480R is used (Tg 138 C, nd 1.525 from the vendor datasheet);
            // only the stress-optic and rheological constants are still borrowed,
            // and they are marked as such in the table.
            //
            // The Tg is the substitution that mattered most and it was not the
            // coefficient being hunted: TOPAS 6017 is Tg 178 C against 480R's
            // 138 C. Against a 124 C mould that is the difference between the
            // model thinking the part is 54 K below Tg and its really being 14 K,
            // which sets how fast the skin freezes and therefore how much
            // orientation survives.
            var p = Polymers.ByName("MS_COP_ZEONEX480R").WithProcessTemps(275.0, 124.0);
            double curvature = Program.Value(args, "-curvature", 75.0);
            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;

            // FILL TIME DERIVED FROM THE MACHINE CONDITIONS, not assumed.
            //
            // The paper states no fill time, and this case carried an assumed
            // 1.0 s - an input nobody had sourced, feeding a clause that failed by
            // 6x. Table 1 does give what determines it: screw diameter 22 mm and
            // injection speed 22 mm/s, so the screw displaces pi/4*D^2*v =
            // 8363 mm^3/s, and the lens is 912 mm^3 (a 32 mm dia, 2 mm centre
            // plano-convex less its spherical cap). That is 0.109 s, NINE TIMES
            // shorter than the assumption, and Q = V/t_fill feeds dp/ds directly.
            //
            // Which way that moves the answer is NOT predicted here. The naive
            // reading is that a 9x higher Q means a 9x higher dp/ds and a worse
            // overshoot, but the fill time also sets how long an element is
            // loaded and how hard it is sheared, which changes the thinning and
            // the memory. Three directional predictions in this project have been
            // wrong for exactly that reason - see new-goal step 1b. Measured, not
            // argued.
            // FILL TIME IS NOW SOURCED, 2026-08-18, from Lai & Wang, Applied
            // Optics 47(12) 2017-2027 (2008) - the companion paper on THIS lens.
            // Its Fig. 5(c) plots shear stress at sensor nodes against "Filling
            // Time (sec)" over 0.28-0.50 s, with every curve rising to its
            // maximum at the right-hand edge: filling ENDS at about 0.50 s.
            //
            // The derivation below gave 0.109 s and is kept because it is still
            // the only way to get a fill time from the ORIGINAL paper - but it
            // assumes the whole screw output enters this one cavity, and the
            // measured 0.50 s says it does not, by about a factor of five. That is
            // the cavity-plus-runner share that has been unsourced since this case
            // was registered.
            //
            // MEASURED CONSEQUENCE: tau_wall falls 1.67 -> 1.05 MPa, which brings
            // it within ~20% of the 0.75-0.89 MPa peak shear stress that paper's
            // OWN simulation reports (Figs. 5a and 10, axes in units of 1e-1 MPa).
            // The in-plane peak barely moves, 4.763e-4 -> 4.434e-4, exactly as the
            // Cross exponent predicts.
            const double SourcedFillTimeS = 0.50;
            double screwDiaMm = 22.0, injSpeedMmPerS = 22.0;
            double screwRate = Math.PI / 4.0 * screwDiaMm * screwDiaMm * injSpeedMmPerS;
            double sagMm = curvature - Math.Sqrt(Math.Max(curvature * curvature - 16.0 * 16.0, 0.0));
            double lensVolMm3 = Math.PI * 16.0 * 16.0 * 2.0
                              - Math.PI * sagMm * sagMm * (3.0 * curvature - sagMm) / 3.0;
            double fillDerived = Math.Max(lensVolMm3 / Math.Max(screwRate, 1e-9), 1e-4);
            double fillUsed = Program.Value(args, "-filltime", SourcedFillTimeS);

            int ntSamples = (int)Program.Value(args, "-nt", 960.0);
            var proc = new Process
            {
                FillTimeS = fillUsed, PackPressureMPa = 98.10, PackTimeS = 3.0,
                TimeSamples = ntSamples,
            };


            var lens = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = p.Name,
                CentreThicknessMm = 2.0, SemiDiameterMm = 16.0,
                FrontRadiusMm = curvature, BackRadiusMm = 0.0,   // plano-convex
            };
            lens.EdgeThicknessMm = lens.ThicknessAt(lens.SemiDiameterMm);
            // EdgeRadial, NOT FilmEdge. This case was set up with FilmEdge, which
            // models "a straight front of CONSTANT width crossing the part" - right
            // for reference case 1, a 100 mm square plate gated across a whole
            // 100 mm edge, and wrong for a 32 mm DISC gated on its rim, where the
            // front fans out to the full diameter at mid-chord and closes again.
            //
            // It matters because dp/ds = 12*eta*Q/(W*h^3). Holding W at the gate's
            // own 12.6 mm across the whole part instead of letting it open to
            // 32 mm over-states dp/ds, and with it tau, everywhere past the gate.
            // The like-for-like comparison that exposed this: at the gate the two
            // cases carry W = 100.0 mm and W = 12.6 mm, and the model claims the
            // lens has 3.4x the wall shear stress of the plate while the
            // measurements say the lens has 3.2x LESS birefringence.
            lens.Gate = new GateSpec
            {
                Kind = GateKind.EdgeRadial, AzimuthDeg = 0,
                // Exposed 2026-08-18 so the ONE remaining unsourced input can be
                // swept. Default unchanged (one eighth of the circumference).
                WidthMm = Program.Value(args, "-gatewidth",
                                        2.0 * Math.PI * lens.SemiDiameterMm / 8.0),
                ThicknessMm = 0.8, IsDefault = false,
            };
            lens.PartingLineZMm = Gating.DefaultPartingLineZ(lens);

            Console.WriteLine(string.Format(ci,
                "  lens: dia {0:F0} mm, CT {1:F2} mm, ET {2:F3} mm, R {3:F0} mm (figure; " +
                "the text says 70 - see -curvature)",
                2 * lens.SemiDiameterMm, lens.CentreThicknessMm, lens.EdgeThicknessMm, curvature));
            Console.WriteLine(string.Format(ci,
                "  process: melt {0:F0} C, mould {1:F0} C, hold {2:F1} MPa, grid nz {3}",
                p.MeltTempC, p.MoldTempC, proc.PackPressureMPa, nz));
            Console.WriteLine(string.Format(ci,
                "  fill time {0:F4} s SOURCED from Lai & Wang, Appl. Opt. 47(12) 2017 " +
                "Fig. 5(c) (filling ends ~0.50 s). For contrast, a {1:F0} mm screw at {2:F0} mm/s " +
                "({3:F0} mm3/s) filling {4:F0} mm3 would give",
                proc.FillTimeS, screwDiaMm, injSpeedMmPerS, screwRate, lensVolMm3));
            Console.WriteLine(string.Format(ci,
                "    (that derivation gives {0:F4} s, {1:F1}x shorter - it assumes the WHOLE "
                + "screw output enters this one cavity, and the measured fill time says it "
                + "does not)", fillDerived, proc.FillTimeS / Math.Max(fillDerived, 1e-9)));
            Console.WriteLine();

            // EVERY model switch, not just the ones this case happened to need.
            //
            // Only -relax-below-tg was wired here, so -fountain, -complementary,
            // -deposition-support and -thinned-lambda were SILENTLY IGNORED by
            // this mode while the argument validator accepted them as globally
            // valid names. A comparison run with -fountain 3 printed numbers
            // identical to the baseline, which made the test vacuous rather than
            // informative. That is the does-nothing-reports-success pattern one
            // level below where the unknown-argument guard was added: the guard
            // asks whether a flag EXISTS, not whether the mode reads it.
            if (Program.Has(args, "-relax-below-tg")) proc.RelaxBelowTg = true;
            // -lagrangian-depth is now the default and is kept as an explicit
            // opt-IN so scripts written while it was optional still say what
            // they mean. -eulerian-depth is the opt-out.
            if (Program.Has(args, "-lagrangian-depth")) proc.LagrangianDepthHistory = true;
            if (Program.Has(args, "-eulerian-depth")) proc.LagrangianDepthHistory = false;
            if (Program.Has(args, "-incremental-thermal")) proc.IncrementalThermal = true;
            if (Program.Has(args, "-snapshot")) proc.IncrementalThermal = false;
            // Chang et al. name the packing flow through the NARROWING channel as
            // the cause of a second birefringence peak near the centre of the gap
            // - the region where this model gives almost nothing. Swept, not
            // assumed.
            if (Program.Has(args, "-narrowing")) proc.ChannelNarrowing = true;
            if (Program.Has(args, "-packing-orientation")) proc.PackingOrientation = true;
            if (Program.Has(args, "-normal-stress")) proc.NormalStressDifference = true;
            // Packing time and pressure gate the packing channel entirely, so
            // they must be sweepable: a mechanism that only acts while material
            // is still molten is decided by how long the packing window overlaps
            // the freezing.
            if (Program.Has(args, "-packtime"))
                proc.PackTimeS = Program.Value(args, "-packtime", proc.PackTimeS);
            if (Program.Has(args, "-packpressure"))
                proc.PackPressureMPa = Program.Value(args, "-packpressure", proc.PackPressureMPa);
            if (Program.Has(args, "-packfrac"))
                proc.PackFlowFraction = Program.Value(args, "-packfrac", 0.05);
            // The relaxation time is the physically open constant: lambda = eta0/G
            // is the MAXWELL time, while the terminal time for chain ORIENTATION -
            // which is what freezes in - is longer by a factor of order 3-6 for an
            // entangled melt, since eta0 ~ G*tau_d/5. Swept, not assumed.
            if (Program.Has(args, "-lambdascale"))
                proc.LambdaScale = Program.Value(args, "-lambdascale", 1.0);
            if (Program.Has(args, "-shape-nodes"))
                proc.DepthShapeGapNodes = (int)Program.Value(args, "-shape-nodes", 6);
            if (Program.Has(args, "-shape-particles"))
                proc.DepthShapeParticles = (int)Program.Value(args, "-shape-particles", 4000);
            if (Program.Has(args, "-shape-steps"))
                proc.DepthShapeSteps = (int)Program.Value(args, "-shape-steps", 3000);
            if (Program.Has(args, "-fountain"))
                proc.FountainStrain = Program.Value(args, "-fountain", 1.0);
            if (Program.Has(args, "-thinned-lambda")) proc.ShearThinnedLambdaDuringFill = true;
            if (Program.Has(args, "-deposition-support")) proc.FountainDepositionSupport = true;
            if (Program.Has(args, "-deposition-decay")) proc.FountainDecaysAlongFlow = true;
            if (Program.Has(args, "-complementary"))
            { proc.ComplementaryShearGate = true; proc.FountainDepositionSupport = true; }
            var fill = FillField.Build(lens, p, proc, 101);
            // FILL-FIELD SUMMARY - see RefCase.cs; the two cases must be
            // comparable on these numbers or the 8x cannot be located.
            say2(string.Format(ci,
                "  fill field: eta {0:E2} Pa.s, Q {1:E2} mm3/s, W(gate) {2:F1} mm, " +
                "h(gate) {3:F3} mm, dp/ds(gate) {4:E2} MPa/mm, tau_wall {5:E2} MPa",
                fill.EtaPaS, fill.FlowRateMm3PerS, fill.Width[0], fill.H[0],
                fill.DpDs[0], fill.DpDs[0] * 0.5 * fill.H[0]));

            var freeze = FreezeHistory.Build(lens.CentreThicknessMm, p, proc, nz, 10 * nz);
            var ch = Channels.Build(lens, p, proc, fill, freeze);

            // The shape is now per-station, so print it at THREE stations rather
            // than one. The layer-removal clause samples s = 0, which is the
            // minimum gap and therefore the first interpolation node exactly -
            // so that clause is insensitive to the node count by construction
            // and cannot be used to test the interpolation. These rows can.
            Console.WriteLine(string.Format(ci,
                "  LAGRANGIAN raw thickness-average dn (discarded by the mean-1 port): {0:E3}",
                Lagrangian.RawThicknessAverageDn));

            // THE CEILING - what this ARCHITECTURE can produce at all, before any
            // parameter is chosen.
            //
            // The flow channel is dn(z) = 2*C*tau(z)*memory(z), and memory is a
            // retained FRACTION: it cannot exceed 1. So the thickness average is
            // bounded above by 2*C*<tau(z)>, whatever the relaxation time, the
            // fountain strain, the channel narrowing, the particle count or the
            // depth construction happen to be.
            //
            // Printed because six candidate fixes were each measured and each
            // bought about 15% where 500% was needed, and the ceiling explains all
            // six at once: they were tuning inside a box smaller than the target.
            // A clause whose ceiling is below its gate is not a test of the
            // parameters - it is a statement about the model's structure.
            {
                double tauSum = 0.0;
                for (int k = 0; k < nz; k++) tauSum += Math.Abs(ch.TauViscMPa[0, k]);
                double tauAvg = tauSum / nz;
                // The ceiling must use the SAME conversion the channel uses. With
                // the normal-stress term on, the enhancement at memory==1 is much
                // larger than at the retained fraction, because it grows with the
                // stress itself - so the ceiling is not simply 2*C*<tau>.
                double ceiling = 0.0;
                for (int k = 0; k < nz; k++)
                {
                    double t = Math.Abs(ch.TauViscMPa[0, k]);
                    double f = 1.0;
                    if (proc.NormalStressDifference && p.MeltModulusPa > 0.0)
                    {
                        double wi = t * 1e6 / p.MeltModulusPa;
                        f = Math.Sqrt(1.0 + wi * wi);
                    }
                    ceiling += 2.0 * p.CMeltBrewster * 1e-6 * t * f;
                }
                ceiling /= nz;
                Console.WriteLine(string.Format(ci,
                    "  CEILING of this architecture: 2*C*<tau> = {0:E3} with memory==1 "
                    + "(<tau> {1:F3} MPa, C {2:F0} Br)", ceiling, tauAvg, p.CMeltBrewster));
                Console.WriteLine(string.Format(ci,
                    "    against the published {0:E3}: ceiling/gate = {1:F2}  =>  {2}",
                    PublishedInPlanePeakDn, ceiling / PublishedInPlanePeakDn,
                    ceiling >= PublishedInPlanePeakDn
                        ? "reachable - the gap is parameters"
                        : "NOT REACHABLE BY ANY PARAMETER CHOICE - the gap is structural"));
                if (ceiling < PublishedInPlanePeakDn)
                    Console.WriteLine(string.Format(ci,
                        "    clearing it needs C ~ {0:F0} Br (Inoue family 1700), or <tau> ~ "
                        + "{1:F2} MPa (their own simulation peaks at 0.75-0.89), or a mechanism "
                        + "that orients material where tau(z) is SMALL - which is the packing "
                        + "flow through the narrowing channel the source names as a second peak "
                        + "at the centre of the gap.",
                        PublishedInPlanePeakDn / (2.0 * tauAvg) * 1e6,
                        PublishedInPlanePeakDn / (2.0 * p.CMeltBrewster * 1e-6)));
            }
            if (ch.DepthShapePerStation != null)
            {
                Console.WriteLine(
                    "  depth shape: {0}, {1} gap node(s) over h/h0 {2:F3}-{3:F3}, min band count {4}",
                    ch.DepthShapeSource, ch.DepthShapeNodes,
                    ch.DepthShapeGapMin, ch.DepthShapeGapMax, ch.DepthShapeMinCount);
                int nsT = ch.S.Length, nzT = freeze.NodeCount;
                foreach (int ii in new[] { 0, nsT / 5, (2 * nsT) / 5, nsT / 2 })
                {
                    Console.Write("    s = {0,5:F1} mm  h/h0 {1:F3}  phi:",
                        ch.S[ii], fill.H[Math.Min(ii, fill.H.Length - 1)] / freeze.ThicknessMm);
                    for (int ff = 10; ff >= 0; ff -= 2)
                    {
                        int kk = (int)Math.Round((nzT - 1) * (0.5 + 0.05 * ff));
                        kk = Math.Max(0, Math.Min(nzT - 1, kk));
                        Console.Write("  {0}%={1:F3}", ff * 10, ch.DepthShapePerStation[ii, kk]);
                    }
                    Console.WriteLine();
                }
            }
            double half = 0.5 * lens.CentreThicknessMm;

            // DEPTH DIAGNOSTIC at the gate station, so this case can be compared
            // against case 1's on the SAME quantities. The relaxation half of the
            // memory integral has been eliminated (the clock stops ~90 K above
            // Tg), so what is left to examine is the BUILD-UP: how much reduced
            // time each layer accumulates while it is still being loaded, and how
            // much of the local shear stress it therefore reaches.
            {
                int nzd = freeze.NodeCount;
                double halfd = 0.5 * lens.CentreThicknessMm;
                double lam0 = FillField.CrossWlf(p, 0.0, p.MeltTempC, 0.0) / p.MeltModulusPa;
                say2(string.Format(ci,
                    "  lambda(melt {0:F0} C) = {1:E2} s against a fill of {2:F4} s  =>  " +
                    "xi available over the fill = {3:F2}",
                    p.MeltTempC, lam0, proc.FillTimeS, proc.FillTimeS / lam0));
                say2("  depth   t_freeze     xi_frz    memory    tau_visc     dn_flow");
                for (int k = nzd - 1; k >= nzd / 2; k -= Math.Max(1, nzd / 10))
                {
                    double f = Math.Abs(freeze.Z[k]) / halfd;
                    double tAbs = freeze.FreezeTimeS[k];
                    // READ what the model used - never recompute it. Three
                    // diagnostics have already drifted from Channels.Build by
                    // recomputing this with different arguments.
                    double mem = ch.MemoryUsed[0, k];
                    double tau = ch.TauViscMPa[0, k];
                    double xi = 0.0;
                    if (freeze.TimeGridS != null && freeze.TempHistoryC != null)
                    {
                        var hist = new double[freeze.TimeGridS.Length];
                        for (int q = 0; q < hist.Length; q++) hist[q] = freeze.TempHistoryC[k, q];
                        xi = Channels.ReducedTimeToFreeze(freeze.TimeGridS, hist, p, tAbs);
                    }
                    say2(string.Format(ci,
                        "  {0,5:P0}  {1,9:F4}  {2,9:F3}  {3,8:F4}  {4,10:E2}  {5,10:E2}",
                        f, tAbs, xi, mem, tau, Math.Abs(ch.DnFlow[0, k])));
                }
                say2("");
            }

            // ---- (a) in-plane -------------------------------------------------
            int ns = ch.S.Length, nzc = freeze.NodeCount;
            var avg = new double[ns];
            for (int i = 0; i < ns; i++)
            {
                double sum = 0;
                for (int k = 0; k < nzc; k++) sum += Math.Abs(ch.DnFlow[i, k]);
                avg[i] = sum / nzc;
            }
            int argMax = 0;
            for (int i = 1; i < ns; i++) if (avg[i] > avg[argMax]) argMax = i;
            double peak = avg[argMax];
            double peakRatio = peak / PublishedInPlanePeakDn;
            bool peakOk = peakRatio >= 1.0 / FactorBar && peakRatio <= FactorBar;
            bool shapeOk = ch.S[argMax] <= 0.25 * ch.S[ns - 1] && avg[ns - 1] < peak;

            Console.WriteLine("  measured along the filling path (paper Fig. 7, error < 10%)");
            Console.WriteLine("    distance   measured     model");
            double[] mmPts = { 0, 3, 6, 12, 24 };
            // Same x100 correction as the peak - these are the same curve.
            double[] measPts = { 3.7e-3, 2.5e-3, 1.0e-3, 0.2e-3, 0.4e-3 };
            for (int j = 0; j < mmPts.Length; j++)
            {
                int idx = (int)Math.Round(mmPts[j] / Math.Max(ch.S[ns - 1], 1e-9) * (ns - 1));
                idx = Math.Max(0, Math.Min(ns - 1, idx));
                Console.WriteLine(string.Format(ci, "    {0,5:F0} mm   {1:E2}    {2:E2}",
                    mmPts[j], measPts[j], avg[idx]));
            }
            Console.WriteLine(string.Format(ci,
                "  (a) in-plane peak {0:E3} against published {1:E3} - ratio {2:F2}x  =>  {3}",
                peak, PublishedInPlanePeakDn, peakRatio, peakOk ? "PASS" : "FAIL"));
            Console.WriteLine(string.Format(ci,
                "      maximum at {0:F1} mm from the gate, far edge {1:P0} of peak  =>  {2}",
                ch.S[argMax], peak > 0 ? avg[ns - 1] / peak : 0.0, shapeOk ? "PASS" : "FAIL"));
            Console.WriteLine();

            // ---- (b) layer removal -------------------------------------------
            // Cumulative |dn| integrated from the surface inward, at the gate
            // station, where the paper's layers were machined. Removal is ONE
            // SIDED - the flat face of a plano-convex lens - so the denominator is
            // the whole thickness while the numerator is one outer layer. That
            // assumption is stated because it halves the answer if wrong, and the
            // two-sided figure is printed beside it.
            // ONE-SIDED, OVER 0.8 mm - settled from the figure, 2026-08-18.
            //
            // Fig. 10's caption reads "layer thickness removed FROM THE SURFACE
            // near the gate area" - singular - and the text says 50% came off
            // "after 0.4 mm, NAMELY HALF OF GATE THICKNESS". The gate land is
            // 0.8 mm, so removal runs from ONE face into a 0.8 mm body. Fig. 10's
            // right-hand axis closes it: removed birefringence runs 0 to
            // 1.84e-5, so 46.2% is ~1.7e-5 against a total near 3.7e-5, which is
            // exactly the measured gate reading. The layer removal decomposes the
            // gate value, over 0.8 mm.
            //
            // This was previously integrated over the 2.0 mm centre thickness and
            // reported one- and two-sided side by side, which was an assumption
            // dressed as a choice. It also exposes a GEOMETRY error here: a
            // 0.273 mm rim cannot survive a 0.4 mm cut, so the real lens must
            // carry a flange of about the gate thickness at its rim and the
            // sagitta-to-the-full-semi-diameter model of it is wrong. Until the
            // flange is modelled, the comparison is made at the station whose
            // LOCAL gap is closest to 0.8 mm, and that station is reported.
            int iCmp = 0; double bestGap = double.MaxValue;
            for (int i2 = 0; i2 < ns; i2++)
            {
                double d = Math.Abs(fill.H[Math.Min(i2, fill.H.Length - 1)] - 0.8);
                if (d < bestGap) { bestGap = d; iCmp = i2; }
            }
            double hCmp = fill.H[Math.Min(iCmp, fill.H.Length - 1)];
            double halfCmp = 0.5 * hCmp;
            Console.WriteLine(string.Format(ci,
                "  layer removal - ONE-SIDED, at the station where the gap is {0:F3} mm " +
                "(s = {1:F1} mm), against the paper's 0.8 mm gate region",
                hCmp, ch.S[iCmp]));
            double total = 0.0;
            for (int k = 0; k < nzc; k++) total += Math.Abs(ch.DnTotalOutOfPlane[iCmp, k]);
            Console.WriteLine("    t (mm)   measured   model");
            int nOk = 0;
            for (int j = 0; j < RemovalDepthMm.Length; j++)
            {
                double t = RemovalDepthMm[j], outer = 0.0;
                double zCut = halfCmp - t * (halfCmp / 0.4);   // t scaled to this gap
                for (int k = 0; k < nzc; k++)
                {
                    double zLoc = freeze.Z[k] * (hCmp / freeze.ThicknessMm);
                    if (Math.Abs(zLoc) >= zCut) outer += Math.Abs(ch.DnTotalOutOfPlane[iCmp, k]);
                }
                double oneSided = total > 0 ? 0.5 * outer / total : 0.0;
                bool ok = Math.Abs(oneSided - RemovedFraction[j]) <= 0.10;
                if (ok) nOk++;
                Console.WriteLine(string.Format(ci,
                    "    {0,5:F1}    {1,7:P1}    {2,7:P1}   {3}",
                    t, RemovedFraction[j], oneSided, ok ? "ok" : "off"));
            }
            bool removalOk = nOk >= 3;
            Console.WriteLine(string.Format(ci,
                "  (b) layer-removal profile: {0} of {1} points within 10 points  =>  {2}",
                nOk, RemovalDepthMm.Length, removalOk ? "PASS" : "FAIL"));

            bool all = peakOk && shapeOk && removalOk;
            Console.WriteLine();
            Console.WriteLine("  VERDICT: " + (all
                ? "second reference case MET"
                : "second reference case NOT met - in-plane peak " + (peakOk ? "PASS" : "FAIL") +
                  ", in-plane shape " + (shapeOk ? "PASS" : "FAIL") +
                  ", layer removal " + (removalOk ? "PASS" : "FAIL")));
            return all ? 0 : 2;
        }
    }
}

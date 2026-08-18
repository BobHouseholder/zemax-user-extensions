using System;

namespace MoldStress
{
    /// <summary>
    /// A3 - the three channels, evaluated on the (s, z) grid A1 and A2 produced.
    ///
    /// They are separate because they are physically separate, and because they
    /// leave the tool by different doors:
    ///
    ///   FLOW ORIENTATION  anisotropic, melt coefficient, becomes an EQUIVALENT
    ///                     stress in A4 - frozen orientation is not a stress in
    ///                     the finished part, so it cannot be handed over as one
    ///                     without conversion.
    ///   THERMAL STRESS    anisotropic, glassy coefficient, and a REAL stress, so
    ///                     it is handed to STAR unchanged. In a flat wall it is
    ///                     equibiaxial, which means it produces exactly zero
    ///                     retardance for an axial ray and a real effect for an
    ///                     oblique one. Handing STAR the tensor rather than a
    ///                     birefringence number is what gets that right for free.
    ///   DENSITY           isotropic, changes n without splitting it, and goes to
    ///                     STAR's DirectIndex channel.
    /// </summary>
    internal sealed class Channels
    {
        public double[] S;                 // path coordinate from the gate, mm
        public double[] Z;                 // height above the mid-plane, mm
        public double[,] DnFlow;           // n_parallel - n_perpendicular, flow frame

        /// <summary>
        /// Flow PLUS thermal, signed, for the OUT-OF-PLANE component (n_x - n_z).
        /// Added 2026-08-17.
        ///
        /// Why this is a separate array rather than a correction to DnFlow: the
        /// two channels do not appear in the same measurement. The thermal
        /// residual stress is EQUIBIAXIAL IN PLANE - sigma_xx = sigma_yy,
        /// sigma_zz = 0 - so for light down z the in-plane difference
        /// (sigma_xx - sigma_yy) is identically ZERO and thermal contributes
        /// NOTHING to in-plane birefringence. For a ray in the plane of the plate
        /// the difference is (sigma_xx - sigma_zz) = sigma_xx and thermal
        /// contributes in full.
        ///
        /// That is not a detail, it decides which criterion may use it. The
        /// registered in-plane clause compares against a thickness-averaged
        /// in-plane peak and must stay on DnFlow. The depth clause compares
        /// against a profile the source measured in the xz and yz planes, on
        /// slabs cut from the plate and viewed edge-on - out-of-plane, so it must
        /// use this. Adding thermal to the in-plane number would be adding a term
        /// that is zero in the geometry it is measured in.
        ///
        /// Converted with the GLASSY coefficient: thermal stress is locked into
        /// solid material below Tg, so C_melt does not apply to it.
        /// </summary>
        public double[,] DnTotalOutOfPlane;
        /// <summary>
        /// The memory factor and shear stress the model ACTUALLY USED, stored per
        /// (station, depth) rather than left to be recomputed.
        ///
        /// Three separate diagnostics have now been caught recomputing these with
        /// a different call than Channels.Build makes - DepthDiag's memory column
        /// evaluating the isothermal closed form, DepthDiag running different
        /// process conditions, and RefCase2's depth table omitting the local-gap
        /// time scaling. Each produced a plausible column that did not reconcile
        /// with the dn beside it, and one of them shaped hours of wrong diagnosis.
        ///
        /// A recomputation can drift. A stored value cannot. Diagnostics read
        /// these; nothing recomputes them.
        /// </summary>
        public double[,] MemoryUsed;
        public double[,] TauViscMPa;

        /// <summary>
        /// The depth shape applied on top of the per-station magnitude, and where
        /// it came from. Null when the Eulerian per-depth history is used, which
        /// is the default. Mean over the wall is 1 by construction.
        /// </summary>
        public double[] DepthShapeApplied;      // at the reporting station, i = 0
        public double[,] DepthShapePerStation;  // [station, depth]
        public int DepthShapeMinCount;
        public int DepthShapeNodes = 0;         // gap ratios the shape was solved at
        public double DepthShapeGapMin = 1.0, DepthShapeGapMax = 1.0;
        public string DepthShapeSource = "eulerian";

        public double[,] SigmaThermalMPa;  // in-plane equibiaxial residual stress
        public double[,] DnDensity;        // isotropic index change
        public Polymer Material;
        public double PeakDnFlow, PeakDepthFraction;

        /// <summary>
        /// Clamp instrumentation, 2026-08-17. A clamped quantity is insensitive
        /// to its inputs, and the depth channel shows every symptom of that: a
        /// freeze-order null returning the same number to two decimals, a 30 C
        /// mould change moving the ratio by 1%, and a depth ratio sitting at ~1.0
        /// as though the memory were the same constant at both sampling depths.
        /// Whether the clamp in MemoryFactorWlf actually BINDS is a measurement,
        /// not an inference, so it is counted. Reset before a run and read after.
        /// </summary>
        public static long ClampCalls, ClampHits;
        public static double MaxRaw, LastRaw;

        public static void ResetClampStats() { ClampCalls = ClampHits = 0; MaxRaw = LastRaw = 0.0; }

        public static Channels Build(MouldedElement e, Polymer p, Process proc,
                                     FillField fill, FreezeHistory freeze)
        {
            int ns = fill.S.Length, nz = freeze.NodeCount;
            var c = new Channels
            {
                S = fill.S, Z = freeze.Z, Material = p,
                DnFlow = new double[ns, nz],
                DnTotalOutOfPlane = new double[ns, nz],
                MemoryUsed = new double[ns, nz],
                TauViscMPa = new double[ns, nz],
                SigmaThermalMPa = new double[ns, nz],
                DnDensity = new double[ns, nz],
            };

            // --- thermal: one profile, from the freeze-off temperature ---------
            // Each layer solidifies stress free and then contracts. Layers that
            // are hotter at freeze-off contract more afterwards and end in
            // tension; the cold skin ends in compression. Force and moment
            // balance are imposed rather than assumed, which is what makes the
            // uniform and linear controls meaningful.
            double eOver1MinusNu = p.ModulusMPa / (1.0 - p.PoissonRatio);
            var sigma = ThermalProfile(freeze.TrefC, freeze.Z, eOver1MinusNu * p.CtePerK);

            // --- density: Lorentz-Lorenz on the packing pressure ---------------
            double llFactor = (p.Nd * p.Nd - 1.0) * (p.Nd * p.Nd + 2.0) / (6.0 * p.Nd);
            const double compressibilityPerMPa = 2.5e-4;      // ~4 GPa bulk modulus
            double pMean = 0.0;
            for (int i = 0; i < ns; i++) pMean += fill.P[i];
            pMean /= ns;

            double tFill = Math.Max(proc.FillTimeS, 1e-6);
            double tPack = Math.Max(proc.PackTimeS, 1e-6);

            // GATE SEAL. Shear does not decay away for ever after filling - it
            // STOPS, when the gate freezes. The gate land is thinner than the
            // wall by design, so it seals first, and after that no melt enters and
            // nothing in the cavity is being sheared however hot the core still is.
            //
            // Solved with the same freeze model applied to the gate's own
            // thickness, so it is geometry the design already carries rather than
            // a fitted time.
            double tGateSeal = double.PositiveInfinity;
            if (e.Gate != null && e.Gate.ThicknessMm > 1e-4)
            {
                try
                {
                    var gateFreeze = FreezeHistory.Build(e.Gate.ThicknessMm, p, proc, 21, 201);
                    tGateSeal = gateFreeze.CentreFreezeTimeS;
                }
                catch { }
            }

            // Molten fraction of the gap at each grid time: the freeze history
            // already says which depth solidifies when, so inverting it gives the
            // position of the solid/melt interface and hence h_melt(t)/h.
            double[] meltFracAtTime = null;
            if (freeze.TimeGridS != null)
            {
                meltFracAtTime = new double[freeze.TimeGridS.Length];
                double halfW = 0.5 * freeze.ThicknessMm;
                for (int j = 0; j < freeze.TimeGridS.Length; j++)
                {
                    double t = freeze.TimeGridS[j];
                    double interfaceZ = 0.0;      // deepest still-molten position
                    for (int k = 0; k < nz; k++)
                        if (freeze.FreezeTimeS[k] > t)
                            interfaceZ = Math.Max(interfaceZ, Math.Abs(freeze.Z[k]));
                    meltFracAtTime[j] = halfW > 0 ? interfaceZ / halfW : 1.0;
                }
            }

            for (int i = 0; i < ns; i++)
            {
                // SOURCE-WINDOW FACTOR for the front deposition, added 2026-08-17.
                //
                // The front can only deposit orientation that the melt feeding it
                // actually carries, and the melt feeding the front at station i is
                // the CORE STREAM. This model already makes exactly that argument
                // for the shear channel, and it is what produces the gate-to-edge
                // decay the reference case requires: "at the far edge, where the
                // melt arrives as filling ends, t_a and t_end coincide and the
                // bracket is identically ZERO: no shear window, no orientation."
                //
                // The fountain term ignored it and so did not decay along the
                // flow at all. With Blake's envelope on - which makes the
                // deposited LAYER thicken with distance - the result was a
                // predicted profile that RISES toward the far edge, 129.3% of the
                // gate value, against a reference that falls roughly linearly to
                // zero. The support was right and the magnitude was missing its
                // along-flow dependence.
                //
                // So the deposition is scaled by the memory factor evaluated at
                // the MID-PLANE - the source the front draws from - at this
                // station. Dimensionless, in [0,1], identically zero at the far
                // edge for the same reason and by the same expression as the
                // shear channel. This is an extension of an argument already in
                // the model rather than a new constant.
                // LOCAL GAP SCALING. FreezeHistory is solved ONCE, on the
                // element's CENTRE thickness, and its depth grid was then paired
                // at every station with that station's own dp/ds. On a plate those
                // agree. On a curved element they do not: a plano-convex lens with
                // a 2.0 mm centre has a 0.273 mm gated rim, so tau = dp/ds * |z|
                // was evaluated out to 7.3x the local half-gap, on a dp/ds already
                // inflated by the Hele-Shaw 1/h^3 there. Measured 2026-08-18 on
                // reference case 2: in-plane peak 370x the published value.
                //
                // The depth grid is therefore read as a NORMALISED depth and
                // mapped onto the local gap, and the freeze clock is scaled with
                // it: 1D conduction gives t_freeze proportional to h^2, so the
                // thin rim solidifies in ~2% of the centre's time rather than the
                // same time. Both halves are needed - scaling z alone would leave
                // the rim molten for as long as the centre.
                double hLoc = fill.H[Math.Min(i, fill.H.Length - 1)];
                double hRatio = freeze.ThicknessMm > 1e-9
                    ? Math.Max(hLoc / freeze.ThicknessMm, 1e-6) : 1.0;
                double tScale = hRatio * hRatio;
                double[] gridLocal = freeze.TimeGridS;
                if (freeze.TimeGridS != null && Math.Abs(tScale - 1.0) > 1e-12)
                {
                    gridLocal = new double[freeze.TimeGridS.Length];
                    for (int q = 0; q < gridLocal.Length; q++)
                        gridLocal[q] = freeze.TimeGridS[q] * tScale;
                }

                double mSource = 1.0;
                if (proc.FountainDecaysAlongFlow)
                {
                    int kMid = nz / 2;
                    double tArriveS = tFill * fill.S[i] / Math.Max(fill.PathLengthMm, 1e-9);
                    double tFreezeSrc = tArriveS + freeze.FreezeTimeS[kMid];
                    double lamS = Math.Max(proc.LambdaScale * (p.MeltModulusPa > 0
                        ? fill.EtaPaS / p.MeltModulusPa : 1e-6), 1e-9);
                    if (freeze.TimeGridS != null && freeze.TempHistoryC != null)
                    {
                        var hS = new double[freeze.TimeGridS.Length];
                        for (int q = 0; q < hS.Length; q++) hS[q] = freeze.TempHistoryC[kMid, q];
                        mSource = MemoryFactorWlf(tArriveS, tFill, tFreezeSrc,
                                                  gridLocal, hS, p, lamS, tPack,
                                                  meltFracAtTime, proc.ChannelNarrowing,
                                                  tGateSeal);
                    }
                    else
                    {
                        mSource = MemoryFactor(tArriveS, tFill, tFreezeSrc, lamS, tPack);
                    }
                }

                for (int k = 0; k < nz; k++)
                {
                    // Shear the layer locked in: the gradient present when it
                    // froze, times its distance from the mid-plane.
                    //
                    // The gradient RISES through filling and decays through
                    // packing. Both halves matter and the rising half is the one
                    // that is easy to leave out: the skin freezes against the
                    // wall almost immediately, while the cavity is barely
                    // pressurised, so it locks in very little despite sitting
                    // where the shear stress would be highest. Without the ramp
                    // this model put the peak exactly at the surface - caught by
                    // this stage's own control.
                    // VISCOELASTIC MEMORY.
                    //
                    // Frozen orientation is not the shear stress at the instant of
                    // freezing - it is what the material still REMEMBERS of its
                    // whole shear history at that instant. For a single Maxwell
                    // mode with relaxation time lambda = eta/G:
                    //
                    //   sigma_e(t_f) = G * INT exp(-(t_f - t')/lambda) gamma_dot dt'
                    //
                    // which for a constant shear rate running from arrival t_a to
                    // the end of flow t_end integrates in closed form to the
                    // viscous stress times a memory factor:
                    //
                    //   sigma_e = tau_visc * [ exp(-(t_f - t_end)/lambda)
                    //                        - exp(-(t_f - t_a  )/lambda) ]
                    //
                    // The two limits are the reason this replaces the ad-hoc ramp
                    // it used to carry. Fast relaxation with a long shear window
                    // returns tau_visc exactly - the instantaneous model is the
                    // short-memory limit of this one, not a rival to it. And at
                    // the far edge, where the melt arrives as filling ends, t_a
                    // and t_end coincide and the bracket is identically ZERO: no
                    // shear window, no orientation, whatever the local stress is.
                    //
                    // That is the gate-to-edge decay the registered reference case
                    // requires and that an instantaneous rule cannot express.
                    double tArrive = tFill * fill.S[i] / Math.Max(fill.PathLengthMm, 1e-9);
                    double zLocal = freeze.Z[k] * hRatio;          // depth in THIS gap
                    double tFreezeAbs = tArrive + freeze.FreezeTimeS[k] * tScale;
                    double tauViscMPa = fill.DpDs[i] * Math.Abs(zLocal);
                    double lambda = Math.Max(proc.LambdaScale * (p.MeltModulusPa > 0
                        ? fill.EtaPaS / p.MeltModulusPa : 1e-6), 1e-9);
                    double memory;
                    if (freeze.TimeGridS != null && freeze.TempHistoryC != null)
                    {
                        var hist = new double[freeze.TimeGridS.Length];
                        for (int q = 0; q < hist.Length; q++) hist[q] = freeze.TempHistoryC[k, q];
                        memory = MemoryFactorWlf(tArrive, tFill, tFreezeAbs,
                                                 gridLocal, hist, p, lambda, tPack,
                                                 meltFracAtTime, proc.ChannelNarrowing,
                                                 tGateSeal, proc.ShearThinnedLambdaDuringFill,
                                                 fill.EtaPaS > 0
                                                     ? tauViscMPa * 1e6 / fill.EtaPaS : 0.0,
                                                 proc.RelaxBelowTg);
                    }
                    else
                    {
                        memory = MemoryFactor(tArrive, tFill, tFreezeAbs, lambda, tPack);
                    }
                    double tauMPa = tauViscMPa * memory;
                    c.MemoryUsed[i, k] = memory;
                    c.TauViscMPa[i, k] = tauViscMPa;

                    // Stress-optical rule in simple shear: the principal stress
                    // difference is 2*tau.
                    double dnShear = 2.0 * p.CMeltBrewster * 1e-12 * (tauMPa * 1e6);

                    // FOUNTAIN FLOW.
                    //
                    // Everything that ends up against the wall got there through
                    // the melt front, where it turned through roughly a right
                    // angle and was stretched on the way. That strain is imposed
                    // ONCE, at deposition, and then relaxes - so what survives is
                    // decided entirely by how much reduced time passes before the
                    // layer freezes.
                    //
                    // The skin freezes almost immediately and keeps nearly all of
                    // it; the core stays hot for seconds and loses nearly all of
                    // it. That is a monotone decay from the surface inward, which
                    // is the shape the published case reports and the shape both
                    // previous treatments missed - one flat, one inverted.
                    //
                    // Note what this term does NOT need: no shear window, no
                    // pressure gradient, no gate distance. It is deposition and
                    // relaxation, nothing else.
                    double dnFountain = 0.0;
                    if (proc.FountainStrain > 0 && freeze.TimeGridS != null)
                    {
                        var histF = new double[freeze.TimeGridS.Length];
                        for (int q = 0; q < histF.Length; q++) histF[q] = freeze.TempHistoryC[k, q];
                        double xiFreeze = ReducedTimeToFreeze(gridLocal, histF, p,
                                                              freeze.FreezeTimeS[k] * tScale);

                        // MAGNITUDE FROM KINEMATICS, not from an assumed strain.
                        //
                        // The first version deposited G * 1.0, a unit strain I
                        // chose. It is not a free parameter: material turning
                        // through the front is extended at a rate set by the front
                        // speed and the gap, and it is only in the front region
                        // for about one gap-crossing time. For a Maxwell fluid
                        // extended at rate edot for a time t_res:
                        //
                        //   sigma = G * lambda * edot * (1 - exp(-t_res/lambda))
                        //
                        // with edot = v_front/(h/2) and t_res = (h/2)/v_front, so
                        // t_res = 1/edot and nothing is left to choose. The
                        // Weissenberg number lambda*edot decides how much of the
                        // available stress the material can actually build.
                        double vFront = fill.PathLengthMm / Math.Max(tFill, 1e-9);
                        double halfGap = Math.Max(0.5 * fill.H[i], 1e-6);
                        double eDot = vFront / halfGap;
                        double wi = lambda * eDot;
                        double eEff = wi * (1.0 - Math.Exp(-1.0 / Math.Max(wi, 1e-12)));

                        double sigmaFrontPa;
                        if (proc.FrontCarriesMeltOrientation)
                        {
                            // THE FRONT DEPOSITS THE ORIENTATION THE MELT ALREADY
                            // CARRIED, 2026-08-17.
                            //
                            // The derivation above is sound and still wrong for
                            // this purpose, because of what it leaves out. It
                            // treats deposition as a FRESH extensional strain
                            // imposed on unoriented material, and a Maxwell fluid
                            // extended at edot for 1/edot cannot build more than
                            // its own plateau modulus: eEff -> 1 as Wi -> inf, so
                            // sigma <= G = 2.8e5 Pa however hard the front is
                            // driven. That ceiling is the saturation measured on
                            // 2026-08-17 - tripling FountainStrain bought 27%.
                            //
                            // But the material arriving at the front is NOT
                            // unoriented. It has just come down the channel at
                            // melt temperature, where the fully developed wall
                            // shear stress is dp/ds * h/2, and for this case that
                            // is ~5e5 Pa - ABOVE the plateau modulus. The front
                            // does not create that orientation, it CARRIES it to
                            // the wall and freezes it there.
                            //
                            // This is the asymmetry the model was missing. The
                            // shear channel assumes every depth's material has sat
                            // at that depth since t=0 and so shares that depth's
                            // thermal history; near the wall that history is cold
                            // from the first instant, the layer never deforms, and
                            // dnShear collapses to ~0 (measured: ratio 0.02 with
                            // the front term off). In fountain flow the skin
                            // material only ARRIVES at the wall when the front
                            // passes. Until that moment it was hot and shearing.
                            //
                            // So the deposited principal stress difference is the
                            // melt's own, 2 * tau_wall, taken at the near-wall
                            // source the front sweeps up, and the only attenuation
                            // is relaxation AFTER deposition - which is what makes
                            // the profile skin-peaked: the skin freezes at once and
                            // keeps nearly all of it, the core stays hot and loses
                            // it. Same exp(-xi) as before, applied to a magnitude
                            // that is no longer capped at G.
                            double tauWallMPa = fill.DpDs[i] * halfGap;
                            sigmaFrontPa = 2.0 * tauWallMPa * 1e6 * proc.FountainStrain
                                           * Math.Exp(-xiFreeze);
                        }
                        else
                        {
                            // Superseded 2026-08-17, kept runnable for comparison
                            // via -frontmode extensional. EXTENSION, so the
                            // principal stress difference IS sigma - the factor of
                            // 2 that belongs to simple shear was applied here as
                            // well and should never have been.
                            sigmaFrontPa = p.MeltModulusPa * eEff * proc.FountainStrain
                                           * Math.Exp(-xiFreeze);
                        }

                        dnFountain = p.CMeltBrewster * 1e-12 * sigmaFrontPa * mSource;

                        // BLAKE'S MAXIMUM-RESIDENCE ENVELOPE. Material inside
                        // z*(s) is core stream - it never reached the front, so it
                        // was never deposited and receives nothing. See
                        // Process.FountainDepositionSupport for the source and for
                        // why this is off by default.
                        if (proc.FountainDepositionSupport)
                        {
                            double sFrac = fill.PathLengthMm > 1e-9
                                ? fill.S[i] / fill.PathLengthMm : 0.0;
                            if (sFrac < 0.0) sFrac = 0.0;
                            if (sFrac > 1.0) sFrac = 1.0;
                            double zStar = Math.Sqrt(Math.Max(1.0 - (2.0 / 3.0) * sFrac, 0.0));
                            double halfWall = Math.Max(0.5 * freeze.ThicknessMm, 1e-9);
                            double zFrac = Math.Abs(freeze.Z[k]) / halfWall;
                            if (zFrac < zStar) dnFountain = 0.0;
                        }
                    }

                    // COMPLEMENTARY GATE, 2026-08-17. The two channels were
                    // double-counting the skin, measured by decomposition: at the
                    // wall the shear channel contributes 4.0e-5 under the
                    // melt-at-rest lambda and 5.15e-4 under the shear-thinned one,
                    // ON TOP OF an unchanged 1.12e-4 fountain deposit. Both claim
                    // the same material.
                    //
                    // They cannot both hold. Material outside z*(s) reached the
                    // wall THROUGH THE FRONT and is thereafter at a no-slip
                    // boundary where the velocity is zero, so it is not sheared
                    // there; before deposition it was in the core, where tau is
                    // LOW, not at the wall where tau is highest. Crediting it with
                    // the full wall shear history is attributing a stress it never
                    // experienced.
                    //
                    // Blake's envelope already answers which material that is. It
                    // was gating only the deposition term; the partition is
                    // complementary - front-deposited material gets the fountain,
                    // core-stream material gets the shear, neither gets both.
                    if (proc.ComplementaryShearGate && proc.FountainDepositionSupport)
                    {
                        double sFr = fill.PathLengthMm > 1e-9
                            ? fill.S[i] / fill.PathLengthMm : 0.0;
                        if (sFr < 0.0) sFr = 0.0;
                        if (sFr > 1.0) sFr = 1.0;
                        double zStarC = Math.Sqrt(Math.Max(1.0 - (2.0 / 3.0) * sFr, 0.0));
                        double halfWc = Math.Max(0.5 * freeze.ThicknessMm, 1e-9);
                        if (Math.Abs(freeze.Z[k]) / halfWc >= zStarC) dnShear = 0.0;
                    }

                    c.DnFlow[i, k] = dnShear + dnFountain;

                    c.SigmaThermalMPa[i, k] = sigma[k];

                    // Signed sum, not a sum of magnitudes: the two channels can
                    // oppose. C_melt is +1000 Br for this grade and K_glass is
                    // -8.5 Br, and the thermal stress itself changes sign through
                    // the wall (compression at the surface, tension in the core).
                    // Summing |.| would manufacture agreement wherever they
                    // cancel, which is exactly where the profile is most
                    // informative. The sampler takes the magnitude afterwards,
                    // which is what a polarimeter reads.
                    c.DnTotalOutOfPlane[i, k] =
                        c.DnFlow[i, k] + p.KGlassBrewster * 1e-6 * sigma[k];
                    c.DnDensity[i, k] = llFactor * compressibilityPerMPa * (fill.P[i] - pMean);
                }
            }

            // ---- THE PORT: depth shape from the Lagrangian, magnitude from here.
            //
            // Applied as a normalised reweighting rather than a replacement, so
            // the transfer is surgical. phi has mean 1 over the wall, so each
            // station's thickness average is multiplied by 1 - every clause that
            // reads a thickness average is invariant BY CONSTRUCTION, and only the
            // depth clauses can respond. That invariant is asserted below rather
            // than assumed, because a reweighting that quietly changed the
            // magnitude would look exactly like a model improvement.
            //
            // What this does NOT do: it does not give the Eulerian channel the
            // Lagrangian's along-flow behaviour, its deposition ordering, or its
            // per-station shape variation. The shape is taken once for the part
            // and applied at every station, so a part whose deposited fraction
            // grows strongly along the flow is not represented. Stated here
            // because the limitation is invisible in the output.
            if (proc.LagrangianDepthHistory)
            {
                // PER-STATION, ON THE LOCAL GAP.
                //
                // The first version of this port solved ONE shape for the part and
                // applied it everywhere, which made the normalised depth profile
                // identical at every station by construction. Right for a plate,
                // wrong for a lens: case 2's gap varies 2.5x along the flow, its
                // layer-removal clause is evaluated at the 0.8 mm gate region, and
                // a shape computed on the 2.0 mm centre thickness put 43.0% of the
                // retardance in the first 0.1 mm against a measured 27.9%.
                //
                // A particle model per station would be ns Lagrangian runs. The
                // shape depends on the station only through the local gap, so it
                // is solved at a few gap ratios spanning the part and interpolated
                // - and a uniform gap collapses to a single solve, so the plate
                // case costs exactly what it did before.
                double gMin = double.MaxValue, gMax = 0.0;
                var gRatio = new double[ns];
                for (int i = 0; i < ns; i++)
                {
                    double hLoc = fill.H[Math.Min(i, fill.H.Length - 1)];
                    gRatio[i] = freeze.ThicknessMm > 1e-9
                        ? Math.Max(hLoc / freeze.ThicknessMm, 1e-6) : 1.0;
                    if (gRatio[i] < gMin) gMin = gRatio[i];
                    if (gRatio[i] > gMax) gMax = gRatio[i];
                }

                int nNode = (gMax - gMin) / Math.Max(gMax, 1e-9) < 0.01
                    ? 1 : Math.Max(2, proc.DepthShapeGapNodes);
                var nodeR = new double[nNode];
                var nodePhi = new double[nNode][];
                int minCount = int.MaxValue;
                for (int m = 0; m < nNode; m++)
                {
                    nodeR[m] = nNode == 1 ? gMax
                             : gMin + (gMax - gMin) * m / (double)(nNode - 1);
                    var fz = freeze.ScaledToGap(nodeR[m]);
                    var lg = Lagrangian.Build(e, p, proc, fill, fz,
                                              Math.Max(200, proc.DepthShapeParticles));
                    int mc;
                    nodePhi[m] = lg.DepthShape(fz.Z, Math.Max(0.5 * fz.ThicknessMm, 1e-9), out mc);
                    if (mc < minCount) minCount = mc;
                }

                c.DepthShapeSource = nNode == 1 ? "lagrangian (uniform gap)"
                                                : "lagrangian (per-station gap)";
                c.DepthShapeMinCount = minCount;
                c.DepthShapeNodes = nNode;
                c.DepthShapeGapMin = gMin;
                c.DepthShapeGapMax = gMax;
                c.DepthShapePerStation = new double[ns, nz];

                var phiI = new double[nz];
                for (int i = 0; i < ns; i++)
                {
                    if (nNode == 1) Array.Copy(nodePhi[0], phiI, nz);
                    else
                    {
                        double u = (gRatio[i] - gMin) / Math.Max(gMax - gMin, 1e-30) * (nNode - 1);
                        int m0 = (int)Math.Floor(u);
                        if (m0 < 0) m0 = 0;
                        if (m0 > nNode - 2) m0 = nNode - 2;
                        double w = u - m0;
                        if (w < 0.0) w = 0.0;
                        if (w > 1.0) w = 1.0;
                        for (int k = 0; k < nz; k++)
                            phiI[k] = (1.0 - w) * nodePhi[m0][k] + w * nodePhi[m0 + 1][k];
                    }

                    // Interpolating two mean-1 shapes gives a mean-1 shape, but
                    // renormalise anyway: the invariant below protects every
                    // thickness-average clause, and it must not rest on an
                    // algebraic identity surviving floating point.
                    double mu = 0.0;
                    for (int k = 0; k < nz; k++) mu += phiI[k];
                    mu /= nz;
                    if (mu > 1e-30) for (int k = 0; k < nz; k++) phiI[k] /= mu;

                    double before = 0.0;
                    for (int k = 0; k < nz; k++) before += Math.Abs(c.DnFlow[i, k]);
                    before /= nz;

                    for (int k = 0; k < nz; k++)
                    {
                        c.DnFlow[i, k] = before * phiI[k];
                        c.DepthShapePerStation[i, k] = phiI[k];
                    }

                    double after = 0.0;
                    for (int k = 0; k < nz; k++) after += Math.Abs(c.DnFlow[i, k]);
                    after /= nz;
                    if (before > 1e-30 && Math.Abs(after - before) / before > 1e-9)
                        throw new InvalidOperationException(string.Format(
                            "Lagrangian depth port moved the thickness average at station {0}: "
                            + "{1:E6} -> {2:E6}. The shape must be mean-1; it is not.",
                            i, before, after));

                    for (int k = 0; k < nz; k++)
                        c.DnTotalOutOfPlane[i, k] =
                            c.DnFlow[i, k] + p.KGlassBrewster * 1e-6 * c.SigmaThermalMPa[i, k];
                }

                c.DepthShapeApplied = new double[nz];
                for (int k = 0; k < nz; k++)
                    c.DepthShapeApplied[k] = c.DepthShapePerStation[0, k];
            }

            // Where the flow birefringence peaks through the thickness - the
            // signature the reference case reports as a surface maximum well above
            // the core value.
            double best = 0.0; int bestK = nz / 2;
            for (int k = 0; k < nz; k++)
            {
                double v = Math.Abs(c.DnFlow[0, k]);
                if (v > best) { best = v; bestK = k; }
            }
            c.PeakDnFlow = best;
            c.PeakDepthFraction = Math.Abs(freeze.Z[bestK]) / (0.5 * freeze.ThicknessMm);
            return c;
        }

        /// <summary>
        /// The fraction of the steady viscous stress a layer still carries when
        /// it freezes, from a single-mode Maxwell memory integral.
        ///
        ///   t_a     when the melt reached this station
        ///   t_fill  when flow stops
        ///   t_f     when this layer crossed Tg (absolute)
        ///   lambda  relaxation time, eta/G
        ///   t_pack  packing flow persists weakly after fill; modelled as a
        ///           shear rate decaying with this time constant
        ///
        /// Bounded to [0, 1]: a layer cannot freeze in more orientation than the
        /// steady state it is heading towards.
        /// </summary>
        /// <summary>
        /// The same memory integral, but with a relaxation time that follows the
        /// layer's own temperature rather than being fixed at melt temperature.
        ///
        /// lambda(T) = eta0(T) / G, with eta0 from the Cross-WLF zero-shear term
        /// already in the material data. As a layer cools toward Tg the WLF shift
        /// raises lambda by orders of magnitude, so what it retains is decided by
        /// how much it relaxed while it was still HOT - not by a single constant
        /// evaluated where it never spends its last second.
        ///
        /// With a time-varying lambda the kernel is exp(-(xi(t_f) - xi(t'))) where
        /// xi is reduced time, INT dt/lambda(T(t)). Integrated numerically along
        /// the stored cooling curve and normalised by the melt-temperature lambda,
        /// so a constant temperature reproduces the closed-form bracket exactly -
        /// which is the control.
        /// </summary>
        /// <summary>
        /// Reduced time accumulated between deposition and freezing,
        /// xi = INT dt/lambda(T(t)). This is the whole fountain term: a strain
        /// imposed once, at the front, then relaxing at a rate that collapses as
        /// the material cools.
        /// </summary>
        public static double ReducedTimeToFreeze(double[] grid, double[] tempC,
                                                 Polymer p, double tFreezeLocal)
        {
            if (grid == null || grid.Length < 2 || tFreezeLocal <= 0) return 0.0;
            double xi = 0.0;
            for (int j = 1; j < grid.Length; j++)
            {
                if (grid[j - 1] >= tFreezeLocal) break;
                double tHi = Math.Min(grid[j], tFreezeLocal);
                double dt = tHi - grid[j - 1];
                if (dt <= 0) continue;
                double tMid = 0.5 * (tempC[j] + tempC[j - 1]);
                double lam = Math.Max(FillField.CrossWlf(p, 0.0, tMid, 0.0) / p.MeltModulusPa, 1e-12);
                xi += dt / lam;
            }
            return xi;
        }

        public static double MemoryFactorWlf(double tA, double tFill, double tF,
                                             double[] grid, double[] tempC,
                                             Polymer p, double lambdaMelt, double tPack,
                                             double[] meltFrac = null,
                                             bool narrowing = false,
                                             double tGateSeal = double.PositiveInfinity,
                                             bool shearThinnedDuringFill = false,
                                             double localShearRate = 0.0,
                                             bool relaxBelowTg = false)
        {
            if (tF <= tA || grid == null || grid.Length < 2) return 0.0;
            double tEndLocal = Math.Min(tF - tA, Math.Max(tFill - tA, 0.0));
            if (tEndLocal <= 0) return 0.0;
            double tFLocal = tF - tA;

            // RELAXATION BELOW Tg - the registered test, 2026-08-18.
            //
            // The integral stops at tFLocal, and the freeze time is DEFINED as the
            // instant T <= Tg, so orientation is locked the moment a layer crosses
            // Tg and never relaxes again. That is a hard cutoff on a transition
            // that is not sharp: just below Tg the material is in the softening
            // zone and still relaxes, only slowly.
            //
            // It predicts the measured pattern. Case 1 moulds 28 K below Tg and
            // its retention is right to 16%; case 2 moulds 14 K below Tg, so far
            // more of the part sits just under Tg where relaxation is still
            // active, and its retention is 13x too high.
            //
            // WLF stays valid there for both materials - the Vogel temperature
            // D2 - A2 is about 86 C for 480R and 105 C for TOPAS, and both moulds
            // run above it - so the test is simply to carry the SAME integral on
            // past the freeze time to the end of the recorded history, letting
            // lambda(T) keep growing. No new constant.
            //
            // RESULT 2026-08-18: REFUTED, and the flag is INERT. Both cases came
            // back identical to four significant figures - 1.16x and 12.87x
            // unchanged - because the relaxation clock has already stopped long
            // BEFORE Tg. lambda = eta0(T)/G is 2.86e7 s at Tg for both materials,
            // a third of a year, and reaches 1 s some 90 K ABOVE Tg in each
            // (268 C for TOPAS, 228 C for 480R). Extending the integral past Tg
            // therefore adds no reduced time at all.
            //
            // So the hard Tg cutoff was never doing the work, and the 13x
            // over-retention on case 2 is NOT explained by it. Kept as a switch
            // because the argument for it is sound and someone will propose it
            // again; it costs nothing and now carries its own refutation.
            if (relaxBelowTg) tFLocal = Math.Max(tFLocal, grid[grid.Length - 1]);

            // Reduced time along the curve, and the shear-weighted integral.
            // Reduced time on the grid, plus its INTERPOLATED value at the freeze
            // instant. Taking xiF from the first grid point at or beyond the
            // freeze time puts the kernel's reference a whole step late, and the
            // kernel decays on the scale of lambda - which can be far shorter
            // than a step. That cost 40% against the closed form and read as a
            // quadrature problem when it was an indexing one.
            double xi = 0.0, integral = 0.0;
            var xiAt = new double[grid.Length];
            var lamAt = new double[grid.Length];
            for (int j = 1; j < grid.Length; j++)
            {
                double dtj = grid[j] - grid[j - 1];
                double tMid = 0.5 * (tempC[j] + tempC[j - 1]);
                // RELAXATION TIME WHILE THE MELT IS FLOWING, 2026-08-17.
                //
                // This line evaluates CrossWlf at shear rate ZERO, i.e. lambda =
                // eta0(T)/G = 0.47 s at 280 C. That is the correct relaxation time
                // for melt at REST. While the cavity is filling the melt is under
                // high shear and is thinned by a factor of ~138 here, so its
                // effective relaxation time is far shorter and orientation decays
                // far faster than this line allows.
                //
                // The consequence is the depth profile's shape: layers that freeze
                // LATE (the core) keep fill-era orientation they should have lost,
                // and the predicted profile peaks at mid-depth instead of at the
                // skin. lambdaMelt - the shear-thinned value - was already being
                // passed in and was DEAD, never referenced in this body.
                // NOTE the name: tMid here is a TEMPERATURE, the mean of two
                // samples of the cooling curve. The integration loop below uses
                // tMidW for a TIME. The first version of this branch compared
                // tMid against tEndLocal - a temperature against a time - so it
                // was never true and the option was silently inert. Caught by the
                // arms printing identical numbers.
                double tTimeMid = 0.5 * (grid[j] + grid[j - 1]);
                // lambda(T, gammaDot) = eta(T, gammaDot)/G. Evaluating Cross at
                // gammaDot = 0 gives the melt-at-REST relaxation time and applies
                // it to material that is being sheared. Passing the LOCAL shear
                // rate keeps the temperature dependence - a near-wall layer that
                // has already cooled still gets its long lambda - where the first
                // version clamped every layer to one melt-temperature value.
                double gd = (shearThinnedDuringFill && tTimeMid <= tEndLocal)
                    ? localShearRate : 0.0;
                double lamHere = FillField.CrossWlf(p, gd, tMid, 0.0) / p.MeltModulusPa;

                // A VITRIFICATION CUTOFF HERE IS PROVABLY INERT - do not add one.
                // Cross-WLF has no glass transition in it, so the obvious worry is
                // that it shear-thins material already below Tg and lets a layer
                // keep accumulating reduced time after it has solidified. It
                // cannot: the integration below breaks at tFLocal, and tFreeze is
                // DEFINED in FreezeHistory as the instant T <= Tg, so the window
                // ends exactly at vitrification and never contains sub-Tg
                // material. Implemented and measured 2026-08-17 - identical
                // numbers in every arm - then removed rather than shipped inert.
                lamAt[j] = Math.Max(lamHere, 1e-12);
                if (dtj > 0) xi += dtj / lamAt[j];
                xiAt[j] = xi;
            }
            double xiF = xi;
            for (int j = 1; j < grid.Length; j++)
            {
                if (grid[j] >= tFLocal)
                {
                    double span = grid[j] - grid[j - 1];
                    double frac = span > 0 ? (tFLocal - grid[j - 1]) / span : 0.0;
                    xiF = xiAt[j - 1] + frac * (xiAt[j] - xiAt[j - 1]);
                    break;
                }
            }

            for (int j = 1; j < grid.Length; j++)
            {
                if (grid[j - 1] >= tFLocal) break;
                double tHi = Math.Min(grid[j], tFLocal);
                double dt = tHi - grid[j - 1];
                if (dt <= 0) continue;
                double spanJ = grid[j] - grid[j - 1];
                double xiHi = spanJ > 0
                    ? xiAt[j - 1] + (dt / spanJ) * (xiAt[j] - xiAt[j - 1])
                    : xiAt[j];

                // Shear weight: full while the cavity is filling, then the weak
                // packing flow decaying with tPack. The closed-form version
                // carries the same 0.1 packing term, and it has to be here too or
                // the two are not comparable - which is exactly what the
                // constant-temperature control caught: 1.16e-11 against 8.75e-2,
                // the whole difference being a term one of them did not have.
                double tMidW = 0.5 * (tHi + grid[j - 1]);

                // PARTIAL FLOW CUT-OFF: filling ends, but packing keeps a weak
                // flow going that decays with tPack rather than stopping dead.
                double weight = tMidW <= tEndLocal
                    ? 1.0
                    : 0.1 * Math.Exp(-(tMidW - tEndLocal) / Math.Max(tPack, 1e-9));

                // and nothing at all once the gate has frozen: the packing term
                // used to decay towards zero without ever reaching it, which left
                // the still-molten core accumulating orientation for seconds after
                // the last melt could possibly have entered.
                if (tA + tMidW >= tGateSeal) weight = 0.0;

                // CHANNEL NARROWING, GRADED. The shipped channel evaluated the
                // shear rate once, in the ORIGINAL gap. As the skin freezes the
                // molten channel closes and, at fixed flow rate, |dp/ds| goes as
                // 1/h_melt^3 - so the shear rate at any still-molten depth rises
                // through the cycle. Applying that as a hard interface switch made
                // the ratio diverge; applied here it is graded by the same memory
                // kernel and damped by the packing decay above, which is the
                // combination the diagnostics pointed at.
                if (narrowing && meltFrac != null && j < meltFrac.Length && meltFrac[j] > 1e-6)
                {
                    double narrow = 1.0 / meltFrac[j];
                    weight *= narrow * narrow * narrow;
                }

                // The kernel varies on the scale of lambda, which can be far
                // shorter than the grid spacing, so it is integrated EXACTLY
                // across each interval on the assumption that reduced time runs
                // linearly within it. A rectangle rule here read 23% low against
                // the closed form purely on quadrature.
                // VISCOSITY-WEIGHTED SHEAR RATE.
                //
                // The shear STRESS is continuous across the gap; the shear RATE is
                // not. A layer takes up strain at gamma_dot = tau/eta(T), so as it
                // stiffens it stops deforming - which is the physics the model was
                // missing and the clamp was standing in for.
                //
                // Substituting gamma_dot = tau/eta into the Maxwell equation gives
                //
                //     dsigma/dt = (tau - sigma) / lambda(t)
                //
                // so sigma relaxes TOWARD the local shear stress and can never pass
                // it. The bound is a consequence of the physics rather than a cap
                // imposed on top of it, and in the integral it appears simply as
                // the disappearance of the dt/dxi Jacobian: the kernel is now
                // integrated in REDUCED time, not in real time.
                double dXi = xiHi - xiAt[j - 1];
                double kernel = dXi > 1e-12
                    ? Math.Exp(-(xiF - xiHi)) * (1.0 - Math.Exp(-dXi))
                    : Math.Exp(-(xiF - xiHi)) * dXi;

                integral += weight * kernel;
            }
            // Already a fraction of the local shear stress: dividing by the
            // melt-temperature lambda was what made the old form unbounded.
            double v = integral;
            // CLAMP RESTORED 2026-08-15, and it is a STAND-IN, not a physical
            // bound. Read this before removing it again.
            //
            // It was removed once, deliberately, to see what it was holding back.
            // With it gone the in-plane peak went 2.02x -> 36.68x and the depth
            // ratio INVERTED, 2.07 -> 0.25, core-peaked where the published case
            // is skin-peaked. The channel-narrowing term was off for that test, so
            // the memory integral alone is responsible.
            //
            // The mechanism is a real defect and it is NOT in this line. With
            // lambda climbing as a layer cools, the Maxwell solution tends to
            // sigma -> G*lambda(T)*gamma_dot = eta(T)*gamma_dot, and eta near Tg is
            // three orders above its melt value. The integral is right; the INPUT
            // is wrong. This model applies the full MELT shear rate to material
            // that is nearly solid, right up to the freeze instant, when that
            // material has in fact stopped deforming and the flow has redistributed
            // into the hot core.
            //
            // So the clamp is standing in for a missing gamma_dot(z,t) weighted by
            // the local viscosity. Until that exists, removing the clamp does not
            // free the physics - it just lets a shear rate applied at the wrong
            // temperature report about 250x the steady stress.
            //
            // Removing it is only progress WITH that weighting in place.
            ClampCalls++;
            if (v > 1.0) ClampHits++;
            if (v > MaxRaw) MaxRaw = v;
            LastRaw = v;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public static double MemoryFactor(double tA, double tFill, double tF,
                                          double lambda, double tPack)
        {
            if (tF <= tA) return 0.0;                       // frozen before it arrived
            double tEnd = Math.Min(tF, tFill);
            double main = tEnd > tA
                ? Math.Exp(-(tF - tEnd) / lambda) - Math.Exp(-(tF - tA) / lambda)
                : 0.0;

            // Packing keeps a weak shear going after the cavity is full. Its
            // contribution decays with tPack and can only add, never subtract.
            double tail = 0.0;
            if (tF > tFill && tFill > tA)
            {
                double w = Math.Exp(-(tF - tFill) / Math.Max(tPack, 1e-9));
                tail = 0.1 * w * (1.0 - Math.Exp(-(tF - tFill) / lambda));
            }
            double v = main + tail;
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }

        /// <summary>
        /// In-plane residual stress from a freeze-off temperature profile, with
        /// force and moment balance imposed. Uniform and linear profiles must
        /// therefore return exactly zero, which is the control.
        /// </summary>
        public static double[] ThermalProfile(double[] tRef, double[] z, double coeff)
        {
            int n = tRef.Length;
            double sum = 0, sumZ = 0, sumZZ = 0, sumTZ = 0;
            for (int i = 0; i < n; i++)
            {
                sum += tRef[i]; sumZ += z[i]; sumZZ += z[i] * z[i]; sumTZ += tRef[i] * z[i];
            }
            double mean = sum / n, meanZ = sumZ / n;
            double varZ = sumZZ / n - meanZ * meanZ;
            double covTZ = sumTZ / n - mean * meanZ;
            double slope = Math.Abs(varZ) > 1e-30 ? covTZ / varZ : 0.0;

            var sigma = new double[n];
            for (int i = 0; i < n; i++)
                sigma[i] = coeff * (tRef[i] - mean - slope * (z[i] - meanZ));
            return sigma;
        }

        /// <summary>
        /// Birefringence at a stated fraction of the half-wall, interpolated.
        ///
        /// The depth criterion registered 2026-08-15 names its sampling points
        /// rather than leaving them to the caller, because the number it gates is
        /// meaningless without them: an average over the middle third includes the
        /// mid-plane, where shear vanishes identically, so it reports a ratio that
        /// is partly an artefact of the band.
        /// </summary>
        public static double DnAtDepthFraction(double[,] dn, double[] z, int station,
                                               double halfWallMm, double fraction)
        {
            // INTERPOLATED, not snapped to the nearest node. Snapping was the
            // first implementation and its own success-arm control caught it: on
            // an analytic profile of known ratio 5.0 it read 5.444, an 8.9% error
            // against a 1% bar, purely because no grid node sits exactly at 0.47
            // of the half-wall. An instrument that cannot return a ratio it is
            // given cannot be used to judge one it is not.
            double target = fraction * halfWallMm;
            int nz = z.Length;
            double loD = double.MaxValue, hiD = double.MaxValue;
            int loK = -1, hiK = -1;
            for (int k = 0; k < nz; k++)
            {
                double a = Math.Abs(z[k]);
                if (a <= target && target - a < loD) { loD = target - a; loK = k; }
                if (a >= target && a - target < hiD) { hiD = a - target; hiK = k; }
            }
            if (loK < 0) return Math.Abs(dn[station, hiK]);
            if (hiK < 0) return Math.Abs(dn[station, loK]);
            double zLo = Math.Abs(z[loK]), zHi = Math.Abs(z[hiK]);
            double vLo = Math.Abs(dn[station, loK]), vHi = Math.Abs(dn[station, hiK]);
            if (Math.Abs(zHi - zLo) < 1e-12) return vLo;
            double t = (target - zLo) / (zHi - zLo);
            return vLo + t * (vHi - vLo);
        }

        public static void SelfCheck()
        {
            Console.WriteLine("  A3 three channels");

            // --- SUCCESS ARM for the depth criterion --------------------------
            // Before any surface/core ratio can mean anything, the extraction has
            // to be shown to return a ratio it is given. Analytic profile with a
            // known ratio of exactly 5.0, through the same sampling code.
            {
                int nArm = 41;
                var zz = new double[nArm];
                var dd = new double[1, nArm];
                double half = 0.75;                       // the registered plate
                for (int k = 0; k < nArm; k++)
                {
                    zz[k] = -half + 2.0 * half * k / (nArm - 1.0);
                    double f = Math.Abs(zz[k]) / half;
                    // Constructed so the ratio between the two SAMPLING points is
                    // exactly 5.0. The first version made it 5.0 at f = 1 while
                    // sampling at f = 0.975, so it expected 5.0 and the correct
                    // answer was 4.811 - the control was wrong, not the extraction,
                    // and it took the disagreement to notice.
                    dd[0, k] = 1.0 + 4.0 * (f - 0.47) / (0.975 - 0.47);
                }
                double surf = DnAtDepthFraction(dd, zz, 0, half, 0.975);
                double deep = DnAtDepthFraction(dd, zz, 0, half, 0.47);
                SelfTest.Near("depth extraction returns a known ratio of 5.0",
                    surf / deep, 5.0, 0.01);
            }

            var p = Polymers.ByName("MS_PMMA");
            var proc = new Process();

            // --- thermal balance controls -----------------------------------
            int n = 41;
            var z = new double[n];
            var flat = new double[n];
            var ramp = new double[n];
            for (int i = 0; i < n; i++)
            {
                z[i] = -1.0 + 2.0 * i / (n - 1.0);
                flat[i] = 150.0;
                ramp[i] = 100.0 + 30.0 * z[i];
            }
            double maxFlat = 0, maxRamp = 0;
            var sFlat = ThermalProfile(flat, z, 1000.0);
            var sRamp = ThermalProfile(ramp, z, 1000.0);
            for (int i = 0; i < n; i++)
            {
                maxFlat = Math.Max(maxFlat, Math.Abs(sFlat[i]));
                maxRamp = Math.Max(maxRamp, Math.Abs(sRamp[i]));
            }
            SelfTest.Near("a uniform freeze-off profile gives zero stress", maxFlat, 0.0, 1e-9);
            SelfTest.Near("a linear freeze-off profile gives zero stress", maxRamp, 0.0, 1e-9);

            // A parabolic profile must NOT give zero, or the check above is
            // passing because the function returns zero for everything.
            var para = new double[n];
            for (int i = 0; i < n; i++) para[i] = 100.0 + 40.0 * (1.0 - z[i] * z[i]);
            var sPara = ThermalProfile(para, z, 1000.0);
            double maxPara = 0;
            for (int i = 0; i < n; i++) maxPara = Math.Max(maxPara, Math.Abs(sPara[i]));
            SelfTest.Check("a parabolic profile does not", maxPara > 1e3,
                string.Format("peak {0:F0} MPa-equivalent", maxPara));
            SelfTest.Check("core ends in tension, skin in compression",
                sPara[n / 2] > 0 && sPara[0] < 0,
                string.Format("core {0:+0.0;-0.0}, skin {1:+0.0;-0.0}", sPara[n / 2], sPara[0]));

            // --- the memory integral, against its own two limits -------------
            // Short memory plus a long shear window must return the steady
            // viscous stress exactly - the instantaneous model is this model's
            // fast-relaxation limit, not a competitor to it.
            SelfTest.Near("memory returns the steady state when lambda is short",
                MemoryFactor(0.0, 10.0, 5.0, 1e-3, 3.0), 1.0, 1e-9);

            // A layer that freezes as the melt arrives has no shear window at
            // all, so it can carry nothing however large the local stress is.
            // This is the term that produces the gate-to-edge decay.
            SelfTest.Near("no shear window means no frozen orientation",
                MemoryFactor(1.0, 1.0, 1.0000001, 0.5, 3.0), 0.0, 1e-6);

            // And it must fall monotonically as the melt arrives later, which is
            // the decay itself rather than a proxy for it.
            double mEarly = MemoryFactor(0.0, 1.0, 1.2, 0.5, 3.0);
            double mMid = MemoryFactor(0.5, 1.0, 1.7, 0.5, 3.0);
            double mLate = MemoryFactor(0.95, 1.0, 2.15, 0.5, 3.0);
            SelfTest.Check("memory falls as the melt arrives later",
                mEarly > mMid && mMid > mLate,
                string.Format("{0:F4} -> {1:F4} -> {2:F4}", mEarly, mMid, mLate));
            SelfTest.Check("memory stays inside [0,1]",
                mEarly <= 1.0 && mLate >= 0.0, "bounded");

            // CONTROL for the WLF version: hold the temperature CONSTANT at melt
            // and the numerical reduced-time integral must reproduce the
            // closed-form constant-lambda bracket. If it does not, the new
            // machinery is not a generalisation of the old one, it is a
            // different model wearing its name.
            {
                var pw = Polymers.ByName("MS_PMMA");
                int ng = 240;
                var grid = new double[ng];
                var flatT = new double[ng];
                for (int j = 0; j < ng; j++) { grid[j] = 2.0 * j / (ng - 1.0); flatT[j] = pw.MeltTempC; }
                double lamMelt = FillField.CrossWlf(pw, 0.0, pw.MeltTempC, 0.0) / pw.MeltModulusPa;
                double wlf = MemoryFactorWlf(0.0, 1.0, 1.4, grid, flatT, pw, lamMelt, 3.0);
                double closed = MemoryFactor(0.0, 1.0, 1.4, lamMelt, 3.0);
                SelfTest.Near("WLF memory reduces to the closed form at constant T",
                    wlf, closed, 0.02);
            }

            // --- fountain flow ------------------------------------------------
            {
                var pf = Polymers.ByName("MS_COC_TOPAS6017");
                var procOn = new Process { FillTimeS = 1.0, PackTimeS = 3.0, FountainStrain = 1.0 };
                var procOff = new Process { FillTimeS = 1.0, PackTimeS = 3.0, FountainStrain = 0.0 };
                var plateF = new MouldedElement
                {
                    FrontSurface = 1, CentreThicknessMm = 1.5, SemiDiameterMm = 50.0,
                    FrontRadiusMm = 0, BackRadiusMm = 0,
                };
                plateF.EdgeThicknessMm = plateF.ThicknessAt(plateF.SemiDiameterMm);
                plateF.Gate = new GateSpec { Kind = GateKind.FilmEdge, AzimuthDeg = 0,
                                             WidthMm = 100, ThicknessMm = 0.9 };
                var fillF = FillField.Build(plateF, pf, procOn, 51);
                var frF = FreezeHistory.Build(plateF.CentreThicknessMm, pf, procOn, 81);
                // THESE CHECKS ARE ABOUT THE EULERIAN DECOMPOSITION, so they are
                // pinned to it. They ask what the fountain term contributes at a
                // fixed depth by subtracting a run with it off - which assumes the
                // fountain is a separable additive term at that depth. Under the
                // Lagrangian depth port it is not: it is folded into the station's
                // thickness average and then redistributed by a shape, so the
                // subtraction returns the difference of two magnitudes scaled by
                // the same shape and the "must not vary along the flow" property
                // stops holding. That is the port changing the decomposition, not
                // the fountain picking up the shear field, and it failed exactly
                // this way when the port became the default (1.19e-4 vs 2.68e-4).
                //
                // Deleting them would lose a real guard on the Eulerian channel,
                // which still ships and is still reachable with -eulerian-depth.
                // So they keep testing what they were written to test, on the path
                // where the property is true, and the default path gets its own
                // checks below.
                procOn.LagrangianDepthHistory = false;
                procOff.LagrangianDepthHistory = false;
                var cOn = Build(plateF, pf, procOn, fillF, frF);
                var cOff = Build(plateF, pf, procOff, fillF, frF);

                // Turning it off must return the previous model EXACTLY - the term
                // is additive and must not perturb anything else.
                int kCore = frF.NodeCount / 2;
                SelfTest.Near("fountain off reproduces the shear-only model",
                    cOff.DnFlow[0, kCore + 3], cOn.DnFlow[0, kCore + 3] -
                    (cOn.DnFlow[0, kCore + 3] - cOff.DnFlow[0, kCore + 3]), 1e-12);

                // Its own contribution must fall monotonically from skin to core:
                // it is a single relaxing strain, so more reduced time means less
                // survives, and reduced time only grows inward.
                double fSkin = Math.Abs(cOn.DnFlow[0, frF.NodeCount - 2] - cOff.DnFlow[0, frF.NodeCount - 2]);
                double fMid = Math.Abs(cOn.DnFlow[0, (3 * frF.NodeCount) / 4] - cOff.DnFlow[0, (3 * frF.NodeCount) / 4]);
                double fCore = Math.Abs(cOn.DnFlow[0, kCore] - cOff.DnFlow[0, kCore]);
                SelfTest.Check("fountain contribution decays from skin to core",
                    fSkin > fMid && fMid > fCore,
                    string.Format("{0:E3} -> {1:E3} -> {2:E3}", fSkin, fMid, fCore));

                // And it must not depend on distance from the gate: deposition and
                // relaxation, nothing else. If it varies along the flow, it has
                // picked up the shear field by mistake.
                int iFar = cOn.S.Length - 1;
                double fFar = Math.Abs(cOn.DnFlow[iFar, frF.NodeCount - 2] - cOff.DnFlow[iFar, frF.NodeCount - 2]);
                SelfTest.Near("fountain is the same at the gate and the far edge",
                    fFar, fSkin, 1e-9);
            }

            // --- Lorentz-Lorenz, against a numerical differentiation ---------
            // Checking the analytic factor against itself proves nothing, so
            // solve the relation directly: (n^2-1)/(n^2+2) = C rho, perturb rho,
            // and see what n does. The first draft of this check carried a
            // hand-typed factor of 0.4342746 and was wrong by 25%.
            double nd = 1.4917, dRho = 1e-6;
            double C = (nd * nd - 1.0) / ((nd * nd + 2.0) * 1.0);      // rho == 1 reference
            double y = C * (1.0 + dRho);
            double nPerturbed = Math.Sqrt((1.0 + 2.0 * y) / (1.0 - y));
            double numeric = (nPerturbed - nd) / dRho;                 // dn / (drho/rho)
            double analytic = (nd * nd - 1.0) * (nd * nd + 2.0) / (6.0 * nd);
            SelfTest.Near("Lorentz-Lorenz factor against a numerical derivative",
                analytic, numeric, 1e-5);

            // --- the assembled channels -------------------------------------
            var plate = new MouldedElement
            {
                FrontSurface = 1, CentreThicknessMm = 2.0, SemiDiameterMm = 10.0,
                FrontRadiusMm = 0, BackRadiusMm = 0,
            };
            plate.EdgeThicknessMm = plate.ThicknessAt(plate.SemiDiameterMm);
            plate.Gate = Gating.DefaultGate(plate);
            var fill = FillField.Build(plate, p, proc, 101);
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, proc, 81);
            var c = Build(plate, p, proc, fill, freeze);

            // Shear vanishes on the mid-plane, so the SHEAR channel must too -
            // measured with the fountain off, because deposition does not vanish
            // there and has no reason to. Running this on the total was fine only
            // while the fountain was gated off by default.
            //
            // Also pinned to the Eulerian path, and for a reason worth stating:
            // this is NOT true under a Lagrangian history and should not be. It
            // holds because an Eulerian element at the mid-plane has sat there
            // since t=0 where the shear stress is exactly zero. Give the material
            // a path and the element now at the mid-plane arrived from somewhere
            // with nonzero shear - it is the last material the front laid down -
            // so it carries orientation, and the model that says otherwise is the
            // one this port exists to replace. Asserting it on the default path
            // would be asserting the assumption that was wrong.
            var procNoF = new Process { FillTimeS = proc.FillTimeS, PackTimeS = proc.PackTimeS,
                                        PackPressureMPa = proc.PackPressureMPa,
                                        FountainStrain = 0.0,
                                        LagrangianDepthHistory = false };
            var cShear = Build(plate, p, procNoF, fill, freeze);
            SelfTest.Near("shear birefringence vanishes at the mid-plane (Eulerian path)",
                cShear.DnFlow[0, freeze.NodeCount / 2], 0.0, 1e-12);

            // --- and what the DEFAULT path must satisfy instead ---------------
            // The port's whole safety argument is that it moves the depth shape
            // and nothing else. That is enforced at runtime by an assertion
            // inside Build, which only fires on the station it is checking; this
            // checks it end to end, on the quantity the in-plane clauses read.
            {
                var procE = new Process { FillTimeS = proc.FillTimeS, PackTimeS = proc.PackTimeS,
                                          PackPressureMPa = proc.PackPressureMPa,
                                          LagrangianDepthHistory = false };
                var procL = new Process { FillTimeS = proc.FillTimeS, PackTimeS = proc.PackTimeS,
                                          PackPressureMPa = proc.PackPressureMPa,
                                          LagrangianDepthHistory = true,
                                          DepthShapeParticles = 1000 };
                var cE = Build(plate, p, procE, fill, freeze);
                var cL = Build(plate, p, procL, fill, freeze);
                int nzS = freeze.NodeCount;
                double avgE = 0.0, avgL = 0.0;
                for (int k = 0; k < nzS; k++)
                {
                    avgE += Math.Abs(cE.DnFlow[0, k]);
                    avgL += Math.Abs(cL.DnFlow[0, k]);
                }
                SelfTest.Near("the depth port leaves the thickness average alone",
                    avgL / nzS, avgE / nzS, 1e-9);

                // ... and it must actually MOVE the shape, or the check above is
                // passing on a port that did nothing.
                double topE = Math.Abs(cE.DnFlow[0, nzS - 2]), topL = Math.Abs(cL.DnFlow[0, nzS - 2]);
                SelfTest.Check("the depth port moves the skin value",
                    topE > 0.0 && Math.Abs(topL - topE) / topE > 0.25,
                    string.Format("skin {0:E3} -> {1:E3}", topE, topL));
            }

            // ... and it must peak between the surface and the core, not at
            // either end. That is the signature the reference case reports.
            SelfTest.Check("flow birefringence peaks below the surface",
                c.PeakDepthFraction > 0.05 && c.PeakDepthFraction < 0.999,
                string.Format("peak {0:E3} at {1:P0} of the half-thickness",
                    c.PeakDnFlow, c.PeakDepthFraction));

            // Density follows pressure, so it must be largest at the gate.
            SelfTest.Check("density index change is largest near the gate",
                Math.Abs(c.DnDensity[0, 0]) > Math.Abs(c.DnDensity[c.S.Length - 1, 0]),
                string.Format("{0:E3} at the gate vs {1:E3} at the far end",
                    c.DnDensity[0, 0], c.DnDensity[c.S.Length - 1, 0]));
        }
    }
}

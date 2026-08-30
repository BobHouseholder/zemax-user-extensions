using System;

namespace MoldStress
{
    /// <summary>Process conditions. Defaults are conventional, not solved.</summary>
    internal sealed class Process
    {
        public double FillTimeS = 0.6;
        public double PackPressureMPa = 60.0;
        public double PackTimeS = 3.0;
        public double MeltTempC = double.NaN;   // NaN => the polymer's own default
        public double MoldTempC = double.NaN;

        /// <summary>
        /// Multiplier on the relaxation time lambda = eta/G. Exists so the
        /// depth-profile hypothesis can be TESTED as a lever rather than argued:
        /// if the surface-peaking is caused by lambda being far shorter than the
        /// fill time, raising this must move the surface/deep ratio.
        /// </summary>
        public double LambdaScale = 1.0;

        /// <summary>
        /// Strain imposed on material as it turns through the fountain at the
        /// advancing melt front and is laid onto the cold wall. Its magnitude is
        /// NOT this number alone: it is set by the front kinematics - a Maxwell
        /// fluid extended at v_front/(h/2) for one gap-crossing time - and this
        /// scales that. 1.0 means the physical model; 0 disables the term.
        ///
        /// ON BY DEFAULT SINCE 2026-08-15. It was gated off between 08-15 and
        /// 08-15 because enabling it made both registered criteria worse. The
        /// viscosity-weighted shear rate inverted that: shear alone now correctly
        /// gives a fast-freezing skin almost no orientation, so deposition at the
        /// front is the only thing left that can orient one - and with both
        /// channels the in-plane peak goes 0.26x -> 0.90x of the published value
        /// and the depth ratio 0.02 -> 0.76, on measured constants with no fitted
        /// parameter between the two channels.
        ///
        /// The gate's own recorded condition was a measured melt stress-optical
        /// coefficient, which was met, and it then stood only on the evidence that
        /// the term made things worse. That evidence no longer holds, so the gate
        /// is lifted rather than quietly kept.
        ///
        /// Disable with -fountain 0 to recover the shear-only model.
        ///
        /// REFUTED AS THE ORIGIN OF THE SURFACE MAXIMUM, 2026-08-19, and KEPT
        /// ANYWAY - both halves matter.
        ///
        /// Wimberger-Friedl, Int. Polym. Process. 11(4) 373 (1996), on the same
        /// mould, machine and PC grade as reference case 4, concludes verbatim:
        /// "The observed behaviour rules out the fountain flow induced
        /// elongational stresses as the origin for the birefringence maximum at
        /// the surface." His evidence is that the surface birefringence is
        /// EQUI-BIAXIAL - equal in the flow direction and transverse to it, and
        /// independent of gate distance - which no flow mechanism produces, and
        /// that its height scales with CAVITY PRESSURE rather than with injection
        /// rate. He replaces it with transient pressure-induced deviatoric
        /// stresses in the vitrifying layer (his Eqs 5-8).
        ///
        /// WHY THE DEFAULT DID NOT FLIP. The refutation is specific: one feature
        /// (the surface maximum), one polymer (PC). Measured 2026-08-19, turning
        /// this term off costs:
        ///
        ///     case 1 (TOPAS COC)  MET  -> NOT met
        ///     case 4 (PC plate)   MET  -> NOT met, on clause (e1)
        ///
        /// and the paper itself says the balance is material-specific - PS is
        /// "dominated to a much greater extent by flow-induced molecular
        /// orientation" than PC. Flipping a global default on one polymer's
        /// evidence would break two registered criteria and swap one unearned
        /// attribution for another.
        ///
        /// WHAT DID CHANGE IS THE CLAIM. This term is no longer described
        /// anywhere as the explanation for the surface maximum. It is a
        /// deposition term that empirically improves two cases, and the mechanism
        /// the source names for the surface maximum is not implemented.
        ///
        /// AND IT EXPOSED A REAL DEFECT. With this term off, case 4's clause (e1)
        /// reads 0.847 at ALL THREE mould temperatures - the peak stops moving
        /// entirely. So the mould-temperature trend this model reproduces comes
        /// from the FOUNTAIN term, not from the thinning solidified layer the
        /// source credits ("an increase in mold temperature leads to a decrease in
        /// thickness of the solidified layer at the end of filling"). The model
        /// gets the published trend for the wrong reason. Recorded, not patched.
        public double FountainStrain = 1.0;

        /// <summary>
        /// What the melt front deposits. Enable the melt-orientation form with
        /// -frontmode carried; the extensional form is the DEFAULT because the
        /// melt-orientation form as written is measurably worse.
        ///
        /// THE IDEA, and it still looks right: the extensional form treats
        /// deposition as a fresh strain imposed on unoriented material, and a
        /// Maxwell fluid extended at edot for 1/edot cannot build more than its
        /// own plateau modulus (eEff -> 1 as Wi -> inf, so sigma <= G = 2.8e5 Pa).
        /// That ceiling is the measured saturation behind the skin deficit -
        /// tripling FountainStrain moved the depth ratio only 0.81 -> 1.03. The
        /// material reaching the front is NOT unoriented: it has just come down
        /// the channel at melt temperature where the wall shear stress is ~5e5 Pa,
        /// above G. So the cap was binding on the wrong quantity.
        ///
        /// WHY IT IS NOT THE DEFAULT, measured 2026-08-17 at nz=81:
        ///
        ///   in-plane peak   1.07x -> 4.57x   (passes -> FAILS a factor of 2)
        ///   depth ratio     0.81  -> 1.09    (fails -> still fails)
        ///   depth null      passes -> FAILS  (1.09 vs 1.09)
        ///
        /// The defect is in the implementation, not obviously in the idea.
        /// 2*tau_wall = dp/ds * (h/2) has NO z dependence, so it lifts every
        /// depth by the same amount and leaves exp(-xi) as the only thing
        /// distinguishing skin from core - which is not enough to produce a
        /// skin peak. It inflated the thickness average fourfold and barely moved
        /// the ratio, and it flattened the profile enough to kill the null again.
        ///
        /// What is missing is that NOT EVERY DEPTH IS FRONT-DEPOSITED. Material
        /// near the mid-plane is the core stream; it is never swept to the wall.
        /// The deposition term needs a weight that falls off inward, and choosing
        /// that weight is the open question rather than another constant.
        /// </summary>
        public bool FrontCarriesMeltOrientation = false;

        /// <summary>
        /// Restrict front deposition to the material that actually passed through
        /// the flow front, using Blake's maximum-residence envelope.
        ///
        /// SOURCE, and it is parameter-free: M. C. Altan, "A Review of
        /// Fiber-Reinforced Injection Molding: Flow Kinematics and Particle
        /// Orientation", J. Thermoplastic Composite Materials 3 (Oct 1990) 275,
        /// section 2.4.4, presenting J. W. Blake's treatment; pathlines measured
        /// by Coyle, Blake & Macosko, AIChE J. 33(7) 1168 (1987).
        ///
        /// Particles are sorted by whether they ever reached the front. For a
        /// Newtonian profile the dividing height - the one moving at the mean
        /// velocity - is x3v = 1/sqrt(3) = 0.577, and the envelope is
        ///
        ///     x1m = (3/2) * (1 - x3m^2),   1/sqrt(3) < x3m < 1
        ///
        /// which inverts to the support boundary used here:
        ///
        ///     z*(s) = sqrt(1 - (2/3) * s/L)      in units of the half gap
        ///
        /// Material inside z* is core stream: it never passed through the front,
        /// so it receives no deposition. This is the term the uniform 2*tau_wall
        /// implementation was missing - it deposited at every depth, including
        /// material that was never at the front.
        ///
        /// CAVEAT, stated because it is being extended beyond its derivation: the
        /// envelope is derived for Newtonian, isothermal flow. Coyle et al. found
        /// shear-thinning changed the front SHAPE and kinematics little, which is
        /// the basis for using it with a Cross-WLF melt, but that is their
        /// statement about front shape and this is a deposition boundary.
        ///
        /// AND NOTE WHERE IT BITES: z*(0) = 1, so at the gate itself the envelope
        /// admits NO deposited material. The boundary crosses the depth
        /// criterion's own surface sampling point (0.975 of the half-wall) at
        /// s/L = 0.075, so only the first ~7% of the flow length is affected -
        /// but the depth criterion samples at s = 0 exactly, which is that
        /// station. The reference paper measured its depth profiles at positions
        /// A, B and C and gives no coordinates for them, so the criterion's
        /// station is NOT moved to suit; the ratio is reported across stations
        /// instead and the gate value is reported as what it is.
        /// </summary>
        public bool FountainDepositionSupport = false;

        /// <summary>
        /// Apply Blake's envelope COMPLEMENTARILY: front-deposited material
        /// (outside z*) gets the fountain term and NOT the shear term; core-stream
        /// material (inside z*) gets shear and no deposition. Requires
        /// FountainDepositionSupport, which supplies the boundary.
        ///
        /// Without it the two channels double-count the skin - measured by
        /// decomposition at the wall: shear contributes 4.0e-5 under the
        /// melt-at-rest lambda and 5.15e-4 under the shear-thinned one, on top of
        /// an unchanged 1.12e-4 fountain deposit, both claiming the same material.
        /// </summary>
        public bool ComplementaryShearGate = false;

        /// <summary>
        /// Let orientation keep relaxing after a layer crosses Tg, on the same
        /// WLF clock, instead of locking it at the freeze instant. Registered as
        /// a falsifier 2026-08-18: case 2's retention must fall by ~13x while
        /// case 1's moves by less than 20%, because 28 K below Tg buys far more
        /// vitrification than 14 K does. If case 1 degrades comparably the
        /// mechanism is wrong.
        /// </summary>
        public bool RelaxBelowTg = false;


        /// <summary>
        /// Scale the front deposition by the shear window available to the melt
        /// FEEDING the front at that station, so the deposited magnitude decays
        /// along the flow the way the shear channel already does.
        ///
        /// Without it the fountain term has no along-flow dependence at all. With
        /// Blake's envelope on - which makes the deposited layer THICKEN with
        /// distance from the gate - the predicted profile rises to 129.3% of the
        /// gate value at the far edge, against a reference that falls roughly
        /// linearly to zero. The support was right and the magnitude was missing
        /// its along-flow term.
        ///
        /// The factor is the memory bracket evaluated at the MID-PLANE, the core
        /// stream the front draws from. It is the same expression, and the same
        /// argument, that already gives the shear channel its gate-to-edge decay:
        /// at the far edge the melt arrives as filling ends, so the window is
        /// identically zero and there is no orientation to deposit. An extension
        /// of an argument already in the model, not a new constant.
        /// </summary>
        /// <summary>
        /// Use the SHEAR-THINNED relaxation time while the cavity is filling.
        ///
        /// MemoryFactorWlf evaluates CrossWlf at shear rate zero, giving
        /// lambda = eta0(T)/G = 0.47 s at 280 C for this grade. That is right for
        /// melt at rest and wrong while the melt is flowing: under fill shear the
        /// viscosity is ~138x lower here, so orientation relaxes far faster than
        /// the zero-shear value allows. The shear-thinned lambda was ALREADY being
        /// passed into that function and was dead code, never referenced.
        /// </summary>
        public bool ShearThinnedLambdaDuringFill = false;

        public bool FountainDecaysAlongFlow = false;


        /// <summary>
        /// Grade the shear rate by the narrowing molten channel, |dp/ds| going as
        /// 1/h_melt^3 as the skin closes the gap. OFF by default: it was inert
        /// under the old memory clamp, and with the clamp gone it is not inert but
        /// it drives orientation into the CORE - measured, depth ratio 2.07 -> 0.22
        /// - which is the opposite of the published skin-peaked profile.
        /// </summary>
        /// <summary>
        /// Take the flow channel's DEPTH SHAPE from the Lagrangian particle model
        /// instead of from the Eulerian per-depth history, preserving each
        /// station's thickness average exactly.
        ///
        /// The Eulerian channel assumes every layer sat at its final depth since
        /// t=0. Under that assumption the memory factor is the product of two
        /// monotone factors running in opposite directions - build-up needs
        /// reduced time, retention is destroyed by it - so it MUST peak somewhere
        /// between wall and core, and it does, at 60% of the half-wall on both
        /// reference cases. The measurements peak at the skin. That is not a
        /// missing term: tripling the deposition raises the wall from 1.49e-4 to
        /// 4.47e-4 and leaves the peak at 60%, and eight configurations of extra
        /// terms were measured and rejected before this.
        ///
        /// Lagrangian.cs already carries the right history - skin material was
        /// sheared in the hot core and carried to the wall by the front - and this
        /// flag is what lets the shipped channel use it without becoming it.
        ///
        /// ON BY DEFAULT since 2026-08-18, after it was measured on both cases.
        /// Case 1 goes from failing both depth clauses to meeting the registered
        /// criterion - depth ratio 0.82 -> 3.44 against a published 2.78, peak
        /// position 53% -> 94% - and case 2's layer removal stays at 3 of 4 once
        /// the shape is solved on the local gap. In-plane numbers are unchanged
        /// on both cases, which the mean-1 normalisation guarantees and the
        /// runtime assertion enforces.
        ///
        /// It costs real time: the shape is a particle solve per gap node, so a
        /// build that took under a second takes tens of seconds. -eulerian-depth
        /// turns it off and restores the previous behaviour exactly.
        /// </summary>
        /// <summary>
        /// Accumulate thermal stress INCREMENTALLY as the solidification front
        /// sweeps, instead of setting it from the temperature profile at the
        /// single instant the centre vitrifies. Validated on reference case 3
        /// (free quench) where it moved every number toward the published values.
        /// ON by default since 2026-08-18, after measurement on cases 1 and 2.
        /// Case 2 is insensitive (first layer 32.6% -> 32.7%, every verdict
        /// unchanged). Case 1 still meets its criterion but its depth ratio moves
        /// 3.43 -> 4.12 against a published 2.78 - i.e. AWAY from the reference.
        /// Adopted anyway, and the reason matters: the snapshot construction was
        /// demonstrably capped (its crossing could not move with initial
        /// temperature at all), and what the move exposes is a thermal
        /// over-contribution on case 1 that the snapshot was partly masking - the
        /// FLOW channel alone gives 2.84 against a published 2.78, and a source
        /// for this material puts the thermal share at 8%, which cannot produce a
        /// 21% shift let alone 45%. `-snapshot` restores the old construction.
        /// </summary>
        public bool IncrementalThermal = true;

        public bool LagrangianDepthHistory = true;

        /// <summary>
        /// How many gap ratios the Lagrangian depth shape is solved at before
        /// interpolating between them. Only used when the gap actually varies;
        /// a uniform gap collapses to one solve regardless. Exposed so the
        /// choice can be swept rather than asserted - 6 is the shipped value
        /// because case 2's layer-removal numbers stop moving there.
        /// </summary>
        public int DepthShapeGapNodes = 6;

        /// <summary>
        /// Particles per Lagrangian solve behind the depth shape. Exposed
        /// because nothing else in the model tests it: the grid sweep varies nz,
        /// which changes the band widths but not the particle seeding, so a
        /// shape that was converged in nz could still be carrying seeding noise
        /// - and that noise would be interpolated between gap nodes as though it
        /// were curvature.
        /// </summary>
        public int DepthShapeParticles = 4000;

        /// <summary>
        /// Time steps in each particle solve. Hardcoded at 3000 and never tested
        /// until 2026-08-18, which is the same gap the particle count had - and
        /// this one is now the dominant cost of a reference case, so an untested
        /// constant here is an untested runtime.
        /// </summary>
        public int DepthShapeSteps = 2000;

        /// <summary>
        /// A SECOND flow-orientation channel: packing flow through the channel
        /// narrowed by the growing frozen layer, which orients material at the
        /// CENTRE of the gap where the fill shear stress is small.
        ///
        /// Chang et al. name this mechanism for the second birefringence peak
        /// they measure near mid-thickness, and it is the only route past the
        /// ceiling that reference case 2's in-plane clause runs into: the fill
        /// channel is bounded by 2*C*&lt;tau_fill(z)&gt;, which is 0.49 of the
        /// published value, because tau_fill is largest exactly where the
        /// retained fraction is smallest.
        ///
        /// Off by default until measured on all three cases.
        /// </summary>
        public bool PackingOrientation = false;

        /// <summary>
        /// The packing flow rate as a fraction of the fill rate. Packing moves
        /// far less material than filling - it is compensating shrinkage, not
        /// filling a cavity - so this is small. Exposed because it is the one
        /// number in the packing channel that is not derived.
        /// </summary>
        public double PackFlowFraction = 0.05;

        /// <summary>
        /// Include the FIRST NORMAL STRESS DIFFERENCE when converting frozen
        /// orientation to birefringence.
        ///
        /// The stress-optic law acts on the PRINCIPAL stress difference,
        /// dn = C*sqrt((s11-s22)^2 + 4*s12^2). This model has been using
        /// dn = 2*C*s12, which drops the normal term - and that simplification is
        /// only valid for a generalized Newtonian fluid, where s11-s22 vanishes.
        /// Lai and Wang say exactly that when they make it: "(sigma11 - sigma22)
        /// is zero because of isobaric pressure conditions for GNF model flow".
        ///
        /// But this model is NOT a GNF: it carries a single-mode Maxwell memory,
        /// and a Maxwell fluid in shear has N1 = 2*s12^2/G. So the conversion
        /// should be dn = 2*C*s12*sqrt(1 + (s12/G)^2), and the root is the term
        /// that has been missing. Chang et al., whose measurement this is
        /// compared against, use White-Metzner - viscoelastic, N1 non-zero.
        ///
        /// ON by default since 2026-08-18, after measurement on all three cases.
        /// Case 1 is essentially unmoved (in-plane 1.16x -> 1.17x, depth 3.44 ->
        /// 3.43, still MET) because its shear stress is a third of case 2's, so
        /// the enhancement is small. Case 2 gains the predicted 1.4x, 0.20x ->
        /// 0.28x. Case 3 is thermal and unaffected. Adopted because the previous
        /// form was the GNF simplification carried by a model that is NOT a GNF -
        /// wrong independently of which way the number moved - and it does NOT
        /// rescue case 2, which still fails by 3.6x. The `-normal-stress` flag is kept as
        /// an explicit opt-in; there is no opt-out because the old form has no
        /// defence.
        /// </summary>
        /// <summary>
        /// The part is ADHERED TO THE CAVITY while it cools, so its in-plane
        /// dimension is set by the mould and not by the polymer's own force and
        /// moment balance. True for a moulding; FALSE for a free quench, where
        /// the part is unconstrained from the first instant.
        ///
        /// This is a BOUNDARY CONDITION, not a coefficient, and it is the
        /// difference between reference case 3 and reference case 4. Setting it
        /// needs EjectionTimeS as well - the construction is meaningless without
        /// a release time, and a silent fallback would be the same
        /// looks-like-it-ran defect that RejectFlagsNotReadBy exists to stop.
        /// </summary>
        public bool MouldAdhesion = false;

        /// <summary>
        /// When the part leaves the cavity, seconds from the start of filling.
        /// NaN means unspecified, and MouldAdhesion then has nothing to act on.
        /// For a moulding this is the cycle time less the fill.
        /// </summary>
        public double EjectionTimeS = double.NaN;

        /// <summary>
        /// The PRESSURE-INDUCED DEVIATORIC term - a layer that vitrifies while
        /// adhered to the cavity is compressed with its in-plane strain pinned,
        /// which is an ANISOTROPIC stress state even though the pressure is
        /// hydrostatic. Wimberger-Friedl, Int. Polym. Process. 11(4) 373 (1996),
        /// Eqs (5)-(8); see Channels.PressureDeviatoricMPa.
        ///
        /// OFF BY DEFAULT. It is new physics with no registered clause of its own
        /// yet, and unlike the channels beside it, its ceiling is enormous - about
        /// 23x the measured surface birefringence at this model's own cavity
        /// pressure - so it is retention-limited and would be easy to tune into
        /// agreement. It ships behind -pressure-vitrification until a criterion
        /// exists that it can fail.
        ///
        /// MEASURED ON CASE 4, 2026-08-19, and the result localises the remaining
        /// work. With this term on, the gapwise average goes 3.43e-4 -> 2.39e-3,
        /// i.e. 3.98x the measured 6.0e-4, and clause (c) FAILS. That clause is
        /// one-sided precisely to catch "reaching the measurement with the wrong
        /// physics", and it was registered before this case was first run and
        /// before this term existed - so it caught a mechanism added days later,
        /// which is what a criterion registered in advance is for.
        ///
        /// AND A SECOND, INDEPENDENT REASON IT OVER-PREDICTS, from the literature
        /// rather than from this model. The stress-optical rule has a measured
        /// validity ceiling: Luap, Karlina, Schweizer & Venerus, Rheol. Acta
        /// (2005), find it holds for monodisperse PS melts up to a critical
        /// stress of about 2.7 MPa and fails above it, with polydispersity
        /// LOWERING that ceiling. This term applies the rule at a deviatoric
        /// stress of ~33 MPa - about 12x beyond where anyone has shown it valid.
        ///
        /// So even with a correct retention model the conversion itself would be
        /// extrapolated far outside its measured range here. That is a separate
        /// defect from the retention one and it is not fixed by the C_t work.
        /// (The 2.7 MPa is measured on PS, not PC, so treat it as an order of
        /// magnitude rather than a threshold for this material.)
        ///
        /// The algebra is not what is wrong: the deviatoric stress is exact
        /// against the source's Eqs (5)-(8) and the self-tests reproduce its own
        /// stated values. What is wrong is the RETENTION. The term is driven here
        /// by the flow channel's Maxwell memory factor, and the source is explicit
        /// that the retention of a transient pressure stress is governed by the
        /// optical-memory function C_t = C_g + C_m[1 - exp(-(t/tau)^beta)], its
        /// Eq (4), with beta = 0.72 for PC. Adopting that is a separate and larger
        /// change - it replaces this model's melt/glassy coefficient SPLIT with a
        /// single time-dependent coefficient - and was deliberately left out of
        /// the batch that added this term.
        /// </summary>
        public bool PressureVitrification = false;

        /// <summary>
        /// FROZEN-IN THERMAL ORIENTATION, opt-in and OFF by default. Wired
        /// 2026-08-21; the machinery had been built and self-tested since
        /// 2026-08-19 and left unconnected.
        ///
        /// It feeds the OUT-OF-PLANE channel only, and that is physics rather
        /// than convenience: cooling-induced orientation in a plate is
        /// equibiaxial in the plane, so for light down z the in-plane difference
        /// is identically zero - the same argument that (correctly) keeps thermal
        /// STRESS out of the in-plane clause. It therefore cannot close case 2's
        /// in-plane failure, and anyone reaching for it to do that should stop.
        ///
        /// WHY IT IS OFF BY DEFAULT, measured 2026-08-21. The retention it
        /// computes, f = 1 - exp(-(D/tau)^beta), rises from 0.05 to 0.95 between
        /// 138 C and 149 C for a 10 s window - and the measured tau(T) it uses is
        /// stated valid by its own source only ABOVE about 148 C, because below
        /// that the volumetric glass transition is passed and the real shift
        /// factors fall below the WLF line. TEN of those ELEVEN degrees lie below
        /// the floor. Above 152 C the retention saturates at 1.0000 and carries
        /// no depth information at all.
        ///
        /// So the channel discriminates in exactly one narrow window, and that
        /// window sits almost entirely outside its law's stated validity, in a
        /// KNOWN direction: WLF over-estimates tau there, so this under-estimates
        /// retention. It is wired so it can be measured rather than argued about;
        /// it is not on, because a channel whose every discriminating value comes
        /// from an invalid extrapolation must not silently enter a shipped run.
        /// </summary>
        public bool ThermalOrientation = false;

        /// <summary>
        /// CAVITY PRESSURE AT CHANGE-OVER, MPa. NaN means unspecified, and the
        /// field then carries only the filling pressure drop, as it always has.
        ///
        /// WHY THIS EXISTS. `FillField` integrates dp/ds back from a melt front
        /// at zero gauge pressure, so `P` is the pressure needed to PUSH THE
        /// FRONT - the Hele-Shaw filling drop, and nothing else. That is the
        /// right quantity while the cavity is still filling and it is the wrong
        /// one the instant the cavity is full: at change-over the machine
        /// switches from speed control to pressure control and the whole cavity
        /// is compressed, which the model had no representation of at all.
        ///
        /// Measured 2026-08-19 on reference case 4: the model's peak cavity
        /// pressure reads 23.1 MPa where the source's own transducer trace peaks
        /// near 80 MPa (Wimberger-Friedl 1991 thesis, ch. 3.3 Fig. 9), a factor
        /// of 3.5 - and that was with NO packing stage, so the whole of it is
        /// change-over compression.
        ///
        /// IT IS ADDED UNIFORMLY, AND THAT IS THE POINT. Compression of a full
        /// cavity is hydrostatic, so it raises P everywhere and leaves dp/ds -
        /// and therefore the wall shear stress and the whole flow channel -
        /// untouched. Anything that changed tau here would be adding a flow that
        /// is not happening.
        ///
        /// WHAT READS IT: the pressure-vitrification term, whose deviatoric
        /// stress is p*(1-2nu)/(1-nu) and which was previously driven by a
        /// pressure 3.5x too low.
        /// </summary>
        public double ChangeoverPressureMPa = double.NaN;

        /// <summary>
        /// CAVITY PRESSURE AS A FUNCTION OF TIME - the input that every previous
        /// attempt at the pressure mechanism was missing.
        ///
        /// Null means unavailable, and the convolution then refuses rather than
        /// inventing a shape. Paired arrays: PressureHistoryS is seconds from the
        /// start of filling, PressureHistoryMPa the cavity pressure there.
        ///
        /// WHY A HISTORY AND NOT A PEAK. The generalized stress-optical rule
        /// integrates over stress INCREMENTS - Eq (3) of Wimberger-Friedl,
        /// Int. Polym. Process. 11(4) 373 (1996) - so what a layer freezes in
        /// depends on when the stress arrived relative to its own vitrification,
        /// and on whether it went away again before then. A peak value cannot
        /// express either. Concretely: the same 80 MPa leaves a large frozen
        /// birefringence in a layer that vitrifies while it acts, and NOTHING in
        /// a layer that vitrifies after it has decayed, because the rise and the
        /// fall cancel.
        /// </summary>
        public double[] PressureHistoryS;
        public double[] PressureHistoryMPa;

        public bool HasPressureHistory
        {
            get
            {
                return PressureHistoryS != null && PressureHistoryMPa != null
                    && PressureHistoryS.Length >= 2
                    && PressureHistoryS.Length == PressureHistoryMPa.Length;
            }
        }

        /// <summary>
        /// HOW MANY POINTS THE COOLING CURVE IS RECORDED AT.
        ///
        /// This was `const int nt = 240` inside FreezeHistory, and being a
        /// constant it did not refine with nz - so an 8x refinement of the SPACE
        /// grid refined the TIME axis by exactly nothing, and every convergence
        /// sweep this project ever ran in nz alone was blind to it.
        ///
        /// It is not a detail. Every layer's vitrification instant is snapped to
        /// the nearest recorded time, so the time grid sets how precisely the
        /// solid set is known - and reference case 1's registered verdict flips
        /// from MET to NOT met between nz 81 and 161 because of it, while the
        /// depth ratio scatters 3.43 / 2.24 / 2.80. Refining this instead makes
        /// that ratio flat and the failure disappear.
        ///
        /// Exposed 2026-08-20 so convergence could be taken in the (nz, nt) plane
        /// rather than along one axis, and RAISED TO 960 the same day because the
        /// sweep settled it. Measured on reference case 1, depth ratio and verdict:
        ///
        ///                 nt=240      nt=480      nt=960
        ///     nz= 41    3.43 MET    3.42 MET    3.42 MET
        ///     nz= 81    2.24 MET    3.27 MET    3.33 MET
        ///     nz=161    2.80 FAIL   3.32 MET    3.38 MET
        ///
        /// At 240 the ratio scatters 3.43/2.24/2.80 and the registered criterion
        /// FLIPS. At 960 it is 3.42/3.33/3.38, flat to 1.5%, and MET at every
        /// grid. The old default was getting the right answer at nz=41 by luck.
        ///
        /// It costs almost nothing: case 1 goes 22.3 s -> 24.6 s, case 3 goes
        /// 79.4 s -> 80.5 s. There was never a performance reason for 240.
        ///
        /// It DOES move published numbers - case 3's shape ratio goes 2.64 -> 3.07
        /// - and those are corrected rather than kept. A number that changes when
        /// the grid is made adequate was never the model's answer.
        /// </summary>
        public int TimeSamples = 960;

        public bool NormalStressDifference = true;

        public bool ChannelNarrowing = false;
    }

    /// <summary>
    /// A1 - the pressure and shear field, from the cavity profile OpticStudio
    /// already holds.
    ///
    /// Lubrication (Hele-Shaw) flow along the path the melt takes from the gate.
    /// For a gap h and a flow-front width W carrying volumetric flow Q:
    ///
    ///     dp/ds = 12 * eta * Q / (W * h^3)              (Newtonian slit)
    ///     tau(z) = |dp/ds| * z                          (z from the mid-plane)
    ///     tau_wall = |dp/ds| * h/2
    ///
    /// The shear stress is LINEAR in z and vanishes at the mid-plane, which is
    /// what puts the frozen-in birefringence peak away from the centre. Nothing
    /// here needs a mesh: h(s) is evaluated from the sag equations.
    ///
    /// Viscosity is Cross-WLF, evaluated at the representative wall shear rate.
    /// A Newtonian mode exists so the stage can be held against Poiseuille flow,
    /// which is the control that must pass before A2 is allowed to use any of it.
    /// </summary>
    internal sealed class FillField
    {
        public double[] S;            // distance from the gate along the flow path, mm
        public double[] H;            // local cavity gap, mm
        public double[] DpDs;         // pressure gradient magnitude, MPa/mm
        public double[] P;            // pressure, MPa, zero at the far end
        public double[] Width;        // flow-front width, mm
        public double EtaPaS;         // viscosity actually used
        public double FlowRateMm3PerS;

        /// <summary>
        /// Smallest radius the converging-flow solution is evaluated at.
        ///
        /// Radial flow into a point is a genuine log singularity: dp/ds goes as
        /// 1/r, so integrating to r = 0 returns a pressure set by the node
        /// spacing rather than by the physics. Caught by this stage's own control
        /// on the first run, which returned 376 MPa where the log law gives 1.14.
        /// Lubrication theory is invalid once the radius is comparable with the
        /// gap anyway, so the floor is half the local gap - a physical bound, not
        /// a numerical fudge - or one node spacing, whichever is larger.
        /// </summary>
        public double RadiusFloorMm;
        /// <summary>The uniform compression added at change-over, MPa; zero when
        /// the field carries only the filling drop.</summary>
        public double ChangeoverPressureMPa;

        /// <summary>
        /// The cavity-gap floor this field was solved with, where it came from,
        /// and how much of the flow path it actually set.
        ///
        /// The last of the three is the one worth printing. dp/ds goes as 1/h^3,
        /// so a floor binding over most of the path means the in-plane field is
        /// being set by the flange rather than by the lens - which is fine, real
        /// parts are like that, but it should not be invisible. A floor that
        /// never binds is a number that changed nothing.
        /// </summary>
        public double FloorMm;
        public bool FloorIsAssumed;
        public int FloorBoundNodes;
        public double FloorBoundFraction;

        public double PathLengthMm { get { return S[S.Length - 1]; } }

        /// <summary>
        /// Cross-WLF. Returns Pa.s for shear rate in 1/s, temperature in C and
        /// pressure in Pa.
        /// </summary>
        public static double CrossWlf(Polymer p, double shearRate1PerS, double tempC, double pressurePa)
        {
            double T = tempC + 273.15;
            double tStar = p.WlfD2K + p.WlfD3KPerPa * pressurePa;
            double a2 = p.WlfA2K + p.WlfD3KPerPa * pressurePa;
            double dT = T - tStar;
            if (a2 + dT <= 1e-9) return 1e12;                    // below Tg: solid
            double eta0 = p.WlfD1PaS * Math.Exp(-p.WlfA1 * dT / (a2 + dT));
            if (shearRate1PerS <= 0) return eta0;
            double x = eta0 * shearRate1PerS / p.CrossTauStarPa;
            return eta0 / (1.0 + Math.Pow(x, 1.0 - p.CrossN));
        }

        /// <summary>
        /// Build the field for one element. <paramref name="newtonianEtaPaS"/>
        /// forces a constant viscosity, which is what the Poiseuille control uses.
        /// </summary>
        public static FillField Build(MouldedElement e, Polymer p, Process proc,
                                      int nodes = 101, double newtonianEtaPaS = double.NaN)
        {
            if (nodes < 3) throw new ArgumentException("need at least 3 nodes");
            double melt = double.IsNaN(proc.MeltTempC) ? p.MeltTempC : proc.MeltTempC;

            var f = new FillField
            {
                S = new double[nodes], H = new double[nodes],
                DpDs = new double[nodes], P = new double[nodes], Width = new double[nodes],
            };

            // Lubrication theory stops being valid once the radius is comparable
            // with the gap, so a converging path ENDS there rather than carrying a
            // constant, enormous gradient into the centre. Continuing past it was
            // the second thing this stage's control caught: it added a plug term
            // worth 43% of the total pressure, on top of the singularity itself.
            double radiusFloor = 0.5 * e.ThicknessAt(0.0);

            // Flow path: from the gate at the rim, across the part, to the far rim.
            // Ring gates converge on the centre, so their path is one radius.
            double pathLen;
            if (e.Gate.Kind == GateKind.RingAllRound)
                pathLen = Math.Max(e.SemiDiameterMm - radiusFloor, 1e-3);
            else
                pathLen = 2.0 * e.SemiDiameterMm;   // rim to rim, film or point

            // Cavity volume, by revolving the gap profile.
            double vol = 0.0;
            int nv = 200;
            for (int i = 0; i < nv; i++)
            {
                double r0 = e.SemiDiameterMm * i / nv, r1 = e.SemiDiameterMm * (i + 1) / nv;
                double rm = 0.5 * (r0 + r1);
                vol += e.ThicknessAt(rm) * Math.PI * (r1 * r1 - r0 * r0);
            }
            f.FlowRateMm3PerS = vol / Math.Max(proc.FillTimeS, 1e-6);

            f.RadiusFloorMm = radiusFloor;

            for (int i = 0; i < nodes; i++)
            {
                double s = pathLen * i / (nodes - 1.0);
                f.S[i] = s;

                // Radius reached, and the width of the advancing front there.
                double r, w;
                if (e.Gate.Kind == GateKind.FilmEdge)
                {
                    // A film gate spans one whole edge, so the front is a
                    // straight line of constant width travelling across the part.
                    // No convergence, no fan: the width is the gate's own.
                    r = Math.Abs(e.SemiDiameterMm - s);
                    w = e.Gate.WidthMm;
                }
                else if (e.Gate.Kind == GateKind.RingAllRound)
                {
                    r = Math.Max(e.SemiDiameterMm - s, f.RadiusFloorMm);
                    w = 2.0 * Math.PI * r;                     // converging annulus
                }
                else
                {
                    // A point gate on the rim: the front fans out to the part's
                    // width at the half-way chord and closes again.
                    r = Math.Abs(e.SemiDiameterMm - s);
                    w = 2.0 * Math.Sqrt(Math.Max(e.SemiDiameterMm * e.SemiDiameterMm - r * r, 1e-12));
                    // A FAN GATE RUNS THE SAME LAW; WHAT DIFFERS IS THE WIDTH.
                    //
                    // FanEdge falls here deliberately rather than getting a
                    // branch of its own. Once the melt is inside the cavity the
                    // CAVITY sets the front shape, and a fan does not change the
                    // cavity - it widens in the runner, so the front enters
                    // already spread instead of at the land. The chord law below
                    // is near zero at s = 0 and is floored at the gate width, so
                    // that entry width is exactly what the floor carries, and a
                    // wider gate lowers the shear rate over the first stretch and
                    // leaves the far field alone. Giving the fan its own formula
                    // here would be inventing physics the cavity does not have.
                    w = Math.Max(w, e.Gate.WidthMm);
                }
                f.Width[i] = w;
                // FLOOR THE CAVITY AT THE GATE LAND.
                //
                // Taking the sagitta out to the full semi-diameter gives a lens a
                // knife rim - 0.273 mm on the 32 mm plano-convex of reference case
                // 2 - and dp/ds goes as 1/h^3, so the whole in-plane field is then
                // set by a rim that does not exist. Measured: in-plane peak 6.60x
                // the published value, with the maximum sitting on that rim.
                //
                // A part cannot be thinner than the gate that feeds it. The model
                // already depends on that rule elsewhere - tGateSeal assumes the
                // gate land freezes BEFORE the wall, which inverts if the wall is
                // thinner - and it is how real moulded optics are built: a lens
                // carries a flange at least as thick as its gate. The source for
                // reference case 2 confirms it independently, since a 0.4 mm
                // one-sided cut was made near the gate and a 0.273 mm rim cannot
                // survive one.
                //
                // This binds only where the sagitta would thin a rim below the
                // gate land. A constant-thickness part is untouched.
                //
                // SINCE 2026-08-29 the floor is the DECLARED flange when there is
                // one, and the gate land only as a fallback. Identical arithmetic
                // when nothing is declared, so no published number moves - what
                // changes is that the run now says WHICH of the two it used and
                // over how much of the path it binds. A floor that never applies
                // and a floor carrying the whole field look the same in the
                // output otherwise, and only one of them is an assumption the
                // reader needs to know about.
                double hGeom = e.ThicknessAt(Math.Min(r, e.SemiDiameterMm));
                double hFloor = e.EffectiveFloorMm;
                if (hFloor > hGeom + 1e-12) f.FloorBoundNodes++;
                f.H[i] = Math.Max(Math.Max(hGeom, hFloor), 1e-4);
            }
            f.FloorMm = e.EffectiveFloorMm;
            f.FloorIsAssumed = e.FloorIsAssumed;
            f.FloorBoundFraction = (double)f.FloorBoundNodes / nodes;

            // Wall shear rate for the viscosity: 6Q/(W h^2) for a slit.
            double hMean = 0.0, wMean = 0.0;
            for (int i = 0; i < nodes; i++) { hMean += f.H[i]; wMean += f.Width[i]; }
            hMean /= nodes; wMean /= nodes;
            double gammaDot = 6.0 * f.FlowRateMm3PerS / Math.Max(wMean * hMean * hMean, 1e-12);

            f.EtaPaS = double.IsNaN(newtonianEtaPaS)
                ? CrossWlf(p, gammaDot, melt, proc.PackPressureMPa * 1e6)
                : newtonianEtaPaS;

            // dp/ds in MPa/mm.  eta [Pa.s] * Q [mm^3/s] / (W [mm] * h^3 [mm^3])
            // gives Pa/mm; divide by 1e6 for MPa/mm.
            for (int i = 0; i < nodes; i++)
                f.DpDs[i] = 12.0 * f.EtaPaS * f.FlowRateMm3PerS
                            / (f.Width[i] * f.H[i] * f.H[i] * f.H[i]) / 1e6;

            // Integrate back from the far end, where the melt front is at zero
            // gauge pressure. The gate therefore carries the highest pressure,
            // which is what makes the near-gate region pack hardest.
            f.P[nodes - 1] = 0.0;
            for (int i = nodes - 2; i >= 0; i--)
                f.P[i] = f.P[i + 1] + 0.5 * (f.DpDs[i] + f.DpDs[i + 1]) * (f.S[i + 1] - f.S[i]);

            // CHANGE-OVER COMPRESSION, added uniformly on top of the filling
            // drop. Hydrostatic, so DpDs is deliberately untouched - the shear
            // stress must not move, because no extra flow is happening.
            if (!double.IsNaN(proc.ChangeoverPressureMPa) && proc.ChangeoverPressureMPa > 0.0)
            {
                f.ChangeoverPressureMPa = proc.ChangeoverPressureMPa;
                for (int i = 0; i < nodes; i++) f.P[i] += proc.ChangeoverPressureMPa;
            }

            return f;
        }

        /// <summary>Shear stress at height z above the mid-plane, MPa.</summary>
        public double ShearAt(int node, double zFromMidPlaneMm)
        {
            return DpDs[node] * Math.Abs(zFromMidPlaneMm);
        }

        /// <summary>
        /// CONTROL: a constant-thickness, constant-width slit under constant
        /// viscosity is exactly Poiseuille, so the computed pressure drop must
        /// equal 12*eta*Q*L/(W h^3) with no fitted anything.
        /// </summary>
        public static void SelfCheck()
        {
            Console.WriteLine("  A1 pressure and shear field");

            // A plate makes h constant; a ring gate makes the width analytic.
            var plate = new MouldedElement
            {
                FrontSurface = 1, CentreThicknessMm = 2.0, SemiDiameterMm = 10.0,
                FrontRadiusMm = 0, BackRadiusMm = 0,
            };
            plate.EdgeThicknessMm = plate.ThicknessAt(plate.SemiDiameterMm);
            plate.Gate = new GateSpec { Kind = GateKind.RingAllRound, AzimuthDeg = 0, WidthMm = 1, ThicknessMm = 1 };
            var pmma = Polymers.ByName("MS_PMMA");
            var proc = new Process { FillTimeS = 0.5 };

            double eta = 500.0;   // Pa.s, fixed so the control has a closed form
            var f = FillField.Build(plate, pmma, proc, 2001, eta);

            // Closed form, integrated over the converging annulus:
            //   dp/ds = 12 eta Q / (2 pi r h^3),  r = R - s
            //   dP    = 12 eta Q / (2 pi h^3) * ln(R / r_min)
            double R = plate.SemiDiameterMm;
            double h = 2.0;
            // The floor is read back off the field, not recomputed here: a control
            // that re-derives the model's own choice can agree with a wrong one.
            double want = 12.0 * eta * f.FlowRateMm3PerS
                          / (2.0 * Math.PI * h * h * h) * Math.Log(R / f.RadiusFloorMm) / 1e6;
            SelfTest.Near("radial Hele-Shaw against the analytic log law", f.P[0], want, 2e-3);

            // Linear-slit control: fix the width by using an edge gate on a very
            // wide part is messy, so check the local relation instead - dp/ds must
            // be exactly 12 eta Q /(W h^3) at every node, which is Poiseuille.
            int mid = f.S.Length / 2;
            double local = 12.0 * eta * f.FlowRateMm3PerS
                           / (f.Width[mid] * Math.Pow(f.H[mid], 3)) / 1e6;
            SelfTest.Near("local gradient is Poiseuille", f.DpDs[mid], local, 1e-12);

            // Shear stress must be linear in z and zero on the mid-plane.
            SelfTest.Near("shear vanishes at the mid-plane", f.ShearAt(mid, 0.0), 0.0, 1e-12);
            SelfTest.Near("shear is linear in z",
                f.ShearAt(mid, 0.5), 0.5 * f.ShearAt(mid, 1.0), 1e-12);

            // CHANGE-OVER COMPRESSION: it must raise P by exactly the amount
            // asked, everywhere, and must NOT move the shear stress. Both arms,
            // because a term that raised dp/ds would be inventing a flow.
            {
                // THE CONTROL MUST DIFFER IN THE VARIABLE AND NOTHING ELSE.
                // First attempt built a FRESH Process for the control instead of
                // reusing the one `f` was built from, so it also changed
                // PackPressureMPa (60 -> 0) - and three of the four assertions
                // failed on a difference that had nothing to do with change-over.
                // The tell was the null failing too: "unspecified leaves the
                // field identical" cannot fail unless the control is wrong.
                double savedChange = proc.ChangeoverPressureMPa;
                proc.ChangeoverPressureMPa = 40.0;
                // `eta` is NOT optional here. f was built as
                // Build(plate, pmma, proc, 2001, eta) with an explicit viscosity
                // override; controls that omitted that fifth argument silently
                // fell back to Cross-WLF and differed in dp/ds by 1.55x. The
                // first repair guessed at the Process and changed nothing -
                // identical failures across both attempts, which is the signal
                // that the variable under suspicion was not the one differing.
                var fc = Build(plate, pmma, proc, f.S.Length, eta);
                proc.ChangeoverPressureMPa = savedChange;
                var fn = Build(plate, pmma, proc, f.S.Length, eta);

                int m2 = f.S.Length / 2;
                SelfTest.Near("change-over raises cavity pressure by exactly its value",
                    fc.P[m2] - f.P[m2], 40.0, 1e-9);
                SelfTest.Near("change-over raises it at the FRONT too (hydrostatic)",
                    fc.P[f.S.Length - 1] - f.P[f.S.Length - 1], 40.0, 1e-9);
                SelfTest.Near("change-over leaves dp/ds untouched",
                    fc.DpDs[m2], f.DpDs[m2], 1e-12);
                SelfTest.Near("change-over leaves the wall shear stress untouched",
                    fc.ShearAt(m2, 1.0), f.ShearAt(m2, 1.0), 1e-12);
                SelfTest.Near("unspecified change-over leaves the field identical",
                    fn.P[m2], f.P[m2], 1e-12);
            }

            // Pressure must fall monotonically from gate to front.
            bool mono = true;
            for (int i = 1; i < f.P.Length; i++) if (f.P[i] > f.P[i - 1] + 1e-12) mono = false;
            SelfTest.Check("pressure falls monotonically from the gate", mono,
                string.Format("gate {0:F3} MPa, front {1:F3} MPa", f.P[0], f.P[f.P.Length - 1]));

            // Cross-WLF must be shear thinning and temperature thinning, or it is
            // not the model its name claims.
            double etaLowRate = CrossWlf(pmma, 1.0, 250, 0);
            double etaHighRate = CrossWlf(pmma, 1e4, 250, 0);
            double etaHot = CrossWlf(pmma, 1e3, 280, 0);
            double etaCool = CrossWlf(pmma, 1e3, 220, 0);
            SelfTest.Check("Cross-WLF shear thins", etaHighRate < etaLowRate,
                string.Format("{0:E3} Pa.s at 1e4 /s vs {1:E3} at 1 /s", etaHighRate, etaLowRate));
            SelfTest.Check("Cross-WLF thins with temperature", etaHot < etaCool,
                string.Format("{0:E3} Pa.s at 280 C vs {1:E3} at 220 C", etaHot, etaCool));
        }
    }
}

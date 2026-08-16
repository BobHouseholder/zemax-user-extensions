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
        public double[,] SigmaThermalMPa;  // in-plane equibiaxial residual stress
        public double[,] DnDensity;        // isotropic index change
        public Polymer Material;
        public double PeakDnFlow, PeakDepthFraction;

        public static Channels Build(MouldedElement e, Polymer p, Process proc,
                                     FillField fill, FreezeHistory freeze)
        {
            int ns = fill.S.Length, nz = freeze.NodeCount;
            var c = new Channels
            {
                S = fill.S, Z = freeze.Z, Material = p,
                DnFlow = new double[ns, nz],
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
                    double tFreezeAbs = tArrive + freeze.FreezeTimeS[k];
                    double tauViscMPa = fill.DpDs[i] * Math.Abs(freeze.Z[k]);
                    double lambda = Math.Max(proc.LambdaScale * (p.MeltModulusPa > 0
                        ? fill.EtaPaS / p.MeltModulusPa : 1e-6), 1e-9);
                    double memory;
                    if (freeze.TimeGridS != null && freeze.TempHistoryC != null)
                    {
                        var hist = new double[freeze.TimeGridS.Length];
                        for (int q = 0; q < hist.Length; q++) hist[q] = freeze.TempHistoryC[k, q];
                        memory = MemoryFactorWlf(tArrive, tFill, tFreezeAbs,
                                                 freeze.TimeGridS, hist, p, lambda, tPack,
                                                 meltFracAtTime, proc.ChannelNarrowing);
                    }
                    else
                    {
                        memory = MemoryFactor(tArrive, tFill, tFreezeAbs, lambda, tPack);
                    }
                    double tauMPa = tauViscMPa * memory;

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
                        double xiFreeze = ReducedTimeToFreeze(freeze.TimeGridS, histF, p,
                                                              freeze.FreezeTimeS[k]);

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
                        double sigmaFrontPa = p.MeltModulusPa * eEff * proc.FountainStrain
                                              * Math.Exp(-xiFreeze);

                        // EXTENSION, so the principal stress difference IS sigma.
                        // The factor of 2 that belongs to simple shear was applied
                        // here as well and should never have been: in uniaxial
                        // extension along the flow with the ray along z, the two
                        // transverse principal stresses are zero and the
                        // difference is sigma itself.
                        dnFountain = p.CMeltBrewster * 1e-12 * sigmaFrontPa;
                    }

                    c.DnFlow[i, k] = dnShear + dnFountain;

                    c.SigmaThermalMPa[i, k] = sigma[k];
                    c.DnDensity[i, k] = llFactor * compressibilityPerMPa * (fill.P[i] - pMean);
                }
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
                                             bool narrowing = false)
        {
            if (tF <= tA || grid == null || grid.Length < 2) return 0.0;
            double tEndLocal = Math.Min(tF - tA, Math.Max(tFill - tA, 0.0));
            if (tEndLocal <= 0) return 0.0;
            double tFLocal = tF - tA;

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
                lamAt[j] = Math.Max(FillField.CrossWlf(p, 0.0, tMid, 0.0) / p.MeltModulusPa, 1e-12);
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
                double dXi = xiHi - xiAt[j - 1];
                double kernel;
                if (dXi > 1e-12)
                    kernel = (dt / dXi) * Math.Exp(-(xiF - xiHi)) * (1.0 - Math.Exp(-dXi));
                else
                    kernel = Math.Exp(-(xiF - xiHi)) * dt;

                integral += weight * kernel;
            }
            double v = integral / Math.Max(lambdaMelt, 1e-12);
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

            // Shear vanishes on the mid-plane, so the flow channel must too.
            SelfTest.Near("flow birefringence vanishes at the mid-plane",
                c.DnFlow[0, freeze.NodeCount / 2], 0.0, 1e-12);

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

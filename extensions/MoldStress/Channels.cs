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
                    double tf = freeze.FreezeTimeS[k];
                    double history = tf <= tFill
                        ? tf / tFill
                        : Math.Exp(-(tf - tFill) / tPack);
                    double tauMPa = fill.DpDs[i] * Math.Abs(freeze.Z[k]) * history;

                    // Stress-optical rule in simple shear: the principal stress
                    // difference is 2*tau.
                    c.DnFlow[i, k] = 2.0 * p.CMeltBrewster * 1e-12 * (tauMPa * 1e6);

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

        public static void SelfCheck()
        {
            Console.WriteLine("  A3 three channels");

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
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, proc, 41);
            var c = Build(plate, p, proc, fill, freeze);

            // Shear vanishes on the mid-plane, so the flow channel must too.
            SelfTest.Near("flow birefringence vanishes at the mid-plane",
                c.DnFlow[0, 40 / 2], 0.0, 1e-12);

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

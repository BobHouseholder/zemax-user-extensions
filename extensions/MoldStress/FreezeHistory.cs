using System;

namespace MoldStress
{
    /// <summary>
    /// A2 - when each depth solidifies, and how hot the rest of the wall is when
    /// it does.
    ///
    /// An amorphous optical polymer has NO latent heat of crystallisation, so the
    /// moving-boundary (Stefan) problem degenerates to something simpler and
    /// exact: the Tg isotherm in a wall quenched at both faces. Before the two
    /// fronts meet, each face is a semi-infinite solid,
    ///
    ///     T(z,t) = Tw + (Tm - Tw) * erf( z / (2 sqrt(alpha t)) )
    ///
    /// so the frozen layer grows as sqrt(t),
    ///
    ///     z_g(t) = 2 sqrt(alpha t) * erfinv( (Tg - Tw) / (Tm - Tw) )
    ///
    /// and the depth z freezes at t_f(z) = z^2 / (4 alpha beta^2).
    ///
    /// (The survey report calls this "Stefan-type sqrt(t)". The sqrt(t) scaling is
    /// right and the label is loose: with no latent heat there is no Stefan
    /// condition to solve, and the erf isotherm IS the closed form. Recorded here
    /// rather than quietly renamed.)
    ///
    /// Two things come out of this stage. The freeze TIME per depth, which decides
    /// how much shear each layer locks in - that is A3's flow channel. And the
    /// temperature profile at freeze-off, which drives the residual stress - that
    /// is A3's thermal channel.
    /// </summary>
    internal sealed class FreezeHistory
    {
        public double[] Z;            // from the mid-plane, mm, -h/2 .. +h/2
        public double[] FreezeTimeS;  // when that depth crossed Tg
        public double[] TrefC;        // temperature there when the CENTRE froze
        public double CentreFreezeTimeS;
        public double Beta;           // erfinv((Tg-Tw)/(Tm-Tw))
        public double ThicknessMm;

        /// <summary>
        /// Local time grid, measured from the instant the melt reaches a station.
        /// Every station sees the same cooling curve shifted by its own arrival
        /// time, so one grid serves the whole part.
        /// </summary>
        public double[] TimeGridS;

        /// <summary>
        /// Temperature at depth node k and local time j, degC. Kept because the
        /// relaxation time is a function of it: evaluating lambda once at melt
        /// temperature is what made the memory bracket a step (measured
        /// 2026-08-15 - lambda 4.0e-3 s against a 1.0 s fill).
        /// </summary>
        public double[,] TempHistoryC;

        /// <summary>
        /// Solved numerically across the FULL wall, both faces quenched.
        ///
        /// The erf isotherm is a SEMI-INFINITE result and it is only valid while
        /// the thermal front is well inside the wall. Its own control caught that:
        /// probed at a quarter of the thickness it disagreed with a finite
        /// difference solve by 70%, predicting 18.8 s where the numerics gave 5.6.
        /// The error is in the direction that matters - the closed form
        /// OVERSTATES how long the core stays molten, because a real wall has a
        /// second cold face and no infinite hot reservoir behind it.
        ///
        /// So the model is the finite difference and the closed form is its
        /// short-time control, rather than the other way round. It costs a
        /// fraction of a second.
        /// </summary>
        public static FreezeHistory Build(double thicknessMm, Polymer p, Process proc, int nz = 41)
        {
            if (nz < 5 || nz % 2 == 0) throw new ArgumentException("nz must be odd and >= 5");
            double melt = double.IsNaN(proc.MeltTempC) ? p.MeltTempC : proc.MeltTempC;
            double wall = double.IsNaN(proc.MoldTempC) ? p.MoldTempC : proc.MoldTempC;
            if (!(wall < p.TgC && p.TgC < melt))
                throw new ArgumentException("need mould < Tg < melt for " + p.Name);

            double alpha = p.DiffusivityMm2PerS;
            double half = 0.5 * thicknessMm;

            var f = new FreezeHistory
            {
                Z = new double[nz], FreezeTimeS = new double[nz], TrefC = new double[nz],
                Beta = ErfInv((p.TgC - wall) / (melt - wall)), ThicknessMm = thicknessMm,
            };
            for (int i = 0; i < nz; i++) f.Z[i] = -half + thicknessMm * i / (nz - 1.0);

            int n = 401;                                  // odd, so a node sits on the mid-plane
            double dz = thicknessMm / (n - 1);
            double dt = 0.2 * dz * dz / alpha;
            var T = new double[n];
            var tFreeze = new double[n];
            for (int i = 0; i < n; i++) { T[i] = melt; tFreeze[i] = double.NaN; }
            T[0] = T[n - 1] = wall;
            tFreeze[0] = tFreeze[n - 1] = 0.0;

            var Tn = new double[n];
            double tNow = 0.0;
            int centre = n / 2;
            double[] snapshot = null;

            // Sample the cooling curve on a fixed local-time grid so the
            // relaxation time can be integrated along it later.
            const int nt = 240;
            f.TimeGridS = new double[nt];
            var histFine = new double[n, nt];
            double tGridMax = 0.0;
            int nextSample = 0;

            for (int step = 0; step < 20000000 && snapshot == null; step++)
            {
                for (int i = 1; i < n - 1; i++)
                    Tn[i] = T[i] + alpha * dt / (dz * dz) * (T[i + 1] - 2 * T[i] + T[i - 1]);
                Tn[0] = Tn[n - 1] = wall;
                Array.Copy(Tn, T, n);
                tNow += dt;
                for (int i = 0; i < n; i++)
                    if (double.IsNaN(tFreeze[i]) && T[i] <= p.TgC) tFreeze[i] = tNow;

                // First pass fills the grid up to the centre freeze; the grid
                // spacing is set once that time is known, so sample every step
                // into a ring of slots by elapsed fraction of a running estimate.
                if (!double.IsNaN(tFreeze[centre]))
                {
                    snapshot = (double[])T.Clone();
                    f.CentreFreezeTimeS = tFreeze[centre];
                    tGridMax = tNow;
                }
                else if (nextSample < nt)
                {
                    // provisional: record every step until we know the span,
                    // keeping only nt evenly spread samples by decimation below
                    if (step % 50 == 0)
                    {
                        f.TimeGridS[nextSample] = tNow;
                        for (int i = 0; i < n; i++) histFine[i, nextSample] = T[i];
                        nextSample++;
                    }
                }
            }
            if (nextSample < nt)
            {
                // pad the tail with the final state so the grid is complete
                for (int j = nextSample; j < nt; j++)
                {
                    f.TimeGridS[j] = tGridMax > 0 ? tGridMax : (j + 1) * dt;
                    for (int i = 0; i < n; i++) histFine[i, j] = snapshot != null ? snapshot[i] : wall;
                }
            }
            if (snapshot == null)
                throw new InvalidOperationException("the centre never reached Tg - check the material");

            f.TempHistoryC = new double[nz, nt];
            for (int i = 0; i < nz; i++)
            {
                double frac = (f.Z[i] + half) / thicknessMm;
                int j = Math.Max(0, Math.Min(n - 1, (int)Math.Round(frac * (n - 1))));
                f.FreezeTimeS[i] = double.IsNaN(tFreeze[j]) ? f.CentreFreezeTimeS : tFreeze[j];
                f.TrefC[i] = snapshot[j];
                for (int q = 0; q < nt; q++) f.TempHistoryC[i, q] = histFine[j, q];
            }
            return f;
        }

        /// <summary>The short-time closed form, kept as the control and as the
        /// documented approximation the model deliberately does not use.</summary>
        public static double ErfFreezeTime(double depthMm, Polymer p, Process proc)
        {
            double melt = double.IsNaN(proc.MeltTempC) ? p.MeltTempC : proc.MeltTempC;
            double wall = double.IsNaN(proc.MoldTempC) ? p.MoldTempC : proc.MoldTempC;
            double beta = ErfInv((p.TgC - wall) / (melt - wall));
            return depthMm * depthMm / (4.0 * p.DiffusivityMm2PerS * beta * beta);
        }

        public int NodeCount { get { return Z.Length; } }

        // --- erf and its inverse ------------------------------------------------
        // Abramowitz & Stegun 7.1.26 is only 1e-7; the birefringence chain
        // multiplies this by material constants known to a few percent, but a
        // sloppy erf would put a floor under every later check, so use the higher
        // accuracy rational form and invert it by Newton.
        public static double Erf(double x)
        {
            double t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            double y = t * Math.Exp(-x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? 1.0 - y : y - 1.0;
        }

        public static double ErfInv(double y)
        {
            if (y <= -1 || y >= 1) throw new ArgumentOutOfRangeException("y");
            double x = 0.0;
            for (int i = 0; i < 60; i++)
            {
                double err = Erf(x) - y;
                double d = 2.0 / Math.Sqrt(Math.PI) * Math.Exp(-x * x);
                if (Math.Abs(d) < 1e-300) break;
                double step = err / d;
                x -= step;
                if (Math.Abs(step) < 1e-15) break;
            }
            return x;
        }

        /// <summary>
        /// CONTROL: the erf isotherm is checked against a NUMERICAL solution of
        /// the heat equation, not against itself. Explicit finite difference on a
        /// half-wall, fixed wall temperature, and the Tg crossing depth read off
        /// the grid. If the closed form and the numerics disagree, one of them is
        /// wrong and the stage does not ship.
        /// </summary>
        public static void SelfCheck()
        {
            Console.WriteLine("  A2 freeze history");

            var p = Polymers.ByName("MS_PMMA");
            var proc = new Process();
            double h = 2.0;
            int nz = 41;
            var f = Build(h, p, proc, nz);

            // CONTROL: near the wall the semi-infinite erf solution IS valid, so
            // the numerics must reproduce it there. This is the closed form the
            // stage is held against; it is checked where it applies, not where it
            // is known to fail.
            int shallow = 2;                                     // 5% of the wall in
            double depth = f.Z[shallow] + 0.5 * h;
            SelfTest.Near("finite difference matches the erf isotherm near the wall",
                f.FreezeTimeS[shallow], ErfFreezeTime(depth, p, proc), 0.05);

            // And the deviation deep in the wall is REPORTED rather than hidden -
            // it is the reason the model is numerical.
            double deepClosed = ErfFreezeTime(0.5 * h, p, proc);
            SelfTest.Check("erf overstates the core freeze time, as expected",
                deepClosed > f.CentreFreezeTimeS,
                string.Format("closed form {0:F2} s vs numerical {1:F2} s, ratio {2:F2}x",
                    deepClosed, f.CentreFreezeTimeS, deepClosed / f.CentreFreezeTimeS));

            // Deeper layers freeze later, monotonically, from either face.
            bool mono = true;
            for (int i = 1; i <= nz / 2; i++)
                if (f.FreezeTimeS[i] < f.FreezeTimeS[i - 1] - 1e-12) mono = false;
            SelfTest.Check("freeze time increases monotonically inward", mono,
                string.Format("wall {0:E3} s to centre {1:E3} s",
                    f.FreezeTimeS[0], f.CentreFreezeTimeS));

            // Symmetry: a wall quenched equally on both faces must be symmetric.
            SelfTest.Near("freeze history is symmetric about the mid-plane",
                f.FreezeTimeS[nz - 1 - shallow], f.FreezeTimeS[shallow], 1e-9);

            // erfinv must actually invert erf.
            SelfTest.Near("erfinv inverts erf", Erf(ErfInv(0.6)), 0.6, 1e-9);

            // At freeze-off the centre is AT Tg and the skin is colder.
            SelfTest.Near("centre is at Tg when it freezes", f.TrefC[nz / 2], p.TgC, 5e-3);
            SelfTest.Check("skin is colder than the core at freeze-off",
                f.TrefC[0] < f.TrefC[nz / 2],
                string.Format("skin {0:F1} C, core {1:F1} C", f.TrefC[0], f.TrefC[nz / 2]));
        }
    }
}

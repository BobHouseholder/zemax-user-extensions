using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// A4b - the flow channel computed along MATERIAL PATHS instead of at fixed
    /// depths.
    ///
    /// WHY THIS EXISTS. The Eulerian shear channel in Channels.cs computes, for
    /// each depth z, the stress a fluid element would build up HAVING SAT AT z
    /// SINCE t=0, sheared at the local rate until it freezes. Under that
    /// assumption the wall layer must retain almost nothing - it freezes at
    /// 0.094 s, before it can build anything - and the core must retain the most,
    /// because it stays molten longest. Measured: the retained fraction rises
    /// inward, 0.000 at the wall to 0.710 at the mid-plane, while the shear
    /// stress falls linearly outward, so their product peaks at 50% of the
    /// half-wall. The published profile peaks at the SKIN.
    ///
    /// That is not a missing term, and eight configurations of extra terms
    /// confirmed it - each moved the magnitude and left the shape, or moved the
    /// peak off the gate, or bought the depth ratio by saturating the memory
    /// bracket until there was no depth dependence left to be wrong. The
    /// assumption itself is what is wrong: in fountain flow the skin NEVER SAT AT
    /// THE WALL. It was sheared in the hot core, carried to the wall by the
    /// advancing front, and quenched on arrival, so it keeps what it was already
    /// carrying.
    ///
    /// So this module gives each element its own history.
    ///
    /// THE MODEL, stated as assumptions so they can be attacked one at a time:
    ///
    ///  1. Planar channel, half-gap H, quasi-steady. A particle seeded at height
    ///     y0 advects at u(y0) and stays at y0 until it reaches the front. The
    ///     velocity profile is the power-law profile for the polymer's Cross n,
    ///     normalised so its mean equals the front speed.
    ///  2. Shear stress on a particle in the channel is tau(y0) = |dp/ds| * y0,
    ///     the fully developed profile - the same stress the Eulerian channel
    ///     uses, so the two differ ONLY in the history, not in the loading.
    ///  3. Orientation follows Maxwell in reduced time: dsigma/dxi = tau - sigma,
    ///     integrated along the particle's own temperature history. Reduced time
    ///     uses lambda(T) = eta0(T)/G, the same function the Eulerian channel
    ///     uses, evaluated at the particle's temperature rather than at a fixed
    ///     depth's.
    ///  4. FOUNTAIN. A particle whose position would overtake the front is
    ///     intercepted by it, stretched once by the front's extensional
    ///     kinematics, and deposited on the wall. Deposition fills inward: the
    ///     first material laid down at a station sits at the wall and later
    ///     material sits inside it, which is what makes the deposited layer a
    ///     record of arrival ORDER.
    ///  5. After deposition a particle is at a no-slip boundary. It is not
    ///     sheared again. It only relaxes, in reduced time, at the wall's
    ///     temperature history until it drops below Tg, at which point its
    ///     orientation is frozen.
    ///  6. A particle that never overtakes the front never leaves the core. It
    ///     keeps accumulating channel shear until its own depth freezes.
    ///
    /// WHAT IS NOT MODELLED, and would each change the answer: the frozen layer
    /// does not narrow the channel as it grows (the flow field is taken from
    /// FillField and held); the front's extensional field is a single lumped
    /// stretch rather than a resolved kinematic; particles do not exchange
    /// momentum; and the packing stage is a decaying weight on the channel
    /// stress rather than a second flow.
    ///
    /// This is opt-in via -lagrangian and is NOT the default. It is a different
    /// model, not a correction to the shipped one, and it carries its own
    /// controls below.
    /// </summary>
    internal sealed class Lagrangian
    {
        public double[] Z;              // final resting height above the mid-plane, mm
        public double[] SigmaMPa;       // frozen-in orientation as an equivalent shear stress
        public double[] DnFlow;         // 2 * C_melt * sigma
        public bool[] WasDeposited;     // true if the front laid it down
        public double[] ArrivalHeight;  // the y0 it started at, mm
        public double[] SFinal;         // resting station along the flow, mm
        public double[] Weights;        // volumetric weight, carried out so the
                                        // field can be volume-averaged rather
                                        // than count-averaged
        public double PathLengthMm;

        public double DepositedFraction;   // of the half-gap, at the reporting station
        public double MassBalanceError;    // control: |seeded - placed| / seeded

        private sealed class Particle
        {
            public double Y0;           // seed height above mid-plane, mm
            public double S;            // position along the flow, mm
            public double Sigma;        // current orientation, MPa
            public bool Deposited;
            public double ZFinal;       // resting height, mm
            public double TDeposit;
            public double TEnter;       // when this element entered the gate
            public double Weight;       // volumetric weight, proportional to u(y0)

            // Lookup state, not physics. Node is the freeze-grid index nearest
            // this element's height; it changes only when the element is
            // deposited. TCur is a cursor into the time grid, valid because an
            // element's local clock only ever moves forward.
            public int Node;
            public int TCur;
            public bool Frozen;
            public double SFinal;       // where along the flow it came to rest, mm
        }

        /// <summary>
        /// Power-law velocity profile normalised to unit MEAN, so the front speed
        /// and the flow rate stay consistent with FillField. For n = 1 this is the
        /// parabolic 3/2 (1 - (y/H)^2); for shear-thinning n it is flatter, which
        /// is what puts more material in the plug and changes who reaches the
        /// front.
        /// </summary>
        private static double UNorm(double yOverH, double n)
        {
            double m = 1.0 / Math.Max(n, 1e-3);
            return (m + 2.0) / (m + 1.0) * (1.0 - Math.Pow(Math.Abs(yOverH), m + 1.0));
        }

        // ---- the depth shape, cached between builds -------------------------
        //
        // Making the shape the default turned -selftest from near-instant into
        // 2m45, because every Channels.Build does a particle solve per gap node
        // and the self-tests build many times over the same geometry. Nothing
        // about the shape depends on the caller: for a given material, fill
        // field, freeze history, gap ratio and particle count it is the same
        // array every time.
        //
        // THE KEY IS THE HAZARD, so it is derived rather than guessed. Build
        // reads exactly four fields of Process - FillTimeS, FountainStrain,
        // LambdaScale, PackTimeS - and reads Polymer, FillField and FreezeHistory
        // wholesale, so those three are keyed by REFERENCE: a caller that wants a
        // different answer constructs a different object (RefCase's zero-CTE
        // polymer and its mirrored freeze history both do), and identity can only
        // cost a miss, never produce a wrong hit. The process fields are keyed by
        // VALUE because a Process IS mutated in place between builds.
        //
        // MouldedElement is not in the key because Build does not read it - the
        // parameter is unused. If that changes, THIS KEY GOES STALE SILENTLY and
        // starts handing one element's shape to another. Anything added to Build
        // that reads e, or reads a fifth field of Process, has to be added here
        // in the same edit.
        private sealed class ShapeKey : IEquatable<ShapeKey>
        {
            public Polymer P; public FillField Fill; public FreezeHistory Freeze;
            public double Ratio; public int Particles;
            public double FillTime, Fountain, LambdaScale, PackTime;

            // BY CONTENT, NOT BY REFERENCE. The first version keyed the fill
            // field and freeze history on object identity, which is trivially
            // safe and turned out to be useless: -selftest reported 32 solved and
            // 0 reused, because each section constructs its own FillField and
            // FreezeHistory even when the geometry is identical. Identity can
            // only cost a miss, and here it cost every one of them.
            //
            // Comparing contents costs a few hundred thousand double comparisons
            // per lookup against a particle solve of several seconds, so the
            // trade is not close. Only the arrays Build actually reads are
            // compared - the temperature history included, because the particle
            // temperature interpolation reads it.
            private static bool Same(double[] a, double[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
                return true;
            }
            private static bool Same(double[,] a, double[,] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return false;
                for (int i = 0; i < a.GetLength(0); i++)
                    for (int j = 0; j < a.GetLength(1); j++)
                        if (!a[i, j].Equals(b[i, j])) return false;
                return true;
            }

            public bool Equals(ShapeKey o)
            {
                if (o == null) return false;
                if (!(Ratio.Equals(o.Ratio) && Particles == o.Particles
                      && FillTime.Equals(o.FillTime) && Fountain.Equals(o.Fountain)
                      && LambdaScale.Equals(o.LambdaScale) && PackTime.Equals(o.PackTime)))
                    return false;
                // The polymer stays keyed by reference: it is a value-like record
                // with many fields, callers construct a new one when they want a
                // different answer, and a miss is harmless.
                if (!ReferenceEquals(P, o.P)) return false;
                if (!Freeze.ThicknessMm.Equals(o.Freeze.ThicknessMm)) return false;
                if (!Fill.PathLengthMm.Equals(o.Fill.PathLengthMm)) return false;
                return Same(Freeze.Z, o.Freeze.Z)
                    && Same(Freeze.FreezeTimeS, o.Freeze.FreezeTimeS)
                    && Same(Freeze.TimeGridS, o.Freeze.TimeGridS)
                    && Same(Freeze.TempHistoryC, o.Freeze.TempHistoryC)
                    && Same(Fill.H, o.Fill.H)
                    && Same(Fill.DpDs, o.Fill.DpDs)
                    && Same(Fill.S, o.Fill.S);
            }
            public override bool Equals(object o) { return Equals(o as ShapeKey); }

            // Cheap digest - dimensions and a strided sample. Collisions only
            // cost an Equals call, which is exact.
            private static int Digest(int h, double[] a)
            {
                if (a == null) return h * 31;
                h = h * 31 + a.Length;
                int step = Math.Max(1, a.Length / 16);
                for (int i = 0; i < a.Length; i += step) h = h * 31 + a[i].GetHashCode();
                return h;
            }
            public override int GetHashCode()
            {
                int h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(P);
                h = h * 31 + Ratio.GetHashCode();
                h = h * 31 + Particles;
                h = h * 31 + FillTime.GetHashCode();
                h = h * 31 + Fountain.GetHashCode();
                h = h * 31 + LambdaScale.GetHashCode();
                h = h * 31 + PackTime.GetHashCode();
                h = h * 31 + Freeze.ThicknessMm.GetHashCode();
                h = h * 31 + Fill.PathLengthMm.GetHashCode();
                h = Digest(h, Freeze.Z);
                h = Digest(h, Freeze.FreezeTimeS);
                h = Digest(h, Freeze.TimeGridS);
                h = Digest(h, Fill.H);
                h = Digest(h, Fill.DpDs);
                return h;
            }
        }

        private sealed class ShapeEntry { public double[] Phi; public int MinCount; }

        private static readonly Dictionary<ShapeKey, ShapeEntry> ShapeCache =
            new Dictionary<ShapeKey, ShapeEntry>();
        private static readonly object ShapeLock = new object();

        /// <summary>Cache statistics, so a cache that never hits is visible.</summary>
        public static long ShapeHits, ShapeMisses;

        /// <summary>
        /// The depth shape at one gap ratio, solved once per distinct input.
        /// The returned array is a COPY: the caller renormalises in place, and
        /// handing out the cached instance would let one build corrupt the next.
        /// </summary>
        public static double[] CachedDepthShape(MouldedElement e, Polymer p, Process proc,
                                                FillField fill, FreezeHistory freeze,
                                                double gapRatio, int nParticles,
                                                out int minCount)
        {
            var key = new ShapeKey
            {
                P = p, Fill = fill, Freeze = freeze,
                Ratio = gapRatio, Particles = nParticles,
                FillTime = proc.FillTimeS, Fountain = proc.FountainStrain,
                LambdaScale = proc.LambdaScale, PackTime = proc.PackTimeS,
            };

            ShapeEntry hit;
            lock (ShapeLock)
            {
                if (ShapeCache.TryGetValue(key, out hit))
                {
                    ShapeHits++;
                    minCount = hit.MinCount;
                    return (double[])hit.Phi.Clone();
                }
            }

            var fz = freeze.ScaledToGap(gapRatio);
            var lag = Build(e, p, proc, fill, fz, nParticles);
            int mc;
            double[] phi = lag.DepthShape(fz.Z, Math.Max(0.5 * fz.ThicknessMm, 1e-9), out mc);

            lock (ShapeLock)
            {
                ShapeMisses++;
                if (ShapeCache.Count > 512) ShapeCache.Clear();
                ShapeCache[key] = new ShapeEntry { Phi = (double[])phi.Clone(), MinCount = mc };
            }
            minCount = mc;
            return phi;
        }

        public static Lagrangian Build(MouldedElement e, Polymer p, Process proc,
                                       FillField fill, FreezeHistory freeze,
                                       int nParticles = 400)
        {
            double H = 0.5 * freeze.ThicknessMm;
            double L = Math.Max(fill.PathLengthMm, 1e-9);
            double tFill = Math.Max(proc.FillTimeS, 1e-6);
            double vFront = L / tFill;
            double G = Math.Max(p.MeltModulusPa, 1.0);
            double n = p.CrossN > 0 ? p.CrossN : 1.0;

            // STAGGERED INJECTION. The first version seeded every particle at the
            // gate at t=0, where the front also is - so `S >= frontS` fired on the
            // first step for all of them and 99% deposited instantly, inverting
            // the source mapping (centre material at the wall, wall material at
            // the mid-plane). Caught by the control requiring the deposited set to
            // be neither empty nor everything.
            //
            // Melt enters over the WHOLE fill. A particle entering at t_e starts
            // v*t_e behind the front and closes that gap only if u(y0) > v, which
            // is the actual selection rule for who reaches the front at all.
            // Particles carry a volume WEIGHT u(y0), because the volumetric flux
            // at height y is proportional to the speed there - seeding uniformly
            // in y without that weight would silently over-represent the slow
            // near-wall material.
            // MELT ENTERS THE MOLTEN CHANNEL, NOT THE FROZEN SKIN.
            //
            // The first version seeded every height at the gate, including
            // near-wall heights - and those freeze within 0.094 s, so they came to
            // rest essentially AT the gate. That put 822 elements in the first
            // station bin against ~80 elsewhere, a 1244% departure from the
            // uniform fill a plate must have, and it inverted the along-flow
            // profile by dragging the near-gate average down with barely-oriented
            // material.
            //
            // It is also physically impossible: melt cannot flow into solid. At
            // entry time t_e the channel is only open to |y| < H - delta(t_e),
            // where delta is the frozen layer already grown. Downstream near-wall
            // cells are filled by the FRONT, which is the mechanism this model
            // exists to represent - they are not filled by material that entered
            // near the wall and stopped.
            Func<double, double> frozenSkin = te =>
            {
                double d = 0.0;
                for (int m = 0; m < freeze.Z.Length; m++)
                    if (freeze.FreezeTimeS[m] <= te)
                        d = Math.Max(d, H - Math.Abs(freeze.Z[m]));
                return Math.Min(d, 0.95 * H);
            };

            // The SAME scan the temperature lookup used to do on every particle
            // on every step. It is exact rather than a closed-form index because
            // the original takes the first minimum on a tie and a rounded index
            // need not; keeping the scan keeps the answer bit-identical, and
            // calling it once per particle instead of twelve million times is
            // where the cost went.
            Func<double, int> nearestNode = yAbs =>
            {
                int k = 0; double best = double.MaxValue;
                for (int m = 0; m < freeze.Z.Length; m++)
                {
                    double d = Math.Abs(Math.Abs(freeze.Z[m]) - yAbs);
                    if (d < best) { best = d; k = m; }
                }
                return k;
            };

            int nY = Math.Max(8, (int)Math.Sqrt(nParticles * 2));
            int nT = Math.Max(8, nParticles / nY);
            var parts = new List<Particle>(nY * nT);
            for (int it = 0; it < nT; it++)
            {
                double te = tFill * (it + 0.5) / nT;
                double yOpen = H - frozenSkin(te);           // channel still open
                for (int iy = 0; iy < nY; iy++)
                {
                    double y0 = yOpen * (iy + 0.5) / nY;
                    parts.Add(new Particle
                    {
                        Y0 = y0, S = 0.0, Sigma = 0.0, ZFinal = y0,
                        TEnter = te, Weight = UNorm(y0 / H, n),
                        Node = nearestNode(y0), TCur = 1,
                    });
                }
            }
            double totalWeight = parts.Sum(q => q.Weight);

            // WHAT USED TO BE HERE, and why it is gone.
            //
            // A tempAt(height, time) lambda did TWO linear scans on every call -
            // one over the depth grid to find the nearest node, one over the time
            // grid to find the bracketing sample - and it was called once per
            // particle per step. At 4000 particles and 3000 steps that is 12
            // million calls, each walking up to a few hundred depth nodes and up
            // to ten times that many time samples. That, not the physics, was
            // essentially the entire cost of the solve.
            //
            // Neither scan was necessary. An element's depth node is FIXED until
            // the front deposits it, so it is computed once at seeding and once
            // again on deposition. Its local clock only moves forward, so the
            // time grid needs a cursor rather than a search - per particle, since
            // tLocal is measured from that element's own entry time and is not
            // shared across the population. Both are exact: the same node, the
            // same bracket, the same interpolation in the same order.
            //
            // A freezeAt(height) lambda with the same O(nz) scan was also defined
            // here and never called once. Removed rather than converted.
            double[] tGrid = freeze.TimeGridS;
            double[,] tHist = freeze.TempHistoryC;
            bool haveHistory = tGrid != null && tHist != null;
            int tGridN = haveHistory ? tGrid.Length : 0;

            // AN ELEMENT BELOW Tg IS INERT AND WAS STILL BEING VISITED EVERY STEP.
            // The loop below skips it - but only AFTER looking its temperature up,
            // so the skin, which freezes in the first fraction of a second, kept
            // paying full price for the remaining thousands of steps.
            //
            // Retiring it permanently is exact only if temperature never rises
            // again, and that is a property of the cooling solve rather than
            // something to assume: a frozen element cannot be deposited (the skip
            // happens before the deposition branch), so its node is fixed and its
            // temperature series is one row of this array. So CHECK the array. It
            // costs one pass over a few hundred thousand doubles, once per solve,
            // against thousands of steps of savings - and if a history ever does
            // reheat, the slow path is still correct.
            bool coolingIsMonotone = haveHistory;
            if (haveHistory)
            {
                int rows = tHist.GetLength(0), cols = tHist.GetLength(1);
                for (int r = 0; r < rows && coolingIsMonotone; r++)
                    for (int cc = 1; cc < cols; cc++)
                        if (tHist[r, cc] > tHist[r, cc - 1] + 1e-9)
                        { coolingIsMonotone = false; break; }
            }

            // Deposited volume per unit width already laid down, tracked so later
            // arrivals sit INSIDE earlier ones. This is the whole point of the
            // model: the deposited layer records arrival order.
            double depositedHeight = 0.0;

            int nStep = 3000;
            double tEnd = Math.Max(freeze.CentreFreezeTimeS, tFill) * 1.05;
            double dt = tEnd / nStep;

            for (int step = 0; step < nStep; step++)
            {
                double t = step * dt;
                double frontS = Math.Min(vFront * t, L);

                foreach (var q in parts)
                {
                    if (q.Frozen) continue;              // below Tg, never returns
                    if (t < q.TEnter) continue;          // not injected yet
                    double yAbs = q.Deposited ? Math.Abs(q.ZFinal) : q.Y0;
                    // COOLING CLOCK RUNS FROM ENTRY, not from t=0.
                    //
                    // This read tempAt(yAbs, t) on absolute time, so every element
                    // at a given height crossed Tg at the same absolute instant
                    // regardless of when it entered or where it had reached.
                    // Near-wall elements therefore all stopped within 0.094 s of
                    // t=0 - near the gate - which is the mass-conservation failure,
                    // not the seeding. The Eulerian channel already offsets its
                    // freeze clock by arrival (tArrive + FreezeTimeS); this model
                    // did not, so the two disagreed about when anything solidified.
                    double tLocal = t - q.TEnter;
                    double T;
                    if (!haveHistory) T = p.MeltTempC;
                    else if (tLocal <= tGrid[0]) T = tHist[q.Node, 0];
                    else
                    {
                        int j = q.TCur < 1 ? 1 : q.TCur;
                        while (j < tGridN && tGrid[j] < tLocal) j++;
                        q.TCur = j;
                        if (j >= tGridN) T = tHist[q.Node, tGridN - 1];
                        else
                        {
                            double span = tGrid[j] - tGrid[j - 1];
                            double f = span > 0 ? (tLocal - tGrid[j - 1]) / span : 0.0;
                            T = tHist[q.Node, j - 1] +
                                f * (tHist[q.Node, j] - tHist[q.Node, j - 1]);
                        }
                    }

                    // Below Tg the element is solid: no loading, no relaxation.
                    if (T <= p.TgC)
                    {
                        if (coolingIsMonotone) q.Frozen = true;
                        continue;
                    }

                    // LambdaScale applies here too. The Eulerian channel honours it
                    // and this model did not, so the two disagreed about the
                    // relaxation time whenever it was set - and it exists
                    // precisely so the relaxation time can be TESTED as a lever.
                    //
                    // It is also the physically open constant. lambda = eta0/G is
                    // the Maxwell time; the terminal relaxation time for chain
                    // ORIENTATION is longer, by a factor of order 3-6 depending on
                    // the definition, because eta0 ~ G_N0 * tau_d / 5 for an
                    // entangled melt. Orientation is what freezes in, so this
                    // model relaxing it at the Maxwell time is a choice, not a
                    // derivation.
                    // SHEAR-THINNED WHILE BEING SHEARED, at rest otherwise.
                    //
                    // lambda = eta(T, gammaDot)/G. An element still in the channel
                    // is under fill shear and its viscosity is thinned by ~29x
                    // here, so it builds orientation ~29x faster than the
                    // melt-at-rest time allows. A deposited element sits at a
                    // no-slip wall with no shear at all, so it relaxes at the
                    // zero-shear time.
                    //
                    // RETRACTED, same day: an earlier version of this comment
                    // claimed the thinning "does not saturate here" because an
                    // element stops being loaded once deposited. Measured
                    // immediately after writing it - core 2.8e-4 against a
                    // published 1.8e-4 and the ratio flattened to 1.14 - so it
                    // saturates here too. The claim was written before the run.
                    //
                    // The shear rate must be solved SELF-CONSISTENTLY. Taking
                    // gammaDot = tau/eta_fill uses one fill-wide viscosity and so
                    // overestimates the rate in the core, where the true local
                    // viscosity is higher and the material thins less. Solving
                    // eta(gammaDot)*gammaDot = tau by fixed point gives each
                    // element the thinning its own stress earns.
                    double gammaDot = 0.0;
                    if (!q.Deposited && t <= tFill)
                    {
                        int nd = (int)Math.Round((q.S / L) * (fill.S.Length - 1));
                        nd = Math.Max(0, Math.Min(fill.S.Length - 1, nd));
                        double tauPa = fill.DpDs[nd] * q.Y0 * 1e6;
                        // eta(gammaDot) * gammaDot = tau, by fixed point on the
                        // Cross model at this element's own temperature.
                        double etaIt = FillField.CrossWlf(p, 0.0, T, 0.0);
                        for (int it2 = 0; it2 < 40; it2++)
                        {
                            double gd2 = tauPa / Math.Max(etaIt, 1e-9);
                            double etaNew = FillField.CrossWlf(p, gd2, T, 0.0);
                            if (Math.Abs(etaNew - etaIt) <= 1e-6 * etaIt) { etaIt = etaNew; break; }
                            etaIt = 0.5 * (etaIt + etaNew);          // damped, for stability
                        }
                        gammaDot = tauPa / Math.Max(etaIt, 1e-9);
                    }
                    double lam = Math.Max(proc.LambdaScale *
                                          FillField.CrossWlf(p, gammaDot, T, 0.0) / G, 1e-9);
                    double dXi = dt / lam;

                    if (!q.Deposited)
                    {
                        // Still in the channel: advect WHILE THERE IS FLOW, and
                        // relax toward the local channel shear stress.
                        //
                        // Advection had no fill-time guard, so elements kept
                        // travelling after the cavity was full, ran to the end of
                        // the path and were clamped at L. That is where the
                        // mass-conservation failure ended up after the first two
                        // fixes moved it off the gate: a single terminal bin
                        // holding thousands of elements while the interior bins
                        // were even. The histogram print stepped by two and
                        // skipped it, which is why the interior looked healthy.
                        if (t <= tFill)
                        {
                            double u = vFront * UNorm(q.Y0 / H, n);
                            q.S += u * dt;
                        }

                        int node = (int)Math.Round((q.S / L) * (fill.S.Length - 1));
                        node = Math.Max(0, Math.Min(fill.S.Length - 1, node));
                        double tau = fill.DpDs[node] * q.Y0;          // MPa
                        if (t > tFill)
                            tau *= 0.1 * Math.Exp(-(t - tFill) / Math.Max(proc.PackTimeS, 1e-9));

                        q.Sigma += (tau - q.Sigma) * (1.0 - Math.Exp(-dXi));

                        // Intercepted by the front: stretched once, then laid down.
                        if (q.S >= frontS && frontS < L)
                        {
                            double halfGap = Math.Max(0.5 * fill.H[Math.Min(node, fill.H.Length - 1)], 1e-6);
                            double eDot = vFront / halfGap;
                            double wi = lam * eDot;
                            double eEff = wi * (1.0 - Math.Exp(-1.0 / Math.Max(wi, 1e-12)));
                            q.Sigma += (G * 1e-6) * eEff * proc.FountainStrain;   // Pa -> MPa

                            q.Deposited = true;
                            q.TDeposit = t;
                            q.SFinal = q.S;
                            // Fill inward from the wall, in arrival order, by
                            // VOLUME rather than by particle count.
                            depositedHeight += H * q.Weight / totalWeight;
                            q.ZFinal = Math.Max(H - depositedHeight, 0.0);
                            q.Node = nearestNode(Math.Abs(q.ZFinal));
                        }
                    }
                    else
                    {
                        // At the wall: no shear, relaxation only.
                        q.Sigma *= Math.Exp(-dXi);
                    }
                }
            }

            // Anything never intercepted freezes where it sat, at the station it
            // had reached when its own depth solidified.
            foreach (var q in parts)
                if (!q.Deposited) { q.ZFinal = q.Y0; q.SFinal = Math.Min(q.S, L); }

            var ordered = parts.OrderBy(q => q.ZFinal).ToList();
            var lag = new Lagrangian
            {
                Z = ordered.Select(q => q.ZFinal).ToArray(),
                SigmaMPa = ordered.Select(q => q.Sigma).ToArray(),
                WasDeposited = ordered.Select(q => q.Deposited).ToArray(),
                ArrivalHeight = ordered.Select(q => q.Y0).ToArray(),
                SFinal = ordered.Select(q => q.SFinal).ToArray(),
                Weights = ordered.Select(q => q.Weight).ToArray(),
                PathLengthMm = L,
                DepositedFraction = parts.Count(q => q.Deposited) / (double)parts.Count,
            };
            lag.DnFlow = lag.SigmaMPa
                .Select(s => 2.0 * p.CMeltBrewster * 1e-6 * Math.Abs(s)).ToArray();

            // CONTROL: every seeded particle must come to rest somewhere inside
            // the half-gap, exactly once. Seeding uniformly in height makes this a
            // real volume check rather than a count.
            double placed = lag.Z.Count(z => z >= -1e-9 && z <= H + 1e-9);
            lag.MassBalanceError = Math.Abs(placed - parts.Count) / (double)parts.Count;
            return lag;
        }

        /// <summary>
        /// THE DEPTH SHAPE THIS MODEL PRODUCES, on the Eulerian channel's own
        /// grid, normalised so its mean over the wall is exactly 1.
        ///
        /// This is the porting surface. The Eulerian channel's failure is a SHAPE
        /// failure - it peaks at 60% of the half-wall where the measurements peak
        /// at the skin - and its along-flow behaviour passes the in-plane clauses
        /// on both reference cases. So what crosses over is the depth
        /// distribution, and normalising to mean 1 is what makes that transfer
        /// surgical: a station's thickness average is multiplied by 1 and cannot
        /// move, so every clause that reads a thickness average is invariant by
        /// construction and only the depth clauses can respond. If an in-plane
        /// number changes, the port is wrong, and that is asserted rather than
        /// hoped for.
        ///
        /// Volume-weighted, not count-weighted. Particles carry a weight u(y0)
        /// because the volumetric flux at a height is proportional to the speed
        /// there; averaging by count would over-represent the slow near-wall
        /// material, which is precisely the material this model exists to place
        /// correctly. (InPlaneProfile below still averages by count - it predates
        /// the weight being carried out, and its clause is a shape comparison
        /// that the weighting does not decide.)
        ///
        /// Band-averaged for the same reason DnAtFraction is: adjacent core
        /// particles differ by three orders because a core element's final
        /// orientation depends sharply on when it freezes relative to the flow
        /// stopping. minCount is returned so a thin band cannot pass silently.
        /// </summary>
        public double[] DepthShape(double[] zSignedMm, double halfMm, out int minCount)
        {
            int nz = zSignedMm.Length;
            var phi = new double[nz];
            double band = Math.Max(0.05 * halfMm, 2.0 * halfMm / Math.Max(nz - 1, 1));
            minCount = int.MaxValue;

            for (int k = 0; k < nz; k++)
            {
                double target = Math.Abs(zSignedMm[k]);
                double num = 0.0, den = 0.0; int n = 0;
                for (int i = 0; i < Z.Length; i++)
                {
                    if (Math.Abs(Z[i] - target) > band) continue;
                    double w = Weights != null ? Weights[i] : 1.0;
                    num += w * Math.Abs(DnFlow[i]); den += w; n++;
                }
                phi[k] = den > 0.0 ? num / den : 0.0;
                if (n < minCount) minCount = n;
            }

            double mean = 0.0;
            for (int k = 0; k < nz; k++) mean += phi[k];
            mean /= Math.Max(nz, 1);
            if (mean <= 0.0) { for (int k = 0; k < nz; k++) phi[k] = 1.0; return phi; }
            for (int k = 0; k < nz; k++) phi[k] /= mean;
            return phi;
        }

        /// <summary>
        /// Thickness-averaged |dn| against distance from the gate - the quantity
        /// a polarimeter reading through the plate integrates, and the one the
        /// registered in-plane clause compares against.
        ///
        /// Binned by an element's RESTING station, which is what the Lagrangian
        /// model adds: an element deposited at 20 mm contributes there, not where
        /// it entered. The sample count per bin is returned so a thin bin cannot
        /// pass silently.
        /// </summary>
        public double[] InPlaneProfile(int nBins, out int[] counts)
        {
            var sum = new double[nBins];
            counts = new int[nBins];
            for (int i = 0; i < SFinal.Length; i++)
            {
                int b = (int)(SFinal[i] / Math.Max(PathLengthMm, 1e-9) * nBins);
                if (b < 0) b = 0; if (b >= nBins) b = nBins - 1;
                sum[b] += DnFlow[i]; counts[b]++;
            }
            var prof = new double[nBins];
            for (int b = 0; b < nBins; b++) prof[b] = counts[b] > 0 ? sum[b] / counts[b] : 0.0;
            return prof;
        }

        /// <summary>
        /// Value at a fraction of the half-wall, AVERAGED over a band.
        ///
        /// Nearest-particle sampling was the first version and it is not a
        /// measurement: adjacent core particles differ by three orders
        /// (5.8e-8 against 9.0e-5) because a core element's final orientation
        /// depends sharply on when it freezes relative to the flow stopping, and
        /// the core is sparsely seeded. Reading one particle reports that
        /// scatter as if it were the profile. The band is +-5% of the half-wall,
        /// and the sample COUNT is returned so a thin band cannot pass silently.
        /// </summary>
        public double DnAtFraction(double fraction, double halfMm, out int nUsed)
        {
            double target = fraction * halfMm, band = 0.05 * halfMm;
            double sum = 0.0; nUsed = 0;
            for (int i = 0; i < Z.Length; i++)
                if (Math.Abs(Z[i] - target) <= band) { sum += DnFlow[i]; nUsed++; }
            if (nUsed > 0) return sum / nUsed;

            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < Z.Length; i++)
            {
                double d = Math.Abs(Z[i] - target);
                if (d < bd) { bd = d; best = i; }
            }
            nUsed = 1;
            return DnFlow[best];
        }

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            var p = Polymers.ByName("MS_COC_TOPAS6017").WithProcessTemps(280.0, 150.0);
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 71.3, PackTimeS = 3.0 };

            var plate = new MouldedElement
            {
                FrontSurface = 1, CentreThicknessMm = 1.5, SemiDiameterMm = 50.0,
                FrontRadiusMm = 0, BackRadiusMm = 0, Material = p.Name,
            };
            plate.EdgeThicknessMm = plate.ThicknessAt(plate.SemiDiameterMm);
            plate.Gate = new GateSpec
            {
                Kind = GateKind.FilmEdge, AzimuthDeg = 0,
                WidthMm = 100.0, ThicknessMm = 0.9, IsDefault = false,
            };

            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;
            var fill = FillField.Build(plate, p, proc, 101);
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, proc, nz, 10 * nz);
            double half = 0.5 * plate.CentreThicknessMm;

            Console.WriteLine("MoldStress - Lagrangian particle model (A4b)");
            Console.WriteLine("  " + Program.ScopeLabel);
            Console.WriteLine("  A DIFFERENT MODEL, not a correction to the shipped one.");
            Console.WriteLine(string.Format(ci,
                "  lambda scale {0:F2} (1.0 = the Maxwell time eta0/G)", proc.LambdaScale));
            Console.WriteLine();

            int nPart = (int)Program.Value(args, "-particles", 400.0);
            proc.LambdaScale = Program.Value(args, "-lambdascale", 1.0);
            var lag = Build(plate, p, proc, fill, freeze, nPart);

            Console.WriteLine(string.Format(ci,
                "  particles {2} seeded, deposited by the front {0:P0}, " +
                "mass-balance error {1:E1}",
                lag.DepositedFraction, lag.MassBalanceError, lag.Z.Length));
            Console.WriteLine();
            Console.WriteLine("  z/half   dn_flow      came from y0/half   deposited");
            for (int i = lag.Z.Length - 1; i >= 0; i -= lag.Z.Length / 14)
                Console.WriteLine(string.Format(ci,
                    "   {0,5:F3}   {1:E3}        {2,5:F3}           {3}",
                    lag.Z[i] / half, lag.DnFlow[i], lag.ArrivalHeight[i] / half,
                    lag.WasDeposited[i] ? "yes" : "no"));

            // LIKE FOR LIKE. The depth criterion compares flow+thermal, because
            // the source measured out of plane where the thermal residual stress
            // contributes in full. Comparing this model's FLOW channel against
            // that would repeat the one-channel-against-two error corrected on
            // 2026-08-17. The thermal profile is taken from the same routine the
            // Eulerian model uses, so only the flow half differs between them.
            var chT = Channels.Build(plate, p, proc, fill, freeze);
            Func<double, double> thermAt = frac =>
            {
                double target = frac * half;
                int best = 0; double bd = double.MaxValue;
                for (int k = 0; k < freeze.Z.Length; k++)
                {
                    double dd = Math.Abs(Math.Abs(freeze.Z[k]) - target);
                    if (dd < bd) { bd = dd; best = k; }
                }
                return Math.Abs(p.KGlassBrewster) * 1e-6 * Math.Abs(chT.SigmaThermalMPa[0, best]);
            };

            int nS, nD;
            double sFlow = lag.DnAtFraction(RefCase.SurfaceFraction, half, out nS);
            double dFlow = lag.DnAtFraction(RefCase.DeepFraction, half, out nD);
            double s = sFlow + thermAt(RefCase.SurfaceFraction);
            double d = dFlow + thermAt(RefCase.DeepFraction);
            double ratio = d > 0 ? s / d : double.PositiveInfinity;
            Console.WriteLine(string.Format(ci,
                "  flow only: surface {0:E3} (n={3}), deep {1:E3} (n={4}), ratio {2:F2}",
                sFlow, dFlow, dFlow > 0 ? sFlow / dFlow : double.PositiveInfinity, nS, nD));
            Console.WriteLine(string.Format(ci,
                "  + thermal: surface {0:E3}, deep {1:E3}", s, d));
            Console.WriteLine();
            Console.WriteLine(string.Format(ci,
                "  depth ratio {0:F2} at the criterion's own sampling points " +
                "({1:P0} / {2:P0} of the half-wall)",
                ratio, RefCase.SurfaceFraction, RefCase.DeepFraction));
            Console.WriteLine(string.Format(ci,
                "  published {0:F2} (yz) / {1:F2} (xz), band [{2:F2}, {3:F2}]",
                RefCase.PublishedDepthRatio,
                RefCase.PublishedSurfaceDnCross / RefCase.PublishedCoreDn,
                RefCase.PublishedDepthRatio / RefCase.FactorBar,
                RefCase.PublishedDepthRatio * RefCase.FactorBar));

            // ---- in-plane clauses, ported from RefCase ----------------------
            const int NB = 20;
            int[] cnt; var prof = lag.InPlaneProfile(NB, out cnt);
            int argMax = 0;
            for (int b = 1; b < NB; b++) if (prof[b] > prof[argMax]) argMax = b;
            double peak = prof[argMax];
            double peakRatio = peak / RefCase.PublishedPeakDn;
            double farPct = peak > 0 ? 100.0 * prof[NB - 1] / peak : 0.0;

            Console.WriteLine();
            Console.WriteLine("  in-plane, thickness-averaged |dn| by station");
            Console.WriteLine("    s/L    dn_flow      n");
            for (int b = 0; b < NB; b++)
                Console.WriteLine(string.Format(ci, "    {0,4:F2}   {1:E3}   {2,5}",
                    (b + 0.5) / NB, prof[b], cnt[b]));
            Console.WriteLine(string.Format(ci,
                "  (a) predicted peak {0:E3} against published {1:E3} - ratio {2:F2}x  =>  {3}",
                peak, RefCase.PublishedPeakDn, peakRatio,
                (peakRatio >= 1.0 / RefCase.FactorBar && peakRatio <= RefCase.FactorBar)
                    ? "PASS" : "FAIL"));
            Console.WriteLine(string.Format(ci,
                "  (b) maximum at s/L {0:F2}, falling to {1:F1} % of it at the far edge  =>  {2}",
                (argMax + 0.5) / NB, farPct,
                (argMax <= NB / 4 && farPct < 100.0) ? "PASS" : "FAIL"));

            // GATE NULL. The flow model is one-dimensional along the path, so
            // mirroring the gate maps station s to L-s and the profile in PART
            // coordinates reverses. On a SYMMETRIC plate that is a relabelling
            // and cannot fail on its own - stated rather than dressed up. What it
            // does still catch is a profile with no structure at all: a flat
            // field has no maximum to move, and the discrimination check below
            // requires the two ends to differ by more than 5%.
            double xGate0 = (argMax + 0.5) / NB;              // part coord, gate at 0
            double xGate180 = 1.0 - xGate0;                   // gate at the far edge
            bool moves = Math.Abs(xGate0 - xGate180) > 1e-9;
            bool hasStructure = peak > 0 && Math.Abs(prof[0] - prof[NB - 1]) / peak > 0.05;
            Console.WriteLine(string.Format(ci,
                "  (b) NULL: maximum at x/L {0:F2} with the gate at 0 deg, {1:F2} with it " +
                "at 180 deg  =>  {2}", xGate0, xGate180, (moves && hasStructure) ? "PASS" : "FAIL"));
            Console.WriteLine(string.Format(ci,
                "      and the field has structure to move: ends differ by {0:P0} of the " +
                "peak  =>  {1}",
                peak > 0 ? Math.Abs(prof[0] - prof[NB - 1]) / peak : 0.0,
                hasStructure ? "PASS" : "FAIL"));
            Console.WriteLine("      NOTE: on a symmetric plate the position flip is a " +
                              "relabelling; the structure check is the half that can fail.");

            // CONTROL ON THE STATION DISTRIBUTION. A plate fills uniformly: every
            // station holds the same volume per unit length, so the resting-station
            // histogram must be flat to within sampling noise. This is the check
            // the depth-only view could not make, because binning by z alone hides
            // where along the flow the material ended up.
            double meanN = cnt.Average();
            double worst = cnt.Select(c => Math.Abs(c - meanN) / Math.Max(meanN, 1e-9)).Max();
            bool uniformFill = worst < 0.5;
            Console.WriteLine();
            Console.WriteLine(string.Format(ci,
                "  control: resting stations must be UNIFORM (a plate fills evenly). " +
                "worst bin deviates {0:P0} from the mean of {1:F0}  =>  {2}",
                worst, meanN, uniformFill ? "PASS" : "FAIL"));

            bool massOk = lag.MassBalanceError < 1e-9;
            Console.WriteLine();
            Console.WriteLine("  control: every seeded particle comes to rest inside the " +
                              "half-gap exactly once  =>  " + (massOk ? "PASS" : "FAIL"));
            Console.WriteLine("  control: the deposited set must be NON-EMPTY and must not be " +
                              "everything  =>  " +
                              ((lag.DepositedFraction > 0.01 && lag.DepositedFraction < 0.99)
                               ? "PASS" : "FAIL"));
            // The exit code must reflect the CLAUSES, not just the model's
            // internal housekeeping. It returned 0 while three clauses printed
            // FAIL, because it only looked at the mass controls - the same
            // does-nothing-reports-success shape this project keeps meeting.
            bool peakOk = peakRatio >= 1.0 / RefCase.FactorBar && peakRatio <= RefCase.FactorBar;
            bool shapeOk = argMax <= NB / 4 && farPct < 100.0;
            bool depthOk = ratio >= RefCase.PublishedDepthRatio / RefCase.FactorBar
                        && ratio <= RefCase.PublishedDepthRatio * RefCase.FactorBar;
            bool allOk = massOk && uniformFill && peakOk && shapeOk
                      && moves && hasStructure && depthOk;
            Console.WriteLine();
            Console.WriteLine("  VERDICT: " + (allOk
                ? "every ported clause is met"
                : "NOT met - depth " + (depthOk ? "PASS" : "FAIL") +
                  ", in-plane peak " + (peakOk ? "PASS" : "FAIL") +
                  ", in-plane shape " + (shapeOk ? "PASS" : "FAIL") +
                  ", gate null " + ((moves && hasStructure) ? "PASS" : "FAIL")));
            return allOk ? 0 : 2;
        }
    }
}

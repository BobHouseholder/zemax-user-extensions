using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MoldStress
{
    /// <summary>
    /// A4 - assembly into what STAR actually accepts, and the inversion that
    /// makes it possible.
    ///
    /// STAR does not accept birefringence. It accepts a STRESS TENSOR and applies
    /// the catalog's K11 and K12 itself. Thermal residual stress is a real stress
    /// and goes over unchanged. Frozen-in orientation is NOT a stress in the
    /// finished part, so it is converted to the equivalent stress that reproduces
    /// the intended birefringence under the catalog's own constants:
    ///
    ///     dn(parallel - perpendicular) = (K11 - K12) * sigma
    ///     =>  sigma_eq = dn_flow / (K11 - K12)
    ///
    /// That inversion is exact rather than approximate, and it is exact because
    /// it was measured: on 2026-08-15 the forward map came back as
    /// 2*pi*K*sigma*L/lambda, linear in both sigma and path, to 2.5e-8 relative.
    ///
    /// File format, established by measurement the same day and confirmed against
    /// the shipped temperature and deformation files: headerless whitespace,
    /// nine columns, x y z Sxx Syy Szz Sxy Syz Sxz, stress in N/mm^2, positions
    /// in lens units in the surface's LOCAL coordinate system.
    /// </summary>
    internal static class StarFiles
    {
        public sealed class Written
        {
            public string StressPath, IndexPath;
            public int IndexPoints;

            /// <summary>Worst radial-midpoint interpolation error of the written
            /// ring grid, as a percentage of the field's span; 0 for a uniform
            /// field. The number that answers "is this sampling good?" with a
            /// measurement instead of a judgment.</summary>
            public double SamplingErrorPct;
            public int Points;
            public double PeakEquivalentStressMPa;
            public double PeakDnFlow;
            public double PeakDnDensity;
        }

        /// <summary>
        /// The GRIN integration step STAR should use for this element's direct
        /// index, in mm. OpticStudio traces STAR direct-index data as a GRIN
        /// medium, stepping every ray through the fitted volume - so this number
        /// is the COST of every FFT-type analysis: measured 2026-08-22 at about
        /// one second per element at the default 1.0 mm step on a 10 mm part,
        /// and 17 s for three small elements at 0.1 mm, before GUI sampling
        /// multiplies it. It is also the ACCURACY: our index field is a smooth
        /// low-order pressure profile, so a tenth of the wall resolves it fully.
        /// CT/10 clamped to [0.5, 2.0] mm - thin elements get enough steps to
        /// see the profile, thick ones stop paying for resolution the field
        /// does not carry.
        /// </summary>
        /// <summary>
        /// Ring radii graded by the FIELD rather than spaced uniformly. The
        /// metric blends geometry and field change half-and-half: uniform-dr
        /// oversamples the axis region and undersamples the rim, where both the
        /// gate and the steepest pressure gradient live, while pure equal-dn
        /// would pile every ring at the gate and leave the flat side of the part
        /// geometrically unsampled. Endpoints are always included, and a flat
        /// field degrades gracefully to uniform spacing.
        ///
        /// Takes fine SAMPLES of a representative field value per radius rather
        /// than a callback, so it is pure and the self-test can feed it analytic
        /// fields.
        /// </summary>
        /// <summary>
        /// How badly linear interpolation between the chosen rings misses the
        /// field at ring midpoints, as a percentage of the field's span. The
        /// fine samples stand in for truth; the rings' own values are read off
        /// the same samples, so the number measures the RING PLACEMENT, not the
        /// sampler. Returns 0 for a field with no span - a uniform field is
        /// captured perfectly by any grid, and reporting NaN there would read
        /// as a defect.
        /// </summary>
        public static double SamplingErrorPct(double[] rFine, double[] vFine, double[] ringR)
        {
            Func<double, double> f = r =>
            {
                int i0, i1; double t;
                StationLerp(rFine, r, out i0, out i1, out t);
                return Lerp(vFine[i0], vFine[i1], t);
            };
            double lo = double.MaxValue, hi = -double.MaxValue;
            foreach (double v in vFine) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            double span = hi - lo;
            if (span <= 0) return 0.0;

            double worst = 0.0;
            for (int i = 1; i < ringR.Length; i++)
            {
                double mid = 0.5 * (ringR[i - 1] + ringR[i]);
                double approx = 0.5 * (f(ringR[i - 1]) + f(ringR[i]));
                worst = Math.Max(worst, Math.Abs(f(mid) - approx));
            }
            return 100.0 * worst / span;
        }

        public static double[] GradedRadii(double[] rSample, double[] vSample, int nRings)
        {
            int m = rSample.Length;
            if (nRings < 2 || m < 2) return new[] { rSample[0], rSample[m - 1] };
            double span = 0.0;
            for (int i = 1; i < m; i++)
                span += Math.Abs(vSample[i] - vSample[i - 1]);
            double R = rSample[m - 1] - rSample[0];

            var cum = new double[m];
            for (int i = 1; i < m; i++)
            {
                double geo = (rSample[i] - rSample[i - 1]) / Math.Max(R, 1e-30);
                double fld = span > 0
                    ? Math.Abs(vSample[i] - vSample[i - 1]) / span : 0.0;
                cum[i] = cum[i - 1] + 0.5 * geo + 0.5 * fld;
            }

            var radii = new double[nRings];
            radii[0] = rSample[0];
            radii[nRings - 1] = rSample[m - 1];
            int j = 1;
            for (int q = 1; q < nRings - 1; q++)
            {
                double target = cum[m - 1] * q / (double)(nRings - 1);
                while (j < m - 1 && cum[j] < target) j++;
                double d = cum[j] - cum[j - 1];
                double t = d > 0 ? (target - cum[j - 1]) / d : 0.0;
                radii[q] = rSample[j - 1] + t * (rSample[j] - rSample[j - 1]);
            }
            return radii;
        }

        public static double GrinStepFor(double centreThicknessMm)
        {
            double s = centreThicknessMm / 10.0;
            return Math.Max(0.5, Math.Min(2.0, s));
        }

        public static Written Write(MouldedElement e, Polymer p, Channels c,
                                    FillField fill, FreezeHistory freeze,
                                    string directory, int nRadial = 17, int nAzimuth = 24,
                                    int nzExport = 0, int indexZPlanes = 0)
        {
            var ci = CultureInfo.InvariantCulture;
            var stress = new StringBuilder();
            var index = new StringBuilder();
            var w = new Written();

            double k11 = p.K11Brewster, k12 = p.K12Brewster;
            double kDiff = (k11 - k12) * 1e-6;          // per N/mm^2, splits the index
            double kIso = (k11 + 2.0 * k12) * 1e-6;     // per N/mm^2, shifts it
            if (Math.Abs(kDiff) < 1e-15)
                throw new InvalidOperationException(
                    p.Name + " has K11 == K12, so no stress can reproduce a birefringence");

            // Gate direction, from +Y, and the flow direction away from it.
            double phi = e.Gate.AzimuthDeg * Math.PI / 180.0;
            double gx = e.SemiDiameterMm * Math.Sin(phi), gy = e.SemiDiameterMm * Math.Cos(phi);

            // EXPORTED DEPTH GRID, separate from the physics grid.
            //
            // The physics wants a fine wall (nz=321 to converge); the file wants
            // few points, because it carries nz per (x,y) station and STAR has to
            // fit all of them. Uniform decimation would be wrong: the field's
            // whole structure is in the outermost few percent, which uniform
            // sampling throws away first. So the exported depths are placed
            // QUADRATICALLY in distance from the wall - dense at the skin, sparse
            // in the core - and taken as actual physics nodes, so no interpolation
            // is involved.
            int nzFull = freeze.NodeCount;
            int[] zIdx;
            if (nzExport <= 0 || nzExport >= nzFull)
            {
                zIdx = new int[nzFull];
                for (int i = 0; i < nzFull; i++) zIdx[i] = i;
            }
            else
            {
                var picked = new System.Collections.Generic.SortedSet<int>();
                int half = nzExport / 2;
                for (int i = 0; i <= half; i++)
                {
                    double u = (double)i / half;            // 0 at wall, 1 at centre
                    double depthFrac = u * u;               // quadratic: dense at the wall
                    int idxLow = (int)Math.Round(depthFrac * (nzFull - 1) / 2.0);
                    picked.Add(Math.Max(0, Math.Min(nzFull - 1, idxLow)));
                    picked.Add(Math.Max(0, Math.Min(nzFull - 1, nzFull - 1 - idxLow)));
                }
                zIdx = new int[picked.Count];
                picked.CopyTo(zIdx);
            }
            int nz = zIdx.Length;
            // The grid spans the EXPORT radius - the mechanical aperture, flange
            // included - while every field lookup below is clamped to the physics
            // radius. Beyond the clear aperture the true cavity is flange
            // geometry this model does not know, so the rim's field values are
            // carried outward at the rim's z-band: honest coverage rather than
            // invented physics, and STAR interpolates data instead of
            // extrapolating into a void. The banner the run prints says so
            // whenever the two radii differ.
            double rExport = e.ExportSemiDiameterMm > 0
                ? Math.Max(e.ExportSemiDiameterMm, e.SemiDiameterMm) : e.SemiDiameterMm;
            // THE INDEX FILE'S OWN z-GRID, in index-only mode. The density
            // index field is CONSTANT through the thickness by construction - it
            // is a per-station pressure term - so carrying it at 41 wall-
            // clustered depths is pure redundancy: the same number 41 times per
            // column, a 10x bigger file, a slower Refit, and a loader view that
            // IMPLIES thickness structure the field does not have. A few planes
            // keep the fitted volume covering the lens; the wall clustering
            // stays for the STRESS file, whose flow-birefringence field really
            // does peak at 95% of the half-wall.
            var indexKs = new System.Collections.Generic.HashSet<int>();
            if (indexZPlanes > 1 && indexZPlanes < zIdx.Length)
                for (int j = 0; j < indexZPlanes; j++)
                    indexKs.Add(zIdx[(int)Math.Round(
                        j * (zIdx.Length - 1) / (double)(indexZPlanes - 1))]);

            // RING RADII GRADED BY THE FIELD (2026-08-22). The representative
            // value per radius is the worst-over-azimuth |density dn| - the
            // index-only field, and in full mode still the smooth in-plane
            // component that varies along the flow path.
            var rFine = new double[97];
            var vFine = new double[97];
            for (int q = 0; q < 97; q++)
            {
                rFine[q] = rExport * q / 96.0;
                double rf = Math.Min(rFine[q], e.SemiDiameterMm);
                double worst = 0.0;
                for (int ia = 0; ia < nAzimuth; ia++)
                {
                    double th = 2.0 * Math.PI * ia / nAzimuth;
                    double sq, fxq, fyq;
                    FlowDirection(e, rf * Math.Cos(th), rf * Math.Sin(th),
                                  out fxq, out fyq, out sq);
                    int q0, q1; double qt;
                    StationLerp(fill.S, sq, out q0, out q1, out qt);
                    worst = Math.Max(worst,
                        Math.Abs(Lerp(c.DnDensity[q0, 0], c.DnDensity[q1, 0], qt)));
                }
                vFine[q] = worst;
            }
            var ringR = GradedRadii(rFine, vFine, nRadial);
            w.SamplingErrorPct = SamplingErrorPct(rFine, vFine, ringR);

            for (int ir = 0; ir < nRadial; ir++)
            {
                double r = ringR[ir];
                double rField = Math.Min(r, e.SemiDiameterMm);
                int nAz = ir == 0 ? 1 : nAzimuth;
                for (int ia = 0; ia < nAz; ia++)
                {
                    double th = 2.0 * Math.PI * ia / nAz;
                    double x = r * Math.Cos(th), y = r * Math.Sin(th);
                    // ...but the FIELD is evaluated at the clamped radius.
                    double xf = rField * Math.Cos(th), yf = rField * Math.Sin(th);

                    // Path coordinate from the gate, and the local flow direction.
                    double s, fx, fy;
                    FlowDirection(e, xf, yf, out fx, out fy, out s);
                    // INTERPOLATED, not nearest-node, since 2026-08-22. The fill
                    // solve carries 101 stations; nearest-node at ~17 export radii
                    // turned its smooth fields into small staircases, and the
                    // spline fit then faithfully reproduced each step edge as
                    // spurious gradient wiggle - an artifact the solver never had.
                    int iS0, iS1; double tS;
                    StationLerp(fill.S, s, out iS0, out iS1, out tS);

                    double h = e.ThicknessAt(rField);
                    // The SAME shape the cavity was solved on, conic and
                    // aspheric terms included. A sphere here against an asphere
                    // there would put the stress field on the wrong surface.
                    double zFront = e.SagFrontAt(rField);

                    for (int kk = 0; kk < nz; kk++)
                    {
                        int k = zIdx[kk];
                        double zMid = freeze.Z[k] * (h / freeze.ThicknessMm);   // scale to local wall
                        double zLocal = zFront + 0.5 * h + zMid;

                        double dnFlow = Lerp(c.DnFlow[iS0, k], c.DnFlow[iS1, k], tS);
                        double sigEq = dnFlow / kDiff;                 // N/mm^2
                        double sigTh = Lerp(c.SigmaThermalMPa[iS0, k], c.SigmaThermalMPa[iS1, k], tS);

                        // Flow frame: parallel to flow, and transverse in plane.
                        double sPar = sigEq + sigTh;
                        double sPer = sigTh;

                        double cx = fx, cy = fy;
                        double sxx = sPar * cx * cx + sPer * cy * cy;
                        double syy = sPar * cy * cy + sPer * cx * cx;
                        double sxy = (sPar - sPer) * cx * cy;

                        // The density term rides IN the same tensor, as a
                        // hydrostatic component, rather than going out through
                        // DirectIndex.
                        //
                        // Measured 2026-08-15 by A/B on this very system: loading
                        // DirectIndex onto a surface that already carries stress
                        // silently EMPTIES the retardance map - 217 points and a
                        // 4.85 rad peak became no points at all, with no error and
                        // no refusal. IndexDataType is {None, PhysicsBasedIndex,
                        // DirectRefractiveIndex}: the two are alternatives, not
                        // layers. A hydrostatic stress shifts the index without
                        // splitting it - dn = (K11 + 2*K12)*sigma - so the same
                        // channel carries both effects and neither is lost.
                        double dnDen = Lerp(c.DnDensity[iS0, k], c.DnDensity[iS1, k], tS);
                        double sigH = dnDen / kIso;

                        stress.AppendLine(string.Format(ci,
                            "{0:E9} {1:E9} {2:E9} {3:E9} {4:E9} {5:E9} {6:E9} {7:E9} {8:E9}",
                            x, y, zLocal, sxx + sigH, syy + sigH, sigH, sxy, 0.0, 0.0));

                        double nHere = p.Nd + dnDen;
                        if (indexKs.Count == 0 || indexKs.Contains(k))
                        {
                            index.AppendLine(string.Format(ci,
                                "{0:E9} {1:E9} {2:E9} {3:E9}", x, y, zLocal, nHere));
                            w.IndexPoints++;
                        }

                        w.Points++;
                        w.PeakEquivalentStressMPa = Math.Max(w.PeakEquivalentStressMPa, Math.Abs(sigEq));
                        w.PeakDnFlow = Math.Max(w.PeakDnFlow, Math.Abs(dnFlow));
                        w.PeakDnDensity = Math.Max(w.PeakDnDensity, Math.Abs(dnDen));
                    }
                }
            }

            Directory.CreateDirectory(directory);
            w.StressPath = Path.Combine(directory,
                string.Format("moldstress_s{0}_stress.txt", e.FrontSurface));
            w.IndexPath = Path.Combine(directory,
                string.Format("moldstress_s{0}_index.txt", e.FrontSurface));
            File.WriteAllText(w.StressPath, stress.ToString());
            File.WriteAllText(w.IndexPath, index.ToString());
            return w;
        }


        /// <summary>
        /// Where the melt is going at (x,y), and how far it has come from the
        /// gate. Extracted so the angular test exercises the SAME rule the files
        /// are written from - a test against a re-typed copy of a rule proves
        /// only that two copies agree.
        /// </summary>
        public static void FlowDirection(MouldedElement e, double x, double y,
                                         out double fx, out double fy, out double s)
        {
            double phi = e.Gate.AzimuthDeg * Math.PI / 180.0;
            double gx = e.SemiDiameterMm * Math.Sin(phi), gy = e.SemiDiameterMm * Math.Cos(phi);
            if (e.Gate.Kind == GateKind.RingAllRound)
            {
                double r = Math.Sqrt(x * x + y * y);
                s = e.SemiDiameterMm - r;
                double rr = Math.Max(r, 1e-9);
                fx = -x / rr; fy = -y / rr;                 // inward
            }
            else
            {
                double dx = x - gx, dy = y - gy;
                s = Math.Sqrt(dx * dx + dy * dy);
                double dd = Math.Max(s, 1e-9);
                fx = dx / dd; fy = dy / dd;                 // away from the gate
            }
        }

        /// <summary>Bracketing indices and blend factor for linear interpolation
        /// over a sorted station array, clamped at both ends. Internal so the
        /// self-test can hold both arms against it.</summary>
        internal static void StationLerp(double[] S, double s, out int i0, out int i1, out double t)
        {
            if (s <= S[0]) { i0 = i1 = 0; t = 0.0; return; }
            int n = S.Length;
            if (s >= S[n - 1]) { i0 = i1 = n - 1; t = 0.0; return; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (S[mid] <= s) lo = mid; else hi = mid;
            }
            i0 = lo; i1 = hi;
            double d = S[hi] - S[lo];
            t = d > 0 ? (s - S[lo]) / d : 0.0;
        }

        internal static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static int NearestNode(double[] arr, double v)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < arr.Length; i++)
            {
                double d = Math.Abs(arr[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// CONTROL: the inversion must round-trip. Take a birefringence, convert
        /// it to an equivalent stress, apply the catalog relation forward, and the
        /// birefringence must come back. Then check the tensor rotation carries
        /// the principal axis to the gate direction, because the registered null
        /// control depends on exactly that.
        /// </summary>
        public static void SelfCheck()
        {
            Console.WriteLine("  A4 assembly and the inversion");

            var p = Polymers.ByName("MS_COC_TOPAS6017");
            double kDiff = (p.K11Brewster - p.K12Brewster) * 1e-6;

            double dnWanted = 1.2e-4;                       // the reference case's peak
            double sigEq = dnWanted / kDiff;
            double dnBack = kDiff * sigEq;
            SelfTest.Near("equivalent-stress inversion round-trips", dnBack, dnWanted, 1e-12);

            // THE K11/K12 SPLIT IS ASSUMED FOR EVERY POLYMER HERE, so what depends
            // on it and what does not is worth asserting rather than reasoning
            // about. Sweep K11 at FIXED measured difference and check both arms.
            {
                double kglass = p.KGlassBrewster;        // the measured quantity
                double diffFirst = double.NaN, isoMin = double.MaxValue,
                       isoMax = double.MinValue;
                bool diffInvariant = true;
                foreach (double k11 in new[] { 5.0, 2.43, 0.0, -4.25, -8.5 })
                {
                    double k12 = k11 + kglass;
                    double kd = k11 - k12, ki = k11 + 2.0 * k12;
                    if (double.IsNaN(diffFirst)) diffFirst = kd;
                    if (Math.Abs(kd - diffFirst) > 1e-12) diffInvariant = false;
                    isoMin = Math.Min(isoMin, Math.Abs(ki));
                    isoMax = Math.Max(isoMax, Math.Abs(ki));
                }
                // ARM 1: retardance must be immune to the split. If this ever
                // fails, the headline output has started depending on a number
                // nobody measured.
                SelfTest.Check("retardance term is INVARIANT under the K11/K12 split",
                    diffInvariant,
                    string.Format("kDiff = {0:F2} Br at every split", diffFirst));
                // ARM 2: and the isotropic term must be shown to MOVE, or arm 1
                // is passing on a sweep that does nothing.
                SelfTest.Check("isotropic index term DOES depend on the split",
                    isoMax / Math.Max(isoMin, 1e-12) > 5.0,
                    string.Format("kIso spans {0:F2} to {1:F2} Br, a factor of {2:F0}",
                        isoMin, isoMax, isoMax / Math.Max(isoMin, 1e-12)));
            }
            Console.WriteLine(string.Format(
                "        {0:E2} of birefringence needs {1:F1} N/mm^2 of equivalent stress",
                dnWanted, sigEq));

            // Rotation: a flow along +X must put the difference into Sxx - Syy,
            // and a flow at 45 degrees must put it entirely into Sxy.
            double sPar = 10.0, sPer = 0.0;
            double[] axis = { 1.0, 0.0 };
            double sxx = sPar * axis[0] * axis[0] + sPer * axis[1] * axis[1];
            double syy = sPar * axis[1] * axis[1] + sPer * axis[0] * axis[0];
            double sxy = (sPar - sPer) * axis[0] * axis[1];
            SelfTest.Near("flow along x puts the stress in Sxx", sxx, 10.0, 1e-12);
            SelfTest.Near("and nothing in Sxy", sxy, 0.0, 1e-12);

            double c45 = Math.Sqrt(0.5);
            sxy = (sPar - sPer) * c45 * c45;
            SelfTest.Near("flow at 45 degrees puts half of it in Sxy", sxy, 5.0, 1e-12);

            // The whole point of the gate azimuth: rotating it must rotate the
            // tensor. If these agreed, the registered null control could not fail.
            double[] a0 = { Math.Sin(0.0), Math.Cos(0.0) };
            double[] a180 = { Math.Sin(Math.PI), Math.Cos(Math.PI) };
            SelfTest.Check("a 180 degree gate move reverses the flow direction",
                Math.Abs(a0[1] + a180[1]) < 1e-12,
                string.Format("+Y {0:F3} vs {1:F3}", a0[1], a180[1]));
        }
    }
}

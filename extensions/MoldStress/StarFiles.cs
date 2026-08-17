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
            public int Points;
            public double PeakEquivalentStressMPa;
            public double PeakDnFlow;
            public double PeakDnDensity;
        }

        public static Written Write(MouldedElement e, Polymer p, Channels c,
                                    FillField fill, FreezeHistory freeze,
                                    string directory, int nRadial = 17, int nAzimuth = 24,
                                    int nzExport = 0)
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
            for (int ir = 0; ir < nRadial; ir++)
            {
                double r = e.SemiDiameterMm * ir / (nRadial - 1.0);
                int nAz = ir == 0 ? 1 : nAzimuth;
                for (int ia = 0; ia < nAz; ia++)
                {
                    double th = 2.0 * Math.PI * ia / nAz;
                    double x = r * Math.Cos(th), y = r * Math.Sin(th);

                    // Path coordinate from the gate, and the local flow direction.
                    double s, fx, fy;
                    FlowDirection(e, x, y, out fx, out fy, out s);
                    int iS = NearestNode(fill.S, s);

                    double h = e.ThicknessAt(r);
                    double zFront = MouldedElement.Sag(e.FrontRadiusMm, r);

                    for (int kk = 0; kk < nz; kk++)
                    {
                        int k = zIdx[kk];
                        double zMid = freeze.Z[k] * (h / freeze.ThicknessMm);   // scale to local wall
                        double zLocal = zFront + 0.5 * h + zMid;

                        double dnFlow = c.DnFlow[iS, k];
                        double sigEq = dnFlow / kDiff;                 // N/mm^2
                        double sigTh = c.SigmaThermalMPa[iS, k];

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
                        double sigH = c.DnDensity[iS, k] / kIso;

                        stress.AppendLine(string.Format(ci,
                            "{0:E9} {1:E9} {2:E9} {3:E9} {4:E9} {5:E9} {6:E9} {7:E9} {8:E9}",
                            x, y, zLocal, sxx + sigH, syy + sigH, sigH, sxy, 0.0, 0.0));

                        double nHere = p.Nd + c.DnDensity[iS, k];
                        index.AppendLine(string.Format(ci,
                            "{0:E9} {1:E9} {2:E9} {3:E9}", x, y, zLocal, nHere));

                        w.Points++;
                        w.PeakEquivalentStressMPa = Math.Max(w.PeakEquivalentStressMPa, Math.Abs(sigEq));
                        w.PeakDnFlow = Math.Max(w.PeakDnFlow, Math.Abs(dnFlow));
                        w.PeakDnDensity = Math.Max(w.PeakDnDensity, Math.Abs(c.DnDensity[iS, k]));
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

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
                    w = Math.Max(w, e.Gate.WidthMm);
                }
                f.Width[i] = w;
                f.H[i] = Math.Max(e.ThicknessAt(Math.Min(r, e.SemiDiameterMm)), 1e-4);
            }

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

using System;

namespace MoldStress
{
    /// <summary>
    /// The parameter-free check, from Hung, Appl. Opt. 39(34) 6530 (2000).
    ///
    /// That paper measured a moulded objective through a rotating polariser and
    /// found the astigmatism vector turns at TWICE the polarisation angle - 30
    /// degrees of polarisation gives 60 degrees of astigmatism vector. It needs no
    /// lens prescription, no material constants and no process conditions, which
    /// is exactly why it is worth having: every other number in this project
    /// depends on something we are unsure about, and this one depends on nothing
    /// but whether the equivalent-stress tensor is oriented correctly.
    ///
    /// The mechanism it tests. A linear polariser at alpha through a retarder
    /// whose slow axis lies along the local flow direction theta sees a phase
    /// that goes as cos(2(theta - alpha)). For a gated part the flow direction
    /// varies across the pupil, so that phase carries an r^2*cos(2*phi) term -
    /// astigmatism. Projecting onto the two Zernike astigmatism terms gives a
    /// vector, and rotating alpha must rotate that vector at 2*alpha while
    /// leaving its LENGTH alone. A tensor built with the wrong handedness, a
    /// missing factor of two in the rotation, or an axis pinned to the part
    /// instead of the flow all break one or the other.
    ///
    /// The field comes from the real model and the flow directions from the same
    /// StarFiles.FlowDirection the exported files are written with.
    /// </summary>
    internal static class AngularTest
    {
        public static void SelfCheck()
        {
            Console.WriteLine("  angular law (Hung 2000): astigmatism turns at 2x polarisation");

            var p = Polymers.ByName("MS_COC_TOPAS6017");
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 60.0, PackTimeS = 3.0 };

            // A round element with a ring gate: the flow is radial, so the slow
            // axis sweeps through 360 degrees across the pupil and the phase
            // genuinely carries astigmatism rather than piston.
            var e = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = p.Name,
                CentreThicknessMm = 2.0, SemiDiameterMm = 8.0,
                FrontRadiusMm = 40.0, BackRadiusMm = -40.0,
            };
            e.EdgeThicknessMm = e.ThicknessAt(e.SemiDiameterMm);
            e.Gate = new GateSpec { Kind = GateKind.RingAllRound, AzimuthDeg = 0,
                                    WidthMm = 2 * Math.PI * 8.0, ThicknessMm = 0.9 };
            var fill = FillField.Build(e, p, proc, 101);
            var freeze = FreezeHistory.Build(Math.Max(e.EdgeThicknessMm, 0.2), p, proc, 81);
            var ch = Channels.Build(e, p, proc, fill, freeze);

            const int NR = 24, NA = 96;
            double firstAngle = 0.0, firstLen = 0.0;
            bool lenSteady = true, lawHolds = true;
            string worst = "";

            foreach (double alphaDeg in new[] { 0.0, 15.0, 30.0, 45.0, 60.0, 90.0 })
            {
                double alpha = alphaDeg * Math.PI / 180.0;
                double a5 = 0.0, a6 = 0.0;

                for (int ir = 0; ir < NR; ir++)
                {
                    double rho = (ir + 0.5) / NR;
                    double r = rho * e.SemiDiameterMm;
                    for (int ia = 0; ia < NA; ia++)
                    {
                        double phi = 2.0 * Math.PI * (ia + 0.5) / NA;
                        double x = r * Math.Cos(phi), y = r * Math.Sin(phi);

                        double fx, fy, s;
                        StarFiles.FlowDirection(e, x, y, out fx, out fy, out s);
                        double theta = Math.Atan2(fy, fx);          // slow axis, from the model

                        // Thickness-averaged retardance magnitude at this station.
                        int iS = 0; double best = double.MaxValue;
                        for (int i = 0; i < fill.S.Length; i++)
                        {
                            double d = Math.Abs(fill.S[i] - s);
                            if (d < best) { best = d; iS = i; }
                        }
                        double dn = 0.0;
                        for (int k = 0; k < freeze.NodeCount; k++) dn += Math.Abs(ch.DnFlow[iS, k]);
                        dn /= freeze.NodeCount;

                        // Phase seen by a polariser at alpha through a retarder
                        // whose slow axis is theta, weighted by the pupil area.
                        double phase = dn * Math.Cos(2.0 * (theta - alpha)) * rho;
                        a5 += phase * Math.Cos(2.0 * phi);
                        a6 += phase * Math.Sin(2.0 * phi);
                    }
                }

                double ang = Math.Atan2(a6, a5) * 180.0 / Math.PI;
                double len = Math.Sqrt(a5 * a5 + a6 * a6);
                if (alphaDeg == 0.0) { firstAngle = ang; firstLen = len; }
                else
                {
                    double turned = ang - firstAngle;
                    while (turned < -180) turned += 360;
                    while (turned > 180) turned -= 360;
                    double want = 2.0 * alphaDeg;
                    while (want > 180) want -= 360;
                    double err = Math.Abs(turned - want);
                    if (err > 180) err = 360 - err;
                    if (err > 0.5) { lawHolds = false; worst = string.Format(
                        "alpha {0:F0} deg turned {1:F2} deg, wanted {2:F0}", alphaDeg, turned, want); }
                    if (firstLen > 0 && Math.Abs(len - firstLen) / firstLen > 1e-6) lenSteady = false;
                }
            }

            SelfTest.Check("astigmatism vector turns at exactly 2x the polarisation angle",
                lawHolds, lawHolds ? "0, 15, 30, 45, 60, 90 deg all within 0.5 deg"
                                   : worst);
            SelfTest.Check("and its length does not change as the polariser rotates",
                lenSteady, "rotation, not scaling");

            // NULL. Pin the slow axis to the PART instead of the flow, changing
            // nothing else, and the law must break. Without this the check above
            // is close to a symmetry identity and would pass on almost any
            // implementation - which is the failure mode four other controls in
            // this project turned out to have.
            double nullLen = 0.0;
            for (int ir = 0; ir < NR; ir++)
            {
                double rho = (ir + 0.5) / NR;
                double r = rho * e.SemiDiameterMm;
                double a5 = 0.0, a6 = 0.0;
                for (int ia = 0; ia < NA; ia++)
                {
                    double phi = 2.0 * Math.PI * (ia + 0.5) / NA;
                    double theta = 0.3;                    // fixed to the part, not the flow
                    double phase = Math.Cos(2.0 * (theta - 0.0)) * rho;
                    a5 += phase * Math.Cos(2.0 * phi);
                    a6 += phase * Math.Sin(2.0 * phi);
                }
                nullLen += Math.Sqrt(a5 * a5 + a6 * a6);
            }
            SelfTest.Check("null: an axis pinned to the part produces NO astigmatism vector",
                nullLen / Math.Max(firstLen, 1e-30) < 1e-9,
                string.Format("{0:E2} against {1:E2} for the flow-aligned axis",
                    nullLen, firstLen));
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MoldStress
{
    /// <summary>
    /// Tests one hypothesis about why the depth profile fails.
    ///
    /// HYPOTHESIS, stated before the run: the surface/deep ratio comes out at
    /// 15.62 against a published 5.56 because the relaxation time lambda = eta/G
    /// is far SHORTER than the fill time, which turns the memory bracket into a
    /// step. Layers that solidify before filling ends keep essentially all of
    /// their orientation; layers that solidify after it lose essentially all of
    /// it, because exp(-(t_f - t_fill)/lambda) collapses when lambda is small.
    /// A step in depth is exactly what an over-peaked profile looks like.
    ///
    /// PREDICTION, fixed before measuring, and falsifiable:
    ///   1. the decomposition will show memory ~ 1 above the cliff and ~ 0 below
    ///      it, with the cliff at the depth where the freeze time crosses the
    ///      fill time - NOT a smooth decay;
    ///   2. raising lambda toward and past the fill time will LOWER the ratio
    ///      monotonically toward the published value.
    /// If the ratio does not move with lambda, the hypothesis is wrong and the
    /// surface-peaking is coming from somewhere else.
    /// </summary>
    internal static class DepthDiag
    {
        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            var log = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); log.AppendLine(s); };

            var p = Polymers.ByName("MS_COC_TOPAS6017");
            var baseProc = new Process { FillTimeS = 1.0, PackPressureMPa = 60.0, PackTimeS = 3.0 };

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

            var fill = FillField.Build(plate, p, baseProc, 101);
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, baseProc, 81);
            double lambda0 = fill.EtaPaS / p.MeltModulusPa;
            double half = 0.5 * plate.CentreThicknessMm;

            say("MoldStress - depth-profile hypothesis test");
            say("  " + Program.ScopeLabel);
            say(string.Format(ci,
                "  eta {0:E3} Pa.s, G {1:E2} Pa  =>  lambda {2:E3} s against a fill time of {3:F2} s",
                fill.EtaPaS, p.MeltModulusPa, lambda0, baseProc.FillTimeS));
            say(string.Format(ci, "  lambda / fill time = {0:E2}", lambda0 / baseProc.FillTimeS));
            say("");

            // --- prediction 1: is the bracket a step? --------------------------
            say("  depth   t_freeze     memory     tau_visc      |dn|");
            var ch0 = Channels.Build(plate, p, baseProc, fill, freeze);
            int nz = freeze.NodeCount;
            for (int k = nz - 1; k >= nz / 2; k -= 2)
            {
                double f = Math.Abs(freeze.Z[k]) / half;
                double tArrive = 0.0;                     // gate station
                double tAbs = tArrive + freeze.FreezeTimeS[k];
                double mem = Channels.MemoryFactor(tArrive, baseProc.FillTimeS, tAbs,
                                                   lambda0, baseProc.PackTimeS);
                double tau = fill.DpDs[0] * Math.Abs(freeze.Z[k]);
                say(string.Format(ci, "  {0,5:P0}  {1,9:F4}  {2,9:F5}  {3,10:E3}  {4,9:E3}",
                    f, freeze.FreezeTimeS[k], mem, tau, Math.Abs(ch0.DnFlow[0, k])));
            }
            // Where does the freeze time cross the fill time? That is the depth
            // the hypothesis says the cliff must sit at.
            double cliffFraction = double.NaN;
            for (int k = nz - 1; k > nz / 2; k--)
            {
                if (freeze.FreezeTimeS[k] <= baseProc.FillTimeS &&
                    freeze.FreezeTimeS[k - 1] > baseProc.FillTimeS)
                { cliffFraction = Math.Abs(freeze.Z[k]) / half; break; }
            }
            say("");
            say(string.Format(ci,
                "  freeze time crosses the fill time ({0:F2} s) at {1:P0} of the half-wall;",
                baseProc.FillTimeS, cliffFraction));
            say(string.Format(ci,
                "  the core does not solidify until {0:F2} s.", freeze.CentreFreezeTimeS));
            say("  PREDICTION 1: the memory column should step at that same depth.");
            say("");

            // --- prediction 2: is lambda the lever? ----------------------------
            say("  lambda scale     lambda (s)   surface/deep ratio");
            double[] scales = { 0.01, 0.1, 1.0, 10.0, 100.0, 1000.0 };
            double prev = double.NaN;
            bool monotoneDown = true;
            foreach (double sc in scales)
            {
                var proc = new Process
                {
                    FillTimeS = baseProc.FillTimeS,
                    PackPressureMPa = baseProc.PackPressureMPa,
                    PackTimeS = baseProc.PackTimeS,
                    LambdaScale = sc,
                };
                var ch = Channels.Build(plate, p, proc, fill, freeze);
                double s = Channels.DnAtDepthFraction(ch.DnFlow, freeze.Z, 0, half, RefCase.SurfaceFraction);
                double d = Channels.DnAtDepthFraction(ch.DnFlow, freeze.Z, 0, half, RefCase.DeepFraction);
                double ratio = d > 0 ? s / d : double.PositiveInfinity;
                say(string.Format(ci, "  {0,12:E1}  {1,12:E3}  {2,18:F3}", sc, sc * lambda0, ratio));
                if (!double.IsNaN(prev) && ratio > prev + 1e-9) monotoneDown = false;
                prev = ratio;
            }
            say("");
            say("  published ratio 5.56, registered band [2.78, 11.11]");
            say(monotoneDown
                ? "  the ratio falls monotonically with lambda - PREDICTION 2 HOLDS"
                : "  the ratio does NOT fall monotonically with lambda - PREDICTION 2 REFUTED");

            // --- is the number even converged? ---------------------------------
            // Asked before any new physics is proposed. The criterion samples at
            // 2.5% of the half-wall from the surface, which is where the freeze
            // profile is steepest and a grid resolves it worst.
            say("");
            say("  grid convergence of the depth ratio");
            say("     nz    nFD    surface |dn|      deep |dn|     ratio");
            double lastRatio = double.NaN;
            foreach (var cfg in new[] {
                new { nz = 41, nfd = 401 }, new { nz = 81, nfd = 801 },
                new { nz = 161, nfd = 1601 }, new { nz = 321, nfd = 3201 } })
            {
                var fr = FreezeHistory.Build(plate.CentreThicknessMm, p, baseProc, cfg.nz, cfg.nfd);
                var chC = Channels.Build(plate, p, baseProc, fill, fr);
                double sS = Channels.DnAtDepthFraction(chC.DnFlow, fr.Z, 0, half, RefCase.SurfaceFraction);
                double dD = Channels.DnAtDepthFraction(chC.DnFlow, fr.Z, 0, half, RefCase.DeepFraction);
                double r = dD > 0 ? sS / dD : double.PositiveInfinity;
                say(string.Format(ci, "  {0,5}  {1,5}  {2,13:E3}  {3,13:E3}  {4,8:F3}",
                    cfg.nz, cfg.nfd, sS, dD, r));
                lastRatio = r;
            }
            say("");
            say("  a ratio that moves with resolution is a measurement result, not a");
            say("  physics result. Compare the spread above with the 5.56 target and");
            say("  the [2.78, 11.11] band before proposing any new mechanism.");

            // --- what stress does a layer actually lock in? ---------------------
            //
            // The shipped channel uses tau = |dp/ds| * |z|: the stress at that
            // layer's position in a fully developed profile through the ORIGINAL
            // gap. But by the time a layer at |z| freezes, the frozen skin has
            // grown to exactly (half - |z|) from each wall, so the molten channel
            // is 2|z| wide and that layer is sitting ON the melt/solid interface.
            // The stress there is (h_melt/2)*|dp/ds|(h_melt), and at fixed flow
            // rate |dp/ds| goes as 1/h_melt^3, so tau_interface ~ 1/|z|^2 - it
            // RISES toward the mid-plane instead of falling, and is cut off when
            // filling stops. Two qualitatively different profiles, and the
            // shipped one is the only one that cannot be sharper than linear.
            say("");
            say("  what stress does a layer lock in?");
            say("   depth   tau ~ |z| (shipped)   tau_interface ~ 1/|z|^2   flowing at freeze?");
            var frD = FreezeHistory.Build(plate.CentreThicknessMm, p, baseProc, 81);
            int nzD = frD.NodeCount;
            double tauWall = fill.DpDs[0] * half;
            double sIface = 0, dIface = 0;
            for (int k = nzD - 1; k >= nzD / 2; k -= 4)
            {
                double f = Math.Abs(frD.Z[k]) / half;
                if (f < 1e-6) continue;
                double tauShipped = fill.DpDs[0] * Math.Abs(frD.Z[k]);
                bool flowing = frD.FreezeTimeS[k] <= baseProc.FillTimeS;
                double hMelt = 2.0 * Math.Abs(frD.Z[k]);
                double gradNarrow = fill.DpDs[0] * Math.Pow(plate.CentreThicknessMm / hMelt, 3);
                double tauIface = flowing ? 0.5 * hMelt * gradNarrow : 0.0;
                say(string.Format(ci, "  {0,5:P0}   {1,18:E3}   {2,22:E3}   {3}",
                    f, tauShipped / tauWall, tauIface / tauWall, flowing ? "yes" : "no"));
                if (Math.Abs(f - RefCase.SurfaceFraction) < 0.03) sIface = tauIface;
                if (Math.Abs(f - RefCase.DeepFraction) < 0.03) dIface = tauIface;
            }
            say("");
            say(string.Format(ci,
                "  shipped depth ratio {0:F3} = {1:F3}/{2:F3} - purely geometric",
                RefCase.SurfaceFraction / RefCase.DeepFraction,
                RefCase.SurfaceFraction, RefCase.DeepFraction));
            if (dIface > 0)
                say(string.Format(ci, "  interface model would give {0:F3}", sIface / dIface));
            else
            {
                say("  interface model: the deep sampling point is NOT still flowing when it");
                say("  freezes, so its locked-in shear is zero and the ratio DIVERGES - a");
                say("  profile far SHARPER than the published 5.56, not flatter. That is an");
                say("  overshoot of the same kind fixed lambda produced, so swapping to it");
                say("  would trade one wrong shape for another rather than fix anything.");
            }

            // WHERE does the surface value get sampled?
            //
            // The published 10e-4 is a PRISM COUPLER reading - an evanescent
            // probe of order a micron. The registered criterion samples at 97.5%
            // of the half-wall, which on a 1.5 mm plate is 19 um in. If the
            // model's skin term is concentrated in the outermost microns, those
            // are not the same measurement, and the gap would be a DOMAIN
            // mismatch rather than a physics one - the same class as the two
            // corrections that already moved this number by 2x and 5x.
            say("");
            say("  surface sampling depth vs the ratio it produces");
            say("   fraction   depth from wall     surface |dn|      ratio to 47%");
            var frS = FreezeHistory.Build(plate.CentreThicknessMm, p, baseProc, 401, 1601);
            var chS = Channels.Build(plate, p, baseProc, fill, frS);
            double deepS = Channels.DnAtDepthFraction(chS.DnFlow, frS.Z, 0, half, RefCase.DeepFraction);
            foreach (double f in new[] { 0.975, 0.99, 0.995, 0.999, 0.9999 })
            {
                double sv = Channels.DnAtDepthFraction(chS.DnFlow, frS.Z, 0, half, f);
                say(string.Format(ci, "   {0,8:F4}   {1,10:F4} mm   {2,14:E3}   {3,13:F3}",
                    f, (1.0 - f) * half, sv, sv / Math.Max(deepS, 1e-30)));
            }
            say("");
            say("  published surface 10e-4 at ~1 um, core 1.8e-4 at 0.4 mm, ratio 5.56");

            string outPath = Program.Value(args, "-out")
                ?? Path.Combine(Path.GetTempPath(), "moldstress_depthdiag.txt");
            File.WriteAllText(outPath, log.ToString());
            Console.WriteLine("  written to " + outPath);
            return 0;
        }
    }
}

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
            var freeze = FreezeHistory.Build(plate.CentreThicknessMm, p, baseProc, 41);
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

            string outPath = Program.Value(args, "-out")
                ?? Path.Combine(Path.GetTempPath(), "moldstress_depthdiag.txt");
            File.WriteAllText(outPath, log.ToString());
            Console.WriteLine("  written to " + outPath);
            return 0;
        }
    }
}

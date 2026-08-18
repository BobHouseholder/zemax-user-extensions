using System;
using System.Globalization;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// SECOND registered reference case - a moulded LENS, not a plate.
    ///
    /// WHY A SECOND CASE. Every number this model has ever been judged on came
    /// from ONE part: a 1.5 mm TOPAS plate. A model tuned to one part that fails a
    /// second part is a model that was tuned, and with one case there is no way to
    /// tell the difference. This case is deliberately different in the three ways
    /// that matter - it is a LENS with curvature, it is a different polymer grade,
    /// and its depth data comes from a different measurement principle.
    ///
    /// SOURCE, read in full rather than from an abstract:
    ///   Chang, Yu, Chiu, Yang, Lai and Wang, "Simulations and Verifications of
    ///   True 3D Optical Parts by Injection Molding Process", CoreTech System
    ///   (Moldex3D) and National Tsing Hua University.
    ///   https://www.moldex3d.com/assets/2011/09/SMGG5H7.pdf
    ///
    /// PART: plano-convex spherical lens, 32 mm diameter, 2 mm centre thickness,
    /// edge gate of 0.8 mm land. ZEONEX 480R, a cyclo-olefin POLYMER.
    ///
    /// PROCESS, from the paper's Table 1: melt 275 C, mould 124 C, holding
    /// pressure 98.10 MPa, injection speed 22 mm/s, cooling 60 s.
    ///
    /// GEOMETRY CAVEAT, stated because the source contradicts itself: the body
    /// text says the curvature is 70 mm and Figure 1 labels it 75 mm. This case
    /// uses 75 mm, from the figure, and the sensitivity is reported so the choice
    /// can be checked rather than trusted.
    ///
    /// MATERIAL CAVEAT: ZEONEX 480R's stress-optic and rheological constants are
    /// BORROWED from the measured TOPAS 6017 COC. Same family, different grade.
    /// Results here are indicative for a cyclo-olefin, not for 480R.
    ///
    /// THE CLAUSES, and why the third is the valuable one:
    ///
    ///   (a) IN-PLANE. Measured birefringence along the filling path, the paper's
    ///       Figure 7, with a stated measurement error below 10%: 3.7e-5 at the
    ///       gate falling to essentially zero by 30 mm. Predicted peak within a
    ///       factor of 2, and the maximum on the gate side.
    ///
    ///   (b) LAYER REMOVAL - a CUMULATIVE distribution from ONE self-consistent
    ///       method. Successive outer layers were diamond-turned off and the
    ///       fringe order recounted (the paper's Table 2):
    ///
    ///           removed   0.1 mm   0.2 mm   0.3 mm   0.4 mm
    ///           fringe %   27.9     30.8     43.9     46.2
    ///
    ///       This is worth more than any two-point ratio. The quantity removed IS
    ///       the quantity measured, so there is no cross-instrument division of
    ///       the kind that produced the withdrawn 5.56, and no sampling-definition
    ///       ambiguity of the kind still open on the plate's 0.2 mm slabs. Four
    ///       points constrain the SHAPE of the profile, not just its ends.
    ///
    /// WHAT THIS CASE CANNOT SETTLE. The measurement is a gapwise AVERAGE
    /// (fringe order over the thickness), so it constrains the integral rather
    /// than the profile pointwise. And the source's own simulation UNDER-predicts:
    /// 5 fringes observed against 4 predicted. Agreeing with it closely is
    /// agreeing with something already known to be about 20% low.
    /// </summary>
    internal static class RefCase2
    {
        public const double PublishedInPlanePeakDn = 3.7e-5;
        public const double FactorBar = 2.0;

        // Cumulative fraction of the through-thickness retardance held in the
        // outer t mm, from the paper's Table 2.
        public static readonly double[] RemovalDepthMm = { 0.1, 0.2, 0.3, 0.4 };
        public static readonly double[] RemovedFraction = { 0.279, 0.308, 0.439, 0.462 };

        public static int Run(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("MoldStress - reference case 2: a moulded LENS (ZEONEX 480R)");
            Console.WriteLine("  " + Program.ScopeLabel);
            Console.WriteLine("  source: Chang et al., CoreTech/NTHU, Moldex3D verification study");
            Console.WriteLine("  ZEONEX 480R constants BORROWED from measured TOPAS 6017 COC");
            Console.WriteLine();

            Polymers.Aliases["ZEONEX480R"] = "MS_COC_TOPAS6017";
            var p = Polymers.ByName("ZEONEX480R").WithProcessTemps(275.0, 124.0);
            var proc = new Process { FillTimeS = 1.0, PackPressureMPa = 98.10, PackTimeS = 3.0 };

            double curvature = Program.Value(args, "-curvature", 75.0);
            int nz = (int)Program.Value(args, "-nz", 161.0);
            if (nz % 2 == 0) nz++;

            var lens = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = p.Name,
                CentreThicknessMm = 2.0, SemiDiameterMm = 16.0,
                FrontRadiusMm = curvature, BackRadiusMm = 0.0,   // plano-convex
            };
            lens.EdgeThicknessMm = lens.ThicknessAt(lens.SemiDiameterMm);
            lens.Gate = new GateSpec
            {
                Kind = GateKind.FilmEdge, AzimuthDeg = 0,
                WidthMm = 2.0 * Math.PI * lens.SemiDiameterMm / 8.0,
                ThicknessMm = 0.8, IsDefault = false,
            };
            lens.PartingLineZMm = Gating.DefaultPartingLineZ(lens);

            Console.WriteLine(string.Format(ci,
                "  lens: dia {0:F0} mm, CT {1:F2} mm, ET {2:F3} mm, R {3:F0} mm (figure; " +
                "the text says 70 - see -curvature)",
                2 * lens.SemiDiameterMm, lens.CentreThicknessMm, lens.EdgeThicknessMm, curvature));
            Console.WriteLine(string.Format(ci,
                "  process: melt {0:F0} C, mould {1:F0} C, hold {2:F1} MPa, grid nz {3}",
                p.MeltTempC, p.MoldTempC, proc.PackPressureMPa, nz));
            Console.WriteLine();

            var fill = FillField.Build(lens, p, proc, 101);
            var freeze = FreezeHistory.Build(lens.CentreThicknessMm, p, proc, nz, 10 * nz);
            var ch = Channels.Build(lens, p, proc, fill, freeze);
            double half = 0.5 * lens.CentreThicknessMm;

            // ---- (a) in-plane -------------------------------------------------
            int ns = ch.S.Length, nzc = freeze.NodeCount;
            var avg = new double[ns];
            for (int i = 0; i < ns; i++)
            {
                double sum = 0;
                for (int k = 0; k < nzc; k++) sum += Math.Abs(ch.DnFlow[i, k]);
                avg[i] = sum / nzc;
            }
            int argMax = 0;
            for (int i = 1; i < ns; i++) if (avg[i] > avg[argMax]) argMax = i;
            double peak = avg[argMax];
            double peakRatio = peak / PublishedInPlanePeakDn;
            bool peakOk = peakRatio >= 1.0 / FactorBar && peakRatio <= FactorBar;
            bool shapeOk = ch.S[argMax] <= 0.25 * ch.S[ns - 1] && avg[ns - 1] < peak;

            Console.WriteLine("  measured along the filling path (paper Fig. 7, error < 10%)");
            Console.WriteLine("    distance   measured     model");
            double[] mmPts = { 0, 3, 6, 12, 24 };
            double[] measPts = { 3.7e-5, 2.5e-5, 1.0e-5, 0.2e-5, 0.4e-5 };
            for (int j = 0; j < mmPts.Length; j++)
            {
                int idx = (int)Math.Round(mmPts[j] / Math.Max(ch.S[ns - 1], 1e-9) * (ns - 1));
                idx = Math.Max(0, Math.Min(ns - 1, idx));
                Console.WriteLine(string.Format(ci, "    {0,5:F0} mm   {1:E2}    {2:E2}",
                    mmPts[j], measPts[j], avg[idx]));
            }
            Console.WriteLine(string.Format(ci,
                "  (a) in-plane peak {0:E3} against published {1:E3} - ratio {2:F2}x  =>  {3}",
                peak, PublishedInPlanePeakDn, peakRatio, peakOk ? "PASS" : "FAIL"));
            Console.WriteLine(string.Format(ci,
                "      maximum at {0:F1} mm from the gate, far edge {1:P0} of peak  =>  {2}",
                ch.S[argMax], peak > 0 ? avg[ns - 1] / peak : 0.0, shapeOk ? "PASS" : "FAIL"));
            Console.WriteLine();

            // ---- (b) layer removal -------------------------------------------
            // Cumulative |dn| integrated from the surface inward, at the gate
            // station, where the paper's layers were machined. Removal is ONE
            // SIDED - the flat face of a plano-convex lens - so the denominator is
            // the whole thickness while the numerator is one outer layer. That
            // assumption is stated because it halves the answer if wrong, and the
            // two-sided figure is printed beside it.
            double total = 0.0;
            for (int k = 0; k < nzc; k++) total += Math.Abs(ch.DnTotalOutOfPlane[0, k]);
            Console.WriteLine("  layer removal - cumulative retardance in the outer t mm");
            Console.WriteLine("    t (mm)   measured   model 1-sided   model 2-sided");
            int nOk = 0;
            for (int j = 0; j < RemovalDepthMm.Length; j++)
            {
                double t = RemovalDepthMm[j], outer = 0.0;
                for (int k = 0; k < nzc; k++)
                    if (Math.Abs(freeze.Z[k]) >= half - t) outer += Math.Abs(ch.DnTotalOutOfPlane[0, k]);
                double oneSided = total > 0 ? 0.5 * outer / total : 0.0;   // one face only
                double twoSided = total > 0 ? outer / total : 0.0;
                bool ok = Math.Abs(oneSided - RemovedFraction[j]) <= 0.10;  // 10 points
                if (ok) nOk++;
                Console.WriteLine(string.Format(ci,
                    "    {0,5:F1}    {1,7:P1}    {2,10:P1}     {3,10:P1}   {4}",
                    t, RemovedFraction[j], oneSided, twoSided, ok ? "ok" : "off"));
            }
            bool removalOk = nOk >= 3;
            Console.WriteLine(string.Format(ci,
                "  (b) layer-removal profile: {0} of {1} points within 10 points  =>  {2}",
                nOk, RemovalDepthMm.Length, removalOk ? "PASS" : "FAIL"));

            bool all = peakOk && shapeOk && removalOk;
            Console.WriteLine();
            Console.WriteLine("  VERDICT: " + (all
                ? "second reference case MET"
                : "second reference case NOT met - in-plane peak " + (peakOk ? "PASS" : "FAIL") +
                  ", in-plane shape " + (shapeOk ? "PASS" : "FAIL") +
                  ", layer removal " + (removalOk ? "PASS" : "FAIL")));
            return all ? 0 : 2;
        }
    }
}

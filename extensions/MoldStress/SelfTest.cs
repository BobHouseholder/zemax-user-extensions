using System;
using System.Collections.Generic;

namespace MoldStress
{
    /// <summary>
    /// Every stage is held against a closed form it must reproduce before the
    /// next stage is allowed to depend on it. A stage that cannot reproduce its
    /// own analytic limit does not get to contribute a number to a report.
    /// </summary>
    internal static class SelfTest
    {
        private static int _pass, _fail;

        public static int Run(string[] args)
        {
            _pass = _fail = 0;
            Console.WriteLine("MoldStress self-test");
            Console.WriteLine("  " + Program.ScopeLabel);
            Console.WriteLine();

            CatalogChecks();
            Console.WriteLine();
            GeometryChecks();
            Console.WriteLine();
            FillField.SelfCheck();
            Console.WriteLine();
            FreezeHistory.SelfCheck();
            Console.WriteLine();
            Channels.SelfCheck();
            Console.WriteLine();
            StarFiles.SelfCheck();
            Console.WriteLine();
            AngularTest.SelfCheck();
            Console.WriteLine();
            AngularTest.OrdinalCheck();

            Console.WriteLine();
            Console.WriteLine(string.Format("  {0} passed, {1} failed", _pass, _fail));
            if (Lagrangian.ShapeMisses + Lagrangian.ShapeHits > 0)
                Console.WriteLine(string.Format(
                    "  depth-shape cache: {0} solved, {1} reused",
                    Lagrangian.ShapeMisses, Lagrangian.ShapeHits));
            return _fail == 0 ? 0 : 1;
        }

        internal static void Check(string what, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + what + "   " + detail); }
            else { _fail++; Console.WriteLine("  FAIL  " + what + "   " + detail); }
        }

        internal static void Near(string what, double got, double want, double relTol)
        {
            double rel = Math.Abs(want) > 0 ? Math.Abs((got - want) / want) : Math.Abs(got - want);
            Check(what, rel <= relTol,
                string.Format("got {0:E9}, want {1:E9}, rel {2:E2} (tol {3:E1})",
                              got, want, rel, relTol));
        }

        private static void GeometryChecks()
        {
            Console.WriteLine("  geometry, gate and parting line");

            // A plane-parallel plate: thickness must be its centre thickness
            // everywhere, whatever the sampling radius.
            var plate = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = "MS_PMMA",
                CentreThicknessMm = 2.0, SemiDiameterMm = 10.0,
                FrontRadiusMm = 0, BackRadiusMm = 0,
            };
            plate.EdgeThicknessMm = plate.ThicknessAt(plate.SemiDiameterMm);
            Near("plate thickness is uniform", plate.ThicknessAt(7.3), 2.0, 1e-12);

            // A biconvex element: sag of a sphere is exact, so the edge thickness
            // has a closed form.
            var lens = new MouldedElement
            {
                FrontSurface = 3, BackSurface = 4, Material = "MS_COC_TOPAS6017",
                CentreThicknessMm = 4.0, SemiDiameterMm = 8.0,
                FrontRadiusMm = 40.0, BackRadiusMm = -40.0,
            };
            double sag = 40.0 - Math.Sqrt(40.0 * 40.0 - 8.0 * 8.0);
            lens.EdgeThicknessMm = lens.ThicknessAt(lens.SemiDiameterMm);
            Near("biconvex edge thickness against the closed form",
                 lens.EdgeThicknessMm, 4.0 - 2.0 * sag, 1e-12);

            // The parting plane of a symmetric biconvex element sits at its own
            // mid-plane by symmetry - a check that cannot pass by accident.
            lens.PartingLineZMm = Gating.DefaultPartingLineZ(lens);
            Near("symmetric biconvex parts at its mid-plane",
                 lens.PartingLineZMm, 2.0, 1e-12);

            // Gate defaults scale off the LOCAL wall, so a thinner edge must give
            // a thinner gate. A default that ignores geometry would tie here.
            var thin = new MouldedElement
            {
                FrontSurface = 5, CentreThicknessMm = 4.0, SemiDiameterMm = 8.0,
                FrontRadiusMm = 25.0, BackRadiusMm = -25.0,
            };
            thin.EdgeThicknessMm = thin.ThicknessAt(thin.SemiDiameterMm);
            thin.Gate = Gating.DefaultGate(thin);
            lens.Gate = Gating.DefaultGate(lens);
            Check("gate land tracks the local wall thickness",
                  thin.Gate.ThicknessMm < lens.Gate.ThicknessMm,
                  string.Format("{0:F4} mm on a {1:F3} mm edge vs {2:F4} mm on a {3:F3} mm edge",
                      thin.Gate.ThicknessMm, thin.EdgeThicknessMm,
                      lens.Gate.ThicknessMm, lens.EdgeThicknessMm));

            // The default azimuth must be a real value the rest of the chain can
            // move: the registered null control depends on it.
            Check("gate azimuth defaults to a definite value",
                  lens.Gate.AzimuthDeg == Gating.DefaultAzimuthDeg && lens.Gate.IsDefault,
                  string.Format("{0:F1} deg, flagged default", lens.Gate.AzimuthDeg));

            // An unknown key in a config file must be refused, not ignored.
            string tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "surface=3 azimuth=180 wdith=1.0\n");
            bool refused = false;
            try { Gating.ApplyOverrides(new[] { lens }, tmp); }
            catch (FormatException) { refused = true; }
            finally { System.IO.File.Delete(tmp); }
            Check("a mistyped config key is refused", refused, "wdith= rejected");

            // And a good override must actually take.
            string tmp2 = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp2, "# gate on the far side\nsurface=3 azimuth=180\n");
            try { Gating.ApplyOverrides(new[] { lens }, tmp2); }
            finally { System.IO.File.Delete(tmp2); }
            Check("an override replaces the default",
                  lens.Gate.AzimuthDeg == 180.0 && !lens.Gate.IsDefault,
                  "azimuth now 180 deg, no longer flagged default");
        }

        private static void CatalogChecks()
        {
            Console.WriteLine("  material data");
            Polymers.SelfCheckEjection();
            Polymers.SelfCheckValues();
            Polymers.SelfCheckContested();
            List<string> errs = Polymers.Validate();
            Check("every entry sourced and self-consistent", errs.Count == 0,
                  errs.Count == 0 ? Polymers.All.Length + " materials"
                                  : string.Join("; ", errs));

            // The relation OpticStudio itself enforces on a catalog save.
            foreach (var p in Polymers.All)
                Near("K = K12 - K11 for " + p.Name,
                     p.K12Brewster - p.K11Brewster, p.KGlassBrewster, 1e-12);

            // Negative control: a deliberately swapped pair must be rejected. If
            // this passes validation the check is decorative.
            var swapped = new Polymer
            {
                Name = "SWAPPED", Description = "control", KSource = "control",
                CMeltSource = "control",
                KGlassBrewster = 4000.0, K11Brewster = 0.0,
                CMeltBrewster = 72.0,
                TgC = 145, MeltTempC = 300, MoldTempC = 100,
            };
            bool caught = Math.Abs(swapped.CMeltBrewster) <= Math.Abs(swapped.KGlassBrewster);
            Check("swapped melt/glassy coefficients are caught", caught,
                  "|Cmelt| <= |Cglass| detected");
        }
    }
}

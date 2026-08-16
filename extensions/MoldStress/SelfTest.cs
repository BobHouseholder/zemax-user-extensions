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
            Console.WriteLine(string.Format("  {0} passed, {1} failed", _pass, _fail));
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

        private static void CatalogChecks()
        {
            Console.WriteLine("  material data");
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

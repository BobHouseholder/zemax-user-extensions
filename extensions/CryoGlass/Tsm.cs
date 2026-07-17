using System;
using System.Globalization;

namespace CryoGlass
{
    // The temperature-dependent Sellmeier evaluator and its self-test.
    static class Tsm
    {
        static double Poly(double[] c, double t)
        {
            double v = 0;
            for (int j = c.Length - 1; j >= 0; j--) v = v * t + c[j];
            return v;
        }

        // Resonance strength / wavelength at temperature T (Kelvin).
        public static double SAt(CharmsMaterial m, int i, double tK) => Poly(m.S[i], tK);
        public static double LAt(CharmsMaterial m, int i, double tK) => Poly(m.L[i], tK);

        // Absolute (vacuum) refractive index at lambda (um), T (Kelvin).
        public static double Index(CharmsMaterial m, double lambdaUm, double tK)
        {
            double l2 = lambdaUm * lambdaUm;
            double sum = 0;
            for (int i = 0; i < 3; i++)
            {
                double li = LAt(m, i, tK);
                sum += SAt(m, i, tK) * l2 / (l2 - li * li);
            }
            return Math.Sqrt(1.0 + sum);
        }

        public static bool InRange(CharmsMaterial m, double lambdaUm, double tK)
            => lambdaUm >= m.LambdaMinUm && lambdaUm <= m.LambdaMaxUm
            && tK >= m.TminK && tK <= m.TmaxK;

        // Verify the evaluator against the paper's own measured-index tables.
        // The TSM fits are published with ~1e-4 average absolute residual, so
        // agreement worse than 5e-4 at any anchor means transcription damage.
        public static bool SelfTest(CharmsMaterial m, bool print)
        {
            double worst = 0;
            bool ok = true;
            foreach (var row in m.SelfTest)
            {
                double n = Index(m, row[0], row[1]);
                double err = Math.Abs(n - row[2]);
                worst = Math.Max(worst, err);
                if (err > 5e-4) ok = false;
                if (print)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-10} lam={1,4:0.0} um T={2,5:0.0} K  paper {3:0.00000}  model {4:0.00000}  |d|={5:0.0E+0}",
                        m.Name, row[0], row[1], row[2], n, err));
            }
            if (print)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: worst |dn| vs published table = {1:0.0E+0}  ({2})",
                    m.Name, worst, ok ? "PASS" : "FAIL - refusing to generate catalogs"));
            return ok;
        }
    }
}

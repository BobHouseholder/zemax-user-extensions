using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CryoGlass
{
    // Emits an OpticStudio .AGF glass catalog frozen at a working temperature
    // T0. Because the CHARMS model at fixed T IS a three-term Sellmeier, the
    // catalog dispersion coefficients are exact - no refit, no refit error:
    //   Sellmeier1: n^2 - 1 = SUM K_i lambda^2 / (lambda^2 - L_i)
    //   K_i = S_i(T0),  L_i = lambda_i(T0)^2
    //
    // A LOCAL Schott thermal model (D0,D1,D2,E0,E1; lambda_tk = 0) is least-
    // squares fitted to the TSM surface over T0 +- dT so OpticStudio's own
    // thermal machinery remains usable NEAR T0; its worst fit error over the
    // fit box is reported per glass - beyond the box, regenerate the catalog.
    //
    // Honesty notes written into the catalog and printed:
    // - CHARMS indices are ABSOLUTE (vacuum). Use the catalog with the system
    //   environment at the working temperature and 0 atm, or expect the
    //   air-index correction to be applied on top.
    // - CHARMS carries no thermal-expansion data: TCE is written as 0 and
    //   must be sourced separately before AthermalScan-style analyses.
    static class AgfWriter
    {
        public static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        public static string Write(string path, IEnumerable<CharmsMaterial> mats, double t0K, int fitHalfWidthK, out List<string> report)
        {
            report = new List<string>();
            var sb = new StringBuilder();
            sb.Append("CC CryoGlass-generated catalog from NASA GSFC CHARMS TSM fits\r\n");
            sb.Append(F("CC Working temperature {0:0.##} K ({1:0.##} C); indices are ABSOLUTE (vacuum)\r\n", t0K, t0K - 273.15));
            sb.Append("CC Set the system environment to the working temperature at 0 atm pressure\r\n");
            sb.Append("CC TCE is NOT provided by CHARMS - the 0 written here must be replaced from a\r\n");
            sb.Append("CC separate source before thermal-expansion-sensitive analyses\r\n");

            foreach (var m in mats)
            {
                double tLo = Math.Max(m.TminK, t0K - fitHalfWidthK);
                double tHi = Math.Min(m.TmaxK, t0K + fitHalfWidthK);
                string name = F("{0}_{1:0}K", m.Name, t0K);

                double[] td = FitSchottLocal(m, t0K, tLo, tHi, out double worstFit);
                report.Add(F("{0}: exact Sellmeier1 at {1:0.##} K; local dn/dT fit over {2:0}-{3:0} K worst |dn|={4:0.0E+0}",
                    name, t0K, tLo, tHi, worstFit));

                double nMid = Tsm.Index(m, 0.5 * (m.LambdaMinUm + m.LambdaMaxUm), t0K);
                sb.Append(F("NM {0} 2 0 {1:0.000000} 0.0 0 -1 -1 -1 -1\r\n", name, nMid));
                sb.Append(F("GC CHARMS absolute index at {0:0.##} K; {1}; valid {2:0.###}-{3:0.###} um, fit box {4:0}-{5:0} K\r\n",
                    t0K, m.Source, m.LambdaMinUm, m.LambdaMaxUm, tLo, tHi));
                double k1 = Tsm.SAt(m, 0, t0K), l1 = Sq(Tsm.LAt(m, 0, t0K));
                double k2 = Tsm.SAt(m, 1, t0K), l2 = Sq(Tsm.LAt(m, 1, t0K));
                double k3 = Tsm.SAt(m, 2, t0K), l3 = Sq(Tsm.LAt(m, 2, t0K));
                sb.Append(F("CD {0:E9} {1:E9} {2:E9} {3:E9} {4:E9} {5:E9}\r\n", k1, l1, k2, l2, k3, l3));
                sb.Append(F("TD {0:E6} {1:E6} {2:E6} {3:E6} {4:E6} {5:E6} {6:0.###}\r\n",
                    td[0], td[1], td[2], td[3], td[4], 0.0, t0K - 273.15));
                sb.Append("ED 0.000000E+000 0.000000E+000 0 0 -\r\n");
                sb.Append(F("LD {0:0.####} {1:0.####}\r\n", m.LambdaMinUm, m.LambdaMaxUm));
                sb.Append("IT 0 0 0\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        static double Sq(double v) => v * v;

        // Least-squares fit of the Schott thermal model to the TSM surface:
        //   dn_abs(lambda, dT) = (n0^2-1)/(2 n0) * [D0 dT + D1 dT^2 + D2 dT^3
        //                        + (E0 dT + E1 dT^2)/lambda^2]      (lam_tk=0)
        // Linear in (D0,D1,D2,E0,E1) -> normal equations, 5x5.
        static double[] FitSchottLocal(CharmsMaterial m, double t0, double tLo, double tHi, out double worst)
        {
            var lams = new List<double>();
            for (int i = 0; i <= 8; i++)
                lams.Add(m.LambdaMinUm + (m.LambdaMaxUm - m.LambdaMinUm) * i / 8.0);
            var temps = new List<double>();
            for (int i = 0; i <= 10; i++)
                temps.Add(tLo + (tHi - tLo) * i / 10.0);

            var ata = new double[5, 5];
            var atb = new double[5];
            foreach (var lam in lams)
            {
                double n0 = Tsm.Index(m, lam, t0);
                double scale = (n0 * n0 - 1.0) / (2.0 * n0);
                double il2 = 1.0 / (lam * lam);
                foreach (var t in temps)
                {
                    double dT = t - t0;
                    if (Math.Abs(dT) < 1e-12) continue;
                    double dn = Tsm.Index(m, lam, t) - n0;
                    var row = new[] { scale * dT, scale * dT * dT, scale * dT * dT * dT, scale * dT * il2, scale * dT * dT * il2 };
                    for (int a = 0; a < 5; a++)
                    {
                        atb[a] += row[a] * dn;
                        for (int b = 0; b < 5; b++) ata[a, b] += row[a] * row[b];
                    }
                }
            }
            var x = Solve5(ata, atb);

            worst = 0;
            foreach (var lam in lams)
            {
                double n0 = Tsm.Index(m, lam, t0);
                double scale = (n0 * n0 - 1.0) / (2.0 * n0);
                double il2 = 1.0 / (lam * lam);
                foreach (var t in temps)
                {
                    double dT = t - t0;
                    double model = scale * (x[0] * dT + x[1] * dT * dT + x[2] * dT * dT * dT + (x[3] * dT + x[4] * dT * dT) * il2);
                    worst = Math.Max(worst, Math.Abs(model - (Tsm.Index(m, lam, t) - n0)));
                }
            }
            return x;
        }

        static double[] Solve5(double[,] a, double[] b)
        {
            int n = 5;
            var m = (double[,])a.Clone();
            var x = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
                if (Math.Abs(m[p, c]) < 1e-30) { x[c] = 0; continue; }
                if (p != c)
                {
                    for (int k = 0; k < n; k++) { var t = m[c, k]; m[c, k] = m[p, k]; m[p, k] = t; }
                    { var t = x[c]; x[c] = x[p]; x[p] = t; }
                }
                for (int r = c + 1; r < n; r++)
                {
                    double f = m[r, c] / m[c, c];
                    for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                    x[r] -= f * x[c];
                }
            }
            for (int c = n - 1; c >= 0; c--)
            {
                if (Math.Abs(m[c, c]) < 1e-30) { x[c] = 0; continue; }
                for (int k = c + 1; k < n; k++) x[c] -= m[c, k] * x[k];
                x[c] /= m[c, c];
            }
            return x;
        }
    }
}

using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MoldStress
{
    /// <summary>
    /// Writes MOLDSTRESS.AGF - a glass catalog whose only reason to exist is the
    /// BD record.
    ///
    /// Measured 2026-08-15 across all 51 catalogs shipped with OpticStudio 2026
    /// R1.03: 578 BD records, every one on a glass, and NONE on any of the 64
    /// polymer materials (APEL, ARTON, ZEON, Apollo, SABIC, and PMMA/POLYCARB/
    /// POLYSTYR in MISC). Without a BD record STAR does not refuse the stress
    /// data - through the ZOS-API it accepts zero of the supplied points, returns
    /// success, and reports retardance exactly zero. A stress-free answer for a
    /// stressed part. That is why this file is a prerequisite and not an extra.
    ///
    /// AGF record layout used here (OpticStudio User Manual, and matching the
    /// shipped SCHOTT/CDGM catalogs):
    ///   NM name 1 0 nd vd 0 0 0
    ///   GC comment
    ///   ED tce 0 density 0 0 0
    ///   CD six Sellmeier-1 style coefficients   (here: Schott formula, code 1)
    ///   TD six thermal coefficients + reference temperature
    ///   OD -1 x6
    ///   LD lambda_min lambda_max
    ///   BD wavelength_um K K11 K12          <- the record this tool exists for
    /// </summary>
    internal static class CatalogWriter
    {
        /// <summary>
        /// The wavelength validity of every MS_* glass, in microns - the LD
        /// record each row carries. NARROW BY CONSTRUCTION: the dispersion is a
        /// two-coefficient fit from nd and vd alone, which is a visible-band
        /// statement, and extending the claimed range without dispersion data
        /// would fabricate an index. Measured 2026-08-22: at 1.2 um the
        /// extrapolated formula put ~185 waves of error on a 10 mm part and FFT
        /// MTF refused to compute - which is how this constant earned its
        /// checks in Convert.Prepare and Runner.
        /// </summary>
        internal const double LambdaMinUm = 0.4;
        internal const double LambdaMaxUm = 1.0;

        /// <summary>The wavelengths outside the MS validity range, or an empty
        /// list. Pure, so both arms are testable without a session.</summary>
        internal static List<double> WavelengthsOutOfRange(IEnumerable<double> um)
        {
            var bad = new List<double>();
            if (um == null) return bad;
            foreach (double w in um)
                if (!(w >= LambdaMinUm && w <= LambdaMaxUm)) bad.Add(w);
            return bad;
        }

        public const string CatalogName = "MOLDSTRESS";

        public static string Write(string path)
        {
            var errs = Polymers.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    "refusing to write a catalog with unsourced or inconsistent data:" +
                    Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", errs));

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("CC MoldStress polymer stress-optic catalog, generated " +
                          DateTime.UtcNow.ToString("yyyy-MM-dd", ci) + " UTC");
            sb.AppendLine("CC PROVISIONAL stress-optical constants - representative of the polymer");
            sb.AppendLine("CC family, not measured for a specific grade. Every value carries its");
            sb.AppendLine("CC source in the GC comment of its material. Do not use for a tolerance.");

            foreach (var p in Polymers.All)
            {
                // Schott dispersion formula (code 1) fitted from nd/vd by a
                // two-term approximation is not honest enough for a catalog, so
                // the index is carried as a constant-dispersion Sellmeier fit
                // anchored on nd and vd. It is adequate here because this catalog
                // exists for its BD record - the design should carry the real
                // material for its index, and MoldStress says so on every run.
                double nd = p.Nd, vd = p.Vd;
                double nF = nd + (nd - 1.0) / (2.0 * vd);
                double nC = nd - (nd - 1.0) / (2.0 * vd);
                // Two-term Sellmeier through (nC,0.6563), (nd,0.5876), (nF,0.4861)
                double b1, c1;
                FitSellmeier(nd, nF, nC, out b1, out c1);

                sb.AppendLine(string.Format(ci,
                    "NM {0} 2 0 {1:F6} {2:F4} 0 0 0", p.Name, nd, vd));
                sb.AppendLine("GC " + p.Description + " | K: " + p.KSource +
                              (p.Provisional ? " | PROVISIONAL" : ""));
                sb.AppendLine(string.Format(ci,
                    "ED {0:E6} 0.000000E+000 {1:F4} 0 0 0", p.CtePerK * 1e6, p.DensityGPerCm3));
                sb.AppendLine(string.Format(ci,
                    "CD {0:E12} {1:E12} 0.000000000000E+000 0.000000000000E+000 0.000000000000E+000 0.000000000000E+000",
                    b1, c1));
                sb.AppendLine("TD 0 0 0 0 0 0 2.0000E+001");
                sb.AppendLine("OD -1 -1 -1 -1 -1 -1");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "LD {0:E6} {1:E6}", LambdaMinUm, LambdaMaxUm));
                // K is written as K12 - K11 so a catalog reload cannot disagree
                // with itself; OpticStudio recomputes it on save from the same rule.
                sb.AppendLine(string.Format(ci, "BD {0:F3} {1:F4} {2:F4} {3:F4}",
                    p.WavelengthUm, p.K12Brewster - p.K11Brewster, p.K11Brewster, p.K12Brewster));
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>Two-term Sellmeier through the d/F/C indices.</summary>
        private static void FitSellmeier(double nd, double nF, double nC,
                                         out double b1, out double c1)
        {
            // n^2 - 1 = b1 * L / (L - c1), L = lambda^2
            double Ld = 0.5876 * 0.5876, Lf = 0.4861 * 0.4861;
            double yd = nd * nd - 1.0, yf = nF * nF - 1.0;
            // Solve the two equations for b1 and c1.
            c1 = (Ld * Lf * (yf - yd)) / (yf * Lf - yd * Ld);
            b1 = yd * (Ld - c1) / Ld;
        }

        /// <summary>
        /// Where OpticStudio looks for catalogs. Writing anywhere else produces a
        /// file the design cannot see, which fails in exactly the silent way this
        /// catalog exists to prevent.
        /// </summary>
        public static string DefaultDirectory()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string cat = Path.Combine(docs, "Zemax", "Glasscat");
            return Directory.Exists(cat) ? cat : docs;
        }
    }
}

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
                double b1, c1;
                FitSellmeier(nd, vd, out b1, out c1);

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

        /// <summary>
        /// One-term Sellmeier reproducing the two things an nd/vd row promises:
        /// the index at d, and the Abbe number. n^2 - 1 = b1 * L / (L - c1).
        ///
        /// REWRITTEN 2026-08-29 after the shipped catalogue was found carrying
        /// INVERTED dispersion - MS_PMMA at Vd -80.6 against real PMMA's +57.4,
        /// index RISING with wavelength, which no transparent polymer does. Two
        /// defects, and the second is why fixing the first is not enough:
        ///
        ///   (a) The old solve was wrong twice in one line - flipped numerator
        ///       sign, and a denominator pairing yf with Lf instead of the cross
        ///       terms - giving c1 = -0.008001 for PMMA where the same fit done
        ///       correctly gives +0.007574. A negative c1 is what inverts the
        ///       curve.
        ///
        ///   (b) It fitted through nF and nC reconstructed as nd +/- (nd-1)/(2vd),
        ///       which places nd exactly MIDWAY between them. Real dispersion is
        ///       curved and nd sits nearer C - 2.35:1 for PMMA - so even the
        ///       corrected algebra returns Vd +80.6 against the +57.4 declared on
        ///       the same row. The row would have disagreed with itself.
        ///
        /// So the reconstruction is gone. b1 follows in closed form from the nd
        /// constraint; c1 is bisected until the fitted nF - nC equals (nd-1)/vd.
        /// The spread rises monotonically in c1 - zero at c1 = 0, where the medium
        /// is dispersionless, and unbounded as c1 approaches Lf - so the root is
        /// bracketed by construction and 100 halvings resolve it to ~1e-31.
        ///
        /// This is a single-resonance fit anchored on two numbers, not a
        /// measured dispersion curve. Against the MISC catalogue's own PMMA it
        /// lands within ~1e-4 at F and ~2e-5 at C, which is the same order as
        /// the moulding index change it carries - stated here because that is
        /// the honest limit of an nd/vd row, and it is why the run refuses
        /// polychromatic results it cannot stand behind.
        /// </summary>
        internal static void FitSellmeier(double nd, double vd,
                                          out double b1, out double c1)
        {
            double Ld = LambdaD * LambdaD;
            double Lf = LambdaF * LambdaF;
            double Lc = LambdaC * LambdaC;
            double yd = nd * nd - 1.0;
            double target = (nd - 1.0) / vd;          // the required nF - nC

            double lo = 0.0, hi = 0.999 * Lf;
            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5 * (lo + hi);
                double b = yd * (Ld - mid) / Ld;
                double nFmid = Math.Sqrt(1.0 + b * Lf / (Lf - mid));
                double nCmid = Math.Sqrt(1.0 + b * Lc / (Lc - mid));
                if (nFmid - nCmid < target) lo = mid; else hi = mid;
            }
            c1 = 0.5 * (lo + hi);
            b1 = yd * (Ld - c1) / Ld;
        }

        /// <summary>The d, F and C lines, in microns. Named because the fit and
        /// its self-tests must use the SAME three, and the old code carried
        /// 0.5876/0.4861 rounded inline while the systems it fitted for used the
        /// full values.</summary>
        internal const double LambdaD = 0.5875618;
        internal const double LambdaF = 0.4861327;
        internal const double LambdaC = 0.6562725;

        /// <summary>Index at a wavelength from a fitted pair, so a caller can
        /// check the fit rather than trust it.</summary>
        internal static double IndexAt(double b1, double c1, double lambdaUm)
        {
            double L = lambdaUm * lambdaUm;
            return Math.Sqrt(1.0 + b1 * L / (L - c1));
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

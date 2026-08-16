using System;
using System.IO;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// MoldStress - estimates the refractive-index change and stress
    /// birefringence that injection moulding leaves in a plastic element, and
    /// applies them through OpticStudio's STAR module so the change in optical
    /// performance can be read directly.
    ///
    /// ESTIMATE. NOT A MOULD-FLOW SIMULATION. NOT VALIDATED AGAINST A MOULDED
    /// PART. That label is on every artifact this tool writes, deliberately.
    /// Commercial mould-flow packages (Moldex3D Optics, Autodesk Moldflow
    /// Insight) solve this properly; this tool exists for the designer who has
    /// OpticStudio and STAR and no mould-flow seat.
    /// </summary>
    internal static class Program
    {
        public const string ScopeLabel =
            "ESTIMATE - not a mould-flow simulation, not validated against a moulded part";

        private static int Main(string[] args)
        {
            try
            {
                string mode = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
                if (Has(args, "-h") || Has(args, "-help") || mode == "help")
                {
                    Usage();
                    return 0;
                }

                if (Has(args, "-writecatalog")) return WriteCatalog(args);
                if (Has(args, "-selftest")) return SelfTest.Run(args);

                Usage();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MoldStress: " + ex.Message);
                return 1;
            }
        }

        private static int WriteCatalog(string[] args)
        {
            string outPath = Value(args, "-out")
                ?? Path.Combine(CatalogWriter.DefaultDirectory(),
                                CatalogWriter.CatalogName + ".AGF");
            string written = CatalogWriter.Write(outPath);

            Console.WriteLine("MoldStress polymer stress-optic catalog");
            Console.WriteLine("  " + ScopeLabel);
            Console.WriteLine();
            Console.WriteLine("  wrote " + written);
            Console.WriteLine();
            Console.WriteLine(string.Format("  {0,-22} {1,8} {2,8} {3,8}   {4}",
                "material", "K", "K11", "K12", "glassy coefficient source"));
            foreach (var p in Polymers.All)
            {
                Console.WriteLine(string.Format("  {0,-22} {1,8:F3} {2,8:F3} {3,8:F3}   {4}",
                    p.Name, p.KGlassBrewster, p.K11Brewster, p.K12Brewster,
                    p.Provisional ? "PROVISIONAL" : "measured"));
            }
            Console.WriteLine();
            Console.WriteLine("  Units are 1e-6 mm^2/N (== 1e-12 /Pa == Brewster), which is what");
            Console.WriteLine("  OpticStudio expects. K = K12 - K11 by construction.");
            Console.WriteLine();
            Console.WriteLine("  These are the GLASSY coefficients. The melt coefficients, which");
            Console.WriteLine("  are 2-3 orders larger and describe frozen-in orientation rather");
            Console.WriteLine("  than stress, are deliberately NOT in the catalog - MoldStress");
            Console.WriteLine("  converts orientation to an equivalent stress instead.");
            Console.WriteLine();
            Console.WriteLine("  Load it in OpticStudio: System Explorer > Material Catalogs > add");
            Console.WriteLine("  '" + CatalogWriter.CatalogName + "'.");
            return 0;
        }

        internal static bool Has(string[] a, string flag)
        {
            return a.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
        }

        internal static string Value(string[] a, string flag)
        {
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase))
                    return a[i + 1];
            return null;
        }

        internal static double Value(string[] a, string flag, double dflt)
        {
            string s = Value(a, flag);
            double v;
            return s != null && double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : dflt;
        }

        private static void Usage()
        {
            Console.WriteLine("MoldStress - injection-moulding index change and stress birefringence");
            Console.WriteLine("  " + ScopeLabel);
            Console.WriteLine();
            Console.WriteLine("  -writecatalog [-out <file.agf>]");
            Console.WriteLine("        Write the polymer stress-optic catalog. No shipped polymer");
            Console.WriteLine("        carries a BD record, and without one STAR silently returns");
            Console.WriteLine("        zero retardance, so this is a prerequisite, not an extra.");
            Console.WriteLine();
            Console.WriteLine("  -selftest");
            Console.WriteLine("        Run every stage against its closed form. Exits non-zero on");
            Console.WriteLine("        any disagreement. Needs no OpticStudio session.");
        }
    }
}

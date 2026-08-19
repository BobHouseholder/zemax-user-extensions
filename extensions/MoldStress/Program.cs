using System;
using System.Globalization;
using System.Collections.Generic;
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

                // A RIBBON LAUNCH ARRIVES WITH NO COMMAND LINE AT ALL.
                // OpticStudio offers no way to supply one, so an extension whose
                // no-argument path prints usage does nothing when its button is
                // pressed - which is what this one did, while the README
                // advertised ribbon operation. With no arguments and no mode,
                // attach to the open system and run the whole chain on it.
                if (args.Length == 0)
                    return Runner.Run(new[] { "-ribbon" });

                int badArg = RejectUnknownArgs(args);
                if (badArg != 0) return badArg;

                if (Has(args, "-writecatalog")) return WriteCatalog(args);
                if (Has(args, "-selftest")) return SelfTest.Run(args);
                if (Has(args, "-gates")) return Gates(args);
                if (Has(args, "-run")) return Runner.Run(args);
                if (Has(args, "-refquench")) return RefQuench.Run(args);
                if (Has(args, "-refcase2")) return RefCase2.Run(args);
                if (Has(args, "-refcase")) return RefCase.Run(args);
                if (Has(args, "-depthdiag")) return DepthDiag.Run(args);
                if (Has(args, "-lagrangian")) return Lagrangian.Run(args);

                // An UNRECOGNISED argument used to print usage and exit 0 - the
                // does-nothing-reports-success pattern this project keeps meeting.
                // It cost a ribbon test in this very session: `-quiet` alone made
                // args non-empty, missed every mode, printed help and returned 0
                // as though it had worked.
                Console.Error.WriteLine("MoldStress: no mode recognised in: " +
                                        string.Join(" ", args));
                Console.Error.WriteLine();
                Usage();
                return UsageError;
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
            // A CONTESTED constant is invisible in the table above, because a
            // number with a source beside it looks settled whether or not anyone
            // disagrees with it. Printed separately so it cannot be skimmed past.
            var contested = Polymers.All.Where(x => !string.IsNullOrEmpty(x.CMeltContested)).ToList();
            if (contested.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  CONTESTED melt stress-optical coefficients ("
                                  + contested.Count + "):");
                foreach (var p2 in contested)
                    Console.WriteLine("    " + p2.Name + " (" + p2.CMeltBrewster.ToString("F0")
                                      + " Br): " + p2.CMeltContested);
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

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Gates(string[] args)
        {
            Session.Locate();
            return GatesConnected(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int GatesConnected(string[] args)
        {
            string file = Value(args, "-file");
            var app = Session.Connect(file);
            try
            {
                var sys = app.PrimarySystem;
                var extra = (Value(args, "-materials") ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var els = Session.FindElements(sys, extra);
                Gating.ApplyOverrides(els, Value(args, "-gateconfig"));

                Console.WriteLine("MoldStress gate and parting-line defaults");
                Console.WriteLine("  " + ScopeLabel);
                Console.WriteLine("  system: " + (string.IsNullOrEmpty(sys.SystemFile)
                                                  ? "(unsaved)" : sys.SystemFile));
                Console.WriteLine();
                if (els.Count == 0)
                {
                    Console.WriteLine("  no mouldable element found. MoldStress only treats materials");
                    Console.WriteLine("  it has stress-optic data for; name others with -materials.");
                    Console.WriteLine("  known: " + string.Join(", ",
                        Polymers.All.Select(p => p.Name)));
                    return 0;
                }
                foreach (var e in els) { Session.Describe(e); Console.WriteLine(); }
                Console.WriteLine("  Override any of it per element with -gateconfig <file>, one line each:");
                Console.WriteLine("      surface=3 azimuth=180 kind=ring width=1.2 thickness=0.5");
                return 0;
            }
            finally
            {
                if (!string.IsNullOrEmpty(file)) { try { app.CloseApplication(); } catch { } }
            }
        }

        /// <summary>
        /// Exit code for a bad command line, kept DISTINCT from 2.
        ///
        /// 2 already means "the registered criterion is not met", which is a
        /// legitimate result. Reusing it for argument errors makes those two
        /// indistinguishable to a caller, so a scripted run that silently
        /// mistyped a flag reads as an honest failing measurement. 64 is the
        /// conventional EX_USAGE.
        /// </summary>
        internal const int UsageError = 64;

        /// <summary>Flags that consume the following token as their value.</summary>
        private static readonly string[] ValueFlags = {
            "-file", "-filltime", "-fountain", "-frontmode", "-gateconfig",
            "-materials", "-melttemp", "-moldtemp", "-nz", "-shape-nodes", "-shape-particles", "-shape-steps", "-ti", "-tc", "-nzexport",
            "-curvature", "-lambdascale", "-out", "-outdir", "-packpressure", "-packtime",
            "-particles",
        };

        /// <summary>Flags that stand alone.</summary>
        private static readonly string[] BoolFlags = {
            "-complementary", "-deposition-decay", "-deposition-support",
            "-depthdiag", "-directindex", "-eulerian-depth",
            "-gates", "-h", "-help", "-quiet",
            "-lagrangian", "-lagrangian-depth", "-refquench",
            "-refcase", "-refcase2", "-relax-below-tg", "-ribbon",
            "-run", "-selftest",
            "-thinned-lambda",
            "-writecatalog",
        };

        /// <summary>
        /// Refuse an argument this tool does not read, instead of absorbing it.
        ///
        /// Main already refuses a command line with NO recognised mode. It did
        /// not refuse an unrecognised flag ALONGSIDE a good mode, so
        /// `-refcase -vitrify` ran the plain reference case and reported success -
        /// the operator believes they measured one configuration and measured
        /// another. That is the same does-nothing-reports-success pattern the
        /// mode check was added for, one level down, and it bit on 2026-08-17
        /// when a removed flag kept being passed and kept printing the default's
        /// numbers.
        ///
        /// A misspelling is the common case and the dangerous one: `-thinned-lamda`
        /// is not a no-op, it is a silent substitution of the default.
        /// </summary>
        internal static int RejectUnknownArgs(string[] a)
        {
            var vf = new HashSet<string>(ValueFlags, StringComparer.OrdinalIgnoreCase);
            var bf = new HashSet<string>(BoolFlags, StringComparer.OrdinalIgnoreCase);
            var unknown = new List<string>();

            for (int i = 0; i < a.Length; i++)
            {
                string t = a[i];
                if (t.Length == 0) continue;
                bool looksLikeFlag = t[0] == '-' && !IsNumeric(t);

                if (looksLikeFlag)
                {
                    if (vf.Contains(t))
                    {
                        // Consume the value, but only if one is actually there -
                        // a value flag at the end of the line must not swallow
                        // the bounds check.
                        if (i + 1 < a.Length &&
                            !(a[i + 1].StartsWith("-") && !IsNumeric(a[i + 1]))) i++;
                    }
                    else if (!bf.Contains(t)) unknown.Add(t);
                }
                else if (!string.Equals(t, "help", StringComparison.OrdinalIgnoreCase))
                {
                    unknown.Add(t);
                }
            }

            if (unknown.Count == 0) return 0;
            Console.Error.WriteLine("MoldStress: unrecognised argument" +
                                    (unknown.Count > 1 ? "s: " : ": ") +
                                    string.Join(" ", unknown.ToArray()));
            Console.Error.WriteLine("  Refusing rather than running a different " +
                                    "configuration than you asked for.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  flags taking a value: " + string.Join(" ", ValueFlags));
            Console.Error.WriteLine("  flags standing alone: " + string.Join(" ", BoolFlags));
            return UsageError;
        }

        private static bool IsNumeric(string t)
        {
            double d;
            return double.TryParse(t, System.Globalization.NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out d);
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
            Console.WriteLine("  -gates [-file <lens.zmx>] [-gateconfig <file>] [-materials A,B]");
            Console.WriteLine("        Report the gate and parting line chosen for every mouldable");
            Console.WriteLine("        element: a single edge gate at +Y sized off the local wall,");
            Console.WriteLine("        a ring gate above 12 mm semi-diameter, and the parting plane");
            Console.WriteLine("        at the rim. Override any of it per element.");
            Console.WriteLine();
            Console.WriteLine("  -run [-file <lens.zmx>] [-gateconfig <f>] [-outdir <d>]");
            Console.WriteLine("       [-filltime s] [-packpressure MPa] [-packtime s]");
            Console.WriteLine("       [-melttemp C] [-moldtemp C]");
            Console.WriteLine("        The whole chain: gate, fill field, freeze history, the three");
            Console.WriteLine("        channels, STAR stress and index files, loaded and applied,");
            Console.WriteLine("        with the performance change against a baseline measured");
            Console.WriteLine("        before anything was imported.");
            Console.WriteLine();
            Console.WriteLine("  -selftest");
            Console.WriteLine("        Run every stage against its closed form. Exits non-zero on");
            Console.WriteLine("        any disagreement. Needs no OpticStudio session.");
        }
    }
}

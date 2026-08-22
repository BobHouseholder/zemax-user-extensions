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
            // FIRST STATEMENT, BEFORE ANYTHING CAN FAIL. Two ribbon clicks in a
            // row appeared to do nothing (2026-08-22), and after the first fix
            // there was still no evidence anywhere of the process having run at
            // all. This breadcrumb splits that world cleanly: if the file exists
            // after a click, the exe ran and the recorded arguments say which
            // path it took; if it does not, OpticStudio never started the exe
            // and the fault is upstream of this program entirely.
            try
            {
                string bdir = Path.Combine(Path.GetTempPath(), "moldstress");
                Directory.CreateDirectory(bdir);
                File.AppendAllText(Path.Combine(bdir, "launch-log.txt"),
                    string.Format("{0:u}  args=[{1}]  cwd={2}",
                        DateTime.UtcNow, string.Join(" | ", args),
                        Environment.CurrentDirectory) + Environment.NewLine);
            }
            catch { }

            try
            {
                string mode = args.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
                if (Has(args, "-h") || Has(args, "-help") || mode == "help")
                {
                    Usage();
                    return 0;
                }

                // A RIBBON LAUNCH DOES NOT ARRIVE WITH AN EMPTY COMMAND LINE.
                // This block previously said it did - an assumption, and it cost
                // two silent clicks on 2026-08-22 before the launch log measured
                // the truth: OpticStudio passes exactly
                //
                //     -zpid={14212} -zplt={Extension} -zsid={100003}
                //
                // (its own process id, the launch type, and a session id). Those
                // arguments sailed past the empty-args test into the
                // unknown-argument refusal, which printed usage to a console that
                // does not exist and exited 64. The sibling AthermalScan has
                // parsed this exact triple as "host launched" all along.
                if (args.Length == 0 || IsHostLaunch(args))
                    return Runner.Run(new[] { "-ribbon" });

                int badArg = RejectUnknownArgs(args);
                if (badArg != 0) return badArg;

                if (Has(args, "-writecatalog")) return WriteCatalog(args);
                if (Has(args, "-selftest")) return SelfTest.Run(args);
                if (Has(args, "-gates")) return Gates(args);
                if (Has(args, "-run")) return Runner.Run(args);
                if (Has(args, "-refplate")) return RefPlate.Run(args);
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

        /// <summary>
        /// TRUE when every argument is one of the -zpid / -zplt / -zsid markers
        /// OpticStudio attaches when IT launches an extension from the ribbon
        /// (measured 2026-08-22, %TEMP%\moldstress\launch-log.txt; AthermalScan
        /// documents the same triple). ALL arguments must match: a command line
        /// that mixes a host marker with anything else is a human invocation
        /// with a typo, and belongs to the strict CLI path that refuses it.
        /// </summary>
        internal static bool IsHostLaunch(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            foreach (var a in args)
            {
                string t = (a ?? "").TrimStart('-', '/');
                bool host = t.StartsWith("zpid", StringComparison.OrdinalIgnoreCase)
                         || t.StartsWith("zplt", StringComparison.OrdinalIgnoreCase)
                         || t.StartsWith("zsid", StringComparison.OrdinalIgnoreCase);
                if (!host) return false;
            }
            return true;
        }

        /// <summary>The one flag -writecatalog reads. It is `-out`, not
        /// `-outdir`, and on 2026-08-21 `-writecatalog -outdir <path>` silently
        /// ignored the argument and wrote to the default location - which is how
        /// this mode's turn came around.</summary>
        internal static readonly string[] CatalogReadsFlags = { "-out" };

        private static int WriteCatalog(string[] args)
        {
            int badForMode = RejectFlagsNotReadBy(args, CatalogReadsFlags, "-writecatalog");
            if (badForMode != 0) return badForMode;

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

        /// <summary>Every flag -gates reads. See Runner.ReadsFlags for why this
        /// must track the reads in both directions.</summary>
        internal static readonly string[] GatesReadsFlags =
        {
            "-file", "-gateconfig", "-materials",
        };

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Gates(string[] args)
        {
            int badForMode = RejectFlagsNotReadBy(args, GatesReadsFlags, "-gates");
            if (badForMode != 0) return badForMode;

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
        /// <summary>The two registries are INTERNAL so the self-test can derive
        /// a mode's "flags it does not read" from the real universe of flags
        /// rather than from a hand-picked triple. Hardcoding that triple across
        /// every mode is what produced four false failures on 2026-08-21: -run
        /// genuinely reads -melttemp, -moldtemp and -directindex, and the test
        /// asserted it must refuse them.</summary>
        internal static readonly string[] ValueFlags = {
            "-file", "-filltime", "-fountain", "-frontmode", "-gateconfig",
            "-materials", "-melttemp", "-moldtemp", "-gatewidth", "-packfrac", "-nz", "-shape-nodes", "-shape-particles", "-shape-steps", "-ti", "-tc", "-nzexport",
            "-curvature", "-lambdascale", "-out", "-outdir", "-packpressure", "-packtime",
            "-particles", "-station", "-semidia", "-gatethick", "-ejecttime", "-nt", "-changeover",
        };

        /// <summary>Flags that stand alone.</summary>
        /// <summary>
        /// Refuse a flag this MODE does not read, even when the flag is valid
        /// somewhere else in the tool.
        ///
        /// `RejectUnknownArgs` asks whether a flag EXISTS. That is not the same
        /// question, and the difference has now bitten three times in one day:
        /// -fountain and friends were accepted by -refcase2 and ignored by it
        /// until they were wired; -shape-nodes returned identical numbers because
        /// the clause samples the one station where interpolation cannot act; and
        /// -packtime was swept across 3 to 40 seconds on -refcase2, produced
        /// identical output at every value, and was never read at all - which
        /// made the sweep vacuous and nearly produced a wrong conclusion about a
        /// mechanism.
        ///
        /// A silently ignored flag is worse than an unknown one. An unknown flag
        /// stops the run; an ignored flag returns a confident number for a
        /// configuration nobody ran.
        /// </summary>
        public static int RejectFlagsNotReadBy(string[] args, string[] readsHere, string mode)
        {
            var known = new HashSet<string>(readsHere, StringComparer.OrdinalIgnoreCase);
            foreach (var a in args)
            {
                if (a == null || !a.StartsWith("-")) continue;
                if (string.Equals(a, mode, StringComparison.OrdinalIgnoreCase)) continue;
                if (known.Contains(a)) continue;
                if (string.Equals(a, "-quiet", StringComparison.OrdinalIgnoreCase)) continue;
                Console.Error.WriteLine(
                    "MoldStress: '" + a + "' is a valid flag but " + mode + " does not read it. "
                    + "It would have been ignored silently, so the run is refused instead.");
                Console.Error.WriteLine("  " + mode + " reads: " + string.Join(" ", readsHere));
                return UsageError;
            }
            return 0;
        }

        internal static readonly string[] BoolFlags = {
            "-complementary", "-deposition-decay", "-deposition-support",
            "-depthdiag", "-directindex", "-eulerian-depth", "-incremental-thermal", "-narrowing", "-normal-stress", "-packing-orientation", "-snapshot",
            "-gates", "-h", "-help", "-quiet",
            "-lagrangian", "-lagrangian-depth", "-refquench", "-refplate",
            "-prepare", "-refcase", "-refcase2", "-relax-below-tg", "-ribbon", "-freeplate", "-adhered", "-pressure-vitrification", "-allow-nonspherical", "-thermal-orientation",
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
            Console.WriteLine("       [-nt n] [-allow-nonspherical]");
            Console.WriteLine("        The whole chain: gate, fill field, freeze history, the three");
            Console.WriteLine("        channels, STAR stress and index files, loaded and applied,");
            Console.WriteLine("        with the performance change against a baseline measured");
            Console.WriteLine("        before anything was imported.");
            Console.WriteLine("        The cavity profile is the real sag - base radius, conic,");
            Console.WriteLine("        and even or odd aspheric terms. A surface type whose shape");
            Console.WriteLine("        cannot be read at all is REFUSED; -allow-nonspherical");
            Console.WriteLine("        substitutes its base radius and says so.");
            Console.WriteLine();
            Console.WriteLine("  -selftest");
            Console.WriteLine("        Run every stage against its closed form. Exits non-zero on");
            Console.WriteLine("        any disagreement. Needs no OpticStudio session.");
        }
    }
}

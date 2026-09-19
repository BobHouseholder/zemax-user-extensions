using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FootprintDxf
{
    // ============================================================
    // FootprintDxf - what this program does (plain words)
    // ============================================================
    // Imagine light blobs (footprints) landing on each lens surface.
    // Mech CAD folks want those outlines as a DXF drawing. OpticStudio
    // has no built-in DXF export, so we shoot a grid of rays (plus a
    // dense ring around the pupil edge), keep the hits that land, wrap
    // a rubber-band (convex hull) around them, and write one closed
    // polyline per surface. We also write a PNG preview of the same
    // shapes. The lens file is never changed.
    // Optional extras: per-field hulls, clear-aperture overlays, or
    // map everything into one global X/Y frame for assembly CAD.
    // Run from User Extensions, or -file / -out from a shell.
    // Forum ask: community.zemax.com ... export-beam-footprints ... 5991
    // ============================================================

    // Switches from the command line or the settings window.
    class Options
    {
        public string FilePath;
        public string OutPath;
        public int Rays = 21;
        // 0 = auto -> max(128, Rays*8). Explicit override via -rimrays / dialog.
        public int RimRays = 0;
        public string Surfaces = "all";
        public bool IncludeImage;
        public string Fields = "all";
        public string Wave = "all"; // primary|all
        public bool Rim;
        public bool PerField;
        public bool Global;
        public bool Aperture;
        public bool NoPng;
        public bool Quiet;
        public bool NoDialog;
        public bool SelfTest;
        public readonly HashSet<string> Explicit =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // How many points around the pupil rim: use your number, or auto max(128, Rays*8).
        public int EffectiveRimRays()
        {
            int n = RimRays > 0 ? RimRays : Math.Max(128, Rays * 8);
            if (n < 16) n = 16;
            if (n > 1024) n = 1024;
            return n;
        }
    }

    partial class Program
    {
        static Options Opts = new Options();
        static ZOSAPI.IZOSAPI_Application App;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Start here: find OpticStudio (or run -selftest with no OpticStudio), then export.
        static void Main(string[] args)
        {
            try { ParseArgs(args); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            if (Opts.SelfTest)
            {
                string detail;
                if (!ConvexHull.SelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: convex hull OK (" + detail + ")");
                if (!OrderAsClosedRingSelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: ring order OK (" + detail + ")");
                if (!LayerNameSelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: layer names OK (" + detail + ")");
                if (!UnitsMapSelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: units/text OK (" + detail + ")");
                if (!GlobalTransformSelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: global transform OK (" + detail + ")");
                if (!ApertureRingSelfCheck(out detail))
                {
                    Console.WriteLine("FATAL: selftest failed: " + detail);
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine("selftest: aperture rings OK (" + detail + ")");
                return;
            }

            string zosError;
            if (!ZemaxLocator.TryInitialize(out zosError))
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation."
                    + (zosError == null ? "" : " " + zosError));
                Environment.ExitCode = 1;
                return;
            }
            try { Run(); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        // Read the flags you typed (-out, -rays, -surfaces, ...).
        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string al = a.ToLowerInvariant();
                string next() => (i + 1 < args.Length) ? args[++i] : null;
                switch (al)
                {
                    case "-out": Opts.OutPath = next(); Opts.Explicit.Add("out"); break;
                    case "-file": Opts.FilePath = next(); break;
                    case "-rays": Opts.Rays = ParseInt(next(), Opts.Rays); Opts.Explicit.Add("rays"); break;
                    case "-rimrays": Opts.RimRays = ParseInt(next(), Opts.RimRays); Opts.Explicit.Add("rimrays"); break;
                    case "-surfaces": Opts.Surfaces = next() ?? "all"; Opts.Explicit.Add("surfaces"); break;
                    case "-includeimage": Opts.IncludeImage = true; Opts.Explicit.Add("includeimage"); break;
                    case "-fields": Opts.Fields = next() ?? "all"; Opts.Explicit.Add("fields"); break;
                    case "-wave": Opts.Wave = next() ?? "all"; Opts.Explicit.Add("wave"); break;
                    case "-rim": Opts.Rim = true; Opts.Explicit.Add("rim"); break;
                    case "-perfield": Opts.PerField = true; Opts.Explicit.Add("perfield"); break;
                    case "-global": Opts.Global = true; Opts.Explicit.Add("global"); break;
                    case "-aperture": Opts.Aperture = true; Opts.Explicit.Add("aperture"); break;
                    case "-nopng": Opts.NoPng = true; Opts.Explicit.Add("nopng"); break;
                    case "-quiet": Opts.Quiet = true; Opts.Explicit.Add("quiet"); break;
                    case "-nodialog": Opts.NoDialog = true; break;
                    case "-selftest": Opts.SelfTest = true; break;
                    default:
                        if (al.StartsWith("-z")) break;
                        if (al.StartsWith("-"))
                            throw new Exception("unknown flag " + a);
                        break;
                }
            }
            if (Opts.Rays < 3) Opts.Rays = 3;
            if (Opts.Rays % 2 == 0) Opts.Rays++; // keep odd so a centre ray exists
            if (Opts.Explicit.Contains("rimrays"))
            {
                if (Opts.RimRays < 16) Opts.RimRays = 16;
                if (Opts.RimRays > 1024) Opts.RimRays = 1024;
            }
        }

        // Parse an integer, or keep the old value if the text is empty/bad.
        static int ParseInt(string s, int keep)
        {
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CI, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid integer - keeping " + keep + ".");
            return keep;
        }

        // Connect to OpticStudio, maybe show the settings window, then call Export.
        static void Run()
        {
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                App = connection.CreateNewApplication();
                if (App == null || App.PrimarySystem == null || !App.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                if (!App.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    App.CloseApplication();
                    throw new Exception("could not load " + Opts.FilePath);
                }
            }
            else
            {
                string connectError;
                if (!ZemaxLocator.TryConnect(out App, out connectError, false))
                    throw new Exception(connectError);
            }

            try
            {
                if (!Opts.NoDialog && !standalone)
                {
                    if (!SettingsDialog.Show(Opts)) return; // Cancel leaves system untouched
                }
                Export(App);
            }
            finally
            {
                if (standalone) App.CloseApplication();
                else
                {
                    App.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(App.ProgressMessage) || !App.ProgressMessage.StartsWith("Done"))
                        App.ProgressMessage = "Footprint DXF export finished.";
                }
            }
        }
    }
}

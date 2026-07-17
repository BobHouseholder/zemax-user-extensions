using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CryoGlass
{
    // CryoGlass — a ZOS-API User Extension.
    //
    // Brings the NASA GSFC CHARMS cryogenic refractive-index dataset
    // (Leviton & Frey temperature-dependent Sellmeier fits, absolute n(λ,T)
    // measured ~20-300 K) into OpticStudio. The ZOS-API cannot override index
    // computation, so support means CATALOG GENERATION: at a working
    // temperature T0 the CHARMS model IS a three-term Sellmeier, so the
    // generated .AGF carries EXACT dispersion coefficients at T0 plus a
    // locally-fitted Schott thermal model for the neighborhood of T0.
    //
    // The built-in self-test (evaluator vs the papers' published measured-
    // index tables) runs before every generation and refuses on disagreement,
    // so a coefficient transcription error can never silently reach a design.
    // Wavelengths/temperatures outside a material's MEASURED range are
    // refused by name - CHARMS stops at ~5.6 um; LWIR is out of coverage and
    // extrapolation is out of bounds.
    //
    // Usage:
    //   (no args)         extension mode: read the open system's environment
    //                     temperature, generate a catalog there, attach it
    //   -temp T           working temperature in KELVIN (standalone: no
    //                     OpticStudio needed; generation is pure math)
    //   -range T1:T2:N    N catalogs spanning T1..T2 K (for STOP sweeps)
    //   -materials "a,b"  subset (default: all; names: SI, GE)
    //   -fitbox K         half-width of the local dn/dT fit box (default 25)
    //   -out <path>       output .AGF path (default CHARMS_<T>K.AGF beside the
    //                     lens file, or in the current directory standalone)
    //   -file <zmx>       standalone with a lens file: read its environment
    //                     temperature, generate + report (never modifies it)
    //   -selftest         run the published-table self-test and exit
    //   -quiet            do not auto-open the report output
    class Options
    {
        public double TempK = double.NaN;
        public string Range = null;
        public string Materials = "";
        public int FitBox = 25;
        public string OutPath = null;
        public string FilePath = null;
        public bool SelfTestOnly = false;
        public bool Quiet = false;
    }

    class Program
    {
        static Options Opts = new Options();

        static void Main(string[] args)
        {
            ParseArgs(args);
            try
            {
                if (!SelfTestAll()) { Environment.ExitCode = 2; return; }
                if (Opts.SelfTestOnly) return;

                bool needZos = double.IsNaN(Opts.TempK) && Opts.Range == null;
                if (!needZos && Opts.FilePath == null)
                {
                    RunStandalone();
                    return;
                }
                if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
                {
                    Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
                    Environment.ExitCode = 1;
                    return;
                }
                RunConnected();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].TrimStart('-', '/').ToLowerInvariant())
                {
                    case "temp": if (i + 1 < args.Length) Opts.TempK = ParseDouble(args[++i], Opts.TempK); break;
                    case "range": if (i + 1 < args.Length) Opts.Range = args[++i]; break;
                    case "materials": if (i + 1 < args.Length) Opts.Materials = args[++i]; break;
                    case "fitbox": if (i + 1 < args.Length) Opts.FitBox = (int)ParseDouble(args[++i], Opts.FitBox); break;
                    case "out": if (i + 1 < args.Length) Opts.OutPath = args[++i]; break;
                    case "file": if (i + 1 < args.Length) Opts.FilePath = args[++i]; break;
                    case "selftest": Opts.SelfTestOnly = true; break;
                    case "quiet": Opts.Quiet = true; break;
                }
            }
            if (Opts.FitBox < 5) Opts.FitBox = 5;
        }

        static double ParseDouble(string s, double keep)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid number - keeping " + keep + ".");
            return keep;
        }

        internal static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        static bool SelfTestAll()
        {
            Console.WriteLine("self-test: evaluator vs published measured-index tables");
            bool ok = true;
            foreach (var m in CharmsData.Materials)
                ok &= Tsm.SelfTest(m, true);
            return ok;
        }

        static List<CharmsMaterial> SelectedMaterials()
        {
            if (string.IsNullOrEmpty(Opts.Materials)) return CharmsData.Materials.ToList();
            var outp = new List<CharmsMaterial>();
            foreach (var part in Opts.Materials.Split(',', ';'))
            {
                var m = CharmsData.Find(part.Trim());
                if (m == null) Console.WriteLine("WARNING: unknown material '" + part.Trim() + "' - available: "
                    + string.Join(", ", CharmsData.Materials.Select(x => x.Name)));
                else outp.Add(m);
            }
            return outp;
        }

        static List<double> Temperatures()
        {
            var temps = new List<double>();
            if (Opts.Range != null)
            {
                var p = Opts.Range.Split(':');
                if (p.Length == 3)
                {
                    double t1 = ParseDouble(p[0], double.NaN), t2 = ParseDouble(p[1], double.NaN);
                    int n = (int)ParseDouble(p[2], 0);
                    if (!double.IsNaN(t1) && !double.IsNaN(t2) && n >= 2)
                        for (int i = 0; i < n; i++) temps.Add(t1 + (t2 - t1) * i / (n - 1));
                }
                if (temps.Count == 0) throw new Exception("could not parse -range (expected T1:T2:N in Kelvin)");
            }
            else temps.Add(Opts.TempK);
            return temps;
        }

        static void Generate(string dir, List<CharmsMaterial> mats, List<double> temps)
        {
            foreach (var t0 in temps)
            {
                var usable = mats.Where(m => t0 >= m.TminK && t0 <= m.TmaxK).ToList();
                foreach (var m in mats.Except(usable))
                    Console.WriteLine(F("REFUSED: {0} is not measured at {1:0.#} K (valid {2:0}-{3:0} K) - not extrapolating.",
                        m.Name, t0, m.TminK, m.TmaxK));
                if (usable.Count == 0)
                {
                    Console.WriteLine(F("no materials usable at {0:0.#} K - nothing generated.", t0));
                    continue;
                }
                string path = Opts.OutPath != null && temps.Count == 1
                    ? Opts.OutPath
                    : Path.Combine(dir, F("CHARMS_{0:0}K.AGF", t0));
                AgfWriter.Write(path, usable, t0, Opts.FitBox, out var report);
                Console.WriteLine("catalog written: " + path);
                foreach (var line in report) Console.WriteLine("  " + line);
                Console.WriteLine("  NOTE: absolute (vacuum) indices at the working temperature; set the system");
                Console.WriteLine("  environment to that temperature at 0 atm. TCE=0 - source thermal expansion");
                Console.WriteLine("  separately before AthermalScan-style analyses.");
            }
        }

        static void RunStandalone()
        {
            Generate(Directory.GetCurrentDirectory(), SelectedMaterials(), Temperatures());
        }

        static void RunConnected()
        {
            ZOSAPI.IZOSAPI_Application app = null;
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                app = connection.CreateNewApplication();
                if (app == null || !app.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                if (!app.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    app.CloseApplication();
                    throw new Exception("could not load " + Opts.FilePath);
                }
            }
            else
            {
                try { app = connection.ConnectToApplication(); } catch { app = null; }
                if (app == null)
                {
                    try { app = connection.ConnectAsExtension(0); } catch { app = null; }
                }
                if (app == null)
                    throw new Exception("could not connect to OpticStudio (use the Programming ribbon or Interactive Extension)");
                if (!app.IsValidLicenseForAPI)
                    throw new Exception("license is not valid for ZOS-API: " + app.LicenseStatus);
            }

            try
            {
                var sys = app.PrimarySystem;
                var env = sys.SystemData.Environment;
                double tK = double.IsNaN(Opts.TempK) ? env.Temperature + 273.15 : Opts.TempK;
                Console.WriteLine(F("working temperature: {0:0.##} K ({1:0.##} C){2}", tK, tK - 273.15,
                    double.IsNaN(Opts.TempK) ? " (from system environment)" : ""));
                if (double.IsNaN(Opts.TempK) && Math.Abs(env.Pressure) > 1e-9)
                    Console.WriteLine("WARNING: system environment pressure is not 0 atm - CHARMS catalogs carry");
                Console.WriteLine("absolute (vacuum) indices; set pressure to 0 for consistent tracing.");

                string dir = string.IsNullOrEmpty(sys.SystemFile)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : Path.GetDirectoryName(sys.SystemFile);
                Opts.TempK = tK;
                Generate(dir, SelectedMaterials(), Temperatures());

                if (IsPlugin(app))
                {
                    app.ProgressPercent = 100;
                    app.ProgressMessage = F("Done. CHARMS catalog generated at {0:0.#} K - add it under System Explorer > Material Catalogs.", tK);
                }
            }
            finally
            {
                if (standalone) { try { app.CloseApplication(); } catch { } }
            }
        }

        static bool IsPlugin(ZOSAPI.IZOSAPI_Application app)
        {
            try { return app.Mode == ZOSAPI.ZOSAPI_Mode.Plugin; } catch { return false; }
        }
    }
}

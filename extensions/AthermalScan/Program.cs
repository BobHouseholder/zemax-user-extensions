using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AthermalScan
{
    // Athermal Scan — a ZOS-API User Extension.
    //
    // One-command passive athermalization analysis for sequential systems,
    // replacing the manual TEMP/PRES multi-configuration workflow (community
    // threads "athermal design", "how to model a system with groups under
    // different temperatures and pressures").
    //
    // For each temperature in the sweep the extension applies OpticStudio's own
    // thermal model transiently: refractive indices adjust through the system
    // environment (Adjust Index To Environment), and radii, thicknesses and
    // polynomial asphere terms expand as (1 + a.dT) using the glass catalog TCE
    // for glass rows and the LDE TCE column (mount material) for air gaps. The
    // original prescription and environment are snapshot and fully restored,
    // including on error.
    //
    // Non-glass gaps follow the Make Thermal rule (manual 2.1.1.4.4.2): they
    // expand along the EDGE, from the rim of one surface to the rim of the next,
    // with the mount contact point walking radially as spacer and lens rim expand
    // at different rates and clamped to the lens mechanical semi-diameter; the
    // result is transferred back onto the centre thickness. So a TCE of 0 still
    // moves a gap when the adjacent radii move, as it should.
    //
    // Still short of Make Thermal: semi-diameters are not expanded, and length
    // parameters outside the even/odd asphere terms (toroidal and biconic radii,
    // Zernike normalisation radii) are not scaled. Gaps bounded by a surface
    // whose sag this tool cannot evaluate fall back to centre scaling and are
    // named in the report.
    //
    // Index convention: OpticStudio always traces RELATIVE index - air at the
    // system temperature and pressure is exactly 1.0, and glass indices are
    // normalised to it (manual 2.1.1.4.2). So the system pressure alone decides
    // whether the reported n, dn/dT and x_f are relative-to-air (P > 0) or
    // absolute/vacuum (P = 0); the difference in dn/dT is n*|dn_air/dT|,
    // ~1.4e-6/K at n=1.5 and 1 atm, which is the whole value for a low-dn/dT
    // crown. The convention in force is stated in the report, and -pressure /
    // -vacuum / -psweep select it explicitly.
    //
    // Reported:
    //  * focus shift, EFFL, RMS spot (fixed plane and refocused) vs temperature
    //  * diffraction depth of focus (+/- 2 lambda N^2) and the passive athermal
    //    temperature range at a fixed image plane
    //  * required housing CTE (dz/dT over the mount track), nearest housing
    //    materials with their residual defocus rates and usable temperature
    //    ranges, and an exact two-metal (bimetallic) length solution
    //  * per-glass opto-thermal table: n, dn/dT (measured numerically from the
    //    live model), catalog TCE, thermal glass constant
    //    x_f = dn/dT/(n-1) - alpha, and an approximate thin-element share of
    //    the total thermal defocus
    //  * a two-panel PNG chart (focus shift with DOF band; RMS vs T)
    //
    // Usage:
    //   (no args)      analyze the system open in OpticStudio (extension mode)
    //   -tmin C        sweep start in Celsius (default -20)
    //   -tmax C        sweep end (default +60)
    //   -steps N       sweep points (default 9)
    //   -track L       housing/mount length in lens units (default: total track)
    //   -pressure P    run the SCAN at P atm instead of the design pressure
    //   -vacuum        shorthand for -pressure 0 (absolute/vacuum indices)
    //   -psweep P1:P2  paired temperature/pressure soak: P ramps with T
    //   -temp0 T       declare the DESIGN temperature (required when the file
    //                  has Adjust Index Data To Environment switched off)
    //   -press0 P      declare the DESIGN pressure, separately from the scan
    //                  pressure - "built in air, flown in vacuum" is
    //                  -press0 1 -vacuum
    //   -freezesolves  freeze value-computing solves on radius/thickness/params
    //                  instead of refusing to run; NOT undone on restore
    //   -nodialog      never put up the settings window (scripted no-argument runs)
    //   -dialog        put the settings window up even outside a ribbon run
    //
    // A ribbon run has no command line and OpticStudio provides no way to give it
    // one, so with no arguments in Plugin mode the settings window collects the
    // sweep, the design environment and the analysis pressure, remembering the last
    // run in %APPDATA%\AthermalScan\lastrun.txt.
    //   -out <prefix>  output prefix for report/chart (default <lens>_athermal)
    //   -outdir <dir>  write the report into this folder instead of beside the lens
    //                  (the settings window sets the same thing)
    //   -file <path>   standalone mode: load the file first
    //   -quiet         do not auto-open report/chart after a ribbon (GUI) run
    class Options
    {
        public double TMin = -20, TMax = 60;
        public int Steps = 9;
        public double Track = 0;
        public string OutPrefix = null;
        public string FilePath = null;
        public bool Quiet = false;
        public double? Pressure = null;      // scan pressure, atm (null = the design pressure)
        public double? PressureEnd = null;   // -psweep end pressure, atm
        public double? Temp0 = null;         // declared design temperature, C
        public double? Press0 = null;        // declared design pressure, atm
        public bool FreezeSolves = false;
        public double? DumpAt = null;        // -dump T: print the expanded prescription and stop
        public bool NoArgs = true;           // launched with no command line at all
        public bool NoDialog = false;        // -nodialog: never put up the settings window
        public bool ForceDialog = false;     // -dialog: put it up even outside Plugin mode
        // Output FOLDER, as chosen in the settings window. -out takes a full prefix
        // because a shell user wants to name the files; a dialog user means "put them
        // somewhere else". A directory-less -out prefix is combined with -outdir
        // when both are given; a full-path -out stays authoritative.
        public string OutDir = null;
        public bool HostLaunched = false;    // -zpid/-zplt/-zsid present: OpticStudio launched us
        public bool NoFiles = false;         // suppress report/chart/csv/json (User Analysis renders in-window)
    }

    class RowSnap
    {
        public double Radius, Thickness, Conic;
        public double SemiDia;       // clear semi-diameter: where the edge is measured
        public double MechSemiDia;   // mechanical semi-diameter (fallback only)
        public double[] Pars = new double[9];
        public ZOSAPI.Editors.LDE.SurfaceType Type;
        public string Material = "";
        public double MountTce;      // LDE TCE column value, in 1e-6/K
        public double AlphaRadius;   // effective expansion coeff for the radius
        public double AlphaThick;    // effective expansion coeff for the gap
        public bool IsGlass;
    }

    partial class Program
    {
        internal static Options Opts = new Options();
        internal static string[] LaunchArgs;
        internal static readonly List<string> Report = new List<string>();

        // STA because a ribbon run puts up the settings window (ScanSettingsDialog).
        [STAThread]
        static void Main(string[] args)
        {
            ParseArgs(args);
            string zosError;
            if (!ZemaxLocator.TryInitialize(out zosError))
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation."
                                  + (zosError == null ? "" : "  " + zosError));
                Environment.ExitCode = 1;
                return;
            }
            try { Run(); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                LaunchLog("FATAL: " + ex.Message);
                // A ribbon run's console dies with the process, so an error printed to
                // it is invisible - and the environment guards exist precisely to
                // refuse loudly. Refusing invisibly is worse than not refusing at all,
                // because the user is left with no scan and no reason.
                if (Opts.HostLaunched && !Opts.Quiet)
                {
                    try
                    {
                        System.Windows.Forms.MessageBox.Show(ex.Message, "Athermal Scan",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                    }
                    catch { /* no desktop - the log still has it */ }
                }
                Environment.ExitCode = 1;
            }
        }

        static readonly HashSet<string> KnownOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tmin", "tmax", "steps", "track", "pressure", "vacuum", "psweep", "temp0", "press0",
            "freezesolves", "dump", "nodialog", "dialog", "out", "outdir", "file", "quiet"
        };

        static void ParseArgs(string[] args)
        {
            LaunchArgs = args;
            bool sawOption = false;
            for (int i = 0; args != null && i < args.Length; i++)
            {
                string a = args[i] ?? "";
                string k = a.TrimStart('-', '/');
                if (KnownOptions.Contains(k)) sawOption = true;
                if (k.StartsWith("zpid", StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("zplt", StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("zsid", StringComparison.OrdinalIgnoreCase))
                    Opts.HostLaunched = true;
            }
            Opts.NoArgs = !sawOption;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].TrimStart('-', '/').ToLowerInvariant())
                {
                    case "tmin": if (i + 1 < args.Length) Opts.TMin = ParseDouble(args[++i], Opts.TMin); break;
                    case "tmax": if (i + 1 < args.Length) Opts.TMax = ParseDouble(args[++i], Opts.TMax); break;
                    case "steps": if (i + 1 < args.Length) Opts.Steps = ParseInt(args[++i], Opts.Steps); break;
                    case "track": if (i + 1 < args.Length) Opts.Track = ParseDouble(args[++i], Opts.Track); break;
                    case "pressure": if (i + 1 < args.Length) Opts.Pressure = ParseDouble(args[++i], 1.0); break;
                    case "vacuum": Opts.Pressure = 0.0; break;
                    case "psweep": if (i + 1 < args.Length) ParsePSweep(args[++i]); break;
                    case "temp0": if (i + 1 < args.Length) Opts.Temp0 = ParseDouble(args[++i], 20.0); break;
                    case "press0": if (i + 1 < args.Length) Opts.Press0 = ParseDouble(args[++i], 1.0); break;
                    case "freezesolves": Opts.FreezeSolves = true; break;
                    case "dump": if (i + 1 < args.Length) Opts.DumpAt = ParseDouble(args[++i], 20.0); break;
                    case "nodialog": Opts.NoDialog = true; break;
                    case "dialog": Opts.ForceDialog = true; break;
                    case "out": if (i + 1 < args.Length) Opts.OutPrefix = args[++i]; break;
                    case "outdir": if (i + 1 < args.Length) Opts.OutDir = args[++i]; break;
                    case "file": if (i + 1 < args.Length) Opts.FilePath = args[++i]; break;
                    case "quiet": Opts.Quiet = true; break;
                }
            }
            if (Opts.Steps < 3) Opts.Steps = 3;
            if (Opts.Pressure.HasValue && Opts.Pressure.Value < 0)
            {
                Console.WriteLine("WARNING: negative pressure is meaningless - clamping to 0 (vacuum).");
                Opts.Pressure = 0.0;
            }
            if (Opts.PressureEnd.HasValue && Opts.PressureEnd.Value < 0)
            {
                Console.WriteLine("WARNING: negative pressure is meaningless - clamping to 0 (vacuum).");
                Opts.PressureEnd = 0.0;
            }
            if (Opts.Press0.HasValue && Opts.Press0.Value < 0)
            {
                Console.WriteLine("WARNING: negative pressure is meaningless - clamping to 0 (vacuum).");
                Opts.Press0 = 0.0;
            }
        }

        static void ParsePSweep(string s)
        {
            var parts = (s ?? "").Split(':');
            if (parts.Length != 2)
            {
                Console.WriteLine("WARNING: -psweep expects P1:P2 in atm - ignoring '" + s + "'.");
                return;
            }
            Opts.Pressure = ParseDouble(parts[0], 1.0);
            Opts.PressureEnd = ParseDouble(parts[1], 0.0);
        }

        static int ParseInt(string s, int keep)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid integer - keeping " + keep + ".");
            return keep;
        }

        static double ParseDouble(string s, double keep)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid number - keeping " +
                keep.ToString(CultureInfo.InvariantCulture) + ".");
            return keep;
        }

        internal static readonly Results R = new Results();

        static void Say(string s)
        {
            Console.WriteLine(s);
            Report.Add(s);
            if (s.StartsWith("WARNING", StringComparison.Ordinal) ||
                s.StartsWith("NOTE:", StringComparison.Ordinal)) R.Warnings.Add(s);
        }
        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        internal static void LaunchLog(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture)
                          + message + Environment.NewLine;
            foreach (var dir in LogDirs())
            {
                try
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string path = Path.Combine(dir, "AthermalScan-launch.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
                    File.AppendAllText(path, line);
                    return;
                }
                catch { }
            }
        }

        static IEnumerable<string> LogDirs()
        {
            string asm = null;
            try { asm = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { }
            yield return asm;
            string tmp = null;
            try { tmp = Path.GetTempPath(); } catch { }
            yield return tmp;
        }

        static void Run()
        {
            ZOSAPI.IZOSAPI_Application app = null;
            LaunchLog("launch argc=" + (LaunchArgs == null ? -1 : LaunchArgs.Length) +
                      " argv=[" + string.Join(" ", LaunchArgs ?? new string[0]) + "]" +
                      " -> noArgs=" + Opts.NoArgs + " hostLaunched=" + Opts.HostLaunched);
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                app = connection.CreateNewApplication();
                if (app == null)
                    throw new Exception("could not start a standalone OpticStudio instance " +
                                        "(CreateNewApplication returned nothing)");
                if (!app.IsValidLicenseForAPI)
                    throw new Exception("a standalone instance started but its license is not valid for " +
                                        "ZOS-API: " + app.LicenseStatus + " (loaded from " +
                                        (ZemaxLocator.ResolvedDirectory ?? "an unknown directory") + ")");
                if (!app.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    app.CloseApplication();
                    throw new Exception("could not load " + Opts.FilePath);
                }
            }
            else
            {
                string connectError;
                if (!ZemaxLocator.TryConnect(out app, out connectError, false))
                    throw new Exception(connectError + " (loaded from " +
                                        (ZemaxLocator.ResolvedDirectory ?? "an unknown directory") + ")");
            }

            if (!standalone && !Opts.NoDialog && (Opts.NoArgs || Opts.ForceDialog))
            {
                bool plugin = false;
                string modeName = "(unreadable)";
                try { modeName = app.Mode.ToString(); plugin = app.Mode == ZOSAPI.ZOSAPI_Mode.Plugin; } catch { }
                bool gui = plugin || Opts.HostLaunched;
                LaunchLog("mode=" + modeName + " plugin=" + plugin + " hostLaunched=" + Opts.HostLaunched +
                          " noArgs=" + Opts.NoArgs + " forceDialog=" + Opts.ForceDialog +
                          " -> dialog=" + (gui || Opts.ForceDialog));
                if (gui || Opts.ForceDialog)
                {
                    var sysNow = app.PrimarySystem;
                    var envNow = sysNow.SystemData.Environment;
                    List<string> solvesNow = null;
                    try { solvesNow = FindComputingSolves(sysNow.LDE, sysNow.LDE.NumberOfSurfaces - 1); }
                    catch { }
                    if (!ScanSettingsDialog.Show(envNow.Temperature, envNow.Pressure,
                                                 envNow.AdjustIndexToEnvironment, Opts, solvesNow))
                    {
                        app.ProgressMessage = "Done. Cancelled - the system was not touched.";
                        Console.WriteLine("Cancelled - the system was not touched.");
                        LaunchLog("cancelled at the settings window - nothing run");
                        return;
                    }
                }
            }

            try { Analyze(app, app.PrimarySystem); }
            finally
            {
                if (standalone) app.CloseApplication();
                else
                {
                    app.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(app.ProgressMessage) || !app.ProgressMessage.StartsWith("Done"))
                        app.ProgressMessage = "Athermal scan complete.";
                }
            }
        }

        static void OpenOutputs(ZOSAPI.IZOSAPI_Application app, params string[] paths)
        {
            if (Opts.Quiet) return;
            try { if (app.Mode != ZOSAPI.ZOSAPI_Mode.Plugin) return; } catch { return; }
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                try { System.Diagnostics.Process.Start(p); }
                catch (Exception ex) { Console.WriteLine("WARNING: could not open " + p + ": " + ex.Message); }
            }
        }

        static double Op(ZOSAPI.IOpticalSystem sys, ZOSAPI.Editors.MFE.MeritOperandType t,
            int p1, int p2, double h1 = 0, double h2 = 0, double p3 = 0, double p4 = 0)
            => sys.MFE.GetOperandValue(t, p1, p2, h1, h2, p3, p4, 0, 0);
    }
}

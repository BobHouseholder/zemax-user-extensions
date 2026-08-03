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
        // somewhere else", so the two are kept separate and OutPrefix still wins.
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
            if (!ZemaxLocator.Initialize())
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
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

        // Every option this program accepts. Membership here is what decides whether a
        // command line was supplied - see ParseArgs.
        static readonly HashSet<string> KnownOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tmin", "tmax", "steps", "track", "pressure", "vacuum", "psweep", "temp0", "press0",
            "freezesolves", "dump", "nodialog", "dialog", "out", "outdir", "file", "quiet"
        };

        static void ParseArgs(string[] args)
        {
            // "No arguments" must mean "no OPTIONS OF OURS", not "no argv entries" and
            // not "nothing dash-prefixed". OpticStudio launches a ribbon extension as
            //     -zpid={30712} -zplt={Extension} -zsid={100002}
            // so both of those weaker tests conclude a command line was supplied and
            // suppress the settings window - the only way a ribbon user can configure
            // anything. Only a recognised option counts.
            LaunchArgs = args;
            bool sawOption = false;
            for (int i = 0; args != null && i < args.Length; i++)
            {
                string a = args[i] ?? "";
                string k = a.TrimStart('-', '/');
                if (KnownOptions.Contains(k)) sawOption = true;
                // -zpid / -zplt / -zsid identify a launch by OpticStudio itself. They
                // are also the only evidence available here that a GUI is present, so
                // they gate the settings window alongside app.Mode.
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

        // -psweep P1:P2 - pressure ramps linearly with the temperature steps.
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

        // TryParse zeroes its out parameter on failure, which would silently
        // replace the documented defaults; keep the default instead and warn.
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
            // Every caution the scan raises already goes through here, so collect them
            // once rather than remembering to add each new one to the structured output.
            if (s.StartsWith("WARNING", StringComparison.Ordinal) ||
                s.StartsWith("NOTE:", StringComparison.Ordinal)) R.Warnings.Add(s);
        }
        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        // One line per launch, next to the deployed .exe. Cheap, bounded, and the only
        // evidence that survives a ribbon run - whose console dies with the process.
        //
        // Falls back to %TEMP% if the deployment directory cannot be written or the
        // assembly location cannot be read. An absent log then means the process did
        // not start, rather than meaning it started somewhere unwritable - which is
        // the difference between "the host never launched it" and "it launched and
        // died", and those need opposite investigations.
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
                catch { /* try the next location */ }
            }
            /* diagnostics must never be the reason a run fails */
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
            // Logged before the connection is attempted, so the parse is on record even
            // when the connection is what fails.
            LaunchLog("launch argc=" + (LaunchArgs == null ? -1 : LaunchArgs.Length) +
                      " argv=[" + string.Join(" ", LaunchArgs ?? new string[0]) + "]" +
                      " -> noArgs=" + Opts.NoArgs + " hostLaunched=" + Opts.HostLaunched);
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                app = connection.CreateNewApplication();
                // Kept apart: "no instance" and "instance but no API licence" have
                // completely different causes, and conflating them sent a real
                // debugging session chasing a licence problem that was actually the
                // ZOS-API assemblies resolving to a decade-old install.
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
                try { app = connection.ConnectToApplication(); } catch { app = null; }
                if (app == null)
                {
                    try { app = connection.ConnectAsExtension(0); } catch { app = null; }
                }
                if (app == null)
                    throw new Exception("could not connect to OpticStudio (use the Programming ribbon or Interactive Extension)");
                if (!app.IsValidLicenseForAPI)
                    throw new Exception("license is not valid for ZOS-API: " + app.LicenseStatus +
                                        " (loaded from " + (ZemaxLocator.ResolvedDirectory ?? "an unknown directory") + ")");
            }

            // A ribbon run has no command line and no way to supply one, so ask.
            // Restricted to Plugin mode by default: anything else may be a scripted
            // no-argument run that must not block on a modal window. -dialog forces
            // it, -nodialog suppresses it.
            if (!standalone && !Opts.NoDialog && (Opts.NoArgs || Opts.ForceDialog))
            {
                bool plugin = false;
                string modeName = "(unreadable)";
                try { modeName = app.Mode.ToString(); plugin = app.Mode == ZOSAPI.ZOSAPI_Mode.Plugin; } catch { }
                bool gui = plugin || Opts.HostLaunched;
                // A ribbon run loses its console instantly, so when the settings window
                // does not appear there is nothing at all to look at. Record what the
                // gate actually saw - this exists because "I ran it and saw no
                // settings" was otherwise undiagnosable without another user attempt.
                LaunchLog("mode=" + modeName + " plugin=" + plugin + " hostLaunched=" + Opts.HostLaunched +
                          " noArgs=" + Opts.NoArgs + " forceDialog=" + Opts.ForceDialog +
                          " -> dialog=" + (gui || Opts.ForceDialog));
                if (gui || Opts.ForceDialog)
                {
                    var sysNow = app.PrimarySystem;
                    var envNow = sysNow.SystemData.Environment;
                    // Ask about solves BEFORE the user fills the form in. Discovering
                    // them afterwards means refusing a run that has already been
                    // configured, and the refusal used to name a command-line flag no
                    // ribbon user can pass.
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

        // Plugin-mode (ribbon) runs lose their console the moment the process
        // exits, so the written files are the only surviving report - open
        // them with their default apps unless -quiet.
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

        internal static void Analyze(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys)
        {
            // sys is passed in: the extension analyses the live system, the User Analysis
            // a CopySystem() clone, so the open prescription is never touched.
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("this extension requires a sequential system");

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;
            var env = sys.SystemData.Environment;

            int primaryWave = 1;
            var wls = sys.SystemData.Wavelengths;
            for (int w = 1; w <= wls.NumberOfWavelengths; w++)
                if (wls.GetWavelength(w).IsPrimary) { primaryWave = w; break; }
            double lambdaUm = wls.GetWavelength(primaryWave).Wavelength;

            Say("=== Athermal Scan ===");
            Say("Lens file : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));

            // ---- guards: the scan must own the whole environment -----------------
            if (Math.Abs(Opts.TMax - Opts.TMin) < 1e-9)
                throw new Exception("-tmin and -tmax must differ; there is nothing to sweep.");
            CheckNoEnvironmentOperands(sys);
            CheckSolves(lde, imgIdx);

            // ---- baseline state -------------------------------------------------
            // The raw values are what gets put back; t0/p0 are the design environment
            // the prescription is taken to have been measured in, which is NOT the
            // same thing when the adjust-index switch is off.
            double tRaw = env.Temperature, pRaw = env.Pressure;
            bool adjust0 = env.AdjustIndexToEnvironment;
            double t0 = tRaw, p0 = pRaw;
            if (!adjust0)
            {
                // Manual, Environment settings: "when the adjust index box is unchecked
                // the system temperature is set to 20 degrees C and the pressure to 1.0
                // atmospheres, and therefore all index data must be relative to that
                // environment". The stored temperature/pressure are then not the design
                // environment, and guessing would silently pick the wrong index
                // reference - in air when the instrument flies in vacuum, or vice versa.
                if (!Opts.Temp0.HasValue)
                    throw new Exception(
                        "'Adjust Index Data To Environment' is OFF in this file, so OpticStudio evaluates " +
                        "all index data as if the system were at 20 C and 1.0 atm and the stored temperature " +
                        "and pressure do not define the design environment. Re-run with -temp0 <C> (plus " +
                        "-pressure <atm> or -vacuum if the design is not at 1 atm) to declare the environment " +
                        "the radii and thicknesses were measured in.");
                t0 = Opts.Temp0.Value;
                // -press0 names the design pressure outright; -pressure alone still
                // means "the design is at this pressure and so is the scan", which is
                // the common case for a file that was never given an environment.
                p0 = Opts.Press0 ?? Opts.Pressure ?? 1.0;
            }
            else
            {
                if (Opts.Temp0.HasValue) t0 = Opts.Temp0.Value;
                if (Opts.Press0.HasValue) p0 = Opts.Press0.Value;
            }

            double pStart = Opts.Pressure ?? p0;
            double pEnd = Opts.PressureEnd ?? pStart;
            bool pVaries = Math.Abs(pEnd - pStart) > 1e-12;
            bool pShifted = Math.Abs(pStart - p0) > 1e-12 || pVaries;

            R.LensFile = sys.SystemFile ?? "";
            R.DesignTempC = t0; R.DesignPressAtm = p0; R.AdjustIndexWasOn = adjust0;
            R.ScanPressStart = pStart; R.ScanPressEnd = pEnd;
            R.TMin = Opts.TMin; R.TMax = Opts.TMax; R.Steps = Opts.Steps;

            Say(F("Design environment: {0:F1} C, {1:F3} atm", t0, p0));
            Say(F("Scan              : {0:F0}..{1:F0} C, {2} steps, {3}",
                Opts.TMin, Opts.TMax, Opts.Steps,
                pVaries ? F("pressure {0:F3} -> {1:F3} atm (paired soak)", pStart, pEnd)
                        : F("pressure {0:F3} atm", pStart)));
            Say("Index convention  : " + Convention(pStart) + (pVaries ? ", at the start of the sweep" : ""));
            if (!adjust0)
                Say("NOTE: the file had 'Adjust Index Data To Environment' OFF, which pins index data to " +
                    "20 C / 1.0 atm; the design environment above was taken from the command line. The " +
                    "switch is enabled for the scan and restored afterwards.");
            if (!adjust0 && !Opts.Pressure.HasValue && !Opts.Press0.HasValue)
                Say("NOTE: neither -press0 nor -pressure was given with -temp0, so the design pressure is " +
                    "assumed to be 1.0 atm (the adjust-off convention). Use -vacuum for a vacuum design, or " +
                    "-press0 1 -vacuum for one built in air and flown in vacuum.");
            if (Opts.Temp0.HasValue && adjust0 && Math.Abs(Opts.Temp0.Value - tRaw) > 1e-9)
                Say(F("NOTE: -temp0 {0:F1} C overrides the file's system temperature of {1:F1} C as the " +
                      "design point.", t0, tRaw));
            if (pShifted)
                Say("NOTE: the scan pressure differs from the design pressure, so the focus shift includes " +
                    "the pressure term; it is reported separately below.");

            // ---- snapshot prescription + effective TCE per row ------------------
            var snaps = new RowSnap[imgIdx];
            var glassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new RowSnap { Type = row.Type };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                s.Thickness = row.Thickness;
                try { s.Conic = row.Conic; } catch { s.Conic = 0; }
                // The radius the edge thickness is measured at. Taken from the
                // snapshot, so it stays the as-built value while the sweep runs.
                try { s.SemiDia = row.SemiDiameter; } catch { s.SemiDia = 0; }
                try { s.MechSemiDia = row.MechanicalSemiDiameter; } catch { s.MechSemiDia = 0; }
                string mat = (row.Material ?? "").Trim();
                s.Material = mat;
                s.IsGlass = mat.Length > 0 && mat != "-" &&
                            !mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                if (s.IsGlass) glassNames.Add(mat);
                try { s.MountTce = row.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.TCE).DoubleValue; }
                catch { s.MountTce = 0; }
                for (int p = 1; p <= 8; p++)
                {
                    try
                    {
                        var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                        s.Pars[p] = row.GetSurfaceCell(col).DoubleValue;
                    }
                    catch { s.Pars[p] = 0; }
                }
                snaps[i - 1] = s;
            }
            if (glassNames.Count == 0)
                throw new Exception("no glass surfaces found - nothing to athermalize");
            foreach (var s in snaps)
                if (s != null && (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak))
                    Say("NOTE: coordinate break thicknesses expand with their TCE column; decenters/tilts are held fixed.");

            // ---- glass TCE + thermal index data from the materials catalog ------
            var glassTce = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var noThermalIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignoreExpansion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var catalogsInUse = sys.SystemData.MaterialCatalogs.GetCatalogsInUse()
                .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var matTool = sys.Tools.OpenMaterialsCatalog();
            try
            {
                foreach (string cat in catalogsInUse)
                {
                    string[] names;
                    try { matTool.SelectedCatalog = cat; names = matTool.GetAllMaterials(); }
                    catch { continue; }
                    foreach (string nm in names)
                    {
                        if (!glassNames.Contains(nm) || glassTce.ContainsKey(nm)) continue;
                        matTool.SelectedMaterial = nm;
                        glassTce[nm] = matTool.TCE; // in 1e-6/K
                        // All six Schott constants zero means OpticStudio computes no
                        // index change at all for this material (manual 2.1.1.4.2:
                        // "if no thermal data has been added to the catalog, no thermal
                        // effects are considered").
                        if (matTool.D0 == 0 && matTool.D1 == 0 && matTool.D2 == 0 &&
                            matTool.E0 == 0 && matTool.E1 == 0)
                            noThermalIndex.Add(nm);
                        if (matTool.IgnoreThermalExpansion) ignoreExpansion.Add(nm);
                    }
                }
            }
            finally { matTool.Close(); }
            foreach (string g in noThermalIndex)
                Say("WARNING: '" + g + "' carries no thermal index constants (D0..E1 all zero), so its " +
                    "absolute index does not change with temperature in this model. At P > 0 the dn/dT " +
                    "reported for it below is purely the air-normalisation term (~1.4e-6/K at n=1.5, " +
                    "1 atm); at P = 0 it is exactly zero.");
            foreach (string g in glassNames)
                if (!glassTce.ContainsKey(g))
                {
                    // Model glasses, MIL-number glasses and GRIN media land here.
                    // Measured on the stock "Doublet using MIL number glasses": dn/dT
                    // comes back as EXACTLY zero at 1 atm, not merely small. The manual
                    // says the relative index of such media is still adjusted for the
                    // surrounding air; in practice no adjustment is applied at all, so
                    // do not promise the reader even that much.
                    Say("WARNING: glass '" + g + "' was not found in the catalogs in use. Assuming TCE = 0, " +
                        "and note that OpticStudio models no dn/dT for model, MIL-number or gradient-index " +
                        "media (manual 2.1.1.4.2) - measured, such a glass reports dn/dT of exactly zero, so " +
                        "this glass's opto-thermal row below is not physical and any dz/dT resting on it is " +
                        "an artefact of that.");
                    glassTce[g] = 0;
                    noThermalIndex.Add(g);
                }
            foreach (string g in ignoreExpansion)
                Say("NOTE: '" + g + "' has the catalog's ignore-thermal-expansion flag set (a gas or liquid). " +
                    "OpticStudio then takes radius expansion from the adjacent solid and only the edge " +
                    "effects from this material; this scan expands it with its own TCE column instead.");

            // effective alphas per the OpticStudio thermal model:
            //  - a glass row's thickness and radius expand with the glass TCE
            //  - the rear surface of a lens (air row following glass) also expands
            //    its RADIUS with that glass TCE; its gap uses the mount TCE column
            for (int i = 0; i < snaps.Length; i++)
            {
                var s = snaps[i];
                if (s == null) continue;
                double mount = s.MountTce;
                if (s.IsGlass)
                {
                    s.AlphaThick = glassTce[s.Material];
                    s.AlphaRadius = glassTce[s.Material];
                }
                else
                {
                    s.AlphaThick = mount;
                    s.AlphaRadius = (i > 0 && snaps[i - 1] != null && snaps[i - 1].IsGlass)
                        ? glassTce[snaps[i - 1].Material] : mount;
                }
            }

            // ---- -dump: expanded prescription at one temperature, then stop ------
            // Exists so the thermal model can be checked surface by surface against
            // OpticStudio's own thermal pickup solves, which is the only external
            // ground truth for the geometry side of this tool.
            if (Opts.DumpAt.HasValue)
            {
                double td = Opts.DumpAt.Value;
                env.AdjustIndexToEnvironment = true;
                try
                {
                    env.Temperature = t0; env.Pressure = p0;
                    ApplyTemperature(sys, snaps, imgIdx, td - t0);
                    env.Temperature = td;
                    Say(F("PRESCRIPTION AT {0:F4} C  (dT = {1:+0.####;-0.####} from the design point)", td, td - t0));
                    Say("  surf                radius             thickness   material");
                    for (int i = 1; i < imgIdx; i++)
                    {
                        var row = lde.GetSurfaceAt(i);
                        double r;
                        try { r = row.Radius; } catch { r = double.PositiveInfinity; }
                        Say(F("  {0,4}   {1,20:G14}   {2,18:G14}   {3}", i, r, row.Thickness, snaps[i - 1].Material));
                    }
                    if (EdgeFallbackRows.Count > 0)
                        Say("  (centre-scaled fallback on surface(s) " +
                            string.Join(", ", EdgeFallbackRows.OrderBy(r => r)) + ")");
                }
                finally
                {
                    RestoreSystem(sys, env, snaps, imgIdx, t0, p0, tRaw, pRaw, adjust0, primaryWave);
                }
                return;
            }

            // ---- the sweep -------------------------------------------------------
            int n = Opts.Steps;
            var temps = new double[n];
            var press = new double[n];
            var focusShift = new double[n];
            var rmsFixed = new double[n];
            var rmsRefoc = new double[n];
            var efl = new double[n];
            // per-glass index at the sweep extremes for dn/dT (surface of first occurrence)
            var glassSurf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
                if (snaps[i - 1].IsGlass && !glassSurf.ContainsKey(snaps[i - 1].Material))
                    glassSurf[snaps[i - 1].Material] = i;
            var indexAtMin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var indexAtMax = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            double efl0 = 0, wfno = 0, totr = 0, track = 0, dofMm = 0;
            double focus0 = double.NaN, eflCheck = double.NaN;
            // one entry per distinct scan pressure that differs from the design
            // pressure: (pressure, focus offset from the design state)
            var pressureTerms = new List<KeyValuePair<double, double>>();
            bool terminated = false;

            // Everything from here to the finally mutates the live prescription and
            // the system environment, so it is unconditionally undone - an exception
            // mid-sweep must not leave the user's lens scaled, at the wrong
            // temperature, with the index-adjust switch flipped.
            env.AdjustIndexToEnvironment = true;
            try
            {
                env.Temperature = t0; env.Pressure = p0;

                // ---- baseline metrics, at the design environment -----------------
                efl0 = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
                wfno = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.WFNO, 0, primaryWave);
                totr = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.TOTR, 0, 0);
                track = Opts.Track > 0 ? Opts.Track : totr;
                dofMm = 2.0 * (lambdaUm * 1e-3) * wfno * wfno; // +/- 2 lambda N^2, lens units (mm assumed)
                Say(F("EFFL {0:F4}, working F/# {1:F3}, total track {2:F3}, mount track L = {3:F3}",
                    efl0, wfno, totr, track));
                Say(F("Diffraction depth of focus: +/- {0:F4} lens units  (2*lambda*N^2, lambda={1:F4} um)",
                    dofMm, lambdaUm));

                // Everything this tool reports is a defocus compared against the depth
                // of focus, so an image space that is not converging makes the whole
                // report meaningless rather than merely imprecise. Caught on the stock
                // "Cooke 40 degree field_zadj" sample, whose image space is near
                // collimated: working F/# 6669, depth of focus +/-48921 on a 17.97
                // total track, and a required housing CTE of -1.9e7 x 1e-6/K reported
                // without a murmur. Refuse instead of emitting numbers like that.
                if (double.IsNaN(wfno) || double.IsInfinity(wfno) || dofMm > Math.Abs(totr))
                    throw new Exception(F(
                        "the image space is not converging - working F/# is {0:G6} and the diffraction depth " +
                        "of focus comes out at +/-{1:G6} lens units against a total track of {2:G6}. Focus " +
                        "shift, athermal range and required housing CTE are all defocus measured against that " +
                        "depth of focus, so none of them means anything here. Check that the image surface is " +
                        "at or near focus; an afocal system needs an angular metric, not a focus shift.",
                        wfno, dofMm, totr));
                R.Efl0 = efl0; R.Wfno = wfno; R.TotalTrack = totr;
                R.MountTrack = track; R.DofMm = dofMm; R.LambdaUm = lambdaUm;

                for (int k = 0; k < n; k++)
                {
                    if (app.TerminateRequested) { terminated = true; break; }
                    double T = Opts.TMin + (Opts.TMax - Opts.TMin) * k / (n - 1);
                    double P = pVaries ? pStart + (pEnd - pStart) * k / (n - 1) : pStart;
                    temps[k] = T; press[k] = P;
                    app.ProgressMessage = F("Evaluating T = {0:F1} C, P = {1:F3} atm...", T, P);
                    app.ProgressPercent = 10 + 70 * k / n;
                    ApplyTemperature(sys, snaps, imgIdx, T - t0);
                    env.Temperature = T; env.Pressure = P;

                    efl[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
                    double focus = MarginalFocus(sys, imgIdx, primaryWave,
                        snaps[imgIdx - 2].Thickness * (1 + snaps[imgIdx - 2].AlphaThick * 1e-6 * (T - t0)));
                    focusShift[k] = focus;
                    rmsFixed[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;

                    // refocused RMS: move the image plane to the marginal focus
                    var lastRow = lde.GetSurfaceAt(imgIdx - 1);
                    double scaledLast = lastRow.Thickness;
                    lastRow.Thickness = focus;
                    rmsRefoc[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;
                    lastRow.Thickness = scaledLast;
                }

                if (!terminated)
                {
                    // focus at the design environment - the zero of the focus shift
                    ApplyTemperature(sys, snaps, imgIdx, 0);
                    env.Temperature = t0; env.Pressure = p0;
                    focus0 = MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness);
                    for (int k = 0; k < n; k++) focusShift[k] -= focus0;

                    // Isolate the pressure term: same temperature, scan pressure. This
                    // is the relative -> absolute index step (every glass index scales
                    // by n_air; air itself is 1.0 at the system pressure by definition),
                    // and it is a constant offset on the sweep, so it never enters dz/dT.
                    // Measure it at every scan pressure that actually differs from the
                    // design pressure. Measuring only at pStart reports ~0 for the
                    // common -psweep case, where the ramp begins at the design
                    // pressure and it is the far end that carries the whole term.
                    foreach (double pp in new[] { pStart, pEnd })
                    {
                        if (Math.Abs(pp - p0) <= 1e-12) continue;
                        if (pressureTerms.Any(kv => Math.Abs(kv.Key - pp) <= 1e-12)) continue;
                        env.Pressure = pp;
                        pressureTerms.Add(new KeyValuePair<double, double>(pp,
                            MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness) - focus0));
                    }

                    // ---- per-glass indices, both points at the SAME pressure ------
                    // Sampling these inside the sweep would mix dT with dP whenever
                    // -psweep is used, and the reported dn/dT would silently carry the
                    // pressure term.
                    app.ProgressMessage = "Measuring per-glass dn/dT...";
                    app.ProgressPercent = 85;
                    env.Pressure = pStart;
                    for (int e = 0; e < 2; e++)
                    {
                        double Te = e == 0 ? Opts.TMin : Opts.TMax;
                        ApplyTemperature(sys, snaps, imgIdx, Te - t0);
                        env.Temperature = Te;
                        var into = e == 0 ? indexAtMin : indexAtMax;
                        foreach (var kv in glassSurf)
                            into[kv.Key] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.INDX, kv.Value, primaryWave);
                    }
                }
            }
            finally
            {
                eflCheck = RestoreSystem(sys, env, snaps, imgIdx, t0, p0, tRaw, pRaw, adjust0, primaryWave);
            }

            if (terminated)
            {
                Say("Terminated by user - system restored, no analysis performed.");
                app.ProgressMessage = "Done. Terminated by user - system restored.";
                return;
            }

            R.Temps = temps; R.Press = press; R.FocusShift = focusShift;
            R.RmsFixed = rmsFixed; R.RmsRefoc = rmsRefoc; R.Efl = efl;
            R.PressureTerms = pressureTerms;
            R.EflCheck = eflCheck;
            R.EdgeFallbackSurfaces = EdgeFallbackRows.OrderBy(i => i).ToList();

            Say(F("Restoration check: EFFL back to {0:G9} (baseline {1:G9}) -> {2}",
                eflCheck, efl0, Math.Abs(eflCheck - efl0) < 1e-6 ? "OK" : "MISMATCH - check the system!"));
            if (EdgeFallbackRows.Count > 0)
                Say("NOTE: surface(s) " + string.Join(", ", EdgeFallbackRows.OrderBy(r => r)) +
                    " have no usable mechanical semi-diameter or a surface form this tool cannot take the " +
                    "sag of, so their gaps were expanded at the centre instead of along the edge. Those " +
                    "gaps will not match Make Thermal where the adjacent radii change.");
            if (pressureTerms.Count > 0)
            {
                Say("");
                Say("PRESSURE TERM at the design temperature (from the relative -> absolute index change;");
                Say("carried by the sweep at that pressure, and not part of dz/dT):");
                foreach (var kv in pressureTerms)
                    Say(F("  {0:F3} -> {1:F3} atm: {2:+0.00000;-0.00000} lens units   ({3:F1} x the depth of focus)",
                        p0, kv.Key, kv.Value, dofMm > 0 ? Math.Abs(kv.Value) / dofMm : 0));
            }

            // ---- sweep table ------------------------------------------------------
            Say("");
            if (pVaries)
            {
                Say("  T (C)   P (atm)   EFFL        focus shift    RMS fixed    RMS refocused");
                Say("  -----   -------   ---------   -----------    ---------    -------------");
                for (int k = 0; k < n; k++)
                    Say(F("  {0,6:F1}  {1,7:F3}   {2,9:F4}   {3,11:+0.00000;-0.00000}    {4,7:F2} um   {5,7:F2} um",
                        temps[k], press[k], efl[k], focusShift[k], rmsFixed[k], rmsRefoc[k]));
                Say("  (focus shift includes the pressure term - see PRESSURE TERM above)");
            }
            else
            {
                Say("  T (C)    EFFL        focus shift    RMS fixed    RMS refocused");
                Say("  -----    ---------   -----------    ---------    -------------");
                for (int k = 0; k < n; k++)
                    Say(F("  {0,6:F1}   {1,9:F4}   {2,11:+0.00000;-0.00000}    {3,7:F2} um   {4,7:F2} um",
                        temps[k], efl[k], focusShift[k], rmsFixed[k], rmsRefoc[k]));
            }

            // ---- athermal analysis ------------------------------------------------
            double slope = LinFit(temps, focusShift); // dz/dT, lens units per C
            Say("");
            Say(F("Thermal defocus rate dz/dT = {0:+0.000000;-0.000000} lens units / C", slope));
            double dtAthermal = Math.Abs(slope) > 1e-12 ? dofMm / Math.Abs(slope) : double.PositiveInfinity;
            Say(F("Fixed-plane athermal range: +/- {0:F1} C about the design temperature (defocus within the DOF)",
                dtAthermal));

            R.DzDt = slope; R.AthermalRangeC = dtAthermal;
            double alphaReq = slope / track * 1e6; // required housing CTE in 1e-6/K
            R.RequiredCte = alphaReq;
            Say("");
            Say(F("PASSIVE HOUSING COMPENSATION over mount track L = {0:F3}:", track));
            Say(F("  required housing CTE = dz/dT / L = {0:+0.00;-0.00} x 1e-6/K", alphaReq));
            var housings = new (string Name, double Cte)[]
            {
                ("Invar 36", 1.3), ("Titanium 6Al4V", 8.6), ("SS 416", 9.9), ("SS 304", 17.3),
                ("Brass", 18.7), ("Aluminum 6061", 23.6), ("Magnesium AZ31", 26.0), ("ALLVAR Alloy 30", -30.0),
            };
            Say("  housing material     CTE(1e-6/K)   residual dz/dT      usable +/- range");
            foreach (var h in housings.OrderBy(h => Math.Abs(h.Cte - alphaReq)))
            {
                double resid = slope - h.Cte * 1e-6 * track;
                double range = Math.Abs(resid) > 1e-12 ? dofMm / Math.Abs(resid) : double.PositiveInfinity;
                Say(F("  {0,-18}   {1,8:F1}      {2,12:+0.000000;-0.000000}    {3,8:F1} C", h.Name, h.Cte, resid, range));
                R.Housings.Add(new Results.HousingRow { Name = h.Name, Cte = h.Cte, ResidualDzDt = resid, UsableRangeC = range });
            }

            // exact bimetallic solution using the two materials bracketing alphaReq
            var lower = housings.Where(h => h.Cte < alphaReq).OrderByDescending(h => h.Cte).ToArray();
            var upper = housings.Where(h => h.Cte >= alphaReq).OrderBy(h => h.Cte).ToArray();
            if (lower.Length > 0 && upper.Length > 0)
            {
                var a = lower[0]; var b = upper[0];
                // L1*a1 + L2*a2 = alphaReq*L,  L1+L2 = L
                double L2 = track * (alphaReq - a.Cte) / (b.Cte - a.Cte);
                double L1 = track - L2;
                Say("");
                R.Bimetallic = F("{0:F3} of {1} + {2:F3} of {3} (total {4:F3})", L1, a.Name, L2, b.Name, track);
                Say(F("  exact bimetallic mount: {0:F3} of {1} + {2:F3} of {3} (total {4:F3})",
                    L1, a.Name, L2, b.Name, track));
            }
            else
            {
                Say("");
                Say("  NO two-metal combination reaches the required CTE: passive housing");
                Say("  compensation alone cannot athermalize this system. Consider optical");
                Say("  athermalization (combine glasses of opposite thermal constant x_f,");
                Say("  see the per-glass table), a re-entrant mount, or active focus.");
            }

            // ---- per-glass opto-thermal table --------------------------------------
            Say("");
            Say("PER-GLASS OPTO-THERMAL DATA (dn/dT measured from the live thermal model):");
            Say("  Index convention: " + Convention(pStart) + ", measured at a fixed " +
                F("{0:F3}", pStart) + " atm over " + F("{0:F0}..{1:F0} C", Opts.TMin, Opts.TMax) + ".");
            if (pStart > 1e-12)
                Say("  These are RELATIVE values: dn/dT is larger than the catalog/datasheet ABSOLUTE " +
                    "dn/dT by n*|dn_air/dT| (~1.4e-6/K at n=1.5, 1 atm). Run with -vacuum for absolute.");
            else
                Say("  These are ABSOLUTE (vacuum) values, directly comparable to catalog dn/dT_abs; " +
                    "an in-air design instead uses the relative values (run without -vacuum).");
            Say("  dn/dT is a secant over the whole sweep and n(T0) a linear interpolation, so both");
            Say("  degrade where the index is strongly non-linear in T (e.g. cryogenic ranges).");
            Say("  glass         n(T0)     dn/dT(1e-6/K)  TCE(1e-6/K)  x_f = dn/dT/(n-1) - a  (1e-6/K)");
            var xf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in glassSurf.Keys)
            {
                double nMin = indexAtMin[g], nMax = indexAtMax[g];
                double dndt = (nMax - nMin) / (Opts.TMax - Opts.TMin) * 1e6;
                double nT0 = nMin + (nMax - nMin) * (t0 - Opts.TMin) / (Opts.TMax - Opts.TMin);
                double x = dndt / (nT0 - 1) - glassTce[g];
                xf[g] = x;
                R.Glasses.Add(new Results.GlassRow { Name = g, NAtT0 = nT0, DnDt = dndt, Tce = glassTce[g], Xf = x,
                    NoThermalIndexData = noThermalIndex.Contains(g) });
                Say(F("  {0,-12}  {1,7:F5}   {2,10:F2}     {3,8:F2}     {4,10:+0.00;-0.00}{5}",
                    g, nT0, dndt, glassTce[g], x,
                    noThermalIndex.Contains(g) ? "   <- no thermal index data: not physical" : ""));
            }
            Say("  (x_f > 0: the element's focus lengthens when heated; pick pairs of opposite x_f");
            Say("   or match the housing to the composite to athermalize - see report for options.)");

            // approximate thin-element share of the thermal power change
            Say("");
            Say("APPROX. ELEMENT CONTRIBUTIONS (thin-element weights, marginal-ray^2 x power):");
            double y1 = Math.Abs(Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.PARY, 1, primaryWave, 0, 0, 0, 1));
            var contrib = new List<(string label, double val)>();
            for (int i = 1; i < imgIdx; i++)
            {
                var s = snaps[i - 1];
                if (!s.IsGlass) continue;
                double nT0 = indexAtMin.ContainsKey(s.Material)
                    ? indexAtMin[s.Material] + (indexAtMax[s.Material] - indexAtMin[s.Material]) * (t0 - Opts.TMin) / (Opts.TMax - Opts.TMin)
                    : 1.5;
                double cFront = (Math.Abs(s.Radius) > 1e10 || s.Radius == 0) ? 0 : 1.0 / s.Radius;
                double rBack = double.PositiveInfinity;
                if (i < imgIdx - 1 && snaps[i] != null) rBack = snaps[i].Radius;
                double cBack = (Math.Abs(rBack) > 1e10 || rBack == 0) ? 0 : 1.0 / rBack;
                double phi = (nT0 - 1) * (cFront - cBack);
                double yi = Math.Abs(Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.PARY, i, primaryWave, 0, 0, 0, 1));
                double w = phi * (yi * yi) / ((1.0 / efl0) * (y1 * y1));
                double c = w * (xf.ContainsKey(s.Material) ? xf[s.Material] : 0);
                contrib.Add((F("surface {0} ({1})", i, s.Material), c));
            }
            double totalC = contrib.Sum(c => Math.Abs(c.val)) + 1e-30;
            foreach (var c in contrib.OrderByDescending(c => Math.Abs(c.val)))
            {
                Say(F("  {0,-28}  weight*x_f = {1,8:+0.00;-0.00}   ({2,5:F1}% of total magnitude)",
                    c.label, c.val, 100.0 * Math.Abs(c.val) / totalC));
                R.Contributions.Add(new Results.ContribRow
                { Label = c.label, WeightedXf = c.val, PercentOfTotal = 100.0 * Math.Abs(c.val) / totalC });
            }

            // ---- outputs -----------------------------------------------------------
            // The User Analysis renders into its own OpticStudio window and has no
            // business scattering report files beside the lens, so it sets NoFiles.
            if (Opts.NoFiles) return;

            string prefix = Opts.OutPrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                string stem = string.IsNullOrEmpty(src)
                    ? "athermal" : Path.GetFileNameWithoutExtension(src) + "_athermal";

                // Default is beside the lens, which for a stock sample means writing
                // into the vendor's own Samples tree. The settings window can name a
                // folder instead; create it if it does not exist, and fall back rather
                // than lose a completed sweep to an unwritable path.
                string dir = null;
                if (!string.IsNullOrWhiteSpace(Opts.OutDir))
                {
                    try
                    {
                        Directory.CreateDirectory(Opts.OutDir);
                        dir = Opts.OutDir;
                    }
                    catch (Exception ex)
                    {
                        Say("WARNING: could not use the chosen output folder '" + Opts.OutDir + "' (" +
                            ex.Message + "). Writing beside the lens instead.");
                    }
                }
                if (dir == null)
                    dir = string.IsNullOrEmpty(src) ? app.ZemaxDataDir : Path.GetDirectoryName(src);

                prefix = Path.Combine(dir, stem);
            }
            if (string.IsNullOrEmpty(R.LensFile)) R.LensFile = Opts.FilePath ?? "";
            File.WriteAllLines(prefix + "_report.txt", Report);
            Chart(temps, focusShift, rmsFixed, rmsRefoc, dofMm, prefix + "_chart.png",
                Path.GetFileName(sys.SystemFile ?? ""));
            // The HTML report is the one meant to be read - the chart is inline SVG, so
            // it is a single file that scales and prints. The CSV and JSON are for
            // diffing runs against each other, which the text transcript cannot support.
            try { Reports.WriteHtml(prefix + "_report.html", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the HTML report: " + ex.Message); }
            try { Reports.WriteCsv(prefix + "_sweep.csv", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the CSV: " + ex.Message); }
            try { Reports.WriteJson(prefix + "_summary.json", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the JSON summary: " + ex.Message); }
            Console.WriteLine();
            Console.WriteLine("Report written to: " + prefix + "_report.html");
            Console.WriteLine("             and: " + prefix + "_report.txt");
            Console.WriteLine("Sweep  written to: " + prefix + "_sweep.csv");
            Console.WriteLine("Summary written to: " + prefix + "_summary.json");
            Console.WriteLine("Chart  written to: " + prefix + "_chart.png");
            // The progress line is the only text that survives in the GUI after a ribbon
            // run, so it names the file actually worth opening.
            app.ProgressMessage = F("Done. dz/dT = {0:+0.000000;-0.000000}/C, athermal +/-{1:F1} C, {2} - report: {3} (+ .txt, .csv, .json, .png)",
                slope, dtAthermal, Convention(pStart), Path.GetFileName(prefix + "_report.html"));
            // Only the HTML is opened: it already contains the chart, so opening the PNG
            // as well would just put a second window in front of the user.
            LaunchLog("wrote " + prefix + "_report.html (+ .txt, .csv, .json, .png)");
            OpenOutputs(app, prefix + "_report.html");
        }

        // Which index reference the reported numbers are in. OpticStudio always traces
        // RELATIVE index - air at the system temperature and pressure is exactly 1.0 at
        // all wavelengths - so the system pressure alone decides this (manual 2.1.1.4.2).
        static string Convention(double pAtm) =>
            pAtm <= 1e-12
                ? "ABSOLUTE (vacuum) - at P = 0 the air reference is unity"
                : F("RELATIVE to air at {0:F3} atm", pAtm);

        // TEMP and PRES multi-configuration operands set the environment for every
        // operand that follows them, and the last pair sets the global environment -
        // and this is true even with a single configuration (manual 2.1.1.4.5). The
        // system-level temperature this scan writes would then not describe what is
        // actually traced: a group given its own PRES keeps its own pressure, hence its
        // own relative/absolute index reference, while the report claims a uniform soak.
        static void CheckNoEnvironmentOperands(ZOSAPI.IOpticalSystem sys)
        {
            var found = new List<string>();
            try
            {
                var mce = sys.MCE;
                for (int r = 1; r <= mce.NumberOfOperands; r++)
                {
                    var op = mce.GetOperandAt(r);
                    if (op == null) continue;
                    if (op.Type == ZOSAPI.Editors.MCE.MultiConfigOperandType.TEMP ||
                        op.Type == ZOSAPI.Editors.MCE.MultiConfigOperandType.PRES)
                        found.Add(F("row {0}: {1}", r, op.Type));
                }
            }
            catch { return; } // no MCE access - nothing to object to
            if (found.Count == 0) return;
            throw new Exception(
                "the multi-configuration editor already defines the environment (" + string.Join(", ", found) +
                "). TEMP/PRES operands govern every operand listed after them and the last pair governs " +
                "everything else, so surfaces in a separately specified group would keep their own " +
                "temperature and pressure - and their own relative/absolute index reference - while this " +
                "scan reported a uniform soak. Analyse this system through the multi-configuration " +
                "workflow instead.");
        }

        // ApplyTemperature writes radius/thickness/parameter values directly, so a solve
        // that COMPUTES its cell overwrites what we write and the thermal model becomes
        // silently wrong - most visibly a marginal ray height solve on the last
        // thickness, which auto-refocuses and reports a focus shift of zero. Variables
        // are harmless: they mark a cell for optimisation, they do not compute it.
        static List<ZOSAPI.Editors.LDE.SurfaceColumn> WritableColumns()
        {
            var cols = new List<ZOSAPI.Editors.LDE.SurfaceColumn>
            {
                ZOSAPI.Editors.LDE.SurfaceColumn.Radius,
                ZOSAPI.Editors.LDE.SurfaceColumn.Thickness,
            };
            for (int p = 1; p <= 8; p++)
                cols.Add((ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p));
            return cols;
        }

        /// <summary>
        /// Value-computing solves on cells the scan must write, described one per entry.
        /// Pure - it reports, it does not freeze or throw, so the settings window can
        /// ask this BEFORE the user commits to a run.
        /// </summary>
        internal static List<string> FindComputingSolves(ZOSAPI.Editors.LDE.ILensDataEditor lde, int imgIdx)
        {
            var found = new List<string>();
            foreach (var col in WritableColumns())
                for (int i = 1; i < imgIdx; i++)
                {
                    ZOSAPI.Editors.SolveType st;
                    try { st = lde.GetSurfaceAt(i).GetSurfaceCell(col).Solve; }
                    catch { continue; } // locked or non-existent cell for this surface type
                    if (st == ZOSAPI.Editors.SolveType.None || st == ZOSAPI.Editors.SolveType.Fixed ||
                        st == ZOSAPI.Editors.SolveType.Variable || st == ZOSAPI.Editors.SolveType.Automatic)
                        continue;
                    found.Add(F("surface {0} {1} ({2})", i, col, st));
                }
            return found;
        }

        static void CheckSolves(ZOSAPI.Editors.LDE.ILensDataEditor lde, int imgIdx)
        {
            if (!Opts.FreezeSolves)
            {
                var offenders = FindComputingSolves(lde, imgIdx);
                if (offenders.Count == 0) return;
                throw new Exception(
                    "value-computing solves sit on cells this scan must write - " +
                    string.Join("; ", offenders.Take(8)) +
                    (offenders.Count > 8 ? F("; and {0} more", offenders.Count - 8) : "") +
                    ". A solve recomputes its cell after every assignment, so the thermal model would be " +
                    "silently overridden: a marginal ray height solve on the last thickness, for instance, " +
                    "auto-refocuses and reports a focus shift of zero. " +
                    // The remedy has to be one the reader can actually carry out. A
                    // ribbon user cannot pass a flag - OpticStudio gives them no way to
                    // - so point them at the checkbox that does the same thing.
                    (Opts.HostLaunched
                        ? "Tick 'Freeze value-computing solves' in the settings window, or remove the solves."
                        : "Remove them, or re-run with -freezesolves to freeze them to their current values " +
                          "first (not undone on restore)."));
            }

            int frozen = 0;
            foreach (var col in WritableColumns())
                for (int i = 1; i < imgIdx; i++)
                {
                    ZOSAPI.Editors.IEditorCell cell;
                    ZOSAPI.Editors.SolveType st;
                    try { cell = lde.GetSurfaceAt(i).GetSurfaceCell(col); st = cell.Solve; }
                    catch { continue; }
                    if (st == ZOSAPI.Editors.SolveType.None || st == ZOSAPI.Editors.SolveType.Fixed ||
                        st == ZOSAPI.Editors.SolveType.Variable || st == ZOSAPI.Editors.SolveType.Automatic)
                        continue;
                    try { if (cell.MakeSolveFixed()) frozen++; } catch { }
                }
            if (frozen > 0)
                Say(F("Froze {0} value-computing solve(s) to their current values. This is NOT undone by " +
                      "the restore - do not save the file unless that is what you want.", frozen));
        }

        // Undo everything the scan touched. Called from a finally, so it must not throw:
        // a failed step is reported and the remaining steps are still attempted. Returns
        // the design-environment EFFL for the restoration check, or NaN.
        static double RestoreSystem(ZOSAPI.IOpticalSystem sys, ZOSAPI.SystemData.ISDEnvironmentData env,
            RowSnap[] snaps, int imgIdx, double t0, double p0, double tRaw, double pRaw,
            bool adjust0, int primaryWave)
        {
            double check = double.NaN;
            try { ApplyTemperature(sys, snaps, imgIdx, 0); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not restore the prescription: " + ex.Message); }
            // measured at the design environment with the adjust switch still on, so it
            // is compared in the same index state as the baseline
            try
            {
                env.Temperature = t0; env.Pressure = p0;
                check = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
            }
            catch (Exception ex) { Console.WriteLine("WARNING: restoration check failed: " + ex.Message); }
            try { env.Temperature = tRaw; env.Pressure = pRaw; env.AdjustIndexToEnvironment = adjust0; }
            catch (Exception ex) { Console.WriteLine("WARNING: could not restore the environment: " + ex.Message); }
            return check;
        }

        // Rows whose gap could not use the edge model and fell back to centre
        // scaling - reported once, since silently using a different physics than
        // the one documented is exactly what this replaced.
        static readonly HashSet<int> EdgeFallbackRows = new HashSet<int>();

        // OpticStudio does NOT scale a non-glass thickness at the centre. The
        // thermal pickup expands the material along a length running from the edge
        // of this surface to the edge of the next, because a mount touches the
        // lenses at their rims, and the result is then transferred back onto the
        // centre thickness (manual 2.1.1.4.4.2). Two consequences the centre-scaling
        // approximation misses entirely:
        //
        //   * the sag change of both bounding surfaces feeds into the gap, so even a
        //     TCE of 0 moves an air space when the adjacent radii move - the manual
        //     is explicit that a 0 TCE is not the way to freeze a thickness.
        //
        // Two details here are measured against Make Thermal's own pickup solves
        // rather than taken from the manual, because the manual's account of them
        // does not survive contact with the numbers:
        //
        //   * the edge is measured at the CLEAR semi-diameter, not the mechanical
        //     one. The manual says "the mechanical semi diameters for each surface
        //     are what determine this edge thickness", but changing a mechanical
        //     semi-diameter from 14 to 20 with the clear semi-diameter held at 12
        //     moves OpticStudio's answer by exactly nothing;
        //   * there is NO contact-point walk. The manual describes the mount and
        //     rim expanding at different rates so the contact point migrates
        //     radially, with a clamp to keep it on the lens. Modelling that walk
        //     leaves a residual of ~0.85 um on the test gap; evaluating both sags
        //     at the same unexpanded height reproduces OpticStudio to ~0.02 um
        //     across curved/plano and TCE 23.6/0 variants. Whatever the walk is
        //     for, it does not show up in a THIC thermal pickup.
        //
        // Sag is evaluated on the snapshot, analytically, for the surface forms this
        // tool already expands (standard/conic, even and odd asphere). Anything else
        // bounding the gap falls back to centre scaling and is named in the report.
        static double EdgeExpandedThickness(RowSnap[] snaps, int i, int imgIdx, double dT, out bool ok)
        {
            ok = false;
            var s = snaps[i - 1];
            RowSnap next = i <= imgIdx - 2 ? snaps[i] : null;   // null => the image plane
            double h0 = s.SemiDia > 0 ? s.SemiDia : s.MechSemiDia;
            if (!(h0 > 0) || double.IsNaN(h0) || double.IsInfinity(h0)) return 0;

            double aMount = s.AlphaThick;    // the spacer / mount material

            // the image plane closes the last gap and is treated as flat
            bool o1, o3, o2 = true, o4 = true;
            double zA0 = Sag(s, h0, 1.0, out o1);
            double zB0 = next == null ? 0 : Sag(next, h0, 1.0, out o2);

            double eRa = 1 + s.AlphaRadius * 1e-6 * dT;
            double eRb = next == null ? 1 : 1 + next.AlphaRadius * 1e-6 * dT;
            double zA1 = Sag(s, h0, eRa, out o3);
            double zB1 = next == null ? 0 : Sag(next, h0, eRb, out o4);

            if (!(o1 && o2 && o3 && o4)) return 0;

            double edge0 = s.Thickness + zB0 - zA0;              // as-built edge length
            double edge1 = edge0 * (1 + aMount * 1e-6 * dT);     // it is the edge that expands
            double t = edge1 - zB1 + zA1;                        // transferred to the centre
            if (double.IsNaN(t) || double.IsInfinity(t)) return 0;
            ok = true;
            return t;
        }

        // Sag of a snapshotted surface at radial height h, with its radius and
        // polynomial terms expanded by eR (eR = 1 gives the as-built surface). The
        // conic is dimensionless and does not scale.
        static double Sag(RowSnap s, double h, double eR, out bool ok)
        {
            ok = false;
            bool even = s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric;
            bool odd = s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere;
            if (s.Type != ZOSAPI.Editors.LDE.SurfaceType.Standard && !even && !odd) return 0;

            double z = 0, R = s.Radius * eR;
            if (!(double.IsInfinity(R) || Math.Abs(R) > 1e10 || R == 0))
            {
                double c = 1.0 / R;
                double u = 1 - (1 + s.Conic) * c * c * h * h;
                if (u < 0) return 0;                 // h is off the surface
                z = c * h * h / (1 + Math.Sqrt(u));
            }
            if (even || odd)
                for (int p = 1; p <= 8; p++)
                {
                    if (s.Pars[p] == 0) continue;
                    int powr = even ? 2 * p : p;
                    z += s.Pars[p] * Math.Pow(eR, 1 - powr) * Math.Pow(h, powr);
                }
            ok = !double.IsNaN(z) && !double.IsInfinity(z);
            return z;
        }

        // apply the thermal model relative to the snapshot (dT = 0 restores)
        static void ApplyTemperature(ZOSAPI.IOpticalSystem sys, RowSnap[] snaps, int imgIdx, double dT)
        {
            var lde = sys.LDE;
            for (int i = 1; i < imgIdx; i++)
            {
                var s = snaps[i - 1];
                var row = lde.GetSurfaceAt(i);
                double eT = 1 + s.AlphaThick * 1e-6 * dT;
                double eR = 1 + s.AlphaRadius * 1e-6 * dT;

                if (s.IsGlass)
                {
                    // A catalog glass expands as a solid: centre thickness scales.
                    row.Thickness = s.Thickness * eT;
                }
                else
                {
                    bool ok;
                    double t = EdgeExpandedThickness(snaps, i, imgIdx, dT, out ok);
                    if (!ok) { t = s.Thickness * eT; EdgeFallbackRows.Add(i); }
                    row.Thickness = t;
                }
                if (s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                {
                    if (!(Math.Abs(s.Radius) > 1e10 || s.Radius == 0))
                        try { row.Radius = s.Radius * eR; } catch { }
                    if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ||
                        s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere)
                    {
                        for (int p = 1; p <= 8; p++)
                        {
                            if (s.Pars[p] == 0) continue;
                            int powr = s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ? 2 * p : p;
                            try
                            {
                                var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                                row.GetSurfaceCell(col).DoubleValue = s.Pars[p] * Math.Pow(eR, 1 - powr);
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        // image-space marginal focus position measured from the last optical surface
        static double MarginalFocus(ZOSAPI.IOpticalSystem sys, int imgIdx, int wave, double lastGap)
        {
            double y = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAY, imgIdx, wave, 0, 0, 0, 1);
            double m = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAB, imgIdx, wave, 0, 0, 0, 1);
            double nz = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAC, imgIdx, wave, 0, 0, 0, 1);
            double u = Math.Abs(nz) > 1e-14 ? m / nz : 0;
            if (Math.Abs(u) < 1e-14) return lastGap;
            return lastGap - y / u;
        }

        static double LinFit(double[] x, double[] y)
        {
            int n = x.Length;
            double sx = x.Sum(), sy = y.Sum(), sxx = x.Sum(v => v * v), sxy = 0;
            for (int i = 0; i < n; i++) sxy += x[i] * y[i];
            return (n * sxy - sx * sy) / (n * sxx - sx * sx);
        }

        static void Chart(double[] t, double[] dz, double[] rmsF, double[] rmsR,
            double dof, string path, string title)
        {
            int W = 1200, H = 800;
            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                var font = new Font("Segoe UI", 10f);
                var fontB = new Font("Segoe UI", 12f, FontStyle.Bold);
                var black = new SolidBrush(Color.Black);

                g.DrawString("Athermal scan - " + title, fontB, black, 20, 8);
                Panel(g, font, 60, 50, W - 100, 320, t, new[] { (dz, Color.FromArgb(0, 90, 200), "focus shift") },
                    "focus shift (lens units)", dof);
                Panel(g, font, 60, 430, W - 100, 320, t,
                    new[] { (rmsF, Color.FromArgb(200, 30, 30), "RMS @ fixed plane"),
                            (rmsR, Color.FromArgb(0, 140, 0), "RMS refocused") },
                    "RMS spot (um)", 0);
                g.DrawString("temperature (C)", font, black, W / 2 - 40, H - 28);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Panel(Graphics g, Font font, int x, int y, int w, int h, double[] t,
            (double[] data, Color color, string label)[] series, string yLabel, double dofBand)
        {
            double xmin = t.Min(), xmax = t.Max();
            double ymin = series.SelectMany(s => s.data).Min();
            double ymax = series.SelectMany(s => s.data).Max();
            if (dofBand > 0) { ymin = Math.Min(ymin, -dofBand * 1.2); ymax = Math.Max(ymax, dofBand * 1.2); }
            if (ymax - ymin < 1e-12) { ymax += 1; ymin -= 1; }
            double pad = 0.08 * (ymax - ymin); ymin -= pad; ymax += pad;
            float PX(double v) => (float)(x + (v - xmin) / (xmax - xmin) * w);
            float PY(double v) => (float)(y + h - (v - ymin) / (ymax - ymin) * h);

            if (dofBand > 0)
                using (var band = new SolidBrush(Color.FromArgb(40, 0, 180, 0)))
                    g.FillRectangle(band, x, PY(dofBand), w, PY(-dofBand) - PY(dofBand));

            using (var axis = new Pen(Color.Black, 1.5f))
            using (var grid = new Pen(Color.FromArgb(230, 230, 230), 1f))
            using (var black = new SolidBrush(Color.Black))
            {
                for (int k = 0; k <= 4; k++)
                {
                    double tv = xmin + (xmax - xmin) * k / 4;
                    g.DrawLine(grid, PX(tv), y, PX(tv), y + h);
                    g.DrawString(tv.ToString("F0"), font, black, PX(tv) - 10, y + h + 4);
                    double yv = ymin + (ymax - ymin) * k / 4;
                    g.DrawLine(grid, x, PY(yv), x + w, PY(yv));
                    g.DrawString(yv.ToString("G3"), font, black, 4, PY(yv) - 8);
                }
                if (ymin < 0 && ymax > 0)
                    using (var zero = new Pen(Color.Gray, 1f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(zero, x, PY(0), x + w, PY(0));
                g.DrawRectangle(axis, x, y, w, h);
                g.DrawString(yLabel, font, black, x, y - 20);

                int lx = x + w - 190, ly = y + 8;
                foreach (var s in series)
                {
                    using (var pen = new Pen(s.color, 2.2f))
                    {
                        var pts = new PointF[t.Length];
                        for (int i = 0; i < t.Length; i++) pts[i] = new PointF(PX(t[i]), PY(s.data[i]));
                        g.DrawLines(pen, pts);
                        foreach (var p in pts) g.FillEllipse(new SolidBrush(s.color), p.X - 3, p.Y - 3, 6, 6);
                        g.DrawLine(pen, lx, ly + 7, lx + 24, ly + 7);
                    }
                    g.DrawString(s.label, font, new SolidBrush(s.color), lx + 28, ly);
                    ly += 18;
                }
            }
        }
    }
}

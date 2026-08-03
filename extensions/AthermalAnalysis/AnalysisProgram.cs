using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ZOSAPI;
using ZOSAPI.Analysis;

namespace AthermalScan
{
    // AthermalScan as a ZOS-API User Analysis: the same sweep, rendered into a native
    // dockable OpticStudio window instead of files that have to be opened separately.
    //
    // It links AthermalScan's own Program.cs rather than reimplementing anything. The
    // thermal model in there is validated to all 14 displayed figures against Make
    // Thermal's pickup solves, and a second copy of that physics would be a second
    // thing to keep in step - the exact defect this codebase has been bitten by.
    // StartupObject in the .csproj selects this entry point over the extension's.
    //
    // THE MUTATION PROBLEM, and why this is safe: the sweep writes radii and
    // thicknesses into the system and restores them afterwards. An analysis window
    // re-runs whenever the system changes, so pointing that at the live prescription
    // would be a loop - the analysis edits the system, the edit triggers a refresh,
    // the refresh edits again. So it runs on IOpticalSystem.CopySystem(), a detached
    // clone. The open prescription is never written to at all, the restore becomes
    // belt-and-braces rather than load-bearing, and a mid-run failure cannot damage
    // anything the user has open.
    static class AnalysisProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            // FIRST statement, before anything that can fail. The previous version
            // logged only after locating OpticStudio, connecting, and checking the
            // licence - so all four of those failure paths exited silently and an
            // absent log could not be distinguished from the host never launching the
            // process at all. Both happened; only this tells them apart.
            Program.LaunchLog("AthermalAnalysis Main: argc=" + (args == null ? 0 : args.Length) +
                              " argv=[" + string.Join(" ", args ?? new string[0]) + "]");

            if (!ZemaxLocator.Initialize())
            {
                Program.LaunchLog("  FATAL: no OpticStudio installation found");
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
                return;
            }
            Program.LaunchLog("  ZOSAPI from " + (ZemaxLocator.ResolvedDirectory ?? "(unknown)"));
            Begin();
        }

        // Every ZOSAPI type is confined below this line, in a method the JIT does not
        // compile until it is called. Main must not so much as DECLARE a ZOSAPI-typed
        // local: the JIT resolves a method's types when it compiles that method, so a
        // ZOSAPI reference in Main forces the assembly to load BEFORE
        // ZemaxLocator.Initialize() has installed the resolver that finds it. The
        // symptom is brutal - FileNotFoundException on ZOSAPI_Interfaces before the
        // first statement of Main runs, so not even a log line is written, and the
        // process looks like it never started. NoInlining stops the optimiser undoing
        // the split.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Begin()
        {
            IZOSAPI_Application app;
            try { app = new ZOSAPI_Connection().ConnectToApplication(); }
            catch (Exception ex)
            {
                // Thrown verbatim when the exe is double-clicked instead of being
                // launched from Programming > User Analyses.
                Program.LaunchLog("  FATAL: ConnectToApplication threw: " + ex.Message);
                Console.WriteLine("FATAL: " + ex.Message);
                return;
            }
            if (app == null)
            {
                Program.LaunchLog("  FATAL: ConnectToApplication returned null");
                Console.WriteLine("FATAL: no connection to OpticStudio."); return;
            }
            if (!app.IsValidLicenseForAPI)
            {
                Program.LaunchLog("  FATAL: licence not valid for ZOS-API: " + app.LicenseStatus);
                Console.WriteLine("FATAL: license is not valid for ZOS-API: " + app.LicenseStatus +
                                  " (loaded from " + (ZemaxLocator.ResolvedDirectory ?? "an unknown directory") + ")");
                return;
            }

            // An analysis writes no files and its console goes nowhere, so without
            // this a failure on someone else's machine leaves nothing to look at.
            // AthermalScan-launch.log lands beside this .exe in the User Analysis
            // folder; the extension writes its own copy beside itself in Extensions.
            Program.LaunchLog("AthermalAnalysis start: mode=" + app.Mode);
            try
            {
                switch (app.Mode)
                {
                    case ZOSAPI_Mode.UserAnalysis: RunAnalysis(app); break;
                    case ZOSAPI_Mode.UserAnalysisSettings: ShowSettings(app); break;
                    default:
                        Program.LaunchLog("FATAL: wrong mode - expected UserAnalysis, found " + app.Mode);
                        Console.WriteLine("FATAL: started in the wrong mode: expected UserAnalysis, found " + app.Mode);
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.LaunchLog("FATAL (unhandled): " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        }

        // ---- settings ------------------------------------------------------------
        // OpticStudio re-launches this program in UserAnalysisSettings mode when the
        // user opens the analysis's settings, and expects it to put up its own window.
        // That is the same ScanSettingsDialog the ribbon extension uses - one dialog,
        // one set of defaults, one lastrun.txt.
        static void ShowSettings(IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            var env = sys.SystemData.Environment;
            var o = new Options();
            ScanSettingsDialog.Show(env.Temperature, env.Pressure, env.AdjustIndexToEnvironment, o);
            // ScanSettingsDialog persists to %APPDATA%\AthermalScan\lastrun.txt on OK
            // and leaves it alone on Cancel, which is what RunAnalysis reads back. The
            // ISettingsData store is deliberately not used as a second home for the
            // same values - two stores would be two things to keep in step.
        }

        // ---- the analysis --------------------------------------------------------
        static void RunAnalysis(IZOSAPI_Application app)
        {
            var data = app.UserAnalysisData;
            data.WindowTitle = "Athermal Scan";

            IOpticalSystem work = null;
            try
            {
                var live = app.PrimarySystem;
                if (live.Mode != SystemType.Sequential)
                {
                    Emit(data, "Athermal Scan requires a sequential system.");
                    return;
                }

                // Settings come from the same file the dialog writes; ScanSettingsDialog
                // loads them, so an unconfigured analysis just gets the documented
                // defaults (-20..60 C, 9 steps, the file's own environment).
                Program.Opts = new Options { NoFiles = true, NoDialog = true, Quiet = true, HostLaunched = true };
                ScanSettingsDialog.LoadInto(Program.Opts);

                // The two guards below are right for the EXTENSION and wrong here, and
                // the difference is the clone. Both exist to stop a silently wrong
                // model being computed against the user's own system; this sweep runs
                // on a throwaway copy, so there is nothing to protect and refusing is
                // pure loss. Worse, both refusals name command-line flags, and an
                // analysis window has no command line at all - the same dead end the
                // ribbon had. Neither can fire from here now.
                //
                // Freezing solves on a clone that is closed seconds later cannot
                // damage anything: the user's prescription is never touched.
                Program.Opts.FreezeSolves = true;

                // With Adjust Index Data To Environment off, OpticStudio itself
                // evaluates index data as though the system were at 20 C / 1.0 atm, so
                // that convention is the honest reading of such a file rather than a
                // guess. The report says so in full, and the settings window overrides
                // it for anyone who knows the real design point.
                var liveEnv = live.SystemData.Environment;
                if (!liveEnv.AdjustIndexToEnvironment && !Program.Opts.Temp0.HasValue)
                {
                    Program.Opts.Temp0 = 20.0;
                    if (!Program.Opts.Press0.HasValue) Program.Opts.Press0 = 1.0;
                }

                work = live.CopySystem();
                if (work == null)
                {
                    Emit(data, "Could not copy the system; nothing was analysed.");
                    return;
                }

                Program.Report.Clear();
                Program.LaunchLog("  analysing a CopySystem() clone of " +
                                  (string.IsNullOrEmpty(live.SystemFile) ? "(untitled)" : live.SystemFile));
                Program.Analyze(app, work);

                var r = Program.R;
                Emit(data, string.Join("\r\n", Program.Report));
                Program.LaunchLog("  ok: dz/dT=" + r.DzDt + " over " +
                                  (r.Temps == null ? 0 : r.Temps.Length) + " points, " +
                                  Program.Report.Count + " report lines");

                // MEASURED, since the documentation says nothing either way: an
                // analysis holds text plus exactly ONE plot. Calling Make2DLinePlot a
                // second time throws nothing and reports nothing - the second plot
                // simply replaces the first, so an earlier version that made a focus
                // plot and then an RMS plot silently showed only the RMS one.
                //
                // So the single plot has to carry the conclusion. That is the focus
                // shift, because every headline number here - dz/dT, the athermal
                // range, the required housing CTE - is derived from it, while RMS is
                // context and stays in the text. The depth-of-focus limits go on as
                // two flat series: same units as the shift, so the temperature range
                // over which the design stays in focus can be read straight off the
                // crossing points instead of computed from the table.
                if (r.Temps != null && r.Temps.Length > 0)
                {
                    try
                    {
                        var plot = data.Make2DLinePlotSafe("Focus shift vs temperature", r.Temps);
                        plot.XLabel = "temperature (C)";
                        plot.YLabel = "focus shift (lens units)";
                        plot.AddSeriesSafe("focus shift", ZOSAPI.Common.ZemaxColor.Color1, r.FocusShift);

                        if (r.DofMm > 0)
                        {
                            var plus = new double[r.Temps.Length];
                            var minus = new double[r.Temps.Length];
                            for (int i = 0; i < r.Temps.Length; i++) { plus[i] = r.DofMm; minus[i] = -r.DofMm; }
                            plot.AddSeriesSafe("+ depth of focus", ZOSAPI.Common.ZemaxColor.Color2, plus);
                            plot.AddSeriesSafe("- depth of focus", ZOSAPI.Common.ZemaxColor.Color3, minus);
                        }
                        data.ShowLegend = true;
                        Program.LaunchLog("  plot: focus shift + " +
                                          (r.DofMm > 0 ? "2 depth-of-focus limits" : "no DOF band"));
                    }
                    catch (Exception ex)
                    {
                        Program.LaunchLog("  plot NOT rendered: " + ex.Message);
                        Console.WriteLine("plot not rendered: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LaunchLog("  FAILED: " + ex.Message);
                Emit(data, "Athermal Scan failed: " + ex.Message);
            }
            finally
            {
                // The clone is detached; close it so it cannot linger as a second
                // system. Failure here is not worth surfacing over the results.
                try { if (work != null) work.Close(false); } catch { }
            }
        }

        static void Emit(IUserAnalysisData data, string text)
        {
            try
            {
                var t = data.MakeText();
                t.Data = text;
            }
            catch (Exception ex)
            {
                Console.WriteLine("could not write the analysis text: " + ex.Message);
            }
        }
    }
}

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
                Program.Opts = new Options { NoFiles = true, NoDialog = true, Quiet = true };
                ScanSettingsDialog.LoadInto(Program.Opts);

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

                // The text carries the whole report - it is the part that must survive.
                // The plots are attempted afterwards and guarded separately, because
                // whether an analysis may hold text AND line plots at once is not
                // documented either way, and a refusal must not cost the results.
                if (r.Temps != null && r.Temps.Length > 0)
                {
                    try
                    {
                        var focus = data.Make2DLinePlotSafe("Focus shift vs temperature", r.Temps);
                        focus.XLabel = "temperature (C)";
                        focus.YLabel = "focus shift (lens units)";
                        focus.AddSeriesSafe("focus shift", ZOSAPI.Common.ZemaxColor.Color1, r.FocusShift);

                        var rms = data.Make2DLinePlotSafe("RMS spot vs temperature", r.Temps);
                        rms.XLabel = "temperature (C)";
                        rms.YLabel = "RMS spot (micron)";
                        rms.AddSeriesSafe("RMS @ fixed plane", ZOSAPI.Common.ZemaxColor.Color2, r.RmsFixed);
                        rms.AddSeriesSafe("RMS refocused", ZOSAPI.Common.ZemaxColor.Color3, r.RmsRefoc);
                        data.ShowLegend = true;
                    }
                    catch (Exception ex)
                    {
                        Program.LaunchLog("  plots NOT rendered: " + ex.Message);
                        Console.WriteLine("plots not rendered: " + ex.Message);
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

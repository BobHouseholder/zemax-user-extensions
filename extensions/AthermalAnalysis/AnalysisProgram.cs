using System;
using System.Linq;
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
        static void Main()
        {
            if (!ZemaxLocator.Initialize())
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
                return;
            }

            IZOSAPI_Application app;
            try { app = new ZOSAPI_Connection().ConnectToApplication(); }
            catch (Exception ex)
            {
                // Thrown verbatim when the exe is double-clicked instead of being
                // launched from Programming > User Analyses.
                Console.WriteLine("FATAL: " + ex.Message);
                return;
            }
            if (app == null) { Console.WriteLine("FATAL: no connection to OpticStudio."); return; }
            if (!app.IsValidLicenseForAPI)
            {
                Console.WriteLine("FATAL: license is not valid for ZOS-API: " + app.LicenseStatus +
                                  " (loaded from " + (ZemaxLocator.ResolvedDirectory ?? "an unknown directory") + ")");
                return;
            }

            switch (app.Mode)
            {
                case ZOSAPI_Mode.UserAnalysis: RunAnalysis(app); break;
                case ZOSAPI_Mode.UserAnalysisSettings: ShowSettings(app); break;
                default:
                    Console.WriteLine("FATAL: started in the wrong mode: expected UserAnalysis, found " + app.Mode);
                    break;
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
                Program.Analyze(app, work);

                var r = Program.R;
                Emit(data, string.Join("\r\n", Program.Report));

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
                        Console.WriteLine("plots not rendered: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
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

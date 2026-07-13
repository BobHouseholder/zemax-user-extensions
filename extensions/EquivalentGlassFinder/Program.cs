using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EquivalentGlassFinder
{
    // Equivalent Glass Finder — a ZOS-API User Extension.
    //
    // Solves the problem raised in the Zemax community thread
    // "Equivalent Glass Feature Proposal"
    // (https://community.zemax.com/got-a-question-7/equivalent-glass-feature-proposal-881):
    // designers need a one-click way to swap the glasses in a design for the
    // equivalent or closest available materials from a chosen catalog, to cut
    // cost and avoid long lead times (e.g. obsolete glasses).
    //
    // For every glass surface in the loaded system the extension finds the
    // closest available catalog glass by weighted distance in (nd, vd, dPgF),
    // reports the top candidates, applies the best match, and prints
    // before/after performance (EFFL, merit function, RMS spot per field).
    //
    // Usage (no arguments needed when launched from the Programming ribbon):
    //   -catalog NAME     draw replacements from this catalog and consider ALL
    //                     glasses (default: catalogs in use, obsolete-only)
    //   -includeObsolete  allow obsolete glasses as candidates
    //   -report           report only, do not modify the system
    //   -reopt            re-optimize existing variables after the swap
    //   -save             save the modified system as <file>_EquivGlass.zmx
    //   -top N            number of candidates to list per glass (default 3)
    //   -wnd/-wvd/-wpgf W distance weights (defaults 100 / 1 / 500)
    //   -quiet            do not auto-open the report after a ribbon (GUI) run
    class Options
    {
        public string TargetCatalog = null;
        public bool IncludeObsolete = false;
        public bool ReportOnly = false;
        public bool ReOptimize = false;
        public bool SaveCopy = false;
        public int TopN = 3;
        public double WeightNd = 100.0;
        public double WeightVd = 1.0;
        public double WeightPgF = 500.0;
        public bool Quiet = false;
    }

    class GlassInfo
    {
        public string Name;
        public string Catalog;
        public double Nd;
        public double Vd;
        public double DPgF;
        public string Status;
        public bool ExcludeSubstitution;
    }

    class Program
    {
        static Options Opts = new Options();
        static readonly List<string> Report = new List<string>();

        // Main deliberately contains no ZOSAPI types: the assemblies are only
        // resolvable after ZOSAPI_NetHelper.Initialize() has located OpticStudio.
        static void Main(string[] args)
        {
            ParseArgs(args);

            bool isInitialized = ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize();
            if (!isInitialized)
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("Found OpticStudio at: " + ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory());

            try
            {
                Run();
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
                string a = args[i].TrimStart('-', '/').ToLowerInvariant();
                switch (a)
                {
                    case "catalog": if (i + 1 < args.Length) Opts.TargetCatalog = args[++i]; break;
                    case "includeobsolete": Opts.IncludeObsolete = true; break;
                    case "report": Opts.ReportOnly = true; break;
                    case "reopt": Opts.ReOptimize = true; break;
                    case "save": Opts.SaveCopy = true; break;
                    case "top": if (i + 1 < args.Length) Opts.TopN = ParseInt(args[++i], Opts.TopN); break;
                    case "wnd": if (i + 1 < args.Length) Opts.WeightNd = ParseDouble(args[++i], Opts.WeightNd); break;
                    case "wvd": if (i + 1 < args.Length) Opts.WeightVd = ParseDouble(args[++i], Opts.WeightVd); break;
                    case "wpgf": if (i + 1 < args.Length) Opts.WeightPgF = ParseDouble(args[++i], Opts.WeightPgF); break;
                    case "quiet": Opts.Quiet = true; break;
                }
            }
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

        static void Say(string line)
        {
            Console.WriteLine(line);
            Report.Add(line);
        }

        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        static void Run()
        {
            var connection = new ZOSAPI.ZOSAPI_Connection();
            ZOSAPI.IZOSAPI_Application app = null;

            // Launched from the OpticStudio Programming ribbon -> ConnectToApplication.
            // Launched from a shell while OpticStudio waits in Interactive Extension
            // mode -> ConnectAsExtension(0). Support both.
            try { app = connection.ConnectToApplication(); }
            catch { app = null; }
            if (app == null)
            {
                try { app = connection.ConnectAsExtension(0); }
                catch { app = null; }
            }
            if (app == null)
            {
                Console.WriteLine("FATAL: could not connect to OpticStudio. Launch this tool from the");
                Console.WriteLine("Programming ribbon, or turn on Programming > Interactive Extension first.");
                Environment.ExitCode = 1;
                return;
            }
            if (!app.IsValidLicenseForAPI)
            {
                Console.WriteLine("FATAL: license is not valid for ZOS-API: " + app.LicenseStatus);
                Environment.ExitCode = 1;
                return;
            }
            Say("Connected to OpticStudio (mode: " + app.Mode + ")");
            // let the user watch the glasses swap in the LDE during the run
            try { app.ShowChangesInUI = true; } catch { }

            try
            {
                RunOnSystem(app);
            }
            finally
            {
                app.ProgressPercent = 100;
                if (string.IsNullOrEmpty(app.ProgressMessage) || !app.ProgressMessage.StartsWith("Done"))
                    app.ProgressMessage = "Equivalent Glass Finder finished.";
            }
        }

        static void RunOnSystem(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
            {
                Say("This extension requires a sequential system.");
                return;
            }

            Say("");
            Say("=== Equivalent Glass Finder ===");
            Say("Lens file : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say("Mode      : " + (Opts.ReportOnly ? "report only" : "apply best match"));
            Say("Target    : " + (Opts.TargetCatalog ?? "(catalogs in use, obsolete glasses only)"));
            Say("");

            app.ProgressPercent = 5;
            app.ProgressMessage = "Scanning lens for glasses...";

            // ---- collect glass surfaces --------------------------------------
            var lde = sys.LDE;
            var surfacesByGlass = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lde.NumberOfSurfaces; i++)
            {
                string mat = lde.GetSurfaceAt(i).Material;
                if (string.IsNullOrWhiteSpace(mat)) continue;
                if (mat.Trim().Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;
                if (!surfacesByGlass.TryGetValue(mat.Trim(), out var list))
                    surfacesByGlass[mat.Trim()] = list = new List<int>();
                list.Add(i);
            }
            if (surfacesByGlass.Count == 0)
            {
                Say("No glass surfaces found - nothing to do.");
                return;
            }
            Say(F("Found {0} distinct glass(es): {1}", surfacesByGlass.Count, string.Join(", ", surfacesByGlass.Keys)));

            // ---- BEFORE metrics ----------------------------------------------
            app.ProgressPercent = 10;
            app.ProgressMessage = "Measuring baseline performance...";
            var before = Snapshot(sys);

            // ---- harvest catalog data ----------------------------------------
            var catalogsInUse = sys.SystemData.MaterialCatalogs.GetCatalogsInUse();
            var lookupCatalogs = catalogsInUse
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!string.IsNullOrEmpty(Opts.TargetCatalog) &&
                !lookupCatalogs.Contains(Opts.TargetCatalog, StringComparer.OrdinalIgnoreCase))
                lookupCatalogs.Add(Opts.TargetCatalog);

            var glassData = new Dictionary<string, GlassInfo>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<GlassInfo>();

            var matTool = sys.Tools.OpenMaterialsCatalog();
            try
            {
                int catIdx = 0;
                foreach (string catName in lookupCatalogs)
                {
                    catIdx++;
                    app.ProgressMessage = "Reading catalog " + catName + "...";
                    app.ProgressPercent = 10 + (int)(40.0 * catIdx / lookupCatalogs.Count);

                    string[] names;
                    try
                    {
                        matTool.SelectedCatalog = catName;
                        names = matTool.GetAllMaterials();
                    }
                    catch
                    {
                        Say("WARNING: could not read catalog '" + catName + "' - skipped.");
                        continue;
                    }

                    bool isCandidateSource = string.IsNullOrEmpty(Opts.TargetCatalog)
                        ? true // obsolete-only mode draws candidates from all catalogs in use
                        : catName.Equals(Opts.TargetCatalog, StringComparison.OrdinalIgnoreCase);

                    foreach (string n in names)
                    {
                        if (app.TerminateRequested)
                        {
                            Say("Cancelled by user.");
                            app.ProgressMessage = "Done. Cancelled by user - no changes made.";
                            return;
                        }
                        matTool.SelectedMaterial = n;
                        var gi = new GlassInfo
                        {
                            Name = n,
                            Catalog = catName,
                            Nd = matTool.Nd,
                            Vd = matTool.Vd,
                            DPgF = matTool.dPgF,
                            Status = matTool.MaterialStatus.ToString(),
                            ExcludeSubstitution = matTool.ExcludeSubstitution
                        };
                        if (!glassData.ContainsKey(n)) glassData[n] = gi;
                        if (isCandidateSource) candidates.Add(gi);
                    }
                }
            }
            finally
            {
                matTool.Close();
            }

            // ---- pick replacements -------------------------------------------
            app.ProgressPercent = 55;
            app.ProgressMessage = "Matching equivalent glasses...";
            Say("");
            Say(F("Distance metric: sqrt( ({0:g}*dNd)^2 + ({1:g}*dVd)^2 + ({2:g}*dPgF)^2 )",
                Opts.WeightNd, Opts.WeightVd, Opts.WeightPgF));

            var replacements = new Dictionary<string, GlassInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in surfacesByGlass.OrderBy(k => k.Value[0]))
            {
                string current = kv.Key;
                Say("");
                Say(F("--- {0}  (surface{1} {2}) ---",
                    current, kv.Value.Count > 1 ? "s" : "", string.Join(", ", kv.Value)));

                if (!glassData.TryGetValue(current, out var cur))
                {
                    Say("    Not found in any catalog in use (model/custom glass?) - skipped.");
                    continue;
                }
                Say(F("    current: nd={0:F5}  vd={1:F2}  dPgF={2:+0.0000;-0.0000}  status={3}  [{4}]",
                    cur.Nd, cur.Vd, cur.DPgF, cur.Status, cur.Catalog));

                bool obsoleteOnlyMode = string.IsNullOrEmpty(Opts.TargetCatalog);
                if (obsoleteOnlyMode && cur.Status != "Obsolete")
                {
                    Say("    Status OK - left unchanged (pass -catalog NAME to convert all glasses).");
                    continue;
                }

                var ranked = candidates
                    .Where(c => !c.ExcludeSubstitution)
                    .Where(c => Opts.IncludeObsolete || c.Status != "Obsolete")
                    .Where(c => !c.Name.Equals(current, StringComparison.OrdinalIgnoreCase) || c.Status != "Obsolete")
                    .Select(c => new { G = c, D = Distance(cur, c) })
                    .OrderBy(x => x.D)
                    .Take(Math.Max(1, Opts.TopN))
                    .ToList();

                if (ranked.Count == 0)
                {
                    Say("    No candidates available - skipped.");
                    continue;
                }

                foreach (var r in ranked)
                {
                    Say(F("    {0}{1,-12} [{2}]  nd={3:F5}  vd={4:F2}  dPgF={5:+0.0000;-0.0000}  status={6,-9}  dist={7:F3}",
                        r == ranked[0] ? "-> " : "   ", r.G.Name, r.G.Catalog, r.G.Nd, r.G.Vd, r.G.DPgF, r.G.Status, r.D));
                }

                var best = ranked[0].G;
                if (best.Name.Equals(current, StringComparison.OrdinalIgnoreCase))
                    Say("    Best match is the current glass - no change needed.");
                else
                    replacements[current] = best;
            }

            // ---- apply --------------------------------------------------------
            Say("");
            if (replacements.Count == 0)
            {
                Say("No replacements to apply.");
                PrintMetrics("BEFORE (unchanged)", before, null, null, sys);
                string rp0 = WriteReportFile(app, sys);
                app.ProgressMessage = "Done. No replacements needed. Report: " + ShortName(rp0);
                OpenOutputs(app, rp0);
                return;
            }
            if (Opts.ReportOnly)
            {
                Say(F("Report-only mode: {0} replacement(s) suggested but NOT applied.", replacements.Count));
                PrintMetrics("BEFORE (unchanged)", before, null, null, sys);
                string rp1 = WriteReportFile(app, sys);
                app.ProgressMessage = F("Done. {0} replacement(s) suggested, none applied. Report: {1}",
                    replacements.Count, ShortName(rp1));
                OpenOutputs(app, rp1);
                return;
            }

            app.ProgressPercent = 65;
            app.ProgressMessage = "Applying replacements...";
            bool terminated = false;
            foreach (var kv in replacements)
            {
                if (app.TerminateRequested)
                {
                    Say("Terminated by user - remaining replacements skipped.");
                    terminated = true;
                    break;
                }
                foreach (int surfIdx in surfacesByGlass[kv.Key])
                {
                    try
                    {
                        lde.GetSurfaceAt(surfIdx).Material = kv.Value.Name;
                        Say(F("Applied: surface {0}  {1} -> {2}", surfIdx, kv.Key, kv.Value.Name));
                    }
                    catch (Exception ex)
                    {
                        Say(F("ERROR applying {0} on surface {1}: {2}", kv.Value.Name, surfIdx, ex.Message));
                    }
                }
            }

            // ---- AFTER metrics ------------------------------------------------
            app.ProgressPercent = 75;
            app.ProgressMessage = "Measuring performance after swap...";
            var after = Snapshot(sys);

            // ---- optional re-optimization ------------------------------------
            Dictionary<string, double[]> reopt = null;
            if (Opts.ReOptimize && !terminated && !app.TerminateRequested)
            {
                app.ProgressPercent = 80;
                app.ProgressMessage = "Re-optimizing existing variables...";
                var opt = sys.Tools.OpenLocalOptimization();
                try
                {
                    if (opt.Variables < 1)
                    {
                        Say("Re-optimization skipped: the system has no variables.");
                    }
                    else
                    {
                        opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares;
                        opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic;
                        double mf0 = opt.InitialMeritFunction;
                        opt.RunAndWaitForCompletion();
                        Say(F("Re-optimized {0} variable(s): merit function {1:G6} -> {2:G6}",
                            opt.Variables, mf0, opt.CurrentMeritFunction));
                    }
                }
                finally { opt.Close(); }
                reopt = Snapshot(sys);
            }

            // ---- results ------------------------------------------------------
            PrintMetrics("RESULTS", before, after, reopt, sys);

            if (Opts.SaveCopy)
            {
                string ext = string.IsNullOrEmpty(sys.SystemFile) ? ".zos" : Path.GetExtension(sys.SystemFile);
                string path = DerivePath(app, sys, "_EquivGlass" + ext);
                sys.SaveAs(path);
                Say("");
                Say("Saved modified system to: " + path);
            }

            string rp = WriteReportFile(app, sys);
            app.ProgressMessage = F("Done. Replaced {0} glass type(s){1}. Report: {2}",
                replacements.Count, terminated ? " (terminated early)" : "", ShortName(rp));
            OpenOutputs(app, rp);
        }

        static string ShortName(string path) => string.IsNullOrEmpty(path) ? "(not written)" : Path.GetFileName(path);

        // Plugin-mode (ribbon) runs lose their console the moment the process
        // exits, so the written report is the only surviving output - open it
        // with its default app unless -quiet.
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

        static double Distance(GlassInfo a, GlassInfo b)
        {
            double dn = Opts.WeightNd * (a.Nd - b.Nd);
            double dv = Opts.WeightVd * (a.Vd - b.Vd);
            double dp = Opts.WeightPgF * (a.DPgF - b.DPgF);
            return Math.Sqrt(dn * dn + dv * dv + dp * dp);
        }

        // EFFL, merit function value and polychromatic RMS spot radius (about the
        // centroid) for every field point, via single-shot operand evaluation.
        static Dictionary<string, double[]> Snapshot(ZOSAPI.IOpticalSystem sys)
        {
            var m = new Dictionary<string, double[]>();
            var mfe = sys.MFE;

            double effl = double.NaN, mf = double.NaN;
            try { effl = mfe.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 1, 0, 0, 0, 0, 0, 0); }
            catch { }
            try { if (mfe.NumberOfOperands > 0) mf = mfe.CalculateMeritFunction(); }
            catch { }
            m["EFFL"] = new[] { effl };
            m["MF"] = new[] { mf };

            var fields = sys.SystemData.Fields;
            int nf = fields.NumberOfFields;
            double maxR = 1e-10;
            for (int i = 1; i <= nf; i++)
            {
                var f = fields.GetField(i);
                maxR = Math.Max(maxR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }
            var rms = new double[nf];
            var fx = new double[nf];
            var fy = new double[nf];
            for (int i = 1; i <= nf; i++)
            {
                var f = fields.GetField(i);
                fx[i - 1] = f.X; fy[i - 1] = f.Y;
                double hx = f.X / maxR, hy = f.Y / maxR;
                try
                {
                    // RSCE: RMS spot radius (centroid ref.), 6 GQ rings, wave 0 = polychromatic
                    rms[i - 1] = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.RSCE,
                        6, 0, hx, hy, 0, 0, 0, 0);
                }
                catch { rms[i - 1] = double.NaN; }
            }
            m["RMS"] = rms;
            m["FX"] = fx;
            m["FY"] = fy;
            return m;
        }

        static void PrintMetrics(string title, Dictionary<string, double[]> before,
            Dictionary<string, double[]> after, Dictionary<string, double[]> reopt,
            ZOSAPI.IOpticalSystem sys)
        {
            string units = sys.SystemData.Units.LensUnits.ToString();
            Say("");
            Say("=== " + title + " ===");
            Say(F("EFFL ({0}):  before={1:F4}{2}{3}", units, before["EFFL"][0],
                after != null ? F("  after={0:F4}", after["EFFL"][0]) : "",
                reopt != null ? F("  reopt={0:F4}", reopt["EFFL"][0]) : ""));
            Say(F("Merit fn  :  before={0:G6}{1}{2}", before["MF"][0],
                after != null ? F("  after={0:G6}", after["MF"][0]) : "",
                reopt != null ? F("  reopt={0:G6}", reopt["MF"][0]) : ""));
            Say("");
            Say("RMS spot radius vs field (micrometers, polychromatic, centroid):");
            Say("  Field   (x, y)          before" + (after != null ? "      after" : "") + (reopt != null ? "      reopt" : ""));
            int nf = before["RMS"].Length;
            for (int i = 0; i < nf; i++)
            {
                string line = F("  {0,3}   ({1,5:F2},{2,6:F2})   {3,8:F3}", i + 1,
                    before["FX"][i], before["FY"][i], before["RMS"][i] * 1000.0);
                if (after != null) line += F("   {0,8:F3}", after["RMS"][i] * 1000.0);
                if (reopt != null) line += F("   {0,8:F3}", reopt["RMS"][i] * 1000.0);
                Say(line);
            }
        }

        static string DerivePath(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys, string suffix)
        {
            if (!string.IsNullOrEmpty(sys.SystemFile))
            {
                string dir = Path.GetDirectoryName(sys.SystemFile);
                string baseName = Path.GetFileNameWithoutExtension(sys.SystemFile);
                return Path.Combine(dir, baseName + suffix);
            }
            return Path.Combine(app.ZemaxDataDir, "Untitled" + suffix);
        }

        static string WriteReportFile(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys)
        {
            try
            {
                string path = DerivePath(app, sys, "_EquivGlassReport.txt");
                File.WriteAllLines(path, Report);
                Console.WriteLine();
                Console.WriteLine("Report written to: " + path);
                return path;
            }
            catch (Exception ex)
            {
                Console.WriteLine("WARNING: could not write report file: " + ex.Message);
                return null;
            }
        }
    }
}

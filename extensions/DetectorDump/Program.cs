using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace DetectorDump
{
    // Detector Dump — a ZOS-API User Extension.
    //
    // Exports the data of EVERY detector in a non-sequential system in one go:
    // native detector files (.DDR/.DDC/.DDP/.DDV), CSV pixel grids, false-colour
    // PNG heatmaps, and a summary table. Solves two recurring community asks:
    // saving data from many detectors is "tedious to manually save one by one",
    // and the detector viewer graphic cannot be saved to an image via the API
    // (threads "How to save detector viewer graphical plot into image file by
    // ZOS-API?" and the batch-detector-export discussions).
    //
    // Usage:
    //   (no args)      extension mode: export all detectors of the open system
    //   -file <zmx>    standalone mode: load the file first
    //   -dir <folder>  output folder (default: <lens>_detectors next to the file)
    //   -trace         run the NSC ray trace first (clears detectors; ray
    //                  splitting/scattering/polarization ON unless -nosplit /
    //                  -noscatter / -nopol given)
    //   -data N        pixel data code for CSV/PNG: 0 flux (default),
    //                  1 irradiance, 2 intensity
    //   -log           logarithmic heatmap scale (4 decades below peak) - useful
    //                  for high dynamic range data such as ghost/split paths
    //   -nocsv / -nopng / -nonative   switch off individual outputs
    class Options
    {
        public string FilePath = null;
        public string OutDir = null;
        public bool Trace = false;
        public bool Split = true, Scatter = true, Pol = true;
        public int DataCode = 0;
        public bool Csv = true, Png = true, Native = true;
        public bool Log = false;
    }

    class Program
    {
        static Options Opts = new Options();

        static void Main(string[] args)
        {
            ParseArgs(args);
            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
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

        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].TrimStart('-', '/').ToLowerInvariant())
                {
                    case "file": if (i + 1 < args.Length) Opts.FilePath = args[++i]; break;
                    case "dir": if (i + 1 < args.Length) Opts.OutDir = args[++i]; break;
                    case "trace": Opts.Trace = true; break;
                    case "nosplit": Opts.Split = false; break;
                    case "noscatter": Opts.Scatter = false; break;
                    case "nopol": Opts.Pol = false; break;
                    case "data": if (i + 1 < args.Length) Opts.DataCode = ParseInt(args[++i], Opts.DataCode); break;
                    case "log": Opts.Log = true; break;
                    case "nocsv": Opts.Csv = false; break;
                    case "nopng": Opts.Png = false; break;
                    case "nonative": Opts.Native = false; break;
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

        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        static void Run()
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

            try { Dump(app); }
            finally
            {
                if (standalone) app.CloseApplication();
                else { app.ProgressPercent = 100; app.ProgressMessage = "Detector dump complete."; }
            }
        }

        static void Dump(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            var nce = sys.NCE;
            if (nce.NumberOfObjects < 1)
                throw new Exception("the system has no non-sequential objects (NSC mode or an NSC group is required)");

            string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
            string outDir = Opts.OutDir;
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = string.IsNullOrEmpty(src)
                    ? Path.Combine(app.ZemaxDataDir, "DetectorDump")
                    : Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_detectors");
            }
            Directory.CreateDirectory(outDir);
            Console.WriteLine("System : " + (string.IsNullOrEmpty(src) ? "(untitled)" : src));
            Console.WriteLine("Output : " + outDir);

            // ---- optional ray trace -------------------------------------------
            if (Opts.Trace)
            {
                app.ProgressMessage = "Tracing rays...";
                var trace = sys.Tools.OpenNSCRayTrace();
                try
                {
                    try { trace.ClearDetectors(0); } catch { }
                    trace.SplitNSCRays = Opts.Split;
                    trace.ScatterNSCRays = Opts.Scatter;
                    trace.UsePolarization = Opts.Pol;
                    trace.IgnoreErrors = true;
                    trace.RunAndWaitForCompletion();
                }
                finally { trace.Close(); }
                Console.WriteLine(F("Trace  : done (split={0}, scatter={1}, polarization={2})",
                    Opts.Split, Opts.Scatter, Opts.Pol));
            }

            // ---- enumerate detectors ------------------------------------------
            var summary = new List<string>
            {
                "obj  type                    pixels      total flux      peak value        hit px  comment",
                "---  ----------------------  ----------  --------------  ----------------  ------  -------"
            };
            int found = 0, exported = 0;

            for (int i = 1; i <= nce.NumberOfObjects; i++)
            {
                uint ur, uc;
                if (!nce.GetDetectorDimensions(i, out ur, out uc)) continue;
                int rows = (int)ur, cols = (int)uc;
                if (rows <= 0 || cols <= 0) continue;
                found++;

                var row = nce.GetObjectAt(i);
                string typeName = row.TypeName ?? "Detector";
                string comment = (row.Comment ?? "").Trim();
                string baseName = F("obj{0:D2}_{1}", i, Sanitize(comment.Length > 0 ? comment : typeName));

                double totalFlux;
                nce.GetDetectorData(i, 0, 0, out totalFlux);

                double[,] grid = null;
                try { grid = nce.GetAllDetectorDataSafe(i, Opts.DataCode); } catch { }
                bool isPolar = typeName.IndexOf("Polar", StringComparison.OrdinalIgnoreCase) >= 0;
                if ((grid == null || grid.Length == 0) && isPolar)
                {
                    try { grid = nce.GetAllPolarDetectorDataSafe(i, ZOSAPI.Editors.NCE.PolarDetectorDataType.Power); }
                    catch { }
                }

                double peak = 0; long hits = 0;
                if (grid != null)
                {
                    foreach (double v in grid) { if (v > peak) peak = v; if (v != 0) hits++; }
                }

                summary.Add(F("{0,3}  {1,-22}  {2,4} x {3,-4}  {4,14:G6}  {5,16:G6}  {6,6}  {7}",
                    i, typeName, rows, cols, totalFlux, peak, hits, comment));

                if (Opts.Native)
                {
                    string ext = typeName.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0 ? ".DDC"
                        : isPolar ? ".DDP"
                        : typeName.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 ? ".DDV"
                        : ".DDR";
                    try
                    {
                        if (nce.SaveDetector(i, Path.Combine(outDir, baseName + ext)))
                            Console.WriteLine(F("  obj {0}: saved native {1}", i, baseName + ext));
                        else
                            Console.WriteLine(F("  obj {0}: native save not supported for this detector", i));
                    }
                    catch (Exception ex) { Console.WriteLine(F("  obj {0}: native save failed: {1}", i, ex.Message)); }
                }

                if (grid == null || grid.Length == 0)
                {
                    Console.WriteLine(F("  obj {0}: no pixel grid available (CSV/PNG skipped)", i));
                    continue;
                }

                if (Opts.Csv)
                {
                    string csvPath = Path.Combine(outDir, baseName + ".csv");
                    WriteCsv(grid, csvPath);
                    Console.WriteLine(F("  obj {0}: wrote {1} ({2}x{3} pixels)", i, Path.GetFileName(csvPath),
                        grid.GetLength(0), grid.GetLength(1)));
                }
                if (Opts.Png)
                {
                    string pngPath = Path.Combine(outDir, baseName + ".png");
                    WriteHeatmap(grid, pngPath, F("obj {0}  {1}  {2}   total={3:G5}  peak={4:G5}",
                        i, typeName, comment, totalFlux, peak));
                    Console.WriteLine(F("  obj {0}: wrote {1}", i, Path.GetFileName(pngPath)));
                }
                exported++;
            }

            summary.Add("");
            summary.Add(F("{0} detector(s) found, {1} with pixel data exported. Data code {2} ({3}).",
                found, exported, Opts.DataCode,
                Opts.DataCode == 0 ? "flux" : Opts.DataCode == 1 ? "irradiance" : "intensity"));
            File.WriteAllLines(Path.Combine(outDir, "detectors_summary.txt"), summary);

            Console.WriteLine();
            foreach (var line in summary) Console.WriteLine(line);
            if (found == 0)
                Console.WriteLine("NOTE: no detectors with data found. Did you run a trace? (pass -trace)");
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            string r = sb.ToString().Trim('_');
            return r.Length > 40 ? r.Substring(0, 40) : (r.Length == 0 ? "detector" : r);
        }

        static void WriteCsv(double[,] grid, string path)
        {
            int nr = grid.GetLength(0), nc = grid.GetLength(1);
            var sb = new StringBuilder(nr * nc * 12);
            for (int r = 0; r < nr; r++)
            {
                for (int c = 0; c < nc; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(grid[r, c].ToString("G9", CultureInfo.InvariantCulture));
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        // classic false-colour lookup: black -> blue -> cyan -> green -> yellow -> red -> white
        static Color Lut(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double[] ks = { 0, 0.166, 0.333, 0.5, 0.666, 0.833, 1.0 };
            int[,] cs = { { 0, 0, 0 }, { 0, 0, 255 }, { 0, 255, 255 }, { 0, 255, 0 }, { 255, 255, 0 }, { 255, 0, 0 }, { 255, 255, 255 } };
            for (int k = 0; k < 6; k++)
            {
                if (t <= ks[k + 1])
                {
                    double f = (t - ks[k]) / (ks[k + 1] - ks[k]);
                    return Color.FromArgb(
                        (int)(cs[k, 0] + f * (cs[k + 1, 0] - cs[k, 0])),
                        (int)(cs[k, 1] + f * (cs[k + 1, 1] - cs[k, 1])),
                        (int)(cs[k, 2] + f * (cs[k + 1, 2] - cs[k, 2])));
                }
            }
            return Color.White;
        }

        static void WriteHeatmap(double[,] grid, string path, string caption)
        {
            int nr = grid.GetLength(0), nc = grid.GetLength(1);
            double peak = 0;
            foreach (double v in grid) if (v > peak) peak = v;
            if (peak <= 0) peak = 1;

            int cell = Math.Max(1, 512 / Math.Max(nr, nc));
            int W = nc * cell, H = nr * cell, cap = 24;
            double floor = peak * 1e-4; // 4 decades for the log display
            using (var bmp = new Bitmap(W, H + cap))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                for (int r = 0; r < nr; r++)
                    for (int c = 0; c < nc; c++)
                    {
                        double v = grid[r, c];
                        double t = Opts.Log
                            ? (v <= floor ? 0 : Math.Log10(v / floor) / 4.0)
                            : v / peak;
                        using (var b = new SolidBrush(Lut(t)))
                            g.FillRectangle(b, c * cell, (nr - 1 - r) * cell, cell, cell);
                    }
                using (var font = new Font("Segoe UI", 9f))
                using (var white = new SolidBrush(Color.White))
                    g.DrawString(caption, font, white, 2, H + 4);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}

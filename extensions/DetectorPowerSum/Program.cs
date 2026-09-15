using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DetectorPowerSum
{
    // Detector Power Sum - a ZOS-API User Extension.
    //
    // Sums the total power accumulated on every Detector Rectangle in a
    // non-sequential system and reports the per-detector values and the grand
    // total. The value is GetDetectorData(obj, 0, 0) - the same "sum of all
    // pixels" flux the NSDD operand and the Detector Viewer's Total Power
    // readout use - so it is radiometric (watts) or photometric (lumens)
    // according to the system's source-units setting, which the report states.
    //
    // Usage:
    //   (no args)      extension mode: sum the detectors of the open system
    //   -file <zmx>    standalone mode: load the file first
    //   -trace         run the NSC ray trace first (clears detectors; ray
    //                  splitting/scattering/polarization ON unless -nosplit /
    //                  -noscatter / -nopol given)
    //   -all           include every detector object (color/polar/volume/
    //                  surface...), not just Detector Rectangles; non-rectangle
    //                  detectors are listed and sub-totalled separately
    //   -out <path>    also write the report to this text file
    //   -quiet         do not auto-open the report after a ribbon (GUI) run
    class Options
    {
        public string FilePath = null;
        public string OutPath = null;
        public bool Trace = false;
        public bool Split = true, Scatter = true, Pol = true;
        public bool All = false;
        public bool Quiet = false;
    }

    class Program
    {
        static Options Opts = new Options();

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
                    case "out": if (i + 1 < args.Length) Opts.OutPath = args[++i]; break;
                    case "trace": Opts.Trace = true; break;
                    case "nosplit": Opts.Split = false; break;
                    case "noscatter": Opts.Scatter = false; break;
                    case "nopol": Opts.Pol = false; break;
                    case "all": Opts.All = true; break;
                    case "quiet": Opts.Quiet = true; break;
                }
            }
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
                string connectError;
                if (!ZemaxLocator.TryConnect(out app, out connectError, false))
                    throw new Exception(connectError);
            }

            try { SumDetectors(app); }
            finally
            {
                if (standalone) app.CloseApplication();
            }
        }

        static void SumDetectors(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            var nce = sys.NCE;
            if (nce.NumberOfObjects < 1)
                throw new Exception("the system has no non-sequential objects (NSC mode or an NSC group is required)");

            string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;

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
            }

            // ---- units: radiometric vs photometric ----------------------------
            var srcUnits = sys.SystemData.Units.SourceUnits;
            string unit, kind;
            switch (srcUnits)
            {
                case ZOSAPI.SystemData.ZemaxSourceUnits.Lumens: unit = "lm"; kind = "photometric"; break;
                case ZOSAPI.SystemData.ZemaxSourceUnits.Joules: unit = "J"; kind = "radiant energy"; break;
                default: unit = "W"; kind = "radiometric"; break;
            }

            // ---- enumerate detectors ------------------------------------------
            var report = new List<string>
            {
                "Detector Power Sum",
                "System : " + (string.IsNullOrEmpty(src) ? "(untitled)" : src),
                F("Units  : {0} ({1}, from the system source-units setting)", unit, kind)
            };
            if (Opts.Trace)
                report.Add(F("Trace  : run before reading (split={0}, scatter={1}, polarization={2})",
                    Opts.Split, Opts.Scatter, Opts.Pol));
            report.Add("");
            report.Add("obj  type                    pixels      hits        power           comment");
            report.Add("---  ----------------------  ----------  ----------  --------------  -------");

            double rectSum = 0, otherSum = 0;
            int rectCount = 0, otherCount = 0;
            bool terminated = false;

            for (int i = 1; i <= nce.NumberOfObjects; i++)
            {
                if (app.TerminateRequested)
                {
                    terminated = true;
                    report.Add("(terminated by user - the sums below are partial)");
                    break;
                }
                app.ProgressPercent = 10 + 85.0 * i / nce.NumberOfObjects;

                var row = nce.GetObjectAt(i);
                bool isRect = row.Type == ZOSAPI.Editors.NCE.ObjectType.DetectorRectangle;

                uint ur = 0, uc = 0;
                bool isDetector = nce.GetDetectorDimensions(i, out ur, out uc) && ur > 0 && uc > 0;
                if (!isRect && !(Opts.All && isDetector)) continue;

                double power = 0, hits = 0;
                bool ok = nce.GetDetectorData(i, 0, 0, out power);   // pixel 0, data 0: total flux
                nce.GetDetectorData(i, -3, 0, out hits);             // pixel -3: total hits

                string typeName = row.TypeName ?? "Detector";
                string comment = (row.Comment ?? "").Trim();
                report.Add(F("{0,3}  {1,-22}  {2,4} x {3,-4}  {4,10:G6}  {5,14}  {6}{7}",
                    i, typeName, ur, uc, hits, ok ? F("{0:G8}", power) : "n/a", comment,
                    isRect ? "" : "  [non-rectangle]"));
                if (!ok)
                {
                    power = 0;
                    report.Add(F("     (obj {0}: this detector type does not report total flux via GetDetectorData - excluded from the sum)", i));
                }

                if (isRect) { rectSum += power; rectCount++; }
                else if (ok) { otherSum += power; otherCount++; }
            }

            report.Add("");
            if (rectCount == 0 && otherCount == 0 && !terminated)
            {
                report.Add(Opts.All
                    ? "No detector objects found in the NCE."
                    : "No Detector Rectangle objects found in the NCE (pass -all to include other detector types).");
            }
            else
            {
                report.Add(F("TOTAL POWER, {0} Detector Rectangle(s) : {1:G8} {2} ({3})",
                    rectCount, rectSum, unit, kind));
                if (Opts.All && otherCount > 0)
                {
                    report.Add(F("Sub-total, {0} other detector(s)       : {1:G8} {2}", otherCount, otherSum, unit));
                    report.Add(F("TOTAL POWER, all {0} detector(s)       : {1:G8} {2}", rectCount + otherCount, rectSum + otherSum, unit));
                }
                if (!Opts.Trace)
                    report.Add("(detector data as accumulated in the file/session - pass -trace to re-trace first)");
            }

            foreach (var line in report) Console.WriteLine(line);

            // ---- file output ---------------------------------------------------
            // Ribbon (plugin) runs lose their console when the process exits, so
            // a written report is the only surviving record - write one next to
            // the lens file by default in that mode and open it unless -quiet.
            string outPath = Opts.OutPath;
            bool plugin = false;
            try { plugin = app.Mode == ZOSAPI.ZOSAPI_Mode.Plugin; } catch { }
            if (string.IsNullOrEmpty(outPath) && plugin)
            {
                outPath = string.IsNullOrEmpty(src)
                    ? Path.Combine(app.ZemaxDataDir, "DetectorPowerSum.txt")
                    : Path.Combine(Path.GetDirectoryName(src),
                        Path.GetFileNameWithoutExtension(src) + "_power_sum.txt");
            }
            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllLines(outPath, report);
                Console.WriteLine("Report : " + outPath);
                if (plugin && !Opts.Quiet)
                {
                    try { System.Diagnostics.Process.Start(outPath); }
                    catch (Exception ex) { Console.WriteLine("WARNING: could not open " + outPath + ": " + ex.Message); }
                }
            }

            app.ProgressPercent = 100;
            app.ProgressMessage = terminated
                ? "Terminated - partial sums reported."
                : rectCount + otherCount == 0
                    ? "No detectors found."
                    : F("Total power: {0:G8} {1} over {2} detector(s)", rectSum + otherSum, unit, rectCount + otherCount);
        }
    }
}

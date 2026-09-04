using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FootprintDxf
{
    // FootprintDxf - ZOS-API User Extension.
    //
    // Exports the envelope of beam footprints on sequential surfaces to a CAD
    // DXF (R12 / AC1009 ASCII). Harvey.Spencer's forum ask:
    // https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991
    //
    // LayoutRender only writes a 2D layout PNG. DetectorDump only dumps NSC
    // detectors. Neither is a per-surface footprint envelope for mech CAD.
    // There is no ZOS-API DXF export; this writes the DXF as text, plus an
    // optional PNG preview beside it (same polylines; skip with -nopng).
    //
    // For each selected surface: batch-trace a pupil grid plus a dense pupil-rim
    // sample set of real rays for the chosen fields/wavelengths, collect local
    // (x,y) intercepts that hit (ignore vignetted/missed), compute the 2D
    // convex hull (Andrew's monotone chain), and write one closed
    // POLYLINE+VERTEX+SEQEND per surface layer. With a dense rim, circular /
    // elliptical footprints keep many hull verts instead of a chunky polygon
    // from the grid alone. Coordinates default to local surface XY in OpticStudio
    // lens units; optional -global maps hits through LDE.GetGlobalMatrix so all
    // layers share one assembly frame (2D DXF uses global X/Y; Z ignored).
    // $INSUNITS is mapped from SystemData.Units.LensUnits (mm/cm/in/m). Layers
    // are always SURF_{n} or SURF_{n}_{comment}. Optional -perfield adds
    // SURF_{n}_F{f} hulls (union SURF layer still written). Optional -aperture
    // adds APER_SURF_{n} clear-aperture overlays. The optical system is never
    // modified.
    //
    // Dense rim@1 is traced once and merged with grid+r=0.99 for the hull.
    // Optional -rim reuses those rim@1 hits for per-field RIM_SURF_{n}_F{f}
    // layers (no second TraceHits; fields are never atan2-merged into one ring).
    //
    // Usage:
    //   (no args)              ribbon / plugin: settings dialog, then export
    //   -out <path.dxf>        output path (default: <lens>_footprints.dxf)
    //   -file <zmx>            standalone: load file, no dialog
    //   -rays N                pupil grid density (odd, default 21)
    //   -rimrays N             dense rim sample count (default max(128, Rays*8);
    //                          clamp 16..1024). Always merged into the main hull.
    //   -surfaces all|1,3,5|1-6   surfaces (default all = 1..image-1)
    //   -includeimage          also include the image surface when -surfaces all
    //   -fields all|1,2        fields (default all)
    //   -wave primary|all      wavelengths (default all)
    //   -rim                   also write per-field pupil-rim polylines
    //                          (RIM_SURF_N_Ff); reuses rim@1 (no second trace)
    //   -perfield              also write per-field convex-hull layers
    //                          SURF_N_Ff (union SURF_N still written)
    //   -global                transform local (x,y) hits via GetGlobalMatrix
    //                          into a common frame (2D global X/Y; Z ignored)
    //   -aperture              also write clear-aperture overlays APER_SURF_N
    //   -nopng                 skip writing the PNG preview beside the DXF
    //   -quiet                 do not auto-open DXF/PNG after a ribbon run
    //   -nodialog              skip settings dialog in plugin mode
    //   -selftest              run convex-hull + ring-order + layer/units +
    //                          transform/ellipse self-check and exit (no OpticStudio)

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

        static int ParseInt(string s, int keep)
        {
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CI, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid integer - keeping " + keep + ".");
            return keep;
        }

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

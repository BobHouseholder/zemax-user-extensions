using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RayExtentEnvelope
{
    // ============================================================
    // RayExtentEnvelope - what this program does (plain words)
    // ============================================================
    // How wide do the rays get as they travel through the lens?
    // We shoot rim rays at the extreme fields, measure the farthest
    // radius at each station, and draw a keep-out envelope. You get
    // a Y-Z PNG outline and a STEP solid (KEEP_OUT tube, plus optional
    // MEMA lens solids). The lens file is never saved or changed.
    // Handy for mech CAD keep-out volumes around the beam.
    // Run with -file / -out, or from User Extensions.
    // ============================================================

    // Switches from the command line (Phase 1 has no settings window).
    class Options
    {
        public string FilePath;
        public string OutPath;
        public bool WantPng = true;
        public bool WantStep = true;
        public bool ExplicitOutputs;
        public int RimRays = 48;
        public string Surfaces = "auto";
        public int Width = 1400;
        public int Height = 900;
        public bool Quiet;
        public bool NoDialog;
        // Default: Rmax = max(ray hit radius, field height) — do not floor with clear aperture.
        public bool NoClap = true;
        // When true, station Z is the surface vertex; default uses the rim hit Z.
        public bool UseVertexZ;
        // When true, STEP is only the KEEP_OUT tube (no MEMA L1/L2/... lens solids).
        public bool EnvelopeOnly;
    }

    partial class Program
    {
        static Options Opts = new Options();
        static ZOSAPI.IZOSAPI_Application App;
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Start here: find OpticStudio, then build the envelope.
        static void Main(string[] args)
        {
            try { ParseArgs(args); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
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

        // Read the flags you typed (-out, -rimrays, -envelopeOnly, ...).
        static void ParseArgs(string[] args)
        {
            bool sawPng = false, sawStep = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string al = a.ToLowerInvariant();
                string next() => (i + 1 < args.Length) ? args[++i] : null;
                switch (al)
                {
                    case "-file": Opts.FilePath = next(); break;
                    case "-out": Opts.OutPath = next(); break;
                    case "-png": sawPng = true; break;
                    case "-step": sawStep = true; break;
                    case "-rimrays": Opts.RimRays = ParseInt(next(), Opts.RimRays); break;
                    case "-surfaces": Opts.Surfaces = next() ?? "auto"; break;
                    case "-width": Opts.Width = ParseInt(next(), Opts.Width); break;
                    case "-height": Opts.Height = ParseInt(next(), Opts.Height); break;
                    case "-quiet": Opts.Quiet = true; break;
                    case "-nodialog": Opts.NoDialog = true; break;
                    case "-clap": Opts.NoClap = false; break;
                    case "-noclap":
                    case "-rayextent": Opts.NoClap = true; break;
                    case "-vertexz": Opts.UseVertexZ = true; break;
                    case "-envelopeonly":
                    case "-nolenses": Opts.EnvelopeOnly = true; break;
                    case "-lenses": Opts.EnvelopeOnly = false; break;
                    default:
                        if (al.StartsWith("-z")) break;
                        if (al.StartsWith("-"))
                            throw new Exception("unknown flag " + a);
                        break;
                }
            }
            if (sawPng || sawStep)
            {
                Opts.ExplicitOutputs = true;
                Opts.WantPng = sawPng;
                Opts.WantStep = sawStep;
            }
            if (Opts.RimRays < 16) Opts.RimRays = 16;
            if (Opts.RimRays > 256) Opts.RimRays = 256;
            if (Opts.Width < 200) Opts.Width = 200;
            if (Opts.Height < 200) Opts.Height = 200;
        }

        // Parse an integer, or keep the old value if the text is bad.
        static int ParseInt(string s, int keep)
        {
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CI, out v)) return v;
            Console.WriteLine("WARNING: '" + s + "' is not a valid integer - keeping " + keep + ".");
            return keep;
        }

        // Connect to OpticStudio and call Export.
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
                Export(App);
            }
            finally
            {
                if (standalone) App.CloseApplication();
                else
                {
                    App.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(App.ProgressMessage) || !App.ProgressMessage.StartsWith("Done"))
                        App.ProgressMessage = "RayExtentEnvelope finished.";
                }
            }
        }

        // User hit Cancel — stop cleanly.
        static bool Cancelled()
        {
            try { return App != null && App.TerminateRequested; }
            catch { return false; }
        }

        // Print a status line.
        static void Say(string s) => Console.WriteLine(s);

        // Open the PNG/STEP in the default apps after a ribbon run (unless -quiet).
        static void OpenOutputs(params string[] paths)
        {
            if (Opts.Quiet) return;
            try { if (App.Mode != ZOSAPI.ZOSAPI_Mode.Plugin) return; } catch { return; }
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                try { System.Diagnostics.Process.Start(p); }
                catch (Exception ex) { Console.WriteLine("WARNING: could not open " + p + ": " + ex.Message); }
            }
        }

        static string F(string fmt, params object[] a) => string.Format(CI, fmt, a);
    }
}

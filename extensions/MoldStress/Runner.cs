using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MoldStress
{
    /// <summary>
    /// The whole chain, end to end: geometry to gate to fields to STAR to a
    /// performance delta.
    ///
    /// The baseline is measured BEFORE anything is imported, and the loaded
    /// result after - so the reported change is a difference between two
    /// measurements of the same system, not between a measurement and a memory.
    /// </summary>
    internal static class Runner
    {
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int Run(string[] args)
        {
            Session.Locate();
            return RunConnected(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunConnected(string[] args)
        {
            string file = Program.Value(args, "-file");
            var app = Session.Connect(file);
            var log = new StringBuilder();
            Action<string> say = s => { Console.WriteLine(s); log.AppendLine(s); };

            try
            {
                var sys = app.PrimarySystem;
                var proc = new Process
                {
                    FillTimeS = Program.Value(args, "-filltime", 0.6),
                    PackPressureMPa = Program.Value(args, "-packpressure", 60.0),
                    PackTimeS = Program.Value(args, "-packtime", 3.0),
                    MeltTempC = Program.Value(args, "-melttemp", double.NaN),
                    MoldTempC = Program.Value(args, "-moldtemp", double.NaN),
                };
                string outDir = Program.Value(args, "-outdir")
                    ?? Path.Combine(Path.GetDirectoryName(
                        string.IsNullOrEmpty(sys.SystemFile)
                            ? Path.Combine(Path.GetTempPath(), "x.zmx") : sys.SystemFile),
                        "moldstress");

                var extra = (Program.Value(args, "-materials") ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var els = Session.FindElements(sys, extra);
                Gating.ApplyOverrides(els, Program.Value(args, "-gateconfig"));

                say("MoldStress");
                say("  " + Program.ScopeLabel);
                say("  system: " + (string.IsNullOrEmpty(sys.SystemFile) ? "(unsaved)" : sys.SystemFile));
                say(string.Format(CultureInfo.InvariantCulture,
                    "  process: fill {0:F2} s, pack {1:F1} MPa for {2:F1} s",
                    proc.FillTimeS, proc.PackPressureMPa, proc.PackTimeS));
                say("");
                if (els.Count == 0) { say("  no mouldable element found."); return 0; }

                // --- baseline, measured before anything is loaded --------------
                double baseWfe = Metric(sys);
                say(string.Format(CultureInfo.InvariantCulture,
                    "  baseline RMS wavefront error: {0:F6} waves", baseWfe));
                say("");

                var written = new List<StarFiles.Written>();
                foreach (var e in els)
                {
                    var p = Polymers.ByName(e.Material);
                    var fill = FillField.Build(e, p, proc, 101);
                    var freeze = FreezeHistory.Build(Math.Max(e.EdgeThicknessMm, 0.2), p, proc, 41);
                    var ch = Channels.Build(e, p, proc, fill, freeze);
                    var w = StarFiles.Write(e, p, ch, fill, freeze, outDir);
                    written.Add(w);

                    Session.Describe(e);
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      fill      gate pressure {0:F2} MPa, viscosity {1:E2} Pa.s, {2:F0} mm3/s",
                        fill.P[0], fill.EtaPaS, fill.FlowRateMm3PerS));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      freeze    core at {0:F2} s, skin {1:F1} C at freeze-off",
                        freeze.CentreFreezeTimeS, freeze.TrefC[0]));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      channels  flow dn {0:E3} peaking at {1:P0} of the half-wall, " +
                        "density dn {2:E3}",
                        ch.PeakDnFlow, ch.PeakDepthFraction, w.PeakDnDensity));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      files     {0} points, peak equivalent stress {1:F1} N/mm2",
                        w.Points, w.PeakEquivalentStressMPa));

                    // --- load it -----------------------------------------------
                    var surf = sys.LDE.GetSurfaceAt(e.FrontSurface);
                    var st = surf.STARData.Stress;
                    try { st.FEAData.UnloadData(); } catch { }
                    st.SetDataIsLocal();
                    st.SetWorkingWavelength(1);
                    // ImportStress(string) is obsolete and, per the API's own
                    // attribute, "no longer implemented" - it returns nothing and
                    // does nothing. Exactly the silent no-op this project keeps
                    // running into. ImportStress_1 returns a status code.
                    int importCode = st.FEAData.ImportStress_1(w.StressPath);
                    int read = st.FEAData.NumberOfDataPoints;
                    st.Fits.Refit();
                    st.Fits.ApplyStress();

                    var di = surf.STARData.DirectIndex;
                    int readIndex = 0;
                    try
                    {
                        // OFF by default, and the A/B above is why: it costs the
                        // retardance map entirely. The density term is already in
                        // the stress tensor as a hydrostatic component, so this
                        // opt-in exists only for an index-only study.
                        if (!Program.Has(args, "-directindex"))
                            throw new Exception("not loaded - density rides in the stress tensor " +
                                                "(pass -directindex to load it instead, which " +
                                                "disables stress birefringence on this surface)");
                        di.SetDataIsLocal();
                        di.FEAData.ImportDirectIndex_1(w.IndexPath);
                        readIndex = di.FEAData.NumberOfDataPoints;
                        di.Fits.Refit();
                    }
                    catch (Exception ex) { say("      index     " + ex.Message); }

                    say(string.Format(CultureInfo.InvariantCulture,
                        "      STAR      stress {0}/{1} points accepted (code {2}), index {3}",
                        read, w.Points, importCode, readIndex));
                    if (read == 0)
                        say("      STAR      REFUSED THE STRESS DATA - does " + e.Material +
                            " carry a BD record? Run -writecatalog and add MOLDSTRESS to the system.");

                    int samples; string note;
                    double peakRet = PeakRetardance(st, e, out samples, out note);
                    if (note != null) say("      retardance UNAVAILABLE: " + note);
                    else
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      retardance peak {0:F4} rad = {1:F1} nm, over {2} map points",
                            peakRet, Math.Abs(peakRet) / (2 * Math.PI) *
                            sys.SystemData.Wavelengths.GetWavelength(1).Wavelength * 1000.0,
                            samples));

                    // The tool's own silent-zero guard. A stress field that
                    // carries real equivalent stress cannot produce exactly zero
                    // retardance; if it does, something upstream is not
                    // functioning and a zero must not be reported as a result.
                    if (note == null && peakRet == 0.0 && w.PeakEquivalentStressMPa > 0)
                        say("      REFUSING that zero: " +
                            string.Format(CultureInfo.InvariantCulture,
                            "{0:F1} N/mm2 of equivalent stress cannot give exactly zero " +
                            "retardance. Check the material carries K11/K12.",
                            w.PeakEquivalentStressMPa));
                    say("");
                }

                double loadedWfe = Metric(sys);
                say(string.Format(CultureInfo.InvariantCulture,
                    "  with moulding effects:       {0:F6} waves", loadedWfe));
                say(string.Format(CultureInfo.InvariantCulture,
                    "  change:                      {0:+0.000000;-0.000000} waves ({1:+0.0;-0.0}%)",
                    loadedWfe - baseWfe,
                    baseWfe > 0 ? 100.0 * (loadedWfe - baseWfe) / baseWfe : 0.0));
                say("");
                say("  Files are in " + outDir);
                say("  In OpticStudio, any analysis window's STAR Effects setting switches");
                say("  between On and Difference, which shows this change directly.");

                string report = Path.Combine(outDir, "moldstress_report.txt");
                Directory.CreateDirectory(outDir);
                File.WriteAllText(report, log.ToString());
                Console.WriteLine("  report: " + report);
                return 0;
            }
            finally
            {
                if (!string.IsNullOrEmpty(file)) { try { app.CloseApplication(); } catch { } }
            }
        }

        /// <summary>
        /// RMS wavefront error on axis. Chosen because it is a merit operand and
        /// so evaluates headlessly - the analysis windows do not, on this API.
        /// </summary>
        private static double Metric(ZOSAPI.IOpticalSystem sys)
        {
            try
            {
                return sys.MFE.GetOperandValue(
                    ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, 1, 0, 0, 0, 0, 0, 0);
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// Peak retardance from STAR's OWN map, with the sample count reported.
        ///
        /// The first version of this walked a grid of points calling
        /// GetRetardance and swallowed every exception, so when the samples all
        /// failed it returned a confident 0.0000 rad on a field STAR had accepted
        /// and fitted - a peak of 4.85 rad was sitting there the whole time. A
        /// silent zero from a stress-free-looking answer is the exact failure
        /// this tool exists to refuse, and it reached the tool's own output.
        /// Nothing is caught silently here now.
        /// </summary>
        private static double PeakRetardance(ZOSAPI.Editors.LDE.ISTAR_Stress st,
                                             MouldedElement e, out int samples, out string note)
        {
            note = null;
            samples = 0;
            double peak = 0.0;
            try
            {
                // density is a sampling SELECTOR, not a point count: 8 returns a
                // 217-point map, 16 returns nothing at all rather than refusing.
                var map = st.Fits.GetRetardanceMap(8, 0, 1, 1.0, 0.0, 0.0, 0.0);
                if (map != null)
                {
                    samples = map.Length;
                    foreach (var pt in map)
                        if (Math.Abs(pt.Retardance) > Math.Abs(peak)) peak = pt.Retardance;
                }
            }
            catch (Exception ex)
            {
                note = "GetRetardanceMap raised: " + ex.Message;
                return double.NaN;
            }
            if (samples == 0) note = "GetRetardanceMap returned no points";
            return peak;
        }
    }
}

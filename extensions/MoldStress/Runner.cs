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
        /// <summary>
        /// Every flag -run READS. Public so the self-test can hold both arms
        /// of the guard against it without an OpticStudio session.
        ///
        /// KEEP THIS IN STEP WITH THE READS BELOW, IN BOTH DIRECTIONS. A flag
        /// missing from the list makes a legitimate run fail loudly, which is
        /// annoying and self-correcting. A flag listed here but never read is the
        /// original defect wearing the guard's uniform.
        /// </summary>
        internal static readonly string[] ReadsFlags =
        {
            "-allow-nonspherical", "-directindex", "-file", "-filltime", "-gateconfig",
            "-materials", "-melttemp", "-moldtemp", "-nz", "-nzexport", "-outdir",
            "-packpressure", "-packtime", "-prepare", "-ribbon",
        };

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int Run(string[] args)
        {
            // BEFORE Session.Locate(), deliberately. Refusing after starting an
            // OpticStudio session costs the user the startup and tells them
            // nothing they could not have been told immediately.
            int badForMode = Program.RejectFlagsNotReadBy(args, ReadsFlags, "-run");
            if (badForMode != 0) return badForMode;

            // Locate() moved INSIDE the ribbon guard below on 2026-08-22: it
            // runs before any ZOSAPI type is touched and can throw, and a throw
            // here previously escaped the wrapper to an invisible stderr.
            if (!Program.Has(args, "-ribbon")) { Session.Locate(); return RunConnected(args); }

            // A RIBBON RUN HAS NO CONSOLE, so an exception that reaches Main's
            // catch prints to a stderr nobody can see and the click appears to do
            // nothing - which is exactly what the first real ribbon click did on
            // 2026-08-22. Every failure a ribbon run can produce must end in an
            // OPENED report, including the ones thrown before an output directory
            // is known; those go to %TEMP%\moldstress.
            try { Session.Locate(); return RunConnected(args); }
            catch (Exception ex)
            {
                string dir = Path.Combine(Path.GetTempPath(), "moldstress");
                string rep = Path.Combine(dir, "moldstress_report.txt");
                try
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(rep,
                        "MoldStress could not run:\r\n\r\n  " + ex.Message + "\r\n\r\n" +
                        "If OpticStudio was not waiting for an extension, start this from\r\n" +
                        "the Programming ribbon. If the message names the licence, note that\r\n" +
                        "STAR (and so this tool) requires an Enterprise-level licence.\r\n");
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(rep) { UseShellExecute = true });
                }
                catch { }
                return 1;
            }
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

                // EVERY return below goes through this, so a refusal is exactly
                // as visible as a success. Before 2026-08-22 the report was
                // written only on the one path that reached the end, and the
                // first real ribbon click took a path that did not.
                Func<int, int> finish = code =>
                {
                    try
                    {
                        Directory.CreateDirectory(outDir);
                        string rep = Path.Combine(outDir, "moldstress_report.txt");
                        File.WriteAllText(rep, log.ToString());
                        Console.WriteLine("  report: " + rep);
                        if (Program.Has(args, "-ribbon") && !Program.Has(args, "-quiet"))
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(rep) { UseShellExecute = true });
                    }
                    catch { }
                    return code;
                };

                var extra = (Program.Value(args, "-materials") ?? "")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var els = Session.FindElements(sys, extra);

                // AUTOMATIC PREPARATION, in ribbon mode or under -prepare. If
                // the system carries ordinary polymer names this tool recognises
                // - PMMA, POLYCARB, 480R... - it is saved as a -MoldStress
                // sibling, the MOLDSTRESS catalogue is written and attached, and
                // the materials are replaced IN THE COPY. The original file is
                // never modified. The pre-substitution metric is measured first,
                // because MS_* glasses carry their own index model and the
                // baseline shift belongs in the report, not hidden under it.
                double originalWfe = double.NaN;
                int replaced = 0;
                if (Program.Has(args, "-ribbon") || Program.Has(args, "-prepare"))
                {
                    originalWfe = Metric(sys);
                    replaced = Convert.Prepare(sys, say);
                    if (replaced > 0)
                    {
                        say(string.Format(CultureInfo.InvariantCulture,
                            "  {0} surface(s) converted; re-scanning the copy", replaced));
                        outDir = Program.Value(args, "-outdir")
                            ?? Path.Combine(Path.GetDirectoryName(sys.SystemFile), "moldstress");
                        els = Session.FindElements(sys, extra);
                    }
                }
                Gating.ApplyOverrides(els, Program.Value(args, "-gateconfig"));

                say("MoldStress");
                say("  " + Program.ScopeLabel);
                say("  system: " + (string.IsNullOrEmpty(sys.SystemFile) ? "(unsaved)" : sys.SystemFile));
                say(string.Format(CultureInfo.InvariantCulture,
                    "  process: fill {0:F2} s, pack {1:F1} MPa for {2:F1} s",
                    proc.FillTimeS, proc.PackPressureMPa, proc.PackTimeS));
                say("");
                if (els.Count == 0)
                {
                    // The likeliest first experience of this tool: a click on a
                    // lens whose materials are ordinary glasses. Saying only "no
                    // mouldable element" names the lack; the fix needs the list.
                    say("  NO MOULDABLE ELEMENT FOUND, so nothing was analysed.");
                    var used = Session.MaterialsInUse(sys);
                    say("  this system's materials: " +
                        (used.Count == 0 ? "(none)" : string.Join(", ", used.ToArray())));
                    say("  MoldStress only recognises its own polymer rows: " +
                        string.Join(", ", Polymers.All.Select(q => q.Name).ToArray()) + ".");
                    say("  Materials it can CONVERT automatically: PMMA, ACRYLIC, POLYCARB,");
                    say("  POLYSTYR, ZEONEX 480R, TOPAS 6017 - name an element's glass one of");
                    say("  those and press the button again: the system is then saved as a");
                    say("  -MoldStress copy, the catalogue attached, and the materials");
                    say("  replaced there. The original file is never modified.");
                    return finish(NothingApplied);
                }

                // --- NON-SPHERICAL SURFACES ARE REFUSED -----------------------
                //
                // This solver reads only the base radius, so every surface it
                // models is a pure sphere. Until 2026-08-20 that substitution was
                // SILENT: an aspheric lens produced a complete, plausible run on a
                // geometry it does not have, and the tool's own validation suite
                // could never have caught it - its one per-lens reference case is
                // plano-convex.
                //
                // Refusing is the right default rather than warning, because the
                // output is not uncertain here, it is about a different part. The
                // escape hatch exists for the case where the departure is known to
                // be negligible, and it prints what is being approximated rather
                // than going quiet.
                var odd = els.Where(x => x.ShapeUnreadable != null).ToList();
                if (odd.Count > 0)
                {
                    bool allow = Program.Has(args, "-allow-nonspherical");
                    say(allow
                        ? "  UNREADABLE SURFACE TYPES, APPROXIMATED AS SPHERES (-allow-nonspherical):"
                        : "  REFUSED: surface types whose shape this solver cannot read.");
                    foreach (var x in odd)
                        say(string.Format("    surfaces {0}-{1}  {2}",
                            x.FrontSurface, x.BackSurface, x.ShapeUnreadable));
                    if (!allow)
                    {
                        say("");
                        say("  Conics and even/odd aspheric terms ARE read and modelled. These");
                        say("  types are not, so only the base radius would survive - a different");
                        say("  cavity profile, feeding the fill time, the wall thickness, the");
                        say("  freeze history and the geometry written into STAR. Pass");
                        say("  -allow-nonspherical to proceed anyway if you know the departure");
                        say("  is negligible for your part.");
                        return finish(Program.UsageError);
                    }
                    say("");
                }

                // A pinched wall is refused outright and has no escape hatch: the
                // gapwise solver divides by the local thickness, and a cavity that
                // closes inside the aperture is not a moulding, it is a geometry
                // error upstream in the prescription.
                foreach (var x in els)
                {
                    double rMin;
                    double hMin = x.MinThicknessMm(out rMin);
                    if (hMin > 0) continue;
                    say(string.Format(CultureInfo.InvariantCulture,
                        "  REFUSED: surfaces {0}-{1} leave no wall - thickness {2:F4} mm " +
                        "at r = {3:F3} mm.", x.FrontSurface, x.BackSurface, hMin, rMin));
                    return finish(Program.UsageError);
                }

                // --- baseline, measured before anything is loaded --------------
                double baseWfe = Metric(sys);
                say(string.Format(CultureInfo.InvariantCulture,
                    "  baseline RMS wavefront error: {0:F6} waves", baseWfe));
                say("");

                var written = new List<StarFiles.Written>();

                // WHAT ACTUALLY LANDED. The delta at the bottom of this report is
                // only a measurement if something was applied between the two
                // metric reads; see NoDeltaReason.
                int elementsWithPoints = 0, elementsApplied = 0;
                var refusedElements = new List<string>();

                // THE POLARISATION HALF, carried out of the loop so it can be
                // reported beside the scalar rather than only inside a per-element
                // block. Peak over the elements that produced a map; the count of
                // those that did NOT is carried too, because a missing map is not
                // a zero and must not read as one.
                double peakRetWaves = 0.0, peakRetNm = 0.0;
                int peakRetFront = 0, peakRetBack = 0;
                int retMeasured = 0, retMissing = 0;

                foreach (var e in els)
                {
                    var p = Polymers.ByName(e.Material);
                    string aliasOf = Polymers.AliasTarget(e.Material);
                    if (aliasOf != null)
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      NOTE: {0} is not in the MoldStress table; its stress-optic and " +
                            "rheological constants are BORROWED from {1}. Substitution, not an " +
                            "identification - results are indicative for {0}.", e.Material, aliasOf));
                    var fill = FillField.Build(e, p, proc, 101);
                    // Physics at the converged grid; the file gets a small
                    // wall-clustered subset of those same nodes. The two used to
                    // be the same number, which forced a choice between a
                    // converged model and a file STAR could ingest.
                    int nzPhys = (int)Program.Value(args, "-nz", 321.0);
                    if (nzPhys % 2 == 0) nzPhys++;
                    int nzExport = (int)Program.Value(args, "-nzexport", 41.0);
                    var freeze = FreezeHistory.Build(Math.Max(e.EdgeThicknessMm, 0.2), p, proc,
                                                     nzPhys, 10 * nzPhys);
                    var ch = Channels.Build(e, p, proc, fill, freeze);
                    var w = StarFiles.Write(e, p, ch, fill, freeze, outDir, 17, 24, nzExport);
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

                    // THE DENSITY HALF CARRIES AN UNMEASURED ASSUMPTION, and until
                    // 2026-08-22 it carried it silently. StarFiles converts that
                    // index shift into an equivalent hydrostatic stress by DIVIDING
                    // by K11 + 2*K12, and writes the result into the STAR file - so
                    // the number above and the file both inherit a split this model
                    // takes from N-BK7, a glass. Waxler et al. (1979) measured the
                    // split for the only two polymers anyone has, and it came out a
                    // factor of 37 and the opposite SIGN from the assumption for an
                    // acrylic. The retardance half is untouched: it rides on the
                    // measured DIFFERENCE and no choice of split moves it.
                    if (!p.SplitMeasured)
                    {
                        double lo, hi;
                        double span = SplitUncertainty.IsotropicSpan(p, out lo, out hi);
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      CAVEAT    that density figure rests on an ASSUMED K11/K12 " +
                            "split - not measured for {0}.", p.Name));
                        say(string.Format(CultureInfo.InvariantCulture,
                            "                across the splits real polymers have been measured " +
                            "at, K11+2K12 spans {0:F1} to {1:F1} Br, a factor of {2:F0}, and the " +
                            "density term scales inversely with it. Sign is not guaranteed either.",
                            lo, hi, span));
                        say("                The retardance above is UNAFFECTED - it rides on the " +
                            "measured difference.");
                    }
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
                    if (w.Points > 0) elementsWithPoints++;
                    if (read > 0) elementsApplied++;
                    // Named, not just counted. "2 of 3 applied" tells the user
                    // there is a problem; the surface pair and the material tell
                    // them which BD record is missing.
                    else if (w.Points > 0)
                        refusedElements.Add(string.Format(CultureInfo.InvariantCulture,
                            "surfaces {0}-{1} ({2})", e.FrontSurface, e.BackSurface, e.Material));
                    if (read == 0)
                        say("      STAR      REFUSED THE STRESS DATA - does " + e.Material +
                            " carry a BD record? Run -writecatalog and add MOLDSTRESS to the system.");

                    int samples; string note;
                    double peakRet = PeakRetardance(st, e, out samples, out note);
                    if (note != null) { retMissing++; say("      retardance UNAVAILABLE: " + note); }
                    else
                    {
                        retMeasured++;
                        // WAVES FIRST. rad and nm are both correct and neither is
                        // comparable by eye to an RMS wavefront error in waves,
                        // which is the number sitting four lines below it.
                        double waves = Math.Abs(peakRet) / (2 * Math.PI);
                        double nm = waves *
                            sys.SystemData.Wavelengths.GetWavelength(1).Wavelength * 1000.0;
                        if (waves > peakRetWaves)
                        {
                            peakRetWaves = waves; peakRetNm = nm;
                            peakRetFront = e.FrontSurface; peakRetBack = e.BackSurface;
                        }
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      retardance peak {0:F4} waves = {1:F1} nm ({2:F4} rad), " +
                            "over {3} map points", waves, nm, peakRet, samples));
                    }

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
                string noDelta = NoDeltaReason(elementsWithPoints, elementsApplied,
                                               baseWfe, loadedWfe);
                bool deltaRefused = noDelta != null;

                // TWO QUANTITIES, TWO QUESTIONS, AND THE RUN ENDS ON THE SECOND.
                //
                // Until 2026-08-21 the last number this tool printed was the RMS
                // wavefront delta, so that is the number people quote. On the one
                // real lens it read +0.5% while peak retardance was 0.41 waves - a
                // factor of 585 - and for a polarisation-sensitive system the
                // scalar is the wrong headline by two and a half orders of
                // magnitude. Both are correct; they answer different questions,
                // and only one of them is about birefringence, which is what this
                // tool exists to estimate.
                string partial = PartialCoverage(elementsWithPoints, elementsApplied);

                say("  WAVEFRONT - what any system sees");
                if (partial != null)
                {
                    // ABOVE the number, not below it. A qualification printed
                    // after the figure it qualifies is read second or not at all,
                    // and this one changes what the figure MEANS.
                    say("    PARTIAL: " + partial);
                    foreach (var r in refusedElements)
                        say("             refused: " + r);
                    say("    The change below is a real measurement of the system as");
                    say("    LOADED, which is not the moulded part. Do not quote it as the");
                    say("    part's moulding effect.");
                }
                if (deltaRefused)
                {
                    say("    NO CHANGE IS REPORTED. " + noDelta);
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    the two metric reads were {0:F6} and {1:F6} waves; their",
                        baseWfe, loadedWfe));
                    say("    difference is NOT a moulding effect and is not printed as one.");
                }
                else
                {
                    if (replaced > 0 && !double.IsNaN(originalWfe))
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    original materials         {0:F6} waves RMS (before substitution)",
                            originalWfe));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    baseline                   {0:F6} waves RMS", baseWfe));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    with moulding effects      {0:F6} waves RMS", loadedWfe));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    change                     {0:+0.000000;-0.000000} waves ({1:+0.0;-0.0}%)",
                        loadedWfe - baseWfe, 100.0 * (loadedWfe - baseWfe) / baseWfe));
                }
                say("");

                say("  POLARISATION - what a birefringent system sees");
                if (retMeasured == 0 && deltaRefused)
                {
                    // Both halves missing. Saying "the wavefront number above
                    // stands alone" here would be wrong - there is no number
                    // above, and the run already said why.
                    say("    NOT MEASURED either. Nothing was applied, so there is no");
                    say("    birefringence to read back.");
                }
                else if (retMeasured == 0)
                {
                    // THE DANGEROUS ONE. Stress was applied and the wavefront
                    // moved, so a number exists and reads as the result - while
                    // the half this tool exists to estimate is simply absent.
                    say("    NOT MEASURED on any element, although stress WAS applied. The");
                    say("    wavefront number above therefore stands alone, and on the one");
                    say("    real lens tested the wavefront understated the polarisation");
                    say("    effect by 585x. Do not quote it as the moulding result until");
                    say("    the retardance map is readable.");
                }
                else
                {
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    peak retardance            {0:F4} waves = {1:F1} nm, " +
                        "on surfaces {2}-{3}", peakRetWaves, peakRetNm,
                        peakRetFront, peakRetBack));
                    if (retMissing > 0)
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    peak is over {0} of {1} elements - {2} produced no map, and a",
                            retMeasured, retMeasured + retMissing, retMissing) +
                            " missing map is not a zero.");

                    string verdict = ScalarVerdict(peakRetWaves, baseWfe, loadedWfe, deltaRefused);
                    if (verdict != null) { say(""); say("    " + verdict); }
                }
                say("");
                say("  Files are in " + outDir);
                // Only when there IS a change. Pointing the user at a Difference
                // view of nothing is the same false reassurance one level down.
                if (!deltaRefused)
                {
                    say("  In OpticStudio, any analysis window's STAR Effects setting switches");
                    say("  between On and Difference, which shows this change directly.");
                }

                // Three outcomes, three codes; the report write and the ribbon
                // open both live in finish(), shared with every refusal above.
                if (deltaRefused) return finish(NothingApplied);
                return finish(partial != null ? PartialApplication : 0);
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
        /// <summary>
        /// How badly the scalar wavefront delta understates the polarisation
        /// effect, in words, or null when there is nothing to warn about.
        /// PURE, so both arms are testable without OpticStudio.
        ///
        /// THE BOUNDARY IS DERIVED, NOT CHOSEN. The two quantities are both in
        /// waves, so the comparison that matters is simply which is larger: once
        /// peak retardance exceeds the wavefront change, a reader quoting the
        /// wavefront change alone is quoting the smaller of two effects and
        /// calling it the result. No threshold was invented to make that fire.
        ///
        /// The ratio is reported whichever way it goes, because the case where
        /// the scalar is the LARGER number is real too and the reader should not
        /// have to infer it from silence.
        /// </summary>
        internal static string ScalarVerdict(double peakRetWaves, double baseWfe,
                                             double loadedWfe, bool deltaRefused)
        {
            if (deltaRefused) return null;                 // no scalar to compare against
            double delta = Math.Abs(loadedWfe - baseWfe);
            if (!(peakRetWaves > 0.0)) return null;

            if (delta <= 0.0)
                return string.Format(CultureInfo.InvariantCulture,
                    "THE WAVEFRONT DID NOT MOVE AT ALL and the retardance is {0:F4} waves. "
                    + "Quoting the wavefront result would report this part as unaffected.",
                    peakRetWaves);

            double ratio = peakRetWaves / delta;
            if (ratio <= 1.0)
                return string.Format(CultureInfo.InvariantCulture,
                    "For scale: peak retardance is {0} the wavefront change, so the "
                    + "wavefront number is the larger effect here.", Times(ratio));

            // THE RATIO CARRIES THE FORCE, so it must not be rounded into
            // nonsense. `{0:F0}` printed "UNDERSTATES BY A FACTOR OF 1" at a
            // ratio of 1.01 - which reads as "not at all", the opposite of the
            // warning. No numeral appears in the claim itself now; the claim is
            // that the wavefront number understates, and the ratio says by how
            // much, to a precision that still means something near 1.
            return string.Format(CultureInfo.InvariantCulture,
                "THE WAVEFRONT NUMBER UNDERSTATES THIS. Peak retardance is {0} the "
                + "wavefront change - {1:F4} waves against {2:F6} waves. For a "
                + "polarisation-sensitive system the retardance is the result.",
                Times(ratio), peakRetWaves, delta);
        }

        /// <summary>A ratio as a multiplier, kept legible across four orders of
        /// magnitude: two decimals near 1 where the digits decide the meaning,
        /// none by the time it reaches the hundreds where they are noise.</summary>
        private static string Times(double ratio)
        {
            string f = ratio < 10.0 ? "{0:F2}x" : "{0:F0}x";
            return string.Format(CultureInfo.InvariantCulture, f, ratio);
        }

        /// <summary>
        /// Exit code for a run that completed, wrote its files, and has NO
        /// performance change to report. Distinct from UsageError (64): the
        /// invocation was fine, the answer is missing.
        /// </summary>
        internal const int NothingApplied = 65;

        /// <summary>
        /// Exit code for a run where SOME elements were applied and some were
        /// not. Distinct from 65: there IS a number, and it is a real
        /// measurement - of the wrong object.
        /// </summary>
        internal const int PartialApplication = 66;

        /// <summary>
        /// Says that only part of the part was loaded, or null when coverage is
        /// complete. PURE, so both arms are testable without OpticStudio.
        ///
        /// THE GAP THIS CLOSES, left open deliberately on 2026-08-21 and named in
        /// that day's report: `NoDeltaReason` fires only when NOTHING was applied.
        /// Two elements of three landing produced a confident before/after with no
        /// hint that a third of the part was missing from the "after". That is the
        /// same defect as the -100% case in a quieter register - there, the number
        /// described a system nothing had been done to; here, it describes a
        /// system some of the moulding was done to, and is quoted as the part.
        ///
        /// The denominator is elements that PRODUCED POINTS, not all elements. An
        /// element that offered nothing was not refused, and counting it as a
        /// miss would fire this warning on runs where coverage is complete.
        /// </summary>
        internal static string PartialCoverage(int elementsWithPoints, int elementsApplied)
        {
            if (elementsApplied <= 0) return null;                    // NoDeltaReason owns this
            if (elementsApplied >= elementsWithPoints) return null;   // complete
            return string.Format(CultureInfo.InvariantCulture,
                "STAR accepted {0} of {1} elements. {2} carried stress that was not applied.",
                elementsApplied, elementsWithPoints, elementsWithPoints - elementsApplied);
        }

        /// <summary>
        /// Why the before/after delta is not a measurement, or null when it is.
        /// PURE, so both arms are testable without OpticStudio.
        ///
        /// FOUND 2026-08-21 on a lens whose material carried no BD record. STAR
        /// rejected every one of 15015 stress points, the retardance map came
        /// back empty, the post-import metric read exactly 0.000000 waves, and
        /// the tool printed:
        ///
        ///     change:  -72.716883 waves (-100.0%)
        ///
        /// and exited 0. Nothing had been applied to anything. A 100%
        /// improvement is the single most attention-grabbing number this tool
        /// can print and it was produced by total failure.
        ///
        /// THREE SEPARATE WAYS THE DELTA CAN BE FICTION, and the run has to be
        /// refused on any of them:
        ///
        /// (a) NOTHING WAS APPLIED. If no element's stress data was accepted,
        ///     the system in front of the second metric read is the same system
        ///     as the first. Any difference is drift, not moulding.
        ///
        /// (b) A METRIC IS NOT A MEASUREMENT. RWRE returning exactly 0.000000 on
        ///     a real system is the merit operand failing, not a perfect
        ///     wavefront - the same "compliance is not a value" trap the
        ///     boundary operands set (MNEA, DIMX and friends all report the
        ///     VIOLATION, and satisfied reads 0.0). NaN is the other half.
        ///
        /// (c) THE BASELINE IS ZERO. The old code divided by it under a
        ///     `baseWfe > 0` ternary whose else-branch printed "+0.0%" - a
        ///     silent zero standing in for an undefined ratio.
        /// </summary>
        internal static string NoDeltaReason(int elementsWithPoints, int elementsApplied,
                                             double baseWfe, double loadedWfe)
        {
            if (elementsWithPoints > 0 && elementsApplied == 0)
                return "STAR accepted no stress data on any element, so nothing was applied "
                     + "and the system measured afterwards is the one measured before.";
            if (!IsMeasurement(baseWfe))
                return "the BASELINE metric did not evaluate (" + Describe(baseWfe)
                     + "), so there is nothing to measure a change against.";
            if (!IsMeasurement(loadedWfe))
                return "the post-import metric did not evaluate (" + Describe(loadedWfe)
                     + "); an RMS wavefront error of exactly zero is the operand failing, "
                     + "not a perfect wavefront.";
            return null;
        }

        /// <summary>A finite, strictly positive RMS wavefront error. Zero is
        /// excluded on purpose - see NoDeltaReason (b).</summary>
        private static bool IsMeasurement(double waves)
        {
            return !double.IsNaN(waves) && !double.IsInfinity(waves) && waves > 0.0;
        }

        private static string Describe(double waves)
        {
            if (double.IsNaN(waves)) return "NaN";
            if (double.IsInfinity(waves)) return "infinite";
            if (waves == 0.0) return "exactly 0.000000 waves";
            return string.Format(CultureInfo.InvariantCulture, "{0:F6} waves", waves);
        }

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

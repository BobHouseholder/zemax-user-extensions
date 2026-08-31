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
            "-full", "-packpressure", "-packtime", "-prepare", "-ribbon",
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

                // THE SAME WAVELENGTH GATE Convert.Prepare applies, for systems
                // that already carry MS_* glasses by hand - they die identically
                // outside the catalogue's validity, just without the conversion
                // step to catch it.
                {
                    var umHere = new List<double>();
                    try
                    {
                        var wlq = sys.SystemData.Wavelengths;
                        for (int i = 1; i <= wlq.NumberOfWavelengths; i++)
                            umHere.Add(wlq.GetWavelength(i).Wavelength);
                    }
                    catch { }
                    var badWl = CatalogWriter.WavelengthsOutOfRange(umHere);
                    if (els.Count > 0 && badWl.Count > 0)
                    {
                        say(string.Format(CultureInfo.InvariantCulture,
                            "  REFUSED: wavelength(s) {0} um lie outside the MOLDSTRESS " +
                            "catalogue's validity, {1:F1}-{2:F1} um.",
                            string.Join(", ", badWl.Select(v => v.ToString("F4", CultureInfo.InvariantCulture))),
                            CatalogWriter.LambdaMinUm, CatalogWriter.LambdaMaxUm));
                        say("  The MS_* glasses are an nd/vd fit - visible-band by construction -");
                        say("  and outside that band the extrapolated index is meaningless: rays");
                        say("  fail and FFT-based analyses refuse to compute. Restrict the system");
                        say("  to " + string.Format(CultureInfo.InvariantCulture, "{0:F1}-{1:F1}",
                            CatalogWriter.LambdaMinUm, CatalogWriter.LambdaMaxUm) +
                            " um for the moulding estimate, then restore its real bands.");
                        return finish(Program.UsageError);
                    }
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

                // WHERE THE DESIGN PUT THE IMAGE PLANE, recorded before any data
                // is loaded, because the file may move it afterwards by itself.
                //
                // A lens whose last airspace carries a focus solve - a marginal
                // ray height solve is the usual one - re-solves that airspace the
                // moment the index data lands. The baseline and the loaded system
                // are then measured at DIFFERENT image planes, and the difference
                // between them is part optics and part refocusing with no way to
                // separate the two from the two numbers. Measured 2026-08-29 on a
                // plastic Cooke triplet: the solve moved the plane 325 um, which
                // is 250 um PAST the real best focus, and the reported change went
                // from 0.010 waves to 0.331. Both numbers are real; only one of
                // them is a moulding effect.
                int imgPrev = sys.LDE.NumberOfSurfaces - 2;
                double planeDesign = double.NaN;
                string planeSolve = null;
                ZOSAPI.Editors.ISolveData planeSolveData = null;
                try
                {
                    planeDesign = sys.LDE.GetSurfaceAt(imgPrev).Thickness;
                    planeSolveData = sys.LDE.GetSurfaceAt(imgPrev).ThicknessCell.GetSolveData();
                    if (planeSolveData != null &&
                        planeSolveData.Type != ZOSAPI.Editors.SolveType.Fixed)
                        planeSolve = planeSolveData.Type.ToString();
                    else
                        planeSolveData = null;
                }
                catch { }

                // WHERE REAL RAYS FOCUS NOW, before any data is loaded. Cheap -
                // there is no index volume to step through yet - and it is half
                // of the only question that matters about the solve's shift:
                // does it correspond to anything real rays do.
                double baseBestMm = double.NaN, baseBestWaves = double.NaN;
                bool baseBestAtEdge = false, baseBestFlat = false;

                // WHAT HAPPENS AT WAVELENGTHS OTHER THAN THE d-LINE, measured
                // before anything is loaded, because two separate things are
                // wrong there and neither is visible in the wavefront number.
                //
                // StarFiles writes ONE index per point - p.Nd, the d-line - and
                // STAR's direct-index route applies it at EVERY wavelength, so a
                // converted element loses its own dispersion. Isolated 2026-08-29
                // with a NULL cloud (every point exactly Nd, physically a no-op):
                // it left the d-line untouched and moved F and C by three orders
                // of magnitude more than the moulding change it carried.
                var waveUm = new List<double>();
                try
                {
                    var wlAll = sys.SystemData.Wavelengths;
                    for (int i = 1; i <= wlAll.NumberOfWavelengths; i++)
                        waveUm.Add(wlAll.GetWavelength(i).Wavelength);
                }
                catch { }
                int dLineIdx = -1;
                {
                    double best = double.MaxValue;
                    for (int i = 0; i < waveUm.Count; i++)
                    {
                        double g = Math.Abs(waveUm[i] - 0.5875618);
                        if (g < best) { best = g; dLineIdx = i; }
                    }
                }
                var indexByElement = new Dictionary<int, double[]>();
                var dnByElement = new Dictionary<int, double>();
                foreach (var eD in els)
                {
                    var n = new double[waveUm.Count];
                    for (int i = 0; i < n.Length; i++)
                    {
                        try
                        {
                            n[i] = sys.MFE.GetOperandValue(
                                ZOSAPI.Editors.MFE.MeritOperandType.INDX,
                                eD.FrontSurface, i + 1, 0, 0, 0, 0, 0, 0);
                        }
                        catch { n[i] = double.NaN; }
                    }
                    indexByElement[eD.FrontSurface] = n;
                }

                // WHERE REAL RAYS FOCUS NOW, before any data is loaded, at the
                // d-line - the wavelength the index data is written at, and the
                // only one where a focus shift means moulding rather than the
                // direct-index route's dispersion flattening.
                if (!double.IsNaN(planeDesign))
                    baseBestMm = BestFocusOffsetMm(sys, imgPrev,
                                                   (dLineIdx >= 0) ? dLineIdx + 1 : 1,
                                                   planeDesign, 0.30, 25,
                                                   out baseBestAtEdge,
                                                   out baseBestFlat,
                                                   out baseBestWaves);
                say("");

                // INDEX-ONLY IS THE DEFAULT, at Bob's direction, 2026-08-22
                // ("scale back, for now, and only calculate the change in
                // refractive index from molding"). In this mode the density
                // index change is loaded through STAR's DIRECT INDEX route and
                // nothing else is applied - no stress tensor, no birefringence,
                // no retardance.
                //
                // The scale-back is also a caveat-shedding move, and that is
                // worth stating: the direct-index route applies DnDensity as an
                // index change without ever touching K11/K12, so the refuted
                // split assumption does not enter; and the flow law the 1989
                // literature indicts drives the birefringence channel, which is
                // not applied here. What remains is Lorentz-Lorenz on the packing
                // pressure - the least-criticised channel in the tool. -full
                // restores the stress/birefringence export.
                bool indexOnly = !Program.Has(args, "-full");
                if (indexOnly)
                {
                    say("  INDEX-ONLY MODE (default): only the refractive-index change from");
                    say("  moulding is computed and applied, through STAR's direct-index route.");
                    say("  No stress, no birefringence, no retardance. Pass -full for those.");
                    say("");
                }

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
                // The peak's MATERIAL, so the band can be taken from the
                // coefficient that actually produced it rather than from
                // whichever element happened to be scanned last.
                string peakRetPolymer = null;
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
                    var w = StarFiles.Write(e, p, ch, fill, freeze, outDir, 17, 24, nzExport,
                                            indexOnly ? 4 : 0);
                    written.Add(w);

                    Session.Describe(e);
                    if (e.ExportSemiDiameterMm > e.SemiDiameterMm + 1e-9)
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      export   to the MECHANICAL semi-diameter, {0:F3} mm " +
                            "(clear aperture {1:F3}); beyond the clear aperture the rim's " +
                            "values are carried outward, not solved",
                            e.ExportSemiDiameterMm, e.SemiDiameterMm));
                    // THE FLANGE, AND WHETHER IT WAS DECLARED OR ASSUMED. The
                    // cavity gap is floored so a lens sagitta cannot produce a
                    // knife rim that dp/ds ~ 1/h^3 would then let dominate the
                    // whole field. That floor used to be the gate land, silently.
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      flange    {0} {1:F3} mm; it sets the gap over {2:F0}% of the flow path",
                        e.FloorIsAssumed ? "NOT DECLARED, assumed equal to the gate land"
                                         : "declared",
                        fill.FloorMm, 100.0 * fill.FloorBoundFraction));
                    if (e.FloorIsAssumed && fill.FloorBoundFraction > 0.25)
                        say("                that assumption is carrying the fill field here - " +
                            "set flange=<mm> in -gateconfig to own the number");
                    if (e.GateFeedsThinEnd)
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      NOTE      the rim gate feeds the THIN end ({0:F3} mm edge " +
                            "against a {1:F3} mm centre). Practice is to gate the thick section; " +
                            "a lens cannot, because the thick section is the aperture - which is " +
                            "why the flange above has to hold the feed open.",
                            e.EdgeThicknessMm, e.CentreThicknessMm));
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
                    dnByElement[e.FrontSurface] = w.PeakDnDensity;

                    // THE DENSITY HALF CARRIES AN UNMEASURED ASSUMPTION - but
                    // ONLY on the stress-tensor route, where StarFiles divides the
                    // index shift by K11 + 2*K12 (a split refuted by Waxler 1979,
                    // factor 37 and the wrong sign for an acrylic). The DIRECT
                    // INDEX route applies DnDensity as-is and never touches the
                    // split, so in index-only mode this caveat would be FALSE and
                    // printing it anyway would teach users to ignore it.
                    if (!indexOnly && !p.SplitMeasured)
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
                        "      files     {0} stress points, {1} index points, " +
                        "peak equivalent stress {2:F1} N/mm2",
                        w.Points, w.IndexPoints, w.PeakEquivalentStressMPa));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "      sampling  ring grid captures the density field to {0:F2}% of " +
                        "its span (radial midpoint test{1})",
                        w.SamplingErrorPct,
                        w.SamplingErrorPct == 0.0 ? "; field is uniform" : ""));

                    // --- load it -----------------------------------------------
                    var surf = sys.LDE.GetSurfaceAt(e.FrontSurface);
                    var st = surf.STARData.Stress;
                    var di = surf.STARData.DirectIndex;
                    int importCode = 0, read = 0, readIndex = 0;

                    if (indexOnly)
                    {
                        // INDEX ONLY: unload any stale stress so nothing rides
                        // along from a previous run, then load the index change
                        // through the direct route. No stress, no birefringence.
                        try { st.FEAData.UnloadData(); } catch { }
                        di.SetDataIsLocal();
                        di.FEAData.ImportDirectIndex_1(w.IndexPath);
                        readIndex = di.FEAData.NumberOfDataPoints;
                        di.Fits.Refit();
                        double step = StarFiles.GrinStepFor(e.CentreThicknessMm);
                        try { di.Fits.GRINStep = step; } catch { }
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      STAR      index {0} points accepted (direct index; " +
                            "no stress applied); GRIN step {1:F2} mm, ~{2:F0} steps/ray",
                            readIndex, step, e.CentreThicknessMm / step));
                    }
                    else
                    {
                        try { st.FEAData.UnloadData(); } catch { }
                        st.SetDataIsLocal();
                        st.SetWorkingWavelength(1);
                        // ImportStress(string) is obsolete and, per the API's own
                        // attribute, "no longer implemented" - it returns nothing
                        // and does nothing. Exactly the silent no-op this project
                        // keeps running into. ImportStress_1 returns a status code.
                        importCode = st.FEAData.ImportStress_1(w.StressPath);
                        read = st.FEAData.NumberOfDataPoints;
                        st.Fits.Refit();
                        st.Fits.ApplyStress();

                        try
                        {
                            // OFF by default in full mode, and the A/B above is
                            // why: it costs the retardance map entirely. The
                            // density term is already in the stress tensor as a
                            // hydrostatic component.
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
                    }

                    say(string.Format(CultureInfo.InvariantCulture,
                        "      STAR      stress {0}/{1} points accepted (code {2}), index {3}",
                        read, w.Points, importCode, readIndex));
                    if (w.Points > 0) elementsWithPoints++;
                    if ((indexOnly ? readIndex : read) > 0) elementsApplied++;
                    // Named, not just counted. "2 of 3 applied" tells the user
                    // there is a problem; the surface pair and the material tell
                    // them which record is missing.
                    else if (w.Points > 0)
                        refusedElements.Add(string.Format(CultureInfo.InvariantCulture,
                            "surfaces {0}-{1} ({2})", e.FrontSurface, e.BackSurface, e.Material));
                    if (!indexOnly && read == 0)
                        say("      STAR      REFUSED THE STRESS DATA - does " + e.Material +
                            " carry a BD record? Run -writecatalog and add MOLDSTRESS to the system.");
                    if (indexOnly && readIndex == 0)
                        say("      STAR      REFUSED THE INDEX DATA - is the MOLDSTRESS " +
                            "catalogue attached to the system?");

                    if (indexOnly)
                    {
                        retMissing++;   // by design, not by failure; the summary says so
                        say("");
                        continue;
                    }
                    int samples; string note;
                    double localBiref = PeakLocalBirefringence(st, e, out samples, out note);
                    if (note != null) { retMissing++; say("      retardance UNAVAILABLE: " + note); }
                    else
                    {
                        retMeasured++;
                        // WAVES FIRST. rad/mm and nm are both correct and neither
                        // is comparable by eye to an RMS wavefront error in waves,
                        // which is the number sitting four lines below it.
                        //
                        // AND IT IS A BOUND, NOT A PEAK. The local birefringence
                        // is trustworthy (see PeakLocalBirefringence); turning it
                        // into a retardance needs the path, and taking the LONGEST
                        // path everywhere over-estimates unless the field is
                        // uniform. Over-estimating is the safe direction and the
                        // word "at most" is in the output so nobody quotes it as a
                        // measured peak.
                        double pathMm = MaxAxialPathMm(e);
                        double waves = RetardanceBoundWaves(localBiref, pathMm);
                        double nm = waves * LambdaDMm * 1e6;   // d-line, NOT wavelength 1
                        if (waves > peakRetWaves)
                        {
                            peakRetWaves = waves; peakRetNm = nm;
                            peakRetFront = e.FrontSurface; peakRetBack = e.BackSurface;
                            peakRetPolymer = e.Material;
                        }
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      birefringence  {0:F5} rad/mm at the d-line, over {1} points",
                            localBiref, samples));
                        say(string.Format(CultureInfo.InvariantCulture,
                            "      retardance     at most {0:F4} waves = {1:F1} nm over the " +
                            "longest path ({2:F3} mm)", waves, nm, pathMm));
                    }

                    // The tool's own silent-zero guard. A stress field that
                    // carries real equivalent stress cannot produce exactly zero
                    // birefringence; if it does, something upstream is not
                    // functioning and a zero must not be reported as a result.
                    if (note == null && localBiref == 0.0 && w.PeakEquivalentStressMPa > 0)
                        say("      REFUSING that zero: " +
                            string.Format(CultureInfo.InvariantCulture,
                            "{0:F1} N/mm2 of equivalent stress cannot give exactly zero " +
                            "retardance. Check the material carries K11/K12.",
                            w.PeakEquivalentStressMPa));
                    say("");
                }

                // THE FIRST READ IS AT WHATEVER PLANE THE FILE CHOSE. If a solve
                // moved it, this is what the user sees on opening the copy - and it
                // is NOT the moulding effect on its own.
                //
                // REFRESH FIRST. Until 2026-08-30 this read came straight off the
                // STAR load and was stale by 0.008160158 waves on the validation
                // triplet - see RefreshAfterStarLoad. The second read below was
                // saved only by the pin-back's write, and only on lenses whose
                // solve actually moves the plane.
                RefreshAfterStarLoad(sys);
                double movedWfe = Metric(sys);
                double planeLoaded = double.NaN;
                try { planeLoaded = sys.LDE.GetSurfaceAt(imgPrev).Thickness; }
                catch { }
                double planeShiftMm = planeLoaded - planeDesign;

                // THE SECOND READ IS AT THE PLANE THE BASELINE WAS MEASURED AT.
                // Pin the airspace and put it back where the design had it, so the
                // only difference between this read and the baseline is the index
                // data. This is the number the report calls the moulding effect.
                bool planePinned = false;
                if (!double.IsNaN(planeDesign) && Math.Abs(planeShiftMm) > 0.0)
                {
                    try
                    {
                        var cell = sys.LDE.GetSurfaceAt(imgPrev).ThicknessCell;
                        cell.MakeSolveFixed();
                        sys.LDE.GetSurfaceAt(imgPrev).Thickness = planeDesign;
                        planePinned = true;
                    }
                    catch { }
                }
                double loadedWfe = planePinned ? Metric(sys) : movedWfe;

                // PUT IT BACK. MakeSolveFixed deleted the user's solve to take the
                // measurement; on the -prepare path that edit lands in the
                // -MoldStress sibling, but `-run` with no -file attaches to a
                // RUNNING OpticStudio and measures the live system. A measurement
                // tool does not get to edit the thing it measures.
                if (planePinned && planeSolveData != null)
                {
                    try
                    {
                        sys.LDE.GetSurfaceAt(imgPrev).ThicknessCell
                           .SetSolveData(planeSolveData);
                    }
                    catch (Exception ex)
                    {
                        say("  WARNING: the image-plane solve on surface "
                            + imgPrev + " was pinned for the measurement and could "
                            + "NOT be restored (" + ex.Message + "). This system now "
                            + "has a fixed last airspace where it had a solve.");
                    }
                }

                // AND WHERE THEY FOCUS WITH THE DATA IN. Same range, same
                // sampling, same metric - a scan is only comparable to another
                // scan on the same grid, which is why both are done here rather
                // than left to whatever the user reaches for.
                double mouldBestMm = double.NaN, mouldBestWaves = double.NaN;
                bool mouldBestAtEdge = false, mouldBestFlat = false;
                int focusWave = (dLineIdx >= 0) ? dLineIdx + 1 : 1;
                if (!double.IsNaN(planeDesign))
                    mouldBestMm = BestFocusOffsetMm(sys, imgPrev, focusWave,
                                                    planeDesign, 0.30, 25,
                                                    out mouldBestAtEdge,
                                                    out mouldBestFlat,
                                                    out mouldBestWaves);

                string noDelta = NoDeltaReason(elementsWithPoints, elementsApplied,
                                               baseWfe, loadedWfe);
                bool deltaRefused = noDelta != null;

                // TWO QUANTITIES, TWO QUESTIONS, AND THE RUN ENDS ON THE SECOND.
                //
                // Until 2026-08-21 the last number this tool printed was the RMS
                // wavefront delta, so that is the number people quote, and for a
                // polarisation-sensitive system the scalar is the wrong headline
                // by orders of magnitude. Both are correct; they answer different
                // questions, and only one of them is about birefringence, which
                // is what this tool exists to estimate.
                //
                // THE FACTOR OF 585 THAT USED TO BE QUOTED HERE IS WITHDRAWN,
                // 2026-08-29. Its numerator came from GetRetardanceMap, which
                // controls have since shown is not a retardance at all - it
                // returns pi or 2*pi on a field with every stress component
                // exactly zero. The lens it was measured on cannot be re-run
                // here, so the number is not being corrected, it is being
                // retracted: it was computed from a route that fails six
                // closed-form controls. On the validation triplet, measured at a
                // pinned plane with the route that passes them, the ratio is
                // 1513x - 1.2125 waves of retardance bound against 0.000802
                // waves of RMS wavefront change, confirmed by three independent
                // routes (ribbon-deployed binary on a live GUI session, the same
                // binary standalone with -file, and a probe following the same
                // pin order). A FIRST replacement of 176x stood for a few hours
                // on 2026-08-29 and was itself wrong in BOTH halves - see the
                // pin-order entry in the README's Open list. The QUALITATIVE claim survives
                // and is if anything stronger; the specific figure does not.
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
                    if (planePinned)
                        say("    measured at the SAME image plane as the baseline, so this is "
                            + "optics and not refocusing - see IMAGE PLANE below");
                }
                say("");

                // --- the focus shift, reported as its own quantity -------------
                say("  IMAGE PLANE - what this file's own focus solve does");
                string planeCase = PlaneCase(planeDesign, planeShiftMm,
                                             planeSolve, planePinned);
                if (planeCase == "unread")
                {
                    say("    NOT READ. The last airspace could not be inspected, so this run");
                    say("    cannot say whether the image plane moved. Treat the wavefront");
                    say("    change above as possibly including a refocus.");
                }
                else if (planeCase == "fixed")
                {
                    say("    the image plane is FIXED in this file and did not move, so the");
                    say("    change above is measured at one plane throughout.");
                }
                else if (planeCase == "unpinned")
                {
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    a {0} solve is on surface {1} and the plane moved {2:+0.0;-0.0} um,",
                        planeSolve ?? "thickness", imgPrev, planeShiftMm * 1000.0));
                    say("    but PINNING IT FAILED, so the change above still mixes the two.");
                    say("    Do not quote it as the moulding effect.");
                }
                else
                {
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    a {0} solve on surface {1} moves the image plane {2:+0.0;-0.0} um",
                        planeSolve ?? "thickness", imgPrev, planeShiftMm * 1000.0));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    at that moved plane the wavefront reads {0:F6} waves RMS,",
                        movedWfe));
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    which is {0:+0.000000;-0.000000} waves against the baseline - "
                        + "but {1:F6} of that",
                        movedWfe - baseWfe, Math.Abs(movedWfe - loadedWfe)));
                    say("    is DEFOCUS, and a refocus removes it.");
                    say("");
                    // MEASURED ON THIS LENS, not quoted from another one.
                    if (double.IsNaN(baseBestMm) || double.IsNaN(mouldBestMm))
                    {
                        say("    Real-ray best focus could NOT be scanned on this system, so");
                        say("    whether the solve's shift corresponds to anything real rays");
                        say("    do is unknown here. On the two lenses where it was measured,");
                        say("    it did not.");
                    }
                    else if (baseBestFlat || mouldBestFlat)
                    {
                        say("    Real-ray best focus could not be scanned: the image plane did");
                        say("    not move across the whole range, so the curve is flat and its");
                        say("    minimum is meaningless. That is an instrument failure, not a");
                        say("    result - most likely a solve holding the plane.");
                    }
                    else if (baseBestAtEdge || mouldBestAtEdge)
                    {
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    Real-ray best focus is NOT BRACKETED by the +/-300 um scan " +
                            "({0:+0.0;-0.0} and {1:+0.0;-0.0} um sit on its edge), so no " +
                            "shift is reported:", baseBestMm * 1000.0, mouldBestMm * 1000.0));
                        say("    a minimum found at the end of a range is a statement about");
                        say("    the range. Scan wider before quoting it.");
                    }
                    else
                    {
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    REAL RAYS, scanned on this lens: best focus {0:+0.0;-0.0} um " +
                            "before", baseBestMm * 1000.0));
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    and {0:+0.0;-0.0} um after - a real shift of {1:+0.0;-0.0} um, " +
                            "against the solve's {2:+0.0;-0.0} um.",
                            mouldBestMm * 1000.0,
                            (mouldBestMm - baseBestMm) * 1000.0, planeShiftMm * 1000.0));
                        // SIGNED. An equal-and-opposite shift is not agreement,
                        // and the first version called -294 um against +275 um
                        // "94 % - real rays DO follow it" because it compared
                        // magnitudes.
                        double ratio = Math.Abs(planeShiftMm) > 0
                            ? (mouldBestMm - baseBestMm) / planeShiftMm
                            : double.NaN;
                        if (double.IsNaN(ratio))
                            say("    The solve did not move the plane, so there is nothing to " +
                                "compare.");
                        else if (ratio < -0.05)
                            say(string.Format(CultureInfo.InvariantCulture,
                                "    Real rays move the OTHER WAY ({0:P0} of the solve's " +
                                "shift), so the", ratio)
                                + " solve is not merely overshooting - it is wrong in sign.");
                        else if (ratio < 0.25)
                            say(string.Format(CultureInfo.InvariantCulture,
                                "    The solve is chasing a PARAXIAL shift real rays do not " +
                                "follow ({0:P0} of it).", ratio));
                        else if (ratio < 0.75)
                            say(string.Format(CultureInfo.InvariantCulture,
                                "    Real rays follow PART of it ({0:P0}); the rest is " +
                                "paraxial only.", ratio));
                        else
                            say(string.Format(CultureInfo.InvariantCulture,
                                "    Real rays DO follow it ({0:P0}), so this is a genuine " +
                                "refocus.", ratio));
                    }
                    say("    A fixed-focus assembly has to hold whichever of these is real;");
                    say("    an adjustable one does not.");
                }
                say("");

                if (waveUm.Count > 1 && dLineIdx >= 0)
                {
                    // The inversion check is on the MATERIAL's own indices, so
                    // this block corrects itself when the catalogue is fixed
                    // instead of becoming a claim nobody re-tests.
                    var inverted = new List<string>();
                    foreach (var eD in els)
                    {
                        double[] n;
                        if (!indexByElement.TryGetValue(eD.FrontSurface, out n)) continue;
                        double vd = Vd(n, waveUm, dLineIdx);
                        if (!double.IsNaN(vd) && vd < 0.0)
                            inverted.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0} (Vd {1:F1})", eD.Material, vd));
                    }

                    say("  DISPERSION - what this run is worth away from the d-line");
                    if (inverted.Count > 0)
                    {
                        say("    REFUSE THE POLYCHROMATIC RESULT. The material rows themselves");
                        say("    carry the WRONG SIGN of dispersion - index RISING with");
                        say("    wavelength - so the baseline is wrong at every wavelength but");
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    {0:F4} um, before any moulding data is loaded:",
                            waveUm[dLineIdx]));
                        foreach (var s2 in inverted) say("      " + s2);
                        say("    A real optical polymer has Vd positive (PMMA +57.4, polystyrene");
                        say("    +30.9). This is a defect in the generated MOULDSTRESS catalogue,");
                        say("    not in the lens: CatalogWriter fits the Sellmeier c1 negative.");
                        say("    Until it is fixed, use ONE wavelength - both this and the");
                        say("    substitution below vanish at the d-line, where the row is");
                        say("    anchored on nd exactly.");
                        say("");
                    }
                    if (indexOnly && elementsApplied > 0)
                    {
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    STAR's direct-index route is MONOCHROMATIC: it applies one"));
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    index per point at all {0} wavelengths, and this run wrote the",
                            waveUm.Count));
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    {0:F4} um value, so each element ALSO loses its own dispersion",
                            waveUm[dLineIdx]));
                        say("    on loading. That substitution is an index error:");
                        foreach (var eD in els)
                        {
                            double[] n;
                            double dn;
                            if (!indexByElement.TryGetValue(eD.FrontSurface, out n)) continue;
                            if (!dnByElement.TryGetValue(eD.FrontSurface, out dn)) continue;
                            say(string.Format(CultureInfo.InvariantCulture,
                                "      surfaces {0}-{1}  {2}   moulding dn {3:E2}",
                                eD.FrontSurface, eD.BackSurface, eD.Material, dn));
                            for (int i = 0; i < waveUm.Count; i++)
                            {
                                if (double.IsNaN(n[i]) || double.IsNaN(n[dLineIdx])) continue;
                                double err = n[dLineIdx] - n[i];
                                string tag = (i == dLineIdx)
                                    ? "   <- written at this wavelength"
                                    : (dn > 0 ? string.Format(CultureInfo.InvariantCulture,
                                        "   {0:F0}x the moulding change", Math.Abs(err) / dn)
                                      : "");
                                say(string.Format(CultureInfo.InvariantCulture,
                                    "        {0:F4} um  imposed error {1:+0.000000;-0.000000}{2}",
                                    waveUm[i], err, tag));
                            }
                        }
                        say("    This is a property of STAR's route, not of this tool's physics:");
                        say("    IndexDataType is read-only, and the switchable PhysicsBasedIndex");
                        say("    route is the stress/temperature one, which would reintroduce the");
                        say("    K11/K12 split Waxler 1979 refuted.");
                    }
                    say("");
                }

                say("  POLARISATION - what a birefringent system sees");
                if (indexOnly)
                {
                    say("    NOT COMPUTED, by design. Index-only mode applies the density index");
                    say("    change and nothing else - no stress tensor, no birefringence, no");
                    say("    retardance. On the validation triplet the retardance bound was");
                    say("    1513x the RMS wavefront change, so a polarisation-sensitive");
                    say("    system needs the full run: pass -full.");
                }
                else if (retMeasured == 0 && deltaRefused)
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
                    say("    wavefront number above therefore stands alone, and on the");
                    say("    validation triplet the wavefront understated the polarisation");
                    say("    effect by 1513x. Do not quote it as the moulding result until");
                    say("    the birefringence is readable.");
                }
                else
                {
                    // AT MOST, not a peak. What STAR returns is a LOCAL
                    // birefringence; turning it into a retardance takes the
                    // longest path through the element, which is exact only if
                    // the field is uniform and an over-estimate otherwise.
                    say(string.Format(CultureInfo.InvariantCulture,
                        "    retardance      at most {0:F4} waves = {1:F1} nm at the d-line, " +
                        "on surfaces {2}-{3}", peakRetWaves, peakRetNm,
                        peakRetFront, peakRetBack));

                    // THE BAND THE NUMBER INHERITS FROM ITS OWN COEFFICIENT.
                    //
                    // Retardance is proportional to K11-K12 = -KGlass, so the
                    // coefficient's stated interval propagates exactly. Printing
                    // four decimals beside a constant whose own citation spans a
                    // factor of three is what this line exists to stop.
                    //
                    // An UNQUANTIFIED row is reported as such and never as a
                    // zero-width band: "the source gave one number" and "the
                    // value is certain" look identical otherwise, and two of the
                    // five rows in this table are the first kind.
                    {
                        var pk = (peakRetPolymer != null) ? Polymers.ByName(peakRetPolymer) : null;
                        double bLo, bHi;
                        if (Polymers.RetardanceBand(pk, peakRetWaves, out bLo, out bHi))
                            say(string.Format(CultureInfo.InvariantCulture,
                                "                    from the coefficient's own interval: " +
                                "{0:F4} to {1:F4} waves ({2} K {3:F1} to {4:F1} Br, a factor of {5:F1})",
                                bLo, bHi, peakRetPolymer,
                                pk.KGlassLowBr, pk.KGlassHighBr,
                                Polymers.RetardanceBandFactor(pk)));
                        else if (pk != null)
                            say(string.Format(CultureInfo.InvariantCulture,
                                "                    band UNQUANTIFIED - {0}'s source states a " +
                                "value and no interval, so this figure's precision is not " +
                                "supported and not bounded either", peakRetPolymer));
                    }
                    say("    That is a BOUND: local birefringence over the longest path,");
                    say("    exact for a uniform field and high otherwise. The map route");
                    say("    this used to read returned pi on a stress-FREE element.");
                    if (retMissing > 0)
                        say(string.Format(CultureInfo.InvariantCulture,
                            "    bound is over {0} of {1} elements - {2} produced no data, and a",
                            retMeasured, retMeasured + retMissing, retMissing) +
                            " missing map is not a zero.");

                    string verdict = ScalarVerdict(peakRetWaves, baseWfe, loadedWfe, deltaRefused);
                    if (verdict != null) { say(""); say("    " + verdict); }
                }
                say("");
                say("  Files are in " + outDir);
                // The cost warning, where the user reads it before opening an
                // analysis. Measured 2026-08-22: FFT-type analyses step every ray
                // through every element's fitted index volume - about a second per
                // element per wavelength at 32x32 on this machine, and GUI
                // sampling of 128x128 is 16x the rays. A long compute with no
                // progress bar reads as a hang.
                if (indexOnly && elementsApplied > 0)
                {
                    say("");
                    say("  NOTE: with STAR index data loaded, FFT-type analyses (MTF, PSF)");
                    say("  trace every ray through the index volume and can take MINUTES on");
                    say("  a multi-element system at high sampling. If a window seems hung,");
                    say("  check CPU in Task Manager: pegged means computing - let it finish");
                    say("  or lower the analysis sampling. Idle means genuinely stuck.");
                }
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

        /// <summary>
        /// Which of four things to say about the image plane. The FORMATTING
        /// stays at the call site; this is the choice, so it can be tested.
        ///
        /// The four are not decoration - they carry different obligations:
        /// "unread" means the run cannot promise the wavefront change is free of
        /// refocusing; "unpinned" means it positively is NOT free of it and the
        /// number must not be quoted; "fixed" and "pinned" both mean it is.
        /// </summary>
        internal static string PlaneCase(double planeDesign, double planeShiftMm,
                                         string planeSolve, bool planePinned)
        {
            if (double.IsNaN(planeDesign)) return "unread";
            if (planeSolve == null && planeShiftMm == 0.0) return "fixed";
            if (planeShiftMm == 0.0) return "fixed";
            return planePinned ? "pinned" : "unpinned";
        }

        /// <summary>
        /// Abbe number from indices already read off the system, so the
        /// dispersion check is on what the LENS actually has rather than on what
        /// a catalogue header claims. NaN when the band is too narrow to divide
        /// by - never a fabricated large number.
        ///
        /// Negative means the index RISES with wavelength, which no transparent
        /// optical polymer does in the visible. Found 2026-08-29 in this tool's
        /// own generated catalogue.
        /// </summary>
        internal static double Vd(double[] n, List<double> waveUm, int dIdx)
        {
            if (n == null || waveUm == null || n.Length != waveUm.Count) return double.NaN;
            if (n.Length < 2 || dIdx < 0 || dIdx >= n.Length) return double.NaN;
            int shortest = 0, longest = 0;
            for (int i = 0; i < waveUm.Count; i++)
            {
                if (waveUm[i] < waveUm[shortest]) shortest = i;
                if (waveUm[i] > waveUm[longest]) longest = i;
            }
            double span = n[shortest] - n[longest];
            if (double.IsNaN(span) || Math.Abs(span) < 1e-9) return double.NaN;
            return (n[dIdx] - 1.0) / span;
        }

        /// <summary>
        /// Where real rays actually focus, by scanning the image plane and
        /// reading the metric at each position. Returns the offset in mm from
        /// the plane's current position; NaN if it could not be measured.
        ///
        /// `atEdge` is not a detail. A minimum found at the end of a scan range
        /// is a statement about the RANGE, and this project shipped exactly that
        /// mistake on 2026-08-29 - a through-focus scan run over +/-60 um whose
        /// curves were still falling at the edge, reported as a minimum, and
        /// only caught because the number looked too round. The caller must say
        /// "not bracketed" rather than quote an edge.
        ///
        /// The plane is restored before returning, so this is a measurement and
        /// not an edit.
        /// </summary>
        private static double BestFocusOffsetMm(ZOSAPI.IOpticalSystem sys, int imgPrev,
                                                int wave, double originMm,
                                                double halfRangeMm, int samples,
                                                out bool atEdge, out bool flat,
                                                out double bestMetric)
        {
            atEdge = false;
            flat = false;
            bestMetric = double.NaN;
            double best = double.NaN;
            try
            {
                var row = sys.LDE.GetSurfaceAt(imgPrev);
                var cell = row.ThicknessCell;

                // PIN IT HERE. The first version set Thickness on a surface whose
                // focus solve was still live, so the solve put it straight back
                // and every sample read the same number - a flat curve whose
                // "minimum" is its first sample, which then reads as an edge.
                // The scan owns its own pinning now rather than depending on
                // where the caller sits in the pin/restore dance.
                ZOSAPI.Editors.ISolveData saved = null;
                try
                {
                    saved = cell.GetSolveData();
                    if (saved != null && saved.Type == ZOSAPI.Editors.SolveType.Fixed)
                        saved = null;
                    cell.MakeSolveFixed();
                }
                catch { }

                // THE ORIGIN IS GIVEN, NOT TAKEN FROM WHEREVER THE PLANE
                // HAPPENS TO BE. The moulded scan runs after the solve has been
                // restored, so `row.Thickness` there is the SOLVED plane - 294 um
                // from where the baseline scan was centred. Two scans on
                // different zeros produce offsets that cannot be subtracted, and
                // the first version subtracted them anyway.
                double restore = row.Thickness;
                double t0 = originMm;
                int bestIdx = -1;
                double lo = double.MaxValue, hi = -double.MaxValue;
                int seen = 0;
                for (int i = 0; i < samples; i++)
                {
                    double d = -halfRangeMm + 2.0 * halfRangeMm * i / (samples - 1.0);
                    row.Thickness = t0 + d;
                    double m = MetricAt(sys, wave);
                    if (double.IsNaN(m) || m <= 0.0) continue;
                    seen++;
                    lo = Math.Min(lo, m);
                    hi = Math.Max(hi, m);
                    if (double.IsNaN(bestMetric) || m < bestMetric)
                    {
                        bestMetric = m;
                        best = d;
                        bestIdx = i;
                    }
                }
                row.Thickness = restore;
                if (saved != null)
                {
                    try { cell.SetSolveData(saved); } catch { }
                }

                // A SCAN THAT CANNOT MOVE ITS VARIABLE IS NOT A MEASUREMENT.
                // Zero span across the whole range means the plane never moved -
                // an instrument failure, and a different thing from a minimum
                // that sits outside the range.
                flat = (seen < 2) || (hi - lo) <= 0.0;
                atEdge = (bestIdx == 0 || bestIdx == samples - 1);
            }
            catch { return double.NaN; }
            return best;
        }

        /// <summary>The metric at a chosen wavelength. Metric() is wavelength 1;
        /// the focus scan needs the d-line, where the direct-index route's
        /// dispersion flattening is absent and a focus shift means moulding.
        /// </summary>
        private static double MetricAt(ZOSAPI.IOpticalSystem sys, int wave)
        {
            try
            {
                return sys.MFE.GetOperandValue(
                    ZOSAPI.Editors.MFE.MeritOperandType.RWRE, 4, wave,
                    0, 0, 0, 0, 0, 0);
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// Invalidate whatever RWRE is being served from, WITHOUT changing the
        /// system. Measured 2026-08-30 (`validation/mtf-triplet/pinorder6.py`):
        /// after ApplyStress() the wavefront operand returns a stale value, a
        /// merit-operand read does NOT refresh it, and any write to the lens
        /// data editor does - including a thickness assigned its own value.
        /// On the validation triplet the stale and refreshed readings differ by
        /// 0.008160158 waves, enough to flip the SIGN of the reported effect.
        ///
        /// Call this after loading STAR data and before every measurement.
        ///
        /// ON THE VALIDATION TRIPLET THIS CHANGES NOTHING, and that is expected
        /// rather than a disappointment: the numbers are bit-identical across
        /// the fix because this path already writes a thickness in the pin-back
        /// before the read. Here the helper is insurance.
        ///
        /// IT IS A CORRECTION ON THE PATH WHERE THE PLANE DOES NOT MOVE. That
        /// pin-back is guarded by `Math.Abs(planeShiftMm) > 0.0`, so on a lens
        /// with no focus solve - or one whose solve leaves the plane alone -
        /// nothing was written and the reading stayed stale. That case is
        /// measured, not reasoned: `pinorder3.py` state A pins the solve first
        /// so the plane never moves, writes nothing afterwards, and reads
        /// -0.007359 waves where the refreshed answer is +0.000802.
        ///
        /// The helper's own operation is verified in `pinorder7.py`: assigning
        /// surface 1's thickness to itself carries 100% of the 0.008160158-wave
        /// gap, so this is not a no-op under a confident comment.
        /// </summary>
        private static void RefreshAfterStarLoad(ZOSAPI.IOpticalSystem sys)
        {
            try
            {
                // surface 1 always exists on a real prescription; assigning a
                // value to itself is a write with no optical consequence.
                var su = sys.LDE.GetSurfaceAt(1);
                su.Thickness = su.Thickness;
            }
            catch { }
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
        /// The d-line, in mm. GetPointRetardanceList reports at THIS wavelength
        /// whatever SetWorkingWavelength was given - measured 2026-08-29 by
        /// reading the same uniform field at working wavelengths 1 and 2 and
        /// getting the same 1.92486 rad, which is the closed form at 0.5875618
        /// and not at 0.486133. It is why the nm conversion no longer uses
        /// wavelength 1, which made the published nm figure 17.3% low.
        /// </summary>
        internal const double LambdaDMm = 0.5875618e-3;

        /// <summary>
        /// PEAK LOCAL BIREFRINGENCE, in radians per mm at the d-line.
        ///
        /// THIS USED TO READ GetRetardanceMap, AND THAT WAS NOT A MEASUREMENT.
        /// Established 2026-08-29 by loading uniform stress fields whose
        /// retardance is known in closed form, every one of which STAR accepted
        /// cleanly (import code 0, 15015 of 15015 points - so these are answers
        /// about STAR, not about a failed import):
        ///
        ///   * a NULL field, every tensor component exactly zero, returned peak
        ///     |R| of exactly pi or 2*pi - the tool would have printed 0.5000 or
        ///     1.0000 waves of retardance from no stress at all;
        ///   * so did a HYDROSTATIC field, whose retardance is zero by symmetry
        ///     while carrying 10 N/mm2;
        ///   * the same physical state ROTATED 45 degrees read 0.062 rad against
        ///     4.260 on element 3, a factor of 69. Retardance is a property of
        ///     the medium and cannot depend on that;
        ///   * it did not scale with stress - 814x the closed form at
        ///     0.02 N/mm2, 0.16x at 200 - passing through 1.0x near 10 N/mm2,
        ///     which is where the one published measurement happened to sit;
        ///   * on every ring of a uniform field it took three values, 0, +d and
        ///     d-pi, with span exactly pi and exact zeros at 0, +-90 and 180
        ///     degrees of azimuth. That is an ANGLE, not a phase.
        ///
        /// GetPointRetardanceList passes all of it. Against the closed form
        /// 2*pi*(K11-K12)*S/lambda_d per mm it reads 0.9978, exactly 0.000000 on
        /// the null and hydrostatic arms, 1.0000 for the same state at 0, 30, 45
        /// and 90 degrees, and 1.9976 for pure shear where theory demands 2.
        ///
        /// It is a LOCAL quantity, so it is returned as one and the caller
        /// bounds the retardance with the longest axial path. That bound is
        /// exact for a uniform field and an over-estimate otherwise, which is
        /// the safe direction for a number this tool already tells people to
        /// treat as an order of magnitude.
        /// </summary>
        private static double PeakLocalBirefringence(ZOSAPI.Editors.LDE.ISTAR_Stress st,
                                                     MouldedElement e, out int samples, out string note)
        {
            note = null;
            samples = 0;
            double peak = 0.0;
            try
            {
                // density is a sampling SELECTOR, not a point count.
                var list = st.Fits.GetPointRetardanceList(8, 0, 1);
                if (list != null)
                {
                    samples = list.Length;
                    foreach (var pt in list)
                        if (Math.Abs(pt.Retardance) > Math.Abs(peak)) peak = pt.Retardance;
                }
            }
            catch (Exception ex)
            {
                note = "GetPointRetardanceList raised: " + ex.Message;
                return double.NaN;
            }
            if (samples == 0) note = "GetPointRetardanceList returned no points";
            return Math.Abs(peak);
        }

        /// <summary>
        /// The longest ray path through the element, which is what turns a local
        /// birefringence into a retardance bound. A biconvex element is thickest
        /// on axis and a biconcave one at its edge, so both ends are taken rather
        /// than assuming the centre - on the validation triplet the middle
        /// element's path peaks at the edge at 2.45 mm against a 1.20 mm centre.
        /// </summary>
        internal static double MaxAxialPathMm(MouldedElement e)
        {
            double ct = e.CentreThicknessMm, et = e.EdgeThicknessMm;
            if (double.IsNaN(et) || et <= 0.0) return ct;
            return Math.Max(ct, et);
        }

        /// <summary>
        /// Local birefringence (rad/mm at the d-line) times the longest path,
        /// expressed in waves. Split out so the self-test can exercise the
        /// conversion without an OpticStudio session.
        /// </summary>
        internal static double RetardanceBoundWaves(double localRadPerMm, double pathMm)
        {
            if (double.IsNaN(localRadPerMm) || double.IsNaN(pathMm)) return double.NaN;
            return Math.Abs(localRadPerMm) * pathMm / (2.0 * Math.PI);
        }
    }
}

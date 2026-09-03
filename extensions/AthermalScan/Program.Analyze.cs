using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AthermalScan
{
    partial class Program
    {
        internal static void Analyze(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys)
        {
            // sys is passed in: the extension analyses the live system, the User Analysis
            // a CopySystem() clone, so the open prescription is never touched.
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("this extension requires a sequential system");

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;
            var env = sys.SystemData.Environment;

            int primaryWave = 1;
            var wls = sys.SystemData.Wavelengths;
            for (int w = 1; w <= wls.NumberOfWavelengths; w++)
                if (wls.GetWavelength(w).IsPrimary) { primaryWave = w; break; }
            double lambdaUm = wls.GetWavelength(primaryWave).Wavelength;

            Say("=== Athermal Scan ===");
            Say("Lens file : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));

            // ---- guards: the scan must own the whole environment -----------------
            if (Math.Abs(Opts.TMax - Opts.TMin) < 1e-9)
                throw new Exception("-tmin and -tmax must differ; there is nothing to sweep.");
            CheckNoEnvironmentOperands(sys);
            CheckSolves(lde, imgIdx);

            // ---- baseline state -------------------------------------------------
            // The raw values are what gets put back; t0/p0 are the design environment
            // the prescription is taken to have been measured in, which is NOT the
            // same thing when the adjust-index switch is off.
            double tRaw = env.Temperature, pRaw = env.Pressure;
            bool adjust0 = env.AdjustIndexToEnvironment;
            double t0 = tRaw, p0 = pRaw;
            if (!adjust0)
            {
                // Manual, Environment settings: "when the adjust index box is unchecked
                // the system temperature is set to 20 degrees C and the pressure to 1.0
                // atmospheres, and therefore all index data must be relative to that
                // environment". The stored temperature/pressure are then not the design
                // environment, and guessing would silently pick the wrong index
                // reference - in air when the instrument flies in vacuum, or vice versa.
                if (!Opts.Temp0.HasValue)
                    throw new Exception(
                        "'Adjust Index Data To Environment' is OFF in this file, so OpticStudio evaluates " +
                        "all index data as if the system were at 20 C and 1.0 atm and the stored temperature " +
                        "and pressure do not define the design environment. Re-run with -temp0 <C> (plus " +
                        "-pressure <atm> or -vacuum if the design is not at 1 atm) to declare the environment " +
                        "the radii and thicknesses were measured in.");
                t0 = Opts.Temp0.Value;
                // -press0 names the design pressure outright; -pressure alone still
                // means "the design is at this pressure and so is the scan", which is
                // the common case for a file that was never given an environment.
                p0 = Opts.Press0 ?? Opts.Pressure ?? 1.0;
            }
            else
            {
                if (Opts.Temp0.HasValue) t0 = Opts.Temp0.Value;
                if (Opts.Press0.HasValue) p0 = Opts.Press0.Value;
            }

            double pStart = Opts.Pressure ?? p0;
            double pEnd = Opts.PressureEnd ?? pStart;
            bool pVaries = Math.Abs(pEnd - pStart) > 1e-12;
            bool pShifted = Math.Abs(pStart - p0) > 1e-12 || pVaries;

            R.LensFile = sys.SystemFile ?? "";
            R.DesignTempC = t0; R.DesignPressAtm = p0; R.AdjustIndexWasOn = adjust0;
            R.ScanPressStart = pStart; R.ScanPressEnd = pEnd;
            R.TMin = Opts.TMin; R.TMax = Opts.TMax; R.Steps = Opts.Steps;

            Say(F("Design environment: {0:F1} C, {1:F3} atm", t0, p0));
            Say(F("Scan              : {0:F0}..{1:F0} C, {2} steps, {3}",
                Opts.TMin, Opts.TMax, Opts.Steps,
                pVaries ? F("pressure {0:F3} -> {1:F3} atm (paired soak)", pStart, pEnd)
                        : F("pressure {0:F3} atm", pStart)));
            Say("Index convention  : " + Convention(pStart) + (pVaries ? ", at the start of the sweep" : ""));
            if (!adjust0)
                Say("NOTE: the file had 'Adjust Index Data To Environment' OFF, which pins index data to " +
                    "20 C / 1.0 atm; the design environment above was taken from the command line. The " +
                    "switch is enabled for the scan and restored afterwards.");
            if (!adjust0 && !Opts.Pressure.HasValue && !Opts.Press0.HasValue)
                Say("NOTE: neither -press0 nor -pressure was given with -temp0, so the design pressure is " +
                    "assumed to be 1.0 atm (the adjust-off convention). Use -vacuum for a vacuum design, or " +
                    "-press0 1 -vacuum for one built in air and flown in vacuum.");
            if (Opts.Temp0.HasValue && adjust0 && Math.Abs(Opts.Temp0.Value - tRaw) > 1e-9)
                Say(F("NOTE: -temp0 {0:F1} C overrides the file's system temperature of {1:F1} C as the " +
                      "design point.", t0, tRaw));
            if (pShifted)
                Say("NOTE: the scan pressure differs from the design pressure, so the focus shift includes " +
                    "the pressure term; it is reported separately below.");

            // ---- snapshot prescription + effective TCE per row ------------------
            var snaps = new RowSnap[imgIdx];
            var glassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new RowSnap { Type = row.Type };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                s.Thickness = row.Thickness;
                try { s.Conic = row.Conic; } catch { s.Conic = 0; }
                // The radius the edge thickness is measured at. Taken from the
                // snapshot, so it stays the as-built value while the sweep runs.
                try { s.SemiDia = row.SemiDiameter; } catch { s.SemiDia = 0; }
                try { s.MechSemiDia = row.MechanicalSemiDiameter; } catch { s.MechSemiDia = 0; }
                string mat = (row.Material ?? "").Trim();
                s.Material = mat;
                s.IsGlass = mat.Length > 0 && mat != "-" &&
                            !mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                if (s.IsGlass) glassNames.Add(mat);
                try { s.MountTce = row.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.TCE).DoubleValue; }
                catch { s.MountTce = 0; }
                for (int p = 1; p <= 8; p++)
                {
                    try
                    {
                        var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                        s.Pars[p] = row.GetSurfaceCell(col).DoubleValue;
                    }
                    catch { s.Pars[p] = 0; }
                }
                snaps[i - 1] = s;
            }
            if (glassNames.Count == 0)
                throw new Exception("no glass surfaces found - nothing to athermalize");
            foreach (var s in snaps)
                if (s != null && (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak))
                    Say("NOTE: coordinate break thicknesses expand with their TCE column; decenters/tilts are held fixed.");

            // ---- glass TCE + thermal index data from the materials catalog ------
            var glassTce = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var noThermalIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignoreExpansion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var catalogsInUse = sys.SystemData.MaterialCatalogs.GetCatalogsInUse()
                .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var matTool = sys.Tools.OpenMaterialsCatalog();
            try
            {
                foreach (string cat in catalogsInUse)
                {
                    string[] names;
                    try { matTool.SelectedCatalog = cat; names = matTool.GetAllMaterials(); }
                    catch { continue; }
                    foreach (string nm in names)
                    {
                        if (!glassNames.Contains(nm) || glassTce.ContainsKey(nm)) continue;
                        matTool.SelectedMaterial = nm;
                        glassTce[nm] = matTool.TCE; // in 1e-6/K
                        // All six Schott constants zero means OpticStudio computes no
                        // index change at all for this material (manual 2.1.1.4.2:
                        // "if no thermal data has been added to the catalog, no thermal
                        // effects are considered").
                        if (matTool.D0 == 0 && matTool.D1 == 0 && matTool.D2 == 0 &&
                            matTool.E0 == 0 && matTool.E1 == 0)
                            noThermalIndex.Add(nm);
                        if (matTool.IgnoreThermalExpansion) ignoreExpansion.Add(nm);
                    }
                }
            }
            finally { matTool.Close(); }
            foreach (string g in noThermalIndex)
                Say("WARNING: '" + g + "' carries no thermal index constants (D0..E1 all zero), so its " +
                    "absolute index does not change with temperature in this model. At P > 0 the dn/dT " +
                    "reported for it below is purely the air-normalisation term (~1.4e-6/K at n=1.5, " +
                    "1 atm); at P = 0 it is exactly zero.");
            foreach (string g in glassNames)
                if (!glassTce.ContainsKey(g))
                {
                    // Model glasses, MIL-number glasses and GRIN media land here.
                    // Measured on the stock "Doublet using MIL number glasses": dn/dT
                    // comes back as EXACTLY zero at 1 atm, not merely small. The manual
                    // says the relative index of such media is still adjusted for the
                    // surrounding air; in practice no adjustment is applied at all, so
                    // do not promise the reader even that much.
                    Say("WARNING: glass '" + g + "' was not found in the catalogs in use. Assuming TCE = 0, " +
                        "and note that OpticStudio models no dn/dT for model, MIL-number or gradient-index " +
                        "media (manual 2.1.1.4.2) - measured, such a glass reports dn/dT of exactly zero, so " +
                        "this glass's opto-thermal row below is not physical and any dz/dT resting on it is " +
                        "an artefact of that.");
                    glassTce[g] = 0;
                    noThermalIndex.Add(g);
                }
            foreach (string g in ignoreExpansion)
                Say("NOTE: '" + g + "' has the catalog's ignore-thermal-expansion flag set (a gas or liquid). " +
                    "OpticStudio then takes radius expansion from the adjacent solid and only the edge " +
                    "effects from this material; this scan expands it with its own TCE column instead.");

            // effective alphas per the OpticStudio thermal model:
            //  - a glass row's thickness and radius expand with the glass TCE
            //  - the rear surface of a lens (air row following glass) also expands
            //    its RADIUS with that glass TCE; its gap uses the mount TCE column
            for (int i = 0; i < snaps.Length; i++)
            {
                var s = snaps[i];
                if (s == null) continue;
                double mount = s.MountTce;
                if (s.IsGlass)
                {
                    s.AlphaThick = glassTce[s.Material];
                    s.AlphaRadius = glassTce[s.Material];
                }
                else
                {
                    s.AlphaThick = mount;
                    s.AlphaRadius = (i > 0 && snaps[i - 1] != null && snaps[i - 1].IsGlass)
                        ? glassTce[snaps[i - 1].Material] : mount;
                }
            }

            // ---- -dump: expanded prescription at one temperature, then stop ------
            // Exists so the thermal model can be checked surface by surface against
            // OpticStudio's own thermal pickup solves, which is the only external
            // ground truth for the geometry side of this tool.
            if (Opts.DumpAt.HasValue)
            {
                double td = Opts.DumpAt.Value;
                env.AdjustIndexToEnvironment = true;
                try
                {
                    env.Temperature = t0; env.Pressure = p0;
                    ApplyTemperature(sys, snaps, imgIdx, td - t0);
                    env.Temperature = td;
                    Say(F("PRESCRIPTION AT {0:F4} C  (dT = {1:+0.####;-0.####} from the design point)", td, td - t0));
                    Say("  surf                radius             thickness   material");
                    for (int i = 1; i < imgIdx; i++)
                    {
                        var row = lde.GetSurfaceAt(i);
                        double r;
                        try { r = row.Radius; } catch { r = double.PositiveInfinity; }
                        Say(F("  {0,4}   {1,20:G14}   {2,18:G14}   {3}", i, r, row.Thickness, snaps[i - 1].Material));
                    }
                    if (EdgeFallbackRows.Count > 0)
                        Say("  (centre-scaled fallback on surface(s) " +
                            string.Join(", ", EdgeFallbackRows.OrderBy(r => r)) + ")");
                }
                finally
                {
                    RestoreSystem(sys, env, snaps, imgIdx, t0, p0, tRaw, pRaw, adjust0, primaryWave);
                }
                return;
            }

            // ---- the sweep -------------------------------------------------------
            int n = Opts.Steps;
            var temps = new double[n];
            var press = new double[n];
            var focusShift = new double[n];
            var rmsFixed = new double[n];
            var rmsRefoc = new double[n];
            var efl = new double[n];
            // per-glass index at the sweep extremes for dn/dT (surface of first occurrence)
            var glassSurf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
                if (snaps[i - 1].IsGlass && !glassSurf.ContainsKey(snaps[i - 1].Material))
                    glassSurf[snaps[i - 1].Material] = i;
            var indexAtMin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var indexAtMax = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            double efl0 = 0, wfno = 0, totr = 0, track = 0, dofMm = 0;
            double focus0 = double.NaN, eflCheck = double.NaN;
            // one entry per distinct scan pressure that differs from the design
            // pressure: (pressure, focus offset from the design state)
            var pressureTerms = new List<KeyValuePair<double, double>>();
            bool terminated = false;

            // Everything from here to the finally mutates the live prescription and
            // the system environment, so it is unconditionally undone - an exception
            // mid-sweep must not leave the user's lens scaled, at the wrong
            // temperature, with the index-adjust switch flipped.
            env.AdjustIndexToEnvironment = true;
            try
            {
                env.Temperature = t0; env.Pressure = p0;

                // ---- baseline metrics, at the design environment -----------------
                efl0 = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
                wfno = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.WFNO, 0, primaryWave);
                totr = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.TOTR, 0, 0);
                track = Opts.Track > 0 ? Opts.Track : totr;
                dofMm = 2.0 * (lambdaUm * 1e-3) * wfno * wfno; // +/- 2 lambda N^2, lens units (mm assumed)
                Say(F("EFFL {0:F4}, working F/# {1:F3}, total track {2:F3}, mount track L = {3:F3}",
                    efl0, wfno, totr, track));
                Say(F("Diffraction depth of focus: +/- {0:F4} lens units  (2*lambda*N^2, lambda={1:F4} um)",
                    dofMm, lambdaUm));

                // Everything this tool reports is a defocus compared against the depth
                // of focus, so an image space that is not converging makes the whole
                // report meaningless rather than merely imprecise. Caught on the stock
                // "Cooke 40 degree field_zadj" sample, whose image space is near
                // collimated: working F/# 6669, depth of focus +/-48921 on a 17.97
                // total track, and a required housing CTE of -1.9e7 x 1e-6/K reported
                // without a murmur. Refuse instead of emitting numbers like that.
                if (double.IsNaN(wfno) || double.IsInfinity(wfno) || dofMm > Math.Abs(totr))
                    throw new Exception(F(
                        "the image space is not converging - working F/# is {0:G6} and the diffraction depth " +
                        "of focus comes out at +/-{1:G6} lens units against a total track of {2:G6}. Focus " +
                        "shift, athermal range and required housing CTE are all defocus measured against that " +
                        "depth of focus, so none of them means anything here. Check that the image surface is " +
                        "at or near focus; an afocal system needs an angular metric, not a focus shift.",
                        wfno, dofMm, totr));
                R.Efl0 = efl0; R.Wfno = wfno; R.TotalTrack = totr;
                R.MountTrack = track; R.DofMm = dofMm; R.LambdaUm = lambdaUm;

                for (int k = 0; k < n; k++)
                {
                    if (app.TerminateRequested) { terminated = true; break; }
                    double T = Opts.TMin + (Opts.TMax - Opts.TMin) * k / (n - 1);
                    double P = pVaries ? pStart + (pEnd - pStart) * k / (n - 1) : pStart;
                    temps[k] = T; press[k] = P;
                    app.ProgressMessage = F("Evaluating T = {0:F1} C, P = {1:F3} atm...", T, P);
                    app.ProgressPercent = 10 + 70 * k / n;
                    ApplyTemperature(sys, snaps, imgIdx, T - t0);
                    env.Temperature = T; env.Pressure = P;

                    efl[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
                    double focus = MarginalFocus(sys, imgIdx, primaryWave,
                        snaps[imgIdx - 2].Thickness * (1 + snaps[imgIdx - 2].AlphaThick * 1e-6 * (T - t0)));
                    focusShift[k] = focus;
                    rmsFixed[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;

                    // refocused RMS: move the image plane to the marginal focus
                    var lastRow = lde.GetSurfaceAt(imgIdx - 1);
                    double scaledLast = lastRow.Thickness;
                    lastRow.Thickness = focus;
                    rmsRefoc[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;
                    lastRow.Thickness = scaledLast;
                }

                if (!terminated)
                {
                    // focus at the design environment - the zero of the focus shift
                    ApplyTemperature(sys, snaps, imgIdx, 0);
                    env.Temperature = t0; env.Pressure = p0;
                    focus0 = MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness);
                    for (int k = 0; k < n; k++) focusShift[k] -= focus0;

                    // Isolate the pressure term: same temperature, scan pressure. This
                    // is the relative -> absolute index step (every glass index scales
                    // by n_air; air itself is 1.0 at the system pressure by definition),
                    // and it is a constant offset on the sweep, so it never enters dz/dT.
                    // Measure it at every scan pressure that actually differs from the
                    // design pressure. Measuring only at pStart reports ~0 for the
                    // common -psweep case, where the ramp begins at the design
                    // pressure and it is the far end that carries the whole term.
                    foreach (double pp in new[] { pStart, pEnd })
                    {
                        if (Math.Abs(pp - p0) <= 1e-12) continue;
                        if (pressureTerms.Any(kv => Math.Abs(kv.Key - pp) <= 1e-12)) continue;
                        env.Pressure = pp;
                        pressureTerms.Add(new KeyValuePair<double, double>(pp,
                            MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness) - focus0));
                    }

                    // ---- per-glass indices, both points at the SAME pressure ------
                    // Sampling these inside the sweep would mix dT with dP whenever
                    // -psweep is used, and the reported dn/dT would silently carry the
                    // pressure term.
                    app.ProgressMessage = "Measuring per-glass dn/dT...";
                    app.ProgressPercent = 85;
                    env.Pressure = pStart;
                    for (int e = 0; e < 2; e++)
                    {
                        double Te = e == 0 ? Opts.TMin : Opts.TMax;
                        ApplyTemperature(sys, snaps, imgIdx, Te - t0);
                        env.Temperature = Te;
                        var into = e == 0 ? indexAtMin : indexAtMax;
                        foreach (var kv in glassSurf)
                            into[kv.Key] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.INDX, kv.Value, primaryWave);
                    }
                }
            }
            finally
            {
                eflCheck = RestoreSystem(sys, env, snaps, imgIdx, t0, p0, tRaw, pRaw, adjust0, primaryWave);
            }

            if (terminated)
            {
                Say("Terminated by user - system restored, no analysis performed.");
                app.ProgressMessage = "Done. Terminated by user - system restored.";
                return;
            }

            R.Temps = temps; R.Press = press; R.FocusShift = focusShift;
            R.RmsFixed = rmsFixed; R.RmsRefoc = rmsRefoc; R.Efl = efl;
            R.PressureTerms = pressureTerms;
            R.EflCheck = eflCheck;
            R.EdgeFallbackSurfaces = EdgeFallbackRows.OrderBy(i => i).ToList();

            Say(F("Restoration check: EFFL back to {0:G9} (baseline {1:G9}) -> {2}",
                eflCheck, efl0, Math.Abs(eflCheck - efl0) < 1e-6 ? "OK" : "MISMATCH - check the system!"));
            if (EdgeFallbackRows.Count > 0)
                Say("NOTE: surface(s) " + string.Join(", ", EdgeFallbackRows.OrderBy(r => r)) +
                    " have no usable mechanical semi-diameter or a surface form this tool cannot take the " +
                    "sag of, so their gaps were expanded at the centre instead of along the edge. Those " +
                    "gaps will not match Make Thermal where the adjacent radii change.");
            if (pressureTerms.Count > 0)
            {
                Say("");
                Say("PRESSURE TERM at the design temperature (from the relative -> absolute index change;");
                Say("carried by the sweep at that pressure, and not part of dz/dT):");
                foreach (var kv in pressureTerms)
                    Say(F("  {0:F3} -> {1:F3} atm: {2:+0.00000;-0.00000} lens units   ({3:F1} x the depth of focus)",
                        p0, kv.Key, kv.Value, dofMm > 0 ? Math.Abs(kv.Value) / dofMm : 0));
            }

            // ---- sweep table ------------------------------------------------------
            Say("");
            if (pVaries)
            {
                Say("  T (C)   P (atm)   EFFL        focus shift    RMS fixed    RMS refocused");
                Say("  -----   -------   ---------   -----------    ---------    -------------");
                for (int k = 0; k < n; k++)
                    Say(F("  {0,6:F1}  {1,7:F3}   {2,9:F4}   {3,11:+0.00000;-0.00000}    {4,7:F2} um   {5,7:F2} um",
                        temps[k], press[k], efl[k], focusShift[k], rmsFixed[k], rmsRefoc[k]));
                Say("  (focus shift includes the pressure term - see PRESSURE TERM above)");
            }
            else
            {
                Say("  T (C)    EFFL        focus shift    RMS fixed    RMS refocused");
                Say("  -----    ---------   -----------    ---------    -------------");
                for (int k = 0; k < n; k++)
                    Say(F("  {0,6:F1}   {1,9:F4}   {2,11:+0.00000;-0.00000}    {3,7:F2} um   {4,7:F2} um",
                        temps[k], efl[k], focusShift[k], rmsFixed[k], rmsRefoc[k]));
            }

            // ---- athermal analysis ------------------------------------------------
            double slope = LinFit(temps, focusShift); // dz/dT, lens units per C
            Say("");
            Say(F("Thermal defocus rate dz/dT = {0:+0.000000;-0.000000} lens units / C", slope));
            double dtAthermal = Math.Abs(slope) > 1e-12 ? dofMm / Math.Abs(slope) : double.PositiveInfinity;
            Say(F("Fixed-plane athermal range: +/- {0:F1} C about the design temperature (defocus within the DOF)",
                dtAthermal));

            R.DzDt = slope; R.AthermalRangeC = dtAthermal;
            double alphaReq = slope / track * 1e6; // required housing CTE in 1e-6/K
            R.RequiredCte = alphaReq;
            Say("");
            Say(F("PASSIVE HOUSING COMPENSATION over mount track L = {0:F3}:", track));
            Say(F("  required housing CTE = dz/dT / L = {0:+0.00;-0.00} x 1e-6/K", alphaReq));
            var housings = new (string Name, double Cte)[]
            {
                ("Invar 36", 1.3), ("Titanium 6Al4V", 8.6), ("SS 416", 9.9), ("SS 304", 17.3),
                ("Brass", 18.7), ("Aluminum 6061", 23.6), ("Magnesium AZ31", 26.0), ("ALLVAR Alloy 30", -30.0),
            };
            Say("  housing material     CTE(1e-6/K)   residual dz/dT      usable +/- range");
            foreach (var h in housings.OrderBy(h => Math.Abs(h.Cte - alphaReq)))
            {
                double resid = slope - h.Cte * 1e-6 * track;
                double range = Math.Abs(resid) > 1e-12 ? dofMm / Math.Abs(resid) : double.PositiveInfinity;
                Say(F("  {0,-18}   {1,8:F1}      {2,12:+0.000000;-0.000000}    {3,8:F1} C", h.Name, h.Cte, resid, range));
                R.Housings.Add(new Results.HousingRow { Name = h.Name, Cte = h.Cte, ResidualDzDt = resid, UsableRangeC = range });
            }

            // exact bimetallic solution using the two materials bracketing alphaReq
            var lower = housings.Where(h => h.Cte < alphaReq).OrderByDescending(h => h.Cte).ToArray();
            var upper = housings.Where(h => h.Cte >= alphaReq).OrderBy(h => h.Cte).ToArray();
            if (lower.Length > 0 && upper.Length > 0)
            {
                var a = lower[0]; var b = upper[0];
                // L1*a1 + L2*a2 = alphaReq*L,  L1+L2 = L
                double L2 = track * (alphaReq - a.Cte) / (b.Cte - a.Cte);
                double L1 = track - L2;
                Say("");
                R.Bimetallic = F("{0:F3} of {1} + {2:F3} of {3} (total {4:F3})", L1, a.Name, L2, b.Name, track);
                Say(F("  exact bimetallic mount: {0:F3} of {1} + {2:F3} of {3} (total {4:F3})",
                    L1, a.Name, L2, b.Name, track));
            }
            else
            {
                Say("");
                Say("  NO two-metal combination reaches the required CTE: passive housing");
                Say("  compensation alone cannot athermalize this system. Consider optical");
                Say("  athermalization (combine glasses of opposite thermal constant x_f,");
                Say("  see the per-glass table), a re-entrant mount, or active focus.");
            }

            // ---- per-glass opto-thermal table --------------------------------------
            Say("");
            Say("PER-GLASS OPTO-THERMAL DATA (dn/dT measured from the live thermal model):");
            Say("  Index convention: " + Convention(pStart) + ", measured at a fixed " +
                F("{0:F3}", pStart) + " atm over " + F("{0:F0}..{1:F0} C", Opts.TMin, Opts.TMax) + ".");
            if (pStart > 1e-12)
                Say("  These are RELATIVE values: dn/dT is larger than the catalog/datasheet ABSOLUTE " +
                    "dn/dT by n*|dn_air/dT| (~1.4e-6/K at n=1.5, 1 atm). Run with -vacuum for absolute.");
            else
                Say("  These are ABSOLUTE (vacuum) values, directly comparable to catalog dn/dT_abs; " +
                    "an in-air design instead uses the relative values (run without -vacuum).");
            Say("  dn/dT is a secant over the whole sweep and n(T0) a linear interpolation, so both");
            Say("  degrade where the index is strongly non-linear in T (e.g. cryogenic ranges).");
            Say("  glass         n(T0)     dn/dT(1e-6/K)  TCE(1e-6/K)  x_f = dn/dT/(n-1) - a  (1e-6/K)");
            var xf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in glassSurf.Keys)
            {
                double nMin = indexAtMin[g], nMax = indexAtMax[g];
                double dndt = (nMax - nMin) / (Opts.TMax - Opts.TMin) * 1e6;
                double nT0 = nMin + (nMax - nMin) * (t0 - Opts.TMin) / (Opts.TMax - Opts.TMin);
                double x = dndt / (nT0 - 1) - glassTce[g];
                xf[g] = x;
                R.Glasses.Add(new Results.GlassRow { Name = g, NAtT0 = nT0, DnDt = dndt, Tce = glassTce[g], Xf = x,
                    NoThermalIndexData = noThermalIndex.Contains(g) });
                Say(F("  {0,-12}  {1,7:F5}   {2,10:F2}     {3,8:F2}     {4,10:+0.00;-0.00}{5}",
                    g, nT0, dndt, glassTce[g], x,
                    noThermalIndex.Contains(g) ? "   <- no thermal index data: not physical" : ""));
            }
            Say("  (x_f > 0: the element's focus lengthens when heated; pick pairs of opposite x_f");
            Say("   or match the housing to the composite to athermalize - see report for options.)");

            // approximate thin-element share of the thermal power change
            Say("");
            Say("APPROX. ELEMENT CONTRIBUTIONS (thin-element weights, marginal-ray^2 x power):");
            double y1 = Math.Abs(Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.PARY, 1, primaryWave, 0, 0, 0, 1));
            var contrib = new List<(string label, double val)>();
            for (int i = 1; i < imgIdx; i++)
            {
                var s = snaps[i - 1];
                if (!s.IsGlass) continue;
                double nT0 = indexAtMin.ContainsKey(s.Material)
                    ? indexAtMin[s.Material] + (indexAtMax[s.Material] - indexAtMin[s.Material]) * (t0 - Opts.TMin) / (Opts.TMax - Opts.TMin)
                    : 1.5;
                double cFront = (Math.Abs(s.Radius) > 1e10 || s.Radius == 0) ? 0 : 1.0 / s.Radius;
                double rBack = double.PositiveInfinity;
                if (i < imgIdx - 1 && snaps[i] != null) rBack = snaps[i].Radius;
                double cBack = (Math.Abs(rBack) > 1e10 || rBack == 0) ? 0 : 1.0 / rBack;
                double phi = (nT0 - 1) * (cFront - cBack);
                double yi = Math.Abs(Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.PARY, i, primaryWave, 0, 0, 0, 1));
                double w = phi * (yi * yi) / ((1.0 / efl0) * (y1 * y1));
                double c = w * (xf.ContainsKey(s.Material) ? xf[s.Material] : 0);
                contrib.Add((F("surface {0} ({1})", i, s.Material), c));
            }
            double totalC = contrib.Sum(c => Math.Abs(c.val)) + 1e-30;
            foreach (var c in contrib.OrderByDescending(c => Math.Abs(c.val)))
            {
                Say(F("  {0,-28}  weight*x_f = {1,8:+0.00;-0.00}   ({2,5:F1}% of total magnitude)",
                    c.label, c.val, 100.0 * Math.Abs(c.val) / totalC));
                R.Contributions.Add(new Results.ContribRow
                { Label = c.label, WeightedXf = c.val, PercentOfTotal = 100.0 * Math.Abs(c.val) / totalC });
            }

            // ---- outputs -----------------------------------------------------------
            // The User Analysis renders into its own OpticStudio window and has no
            // business scattering report files beside the lens, so it sets NoFiles.
            if (Opts.NoFiles) return;

            string prefix = Opts.OutPrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                string stem = string.IsNullOrEmpty(src)
                    ? "athermal" : Path.GetFileNameWithoutExtension(src) + "_athermal";

                // Default is beside the lens, which for a stock sample means writing
                // into the vendor's own Samples tree. The settings window can name a
                // folder instead; create it if it does not exist, and fall back rather
                // than lose a completed sweep to an unwritable path.
                string dir = null;
                if (!string.IsNullOrWhiteSpace(Opts.OutDir))
                {
                    try
                    {
                        Directory.CreateDirectory(Opts.OutDir);
                        dir = Opts.OutDir;
                    }
                    catch (Exception ex)
                    {
                        Say("WARNING: could not use the chosen output folder '" + Opts.OutDir + "' (" +
                            ex.Message + "). Writing beside the lens instead.");
                    }
                }
                if (dir == null)
                    dir = string.IsNullOrEmpty(src) ? app.ZemaxDataDir : Path.GetDirectoryName(src);

                prefix = Path.Combine(dir, stem);
            }
            else if (!string.IsNullOrWhiteSpace(Opts.OutDir) && string.IsNullOrEmpty(Path.GetDirectoryName(prefix)))
            {
                // Honour -outdir even when -out already set a directory-less prefix
                // (issue #1). A full-path -out stays authoritative.
                try
                {
                    Directory.CreateDirectory(Opts.OutDir);
                    prefix = Path.Combine(Opts.OutDir, prefix);
                }
                catch (Exception ex)
                {
                    Say("WARNING: could not use the chosen output folder '" + Opts.OutDir + "' (" +
                        ex.Message + "). Writing to the given -out prefix instead.");
                }
            }
            if (string.IsNullOrEmpty(R.LensFile)) R.LensFile = Opts.FilePath ?? "";
            File.WriteAllLines(prefix + "_report.txt", Report);
            Chart(temps, focusShift, rmsFixed, rmsRefoc, dofMm, prefix + "_chart.png",
                Path.GetFileName(sys.SystemFile ?? ""));
            // The HTML report is the one meant to be read - the chart is inline SVG, so
            // it is a single file that scales and prints. The CSV and JSON are for
            // diffing runs against each other, which the text transcript cannot support.
            try { Reports.WriteHtml(prefix + "_report.html", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the HTML report: " + ex.Message); }
            try { Reports.WriteCsv(prefix + "_sweep.csv", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the CSV: " + ex.Message); }
            try { Reports.WriteJson(prefix + "_summary.json", R); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not write the JSON summary: " + ex.Message); }
            Console.WriteLine();
            Console.WriteLine("Report written to: " + Path.GetFullPath(prefix + "_report.html"));
            Console.WriteLine("             and: " + Path.GetFullPath(prefix + "_report.txt"));
            Console.WriteLine("Sweep  written to: " + Path.GetFullPath(prefix + "_sweep.csv"));
            Console.WriteLine("Summary written to: " + Path.GetFullPath(prefix + "_summary.json"));
            Console.WriteLine("Chart  written to: " + Path.GetFullPath(prefix + "_chart.png"));
            // The progress line is the only text that survives in the GUI after a ribbon
            // run, so it names the file actually worth opening.
            app.ProgressMessage = F("Done. dz/dT = {0:+0.000000;-0.000000}/C, athermal +/-{1:F1} C, {2} - report: {3} (+ .txt, .csv, .json, .png)",
                slope, dtAthermal, Convention(pStart), Path.GetFileName(prefix + "_report.html"));
            // Only the HTML is opened: it already contains the chart, so opening the PNG
            // as well would just put a second window in front of the user.
            LaunchLog("wrote " + prefix + "_report.html (+ .txt, .csv, .json, .png)");
            OpenOutputs(app, prefix + "_report.html");
        }

        // Which index reference the reported numbers are in. OpticStudio always traces
        // RELATIVE index - air at the system temperature and pressure is exactly 1.0 at
        // all wavelengths - so the system pressure alone decides this (manual 2.1.1.4.2).
    }
}

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
    // Athermal Scan — a ZOS-API User Extension.
    //
    // One-command passive athermalization analysis for sequential systems,
    // replacing the manual TEMP/PRES multi-configuration workflow (community
    // threads "athermal design", "how to model a system with groups under
    // different temperatures and pressures").
    //
    // For each temperature in the sweep the extension applies OpticStudio's own
    // thermal model transiently: refractive indices adjust through the system
    // environment (Adjust Index To Environment), and radii, thicknesses and
    // polynomial asphere terms expand as (1 + a.dT) using the glass catalog TCE
    // for glass rows and the LDE TCE column (mount material) for air gaps -
    // the same physics the Make Thermal tool encodes in pickup solves. The
    // original prescription and environment are snapshot and fully restored.
    //
    // Reported:
    //  * focus shift, EFFL, RMS spot (fixed plane and refocused) vs temperature
    //  * diffraction depth of focus (+/- 2 lambda N^2) and the passive athermal
    //    temperature range at a fixed image plane
    //  * required housing CTE (dz/dT over the mount track), nearest housing
    //    materials with their residual defocus rates and usable temperature
    //    ranges, and an exact two-metal (bimetallic) length solution
    //  * per-glass opto-thermal table: n, dn/dT (measured numerically from the
    //    live model), catalog TCE, thermal glass constant
    //    x_f = dn/dT/(n-1) - alpha, and an approximate thin-element share of
    //    the total thermal defocus
    //  * a two-panel PNG chart (focus shift with DOF band; RMS vs T)
    //
    // Usage:
    //   (no args)      analyze the system open in OpticStudio (extension mode)
    //   -tmin C        sweep start in Celsius (default -20)
    //   -tmax C        sweep end (default +60)
    //   -steps N       sweep points (default 9)
    //   -track L       housing/mount length in lens units (default: total track)
    //   -out <prefix>  output prefix for report/chart (default <lens>_athermal)
    //   -file <path>   standalone mode: load the file first
    class Options
    {
        public double TMin = -20, TMax = 60;
        public int Steps = 9;
        public double Track = 0;
        public string OutPrefix = null;
        public string FilePath = null;
    }

    class RowSnap
    {
        public double Radius, Thickness;
        public double[] Pars = new double[9];
        public ZOSAPI.Editors.LDE.SurfaceType Type;
        public string Material = "";
        public double MountTce;      // LDE TCE column value, in 1e-6/K
        public double AlphaRadius;   // effective expansion coeff for the radius
        public double AlphaThick;    // effective expansion coeff for the gap
        public bool IsGlass;
    }

    class Program
    {
        static Options Opts = new Options();
        static readonly List<string> Report = new List<string>();

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
                    case "tmin": if (i + 1 < args.Length) double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out Opts.TMin); break;
                    case "tmax": if (i + 1 < args.Length) double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out Opts.TMax); break;
                    case "steps": if (i + 1 < args.Length) int.TryParse(args[++i], out Opts.Steps); break;
                    case "track": if (i + 1 < args.Length) double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out Opts.Track); break;
                    case "out": if (i + 1 < args.Length) Opts.OutPrefix = args[++i]; break;
                    case "file": if (i + 1 < args.Length) Opts.FilePath = args[++i]; break;
                }
            }
            if (Opts.Steps < 3) Opts.Steps = 3;
        }

        static void Say(string s) { Console.WriteLine(s); Report.Add(s); }
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

            try { Analyze(app); }
            finally
            {
                if (standalone) app.CloseApplication();
                else { app.ProgressPercent = 100; app.ProgressMessage = "Athermal scan complete."; }
            }
        }

        static double Op(ZOSAPI.IOpticalSystem sys, ZOSAPI.Editors.MFE.MeritOperandType t,
            int p1, int p2, double h1 = 0, double h2 = 0, double p3 = 0, double p4 = 0)
            => sys.MFE.GetOperandValue(t, p1, p2, h1, h2, p3, p4, 0, 0);

        static void Analyze(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
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

            // ---- baseline state -------------------------------------------------
            double t0 = env.Temperature, p0 = env.Pressure;
            bool adjust0 = env.AdjustIndexToEnvironment;
            env.AdjustIndexToEnvironment = true;

            Say("=== Athermal Scan ===");
            Say("Lens file : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(F("Design temperature: {0:F1} C, pressure {1:F2} atm  (sweep {2:F0}..{3:F0} C, {4} steps)",
                t0, p0, Opts.TMin, Opts.TMax, Opts.Steps));

            // ---- snapshot prescription + effective TCE per row ------------------
            var snaps = new RowSnap[imgIdx];
            var glassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new RowSnap { Type = row.Type };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                s.Thickness = row.Thickness;
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

            // ---- glass TCE from the materials catalog ---------------------------
            var glassTce = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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
                    }
                }
            }
            finally { matTool.Close(); }
            foreach (string g in glassNames)
                if (!glassTce.ContainsKey(g))
                {
                    Say("WARNING: TCE for glass '" + g + "' not found in the catalogs in use; assuming 0.");
                    glassTce[g] = 0;
                }

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

            // ---- baseline metrics ----------------------------------------------
            double efl0 = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
            double wfno = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.WFNO, 0, primaryWave);
            double totr = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.TOTR, 0, 0);
            double track = Opts.Track > 0 ? Opts.Track : totr;
            double dofMm = 2.0 * (lambdaUm * 1e-3) * wfno * wfno; // +/- 2 lambda N^2, lens units (mm assumed)
            Say(F("EFFL {0:F4}, working F/# {1:F3}, total track {2:F3}, mount track L = {3:F3}", efl0, wfno, track == totr ? totr : track, track));
            Say(F("Diffraction depth of focus: +/- {0:F4} lens units  (2*lambda*N^2, lambda={1:F4} um)", dofMm, lambdaUm));

            // ---- the sweep -------------------------------------------------------
            int n = Opts.Steps;
            var temps = new double[n];
            var focusShift = new double[n];
            var rmsFixed = new double[n];
            var rmsRefoc = new double[n];
            var efl = new double[n];
            // per-glass index at extremes for dn/dT (surface of first occurrence)
            var glassSurf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < imgIdx; i++)
                if (snaps[i - 1].IsGlass && !glassSurf.ContainsKey(snaps[i - 1].Material))
                    glassSurf[snaps[i - 1].Material] = i;
            var indexAtMin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var indexAtMax = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            double focus0 = double.NaN;
            for (int k = 0; k < n; k++)
            {
                double T = Opts.TMin + (Opts.TMax - Opts.TMin) * k / (n - 1);
                temps[k] = T;
                app.ProgressMessage = F("Evaluating T = {0:F1} C...", T);
                app.ProgressPercent = 10 + 70 * k / n;
                ApplyTemperature(sys, snaps, imgIdx, T - t0);
                env.Temperature = T;

                efl[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
                double focus = MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness * (1 + snaps[imgIdx - 2].AlphaThick * 1e-6 * (T - t0)));
                if (k == 0) { /* filled below relative to design temp */ }
                focusShift[k] = focus;
                rmsFixed[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;

                // refocused RMS: move the image plane to the marginal focus
                var lastRow = lde.GetSurfaceAt(imgIdx - 1);
                double scaledLast = lastRow.Thickness;
                lastRow.Thickness = focus;
                rmsRefoc[k] = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 6, 0) * 1000.0;
                lastRow.Thickness = scaledLast;

                foreach (var kv in glassSurf)
                {
                    double idx = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.INDX, kv.Value, primaryWave);
                    if (k == 0) indexAtMin[kv.Key] = idx;
                    if (k == n - 1) indexAtMax[kv.Key] = idx;
                }
            }

            // focus at the design temperature (interpolate the sweep base):
            ApplyTemperature(sys, snaps, imgIdx, 0);
            env.Temperature = t0;
            focus0 = MarginalFocus(sys, imgIdx, primaryWave, snaps[imgIdx - 2].Thickness);
            for (int k = 0; k < n; k++) focusShift[k] -= focus0;

            // ---- restore ---------------------------------------------------------
            env.Temperature = t0; env.Pressure = p0;
            env.AdjustIndexToEnvironment = adjust0;
            ApplyTemperature(sys, snaps, imgIdx, 0);
            double eflCheck = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
            Say(F("Restoration check: EFFL back to {0:G9} (baseline {1:G9}) -> {2}",
                eflCheck, efl0, Math.Abs(eflCheck - efl0) < 1e-6 ? "OK" : "MISMATCH - check the system!"));

            // ---- sweep table ------------------------------------------------------
            Say("");
            Say("  T (C)    EFFL        focus shift    RMS fixed    RMS refocused");
            Say("  -----    ---------   -----------    ---------    -------------");
            for (int k = 0; k < n; k++)
                Say(F("  {0,6:F1}   {1,9:F4}   {2,11:+0.00000;-0.00000}    {3,7:F2} um   {4,7:F2} um",
                    temps[k], efl[k], focusShift[k], rmsFixed[k], rmsRefoc[k]));

            // ---- athermal analysis ------------------------------------------------
            double slope = LinFit(temps, focusShift); // dz/dT, lens units per C
            Say("");
            Say(F("Thermal defocus rate dz/dT = {0:+0.000000;-0.000000} lens units / C", slope));
            double dtAthermal = Math.Abs(slope) > 1e-12 ? dofMm / Math.Abs(slope) : double.PositiveInfinity;
            Say(F("Fixed-plane athermal range: +/- {0:F1} C about the design temperature (defocus within the DOF)",
                dtAthermal));

            double alphaReq = slope / track * 1e6; // required housing CTE in 1e-6/K
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
            Say("  glass         n(T0)     dn/dT(1e-6/K)  TCE(1e-6/K)  x_f = dn/dT/(n-1) - a  (1e-6/K)");
            var xf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in glassSurf.Keys)
            {
                double nMin = indexAtMin[g], nMax = indexAtMax[g];
                double dndt = (nMax - nMin) / (Opts.TMax - Opts.TMin) * 1e6;
                double nT0 = nMin + (nMax - nMin) * (t0 - Opts.TMin) / (Opts.TMax - Opts.TMin);
                double x = dndt / (nT0 - 1) - glassTce[g];
                xf[g] = x;
                Say(F("  {0,-12}  {1,7:F5}   {2,10:F2}     {3,8:F2}     {4,10:+0.00;-0.00}", g, nT0, dndt, glassTce[g], x));
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
                Say(F("  {0,-28}  weight*x_f = {1,8:+0.00;-0.00}   ({2,5:F1}% of total magnitude)",
                    c.label, c.val, 100.0 * Math.Abs(c.val) / totalC));

            // ---- outputs -----------------------------------------------------------
            string prefix = Opts.OutPrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                prefix = string.IsNullOrEmpty(src)
                    ? Path.Combine(app.ZemaxDataDir, "athermal")
                    : Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_athermal");
            }
            File.WriteAllLines(prefix + "_report.txt", Report);
            Chart(temps, focusShift, rmsFixed, rmsRefoc, dofMm, prefix + "_chart.png",
                Path.GetFileName(sys.SystemFile ?? ""));
            Console.WriteLine();
            Console.WriteLine("Report written to: " + prefix + "_report.txt");
            Console.WriteLine("Chart  written to: " + prefix + "_chart.png");
        }

        // apply the thermal model relative to the snapshot (dT = 0 restores)
        static void ApplyTemperature(ZOSAPI.IOpticalSystem sys, RowSnap[] snaps, int imgIdx, double dT)
        {
            var lde = sys.LDE;
            for (int i = 1; i < imgIdx; i++)
            {
                var s = snaps[i - 1];
                var row = lde.GetSurfaceAt(i);
                double eT = 1 + s.AlphaThick * 1e-6 * dT;
                double eR = 1 + s.AlphaRadius * 1e-6 * dT;
                row.Thickness = s.Thickness * eT;
                if (s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                {
                    if (!(Math.Abs(s.Radius) > 1e10 || s.Radius == 0))
                        try { row.Radius = s.Radius * eR; } catch { }
                    if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ||
                        s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere)
                    {
                        for (int p = 1; p <= 8; p++)
                        {
                            if (s.Pars[p] == 0) continue;
                            int powr = s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ? 2 * p : p;
                            try
                            {
                                var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                                row.GetSurfaceCell(col).DoubleValue = s.Pars[p] * Math.Pow(eR, 1 - powr);
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        // image-space marginal focus position measured from the last optical surface
        static double MarginalFocus(ZOSAPI.IOpticalSystem sys, int imgIdx, int wave, double lastGap)
        {
            double y = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAY, imgIdx, wave, 0, 0, 0, 1);
            double m = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAB, imgIdx, wave, 0, 0, 0, 1);
            double nz = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAC, imgIdx, wave, 0, 0, 0, 1);
            double u = Math.Abs(nz) > 1e-14 ? m / nz : 0;
            if (Math.Abs(u) < 1e-14) return lastGap;
            return lastGap - y / u;
        }

        static double LinFit(double[] x, double[] y)
        {
            int n = x.Length;
            double sx = x.Sum(), sy = y.Sum(), sxx = x.Sum(v => v * v), sxy = 0;
            for (int i = 0; i < n; i++) sxy += x[i] * y[i];
            return (n * sxy - sx * sy) / (n * sxx - sx * sx);
        }

        static void Chart(double[] t, double[] dz, double[] rmsF, double[] rmsR,
            double dof, string path, string title)
        {
            int W = 1200, H = 800;
            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                var font = new Font("Segoe UI", 10f);
                var fontB = new Font("Segoe UI", 12f, FontStyle.Bold);
                var black = new SolidBrush(Color.Black);

                g.DrawString("Athermal scan - " + title, fontB, black, 20, 8);
                Panel(g, font, 60, 50, W - 100, 320, t, new[] { (dz, Color.FromArgb(0, 90, 200), "focus shift") },
                    "focus shift (lens units)", dof);
                Panel(g, font, 60, 430, W - 100, 320, t,
                    new[] { (rmsF, Color.FromArgb(200, 30, 30), "RMS @ fixed plane"),
                            (rmsR, Color.FromArgb(0, 140, 0), "RMS refocused") },
                    "RMS spot (um)", 0);
                g.DrawString("temperature (C)", font, black, W / 2 - 40, H - 28);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Panel(Graphics g, Font font, int x, int y, int w, int h, double[] t,
            (double[] data, Color color, string label)[] series, string yLabel, double dofBand)
        {
            double xmin = t.Min(), xmax = t.Max();
            double ymin = series.SelectMany(s => s.data).Min();
            double ymax = series.SelectMany(s => s.data).Max();
            if (dofBand > 0) { ymin = Math.Min(ymin, -dofBand * 1.2); ymax = Math.Max(ymax, dofBand * 1.2); }
            if (ymax - ymin < 1e-12) { ymax += 1; ymin -= 1; }
            double pad = 0.08 * (ymax - ymin); ymin -= pad; ymax += pad;
            float PX(double v) => (float)(x + (v - xmin) / (xmax - xmin) * w);
            float PY(double v) => (float)(y + h - (v - ymin) / (ymax - ymin) * h);

            if (dofBand > 0)
                using (var band = new SolidBrush(Color.FromArgb(40, 0, 180, 0)))
                    g.FillRectangle(band, x, PY(dofBand), w, PY(-dofBand) - PY(dofBand));

            using (var axis = new Pen(Color.Black, 1.5f))
            using (var grid = new Pen(Color.FromArgb(230, 230, 230), 1f))
            using (var black = new SolidBrush(Color.Black))
            {
                for (int k = 0; k <= 4; k++)
                {
                    double tv = xmin + (xmax - xmin) * k / 4;
                    g.DrawLine(grid, PX(tv), y, PX(tv), y + h);
                    g.DrawString(tv.ToString("F0"), font, black, PX(tv) - 10, y + h + 4);
                    double yv = ymin + (ymax - ymin) * k / 4;
                    g.DrawLine(grid, x, PY(yv), x + w, PY(yv));
                    g.DrawString(yv.ToString("G3"), font, black, 4, PY(yv) - 8);
                }
                if (ymin < 0 && ymax > 0)
                    using (var zero = new Pen(Color.Gray, 1f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(zero, x, PY(0), x + w, PY(0));
                g.DrawRectangle(axis, x, y, w, h);
                g.DrawString(yLabel, font, black, x, y - 20);

                int lx = x + w - 190, ly = y + 8;
                foreach (var s in series)
                {
                    using (var pen = new Pen(s.color, 2.2f))
                    {
                        var pts = new PointF[t.Length];
                        for (int i = 0; i < t.Length; i++) pts[i] = new PointF(PX(t[i]), PY(s.data[i]));
                        g.DrawLines(pen, pts);
                        foreach (var p in pts) g.FillEllipse(new SolidBrush(s.color), p.X - 3, p.Y - 3, 6, 6);
                        g.DrawLine(pen, lx, ly + 7, lx + 24, ly + 7);
                    }
                    g.DrawString(s.label, font, new SolidBrush(s.color), lx + 28, ly);
                    ly += 18;
                }
            }
        }
    }
}

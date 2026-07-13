using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LayoutRender
{
    // Layout Render — a ZOS-API User Extension.
    //
    // Exports a 2D (Y-Z) layout of the loaded sequential system to a PNG image,
    // entirely headlessly. The ZOS-API provides no way to save layout windows
    // as images (see community threads "Feature Request: Layout Window Exports"
    // and "How do I output the image of an analysis in ZOS-API?" — the only
    // workaround, ZPL EXPORTJPG, does not work in standalone applications).
    // This extension rebuilds the drawing from first principles instead:
    // surface cross-sections are sampled from the sag equations in local
    // coordinates and mapped to global coordinates via GetGlobalMatrix, lens
    // edges are closed over glass gaps, and ray fans are traced with the batch
    // ray tracer (one field per colour, terminated where a ray fails).
    //
    // Usage:
    //   (no args)         render the system open in OpticStudio (extension mode)
    //                     to <lensfile>_layout.png
    //   -out <path.png>   explicit output path
    //   -rays N           rays per fan (default 7)
    //   -width W -height H  image size in pixels (default 1400 x 900)
    //   -file <path>      standalone mode: load <path> and render it
    class Options
    {
        public string FilePath = null;
        public string OutPath = null;
        public int Rays = 7;
        public int Width = 1400;
        public int Height = 900;
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
                    case "out": if (i + 1 < args.Length) Opts.OutPath = args[++i]; break;
                    case "rays": if (i + 1 < args.Length) Opts.Rays = ParseInt(args[++i], Opts.Rays); break;
                    case "width": if (i + 1 < args.Length) Opts.Width = ParseInt(args[++i], Opts.Width); break;
                    case "height": if (i + 1 < args.Length) Opts.Height = ParseInt(args[++i], Opts.Height); break;
                }
            }
            if (Opts.Rays < 2) Opts.Rays = 2;
            if (Opts.Width < 200) Opts.Width = 200;
            if (Opts.Height < 200) Opts.Height = 200;
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

            try
            {
                RenderSystem(app);
            }
            finally
            {
                if (standalone) app.CloseApplication();
                else { app.ProgressPercent = 100; app.ProgressMessage = "Layout rendered."; }
            }
        }

        // rigid transform of one surface's local frame into global coordinates
        class Frame
        {
            public double[,] R = new double[3, 3];
            public double X, Y, Z;
            public bool Valid;
            public (double gx, double gy, double gz) ToGlobal(double x, double y, double z) =>
                (R[0, 0] * x + R[0, 1] * y + R[0, 2] * z + X,
                 R[1, 0] * x + R[1, 1] * y + R[1, 2] * z + Y,
                 R[2, 0] * x + R[2, 1] * y + R[2, 2] * z + Z);
        }

        class SurfInfo
        {
            public int Index;
            public ZOSAPI.Editors.LDE.SurfaceType Type;
            public double Radius, Conic, SemiDiameter;
            public double[] Pars = new double[9];
            public string EffMedium = "";   // medium AFTER this surface
            public Frame Frame;
            public List<PointF> Section;    // projected (z,y) polyline, model units
        }

        static void RenderSystem(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("this extension requires a sequential system");

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;
            Console.WriteLine("Rendering: " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));

            // ---- gather surface data -------------------------------------------
            var surfs = new List<SurfInfo>();
            string prevMedium = "";
            for (int i = 1; i <= imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new SurfInfo { Index = i, Type = row.Type };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                try { s.Conic = row.Conic; } catch { s.Conic = 0; }
                if (Math.Abs(s.Conic) > 1e10) s.Conic = 0;
                try { s.SemiDiameter = row.SemiDiameter; } catch { s.SemiDiameter = 0; }
                for (int p = 1; p <= 8; p++)
                {
                    try
                    {
                        var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                        s.Pars[p] = row.GetSurfaceCell(col).DoubleValue;
                    }
                    catch { s.Pars[p] = 0; }
                }
                string mat = (row.Material ?? "").Trim();
                if (mat == "-" || (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak && mat.Length == 0))
                    s.EffMedium = prevMedium;
                else
                    s.EffMedium = mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase) ? "" : mat;
                prevMedium = s.EffMedium;

                s.Frame = GetFrame(lde, i);
                surfs.Add(s);
            }

            // ---- surface cross-sections (local sag -> global -> Z/Y plane) -----
            // decentered apertures (off-axis mirror sections etc.) shift and size
            // the drawn section so elements appear where the light actually is
            foreach (var s in surfs)
            {
                if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                if (!s.Frame.Valid) continue;
                double yCen = 0, yHalf = s.SemiDiameter;
                try
                {
                    var ad = lde.GetSurfaceAt(s.Index).ApertureData;
                    var st = ad.CurrentTypeSettings;
                    switch (ad.CurrentType)
                    {
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularAperture:
                            var r = (ZOSAPI.Editors.LDE.ISurfaceApertureRectangular)st;
                            yCen = r.ApertureYDecenter; yHalf = r.YHalfWidth; break;
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                            var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                            yCen = c.ApertureYDecenter; yHalf = c.MaximumRadius; break;
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                            var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                            yCen = e.ApertureYDecenter; yHalf = e.YHalfWidth; break;
                    }
                }
                catch { }
                if (yHalf < 1e-9) continue;
                var pts = new List<PointF>();
                int nSteps = 40;
                for (int k = 0; k <= nSteps; k++)
                {
                    double y = yCen - yHalf + 2.0 * yHalf * k / nSteps;
                    double z = Sag(s, y);
                    if (double.IsNaN(z)) continue;
                    var g = s.Frame.ToGlobal(0, y, z);
                    pts.Add(new PointF((float)g.gz, (float)g.gy));
                }
                if (pts.Count >= 2) s.Section = pts;
            }

            // ---- ray fans -------------------------------------------------------
            var fields = sys.SystemData.Fields;
            int nf = fields.NumberOfFields;
            double maxR = 1e-10;
            for (int i = 1; i <= nf; i++)
            {
                var f = fields.GetField(i);
                maxR = Math.Max(maxR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }
            int wave = 1;
            try
            {
                var wls = sys.SystemData.Wavelengths;
                for (int w = 1; w <= wls.NumberOfWavelengths; w++)
                    if (wls.GetWavelength(w).IsPrimary) { wave = w; break; }
            }
            catch { }

            int raysPerFan = Opts.Rays;
            int nRays = nf * raysPerFan;
            // rayPts[field][ray] = list of global (z,y) points; rayOk tracks failure
            var rayPts = new List<PointF>[nf, raysPerFan];
            var rayAlive = new bool[nf, raysPerFan];
            for (int fi = 0; fi < nf; fi++)
                for (int r = 0; r < raysPerFan; r++) { rayPts[fi, r] = new List<PointF>(); rayAlive[fi, r] = true; }

            for (int surf = 1; surf <= imgIdx; surf++)
            {
                var frame = surfs[surf - 1].Frame;
                var trace = sys.Tools.OpenBatchRayTrace();
                try
                {
                    var data = trace.CreateNormUnpol(nRays, ZOSAPI.Tools.RayTrace.RaysType.Real, surf);
                    for (int fi = 0; fi < nf; fi++)
                    {
                        var f = fields.GetField(fi + 1);
                        double hx = f.X / maxR, hy = f.Y / maxR;
                        for (int r = 0; r < raysPerFan; r++)
                        {
                            double py = raysPerFan == 1 ? 0 : -1.0 + 2.0 * r / (raysPerFan - 1);
                            data.AddRay(wave, hx, hy, 0, py, ZOSAPI.Tools.RayTrace.OPDMode.None);
                        }
                    }
                    trace.RunAndWaitForCompletion();
                    data.StartReadingResults();
                    int rayNum, errCode, vigCode;
                    double x, y, z, l, m, n, l2, m2, n2, opd, inten;
                    int idx = 0;
                    while (data.ReadNextResult(out rayNum, out errCode, out vigCode,
                        out x, out y, out z, out l, out m, out n, out l2, out m2, out n2, out opd, out inten))
                    {
                        int fi = idx / raysPerFan, r = idx % raysPerFan;
                        idx++;
                        if (fi >= nf) break;
                        if (!rayAlive[fi, r]) continue;
                        if (errCode != 0) { rayAlive[fi, r] = false; continue; }
                        if (frame.Valid)
                        {
                            var g = frame.ToGlobal(x, y, z);
                            rayPts[fi, r].Add(new PointF((float)g.gz, (float)g.gy));
                        }
                    }
                }
                finally { trace.Close(); }
            }

            // ---- assemble drawing primitives -----------------------------------
            var surfLines = new List<List<PointF>>();
            var edgeLines = new List<List<PointF>>();
            foreach (var s in surfs)
                if (s.Section != null) surfLines.Add(s.Section);
            // close lens elements over glass gaps
            for (int i = 0; i < surfs.Count - 1; i++)
            {
                var a = surfs[i];
                if (string.IsNullOrEmpty(a.EffMedium) || a.Section == null) continue;
                // find the next drawn surface (skips CBs inside the glass)
                for (int j = i + 1; j < surfs.Count; j++)
                {
                    if (surfs[j].Section == null) continue;
                    edgeLines.Add(new List<PointF> { a.Section.First(), surfs[j].Section.First() });
                    edgeLines.Add(new List<PointF> { a.Section.Last(), surfs[j].Section.Last() });
                    break;
                }
            }

            var fans = new List<(List<PointF> pts, int field)>();
            for (int fi = 0; fi < nf; fi++)
                for (int r = 0; r < raysPerFan; r++)
                    if (rayPts[fi, r].Count >= 2) fans.Add((rayPts[fi, r], fi));

            // ---- auto-orient: rotate so the dominant beam direction is horizontal.
            // Reversed/folded systems anchor global coordinates to a possibly
            // rotated first surface, which would otherwise draw the whole train
            // diagonally. PCA over the ray points finds the principal axis.
            var all = fans.SelectMany(f => f.pts).ToList();
            if (all.Count > 4)
            {
                double mz = all.Average(p => p.X), my = all.Average(p => p.Y);
                double szz = 0, syy = 0, szy = 0;
                foreach (var p in all)
                {
                    szz += (p.X - mz) * (p.X - mz);
                    syy += (p.Y - my) * (p.Y - my);
                    szy += (p.X - mz) * (p.Y - my);
                }
                double ang = 0.5 * Math.Atan2(2 * szy, szz - syy);
                if (Math.Abs(ang) > 0.02)
                {
                    float ca = (float)Math.Cos(-ang), sa = (float)Math.Sin(-ang);
                    PointF Rot(PointF p) => new PointF(
                        (float)(mz + (p.X - mz) * ca - (p.Y - my) * sa),
                        (float)(my + (p.X - mz) * sa + (p.Y - my) * ca));
                    foreach (var line in fans.Select(f => f.pts))
                        for (int i = 0; i < line.Count; i++) line[i] = Rot(line[i]);
                    // rotating Section also covers surfLines (same list objects);
                    // edgeLines hold value copies and need their own pass
                    foreach (var s in surfs)
                        if (s.Section != null)
                            for (int i = 0; i < s.Section.Count; i++) s.Section[i] = Rot(s.Section[i]);
                    foreach (var line in edgeLines)
                        for (int i = 0; i < line.Count; i++) line[i] = Rot(line[i]);
                    Console.WriteLine(F("Auto-oriented layout by {0:F1} degrees to level the beam axis.", ang * 180 / Math.PI));
                }
            }

            // ---- render to PNG --------------------------------------------------
            string outPath = Opts.OutPath;
            if (string.IsNullOrEmpty(outPath))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                outPath = string.IsNullOrEmpty(src)
                    ? Path.Combine(app.ZemaxDataDir, "layout.png")
                    : Path.Combine(Path.GetDirectoryName(src), Path.GetFileNameWithoutExtension(src) + "_layout.png");
            }
            string title = Path.GetFileName(string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile);
            Draw(surfLines, edgeLines, fans, outPath, title);
            Console.WriteLine("Layout written to: " + outPath);
        }

        static Frame GetFrame(ZOSAPI.Editors.LDE.ILensDataEditor lde, int surf)
        {
            var fr = new Frame();
            double r11, r12, r13, r21, r22, r23, r31, r32, r33, x, y, z;
            try
            {
                fr.Valid = lde.GetGlobalMatrix(surf, out r11, out r12, out r13, out r21, out r22, out r23,
                    out r31, out r32, out r33, out x, out y, out z);
                fr.R[0, 0] = r11; fr.R[0, 1] = r12; fr.R[0, 2] = r13;
                fr.R[1, 0] = r21; fr.R[1, 1] = r22; fr.R[1, 2] = r23;
                fr.R[2, 0] = r31; fr.R[2, 1] = r32; fr.R[2, 2] = r33;
                fr.X = x; fr.Y = y; fr.Z = z;
            }
            catch { fr.Valid = false; }
            return fr;
        }

        static double Sag(SurfInfo s, double y)
        {
            double z = 0;
            switch (s.Type)
            {
                case ZOSAPI.Editors.LDE.SurfaceType.Tilted:
                    return y * s.Pars[2]; // y tangent
                case ZOSAPI.Editors.LDE.SurfaceType.Paraxial:
                case ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY:
                    return 0;
            }
            if (Math.Abs(s.Radius) > 1e10 || s.Radius == 0)
                z = 0;
            else
            {
                double c = 1.0 / s.Radius;
                double disc = 1 - (1 + s.Conic) * c * c * y * y;
                if (disc < 0) return double.NaN;
                z = c * y * y / (1 + Math.Sqrt(disc));
            }
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric)
            {
                double y2 = y * y, term = y2;
                for (int p = 1; p <= 8; p++) { z += s.Pars[p] * term; term *= y2; }
            }
            else if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere)
            {
                double ay = Math.Abs(y), term = ay;
                for (int p = 1; p <= 8; p++) { z += s.Pars[p] * term; term *= ay; }
            }
            return z;
        }

        static readonly Color[] FieldColors = new[]
        {
            Color.FromArgb(0, 0, 220), Color.FromArgb(0, 160, 0), Color.FromArgb(220, 0, 0),
            Color.FromArgb(0, 170, 170), Color.FromArgb(190, 0, 190), Color.FromArgb(180, 150, 0),
            Color.FromArgb(90, 90, 90), Color.FromArgb(255, 128, 0),
        };

        static void Draw(List<List<PointF>> surfLines, List<List<PointF>> edgeLines,
            List<(List<PointF> pts, int field)> fans, string outPath, string title)
        {
            // model bounds (drawing plane: X = global Z, Y = global Y)
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var line in surfLines.Concat(edgeLines).Concat(fans.Select(f => f.pts)))
                foreach (var p in line)
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            if (minX >= maxX) { minX -= 1; maxX += 1; }
            if (minY >= maxY) { minY -= 1; maxY += 1; }

            int W = Opts.Width, H = Opts.Height, margin = 60, footer = 50;
            float scale = Math.Min((W - 2f * margin) / (maxX - minX), (H - 2f * margin - footer) / (maxY - minY));
            float ox = margin - minX * scale + (W - 2f * margin - (maxX - minX) * scale) / 2f;
            float oy = H - margin - footer + minY * scale + (H - 2f * margin - footer - (maxY - minY) * scale) / -2f;
            PointF Map(PointF p) => new PointF(ox + p.X * scale, oy - p.Y * scale);

            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

                using (var rayPenBase = new Pen(Color.Black, 1f))
                {
                    foreach (var (pts, field) in fans)
                    {
                        using (var pen = new Pen(FieldColors[field % FieldColors.Length], 1f))
                            if (pts.Count >= 2) g.DrawLines(pen, pts.Select(Map).ToArray());
                    }
                }
                using (var sPen = new Pen(Color.Black, 1.8f))
                {
                    foreach (var line in surfLines)
                        if (line.Count >= 2) g.DrawLines(sPen, line.Select(Map).ToArray());
                    foreach (var line in edgeLines)
                        if (line.Count >= 2) g.DrawLines(sPen, line.Select(Map).ToArray());
                }

                // scale bar: a round number close to 20% of the span
                double span = maxX - minX;
                double bar = Math.Pow(10, Math.Floor(Math.Log10(span * 0.25)));
                if (span * 0.25 / bar >= 5) bar *= 5;
                else if (span * 0.25 / bar >= 2) bar *= 2;
                float bx0 = margin, by = H - footer + 8;
                using (var pen = new Pen(Color.Black, 2f))
                using (var font = new Font("Segoe UI", 11f))
                using (var brush = new SolidBrush(Color.Black))
                {
                    g.DrawLine(pen, bx0, by, bx0 + (float)(bar * scale), by);
                    g.DrawLine(pen, bx0, by - 4, bx0, by + 4);
                    g.DrawLine(pen, bx0 + (float)(bar * scale), by - 4, bx0 + (float)(bar * scale), by + 4);
                    g.DrawString(F("{0:G4} lens units", bar), font, brush, bx0 + (float)(bar * scale) + 8, by - 10);
                    g.DrawString(title + "  -  Y-Z layout (LayoutRender user extension)", font, brush, margin, H - footer + 24);
                }

                bmp.Save(outPath, ImageFormat.Png);
            }
        }
    }
}

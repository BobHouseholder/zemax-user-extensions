using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RayExtentEnvelope
{
    // ============================================================
    // PngLayout - draw the Y-Z side-view PNG
    // ============================================================
    // Draws glass outlines, the stop, and the ray keep-out envelope
    // as a picture. Includes a simple mm scale bar.
    // ============================================================
    static class PngLayout
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        // Pick a round mm grid step near the rough size.
        static double NiceMmStep(double rough)
        {
            if (rough <= 0 || double.IsNaN(rough) || double.IsInfinity(rough)) return 1;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double f = rough / exp;
            double nice;
            if (f <= 1.0) nice = 1;
            else if (f <= 2.0) nice = 2;
            else if (f <= 5.0) nice = 5;
            else nice = 10;
            return nice * exp;
        }
        // Half-step helper for the scale bar / grid.
        static double HalfNiceMm(double bar)
        {
            if (bar <= 0 || double.IsNaN(bar) || double.IsInfinity(bar)) return 1;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(bar)));
            double f = Math.Round(bar / exp);
            if (f <= 1.0) return 0.5 * exp;
            if (f <= 2.0) return 1.0 * exp;
            return 2.0 * exp;
        }

        // Render lens lines, stop, and envelope into a PNG file.
        public static void Write(
            string path,
            List<(List<PointF> pts, string kind)> lensLines,
            List<List<PointF>> stopLines,
            IList<(double Z, double Rmax)> env,
            int width, int height, string title)
        {
            if (width < 200) width = 200;
            if (height < 200) height = 200;

            var all = new List<PointF>();
            foreach (var pair in lensLines)
                if (pair.pts != null) all.AddRange(pair.pts);
            foreach (var line in stopLines)
                if (line != null) all.AddRange(line);
            foreach (var s in env)
            {
                all.Add(new PointF((float)s.Z, (float)s.Rmax));
                all.Add(new PointF((float)s.Z, (float)(-s.Rmax)));
            }
            if (all.Count < 2)
                throw new Exception("PngLayout: nothing to draw");

            float minX = all.Min(p => p.X), maxX = all.Max(p => p.X);
            float minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            if (maxX - minX < 1e-9f) { minX -= 1; maxX += 1; }
            if (maxY - minY < 1e-9f) { minY -= 1; maxY += 1; }
            float dx = maxX - minX, dy = maxY - minY;
            minX -= 0.06f * dx; maxX += 0.06f * dx;
            minY -= 0.06f * dy; maxY += 0.06f * dy;
            dx = maxX - minX; dy = maxY - minY;

            int margin = 60, footer = 64;
            float scale = Math.Min((width - 2f * margin) / dx, (height - 2f * margin - footer) / dy);
            float ox = margin - minX * scale + (width - 2f * margin - dx * scale) / 2f;
            float oy = height - margin - footer + minY * scale
                + (height - 2f * margin - footer - dy * scale) / -2f;
            PointF Map(PointF p) => new PointF(ox + p.X * scale, oy - p.Y * scale);

            // Preferred scale length from view span (1-2-5), then major grid is one
            // nice notch finer. Scale bar length is then forced equal to that
            // major grid spacing so bar and grid always match (same mm value).
            double span = dx;
            double bar = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(span * 0.25, 1e-12))));
            if (span * 0.25 / bar >= 5) bar *= 5;
            else if (span * 0.25 / bar >= 2) bar *= 2;

            // Grid majors: one nice notch finer than the preferred bar length
            // (e.g. preferred 2 mm -> 1 mm majors). If that would pack lines
            // under ~25 px, step up to a nicer spacing from on-screen density.
            double gridStep = HalfNiceMm(bar);
            if (gridStep * scale < 25.0)
                gridStep = NiceMmStep(25.0 / (double)scale);
            if (gridStep < 1e-6) gridStep = 1;

            // Scale bar length = major grid spacing (label uses same mm value).
            bar = gridStep;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

                Console.WriteLine(string.Format(CI,
                    "PNG grid: {0:G4} mm (scale bar {1:G4} mm)", gridStep, bar));
                using (var gridPen = new Pen(Color.FromArgb(220, 220, 220), 1f))
                {
                    double x0 = Math.Ceiling(minX / gridStep) * gridStep;
                    for (double x = x0; x <= maxX + 1e-9; x += gridStep)
                    {
                        var a = Map(new PointF((float)x, minY));
                        var b = Map(new PointF((float)x, maxY));
                        g.DrawLine(gridPen, a, b);
                    }
                    double y0 = Math.Ceiling(minY / gridStep) * gridStep;
                    for (double y = y0; y <= maxY + 1e-9; y += gridStep)
                    {
                        var a = Map(new PointF(minX, (float)y));
                        var b = Map(new PointF(maxX, (float)y));
                        g.DrawLine(gridPen, a, b);
                    }
                }

                if (env != null && env.Count >= 2)
                {
                    var upper = env.Select(s => Map(new PointF((float)s.Z, (float)s.Rmax))).ToArray();
                    var lower = env.Select(s => Map(new PointF((float)s.Z, (float)(-s.Rmax)))).Reverse().ToArray();
                    var poly = upper.Concat(lower).ToArray();
                    using (var brush = new SolidBrush(Color.FromArgb(40, 220, 80, 40)))
                        g.FillPolygon(brush, poly);
                    using (var pen = new Pen(Color.FromArgb(220, 60, 30), 2.2f))
                    {
                        if (upper.Length >= 2) g.DrawLines(pen, upper);
                        var lowerFwd = env.Select(s => Map(new PointF((float)s.Z, (float)(-s.Rmax)))).ToArray();
                        if (lowerFwd.Length >= 2) g.DrawLines(pen, lowerFwd);
                    }
                }

                using (var lensPen = new Pen(Color.FromArgb(20, 20, 20), 2f))
                {
                    foreach (var pair in lensLines)
                    {
                        if (pair.pts == null || pair.pts.Count < 2) continue;
                        g.DrawLines(lensPen, pair.pts.Select(Map).ToArray());
                    }
                }

                using (var stopPen = new Pen(Color.FromArgb(0, 90, 200), 1.8f))
                {
                    stopPen.DashStyle = DashStyle.Dash;
                    foreach (var line in stopLines)
                    {
                        if (line == null || line.Count < 2) continue;
                        g.DrawLines(stopPen, line.Select(Map).ToArray());
                    }
                }

                using (var axisPen = new Pen(Color.FromArgb(180, 180, 180), 1f))
                {
                    axisPen.DashStyle = DashStyle.Dot;
                    g.DrawLine(axisPen, Map(new PointF(minX, 0)), Map(new PointF(maxX, 0)));
                }

                // Scale bar in mm, kept inside the footer so it is never clipped.
                float bx0 = margin;
                float by = height - footer + 14;
                float barPx = (float)(bar * scale);
                if (bx0 + barPx > width - margin) barPx = Math.Max(20f, width - margin - bx0);
                using (var pen = new Pen(Color.Black, 2f))
                using (var font = new Font("Segoe UI", 11f))
                using (var brush = new SolidBrush(Color.Black))
                using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
                {
                    g.DrawLine(pen, bx0, by, bx0 + barPx, by);
                    g.DrawLine(pen, bx0, by - 5, bx0, by + 5);
                    g.DrawLine(pen, bx0 + barPx, by - 5, bx0 + barPx, by + 5);
                    string barLabel = string.Format(CI, "{0:G4} mm", bar);
                    var barSz = g.MeasureString(barLabel, font);
                    float labelX = bx0 + barPx + 8;
                    if (labelX + barSz.Width > width - 8)
                        labelX = Math.Max(8f, bx0 + barPx - barSz.Width);
                    g.DrawString(barLabel, font, brush, labelX, by - barSz.Height - 2);
                    string hdr = (string.IsNullOrEmpty(title) ? "system" : title)
                        + "  -  max ray-extent envelope (RayExtentEnvelope)";
                    g.DrawString(hdr, font, brush, margin, height - footer + 36);
                    g.DrawString(
                        string.Format(CI,
                            "Black: glass   Blue dash: stop   Red: radial envelope   Light gray: {0:G4} mm grid   Frame: object Z=0",
                            gridStep),
                        font, gray, margin, 12);
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}

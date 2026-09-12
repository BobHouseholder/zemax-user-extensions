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
    // Headless Y-Z PNG: glass outlines, stop, max-ray radial envelope (+/- R vs Z).
    static class PngLayout
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

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

            int margin = 60, footer = 52;
            float scale = Math.Min((width - 2f * margin) / dx, (height - 2f * margin - footer) / dy);
            float ox = margin - minX * scale + (width - 2f * margin - dx * scale) / 2f;
            float oy = height - margin - footer + minY * scale
                + (height - 2f * margin - footer - dy * scale) / -2f;
            PointF Map(PointF p) => new PointF(ox + p.X * scale, oy - p.Y * scale);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

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

                double span = dx;
                double bar = Math.Pow(10, Math.Floor(Math.Log10(span * 0.25)));
                if (span * 0.25 / bar >= 5) bar *= 5;
                else if (span * 0.25 / bar >= 2) bar *= 2;
                float bx0 = margin, by = height - footer + 8;
                using (var pen = new Pen(Color.Black, 2f))
                using (var font = new Font("Segoe UI", 11f))
                using (var brush = new SolidBrush(Color.Black))
                using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
                {
                    g.DrawLine(pen, bx0, by, bx0 + (float)(bar * scale), by);
                    g.DrawLine(pen, bx0, by - 4, bx0, by + 4);
                    g.DrawLine(pen, bx0 + (float)(bar * scale), by - 4, bx0 + (float)(bar * scale), by + 4);
                    g.DrawString(string.Format(CI, "{0:G4} lens units", bar), font, brush,
                        bx0 + (float)(bar * scale) + 8, by - 10);
                    string hdr = (string.IsNullOrEmpty(title) ? "system" : title)
                        + "  -  max ray-extent envelope (RayExtentEnvelope)";
                    g.DrawString(hdr, font, brush, margin, height - footer + 26);
                    g.DrawString("Black: glass   Blue dash: stop   Red: radial envelope", font, gray,
                        margin, 12);
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace FootprintDxf
{
    // Headless PNG preview of the same footprint polylines written to DXF.
    // Pure System.Drawing — no WinForms window. Y-up lens coords → screen Y flip.
    static class PngWriter
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // AutoCAD Color Index 1..7-ish, darkened so they read on white.
        static readonly Color[] AciColors = new[]
        {
            Color.FromArgb(220, 40, 40),   // 1 red
            Color.FromArgb(200, 170, 0),   // 2 yellow
            Color.FromArgb(0, 160, 40),    // 3 green
            Color.FromArgb(0, 160, 170),   // 4 cyan
            Color.FromArgb(40, 80, 220),   // 5 blue
            Color.FromArgb(180, 40, 180),  // 6 magenta
            Color.FromArgb(40, 40, 40),    // 7 black (white on dark CAD)
        };

        const int DefaultW = 1200;
        const int DefaultH = 900;
        const int Pad = 48;
        const int TitleH = 36;
        const int LegendW = 160;

        public static void Write(string path, IList<DxfWriter.LayerPoly> polys, string title)
        {
            Write(path, polys, title, DefaultW, DefaultH);
        }

        public static void Write(string path, IList<DxfWriter.LayerPoly> polys, string title,
            int width, int height)
        {
            if (polys == null || polys.Count == 0)
                throw new Exception("PngWriter: no polylines to draw");
            if (width < 200) width = 200;
            if (height < 200) height = 200;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            int drawable = 0;
            foreach (var p in polys)
            {
                if (p.Vertices == null || p.Vertices.Count < 3) continue;
                drawable++;
                foreach (var v in p.Vertices)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                }
            }
            if (drawable == 0)
                throw new Exception("PngWriter: every polyline had fewer than 3 vertices");

            if (maxX - minX < 1e-12) { minX -= 1; maxX += 1; }
            if (maxY - minY < 1e-12) { minY -= 1; maxY += 1; }

            // Pad model extents ~5% so hull edges aren't clipped.
            double dx = maxX - minX, dy = maxY - minY;
            minX -= 0.05 * dx; maxX += 0.05 * dx;
            minY -= 0.05 * dy; maxY += 0.05 * dy;
            dx = maxX - minX; dy = maxY - minY;

            int plotL = Pad;
            int plotT = Pad + TitleH;
            int plotR = width - Pad - LegendW;
            int plotB = height - Pad;
            float plotW = Math.Max(1, plotR - plotL);
            float plotH = Math.Max(1, plotB - plotT);

            // Equal aspect: fit the model box into the plot rect.
            float scale = Math.Min(plotW / (float)dx, plotH / (float)dy);
            float usedW = (float)dx * scale;
            float usedH = (float)dy * scale;
            float ox = plotL + (plotW - usedW) * 0.5f;
            float oy = plotT + (plotH - usedH) * 0.5f;

            // Lens Y-up → screen Y-down: screenY = oy + usedH - (y - minY) * scale
            PointF Map(double x, double y) =>
                new PointF(ox + (float)((x - minX) * scale),
                           oy + usedH - (float)((y - minY) * scale));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

                using (var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (var labelFont = new Font("Segoe UI", 9f))
                using (var brush = new SolidBrush(Color.Black))
                using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
                {
                    string hdr = string.IsNullOrEmpty(title) ? "FootprintDxf preview" : title;
                    g.DrawString(hdr, titleFont, brush, Pad, Pad / 2f);

                    // Light plot frame.
                    using (var framePen = new Pen(Color.FromArgb(210, 210, 210), 1f))
                        g.DrawRectangle(framePen, plotL, plotT, plotW, plotH);

                    int colorIdx = 0;
                    var legendItems = new List<(string name, Color c)>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var p in polys)
                    {
                        if (p.Vertices == null || p.Vertices.Count < 3) continue;
                        Color c = AciColors[colorIdx % AciColors.Length];
                        colorIdx++;

                        string layer = DxfWriter.SanitizeLayer(p.LayerName);
                        if (seen.Add(layer))
                            legendItems.Add((layer, c));

                        var pts = new PointF[p.Vertices.Count + 1];
                        for (int i = 0; i < p.Vertices.Count; i++)
                            pts[i] = Map(p.Vertices[i].X, p.Vertices[i].Y);
                        pts[pts.Length - 1] = pts[0]; // close

                        using (var pen = new Pen(c, 1.8f))
                            g.DrawLines(pen, pts);

                        // Label near first vertex (same idea as DXF TEXT).
                        string label = !string.IsNullOrEmpty(p.Comment) ? p.Comment : layer;
                        if (!string.IsNullOrEmpty(label))
                        {
                            var lp = pts[0];
                            using (var lb = new SolidBrush(c))
                                g.DrawString(label, labelFont, lb, lp.X + 4, lp.Y - 14);
                        }
                    }

                    // Legend column on the right.
                    float lx = plotR + 12;
                    float ly = plotT;
                    g.DrawString("Layers", labelFont, gray, lx, ly);
                    ly += 18;
                    foreach (var item in legendItems)
                    {
                        using (var pen = new Pen(item.c, 2.5f))
                            g.DrawLine(pen, lx, ly + 7, lx + 22, ly + 7);
                        g.DrawString(item.name, labelFont, brush, lx + 28, ly);
                        ly += 16;
                        if (ly > plotB - 8) break;
                    }

                    // Tiny footer: lens-unit note.
                    g.DrawString("Local surface XY (lens units). Y up.", labelFont, gray,
                        Pad, height - Pad + 8);
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}

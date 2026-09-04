using System;
using System.Collections.Generic;

namespace FootprintDxf
{
    // Andrew's monotone-chain 2D convex hull. Pure geometry — no ZOS-API.
    static class ConvexHull
    {
        public struct Pt
        {
            public double X, Y;
            public Pt(double x, double y) { X = x; Y = y; }
        }

        // Returns the hull vertices in CCW order, without repeating the first
        // point at the end. Fewer than 3 distinct points → empty (no polygon).
        public static List<Pt> Compute(IList<Pt> input)
        {
            if (input == null || input.Count == 0) return new List<Pt>();

            var pts = new List<Pt>(input.Count);
            for (int i = 0; i < input.Count; i++) pts.Add(input[i]);

            pts.Sort((a, b) =>
            {
                int c = a.X.CompareTo(b.X);
                return c != 0 ? c : a.Y.CompareTo(b.Y);
            });

            // Deduplicate exact duplicates so a repeated intercept does not
            // inflate the lower/upper chains.
            int w = 1;
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].X != pts[w - 1].X || pts[i].Y != pts[w - 1].Y)
                    pts[w++] = pts[i];
            }
            if (w < pts.Count) pts.RemoveRange(w, pts.Count - w);
            if (pts.Count < 3) return new List<Pt>();

            var lower = new List<Pt>();
            for (int i = 0; i < pts.Count; i++)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], pts[i]) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(pts[i]);
            }

            var upper = new List<Pt>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], pts[i]) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(pts[i]);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower.Count >= 3 ? lower : new List<Pt>();
        }

        static double Cross(Pt o, Pt a, Pt b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        // Tiny self-check: square corners + interior junk → exactly the 4 corners.
        public static bool SelfCheck(out string detail)
        {
            var pts = new List<Pt>
            {
                new Pt(0, 0), new Pt(1, 0), new Pt(1, 1), new Pt(0, 1),
                new Pt(0.5, 0.5), new Pt(0.2, 0.7), new Pt(0.9, 0.1),
                new Pt(0, 0), new Pt(1, 1) // duplicates
            };
            var h = Compute(pts);
            if (h.Count != 4)
            {
                detail = "expected 4 hull verts, got " + h.Count;
                return false;
            }
            // Area of unit square must be 1 (shoelace).
            double area = 0;
            for (int i = 0; i < h.Count; i++)
            {
                var a = h[i];
                var b = h[(i + 1) % h.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            area = Math.Abs(area) * 0.5;
            if (Math.Abs(area - 1.0) > 1e-9)
            {
                detail = "unit-square hull area " + area + " (expected 1)";
                return false;
            }
            // Degenerate: collinear → empty.
            var line = Compute(new[] { new Pt(0, 0), new Pt(1, 0), new Pt(2, 0) });
            if (line.Count != 0)
            {
                detail = "collinear points should yield empty hull";
                return false;
            }
            detail = "ok";
            return true;
        }
    }
}

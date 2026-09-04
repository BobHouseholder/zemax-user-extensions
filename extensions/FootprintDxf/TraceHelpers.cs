using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FootprintDxf
{
    partial class Program
    {
        static List<ConvexHull.Pt> TraceHits(
            ZOSAPI.IOpticalSystem sys,
            int surf,
            List<int> fieldList,
            List<int> waveList,
            List<(double px, double py)> samples,
            double maxR,
            ZOSAPI.SystemData.IFields fields)
        {
            var hits = new List<ConvexHull.Pt>();
            int nRays = fieldList.Count * waveList.Count * samples.Count;
            if (nRays == 0) return hits;

            // Cap a single batch to keep memory sane on huge grids.
            const int batchCap = 20000;
            int offset = 0;
            while (offset < nRays)
            {
                if (Cancelled()) return hits;
                int thisBatch = Math.Min(batchCap, nRays - offset);
                var trace = sys.Tools.OpenBatchRayTrace();
                try
                {
                    var data = trace.CreateNormUnpol(thisBatch, ZOSAPI.Tools.RayTrace.RaysType.Real, surf);
                    int added = 0;
                    int skip = offset;
                    foreach (int fi in fieldList)
                    {
                        var f = fields.GetField(fi);
                        double hx = f.X / maxR, hy = f.Y / maxR;
                        foreach (int w in waveList)
                        {
                            foreach (var s in samples)
                            {
                                if (skip > 0) { skip--; continue; }
                                if (added >= thisBatch) goto filled;
                                data.AddRay(w, hx, hy, s.px, s.py, ZOSAPI.Tools.RayTrace.OPDMode.None);
                                added++;
                            }
                        }
                    }
                filled:
                    trace.RunAndWaitForCompletion();
                    data.StartReadingResults();
                    int rayNum, errCode, vigCode;
                    double x, y, z, l, m, n, l2, m2, n2, opd, inten;
                    while (data.ReadNextResult(out rayNum, out errCode, out vigCode,
                        out x, out y, out z, out l, out m, out n, out l2, out m2, out n2, out opd, out inten))
                    {
                        if (errCode != 0) continue;
                        if (vigCode != 0) continue; // ignore vignetted
                        hits.Add(new ConvexHull.Pt(x, y));
                    }
                }
                finally { trace.Close(); }
                offset += thisBatch;
            }
            return hits;
        }

        static List<(double px, double py)> BuildPupilGrid(int n)
        {
            var list = new List<(double, double)>(n * n);
            for (int iy = 0; iy < n; iy++)
            {
                double py = n == 1 ? 0 : -1.0 + 2.0 * iy / (n - 1);
                for (int ix = 0; ix < n; ix++)
                {
                    double px = n == 1 ? 0 : -1.0 + 2.0 * ix / (n - 1);
                    if (px * px + py * py <= 1.0000001)
                        list.Add((px, py));
                }
            }
            return list;
        }

        // Angular samples on a circle of the given normalised pupil radius.
        static List<(double px, double py)> BuildPupilRim(int n, double radius = 1.0)
        {
            var list = new List<(double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                list.Add((radius * Math.Cos(a), radius * Math.Sin(a)));
            }
            return list;
        }

        // Dense rim always used for the main SURF hull: full rim at r=1 plus a
        // near-edge ring at r=0.99 (helps numerical vignetting). Same angular count.
        static List<(double px, double py)> BuildDenseRimSamples(int n)
        {
            var list = new List<(double, double)>(n * 2);
            list.AddRange(BuildPupilRim(n, 1.0));
            list.AddRange(BuildPupilRim(n, 0.99));
            return list;
        }

        // Sort hits by atan2 around the centroid into a closed ring. Used for
        // optional -rim RIM_… layers only (main SURF layers stay convex hull).
        // Drops exact consecutive duplicates after sorting.
        static List<ConvexHull.Pt> OrderAsClosedRing(List<ConvexHull.Pt> hits)
        {
            if (hits == null || hits.Count == 0) return new List<ConvexHull.Pt>();
            double cx = 0, cy = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                cx += hits[i].X;
                cy += hits[i].Y;
            }
            cx /= hits.Count;
            cy /= hits.Count;

            var ordered = new List<ConvexHull.Pt>(hits);
            ordered.Sort((a, b) =>
            {
                double aa = Math.Atan2(a.Y - cy, a.X - cx);
                double bb = Math.Atan2(b.Y - cy, b.X - cx);
                int c = aa.CompareTo(bb);
                if (c != 0) return c;
                // Stable tie-break for identical angles.
                c = a.X.CompareTo(b.X);
                return c != 0 ? c : a.Y.CompareTo(b.Y);
            });

            var ring = new List<ConvexHull.Pt>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                if (ring.Count > 0
                    && ring[ring.Count - 1].X == p.X
                    && ring[ring.Count - 1].Y == p.Y)
                    continue;
                ring.Add(p);
            }
            // Also drop wrap-around duplicate (first == last).
            if (ring.Count >= 2
                && ring[0].X == ring[ring.Count - 1].X
                && ring[0].Y == ring[ring.Count - 1].Y)
                ring.RemoveAt(ring.Count - 1);
            return ring;
        }

        // Scrambled unit-circle samples must sort by atan2 around the input
        // centroid; exact consecutive duplicates must be dropped.
        static bool OrderAsClosedRingSelfCheck(out string detail)
        {
            double s = Math.Sqrt(0.5);
            var pts = new List<ConvexHull.Pt>
            {
                new ConvexHull.Pt(0, 1),
                new ConvexHull.Pt(-s, -s),
                new ConvexHull.Pt(1, 0),
                new ConvexHull.Pt(s, s),
                new ConvexHull.Pt(-1, 0),
                new ConvexHull.Pt(0, -1),
                new ConvexHull.Pt(-s, s),
                new ConvexHull.Pt(s, -s),
                new ConvexHull.Pt(1, 0), // duplicate of east
            };
            double cx = 0, cy = 0;
            for (int i = 0; i < pts.Count; i++) { cx += pts[i].X; cy += pts[i].Y; }
            cx /= pts.Count; cy /= pts.Count;

            var ring = OrderAsClosedRing(pts);
            if (ring.Count != 8)
            {
                detail = "expected 8 ring verts after dedupe, got " + ring.Count;
                return false;
            }
            for (int i = 1; i < ring.Count; i++)
            {
                double a0 = Math.Atan2(ring[i - 1].Y - cy, ring[i - 1].X - cx);
                double a1 = Math.Atan2(ring[i].Y - cy, ring[i].X - cx);
                if (a1 < a0 - 1e-12)
                {
                    detail = "ring not angle-ordered at index " + i;
                    return false;
                }
            }
            detail = "ok";
            return true;
        }

        static string LayerName(int surf, string comment)
        {
            if (!string.IsNullOrWhiteSpace(comment))
                return DxfWriter.SanitizeLayer(comment);
            return "SURF_" + surf.ToString(CI);
        }
    }
}

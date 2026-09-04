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

        static List<(double px, double py)> BuildPupilRim(int n)
        {
            var list = new List<(double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                list.Add((Math.Cos(a), Math.Sin(a)));
            }
            return list;
        }

        // For rim samples traced across many fields/waves the hit cloud is not
        // ordered; take the convex hull of rim hits as the closed ring (same
        // envelope product, denser when rim alone is requested with the grid).
        static List<ConvexHull.Pt> OrderAsClosedRing(List<ConvexHull.Pt> hits)
        {
            return ConvexHull.Compute(hits);
        }

        static string LayerName(int surf, string comment)
        {
            if (!string.IsNullOrWhiteSpace(comment))
                return DxfWriter.SanitizeLayer(comment);
            return "SURF_" + surf.ToString(CI);
        }
    }
}

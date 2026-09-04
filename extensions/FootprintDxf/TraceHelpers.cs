using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FootprintDxf
{
    partial class Program
    {
        // Map OpticStudio lens units -> AutoCAD $INSUNITS. Returns false if unknown
        // (caller should omit or set 0 and stamp the unit name in title/TEXT).
        static bool TryMapInsUnits(ZOSAPI.SystemData.ZemaxSystemUnits lensUnits,
            out int insUnits, out string label)
        {
            switch (lensUnits)
            {
                case ZOSAPI.SystemData.ZemaxSystemUnits.Millimeters:
                    insUnits = 4; label = "mm"; return true;
                case ZOSAPI.SystemData.ZemaxSystemUnits.Centimeters:
                    insUnits = 5; label = "cm"; return true;
                case ZOSAPI.SystemData.ZemaxSystemUnits.Inches:
                    insUnits = 1; label = "in"; return true;
                case ZOSAPI.SystemData.ZemaxSystemUnits.Meters:
                    insUnits = 6; label = "m"; return true;
                default:
                    insUnits = 0; label = lensUnits.ToString(); return false;
            }
        }

        // Pure helper for -selftest (no ZOS). Mirrors TryMapInsUnits mapping.
        static bool TryMapInsUnitsByName(string unitName, out int insUnits, out string label)
        {
            switch ((unitName ?? "").Trim().ToLowerInvariant())
            {
                case "millimeters": case "mm":
                    insUnits = 4; label = "mm"; return true;
                case "centimeters": case "cm":
                    insUnits = 5; label = "cm"; return true;
                case "inches": case "in":
                    insUnits = 1; label = "in"; return true;
                case "meters": case "m":
                    insUnits = 6; label = "m"; return true;
                default:
                    insUnits = 0; label = unitName ?? "unknown"; return false;
            }
        }

        static List<ConvexHull.Pt> TraceHits(
            ZOSAPI.IOpticalSystem sys,
            int surf,
            List<int> fieldList,
            List<int> waveList,
            List<(double px, double py)> samples,
            double maxR,
            ZOSAPI.SystemData.IFields fields)
        {
            var byField = TraceHitsByField(sys, surf, fieldList, waveList, samples, maxR, fields);
            var hits = new List<ConvexHull.Pt>();
            foreach (var kv in byField)
                hits.AddRange(kv.Value);
            return hits;
        }

        // Same batching as TraceHits, but groups successful hits by field index so
        // rim layers can be written per-field without a second TraceHits.
        static Dictionary<int, List<ConvexHull.Pt>> TraceHitsByField(
            ZOSAPI.IOpticalSystem sys,
            int surf,
            List<int> fieldList,
            List<int> waveList,
            List<(double px, double py)> samples,
            double maxR,
            ZOSAPI.SystemData.IFields fields)
        {
            var byField = new Dictionary<int, List<ConvexHull.Pt>>();
            foreach (int fi in fieldList)
                byField[fi] = new List<ConvexHull.Pt>();

            int nF = fieldList.Count, nW = waveList.Count, nS = samples.Count;
            int nRays = nF * nW * nS;
            if (nRays == 0) return byField;

            // Cap a single batch to keep memory sane on huge grids.
            const int batchCap = 20000;
            int offset = 0;
            while (offset < nRays)
            {
                if (Cancelled()) return byField;
                int thisBatch = Math.Min(batchCap, nRays - offset);
                var trace = sys.Tools.OpenBatchRayTrace();
                try
                {
                    var data = trace.CreateNormUnpol(thisBatch, ZOSAPI.Tools.RayTrace.RaysType.Real, surf);
                    // Linear index -> (field, wave, sample); no O(n²) skip re-scan.
                    for (int bi = 0; bi < thisBatch; bi++)
                    {
                        int idx = offset + bi;
                        int fi = idx / (nW * nS);
                        int rem = idx % (nW * nS);
                        int wi = rem / nS;
                        int si = rem % nS;
                        var f = fields.GetField(fieldList[fi]);
                        double hx = f.X / maxR, hy = f.Y / maxR;
                        data.AddRay(waveList[wi], hx, hy, samples[si].px, samples[si].py,
                            ZOSAPI.Tools.RayTrace.OPDMode.None);
                    }
                    trace.RunAndWaitForCompletion();
                    data.StartReadingResults();
                    int rayNum, errCode, vigCode;
                    double x, y, z, l, m, n, l2, m2, n2, opd, inten;
                    while (data.ReadNextResult(out rayNum, out errCode, out vigCode,
                        out x, out y, out z, out l, out m, out n, out l2, out m2, out n2, out opd, out inten))
                    {
                        if (errCode != 0) continue;
                        if (vigCode != 0) continue; // ignore vignetted
                        // rayNum is 1-based within the batch.
                        int idx = offset + (rayNum - 1);
                        if (idx < 0 || idx >= nRays) continue;
                        int fi = idx / (nW * nS);
                        if (fi < 0 || fi >= nF) continue;
                        byField[fieldList[fi]].Add(new ConvexHull.Pt(x, y));
                    }
                }
                finally { trace.Close(); }
                offset += thisBatch;
            }
            return byField;
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

        // Sort hits by atan2 around the centroid into a closed ring. Used for
        // optional -rim RIM_... layers only (main SURF layers stay convex hull).
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

        // Always include surface index. Prefer SURF_{n} or SURF_{n}_{sanitizedComment}.
        static string LayerName(int surf, string comment)
        {
            string name = "SURF_" + surf.ToString(CI);
            if (!string.IsNullOrWhiteSpace(comment))
            {
                string sanitized = DxfWriter.SanitizeLayer(comment);
                if (!string.IsNullOrEmpty(sanitized) &&
                    !sanitized.Equals("SURF", StringComparison.OrdinalIgnoreCase))
                    name = name + "_" + sanitized;
            }
            return DxfWriter.SanitizeLayer(name);
        }

        static bool LayerNameSelfCheck(out string detail)
        {
            string a = LayerName(3, null);
            if (a != "SURF_3") { detail = "null comment -> SURF_3, got " + a; return false; }
            string b = LayerName(3, "");
            if (b != "SURF_3") { detail = "empty comment -> SURF_3, got " + b; return false; }
            string c = LayerName(5, "Front Element");
            if (!c.StartsWith("SURF_5_", StringComparison.Ordinal))
            { detail = "commented layer must start SURF_5_, got " + c; return false; }
            if (!c.Contains("Front") && !c.Contains("Element"))
            { detail = "sanitized comment missing from " + c; return false; }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string u1 = DxfWriter.EnsureUniqueLayer("SURF_1", used);
            string u2 = DxfWriter.EnsureUniqueLayer("SURF_1", used);
            if (u1 != "SURF_1" || u2 != "SURF_1_2")
            { detail = "uniqueness expected SURF_1 / SURF_1_2, got " + u1 + " / " + u2; return false; }
            detail = "ok";
            return true;
        }

        static bool UnitsMapSelfCheck(out string detail)
        {
            int iu; string lab;
            if (!TryMapInsUnitsByName("Millimeters", out iu, out lab) || iu != 4 || lab != "mm")
            { detail = "mm map failed"; return false; }
            if (!TryMapInsUnitsByName("Inches", out iu, out lab) || iu != 1 || lab != "in")
            { detail = "in map failed"; return false; }
            if (!TryMapInsUnitsByName("Centimeters", out iu, out lab) || iu != 5 || lab != "cm")
            { detail = "cm map failed"; return false; }
            if (!TryMapInsUnitsByName("Meters", out iu, out lab) || iu != 6 || lab != "m")
            { detail = "m map failed"; return false; }
            if (TryMapInsUnitsByName("Furlongs", out iu, out lab) || iu != 0)
            { detail = "unknown should be false/0"; return false; }
            string folded = DxfWriter.SanitizeText("Café - S1");
            if (folded.IndexOf((char)0xE9) >= 0)
            { detail = "SanitizeText left Latin-1: " + folded; return false; }
            if (string.IsNullOrEmpty(folded) || folded.IndexOf('S') < 0)
            { detail = "SanitizeText emptied text: " + folded; return false; }
            detail = "ok";
            return true;
        }
    }
}

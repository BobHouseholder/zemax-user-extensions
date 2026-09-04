using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FootprintDxf
{
    partial class Program
    {
        static void Export(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys == null)
                throw new Exception("PrimarySystem is null - no optical system is available");
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("FootprintDxf requires a sequential system (NSC is not supported)");

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;
            if (imgIdx < 1)
                throw new Exception("system has no surfaces to export");

            var surfList = ResolveSurfaces(Opts.Surfaces, imgIdx, Opts.IncludeImage, lde);
            if (surfList.Count == 0)
                throw new Exception("no surfaces selected");

            var fieldList = ResolveFields(sys, Opts.Fields);
            var waveList = ResolveWaves(sys, Opts.Wave);
            if (fieldList.Count == 0) throw new Exception("no fields selected");
            if (waveList.Count == 0) throw new Exception("no wavelengths selected");

            // Lens units -> DXF $INSUNITS (do not hard-code mm).
            var lensUnits = sys.SystemData.Units.LensUnits;
            int insUnitsCode;
            string unitsLabel;
            bool unitsKnown = TryMapInsUnits(lensUnits, out insUnitsCode, out unitsLabel);
            if (!unitsKnown)
            {
                Console.WriteLine("WARNING: unrecognized lens units '" + lensUnits +
                    "' - $INSUNITS set to 0 (unitless); units stamped in title/TEXT.");
                insUnitsCode = 0;
            }

            // Field normalisation for NormUnpol hx/hy (same pattern as LayoutRender).
            var fields = sys.SystemData.Fields;
            double maxR = 1e-10;
            for (int i = 1; i <= fields.NumberOfFields; i++)
            {
                var f = fields.GetField(i);
                maxR = Math.Max(maxR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }

            int rimN = Opts.EffectiveRimRays();
            var pupilSamples = BuildPupilGrid(Opts.Rays);
            // Dense rim: outer rim@1 traced once (reused for -rim); near-edge 0.99
            // merged with the grid for the hull. No second TraceHits of rim@1.
            var rim1Samples = BuildPupilRim(rimN, 1.0);
            var rim099Samples = BuildPupilRim(rimN, 0.99);
            var innerSamples = new List<(double px, double py)>(pupilSamples.Count + rim099Samples.Count);
            innerSamples.AddRange(pupilSamples);
            innerSamples.AddRange(rim099Samples);

            Say("=== FootprintDxf ===");
            Say("Forum : https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991");
            Say("Lens  : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(string.Format(CI,
                "Surfaces: {0}  Fields: {1}  Waves: {2}  Pupil grid: {3}x{3} ({4} in-circle)  Rim: {5}x2 (r=1+0.99)",
                string.Join(",", surfList), fieldList.Count, waveList.Count,
                Opts.Rays, pupilSamples.Count, rimN));
            Say(string.Format(CI,
                "Coords: local surface XY (lens units = {0}). $INSUNITS={1}. System is not modified.",
                unitsLabel, insUnitsCode));

            string outPath = Opts.OutPath;
            if (string.IsNullOrEmpty(outPath))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                outPath = string.IsNullOrEmpty(src)
                    ? Path.Combine(app.ZemaxDataDir, "footprints.dxf")
                    : Path.Combine(Path.GetDirectoryName(src) ?? ".",
                        Path.GetFileNameWithoutExtension(src) + "_footprints.dxf");
            }

            var polys = new List<DxfWriter.LayerPoly>();
            var usedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int done = 0;
            foreach (int surf in surfList)
            {
                if (Cancelled()) return;
                done++;
                app.ProgressPercent = 5 + 90.0 * (done - 1) / Math.Max(1, surfList.Count);
                app.ProgressMessage = string.Format(CI, "Tracing footprint on surface {0} ({1}/{2})...",
                    surf, done, surfList.Count);

                string comment = null;
                try { comment = (lde.GetSurfaceAt(surf).Comment ?? "").Trim(); } catch { }

                // grid + r=0.99 (all fields), then rim@1 once (by field) - merge for hull.
                var innerHits = TraceHits(sys, surf, fieldList, waveList, innerSamples, maxR, fields);
                if (Cancelled()) return;
                var rimByField = TraceHitsByField(sys, surf, fieldList, waveList, rim1Samples, maxR, fields);
                if (Cancelled()) return;

                var hits = new List<ConvexHull.Pt>(innerHits.Count + rim1Samples.Count * fieldList.Count);
                hits.AddRange(innerHits);
                int rimHitTotal = 0;
                foreach (var kv in rimByField)
                {
                    hits.AddRange(kv.Value);
                    rimHitTotal += kv.Value.Count;
                }

                var hull = ConvexHull.Compute(hits);
                string layer = DxfWriter.EnsureUniqueLayer(LayerName(surf, comment), usedLayers);
                if (hull.Count < 3)
                {
                    Console.WriteLine(string.Format(CI,
                        "WARNING: surface {0} - no usable hull ({1} hit(s)); skipping.",
                        surf, hits.Count));
                }
                else
                {
                    polys.Add(new DxfWriter.LayerPoly
                    {
                        LayerName = layer,
                        Comment = string.IsNullOrEmpty(comment) ? ("S" + surf) : ("S" + surf + " " + comment),
                        Vertices = hull
                    });
                    Say(string.Format(CI,
                        "  Surf {0}: {1} hits (inner {2} + rim@1 {3}) -> hull {4} verts  layer={5}",
                        surf, hits.Count, innerHits.Count, rimHitTotal, hull.Count, layer));
                }

                // Optional RIM_... layers: reuse rim@1 hits - one layer per field.
                // Do NOT atan2-merge all fields into a single ring.
                if (Opts.Rim)
                {
                    foreach (int fi in fieldList)
                    {
                        if (Cancelled()) return;
                        List<ConvexHull.Pt> rimHits;
                        if (!rimByField.TryGetValue(fi, out rimHits) || rimHits == null)
                            continue;
                        var rimPoly = OrderAsClosedRing(rimHits);
                        if (rimPoly.Count < 3) continue;
                        string rimLayer = DxfWriter.EnsureUniqueLayer(
                            "RIM_" + layer + "_F" + fi.ToString(CI), usedLayers);
                        polys.Add(new DxfWriter.LayerPoly
                        {
                            LayerName = rimLayer,
                            Comment = "rim S" + surf + " F" + fi.ToString(CI),
                            Vertices = rimPoly
                        });
                        Say(string.Format(CI,
                            "  Surf {0} field {1}: rim@1 {2} hits -> ring {3} verts  layer={4}",
                            surf, fi, rimHits.Count, rimPoly.Count, rimLayer));
                    }
                }
            }

            if (Cancelled()) return;

            if (polys.Count == 0)
                throw new Exception("no footprint polylines to write (every selected surface had an empty hull)");

            string lensName = Path.GetFileName(
                string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile);
            string title = "FootprintDxf " + lensName + " [" + unitsLabel + "]";
            // Always pass insUnitsCode (0 when unknown - stamped in title above).
            DxfWriter.Write(outPath, polys, title, insUnitsCode);
            Say("DXF written to: " + outPath);

            string pngPath = null;
            if (!Opts.NoPng)
            {
                pngPath = Path.ChangeExtension(outPath, ".png");
                PngWriter.Write(pngPath, polys, title);
                Say("PNG written to: " + pngPath);
            }

            app.ProgressMessage = "Done. Footprint DXF written to " + Path.GetFileName(outPath)
                + (pngPath != null ? " (+ PNG preview)" : "");
            OpenOutputs(app, outPath, pngPath);
        }
    }
}

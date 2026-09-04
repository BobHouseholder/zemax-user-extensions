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
            // Dense rim: outer rim@1 traced once (reused for -rim / -perfield); near-edge
            // 0.99 merged with the grid for the hull. No second TraceHits of rim@1.
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
            if (Opts.Global)
            {
                Say(string.Format(CI,
                    "Coords: GLOBAL frame via LDE.GetGlobalMatrix (lens units = {0}). " +
                    "2D DXF uses global X/Y; Z ignored. $INSUNITS={1}. System is not modified.",
                    unitsLabel, insUnitsCode));
            }
            else
            {
                Say(string.Format(CI,
                    "Coords: local surface XY (lens units = {0}). $INSUNITS={1}. System is not modified.",
                    unitsLabel, insUnitsCode));
            }
            if (Opts.PerField)
                Say("Per-field: also writing SURF_{n}_F{f} hull layers (union SURF_{n} kept).");
            if (Opts.Aperture)
                Say("Aperture: writing APER_SURF_{n} clear-aperture overlays when available.");

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

                GlobalFrame frame = default(GlobalFrame);
                bool useGlobal = false;
                if (Opts.Global)
                {
                    frame = TryGetGlobalFrame(lde, surf);
                    if (!frame.Valid)
                    {
                        Console.WriteLine(string.Format(CI,
                            "WARNING: surface {0} - GetGlobalMatrix failed; using local XY for this surface.",
                            surf));
                    }
                    else useGlobal = true;
                }

                // grid + r=0.99 by field, then rim@1 by field - merge for union hull.
                // Partitioned hits also feed -perfield / -rim without a second TraceHits.
                var innerByField = TraceHitsByField(sys, surf, fieldList, waveList, innerSamples, maxR, fields);
                if (Cancelled()) return;
                var rimByField = TraceHitsByField(sys, surf, fieldList, waveList, rim1Samples, maxR, fields);
                if (Cancelled()) return;

                if (useGlobal)
                {
                    innerByField = TransformHitsByFieldXY(innerByField, frame);
                    rimByField = TransformHitsByFieldXY(rimByField, frame);
                }

                var hits = new List<ConvexHull.Pt>();
                int innerHitTotal = 0, rimHitTotal = 0;
                foreach (int fi in fieldList)
                {
                    List<ConvexHull.Pt> ih;
                    if (innerByField.TryGetValue(fi, out ih) && ih != null)
                    {
                        hits.AddRange(ih);
                        innerHitTotal += ih.Count;
                    }
                    List<ConvexHull.Pt> rh;
                    if (rimByField.TryGetValue(fi, out rh) && rh != null)
                    {
                        hits.AddRange(rh);
                        rimHitTotal += rh.Count;
                    }
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
                        surf, hits.Count, innerHitTotal, rimHitTotal, hull.Count, layer));
                }

                // Optional per-field SURF_{n}_F{f} hulls (union layer above still written).
                if (Opts.PerField)
                {
                    foreach (int fi in fieldList)
                    {
                        if (Cancelled()) return;
                        var fieldHits = new List<ConvexHull.Pt>();
                        List<ConvexHull.Pt> ih, rh;
                        if (innerByField.TryGetValue(fi, out ih) && ih != null) fieldHits.AddRange(ih);
                        if (rimByField.TryGetValue(fi, out rh) && rh != null) fieldHits.AddRange(rh);
                        var fHull = ConvexHull.Compute(fieldHits);
                        if (fHull.Count < 3)
                        {
                            Console.WriteLine(string.Format(CI,
                                "WARNING: surface {0} field {1} - no usable per-field hull ({2} hit(s)); skipping.",
                                surf, fi, fieldHits.Count));
                            continue;
                        }
                        string fLayer = DxfWriter.EnsureUniqueLayer(
                            layer + "_F" + fi.ToString(CI), usedLayers);
                        polys.Add(new DxfWriter.LayerPoly
                        {
                            LayerName = fLayer,
                            Comment = "S" + surf + " F" + fi.ToString(CI),
                            Vertices = fHull
                        });
                        Say(string.Format(CI,
                            "  Surf {0} field {1}: {2} hits -> hull {3} verts  layer={4}",
                            surf, fi, fieldHits.Count, fHull.Count, fLayer));
                    }
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

                // Optional clear-aperture overlay (same frame as footprints).
                if (Opts.Aperture)
                {
                    List<ConvexHull.Pt> aperVerts;
                    string aperKind, aperWarn;
                    if (!TryBuildApertureOverlay(lde, surf, out aperVerts, out aperKind, out aperWarn))
                    {
                        Console.WriteLine(string.Format(CI,
                            "WARNING: surface {0} - aperture overlay skipped ({1}).",
                            surf, aperWarn ?? "no data"));
                    }
                    else
                    {
                        if (useGlobal)
                            aperVerts = TransformHitsXY(aperVerts, frame);
                        string aperLayer = DxfWriter.EnsureUniqueLayer("APER_" + layer, usedLayers);
                        polys.Add(new DxfWriter.LayerPoly
                        {
                            LayerName = aperLayer,
                            Comment = "aper S" + surf + " " + aperKind,
                            Vertices = aperVerts
                        });
                        Say(string.Format(CI,
                            "  Surf {0}: aperture {1} -> {2} verts  layer={3}",
                            surf, aperKind, aperVerts.Count, aperLayer));
                    }
                }
            }

            if (Cancelled()) return;

            if (polys.Count == 0)
                throw new Exception("no footprint polylines to write (every selected surface had an empty hull)");

            string lensName = Path.GetFileName(
                string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile);
            string title = "FootprintDxf " + lensName + " [" + unitsLabel + "]"
                + (Opts.Global ? " [global XY]" : "");
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

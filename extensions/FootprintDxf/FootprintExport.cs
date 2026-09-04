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
                throw new Exception("PrimarySystem is null — no optical system is available");
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

            // Field normalisation for NormUnpol hx/hy (same pattern as LayoutRender).
            var fields = sys.SystemData.Fields;
            double maxR = 1e-10;
            for (int i = 1; i <= fields.NumberOfFields; i++)
            {
                var f = fields.GetField(i);
                maxR = Math.Max(maxR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }

            var pupilSamples = BuildPupilGrid(Opts.Rays);
            var rimSamples = Opts.Rim ? BuildPupilRim(Math.Max(64, Opts.Rays * 4)) : null;

            Say("=== FootprintDxf ===");
            Say("Forum : https://community.zemax.com/got-a-question-7/how-can-i-export-beam-footprints-to-a-cad-or-dxf-file-5991");
            Say("Lens  : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(string.Format(CI, "Surfaces: {0}  Fields: {1}  Waves: {2}  Pupil grid: {3}x{3} ({4} in-circle)",
                string.Join(",", surfList), fieldList.Count, waveList.Count, Opts.Rays, pupilSamples.Count));
            Say("Coords: local surface XY (lens units, usually mm). System is not modified.");

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

                var hits = TraceHits(sys, surf, fieldList, waveList, pupilSamples, maxR, fields);
                if (Cancelled()) return;

                var hull = ConvexHull.Compute(hits);
                string layer = LayerName(surf, comment);
                if (hull.Count < 3)
                {
                    Console.WriteLine(string.Format(CI,
                        "WARNING: surface {0} — no usable hull ({1} hit(s)); skipping.",
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
                    Say(string.Format(CI, "  Surf {0}: {1} hits → hull {2} verts  layer={3}",
                        surf, hits.Count, hull.Count, layer));
                }

                if (Opts.Rim && rimSamples != null)
                {
                    if (Cancelled()) return;
                    var rimHits = TraceHits(sys, surf, fieldList, waveList, rimSamples, maxR, fields);
                    // Rim samples are already in angular order around the pupil;
                    // keep that order as a closed polyline (not re-hulled).
                    var rimPoly = OrderAsClosedRing(rimHits);
                    if (rimPoly.Count >= 3)
                    {
                        polys.Add(new DxfWriter.LayerPoly
                        {
                            LayerName = "RIM_" + layer,
                            Comment = "rim S" + surf,
                            Vertices = rimPoly
                        });
                    }
                }
            }

            if (Cancelled()) return;

            if (polys.Count == 0)
                throw new Exception("no footprint polylines to write (every selected surface had an empty hull)");

            string title = "FootprintDxf " + Path.GetFileName(
                string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile);
            DxfWriter.Write(outPath, polys, title);
            Say("DXF written to: " + outPath);
            app.ProgressMessage = "Done. Footprint DXF written to " + Path.GetFileName(outPath);
            OpenOutputs(app, outPath);
        }
    }
}

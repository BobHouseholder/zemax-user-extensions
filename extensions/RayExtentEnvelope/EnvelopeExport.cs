using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RayExtentEnvelope
{
    // ============================================================
    // EnvelopeExport - measure ray extents and write PNG/STEP
    // ============================================================
    // Traces extreme-field rim rays, records max radius at each
    // station, then builds the keep-out envelope picture and solid.
    // The lens file is never saved or changed.
    // ============================================================
    partial class Program
    {
        // Where a surface sits in global space (rotation + origin).
        class Frame
        {
            public double[,] R = new double[3, 3];
            public double X, Y, Z;
            public bool Valid;
            public (double gx, double gy, double gz) ToGlobal(double x, double y, double z) =>
                (R[0, 0] * x + R[0, 1] * y + R[0, 2] * z + X,
                 R[1, 0] * x + R[1, 1] * y + R[1, 2] * z + Y,
                 R[2, 0] * x + R[2, 1] * y + R[2, 2] * z + Z);
        }

        // One lens surface: shape, glass, stop flag, and YZ drawing section.
        class SurfInfo
        {
            public int Index;
            public ZOSAPI.Editors.LDE.SurfaceType Type;
            public double Radius, Conic, SemiDiameter, MechanicalSemiDiameter, Thickness;
            public double[] Pars = new double[9];
            public string Material = "";
            public string EffMedium = "";
            public bool IsStop;
            public bool IsDummy;
            public bool Draw;
            public Frame Frame = new Frame();
            public List<PointF> Section; // YZ drawing plane: X=global Z, Y=global Y
        }

        // One keep-out sample: surface index, Z along axis, max ray radius.
        class Station
        {
            public int Surf;
            public double Z;
            public double Rmax;
            public int Hits;
        }

        // One MEMA lens solid: closed RZ profile spun in the front-surface frame.
        class LensSolid
        {
            public string Name;
            public List<(double z, double r)> ProfileRz; // local RZ closed profile (axis Z)
            public Frame Frame; // front-surface global frame
        }

        // Main job: measure rim extents, build envelope, write PNG and/or STEP.
        static void Export(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys == null)
                throw new Exception("PrimarySystem is null - no optical system is available");
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("RayExtentEnvelope requires a sequential system");

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;
            if (imgIdx < 1)
                throw new Exception("system has no surfaces");

            int stopIdx = 0;
            try { stopIdx = sys.LDE.GetSurfaceAt(1).IsStop ? 1 : 0; } catch { }
            try
            {
                for (int i = 1; i <= imgIdx; i++)
                    if (lde.GetSurfaceAt(i).IsStop) { stopIdx = i; break; }
            }
            catch { }

            var surfs = GatherSurfaces(lde, imgIdx, stopIdx);
            BuildSections(lde, surfs);

            var fieldsApi = sys.SystemData.Fields;
            double maxFieldR = 1e-10;
            for (int i = 1; i <= fieldsApi.NumberOfFields; i++)
            {
                var f = fieldsApi.GetField(i);
                maxFieldR = Math.Max(maxFieldR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }
            var extremeFields = new List<int>();
            for (int i = 1; i <= fieldsApi.NumberOfFields; i++)
            {
                var f = fieldsApi.GetField(i);
                double r = Math.Sqrt(f.X * f.X + f.Y * f.Y);
                if (r >= maxFieldR * 0.999999) extremeFields.Add(i);
            }
            if (extremeFields.Count == 0)
                for (int i = 1; i <= fieldsApi.NumberOfFields; i++) extremeFields.Add(i);

            int wave = 1;
            try
            {
                var wls = sys.SystemData.Wavelengths;
                for (int w = 1; w <= wls.NumberOfWavelengths; w++)
                    if (wls.GetWavelength(w).IsPrimary) { wave = w; break; }
            }
            catch { }

            var stationsSurf = ResolveStations(Opts.Surfaces, surfs, imgIdx);
            if (stationsSurf.Count == 0)
                throw new Exception("no envelope stations selected");

            var rim = BuildPupilRim(Opts.RimRays, 1.0);

            Say("=== RayExtentEnvelope ===");
            Say("Lens  : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(F("Extreme fields: {0}  Primary wave: {1}  Rim samples: {2}  Stations: {3}",
                string.Join(",", extremeFields), wave, Opts.RimRays, string.Join(",", stationsSurf)));
            Say("System is not modified.");
            Say(Opts.NoClap
                ? "Rmax mode: rayExtent (default) = max(rayR, fieldH) - no CLAP/Semi floor"
                : "Rmax mode: CA floor (-clap) = max(rayR, CLAP, fieldH)");
            Say(Opts.UseVertexZ
                ? "Station Z: vertex (-vertexZ) = Frame.Z"
                : "Station Z: rim (default) = ray-hit Z, else Frame.Z+sag");

            app.ProgressPercent = 5;
            app.ProgressMessage = "Tracing max-extent pupil-rim rays...";

            var stations = new List<Station>();
            int done = 0;
            foreach (int surf in stationsSurf)
            {
                if (Cancelled())
                {
                    Say("Terminated by user - no outputs written.");
                    app.ProgressMessage = "Done. Terminated by user.";
                    return;
                }
                done++;
                app.ProgressPercent = 5 + 70.0 * (done - 1) / Math.Max(1, stationsSurf.Count);
                app.ProgressMessage = F("Tracing station surface {0} ({1}/{2})...", surf, done, stationsSurf.Count);

                var si = surfs.FirstOrDefault(s => s.Index == surf);
                if (si == null || !si.Frame.Valid) continue;

                double rayR = 0;
                double rimHitZ = double.NaN;
                int hits = 0;
                bool hasRimHit = TraceRimMaxRadius(sys, surf, extremeFields, wave, rim, maxFieldR, fieldsApi, si.Frame,
                    ref rayR, ref rimHitZ, ref hits);
                double clap = ClearRadiusForStation(si, surfs, lde);
                double fieldH = FieldHeightAtStation(sys, surf, extremeFields, wave, maxFieldR, fieldsApi, si.Frame);
                clap = SanitizeRadius(clap);
                fieldH = SanitizeRadius(fieldH);
                rayR = SanitizeRadius(rayR);
                // Default: do not floor with clear aperture — only ray radius and field height.
                double floored = Opts.NoClap
                    ? Math.Max(rayR, fieldH)
                    : Math.Max(rayR, Math.Max(clap, fieldH));
                floored = SanitizeRadius(floored);
                // Skip infinite-conjugate object (Z ~ -1e10) or non-finite radii.
                if (surf == 0 && !IsFiniteObjectStation(si))
                {
                    Say(F("  Surf {0}: skipped (infinite/non-finite object plane)", surf));
                    continue;
                }
                if (!(floored > 0) || Math.Abs(si.Frame.Z) > 1e8)
                {
                    Say(F("  Surf {0}: skipped (non-finite station Z={1:G6} R={2:G6})", surf, si.Frame.Z, floored));
                    continue;
                }
                // Station Z = rim Z by default (ray hit, else Frame.Z+Sag); -vertexZ uses Frame.Z.
                double vertexZ = si.Frame.Z;
                double stationZ;
                string zSrc;
                if (Opts.UseVertexZ)
                {
                    stationZ = vertexZ;
                    zSrc = "vertex";
                }
                else if (hasRimHit && rayR > 0)
                {
                    stationZ = rimHitZ;
                    zSrc = "rayHit";
                }
                else
                {
                    stationZ = RimZFromSag(si, floored);
                    zSrc = "sag";
                }
                if (double.IsNaN(stationZ) || double.IsInfinity(stationZ))
                {
                    Say(F("  Surf {0}: skipped (non-finite rim Z from {1})", surf, zSrc));
                    continue;
                }
                if (floored > rayR + 1e-12)
                    Say(F("  Surf {0}: ray R={1:G6}  CLAP={2:G6}  fieldH={3:G6} -> floor R={4:G6}",
                        surf, rayR, clap, fieldH, floored));
                Say(F("  Surf {0}: vertexZ={1:G6}  rimZ={2:G6} ({3})  rayR={4:G6}  fieldH={5:G6}  CLAP={6:G6}  floorR={7:G6}  hits={8}",
                    surf, vertexZ, stationZ, zSrc, rayR, fieldH, clap, floored, hits));
                stations.Add(new Station
                {
                    Surf = surf,
                    Z = stationZ,
                    Rmax = floored,
                    Hits = hits
                });
            }

            if (stations.All(s => s.Rmax <= 0))
                throw new Exception("no envelope radii - check fields/apertures/CLAP");

            // Monotone radial envelope vs Z (keep max R if duplicate Z).
            // Allow CLAP/object floors even when ray hits are zero (e.g. object plane).
            var env = stations
                .Where(s => s.Rmax > 0)
                .GroupBy(s => Math.Round(s.Z, 9))
                .Select(g => new Station { Z = g.First().Z, Rmax = g.Max(x => x.Rmax), Surf = g.First().Surf, Hits = g.Sum(x => x.Hits) })
                .OrderBy(s => s.Z)
                .ToList();

            // Object-at-0 CAD frame: shift all Z so surface-0 (finite object) is at Z=0.
            // Matches Concept-24 L1L2L3 / KEEP_OUT_plus_L1L2L3 STEPs (not Zemax L1-at-0).
            double zObj = 0;
            var objSurf = surfs.FirstOrDefault(s => s.Index == 0);
            if (objSurf != null && objSurf.Frame.Valid && IsFiniteObjectStation(objSurf))
                zObj = objSurf.Frame.Z;
            if (Math.Abs(zObj) > 1e-12 && Math.Abs(zObj) < 1e8)
            {
                Say(F("Frame: object-at-0 (Zemax object Z={0:G6} -> CAD Z=0; shift={1:G6} mm)",
                    zObj, -zObj));
                foreach (var st in env) st.Z -= zObj;
                foreach (var st in stations) st.Z -= zObj;
                foreach (var s in surfs)
                {
                    if (s.Frame.Valid) s.Frame.Z -= zObj;
                    if (s.Section != null)
                    {
                        for (int i = 0; i < s.Section.Count; i++)
                        {
                            var pt = s.Section[i];
                            s.Section[i] = new PointF(pt.X - (float)zObj, pt.Y);
                        }
                    }
                }
            }
            else
            {
                Say("Frame: object-at-0 (object already ~0 or infinite conjugate; no Z shift)");
            }

            var lenses = BuildLensSolids(surfs);
            var lensLines = BuildLensPolylines(surfs);
            var stopLines = BuildStopPolylines(surfs);

            string pngPath, stepPath;
            ResolveOutPaths(app, sys, out pngPath, out stepPath);

            app.ProgressPercent = 80;
            if (Opts.WantPng)
            {
                app.ProgressMessage = "Writing PNG...";
                var envTuples = env.Select(s => (s.Z, s.Rmax)).ToList();
                PngLayout.Write(pngPath, lensLines, stopLines, envTuples, Opts.Width, Opts.Height,
                    Path.GetFileName(string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
                Say("PNG  : " + pngPath + "  (" + new FileInfo(pngPath).Length + " bytes)");
            }
            app.ProgressPercent = 90;
            if (Opts.WantStep)
            {
                app.ProgressMessage = "Writing STEP...";
                // Default: KEEP_OUT + MEMA L1/L2/... . -envelopeOnly = KEEP_OUT only.
                var lensTuples = Opts.EnvelopeOnly
                    ? lenses.Where(_ => false).Select(L => (
                        L.Name,
                        L.ProfileRz,
                        L.Frame.R,
                        L.Frame.X, L.Frame.Y, L.Frame.Z,
                        L.Frame.Valid)).ToList()
                    : lenses.Select(L => (
                        L.Name,
                        L.ProfileRz,
                        L.Frame.R,
                        L.Frame.X, L.Frame.Y, L.Frame.Z,
                        L.Frame.Valid)).ToList();
                var envTuples2 = env.Select(s => (s.Z, s.Rmax)).ToList();
                if (envTuples2.Count >= 2)
                {
                    Say(F("KEEP_OUT Z span: {0:G6} .. {1:G6} mm (object-at-0)",
                        envTuples2[0].Z, envTuples2[envTuples2.Count - 1].Z));
                }
                if (!Opts.EnvelopeOnly && lensTuples.Count > 0)
                    Say("MEMA lenses: " + string.Join(", ", lensTuples.Select(t => t.Name)));
                StepWriter.Write(stepPath, lensTuples, envTuples2, 32);
                Say("STEP : " + stepPath + "  (" + new FileInfo(stepPath).Length + " bytes)"
                    + (Opts.EnvelopeOnly ? "  [KEEP_OUT only]" : "  [KEEP_OUT+MEMA]"));
                if (!string.IsNullOrEmpty(StepWriter.LastWriteMode)) Say("STEP mode: " + StepWriter.LastWriteMode);
            }

            app.ProgressMessage = "Done. RayExtentEnvelope outputs written.";
            var open = new List<string>();
            if (Opts.WantPng) open.Add(pngPath);
            if (Opts.WantStep) open.Add(stepPath);
            OpenOutputs(open.ToArray());
        }

        // Read every surface's shape, glass, and global frame from the LDE.
        static List<SurfInfo> GatherSurfaces(ZOSAPI.Editors.LDE.ILensDataEditor lde, int imgIdx, int stopIdx)
        {
            var surfs = new List<SurfInfo>();
            string prevMedium = "";
            // Include surface 0 (object) so keep-out can start at the object plane.
            for (int i = 0; i <= imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new SurfInfo { Index = i, Type = row.Type, IsStop = (i == stopIdx) };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                try { s.Conic = row.Conic; } catch { s.Conic = 0; }
                if (Math.Abs(s.Conic) > 1e10) s.Conic = 0;
                try { s.SemiDiameter = row.SemiDiameter; } catch { s.SemiDiameter = 0; }
                try { s.MechanicalSemiDiameter = row.MechanicalSemiDiameter; } catch { s.MechanicalSemiDiameter = 0; }
                try { s.Thickness = row.Thickness; } catch { s.Thickness = 0; }
                for (int p = 1; p <= 8; p++)
                {
                    try
                    {
                        var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(
                            typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                        s.Pars[p] = row.GetSurfaceCell(col).DoubleValue;
                    }
                    catch { s.Pars[p] = 0; }
                }
                string mat = (row.Material ?? "").Trim();
                s.Material = mat;
                if (mat == "-" || (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak && mat.Length == 0))
                    s.EffMedium = prevMedium;
                else
                    s.EffMedium = mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase) ? "" : mat;
                prevMedium = s.EffMedium;

                s.Frame = GetFrame(lde, i);
                s.IsDummy = IsDummySurface(s);
                // Draw glass (material on this surface starts a glass gap) and stop. Never CB/dummies.
                bool glassStart = !string.IsNullOrEmpty(s.Material)
                    && s.Material != "-"
                    && !s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
                    && s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak;
                s.Draw = !s.IsDummy && s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak
                    && (glassStart || s.IsStop || (!string.IsNullOrEmpty(s.EffMedium)
                        && s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak));
                // Refine: draw surfaces that bound glass or are stop. Mark glass-bound backs too.
                surfs.Add(s);
            }

            // Glass span: material on i means glass after i until next non-empty material change.
            // For drawing, any surface that is front or back of a glass element should have a section.
            for (int i = 0; i < surfs.Count; i++)
            {
                var a = surfs[i];
                if (a.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) { a.Draw = false; continue; }
                if (a.IsStop) { a.Draw = true; continue; }
                bool startsGlass = !string.IsNullOrEmpty(a.Material) && a.Material != "-"
                    && !a.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                if (startsGlass) { a.Draw = true; continue; }
                // Back of glass: previous surface had EffMedium non-empty (glass after prev).
                if (i > 0 && !string.IsNullOrEmpty(surfs[i - 1].EffMedium)
                    && surfs[i - 1].Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                {
                    a.Draw = true;
                    continue;
                }
                a.Draw = false;
            }
            return surfs;
        }

        // True if this surface is just a marker (no real glass to draw).
        static bool IsDummySurface(SurfInfo s)
        {
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) return true;
            if (s.IsStop) return false;
            bool hasMat = !string.IsNullOrEmpty(s.Material) && s.Material != "-"
                && !s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
            if (hasMat) return false;
            // Flat Standard (or similar) air surface with no power ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â dummy spacer.
            bool flat = s.Radius == 0 || Math.Abs(s.Radius) > 1e10;
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.Standard && flat) return true;
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.Paraxial) return true;
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY) return true;
            return false;
        }

        // Build YZ cross-section polylines for drawing each surface.
        static void BuildSections(ZOSAPI.Editors.LDE.ILensDataEditor lde, List<SurfInfo> surfs)
        {
            foreach (var s in surfs)
            {
                if (!s.Draw) continue;
                if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                if (!s.Frame.Valid) continue;
                double yCen = 0, yHalf = s.SemiDiameter;
                try
                {
                    var ad = lde.GetSurfaceAt(s.Index).ApertureData;
                    var st = ad.CurrentTypeSettings;
                    switch (ad.CurrentType)
                    {
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularAperture:
                            var r = (ZOSAPI.Editors.LDE.ISurfaceApertureRectangular)st;
                            yCen = r.ApertureYDecenter; yHalf = r.YHalfWidth; break;
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                            var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                            yCen = c.ApertureYDecenter; yHalf = c.MaximumRadius; break;
                        case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                            var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                            yCen = e.ApertureYDecenter; yHalf = e.YHalfWidth; break;
                    }
                }
                catch { }
                if (yHalf < 1e-9) continue;
                var pts = new List<PointF>();
                int nSteps = 48;
                for (int k = 0; k <= nSteps; k++)
                {
                    double y = yCen - yHalf + 2.0 * yHalf * k / nSteps;
                    double z = Sag(s, y);
                    if (double.IsNaN(z)) continue;
                    var g = s.Frame.ToGlobal(0, y, z);
                    pts.Add(new PointF((float)g.gz, (float)g.gy));
                }
                if (pts.Count >= 2) s.Section = pts;
            }
        }

        // Turn "auto" / "all" / "0,2,5" into the station surface list.
        static List<int> ResolveStations(string spec, List<SurfInfo> surfs, int imgIdx)
        {
            if (string.IsNullOrWhiteSpace(spec) || spec.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                // Default stations:
                //   - include finite object (surface 0); skip infinite conjugates
                //   - drawn optical surfaces (glass + stop) up through AutoLastStation
                //   - AutoLastStation = paraxial phone/stop when unused post-image air follows
                //     (e.g. Concept-24: through S9, exclude S10+S11); else formal image
                int last = AutoLastStation(surfs, imgIdx);
                var list = new List<int>();
                var obj = surfs.FirstOrDefault(s => s.Index == 0);
                if (obj != null && IsFiniteObjectStation(obj)) list.Add(0);
                foreach (var s in surfs)
                {
                    if (s.Index <= 0) continue;
                    if (s.Index > last) continue;
                    if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                    if (s.Draw || s.Index == last) list.Add(s.Index);
                }
                if (!list.Contains(last) && last >= 0) list.Add(last);
                list.Sort();
                return list.Distinct().ToList();
            }
            if (spec.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return surfs.Where(s => s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                    .Select(s => s.Index).ToList();
            }
            var result = new List<int>();
            foreach (var part in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                int dash = p.IndexOf('-');
                if (dash > 0)
                {
                    int a, b;
                    if (int.TryParse(p.Substring(0, dash), NumberStyles.Integer, CI, out a)
                        && int.TryParse(p.Substring(dash + 1), NumberStyles.Integer, CI, out b))
                    {
                        if (a > b) { int t = a; a = b; b = t; }
                        for (int i = a; i <= b; i++)
                            if (i >= 0 && i <= imgIdx) result.Add(i);
                    }
                }
                else
                {
                    int v;
                    if (int.TryParse(p, NumberStyles.Integer, CI, out v) && v >= 0 && v <= imgIdx)
                        result.Add(v);
                }
            }
            return result.Distinct().OrderBy(x => x).ToList();
        }

        // True if the object is at a finite distance (not infinity).
        static bool IsFiniteObjectStation(SurfInfo obj)
        {
            if (obj == null || obj.Index != 0) return false;
            if (!obj.Frame.Valid) return false;
            if (Math.Abs(obj.Frame.Z) > 1e8) return false;
            if (double.IsInfinity(obj.Thickness) || Math.Abs(obj.Thickness) > 1e8) return false;
            double semi = SanitizeRadius(Math.Abs(obj.SemiDiameter));
            // Finite conjugates have a usable object semi or finite thickness to surface 1.
            return semi > 0 || (Math.Abs(obj.Thickness) > 1e-12 && Math.Abs(obj.Thickness) < 1e8);
        }

        // Clamp weird / negative radii to something we can draw.
        static double SanitizeRadius(double r)
        {
            if (double.IsNaN(r) || double.IsInfinity(r)) return 0;
            if (Math.Abs(r) > 1e8) return 0;
            return Math.Max(0, r);
        }

        /// <summary>
        /// Last envelope station for -surfaces auto.
        /// If a Paraxial/ParaxialXY surface exists before the formal image and unused
        /// surfaces follow it (post-image flare / dummy air), stop there (phone plane).
        /// Otherwise use the formal image surface (Cooke and typical objectives).
        /// </summary>
        // Pick the last useful station (phone/image) for auto mode.
        static int AutoLastStation(List<SurfInfo> surfs, int imgIdx)
        {
            SurfInfo parax = null;
            foreach (var s in surfs)
            {
                if (s.Index <= 0) continue;
                if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.Paraxial
                    || s.Type == ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY)
                    parax = s;
            }
            if (parax != null && parax.Index < imgIdx)
            {
                bool trailingAfter = surfs.Any(s => s.Index > parax.Index
                    && s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak);
                if (trailingAfter)
                    return parax.Index;
            }
            return imgIdx;
        }

        /// <summary>
        /// Mechanical clear radius (CLAP / circular aperture / SemiDiameter).
        /// For Paraxial/phone stations with floating tiny DIAM, walk back to the
        /// previous drawn glass surface so vignette cannot pinch below mechanical CA.
        /// </summary>
        // Clear-aperture radius used only when -clap floors the envelope.
        static double ClearRadiusForStation(SurfInfo si, List<SurfInfo> surfs,
            ZOSAPI.Editors.LDE.ILensDataEditor lde)
        {
            double clap = ReadClearRadius(lde, si);
            bool paraxLike = si.Type == ZOSAPI.Editors.LDE.SurfaceType.Paraxial
                || si.Type == ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY
                || si.Index == 0;
            if (paraxLike || clap < 1e-9)
            {
                for (int i = surfs.Count - 1; i >= 0; i--)
                {
                    var p = surfs[i];
                    if (p.Index >= si.Index) continue;
                    if (p.Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                    if (p.Type == ZOSAPI.Editors.LDE.SurfaceType.Paraxial
                        || p.Type == ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY) continue;
                    if (!p.Draw && p.Index != 0) continue;
                    double prev = ReadClearRadius(lde, p);
                    if (prev > clap) clap = prev;
                    if (prev > 1e-9 && p.Index > 0) break; // nearest prior mechanical CA
                }
            }
            // Object: prefer its own SemiDiameter (often the entrance keep-out).
            if (si.Index == 0)
            {
                double semi = 0;
                try { semi = Math.Abs(si.SemiDiameter); } catch { }
                if (semi > clap) clap = semi;
            }
            return clap;
        }

        // Read CLAP / semi-diameter style clear radius from the surface.
        static double ReadClearRadius(ZOSAPI.Editors.LDE.ILensDataEditor lde, SurfInfo si)
        {
            double yHalf = 0;
            try { yHalf = Math.Abs(si.SemiDiameter); } catch { yHalf = 0; }
            try
            {
                var ad = lde.GetSurfaceAt(si.Index).ApertureData;
                var st = ad.CurrentTypeSettings;
                switch (ad.CurrentType)
                {
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularObscuration:
                        var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                        yHalf = Math.Max(yHalf, Math.Abs(c.MaximumRadius));
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularAperture:
                        var r = (ZOSAPI.Editors.LDE.ISurfaceApertureRectangular)st;
                        yHalf = Math.Max(yHalf, Math.Max(Math.Abs(r.XHalfWidth), Math.Abs(r.YHalfWidth)));
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                        var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                        yHalf = Math.Max(yHalf, Math.Max(Math.Abs(e.XHalfWidth), Math.Abs(e.YHalfWidth)));
                        break;
                }
            }
            catch { }
            return SanitizeRadius(yHalf);
        }

        /// <summary>
        /// Extreme-field chief-ray radial height at the station (field height at that plane).
        /// </summary>
        // How high the extreme field chief ray sits at this station.
        static double FieldHeightAtStation(
            ZOSAPI.IOpticalSystem sys, int surf, List<int> fieldList, int wave,
            double maxR, ZOSAPI.SystemData.IFields fields, Frame frame)
        {
            if (!frame.Valid || fieldList == null || fieldList.Count == 0 || maxR <= 0) return 0;
            double rmax = 0;
            var trace = sys.Tools.OpenBatchRayTrace();
            try
            {
                var data = trace.CreateNormUnpol(fieldList.Count, ZOSAPI.Tools.RayTrace.RaysType.Real, surf);
                foreach (int fi in fieldList)
                {
                    var f = fields.GetField(fi);
                    double hx = f.X / maxR, hy = f.Y / maxR;
                    data.AddRay(wave, hx, hy, 0, 0, ZOSAPI.Tools.RayTrace.OPDMode.None);
                }
                trace.RunAndWaitForCompletion();
                data.StartReadingResults();
                int rayNum, errCode, vigCode;
                double x, y, z, l, m, n, l2, m2, n2, opd, inten;
                while (data.ReadNextResult(out rayNum, out errCode, out vigCode,
                    out x, out y, out z, out l, out m, out n, out l2, out m2, out n2, out opd, out inten))
                {
                    if (errCode != 0) continue;
                    var g = frame.ToGlobal(x, y, z);
                    double rho = Math.Sqrt(g.gx * g.gx + g.gy * g.gy);
                    if (rho > rmax) rmax = rho;
                }
            }
            catch { }
            finally { try { trace.Close(); } catch { } }
            return rmax;
        }

        // n points around the pupil rim (radius 1 = full aperture).
        static List<(double px, double py)> BuildPupilRim(int n, double radius)
        {
            var list = new List<(double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                list.Add((radius * Math.Cos(a), radius * Math.Sin(a)));
            }
            return list;
        }

        /// <summary>
        /// Trace extreme-field pupil-rim rays to <paramref name="surf"/>.
        /// Updates <paramref name="rmax"/> to the max global radial hit and
        /// <paramref name="rimHitZ"/> to the global Z of that same max-R hit
        /// (surface rim Z, not vertex Z). Returns whether a finite rim hit exists.
        /// </summary>
        // Trace rim rays and return the farthest radial hit (and its Z).
        static bool TraceRimMaxRadius(
            ZOSAPI.IOpticalSystem sys, int surf, List<int> fieldList, int wave,
            List<(double px, double py)> samples, double maxR,
            ZOSAPI.SystemData.IFields fields, Frame frame,
            ref double rmax, ref double rimHitZ, ref int hits)
        {
            rmax = 0; rimHitZ = double.NaN; hits = 0;
            int nRays = fieldList.Count * samples.Count;
            if (nRays == 0) return false;
            var trace = sys.Tools.OpenBatchRayTrace();
            try
            {
                var data = trace.CreateNormUnpol(nRays, ZOSAPI.Tools.RayTrace.RaysType.Real, surf);
                foreach (int fi in fieldList)
                {
                    var f = fields.GetField(fi);
                    double hx = f.X / maxR, hy = f.Y / maxR;
                    foreach (var s in samples)
                        data.AddRay(wave, hx, hy, s.px, s.py, ZOSAPI.Tools.RayTrace.OPDMode.None);
                }
                trace.RunAndWaitForCompletion();
                data.StartReadingResults();
                int rayNum, errCode, vigCode;
                double x, y, z, l, m, n, l2, m2, n2, opd, inten;
                while (data.ReadNextResult(out rayNum, out errCode, out vigCode,
                    out x, out y, out z, out l, out m, out n, out l2, out m2, out n2, out opd, out inten))
                {
                    if (errCode != 0) continue;
                    // Keep vignetted hits: they still land on the surface and define extent to edges.
                    if (!frame.Valid) continue;
                    var g = frame.ToGlobal(x, y, z);
                    double rho = Math.Sqrt(g.gx * g.gx + g.gy * g.gy);
                    if (rho > rmax)
                    {
                        rmax = rho;
                        rimHitZ = g.gz;
                    }
                    hits++;
                }
            }
            finally { trace.Close(); }
            return hits > 0 && !double.IsNaN(rmax) && !double.IsInfinity(rmax) && rmax > 0
                && !double.IsNaN(rimHitZ) && !double.IsInfinity(rimHitZ);
        }

        /// <summary>
        /// Global Z of the surface at radial height R (local Y = R, X = 0):
        /// Frame.ToGlobal(0, R, Sag(surface, R)).gz. Untilited/centered this is
        /// Frame.Z + sag; with tilt the rim point is transformed properly.
        /// </summary>
        // Estimate rim Z from the surface sag formula when a ray hit Z is missing.
        static double RimZFromSag(SurfInfo si, double r)
        {
            double sag = Sag(si, r);
            if (double.IsNaN(sag) || double.IsInfinity(sag)) sag = 0;
            if (!si.Frame.Valid) return si.Frame.Z + sag;
            var g = si.Frame.ToGlobal(0, r, sag);
            return g.gz;
        }

        // Glass outlines for the PNG (kind tags glass vs air markers).
        static List<(List<PointF> pts, string kind)> BuildLensPolylines(List<SurfInfo> surfs)
        {
            var lines = new List<(List<PointF>, string)>();
            for (int i = 0; i < surfs.Count; i++)
            {
                var a = surfs[i];
                bool startsGlass = !string.IsNullOrEmpty(a.Material) && a.Material != "-"
                    && !a.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
                    && a.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak;
                if (!startsGlass || a.Section == null) continue;
                // Find next drawn surface with section (back of element).
                SurfInfo b = null;
                for (int j = i + 1; j < surfs.Count; j++)
                {
                    if (surfs[j].Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                    if (surfs[j].Section == null) continue;
                    b = surfs[j];
                    break;
                }
                if (b == null) continue;
                lines.Add((a.Section, "lens"));
                lines.Add((b.Section, "lens"));
                lines.Add((new List<PointF> { a.Section.First(), b.Section.First() }, "edge"));
                lines.Add((new List<PointF> { a.Section.Last(), b.Section.Last() }, "edge"));
            }
            return lines;
        }

        // Stop aperture marks for the PNG.
        static List<List<PointF>> BuildStopPolylines(List<SurfInfo> surfs)
        {
            var lines = new List<List<PointF>>();
            foreach (var s in surfs.Where(x => x.IsStop && x.Section != null && x.Section.Count >= 2))
            {
                // Draw stop as the surface section (aperture edge).
                lines.Add(s.Section);
                // Also a vertical tick at +/- semi if section is flat-ish.
                var first = s.Section.First();
                var last = s.Section.Last();
                double zc = 0.5 * (first.X + last.X);
                lines.Add(new List<PointF> {
                    new PointF((float)zc, first.Y),
                    new PointF((float)zc, last.Y)
                });
            }
            return lines;
        }

        // Closed RZ profiles for MEMA L1/L2/... STEP solids.
        static List<LensSolid> BuildLensSolids(List<SurfInfo> surfs)
        {
            // Full MEMA blanks (MechanicalSemiDiameter), named L1/L2/L3...
            // Optical faces sampled to CLAP/Semi; flange out to MEMA OD.
            // NOT the old CLAP-stub LENS_* naming.
            var solids = new List<LensSolid>();
            int lensOrd = 0;
            for (int i = 0; i < surfs.Count; i++)
            {
                var a = surfs[i];
                bool startsGlass = !string.IsNullOrEmpty(a.Material) && a.Material != "-"
                    && !a.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
                    && a.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak;
                if (!startsGlass) continue;
                SurfInfo b = null;
                for (int j = i + 1; j < surfs.Count; j++)
                {
                    if (surfs[j].Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak) continue;
                    b = surfs[j];
                    break;
                }
                if (b == null) continue;
                lensOrd++;
                double clapA = Math.Max(SanitizeRadius(Math.Abs(a.SemiDiameter)), 1e-6);
                double clapB = Math.Max(SanitizeRadius(Math.Abs(b.SemiDiameter)), 1e-6);
                double memaA = SanitizeRadius(Math.Abs(a.MechanicalSemiDiameter));
                double memaB = SanitizeRadius(Math.Abs(b.MechanicalSemiDiameter));
                double mema = Math.Max(Math.Max(memaA, memaB), Math.Max(clapA, clapB));
                if (!(mema > 0)) mema = Math.Max(clapA, clapB);
                double t = a.Thickness;
                int n = 24;
                var profile = new List<(double z, double r)>();
                // Front optical: axis -> CLAP
                for (int k = 0; k <= n; k++)
                {
                    double r = clapA * k / n;
                    double z = Sag(a, r);
                    if (double.IsNaN(z)) z = 0;
                    profile.Add((z, r));
                }
                double zFrontClap = Sag(a, clapA);
                if (double.IsNaN(zFrontClap)) zFrontClap = 0;
                // Front land CLAP -> MEMA (constant Z at front CA rim)
                if (mema > clapA + 1e-9)
                {
                    for (int k = 1; k <= 8; k++)
                    {
                        double r = clapA + (mema - clapA) * k / 8.0;
                        profile.Add((zFrontClap, r));
                    }
                }
                double zBackClap = Sag(b, clapB);
                if (double.IsNaN(zBackClap)) zBackClap = 0;
                double zBackAtMema = t + zBackClap;
                double zFrontAtMema = zFrontClap;
                // Edge at MEMA (front land Z -> back land Z)
                if (Math.Abs(zBackAtMema - zFrontAtMema) > 1e-12)
                    profile.Add((zBackAtMema, mema));
                // Back land MEMA -> CLAP
                if (mema > clapB + 1e-9)
                {
                    for (int k = 7; k >= 0; k--)
                    {
                        double r = clapB + (mema - clapB) * k / 8.0;
                        profile.Add((t + zBackClap, r));
                    }
                }
                else
                {
                    profile.Add((t + zBackClap, clapB));
                }
                // Back optical: CLAP -> axis
                for (int k = n - 1; k >= 0; k--)
                {
                    double r = clapB * k / n;
                    double zb = Sag(b, r);
                    if (double.IsNaN(zb)) zb = 0;
                    profile.Add((t + zb, r));
                }
                solids.Add(new LensSolid
                {
                    Name = "L" + lensOrd.ToString(CI),
                    ProfileRz = profile,
                    Frame = a.Frame
                });
            }
            return solids;
        }

        // Make a safe STEP / file name fragment.
        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "GLASS";
            var chars = s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            return new string(chars);
        }

        // Decide PNG and STEP output paths from -out / lens name.
        static void ResolveOutPaths(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys,
            out string pngPath, out string stepPath)
        {
            string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
            string stem = string.IsNullOrEmpty(src) ? "ray_extent" : Path.GetFileNameWithoutExtension(src);
            string dir = string.IsNullOrEmpty(src)
                ? (app.ZemaxDataDir ?? ".")
                : (Path.GetDirectoryName(src) ?? ".");
            string basePath = null;

            if (!string.IsNullOrEmpty(Opts.OutPath))
            {
                string o = Opts.OutPath.Trim();
                if (Directory.Exists(o) || o.EndsWith("\\") || o.EndsWith("/"))
                {
                    dir = o.TrimEnd('\\', '/');
                    Directory.CreateDirectory(dir);
                    basePath = Path.Combine(dir, stem + "_RayExtentEnvelope");
                }
                else
                {
                    string ext = Path.GetExtension(o).ToLowerInvariant();
                    if (ext == ".png" || ext == ".step" || ext == ".stp")
                    {
                        dir = Path.GetDirectoryName(o) ?? dir;
                        Directory.CreateDirectory(dir);
                        basePath = Path.Combine(dir, Path.GetFileNameWithoutExtension(o));
                    }
                    else
                    {
                        dir = Path.GetDirectoryName(o) ?? dir;
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        basePath = o;
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(dir);
                basePath = Path.Combine(dir, stem + "_RayExtentEnvelope");
            }

            pngPath = basePath + ".png";
            stepPath = basePath + ".step";
        }

        // Ask OpticStudio for this surface's global matrix.
        static Frame GetFrame(ZOSAPI.Editors.LDE.ILensDataEditor lde, int surf)
        {
            var fr = new Frame();
            double r11, r12, r13, r21, r22, r23, r31, r32, r33, x, y, z;
            try
            {
                fr.Valid = lde.GetGlobalMatrix(surf, out r11, out r12, out r13, out r21, out r22, out r23,
                    out r31, out r32, out r33, out x, out y, out z);
                fr.R[0, 0] = r11; fr.R[0, 1] = r12; fr.R[0, 2] = r13;
                fr.R[1, 0] = r21; fr.R[1, 1] = r22; fr.R[1, 2] = r23;
                fr.R[2, 0] = r31; fr.R[2, 1] = r32; fr.R[2, 2] = r33;
                fr.X = x; fr.Y = y; fr.Z = z;
            }
            catch { fr.Valid = false; }
            return fr;
        }

        // Surface sag z(y) for spheres / conics / even aspheres we understand.
        static double Sag(SurfInfo s, double y)
        {
            double z = 0;
            switch (s.Type)
            {
                case ZOSAPI.Editors.LDE.SurfaceType.Tilted:
                    return y * s.Pars[2];
                case ZOSAPI.Editors.LDE.SurfaceType.Paraxial:
                case ZOSAPI.Editors.LDE.SurfaceType.ParaxialXY:
                    return 0;
            }
            if (Math.Abs(s.Radius) > 1e10 || s.Radius == 0)
                z = 0;
            else
            {
                double c = 1.0 / s.Radius;
                double disc = 1 - (1 + s.Conic) * c * c * y * y;
                if (disc < 0) return double.NaN;
                z = c * y * y / (1 + Math.Sqrt(disc));
            }
            if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric)
            {
                double y2 = y * y, term = y2;
                for (int p = 1; p <= 8; p++) { z += s.Pars[p] * term; term *= y2; }
            }
            else if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere)
            {
                double ay = Math.Abs(y), term = ay;
                for (int p = 1; p <= 8; p++) { z += s.Pars[p] * term; term *= ay; }
            }
            return z;
        }
    }
}

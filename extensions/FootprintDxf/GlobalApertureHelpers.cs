using System;
using System.Collections.Generic;

namespace FootprintDxf
{
    partial class Program
    {
        // LayoutRender-style global frame from LDE.GetGlobalMatrix.
        // Maps local (x,y,z) -> global; FootprintDxf uses z=0 and drops gz (2D).
        struct GlobalFrame
        {
            public bool Valid;
            public double R00, R01, R02, R10, R11, R12, R20, R21, R22;
            public double X, Y, Z;

            public ConvexHull.Pt ToGlobalXY(double lx, double ly, double lz = 0.0)
            {
                double gx = R00 * lx + R01 * ly + R02 * lz + X;
                double gy = R10 * lx + R11 * ly + R12 * lz + Y;
                return new ConvexHull.Pt(gx, gy);
            }
        }

        static GlobalFrame TryGetGlobalFrame(ZOSAPI.Editors.LDE.ILensDataEditor lde, int surf)
        {
            var fr = new GlobalFrame();
            try
            {
                double r11, r12, r13, r21, r22, r23, r31, r32, r33, x, y, z;
                fr.Valid = lde.GetGlobalMatrix(surf, out r11, out r12, out r13, out r21, out r22, out r23,
                    out r31, out r32, out r33, out x, out y, out z);
                fr.R00 = r11; fr.R01 = r12; fr.R02 = r13;
                fr.R10 = r21; fr.R11 = r22; fr.R12 = r23;
                fr.R20 = r31; fr.R21 = r32; fr.R22 = r33;
                fr.X = x; fr.Y = y; fr.Z = z;
            }
            catch { fr.Valid = false; }
            return fr;
        }

        // Pure helper: apply a 3x3 + translation to (x,y) with z=0 (selftest / docs).
        static ConvexHull.Pt LocalToGlobalXY(
            double r00, double r01, double r02,
            double r10, double r11, double r12,
            double tx, double ty,
            double lx, double ly, double lz = 0.0)
        {
            return new ConvexHull.Pt(
                r00 * lx + r01 * ly + r02 * lz + tx,
                r10 * lx + r11 * ly + r12 * lz + ty);
        }

        static List<ConvexHull.Pt> TransformHitsXY(List<ConvexHull.Pt> hits, GlobalFrame fr)
        {
            if (hits == null || hits.Count == 0 || !fr.Valid) return hits ?? new List<ConvexHull.Pt>();
            var outHits = new List<ConvexHull.Pt>(hits.Count);
            for (int i = 0; i < hits.Count; i++)
            {
                var p = hits[i];
                outHits.Add(fr.ToGlobalXY(p.X, p.Y, 0.0));
            }
            return outHits;
        }

        static Dictionary<int, List<ConvexHull.Pt>> TransformHitsByFieldXY(
            Dictionary<int, List<ConvexHull.Pt>> byField, GlobalFrame fr)
        {
            if (byField == null || !fr.Valid) return byField;
            var outMap = new Dictionary<int, List<ConvexHull.Pt>>();
            foreach (var kv in byField)
                outMap[kv.Key] = TransformHitsXY(kv.Value, fr);
            return outMap;
        }

        // Closed circle/ellipse ring in local XY (N verts, no repeated first).
        static List<ConvexHull.Pt> MakeEllipseRing(double cx, double cy, double rx, double ry, int n = 64)
        {
            if (n < 8) n = 8;
            if (rx < 0) rx = -rx;
            if (ry < 0) ry = -ry;
            var pts = new List<ConvexHull.Pt>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                pts.Add(new ConvexHull.Pt(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }
            return pts;
        }

        // Clear-aperture overlay from ZOS-API. Circular / elliptical / SemiDiameter
        // fallback. Rectangular and other types -> skip with WARNING (no fail).
        static bool TryBuildApertureOverlay(
            ZOSAPI.Editors.LDE.ILensDataEditor lde, int surf,
            out List<ConvexHull.Pt> verts, out string kind, out string warn)
        {
            verts = null;
            kind = null;
            warn = null;
            try
            {
                var row = lde.GetSurfaceAt(surf);
                var ad = row.ApertureData;
                var st = ad.CurrentTypeSettings;
                switch (ad.CurrentType)
                {
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                    {
                        var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                        double r = c.MaximumRadius;
                        double dx = 0, dy = 0;
                        try { dx = c.ApertureXDecenter; } catch { }
                        try { dy = c.ApertureYDecenter; } catch { }
                        if (r <= 1e-12)
                        {
                            warn = "CircularAperture MaximumRadius <= 0";
                            return false;
                        }
                        verts = MakeEllipseRing(dx, dy, r, r);
                        kind = "CircularAperture r=" + r.ToString("0.####", CI);
                        return true;
                    }
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                    {
                        var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                        double hx = e.XHalfWidth, hy = e.YHalfWidth;
                        double dx = 0, dy = 0;
                        try { dx = e.ApertureXDecenter; } catch { }
                        try { dy = e.ApertureYDecenter; } catch { }
                        if (hx <= 1e-12 || hy <= 1e-12)
                        {
                            warn = "EllipticalAperture half-width <= 0";
                            return false;
                        }
                        verts = MakeEllipseRing(dx, dy, hx, hy);
                        kind = "EllipticalAperture " + hx.ToString("0.####", CI) + "x" + hy.ToString("0.####", CI);
                        return true;
                    }
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.None:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.FloatingAperture:
                    {
                        double sd = 0;
                        try { sd = row.SemiDiameter; } catch { sd = 0; }
                        if (sd <= 1e-12)
                        {
                            warn = "no clear SemiDiameter for aperture type " + ad.CurrentType;
                            return false;
                        }
                        verts = MakeEllipseRing(0, 0, sd, sd);
                        kind = "SemiDiameter r=" + sd.ToString("0.####", CI);
                        return true;
                    }
                    default:
                        warn = "aperture type " + ad.CurrentType + " is not circular/elliptical; skipping";
                        return false;
                }
            }
            catch (Exception ex)
            {
                warn = "aperture read failed: " + ex.Message;
                return false;
            }
        }

        static bool GlobalTransformSelfCheck(out string detail)
        {
            // Identity + translation (1,2): (3,4) -> (4,6)
            var p = LocalToGlobalXY(1, 0, 0, 0, 1, 0, 1, 2, 3, 4, 0);
            if (Math.Abs(p.X - 4) > 1e-12 || Math.Abs(p.Y - 6) > 1e-12)
            { detail = "identity+translate failed: " + p.X + "," + p.Y; return false; }
            // 90 deg CCW about Z: (1,0) -> (0,1) with origin at 0
            // R = [[0,-1,0],[1,0,0],...]
            p = LocalToGlobalXY(0, -1, 0, 1, 0, 0, 0, 0, 1, 0, 0);
            if (Math.Abs(p.X - 0) > 1e-12 || Math.Abs(p.Y - 1) > 1e-12)
            { detail = "90deg failed: " + p.X + "," + p.Y; return false; }
            detail = "ok";
            return true;
        }

        static bool ApertureRingSelfCheck(out string detail)
        {
            var c = MakeEllipseRing(0, 0, 2, 2, 8);
            if (c.Count != 8) { detail = "circle count " + c.Count; return false; }
            if (Math.Abs(c[0].X - 2) > 1e-12 || Math.Abs(c[0].Y) > 1e-12)
            { detail = "circle east vert " + c[0].X + "," + c[0].Y; return false; }
            var e = MakeEllipseRing(1, -1, 3, 1, 4);
            if (e.Count != 4) { detail = "ellipse count"; return false; }
            if (Math.Abs(e[0].X - 4) > 1e-12 || Math.Abs(e[0].Y - (-1)) > 1e-12)
            { detail = "ellipse east " + e[0].X + "," + e[0].Y; return false; }
            detail = "ok";
            return true;
        }
    }
}

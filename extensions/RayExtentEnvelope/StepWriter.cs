using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RayExtentEnvelope
{
    // Minimal AP214 STEP writer: faceted BREP solids of revolution (lenses + envelope).
    // Pure C# — no external CAD kernel.
    static class StepWriter
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        class Vec3
        {
            public double X, Y, Z;
            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        class Mesh
        {
            public string Name;
            public List<Vec3> Verts = new List<Vec3>();
            public List<int[]> Tris = new List<int[]>(); // each 3 indices, outward CCW
            public int Add(Vec3 v)
            {
                Verts.Add(v);
                return Verts.Count - 1;
            }
        }

        public static void Write(
            string path,
            IList<(string Name, List<(double z, double r)> ProfileRz, double[,] R, double tx, double ty, double tz, bool frameValid)> lenses,
            IList<(double Z, double Rmax)> env,
            int circSegs)
        {
            if (circSegs < 8) circSegs = 8;
            var meshes = new List<Mesh>();

            foreach (var lens in lenses)
            {
                if (lens.ProfileRz == null || lens.ProfileRz.Count < 4) continue;
                var m = RevolveProfile(lens.ProfileRz, circSegs, lens.Name);
                if (lens.frameValid)
                    TransformMesh(m, lens.R, lens.tx, lens.ty, lens.tz);
                meshes.Add(m);
            }

            if (env != null && env.Count >= 2)
            {
                var profile = new List<(double z, double r)>();
                profile.Add((env[0].Z, 0));
                foreach (var s in env) profile.Add((s.Z, Math.Max(s.Rmax, 1e-9)));
                profile.Add((env[env.Count - 1].Z, 0));
                // close along axis back to start (revolve closes itself; profile should be a loop in RZ)
                // Ensure closed: last to first along r=0 if needed
                if (Math.Abs(profile[profile.Count - 1].r) > 1e-15 || Math.Abs(profile[0].z - profile[profile.Count - 1].z) > 1e-12)
                {
                    // already ends on axis; add start axis point if first isn't on axis
                }
                if (profile[0].r > 1e-15) profile.Insert(0, (profile[0].z, 0));
                meshes.Add(RevolveProfile(profile, circSegs, "RAY_ENVELOPE"));
            }

            if (meshes.Count == 0)
                throw new Exception("StepWriter: no solids to write");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            File.WriteAllText(path, EmitStep(meshes), new UTF8Encoding(false));
        }

        static Mesh RevolveProfile(List<(double z, double r)> profile, int nSeg, string name)
        {
            // profile is a closed polyline in RZ (r>=0). We revolve about Z.
            // Deduplicate consecutive points.
            var prof = new List<(double z, double r)>();
            foreach (var p in profile)
            {
                double r = Math.Max(0, p.r);
                if (prof.Count > 0)
                {
                    var q = prof[prof.Count - 1];
                    if (Math.Abs(q.z - p.z) < 1e-12 && Math.Abs(q.r - r) < 1e-12) continue;
                }
                prof.Add((p.z, r));
            }
            if (prof.Count >= 2)
            {
                var a = prof[0]; var b = prof[prof.Count - 1];
                if (Math.Abs(a.z - b.z) < 1e-12 && Math.Abs(a.r - b.r) < 1e-12)
                    prof.RemoveAt(prof.Count - 1);
            }
            if (prof.Count < 3)
                throw new Exception("StepWriter: profile too short for " + name);

            var mesh = new Mesh { Name = name };
            int nRing = prof.Count;
            // rings[i][j] = vertex index for profile point i at angle j
            var rings = new int[nRing][];
            for (int i = 0; i < nRing; i++)
            {
                rings[i] = new int[nSeg];
                double z = prof[i].z, r = prof[i].r;
                if (r < 1e-12)
                {
                    int idx = mesh.Add(new Vec3(0, 0, z));
                    for (int j = 0; j < nSeg; j++) rings[i][j] = idx;
                }
                else
                {
                    for (int j = 0; j < nSeg; j++)
                    {
                        double a = 2.0 * Math.PI * j / nSeg;
                        rings[i][j] = mesh.Add(new Vec3(r * Math.Cos(a), r * Math.Sin(a), z));
                    }
                }
            }

            for (int i = 0; i < nRing; i++)
            {
                int i1 = (i + 1) % nRing;
                bool axis0 = prof[i].r < 1e-12;
                bool axis1 = prof[i1].r < 1e-12;
                for (int j = 0; j < nSeg; j++)
                {
                    int j1 = (j + 1) % nSeg;
                    int a = rings[i][j], b = rings[i][j1], c = rings[i1][j1], d = rings[i1][j];
                    if (axis0 && axis1) continue;
                    if (axis0)
                    {
                        // triangle a(=axis), c, d  — but a==b
                        if (c != d && a != c) mesh.Tris.Add(new[] { a, c, d });
                    }
                    else if (axis1)
                    {
                        if (a != b && a != d) mesh.Tris.Add(new[] { a, b, d });
                    }
                    else
                    {
                        if (a != b && b != c) mesh.Tris.Add(new[] { a, b, c });
                        if (a != c && c != d) mesh.Tris.Add(new[] { a, c, d });
                    }
                }
            }
            return mesh;
        }

        static void TransformMesh(Mesh m, double[,] R, double tx, double ty, double tz)
        {
            for (int i = 0; i < m.Verts.Count; i++)
            {
                var v = m.Verts[i];
                // local (x,y,z) -> global; R rows are global basis of local axes (GetGlobalMatrix layout)
                double gx = R[0, 0] * v.X + R[0, 1] * v.Y + R[0, 2] * v.Z + tx;
                double gy = R[1, 0] * v.X + R[1, 1] * v.Y + R[1, 2] * v.Z + ty;
                double gz = R[2, 0] * v.X + R[2, 1] * v.Y + R[2, 2] * v.Z + tz;
                m.Verts[i] = new Vec3(gx, gy, gz);
            }
        }

        static string EmitStep(List<Mesh> meshes)
        {
            var sb = new StringBuilder();
            int id = 1;
            int Next() => id++;

            sb.AppendLine("ISO-10303-21;");
            sb.AppendLine("HEADER;");
            sb.AppendLine("FILE_DESCRIPTION(('RayExtentEnvelope faceted BREP'),'2;1');");
            sb.AppendLine("FILE_NAME('RayExtentEnvelope.step','" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CI)
                + "',('RayExtentEnvelope'),('Zemax User Extension'),");
            sb.AppendLine("  'RayExtentEnvelope','RayExtentEnvelope','');");
            sb.AppendLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));");
            sb.AppendLine("ENDSEC;");
            sb.AppendLine("DATA;");

            int appCtx = Next();
            sb.AppendLine(string.Format(CI, "#{0}=APPLICATION_CONTEXT('core data for automotive mechanical design processes');", appCtx));
            int appProto = Next();
            sb.AppendLine(string.Format(CI, "#{0}=APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{1});", appProto, appCtx));
            int prodCtx = Next();
            sb.AppendLine(string.Format(CI, "#{0}=PRODUCT_CONTEXT('',#{1},'mechanical');", prodCtx, appCtx));
            int defCtx = Next();
            sb.AppendLine(string.Format(CI, "#{0}=PRODUCT_DEFINITION_CONTEXT('design',#{1},'');", defCtx, appCtx));

            int lenUnit = Next();
            sb.AppendLine(string.Format(CI, "#{0}=(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.));", lenUnit));
            int geomCtx = Next();
            // uncertainty
            int unc = Next();
            sb.AppendLine(string.Format(CI, "#{0}=UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-6),#{1},'distance_accuracy_value','confusion accuracy');", unc, lenUnit));
            sb.AppendLine(string.Format(CI,
                "#{0}=(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{1}))GLOBAL_UNIT_ASSIGNED_CONTEXT((#{2}))REPRESENTATION_CONTEXT('Context','3D'));",
                geomCtx, unc, lenUnit));

            var shapeItems = new List<int>();
            int solidIdx = 0;
            foreach (var mesh in meshes)
            {
                solidIdx++;
                string pname = Sanitize(mesh.Name);
                int prod = Next();
                sb.AppendLine(string.Format(CI, "#{0}=PRODUCT('{1}','{1}','',(#{2}));", prod, pname, prodCtx));
                int pdf = Next();
                sb.AppendLine(string.Format(CI, "#{0}=PRODUCT_DEFINITION_FORMATION('',#{1});", pdf, prod));
                int pd = Next();
                sb.AppendLine(string.Format(CI, "#{0}=PRODUCT_DEFINITION('design','',#{1},#{2});", pd, pdf, defCtx));
                int pds = Next();
                sb.AppendLine(string.Format(CI, "#{0}=PRODUCT_DEFINITION_SHAPE('','',#{1});", pds, pd));

                // points
                var pointIds = new int[mesh.Verts.Count];
                for (int i = 0; i < mesh.Verts.Count; i++)
                {
                    var v = mesh.Verts[i];
                    pointIds[i] = Next();
                    sb.AppendLine(string.Format(CI, "#{0}=CARTESIAN_POINT('',({1:G17},{2:G17},{3:G17}));",
                        pointIds[i], v.X, v.Y, v.Z));
                }

                var faceIds = new List<int>();
                foreach (var t in mesh.Tris)
                {
                    if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) continue;
                    int p0 = pointIds[t[0]], p1 = pointIds[t[1]], p2 = pointIds[t[2]];
                    int loop = Next();
                    sb.AppendLine(string.Format(CI, "#{0}=POLY_LOOP('',(#{1},#{2},#{3}));", loop, p0, p1, p2));
                    int bound = Next();
                    sb.AppendLine(string.Format(CI, "#{0}=FACE_OUTER_BOUND('',#{1},.T.);", bound, loop));
                    int face = Next();
                    sb.AppendLine(string.Format(CI, "#{0}=FACE('',(#{1}),.T.);", face, bound));
                    faceIds.Add(face);
                }
                if (faceIds.Count == 0) continue;

                int shell = Next();
                sb.AppendLine(string.Format(CI, "#{0}=CLOSED_SHELL('',({1}));", shell,
                    string.Join(",", faceIds.Select(f => "#" + f.ToString(CI)))));
                int brep = Next();
                sb.AppendLine(string.Format(CI, "#{0}=FACETED_BREP('{1}',#{2});", brep, pname, shell));
                int shapeRep = Next();
                sb.AppendLine(string.Format(CI, "#{0}=ADVANCED_BREP_SHAPE_REPRESENTATION('{1}',(#{2}),#{3});",
                    shapeRep, pname, brep, geomCtx));
                int sdr = Next();
                sb.AppendLine(string.Format(CI, "#{0}=SHAPE_DEFINITION_REPRESENTATION(#{1},#{2});", sdr, pds, shapeRep));
                shapeItems.Add(brep);
            }

            sb.AppendLine("ENDSEC;");
            sb.AppendLine("END-ISO-10303-21;");
            if (shapeItems.Count == 0)
                throw new Exception("StepWriter: failed to emit any FACETED_BREP solids");
            return sb.ToString();
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "SOLID";
            var chars = s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            string t = new string(chars);
            if (t.Length > 40) t = t.Substring(0, 40);
            return t;
        }
    }
}

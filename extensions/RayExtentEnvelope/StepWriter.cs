using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;

namespace RayExtentEnvelope
{
    // ============================================================
    // StepWriter - write STEP solids of revolution
    // ============================================================
    // Builds a STEP file with a KEEP_OUT tube (the ray envelope) and
    // optional MEMA L1/L2/... lens solids spun from RZ profiles.
    // Mech CAD can import this for packaging keep-outs.
    // ============================================================

    static class StepWriter
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        class Vec3
        {
            public double X, Y, Z;
            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        // Triangle mesh we can revolve and export.
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

        // Write the STEP file with KEEP_OUT and any lens solids passed in.
        // Write KEEP_OUT (+ optional lens meshes) as STEP, with STL fallback helpers.
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
                meshes.Add(RevolveProfile(profile, circSegs, "KEEP_OUT"));
            }

            if (meshes.Count == 0)
                throw new Exception("StepWriter: no solids to write");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            string stlTmp = path + ".mesh.stl";
            WriteAsciiStl(stlTmp, meshes);
            if (TryOccRhinoStep(stlTmp, path))
            {
                LastWriteMode = "OCC named-solids UnifySameDomain AP214IS/MM (Rhino)";
                try { File.Delete(stlTmp); } catch { }
                return;
            }
            File.WriteAllText(path, EmitStep(meshes), new UTF8Encoding(false));
            LastWriteMode = "FACETED_BREP fallback (OCC/python unavailable)";
            try { File.Delete(stlTmp); } catch { }
        }

        /// <summary>How the last Write produced STEP: OCC Rhino path or faceted fallback.</summary>
        public static string LastWriteMode = "";

        // Spin an RZ profile around Z into a triangle mesh.
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
                        // triangle a(=axis), c, d  ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â but a==b
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

        // Move a mesh into a surface's global frame.
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

        // Turn meshes into STEP text (or ask OpenCascade/Rhino if available).
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


        // Fallback: write an ASCII STL if STEP tooling is missing.
        static void WriteAsciiStl(string path, List<Mesh> meshes)
        {
            var sb = new StringBuilder();
            foreach (var mesh in meshes)
            {
                string sname = Sanitize(string.IsNullOrEmpty(mesh.Name) ? "SOLID" : mesh.Name);
                sb.AppendLine("solid " + sname);
                foreach (var t in mesh.Tris)
                {
                    if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) continue;
                    var a = mesh.Verts[t[0]];
                    var b = mesh.Verts[t[1]];
                    var c = mesh.Verts[t[2]];
                    double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                    double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                    double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl > 1e-30) { nx /= nl; ny /= nl; nz /= nl; }
                    sb.AppendLine(string.Format(CI, "  facet normal {0:G17} {1:G17} {2:G17}", nx, ny, nz));
                    sb.AppendLine("    outer loop");
                    sb.AppendLine(string.Format(CI, "      vertex {0:G17} {1:G17} {2:G17}", a.X, a.Y, a.Z));
                    sb.AppendLine(string.Format(CI, "      vertex {0:G17} {1:G17} {2:G17}", b.X, b.Y, b.Z));
                    sb.AppendLine(string.Format(CI, "      vertex {0:G17} {1:G17} {2:G17}", c.X, c.Y, c.Z));
                    sb.AppendLine("    endloop");
                    sb.AppendLine("  endfacet");
                }
                sb.AppendLine("endsolid " + sname);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // Try OpenCascade or Rhino scripting to emit a real STEP solid.
        static bool TryOccRhinoStep(string stlPath, string stepPath)
        {
            string script = FindOccScript();
            if (script == null) return false;
            string py = FindPython();
            if (py == null) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = py,
                    Arguments = (py == "py" ? "-3 " : "") + "\"" + script + "\" \"" + stlPath + "\" \"" + stepPath + "\" --named-solids",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } return false; }
                    if (p.ExitCode != 0) return false;
                }
                if (!File.Exists(stepPath) || new FileInfo(stepPath).Length < 64) return false;
                                // Rhino-friendly: MANIFOLD + ADVANCED_FACE + mm. Multi-solid OK (KEEP_OUT+L*).
                string head = File.ReadAllText(stepPath);
                if (head.IndexOf("MANIFOLD_SOLID_BREP", StringComparison.Ordinal) < 0) return false;
                if (head.IndexOf("ADVANCED_FACE", StringComparison.Ordinal) < 0) return false;
                if (head.IndexOf("MILLI", StringComparison.Ordinal) < 0
                    || head.IndexOf("METRE", StringComparison.Ordinal) < 0) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Locate the helper script that talks to OpenCascade.
        static string FindOccScript()
        {
            string env = Environment.GetEnvironmentVariable("RAYEXTENT_STL_TO_STEP");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            var cands = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            cands.Add(Path.Combine(baseDir, "stl_to_rhino_step.py"));
            cands.Add(Path.Combine(baseDir, "tools", "stl_to_rhino_step.py"));
            // Walk up from baseDir and from cwd looking for tools/stl_to_rhino_step.py
            foreach (string start in new[] { baseDir, Directory.GetCurrentDirectory() })
            {
                try
                {
                    var dir = new DirectoryInfo(Path.GetFullPath(start));
                    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                    {
                        cands.Add(Path.Combine(dir.FullName, "tools", "stl_to_rhino_step.py"));
                        cands.Add(Path.Combine(dir.FullName, "stl_to_rhino_step.py"));
                    }
                }
                catch { }
            }
            foreach (var c in cands)
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            return null;
        }

        // Find a Python executable for the OpenCascade helper.
        static string FindPython()
        {
            string env = Environment.GetEnvironmentVariable("RAYEXTENT_PYTHON");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
            foreach (string name in new[] { "python", "python3", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = name,
                        Arguments = name == "py" ? "-3 -c \"import OCP; print('ok')\"" : "-c \"import OCP; print('ok')\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (p == null) continue;
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(15000);
                        if (p.ExitCode == 0 && o.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0)
                            return name == "py" ? "py" : name;
                    }
                }
                catch { }
            }
            // py launcher needs special args prefix when invoking script
            return null;
        }

        // Make a safe name fragment for STEP entities / files.
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

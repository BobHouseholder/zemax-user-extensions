using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FootprintDxf
{
    // Minimal DXF R12 / AC1009 ASCII writer. POLYLINE + VERTEX + SEQEND for
    // maximum compatibility with ancient mechanical CAD. Coordinates are local
    // surface XY in OpticStudio lens units (typically mm).
    static class DxfWriter
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public class LayerPoly
        {
            public string LayerName;
            public string Comment; // optional DXF COMMENT entity text
            public List<ConvexHull.Pt> Vertices; // closed by repeating first at end if needed
        }

        public static void Write(string path, IList<LayerPoly> polys, string title)
        {
            var sb = new StringBuilder(4096);
            void Pair(int code, string val)
            {
                sb.Append(code.ToString(CI));
                sb.Append("\r\n");
                sb.Append(val);
                sb.Append("\r\n");
            }
            void PairD(int code, double v) => Pair(code, v.ToString("0.############", CI));

            Pair(0, "SECTION");
            Pair(2, "HEADER");
            Pair(9, "$ACADVER");
            Pair(1, "AC1009");
            Pair(9, "$INSUNITS");
            Pair(70, "4"); // millimetres — OpticStudio lens units are usually mm; document in README
            Pair(0, "ENDSEC");

            Pair(0, "SECTION");
            Pair(2, "TABLES");
            Pair(0, "TABLE");
            Pair(2, "LAYER");
            Pair(70, Math.Max(1, polys.Count).ToString(CI));
            // Layer 0 always present
            Pair(0, "LAYER");
            Pair(2, "0");
            Pair(70, "0");
            Pair(62, "7");
            Pair(6, "CONTINUOUS");
            int color = 1;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0" };
            foreach (var p in polys)
            {
                string name = SanitizeLayer(p.LayerName);
                if (!seen.Add(name)) continue;
                Pair(0, "LAYER");
                Pair(2, name);
                Pair(70, "0");
                Pair(62, (color++ % 7 + 1).ToString(CI));
                Pair(6, "CONTINUOUS");
            }
            Pair(0, "ENDTAB");
            Pair(0, "ENDSEC");

            Pair(0, "SECTION");
            Pair(2, "ENTITIES");

            if (!string.IsNullOrEmpty(title))
            {
                Pair(0, "TEXT");
                Pair(8, "0");
                Pair(10, "0");
                Pair(20, "0");
                Pair(30, "0");
                Pair(40, "1");
                Pair(1, title);
            }

            foreach (var p in polys)
            {
                if (p.Vertices == null || p.Vertices.Count < 3) continue;
                string layer = SanitizeLayer(p.LayerName);

                if (!string.IsNullOrEmpty(p.Comment))
                {
                    // DXF COMMENT is not universal; put a TEXT near the first vertex instead.
                    var v0 = p.Vertices[0];
                    Pair(0, "TEXT");
                    Pair(8, layer);
                    PairD(10, v0.X);
                    PairD(20, v0.Y);
                    Pair(30, "0");
                    Pair(40, "0.5");
                    Pair(1, p.Comment);
                }

                // Closed polyline (flag 1). Entities follow until SEQEND (flag 66).
                Pair(0, "POLYLINE");
                Pair(8, layer);
                Pair(66, "1");
                Pair(70, "1"); // closed
                Pair(10, "0");
                Pair(20, "0");
                Pair(30, "0");

                for (int i = 0; i < p.Vertices.Count; i++)
                {
                    var v = p.Vertices[i];
                    Pair(0, "VERTEX");
                    Pair(8, layer);
                    PairD(10, v.X);
                    PairD(20, v.Y);
                    Pair(30, "0");
                }
                Pair(0, "SEQEND");
                Pair(8, layer);
            }

            Pair(0, "ENDSEC");
            Pair(0, "EOF");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string SanitizeLayer(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "SURF";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length == 0) s = "SURF";
            if (s.Length > 31) s = s.Substring(0, 31);
            return s;
        }
    }
}

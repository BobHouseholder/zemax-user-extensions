using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AthermalScan
{
    // Everything the scan measured, in one place, so it can be emitted more than
    // once. The text log is a transcript - fine to read, useless to diff - and every
    // check in this extension's own validation history was done by hand-diffing
    // console output. The CSV and JSON exist so that stops being necessary.
    class Results
    {
        public string LensFile = "";
        public double DesignTempC, DesignPressAtm;
        public bool AdjustIndexWasOn;
        public double ScanPressStart, ScanPressEnd;
        public double TMin, TMax;
        public int Steps;
        public double Efl0, Wfno, TotalTrack, MountTrack, DofMm, LambdaUm;
        public double DzDt, AthermalRangeC, RequiredCte;
        public double EflCheck;

        public double[] Temps, Press, FocusShift, RmsFixed, RmsRefoc, Efl;

        public List<KeyValuePair<double, double>> PressureTerms = new List<KeyValuePair<double, double>>();
        public List<GlassRow> Glasses = new List<GlassRow>();
        public List<HousingRow> Housings = new List<HousingRow>();
        public List<ContribRow> Contributions = new List<ContribRow>();
        public string Bimetallic = null;
        public List<string> Warnings = new List<string>();
        public List<int> EdgeFallbackSurfaces = new List<int>();

        public string Convention => ScanPressStart <= 1e-12 ? "absolute (vacuum)"
            : string.Format(CultureInfo.InvariantCulture, "relative to air at {0:F3} atm", ScanPressStart);

        public class GlassRow
        {
            public string Name; public double NAtT0, DnDt, Tce, Xf; public bool NoThermalIndexData;
        }
        public class HousingRow
        {
            public string Name; public double Cte, ResidualDzDt, UsableRangeC;
        }
        public class ContribRow
        {
            public string Label; public double WeightedXf, PercentOfTotal;
        }
    }

    static class Reports
    {
        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);
        static string N(double v) => double.IsNaN(v) || double.IsInfinity(v)
            ? "null" : v.ToString("R", CultureInfo.InvariantCulture);

        // ---- CSV: the sweep table, one row per temperature ----------------------
        public static void WriteCsv(string path, Results r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("temperature_C,pressure_atm,efl,focus_shift,rms_fixed_um,rms_refocused_um");
            for (int k = 0; k < r.Temps.Length; k++)
                sb.AppendLine(F("{0},{1},{2},{3},{4},{5}",
                    N(r.Temps[k]), N(r.Press[k]), N(r.Efl[k]),
                    N(r.FocusShift[k]), N(r.RmsFixed[k]), N(r.RmsRefoc[k])));
            File.WriteAllText(path, sb.ToString());
        }

        // ---- JSON: everything else, hand-rolled to avoid a dependency -----------
        static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public static void WriteJson(string path, Results r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine(F("  \"tool\": \"AthermalScan\","));
            sb.AppendLine(F("  \"lensFile\": {0},", Q(r.LensFile)));
            sb.AppendLine(F("  \"design\": {{ \"temperatureC\": {0}, \"pressureAtm\": {1}, \"adjustIndexWasOn\": {2} }},",
                N(r.DesignTempC), N(r.DesignPressAtm), r.AdjustIndexWasOn ? "true" : "false"));
            sb.AppendLine(F("  \"scan\": {{ \"tMinC\": {0}, \"tMaxC\": {1}, \"steps\": {2}, \"pressureStartAtm\": {3}, \"pressureEndAtm\": {4}, \"indexConvention\": {5} }},",
                N(r.TMin), N(r.TMax), r.Steps, N(r.ScanPressStart), N(r.ScanPressEnd), Q(r.Convention)));
            sb.AppendLine(F("  \"baseline\": {{ \"efl\": {0}, \"workingFNo\": {1}, \"totalTrack\": {2}, \"mountTrackL\": {3}, \"depthOfFocus\": {4}, \"primaryWavelengthUm\": {5} }},",
                N(r.Efl0), N(r.Wfno), N(r.TotalTrack), N(r.MountTrack), N(r.DofMm), N(r.LambdaUm)));
            sb.AppendLine(F("  \"results\": {{ \"dzdt\": {0}, \"athermalRangeC\": {1}, \"requiredHousingCte\": {2}, \"bimetallic\": {3} }},",
                N(r.DzDt), N(r.AthermalRangeC), N(r.RequiredCte), Q(r.Bimetallic)));

            sb.AppendLine("  \"pressureTerms\": [");
            sb.AppendLine(string.Join(",\n", r.PressureTerms.Select(p => F(
                "    {{ \"toAtm\": {0}, \"focusOffset\": {1}, \"dofMultiple\": {2} }}",
                N(p.Key), N(p.Value), N(r.DofMm > 0 ? Math.Abs(p.Value) / r.DofMm : double.NaN)))));
            sb.AppendLine("  ],");

            sb.AppendLine("  \"glasses\": [");
            sb.AppendLine(string.Join(",\n", r.Glasses.Select(g => F(
                "    {{ \"name\": {0}, \"nAtT0\": {1}, \"dndt\": {2}, \"tce\": {3}, \"xf\": {4}, \"noThermalIndexData\": {5} }}",
                Q(g.Name), N(g.NAtT0), N(g.DnDt), N(g.Tce), N(g.Xf), g.NoThermalIndexData ? "true" : "false"))));
            sb.AppendLine("  ],");

            sb.AppendLine("  \"housings\": [");
            sb.AppendLine(string.Join(",\n", r.Housings.Select(h => F(
                "    {{ \"name\": {0}, \"cte\": {1}, \"residualDzdt\": {2}, \"usableRangeC\": {3} }}",
                Q(h.Name), N(h.Cte), N(h.ResidualDzDt), N(h.UsableRangeC)))));
            sb.AppendLine("  ],");

            sb.AppendLine("  \"elementContributions\": [");
            sb.AppendLine(string.Join(",\n", r.Contributions.Select(c => F(
                "    {{ \"label\": {0}, \"weightedXf\": {1}, \"percentOfTotal\": {2} }}",
                Q(c.Label), N(c.WeightedXf), N(c.PercentOfTotal)))));
            sb.AppendLine("  ],");

            sb.AppendLine(F("  \"restorationCheck\": {{ \"eflBaseline\": {0}, \"eflAfter\": {1}, \"ok\": {2} }},",
                N(r.Efl0), N(r.EflCheck), Math.Abs(r.EflCheck - r.Efl0) < 1e-6 ? "true" : "false"));
            sb.AppendLine(F("  \"edgeFallbackSurfaces\": [{0}],",
                string.Join(", ", r.EdgeFallbackSurfaces.Select(i => i.ToString(CultureInfo.InvariantCulture)))));
            sb.AppendLine(F("  \"warnings\": [{0}]", string.Join(", ", r.Warnings.Select(Q))));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
        }

        // ---- HTML: the human-facing report, chart included as inline SVG --------
        static string E(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public static void WriteHtml(string path, Results r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>Athermal Scan - " + E(Path.GetFileName(r.LensFile)) + "</title>");
            sb.AppendLine(@"<style>
:root{color-scheme:light dark}
body{font:14px/1.5 'Segoe UI',system-ui,sans-serif;margin:0;padding:2rem;max-width:62rem}
h1{font-size:1.4rem;margin:0 0 .2rem} h2{font-size:1.05rem;margin:2rem 0 .6rem;
  border-bottom:1px solid #8884;padding-bottom:.25rem}
.sub{opacity:.7;margin:0 0 1.5rem}
table{border-collapse:collapse;margin:.5rem 0;font-variant-numeric:tabular-nums}
th,td{padding:.3rem .7rem;text-align:right;border-bottom:1px solid #8883}
th{font-weight:600;text-align:right} td:first-child,th:first-child{text-align:left}
.kv{display:grid;grid-template-columns:auto 1fr;gap:.2rem 1.2rem;margin:.5rem 0}
.kv dt{opacity:.7} .kv dd{margin:0;font-variant-numeric:tabular-nums}
.warn{background:#f9a82622;border-left:3px solid #d98c00;padding:.6rem .9rem;margin:.4rem 0}
.note{opacity:.75;font-size:.92em;margin:.4rem 0}
.conv{display:inline-block;padding:.15rem .5rem;border:1px solid #8886;border-radius:.4rem;
  font-size:.9em;font-weight:600}
figure{margin:1rem 0} svg{max-width:100%;height:auto}
.scroll{overflow-x:auto}
</style></head><body>");

            sb.AppendLine("<h1>Athermal Scan</h1>");
            sb.AppendLine("<p class=\"sub\">" + E(string.IsNullOrEmpty(r.LensFile) ? "(untitled system)" : r.LensFile) + "</p>");

            sb.AppendLine("<div class=\"kv\">");
            sb.AppendLine(F("<dt>Design environment</dt><dd>{0:F1} &deg;C, {1:F3} atm</dd>", r.DesignTempC, r.DesignPressAtm));
            sb.AppendLine(F("<dt>Scan</dt><dd>{0:F0} to {1:F0} &deg;C, {2} steps, {3}</dd>", r.TMin, r.TMax, r.Steps,
                Math.Abs(r.ScanPressEnd - r.ScanPressStart) > 1e-12
                    ? F("pressure {0:F3} &rarr; {1:F3} atm", r.ScanPressStart, r.ScanPressEnd)
                    : F("pressure {0:F3} atm", r.ScanPressStart)));
            sb.AppendLine(F("<dt>Index convention</dt><dd><span class=\"conv\">{0}</span></dd>", E(r.Convention)));
            sb.AppendLine(F("<dt>EFFL / working F/#</dt><dd>{0:F4} / {1:F3}</dd>", r.Efl0, r.Wfno));
            sb.AppendLine(F("<dt>Depth of focus</dt><dd>&plusmn;{0:F4} lens units at &lambda; = {1:F4} &micro;m</dd>", r.DofMm, r.LambdaUm));
            sb.AppendLine(F("<dt>Mount track L</dt><dd>{0:F3}</dd>", r.MountTrack));
            sb.AppendLine("</div>");

            foreach (string w in r.Warnings)
                sb.AppendLine("<div class=\"warn\">" + E(w) + "</div>");

            sb.AppendLine("<h2>Result</h2><div class=\"kv\">");
            sb.AppendLine(F("<dt>Thermal defocus rate dz/dT</dt><dd>{0:+0.000000;-0.000000} lens units / &deg;C</dd>", r.DzDt));
            sb.AppendLine(F("<dt>Fixed-plane athermal range</dt><dd>&plusmn;{0:F1} &deg;C</dd>", r.AthermalRangeC));
            sb.AppendLine(F("<dt>Required housing CTE</dt><dd>{0:+0.00;-0.00} &times; 10<sup>-6</sup>/K</dd>", r.RequiredCte));
            if (!string.IsNullOrEmpty(r.Bimetallic))
                sb.AppendLine("<dt>Bimetallic mount</dt><dd>" + E(r.Bimetallic) + "</dd>");
            sb.AppendLine("</div>");

            foreach (var p in r.PressureTerms)
                sb.AppendLine(F("<p class=\"note\">Pressure term {0:F3} &rarr; {1:F3} atm: <b>{2:+0.00000;-0.00000}</b> lens units " +
                    "({3:F1}&times; the depth of focus) &mdash; the relative &rarr; absolute index step, a fixed offset on the " +
                    "sweep and not part of dz/dT.</p>", r.DesignPressAtm, p.Key, p.Value,
                    r.DofMm > 0 ? Math.Abs(p.Value) / r.DofMm : 0));

            sb.AppendLine("<h2>Focus shift and spot size</h2><figure>");
            sb.AppendLine(Svg(r));
            sb.AppendLine("</figure>");

            sb.AppendLine("<h2>Sweep</h2><div class=\"scroll\"><table><thead><tr>" +
                "<th>T (&deg;C)</th><th>P (atm)</th><th>EFFL</th><th>Focus shift</th>" +
                "<th>RMS fixed (&micro;m)</th><th>RMS refocused (&micro;m)</th></tr></thead><tbody>");
            for (int k = 0; k < r.Temps.Length; k++)
                sb.AppendLine(F("<tr><td>{0:F1}</td><td>{1:F3}</td><td>{2:F4}</td><td>{3:+0.00000;-0.00000}</td><td>{4:F2}</td><td>{5:F2}</td></tr>",
                    r.Temps[k], r.Press[k], r.Efl[k], r.FocusShift[k], r.RmsFixed[k], r.RmsRefoc[k]));
            sb.AppendLine("</tbody></table></div>");

            sb.AppendLine("<h2>Per-glass opto-thermal data</h2>");
            sb.AppendLine(F("<p class=\"note\">Measured from the live thermal model, {0}. Relative dn/dT exceeds the " +
                "absolute (catalog) value by n&middot;|dn<sub>air</sub>/dT|, about 1.4&times;10<sup>-6</sup>/K at n = 1.5 and " +
                "1 atm &mdash; enough to flip the sign of x<sub>f</sub> for a low-dn/dT crown.</p>", E(r.Convention)));
            sb.AppendLine("<div class=\"scroll\"><table><thead><tr><th>Glass</th><th>n(T0)</th>" +
                "<th>dn/dT (10<sup>-6</sup>/K)</th><th>TCE (10<sup>-6</sup>/K)</th>" +
                "<th>x<sub>f</sub> (10<sup>-6</sup>/K)</th></tr></thead><tbody>");
            foreach (var g in r.Glasses)
                sb.AppendLine(F("<tr><td>{0}{1}</td><td>{2:F5}</td><td>{3:F2}</td><td>{4:F2}</td><td>{5:+0.00;-0.00}</td></tr>",
                    E(g.Name), g.NoThermalIndexData ? " <b>(no thermal index data)</b>" : "",
                    g.NAtT0, g.DnDt, g.Tce, g.Xf));
            sb.AppendLine("</tbody></table></div>");

            sb.AppendLine("<h2>Passive housing compensation</h2><div class=\"scroll\"><table><thead><tr>" +
                "<th>Material</th><th>CTE (10<sup>-6</sup>/K)</th><th>Residual dz/dT</th>" +
                "<th>Usable range (&plusmn;&deg;C)</th></tr></thead><tbody>");
            foreach (var h in r.Housings)
                sb.AppendLine(F("<tr><td>{0}</td><td>{1:F1}</td><td>{2:+0.000000;-0.000000}</td><td>{3:F1}</td></tr>",
                    E(h.Name), h.Cte, h.ResidualDzDt, h.UsableRangeC));
            sb.AppendLine("</tbody></table></div>");

            if (r.Contributions.Count > 0)
            {
                sb.AppendLine("<h2>Approximate element contributions</h2><div class=\"scroll\"><table><thead><tr>" +
                    "<th>Element</th><th>weight &times; x<sub>f</sub></th><th>Share</th></tr></thead><tbody>");
                foreach (var c in r.Contributions)
                    sb.AppendLine(F("<tr><td>{0}</td><td>{1:+0.00;-0.00}</td><td>{2:F1}%</td></tr>",
                        E(c.Label), c.WeightedXf, c.PercentOfTotal));
                sb.AppendLine("</tbody></table></div>");
            }

            sb.AppendLine(F("<p class=\"note\">Restoration check: EFFL back to {0:G9} against a baseline of {1:G9} &mdash; <b>{2}</b>.</p>",
                r.EflCheck, r.Efl0, Math.Abs(r.EflCheck - r.Efl0) < 1e-6 ? "OK" : "MISMATCH, check the system"));
            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // Two stacked panels as one inline SVG: focus shift with the depth-of-focus
        // band, and RMS spot against temperature. Scales with the page and prints,
        // which the fixed-size PNG never did.
        static string Svg(Results r)
        {
            const int W = 860, H = 520, PAD_L = 70, PAD_R = 20, PAD_T = 26, GAP = 56;
            int panelH = (H - PAD_T * 2 - GAP) / 2;
            int plotW = W - PAD_L - PAD_R;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<svg viewBox=\"0 0 {0} {1}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Segoe UI,sans-serif\" font-size=\"11\">", W, H);
            sb.Append("<style>.ax{stroke:#8887;fill:none}.gr{stroke:#8883;fill:none}" +
                      ".lbl{fill:currentColor;opacity:.75}.ttl{fill:currentColor;font-weight:600}</style>");

            double tMin = r.Temps.Min(), tMax = r.Temps.Max();
            if (tMax - tMin < 1e-12) tMax = tMin + 1;

            Panel(sb, r, 0, PAD_L, PAD_T, plotW, panelH, tMin, tMax,
                new[] { new Tuple<double[], string, string>(r.FocusShift, "#2f6fd0", "focus shift") },
                "focus shift (lens units)", r.DofMm);
            Panel(sb, r, 1, PAD_L, PAD_T + panelH + GAP, plotW, panelH, tMin, tMax,
                new[] { new Tuple<double[], string, string>(r.RmsFixed, "#c62f2f", "RMS @ fixed plane"),
                        new Tuple<double[], string, string>(r.RmsRefoc, "#1f8a3b", "RMS refocused") },
                "RMS spot (micron)", 0);

            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<text class=\"lbl\" x=\"{0}\" y=\"{1}\" text-anchor=\"middle\">temperature (C)</text>",
                PAD_L + plotW / 2, H - 4);
            sb.Append("</svg>");
            return sb.ToString();
        }

        static void Panel(StringBuilder sb, Results r, int idx, int x, int y, int w, int h,
            double tMin, double tMax, Tuple<double[], string, string>[] series, string yLabel, double dofBand)
        {
            double yMin = series.SelectMany(s => s.Item1).Min();
            double yMax = series.SelectMany(s => s.Item1).Max();
            if (dofBand > 0) { yMin = Math.Min(yMin, -dofBand * 1.2); yMax = Math.Max(yMax, dofBand * 1.2); }
            if (yMax - yMin < 1e-12) { yMax += 1; yMin -= 1; }
            double pad = 0.08 * (yMax - yMin); yMin -= pad; yMax += pad;

            Func<double, double> PX = v => x + (v - tMin) / (tMax - tMin) * w;
            Func<double, double> PY = v => y + h - (v - yMin) / (yMax - yMin) * h;

            if (dofBand > 0)
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<rect x=\"{0:F1}\" y=\"{1:F1}\" width=\"{2:F1}\" height=\"{3:F1}\" fill=\"#1f8a3b\" opacity=\".13\"/>",
                    x, PY(dofBand), w, PY(-dofBand) - PY(dofBand));

            for (int k = 0; k <= 4; k++)
            {
                double tv = tMin + (tMax - tMin) * k / 4, yv = yMin + (yMax - yMin) * k / 4;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<line class=\"gr\" x1=\"{0:F1}\" y1=\"{1}\" x2=\"{0:F1}\" y2=\"{2}\"/>", PX(tv), y, y + h);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<line class=\"gr\" x1=\"{0}\" y1=\"{1:F1}\" x2=\"{2}\" y2=\"{1:F1}\"/>", x, PY(yv), x + w);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<text class=\"lbl\" x=\"{0:F1}\" y=\"{1}\" text-anchor=\"middle\">{2:F0}</text>", PX(tv), y + h + 14, tv);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<text class=\"lbl\" x=\"{0}\" y=\"{1:F1}\" text-anchor=\"end\">{2:G3}</text>", x - 6, PY(yv) + 4, yv);
            }
            if (yMin < 0 && yMax > 0)
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<line class=\"ax\" stroke-dasharray=\"4 3\" x1=\"{0}\" y1=\"{1:F1}\" x2=\"{2}\" y2=\"{1:F1}\"/>", x, PY(0), x + w);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<rect class=\"ax\" x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\"/>", x, y, w, h);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<text class=\"ttl\" x=\"{0}\" y=\"{1}\">{2}</text>", x, y - 8, yLabel);

            int li = 0;
            foreach (var s in series)
            {
                var pts = string.Join(" ", Enumerable.Range(0, r.Temps.Length).Select(i =>
                    string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1}", PX(r.Temps[i]), PY(s.Item1[i]))));
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<polyline fill=\"none\" stroke=\"{0}\" stroke-width=\"2\" points=\"{1}\"/>", s.Item2, pts);
                for (int i = 0; i < r.Temps.Length; i++)
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "<circle cx=\"{0:F1}\" cy=\"{1:F1}\" r=\"2.6\" fill=\"{2}\"/>", PX(r.Temps[i]), PY(s.Item1[i]), s.Item2);
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "<line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{1}\" stroke=\"{3}\" stroke-width=\"2\"/>" +
                    "<text class=\"lbl\" x=\"{4}\" y=\"{5}\">{6}</text>",
                    x + w - 170, y + 14 + li * 16, x + w - 148, s.Item2, x + w - 142, y + 18 + li * 16, s.Item3);
                li++;
            }
        }
    }
}

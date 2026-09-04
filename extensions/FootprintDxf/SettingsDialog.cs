using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace FootprintDxf
{
    // Ribbon runs get no command line. OpticStudio launches User Extensions with
    // no arguments, so without a window every knob would only be reachable from
    // a shell. Same pattern as DistortionTarget / GpimGhostReduce.
    // Cancel returns false — the caller must then write nothing (system untouched).
    class SettingsDialog : Form
    {
        readonly TextBox _out, _rays, _surfaces, _fields;
        readonly ComboBox _wave;
        readonly CheckBox _includeImage, _rim;
        readonly Label _hint;
        readonly Button _ok;

        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FootprintDxf", "lastrun.txt");

        public static bool Show(Options o)
        {
            Application.EnableVisualStyles();
            using (var dlg = new SettingsDialog(o))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                dlg.Apply(o);
                dlg.SaveLastRun(o);
                return true;
            }
        }

        static CultureInfo CI => CultureInfo.InvariantCulture;

        SettingsDialog(Options o)
        {
            Text = "Footprint DXF export";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            LoadLastRun(o);

            const int GW = 480, PAD = 12;
            int y = PAD;

            var gOut = new GroupBox { Text = "Output", Left = PAD, Top = y, Width = GW, Height = 56 };
            _out = Field(gOut, 0, "DXF path (blank = beside lens)", o.OutPath ?? "");
            Controls.Add(gOut);
            y += gOut.Height + 8;

            var gTrace = new GroupBox { Text = "Ray sampling", Left = PAD, Top = y, Width = GW, Height = 140 };
            _rays = Field(gTrace, 0, "Pupil grid density (odd)", o.Rays.ToString(CI));
            _surfaces = Field(gTrace, 1, "Surfaces (all | 1,3 | 1-6 | name)", o.Surfaces ?? "all");
            _fields = Field(gTrace, 2, "Fields (all | 1,2)", o.Fields ?? "all");

            gTrace.Controls.Add(new Label { Text = "Wavelengths", Left = 14, Top = 22 + 3 * 28 + 3, Width = 200 });
            _wave = new ComboBox
            {
                Left = 220, Top = 22 + 3 * 28, Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _wave.Items.AddRange(new object[] { "all", "primary" });
            _wave.SelectedIndex = (o.Wave ?? "all").Equals("primary", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            gTrace.Controls.Add(_wave);
            Controls.Add(gTrace);
            y += gTrace.Height + 8;

            var gFlags = new GroupBox { Text = "Options", Left = PAD, Top = y, Width = GW, Height = 80 };
            _includeImage = new CheckBox
            {
                Text = "Include image surface when Surfaces = all",
                Left = 14, Top = 22, Width = GW - 28,
                Checked = o.IncludeImage
            };
            _rim = new CheckBox
            {
                Text = "Also write denser pupil-rim polylines (RIM_… layers)",
                Left = 14, Top = 48, Width = GW - 28,
                Checked = o.Rim
            };
            gFlags.Controls.Add(_includeImage);
            gFlags.Controls.Add(_rim);
            Controls.Add(gFlags);
            y += gFlags.Height + 8;

            _hint = new Label
            {
                Left = PAD + 2, Top = y, Width = GW - 4, Height = 72,
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(_hint);
            y += _hint.Height + 6;

            _ok = new Button { Text = "Export", DialogResult = DialogResult.OK, Left = PAD + GW - 178, Top = y, Width = 85, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = PAD + GW - 88, Top = y, Width = 85, Height = 28 };
            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;
            ClientSize = new Size(GW + 2 * PAD, y + 28 + PAD);

            foreach (var tb in new[] { _rays, _surfaces, _fields, _out })
                tb.TextChanged += (s, e) => Recompute();
            _wave.SelectedIndexChanged += (s, e) => Recompute();
            _includeImage.CheckedChanged += (s, e) => Recompute();
            _rim.CheckedChanged += (s, e) => Recompute();
            Recompute();
        }

        TextBox Field(GroupBox g, int row, string label, string value)
        {
            int top = 22 + row * 28;
            g.Controls.Add(new Label { Text = label, Left = 14, Top = top + 3, Width = 200 });
            var tb = new TextBox { Left = 220, Top = top, Width = 240, Text = value };
            g.Controls.Add(tb);
            return tb;
        }

        static bool TryI(TextBox tb, out int v) =>
            int.TryParse(tb.Text.Trim(), NumberStyles.Integer, CI, out v);

        void Recompute()
        {
            int rays;
            if (!TryI(_rays, out rays) || rays < 3)
            {
                _hint.ForeColor = Color.Firebrick;
                _hint.Text = "Pupil grid density must be an integer >= 3.";
                _ok.Enabled = false;
                return;
            }
            _ok.Enabled = true;
            _hint.ForeColor = SystemColors.GrayText;
            string wave = _wave.SelectedIndex == 1 ? "primary wavelength only" : "all wavelengths";
            string img = _includeImage.Checked ? " (incl. image)" : "";
            string rim = _rim.Checked ? " Plus rim polylines." : "";
            _hint.Text = string.Format(CI,
                "Batch-trace a {0}×{0} pupil grid on surfaces [{1}]{2}, fields [{3}], {4}. " +
                "Convex hull of local (x,y) hits → closed DXF polyline per surface. System is not modified.{5}",
                rays, string.IsNullOrWhiteSpace(_surfaces.Text) ? "all" : _surfaces.Text.Trim(),
                img,
                string.IsNullOrWhiteSpace(_fields.Text) ? "all" : _fields.Text.Trim(),
                wave, rim);
        }

        void Apply(Options o)
        {
            int rays;
            if (TryI(_rays, out rays))
            {
                if (rays < 3) rays = 3;
                if (rays % 2 == 0) rays++;
                o.Rays = rays;
            }
            o.Surfaces = string.IsNullOrWhiteSpace(_surfaces.Text) ? "all" : _surfaces.Text.Trim();
            o.Fields = string.IsNullOrWhiteSpace(_fields.Text) ? "all" : _fields.Text.Trim();
            o.Wave = _wave.SelectedIndex == 1 ? "primary" : "all";
            o.IncludeImage = _includeImage.Checked;
            o.Rim = _rim.Checked;
            o.OutPath = string.IsNullOrWhiteSpace(_out.Text) ? null : _out.Text.Trim();
        }

        void SaveLastRun(Options o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, string.Join("\r\n", new[]
                {
                    "out=" + (o.OutPath ?? ""),
                    "rays=" + o.Rays.ToString(CI),
                    "surfaces=" + (o.Surfaces ?? "all"),
                    "fields=" + (o.Fields ?? "all"),
                    "wave=" + (o.Wave ?? "all"),
                    "includeimage=" + (o.IncludeImage ? "1" : "0"),
                    "rim=" + (o.Rim ? "1" : "0")
                }));
            }
            catch { }
        }

        static void LoadLastRun(Options o)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (o.Explicit.Contains(k)) continue;
                    int i;
                    switch (k)
                    {
                        case "out": if (val.Length > 0) o.OutPath = val; break;
                        case "rays": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.Rays = i; break;
                        case "surfaces": if (val.Length > 0) o.Surfaces = val; break;
                        case "fields": if (val.Length > 0) o.Fields = val; break;
                        case "wave": if (val.Length > 0) o.Wave = val; break;
                        case "includeimage": o.IncludeImage = val == "1"; break;
                        case "rim": o.Rim = val == "1"; break;
                    }
                }
            }
            catch { }
        }
    }
}

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
    // Cancel returns false - the caller must then write nothing (system untouched).
    class SettingsDialog : Form
    {
        readonly TextBox _out, _rays, _rimRays, _surfaces, _fields;
        readonly ComboBox _wave;
        readonly CheckBox _includeImage, _rim, _perField, _global, _aperture, _writePng, _openOutputs;
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

            var gTrace = new GroupBox { Text = "Ray sampling", Left = PAD, Top = y, Width = GW, Height = 168 };
            _rays = Field(gTrace, 0, "Pupil grid density (odd)", o.Rays.ToString(CI));
            string rimDefault = o.RimRays > 0
                ? o.RimRays.ToString(CI)
                : o.EffectiveRimRays().ToString(CI);
            _rimRays = Field(gTrace, 1, "Dense rim rays (16..1024)", rimDefault);
            _surfaces = Field(gTrace, 2, "Surfaces (all | 1,3 | 1-6 | name)", o.Surfaces ?? "all");
            _fields = Field(gTrace, 3, "Fields (all | 1,2)", o.Fields ?? "all");

            gTrace.Controls.Add(new Label { Text = "Wavelengths", Left = 14, Top = 22 + 4 * 28 + 3, Width = 200 });
            _wave = new ComboBox
            {
                Left = 220, Top = 22 + 4 * 28, Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _wave.Items.AddRange(new object[] { "all", "primary" });
            _wave.SelectedIndex = (o.Wave ?? "all").Equals("primary", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            gTrace.Controls.Add(_wave);
            Controls.Add(gTrace);
            y += gTrace.Height + 8;

            var gFlags = new GroupBox { Text = "Options", Left = PAD, Top = y, Width = GW, Height = 210 };
            _includeImage = new CheckBox
            {
                Text = "Include image surface when Surfaces = all",
                Left = 14, Top = 22, Width = GW - 28,
                Checked = o.IncludeImage
            };
            _rim = new CheckBox
            {
                Text = "Also write separate RIM_... layers (dense rim always used for main hull)",
                Left = 14, Top = 48, Width = GW - 28,
                Checked = o.Rim
            };
            _perField = new CheckBox
            {
                Text = "Also write per-field hull layers SURF_n_Ff (union SURF_n kept)",
                Left = 14, Top = 74, Width = GW - 28,
                Checked = o.PerField
            };
            _global = new CheckBox
            {
                Text = "Global / decentered frame (GetGlobalMatrix; stack surfaces in one drawing)",
                Left = 14, Top = 100, Width = GW - 28,
                Checked = o.Global
            };
            _aperture = new CheckBox
            {
                Text = "Draw clear-aperture overlays (APER_SURF_n)",
                Left = 14, Top = 126, Width = GW - 28,
                Checked = o.Aperture
            };
            _writePng = new CheckBox
            {
                Text = "Write PNG preview",
                Left = 14, Top = 152, Width = GW - 28,
                Checked = !o.NoPng
            };
            _openOutputs = new CheckBox
            {
                Text = "Open outputs when done",
                Left = 14, Top = 178, Width = GW - 28,
                Checked = !o.Quiet
            };
            gFlags.Controls.Add(_includeImage);
            gFlags.Controls.Add(_rim);
            gFlags.Controls.Add(_perField);
            gFlags.Controls.Add(_global);
            gFlags.Controls.Add(_aperture);
            gFlags.Controls.Add(_writePng);
            gFlags.Controls.Add(_openOutputs);
            Controls.Add(gFlags);
            y += gFlags.Height + 8;

            _hint = new Label
            {
                Left = PAD + 2, Top = y, Width = GW - 4, Height = 128,
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

            foreach (var tb in new[] { _rays, _rimRays, _surfaces, _fields, _out })
                tb.TextChanged += (s, e) => Recompute();
            _wave.SelectedIndexChanged += (s, e) => Recompute();
            _includeImage.CheckedChanged += (s, e) => Recompute();
            _rim.CheckedChanged += (s, e) => Recompute();
            _perField.CheckedChanged += (s, e) => Recompute();
            _global.CheckedChanged += (s, e) => Recompute();
            _aperture.CheckedChanged += (s, e) => Recompute();
            _writePng.CheckedChanged += (s, e) => Recompute();
            _openOutputs.CheckedChanged += (s, e) => Recompute();
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
            int rays, rimRays;
            if (!TryI(_rays, out rays) || rays < 3)
            {
                _hint.ForeColor = Color.Firebrick;
                _hint.Text = "Pupil grid density must be an integer >= 3.";
                _ok.Enabled = false;
                return;
            }
            if (!TryI(_rimRays, out rimRays) || rimRays < 16 || rimRays > 1024)
            {
                _hint.ForeColor = Color.Firebrick;
                _hint.Text = "Dense rim rays must be an integer in [16, 1024].";
                _ok.Enabled = false;
                return;
            }
            _ok.Enabled = true;
            _hint.ForeColor = SystemColors.GrayText;
            string wave = _wave.SelectedIndex == 1 ? "primary wavelength only" : "all wavelengths";
            string img = _includeImage.Checked ? " (incl. image)" : "";
            string rimExtra = _rim.Checked
                ? " Also writes per-field RIM_..._F{f} layers (reuses rim@1; no second trace)."
                : "";
            string pfExtra = _perField.Checked
                ? " Also writes per-field SURF_n_Ff hulls (union SURF_n kept)."
                : "";
            string globNote = _global.Checked
                ? " Coordinates: global XY via GetGlobalMatrix (Z ignored)."
                : " Coordinates: local surface XY.";
            string aperNote = _aperture.Checked
                ? " Also draws APER_SURF_n clear-aperture overlays."
                : "";
            string pngNote = _writePng.Checked
                ? " Also writes a PNG preview beside the DXF."
                : " PNG preview skipped.";
            string openNote = _openOutputs.Checked ? "" : " Outputs will not auto-open.";
            _hint.Text = string.Format(CI,
                "Batch-trace a {0}x{0} pupil grid plus a dense rim ({1} samples at r=1 and r=0.99) " +
                "on surfaces [{2}]{3}, fields [{4}], {5}. Convex hull of (x,y) hits -> closed " +
                "DXF polyline per surface (dense rim is always in the main hull). " +
                "Note: convex hull overestimates concave vignetted shapes." +
                "{6}{7}{8}{9}{10}{11} System is not modified.",
                rays, rimRays,
                string.IsNullOrWhiteSpace(_surfaces.Text) ? "all" : _surfaces.Text.Trim(),
                img,
                string.IsNullOrWhiteSpace(_fields.Text) ? "all" : _fields.Text.Trim(),
                wave, rimExtra, pfExtra, globNote, aperNote, pngNote, openNote);
        }

        void Apply(Options o)
        {
            int rays, rimRays;
            if (TryI(_rays, out rays))
            {
                if (rays < 3) rays = 3;
                if (rays % 2 == 0) rays++;
                o.Rays = rays;
            }
            if (TryI(_rimRays, out rimRays))
            {
                if (rimRays < 16) rimRays = 16;
                if (rimRays > 1024) rimRays = 1024;
                o.RimRays = rimRays;
            }
            o.Surfaces = string.IsNullOrWhiteSpace(_surfaces.Text) ? "all" : _surfaces.Text.Trim();
            o.Fields = string.IsNullOrWhiteSpace(_fields.Text) ? "all" : _fields.Text.Trim();
            o.Wave = _wave.SelectedIndex == 1 ? "primary" : "all";
            o.IncludeImage = _includeImage.Checked;
            o.Rim = _rim.Checked;
            if (!o.Explicit.Contains("perfield"))
                o.PerField = _perField.Checked;
            if (!o.Explicit.Contains("global"))
                o.Global = _global.Checked;
            if (!o.Explicit.Contains("aperture"))
                o.Aperture = _aperture.Checked;
            // Map checkboxes -> NoPng / Quiet. Explicit CLI still wins (LoadLastRun + no dialog override of Explicit).
            if (!o.Explicit.Contains("nopng"))
                o.NoPng = !_writePng.Checked;
            if (!o.Explicit.Contains("quiet"))
                o.Quiet = !_openOutputs.Checked;
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
                    "rimrays=" + o.RimRays.ToString(CI),
                    "surfaces=" + (o.Surfaces ?? "all"),
                    "fields=" + (o.Fields ?? "all"),
                    "wave=" + (o.Wave ?? "all"),
                    "includeimage=" + (o.IncludeImage ? "1" : "0"),
                    "rim=" + (o.Rim ? "1" : "0"),
                    "perfield=" + (o.PerField ? "1" : "0"),
                    "global=" + (o.Global ? "1" : "0"),
                    "aperture=" + (o.Aperture ? "1" : "0"),
                    "writepng=" + (o.NoPng ? "0" : "1"),
                    "openoutputs=" + (o.Quiet ? "0" : "1")
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
                    // Explicit CLI wins. writepng <-> nopng, openoutputs <-> quiet.
                    if (k == "writepng" && o.Explicit.Contains("nopng")) continue;
                    if (k == "openoutputs" && o.Explicit.Contains("quiet")) continue;
                    if (o.Explicit.Contains(k)) continue;
                    int i;
                    switch (k)
                    {
                        case "out": if (val.Length > 0) o.OutPath = val; break;
                        case "rays": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.Rays = i; break;
                        case "rimrays": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.RimRays = i; break;
                        case "surfaces": if (val.Length > 0) o.Surfaces = val; break;
                        case "fields": if (val.Length > 0) o.Fields = val; break;
                        case "wave": if (val.Length > 0) o.Wave = val; break;
                        case "includeimage": o.IncludeImage = val == "1"; break;
                        case "rim": o.Rim = val == "1"; break;
                        case "perfield": o.PerField = val == "1"; break;
                        case "global": o.Global = val == "1"; break;
                        case "aperture": o.Aperture = val == "1"; break;
                        case "writepng": o.NoPng = val != "1"; break;
                        case "openoutputs": o.Quiet = val != "1"; break;
                        case "nopng": o.NoPng = val == "1"; break; // legacy alias
                        case "quiet": o.Quiet = val == "1"; break; // legacy alias
                    }
                }
            }
            catch { }
        }
    }
}

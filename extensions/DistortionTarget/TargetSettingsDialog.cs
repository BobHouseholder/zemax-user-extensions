using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DistortionTarget
{
    // A ribbon run gets no command line. OpticStudio launches the extension from
    // Programming > User Extensions with no arguments and offers nowhere to supply
    // any, so without a window every parameter this tool has would be reachable
    // only from a shell. Ansys's own CODE V converter extension answers this the
    // same way (manual 1.5.3.7.2).
    //
    // The derived read-out is the point of the dialog, not decoration. The geometry
    // this builds has one failure mode that is invisible in the inputs and obvious
    // in the outputs: a dot count that puts the corner dots over the edge of the
    // plate. Reading "201 dots, 0.5 mm pitch, 100 mm plate" tells you nothing;
    // reading "outermost dot edge 50.125 mm, clearance -0.125 mm" tells you at once.
    // So it recomputes on every keystroke and refuses OK while the geometry is
    // impossible, rather than accepting the form and failing afterwards.
    //
    // Controls are CHILDREN of their group boxes, positioned in group-local
    // coordinates — the sibling-at-absolute-coordinates arrangement paints the
    // group over its own fields, because WinForms z-order puts index 0 in front and
    // Controls.Add appends.
    class TargetSettingsDialog : Form
    {
        readonly TextBox _n, _pitch, _dot, _plate, _thick, _material, _coating, _film, _draw;
        readonly CheckBox _rig;
        readonly Label _derived;
        readonly Button _ok;

        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DistortionTarget", "lastrun.txt");

        // Returns false if the user cancelled — the caller must then build nothing,
        // since Build() replaces the open system outright.
        public static bool Show(Options o)
        {
            Application.EnableVisualStyles();
            using (var dlg = new TargetSettingsDialog(o))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                dlg.Apply(o);
                dlg.SaveLastRun(o);
                return true;
            }
        }

        static CultureInfo CI => CultureInfo.InvariantCulture;

        TargetSettingsDialog(Options o)
        {
            Text = "Distortion target";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            LoadLastRun(o);

            const int GW = 430, PAD = 12;
            int y = PAD;

            var gGrid = new GroupBox { Text = "Dot grid", Left = PAD, Top = y, Width = GW, Height = 108 };
            _n = Field(gGrid, 0, "Dots per side", o.N.ToString(CI));
            _pitch = Field(gGrid, 1, "Pitch (mm)", o.Pitch.ToString(CI));
            _dot = Field(gGrid, 2, "Dot diameter (mm)", o.DotDia.ToString(CI));
            Controls.Add(gGrid);
            y += gGrid.Height + 8;

            var gSub = new GroupBox { Text = "Substrate", Left = PAD, Top = y, Width = GW, Height = 108 };
            _plate = Field(gSub, 0, "Outer size (mm)", o.Plate.ToString(CI));
            _thick = Field(gSub, 1, "Thickness (mm)", o.PlateT.ToString(CI));
            _material = Field(gSub, 2, "Material", o.Material);
            Controls.Add(gSub);
            y += gSub.Height + 8;

            var gChrome = new GroupBox { Text = "Chrome", Left = PAD, Top = y, Width = GW, Height = 80 };
            _coating = Field(gChrome, 0, "Coating", o.Coating);
            _film = Field(gChrome, 1, "Film thickness (mm)", o.Film.ToString(CI));
            Controls.Add(gChrome);
            y += gChrome.Height + 8;

            var gView = new GroupBox { Text = "Display", Left = PAD, Top = y, Width = GW, Height = 80 };
            _draw = Field(gView, 0, "Draw limit", o.DrawLimit.ToString(CI));
            _rig = new CheckBox
            {
                Text = "Also add a collimated source and detector",
                Left = 14,
                Top = 22 + 28,
                Width = GW - 28,
                Checked = o.Rig
            };
            gView.Controls.Add(_rig);
            Controls.Add(gView);
            y += gView.Height + 8;

            _derived = new Label
            {
                Left = PAD + 2,
                Top = y,
                Width = GW - 4,
                Height = 74,
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(_derived);
            y += _derived.Height + 6;

            var reset = new Button { Text = "Edmund 15963 defaults", Left = PAD, Top = y, Width = 160, Height = 28 };
            reset.Click += (s, e) => LoadDefaults();
            Controls.Add(reset);

            _ok = new Button { Text = "Build", DialogResult = DialogResult.OK, Left = PAD + GW - 178, Top = y, Width = 85, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = PAD + GW - 88, Top = y, Width = 85, Height = 28 };
            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;

            ClientSize = new Size(GW + 2 * PAD, y + 28 + PAD);

            foreach (var tb in new[] { _n, _pitch, _dot, _plate, _thick, _film, _draw })
                tb.TextChanged += (s, e) => Recompute();
            Recompute();
        }

        TextBox Field(GroupBox g, int row, string label, string value)
        {
            int top = 22 + row * 28;
            g.Controls.Add(new Label { Text = label, Left = 14, Top = top + 3, Width = 190 });
            var tb = new TextBox { Left = 210, Top = top, Width = 190, Text = value };
            g.Controls.Add(tb);
            return tb;
        }

        static bool TryD(TextBox tb, out double v) =>
            double.TryParse(tb.Text.Trim(), NumberStyles.Float, CI, out v);

        static bool TryI(TextBox tb, out int v) =>
            int.TryParse(tb.Text.Trim(), NumberStyles.Integer, CI, out v);

        void Recompute()
        {
            int n, draw;
            double pitch, dot, plate, thick, film;
            if (!TryI(_n, out n) || !TryD(_pitch, out pitch) || !TryD(_dot, out dot) ||
                !TryD(_plate, out plate) || !TryD(_thick, out thick) || !TryD(_film, out film) ||
                !TryI(_draw, out draw))
            {
                _derived.ForeColor = Color.Firebrick;
                _derived.Text = "Some field is not a number.";
                _ok.Enabled = false;
                return;
            }

            double span = (n - 1) * pitch;
            double edge = span / 2.0 + dot / 2.0;
            double clear = plate / 2.0 - edge;
            double fill = Math.PI * (dot / 2.0) * (dot / 2.0) / (pitch * pitch);

            string reason = null;
            if (n < 2) reason = "Dots per side must be at least 2.";
            else if (pitch <= 0 || dot <= 0) reason = "Pitch and dot diameter must be positive.";
            else if (dot >= pitch) reason = "Dots this large on this pitch would touch or overlap.";
            else if (thick <= 0) reason = "Substrate thickness must be positive.";
            else if (film <= 0) reason = "Chrome film thickness must be positive.";
            else if (clear <= 0)
                reason = string.Format(CI, "Corner dots hang {0:0.###} mm over the edge of the plate.", -clear);

            _ok.Enabled = reason == null;
            if (reason != null)
            {
                _derived.ForeColor = Color.Firebrick;
                _derived.Text = string.Format(CI,
                    "{0}\r\n{1:n0} dots  ·  span {2:0.###} mm  ·  outermost edge {3:0.###} mm  ·  clearance {4:0.###} mm",
                    reason, (long)n * n, span, edge, clear);
                return;
            }

            _derived.ForeColor = SystemColors.GrayText;
            _derived.Text = string.Format(CI,
                "{0:n0} dots  ·  span {1:0.###} mm  ·  outermost edge {2:0.###} mm  ·  clearance {3:0.###} mm\r\n" +
                "chrome fill {4:0.0000}  ·  open {5:0.0000}  ·  {6}\r\n" +
                "drawing {7:n0} of {8:n0} dots — drawing only, no traced result depends on it\r\n" +
                "radiometry needs ray splitting on, or the coating is not applied at all",
                (long)n * n, span, edge, clear, fill, 1.0 - fill,
                n % 2 == 1 ? "one dot on axis" : "EVEN count, no dot on axis",
                Math.Min(draw, (long)n * n), (long)n * n);
        }

        void LoadDefaults()
        {
            var d = new Options();
            _n.Text = d.N.ToString(CI);
            _pitch.Text = d.Pitch.ToString(CI);
            _dot.Text = d.DotDia.ToString(CI);
            _plate.Text = d.Plate.ToString(CI);
            _thick.Text = d.PlateT.ToString(CI);
            _material.Text = d.Material;
            _coating.Text = d.Coating;
            _film.Text = d.Film.ToString(CI);
            _draw.Text = d.DrawLimit.ToString(CI);
            _rig.Checked = d.Rig;
            Recompute();
        }

        void Apply(Options o)
        {
            int n, draw; double v;
            if (TryI(_n, out n)) o.N = n;
            if (TryD(_pitch, out v)) o.Pitch = v;
            if (TryD(_dot, out v)) o.DotDia = v;
            if (TryD(_plate, out v)) o.Plate = v;
            if (TryD(_thick, out v)) o.PlateT = v;
            if (TryD(_film, out v)) o.Film = v;
            if (TryI(_draw, out draw)) o.DrawLimit = draw;
            if (!string.IsNullOrWhiteSpace(_material.Text)) o.Material = _material.Text.Trim();
            if (!string.IsNullOrWhiteSpace(_coating.Text)) o.Coating = _coating.Text.Trim();
            o.Rig = _rig.Checked;
        }

        void SaveLastRun(Options o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, string.Join("\r\n", new[]
                {
                    "n=" + o.N.ToString(CI),
                    "pitch=" + o.Pitch.ToString(CI),
                    "dot=" + o.DotDia.ToString(CI),
                    "plate=" + o.Plate.ToString(CI),
                    "thick=" + o.PlateT.ToString(CI),
                    "material=" + o.Material,
                    "coating=" + o.Coating,
                    "film=" + o.Film.ToString(CI),
                    "drawlimit=" + o.DrawLimit.ToString(CI),
                    "rig=" + (o.Rig ? "1" : "0")
                }));
            }
            catch { /* a settings file we cannot write is not worth failing a build over */ }
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
                    double d; int i;
                    switch (k)
                    {
                        case "n": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.N = i; break;
                        case "pitch": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.Pitch = d; break;
                        case "dot": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.DotDia = d; break;
                        case "plate": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.Plate = d; break;
                        case "thick": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.PlateT = d; break;
                        case "material": if (val.Length > 0) o.Material = val; break;
                        case "coating": if (val.Length > 0) o.Coating = val; break;
                        case "film": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.Film = d; break;
                        case "drawlimit": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.DrawLimit = i; break;
                        case "rig": o.Rig = val == "1"; break;
                    }
                }
            }
            catch { }
        }
    }
}

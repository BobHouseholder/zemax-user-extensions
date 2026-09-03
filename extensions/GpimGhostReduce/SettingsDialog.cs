using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GpimGhostReduce
{
    // A ribbon run gets no command line. OpticStudio launches the extension from
    // Programming > User Extensions with no arguments, so without a window the
    // only way to pick Mode / Max pairs / Balance would be a shell.
    class SettingsDialog : Form
    {
        readonly ComboBox _kind;
        readonly TextBox _top, _balance, _cycles;
        readonly CheckBox _optimize;
        readonly Label _hint;
        readonly Button _ok;

        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GpimGhostReduce", "lastrun.txt");

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
            Text = "GPIM ghost reduce";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            LoadLastRun(o);

            const int GW = 440, PAD = 12;
            int y = PAD;

            var g = new GroupBox { Text = "GPIM operands", Left = PAD, Top = y, Width = GW, Height = 168 };
            g.Controls.Add(new Label { Text = "Ghost type", Left = 14, Top = 25, Width = 180 });
            _kind = new ComboBox
            {
                Left = 200, Top = 22, Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _kind.Items.AddRange(new object[] { "Image ghosts (Mode 1)", "Pupil ghosts (Mode 0)", "Both" });
            _kind.SelectedIndex = o.Kind == GhostKind.Pupil ? 1 : o.Kind == GhostKind.Both ? 2 : 0;
            g.Controls.Add(_kind);

            _top = Field(g, 1, "Max pairs (0 = auto from scan)", o.TopN.ToString(CI));
            _balance = Field(g, 2, "Balance vs existing MF (1 = equal)", o.Balance.ToString(CI));
            Controls.Add(g);
            y += g.Height + 8;

            var gOpt = new GroupBox { Text = "Optimize", Left = PAD, Top = y, Width = GW, Height = 80 };
            _optimize = new CheckBox
            {
                Text = "Run local DLS after inserting GPIM",
                Left = 14, Top = 22, Width = GW - 28,
                Checked = o.Optimize
            };
            gOpt.Controls.Add(_optimize);
            _cycles = Field(gOpt, 1, "DLS cycles (0 = automatic)", o.Cycles.ToString(CI));
            Controls.Add(gOpt);
            y += gOpt.Height + 8;

            _hint = new Label
            {
                Left = PAD + 2, Top = y, Width = GW - 4, Height = 88,
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(_hint);
            y += _hint.Height + 6;

            _ok = new Button { Text = "Apply", DialogResult = DialogResult.OK, Left = PAD + GW - 178, Top = y, Width = 85, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = PAD + GW - 88, Top = y, Width = 85, Height = 28 };
            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;
            ClientSize = new Size(GW + 2 * PAD, y + 28 + PAD);

            foreach (var tb in new[] { _top, _balance, _cycles })
                tb.TextChanged += (s, e) => Recompute();
            _kind.SelectedIndexChanged += (s, e) => Recompute();
            Recompute();
        }

        TextBox Field(GroupBox g, int row, string label, string value)
        {
            int top = 22 + row * 28;
            g.Controls.Add(new Label { Text = label, Left = 14, Top = top + 3, Width = 184 });
            var tb = new TextBox { Left = 200, Top = top, Width = 220, Text = value };
            g.Controls.Add(tb);
            return tb;
        }

        static bool TryD(TextBox tb, out double v) =>
            double.TryParse(tb.Text.Trim(), NumberStyles.Float, CI, out v);

        static bool TryI(TextBox tb, out int v) =>
            int.TryParse(tb.Text.Trim(), NumberStyles.Integer, CI, out v);

        void Recompute()
        {
            int top, cycles; double b;
            if (!TryI(_top, out top) || !TryD(_balance, out b) || !TryI(_cycles, out cycles))
            {
                _hint.ForeColor = Color.Firebrick;
                _hint.Text = "Some field is not a number.";
                _ok.Enabled = false;
                return;
            }
            if (top < 0 || b < 0 || cycles < 0)
            {
                _hint.ForeColor = Color.Firebrick;
                _hint.Text = "Max pairs, balance and cycles must be >= 0.";
                _ok.Enabled = false;
                return;
            }
            _ok.Enabled = true;
            _hint.ForeColor = SystemColors.GrayText;
            string kind = _kind.SelectedIndex == 1 ? "pupil" : _kind.SelectedIndex == 2 ? "image and pupil" : "image";
            string cap = top == 0 ? "auto (up to 8 hot pairs)" : "at most " + top.ToString(CI);
            _hint.Text = string.Format(CI,
                "Full-scan double-bounce {0} ghosts. Keep {1}. Scale GPIM weights so ghosts pull {2:0.###}× as hard as the existing merit function. Original operands are never deleted. DLS is skipped if the file has no weighted MF.",
                kind, cap, b);
        }

        void Apply(Options o)
        {
            int top, cycles; double b;
            if (TryI(_top, out top)) o.TopN = top;
            if (TryD(_balance, out b)) o.Balance = b;
            if (TryI(_cycles, out cycles)) o.Cycles = cycles;
            o.Optimize = _optimize.Checked;
            o.Kind = _kind.SelectedIndex == 1 ? GhostKind.Pupil : _kind.SelectedIndex == 2 ? GhostKind.Both : GhostKind.Image;
        }

        void SaveLastRun(Options o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, string.Join("\r\n", new[]
                {
                    "mode=" + (o.Kind == GhostKind.Pupil ? "pupil" : o.Kind == GhostKind.Both ? "both" : "image"),
                    "top=" + o.TopN.ToString(CI),
                    "balance=" + o.Balance.ToString(CI),
                    "optimize=" + (o.Optimize ? "1" : "0"),
                    "cycles=" + o.Cycles.ToString(CI)
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
                    double d; int i;
                    switch (k)
                    {
                        case "mode":
                            if (val == "pupil") o.Kind = GhostKind.Pupil;
                            else if (val == "both") o.Kind = GhostKind.Both;
                            else o.Kind = GhostKind.Image;
                            break;
                        case "top": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.TopN = i; break;
                        case "balance": if (double.TryParse(val, NumberStyles.Float, CI, out d)) o.Balance = d; break;
                        case "weight":
                            // old lastrun.txt: treat as unused; balance is the new knob
                            break;
                        case "optimize": o.Optimize = val == "1"; break;
                        case "cycles": if (int.TryParse(val, NumberStyles.Integer, CI, out i)) o.Cycles = i; break;
                    }
                }
            }
            catch { }
        }
    }
}

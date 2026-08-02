using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace AthermalScan
{
    // A ribbon run gets no command line. OpticStudio launches the extension from
    // Programming > User Extensions with no arguments and offers nowhere to supply
    // any, so every option this tool has was reachable only from a shell - the
    // sweep range, and more importantly whether the system is analysed in air or in
    // vacuum, which moves dz/dT by more than half on a plain triplet. Worse, once
    // the environment guards went in, a file with Adjust Index Data To Environment
    // switched off failed on the ribbon with "re-run with -temp0 <C>", advice a
    // ribbon user has no way to follow.
    //
    // Ansys's own CODE V converter extension answers this by putting up its own
    // window (manual 1.5.3.7.2); this does the same. Shown when the process was
    // started with no arguments and OpticStudio is driving it; -nodialog restores
    // the silent defaults-only behaviour for scripted runs.
    class ScanSettingsDialog : Form
    {
        readonly TextBox _tmin, _tmax, _steps, _t0, _p0, _pfixed, _pramp, _track;
        readonly RadioButton _pSame, _pVac, _pFixed, _pRamp;
        readonly CheckBox _freeze;
        readonly bool _adjustOn;

        public static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AthermalScan", "lastrun.txt");

        // Returns false if the user cancelled - the caller must then do nothing at
        // all, since the scan mutates the live prescription.
        public static bool Show(double sysTemp, double sysPress, bool adjustOn, Options o)
        {
            Application.EnableVisualStyles();
            using (var dlg = new ScanSettingsDialog(sysTemp, sysPress, adjustOn, o))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                dlg.Apply(o);
                dlg.SaveLastRun(o);
                return true;
            }
        }

        ScanSettingsDialog(double sysTemp, double sysPress, bool adjustOn, Options o)
        {
            _adjustOn = adjustOn;
            Text = "Athermal Scan";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(430, 476);
            Font = new Font("Segoe UI", 9f);

            LoadLastRun(o); // last run's values, if any, override the built-in defaults

            int y = 10;
            AddGroup("Temperature sweep", ref y, 88);
            _tmin = AddField("Start (C)", 30, y - 58, o.TMin.ToString(CultureInfo.InvariantCulture));
            _tmax = AddField("End (C)", 30, y - 32, o.TMax.ToString(CultureInfo.InvariantCulture));
            _steps = AddField("Steps", 250, y - 58, o.Steps.ToString(CultureInfo.InvariantCulture));

            AddGroup("Design environment - what the prescription was measured in", ref y, adjustOn ? 88 : 108);
            _t0 = AddField("Temperature (C)", 30, y - (adjustOn ? 58 : 78),
                (o.Temp0 ?? sysTemp).ToString("0.###", CultureInfo.InvariantCulture));
            _p0 = AddField("Pressure (atm)", 250, y - (adjustOn ? 58 : 78),
                (o.Press0 ?? (adjustOn ? sysPress : 1.0)).ToString("0.###", CultureInfo.InvariantCulture));
            if (!adjustOn)
                Add(new Label
                {
                    Left = 30, Top = y - 46, Width = 370, Height = 32,
                    ForeColor = Color.FromArgb(160, 60, 0),
                    Text = "This file has Adjust Index Data To Environment OFF, so its stored " +
                           "temperature and pressure do not define the design environment. Enter it here."
                });

            AddGroup("Analyse at", ref y, 122);
            int gy = y - 108;
            _pSame = AddRadio("The design pressure", 30, gy);
            _pVac = AddRadio("Vacuum (0 atm) - absolute indices", 30, gy + 24);
            _pFixed = AddRadio("Fixed pressure (atm):", 30, gy + 48);
            _pfixed = AddBox(230, gy + 46, 70, "0");
            _pRamp = AddRadio("Ramp with temperature, ending at (atm):", 30, gy + 72);
            _pramp = AddBox(300, gy + 70, 70, "0");
            SelectPressureMode(o);

            AddGroup("Options", ref y, 84);
            _track = AddField("Mount track L (blank = total track)", 30, y - 54,
                o.Track > 0 ? o.Track.ToString(CultureInfo.InvariantCulture) : "");
            _freeze = new CheckBox
            {
                Left = 30, Top = y - 28, Width = 380, Checked = o.FreezeSolves,
                Text = "Freeze value-computing solves (not undone afterwards)"
            };
            Add(_freeze);

            var ok = new Button { Text = "Run scan", Left = 230, Top = y + 8, Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 328, Top = y + 8, Width = 82, DialogResult = DialogResult.Cancel };
            Add(ok); Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        void SelectPressureMode(Options o)
        {
            if (o.PressureEnd.HasValue)
            {
                _pRamp.Checked = true;
                _pramp.Text = o.PressureEnd.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else if (o.Pressure.HasValue && o.Pressure.Value <= 1e-12) _pVac.Checked = true;
            else if (o.Pressure.HasValue)
            {
                _pFixed.Checked = true;
                _pfixed.Text = o.Pressure.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else _pSame.Checked = true;
        }

        // ---- tiny layout helpers -------------------------------------------------
        void Add(Control c) => Controls.Add(c);

        void AddGroup(string title, ref int y, int height)
        {
            Add(new GroupBox { Left = 12, Top = y, Width = 406, Height = height, Text = title });
            y += height + 10;
        }

        TextBox AddField(string label, int x, int y, string value)
        {
            Add(new Label { Left = x, Top = y + 3, Width = 200, Text = label });
            return AddBox(x + (label.Length > 22 ? 240 : 130), y, 70, value);
        }

        TextBox AddBox(int x, int y, int w, string value)
        {
            var t = new TextBox { Left = x, Top = y, Width = w, Text = value };
            Add(t);
            return t;
        }

        RadioButton AddRadio(string text, int x, int y)
        {
            var r = new RadioButton { Left = x, Top = y, Width = 300, Text = text };
            Add(r);
            return r;
        }

        // ---- results -------------------------------------------------------------
        static double Num(TextBox t, double fallback)
        {
            double v;
            return double.TryParse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        void Apply(Options o)
        {
            o.TMin = Num(_tmin, o.TMin);
            o.TMax = Num(_tmax, o.TMax);
            int steps;
            if (int.TryParse(_steps.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out steps))
                o.Steps = steps < 3 ? 3 : steps;
            o.Track = string.IsNullOrWhiteSpace(_track.Text) ? 0 : Num(_track, 0);
            o.FreezeSolves = _freeze.Checked;

            // The design point is always declared explicitly from here: with the
            // adjust switch off it has to be, and with it on this simply restates
            // what the file already says unless the user changed it.
            o.Temp0 = Num(_t0, 20);
            o.Press0 = Math.Max(0, Num(_p0, 1));

            o.Pressure = null; o.PressureEnd = null;
            if (_pVac.Checked) o.Pressure = 0;
            else if (_pFixed.Checked) o.Pressure = Math.Max(0, Num(_pfixed, 0));
            else if (_pRamp.Checked)
            {
                o.Pressure = o.Press0;                              // ramp starts at the design pressure
                o.PressureEnd = Math.Max(0, Num(_pramp, 0));
            }
            if (!_adjustOn && !o.Pressure.HasValue) o.Pressure = o.Press0;
        }

        // ---- last-run persistence -------------------------------------------------
        static void LoadLastRun(Options o)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    double d; int i;
                    bool isNum = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                    switch (k)
                    {
                        case "tmin": if (isNum) o.TMin = d; break;
                        case "tmax": if (isNum) o.TMax = d; break;
                        case "steps": if (int.TryParse(v, out i)) o.Steps = i; break;
                        case "track": if (isNum) o.Track = d; break;
                        case "temp0": if (isNum) o.Temp0 = d; break;
                        case "press0": if (isNum) o.Press0 = d; break;
                        case "pressure": if (isNum) o.Pressure = d; break;
                        case "pressureEnd": if (isNum) o.PressureEnd = d; break;
                        case "freeze": o.FreezeSolves = v == "1"; break;
                    }
                }
            }
            catch { /* a corrupt or unreadable settings file just means defaults */ }
        }

        void SaveLastRun(Options o)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var lines = new System.Collections.Generic.List<string>
                {
                    F("tmin", o.TMin), F("tmax", o.TMax), "steps=" + o.Steps,
                    F("track", o.Track), "freeze=" + (o.FreezeSolves ? "1" : "0"),
                };
                if (o.Temp0.HasValue) lines.Add(F("temp0", o.Temp0.Value));
                if (o.Press0.HasValue) lines.Add(F("press0", o.Press0.Value));
                if (o.Pressure.HasValue) lines.Add(F("pressure", o.Pressure.Value));
                if (o.PressureEnd.HasValue) lines.Add(F("pressureEnd", o.PressureEnd.Value));
                File.WriteAllLines(SettingsPath, lines);
            }
            catch { /* not being able to remember the last run is not worth failing over */ }
        }

        static string F(string k, double v) => k + "=" + v.ToString("R", CultureInfo.InvariantCulture);
    }
}

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
            Font = new Font("Segoe UI", 9f);

            LoadLastRun(o); // last run's values, if any, override the built-in defaults

            // Every control is a CHILD of its group box, positioned in the group's own
            // coordinates. The first version made them siblings at absolute
            // coordinates, which failed twice over: WinForms z-order puts index 0 in
            // front and Controls.Add appends, so each group - added before the fields
            // it encloses - painted straight over them and the dialog rendered as four
            // empty boxes; and the hand-computed offsets put a 200px label on top of
            // its own text box, pushed the Steps box past the client width, and
            // truncated the warning mid-sentence. Parenting removes the whole class:
            // a child cannot be hidden by its own frame, and local coordinates cannot
            // drift out of the group.
            //
            // The four pressure radios all live in ONE group box, so they still form a
            // single mutually exclusive set - WinForms groups radios by parent.
            const int GW = 436, PAD = 12, LBL = 22;
            int y = PAD;

            var gSweep = AddGroup("Temperature sweep", ref y, GW, 84);
            _tmin = AddField(gSweep, "Start (C)", 16, LBL, 100, o.TMin.ToString(CultureInfo.InvariantCulture));
            _tmax = AddField(gSweep, "End (C)", 16, LBL + 28, 100, o.TMax.ToString(CultureInfo.InvariantCulture));
            _steps = AddField(gSweep, "Steps", 240, LBL, 300, o.Steps.ToString(CultureInfo.InvariantCulture));

            var gEnv = AddGroup("Design environment - what the prescription was measured in",
                ref y, GW, adjustOn ? 60 : 122);
            _t0 = AddField(gEnv, "Temperature (C)", 16, LBL, 130,
                (o.Temp0 ?? sysTemp).ToString("0.###", CultureInfo.InvariantCulture));
            _p0 = AddField(gEnv, "Pressure (atm)", 240, LBL, 340,
                (o.Press0 ?? (adjustOn ? sysPress : 1.0)).ToString("0.###", CultureInfo.InvariantCulture));
            if (!adjustOn)
                gEnv.Controls.Add(new Label
                {
                    Left = 16, Top = LBL + 30, Width = GW - 36, Height = 56,
                    ForeColor = Color.FromArgb(150, 70, 0),
                    Text = "This file has Adjust Index Data To Environment OFF. OpticStudio then pins " +
                           "index data to 20 C / 1.0 atm, so the temperature and pressure stored in the " +
                           "file do not define the design environment - enter it here."
                });

            var gAt = AddGroup("Analyse at", ref y, GW, 118);
            _pSame = AddRadio(gAt, "The design pressure", 16, LBL - 4);
            _pVac = AddRadio(gAt, "Vacuum (0 atm) - absolute indices", 16, LBL + 20);
            _pFixed = AddRadio(gAt, "Fixed pressure (atm):", 16, LBL + 44);
            _pfixed = AddBox(gAt, 250, LBL + 44, 70, "0");
            _pRamp = AddRadio(gAt, "Ramp with temperature, ending at (atm):", 16, LBL + 68);
            _pramp = AddBox(gAt, 250, LBL + 68, 70, "0");
            SelectPressureMode(o);

            var gOpt = AddGroup("Options", ref y, GW, 80);
            _track = AddField(gOpt, "Mount track L (blank = total track)", 16, LBL, 220,
                o.Track > 0 ? o.Track.ToString(CultureInfo.InvariantCulture) : "");
            _freeze = new CheckBox
            {
                Left = 16, Top = LBL + 30, Width = GW - 36, Checked = o.FreezeSolves,
                Text = "Freeze value-computing solves (not undone afterwards)"
            };
            gOpt.Controls.Add(_freeze);

            var ok = new Button { Text = "Run scan", Left = PAD + GW - 188, Top = y, Width = 92, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = PAD + GW - 90, Top = y, Width = 90, DialogResult = DialogResult.Cancel };
            Add(ok); Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            // Size the form to the content instead of to a guessed constant, so the
            // buttons cannot end up clipped by the bottom edge again.
            ClientSize = new Size(PAD * 2 + GW, y + ok.Height + PAD);
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

        // ---- layout helpers ------------------------------------------------------
        // All coordinates inside a group are LOCAL to that group. Labels are AutoSize
        // so a long caption cannot silently paint over the box beside it, and the box
        // x is passed explicitly rather than guessed from the caption's length.
        void Add(Control c) => Controls.Add(c);

        GroupBox AddGroup(string title, ref int y, int width, int height)
        {
            var g = new GroupBox { Left = 12, Top = y, Width = width, Height = height, Text = title };
            Add(g);
            y += height + 10;
            return g;
        }

        static TextBox AddField(GroupBox g, string label, int x, int y, int boxX, string value)
        {
            g.Controls.Add(new Label { Left = x, Top = y + 4, AutoSize = true, Text = label });
            return AddBox(g, boxX, y, 70, value);
        }

        static TextBox AddBox(GroupBox g, int x, int y, int w, string value)
        {
            var t = new TextBox { Left = x, Top = y, Width = w, Text = value };
            g.Controls.Add(t);
            return t;
        }

        static RadioButton AddRadio(GroupBox g, string text, int x, int y)
        {
            var r = new RadioButton { Left = x, Top = y, AutoSize = true, Text = text };
            g.Controls.Add(r);
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
        /// <summary>
        /// Apply the last run's saved settings without showing the dialog. The User
        /// Analysis needs this: OpticStudio runs it and shows its settings form as two
        /// separate launches, so the run has to read what the form last wrote.
        /// </summary>
        internal static void LoadInto(Options o) => LoadLastRun(o);

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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AthermalScan
{
    partial class Program
    {
        static string Convention(double pAtm) =>
            pAtm <= 1e-12
                ? "ABSOLUTE (vacuum) - at P = 0 the air reference is unity"
                : F("RELATIVE to air at {0:F3} atm", pAtm);

        // TEMP and PRES multi-configuration operands set the environment for every
        // operand that follows them, and the last pair sets the global environment -
        // and this is true even with a single configuration (manual 2.1.1.4.5). The
        // system-level temperature this scan writes would then not describe what is
        // actually traced: a group given its own PRES keeps its own pressure, hence its
        // own relative/absolute index reference, while the report claims a uniform soak.
        static void CheckNoEnvironmentOperands(ZOSAPI.IOpticalSystem sys)
        {
            var found = new List<string>();
            try
            {
                var mce = sys.MCE;
                for (int r = 1; r <= mce.NumberOfOperands; r++)
                {
                    var op = mce.GetOperandAt(r);
                    if (op == null) continue;
                    if (op.Type == ZOSAPI.Editors.MCE.MultiConfigOperandType.TEMP ||
                        op.Type == ZOSAPI.Editors.MCE.MultiConfigOperandType.PRES)
                        found.Add(F("row {0}: {1}", r, op.Type));
                }
            }
            catch { return; } // no MCE access - nothing to object to
            if (found.Count == 0) return;
            throw new Exception(
                "the multi-configuration editor already defines the environment (" + string.Join(", ", found) +
                "). TEMP/PRES operands govern every operand listed after them and the last pair governs " +
                "everything else, so surfaces in a separately specified group would keep their own " +
                "temperature and pressure - and their own relative/absolute index reference - while this " +
                "scan reported a uniform soak. Analyse this system through the multi-configuration " +
                "workflow instead.");
        }

        // ApplyTemperature writes radius/thickness/parameter values directly, so a solve
        // that COMPUTES its cell overwrites what we write and the thermal model becomes
        // silently wrong - most visibly a marginal ray height solve on the last
        // thickness, which auto-refocuses and reports a focus shift of zero. Variables
        // are harmless: they mark a cell for optimisation, they do not compute it.
        static List<ZOSAPI.Editors.LDE.SurfaceColumn> WritableColumns()
        {
            var cols = new List<ZOSAPI.Editors.LDE.SurfaceColumn>
            {
                ZOSAPI.Editors.LDE.SurfaceColumn.Radius,
                ZOSAPI.Editors.LDE.SurfaceColumn.Thickness,
            };
            for (int p = 1; p <= 8; p++)
                cols.Add((ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p));
            return cols;
        }

        /// <summary>
        /// Value-computing solves on cells the scan must write, described one per entry.
        /// Pure - it reports, it does not freeze or throw, so the settings window can
        /// ask this BEFORE the user commits to a run.
        /// </summary>
        internal static List<string> FindComputingSolves(ZOSAPI.Editors.LDE.ILensDataEditor lde, int imgIdx)
        {
            var found = new List<string>();
            foreach (var col in WritableColumns())
                for (int i = 1; i < imgIdx; i++)
                {
                    ZOSAPI.Editors.SolveType st;
                    try { st = lde.GetSurfaceAt(i).GetSurfaceCell(col).Solve; }
                    catch { continue; } // locked or non-existent cell for this surface type
                    if (st == ZOSAPI.Editors.SolveType.None || st == ZOSAPI.Editors.SolveType.Fixed ||
                        st == ZOSAPI.Editors.SolveType.Variable || st == ZOSAPI.Editors.SolveType.Automatic)
                        continue;
                    found.Add(F("surface {0} {1} ({2})", i, col, st));
                }
            return found;
        }

        static void CheckSolves(ZOSAPI.Editors.LDE.ILensDataEditor lde, int imgIdx)
        {
            if (!Opts.FreezeSolves)
            {
                var offenders = FindComputingSolves(lde, imgIdx);
                if (offenders.Count == 0) return;
                throw new Exception(
                    "value-computing solves sit on cells this scan must write - " +
                    string.Join("; ", offenders.Take(8)) +
                    (offenders.Count > 8 ? F("; and {0} more", offenders.Count - 8) : "") +
                    ". A solve recomputes its cell after every assignment, so the thermal model would be " +
                    "silently overridden: a marginal ray height solve on the last thickness, for instance, " +
                    "auto-refocuses and reports a focus shift of zero. " +
                    // The remedy has to be one the reader can actually carry out. A
                    // ribbon user cannot pass a flag - OpticStudio gives them no way to
                    // - so point them at the checkbox that does the same thing.
                    (Opts.HostLaunched
                        ? "Tick 'Freeze value-computing solves' in the settings window, or remove the solves."
                        : "Remove them, or re-run with -freezesolves to freeze them to their current values " +
                          "first (not undone on restore)."));
            }

            int frozen = 0;
            foreach (var col in WritableColumns())
                for (int i = 1; i < imgIdx; i++)
                {
                    ZOSAPI.Editors.IEditorCell cell;
                    ZOSAPI.Editors.SolveType st;
                    try { cell = lde.GetSurfaceAt(i).GetSurfaceCell(col); st = cell.Solve; }
                    catch { continue; }
                    if (st == ZOSAPI.Editors.SolveType.None || st == ZOSAPI.Editors.SolveType.Fixed ||
                        st == ZOSAPI.Editors.SolveType.Variable || st == ZOSAPI.Editors.SolveType.Automatic)
                        continue;
                    try { if (cell.MakeSolveFixed()) frozen++; } catch { }
                }
            if (frozen > 0)
                Say(F("Froze {0} value-computing solve(s) to their current values. This is NOT undone by " +
                      "the restore - do not save the file unless that is what you want.", frozen));
        }

        // Undo everything the scan touched. Called from a finally, so it must not throw:
        // a failed step is reported and the remaining steps are still attempted. Returns
        // the design-environment EFFL for the restoration check, or NaN.
        static double RestoreSystem(ZOSAPI.IOpticalSystem sys, ZOSAPI.SystemData.ISDEnvironmentData env,
            RowSnap[] snaps, int imgIdx, double t0, double p0, double tRaw, double pRaw,
            bool adjust0, int primaryWave)
        {
            double check = double.NaN;
            try { ApplyTemperature(sys, snaps, imgIdx, 0); }
            catch (Exception ex) { Console.WriteLine("WARNING: could not restore the prescription: " + ex.Message); }
            // measured at the design environment with the adjust switch still on, so it
            // is compared in the same index state as the baseline
            try
            {
                env.Temperature = t0; env.Pressure = p0;
                check = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, primaryWave);
            }
            catch (Exception ex) { Console.WriteLine("WARNING: restoration check failed: " + ex.Message); }
            try { env.Temperature = tRaw; env.Pressure = pRaw; env.AdjustIndexToEnvironment = adjust0; }
            catch (Exception ex) { Console.WriteLine("WARNING: could not restore the environment: " + ex.Message); }
            return check;
        }

        // Rows whose gap could not use the edge model and fell back to centre
        // scaling - reported once, since silently using a different physics than
        // the one documented is exactly what this replaced.
        static readonly HashSet<int> EdgeFallbackRows = new HashSet<int>();

        // OpticStudio does NOT scale a non-glass thickness at the centre. The
        // thermal pickup expands the material along a length running from the edge
        // of this surface to the edge of the next, because a mount touches the
        // lenses at their rims, and the result is then transferred back onto the
        // centre thickness (manual 2.1.1.4.4.2). Two consequences the centre-scaling
        // approximation misses entirely:
        //
        //   * the sag change of both bounding surfaces feeds into the gap, so even a
        //     TCE of 0 moves an air space when the adjacent radii move - the manual
        //     is explicit that a 0 TCE is not the way to freeze a thickness.
        //
        // Two details here are measured against Make Thermal's own pickup solves
        // rather than taken from the manual, because the manual's account of them
        // does not survive contact with the numbers:
        //
        //   * the edge is measured at the CLEAR semi-diameter, not the mechanical
        //     one. The manual says "the mechanical semi diameters for each surface
        //     are what determine this edge thickness", but changing a mechanical
        //     semi-diameter from 14 to 20 with the clear semi-diameter held at 12
        //     moves OpticStudio's answer by exactly nothing;
        //   * there is NO contact-point walk. The manual describes the mount and
        //     rim expanding at different rates so the contact point migrates
        //     radially, with a clamp to keep it on the lens. Modelling that walk
        //     leaves a residual of ~0.85 um on the test gap; evaluating both sags
        //     at the same unexpanded height reproduces OpticStudio to ~0.02 um
        //     across curved/plano and TCE 23.6/0 variants. Whatever the walk is
        //     for, it does not show up in a THIC thermal pickup.
        //
        // Sag is evaluated on the snapshot, analytically, for the surface forms this
        // tool already expands (standard/conic, even and odd asphere). Anything else
        // bounding the gap falls back to centre scaling and is named in the report.
        static double EdgeExpandedThickness(RowSnap[] snaps, int i, int imgIdx, double dT, out bool ok)
        {
            ok = false;
            var s = snaps[i - 1];
            RowSnap next = i <= imgIdx - 2 ? snaps[i] : null;   // null => the image plane
            double h0 = s.SemiDia > 0 ? s.SemiDia : s.MechSemiDia;
            if (!(h0 > 0) || double.IsNaN(h0) || double.IsInfinity(h0)) return 0;

            double aMount = s.AlphaThick;    // the spacer / mount material

            // the image plane closes the last gap and is treated as flat
            bool o1, o3, o2 = true, o4 = true;
            double zA0 = Sag(s, h0, 1.0, out o1);
            double zB0 = next == null ? 0 : Sag(next, h0, 1.0, out o2);

            double eRa = 1 + s.AlphaRadius * 1e-6 * dT;
            double eRb = next == null ? 1 : 1 + next.AlphaRadius * 1e-6 * dT;
            double zA1 = Sag(s, h0, eRa, out o3);
            double zB1 = next == null ? 0 : Sag(next, h0, eRb, out o4);

            if (!(o1 && o2 && o3 && o4)) return 0;

            double edge0 = s.Thickness + zB0 - zA0;              // as-built edge length
            double edge1 = edge0 * (1 + aMount * 1e-6 * dT);     // it is the edge that expands
            double t = edge1 - zB1 + zA1;                        // transferred to the centre
            if (double.IsNaN(t) || double.IsInfinity(t)) return 0;
            ok = true;
            return t;
        }

        // Sag of a snapshotted surface at radial height h, with its radius and
        // polynomial terms expanded by eR (eR = 1 gives the as-built surface). The
        // conic is dimensionless and does not scale.
        static double Sag(RowSnap s, double h, double eR, out bool ok)
        {
            ok = false;
            bool even = s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric;
            bool odd = s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere;
            if (s.Type != ZOSAPI.Editors.LDE.SurfaceType.Standard && !even && !odd) return 0;

            double z = 0, R = s.Radius * eR;
            if (!(double.IsInfinity(R) || Math.Abs(R) > 1e10 || R == 0))
            {
                double c = 1.0 / R;
                double u = 1 - (1 + s.Conic) * c * c * h * h;
                if (u < 0) return 0;                 // h is off the surface
                z = c * h * h / (1 + Math.Sqrt(u));
            }
            if (even || odd)
                for (int p = 1; p <= 8; p++)
                {
                    if (s.Pars[p] == 0) continue;
                    int powr = even ? 2 * p : p;
                    z += s.Pars[p] * Math.Pow(eR, 1 - powr) * Math.Pow(h, powr);
                }
            ok = !double.IsNaN(z) && !double.IsInfinity(z);
            return z;
        }

        // apply the thermal model relative to the snapshot (dT = 0 restores)
        static void ApplyTemperature(ZOSAPI.IOpticalSystem sys, RowSnap[] snaps, int imgIdx, double dT)
        {
            var lde = sys.LDE;
            for (int i = 1; i < imgIdx; i++)
            {
                var s = snaps[i - 1];
                var row = lde.GetSurfaceAt(i);
                double eT = 1 + s.AlphaThick * 1e-6 * dT;
                double eR = 1 + s.AlphaRadius * 1e-6 * dT;

                if (s.IsGlass)
                {
                    // A catalog glass expands as a solid: centre thickness scales.
                    row.Thickness = s.Thickness * eT;
                }
                else
                {
                    bool ok;
                    double t = EdgeExpandedThickness(snaps, i, imgIdx, dT, out ok);
                    if (!ok) { t = s.Thickness * eT; EdgeFallbackRows.Add(i); }
                    row.Thickness = t;
                }
                if (s.Type != ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                {
                    if (!(Math.Abs(s.Radius) > 1e10 || s.Radius == 0))
                        try { row.Radius = s.Radius * eR; } catch { }
                    if (s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ||
                        s.Type == ZOSAPI.Editors.LDE.SurfaceType.OddAsphere)
                    {
                        for (int p = 1; p <= 8; p++)
                        {
                            if (s.Pars[p] == 0) continue;
                            int powr = s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric ? 2 * p : p;
                            try
                            {
                                var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
                                row.GetSurfaceCell(col).DoubleValue = s.Pars[p] * Math.Pow(eR, 1 - powr);
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        // image-space marginal focus position measured from the last optical surface
        static double MarginalFocus(ZOSAPI.IOpticalSystem sys, int imgIdx, int wave, double lastGap)
        {
            double y = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAY, imgIdx, wave, 0, 0, 0, 1);
            double m = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAB, imgIdx, wave, 0, 0, 0, 1);
            double nz = Op(sys, ZOSAPI.Editors.MFE.MeritOperandType.REAC, imgIdx, wave, 0, 0, 0, 1);
            double u = Math.Abs(nz) > 1e-14 ? m / nz : 0;
            if (Math.Abs(u) < 1e-14) return lastGap;
            return lastGap - y / u;
        }

        static double LinFit(double[] x, double[] y)
        {
            int n = x.Length;
            double sx = x.Sum(), sy = y.Sum(), sxx = x.Sum(v => v * v), sxy = 0;
            for (int i = 0; i < n; i++) sxy += x[i] * y[i];
            return (n * sxy - sx * sy) / (n * sxx - sx * sx);
        }

        static void Chart(double[] t, double[] dz, double[] rmsF, double[] rmsR,
            double dof, string path, string title)
        {
            int W = 1200, H = 800;
            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                var font = new Font("Segoe UI", 10f);
                var fontB = new Font("Segoe UI", 12f, FontStyle.Bold);
                var black = new SolidBrush(Color.Black);

                g.DrawString("Athermal scan - " + title, fontB, black, 20, 8);
                Panel(g, font, 60, 50, W - 100, 320, t, new[] { (dz, Color.FromArgb(0, 90, 200), "focus shift") },
                    "focus shift (lens units)", dof);
                Panel(g, font, 60, 430, W - 100, 320, t,
                    new[] { (rmsF, Color.FromArgb(200, 30, 30), "RMS @ fixed plane"),
                            (rmsR, Color.FromArgb(0, 140, 0), "RMS refocused") },
                    "RMS spot (um)", 0);
                g.DrawString("temperature (C)", font, black, W / 2 - 40, H - 28);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Panel(Graphics g, Font font, int x, int y, int w, int h, double[] t,
            (double[] data, Color color, string label)[] series, string yLabel, double dofBand)
        {
            double xmin = t.Min(), xmax = t.Max();
            double ymin = series.SelectMany(s => s.data).Min();
            double ymax = series.SelectMany(s => s.data).Max();
            if (dofBand > 0) { ymin = Math.Min(ymin, -dofBand * 1.2); ymax = Math.Max(ymax, dofBand * 1.2); }
            if (ymax - ymin < 1e-12) { ymax += 1; ymin -= 1; }
            double pad = 0.08 * (ymax - ymin); ymin -= pad; ymax += pad;
            float PX(double v) => (float)(x + (v - xmin) / (xmax - xmin) * w);
            float PY(double v) => (float)(y + h - (v - ymin) / (ymax - ymin) * h);

            if (dofBand > 0)
                using (var band = new SolidBrush(Color.FromArgb(40, 0, 180, 0)))
                    g.FillRectangle(band, x, PY(dofBand), w, PY(-dofBand) - PY(dofBand));

            using (var axis = new Pen(Color.Black, 1.5f))
            using (var grid = new Pen(Color.FromArgb(230, 230, 230), 1f))
            using (var black = new SolidBrush(Color.Black))
            {
                for (int k = 0; k <= 4; k++)
                {
                    double tv = xmin + (xmax - xmin) * k / 4;
                    g.DrawLine(grid, PX(tv), y, PX(tv), y + h);
                    g.DrawString(tv.ToString("F0"), font, black, PX(tv) - 10, y + h + 4);
                    double yv = ymin + (ymax - ymin) * k / 4;
                    g.DrawLine(grid, x, PY(yv), x + w, PY(yv));
                    g.DrawString(yv.ToString("G3"), font, black, 4, PY(yv) - 8);
                }
                if (ymin < 0 && ymax > 0)
                    using (var zero = new Pen(Color.Gray, 1f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(zero, x, PY(0), x + w, PY(0));
                g.DrawRectangle(axis, x, y, w, h);
                g.DrawString(yLabel, font, black, x, y - 20);

                int lx = x + w - 190, ly = y + 8;
                foreach (var s in series)
                {
                    using (var pen = new Pen(s.color, 2.2f))
                    {
                        var pts = new PointF[t.Length];
                        for (int i = 0; i < t.Length; i++) pts[i] = new PointF(PX(t[i]), PY(s.data[i]));
                        g.DrawLines(pen, pts);
                        foreach (var p in pts) g.FillEllipse(new SolidBrush(s.color), p.X - 3, p.Y - 3, 6, 6);
                        g.DrawLine(pen, lx, ly + 7, lx + 24, ly + 7);
                    }
                    g.DrawString(s.label, font, new SolidBrush(s.color), lx + 28, ly);
                    ly += 18;
                }
            }
        }
    }
}

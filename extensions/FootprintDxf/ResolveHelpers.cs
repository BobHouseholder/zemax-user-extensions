using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FootprintDxf
{
    partial class Program
    {
        static List<int> ResolveSurfaces(string spec, int imgIdx, bool includeImage,
            ZOSAPI.Editors.LDE.ILensDataEditor lde)
        {
            var set = new SortedSet<int>();
            string s = (spec ?? "all").Trim();
            if (s.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                int last = includeImage ? imgIdx : imgIdx - 1;
                for (int i = 1; i <= last; i++) set.Add(i);
                return set.ToList();
            }

            foreach (string part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;

                // Named surface: match Comment (case-insensitive), or IMA/IMAGE/OBJ.
                if (!char.IsDigit(p[0]) && p.IndexOf('-') < 0)
                {
                    if (p.Equals("ima", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("image", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(imgIdx);
                        continue;
                    }
                    if (p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("object", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("WARNING: object surface skipped (no useful footprint).");
                        continue;
                    }
                    bool found = false;
                    for (int i = 1; i <= imgIdx; i++)
                    {
                        string c = null;
                        try { c = (lde.GetSurfaceAt(i).Comment ?? "").Trim(); } catch { }
                        if (!string.IsNullOrEmpty(c) && c.Equals(p, StringComparison.OrdinalIgnoreCase))
                        {
                            set.Add(i);
                            found = true;
                        }
                    }
                    if (!found)
                        Console.WriteLine("WARNING: no surface named '" + p + "'.");
                    continue;
                }

                int dash = p.IndexOf('-');
                if (dash > 0)
                {
                    int a, b;
                    if (int.TryParse(p.Substring(0, dash), NumberStyles.Integer, CI, out a) &&
                        int.TryParse(p.Substring(dash + 1), NumberStyles.Integer, CI, out b))
                    {
                        if (a > b) { int t = a; a = b; b = t; }
                        for (int i = a; i <= b; i++)
                            if (i >= 1 && i <= imgIdx) set.Add(i);
                        continue;
                    }
                }
                int one;
                if (int.TryParse(p, NumberStyles.Integer, CI, out one))
                {
                    if (one >= 1 && one <= imgIdx) set.Add(one);
                    else Console.WriteLine("WARNING: surface " + one + " out of range.");
                }
                else
                    Console.WriteLine("WARNING: could not parse surface '" + p + "'.");
            }
            return set.ToList();
        }

        static List<int> ResolveFields(ZOSAPI.IOpticalSystem sys, string spec)
        {
            var fields = sys.SystemData.Fields;
            int nf = fields.NumberOfFields;
            var set = new SortedSet<int>();
            string s = (spec ?? "all").Trim();
            if (s.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 1; i <= nf; i++) set.Add(i);
                return set.ToList();
            }
            foreach (string part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CI, out v) && v >= 1 && v <= nf)
                    set.Add(v);
                else
                    Console.WriteLine("WARNING: field '" + part + "' ignored.");
            }
            return set.ToList();
        }

        static List<int> ResolveWaves(ZOSAPI.IOpticalSystem sys, string spec)
        {
            var wls = sys.SystemData.Wavelengths;
            int nw = wls.NumberOfWavelengths;
            var set = new SortedSet<int>();
            string s = (spec ?? "all").Trim();
            if (s.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("pri", StringComparison.OrdinalIgnoreCase))
            {
                for (int w = 1; w <= nw; w++)
                {
                    try
                    {
                        if (wls.GetWavelength(w).IsPrimary) { set.Add(w); break; }
                    }
                    catch { }
                }
                if (set.Count == 0) set.Add(1);
                return set.ToList();
            }
            if (s.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                for (int w = 1; w <= nw; w++) set.Add(w);
                return set.ToList();
            }
            foreach (string part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CI, out v) && v >= 1 && v <= nw)
                    set.Add(v);
                else
                    Console.WriteLine("WARNING: wavelength '" + part + "' ignored.");
            }
            return set.ToList();
        }

        static void OpenOutputs(ZOSAPI.IZOSAPI_Application app, params string[] paths)
        {
            if (Opts.Quiet) return;
            try { if (app.Mode != ZOSAPI.ZOSAPI_Mode.Plugin) return; } catch { return; }
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                try { System.Diagnostics.Process.Start(p); }
                catch (Exception ex) { Console.WriteLine("WARNING: could not open " + p + ": " + ex.Message); }
            }
        }

        static bool Cancelled()
        {
            try
            {
                if (App != null && App.TerminateRequested)
                {
                    Say("Cancelled.");
                    App.ProgressMessage = "Cancelled.";
                    return true;
                }
            }
            catch { }
            return false;
        }

        static void Say(string s)
        {
            Console.WriteLine(s);
            try { if (App != null) App.ProgressMessage = s; } catch { }
        }
    }
}

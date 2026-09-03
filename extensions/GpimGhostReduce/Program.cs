using System;
using System.Collections.Generic;
using System.Globalization;
using ZOSAPI;
using ZOSAPI.Editors;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.Optimization;

namespace GpimGhostReduce
{
    // GpimGhostReduce — ZOS-API User Extension.
    //
    // Implements the sequential ghost-reduction loop from Ansys Optics
    // "Stray Light Analysis with Ghost Focus Generator"
    // https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator
    //
    // Rank double-bounce image (and optionally pupil) ghosts with GPIM, then
    // append GPIM operands (target 0) to the EXISTING merit function so a later
    // optimize pushes the ghost focus off the image plane without throwing away
    // the design MF. OpticStudio defines GPIM as 1/|z_ghost − z_image|, which is
    // why the article targets 0.
    //
    // Geometric Image Analysis of the saved Ghost Focus Generator files is still
    // a manual check; this extension does the operand half of the article.

    enum GhostKind { Image = 1, Pupil = 0, Both = -1 }

    class Options
    {
        public GhostKind Kind = GhostKind.Image;
        public int TopN = 3;          // 0 => one GPIM with Surf1=Surf2=-1
        public double Weight = 1.0;
        public bool Optimize;
        public int Cycles = 10;       // 0 = automatic
        public string FilePath;
        public string SavePath;
        public bool NoDialog;
        public readonly HashSet<string> Explicit =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    class GhostHit
    {
        public int Mode;
        public int Surf1;
        public int Surf2;
        public double Gpim;
        public int Wfb;
        public int Wsb;
        public string Label { get { return Mode == 1 ? "image" : "pupil"; } }
    }

    class Program
    {
        static Options Opts = new Options();
        static ZOSAPI.IZOSAPI_Application App;
        static CultureInfo CI = CultureInfo.InvariantCulture;

        // Cached GPIM MFE cell indices, filled from the operand's own headers
        // the first time we insert a GPIM row. Help names Surf1, Surf2, Mode,
        // WFB, WSB but does not say which Param column they occupy.
        static int ColSurf1 = -1, ColSurf2 = -1, ColMode = -1, ColWfb = -1, ColWsb = -1;
        static bool MapReady;

        static void Main(string[] args)
        {
            ParseArgs(args);
            string zosError;
            if (!ZemaxLocator.TryInitialize(out zosError))
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation."
                                  + (zosError == null ? "" : "  " + zosError));
                Environment.ExitCode = 1;
                return;
            }
            try { Run(); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }
 marquee unused
        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string al = a.ToLowerInvariant();
                string next() => (i + 1 < args.Length) ? args[++i] : null;
                switch (al)
                {
                    case "-top": Opts.TopN = int.Parse(next(), CI); Opts.Explicit.Add("top"); break;
                    case "-weight": Opts.Weight = double.Parse(next(), CI); Opts.Explicit.Add("weight"); break;
                    case "-mode":
                        Opts.Kind = ParseKind(next());
                        Opts.Explicit.Add("mode");
                        break;
                    case "-optimize": Opts.Optimize = true; Opts.Explicit.Add("optimize"); break;
                    case "-cycles": Opts.Cycles = int.Parse(next(), CI); Opts.Explicit.Add("cycles"); break;
                    case "-save": Opts.SavePath = next(); break;
                    case "-file": Opts.FilePath = next(); break;
                    case "-nodialog": Opts.NoDialog = true; break;
                    case "-quiet": break;
                    default:
                        if (al.StartsWith("-z")) break;
                        if (al.StartsWith("-"))
                            throw new Exception("unknown flag " + a);
                        break;
                }
            }
        }

        static GhostKind ParseKind(string s)
        {
            if (s == null) throw new Exception("-mode needs image, pupil, or both");
            switch (s.Trim().ToLowerInvariant())
            {
                case "image": case "1": return GhostKind.Image;
                case "pupil": case "0": return GhostKind.Pupil;
                case "both": return GhostKind.Both;
                default: throw new Exception("unknown -mode " + s + " (use image, pupil, or both)");
            }
        }

        static void Validate(Options o)
        {
            if (o.TopN < 0) throw new Exception("-top must be >= 0");
            if (o.Weight < 0) throw new Exception("-weight must be >= 0");
            if (o.Cycles < 0) throw new Exception("-cycles must be >= 0");
        }

        static void Run()
        {
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                App = connection.CreateNewApplication();
                if (App == null || App.PrimarySystem == null || !App.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                if (!App.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    App.CloseApplication();
                    throw new Exception("could not load " + Opts.FilePath);
                }
            }
            else
            {
                try { App = connection.ConnectToApplication(); } catch { App = null; }
                if (App == null)
                {
                    try { App = connection.ConnectAsExtension(0); } catch { App = null; }
                }
                if (App == null || App.PrimarySystem == null)
                    throw new Exception("could not connect to OpticStudio (use the Programming ribbon or Interactive Extension)");
                if (!App.IsValidLicenseForAPI)
                    throw new Exception("license is not valid for ZOS-API: " + App.LicenseStatus);
            }

            try
            {
                if (!Opts.NoDialog && !standalone)
                {
                    if (!SettingsDialog.Show(Opts)) return;
                }
                Validate(Opts);
                Apply(App);
            }
            finally
            {
                if (standalone) App.CloseApplication();
                else
                {
                    App.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(App.ProgressMessage) || !App.ProgressMessage.StartsWith("Done"))
                        App.ProgressMessage = "GPIM ghost reduce finished.";
                }
            }
        }

        static void Apply(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("this extension requires a sequential system");

            var lde = sys.LDE;
            int img = lde.NumberOfSurfaces - 1;
            if (img < 3)
                throw new Exception("need at least one real surface between object and image");

            Say("=== GpimGhostReduce ===");
            Say("Article : https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator");
            Say("Lens    : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(string.Format(CI, "Surfaces: OBJ=0 .. IMA={0}", img));
            Say("Kind    : " + Opts.Kind + ", top " + Opts.TopN + ", weight " + Opts.Weight.ToString("0.###", CI));

            var modes = new List<int>();
            if (Opts.Kind == GhostKind.Image || Opts.Kind == GhostKind.Both) modes.Add(1);
            if (Opts.Kind == GhostKind.Pupil || Opts.Kind == GhostKind.Both) modes.Add(0);

            var mfe = sys.MFE;
            double mfBefore = SafeMf(mfe);

            var chosen = new List<GhostHit>();
            foreach (int mode in modes)
            {
                if (Cancelled()) return;
                var ranked = Rank(sys, mfe, img, mode);
                Say("");
                Say(mode == 1 ? "-- image ghosts (Mode 1) --" : "-- pupil ghosts (Mode 0) --");
                if (ranked.Count == 0)
                {
                    Say("  none with a usable GPIM value");
                    continue;
                }
                int show = Math.Min(10, ranked.Count);
                for (int i = 0; i < show; i++)
                {
                    var h = ranked[i];
                    Say(string.Format(CI, "  {0,2}. Surf {1,2} then {2,2}   GPIM={3:0.6e}  (WFB={4} WSB={5})",
                        i + 1, h.Surf1, h.Surf2, h.Gpim, h.Wfb, h.Wsb));
                }
                if (Opts.TopN == 0)
                {
                    chosen.Add(new GhostHit { Mode = mode, Surf1 = -1, Surf2 = -1, Gpim = ranked[0].Gpim, Wfb = ranked[0].Wfb, Wsb = ranked[0].Wsb });
                }
                else
                {
                    int n = Math.Min(Opts.TopN, ranked.Count);
                    for (int i = 0; i < n; i++) chosen.Add(ranked[i]);
                }
            }

            if (chosen.Count == 0)
                throw new Exception("no ghost pairs to constrain — nothing appended to the merit function");

            int added = InsertOperands(mfe, chosen);
            Say("");
            Say("Appended " + added + " GPIM operand(s), target 0, leaving the original merit function in place.");

            double mfAfterInsert = SafeMf(mfe);
            Say(string.Format(CI, "Merit function: {0:0.6e} -> {1:0.6e} after insert", mfBefore, mfAfterInsert));

            if (Opts.Optimize)
            {
                if (Cancelled()) return;
                Say("Running local DLS (" + (Opts.Cycles == 0 ? "automatic cycles" : Opts.Cycles + " cycles") + ")...");
                RunLocalOpt(sys);
                double mfAfterOpt = SafeMf(mfe);
                Say(string.Format(CI, "Merit function after DLS: {0:0.6e}", mfAfterOpt));
                Say("Re-ranked after optimize:");
                foreach (int mode in modes)
                {
                    if (Cancelled()) return;
                    var ranked = Rank(sys, mfe, img, mode);
                    int n = Math.Min(3, ranked.Count);
                    for (int i = 0; i < n; i++)
                    {
                        var h = ranked[i];
                        Say(string.Format(CI, "  {0}  Surf {1} then {2}  GPIM={3:0.6e}", h.Label, h.Surf1, h.Surf2, h.Gpim));
                    }
                }
            }

            if (!string.IsNullOrEmpty(Opts.SavePath))
            {
                string full = System.IO.Path.GetFullPath(Opts.SavePath);
                sys.SaveAs(full);
                if (!System.IO.File.Exists(full))
                    throw new Exception("SaveAs reported no error but wrote no file at " + full);
                Say("saved " + full);
            }

            Say("");
            Say("Next (from the article): Analyze > Ghost Focus Generator on the worst pair,");
            Say("save the double-bounce file, Geometric Image Analysis for peak irradiance.");
            Say("GPIM only defocuses the ghost; it does not replace a coating or NSC stray-light check.");
            app.ProgressMessage = "Done — " + added + " GPIM operand(s) appended.";
        }

        static List<GhostHit> Rank(ZOSAPI.IOpticalSystem sys, IMeritFunctionEditor mfe, int img, int mode)
        {
            var hits = new List<GhostHit>();
            int scratch = EnsureScratchGpim(mfe);
            var op = mfe.GetOperandAt(scratch);
            LearnMap(op);

            int done = 0, total = 0;
            for (int s1 = 2; s1 < img; s1++)
                for (int s2 = 1; s2 < s1; s2++) total++;

            for (int s1 = 2; s1 < img; s1++)
            {
                for (int s2 = 1; s2 < s1; s2++)
                {
                    if (Cancelled()) return hits;
                    done++;
                    if (done == 1 || done % 25 == 0)
                    {
                        App.ProgressPercent = (int)(100.0 * done / Math.Max(1, total));
                        App.ProgressMessage = string.Format(CI, "Scanning {0} ghosts {1}/{2}",
                            mode == 1 ? "image" : "pupil", done, total);
                    }
                    WriteGpim(op, s1, s2, mode);
                    double v = ReadValue(op, mfe);
                    if (!Usable(v)) continue;
                    int wfb, wsb;
                    ReadWorst(op, out wfb, out wsb);
                    hits.Add(new GhostHit { Mode = mode, Surf1 = s1, Surf2 = s2, Gpim = v, Wfb = wfb, Wsb = wsb });
                }
            }

            WriteGpim(op, -1, -1, mode);
            double all = ReadValue(op, mfe);
            int awfb, awsb;
            ReadWorst(op, out awfb, out awsb);
            if (Usable(all))
                Say(string.Format(CI, "  OpticStudio worst-of-all (Surf1=Surf2=-1): GPIM={0:0.6e}  WFB={1} WSB={2}",
                    all, awfb, awsb));

            mfe.RemoveOperandAt(scratch);
            hits.Sort((a, b) => b.Gpim.CompareTo(a.Gpim));
            return hits;
        }

        static int InsertOperands(IMeritFunctionEditor mfe, List<GhostHit> chosen)
        {
            int added = 0;
            foreach (var h in chosen)
            {
                if (FindExisting(mfe, h.Mode, h.Surf1, h.Surf2) > 0)
                {
                    Say(string.Format(CI, "  skip existing GPIM {0} Surf {1}/{2}", h.Label, h.Surf1, h.Surf2));
                    continue;
                }
                if (added == 0)
                {
                    int blank = mfe.NumberOfOperands + 1;
                    mfe.InsertNewOperandAt(blank);
                    var bl = mfe.GetOperandAt(blank);
                    bl.ChangeType(MeritOperandType.BLNK);
                    try { SetComment(bl, "GPIM ghost reduce (" + h.Label + ")"); } catch { }
                }
                int row = mfe.NumberOfOperands + 1;
                mfe.InsertNewOperandAt(row);
                var op = mfe.GetOperandAt(row);
                op.ChangeType(MeritOperandType.GPIM);
                LearnMap(op);
                WriteGpim(op, h.Surf1, h.Surf2, h.Mode);
                op.Target = 0.0;
                op.Weight = Opts.Weight;
                added++;
            }
            return added;
        }

        static int FindExisting(IMeritFunctionEditor mfe, int mode, int s1, int s2)
        {
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                var op = mfe.GetOperandAt(i);
                if (op.Type != MeritOperandType.GPIM) continue;
                LearnMap(op);
                int ms1 = ReadInt(op, ColSurf1, int.MinValue);
                int ms2 = ReadInt(op, ColSurf2, int.MinValue);
                int mm = ReadInt(op, ColMode, int.MinValue);
                if (ms1 == s1 && ms2 == s2 && mm == mode) return i;
            }
            return 0;
        }

        static int EnsureScratchGpim(IMeritFunctionEditor mfe)
        {
            int row = mfe.NumberOfOperands + 1;
            mfe.InsertNewOperandAt(row);
            var op = mfe.GetOperandAt(row);
            op.ChangeType(MeritOperandType.GPIM);
            op.Weight = 0.0;
            op.Target = 0.0;
            return row;
        }

        static void LearnMap(IOperand op)
        {
            if (MapReady) return;
            int n = 16;
            try { if (op.Editor != null) n = Math.Max(8, op.Editor.NumberOfColumns); } catch { }
            for (int c = 0; c < n; c++)
            {
                IEditorCell cell = null;
                try { cell = op.GetCellAt(c); } catch { continue; }
                if (cell == null) continue;
                string h = null;
                try { h = cell.Header; } catch { }
                if (string.IsNullOrEmpty(h)) continue;
                string u = h.Trim().ToUpperInvariant().Replace(" ", "");
                if (u == "SURF1" || u == "SUR1") ColSurf1 = c;
                else if (u == "SURF2" || u == "SUR2") ColSurf2 = c;
                else if (u == "MODE") ColMode = c;
                else if (u == "WFB") ColWfb = c;
                else if (u == "WSB") ColWsb = c;
            }
            if (ColSurf1 < 0) ColSurf1 = 2;
            if (ColSurf2 < 0) ColSurf2 = 3;
            if (ColMode < 0) ColMode = 4;
            MapReady = true;
            Say(string.Format(CI, "  GPIM cells: Surf1=col {0}, Surf2=col {1}, Mode=col {2}, WFB=col {3}, WSB=col {4}",
                ColSurf1, ColSurf2, ColMode, ColWfb, ColWsb));
        }

        static void WriteGpim(IOperand op, int s1, int s2, int mode)
        {
            SetInt(op, ColSurf1, s1);
            SetInt(op, ColSurf2, s2);
            SetInt(op, ColMode, mode);
        }

        static void SetInt(IOperand op, int col, int value)
        {
            if (col < 0) return;
            var cell = op.GetCellAt(col);
            if (cell == null) return;
            if (cell.DataType == CellDataType.Integer) cell.IntegerValue = value;
            else cell.DoubleValue = value;
        }

        static int ReadInt(IOperand op, int col, int missing)
        {
            if (col < 0) return missing;
            var cell = op.GetCellAt(col);
            if (cell == null) return missing;
            try
            {
                if (cell.DataType == CellDataType.Integer) return cell.IntegerValue;
                return (int)Math.Round(cell.DoubleValue);
            }
            catch { return missing; }
        }

        static void ReadWorst(IOperand op, out int wfb, out int wsb)
        {
            wfb = ReadInt(op, ColWfb, 0);
            wsb = ReadInt(op, ColWsb, 0);
        }

        static double ReadValue(IOperand op, IMeritFunctionEditor mfe)
        {
            try
            {
                double v = op.Value;
                if (Usable(v)) return v;
            }
            catch { }
            try { mfe.CalculateMeritFunction(); } catch { }
            try { return op.Value; } catch { return double.NaN; }
        }

        static bool Usable(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            if (v == 0.0) return false;
            if (Math.Abs(v) > 1e8) return false;
            return true;
        }

        static void SetComment(IOperand op, string text)
        {
            int n = 16;
            try { if (op.Editor != null) n = Math.Max(8, op.Editor.NumberOfColumns); } catch { }
            for (int c = 1; c < n; c++)
            {
                IEditorCell cell = null;
                try { cell = op.GetCellAt(c); } catch { continue; }
                if (cell == null) continue;
                if (cell.DataType == CellDataType.String)
                {
                    cell.Value = text;
                    return;
                }
            }
        }

        static void RunLocalOpt(ZOSAPI.IOpticalSystem sys)
        {
            var tool = sys.Tools.OpenLocalOptimization();
            if (tool == null) throw new Exception("could not open local optimization");
            try
            {
                tool.Algorithm = OptimizationAlgorithm.DampedLeastSquares;
                try
                {
                    if (Opts.Cycles <= 0) tool.Cycles = OptimizationCycles.Automatic;
                    else if (Opts.Cycles <= 5) tool.Cycles = OptimizationCycles.Fixed_5_Cycles;
                    else if (Opts.Cycles <= 10) tool.Cycles = OptimizationCycles.Fixed_10_Cycles;
                    else if (Opts.Cycles <= 50) tool.Cycles = OptimizationCycles.Fixed_50_Cycles;
                    else tool.Cycles = OptimizationCycles.Automatic;
                }
                catch { }
                tool.RunAndWaitForCompletion();
            }
            finally
            {
                try { tool.Close(); } catch { }
            }
        }

        static double SafeMf(IMeritFunctionEditor mfe)
        {
            try { return mfe.CalculateMeritFunction(); }
            catch { return double.NaN; }
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

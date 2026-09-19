using System;
using System.Collections.Generic;
using System.Globalization;
using ZOSAPI;
using ZOSAPI.Editors;
using ZOSAPI.Editors.MFE;
using ZOSAPI.Tools.Optimization;

namespace GpimGhostReduce
{
    // ============================================================
    // GpimGhostReduce - what this program does (plain words)
    // ============================================================
    // Lenses can make "ghost" images: light bounces the wrong way
    // off two surfaces and still lands near the real image. That
    // foggy spot hurts the picture. This tool finds the worst
    // double-bounce pairs, then adds GPIM rows to the merit
    // function (the report card for how good the lens is).
    // GPIM is basically "how focused is this ghost?" — big means
    // sharp and bad; we aim for 0 so the ghost is defocused.
    // We never delete your old report-card rows; we only append.
    // Weights are scaled so ghosts pull about as hard as the rest
    // of the report card (balance = 1 means equal pull).
    // Optionally we then run a short local optimize (DLS).
    // Based on Ansys Optics "Stray Light Analysis with Ghost Focus
    // Generator". Run from User Extensions or with -file / -save.
    // ============================================================

    // Which kind of ghost to hunt: near the image, near the pupil, or both.
    enum GhostKind { Image = 1, Pupil = 0, Both = -1 }

    // Switches from the command line or the settings window.
    class Options
    {
        public GhostKind Kind = GhostKind.Image;
        public int TopN = 0;            // 0 = pick automatically from the scan, else max pairs to keep
        public double Weight = 1.0;     // used only when -weight is typed in by hand
        public double Balance = 1.0;    // how hard ghosts pull vs the existing report card (1 = equal)
        public bool Optimize;
        public int Cycles = 10;
        public string FilePath;
        public string SavePath;
        public bool NoDialog;
        // Flags the user typed so we do not overwrite them from lastrun.txt.
        public readonly HashSet<string> Explicit =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    // One double-bounce ghost pair we measured (surfaces, score, weight).
    class GhostHit
    {
        public int Mode;
        public int Surf1;
        public int Surf2;
        public double Gpim;
        public double Weight;
        public int Wfb;
        public int Wsb;
        public string Label { get { return Mode == 1 ? "image" : "pupil"; } }
    }

    class Program
    {
        static Options Opts = new Options();
        static ZOSAPI.IZOSAPI_Application App;
        static CultureInfo CI = CultureInfo.InvariantCulture;

        // Column numbers for GPIM cells — learned once from the row headers.
        static int ColSurf1 = -1, ColSurf2 = -1, ColMode = -1, ColWfb = -1, ColWsb = -1;
        static bool MapReady;

        const int AutoCap = 8;          // at most this many pairs when TopN is auto
        const double HotFrac = 0.10;    // stop if the next pair is under 10% of the hottest
        const double CoverFrac = 0.80;  // stop once we have covered 80% of total ghost score
        const double EmptyMf = 1e-12;   // treat tinier report-card scores as "empty"

        // Start here: find OpticStudio, then run the ghost scan.
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

        // Read the flags you typed (-top, -balance, -optimize, ...).
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
                    case "-balance": Opts.Balance = double.Parse(next(), CI); Opts.Explicit.Add("balance"); break;
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

        // Turn the word image / pupil / both into our GhostKind enum.
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

        // Make sure numbers are not negative before we touch the lens.
        static void Validate(Options o)
        {
            if (o.TopN < 0) throw new Exception("-top must be >= 0");
            if (o.Weight < 0) throw new Exception("-weight must be >= 0");
            if (o.Balance < 0) throw new Exception("-balance must be >= 0");
            if (o.Cycles < 0) throw new Exception("-cycles must be >= 0");
        }

        // Connect to OpticStudio (open file or attach to the live app), show the
        // settings window if needed, then do the real work in Apply.
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
                string connectError;
                if (!ZemaxLocator.TryConnect(out App, out connectError, false))
                    throw new Exception(connectError);
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

        // Main job: scan every double-bounce pair, pick the hot ones, append GPIM
        // rows (never replace the old report card), optionally optimize, maybe save.
        static void Apply(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
                throw new Exception("this extension requires a sequential system");

            var lde = sys.LDE;
            int img = lde.NumberOfSurfaces - 1;
            if (img < 3)
                throw new Exception("need at least one real surface between object and image");

            var mfe = sys.MFE;
            int baseline = mfe.NumberOfOperands;
            int weighted = CountWeighted(mfe);
            double mfBefore = SafeMf(mfe);

            Say("=== GpimGhostReduce ===");
            Say("Article : https://optics.ansys.com/hc/en-us/articles/43071067483795-Stray-Light-Analysis-with-Ghost-Focus-Generator");
            Say("Lens    : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(string.Format(CI, "Surfaces: OBJ=0 .. IMA={0}", img));
            Say(string.Format(CI, "Existing MFE: {0} operand(s), {1} weighted, MF={2:E6}",
                baseline, weighted, mfBefore));
            Say("Kind    : " + Opts.Kind
                + (Opts.TopN == 0 ? ", auto pairs" : ", max " + Opts.TopN)
                + (Opts.Explicit.Contains("weight")
                    ? ", raw weight " + Opts.Weight.ToString("0.###", CI)
                    : ", balance " + Opts.Balance.ToString("0.###", CI)));

            var modes = new List<int>();
            if (Opts.Kind == GhostKind.Image || Opts.Kind == GhostKind.Both) modes.Add(1);
            if (Opts.Kind == GhostKind.Pupil || Opts.Kind == GhostKind.Both) modes.Add(0);

            var chosen = new List<GhostHit>();
            foreach (int mode in modes)
            {
                if (Cancelled()) return;
                var ranked = Rank(sys, mfe, img, mode);
                Say("");
                Say(mode == 1 ? "-- image ghosts (Mode 1), full scan --" : "-- pupil ghosts (Mode 0), full scan --");
                if (ranked.Count == 0)
                {
                    Say("  none with a usable GPIM value");
                    continue;
                }
                int show = Math.Min(10, ranked.Count);
                for (int i = 0; i < show; i++)
                {
                    var h = ranked[i];
                    Say(string.Format(CI, "  {0,2}. Surf {1,2} then {2,2}   GPIM={3:E6}  (WFB={4} WSB={5})",
                        i + 1, h.Surf1, h.Surf2, h.Gpim, h.Wfb, h.Wsb));
                }
                var pick = SelectNeeded(ranked);
                Say(string.Format(CI, "  keeping {0} of {1} pair(s)", pick.Count, ranked.Count));
                chosen.AddRange(pick);
            }

            if (chosen.Count == 0)
                throw new Exception("no ghost pairs to constrain — existing merit function unchanged");

            AssignWeights(chosen, mfBefore, weighted);
            var addedRows = new List<int>();
            int added = InsertOperands(mfe, chosen, baseline, addedRows);
            Say("");
            Say("Appended " + added + " GPIM operand(s), target 0. Original " + baseline + " operand(s) left in place.");

            double mfAfterInsert = SafeMf(mfe);
            double designAfterInsert = DesignOnlyMf(mfe, addedRows);
            Say(string.Format(CI, "Merit function: existing {0:E6} -> combined {1:E6} after insert (design-only {2:E6})",
                mfBefore, mfAfterInsert, designAfterInsert));

            bool ranOpt = false;
            if (Opts.Optimize)
            {
                if (weighted == 0 || !UsableMf(mfBefore))
                {
                    Say("Skipping DLS: existing merit function has no weighted operands, so optimize would only chase ghosts and dump image quality.");
                    Say("Add a normal MF (Optimization Wizard) first, then re-run with -optimize.");
                }
                else
                {
                    if (Cancelled()) return;
                    Say("Running local DLS (" + (Opts.Cycles == 0 ? "automatic cycles" : Opts.Cycles + " cycles") + ")...");
                    RunLocalOpt(sys);
                    ranOpt = true;
                    double mfAfterOpt = SafeMf(mfe);
                    double designAfterOpt = DesignOnlyMf(mfe, addedRows);
                    Say(string.Format(CI, "After DLS: combined MF={0:E6}  design-only MF={1:E6} (was {2:E6})",
                        mfAfterOpt, designAfterOpt, mfBefore));
                    if (UsableMf(mfBefore) && UsableMf(designAfterOpt) && designAfterOpt > 2.0 * mfBefore)
                        Say("Note: design-only MF more than doubled — ghosts and image quality are fighting; try a lower -balance.");
                    Say("Re-ranked after optimize:");
                    foreach (int mode in modes)
                    {
                        if (Cancelled()) return;
                        var ranked = Rank(sys, mfe, img, mode);
                        int n = Math.Min(3, ranked.Count);
                        for (int i = 0; i < n; i++)
                        {
                            var h = ranked[i];
                            Say(string.Format(CI, "  {0}  Surf {1} then {2}  GPIM={3:E6}", h.Label, h.Surf1, h.Surf2, h.Gpim));
                        }
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
            app.ProgressMessage = ranOpt
                ? "Done — " + added + " GPIM operand(s) appended and DLS run."
                : "Done — " + added + " GPIM operand(s) appended; existing MF kept.";
        }

        // Try every surface pair with a temporary GPIM row and rank by score (hottest first).
        // Surf1=Surf2=-1 asks OpticStudio for its own worst-of-all reading (API quirk).
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
                    if (Cancelled())
                    {
                        mfe.RemoveOperandAt(scratch);
                        return hits;
                    }
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

            // Surf1=Surf2=-1: ask OpticStudio for its own worst-of-all ghost reading.
            WriteGpim(op, -1, -1, mode);
            double all = ReadValue(op, mfe);
            int awfb, awsb;
            ReadWorst(op, out awfb, out awsb);
            if (Usable(all))
                Say(string.Format(CI, "  OpticStudio worst-of-all (Surf1=Surf2=-1): GPIM={0:E6}  WFB={1} WSB={2}",
                    all, awfb, awsb));

            mfe.RemoveOperandAt(scratch);
            hits.Sort((a, b) => b.Gpim.CompareTo(a.Gpim));
            return hits;
        }

        // Keep only the hottest pairs: stop at the cap, or when cooler than HotFrac,
        // or once we have CoverFrac of the total ghost score.
        static List<GhostHit> SelectNeeded(List<GhostHit> ranked)
        {
            var keep = new List<GhostHit>();
            if (ranked.Count == 0) return keep;
            int cap = Opts.TopN > 0 ? Opts.TopN : AutoCap;
            double gmax = ranked[0].Gpim;
            double total = 0;
            for (int i = 0; i < ranked.Count; i++) total += ranked[i].Gpim;
            double acc = 0;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (keep.Count >= cap) break;
                var h = ranked[i];
                if (keep.Count > 0 && h.Gpim < HotFrac * gmax) break;
                keep.Add(h);
                acc += h.Gpim;
                if (acc >= CoverFrac * total) break;
            }
            if (keep.Count == 0) keep.Add(ranked[0]);
            return keep;
        }

        // Set how hard each new GPIM row pulls. With -weight we use that number;
        // otherwise scale so ghost pull ≈ Balance × existing report-card pull.
        static void AssignWeights(List<GhostHit> chosen, double m0, int weighted)
        {
            if (Opts.Explicit.Contains("weight"))
            {
                for (int i = 0; i < chosen.Count; i++)
                    chosen[i].Weight = Opts.Weight;
                Say("Using explicit GPIM weight " + Opts.Weight.ToString("0.###", CI) + " on every added row.");
                return;
            }

            if (weighted == 0 || !UsableMf(m0))
            {
                for (int i = 0; i < chosen.Count; i++)
                    chosen[i].Weight = 1.0;
                Say("Existing MF is empty — GPIM weight 1.0, no DLS so the lens is not rebuilt around ghosts.");
                return;
            }

            int n = chosen.Count;
            // Merit function is roughly sum of (weight * value)^2; match Balance*m0 of pull.
            double budget = (Opts.Balance * m0) * (Opts.Balance * m0);
            Say(string.Format(CI,
                "Scaling GPIM weights so ghost contribution equals {0:0.###}× existing MF² ({1:E6}).",
                Opts.Balance, budget));
            for (int i = 0; i < n; i++)
            {
                double g = chosen[i].Gpim;
                double g2 = g * g;
                double w;
                if (g2 < 1e-30) w = 0;
                else w = budget / (n * g2);
                if (w > 1e6) w = 1e6;
                if (w < 1e-6 && w > 0) w = 1e-6;
                chosen[i].Weight = w;
                Say(string.Format(CI, "  Surf {0}/{1}  GPIM={2:E6}  weight={3:E6}",
                    chosen[i].Surf1, chosen[i].Surf2, g, w));
            }
        }

        // Append blank + GPIM rows at the end. Skip pairs already in the report card.
        // Abort if somehow the old rows got shorter (we must never erase them).
        static int InsertOperands(IMeritFunctionEditor mfe, List<GhostHit> chosen, int baseline, List<int> addedRows)
        {
            int added = 0;
            bool header = false;
            for (int i = 0; i < chosen.Count; i++)
            {
                var h = chosen[i];
                if (FindExisting(mfe, h.Mode, h.Surf1, h.Surf2) > 0)
                {
                    Say(string.Format(CI, "  skip existing GPIM {0} Surf {1}/{2}", h.Label, h.Surf1, h.Surf2));
                    continue;
                }
                if (!header)
                {
                    int blank = mfe.NumberOfOperands + 1;
                    mfe.InsertNewOperandAt(blank);
                    var bl = mfe.GetOperandAt(blank);
                    bl.ChangeType(MeritOperandType.BLNK);
                    try { SetComment(bl, "GPIM ghost reduce (" + h.Label + ")"); } catch { }
                    header = true;
                }
                int row = mfe.NumberOfOperands + 1;
                mfe.InsertNewOperandAt(row);
                var op = mfe.GetOperandAt(row);
                op.ChangeType(MeritOperandType.GPIM);
                LearnMap(op);
                WriteGpim(op, h.Surf1, h.Surf2, h.Mode);
                // GPIM = 1/|z_ghost - z_image|; target 0 means "please defocus this ghost."
                op.Target = 0.0;
                op.Weight = h.Weight;
                addedRows.Add(row);
                added++;
            }
            if (mfe.NumberOfOperands < baseline)
                throw new Exception("existing merit function was shortened — aborting without save");
            return added;
        }

        // Look for an existing GPIM row with the same mode and surface pair.
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

        // Add a temporary weight-0 GPIM row we reuse while scanning, then delete.
        static int EnsureScratchGpim(IMeritFunctionEditor mfe)
        {
            int row = mfe.NumberOfOperands + 1;
            mfe.InsertNewOperandAt(row);
            var op = mfe.GetOperandAt(row);
            op.ChangeType(MeritOperandType.GPIM);
            op.Weight = 0.0; // scratch row must not pull on the real report card
            op.Target = 0.0;
            return row;
        }

        // Count report-card rows that actually pull (weight > 0, not blank).
        static int CountWeighted(IMeritFunctionEditor mfe)
        {
            int n = 0;
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                var op = mfe.GetOperandAt(i);
                try
                {
                    if (op.Type == MeritOperandType.BLNK) continue;
                    if (op.Weight > 0) n++;
                }
                catch { }
            }
            return n;
        }

        // Temporarily zero our new GPIM weights so we can read the old design score alone.
        static double DesignOnlyMf(IMeritFunctionEditor mfe, List<int> gpimRows)
        {
            var saved = new List<double>();
            for (int i = 0; i < gpimRows.Count; i++)
            {
                var op = mfe.GetOperandAt(gpimRows[i]);
                saved.Add(op.Weight);
                op.Weight = 0; // zero only our new rows so we see the old design score
            }
            double v = SafeMf(mfe);
            for (int i = 0; i < gpimRows.Count; i++)
                mfe.GetOperandAt(gpimRows[i]).Weight = saved[i];
            try { mfe.CalculateMeritFunction(); } catch { }
            return v;
        }

        // Grab one cell from a merit-function row (OpticStudio 2026 uses GetOperandCell).
        static IEditorCell GetCell(IMFERow op, int col)
        {
            if (col < 0) return null;
            try { return op.GetOperandCell((MeritColumn)col); }
            catch { return null; }
        }

        // Figure out which columns are Surf1 / Surf2 / Mode / WFB / WSB from headers once.
        static void LearnMap(IMFERow op)
        {
            if (MapReady) return;
            int n = (int)MeritColumn.Contrib;
            for (int c = 1; c <= n; c++)
            {
                IEditorCell cell = GetCell(op, c);
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
            if (ColSurf1 < 0) ColSurf1 = (int)MeritColumn.Param1;
            if (ColSurf2 < 0) ColSurf2 = (int)MeritColumn.Param2;
            if (ColMode < 0) ColMode = (int)MeritColumn.Param3;
            MapReady = true;
            Say(string.Format(CI, "  GPIM cells: Surf1=col {0}, Surf2=col {1}, Mode=col {2}, WFB=col {3}, WSB=col {4}",
                ColSurf1, ColSurf2, ColMode, ColWfb, ColWsb));
        }

        // Fill Surf1, Surf2, and Mode on a GPIM row.
        static void WriteGpim(IMFERow op, int s1, int s2, int mode)
        {
            SetInt(op, ColSurf1, s1);
            SetInt(op, ColSurf2, s2);
            SetInt(op, ColMode, mode);
        }

        // Write an integer cell, or a double if that is what OpticStudio gave us.
        static void SetInt(IMFERow op, int col, int value)
        {
            var cell = GetCell(op, col);
            if (cell == null) return;
            if (cell.DataType == CellDataType.Integer) cell.IntegerValue = value;
            else cell.DoubleValue = value;
        }

        // Read an integer cell; return missing if the cell is gone or weird.
        static int ReadInt(IMFERow op, int col, int missing)
        {
            var cell = GetCell(op, col);
            if (cell == null) return missing;
            try
            {
                if (cell.DataType == CellDataType.Integer) return cell.IntegerValue;
                return (int)Math.Round(cell.DoubleValue);
            }
            catch { return missing; }
        }

        // Read WFB / WSB (worst field / worst wavelength OpticStudio reports).
        static void ReadWorst(IMFERow op, out int wfb, out int wsb)
        {
            wfb = ReadInt(op, ColWfb, 0);
            wsb = ReadInt(op, ColWsb, 0);
        }

        // Recalculate the report card, then read this row's GPIM value.
        static double ReadValue(IMFERow op, IMeritFunctionEditor mfe)
        {
            try { mfe.CalculateMeritFunction(); } catch { }
            try { return op.Value; } catch { return double.NaN; }
        }

        // True if this GPIM number looks real (not NaN, zero, or absurdly huge).
        static bool Usable(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            if (v == 0.0) return false;
            if (Math.Abs(v) > 1e8) return false;
            return true;
        }

        // True if the overall report-card score is big enough to trust for weighting.
        static bool UsableMf(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            return v > EmptyMf;
        }

        // Put a short note in the comment cell of a blank/header row.
        static void SetComment(IMFERow op, string text)
        {
            var cell = GetCell(op, (int)MeritColumn.Comment);
            if (cell != null && cell.DataType == CellDataType.String)
            {
                cell.Value = text;
                return;
            }
            int n = (int)MeritColumn.Contrib;
            for (int c = 1; c <= n; c++)
            {
                cell = GetCell(op, c);
                if (cell == null) continue;
                if (cell.DataType == CellDataType.String)
                {
                    cell.Value = text;
                    return;
                }
            }
        }

        // Run a short local optimize (damped least squares) so the lens can defocus ghosts.
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

        // Calculate the report-card score; return NaN if OpticStudio throws.
        static double SafeMf(IMeritFunctionEditor mfe)
        {
            try { return mfe.CalculateMeritFunction(); }
            catch { return double.NaN; }
        }

        // User hit Cancel in OpticStudio — stop scanning cleanly.
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

        // Print to the console and also show the line in OpticStudio's progress bar.
        static void Say(string s)
        {
            Console.WriteLine(s);
            try { if (App != null) App.ProgressMessage = s; } catch { }
        }
    }
}

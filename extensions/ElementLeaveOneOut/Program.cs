// ============================================================
// ElementLeaveOneOut - what this program does (plain words)
// ============================================================
// Imagine a stack of lenses. We want to know which lens we need
// the least. So we try this game for each lens:
//   1) Take that lens out.
//   2) Tweak the leftover lenses a little (local optimize).
//   3) Check the "report card" score (merit function).
// The report card number going UP a little means "we miss that
// lens a little." Going UP a lot means "we really needed it."
// We keep the try where the report card got worse the LEAST
// (or even got better), and save a new .zmx with that lens gone.
//
// Also: glass and air gaps are not allowed to go negative or
// paper-thin. If the report card has no thickness rules yet,
// we add them (glass at least 1 mm, air at least 0.5 mm).
//
// Flags you can pass in:
//   -file <zmx>  -save <path>  -out <dir>  -cycles K  -top N
//   -rank power|loo  -report [path]  -nodialog  -quiet
//   -allowbadmf   (let a weird baseline MF keep going — normally we stop)
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ElementLeaveOneOut
{
    // Little box of switches from the command line.
    class Options
    {
        public string FilePath;
        public string SavePath;
        public string OutDir;
        public string ReportPath;
        public int Cycles = 30;
        public int TopN = 0;
        public bool RankPowerOnly = false;
        public bool Quiet = false;
        // Let a bad baseline merit-function score continue (normally we refuse).
        public bool AllowBadMf = false;
    }

    // Stop the tool with a known exit code (2 = refused / fail-closed).
    class ToolExitException : Exception
    {
        public int Code;
        public ToolExitException(int code, string message) : base(message) { Code = code; }
    }

    // One removable lens or mirror: which surfaces it owns, and a rough "how strong" number.
    class ElementInfo
    {
        public int Index;
        public int Front;
        public int Rear;
        public string Label;
        public double AbsPower;
        public bool IsMirror;
    }

    // What happened when we tried removing one element (score before/after, did it work).
    class TrialResult
    {
        public ElementInfo Element;
        public double MfAfter;
        public double Delta;
        public string Error;
        public bool Ok;
    }

    class Program
    {
        static Options Opts = new Options();
        static readonly List<string> Report = new List<string>();
        // Once Quick Focus / focus-gap DLS cannot open, stop retrying every trial.
        static bool SkipStandaloneRefocus = false;
        static bool LoggedStandaloneRefocusSkip = false;

        static void Main(string[] args)
        {
            try { ParseArgs(args); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            string zosError;
            if (!ZemaxLocator.TryInitialize(out zosError))
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation."
                                  + (zosError == null ? "" : "  " + zosError));
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("Found OpticStudio at: " + (ZemaxLocator.ResolvedDirectory ?? "(unknown)"));

            try { Run(); }
            catch (ToolExitException tex)
            {
                Console.WriteLine("FATAL: " + tex.Message);
                Environment.ExitCode = tex.Code;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message + Environment.NewLine + ex.StackTrace);
                Environment.ExitCode = 1;
            }
        }

        // Read the words you typed after the program name (-file, -out, ...).
        // We just fill in the Options box so the rest of the program knows what you want.
        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (raw.StartsWith("-") || raw.StartsWith("/"))
                {
                    string a = raw.TrimStart('-', '/').ToLowerInvariant();
                    string next() => (i + 1 < args.Length) ? args[++i] : null;
                    switch (a)
                    {
                        case "file": Opts.FilePath = next(); break;
                        case "save": Opts.SavePath = next(); break;
                        case "out": Opts.OutDir = next(); break;
                        case "report":
                        {
                            // Optional path; do not consume the next token if it is another flag.
                            if (i + 1 < args.Length && !(args[i + 1].StartsWith("-") || args[i + 1].StartsWith("/")))
                                Opts.ReportPath = args[++i];
                            else
                                Opts.ReportPath = "";
                            break;
                        }
                        case "cycles": Opts.Cycles = ParseInt(next(), Opts.Cycles); break;
                        case "top": Opts.TopN = ParseInt(next(), Opts.TopN); break;
                        case "rank":
                            {
                                string v = (next() ?? "loo").ToLowerInvariant();
                                if (v == "power") Opts.RankPowerOnly = true;
                                else if (v == "loo") Opts.RankPowerOnly = false;
                                else throw new Exception("unknown -rank value (use power|loo)");
                                break;
                            }
                        case "nodialog": break;
                        case "quiet": Opts.Quiet = true; break;
                        case "allowbadmf": Opts.AllowBadMf = true; break;
                        default: throw new Exception("unknown flag " + raw);
                    }
                }
                else if (string.IsNullOrEmpty(Opts.FilePath))
                    Opts.FilePath = raw;
            }
        }

        static int ParseInt(string s, int keep)
        {
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return keep;
        }

        static void Say(string line)
        {
            Console.WriteLine(line);
            Report.Add(line);
        }

        static string F(string fmt, params object[] a) =>
            string.Format(CultureInfo.InvariantCulture, fmt, a);

        // Start OpticStudio, open the lens file (or attach to the open one),
        // then run the leave-one-out game. If we started OpticStudio ourselves,
        // we close it when we are done so it does not stay running forever.
        static void Run()
        {
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);
            ZOSAPI.IZOSAPI_Application app = null;
            ZOSAPI.IOpticalSystem sys = null;

            if (standalone)
            {
                var connection = new ZOSAPI.ZOSAPI_Connection();
                app = connection.CreateNewApplication();
                if (app == null || app.PrimarySystem == null || !app.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                sys = app.PrimarySystem;
                if (!sys.LoadFile(Opts.FilePath, false) || sys.LDE.NumberOfSurfaces < 3)
                {
                    try { app.CloseApplication(); } catch { }
                    throw new Exception("failed to load " + Opts.FilePath);
                }
                Say("Loaded: " + Opts.FilePath);
            }
            else
            {
                string connectError;
                if (!ZemaxLocator.TryConnect(out app, out connectError, false))
                    throw new Exception(connectError);
                sys = app.PrimarySystem;
                if (sys == null)
                    throw new Exception("no PrimarySystem (is OpticStudio listening?)");
                Say("Connected to OpticStudio (mode: " + app.Mode + ")");
            }

            try
            {
                try { app.ShowChangesInUI = true; } catch { }
                RunOnSystem(app, sys);
            }
            finally
            {
                if (standalone && app != null)
                {
                    try { app.CloseApplication(); } catch { }
                }
            }
        }

        // Big picture steps:
        // 1) Make sure the report card (merit function) exists and has thickness rules.
        // 2) Write down the starting score.
        // 3) Find every removable lens/mirror.
        // 4) Try removing each one, re-tweak, and remember the score change.
        // 5) Pick the friendliest removal and save that new lens file.
        static void RunOnSystem(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys)
        {
            Say("=== ElementLeaveOneOut ===");
            Say("Method: leave-one-out deletion + local DLS reopt on existing MF");

            var mfe = sys.MFE;
            if (mfe == null || mfe.NumberOfOperands < 1 || !HasPositiveWeight(mfe))
            {
                Say("Merit function empty/unweighted - building default RMS spot MF (Optimization Wizard defaults).");
                SeedBaselineMf(sys);
                mfe = sys.MFE;
            }

            EnsureThicknessConstraints(sys);
            mfe = sys.MFE;
            double mf0 = mfe.CalculateMeritFunction();
            double effl0 = SafeEffl(sys);
            Say(F("Baseline MF: {0:G8}", mf0));
            Say(F("Baseline EFFL: {0:G8}", effl0));
            // Health gate: refuse a nonsense baseline unless -allowbadmf.
            GateBaselineMf(mf0);

            var elements = EnumerateElements(sys);
            if (elements.Count < 1)
                throw new Exception("no removable lens/mirror elements found");
            Say(F("Removable elements: {0}", elements.Count));
            foreach (var e in elements)
                Say(F("  E{0}: surfaces {1}-{2}  |power|~{3:G4}  {4}  {5}",
                    e.Index, e.Front, e.Rear, e.AbsPower,
                    e.IsMirror ? "mirror" : "lens", e.Label));

            string workDir = Opts.OutDir;
            if (string.IsNullOrEmpty(workDir))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                workDir = string.IsNullOrEmpty(src)
                    ? Path.Combine(Path.GetTempPath(), "ElementLeaveOneOut")
                    : Path.Combine(Path.GetDirectoryName(src) ?? Path.GetTempPath(), "_ElementLeaveOneOut");
            }
            Directory.CreateDirectory(workDir);
            string baseCopy = Path.Combine(workDir, "_baseline_loo.zmx");
            sys.SaveAs(baseCopy);
            Say("Baseline copy: " + baseCopy);

            List<ElementInfo> candidates = elements.OrderBy(e => e.AbsPower).ToList();
            if (Opts.TopN > 0 && Opts.TopN < candidates.Count)
            {
                candidates = candidates.Take(Opts.TopN).ToList();
                Say(F("Prefilter: testing {0} weakest-|power| element(s)", candidates.Count));
            }

            var trials = new List<TrialResult>();
            if (Opts.RankPowerOnly)
            {
                trials.Add(RunTrial(app, sys, baseCopy, candidates[0], mf0, effl0));
            }
            else
            {
                int n = 0;
                foreach (var el in candidates)
                {
                    n++;
                    if (app.TerminateRequested) { Say("Terminate requested."); break; }
                    try
                    {
                        app.ProgressPercent = (int)(100.0 * n / Math.Max(candidates.Count, 1));
                        app.ProgressMessage = F("LOO E{0} ({1}/{2})", el.Index, n, candidates.Count);
                    }
                    catch { }
                    Say("");
                    Say(F("--- Trial E{0} surfaces {1}-{2} ---", el.Index, el.Front, el.Rear));
                    trials.Add(RunTrial(app, sys, baseCopy, el, mf0, effl0));
                }
            }

            var okTrials = trials.Where(t => t.Ok).OrderBy(t => t.Delta).ToList();
            Say("");
            Say("=== LOO RESULTS ===");
            Say(F("{0,-6} {1,-12} {2,14} {3,14} {4}", "Elem", "Surfaces", "MF_after", "Delta", "Note"));
            foreach (var t in trials.OrderBy(t => t.Element.Index))
            {
                string surf = t.Element.Front + "-" + t.Element.Rear;
                if (!t.Ok)
                    Say(F("E{0,-5} {1,-12} {2,14} {3,14} {4}", t.Element.Index, surf, "FAIL", "", t.Error));
                else
                    Say(F("E{0,-5} {1,-12} {2,14:G8} {3,14:G8} {4}",
                        t.Element.Index, surf, t.MfAfter, t.Delta,
                        t.Delta <= 0 ? "improved/equal" : ""));
            }

            // Always write the per-trial CSV (even when we fail-closed).
            string csv = Path.Combine(workDir, "ElementLeaveOneOut_trials.csv");
            WriteTrialsCsv(csv, trials, mf0);
            Say("CSV: " + csv);

            if (okTrials.Count == 0)
            {
                // Fail closed: do NOT write *_minus1.zmx when every trial was bad.
                Say("");
                Say("FAIL-CLOSED: no valid LOO trials remain; not saving *_minus1.zmx.");
                if (Opts.ReportPath != null)
                {
                    string reportPath = Opts.ReportPath.Length == 0
                        ? Path.Combine(workDir, "ElementLeaveOneOut_report.txt")
                        : Opts.ReportPath;
                    File.WriteAllLines(reportPath, Report, Encoding.UTF8);
                    Say("Report: " + reportPath);
                }
                throw new ToolExitException(2, "no valid LOO trials remain (fail-closed)");
            }

            var best = okTrials[0];
            Say("");
            Say(F("Winner: E{0} surfaces {1}-{2}  MF {3:G8} -> {4:G8}  (delta {5:G8})",
                best.Element.Index, best.Element.Front, best.Element.Rear,
                mf0, best.MfAfter, best.Delta));

            Reload(sys, baseCopy);
            ApplyDeletionAndMf(sys, best.Element, effl0);
            EnsureOptimizationVariables(sys);
            double mfFinal = LocalReopt(sys, app);
            Say(F("Final MF after winner reopt: {0:G8}", mfFinal));

            string savePath = Opts.SavePath;
            if (string.IsNullOrEmpty(savePath))
            {
                string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                string stem = string.IsNullOrEmpty(src) ? "system" : Path.GetFileNameWithoutExtension(src);
                savePath = Path.Combine(workDir, stem + "_minus1.zmx");
            }
            sys.SaveAs(savePath);
            Say("Saved reduced system: " + savePath);

            if (Opts.ReportPath != null)
            {
                string reportPath = Opts.ReportPath.Length == 0
                    ? Path.Combine(workDir, "ElementLeaveOneOut_report.txt")
                    : Opts.ReportPath;
                File.WriteAllLines(reportPath, Report, Encoding.UTF8);
                Say("Report: " + reportPath);
            }

            Say(F("RETURN mf0={0:G8} mf_after={1:G8} delta={2:G8} save={3}",
                mf0, mfFinal, mfFinal - mf0, savePath));
        }

        // Write the spreadsheet of every trial (good and bad).
        static void WriteTrialsCsv(string csv, List<TrialResult> trials, double mf0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("element,front,rear,label,abs_power,mf0,mf_after,delta,ok,error");
            foreach (var t in trials)
            {
                sb.AppendLine(string.Join(",",
                    t.Element.Index, t.Element.Front, t.Element.Rear, Csv(t.Element.Label),
                    F("{0:G8}", t.Element.AbsPower), F("{0:G8}", mf0),
                    t.Ok ? F("{0:G8}", t.MfAfter) : "",
                    t.Ok ? F("{0:G8}", t.Delta) : "",
                    t.Ok ? "1" : "0", Csv(t.Error ?? "")));
            }
            File.WriteAllText(csv, sb.ToString(), Encoding.UTF8);
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // One try: put the original system back, pull out ONE element,
        // fix the report-card surface numbers, add thickness rules if needed,
        // tweak with DLS, and write down the new score.
        static TrialResult RunTrial(
            ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys,
            string baseCopy, ElementInfo el, double mf0, double effl0)
        {
            var tr = new TrialResult { Element = el };
            try
            {
                Reload(sys, baseCopy);
                ApplyDeletionAndMf(sys, el, effl0);
                EnsureOptimizationVariables(sys);
                double mf = LocalReopt(sys, app);
                tr.MfAfter = mf;
                tr.Delta = mf - mf0;
                // Reject broken / absurd scores so they cannot win.
                string why;
                if (TrialMfIsBroken(mf, mf0, out why))
                {
                    tr.Ok = false;
                    tr.Error = why;
                    Say(F("  REJECT: {0}  (MF={1:G8}, delta={2:G8})", why, mf, tr.Delta));
                }
                else
                {
                    tr.Ok = true;
                    Say(F("  MF after reopt: {0:G8}  (delta {1:G8})", mf, tr.Delta));
                }
            }
            catch (Exception ex)
            {
                tr.Ok = false;
                tr.Error = ex.Message;
                Say("  FAIL: " + ex.Message);
            }
            return tr;
        }

        // True if x is a normal number (not NaN and not Inf).
        static bool IsFinite(double x) => !(double.IsNaN(x) || double.IsInfinity(x));

        // After reopt: is this trial's MF too broken to trust as a winner?
        // Rules: non-finite MF, non-finite delta, MF >= 1e8, or MF >= 1e6 * MF0
        // (when MF0 is a real positive baseline).
        static bool TrialMfIsBroken(double mfAfter, double mf0, out string why)
        {
            if (!IsFinite(mfAfter))
            {
                why = "MF after reopt is non-finite";
                return true;
            }
            if (mfAfter >= 1e8)
            {
                why = "MF after reopt absurd (>= 1e8)";
                return true;
            }
            if (IsFinite(mf0) && mf0 > 0 && mfAfter >= 1e6 * mf0)
            {
                why = "MF after reopt >= 1e6 * MF0 (broken trial)";
                return true;
            }
            double delta = mfAfter - mf0;
            if (!IsFinite(delta))
            {
                why = "delta is non-finite";
                return true;
            }
            why = null;
            return false;
        }

        // After seed + baseline MF0: stop if the starting score is nonsense,
        // unless the user passed -allowbadmf (then warn hard and continue).
        static void GateBaselineMf(double mf0)
        {
            bool bad = !IsFinite(mf0) || mf0 <= 0.0 || mf0 >= 1e8;
            if (!bad) return;
            string detail = !IsFinite(mf0) ? "non-finite"
                : (mf0 <= 0.0 ? "<= 0" : ">= 1e8 (absurd)");
            if (!Opts.AllowBadMf)
            {
                Say(F("REFUSED: baseline MF is unhealthy ({0}, MF0={1:G8}). Pass -allowbadmf to override.",
                    detail, mf0));
                throw new ToolExitException(2,
                    "unhealthy baseline MF (" + detail + ")");
            }
            Say(F("WARNING: -allowbadmf: proceeding with unhealthy baseline MF ({0}, MF0={1:G8}).",
                detail, mf0));
        }

        // Load the saved "before we touched anything" copy so each try starts clean.
        static void Reload(ZOSAPI.IOpticalSystem sys, string path)
        {
            if (!sys.LoadFile(path, false) || sys.LDE.NumberOfSurfaces < 3)
                throw new Exception("reload failed: " + path);
        }

        // Ask OpticStudio for the effective focal length. If it cannot answer, say "I don't know" (NaN).
        static double SafeEffl(ZOSAPI.IOpticalSystem sys)
        {
            try
            {
                return sys.MFE.GetOperandValue(
                    ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 1, 0, 0, 0, 0, 0, 0);
            }
            catch { return double.NaN; }
        }

        // Walk the surface list and group glass (or mirror) into "elements."
        // An element is a clump of surfaces that stick together as one lens or mirror.
        // We skip empty air gaps; those are just spaces between parts.
        static List<ElementInfo> EnumerateElements(ZOSAPI.IOpticalSystem sys)
        {
            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            var list = new List<ElementInfo>();
            int s = 1;
            int idx = 0;
            while (s < n - 1)
            {
                string mat = (lde.GetSurfaceAt(s).Material ?? "").Trim();
                if (mat.Length == 0) { s++; continue; }
                int front = s;
                bool mirror = mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                var mats = new List<string> { mat };
                s++;
                while (s < n - 1)
                {
                    string m2 = (lde.GetSurfaceAt(s).Material ?? "").Trim();
                    if (m2.Length == 0) break;
                    mats.Add(m2);
                    if (m2.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) mirror = true;
                    s++;
                }
                int rear = s;
                if (rear >= n - 1) break;
                double power = 0;
                for (int i = front; i < rear; i++)
                {
                    double R = lde.GetSurfaceAt(i).Radius;
                    if (Math.Abs(R) < 1e-12 || double.IsInfinity(R)) continue;
                    string mi = (lde.GetSurfaceAt(i).Material ?? "").Trim();
                    double k = mi.Equals("MIRROR", StringComparison.OrdinalIgnoreCase) ? 2.0 : 0.5;
                    power += k / R;
                }
                idx++;
                list.Add(new ElementInfo
                {
                    Index = idx, Front = front, Rear = rear,
                    AbsPower = Math.Abs(power), IsMirror = mirror,
                    Label = string.Join("+", mats)
                });
                s = rear + 1;
            }
            return list;
        }

        // Pull the element out of the lens list, fix the report card so it still
        // talks about the surfaces that remain, keep focal length on target,
        // and make sure thickness rules still exist after the surgery.
        static void ApplyDeletionAndMf(ZOSAPI.IOpticalSystem sys, ElementInfo el, double effl0)
        {
            RemapAndCleanMf(sys, el.Front, el.Rear);
            DeleteElement(sys, el.Front, el.Rear);
            EnsureEfflAnchor(sys, effl0);
            EnsureThicknessConstraints(sys);
        }

        // Remove the surfaces for this lens. The thickness that lens used to have
        // gets poured into the surface in front of it so the total length stays honest.
        static void DeleteElement(ZOSAPI.IOpticalSystem sys, int front, int rear)
        {
            var lde = sys.LDE;
            double absorb = 0;
            for (int i = front; i <= rear; i++)
                absorb += lde.GetSurfaceAt(i).Thickness;
            if (front > 0)
                lde.GetSurfaceAt(front - 1).Thickness =
                    lde.GetSurfaceAt(front - 1).Thickness + absorb;
            for (int i = rear; i >= front; i--)
                lde.RemoveSurfaceAt(i);
        }

        // The report card names surfaces by number. After we delete some surfaces,
        // those numbers would point at the wrong places — like a house number after
        // you tear a house out of the street. So we:
        //   - throw away rules that talked about deleted surfaces
        //   - subtract how many we removed from bigger surface numbers
        static void RemapAndCleanMf(ZOSAPI.IOpticalSystem sys, int front, int rear)
        {
            var mfe = sys.MFE;
            int removed = rear - front + 1;
            for (int row = mfe.NumberOfOperands; row >= 1; row--)
            {
                ZOSAPI.Editors.MFE.IMFERow op;
                try { op = mfe.GetOperandAt(row); }
                catch { continue; }

                int p1 = ReadSurfParam(op, ZOSAPI.Editors.MFE.MeritColumn.Param1);
                int p2 = ReadSurfParam(op, ZOSAPI.Editors.MFE.MeritColumn.Param2);

                bool hitDeleted =
                    (p1 >= front && p1 <= rear) ||
                    (p2 >= front && p2 <= rear);
                if (hitDeleted)
                {
                    try { mfe.RemoveOperandAt(row); }
                    catch { try { op.Weight = 0; } catch { } }
                    continue;
                }
                if (p1 > rear)
                    WriteSurfParam(op, ZOSAPI.Editors.MFE.MeritColumn.Param1, p1 - removed);
                if (p2 > rear)
                    WriteSurfParam(op, ZOSAPI.Editors.MFE.MeritColumn.Param2, p2 - removed);
            }
        }

        // Peek at one surface-number slot on a report-card row. -1 means "could not read."
        static int ReadSurfParam(ZOSAPI.Editors.MFE.IMFERow op, ZOSAPI.Editors.MFE.MeritColumn col)
        {
            try
            {
                return (int)Math.Round(op.GetOperandCell(col).DoubleValue);
            }
            catch { return -1; }
        }

        // Write a new surface number into one report-card slot.
        static void WriteSurfParam(ZOSAPI.Editors.MFE.IMFERow op, ZOSAPI.Editors.MFE.MeritColumn col, int value)
        {
            try { op.GetOperandCell(col).DoubleValue = value; } catch { }
        }

        // These numbers are the fences: glass must stay at least 1 mm thick,
        // air gaps at least 0.5 mm. Max 1000 mm is "don't grow forever."
        // Weight 1 means "care about this rule about as much as a normal rule."
        const double GlassMinCt = 1.0;
        const double GlassMaxCt = 1000.0;
        const double AirMinCt = 0.5;
        const double AirMaxCt = 1000.0;
        const double ThicknessBoundWeight = 100.0; // heavy so DLS does not ignore the fence

        // Look through the report card for thickness rules (min/max glass or air).
        // If we already have them, we do not need to add more.
        static bool HasThicknessBoundaryOperands(ZOSAPI.Editors.MFE.IMeritFunctionEditor mfe)
        {
            if (mfe == null) return false;
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                try
                {
                    var op = mfe.GetOperandAt(i);
                    if (op.Weight <= 0) continue;
                    string t = op.Type.ToString();
                    if (t.IndexOf("MNCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MXCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNCA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MXCA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNEG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNEA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("CTGT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("CTLT", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        // If the report card has no "don't go too thin / too thick" rules,
        // add them for every in-between surface:
        //   glass thickness: at least 1 mm, not more than 1000 mm
        //   air gap:         at least 0.5 mm, not more than 1000 mm
        // That stops the optimizer from making negative glass or smashed-together parts.
        static void EnsureThicknessConstraints(ZOSAPI.IOpticalSystem sys)
        {
            var mfe = sys.MFE;
            if (mfe == null) return;
            if (HasThicknessBoundaryOperands(mfe))
            {
                // Rules exist, but make sure OpticStudio actually listens (spot terms can shout louder).
                StrengthenThicknessBoundWeights(mfe);
                Say("  Thickness constraints already present in MF (weights reinforced).");
                return;
            }

            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            int added = 0;
            // Surface i thickness is glass if Material[i] is set, else air (skip object/image).
            for (int i = 1; i < n - 1; i++)
            {
                string mat = "";
                try { mat = (lde.GetSurfaceAt(i).Material ?? "").Trim(); } catch { }
                bool isGlass = mat.Length > 0 &&
                    !mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                double minT = isGlass ? GlassMinCt : AirMinCt;
                double maxT = isGlass ? GlassMaxCt : AirMaxCt;
                if (AddThicknessBound(mfe, ZOSAPI.Editors.MFE.MeritOperandType.MNCT, i, minT))
                    added++;
                if (AddThicknessBound(mfe, ZOSAPI.Editors.MFE.MeritOperandType.MXCT, i, maxT))
                    added++;
            }
            Say(F("  Added {0} glass/air thickness bounds (MNCT/MXCT; glass>={1:G4} air>={2:G4}).",
                added, GlassMinCt, AirMinCt));
        }

        // Add one thickness rule (minimum or maximum) for one surface's center thickness.
        static bool AddThicknessBound(
            ZOSAPI.Editors.MFE.IMeritFunctionEditor mfe,
            ZOSAPI.Editors.MFE.MeritOperandType type,
            int surf,
            double target)
        {
            try
            {
                mfe.AddOperand();
                var op = mfe.GetOperandAt(mfe.NumberOfOperands);
                op.ChangeType(type);
                try
                {
                    op.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param1).DoubleValue = surf;
                }
                catch { }
                try
                {
                    // Same-surface span: constrain that surface's center thickness.
                    op.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param2).DoubleValue = surf;
                }
                catch { }
                op.Target = target;
                op.Weight = ThicknessBoundWeight;
                return true;
            }
            catch (Exception ex)
            {
                Say(F("  WARNING: could not add {0} on S{1}: {2}", type, surf, ex.Message));
                return false;
            }
        }


        // If thickness rules are already there, turn their "how much we care" knob up.
        // Otherwise a loud spot-size rule can talk over a quiet thickness rule.
        static void StrengthenThicknessBoundWeights(ZOSAPI.Editors.MFE.IMeritFunctionEditor mfe)
        {
            if (mfe == null) return;
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                try
                {
                    var op = mfe.GetOperandAt(i);
                    string t = op.Type.ToString();
                    bool isBound =
                        t.IndexOf("MNCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MXCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNCA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MXCA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNEG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("MNEA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("CTGT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.IndexOf("CTLT", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isBound) continue;
                    if (op.Weight < ThicknessBoundWeight)
                        op.Weight = ThicknessBoundWeight;
                }
                catch { }
            }
        }

        // Last seatbelt for NEGATIVE gaps only.
        // Tiny positive air/glass can be real design (a hairline air space). Forcing those up
        // to 0.5/1 mm wrecks the lens and can knock it out of focus. Soft MNCT/MXCT rules
        // already nudge the optimizer during DLS; here we only un-do smash-through (th < 0).
        static void ClampNegativeThicknesses(ZOSAPI.IOpticalSystem sys)
        {
            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            int fixedCount = 0;
            for (int i = 1; i < n - 1; i++)
            {
                try
                {
                    var s = lde.GetSurfaceAt(i);
                    double th = s.Thickness;
                    if (!(th < 0)) continue; // keep designed thin-but-positive gaps
                    string mat = (s.Material ?? "").Trim();
                    bool isGlass = mat.Length > 0 &&
                        !mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                    // Refocus thickness (last gap before image) gets a tiny positive air floor
                    // so Quick Focus still has a legal starting point.
                    bool isRefocusGap = (i == n - 2);
                    double floor = isRefocusGap ? 1e-3
                        : (isGlass ? GlassMinCt : AirMinCt);
                    s.Thickness = floor;
                    fixedCount++;
                }
                catch { }
            }
            if (fixedCount > 0)
                Say(F("  Clamped {0} negative thickness(es) back to a legal floor.", fixedCount));
        }


        // Keep the focal length near what it was before we removed a lens.
        // If that rule is missing, add it. Otherwise just set the target.
        static void EnsureEfflAnchor(ZOSAPI.IOpticalSystem sys, double effl0)
        {
            if (double.IsNaN(effl0) || Math.Abs(effl0) < 1e-12) return;
            var mfe = sys.MFE;
            bool hasEffl = false;
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                try
                {
                    var op = mfe.GetOperandAt(i);
                    if (op.Type.ToString().IndexOf("EFFL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasEffl = true;
                        op.Target = effl0;
                        if (op.Weight <= 0) op.Weight = 1.0;
                    }
                }
                catch { }
            }
            if (!hasEffl)
            {
                try
                {
                    mfe.AddOperand();
                    var op = mfe.GetOperandAt(mfe.NumberOfOperands);
                    op.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.EFFL);
                    op.Target = effl0;
                    op.Weight = 1.0;
                    Say(F("  Inserted EFFL anchor target={0:G8}", effl0));
                }
                catch (Exception ex)
                {
                    Say("  WARNING: could not insert EFFL anchor: " + ex.Message);
                }
            }
        }

        // Let OpticStudio gently tweak the variable radii and thicknesses (DLS)
        // so the report card gets as happy as it can with the lens we just removed.
        static double LocalReopt(ZOSAPI.IOpticalSystem sys, ZOSAPI.IZOSAPI_Application app)
        {
            var opt = sys.Tools.OpenLocalOptimization();
            try
            {
                if (opt.Variables < 1)
                {
                    Say("  Reopt skipped: no variables");
                    ClampNegativeThicknesses(sys);
                    RunQuickFocus(sys);
                    return sys.MFE.CalculateMeritFunction();
                }
                opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares;
                opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic;
                double before = opt.InitialMeritFunction;
                opt.RunAndWaitForCompletion();
                double after = opt.CurrentMeritFunction;
                Say(F("  DLS vars={0}: {1:G8} -> {2:G8}", opt.Variables, before, after));
                int extra = Math.Max(0, (Opts.Cycles / 10) - 1);
                for (int k = 0; k < extra && !app.TerminateRequested; k++)
                {
                    opt.RunAndWaitForCompletion();
                    after = opt.CurrentMeritFunction;
                }
                ClampNegativeThicknesses(sys);
                // After DLS (and any negative-gap fix), explicitly refocus.
                RunQuickFocus(sys);
                after = sys.MFE.CalculateMeritFunction();
                return after;
            }
            finally { opt.Close(); }
        }

        // True if at least one report-card row has a weight bigger than zero
        // (a row with weight 0 is like a note nobody listens to).
        static bool HasPositiveWeight(ZOSAPI.Editors.MFE.IMeritFunctionEditor mfe)
        {
            if (mfe == null) return false;
            for (int i = 1; i <= mfe.NumberOfOperands; i++)
            {
                try { if (mfe.GetOperandAt(i).Weight > 0) return true; } catch { }
            }
            return false;
        }

        // Build a normal OpticStudio "default" report card: RMS spot size,
        // aimed at the centroid, with glass/air thickness limits turned on.
        // We only do this when the file had no useful report card yet.
        static void SeedBaselineMf(ZOSAPI.IOpticalSystem sys)
        {
            // OpticStudio Optimization Wizard defaults: RMS Spot Radius, centroid,
            // Gaussian quadrature - same as UI "Default Merit Function" for sequential.
            var mfe = sys.MFE;
            try
            {
                var wiz = mfe.SEQOptimizationWizard2;
                if (wiz == null)
                    throw new Exception("SEQOptimizationWizard2 unavailable");

                wiz.ResetSettings();
                wiz.Criterion = ZOSAPI.Wizards.CriterionTypes.Spot;
                wiz.Type = ZOSAPI.Wizards.OptimizationTypes.RMS;
                wiz.Reference = ZOSAPI.Wizards.ReferenceTypes.Centroid;
                wiz.UseGaussianQuadrature = true;
                wiz.UseRectangularArray = false;
                // Leave Rings/Arms/Obscuration/fields at wizard defaults after ResetSettings.
                wiz.UseAllFields = true;
                wiz.AssumeAxialSymmetry = true;
                wiz.IgnoreLateralColor = false;
                wiz.AddFavoriteOperands = false;
                wiz.UseGlassBoundaryValues = true;
                wiz.UseAirBoundaryValues = true;
                wiz.OptimizeForBestNominalPerformance = true;
                wiz.OptimizeForManufacturingYield = false;
                wiz.UseMaximumDistortion = false;

                Say("  Applying SEQOptimizationWizard2 (RMS Spot / Centroid / GQ)...");
                wiz.Apply();
                Say(F("  Default MF operands: {0}", mfe.NumberOfOperands));
                if (mfe.NumberOfOperands < 1 || !HasPositiveWeight(mfe))
                    throw new Exception("wizard Apply left MFE empty");
            }
            catch (Exception ex)
            {
                Say("  Wizard2 failed (" + ex.Message + ") - trying SEQOptimizationWizard...");
                var wiz1 = mfe.SEQOptimizationWizard;
                if (wiz1 == null)
                    throw new Exception("no sequential optimization wizard available");
                wiz1.ResetSettings();
                // v1: Type/Data/Reference are integer indices into Get*At tables;
                // RMS Spot + Centroid are the usual defaults after ResetSettings.
                try { wiz1.IsAssumeAxialSymmetryUsed = true; } catch { }
                try { wiz1.IsGlassUsed = true; } catch { }
                try { wiz1.IsAirUsed = true; } catch { }
                wiz1.Apply();
                Say(F("  Default MF operands (v1 wizard): {0}", mfe.NumberOfOperands));
                if (mfe.NumberOfOperands < 1 || !HasPositiveWeight(mfe))
                    throw new Exception("v1 wizard Apply left MFE empty: " + ex.Message);
            }
        }


        // Mark radii and thicknesses as things the optimizer is allowed to move
        // (except we leave the very ends alone). No variables = nothing to tweak.
        // Then make sure the "refocus" knob is free: the air thickness just before the image.
        static void EnsureOptimizationVariables(ZOSAPI.IOpticalSystem sys)
        {
            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            for (int i = 1; i < n - 1; i++)
            {
                var s = lde.GetSurfaceAt(i);
                try
                {
                    try { s.RadiusCell.MakeSolveVariable(); }
                    catch
                    {
                        try { s.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.Radius).MakeSolveVariable(); }
                        catch { }
                    }
                    try { s.ThicknessCell.MakeSolveVariable(); }
                    catch
                    {
                        try { s.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.Thickness).MakeSolveVariable(); }
                        catch { }
                    }
                }
                catch { }
            }
            EnsureRefocusVariable(sys);
        }

        // The thickness on the surface right before the image is the focus knob
        // (how far the film / sensor sits). After we yank a lens out, that distance
        // usually needs to move. Force it to be a variable even if something else
        // had locked it.
        static void EnsureRefocusVariable(ZOSAPI.IOpticalSystem sys)
        {
            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            if (n < 3) return;
            int focusSurf = n - 2; // thickness here = distance to image surface
            try
            {
                var s = lde.GetSurfaceAt(focusSurf);
                try { s.ThicknessCell.MakeSolveVariable(); }
                catch
                {
                    try { s.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.Thickness).MakeSolveVariable(); }
                    catch (Exception ex)
                    {
                        Say("  WARNING: could not free refocus thickness on S" + focusSurf + ": " + ex.Message);
                        return;
                    }
                }
                Say(F("  Refocus variable: thickness of S{0} (gap to image).", focusSurf));
            }
            catch (Exception ex)
            {
                Say("  WARNING: EnsureRefocusVariable failed: " + ex.Message);
            }
        }

        // Slide the image distance until the spot is sharp again.
        // Prefer OpticStudio Quick Focus; if that tool is missing (common in standalone),
        // fall back to a tiny DLS that only moves the focus-gap thickness.
        // If neither can open, log ONE clear line and skip further retries —
        // main DLS already frees the image-gap via EnsureRefocusVariable.
        static void RunQuickFocus(ZOSAPI.IOpticalSystem sys)
        {
            if (SkipStandaloneRefocus)
                return;

            EnsureRefocusVariable(sys);
            if (TryNativeQuickFocus(sys))
                return;
            if (TryRefocusByFocusGapOnly(sys))
                return;

            // Neither tool opened — say so once, then stay quiet for later trials.
            if (!LoggedStandaloneRefocusSkip)
            {
                Say("  Refocus tools unavailable in this session (Quick Focus / focus-gap DLS); "
                    + "skipping further refocus retries. Main DLS already frees the image-gap variable.");
                LoggedStandaloneRefocusSkip = true;
            }
            SkipStandaloneRefocus = true;
        }

        static bool TryNativeQuickFocus(ZOSAPI.IOpticalSystem sys)
        {
            ZOSAPI.Tools.General.IQuickFocus qf = null;
            try
            {
                qf = sys.Tools.OpenQuickFocus();
                if (qf == null)
                    return false;
                try { qf.UseCentroid = true; } catch { }
                try { qf.Criterion = ZOSAPI.Tools.General.QuickFocusCriterion.SpotSizeRadial; } catch { }
                qf.RunAndWaitForCompletion();
                Say("  Quick Focus done (native tool).");
                return true;
            }
            catch
            {
                // Stay quiet on failure — RunQuickFocus may fall back or log once.
                return false;
            }
            finally
            {
                try { if (qf != null) qf.Close(); } catch { }
            }
        }

        // Lock every other knob, free only the gap-to-image thickness, then DLS.
        // Returns true if the focus-gap DLS actually ran; false if it could not open.
        static bool TryRefocusByFocusGapOnly(ZOSAPI.IOpticalSystem sys)
        {
            var lde = sys.LDE;
            int n = lde.NumberOfSurfaces;
            if (n < 3) return false;
            int focusSurf = n - 2;

            // Freeze radii/thicknesses on all in-between surfaces...
            for (int i = 1; i < n - 1; i++)
            {
                try
                {
                    var s = lde.GetSurfaceAt(i);
                    try { s.RadiusCell.MakeSolveFixed(); } catch { }
                    try { s.ThicknessCell.MakeSolveFixed(); } catch { }
                }
                catch { }
            }
            // ...then free only the focus gap.
            try
            {
                var fs = lde.GetSurfaceAt(focusSurf);
                if (!fs.ThicknessCell.MakeSolveVariable())
                    try { fs.GetSurfaceCell(ZOSAPI.Editors.LDE.SurfaceColumn.Thickness).MakeSolveVariable(); } catch { }
            }
            catch
            {
                EnsureOptimizationVariables(sys);
                return false;
            }

            var opt = sys.Tools.OpenLocalOptimization();
            if (opt == null)
            {
                EnsureOptimizationVariables(sys);
                return false;
            }
            try
            {
                if (opt.Variables < 1)
                    return false;
                opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares;
                opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic;
                double before = opt.InitialMeritFunction;
                opt.RunAndWaitForCompletion();
                double after = opt.CurrentMeritFunction;
                Say(F("  Refocus DLS (focus gap S{0} only): {1:G8} -> {2:G8}", focusSurf, before, after));
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { opt.Close(); } catch { }
                // Put the usual variables back for any later steps / save clarity.
                EnsureOptimizationVariables(sys);
            }
        }

    }
}



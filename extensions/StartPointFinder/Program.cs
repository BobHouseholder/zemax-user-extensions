// ============================================================
// StartPointFinder - what this program does (plain words)
// ============================================================
// You give it a folder full of lens files that are "cousins":
// same family, same number of lenses in the same order (for
// example ten Double Gauss camera lenses). Each one was made for
// a slightly different job: a different focal length, f-number,
// or field of view.
//
// The program:
//   1) Reads every .zmx in the folder and writes down its basic
//      "first-order" numbers: focal length (EFL), f-number,
//      half field of view, and length (track). A file whose glass
//      does not resolve (catalog not installed) or whose numbers
//      are absurd (EFL 1e10, F/0.001) is skipped with the reason.
//      Byte-identical files (and identical prescriptions) count once.
//   2) Keeps only the files that share one lens layout AND one
//      object distance (infinite, or the same finite distance) and
//      skips the odd ones out, with a count per reason. -layout
//      picks another family; the runner-up groups are listed.
//   3) Shrinks or grows every kept lens to focal length 1 so the
//      shapes can be compared fairly.
//   4) Takes the job YOU ask for: F/# and half field of view (and
//      optionally focal length). Default = the middle value of the
//      inputs. Values outside the inputs' range are refused (or
//      clamped with -clamp). Distance between jobs is measured in
//      the folder's own spread (log2 F/# range, field range);
//      on-axis-only designs get a penalty for field requests.
//   5) Builds every start it can make at that job (every input
//      design, and a blend of the Standard-surface inputs), sizes
//      each to the job and scores it with the same report card
//      WITHOUT optimizing ("the screen"). Default -start best then
//      optimizes only the 3 best-screened starts (-top N, or -top all
//      to optimize every start; if none of them passes the checks it
//      goes on down the list until one does) and keeps the winner that
//      passes the checks; -start nearest / blend force one. Optional -hammer SEC
//      gives the 3 best optimized lenses that pass the checks a longer
//      Hammer polish each, then keeps the best again.
//      Designs with Even Asphere / Zernike / Paraxial surfaces are
//      rebuilt from their own file (scaled; asphere terms fixed)
//      and are never averaged into the blend.
//   6) Saves a NEW .zmx plus CSV/JSON/report and a side-view PNG,
//      with all candidates ranked and the runtime.
// Input files are only read. They are never saved over.
//
// Flags you can pass in:
//   -dir <folder>  -out <dir>  -fno N  -hfov DEG  -efl F  -clamp
//   -start best|nearest|blend  -layout <signature>
//   -weight near|soft|equal  -glass nearest|majority  -passes K
//   -hammer SEC  -top N|all  -compare reopt|refocus|none  -min N
//   -force  -ask  -nopng  -nodialog  -quiet
// Every lens that is checked must also trace its pupil-edge rays at
// every field (7 real rays): a lens whose edge rays miss a surface
// fails the checks like any other failure.
// No -dir: a folder picker opens (ribbon use), then a small window
// asks for F/# and field; at the end it offers to open the result
// in the main window. Exit codes: 0 ok,
// 1 error, 2 refused, 3 result written but failed envelope check.
// Python twin: python/StartPointFinder/start_point_finder.py
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WF = System.Windows.Forms;

namespace StartPointFinder
{
    // Little box of switches from the command line.
    class Options
    {
        public string Dir;
        public string OutDir;
        public double? Efl, Fno, Fov;      // user targets (Fov = half field, degrees)
        public bool Clamp = false;
        public string Start = "best";       // best = lowest MF among candidates that pass the checks (default); nearest; blend
        public string Layout = "";          // -layout: pick this layout signature instead of the biggest group
        public string Weight = "near";      // near = closest inputs dominate; soft = old gentle weights
        public bool Ask = false;            // show the F/# + field window even with -dir
        public string Glass = "nearest";    // nearest = glass closest to the average nd/Vd
        public int Passes = 3;              // automatic DLS runs (stops early when stuck)
        public int HammerSec = 0;           // optional Hammer time per polished lens (0 = off)
        public int Top = 3;                 // -top: in -start best, optimize only this many best-screened starts (0 = all)
        public string Compare = "reopt";    // how to score inputs at the target
        public int MinGroup = 2;
        public bool Force = false;
        public bool NoPng = false;
        public bool NoDialog = false;
        public bool Quiet = false;
    }

    // Stop the tool with a known exit code (2 = refused, 3 = outside envelope).
    class ToolExitException : Exception
    {
        public int Code;
        public ToolExitException(int code, string message) : base(message) { Code = code; }
    }

    // One surface written down as plain numbers (curvature = 1 / radius).
    class SurfRx
    {
        public double Curv, Thick, Conic;
        public string Glass = "", Catalog = "";
        public string Type = "";            // "" = Standard; otherwise OpticStudio's type name
        public double[] Pars;               // Par1..Par8 of a non-Standard surface (for the duplicate check)
    }

    // Everything we learned from one input .zmx file.
    class Design
    {
        public string Name, Path;
        public bool Ok;
        public string Reason = "", Note = "";
        public string Cat = "";            // short skip category for the summary counts
        public string Hash = "";           // SHA-256 of the file bytes (duplicate check)
        public List<string> FileCatalogs = new List<string>();  // GCAT line of the file
        public double ObjDist = double.PositiveInfinity;        // object distance (infinity = far away)
        public double ObjRep = double.PositiveInfinity;         // object distance of its group (finite only)
        public bool IsSpecial;             // has Even Asphere / Zernike / Paraxial surfaces
        public string SpecialNote = "";    // e.g. "S4 Even Asphere, S5 Even Asphere"
        public bool BlendMember;           // averaged into the blend (Standard surfaces only)
        public bool OnAxis;                // on-axis-only design in a folder that has field designs
        public int NSurf, Stop;
        public string Signature = "";
        public List<SurfRx> Surfs = new List<SurfRx>();
        public double Efl = double.NaN, Fno = double.NaN, Hfov = double.NaN, Track = double.NaN;
        public double Weight;              // blend weight while building the blend
        public double BlendWeight;         // closeness weight a blend would use (shown in the CSV)
        public double Dist = double.NaN;   // distance to the request in (F/#, field) space
        public Dictionary<int, double[]> NdVd = new Dictionary<int, double[]>();
        public double MfRefocus = double.NaN, MfReopt = double.NaN, SpotReoptUm = double.NaN;
        public bool ReoptPass = true;      // did its optimized version pass the result checks?
        public double TrackRatio => Efl != 0 ? Track / Efl : double.NaN;
    }

    // Smallest / middle / largest of each first-order number.
    class Envelope
    {
        public double[] Efl, Fno, Hfov, TrackRatio;   // each is {min, median, max}
    }

    class Target { public double Efl, Fno, Hfov, Obj = double.PositiveInfinity; }  // Obj = held object distance

    // Where the requested (F/#, field) pair sits among the inputs' pairs.
    class PairInfo
    {
        public Design Nearest;
        public double Dist = double.PositiveInfinity;
        public bool InsideHull, Warn, Exact;   // Exact = request equals an input's (F/#, field)
        public string Text = "";
    }

    // Optimizer fences measured from the inputs at focal length 1.
    class Bounds { public double GlassMin, GlassMax, AirMin, AirMax; }

    // One start we tried at the request: an input design or the blend.
    class Cand
    {
        public string Kind = "input";      // "input" or "blend"
        public Design D;                   // the input (null for the blend)
        public string Label = "";
        public double MfStart = double.NaN, Mf = double.NaN, Spot = double.NaN;
        public bool Optimized;
        public bool Pass = true;           // optimized lens passed the same checks as the final result
        public string Fail = "";           // short reason when it did not
        public string Note = "";
        public int ScreenRank;             // 1 = lowest report-card score before optimizing ("the screen")
        public bool? EdgeOk;               // did its pupil-edge rays trace at every field? (null = not checked)
        // Hammer polish (only with -hammer): the DLS numbers are kept so the report can show both.
        public double MfDls = double.NaN, SpotDls = double.NaN;
        public bool Hammered;              // true = the Hammer result is the one this candidate now stands for
        public string HamNote = "";
        public string OptPath = "", StartPath = "";  // temp copies of its DLS lens and start (only for -hammer)
    }

    // Everything one candidate run needs (keeps the argument lists short).
    class RunCtx
    {
        public ZOSAPI.IZOSAPI_Application App;
        public ZOSAPI.IOpticalSystem S;
        public Target T; public Envelope Env; public Bounds B;
        public List<string> Catalogs; public int Stop; public int BlendCount;
        public Dictionary<string, string> Outs; public string TmpStart; public string OutDir;
        public string Mode; public PairInfo Pair;
        public Cand Chosen; public int NOpt;
    }

    class Program
    {
        const string Tool = "StartPointFinder";
        const int HammerKeep = 3;          // -hammer polishes this many of the best optimized lenses that pass the checks
        static Options Opts = new Options();
        static readonly List<string> Report = new List<string>();
        static readonly Dictionary<string, double[]> GlassCache = new Dictionary<string, double[]>();
        static bool FolderFromDialog = false;
        static bool Attached = false;      // true = launched from the ribbon and attached to OpticStudio
        static HashSet<string> AvailCats = new HashSet<string>();  // glass catalogs installed on this PC

        [STAThread]
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

            try { Environment.ExitCode = Run(); }
            catch (ToolExitException tex)
            {
                Say("FATAL: " + tex.Message);
                Environment.ExitCode = tex.Code;
            }
            catch (Exception ex)
            {
                Say("FATAL: " + ex.Message + Environment.NewLine + ex.StackTrace);
                Environment.ExitCode = 1;
            }
        }

        // Read the words you typed after the program name (-dir, -out, ...).
        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (raw.StartsWith("-") || raw.StartsWith("/"))
                {
                    string a = raw.TrimStart('-', '/').ToLowerInvariant();
                    // The ribbon launches us with -zpid={..} -zplt={Extension} -zsid={..};
                    // those are OpticStudio's own markers, not ours, so skip them.
                    if (a.StartsWith("zpid") || a.StartsWith("zplt") || a.StartsWith("zsid")) continue;
                    string next()
                    {
                        if (i + 1 >= args.Length) throw new Exception("flag " + raw + " needs a value");
                        return args[++i];
                    }
                    switch (a)
                    {
                        case "dir": Opts.Dir = next(); break;
                        case "out": Opts.OutDir = next(); break;
                        case "efl": Opts.Efl = Num(next(), raw); break;
                        case "fno": Opts.Fno = Num(next(), raw); break;
                        case "hfov":
                        case "fov": Opts.Fov = Num(next(), raw); break;  // -fov kept as an old name
                        case "clamp": Opts.Clamp = true; break;
                        case "start": Opts.Start = Pick(next(), raw, "best", "nearest", "blend"); break;
                        case "layout": Opts.Layout = next().Trim(); break;
                        case "weight": Opts.Weight = Pick(next(), raw, "near", "soft", "equal"); break;
                        case "ask": Opts.Ask = true; break;
                        case "glass": Opts.Glass = Pick(next(), raw, "nearest", "majority"); break;
                        case "passes": Opts.Passes = Math.Max(1, (int)Num(next(), raw)); break;
                        case "hammer": Opts.HammerSec = Math.Max(0, (int)Num(next(), raw)); break;
                        case "top":
                            {
                                // "-top all" = optimize every start (the v2 behavior); "-top N" = only the N best-screened.
                                string tok = next();
                                Opts.Top = tok.Trim().ToLowerInvariant() == "all" ? 0 : Math.Max(1, (int)Num(tok, raw));
                                break;
                            }
                        case "compare": Opts.Compare = Pick(next(), raw, "reopt", "refocus", "none"); break;
                        case "min": Opts.MinGroup = Math.Max(2, (int)Num(next(), raw)); break;
                        case "force": Opts.Force = true; break;
                        case "nopng": Opts.NoPng = true; break;
                        case "nodialog": Opts.NoDialog = true; break;
                        case "quiet": Opts.Quiet = true; break;
                        default: throw new Exception("unknown flag " + raw);
                    }
                }
                else if (string.IsNullOrEmpty(Opts.Dir))
                    Opts.Dir = raw;
            }
        }

        static double Num(string s, string flag)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            throw new Exception("flag " + flag + " needs a number, got '" + s + "'");
        }

        static string Pick(string v, string flag, params string[] allowed)
        {
            string low = (v ?? "").ToLowerInvariant();
            if (!allowed.Contains(low)) throw new Exception("flag " + flag + " must be one of " + string.Join("|", allowed));
            return low;
        }

        static void Say(string line)
        {
            Console.WriteLine(line);
            Report.Add(line);
        }

        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        // Short number text for tables ("nan" when we do not know).
        static string G(double x, int digits = 6) =>
            IsFinite(x) ? x.ToString("G" + digits, CultureInfo.InvariantCulture) : "nan";

        static bool IsFinite(double x) => !(double.IsNaN(x) || double.IsInfinity(x));

        // Round to a fixed grid (x * scale, round half to even, / scale). Distances and
        // derived field angles go through this so the C# and Python builds get the very
        // same numbers: log/atan can differ in the last bit between runtimes, and DLS
        // turns a last-bit difference in the blend into a different local minimum.
        static double Quant(double x, double scale) => IsFinite(x) ? Math.Round(x * scale) / scale : x;

        // Use -dir if given; otherwise pop up a folder picker (unless -nodialog).
        static string ChooseFolder()
        {
            if (!string.IsNullOrEmpty(Opts.Dir)) return Opts.Dir;
            if (Opts.NoDialog) throw new ToolExitException(2, "no -dir given and -nodialog is set (nothing to read)");
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "StartPointFinder: pick the folder that holds the starting designs";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath))
                    throw new ToolExitException(2, "no folder picked");
                FolderFromDialog = true;
                return dlg.SelectedPath;
            }
        }

        // Pick the folder, then get an OpticStudio to work in.
        // -dir given: start our own hidden OpticStudio (standalone).
        // From the ribbon: attach, and do all work in a NEW extra system so the
        // lens you have open is never touched. If attach fails, go standalone.
        static int Run()
        {
            string folder = ChooseFolder();
            ZOSAPI.IZOSAPI_Application app = null;
            ZOSAPI.IOpticalSystem work = null;
            bool standalone = !string.IsNullOrEmpty(Opts.Dir);
            if (!standalone)
            {
                string err;
                if (ZemaxLocator.TryConnect(out app, out err, false) && app != null)
                {
                    work = app.CreateNewSystem(ZOSAPI.SystemType.Sequential);
                    Attached = true;
                    Say("Connected to OpticStudio (mode: " + app.Mode + "); working in a separate system.");
                }
                else
                {
                    standalone = true;
                    app = null;
                }
            }
            if (standalone)
            {
                var connection = new ZOSAPI.ZOSAPI_Connection();
                app = connection.CreateNewApplication();
                if (app == null || app.PrimarySystem == null || !app.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                work = app.PrimarySystem;
            }
            if (work == null) throw new Exception("no optical system to work in");
            try
            {
                return RunOnFolder(app, work, folder);
            }
            finally
            {
                if (standalone) { try { app.CloseApplication(); } catch { } }
                else
                {
                    // Close only the extra system we made (the last one).
                    try { app.CloseSystemAt(app.NumberOfOpticalSystems - 1, false); } catch { }
                }
            }
        }

        // Big picture:
        // 1) Read every design (skip duplicates).  2) Keep the biggest same-layout,
        //    same-object-distance group.  3) Envelope (min / median / max).
        // 4) Pick and check the target.  5) Closeness + blend recipe.
        // 6) Try every candidate start, keep the best (or the forced one), check, write files.
        static int RunOnFolder(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem S, string folder)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder)) throw new ToolExitException(2, "folder not found: " + folder);
            var files = Directory.GetFiles(folder, "*.zmx", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".zmx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
            Say("=== " + Tool + " ===");
            Say("Folder: " + folder);
            if (files.Count == 0) throw new ToolExitException(2, "no .zmx files in " + folder);
            Say(F("Found {0} .zmx file(s).", files.Count));

            string outDir = Path.GetFullPath(string.IsNullOrEmpty(Opts.OutDir) ? Path.Combine(folder, "_" + Tool) : Opts.OutDir);
            var outs = OutPaths(outDir);
            GuardOutputs(outs, files);

            // 1) read (the installed catalog list lets a skip reason name a missing catalog)
            AvailCats = InstalledCatalogs(S);
            var designs = new List<Design>();
            int k = 0;
            foreach (var f in files)
            {
                k++;
                Progress(app, 2 + 18 * k / files.Count, "Reading " + Path.GetFileName(f));
                if (Terminated(app)) throw new ToolExitException(2, "terminated by user");
                designs.Add(ReadDesign(S, f));
            }
            // Identical files (or identical prescriptions) count once.
            MarkDuplicates(designs);

            // 2) group
            List<string> others;
            var group = PickGroup(designs, out others);
            foreach (var d in designs.Where(d => !d.Ok)) Say("  SKIP " + d.Name + ": " + d.Reason);
            string summary = SkipSummary(designs);
            Say(F("Usable: {0} of {1} file(s){2}", group.Count, designs.Count, summary.Length > 0 ? "; skipped: " + summary : ""));
            foreach (var o in others) Say("  " + o);
            if (group.Count < Opts.MinGroup)
            {
                WriteTables(outs, designs, null, null, double.NaN, double.NaN, null, null);
                Say("REFUSED: not enough usable designs share one layout.");
                WriteReport(outs);
                throw new ToolExitException(2, F("only {0} of {1} file(s) usable in one layout group (need {2}){3}",
                    group.Count, designs.Count, Opts.MinGroup, summary.Length > 0 ? "; skipped: " + summary : ""));
            }
            Say(F("Kept {0} design(s) with layout {1}", group.Count, GroupLabel(group[0])));

            // 3) envelope (and the distance scales that come from it)
            var env = MakeEnvelope(group);
            SetScales(env);
            foreach (var d in group) d.OnAxis = OnAxisOnly(d.Hfov);
            Say("First-order envelope (min / median / max):");
            SayEnv("EFL", env.Efl); SayEnv("F/#", env.Fno); SayEnv("half-FOV deg", env.Hfov); SayEnv("track/EFL", env.TrackRatio);
            var finiteObj = group.Where(d => IsFinite(d.ObjDist)).Select(d => d.ObjDist).ToList();
            double obj = finiteObj.Count > 0 ? Median(finiteObj) : double.PositiveInfinity;
            if (IsFinite(obj)) Say(F("Object distance: held at {0} for every candidate (all kept designs share it).", G(obj)));

            // 4) target: ask in a small window when interactive, then check it against the envelope
            if (!Opts.NoDialog && (FolderFromDialog || Opts.Ask)) AskTargets(group, env);
            var target = PickTarget(env);
            target.Obj = obj;
            Say(F("Target: EFL {0}  F/{1}  half-FOV {2} deg", G(target.Efl), G(target.Fno), G(target.Hfov)));
            var pair = PairCheck(group, target.Fno, target.Hfov);
            Say("  " + pair.Text);
            if (pair.Warn)
                Say("  WARNING: this F/# + field pair is extrapolation in 2D (each value is inside its own range, "
                    + "but no input design was made for this combination).");

            // Blend members: Standard surfaces only (aspheres are rebuilt from their own file),
            // and for a field request no on-axis-only design: its shape was never asked to
            // handle field, so it should not move the average. It stays a candidate.
            bool fieldAsk = target.Hfov > 1e-6;
            foreach (var d in group) d.BlendMember = !d.IsSpecial && !(d.OnAxis && fieldAsk);
            var members = group.Where(d => d.BlendMember).ToList();
            if (members.Count < group.Count)
            {
                var left = group.Where(d => !d.BlendMember).ToList();
                Say(F("Not averaged into the blend: {0}",
                    string.Join("; ", left.Select(d => d.IsSpecial ? d.Name + " [" + d.SpecialNote + "] (own file)" : d.Name + " (on-axis only)"))));
            }

            // 5) closeness weights; the blend recipe uses Standard-surface, field designs only
            bool blendOk = members.Count >= 2;
            string mode = Opts.Start;
            if (mode == "blend" && !blendOk)
            {
                Say(F("  -start blend needs 2+ blend members (Standard-surface; no on-axis-only design for a field request; have {0}); using nearest instead.", members.Count));
                mode = "nearest";
            }
            SetWeights(group, members, target);
            SayWeights(group, pair, mode);
            var bounds = ThicknessBounds(group);
            var catalogs = group.SelectMany(d => d.Surfs).Select(s => s.Catalog)
                .Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
            List<SurfRx> blendRx = null;
            if (blendOk)
            {
                List<string> glassNotes;
                blendRx = StartShape(members, out glassNotes);
                foreach (var n in glassNotes) Say("  blend " + n);
            }
            else Say(F("  blend n/a: only {0} blend member(s) in the group.", members.Count));

            // 6) candidates: each is sized to the target, refocused and scored ("the screen").
            // Then the chosen ones are optimized with the same report card. The keeper is saved as it goes.
            Directory.CreateDirectory(outDir);
            var x = new RunCtx
            {
                App = app, S = S, T = target, Env = env, B = bounds, Catalogs = catalogs,
                Stop = group[0].Stop, BlendCount = members.Count, Outs = outs, OutDir = outDir,
                TmpStart = Path.Combine(outDir, Tool + "_candidate.tmp.zmx"), Mode = mode, Pair = pair,
            };
            var cands = new List<Cand>();
            bool reopt = Opts.Compare == "reopt";
            // The top-N screen only applies to -start best with the full re-optimized comparison.
            bool screen = mode == "best" && reopt && Opts.Top > 0;
            if (screen)
            {
                // Step A: build and score every start WITHOUT optimizing (fast: no DLS at all).
                Say("Screening every start at the request (sized + refocused, no optimization):");
                int j = 0;
                foreach (var d in group)
                {
                    j++;
                    if (Terminated(app)) { Say("  Terminate requested; skipping the rest of the inputs."); break; }
                    Progress(app, 20 + 10 * j / (group.Count + 1), "Screening " + d.Name);
                    var c = Evaluate(x, "input", d, null, false);
                    d.MfRefocus = c.MfStart;
                    cands.Add(c);
                }
                if (blendOk && !Terminated(app)) cands.Add(Evaluate(x, "blend", null, blendRx, false));
                SetScreenRanks(cands);
                var picked = new HashSet<Cand>(cands.Where(c => c.ScreenRank > 0 && c.ScreenRank <= Opts.Top));
                foreach (var c in cands.OrderBy(c => c.ScreenRank))
                    Say(F("  #{0,-2} {1,-34} start MF {2,12}{3}{4}", c.ScreenRank, c.Label, G(c.MfStart),
                        picked.Contains(c) ? "   -> optimize" : "", c.Note.Length > 0 ? "   (" + c.Note + ")" : ""));
                Say(F("Optimizing the {0} best-screened start(s) of {1} (-top {2}; -top all optimizes every start):",
                    picked.Count, cands.Count, Opts.Top));
                // Step B: optimize only the picked starts, best-screened first. Each is rebuilt exactly
                // as in the screen (same numbers), so its result is the same as when every start was optimized.
                // Safety net: if none of the top N passes the checks, keep going down the screen
                // list one start at a time until one passes (or every start has been tried).
                int done = 0;
                bool fallbackSaid = false;
                foreach (int i in Enumerable.Range(0, cands.Count).OrderBy(i => cands[i].ScreenRank).ToList())
                {
                    var c = cands[i];
                    if (!picked.Contains(c))
                    {
                        if (cands.Any(o => o.Optimized && o.Pass))
                        {
                            if (c.D != null) c.D.Note = F("not optimized (screen rank {0} of {1}; -top {2})", c.ScreenRank, cands.Count, Opts.Top);
                            continue;
                        }
                        if (!fallbackSaid)
                        {
                            Say(F("  None of the top {0} passed the checks; optimizing the next screened start(s) until one passes:", Opts.Top));
                            fallbackSaid = true;
                        }
                    }
                    if (Terminated(app)) { Say("  Terminate requested; skipping the rest of the starts."); break; }
                    done++;
                    Progress(app, Math.Min(90, 30 + 60 * done / (picked.Count + 1)), "Optimizing " + c.Label);
                    int rank = c.ScreenRank;
                    c = Evaluate(x, c.Kind, c.D, c.Kind == "blend" ? blendRx : null, true);
                    c.ScreenRank = rank;
                    cands[i] = c;
                    if (c.D != null) { c.D.MfReopt = c.Mf; c.D.SpotReoptUm = c.Spot; c.D.ReoptPass = c.Pass; }
                    Say(F("  #{0,-2} {1,-34} start MF {2,12} -> MF {3,12}  spot {4,8} um{5}", rank, c.Label, G(c.MfStart), G(c.Mf),
                        G(c.Spot, 4), c.Optimized && !c.Pass ? "   FAILS CHECK: " + c.Fail : ""));
                }
            }
            else
            {
                if (Opts.Compare != "none")
                {
                    Say(F("Scoring every kept input at the request ({0}):", Opts.Compare));
                    int j = 0;
                    foreach (var d in group)
                    {
                        j++;
                        if (Terminated(app)) { Say("  Terminate requested; skipping the rest of the inputs."); break; }
                        Progress(app, 20 + 70 * j / (group.Count + 1), "Scoring " + d.Name);
                        var c = Evaluate(x, "input", d, null, reopt);
                        d.MfRefocus = c.MfStart;
                        if (reopt) { d.MfReopt = c.Mf; d.SpotReoptUm = c.Spot; d.ReoptPass = c.Pass; cands.Add(c); }
                        Say(F("  {0,-28} refocus MF {1,12}   reopt MF {2,12}   reopt spot {3,8} um{4}{5}",
                            d.Name, G(d.MfRefocus), G(d.MfReopt), G(d.SpotReoptUm, 4), d.IsSpecial ? "   (own file, scaled)" : "",
                            c.Optimized && !c.Pass ? "   FAILS CHECK: " + c.Fail : ""));
                    }
                }
                // Inputs not optimized above: the nearest input is still a candidate.
                if (!reopt && pair.Nearest != null && mode != "blend" && !Terminated(app))
                    cands.Add(Evaluate(x, "input", pair.Nearest, null, true));
                if (blendOk && (mode != "nearest" || Opts.Compare != "none") && !Terminated(app))
                {
                    Progress(app, 92, "Optimizing the blend");
                    cands.Add(Evaluate(x, "blend", null, blendRx, true));
                }
                SetScreenRanks(cands);
            }
            TryDelete(x.TmpStart);
            if (x.Chosen == null)
            {
                CleanupCandFiles(cands);
                throw new ToolExitException(1, "no candidate start could be built and optimized (see the lines above)");
            }

            // Optional Hammer polish: the best few lenses that pass the checks (or just the forced
            // keeper for -start nearest/blend), then keep the best again. It writes the keeper's files.
            if (Opts.HammerSec > 0 && !Terminated(app))
            {
                Progress(app, 93, "Hammer polish");
                HammerPolish(x, cands);
            }
            CleanupCandFiles(cands);
            S.LoadFile(outs["result"], false);
            double mfResult = x.Chosen.Mf;
            double mfStart = x.Chosen.MfStart;
            Say(F("Chosen start: {0}. MF {1} before optimization, {2} after ({3})", x.Chosen.Label, G(mfStart), G(mfResult), outs["start"]));
            var resultFo = Measure(S, target.Obj);
            bool pass;
            var checks = Validate(S, resultFo, target, env, bounds, mfResult, out pass);
            Say("Saved: " + outs["result"]);
            bool pngOk = false;
            if (!Opts.NoPng)
            {
                try { DrawLayoutPng(S, outs["png"], target, mfResult, x.Chosen.Label); pngOk = true; }
                catch (Exception ex) { Say("  PNG failed: " + ex.Message); }
            }

            // Ranked list of every optimized candidate: ones that pass the checks first, then by score.
            var ranked = Ranked(cands);
            Say("Candidates at the request (same report card, each optimized), best first:");
            for (int r = 0; r < ranked.Count; r++)
            {
                var c = ranked[r];
                var tags = new List<string>();
                if (c.ScreenRank > 0) tags.Add("screen #" + c.ScreenRank);
                if (ReferenceEquals(c, x.Chosen)) tags.Add("CHOSEN");
                if (c.D != null && ReferenceEquals(c.D, pair.Nearest)) tags.Add("nearest");
                if (c.D != null && c.D.IsSpecial) tags.Add("own file");
                if (c.D != null && c.D.OnAxis) tags.Add("on-axis input");
                if (c.HamNote.Length > 0) tags.Add(c.HamNote);
                if (!c.Pass) tags.Add("fails check: " + c.Fail);
                Say(F("  {0,2}. {1,-34} start MF {2,12} -> MF {3,12}  spot {4,8} um{5}", r + 1, c.Label, G(c.MfStart), G(c.Mf),
                    G(c.Spot, 4), tags.Count > 0 ? "  [" + string.Join(", ", tags) + "]" : ""));
            }
            var skippedScreen = cands.Where(c => !c.Optimized && c.ScreenRank > 0).OrderBy(c => c.ScreenRank).ToList();
            if (screen && skippedScreen.Count > 0)
                Say(F("  Not optimized (outside the top {0} of the screen): {1}", Opts.Top,
                    string.Join(", ", skippedScreen.Select(c => "#" + c.ScreenRank + " " + c.Label))));
            // Say plainly when "best" stepped over a lower score because that lens failed the checks.
            if (mode == "best")
            {
                int skipped = ranked.Count(c => !c.Pass && IsFinite(c.Mf) && c.Mf < x.Chosen.Mf);
                if (!x.Chosen.Pass) Say("  No candidate passed the checks; kept the lowest score (see the check below).");
                else if (skipped > 0) Say(F("  Passed over {0} lower-scoring candidate(s) that failed the checks.", skipped));
            }
            // Blend and nearest are compared on their DLS scores (before any Hammer), like the inputs table.
            double nearMf = cands.Where(c => c.Optimized && c.D != null && ReferenceEquals(c.D, pair.Nearest)).Select(DlsMf).DefaultIfEmpty(double.NaN).First();
            double blendMf = cands.Where(c => c.Kind == "blend" && c.Optimized).Select(DlsMf).DefaultIfEmpty(double.NaN).First();
            double secs = clock.Elapsed.TotalSeconds;

            var checkText = checks.Select(c => c.Item1 + " | " + (c.Item2 ? "ok" : "FAIL")).ToList();
            WriteTables(outs, designs, env, target, mfStart, mfResult, resultFo, checkText, pass, pair, blendMf, cands, x.Chosen, secs);
            string bestName;
            double bestMf = BestInput(group, out bestName);
            Say("");
            Say("=== RESULT ===");
            Say(F("Chosen start: {0} (-start {1})", x.Chosen.Label, mode));
            Say(F("Result MF {0} (mean RMS spot {1} um)  vs best input at target ({2}) MF {3}",
                G(mfResult), G(resultFo["spot_um"], 4), string.IsNullOrEmpty(bestName) ? "n/a" : bestName, G(bestMf)));
            if (IsFinite(bestMf) && IsFinite(mfResult)) Say(VersusText("the best input re-sized to the target", mfResult, bestMf));
            // Blend vs nearest-design start, both optimized with the same report card.
            if (IsFinite(nearMf) && IsFinite(blendMf) && pair.Nearest != null)
                Say(F("Blend vs nearest-design start ({0}): {1} (blend MF {2}, nearest MF {3}).",
                    pair.Nearest.Name, Math.Abs(blendMf / nearMf - 1.0) < 5e-4 ? "they match"
                    : (blendMf < nearMf ? "blend wins" : "nearest wins") + F(", blend {0:+0.0;-0.0}%", 100.0 * (blendMf / nearMf - 1.0)),
                    G(blendMf), G(nearMf)));
            Say("Envelope check: " + (pass ? "PASS" : "FAIL"));
            foreach (var c in checks) Say("  [" + (c.Item2 ? "ok" : "XX") + "] " + c.Item1);
            Say(F("Runtime: {0:0} s ({1} optimization(s))", secs, x.NOpt));
            Say("Files: " + outDir);
            WriteReport(outs);
            Progress(app, 100, "Done. " + (pass ? "Result saved." : "Result saved but failed the envelope check."));
            if (!Opts.Quiet && FolderFromDialog && pngOk) OpenFile(outs["png"]);
            if (Attached && !Opts.Quiet && !Opts.NoDialog) OfferLoad(app, outs["result"]);
            Say(F("RETURN start={0} chosen={1} nearest={2} mf_start={3} mf_result={4} best_input_mf={5} nearest_mf={6} blend_mf={7} pair_warn={8} envelope={9} secs={10:0} top={11} nopt={12} screen_rank={13} hammer={14} save={15}",
                mode, x.Chosen.Label, pair.Nearest == null ? "n/a" : pair.Nearest.Name, G(mfStart, 8), G(mfResult, 8), G(bestMf, 8),
                G(nearMf, 8), G(blendMf, 8), pair.Warn ? "yes" : "no", pass ? "PASS" : "FAIL", secs,
                screen ? Opts.Top.ToString(CultureInfo.InvariantCulture) : "all", x.NOpt, x.Chosen.ScreenRank, Opts.HammerSec, outs["result"]));
            if (!pass) throw new ToolExitException(3, "result was written but failed the envelope check (see report)");
            return 0;
        }

        // "matches" / "is better than" / "is worse than", with the percentage.
        static string VersusText(string what, double mf, double reference)
        {
            double rel = mf / reference - 1.0;
            if (Math.Abs(rel) < 5e-4) return "Result matches " + what + ".";
            return F("Result is {0} than {1} ({2:+0.0;-0.0}%).", rel < 0 ? "better" : "worse", what, 100.0 * rel);
        }

        // Try one candidate start at the request: build it, refocus, score it; with
        // optimize, save it as a temp start, polish it, and keep it if it is the one
        // this mode wants (best = passes the checks, then lowest MF so far). The keeper's start and result are
        // written right away, so nothing has to be rebuilt at the end.
        static Cand Evaluate(RunCtx x, string kind, Design d, List<SurfRx> rx, bool optimize)
        {
            var c = new Cand { Kind = kind, D = d, Label = kind == "blend" ? F("blend of {0} designs", x.BlendCount) : d.Name };
            try
            {
                var S = x.S;
                if (kind == "blend") BuildSystem(S, rx, x.Stop, x.T, x.Catalogs, "StartPointFinder blended start");
                else if (d.IsSpecial) BuildNative(S, d, x.T, "StartPointFinder start = input " + d.Name + " (own file, scaled)");
                else BuildSystem(S, ScaledRx(d), d.Stop, x.T, x.Catalogs, "StartPointFinder start = input " + d.Name + " (scaled)");
                BuildMerit(S, x.T, x.Env, x.B);
                QuickFocus(S);
                c.MfStart = Merit(S);
                if (!optimize) return c;
                S.SaveAs(x.TmpStart);
                MakeVariables(S, kind == "blend" ? x.Stop : d.Stop);
                c.Mf = Optimize(x.App, S, Opts.Passes, 0);
                c.Spot = MeanSpotUm(S);
                c.Optimized = true;
                x.NOpt++;
                // Give every optimized candidate the same honest checks as the final result.
                // A low score is not enough: the optimizer can "win" by making rays miss a
                // surface or by bending an asphere so its edge no longer exists.
                bool ok;
                bool? edgeOk;
                var chk = Validate(S, Measure(S, x.T.Obj), x.T, x.Env, x.B, c.Mf, out ok, out edgeOk);
                c.Pass = ok;
                c.EdgeOk = edgeOk;
                c.Fail = string.Join("; ", chk.Where(t => !t.Item2).Select(t => ShortCheck(t.Item1)));
                c.MfDls = c.Mf; c.SpotDls = c.Spot;
                if (Opts.HammerSec > 0)
                {
                    // -hammer polishes several candidates later, so keep a temp copy of each
                    // optimized lens and its start (deleted again before the tool ends).
                    string stem = Path.Combine(x.OutDir, F("{0}_cand{1:00}", Tool, x.NOpt));
                    c.OptPath = stem + ".tmp.zmx"; c.StartPath = stem + "_start.tmp.zmx";
                    S.SaveAs(c.OptPath);
                    File.Copy(x.TmpStart, c.StartPath, true);
                }
                // best = a lens that passes beats one that fails; then the lower score wins.
                bool keep = x.Mode == "best" ? IsFinite(c.Mf) && (x.Chosen == null || Better(c, x.Chosen))
                    : x.Mode == "blend" ? kind == "blend"
                    : kind == "input" && ReferenceEquals(d, x.Pair.Nearest);
                if (keep)
                {
                    S.SaveAs(x.Outs["result"]);
                    File.Copy(x.TmpStart, x.Outs["start"], true);
                    x.Chosen = c;
                }
            }
            catch (Exception ex)
            {
                c.Note = "failed: " + ex.Message;
                Say("  candidate " + c.Label + " failed: " + ex.Message);
            }
            return c;
        }

        // Is candidate a a better keeper than b? Passing the checks counts first, then the score.
        static bool Better(Cand a, Cand b) => (a.Pass && !b.Pass) || (a.Pass == b.Pass && a.Mf < b.Mf);

        // Optimized candidates in report order: passing ones first, each part by score.
        static List<Cand> Ranked(List<Cand> cands) => cands.Where(c => c.Optimized)
            .OrderBy(c => c.Pass ? 0 : 1).ThenBy(c => IsFinite(c.Mf) ? c.Mf : double.MaxValue).ToList();

        // Number the starts by their report-card score before optimizing (1 = lowest).
        // A start whose score could not be computed (rays fail) goes to the end.
        // Ties keep their reading order, so the C# and Python builds rank the same way.
        static void SetScreenRanks(List<Cand> cands)
        {
            var order = Enumerable.Range(0, cands.Count)
                .OrderBy(i => IsFinite(cands[i].MfStart) ? cands[i].MfStart : double.PositiveInfinity).ThenBy(i => i).ToList();
            for (int r = 0; r < order.Count; r++) cands[order[r]].ScreenRank = r + 1;
        }

        // A candidate's score after DLS only (before any Hammer polish).
        static double DlsMf(Cand c) => IsFinite(c.MfDls) ? c.MfDls : c.Mf;

        // Hammer = a longer "shake it and walk downhill again" search that stops after
        // HammerSec seconds of wall time (so results vary a little from PC to PC).
        // -start best: polish the best HammerKeep optimized lenses that pass the checks
        // (the keeper alone if none pass), check each again, and keep the best.
        // -start nearest/blend: polish only the forced keeper.
        // A polished lens that now FAILS the checks is not used when its DLS version passed.
        // Writes the keeper's result and start files and updates x.Chosen.
        static void HammerPolish(RunCtx x, List<Cand> cands)
        {
            var S = x.S;
            var polish = x.Mode == "best" ? Ranked(cands).Where(c => c.Pass).Take(HammerKeep).ToList() : new List<Cand>();
            if (polish.Count == 0) polish.Add(x.Chosen);
            Say(F("Hammer polish ({0} s each, wall-time limited) on {1} lens(es):", Opts.HammerSec, polish.Count));
            foreach (var c in polish)
            {
                if (Terminated(x.App)) break;
                if (string.IsNullOrEmpty(c.OptPath) || !File.Exists(c.OptPath)) continue;
                S.LoadFile(c.OptPath, false);
                double mfH = Optimize(x.App, S, 0, Opts.HammerSec);
                var fo = Measure(S, x.T.Obj);
                bool okH; bool? edgeH;
                var chk = Validate(S, fo, x.T, x.Env, x.B, mfH, out okH, out edgeH);
                string failH = string.Join("; ", chk.Where(t => !t.Item2).Select(t => ShortCheck(t.Item1)));
                // Use the polished lens when it passes, or when neither version passes and it scores lower.
                bool use = IsFinite(mfH) && (okH || (!c.Pass && mfH < c.Mf));
                if (use)
                {
                    string hamPath = c.OptPath.Replace(".tmp.zmx", "_ham.tmp.zmx");
                    S.SaveAs(hamPath);
                    c.OptPath = hamPath;
                    c.Mf = mfH; c.Spot = fo["spot_um"]; c.Pass = okH; c.Fail = failH; c.EdgeOk = edgeH;
                    c.Hammered = true;
                    c.HamNote = "Hammer " + G(c.MfDls) + " -> " + G(mfH);
                }
                else c.HamNote = "Hammer result " + G(mfH) + " failed checks (" + failH + "); kept DLS";
                Say(F("  {0,-34} DLS MF {1,12} -> Hammer MF {2,12}  spot {3,8} um  {4}", c.Label, G(c.MfDls), G(mfH),
                    G(fo["spot_um"], 4), use ? "used" : "NOT used (fails: " + failH + ")"));
            }
            // Pick the keeper again among every optimized candidate (polished ones now carry Hammer numbers).
            if (x.Mode == "best")
                foreach (var c in Ranked(cands))
                    if (IsFinite(c.Mf) && Better(c, x.Chosen)) x.Chosen = c;
            if (!string.IsNullOrEmpty(x.Chosen.OptPath) && File.Exists(x.Chosen.OptPath))
            {
                File.Copy(x.Chosen.OptPath, x.Outs["result"], true);
                File.Copy(x.Chosen.StartPath, x.Outs["start"], true);
            }
        }

        // Delete the per-candidate temp lenses that -hammer needed.
        static void CleanupCandFiles(List<Cand> cands)
        {
            foreach (var c in cands)
                foreach (var p in new[] { c.OptPath, c.StartPath, (c.OptPath ?? "").Replace("_ham.tmp.zmx", ".tmp.zmx") })
                    if (!string.IsNullOrEmpty(p)) TryDelete(p);
        }

        // The short name of a failed check: its words up to the first number or
        // comparison, so "thinnest glass edge -0.3 > 0" becomes "thinnest glass edge".
        static string ShortCheck(string item)
        {
            int i = item.IndexOf(" (", StringComparison.Ordinal);
            if (i > 0) item = item.Substring(0, i);
            var stop = new HashSet<string> { "vs", "inside", ">=", ">", "<", "<=" };
            var keep = new List<string>();
            foreach (var w in item.Split(' '))
            {
                double num;
                if (stop.Contains(w) || double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) break;
                keep.Add(w);
            }
            if (keep.Count > 1 && keep[keep.Count - 1] == "and") keep.RemoveAt(keep.Count - 1);  // "finite and < 1e8"
            return keep.Count > 0 ? string.Join(" ", keep) : item;
        }

        // An input's own recipe at focal length 1 (curvature x EFL, thickness / EFL).
        static List<SurfRx> ScaledRx(Design d) => d.Surfs.Select(s => new SurfRx
        {
            Curv = s.Curv * d.Efl, Thick = s.Thick / d.Efl, Conic = s.Conic, Glass = s.Glass, Catalog = s.Catalog
        }).ToList();

        // Remove a temp file (and a same-named side file OpticStudio may write).
        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try
            {
                string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path);
                foreach (var f in Directory.GetFiles(dir, stem + ".*")) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        // Open a picture with Windows' default viewer (explorer handles the file type).
        static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start(path); }
            catch
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\""); }
                catch (Exception ex) { Say("  Could not open " + path + ": " + ex.Message); }
            }
        }

        // Ribbon only: offer to load the result into the main window. Default is No,
        // because loading replaces the lens that is open there.
        static void OfferLoad(ZOSAPI.IZOSAPI_Application app, string result)
        {
            try
            {
                var main = app.PrimarySystem;
                bool dirty = false;
                try { dirty = main.NeedsSave; } catch { }
                string msg = "Open the result in the main OpticStudio window now?\n\n" + result + "\n\nThis replaces the lens that is open there"
                    + (dirty ? " - and that lens has UNSAVED changes, which would be lost." : ".");
                var answer = WF.MessageBox.Show(msg, Tool, WF.MessageBoxButtons.YesNo,
                    dirty ? WF.MessageBoxIcon.Warning : WF.MessageBoxIcon.Question, WF.MessageBoxDefaultButton.Button2);
                if (answer != WF.DialogResult.Yes) { Say("Result not loaded into the main window (you chose No)."); return; }
                Say(main.LoadFile(result, false) ? "Loaded the result into the main window." : "Could not load the result into the main window.");
            }
            catch (Exception ex) { Say("  Could not offer to load the result: " + ex.Message); }
        }

        // Envelope numbers at 5 digits, so 0.9999995 shows as 1.
        static void SayEnv(string label, double[] e) =>
            Say(F("  {0,-13} {1,10} {2,10} {3,10}", label, G(e[0], 5), G(e[1], 5), G(e[2], 5)));

        // Ribbon progress bar + "Terminate" button (quietly ignored when standalone).
        static void Progress(ZOSAPI.IZOSAPI_Application app, int pct, string msg)
        {
            try { app.ProgressPercent = pct; app.ProgressMessage = msg; } catch { }
        }

        static bool Terminated(ZOSAPI.IZOSAPI_Application app)
        {
            try { return app.TerminateRequested; } catch { return false; }
        }

        static Dictionary<string, string> OutPaths(string outDir)
        {
            return new Dictionary<string, string>
            {
                { "result", Path.Combine(outDir, Tool + "_result.zmx") },
                { "start", Path.Combine(outDir, Tool + "_start.zmx") },
                { "csv", Path.Combine(outDir, Tool + "_firstorder.csv") },
                { "json", Path.Combine(outDir, Tool + "_summary.json") },
                { "report", Path.Combine(outDir, Tool + "_report.txt") },
                { "png", Path.Combine(outDir, Tool + "_layout.png") },
            };
        }

        // Never write over an input; only replace old outputs with -force.
        static void GuardOutputs(Dictionary<string, string> outs, List<string> inputs)
        {
            var low = new HashSet<string>(inputs.Select(p => Path.GetFullPath(p)), StringComparer.OrdinalIgnoreCase);
            foreach (var p in outs.Values)
                if (low.Contains(p)) throw new ToolExitException(2, "output would overwrite an input: " + p);
            var existing = outs.Values.FirstOrDefault(File.Exists);
            if (existing != null && !Opts.Force)
                throw new ToolExitException(2, "outputs already exist (pass -force to replace them): " + existing);
        }

        // Save every line we printed into the text report.
        static void WriteReport(Dictionary<string, string> outs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outs["report"]));
            File.WriteAllLines(outs["report"], Report, Encoding.UTF8);
        }

        // ---------------------------------------------------------------
        // Step 1: read one design
        // ---------------------------------------------------------------

        // Open one file (read only; we never save it) and write down its shape
        // and its first-order numbers. Anything we cannot use gets a reason and a
        // short category (for the "skipped: 3 finite object, ..." summary).
        static Design ReadDesign(ZOSAPI.IOpticalSystem S, string path)
        {
            var d = new Design { Name = Path.GetFileName(path), Path = path };
            try
            {
                var bytes = File.ReadAllBytes(path);
                d.Hash = Sha256Hex(bytes);
                d.FileCatalogs = GcatList(bytes);
                if (!S.LoadFile(path, false)) return Reject(d, "could not load", "could not load");
                if (S.Mode != ZOSAPI.SystemType.Sequential) return Reject(d, "not a Sequential system", "not sequential");
                var lde = S.LDE;
                int n = lde.NumberOfSurfaces;
                d.NSurf = n;
                d.Stop = lde.StopSurface;
                // Infinite object (objectives) or a finite one; a finite one must match the group's.
                double t0 = lde.GetSurfaceAt(0).Thickness;
                d.ObjDist = IsFinite(t0) && Math.Abs(t0) < 1e9 ? t0 : double.PositiveInfinity;
                var mask = new StringBuilder();
                var special = new List<string>();
                for (int i = 1; i < n - 1; i++)
                {
                    var s = lde.GetSurfaceAt(i);
                    bool std = s.Type == ZOSAPI.Editors.LDE.SurfaceType.Standard;
                    bool allowed = std || s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric
                        || s.Type == ZOSAPI.Editors.LDE.SurfaceType.ZernikeStandardSag
                        || s.Type == ZOSAPI.Editors.LDE.SurfaceType.Paraxial;
                    if (!allowed)
                        return Reject(d, F("surface {0} is {1} (Standard, Even Asphere, Zernike Standard Sag and Paraxial are supported)",
                            i, s.TypeName), "unsupported surface type");
                    if (!std) special.Add(F("S{0} {1}", i, s.TypeName));
                    string mat = (s.Material ?? "").Trim();
                    if (mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                        return Reject(d, F("surface {0} is a mirror (refractive objectives only)", i), "mirror");
                    string cat = "";
                    if (mat.Length > 0)
                    {
                        try { cat = (s.MaterialCatalog ?? "").Trim(); } catch { cat = ""; }
                        if (cat.Length == 0)
                            return Reject(d, F("surface {0} glass '{1}' has no catalog (model glass not supported)", i, mat), "model glass");
                    }
                    double conic = IsFinite(s.Conic) ? s.Conic : 0.0;
                    d.Surfs.Add(new SurfRx
                    {
                        Curv = Curv(s.Radius), Thick = s.Thickness, Glass = mat, Catalog = cat, Conic = conic,
                        Type = std ? "" : s.TypeName, Pars = std ? null : ReadPars(s),
                    });
                    mask.Append(mat.Length > 0 ? 'G' : 'A');
                    if (mat.Length > 0)
                    {
                        // A glass that does not resolve comes back as air (nd 1, Vd 0): say which catalog is missing.
                        var nv = GlassIndex(S, cat, mat);
                        d.NdVd[i] = nv;
                        if (!(nv[0] > 1.0001 && nv[1] > 0))
                        {
                            var missing = d.FileCatalogs.Where(c => !AvailCats.Contains(c)).ToList();
                            return missing.Count > 0
                                ? Reject(d, F("surface {0} glass '{1}' does not resolve: catalog {2} is not installed",
                                    i, mat, string.Join(", ", missing)), "missing glass catalog")
                                : Reject(d, F("surface {0} glass '{1}' does not resolve in catalog {2}", i, mat, cat), "unresolved glass");
                        }
                    }
                }
                d.IsSpecial = special.Count > 0;
                d.SpecialNote = string.Join(", ", special);
                // Layout fingerprint: surface count, stop position, glass/air pattern.
                d.Signature = F("{0}surf/stop{1}/{2}", n, d.Stop, mask);
                d.Efl = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, PrimaryWave(S));
                d.Fno = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.ISFN);
                d.Track = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.TOTR);
                if (!IsFinite(d.Efl) || d.Efl <= 0) return Reject(d, "EFL is not a positive number (" + G(d.Efl) + ")", "EFL not positive");
                // Absurd first-order data means the file is not what it looks like (e.g. glass gone to air).
                if (!(d.Efl < 1e6) || !(d.Fno >= 0.05 && d.Fno <= 1000) || !IsFinite(d.Track))
                    return Reject(d, F("first-order data is not physical (EFL {0}, F/# {1}, track {2})", G(d.Efl), G(d.Fno), G(d.Track)),
                        "unphysical first order");
                d.Hfov = HalfFov(S, d.Efl, d.ObjDist);
                if (!IsFinite(d.Hfov))
                    return Reject(d, "field type " + S.SystemData.Fields.GetFieldType() + " is not supported here", "field type");
                d.Ok = true;
                return d;
            }
            catch (Exception ex)
            {
                return Reject(d, "read error: " + ex.Message, "read error");
            }
        }

        // Mark a design skipped with a reason (long) and a category (short).
        static Design Reject(Design d, string reason, string cat)
        {
            d.Ok = false; d.Reason = reason; d.Cat = cat;
            return d;
        }

        // Par1..Par8 of a surface (even-asphere terms on Even Asphere / Zernike Standard Sag).
        static double[] ReadPars(ZOSAPI.Editors.LDE.ILDERow s)
        {
            var v = new double[8];
            for (int k = 0; k < 8; k++)
            {
                try
                {
                    var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + (k + 1));
                    v[k] = s.GetSurfaceCell(col).DoubleValue;
                }
                catch { v[k] = double.NaN; }
            }
            return v;
        }

        // Even-asphere terms of a surface, or null for a surface without them.
        static double[] AsphereTerms(ZOSAPI.Editors.LDE.ILDERow s) =>
            s.Type == ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric || s.Type == ZOSAPI.Editors.LDE.SurfaceType.ZernikeStandardSag
                ? ReadPars(s) : null;

        static string Sha256Hex(byte[] bytes)
        {
            using (var h = SHA256.Create())
                return string.Concat(h.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        // Catalog name in one spelling: upper case, no ".AGF".
        static string NormCat(string c)
        {
            string u = (c ?? "").Trim().ToUpperInvariant();
            return u.EndsWith(".AGF") ? u.Substring(0, u.Length - 4) : u;
        }

        // .zmx text: UTF-16 (with or without BOM), UTF-8 with BOM, else Latin-1.
        static string DecodeZmx(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[1] == 0) return Encoding.Unicode.GetString(b);
            return Encoding.GetEncoding(28591).GetString(b);
        }

        // The glass catalogs the file asks for (its "GCAT" line).
        static List<string> GcatList(byte[] bytes)
        {
            foreach (var raw in DecodeZmx(bytes).Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("GCAT ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("GCAT\t", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(5).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(NormCat).ToList();
            }
            return new List<string>();
        }

        // Glass catalogs installed on this PC (names in NormCat spelling).
        static HashSet<string> InstalledCatalogs(ZOSAPI.IOpticalSystem S)
        {
            var set = new HashSet<string>();
            try { foreach (var c in S.SystemData.MaterialCatalogs.GetAvailableCatalogs()) set.Add(NormCat(c)); } catch { }
            return set;
        }

        // Byte-identical files count once; so do files with the same lens data
        // (same surfaces, glasses, aperture and field) saved with other settings.
        static void MarkDuplicates(List<Design> designs)
        {
            var seen = new Dictionary<string, string>();
            foreach (var d in designs)
            {
                if (string.IsNullOrEmpty(d.Hash)) continue;
                string first;
                if (seen.TryGetValue(d.Hash, out first)) Reject(d, "duplicate of " + first + " (identical file content)", "duplicate file");
                else seen[d.Hash] = d.Name;
            }
            var keys = new Dictionary<string, string>();
            foreach (var d in designs.Where(d => d.Ok))
            {
                string key = RxKey(d), first;
                if (keys.TryGetValue(key, out first)) Reject(d, "same prescription as " + first + " (files differ outside the lens data)", "same prescription");
                else keys[key] = d.Name;
            }
        }

        // Lens data written as text at 9 significant digits (equal text = same lens).
        static string RxKey(Design d)
        {
            var sb = new StringBuilder();
            sb.Append(d.Signature).Append('|').Append(G(d.ObjDist, 9)).Append('|').Append(G(d.Efl, 9)).Append('|')
              .Append(G(d.Fno, 9)).Append('|').Append(G(d.Hfov, 9));
            foreach (var s in d.Surfs)
            {
                sb.Append('|').Append(s.Type).Append(';').Append(G(s.Curv, 9)).Append(';').Append(G(s.Thick, 9)).Append(';')
                  .Append(s.Glass.ToUpperInvariant()).Append(';').Append(G(s.Conic, 9));
                if (s.Pars != null) foreach (var p in s.Pars) sb.Append(';').Append(G(p, 9));
            }
            return sb.ToString();
        }

        // "3 finite object, 2 duplicate file" - skip counts by category, first seen first.
        static string SkipSummary(List<Design> designs)
        {
            var order = new List<string>();
            var count = new Dictionary<string, int>();
            foreach (var d in designs.Where(d => !d.Ok))
            {
                string c = string.IsNullOrEmpty(d.Cat) ? "other" : d.Cat;
                if (!count.ContainsKey(c)) { count[c] = 0; order.Add(c); }
                count[c]++;
            }
            return string.Join(", ", order.Select(c => count[c] + " " + c));
        }

        // Ask OpticStudio for one merit-function number (EFFL, ISFN, ...) without editing the MF.
        static double Op(ZOSAPI.IOpticalSystem S, ZOSAPI.Editors.MFE.MeritOperandType t,
            int i1 = 0, int i2 = 0, double d3 = 0, double d4 = 0, double d5 = 0, double d6 = 0)
        {
            try { return S.MFE.GetOperandValue(t, i1, i2, d3, d4, d5, d6, 0, 0); }
            catch { return double.NaN; }
        }

        // Number of the main (primary) wavelength; EFL is measured there.
        static int PrimaryWave(ZOSAPI.IOpticalSystem S)
        {
            try
            {
                var w = S.SystemData.Wavelengths;
                for (int k = 1; k <= w.NumberOfWavelengths; k++)
                    if (w.GetWavelength(k).IsPrimary) return k;
            }
            catch { }
            return 1;
        }

        // Biggest field angle in degrees. Image-height fields become angles via atan(h / EFL);
        // object-height fields (finite object) via atan(h / (object distance + entrance pupil position)).
        static double HalfFov(ZOSAPI.IOpticalSystem S, double efl, double obj)
        {
            var fields = S.SystemData.Fields;
            var ft = fields.GetFieldType();
            double biggest = 0;
            for (int k = 1; k <= fields.NumberOfFields; k++)
            {
                var f = fields.GetField(k);
                biggest = Math.Max(biggest, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }
            if (ft == ZOSAPI.SystemData.FieldType.Angle) return biggest;
            if (ft == ZOSAPI.SystemData.FieldType.ParaxialImageHeight || ft == ZOSAPI.SystemData.FieldType.RealImageHeight)
                return Quant(Math.Atan(biggest / efl) * 180.0 / Math.PI, 1e10);
            if (ft == ZOSAPI.SystemData.FieldType.ObjectHeight && IsFinite(obj))
            {
                double enpp = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.ENPP);
                double L = obj + (IsFinite(enpp) ? enpp : 0.0);
                if (L > 0) return Quant(Math.Atan(biggest / L) * 180.0 / Math.PI, 1e10);
            }
            return double.NaN;
        }

        // Look up nd (how much the glass bends light) and Vd (how much it splits colors).
        static double[] GlassIndex(ZOSAPI.IOpticalSystem S, string catalog, string name)
        {
            string key = catalog.ToUpperInvariant() + "|" + name.ToUpperInvariant();
            double[] v;
            if (GlassCache.TryGetValue(key, out v)) return v;
            v = new[] { double.NaN, double.NaN };
            try
            {
                var mc = S.Tools.OpenMaterialsCatalog();
                try
                {
                    mc.SelectedCatalog = catalog;
                    mc.SelectedMaterial = name;
                    v = new[] { mc.Nd, mc.Vd };
                }
                finally { try { mc.Close(); } catch { } }  // only one tool may be open at a time
            }
            catch { }
            GlassCache[key] = v;
            return v;
        }

        // ---------------------------------------------------------------
        // Step 2 + 3: group and envelope
        // ---------------------------------------------------------------

        // Keep the biggest family that shares one layout fingerprint AND one object
        // distance (infinite, or finite values within 0.1%); -layout picks the
        // fingerprint instead. Everyone else is skipped; other groups are listed.
        static List<Design> PickGroup(List<Design> designs, out List<string> others)
        {
            others = new List<string>();
            var ok = designs.Where(d => d.Ok).ToList();
            if (ok.Count == 0) return new List<Design>();
            var reps = new List<double>();
            foreach (var d in ok)
            {
                if (!IsFinite(d.ObjDist)) { d.ObjRep = double.PositiveInfinity; continue; }
                int r = reps.FindIndex(v => SameObj(v, d.ObjDist));
                if (r < 0) { reps.Add(d.ObjDist); r = reps.Count - 1; }
                d.ObjRep = reps[r];
            }
            Func<Design, string> key = d => d.Signature + "|" + G(d.ObjRep, 12);
            var order = ok.Select(key).Distinct().ToList();
            var buckets = ok.GroupBy(key).ToDictionary(gr => gr.Key, gr => gr.ToList());
            var pool = order;
            if (!string.IsNullOrEmpty(Opts.Layout))
            {
                pool = order.Where(kk => string.Equals(buckets[kk][0].Signature, Opts.Layout, StringComparison.OrdinalIgnoreCase)).ToList();
                if (pool.Count == 0)
                    throw new ToolExitException(2, F("-layout {0} matches no usable group; groups here: {1}", Opts.Layout,
                        string.Join("; ", order.Select(kk => GroupLabel(buckets[kk][0]) + " (" + buckets[kk].Count + ")"))));
            }
            // Biggest group wins; on a tie, the group seen first (sorted file order) wins.
            string best = pool.OrderByDescending(kk => buckets[kk].Count).ThenBy(kk => order.IndexOf(kk)).First();
            var main = buckets[best];
            var m0 = main[0];
            foreach (var d in ok.Where(d => key(d) != best))
            {
                if (d.Signature == m0.Signature)
                    Reject(d, F("object distance {0} differs from the main group's {1} (a finite object must match within 0.1%)",
                        ObjText(d.ObjDist), ObjText(m0.ObjRep)), "object distance mismatch");
                else
                    Reject(d, "layout " + d.Signature + " does not match the main group " + m0.Signature, "layout mismatch");
            }
            foreach (var kk in order.Where(kk => kk != best).OrderByDescending(kk => buckets[kk].Count).ThenBy(kk => order.IndexOf(kk)).Take(5))
            {
                var b = buckets[kk];
                string names = string.Join(", ", b.Take(4).Select(d => d.Name)) + (b.Count > 4 ? ", ..." : "");
                others.Add(F("other group: {0}: {1} design(s) ({2}); pick it with -layout {3}", GroupLabel(b[0]), b.Count, names, b[0].Signature));
            }
            return main;
        }

        // Two finite object distances count as the same within 0.1% (tiny absolute floor).
        static bool SameObj(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-3 * Math.Abs(a), 1e-6);

        static string ObjText(double v) => IsFinite(v) ? G(v) : "infinity";

        static string GroupLabel(Design d) => d.Signature + ", object at " + ObjText(d.ObjRep);

        static double Median(List<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        }

        static double[] Span(List<double> v) => new[] { v.Min(), Median(v), v.Max() };

        // Smallest, middle (median), and largest value of each first-order number.
        static Envelope MakeEnvelope(List<Design> g) => new Envelope
        {
            Efl = Span(g.Select(d => d.Efl).ToList()),
            Fno = Span(g.Select(d => d.Fno).ToList()),
            Hfov = Span(g.Select(d => d.Hfov).ToList()),
            TrackRatio = Span(g.Select(d => d.TrackRatio).ToList()),
        };

        // Median by default. A user value outside min..max is refused (or clamped with -clamp).
        static Target PickTarget(Envelope env)
        {
            var t = new Target
            {
                Efl = OneTarget(env.Efl, Opts.Efl, "-efl"),
                Fno = OneTarget(env.Fno, Opts.Fno, "-fno"),
                Hfov = OneTarget(env.Hfov, Opts.Fov, "-hfov"),
            };
            if (t.Efl <= 0 || t.Fno <= 0) throw new ToolExitException(2, "target EFL and F/# must be positive");
            return t;
        }

        static double OneTarget(double[] e, double? user, string label)
        {
            if (!user.HasValue) return e[1];
            double u = user.Value;
            // Round-off slack (1e-5 relative) so "-fno 1" counts as inside a range that starts at 0.9999995.
            if (InRange(u, e)) return Math.Min(Math.Max(u, e[0]), e[2]);
            if (Opts.Clamp)
            {
                double c = Math.Min(Math.Max(u, e[0]), e[2]);
                Say(F("  {0} {1} is outside {2}..{3}; clamped to {4}", label, G(u), G(e[0], 5), G(e[2], 5), G(c, 5)));
                return c;
            }
            throw new ToolExitException(2, F("{0} {1} is outside the input envelope {2}..{3} (use -clamp to clamp)",
                label, G(u), G(e[0], 5), G(e[2], 5)));
        }

        static bool InRange(double u, double[] e) =>
            u >= e[0] - (1e-5 * Math.Abs(e[0]) + 1e-6) && u <= e[2] + (1e-5 * Math.Abs(e[2]) + 1e-6);

        // ---------------------------------------------------------------
        // Steering by F/# and field
        // ---------------------------------------------------------------

        // Distance between two (F/#, half-field) pairs, each axis measured in the
        // folder's own spread (so neither axis swamps the other):
        //   d = sqrt( (log2(Fa/Fb) / log2(Fmax/Fmin))^2 + ((fieldA - fieldB) / (fmax - fmin))^2 )
        // An axis with no spread uses 1 (F/#) or 10 deg (field); the request is in range,
        // so that axis then adds nothing. An on-axis-only design (half-field 0) in a
        // folder that has field designs gets +1 (in quadrature) for a field request:
        // its shape was never asked to handle field, so it should not look close.
        const double NearEps = 0.1;        // softening so an exact match does not get infinite weight
        const double FarDist = 0.5;        // farther than this from every input = warn
        const double OnAxisPenalty = 1.0;
        static double FnoScale = 1.0, FieldScale = 10.0, FieldMax = 0.0;

        static void SetScales(Envelope env)
        {
            double sf = Math.Log(env.Fno[2] / env.Fno[0], 2.0);
            FnoScale = IsFinite(sf) && sf > 1e-6 ? sf : 1.0;
            double sv = env.Hfov[2] - env.Hfov[0];
            FieldScale = IsFinite(sv) && sv > 1e-6 ? sv : 10.0;
            FieldMax = env.Hfov[2];
        }

        static bool OnAxisOnly(double hfov) => hfov <= 1e-6 && FieldMax > 1e-6;

        static double RawDist(double fnoA, double hfovA, double fnoB, double hfovB)
        {
            double df = (fnoA > 0 && fnoB > 0) ? Math.Log(fnoA / fnoB, 2.0) / FnoScale : 4.0;
            double dv = (hfovA - hfovB) / FieldScale;
            return Math.Sqrt(df * df + dv * dv);
        }

        // Distance from a design (first pair) to a request (second pair), with the on-axis penalty.
        static double NormDist(double fnoD, double hfovD, double fnoT, double hfovT)
        {
            double d = RawDist(fnoD, hfovD, fnoT, hfovT);
            if (OnAxisOnly(hfovD) && hfovT > 1e-6) d = Math.Sqrt(d * d + OnAxisPenalty * OnAxisPenalty);
            return d;
        }

        // How much each blend member counts in the average (always adds up to 1).
        //   near  (default): w = 1 / (d^2 + 0.1^2)   inverse-square, the closest inputs dominate
        //   soft  (old):     w = 1 / (0.25 + d)      gentle, everyone keeps a fair share
        //   equal:           w = 1
        // Every kept design gets a distance; only members (Standard surfaces) get a weight.
        static void SetWeights(List<Design> group, List<Design> members, Target t)
        {
            foreach (var d in group)
            {
                d.Dist = Quant(NormDist(d.Fno, d.Hfov, t.Fno, t.Hfov), 1e12);
                d.Weight = 0.0;
                d.BlendWeight = 0.0;
            }
            foreach (var d in members)
            {
                if (Opts.Weight == "equal") d.Weight = 1.0;
                else if (Opts.Weight == "soft") d.Weight = 1.0 / (0.25 + d.Dist);
                else d.Weight = 1.0 / (d.Dist * d.Dist + NearEps * NearEps);
            }
            double total = members.Sum(d => d.Weight);
            foreach (var d in members) { d.Weight = total > 0 ? d.Weight / total : 0.0; d.BlendWeight = d.Weight; }
        }

        // Print every kept design by distance, with its blend weight ("--" = not in the blend).
        static void SayWeights(List<Design> group, PairInfo pair, string mode)
        {
            Say(F("Closeness (weight {0}; distance = log2 F/# over {1} and field over {2} deg{3}). Start mode: {4}",
                Opts.Weight, G(FnoScale, 4), G(FieldScale, 4), FieldMax > 1e-6 ? ", on-axis-only designs +1 in quadrature when a field is asked" : "", mode));
            foreach (var d in group.OrderBy(d => d.Dist).ThenByDescending(d => d.BlendWeight))
            {
                string tags = (ReferenceEquals(d, pair.Nearest) ? "   <- nearest" : "")
                    + (d.OnAxis ? "   [on-axis only]" : "") + (d.IsSpecial ? "   [own file, not in blend]" : d.BlendMember ? "" : "   [not in blend]");
                Say(F("  {0,7}  d={1,-6}  F/{2,-8} {3,8} deg  {4}{5}",
                    d.BlendMember ? F("{0:0.0}%", 100.0 * d.BlendWeight) : "--", G(d.Dist, 3), G(d.Fno, 4), G(d.Hfov, 4), d.Name, tags));
            }
        }

        // Where does the requested pair sit among the inputs' pairs?
        // Nearest input (same distance as the weights) and whether the pair is inside
        // the convex hull of the inputs' points (same scaled axes). A request equal to
        // an input is inside by definition. Outside the hull, or farther than FarDist
        // from every input = 2D extrapolation, so we warn.
        static PairInfo PairCheck(List<Design> group, double fno, double hfov)
        {
            var p = new PairInfo();
            foreach (var d in group)
            {
                double dist = NormDist(d.Fno, d.Hfov, fno, hfov);
                if (dist < p.Dist) { p.Dist = dist; p.Nearest = d; }
                if (RawDist(d.Fno, d.Hfov, fno, hfov) < 1e-9) p.Exact = true;
            }
            var pts = group.Where(d => d.Fno > 0)
                .Select(d => new[] { Math.Log(d.Fno, 2.0) / FnoScale, d.Hfov / FieldScale }).ToList();
            p.InsideHull = p.Exact || (fno > 0 && InsideHull(pts, Math.Log(fno, 2.0) / FnoScale, hfov / FieldScale));
            p.Warn = !p.Exact && (!p.InsideHull || p.Dist > FarDist);
            p.Text = F("Request F/{0} at {1} deg: nearest input {2} (F/{3}, {4} deg) at distance {5}; "
                + "inside the inputs' F/#-field hull: {6}{7}",
                G(fno, 4), G(hfov, 4), p.Nearest == null ? "n/a" : p.Nearest.Name,
                p.Nearest == null ? "nan" : G(p.Nearest.Fno, 4), p.Nearest == null ? "nan" : G(p.Nearest.Hfov, 4),
                G(p.Dist, 3), p.InsideHull ? "yes" : "no", p.Exact ? " (matches an input exactly)" : "");
            return p;
        }

        // Point-in-convex-hull test in 2D (hull by the monotone-chain method).
        // Repeated points are dropped first. A hull that collapses to a line or a
        // point only "contains" points on it.
        static bool InsideHull(List<double[]> pts, double x, double y)
        {
            const double tol = 1e-9;
            Func<double[], double[], double[], double> cross = (o, a, b) =>
                (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
            var P = new List<double[]>();
            foreach (var q in pts.OrderBy(q => q[0]).ThenBy(q => q[1]))
                if (P.Count == 0 || Math.Abs(P[P.Count - 1][0] - q[0]) > tol || Math.Abs(P[P.Count - 1][1] - q[1]) > tol) P.Add(q);
            if (P.Count == 0) return false;
            var hull = new List<double[]>();
            // Lower edge left to right, then upper edge right to left (counter-clockwise).
            foreach (var q in P)
            {
                while (hull.Count >= 2 && cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= tol) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            int lower = hull.Count + 1;
            for (int i = P.Count - 2; i >= 0; i--)
            {
                var q = P[i];
                while (hull.Count >= lower && cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= tol) hull.RemoveAt(hull.Count - 1);
                hull.Add(q);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);  // last point repeats the first
            var r = new[] { x, y };
            if (hull.Count == 1) return Math.Abs(hull[0][0] - x) < 1e-6 && Math.Abs(hull[0][1] - y) < 1e-6;
            if (hull.Count == 2)
            {
                // Degenerate (all inputs on one line): on the segment only.
                double[] a = hull[0], b = hull[1];
                double len2 = Math.Pow(b[0] - a[0], 2) + Math.Pow(b[1] - a[1], 2);
                if (len2 < 1e-18) return Math.Abs(a[0] - x) < 1e-6 && Math.Abs(a[1] - y) < 1e-6;
                double tpar = ((x - a[0]) * (b[0] - a[0]) + (y - a[1]) * (b[1] - a[1])) / len2;
                return Math.Abs(cross(a, b, r)) < 1e-6 && tpar >= -1e-9 && tpar <= 1 + 1e-9;
            }
            for (int i = 0; i < hull.Count; i++)
                if (cross(hull[i], hull[(i + 1) % hull.Count], r) < -1e-9) return false;
            return true;
        }

        // The "what job?" window (ribbon / interactive use): F/#, half field and EFL,
        // prefilled with the folder's medians and showing each min..max. Out-of-range
        // values are refused here unless "clamp" is ticked; a 2D-extrapolation pair asks
        // "continue?". Cancel = exit 2. The answers go into Opts like command-line flags.
        static void AskTargets(List<Design> group, Envelope env)
        {
            var form = new WF.Form
            {
                Text = "StartPointFinder: F/# and field of view",
                FormBorderStyle = WF.FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                StartPosition = WF.FormStartPosition.CenterScreen,
                ClientSize = new Size(560, 420), TopMost = true,
            };
            using (form)
            {
                form.Controls.Add(new WF.Label
                {
                    Left = 12, Top = 10, Width = 536, Height = 34,
                    Text = F("{0} designs share one layout. Type the job you want; leave a box as it is to use the folder's median.", group.Count),
                });
                // One row per number: name, box (prefilled), and the folder's range.
                string[] names = { "F/# (image space)", "Half field of view (deg)", "Focal length EFL (lens units)" };
                double[][] spans = { env.Fno, env.Hfov, env.Efl };
                double?[] given = { Opts.Fno, Opts.Fov, Opts.Efl };
                var boxes = new WF.TextBox[3];
                var prefill = new string[3];
                for (int i = 0; i < 3; i++)
                {
                    int top = 52 + 32 * i;
                    form.Controls.Add(new WF.Label { Left = 12, Top = top + 3, Width = 180, Text = names[i] });
                    prefill[i] = (given[i] ?? spans[i][1]).ToString("G10", CultureInfo.InvariantCulture);
                    boxes[i] = new WF.TextBox { Left = 196, Top = top, Width = 90, Text = prefill[i] };
                    form.Controls.Add(boxes[i]);
                    form.Controls.Add(new WF.Label
                    {
                        Left = 296, Top = top + 3, Width = 252,
                        Text = F("inputs {0} .. {1} (median {2})", G(spans[i][0], 5), G(spans[i][2], 5), G(spans[i][1], 5)),
                    });
                }
                var clamp = new WF.CheckBox { Left = 12, Top = 150, Width = 536, Checked = Opts.Clamp,
                    Text = "Clamp out-of-range values to the nearest edge (otherwise they are refused)" };
                form.Controls.Add(clamp);
                form.Controls.Add(new WF.Label { Left = 12, Top = 183, Width = 180, Text = "Start from" });
                var start = new WF.ComboBox { Left = 196, Top = 180, Width = 352, DropDownStyle = WF.ComboBoxStyle.DropDownList };
                start.Items.Add("best of all candidates (default)");
                start.Items.Add("nearest input design");
                start.Items.Add("blend of the inputs (closest count most)");
                start.SelectedIndex = Opts.Start == "nearest" ? 1 : Opts.Start == "blend" ? 2 : 0;
                form.Controls.Add(start);
                // The inputs' own pairs, so the user can see where designs exist.
                var list = new WF.ListBox { Left = 12, Top = 214, Width = 536, Height = 150, Font = new Font(FontFamily.GenericMonospace, 8.5f) };
                foreach (var d in group.OrderBy(d => d.Fno).ThenBy(d => d.Hfov))
                    list.Items.Add(F("F/{0,-7} {1,7} deg  EFL {2,-9} {3}", G(d.Fno, 4), G(d.Hfov, 4), G(d.Efl, 5), d.Name));
                form.Controls.Add(list);
                var ok = new WF.Button { Text = "OK", Left = 384, Top = 378, Width = 78 };
                var cancel = new WF.Button { Text = "Cancel", Left = 470, Top = 378, Width = 78, DialogResult = WF.DialogResult.Cancel };
                form.Controls.Add(ok); form.Controls.Add(cancel);
                form.AcceptButton = ok; form.CancelButton = cancel;

                var picked = new double[3];
                ok.Click += (sender, e) =>
                {
                    // Read each box; an untouched box means "exactly the median".
                    for (int i = 0; i < 3; i++)
                    {
                        string txt = boxes[i].Text.Trim();
                        double v;
                        if (txt == prefill[i]) v = given[i] ?? spans[i][1];
                        else if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                                 && !double.TryParse(txt, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                        {
                            WF.MessageBox.Show(form, names[i] + ": '" + txt + "' is not a number.", Tool);
                            return;
                        }
                        bool inside = InRange(v, spans[i]);
                        if (!inside && !clamp.Checked)
                        {
                            WF.MessageBox.Show(form, F("{0} = {1} is outside the folder's range {2} .. {3}.\nType a value inside it, or tick Clamp.",
                                names[i], G(v), G(spans[i][0], 5), G(spans[i][2], 5)), Tool);
                            return;
                        }
                        picked[i] = Math.Min(Math.Max(v, spans[i][0]), spans[i][2]);
                    }
                    if (picked[0] <= 0 || picked[2] <= 0) { WF.MessageBox.Show(form, "F/# and EFL must be positive.", Tool); return; }
                    // 2D check: each value may be in range while the pair is not.
                    var pc = PairCheck(group, picked[0], picked[1]);
                    if (pc.Warn && WF.MessageBox.Show(form, pc.Text + "\n\nNo input was made for this F/# + field combination, "
                            + "so the start is an extrapolation. Continue?", Tool, WF.MessageBoxButtons.YesNo) != WF.DialogResult.Yes)
                        return;
                    form.DialogResult = WF.DialogResult.OK;
                };
                if (form.ShowDialog() != WF.DialogResult.OK) throw new ToolExitException(2, "cancelled in the F/# and field window");
                Opts.Fno = picked[0]; Opts.Fov = picked[1]; Opts.Efl = picked[2];
                Opts.Clamp = clamp.Checked;
                Opts.Start = start.SelectedIndex == 1 ? "nearest" : start.SelectedIndex == 2 ? "blend" : "best";
                Say(F("Window: F/{0}  half-FOV {1} deg  EFL {2}  start {3}{4}", G(picked[0]), G(picked[1]), G(picked[2]),
                    Opts.Start, Opts.Clamp ? "  (clamp on)" : ""));
            }
        }

        // Fences for the optimizer, measured from the inputs at focal length 1:
        // glass no thinner than 80% of the thinnest input glass, no thicker than
        // 120% of the thickest; air the same idea (with a tiny floor).
        static Bounds ThicknessBounds(List<Design> group)
        {
            var glass = new List<double>(); var air = new List<double>(); var bfl = new List<double>();
            foreach (var d in group)
            {
                for (int i = 0; i < d.Surfs.Count - 1; i++)  // last gap = focus distance, own rule
                    (d.Surfs[i].Glass.Length > 0 ? glass : air).Add(d.Surfs[i].Thick / d.Efl);
                bfl.Add(d.Surfs[d.Surfs.Count - 1].Thick / d.Efl);
            }
            return new Bounds
            {
                GlassMin = 0.8 * glass.Min(),
                GlassMax = 1.2 * glass.Max(),
                AirMin = air.Count > 0 ? Math.Max(0.002, 0.8 * air.Min()) : 0.002,
                AirMax = 1.2 * air.Concat(bfl).Max(),
            };
        }

        // ---------------------------------------------------------------
        // Step 5: build the start shape
        // ---------------------------------------------------------------

        // Weighted average of the shapes (blend members only, closeness weights).
        // Each input is first scaled to focal length 1 (curvature
        // times EFL, thickness divided by EFL), then we take the weighted mean surface
        // by surface. Glass cannot be averaged, so we pick a real glass that the inputs
        // used at that spot.
        static List<SurfRx> StartShape(List<Design> group, out List<string> notes)
        {
            notes = new List<string>();
            var outRx = new List<SurfRx>();
            int n = group[0].Surfs.Count;
            for (int j = 0; j < n; j++)
            {
                var s = new SurfRx
                {
                    Curv = group.Sum(d => d.Weight * d.Surfs[j].Curv * d.Efl),
                    Thick = group.Sum(d => d.Weight * d.Surfs[j].Thick / d.Efl),
                    Conic = group.Sum(d => d.Weight * d.Surfs[j].Conic),
                };
                if (group[0].Surfs[j].Glass.Length > 0)
                {
                    string glass, cat, note;
                    ChooseGlass(group, j, out glass, out cat, out note);
                    s.Glass = glass; s.Catalog = cat;
                    notes.Add(F("S{0} glass: {1}", j + 1, note));
                }
                outRx.Add(s);
            }
            return outRx;
        }

        // One glass the inputs used at a spot, with its nd/Vd and how much that input counts.
        class GlassCand { public string Glass, Catalog; public double Nd, Vd, W; }

        // Pick one input glass for surface j (closest to the average nd/Vd, or most used).
        static void ChooseGlass(List<Design> group, int j, out string glass, out string catalog, out string note)
        {
            int surfNo = j + 1;
            var cands = new List<GlassCand>();
            foreach (var d in group)
            {
                double[] nv;
                if (!d.NdVd.TryGetValue(surfNo, out nv)) nv = new[] { double.NaN, double.NaN };
                cands.Add(new GlassCand { Glass = d.Surfs[j].Glass, Catalog = d.Surfs[j].Catalog, Nd = nv[0], Vd = nv[1], W = d.Weight });
            }
            var good = cands.Where(c => IsFinite(c.Nd) && IsFinite(c.Vd)).ToList();
            double ndBar = double.NaN, vdBar = double.NaN;
            if (good.Count > 0)
            {
                double ws = good.Sum(c => c.W);
                ndBar = good.Sum(c => c.Nd * c.W) / ws;
                vdBar = good.Sum(c => c.Vd * c.W) / ws;
            }
            // 0.02 in nd and 5 in Vd count as "one step" apart.
            Func<GlassCand, double> dist = c =>
                (IsFinite(c.Nd) && IsFinite(c.Vd) && IsFinite(ndBar))
                    ? Math.Sqrt(Math.Pow((c.Nd - ndBar) / 0.02, 2) + Math.Pow((c.Vd - vdBar) / 5.0, 2)) : 1e9;
            GlassCand pick;
            if (Opts.Glass == "majority" || good.Count == 0)
            {
                var votes = cands.GroupBy(c => c.Glass + "|" + c.Catalog).ToDictionary(gr => gr.Key, gr => gr.Sum(c => c.W));
                double top = votes.Values.Max();
                pick = cands.Where(c => Math.Abs(votes[c.Glass + "|" + c.Catalog] - top) < 1e-12).OrderBy(dist).First();
            }
            else pick = cands.OrderBy(dist).First();
            glass = pick.Glass; catalog = pick.Catalog;
            note = F("{0} ({1}) nd={2} Vd={3}; weighted mean nd={4} Vd={5}",
                pick.Glass, pick.Catalog, G(pick.Nd, 5), G(pick.Vd, 4), G(ndBar, 5), G(vdBar, 4));
        }

        // Make a brand-new lens from the normalized recipe (focal length 1), sized to
        // the target: entrance pupil = EFL / F#, angle fields 0, 0.7, 1 x FOV, F d C
        // wavelengths, the group's object distance. Then nudge the size so EFL is
        // exact and focus paraxially.
        static void BuildSystem(ZOSAPI.IOpticalSystem S, List<SurfRx> rx, int stop, Target t, List<string> catalogs, string title)
        {
            S.New(false);
            var sd = S.SystemData;
            sd.Aperture.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter;
            sd.Aperture.ApertureValue = t.Efl / t.Fno;
            sd.Fields.SetFieldType(ZOSAPI.SystemData.FieldType.Angle);
            sd.Fields.GetField(1).Y = 0.0;
            if (t.Hfov > 1e-6)
            {
                sd.Fields.AddField(0.0, 0.7 * t.Hfov, 1.0);
                sd.Fields.AddField(0.0, t.Hfov, 1.0);
            }
            sd.Wavelengths.SelectWavelengthPreset(ZOSAPI.SystemData.WavelengthPreset.FdC_Visible);
            foreach (var cat in catalogs)
            {
                try { if (!sd.MaterialCatalogs.IsCatalogInUse(cat)) sd.MaterialCatalogs.AddCatalog(cat); } catch { }
            }
            try { sd.TitleNotes.Title = title; } catch { }
            var lde = S.LDE;
            while (lde.NumberOfSurfaces < rx.Count + 2) lde.InsertNewSurfaceAt(lde.NumberOfSurfaces - 1);
            for (int i = 1; i <= rx.Count; i++)
            {
                var surf = lde.GetSurfaceAt(i);
                double c = rx[i - 1].Curv / t.Efl;  // back from "focal length 1" to real size
                surf.Radius = Math.Abs(c) > 1e-12 ? 1.0 / c : double.PositiveInfinity;
                surf.Thickness = rx[i - 1].Thick * t.Efl;
                surf.Conic = rx[i - 1].Conic;
                surf.Material = rx[i - 1].Glass;
            }
            lde.GetSurfaceAt(stop).IsStop = true;
            if (IsFinite(t.Obj)) lde.GetSurfaceAt(0).Thickness = t.Obj;

            // Glass and wavelengths shift EFL a little; one pure re-scale fixes it exactly.
            for (int pass = 0; pass < 2; pass++)
            {
                double efl = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, PrimaryWave(S));
                if (!IsFinite(efl) || efl <= 0) break;
                double k = t.Efl / efl;
                if (Math.Abs(k - 1.0) < 1e-9) break;
                for (int i = 1; i < lde.NumberOfSurfaces - 1; i++)
                {
                    var surf = lde.GetSurfaceAt(i);
                    if (IsFinite(surf.Radius) && Math.Abs(surf.Radius) < 1e10) surf.Radius *= k;
                    surf.Thickness *= k;
                }
            }
            ParaxialFocus(S);
        }

        // Start from a design's OWN file (for Even Asphere / Zernike / Paraxial
        // surfaces, which a plain recipe cannot carry): open it (read only), drop its
        // variables, extra configurations and merit rows, scale it with OpticStudio's
        // Scale Lens tool (asphere terms scale too) to the target EFL, then give it
        // the same pupil, fields, wavelengths and object distance as every other
        // candidate, fix the EFL exactly and focus paraxially.
        static void BuildNative(ZOSAPI.IOpticalSystem S, Design d, Target t, string title)
        {
            if (!S.LoadFile(d.Path, false)) throw new Exception("could not reload " + d.Name);
            try { if (S.MCE.NumberOfConfigurations > 1) S.MCE.MakeSingleConfiguration(); } catch { }
            try { S.Tools.RemoveAllVariables(); } catch { }
            try { S.MFE.DeleteAllRows(); } catch { }
            ScaleBy(S, t.Efl / d.Efl);
            Retarget(S, t);
            try { S.SystemData.TitleNotes.Title = title; } catch { }
            // New wavelengths shift EFL a little; a pure re-scale fixes it exactly.
            for (int pass = 0; pass < 2; pass++)
            {
                double efl = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, PrimaryWave(S));
                if (!IsFinite(efl) || efl <= 0) break;
                double k = t.Efl / efl;
                if (Math.Abs(k - 1.0) < 1e-9) break;
                ScaleBy(S, k);
                Retarget(S, t);
            }
            ParaxialFocus(S);
        }

        // Scale the whole lens by k (OpticStudio's Scale Lens tool).
        static void ScaleBy(ZOSAPI.IOpticalSystem S, double k)
        {
            var sc = S.Tools.OpenScale();
            if (sc == null) throw new Exception("could not open the Scale Lens tool");
            try
            {
                sc.ScaleByFactor = true;
                sc.ScaleFactor = k;
                sc.RunAndWaitForCompletion();
            }
            finally { sc.Close(); }
        }

        // Same job as every candidate: EPD = EFL / F#, angle fields 0 / 0.7 / 1 x FOV
        // (no vignetting factors), F d C wavelengths, held object distance.
        static void Retarget(ZOSAPI.IOpticalSystem S, Target t)
        {
            var sd = S.SystemData;
            sd.Aperture.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.EntrancePupilDiameter;
            sd.Aperture.ApertureValue = t.Efl / t.Fno;
            var f = sd.Fields;
            f.SetFieldType(ZOSAPI.SystemData.FieldType.Angle);
            while (f.NumberOfFields > 1) f.RemoveField(f.NumberOfFields);
            var f1 = f.GetField(1);
            f1.X = 0.0; f1.Y = 0.0; f1.Weight = 1.0;
            if (t.Hfov > 1e-6)
            {
                f.AddField(0.0, 0.7 * t.Hfov, 1.0);
                f.AddField(0.0, t.Hfov, 1.0);
            }
            try { f.ClearVignetting(); } catch { }
            sd.Wavelengths.SelectWavelengthPreset(ZOSAPI.SystemData.WavelengthPreset.FdC_Visible);
            if (IsFinite(t.Obj)) S.LDE.GetSurfaceAt(0).Thickness = t.Obj;
        }

        // Put the image where the paraxial edge ray crosses the axis, then freeze that number.
        static void ParaxialFocus(ZOSAPI.IOpticalSystem S)
        {
            var lde = S.LDE;
            var cell = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2).ThicknessCell;
            try
            {
                var solve = cell.CreateSolveType(ZOSAPI.Editors.SolveType.MarginalRayHeight);
                cell.SetSolveData(solve);  // height 0 at pupil zone 0 = paraxial focus
                double unused = lde.GetSurfaceAt(lde.NumberOfSurfaces - 2).Thickness;
                cell.MakeSolveFixed();     // keep the number, drop the solve
            }
            catch (Exception ex) { Say("  WARNING: paraxial focus solve failed: " + ex.Message); }
        }

        // Slide the image plane to the smallest RMS spot (OpticStudio Quick Focus).
        static void QuickFocus(ZOSAPI.IOpticalSystem S)
        {
            ZOSAPI.Tools.General.IQuickFocus qf = null;
            try
            {
                qf = S.Tools.OpenQuickFocus();
                if (qf == null) return;
                try { qf.Criterion = ZOSAPI.Tools.General.QuickFocusCriterion.SpotSizeRadial; qf.UseCentroid = true; } catch { }
                qf.RunAndWaitForCompletion();
            }
            catch { }
            finally { try { if (qf != null) qf.Close(); } catch { } }
        }

        // ---------------------------------------------------------------
        // Merit function ("report card") + optimization
        // ---------------------------------------------------------------

        // The report card: OpticStudio default RMS spot (centroid, Gaussian quadrature)
        // with glass/air thickness fences taken from the inputs, plus
        //   EFFL = target focal length (keeps the size right),
        //   TOTR between the shortest and longest input track (scaled),
        //   ISFN shown with weight 0 (F/# is held by fixed pupil + fixed EFL).
        static void BuildMerit(ZOSAPI.IOpticalSystem S, Target t, Envelope env, Bounds b)
        {
            var mfe = S.MFE;
            var wiz = mfe.SEQOptimizationWizard2;
            wiz.ResetSettings();
            wiz.Criterion = ZOSAPI.Wizards.CriterionTypes.Spot;
            wiz.Type = ZOSAPI.Wizards.OptimizationTypes.RMS;
            wiz.Reference = ZOSAPI.Wizards.ReferenceTypes.Centroid;
            wiz.UseGaussianQuadrature = true;
            wiz.UseAllFields = true;
            wiz.AssumeAxialSymmetry = true;
            wiz.AddFavoriteOperands = false;
            wiz.UseGlassBoundaryValues = true;
            wiz.GlassMin = b.GlassMin * t.Efl;
            wiz.GlassMax = b.GlassMax * t.Efl;
            wiz.GlassEdgeThickness = 0.5 * b.GlassMin * t.Efl;
            wiz.UseAirBoundaryValues = true;
            wiz.AirMin = b.AirMin * t.Efl;
            wiz.AirMax = b.AirMax * t.Efl;
            wiz.AirEdgeThickness = 0.0;
            wiz.OptimizeForBestNominalPerformance = true;
            wiz.OptimizeForManufacturingYield = false;
            wiz.Apply();

            AddRow(mfe, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, t.Efl, 1.0, 0, PrimaryWave(S));
            AddRow(mfe, ZOSAPI.Editors.MFE.MeritOperandType.ISFN, t.Fno, 0.0);
            int trackRow = AddRow(mfe, ZOSAPI.Editors.MFE.MeritOperandType.TOTR, 0.0, 0.0);
            AddRow(mfe, ZOSAPI.Editors.MFE.MeritOperandType.OPLT, env.TrackRatio[2] * t.Efl, 1.0, trackRow);
            AddRow(mfe, ZOSAPI.Editors.MFE.MeritOperandType.OPGT, env.TrackRatio[0] * t.Efl, 1.0, trackRow);
        }

        // Add one line to the report card and return its row number.
        static int AddRow(ZOSAPI.Editors.MFE.IMeritFunctionEditor mfe, ZOSAPI.Editors.MFE.MeritOperandType type,
            double target, double weight, int p1 = 0, int p2 = 0)
        {
            mfe.AddOperand();
            int row = mfe.NumberOfOperands;
            var o = mfe.GetOperandAt(row);
            o.ChangeType(type);
            SetIntCell(o, ZOSAPI.Editors.MFE.MeritColumn.Param1, p1);
            SetIntCell(o, ZOSAPI.Editors.MFE.MeritColumn.Param2, p2);
            o.Target = target;
            o.Weight = weight;
            return row;
        }

        static void SetIntCell(ZOSAPI.Editors.MFE.IMFERow o, ZOSAPI.Editors.MFE.MeritColumn col, int v)
        {
            if (v == 0) return;
            var cell = o.GetOperandCell(col);
            try { cell.IntegerValue = v; } catch { cell.DoubleValue = v; }
        }

        static double Merit(ZOSAPI.IOpticalSystem S)
        {
            try { return S.MFE.CalculateMeritFunction(); } catch { return double.NaN; }
        }

        // Let the optimizer move every radius (not the flat stop) and every thickness.
        // Asphere terms of own-file starts stay fixed.
        static void MakeVariables(ZOSAPI.IOpticalSystem S, int stop)
        {
            var lde = S.LDE;
            for (int i = 1; i < lde.NumberOfSurfaces - 1; i++)
            {
                var s = lde.GetSurfaceAt(i);
                if (i != stop) { try { s.RadiusCell.MakeSolveVariable(); } catch { } }
                try { s.ThicknessCell.MakeSolveVariable(); } catch { }
            }
        }

        // Damped least squares (DLS) = "walk downhill" on the report card.
        // Run it up to `passes` times (0 = Hammer only); stop early when it barely improves.
        // Optional Hammer = a longer "shake and walk downhill" search, time-limited.
        static double Optimize(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem S, int passes, int hammerSec)
        {
            if (passes > 0)
            {
                var opt = S.Tools.OpenLocalOptimization();
                if (opt == null) throw new Exception("could not open local optimization");
                try
                {
                    opt.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares;
                    opt.Cycles = ZOSAPI.Tools.Optimization.OptimizationCycles.Automatic;
                    double last = opt.InitialMeritFunction;
                    for (int k = 0; k < passes; k++)
                    {
                        if (Terminated(app)) break;
                        opt.RunAndWaitForCompletion();
                        double cur = opt.CurrentMeritFunction;
                        if (!IsFinite(cur) || last - cur < 1e-3 * Math.Max(Math.Abs(last), 1e-12)) break;
                        last = cur;
                    }
                }
                finally { opt.Close(); }
            }
            if (hammerSec > 0 && !Terminated(app))
            {
                var ham = S.Tools.OpenHammerOptimization();
                if (ham != null)
                {
                    try
                    {
                        ham.Algorithm = ZOSAPI.Tools.Optimization.OptimizationAlgorithm.DampedLeastSquares;
                        try { ham.NumberOfCores = ham.MaxCores; } catch { }
                        double before = ham.InitialMeritFunction;
                        ham.RunAndWaitWithTimeout(hammerSec);
                        try { ham.Cancel(); ham.WaitForCompletion(); } catch { }
                        Say(F("  Hammer {0} s: {1} -> {2}", hammerSec, G(before), G(ham.CurrentMeritFunction)));
                    }
                    finally { ham.Close(); }
                }
            }
            return Merit(S);
        }

        // ---------------------------------------------------------------
        // Measure, compare, validate
        // ---------------------------------------------------------------

        // Plain-language score: average RMS spot radius over the fields, in microns (all wavelengths).
        static double MeanSpotUm(ZOSAPI.IOpticalSystem S)
        {
            var fields = S.SystemData.Fields;
            double fmax = 1e-12;
            for (int k = 1; k <= fields.NumberOfFields; k++) fmax = Math.Max(fmax, Math.Abs(fields.GetField(k).Y));
            var vals = new List<double>();
            for (int k = 1; k <= fields.NumberOfFields; k++)
            {
                double hy = fmax > 1e-9 ? fields.GetField(k).Y / fmax : 0.0;
                double v = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.RSCE, 4, 0, 0.0, hy);  // 4 rings, wave 0 = all
                if (IsFinite(v)) vals.Add(v * 1000.0);
            }
            return vals.Count > 0 ? vals.Average() : double.NaN;
        }

        // First-order numbers of whatever lens is loaded now.
        static Dictionary<string, double> Measure(ZOSAPI.IOpticalSystem S, double obj)
        {
            double efl = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, PrimaryWave(S));
            double track = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.TOTR);
            return new Dictionary<string, double>
            {
                { "spot_um", MeanSpotUm(S) },
                { "efl", efl },
                { "fno", Op(S, ZOSAPI.Editors.MFE.MeritOperandType.ISFN) },
                { "hfov", IsFinite(efl) && efl > 0 ? HalfFov(S, efl, obj) : double.NaN },
                { "track", track },
                { "track_ratio", IsFinite(efl) && efl != 0 ? track / efl : double.NaN },
            };
        }

        // The input with the lowest score at the target (reopt score if we have it),
        // counting only inputs whose optimized lens passed the checks when any did.
        static double BestInput(List<Design> group, out string name)
        {
            name = "";
            double best = double.NaN;
            // Inputs whose optimized lens passed the checks count first; if none did, all count.
            bool anyPass = group.Any(d => d.ReoptPass && IsFinite(d.MfReopt));
            // With the top-N screen some inputs are never optimized; their refocus-only score
            // is not comparable with optimized scores, so they only count if nothing was optimized.
            bool anyOpt = group.Any(d => IsFinite(d.MfReopt));
            foreach (var d in group)
            {
                if (anyPass && !d.ReoptPass) continue;
                if (anyOpt && !IsFinite(d.MfReopt)) continue;
                double v = IsFinite(d.MfReopt) ? d.MfReopt : d.MfRefocus;
                if (IsFinite(v) && (!IsFinite(best) || v < best)) { best = v; name = d.Name; }
            }
            return best;
        }

        // How far a curved surface bulges at height y (standard conic sag formula).
        static double Sag(double c, double k, double y)
        {
            double arg = 1.0 - (1.0 + k) * c * c * y * y;
            if (arg < 0) return double.NaN;
            return c * y * y / (1.0 + Math.Sqrt(arg));
        }

        // Sag with even-asphere terms added (pars = Par1..Par8: a1 r^2 + a2 r^4 + ...).
        // Zernike terms are left out (good enough for edge checks and the picture).
        static double SagAt(double c, double k, double[] pars, double y)
        {
            double z = Sag(c, k, y);
            if (pars != null && IsFinite(z))
                for (int j = 0; j < 8; j++)
                    if (IsFinite(pars[j])) z += pars[j] * Math.Pow(y, 2 * (j + 1));
            return z;
        }

        // Curvature = 1 / radius; a flat surface (infinite radius) has curvature 0.
        static double Curv(double r) => (!IsFinite(r) || Math.Abs(r) > 1e10 || r == 0) ? 0.0 : 1.0 / r;

        // Check the result honestly: EFL and F/# within 1% of target, field equal to
        // target, track/EFL inside the inputs' range (2% slack), glass at least 95% of
        // the center fence, glass edges not paper-thin, no colliding air gaps, sane score.
        static List<Tuple<string, bool>> Validate(ZOSAPI.IOpticalSystem S, Dictionary<string, double> fo,
            Target t, Envelope env, Bounds b, double mf, out bool pass)
        {
            bool? edgeOk;
            return Validate(S, fo, t, env, b, mf, out pass, out edgeOk);
        }

        // Same checks, and also says whether the pupil-edge rays traced (null = not checked).
        static List<Tuple<string, bool>> Validate(ZOSAPI.IOpticalSystem S, Dictionary<string, double> fo,
            Target t, Envelope env, Bounds b, double mf, out bool pass, out bool? edgeOk)
        {
            var items = new List<Tuple<string, bool>>();
            items.Add(Tuple.Create(F("EFL {0} vs target {1} (1%)", G(fo["efl"]), G(t.Efl)),
                IsFinite(fo["efl"]) && Math.Abs(fo["efl"] / t.Efl - 1) <= 0.01));
            items.Add(Tuple.Create(F("F/# {0} vs target {1} (1%)", G(fo["fno"]), G(t.Fno)),
                IsFinite(fo["fno"]) && Math.Abs(fo["fno"] / t.Fno - 1) <= 0.01));
            items.Add(Tuple.Create(F("half-FOV {0} vs target {1}", G(fo["hfov"]), G(t.Hfov)),
                IsFinite(fo["hfov"]) && Math.Abs(fo["hfov"] - t.Hfov) <= 1e-6 + 1e-3 * t.Hfov));
            double lo = env.TrackRatio[0], hi = env.TrackRatio[2];
            items.Add(Tuple.Create(F("track/EFL {0} inside {1}..{2} (2% slack)", G(fo["track_ratio"]), G(lo), G(hi)),
                IsFinite(fo["track_ratio"]) && fo["track_ratio"] >= lo * 0.98 && fo["track_ratio"] <= hi * 1.02));
            var lde = S.LDE;
            int n = lde.NumberOfSurfaces;
            double minGlass = double.PositiveInfinity, minGlassEdge = double.PositiveInfinity, minAirEdge = double.PositiveInfinity;
            int nGlass = 0, nAir = 0;
            var badSag = new List<string>();
            var badSd = new List<string>();
            var pars = new Dictionary<int, double[]>();
            Func<int, double[]> terms = i => { if (!pars.ContainsKey(i)) pars[i] = AsphereTerms(lde.GetSurfaceAt(i)); return pars[i]; };
            // A strong conic has no sag past y = 1 / (|c| sqrt(1 + k)); say where that cuts a clear aperture.
            for (int i = 1; i < n - 1; i++)
            {
                var s = lde.GetSurfaceAt(i);
                double cv = Curv(s.Radius), kk = s.Conic;
                // No semi-diameter at all means OpticStudio could not trace the aperture there.
                if (!IsFinite(s.SemiDiameter)) { badSd.Add("S" + i); continue; }
                if (!IsFinite(Sag(cv, kk, s.SemiDiameter)))
                    badSag.Add(F("S{0} (beyond y={1} of semi-diameter {2})", i, G(1.0 / (Math.Abs(cv) * Math.Sqrt(1.0 + kk)), 4), G(s.SemiDiameter, 4)));
            }
            for (int i = 1; i < n - 2; i++)
            {
                var a = lde.GetSurfaceAt(i);
                var c = lde.GetSurfaceAt(i + 1);
                bool isGlass = (a.Material ?? "").Trim().Length > 0;
                // Glass edge is at the bigger of the two faces; an air gap where both faces exist.
                double y = isGlass ? Math.Max(a.SemiDiameter, c.SemiDiameter) : Math.Min(a.SemiDiameter, c.SemiDiameter);
                // The smaller face runs flat past its own clear aperture (as OpticStudio draws it).
                double edge = a.Thickness + SagAt(Curv(c.Radius), c.Conic, terms(i + 1), Math.Min(y, c.SemiDiameter))
                    - SagAt(Curv(a.Radius), a.Conic, terms(i), Math.Min(y, a.SemiDiameter));
                if (isGlass) { nGlass++; minGlass = Math.Min(minGlass, a.Thickness); } else nAir++;
                if (!IsFinite(edge)) continue;  // undefined sag: reported by its own check below
                if (isGlass) minGlassEdge = Math.Min(minGlassEdge, edge); else minAirEdge = Math.Min(minAirEdge, edge);
            }
            double gmin = b.GlassMin * t.Efl;
            // No glass or no inner air gap (e.g. a cemented doublet) = nothing to check, said plainly.
            if (nGlass == 0)
                items.Add(Tuple.Create("glass thickness: n/a (no glass elements)", true));
            else
            {
                items.Add(Tuple.Create(F("thinnest glass center {0} >= 95% of fence {1}", G(minGlass), G(gmin)), minGlass >= 0.95 * gmin));
                items.Add(IsFinite(minGlassEdge)
                    ? Tuple.Create(F("thinnest glass edge {0} > 0", G(minGlassEdge)), minGlassEdge > 0)
                    : Tuple.Create("thinnest glass edge: not computable (see the sag check)", true));
            }
            if (nAir == 0)
                items.Add(Tuple.Create("air gaps do not collide: n/a (no internal air gaps)", true));
            else if (!IsFinite(minAirEdge))
                items.Add(Tuple.Create("air gaps do not collide: not computable (see the sag check)", true));
            else
            {
                // Edges that touch within 1e-4 x EFL count as touching, not colliding
                // (the optimizer holds the edge-gap boundary only approximately).
                double touch = 1e-4 * t.Efl;
                items.Add(Tuple.Create(F("air gaps do not collide (min edge gap {0}, tolerance {1})", G(minAirEdge), G(touch, 3)),
                    minAirEdge >= -touch));
            }
            if (badSd.Count > 0)
                items.Add(Tuple.Create("clear aperture not computable at " + string.Join(", ", badSd)
                    + " (OpticStudio returned no semi-diameter; rays likely fail there)", false));
            items.Add(badSag.Count == 0
                ? Tuple.Create("every surface sag is defined across its clear aperture", true)
                : Tuple.Create("surface sag undefined at the clear aperture of " + string.Join(", ", badSag)
                    + " (conic too strong for the semi-diameter)", false));
            items.Add(Tuple.Create(F("merit function finite and < 1e8 ({0})", G(mf)), IsFinite(mf) && mf < 1e8));
            // Pupil-edge rays: the spot rows of the report card can all trace while a ray at
            // the very edge of the pupil still misses a surface (seen on hard cases).
            string edgeText;
            edgeOk = EdgeRayCheck(S, t.Hfov > 1e-6, out edgeText);
            items.Add(Tuple.Create(edgeText, edgeOk != false));
            pass = items.All(x => x.Item2);
            return items;
        }

        // The 7 pupil-edge rays (normalized field Hy, pupil Px, Py), same set as the re-optimization
        // study: top of the pupil on axis, top/bottom at 0.7 and full field, and the side (sagittal)
        // edge at 0.7 and full field. An on-axis-only request traces just the first one.
        static readonly double[][] EdgeRays =
        {
            new[] { 0.0, 0.0, 1.0 }, new[] { 0.7, 0.0, 1.0 }, new[] { 0.7, 0.0, -1.0 }, new[] { 1.0, 0.0, 1.0 },
            new[] { 1.0, 0.0, -1.0 }, new[] { 1.0, 1.0, 0.0 }, new[] { 0.7, 1.0, 0.0 },
        };

        // Trace each pupil-edge ray to the image with a temporary REAY row (weight 0, so
        // the score itself does not change). When a ray misses a surface, OpticStudio
        // cannot compute the report card at all, which is how a failed ray shows up here.
        // The rows are removed again, so the saved lens keeps its own report card.
        // Returns true / false, or null when the report card already fails without the
        // extra rays (then the merit-function check has already failed).
        static bool? EdgeRayCheck(ZOSAPI.IOpticalSystem S, bool hasField, out string text)
        {
            var rays = hasField ? EdgeRays : EdgeRays.Take(1).ToArray();
            double baseMf = Merit(S);
            if (!(IsFinite(baseMf) && baseMf < 1e8))
            {
                text = "edge rays: not checked (the report card itself cannot be computed)";
                return null;
            }
            var mfe = S.MFE;
            int img = S.LDE.NumberOfSurfaces - 1, wave = PrimaryWave(S);
            var failed = new List<string>();
            foreach (var r in rays)
            {
                mfe.AddOperand();
                int row = mfe.NumberOfOperands;
                var o = mfe.GetOperandAt(row);
                o.ChangeType(ZOSAPI.Editors.MFE.MeritOperandType.REAY);
                o.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param1).IntegerValue = img;
                o.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param2).IntegerValue = wave;
                o.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param4).DoubleValue = r[0];
                o.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param5).DoubleValue = r[1];
                o.GetOperandCell(ZOSAPI.Editors.MFE.MeritColumn.Param6).DoubleValue = r[2];
                o.Weight = 0.0;
                double m = Merit(S);
                mfe.RemoveOperandAt(row);
                if (!(IsFinite(m) && m < 1e8)) failed.Add(F("Hy {0} P({1}, {2})", G(r[0]), G(r[1]), G(r[2])));
            }
            Merit(S);  // recompute with the original rows only
            if (failed.Count > 0)
            {
                text = F("edge rays trace at every field ({0} of {1} fail: {2})", failed.Count, rays.Length, string.Join(", ", failed));
                return false;
            }
            text = F("edge rays trace at every field ({0} of {1} pupil-edge rays)", rays.Length, rays.Length);
            return true;
        }

        // ---------------------------------------------------------------
        // Output files
        // ---------------------------------------------------------------

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // JSON helpers: numbers (NaN becomes null) and quoted text.
        static string J(double x) => IsFinite(x) ? x.ToString("R", CultureInfo.InvariantCulture) : "null";
        static string Js(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // Write the first-order CSV (inputs vs result) and a small JSON summary.
        static void WriteTables(Dictionary<string, string> outs, List<Design> designs, Envelope env, Target t,
            double mfStart, double mfResult, Dictionary<string, double> fo, List<string> checks, bool pass = false,
            PairInfo pair = null, double blendMf = double.NaN, List<Cand> cands = null, Cand chosen = null, double secs = double.NaN)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outs["csv"]));
            var sb = new StringBuilder();
            sb.AppendLine("role,name,status,reason,efl,fno,hfov_deg,track,track_over_efl,weight,dist_norm,mf_scaled_refocus,mf_scaled_reopt,spot_um,obj_dist,surfaces,blend_member,on_axis");
            foreach (var d in designs)
                sb.AppendLine(string.Join(",", "input", Csv(d.Name), d.Ok ? "kept" : "skipped", Csv(d.Ok ? d.Note : d.Reason),
                    G(d.Efl, 8), G(d.Fno), G(d.Hfov), G(d.Track, 8), G(d.TrackRatio),
                    d.Ok ? G(d.BlendWeight, 4) : "", d.Ok ? G(d.Dist, 4) : "", d.Ok ? G(d.MfRefocus, 8) : "", d.Ok ? G(d.MfReopt, 8) : "",
                    d.Ok ? G(d.SpotReoptUm, 5) : "", ObjCsv(d.ObjDist), Csv(d.SpecialNote),
                    d.Ok ? (d.BlendMember ? "yes" : "no") : "", d.Ok ? (d.OnAxis ? "yes" : "no") : ""));
            if (env != null)
            {
                string[] roles = { "envelope_min", "envelope_median", "envelope_max" };
                for (int i = 0; i < 3; i++)
                    sb.AppendLine(string.Join(",", roles[i], "", "", "", G(env.Efl[i], 8), G(env.Fno[i]), G(env.Hfov[i]), "",
                        G(env.TrackRatio[i]), "", "", "", "", "", "", "", "", ""));
            }
            if (t != null)
                sb.AppendLine(string.Join(",", "target", "", "", "", G(t.Efl, 8), G(t.Fno), G(t.Hfov), "", "", "", "", "", "", "",
                    ObjCsv(t.Obj), "", "", ""));
            if (cands != null)
                foreach (var c in cands.Where(c => c.Kind == "blend"))
                    sb.AppendLine(string.Join(",", "candidate_blend", Csv(c.Label), c.Optimized ? "optimized" : "failed", Csv(c.Note),
                        "", "", "", "", "", "", "", G(c.MfStart, 8), G(c.Mf, 8), G(c.Spot, 5), "", "", "", ""));
            if (fo != null)
            {
                sb.AppendLine(string.Join(",", "start", Tool + "_start.zmx", Opts.Start, Csv(chosen == null ? "" : chosen.Label),
                    "", "", "", "", "", "", "", G(mfStart, 8), "", "", "", "", "", ""));
                sb.AppendLine(string.Join(",", "result", Tool + "_result.zmx", pass ? "PASS" : "FAIL", Csv(chosen == null ? "" : "start: " + chosen.Label),
                    G(fo["efl"], 8), G(fo["fno"]), G(fo["hfov"]), G(fo["track"], 8), G(fo["track_ratio"]), "", "", "",
                    G(mfResult, 8), G(fo["spot_um"], 5), "", "", "", ""));
            }
            File.WriteAllText(outs["csv"], sb.ToString(), Encoding.UTF8);

            var js = new StringBuilder();
            js.AppendLine("{");
            js.AppendLine("  \"tool\": \"" + Tool + "\",");
            js.AppendLine("  \"inputs\": [");
            for (int i = 0; i < designs.Count; i++)
            {
                var d = designs[i];
                js.Append("    {\"name\": " + Js(d.Name) + ", \"kept\": " + (d.Ok ? "true" : "false")
                    + ", \"reason\": " + Js(d.Reason) + ", \"skip_category\": " + Js(d.Cat) + ", \"signature\": " + Js(d.Signature)
                    + ", \"efl\": " + J(d.Efl) + ", \"fno\": " + J(d.Fno) + ", \"hfov_deg\": " + J(d.Hfov)
                    + ", \"track\": " + J(d.Track) + ", \"object_distance\": " + J(d.ObjDist)
                    + ", \"surfaces\": " + Js(d.SpecialNote) + ", \"blend_member\": " + (d.Ok && d.BlendMember ? "true" : "false")
                    + ", \"on_axis\": " + (d.Ok && d.OnAxis ? "true" : "false")
                    + ", \"weight\": " + J(d.BlendWeight) + ", \"dist_norm\": " + J(d.Dist)
                    + ", \"mf_scaled_refocus\": " + J(d.MfRefocus) + ", \"mf_scaled_reopt\": " + J(d.MfReopt)
                    + ", \"spot_scaled_reopt_um\": " + J(d.SpotReoptUm) + ", \"note\": " + Js(d.Note) + "}");
                js.AppendLine(i < designs.Count - 1 ? "," : "");
            }
            js.AppendLine("  ],");
            if (env != null)
                js.AppendLine("  \"envelope\": {\"efl\": [" + string.Join(", ", env.Efl.Select(J)) + "], \"fno\": ["
                    + string.Join(", ", env.Fno.Select(J)) + "], \"hfov\": [" + string.Join(", ", env.Hfov.Select(J))
                    + "], \"track_ratio\": [" + string.Join(", ", env.TrackRatio.Select(J)) + "]},");
            if (env != null)
                js.AppendLine("  \"distance_scales\": {\"log2_fno_range\": " + J(FnoScale) + ", \"field_range_deg\": " + J(FieldScale)
                    + ", \"on_axis_penalty\": " + J(FieldMax > 1e-6 ? OnAxisPenalty : 0.0) + "},");
            if (t != null)
                js.AppendLine("  \"target\": {\"efl\": " + J(t.Efl) + ", \"fno\": " + J(t.Fno) + ", \"hfov\": " + J(t.Hfov)
                    + ", \"object_distance\": " + J(t.Obj) + "},");
            if (pair != null)
                js.AppendLine("  \"request\": {\"nearest\": " + Js(pair.Nearest == null ? "" : pair.Nearest.Name)
                    + ", \"nearest_dist\": " + J(pair.Dist) + ", \"inside_hull\": " + (pair.InsideHull ? "true" : "false")
                    + ", \"exact_match\": " + (pair.Exact ? "true" : "false")
                    + ", \"warn_2d\": " + (pair.Warn ? "true" : "false") + ", \"start\": " + Js(Opts.Start)
                    + ", \"nearest_mf_scaled_reopt\": " + J(pair.Nearest == null ? double.NaN : pair.Nearest.MfReopt)
                    + ", \"blend_mf\": " + J(blendMf) + "},");
            if (cands != null)
            {
                var ranked = Ranked(cands);
                js.AppendLine("  \"candidates\": [" + string.Join(", ", ranked.Select(c => "{\"label\": " + Js(c.Label) + ", \"kind\": " + Js(c.Kind)
                    + ", \"screen_rank\": " + c.ScreenRank
                    + ", \"mf_start\": " + J(c.MfStart) + ", \"mf\": " + J(c.Mf) + ", \"spot_um\": " + J(c.Spot)
                    + ", \"passes_check\": " + (c.Pass ? "true" : "false") + ", \"fail\": " + Js(c.Fail)
                    + ", \"edge_rays_ok\": " + (c.EdgeOk == null ? "null" : c.EdgeOk.Value ? "true" : "false")
                    + ", \"mf_dls\": " + J(c.MfDls) + ", \"spot_dls_um\": " + J(c.SpotDls)
                    + ", \"hammered\": " + (c.Hammered ? "true" : "false") + ", \"hammer_note\": " + Js(c.HamNote)
                    + ", \"chosen\": " + (ReferenceEquals(c, chosen) ? "true" : "false") + "}")) + "],");
                // The screen: every start's score before optimizing, lowest first, and whether it was optimized.
                js.AppendLine("  \"screen\": [" + string.Join(", ", cands.OrderBy(c => c.ScreenRank > 0 ? c.ScreenRank : int.MaxValue)
                    .Select(c => "{\"rank\": " + c.ScreenRank + ", \"label\": " + Js(c.Label) + ", \"kind\": " + Js(c.Kind)
                    + ", \"mf_start\": " + J(c.MfStart) + ", \"optimized\": " + (c.Optimized ? "true" : "false")
                    + ", \"note\": " + Js(c.Note) + "}")) + "],");
                js.AppendLine("  \"chosen\": " + Js(chosen == null ? "" : chosen.Label) + ", \"runtime_s\": " + J(Math.Round(secs, 1)) + ",");
            }
            if (fo != null)
                js.AppendLine("  \"result_first_order\": {" + string.Join(", ", fo.Select(kv => Js(kv.Key) + ": " + J(kv.Value))) + "},");
            if (checks != null)
                js.AppendLine("  \"envelope_check\": {\"pass\": " + (pass ? "true" : "false") + ", \"items\": ["
                    + string.Join(", ", checks.Select(Js)) + "]},");
            js.AppendLine("  \"mf_start\": " + J(mfStart) + ", \"mf_result\": " + J(mfResult) + ",");
            js.AppendLine("  \"settings\": {\"start\": " + Js(Opts.Start) + ", \"layout\": " + Js(Opts.Layout) + ", \"weight\": " + Js(Opts.Weight)
                + ", \"glass\": " + Js(Opts.Glass) + ", \"passes\": " + Opts.Passes + ", \"hammer_sec\": " + Opts.HammerSec
                + ", \"hammer_keep\": " + HammerKeep + ", \"top\": " + (Opts.Top <= 0 ? "\"all\"" : Opts.Top.ToString(CultureInfo.InvariantCulture))
                + ", \"compare\": " + Js(Opts.Compare) + ", \"min_group\": " + Opts.MinGroup + "}");
            js.AppendLine("}");
            File.WriteAllText(outs["json"], js.ToString(), Encoding.UTF8);
            Say("CSV: " + outs["csv"]);
        }

        static string ObjCsv(double v) => IsFinite(v) ? G(v, 8) : "inf";

        // Draw a side view (Y-Z) of the result: each surface as a curve, glass edges,
        // and a fan of real rays per field (REAY/REAZ heights, placed with GLCZ).
        // OpticStudio's API cannot save its own layout window as a picture, so we draw it.
        static void DrawLayoutPng(ZOSAPI.IOpticalSystem S, string path, Target t, double mf, string startLabel)
        {
            var lde = S.LDE;
            int n = lde.NumberOfSurfaces;
            var zs = new double[n];
            for (int i = 0; i < n; i++)
            {
                zs[i] = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.GLCZ, i);
                if (!IsFinite(zs[i])) zs[i] = 0;
            }
            var surfLines = new List<List<PointF>>();
            var edges = new Dictionary<int, PointF[]>();
            for (int i = 1; i < n; i++)
            {
                var s = lde.GetSurfaceAt(i);
                double c = Curv(s.Radius), h = s.SemiDiameter;
                var terms = AsphereTerms(s);
                var pts = new List<PointF>();
                for (int k = 0; k <= 60; k++)
                {
                    double y = -h + 2 * h * k / 60.0;
                    double z = zs[i] + SagAt(c, s.Conic, terms, y);
                    if (IsFinite(z)) pts.Add(new PointF((float)z, (float)y));
                }
                if (pts.Count > 1) { surfLines.Add(pts); edges[i] = new[] { pts[0], pts[pts.Count - 1] }; }
            }
            // Close each piece of glass with straight edge lines (top and bottom).
            for (int i = 1; i < n - 1; i++)
                if ((lde.GetSurfaceAt(i).Material ?? "").Trim().Length > 0 && edges.ContainsKey(i) && edges.ContainsKey(i + 1))
                    for (int e = 0; e < 2; e++) surfLines.Add(new List<PointF> { edges[i][e], edges[i + 1][e] });

            var rays = new List<Tuple<List<PointF>, int>>();
            var fields = S.SystemData.Fields;
            double fmax = 1e-12;
            for (int k = 1; k <= fields.NumberOfFields; k++) fmax = Math.Max(fmax, Math.Abs(fields.GetField(k).Y));
            int wave = PrimaryWave(S);
            double[] pys = { -1, -0.66, -0.33, 0, 0.33, 0.66, 1 };
            for (int fi = 1; fi <= fields.NumberOfFields; fi++)
            {
                double hy = fmax > 1e-9 ? fields.GetField(fi).Y / fmax : 0.0;
                foreach (double py in pys)
                {
                    var pts = new List<PointF>();
                    for (int i = 1; i < n; i++)
                    {
                        double y = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.REAY, i, wave, 0, hy, 0, py);
                        double z = Op(S, ZOSAPI.Editors.MFE.MeritOperandType.REAZ, i, wave, 0, hy, 0, py);
                        if (IsFinite(y) && IsFinite(z)) pts.Add(new PointF((float)(zs[i] + z), (float)y));
                    }
                    if (pts.Count > 1) rays.Add(Tuple.Create(pts, fi - 1));
                }
            }

            // Fit everything into the picture with the same scale across and up.
            var all = surfLines.SelectMany(p => p).Concat(rays.SelectMany(r => r.Item1)).ToList();
            float zMin = all.Min(p => p.X), zMax = all.Max(p => p.X), yMin = all.Min(p => p.Y), yMax = all.Max(p => p.Y);
            int W = 1400, H = 700, margin = 50, footer = 40;
            float scale = Math.Min((W - 2 * margin) / Math.Max(zMax - zMin, 1e-3f),
                                   (H - 2 * margin - footer) / Math.Max(yMax - yMin, 1e-3f));
            float cx = (zMin + zMax) / 2, cy = (yMin + yMax) / 2;
            PointF Map(PointF p) => new PointF(W / 2f + (p.X - cx) * scale, (H - footer) / 2f - (p.Y - cy) * scale);
            Color[] colors = { Color.RoyalBlue, Color.ForestGreen, Color.Crimson };
            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                foreach (var r in rays)
                    using (var pen = new Pen(colors[r.Item2 % 3], 1f))
                        g.DrawLines(pen, r.Item1.Select(Map).ToArray());
                using (var pen = new Pen(Color.Black, 1.6f))
                    foreach (var line in surfLines) g.DrawLines(pen, line.Select(Map).ToArray());
                using (var font = new Font("Segoe UI", 11f))
                using (var brush = new SolidBrush(Color.Black))
                    g.DrawString(F("StartPointFinder result   EFL {0}   F/{1}   half-FOV {2} deg   MF {3}   start: {4}",
                        G(t.Efl, 4), G(t.Fno, 3), G(t.Hfov, 3), G(mf, 4), startLabel), font, brush, margin, H - footer + 8);
                bmp.Save(path, ImageFormat.Png);
            }
            Say("PNG: " + path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReverseSystem
{
    // Reverse System — a ZOS-API User Extension.
    //
    // Reverses the loaded sequential system in place, INCLUDING systems that
    // contain coordinate breaks and virtual propagation (negative thickness),
    // which OpticStudio's built-in Reverse Elements tool does not handle (see
    // community.zemax.com threads "How to flip the whole optical system",
    // "Reverse elements erases materials", "Reversing the path").
    //
    // Method: the reversed system is the mirror image of the original
    // traversed backwards. Formally each row operation S becomes M·S⁻¹·M
    // (M = reflection through the x-y plane), applied in reverse order:
    //   * optical surfaces : radius and polynomial sag terms negate, conic kept
    //   * gaps             : order reversed, signs kept (virtual propagation OK)
    //   * materials        : ride with their (reversed) gaps
    //   * coordinate break : decenter X/Y and tilt Z negate, tilt X/Y kept,
    //                        order flag flips (inverse transform, mirrored)
    // Conjugate states swap for a TRUE reversal: the reversed object space takes
    // on the state of the original image space (collimated in if the original
    // exit beam was collimated; a point at the original focus distance if it
    // converged) and vice versa, including the afocal-image-space flag. Pass
    // -keepconj to instead keep the object/image gaps in place ("flip the lens
    // between its mounts"). All solves in the reversed range are frozen to their
    // current values first, so pickups cannot corrupt the result.
    //
    // Usage:
    //   (no args)      reverse the system open in OpticStudio (extension mode)
    //   -save          save a copy as <file>_Reversed.<ext>
    //   -keepconj      keep object/image gaps in place (no conjugate swap)
    //   -refocus       run Quick Focus after reversing
    //   -file <path>   standalone mode: load <path>, reverse, save _Reversed copy
    //   -out <path>    standalone mode: explicit output path
    //   -keepaperture  do NOT convert the system aperture to Float By Stop Size
    //
    // Aperture handling: EPD / F-number / NA aperture definitions describe the
    // beam entering the ORIGINAL front of the system, so they no longer define
    // the same physical bundle once the system is reversed. The one definition
    // that is direction-independent is the physical stop itself, so the tool
    // records the stop's clear semi-diameter before reversing, fixes that value
    // on the relocated stop surface, and switches the system aperture to
    // Float By Stop Size — the reversed trace is then bounded by the same iris.
    //   -georeport     report global surface geometry (no changes) and exit
    //   -rayaim        enable real ray aiming after reversing (recommended when
    //                  the stop ends up buried behind tilted elements)
    class Options
    {
        public bool SaveCopy = false;
        public bool KeepConjugates = false;
        public bool Refocus = false;
        public bool KeepAperture = false;
        public bool GeoReport = false;
        public bool RayAim = false;
        public string FilePath = null;   // standalone test mode
        public string OutPath = null;
    }

    class RowSnap
    {
        public int OldIndex;
        public ZOSAPI.Editors.LDE.SurfaceType Type;
        public string TypeName;
        public double Radius;
        public double Conic;
        public double Thickness;
        public string Material;
        public string Comment;
        public double[] Pars = new double[9]; // 1..8 used
        public bool IsStop;
        public double SemiDiameter;
        public bool SDAutomatic;
        public ZOSAPI.Editors.LDE.SurfaceApertureTypes ApType;
        public double ApP1, ApP2, ApXDec, ApYDec;
        public double ApUdaScale;
        public int ApArms;
        public string ApFile;
        public bool ApPickup;
    }

    class Program
    {
        static Options Opts = new Options();
        static readonly List<string> Report = new List<string>();

        // NOTE: must be a method, not a static field — static ZOSAPI-typed state
        // would trigger assembly loading before ZOSAPI_NetHelper.Initialize().
        static bool IsSupported(ZOSAPI.Editors.LDE.SurfaceType t)
        {
            switch (t)
            {
                case ZOSAPI.Editors.LDE.SurfaceType.Standard:
                case ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak:
                case ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric:
                case ZOSAPI.Editors.LDE.SurfaceType.OddAsphere:
                case ZOSAPI.Editors.LDE.SurfaceType.Tilted:
                case ZOSAPI.Editors.LDE.SurfaceType.Paraxial:
                    return true;
                default:
                    return false;
            }
        }

        static void Main(string[] args)
        {
            ParseArgs(args);

            if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("Found OpticStudio at: " + ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory());

            try { Run(); }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }

        static void ParseArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].TrimStart('-', '/').ToLowerInvariant())
                {
                    case "save": Opts.SaveCopy = true; break;
                    case "keepconj": Opts.KeepConjugates = true; break;
                    case "refocus": Opts.Refocus = true; break;
                    case "keepaperture": Opts.KeepAperture = true; break;
                    case "georeport": Opts.GeoReport = true; break;
                    case "rayaim": Opts.RayAim = true; break;
                    case "file": if (i + 1 < args.Length) Opts.FilePath = args[++i]; break;
                    case "out": if (i + 1 < args.Length) Opts.OutPath = args[++i]; break;
                }
            }
        }

        static void Say(string line) { Console.WriteLine(line); Report.Add(line); }
        static string F(string fmt, params object[] a) => string.Format(CultureInfo.InvariantCulture, fmt, a);

        static void Run()
        {
            ZOSAPI.IZOSAPI_Application app = null;
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath);

            if (standalone)
            {
                app = connection.CreateNewApplication();
                if (app == null || !app.IsValidLicenseForAPI)
                {
                    Console.WriteLine("FATAL: could not start standalone OpticStudio instance.");
                    Environment.ExitCode = 1;
                    return;
                }
                if (!app.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    Console.WriteLine("FATAL: could not load " + Opts.FilePath);
                    app.CloseApplication();
                    Environment.ExitCode = 1;
                    return;
                }
                Say("Connected (standalone test mode), loaded: " + Opts.FilePath);
            }
            else
            {
                try { app = connection.ConnectToApplication(); } catch { app = null; }
                if (app == null)
                {
                    try { app = connection.ConnectAsExtension(0); } catch { app = null; }
                }
                if (app == null)
                {
                    Console.WriteLine("FATAL: could not connect to OpticStudio. Launch this tool from the");
                    Console.WriteLine("Programming ribbon, or turn on Programming > Interactive Extension first.");
                    Environment.ExitCode = 1;
                    return;
                }
                if (!app.IsValidLicenseForAPI)
                {
                    Console.WriteLine("FATAL: license is not valid for ZOS-API: " + app.LicenseStatus);
                    Environment.ExitCode = 1;
                    return;
                }
                Say("Connected to OpticStudio (mode: " + app.Mode + ")");
            }

            try
            {
                RunOnSystem(app);
            }
            finally
            {
                if (standalone)
                {
                    app.CloseApplication();
                }
                else
                {
                    app.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(app.ProgressMessage) || !app.ProgressMessage.StartsWith("Done"))
                        app.ProgressMessage = "Reverse System finished.";
                }
            }
        }

        static void RunOnSystem(ZOSAPI.IZOSAPI_Application app)
        {
            var sys = app.PrimarySystem;
            if (sys.Mode != ZOSAPI.SystemType.Sequential)
            {
                Say("This extension requires a sequential system.");
                return;
            }

            var lde = sys.LDE;
            int imgIdx = lde.NumberOfSurfaces - 1;

            Say("");
            Say("=== Reverse System ===");
            Say("Lens file : " + (string.IsNullOrEmpty(sys.SystemFile) ? "(untitled)" : sys.SystemFile));
            Say(F("Surfaces  : {0} (OBJ=0 .. IMA={1})", lde.NumberOfSurfaces, imgIdx));
            Say("Conjugates: " + (Opts.KeepConjugates ? "object/image gaps kept in place (-keepconj)" : "true reversal (conjugate states swap)"));

            if (imgIdx < 2)
            {
                Say("Nothing to reverse (no surfaces between object and image).");
                return;
            }

            if (Opts.GeoReport)
            {
                // Diagnostic mode: dump global vertex positions and local axis
                // directions so original/reversed geometry can be compared as
                // rigid point sets. No modification is made.
                Say("");
                Say("Global surface geometry (vertex, local X axis, local Z axis):");
                Say("  #   type               vertex (x,y,z)                    xAxis (x,y,z)                  zAxis (x,y,z)");
                for (int i = 0; i <= imgIdx; i++)
                {
                    double r11, r12, r13, r21, r22, r23, r31, r32, r33, x, y, z;
                    bool ok = lde.GetGlobalMatrix(i, out r11, out r12, out r13, out r21, out r22, out r23,
                        out r31, out r32, out r33, out x, out y, out z);
                    Say(F("  {0,-3} {1,-18} {2,10:F5} {3,10:F5} {4,10:F5}   {5,8:F5} {6,8:F5} {7,8:F5}   {8,8:F5} {9,8:F5} {10,8:F5}{11}",
                        i, lde.GetSurfaceAt(i).TypeName, x, y, z, r11, r21, r31, r13, r23, r33, ok ? "" : "  (!)"));
                }
                app.ProgressMessage = "Done. Geometry report only - no changes made.";
                return;
            }

            // ---- validate ------------------------------------------------------
            app.ProgressPercent = 5;
            app.ProgressMessage = "Validating system...";
            var problems = new List<string>();
            for (int i = 1; i < imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                if (!IsSupported(row.Type))
                    problems.Add(F("  surface {0}: unsupported type '{1}'", i, row.TypeName));
            }
            if (sys.MCE.NumberOfConfigurations > 1)
            {
                // MCE operands that reference surface numbers would silently apply
                // to the WRONG rows after reversal (e.g. a GLSS on surface 4 lands
                // on whatever now sits at row 4), so their presence is a blocker.
                var stale = new List<string>();
                for (int i = 1; i <= sys.MCE.NumberOfOperands; i++)
                {
                    var op = sys.MCE.GetOperandAt(i);
                    switch (op.Type)
                    {
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.THIC:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.GLSS:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.CRVT:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.CONN:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.SDIA:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.COTN:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.IGNR:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.PRAM:
                        case ZOSAPI.Editors.MCE.MultiConfigOperandType.STPS:
                            stale.Add(F("  MCE operand {0} ({1}) references a surface number", i, op.TypeName));
                            break;
                    }
                }
                if (stale.Count > 0)
                    problems.AddRange(stale);
                else
                    Say("WARNING: multi-configuration system; only configuration-independent data is affected by the reversal.");
            }
            if (problems.Count > 0)
            {
                Say("Cannot reverse this system:");
                foreach (var p in problems) Say(p);
                Say("Supported: Standard, Coordinate Break, Even/Odd Asphere, Tilted, Paraxial (refractive and reflective).");
                app.ProgressMessage = "Done. System not reversed (unsupported features).";
                return;
            }

            // ---- BEFORE metrics ------------------------------------------------
            app.ProgressPercent = 10;
            app.ProgressMessage = "Measuring baseline...";
            var before = Snapshot(sys);

            // ---- freeze all solves in the reversed range -----------------------
            // Pickups and other solves reference fixed row numbers and would
            // corrupt the data during the rewrite, so freeze everything to its
            // current value first.
            app.ProgressPercent = 20;
            app.ProgressMessage = "Freezing solves...";
            int frozen = 0;
            var freezeCols = new List<ZOSAPI.Editors.LDE.SurfaceColumn>
            {
                ZOSAPI.Editors.LDE.SurfaceColumn.Radius,
                ZOSAPI.Editors.LDE.SurfaceColumn.Thickness,
                ZOSAPI.Editors.LDE.SurfaceColumn.Material,
                ZOSAPI.Editors.LDE.SurfaceColumn.Conic,
            };
            for (int p = 1; p <= 8; p++)
                freezeCols.Add((ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p));

            for (int i = 1; i < imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                foreach (var col in freezeCols)
                {
                    try
                    {
                        var cell = row.GetSurfaceCell(col);
                        var st = cell.Solve;
                        if (st != ZOSAPI.Editors.SolveType.Fixed && st != ZOSAPI.Editors.SolveType.None &&
                            st != ZOSAPI.Editors.SolveType.Automatic)
                        {
                            if (cell.MakeSolveFixed()) frozen++;
                        }
                    }
                    catch { /* locked or non-existent cell for this type */ }
                }
            }
            if (frozen > 0) Say(F("Froze {0} solve(s) (pickups/variables) to their current values.", frozen));

            // ---- snapshot ------------------------------------------------------
            app.ProgressPercent = 30;
            app.ProgressMessage = "Reading lens data...";
            var snaps = new RowSnap[imgIdx + 1]; // index by old row (0..imgIdx used partially)
            for (int i = 0; i <= imgIdx; i++)
            {
                var row = lde.GetSurfaceAt(i);
                var s = new RowSnap
                {
                    OldIndex = i,
                    Type = row.Type,
                    TypeName = row.TypeName,
                    Thickness = row.Thickness,
                    Material = (row.Material ?? "").Trim(),
                    Comment = row.Comment ?? "",
                    IsStop = row.IsStop,
                };
                try { s.Radius = row.Radius; } catch { s.Radius = double.PositiveInfinity; }
                try { s.Conic = row.Conic; } catch { s.Conic = 0; }
                // CB/Tilted rows report a locked conic cell as +Infinity — never
                // propagate that as a real conic value.
                if (Math.Abs(s.Conic) > 1e10 || double.IsNaN(s.Conic)) s.Conic = 0;
                foreach (int p in ParamsUsed(s.Type))
                {
                    var cell = GetPar(row, p);
                    try { s.Pars[p] = cell.DoubleValue; }
                    catch { try { s.Pars[p] = cell.IntegerValue; } catch { s.Pars[p] = 0; } }
                }
                try { s.SemiDiameter = row.SemiDiameter; } catch { s.SemiDiameter = 0; }
                try { s.SDAutomatic = row.SemiDiameterCell.Solve == ZOSAPI.Editors.SolveType.Automatic; }
                catch { s.SDAutomatic = true; }
                ReadAperture(row, s);
                snaps[i] = s;
            }

            // Effective medium per gap (resolves '-' continuation on CB rows).
            // MIRROR is a SURFACE property, not a gap medium: reflection returns
            // the ray into the incident medium, so a mirror row continues the
            // previous medium and the MIRROR marker travels with the surface.
            var eff = new string[imgIdx + 1];
            var isMirror = new bool[imgIdx + 1];
            int mirrorCount = 0;
            for (int i = 0; i <= imgIdx; i++)
            {
                string m = snaps[i].Material;
                isMirror[i] = m.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                if (isMirror[i] && i >= 1 && i < imgIdx) mirrorCount++;
                if (m == "-" || isMirror[i] ||
                    (snaps[i].Type == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak && m.Length == 0))
                    eff[i] = i > 0 ? eff[i - 1] : "";
                else
                    eff[i] = m;
            }
            // Zemax thickness signs encode the propagation direction, which
            // alternates with the number of reflections ALONG THE TRAVERSAL.
            // Reversing the traversal multiplies every interior gap by
            // (-1)^(total mirrors); virtual propagation survives automatically.
            double mirrorSign = (mirrorCount % 2 == 1) ? -1.0 : 1.0;
            if (mirrorCount > 0)
                Say(F("Reflective system: {0} mirror surface(s) ({1}) - interior gap signs {2}.",
                    mirrorCount, mirrorCount % 2 == 1 ? "odd" : "even",
                    mirrorSign < 0 ? "flip" : "are preserved"));

            int oldStop = lde.StopSurface;

            // ---- record the aperture definition & physical stop size ----------
            // The stop clear semi-diameter is the direction-independent beam
            // definition. Prefer the traced marginal-ray height at the stop; fall
            // back to the stop row's semi-diameter cell.
            var aperture = sys.SystemData.Aperture;
            var apType0 = aperture.ApertureType;
            double apVal0 = aperture.ApertureValue;
            int primaryWave = 1;
            try
            {
                var wls = sys.SystemData.Wavelengths;
                for (int w = 1; w <= wls.NumberOfWavelengths; w++)
                    if (wls.GetWavelength(w).IsPrimary) { primaryWave = w; break; }
            }
            catch { }
            double stopClearSD = 0;
            if (oldStop >= 1 && oldStop < imgIdx)
            {
                stopClearSD = snaps[oldStop].SemiDiameter;
                try
                {
                    // paraxial marginal ray height at the stop, at the PRIMARY
                    // wavelength: Float By Stop Size pins the pupil paraxially at
                    // the primary wavelength, so this value reproduces the
                    // original bundle exactly
                    double r1 = Math.Abs(sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.PARY, oldStop, primaryWave, 0, 0, 0, 1, 0, 0));
                    double r2 = Math.Abs(sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.PARX, oldStop, primaryWave, 0, 0, 1, 0, 0, 0));
                    double traced = Math.Max(r1, r2);
                    if (traced > 1e-12 && !double.IsNaN(traced)) stopClearSD = traced;
                }
                catch { /* keep the cell value */ }
            }

            // ---- conjugate state analysis (true reversal) ----------------------
            // The reversed object space must reproduce the ORIGINAL IMAGE-SPACE
            // light state: collimated exit beam -> collimated (infinite) object;
            // beam converging to a focus -> point object at that distance. The
            // reversed image space likewise takes the original object state.
            // REAL marginal ray fans are analysed separately in x and y (paraxial
            // rays are unreliable through strongly tilted geometry, and beam
            // shapers can be astigmatic); classification uses the fan with the
            // larger beam extent, relative to the physical length of the system.
            bool objCollimated = snaps[0].Thickness > 1e10;
            bool imgCollimated = false;
            // image-space focus as a PHYSICAL propagation distance from the last
            // surface: after an odd number of mirrors the beam travels -z, so the
            // signed z-coordinate must be flipped by (-1)^mirrors
            double focusFromLast = mirrorSign * snaps[imgIdx - 1].Thickness;
            double sysLen = 0;
            for (int i = 1; i < imgIdx; i++)
                if (Math.Abs(snaps[i].Thickness) < 1e8) sysLen += Math.Abs(snaps[i].Thickness);
            // a fan whose crossing is many times the physical system length is
            // collimated for reversal purposes
            double collimLimit = Math.Max(50.0 * sysLen, 500.0);
            try { imgCollimated = aperture.AFocalImageSpace; } catch { }
            if (!imgCollimated)
            {
                try
                {
                    // real marginal ray fans at the image surface (on-axis field)
                    double yI = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAY, imgIdx, primaryWave, 0, 0, 0, 1, 0, 0);
                    double mY = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAB, imgIdx, primaryWave, 0, 0, 0, 1, 0, 0);
                    double nY = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAC, imgIdx, primaryWave, 0, 0, 0, 1, 0, 0);
                    double xI = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAX, imgIdx, primaryWave, 0, 0, 1, 0, 0, 0);
                    double lX = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAA, imgIdx, primaryWave, 0, 0, 1, 0, 0, 0);
                    double nX = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.REAC, imgIdx, primaryWave, 0, 0, 1, 0, 0, 0);
                    double uY = Math.Abs(nY) > 1e-14 ? mY / nY : 0;
                    double uX = Math.Abs(nX) > 1e-14 ? lX / nX : 0;
                    double lastGap = snaps[imgIdx - 1].Thickness;
                    double focY = Math.Abs(uY) > 1e-14 ? mirrorSign * (lastGap - yI / uY) : double.PositiveInfinity;
                    double focX = Math.Abs(uX) > 1e-14 ? mirrorSign * (lastGap - xI / uX) : double.PositiveInfinity;
                    bool colY = Math.Abs(focY) > collimLimit;
                    bool colX = Math.Abs(focX) > collimLimit;
                    Say("");
                    Say(F("Image-space real ray fans: x-fan {0} (extent {1:G4}); y-fan {2} (extent {3:G4})",
                        colX ? "collimated" : F("focus {0:G6} after last surface", focX), Math.Abs(xI),
                        colY ? "collimated" : F("focus {0:G6} after last surface", focY), Math.Abs(yI)));
                    bool astigmatic = (colX != colY) ||
                        (!colX && !colY && Math.Abs(focX - focY) > 0.2 * Math.Max(Math.Abs(focX), Math.Abs(focY)));
                    if (astigmatic)
                        Say("WARNING: the exit beam is astigmatic - no single point conjugate exists. " +
                            "Classifying by the dominant (larger) fan; override with the object distance manually if needed.");
                    // dominant fan = larger beam extent at the image
                    if (Math.Abs(yI) >= Math.Abs(xI)) { imgCollimated = colY; focusFromLast = focY; }
                    else { imgCollimated = colX; focusFromLast = focX; }
                    if (!imgCollimated && (!colX && !colY) && !astigmatic)
                        focusFromLast = 0.5 * (focX + focY);
                }
                catch { /* keep: focus assumed at the image plane */ }
            }
            Say("");
            Say("Object space (original): " + (objCollimated ? "collimated (object at infinity)"
                : F("point source {0:G6} before the first surface", snaps[0].Thickness)));
            Say("Image space  (original): " + (imgCollimated ? "COLLIMATED (dominant real-ray fan)"
                : F("converges to a focus {0:G6} after the last surface", focusFromLast)));

            // ---- compute reversed rows ----------------------------------------
            // new row k (1..imgIdx-1) <- transformed old row (imgIdx - k)
            // new gap  k (1..imgIdx-2) <- old gap (imgIdx - 1 - k); end gaps kept
            app.ProgressPercent = 40;
            app.ProgressMessage = "Reversing...";

            var newType = new ZOSAPI.Editors.LDE.SurfaceType[imgIdx];
            var newRadius = new double[imgIdx];
            var newConic = new double[imgIdx];
            var newPars = new double[imgIdx][];
            var newComment = new string[imgIdx];
            var newThick = new double[imgIdx];
            var newEff = new string[imgIdx];
            var srcOf = new int[imgIdx];

            // EVEN mirror count (incl. refractive): reversal = conjugation by the
            // z-mirror -> radii and sag terms negate, CB decenters and tilt-Z
            // negate. ODD mirror count: the z-mirror alone would leave the light
            // entering along -z, so the operator gains a 180-degree rotation and
            // becomes conjugation by the Y-FLIP mirror -> radii/conic/aspheres
            // are KEPT, gaps negate, and the CB rule is (-dx,+dy,+tx,-ty,+tz).
            bool oddMirrors = mirrorSign < 0;
            for (int k = 1; k < imgIdx; k++)
            {
                var src = snaps[imgIdx - k];
                srcOf[k] = src.OldIndex;
                newType[k] = src.Type;
                newComment[k] = src.Comment;
                newConic[k] = src.Conic;
                newRadius[k] = (oddMirrors || Math.Abs(src.Radius) > 1e10 || src.Radius == 0) ? src.Radius : -src.Radius;
                var pars = (double[])src.Pars.Clone();
                switch (src.Type)
                {
                    case ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak:
                        if (oddMirrors)
                        {
                            pars[1] = -pars[1];      // decenter X
                            // pars[2] (decenter Y), pars[3] (tilt X) unchanged
                            pars[4] = -pars[4];      // tilt Y
                            // pars[5] (tilt Z) unchanged
                        }
                        else
                        {
                            pars[1] = -pars[1];      // decenter X
                            pars[2] = -pars[2];      // decenter Y
                            // pars[3], pars[4] (tilt X, tilt Y) unchanged
                            pars[5] = -pars[5];      // tilt Z
                        }
                        pars[6] = pars[6] == 0 ? 1 : 0; // order flag flips
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric:
                    case ZOSAPI.Editors.LDE.SurfaceType.OddAsphere:
                        if (!oddMirrors)
                            for (int p = 1; p <= 8; p++) pars[p] = -pars[p]; // sag negates
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceType.Tilted:
                        if (oddMirrors)
                            pars[2] = -pars[2];      // y tangent only (y-flip)
                        else
                        {
                            pars[1] = -pars[1];      // x tangent
                            pars[2] = -pars[2];      // y tangent
                        }
                        break;
                        // Standard: nothing; Paraxial: focal length/OPD mode kept
                }
                newPars[k] = pars;

                newThick[k] = (k <= imgIdx - 2) ? mirrorSign * snaps[imgIdx - 1 - k].Thickness : snaps[imgIdx - 1].Thickness;
                newEff[k] = (k <= imgIdx - 2) ? eff[imgIdx - 1 - k] : eff[imgIdx - 1];
            }
            if (!Opts.KeepConjugates)
            {
                // reversed object space <- original image space state
                double newT0 = imgCollimated ? 1e18 : focusFromLast;
                lde.GetSurfaceAt(0).Thickness = newT0;
                // reversed image space <- original object space state; when the
                // output is collimated the image plane is only an evaluation
                // plane, so keep the existing gap for scale
                // the reversed image gap carries the exit-direction parity
                newThick[imgIdx - 1] = objCollimated ? snaps[imgIdx - 1].Thickness : mirrorSign * snaps[0].Thickness;
                Say("");
                Say("Reversed object space  : " + (imgCollimated ? "collimated (object set to infinity)"
                    : F("point source {0:G6} before the first surface", newT0)));
                Say("Reversed image space   : " + (objCollimated
                    ? F("collimated; image plane is an evaluation plane {0:G6} after the last surface", newThick[imgIdx - 1])
                    : F("focuses {0:G6} after the last surface (the original object location)", newThick[imgIdx - 1])));
                bool anyOffAxisField = false;
                var flds = sys.SystemData.Fields;
                for (int fi = 1; fi <= flds.NumberOfFields; fi++)
                {
                    var fld = flds.GetField(fi);
                    if (Math.Abs(fld.X) > 1e-12 || Math.Abs(fld.Y) > 1e-12) { anyOffAxisField = true; break; }
                }
                if (anyOffAxisField)
                    Say("NOTE: off-axis fields are defined for the ORIGINAL conjugates - review them for the reversed system.");
            }

            // ---- write ---------------------------------------------------------
            app.ProgressPercent = 55;
            app.ProgressMessage = "Writing reversed lens data...";
            var writeErrors = new List<string>();
            for (int k = 1; k < imgIdx; k++)
            {
                var row = lde.GetSurfaceAt(k);
                try
                {
                    if (row.Type != newType[k])
                    {
                        // clear any stale conic before the cell becomes locked
                        // (a leftover CONI infinity on a CB row corrupts the trace)
                        try { row.Conic = 0; } catch { }
                        if (!row.ChangeType(row.GetSurfaceTypeSettings(newType[k])))
                            throw new Exception("ChangeType to " + newType[k] + " failed");
                    }
                    bool isCB = newType[k] == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak;
                    if (!isCB)
                    {
                        try { row.Radius = newRadius[k]; } catch { }
                        try { row.Conic = newConic[k]; } catch { }
                    }
                    row.Thickness = newThick[k];
                    row.Comment = newComment[k];
                    foreach (int p in ParamsUsed(newType[k]))
                    {
                        var cell = GetPar(row, p);
                        try { cell.DoubleValue = newPars[k][p]; }
                        catch
                        {
                            try { cell.IntegerValue = (int)Math.Round(newPars[k][p]); }
                            catch (Exception ex) { writeErrors.Add(F("row {0} Par{1}: {2}", k, p, ex.Message)); }
                        }
                    }
                    if (!isCB)
                    {
                        // semi-diameters travel with their surfaces: user-fixed
                        // values are copied, automatic stays automatic
                        var srcSnap = snaps[srcOf[k]];
                        if (srcSnap.SDAutomatic)
                        {
                            try
                            {
                                var sdCell = row.SemiDiameterCell;
                                if (sdCell.Solve != ZOSAPI.Editors.SolveType.Automatic)
                                    sdCell.SetSolveData(sdCell.CreateSolveType(ZOSAPI.Editors.SolveType.Automatic));
                            }
                            catch { }
                        }
                        else
                        {
                            try { row.SemiDiameter = srcSnap.SemiDiameter; } catch { }
                        }
                    }
                    // surface apertures clip real rays and MUST travel with their
                    // surfaces; decenters are invariant under the mirror
                    WriteAperture(row, snaps[srcOf[k]], writeErrors, k);
                    if (!isCB)
                    {
                        // the MIRROR marker travels with the surface geometry
                        string want = isMirror[srcOf[k]] ? "MIRROR" : newEff[k];
                        string current = (row.Material ?? "").Trim();
                        if (!current.Equals(want, StringComparison.OrdinalIgnoreCase))
                            row.Material = want;
                    }
                    else if (k >= 2 && !newEff[k].Equals(newEff[k - 1], StringComparison.OrdinalIgnoreCase))
                    {
                        writeErrors.Add(F("row {0}: medium changes at a coordinate break ({1} -> {2}) - check manually",
                            k, newEff[k - 1], newEff[k]));
                    }
                }
                catch (Exception ex)
                {
                    writeErrors.Add(F("row {0}: {1}", k, ex.Message));
                }
            }

            // ---- stop surface ---------------------------------------------------
            if (oldStop >= 1 && oldStop < imgIdx)
            {
                int newStop = imgIdx - oldStop;
                // stop cannot sit on a coordinate break; walk to nearest regular row
                int tries = 0;
                while (newType.ElementAtOrDefault(newStop) == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak
                       && newStop > 1 && tries++ < imgIdx)
                    newStop--;
                try
                {
                    lde.GetSurfaceAt(newStop).IsStop = true;
                    Say(F("Stop surface: {0} -> {1}", oldStop, newStop));
                }
                catch (Exception ex) { writeErrors.Add("stop: " + ex.Message); }

                // ---- make the aperture definition direction-independent -------
                if (!Opts.KeepAperture)
                {
                    try
                    {
                        var stopRow = lde.GetSurfaceAt(newStop);
                        // a floating aperture follows the semi-diameter, which is
                        // about to become the beam definition; pin the physical
                        // iris size with a fixed circular aperture instead
                        try
                        {
                            var stopAd = stopRow.ApertureData;
                            double physicalSD = snaps[oldStop].SemiDiameter;
                            if (stopAd.CurrentType == ZOSAPI.Editors.LDE.SurfaceApertureTypes.FloatingAperture &&
                                Math.Abs(physicalSD - stopClearSD) > 1e-9)
                            {
                                var circ = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)stopAd.CreateApertureTypeSettings(
                                    ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture);
                                circ.MinimumRadius = 0; circ.MaximumRadius = physicalSD;
                                stopAd.ChangeApertureTypeSettings(circ);
                                Say(F("Stop aperture: floating -> fixed circular r={0:F4} (physical iris preserved)", physicalSD));
                            }
                        }
                        catch { }
                        // the physical iris keeps its size when the lens is flipped
                        stopRow.SemiDiameter = stopClearSD;
                        if (apType0 != ZOSAPI.SystemData.ZemaxApertureType.FloatByStopSize)
                        {
                            aperture.ApertureType = ZOSAPI.SystemData.ZemaxApertureType.FloatByStopSize;
                            Say(F("Aperture: {0} {1:G6} -> FloatByStopSize, stop semi-diameter fixed at {2:F4} (same physical beam, reversed)",
                                apType0, apVal0, stopClearSD));
                        }
                        else
                        {
                            Say(F("Aperture: FloatByStopSize kept, stop semi-diameter fixed at {0:F4}", stopClearSD));
                        }
                    }
                    catch (Exception ex) { writeErrors.Add("aperture: " + ex.Message); }
                }
                else
                {
                    Say(F("Aperture: left as {0} {1:G6} (-keepaperture) - NOTE: this no longer describes the same physical beam.",
                        apType0, apVal0));
                }
            }

            if (!Opts.KeepConjugates)
            {
                try
                {
                    bool newAfocal = objCollimated;
                    if (aperture.AFocalImageSpace != newAfocal)
                    {
                        aperture.AFocalImageSpace = newAfocal;
                        Say("Afocal image space: " + (newAfocal
                            ? "ENABLED (the reversed image space is collimated)"
                            : "disabled (the reversed image space focuses to a point)"));
                    }
                }
                catch (Exception ex) { Say("WARNING: could not set the afocal flag: " + ex.Message); }
            }

            if (Opts.RayAim)
            {
                try
                {
                    sys.SystemData.RayAiming.RayAiming = ZOSAPI.SystemData.RayAimingMethod.Real;
                    Say("Ray aiming set to Real (rays will seek the relocated stop).");
                }
                catch (Exception ex) { Say("WARNING: could not enable ray aiming: " + ex.Message); }
            }

            if (Opts.Refocus)
            {
                app.ProgressPercent = 70;
                app.ProgressMessage = "Quick focus...";
                try
                {
                    var qf = sys.Tools.OpenQuickFocus();
                    qf.RunAndWaitForCompletion();
                    qf.Close();
                    Say("Quick focus applied.");
                }
                catch (Exception ex) { Say("WARNING: quick focus failed: " + ex.Message); }
            }

            // ---- mapping table ---------------------------------------------------
            Say("");
            Say("Row map (new <- old):  type            radius            thickness    medium");
            for (int k = 1; k < imgIdx; k++)
            {
                var src = snaps[srcOf[k]];
                string rad = newType[k] == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak ? "-"
                    : (Math.Abs(newRadius[k]) > 1e10 || newRadius[k] == 0) ? "Infinity" : F("{0:F4}", newRadius[k]);
                string extra = "";
                if (newType[k] == ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak)
                    extra = F("  CB(dx={0:g4}, dy={1:g4}, tx={2:g4}, ty={3:g4}, tz={4:g4}, order={5:g1})",
                        newPars[k][1], newPars[k][2], newPars[k][3], newPars[k][4], newPars[k][5], newPars[k][6]);
                string medium = isMirror[srcOf[k]] ? "MIRROR" + (newEff[k].Length == 0 ? "" : "/" + newEff[k])
                    : (newEff[k].Length == 0 ? "(air)" : newEff[k]);
                Say(F("  {0,3} <- {1,3}   {2,-15} {3,12}   {4,12:F4}    {5}{6}",
                    k, srcOf[k], src.TypeName, rad, newThick[k], medium, extra));
            }

            if (writeErrors.Count > 0)
            {
                Say("");
                Say("WARNINGS/ERRORS during write:");
                foreach (var e in writeErrors) Say("  " + e);
            }

            // ---- AFTER metrics ----------------------------------------------------
            app.ProgressPercent = 85;
            app.ProgressMessage = "Measuring reversed system...";
            var after = Snapshot(sys);
            PrintMetrics(before, after, sys);

            // ---- save --------------------------------------------------------------
            string savedTo = null;
            if (Opts.SaveCopy || !string.IsNullOrEmpty(Opts.FilePath))
            {
                savedTo = Opts.OutPath;
                if (string.IsNullOrEmpty(savedTo))
                {
                    string src = !string.IsNullOrEmpty(Opts.FilePath) ? Opts.FilePath : sys.SystemFile;
                    if (string.IsNullOrEmpty(src))
                        savedTo = Path.Combine(app.ZemaxDataDir, "Untitled_Reversed.zos");
                    else
                        savedTo = Path.Combine(Path.GetDirectoryName(src),
                            Path.GetFileNameWithoutExtension(src) + "_Reversed" + Path.GetExtension(src));
                }
                sys.SaveAs(savedTo);
                Say("");
                Say("Saved reversed system to: " + savedTo);
            }

            WriteReportFile(app, sys, savedTo);
            app.ProgressMessage = "Done. System reversed.";
        }

        static IEnumerable<int> ParamsUsed(ZOSAPI.Editors.LDE.SurfaceType t)
        {
            switch (t)
            {
                case ZOSAPI.Editors.LDE.SurfaceType.CoordinateBreak: return Enumerable.Range(1, 6);
                case ZOSAPI.Editors.LDE.SurfaceType.EvenAspheric:
                case ZOSAPI.Editors.LDE.SurfaceType.OddAsphere: return Enumerable.Range(1, 8);
                case ZOSAPI.Editors.LDE.SurfaceType.Tilted:
                case ZOSAPI.Editors.LDE.SurfaceType.Paraxial: return Enumerable.Range(1, 2);
                default: return Enumerable.Empty<int>();
            }
        }

        static ZOSAPI.Editors.IEditorCell GetPar(ZOSAPI.Editors.LDE.ILDERow row, int p)
        {
            var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + p);
            return row.GetSurfaceCell(col);
        }

        static void ReadAperture(ZOSAPI.Editors.LDE.ILDERow row, RowSnap s)
        {
            s.ApType = ZOSAPI.Editors.LDE.SurfaceApertureTypes.None;
            try
            {
                var ad = row.ApertureData;
                s.ApType = ad.CurrentType;
                s.ApPickup = ad.IsPickedUp;
                var st = ad.CurrentTypeSettings;
                switch (s.ApType)
                {
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularObscuration:
                        var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                        s.ApP1 = c.MinimumRadius; s.ApP2 = c.MaximumRadius;
                        s.ApXDec = c.ApertureXDecenter; s.ApYDec = c.ApertureYDecenter;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularObscuration:
                        var r = (ZOSAPI.Editors.LDE.ISurfaceApertureRectangular)st;
                        s.ApP1 = r.XHalfWidth; s.ApP2 = r.YHalfWidth;
                        s.ApXDec = r.ApertureXDecenter; s.ApYDec = r.ApertureYDecenter;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalObscuration:
                        var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                        s.ApP1 = e.XHalfWidth; s.ApP2 = e.YHalfWidth;
                        s.ApXDec = e.ApertureXDecenter; s.ApYDec = e.ApertureYDecenter;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.Spider:
                        var sp = (ZOSAPI.Editors.LDE.ISurfaceApertureSpider)st;
                        s.ApP1 = sp.WidthOfArms; s.ApArms = sp.NumberOfArms;
                        s.ApXDec = sp.ApertureXDecenter; s.ApYDec = sp.ApertureYDecenter;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.UserAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.UserObscuration:
                        var u = (ZOSAPI.Editors.LDE.ISurfaceApertureUser)st;
                        s.ApFile = u.ApertureFile; s.ApUdaScale = u.UDASCale;
                        s.ApXDec = u.ApertureXDecenter; s.ApYDec = u.ApertureYDecenter;
                        break;
                        // None / FloatingAperture carry no parameters
                }
            }
            catch { s.ApType = ZOSAPI.Editors.LDE.SurfaceApertureTypes.None; }
        }

        static void WriteAperture(ZOSAPI.Editors.LDE.ILDERow row, RowSnap src, List<string> errs, int k)
        {
            try
            {
                var ad = row.ApertureData;
                if (ad.CurrentType == src.ApType &&
                    src.ApType == ZOSAPI.Editors.LDE.SurfaceApertureTypes.None)
                    return;
                var st = ad.CreateApertureTypeSettings(src.ApType);
                switch (src.ApType)
                {
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.CircularObscuration:
                        var c = (ZOSAPI.Editors.LDE.ISurfaceApertureCircular)st;
                        c.MinimumRadius = src.ApP1; c.MaximumRadius = src.ApP2;
                        c.ApertureXDecenter = src.ApXDec; c.ApertureYDecenter = src.ApYDec;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.RectangularObscuration:
                        var r = (ZOSAPI.Editors.LDE.ISurfaceApertureRectangular)st;
                        r.XHalfWidth = src.ApP1; r.YHalfWidth = src.ApP2;
                        r.ApertureXDecenter = src.ApXDec; r.ApertureYDecenter = src.ApYDec;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.EllipticalObscuration:
                        var e = (ZOSAPI.Editors.LDE.ISurfaceApertureElliptical)st;
                        e.XHalfWidth = src.ApP1; e.YHalfWidth = src.ApP2;
                        e.ApertureXDecenter = src.ApXDec; e.ApertureYDecenter = src.ApYDec;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.Spider:
                        var sp = (ZOSAPI.Editors.LDE.ISurfaceApertureSpider)st;
                        sp.WidthOfArms = src.ApP1; sp.NumberOfArms = src.ApArms;
                        sp.ApertureXDecenter = src.ApXDec; sp.ApertureYDecenter = src.ApYDec;
                        break;
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.UserAperture:
                    case ZOSAPI.Editors.LDE.SurfaceApertureTypes.UserObscuration:
                        var u = (ZOSAPI.Editors.LDE.ISurfaceApertureUser)st;
                        u.ApertureFile = src.ApFile; u.UDASCale = src.ApUdaScale;
                        u.ApertureXDecenter = src.ApXDec; u.ApertureYDecenter = src.ApYDec;
                        break;
                }
                if (!ad.ChangeApertureTypeSettings(st))
                    errs.Add(F("row {0}: aperture ({1}) could not be applied", k, src.ApType));
                if (src.ApPickup)
                    errs.Add(F("row {0}: source aperture used a pickup - not remapped, check manually", k));
            }
            catch (Exception ex)
            {
                if (src.ApType != ZOSAPI.Editors.LDE.SurfaceApertureTypes.None)
                    errs.Add(F("row {0}: aperture ({1}): {2}", k, src.ApType, ex.Message));
            }
        }

        static Dictionary<string, double[]> Snapshot(ZOSAPI.IOpticalSystem sys)
        {
            var m = new Dictionary<string, double[]>();
            var mfe = sys.MFE;
            double effl = double.NaN, mf = double.NaN;
            try { effl = mfe.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.EFFL, 0, 1, 0, 0, 0, 0, 0, 0); } catch { }
            try { if (mfe.NumberOfOperands > 0) mf = mfe.CalculateMeritFunction(); } catch { }
            m["EFFL"] = new[] { effl };
            m["MF"] = new[] { mf };

            var fields = sys.SystemData.Fields;
            int nf = fields.NumberOfFields;
            double maxR = 1e-10;
            for (int i = 1; i <= nf; i++)
            {
                var f = fields.GetField(i);
                maxR = Math.Max(maxR, Math.Sqrt(f.X * f.X + f.Y * f.Y));
            }
            var rms = new double[nf]; var fx = new double[nf]; var fy = new double[nf];
            for (int i = 1; i <= nf; i++)
            {
                var f = fields.GetField(i);
                fx[i - 1] = f.X; fy[i - 1] = f.Y;
                try
                {
                    rms[i - 1] = sys.MFE.GetOperandValue(ZOSAPI.Editors.MFE.MeritOperandType.RSCE,
                        6, 0, f.X / maxR, f.Y / maxR, 0, 0, 0, 0);
                }
                catch { rms[i - 1] = double.NaN; }
            }
            m["RMS"] = rms; m["FX"] = fx; m["FY"] = fy;
            return m;
        }

        static void PrintMetrics(Dictionary<string, double[]> before, Dictionary<string, double[]> after,
            ZOSAPI.IOpticalSystem sys)
        {
            Say("");
            Say("=== RESULTS ===");
            Say(F("EFFL      :  before={0:F4}  after={1:F4}   (focal length is reversal-invariant)",
                before["EFFL"][0], after["EFFL"][0]));
            if (!double.IsNaN(before["MF"][0]))
                Say(F("Merit fn  :  before={0:G6}  after={1:G6}", before["MF"][0], after["MF"][0]));
            Say("");
            Say("RMS spot radius vs field (micrometers, polychromatic, centroid):");
            Say("  Field   (x, y)          before      after");
            int nf = before["RMS"].Length;
            for (int i = 0; i < nf; i++)
            {
                Say(F("  {0,3}   ({1,5:F2},{2,6:F2})   {3,8:F3}   {4,8:F3}", i + 1,
                    before["FX"][i], before["FY"][i], before["RMS"][i] * 1000.0, after["RMS"][i] * 1000.0));
            }
            Say("(RMS may legitimately differ after a single reversal of an asymmetric design;");
            Say(" reversing twice must reproduce the original values exactly.)");
        }

        static void WriteReportFile(ZOSAPI.IZOSAPI_Application app, ZOSAPI.IOpticalSystem sys, string savedTo)
        {
            try
            {
                string basis = savedTo ?? sys.SystemFile;
                string path = string.IsNullOrEmpty(basis)
                    ? Path.Combine(app.ZemaxDataDir, "ReverseReport.txt")
                    : Path.Combine(Path.GetDirectoryName(basis),
                        Path.GetFileNameWithoutExtension(basis) + "_ReverseReport.txt");
                File.WriteAllLines(path, Report);
                Console.WriteLine();
                Console.WriteLine("Report written to: " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("WARNING: could not write report file: " + ex.Message);
            }
        }
    }
}

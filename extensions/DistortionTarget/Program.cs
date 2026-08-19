using System;
using System.Globalization;
using System.Text;
using ZOSAPI.Editors;
using ZOSAPI.Editors.NCE;

namespace DistortionTarget
{
    // Distortion Target — a ZOS-API User Extension.
    //
    // Builds a chrome-on-glass dot distortion target in non-sequential mode: a
    // glass plate carrying a square grid of chrome dots, replicated by an Array
    // object rather than placed as individual objects. Defaults reproduce Edmund
    // Optics 15963 (100 x 100 x 1.5 mm soda-lime, 0.250 mm dots on a 0.500 mm
    // pitch, reflective first-surface chromium), which is a model that was built
    // and traced against pre-registered acceptance criteria before this extension
    // existed; every default here is a measured configuration, not a guess.
    //
    // Usage:
    //   (no args)         ribbon mode: settings dialog, then build into the open system
    //   -nodialog         skip the dialog and use defaults / the flags given
    //   -n <int>          dots per side            (default 199)
    //   -pitch <mm>       dot centre-to-centre     (default 0.500)
    //   -dot <mm>         dot diameter             (default 0.250)
    //   -plate <mm>       substrate outer size     (default 100.0)
    //   -thick <mm>       substrate thickness      (default 1.50)
    //   -material <name>  substrate glass          (default B270)
    //   -coating <name>   chrome coating           (default CHROME_OD3)
    //   -film <mm>        chrome film thickness    (default 0.0001)
    //   -drawlimit <int>  array elements DRAWN     (default 2000)
    //   -rig              also add a collimated source and a detector
    //   -save <path>      save the built system here
    //   -file <path>      standalone mode: start our own OpticStudio first
    //
    // Three things in here are not obvious and each cost a real debugging session
    // on 2026 R1.03. They are commented where they bite: the dot cannot be a flat
    // object, parameter cells are typed, and RaysIgnoreObject is an enum.
    class Options
    {
        public int N = 199;
        public double Pitch = 0.500;
        public double DotDia = 0.250;
        public double Plate = 100.0;
        public double PlateT = 1.50;
        public string Material = "B270";
        public string Coating = "CHROME_OD3";
        public double Film = 0.0001;
        public int DrawLimit = 2000;
        public bool Rig = false;
        public string SavePath = null;
        public string FilePath = null;
        public bool NoDialog = false;

        // Which settings the command line set explicitly. The dialog seeds itself
        // from the last run, and without this that saved file silently outranks a
        // flag the user just typed: `-n 150` would open the dialog showing 199.
        // Last run beats the built-in defaults; an explicit flag beats both.
        public readonly System.Collections.Generic.HashSet<string> Explicit =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public double Span => (N - 1) * Pitch;
        public double OuterEdge => Span / 2.0 + DotDia / 2.0;
        public double Clearance => Plate / 2.0 - OuterEdge;
        public double ChromeFraction => Math.PI * (DotDia / 2.0) * (DotDia / 2.0) / (Pitch * Pitch);
    }

    class Program
    {
        static Options Opts = new Options();

        static void Main(string[] args)
        {
            ParseArgs(args);
            if (!ZemaxLocator.Initialize())
            {
                Console.WriteLine("FATAL: failed to locate an OpticStudio installation.");
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

        static void ParseArgs(string[] args)
        {
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next() => (i + 1 < args.Length) ? args[++i] : null;
                switch (a)
                {
                    case "-n": Opts.N = int.Parse(next(), ci); Opts.Explicit.Add("n"); break;
                    case "-pitch": Opts.Pitch = double.Parse(next(), ci); Opts.Explicit.Add("pitch"); break;
                    case "-dot": Opts.DotDia = double.Parse(next(), ci); Opts.Explicit.Add("dot"); break;
                    case "-plate": Opts.Plate = double.Parse(next(), ci); Opts.Explicit.Add("plate"); break;
                    case "-thick": Opts.PlateT = double.Parse(next(), ci); Opts.Explicit.Add("thick"); break;
                    case "-material": Opts.Material = next(); Opts.Explicit.Add("material"); break;
                    case "-coating": Opts.Coating = next(); Opts.Explicit.Add("coating"); break;
                    case "-film": Opts.Film = double.Parse(next(), ci); Opts.Explicit.Add("film"); break;
                    case "-drawlimit": Opts.DrawLimit = int.Parse(next(), ci); Opts.Explicit.Add("drawlimit"); break;
                    case "-rig": Opts.Rig = true; Opts.Explicit.Add("rig"); break;
                    case "-save": Opts.SavePath = next(); break;
                    case "-file": Opts.FilePath = next(); break;
                    case "-nodialog": Opts.NoDialog = true; break;
                }
            }
        }

        // Refuse a geometry that cannot be built rather than building a wrong one.
        // The overhang check is here because it caught a real error: reading the
        // vendor's "pattern size 100 x 100" as the grid span gives 201 dots, whose
        // outermost edge lands at 50.125 mm on a 100 mm plate — 0.125 mm off the
        // part. A target whose corner dots hang over the edge is not a target.
        static void Validate(Options o)
        {
            if (o.N < 2) throw new Exception("dots per side must be at least 2");
            if (o.Pitch <= 0 || o.DotDia <= 0) throw new Exception("pitch and dot diameter must be positive");
            if (o.DotDia >= o.Pitch)
                throw new Exception(string.Format(ci(),
                    "dots of {0} mm on a {1} mm pitch would touch or overlap", o.DotDia, o.Pitch));
            if (o.PlateT <= 0) throw new Exception("substrate thickness must be positive");
            if (o.Film <= 0) throw new Exception("chrome film thickness must be positive");
            if (o.OuterEdge >= o.Plate / 2.0)
                throw new Exception(string.Format(ci(),
                    "{0} x {0} dots on a {1} mm pitch span {2:0.###} mm centre-to-centre, " +
                    "putting the outermost dot edge at {3:0.###} mm — outside a {4:0.###} mm " +
                    "plate. Reduce the count, the pitch, or enlarge the substrate.",
                    o.N, o.Pitch, o.Span, o.OuterEdge, o.Plate));
        }

        static CultureInfo ci() { return CultureInfo.InvariantCulture; }

        static void Run()
        {
            ZOSAPI.IZOSAPI_Application app = null;
            var connection = new ZOSAPI.ZOSAPI_Connection();
            bool standalone = !string.IsNullOrEmpty(Opts.FilePath) || Opts.SavePath != null && Opts.NoDialog;

            if (standalone)
            {
                app = connection.CreateNewApplication();
                if (app == null || app.PrimarySystem == null || !app.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                if (!string.IsNullOrEmpty(Opts.FilePath) && !app.PrimarySystem.LoadFile(Opts.FilePath, false))
                {
                    app.CloseApplication();
                    throw new Exception("could not load " + Opts.FilePath);
                }
            }
            else
            {
                try { app = connection.ConnectToApplication(); } catch { app = null; }
                if (app == null)
                {
                    try { app = connection.ConnectAsExtension(0); } catch { app = null; }
                }
                // A stub with no PrimarySystem is NOT a connection: ConnectAsExtension
                // returns a live-looking application with PrimarySystem == null when no
                // OpticStudio is listening, and every later call then fails in a way that
                // reads like a licence fault instead of a missing host.
                if (app == null || app.PrimarySystem == null)
                    throw new Exception("could not connect to OpticStudio (use the Programming ribbon or Interactive Extension)");
                if (!app.IsValidLicenseForAPI)
                    throw new Exception("license is not valid for ZOS-API: " + app.LicenseStatus);
            }

            try
            {
                // A ribbon run gets no command line, so the dialog is the only place
                // these values can be set. -nodialog restores silent behaviour for
                // scripted runs.
                if (!Opts.NoDialog && !standalone)
                {
                    if (!TargetSettingsDialog.Show(Opts)) return;
                }
                Validate(Opts);
                Build(app);
            }
            finally
            {
                if (standalone) app.CloseApplication();
                else
                {
                    app.ProgressPercent = 100;
                    if (string.IsNullOrEmpty(app.ProgressMessage) || !app.ProgressMessage.StartsWith("Done"))
                        app.ProgressMessage = "Distortion target built.";
                }
            }
        }

        // Parameter cells are TYPED, and the wrong accessor is a hard error rather
        // than a coercion: an Array object's counts and draw limit are Integer, and
        // DoubleValue throws ArgumentException on them — from the getter as well as
        // the setter, so the type cannot be discovered by reading the cell first.
        // cell.DataType is the only safe probe.
        static void Par(INCERow row, int n, double value)
        {
            var col = (ObjectColumn)Enum.Parse(typeof(ObjectColumn), "Par" + n.ToString(ci()));
            var cell = row.GetObjectCell(col);
            if (cell.DataType == CellDataType.Integer)
            {
                if (value != Math.Floor(value))
                    throw new Exception("Par" + n + " is an integer cell; refusing to truncate " + value);
                cell.IntegerValue = (int)value;
            }
            else cell.DoubleValue = value;
        }

        static INCERow NewObject(INonSeqEditor nce, int index, ObjectType type)
        {
            if (index > nce.NumberOfObjects + 1)
                throw new Exception("cannot insert at " + index + "; editor holds " + nce.NumberOfObjects);
            var row = nce.InsertNewObjectAt(index);
            if (row == null) throw new Exception("InsertNewObjectAt(" + index + ") returned null");
            row.ChangeType(row.GetObjectTypeSettings(type));
            return row;
        }

        static void Build(ZOSAPI.IZOSAPI_Application app)
        {
            var sysm = app.PrimarySystem;
            var o = Opts;

            sysm.New(false);
            sysm.MakeNonSequential();
            sysm.SystemData.Units.LensUnits = ZOSAPI.SystemData.ZemaxSystemUnits.Millimeters;

            var nce = sysm.NCE;

            var glass = NewObject(nce, 1, ObjectType.RectangularVolume);
            glass.Comment = "substrate " + o.Plate.ToString("0.##", ci()) + " sq x " + o.PlateT.ToString("0.##", ci());
            glass.Material = o.Material;
            Par(glass, 1, o.Plate / 2.0);
            Par(glass, 2, o.Plate / 2.0);
            Par(glass, 3, o.PlateT);
            Par(glass, 4, o.Plate / 2.0);
            Par(glass, 5, o.Plate / 2.0);
            glass.ZPosition = 0.0;

            // The dot is a thin CYLINDER VOLUME, not a flat disc, and that is forced:
            // assigning a Coating to a single-face flat object (Annulus, Ellipse,
            // Rectangle, CylinderPipe) is SILENTLY IGNORED on 2026 R1.03 — no
            // exception, and the property still reads 'None' afterwards, for any
            // coating name including stock ones. Only multi-face solids and
            // PolygonObject accept one. A flat dot therefore blocks light but carries
            // no reflectance at all, which is exactly wrong for a part whose datasheet
            // headline is "reflective first surface chromium".
            var dot = NewObject(nce, 2, ObjectType.CylinderVolume);
            dot.Comment = "chrome dot (array parent)";
            Par(dot, 1, o.DotDia / 2.0);   // Front R
            Par(dot, 2, o.Film);           // Z Length
            Par(dot, 3, o.DotDia / 2.0);   // Back R
            dot.ZPosition = -o.Film;       // back face lands on the glass front face at z = 0

            // Face 1 is the FRONT face (0 = Side Faces, 2 = Back Face). Coating the
            // wrong face is as silent as not coating at all, so read it back.
            var face = dot.CoatScatterData.GetFaceData(1);
            face.Coating = o.Coating;
            if (face.Coating != o.Coating)
                throw new Exception("coating '" + o.Coating + "' did not take — it reads '" +
                    face.Coating + "'. Is it in COATING.DAT? (Libraries > Coatings, then Reload.)");

            // Not a bool: RaysIgnoreObject is RaysIgnoreObjectType {Never, Always,
            // OnLaunch}. The parent is a template the Array replicates; if rays did not
            // ignore it, it would be traced in its own right and double-count one dot.
            dot.TypeData.RaysIgnoreObject = RaysIgnoreObjectType.Always;
            dot.DrawData.DoNotDrawObject = true;

            var arr = NewObject(nce, 3, ObjectType.Array);
            arr.Comment = o.N + " x " + o.N + " dot array";
            Par(arr, 1, 2);              // Parent Object #
            Par(arr, 2, o.N);            // Number X'
            Par(arr, 3, o.N);            // Number Y'
            Par(arr, 4, 1);              // Number Z'
            Par(arr, 5, o.Pitch);        // Delta1 X'
            Par(arr, 6, o.Pitch);        // Delta1 Y'
            Par(arr, 7, 0.0);            // Delta1 Z'
            Par(arr, 8, 1); Par(arr, 9, 0); Par(arr, 10, 0);    // X' axis
            Par(arr, 11, 0); Par(arr, 12, 1); Par(arr, 13, 0);  // Y' axis
            Par(arr, 14, 0); Par(arr, 15, 0); Par(arr, 16, 1);  // Z' axis
            Par(arr, 20, o.DrawLimit);   // Draw Limit — DRAWING ONLY, no effect on any trace
            // array indices run 0..n-1, so shift the object to centre the pattern
            arr.XPosition = -o.Span / 2.0;
            arr.YPosition = -o.Span / 2.0;
            arr.ZPosition = 0.0;

            if (o.Rig) AddRig(nce, o);

            if (!string.IsNullOrEmpty(o.SavePath))
            {
                // SaveAs resolves in the OpticStudio process, not this one, so a bare
                // relative name can write nowhere at all while reporting success.
                string full = System.IO.Path.GetFullPath(o.SavePath);
                sysm.SaveAs(full);
                if (!System.IO.File.Exists(full))
                    throw new Exception("SaveAs reported no error but wrote no file at " + full);
                Console.WriteLine("saved " + full);
            }

            Console.WriteLine(Summary(o));
        }

        static void AddRig(INonSeqEditor nce, Options o)
        {
            var src = NewObject(nce, nce.NumberOfObjects + 1, ObjectType.SourceRectangle);
            src.Comment = "collimated beam";
            double half = Math.Min(45.0, o.Span / 2.0 - o.Pitch);
            Par(src, 1, 20); Par(src, 2, 1000000); Par(src, 3, 1.0);
            Par(src, 4, 0); Par(src, 5, 0);
            Par(src, 6, half); Par(src, 7, half);
            Par(src, 8, 0.0); Par(src, 9, 0.0); Par(src, 10, 0.0);
            src.ZPosition = -10.0;

            var det = NewObject(nce, nce.NumberOfObjects + 1, ObjectType.DetectorRectangle);
            det.Comment = "transmitted power";
            Par(det, 1, o.Plate / 2.0); Par(det, 2, o.Plate / 2.0);
            Par(det, 3, 100); Par(det, 4, 100); Par(det, 5, 0);
            det.ZPosition = o.PlateT + 10.0;
        }

        static string Summary(Options o)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(ci(), "built {0} x {0} = {1:n0} dots, {2} mm pitch, {3} mm dots",
                o.N, (long)o.N * o.N, o.Pitch, o.DotDia));
            sb.AppendLine(string.Format(ci(), "  pattern span {0:0.###} mm, outermost dot edge {1:0.###} mm, " +
                "clearance to face {2:0.###} mm", o.Span, o.OuterEdge, o.Clearance));
            sb.AppendLine(string.Format(ci(), "  chrome fill {0:0.0000}, open {1:0.0000}",
                o.ChromeFraction, 1.0 - o.ChromeFraction));
            sb.AppendLine(o.N % 2 == 1
                ? "  odd count: one dot lands on the optical axis"
                : "  EVEN count: no dot on axis, the grid straddles it");
            sb.AppendLine(string.Format(ci(), "  draw limit {0:n0} of {1:n0} — drawing only, no traced result depends on it",
                o.DrawLimit, (long)o.N * o.N));
            sb.Append("  radiometry needs ray splitting ON: without it OpticStudio applies " +
                "no coating, so the chrome neither blocks nor reflects");
            return sb.ToString();
        }
    }
}

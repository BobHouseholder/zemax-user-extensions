using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace MoldStress
{
    /// <summary>
    /// Connecting, and reading the moulded elements out of the Lens Data Editor.
    /// Same two paths as the other extensions in this repo: standalone when a
    /// -file is given, otherwise attach to the running OpticStudio so a ribbon
    /// run works on whatever is open.
    /// </summary>
    internal static class Session
    {
        /// <summary>
        /// Must run before any method whose BODY mentions a ZOSAPI type, because
        /// the JIT resolves assemblies at method entry, not at first use. Calling
        /// it from inside Connect() would already be too late - Connect's own
        /// frame needs ZOSAPI_Interfaces resolved before its first line runs, and
        /// the failure reads as "could not load file or assembly", which looks
        /// like a deployment problem rather than an ordering one.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void Locate()
        {
            if (!ZemaxLocator.Initialize())
                throw new Exception("could not locate an OpticStudio installation; " +
                                    "set ZEMAX_ROOT or install OpticStudio");
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static ZOSAPI.IZOSAPI_Application Connect(string filePath)
        {
            var connection = new ZOSAPI.ZOSAPI_Connection();
            ZOSAPI.IZOSAPI_Application app;

            if (!string.IsNullOrEmpty(filePath))
            {
                app = connection.CreateNewApplication();
                if (app == null || !app.IsValidLicenseForAPI)
                    throw new Exception("could not start a standalone OpticStudio instance");
                if (!app.PrimarySystem.LoadFile(filePath, false))
                {
                    app.CloseApplication();
                    throw new Exception("could not load " + filePath);
                }
                return app;
            }

            try { app = connection.ConnectToApplication(); } catch { app = null; }
            if (app == null) { try { app = connection.ConnectAsExtension(0); } catch { app = null; } }
            if (app == null)
                throw new Exception("could not connect to OpticStudio " +
                                    "(run from the Programming ribbon, or pass -file)");
            if (!app.IsValidLicenseForAPI)
                throw new Exception("license is not valid for ZOS-API: " + app.LicenseStatus);
            return app;
        }

        /// <summary>
        /// Every element made of a MOULDABLE material, with its cavity geometry.
        ///
        /// "Mouldable" is decided by the material carrying a BD record we wrote,
        /// or by the caller naming it. Guessing from the index would sweep in
        /// every glass in the design, and silently moulding a glass singlet is a
        /// worse failure than skipping a plastic one.
        /// </summary>
        public static List<MouldedElement> FindElements(ZOSAPI.IOpticalSystem sys,
                                                        IEnumerable<string> extraMaterials)
        {
            var known = new HashSet<string>(Polymers.All.Select(p => p.Name),
                                            StringComparer.OrdinalIgnoreCase);
            if (extraMaterials != null)
                foreach (var m in extraMaterials)
                {
                    // "NAME=POLYMER" registers an alias; bare "NAME" is only
                    // useful if the table already has an entry of that name.
                    int eq = m.IndexOf('=');
                    if (eq > 0)
                    {
                        string real = m.Substring(0, eq).Trim();
                        string target = m.Substring(eq + 1).Trim();
                        Polymers.Aliases[real] = target;
                        known.Add(real);

                        // ALIASING IS BORROWING, AND A CONTESTED CONSTANT MUST
                        // NOT BE BORROWED SILENTLY. The alias map is a single
                        // command-line flag that gives one grade another grade's
                        // measured constants; that is the intended feature, and
                        // it is exactly how a disputed number reaches a material
                        // nobody checked it against.
                        try
                        {
                            var tp = Polymers.ByName(target);
                            if (!string.IsNullOrEmpty(tp.CMeltContested))
                                Console.WriteLine(
                                    "  WARNING: '" + real + "' borrows the melt stress-optical "
                                    + "coefficient of " + tp.Name + ", which is CONTESTED. "
                                    + tp.CMeltContested);
                            else if (tp.Provisional)
                                Console.WriteLine(
                                    "  NOTE: '" + real + "' borrows PROVISIONAL constants from "
                                    + tp.Name + " - representative of the family, not measured "
                                    + "for this grade.");
                        }
                        catch (ArgumentException)
                        {
                            // Unknown target is reported later by ByName at the
                            // point of use, with the full list of known names.
                        }
                    }
                    else known.Add(m.Trim());
                }

            var lde = sys.LDE;
            var found = new List<MouldedElement>();

            for (int i = 1; i < lde.NumberOfSurfaces - 1; i++)
            {
                var s = lde.GetSurfaceAt(i);
                string mat = (s.Material ?? "").Trim();
                if (mat.Length == 0) continue;                 // air gap
                if (!known.Contains(mat)) continue;

                var next = lde.GetSurfaceAt(i + 1);
                var e = new MouldedElement
                {
                    FrontSurface = i,
                    BackSurface = i + 1,
                    Material = mat,
                    CentreThicknessMm = s.Thickness,
                    SemiDiameterMm = s.SemiDiameter,
                    FrontRadiusMm = s.Radius,
                    BackRadiusMm = next.Radius,
                };
                // The cavity is bounded by the smaller of the two apertures.
                e.SemiDiameterMm = Math.Min(s.SemiDiameter, next.SemiDiameter);
                e.EdgeThicknessMm = e.ThicknessAt(e.SemiDiameterMm);
                // Both bounding surfaces, because the cavity is the gap between
                // them and either one being non-spherical makes the profile wrong.
                string dFront = ShapeDeparture(SurfaceTypeName(s), ConicOf(s), ParsOf(s));
                string dBack = ShapeDeparture(SurfaceTypeName(next), ConicOf(next), ParsOf(next));
                if (dFront != null || dBack != null)
                    e.ShapeDeparture = string.Join(" | ", new[]
                    {
                        dFront == null ? null : "surface " + i + ": " + dFront,
                        dBack == null ? null : "surface " + (i + 1) + ": " + dBack,
                    }.Where(x => x != null).ToArray());

                e.Gate = Gating.DefaultGate(e);
                e.PartingLineZMm = Gating.DefaultPartingLineZ(e);
                found.Add(e);
            }
            return found;
        }

        /// <summary>
        /// Names how a surface departs from a sphere, or returns null if it does
        /// not. PURE, so it can be tested without OpticStudio running - which is
        /// the whole reason it takes loose values rather than an ILDERow.
        ///
        /// WHY THIS EXISTS. This tool reads only the base radius, and every
        /// surface therefore becomes a pure sphere. That substitution was SILENT
        /// until 2026-08-20: an aspheric lens produced a full, plausible run built
        /// on a geometry it does not have. Moulded optics are asphere-heavy almost
        /// by definition - asphericity for free is the economic reason to mould
        /// rather than grind - so the silently-wrong case is the likely case, and
        /// the tool's own validation suite never tests one: its only per-lens
        /// reference case is plano-convex.
        ///
        /// Surface types other than Standard/EvenAspheric/OddAsphere are refused
        /// outright rather than inspected, because this function cannot know what
        /// their parameters mean.
        /// </summary>
        public static string ShapeDeparture(string typeName, double conic, double[] pars)
        {
            var why = new List<string>();
            bool even = string.Equals(typeName, "EvenAspheric", StringComparison.OrdinalIgnoreCase);
            bool odd = string.Equals(typeName, "OddAsphere", StringComparison.OrdinalIgnoreCase);
            bool standard = string.Equals(typeName, "Standard", StringComparison.OrdinalIgnoreCase);

            if (!standard && !even && !odd)
                why.Add("surface type " + (typeName ?? "?") + " is not one this solver can read");

            if (Math.Abs(conic) > 1e-12)
                why.Add(string.Format(CultureInfo.InvariantCulture, "conic {0:F6}", conic));

            if ((even || odd) && pars != null)
            {
                var terms = new List<string>();
                for (int i = 0; i < pars.Length; i++)
                    if (Math.Abs(pars[i]) > 0.0)
                        terms.Add(string.Format(CultureInfo.InvariantCulture,
                            "r^{0} {1:E2}", even ? 2 * (i + 1) : (i + 1), pars[i]));
                if (terms.Count > 0)
                    why.Add("aspheric terms " + string.Join(", ", terms.ToArray()));
            }
            return why.Count == 0 ? null : string.Join("; ", why.ToArray());
        }

        /// <summary>The three LDE reads the detector needs, each guarded - some
        /// surface types throw on these rather than returning a default, which is
        /// why the sibling extension wraps every one of them too.</summary>
        private static string SurfaceTypeName(ZOSAPI.Editors.LDE.ILDERow row)
        {
            try { return row.Type.ToString(); } catch { return null; }
        }

        private static double ConicOf(ZOSAPI.Editors.LDE.ILDERow row)
        {
            try { return row.Conic; } catch { return 0.0; }
        }

        private static double[] ParsOf(ZOSAPI.Editors.LDE.ILDERow row)
        {
            var v = new double[8];
            for (int k = 1; k <= 8; k++)
            {
                try
                {
                    var col = (ZOSAPI.Editors.LDE.SurfaceColumn)Enum.Parse(
                        typeof(ZOSAPI.Editors.LDE.SurfaceColumn), "Par" + k);
                    v[k - 1] = row.GetSurfaceCell(col).DoubleValue;
                }
                catch { v[k - 1] = 0.0; }
            }
            return v;
        }

        public static void Describe(MouldedElement e)
        {
            Console.WriteLine(string.Format(
                "  surfaces {0}-{1}  {2,-18} CT {3,6:F3}  ET {4,6:F3}  semi {5,6:F3}",
                e.FrontSurface, e.BackSurface, e.Material,
                e.CentreThicknessMm, e.EdgeThicknessMm, e.SemiDiameterMm));
            Console.WriteLine("      gate     " + e.Gate);
            Console.WriteLine(string.Format(
                "      parting  z = {0:F4} mm from the front vertex", e.PartingLineZMm));
            if (e.ShapeDeparture != null)
                Console.WriteLine("      NOT SPHERICAL: " + e.ShapeDeparture);
            if (e.EdgeThicknessMm <= 0)
                Console.WriteLine("      WARNING: edge thickness is not positive - " +
                                  "the surfaces interpenetrate inside the clear aperture");
        }
    }
}

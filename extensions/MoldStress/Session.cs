using System;
using System.Collections.Generic;
using System.Linq;

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
                e.Gate = Gating.DefaultGate(e);
                e.PartingLineZMm = Gating.DefaultPartingLineZ(e);
                found.Add(e);
            }
            return found;
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
            if (e.EdgeThicknessMm <= 0)
                Console.WriteLine("      WARNING: edge thickness is not positive - " +
                                  "the surfaces interpenetrate inside the clear aperture");
        }
    }
}

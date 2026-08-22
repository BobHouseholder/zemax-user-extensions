using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MoldStress
{
    /// <summary>One moulded element, as MoldStress needs to see it.</summary>
    internal sealed class MouldedElement
    {
        public int FrontSurface;            // LDE index of the surface where glass begins
        public int BackSurface;
        public string Material;
        public double CentreThicknessMm;
        public double SemiDiameterMm;
        public double EdgeThicknessMm;
        public double FrontRadiusMm;        // 0 => plano
        public double BackRadiusMm;

        /// <summary>
        /// The REST of the surface shape, read from the LDE since 2026-08-20.
        /// Before that only the base radii were read and every surface became a
        /// pure sphere - which for a moulded optic is usually the wrong part,
        /// since asphericity at no extra unit cost is the economic reason to
        /// mould rather than grind.
        ///
        /// `Pars` are LDE parameter cells 1..8. An EVEN asphere reads them as
        /// the coefficients of r^2, r^4, r^6 ...; an ODD asphere as r^1, r^2,
        /// r^3 ... Those are the conventions the AthermalScan extension already
        /// evaluates against this same API.
        /// </summary>
        public double FrontConic, BackConic;
        public double[] FrontPars, BackPars;
        public bool FrontIsEvenAsphere, BackIsEvenAsphere;
        public bool FrontIsOddAsphere, BackIsOddAsphere;

        public GateSpec Gate;
        public double PartingLineZMm;       // local z of the parting plane, from the front vertex

        /// <summary>
        /// How this element's surfaces depart from a plain sphere, or null when
        /// they do not. INFORMATIONAL since 2026-08-20 - conics and even/odd
        /// aspheric terms are now read and modelled, so this is reported rather
        /// than refused. It is still worth printing: the cavity profile is the
        /// whole geometry input, feeding the fill time, the wall thickness, the
        /// freeze history and the z-coordinates written into STAR, so the user
        /// should see which shape the run was built on.
        ///
        /// `ShapeUnreadable` is the half that IS still refused: a surface type
        /// whose parameters this solver cannot interpret at all, where the base
        /// radius is the only thing it can fall back on.
        /// </summary>
        public string ShapeDeparture;
        public string ShapeUnreadable;

        /// <summary>
        /// The radius the STAR EXPORT must cover, as opposed to the radius the
        /// physics runs on. Zero means "same as SemiDiameterMm".
        ///
        /// WHY THEY DIFFER (found 2026-08-22, in OpticStudio's Multiphysics
        /// Data Loader): `SemiDiameterMm` is the smaller of the two surfaces'
        /// OPTICAL semi-diameters, which bounds the solve - but the loader draws
        /// the part to its MECHANICAL semi-diameter, and a moulded lens has a
        /// flange, so the mechanical aperture routinely exceeds the clear one.
        /// A cloud that stops at the clear aperture leaves the flange annulus
        /// empty, STAR is left to extrapolate there, and in the loader the data
        /// visibly fails to fill the lens.
        /// </summary>
        public double ExportSemiDiameterMm;

        /// <summary>The larger of the mechanical semi-diameters, floored at the
        /// physics radius; guards let a zero or unreadable MEMA fall back
        /// harmlessly. Pure, so the choice is testable without a session.</summary>
        public static double ExportRadius(double mechFront, double mechBack, double optical)
        {
            double m = 0.0;
            if (!double.IsNaN(mechFront) && mechFront > 0) m = Math.Max(m, mechFront);
            if (!double.IsNaN(mechBack) && mechBack > 0) m = Math.Max(m, mechBack);
            return Math.Max(m, optical);
        }

        /// <summary>
        /// Thickness of the cavity at radius r, from the two surface sags. This
        /// is the whole reason the estimator needs no mesh: OpticStudio already
        /// holds the cavity profile exactly.
        /// </summary>
        public double ThicknessAt(double r)
        {
            return CentreThicknessMm - SagFrontAt(r) + SagBackAt(r);
        }

        /// <summary>The two surface sags including conic and aspheric terms.
        /// Exposed because StarFiles writes these same z-coordinates into the
        /// export: a cavity solved on one shape and exported on another would be
        /// worse than either one used consistently.</summary>
        public double SagFrontAt(double r)
        {
            return Sag(FrontRadiusMm, FrontConic, FrontPars,
                       FrontIsEvenAsphere, FrontIsOddAsphere, r);
        }

        public double SagBackAt(double r)
        {
            return Sag(BackRadiusMm, BackConic, BackPars,
                       BackIsEvenAsphere, BackIsOddAsphere, r);
        }

        /// <summary>
        /// The thinnest point of the cavity anywhere inside the clear aperture,
        /// and where it is.
        ///
        /// FOR A SPHERE THIS IS ALWAYS AN END POINT - the thickness is monotonic
        /// in r - which is why the centre and edge thicknesses were sufficient
        /// while every surface was a sphere. An asphere has no such guarantee: a
        /// steepening high-order term can pinch the wall in the middle of the
        /// aperture, and that pinch is what sets the fill and the freeze time,
        /// not either end. Scanned rather than solved, because the sag is a
        /// polynomial of arbitrary order plus a square root.
        /// </summary>
        public double MinThicknessMm(out double atRadiusMm)
        {
            const int n = 129;
            double best = double.MaxValue; atRadiusMm = 0.0;
            for (int i = 0; i < n; i++)
            {
                double r = SemiDiameterMm * i / (n - 1.0);
                double h = ThicknessAt(r);
                if (h < best) { best = h; atRadiusMm = r; }
            }
            return best;
        }

        /// <summary>The sphere. Kept as the control: the full form below must
        /// reproduce it bit-for-bit at zero conic with no terms, and the
        /// self-test asserts exactly that.</summary>
        internal static double Sag(double radius, double r)
        {
            if (radius == 0 || double.IsInfinity(radius)) return 0.0;
            double c = 1.0 / radius;
            double arg = 1.0 - c * c * r * r;
            if (arg <= 0) return radius;            // beyond the hemisphere: clamp
            return c * r * r / (1.0 + Math.Sqrt(arg));
        }

        /// <summary>
        /// THE FULL SAG - conic plus even or odd aspheric terms:
        ///
        ///     z = c r^2 / (1 + sqrt(1 - (1+k) c^2 r^2))  +  SUM a_i r^p
        ///
        /// with p = 2i for an even asphere and p = i for an odd one.
        ///
        /// The conic sits in the DENOMINATOR, so it is not a small correction to
        /// the leading term: k = -1 (parabola) removes the r-dependence of the
        /// square root entirely, and an oblate surface (k > 0) runs out of real
        /// surface at a SMALLER radius than the sphere of the same base radius.
        ///
        /// The clamp means what it meant in the spherical form - r is off the
        /// end of the surface - but the value it returns is not the same. The
        /// sphere's clamp returns `radius`, its pole; the conic's pole is at
        /// r^2 = R^2/(1+k), where the sag is R/(1+k). At k = 0 that IS `radius`,
        /// which is what keeps the sphere case bit-identical. k <= -1 can never
        /// reach the clamp: 1+k <= 0 leaves the argument at or above 1.
        /// </summary>
        internal static double Sag(double radius, double conic, double[] pars,
                                   bool evenAsphere, bool oddAsphere, double r)
        {
            double z = 0.0;
            if (!(radius == 0 || double.IsInfinity(radius)))
            {
                double c = 1.0 / radius;
                double arg = 1.0 - (1.0 + conic) * c * c * r * r;
                z = arg <= 0
                    ? radius / (1.0 + conic)               // the pole; see above
                    : c * r * r / (1.0 + Math.Sqrt(arg));
            }
            if ((evenAsphere || oddAsphere) && pars != null)
                for (int i = 0; i < pars.Length; i++)
                {
                    if (pars[i] == 0.0) continue;
                    int power = evenAsphere ? 2 * (i + 1) : (i + 1);
                    z += pars[i] * Math.Pow(r, power);
                }
            return z;
        }
    }

    internal enum GateKind { EdgeRadial, RingAllRound, FilmEdge }

    internal sealed class GateSpec
    {
        public GateKind Kind;
        public double AzimuthDeg;           // where the gate sits, 0 = +Y
        public double WidthMm;
        public double ThicknessMm;
        public bool IsDefault;              // false once a config file has overridden it

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} at {1:F1} deg, {2:F2} x {3:F2} mm{4}",
                Kind, AzimuthDeg, WidthMm, ThicknessMm, IsDefault ? " (default)" : " (override)");
        }
    }

    /// <summary>
    /// Chooses where the melt enters and where the tool splits, from geometry
    /// alone, and lets a config file overrule it per element.
    ///
    /// The defaults are conventional moulding practice, not a solve: a single
    /// edge gate on the rim at +Y, sized off the local wall thickness, and the
    /// parting line at the plane of maximum diameter - which for a lens is where
    /// the two surfaces meet the edge, and is the only plane the tool can open
    /// through without an undercut.
    ///
    /// The azimuth matters more than it looks. The registered null control for
    /// this whole goal moves the gate to the opposite side and requires the
    /// retardance maximum to move with it, so a gate azimuth that is ignored
    /// downstream makes the null unfalsifiable.
    /// </summary>
    internal static class Gating
    {
        public const double DefaultAzimuthDeg = 0.0;

        public static GateSpec DefaultGate(MouldedElement e)
        {
            double edge = Math.Max(e.EdgeThicknessMm, 0.1);
            bool ringPreferred = e.SemiDiameterMm > 12.0;   // large parts fill unevenly from a point

            return new GateSpec
            {
                Kind = ringPreferred ? GateKind.RingAllRound : GateKind.EdgeRadial,
                AzimuthDeg = DefaultAzimuthDeg,
                // Conventional starting point: gate land about 60% of the local
                // wall, and a width a few times its own depth so it freezes after
                // the cavity rather than before it.
                ThicknessMm = 0.6 * edge,
                WidthMm = ringPreferred ? 2 * Math.PI * e.SemiDiameterMm : 3.0 * 0.6 * edge,
                IsDefault = true,
            };
        }

        /// <summary>
        /// The parting plane. For a biconvex or meniscus element the maximum
        /// diameter is at the rim, so the split is at the axial position where
        /// the edge sits: the front vertex plus the front sag at the semi-
        /// diameter. A plano-convex element parts at its flat face.
        /// </summary>
        public static double DefaultPartingLineZ(MouldedElement e)
        {
            double zFrontEdge = e.SagFrontAt(e.SemiDiameterMm);
            double zBackEdge = e.CentreThicknessMm + e.SagBackAt(e.SemiDiameterMm);
            return 0.5 * (zFrontEdge + zBackEdge);
        }

        /// <summary>
        /// Per-element overrides, one line each:
        ///     surface=3 azimuth=180 kind=ring width=1.2 thickness=0.5 parting=2.4
        /// Unknown keys are refused rather than ignored - a typo that silently
        /// leaves the default in place is the failure mode this format exists to
        /// avoid.
        /// </summary>
        public static void ApplyOverrides(IEnumerable<MouldedElement> elements, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) throw new FileNotFoundException("gate config not found", path);

            int lineNo = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tok in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = tok.Split('=');
                    if (bits.Length != 2)
                        throw new FormatException("gate config line " + lineNo + ": expected key=value, got '" + tok + "'");
                    kv[bits[0]] = bits[1];
                }
                if (!kv.ContainsKey("surface"))
                    throw new FormatException("gate config line " + lineNo + ": no surface= key");

                int surf = int.Parse(kv["surface"], CultureInfo.InvariantCulture);
                var el = elements.FirstOrDefault(x => x.FrontSurface == surf);
                if (el == null)
                    throw new FormatException("gate config line " + lineNo +
                        ": surface " + surf + " is not the front of a moulded element");

                foreach (var key in kv.Keys)
                {
                    string v = kv[key];
                    switch (key.ToLowerInvariant())
                    {
                        case "surface": break;
                        case "azimuth": el.Gate.AzimuthDeg = D(v); el.Gate.IsDefault = false; break;
                        case "width": el.Gate.WidthMm = D(v); el.Gate.IsDefault = false; break;
                        case "thickness": el.Gate.ThicknessMm = D(v); el.Gate.IsDefault = false; break;
                        case "parting": el.PartingLineZMm = D(v); break;
                        case "kind":
                            el.Gate.Kind = ParseKind(v, lineNo);
                            el.Gate.IsDefault = false;
                            break;
                        default:
                            throw new FormatException("gate config line " + lineNo +
                                ": unknown key '" + key + "'");
                    }
                }
            }
        }

        private static GateKind ParseKind(string v, int lineNo)
        {
            switch (v.ToLowerInvariant())
            {
                case "edge": return GateKind.EdgeRadial;
                case "ring": return GateKind.RingAllRound;
                case "film": return GateKind.FilmEdge;
                default:
                    throw new FormatException("gate config line " + lineNo +
                        ": kind must be edge, ring or film");
            }
        }

        private static double D(string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}

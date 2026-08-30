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

    /// <summary>
    /// FanEdge added 2026-08-29. It is the gate the one comparative study on a
    /// real lens actually chose: Nagy et al. (PMC11360770) put six geometries on
    /// a 16.5 mm planoconvex PC lens and picked a fan on peak shear stress,
    /// 0.46 MPa against 0.93 MPa for a point gate. It is NOT the default here,
    /// deliberately - that study measured sink marks and deformation and states
    /// plainly that "optical properties were not examined", so there is no
    /// measured optical ground to move every number in this project onto.
    /// Available under `kind=fan`; the default stays EdgeRadial.
    /// </summary>
    internal enum GateKind { EdgeRadial, RingAllRound, FilmEdge, FanEdge }

    internal sealed class GateSpec
    {
        public GateKind Kind;
        public double AzimuthDeg;           // where the gate sits, 0 = +Y
        public double WidthMm;
        public double ThicknessMm;
        public bool IsDefault;              // false once a config file has overridden it

        /// <summary>
        /// WHERE THE MOUNTING DATUM SITS, in the same convention as AzimuthDeg;
        /// NaN when nobody has said. A lens is located in its barrel by a
        /// reference surface on the rim, and the gate has to be cut off that rim
        /// after moulding - so the constraint on gate azimuth is not the clear
        /// aperture, which no rim gate touches, but the DATUM, which the cutting
        /// tool must not reach.
        ///
        /// US 5,975,882 is explicit about it: "should the reference surface be
        /// damaged by a cutter blade in a gate-cut operation, the resulting
        /// surface flaw could make it difficult to assemble the optical component
        /// into an optically aligned position on a lens holder." A Zemax file
        /// carries no datum, so this can only ever be an input.
        /// </summary>
        public double DatumAzimuthDeg = double.NaN;

        /// <summary>
        /// True while AzimuthDeg is the arbitrary 0 deg rather than a choice.
        /// This matters because azimuth is NOT decorative - it reaches the
        /// exported field through StarFiles, and the registered null control for
        /// this whole goal requires the retardance maximum to move with it. A
        /// placeholder that reads like a decision is the thing to avoid.
        /// </summary>
        public bool AzimuthIsPlaceholder;

        public bool HasDatum { get { return !double.IsNaN(DatumAzimuthDeg); } }

        public override string ToString()
        {
            string note = IsDefault ? " (default)" : " (override)";
            if (AzimuthIsPlaceholder)
                note += ", azimuth is a PLACEHOLDER - no mounting datum given";
            else if (HasDatum)
            {
                // ONLY claim "opposite the datum" when it actually is. An
                // explicit azimuth overrules the datum, and a note that still
                // said "opposite" would be describing a placement the gate does
                // not have - which is worse than saying nothing.
                double want = AzimuthOppositeDatum(DatumAzimuthDeg);
                double got = ((AzimuthDeg % 360.0) + 360.0) % 360.0;
                note += Math.Abs(want - got) < 1e-9
                    ? string.Format(CultureInfo.InvariantCulture,
                        ", opposite a datum at {0:F1} deg", DatumAzimuthDeg)
                    : string.Format(CultureInfo.InvariantCulture,
                        ", set explicitly against a datum at {0:F1} deg", DatumAzimuthDeg);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} at {1:F1} deg, {2:F2} x {3:F2} mm{4}",
                Kind, AzimuthDeg, WidthMm, ThicknessMm, note);
        }

        /// <summary>
        /// The gate goes as far from the datum as the rim allows, which is
        /// diametrically opposite it. Normalised to [0, 360).
        /// </summary>
        public static double AzimuthOppositeDatum(double datumDeg)
        {
            double a = (datumDeg + 180.0) % 360.0;
            return a < 0.0 ? a + 360.0 : a;
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

        /// <summary>
        /// FLOW LENGTH OVER WALL THICKNESS, the ratio a moulder actually judges
        /// "will one gate fill this?" by. Above the limit the melt front is
        /// expected to freeze before it arrives and a rim ring is preferred to a
        /// single point.
        ///
        /// THIS NUMBER IS NOT SOURCED, and neither was what it replaced. Sweep 5
        /// (2026-08-29) went looking and found that gate guidance for imaging
        /// lenses is qualitative where it is optical and quantitative only where
        /// it is general moulding practice; nothing measured pins a threshold for
        /// an optical part. What changed is the SHAPE of the rule, not its
        /// pedigree: the old test was `SemiDiameterMm > 12.0`, an absolute size
        /// that says a 12 mm part of any thickness fills the same way, which is
        /// the one thing L/t makes obviously false. 150 is the middle of the
        /// range trade sources quote for easy-flow thermoplastics.
        ///
        /// It is deliberately loose enough not to disturb any published result:
        /// the validation triplet runs L/t of 3.0, 3.2 and 2.5, and both rules
        /// call all three an edge gate. The reference cases set their gates
        /// explicitly and never reach this code.
        /// </summary>
        public const double RingFlowLengthRatio = 150.0;

        /// <summary>
        /// L/t for a single rim gate: rim to rim over the mean wall. The mean is
        /// taken over AREA rather than radius, because the outer annuli carry
        /// most of the part and a radius-average would be dominated by a centre
        /// the melt reaches last.
        /// </summary>
        public static double FlowLengthRatio(MouldedElement e)
        {
            double semi = Math.Max(e.SemiDiameterMm, 1e-6);
            double area = 0.0, vol = 0.0;
            const int n = 200;
            for (int i = 0; i < n; i++)
            {
                double r0 = semi * i / n, r1 = semi * (i + 1) / n;
                double rm = 0.5 * (r0 + r1);
                double da = Math.PI * (r1 * r1 - r0 * r0);
                area += da;
                vol += e.ThicknessAt(rm) * da;
            }
            double tMean = (area > 0.0) ? vol / area : e.CentreThicknessMm;
            if (!(tMean > 1e-9)) return double.PositiveInfinity;
            return (2.0 * semi) / tMean;
        }

        /// <summary>
        /// The width each kind gets when nobody has specified one. Split out of
        /// DefaultGate so that changing the KIND in a config file resizes the
        /// gate the same way, rather than leaving an edge gate's width on a fan.
        ///
        /// Sourcing, honestly: the edge and ring rules are the ones this project
        /// has always used and sweep 5 could not find a source for either. The
        /// fan rule is new and is no better sourced - a quarter of the part
        /// width is ordinary fan practice and nothing measured pins it for an
        /// optical part. What IS defensible is the ORDER: a fan must be wider
        /// than the edge gate it replaces or it is not a fan, and the floor below
        /// enforces exactly that and nothing more.
        /// </summary>
        public static double DefaultWidthFor(GateKind kind, MouldedElement e, double landMm)
        {
            switch (kind)
            {
                case GateKind.RingAllRound:
                    return 2.0 * Math.PI * e.SemiDiameterMm;
                case GateKind.FilmEdge:
                    // A film gate spans the edge it sits on.
                    return 2.0 * e.SemiDiameterMm;
                case GateKind.FanEdge:
                    // STRICTLY WIDER THAN THE EDGE GATE IT REPLACES. The first
                    // version of this read `max(0.5 * semi, 3 * land)` and the
                    // self-test caught it: on a small thick-edged lens - semi 8,
                    // land 1.43 - half the semi-diameter is 4.00 and the edge
                    // gate is 4.29, so the fan came out IDENTICAL and `kind=fan`
                    // would have been an edge gate under another name. Doubling
                    // the edge width is the floor; the part-width term only takes
                    // over on parts big enough for it to matter.
                    return Math.Max(0.5 * e.SemiDiameterMm, 2.0 * 3.0 * landMm);
                default:
                    return 3.0 * landMm;
            }
        }

        public static GateSpec DefaultGate(MouldedElement e)
        {
            double edge = Math.Max(e.EdgeThicknessMm, 0.1);
            // WAS `SemiDiameterMm > 12.0`. A bare diameter cannot distinguish a
            // 24 mm plate 1 mm thick, which one point gate will not fill, from a
            // 24 mm lens 8 mm thick, which it will.
            bool ringPreferred = FlowLengthRatio(e) > RingFlowLengthRatio;

            return new GateSpec
            {
                Kind = ringPreferred ? GateKind.RingAllRound : GateKind.EdgeRadial,
                // 0 deg is arbitrary and is FLAGGED as arbitrary. Nothing in a
                // lens file says where the mounting datum is, so there is no
                // honest default here - only a placeholder that says so.
                AzimuthDeg = DefaultAzimuthDeg,
                AzimuthIsPlaceholder = true,
                DatumAzimuthDeg = double.NaN,
                // Conventional starting point: gate land about 60% of the local
                // wall, and a width a few times its own depth so it freezes after
                // the cavity rather than before it.
                ThicknessMm = 0.6 * edge,
                WidthMm = DefaultWidthFor(
                    ringPreferred ? GateKind.RingAllRound : GateKind.EdgeRadial, e, 0.6 * edge),
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
        ///
        /// `datum=<deg>` says where the mounting reference surface is, and the
        /// gate is then placed diametrically opposite it - which is the real
        /// constraint on a lens gate, since the rim carries the datum and the
        /// gate has to be cut off the rim. `azimuth=` still wins if both are
        /// given, because an explicit azimuth is a decision.
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

                // VALIDATE EVERY KEY FIRST, then apply in a FIXED ORDER. The keys
                // arrive in a dictionary, and `datum` and `azimuth` both write
                // AzimuthDeg - so applying them in enumeration order would make
                // the result depend on hash ordering. Explicit azimuth wins;
                // datum only places the gate when no azimuth was given.
                foreach (var key in kv.Keys)
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "surface": case "azimuth": case "datum": case "width":
                        case "thickness": case "parting": case "kind":
                            break;
                        default:
                            throw new FormatException("gate config line " + lineNo +
                                ": unknown key '" + key + "'");
                    }
                }

                string tmp;
                if (kv.TryGetValue("width", out tmp)) { el.Gate.WidthMm = D(tmp); el.Gate.IsDefault = false; }
                if (kv.TryGetValue("thickness", out tmp)) { el.Gate.ThicknessMm = D(tmp); el.Gate.IsDefault = false; }
                if (kv.TryGetValue("parting", out tmp)) { el.PartingLineZMm = D(tmp); }
                if (kv.TryGetValue("kind", out tmp))
                {
                    el.Gate.Kind = ParseKind(tmp, lineNo);
                    el.Gate.IsDefault = false;
                    // CHANGING THE KIND HAS TO RESIZE THE GATE, or `kind=fan` is
                    // an edge gate wearing a different name: the fill model reads
                    // WidthMm, not Kind, so a fan that kept the edge width would
                    // produce bit-identical output and the option would be a lie.
                    // An explicit width= on the same line still wins - not by
                    // ordering, which would be fragile, but because the resize is
                    // skipped outright when the line carries one.
                    if (!kv.ContainsKey("width"))
                        el.Gate.WidthMm = DefaultWidthFor(el.Gate.Kind, el, el.Gate.ThicknessMm);
                }
                if (kv.TryGetValue("datum", out tmp))
                {
                    el.Gate.DatumAzimuthDeg = D(tmp);
                    el.Gate.IsDefault = false;
                }
                if (kv.TryGetValue("azimuth", out tmp))
                {
                    // An explicit azimuth is a decision, so it is never a
                    // placeholder - even if it happens to equal 0.
                    el.Gate.AzimuthDeg = D(tmp);
                    el.Gate.AzimuthIsPlaceholder = false;
                    el.Gate.IsDefault = false;
                }
                else if (el.Gate.HasDatum)
                {
                    el.Gate.AzimuthDeg = GateSpec.AzimuthOppositeDatum(el.Gate.DatumAzimuthDeg);
                    el.Gate.AzimuthIsPlaceholder = false;
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
                case "fan": return GateKind.FanEdge;
                default:
                    throw new FormatException("gate config line " + lineNo +
                        ": kind must be edge, ring, film or fan");
            }
        }

        private static double D(string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}

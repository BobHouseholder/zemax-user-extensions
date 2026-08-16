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

        public GateSpec Gate;
        public double PartingLineZMm;       // local z of the parting plane, from the front vertex

        /// <summary>
        /// Thickness of the cavity at radius r, from the two surface sags. This
        /// is the whole reason the estimator needs no mesh: OpticStudio already
        /// holds the cavity profile exactly.
        /// </summary>
        public double ThicknessAt(double r)
        {
            return CentreThicknessMm - Sag(FrontRadiusMm, r) + Sag(BackRadiusMm, r);
        }

        internal static double Sag(double radius, double r)
        {
            if (radius == 0 || double.IsInfinity(radius)) return 0.0;
            double c = 1.0 / radius;
            double arg = 1.0 - c * c * r * r;
            if (arg <= 0) return radius;            // beyond the hemisphere: clamp
            return c * r * r / (1.0 + Math.Sqrt(arg));
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
            double zFrontEdge = MouldedElement.Sag(e.FrontRadiusMm, e.SemiDiameterMm);
            double zBackEdge = e.CentreThicknessMm + MouldedElement.Sag(e.BackRadiusMm, e.SemiDiameterMm);
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

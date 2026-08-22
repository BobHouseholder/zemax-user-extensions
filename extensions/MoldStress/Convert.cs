using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// Turns a lens built from ORDINARY catalogue polymers into one this tool can
    /// analyse, without touching the original file.
    ///
    /// WHY REPLACEMENT RATHER THAN ALIASING. The alias map borrows constants
    /// under the original material name, which is enough for this tool's own
    /// solver - but STAR is not this tool. STAR reads the birefringence (BD)
    /// record from the glass catalogue of the material actually named in the
    /// LDE, and no vendor catalogue carries BD data for PMMA or POLYCARB. That
    /// is precisely the "stress 0/15015 points accepted - does X carry a BD
    /// record?" failure measured on 2026-08-21. For STAR to accept the import,
    /// the lens must genuinely name an MS_* glass from the attached MOLDSTRESS
    /// catalogue, so the conversion writes a SIBLING FILE and edits that.
    /// </summary>
    internal static class Convert
    {
        /// <summary>
        /// Catalogue names this tool recognises as one of its own polymers, for
        /// automatic replacement. DELIBERATELY CONSERVATIVE - each row here is a
        /// claim that the real material's moulding constants are the MS row's,
        /// and this repo has measured what borrowing across grades costs:
        /// TOPAS 5013 is reported at -700 Br against 6017's +1000 (sign!), and
        /// E48R is not 480R. So: no generic "COC", no bare grade numbers, no
        /// sibling grades. A name not listed is refused with instructions, which
        /// is cheaper than a silently wrong sign.
        /// </summary>
        private static readonly Dictionary<string, string> Replacements =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PMMA",          "MS_PMMA" },
                { "ACRYLIC",       "MS_PMMA" },
                { "POLYCARB",      "MS_POLYCARB" },
                { "POLYCARBONATE", "MS_POLYCARB" },
                { "POLYSTYR",      "MS_POLYSTYR" },
                { "POLYSTYRENE",   "MS_POLYSTYR" },
                { "480R",          "MS_COP_ZEONEX480R" },
                { "ZEONEX480R",    "MS_COP_ZEONEX480R" },
                { "ZEONEX-480R",   "MS_COP_ZEONEX480R" },
                { "ZEONEX 480R",   "MS_COP_ZEONEX480R" },
                { "TOPAS6017",     "MS_COC_TOPAS6017" },
                { "TOPAS-6017",    "MS_COC_TOPAS6017" },
                { "TOPAS 6017",    "MS_COC_TOPAS6017" },
            };

        /// <summary>The MS_* row an ordinary catalogue name maps to, or null.
        /// An MS_* name returns null - already converted, nothing to do.</summary>
        public static string MsReplacement(string material)
        {
            string m = (material ?? "").Trim();
            if (m.Length == 0) return null;
            if (m.StartsWith("MS_", StringComparison.OrdinalIgnoreCase)) return null;
            string t;
            return Replacements.TryGetValue(m, out t) ? t : null;
        }

        /// <summary>
        /// "lens.zmx" -> "lens-MoldStress.zmx", beside the original. Null when
        /// the system has never been saved - the copy needs a home, and choosing
        /// one silently would scatter files the user never asked for.
        /// </summary>
        public static string SuffixPath(string systemFile)
        {
            if (string.IsNullOrEmpty(systemFile)) return null;
            string dir = Path.GetDirectoryName(systemFile) ?? "";
            string stem = Path.GetFileNameWithoutExtension(systemFile);
            string ext = Path.GetExtension(systemFile);
            if (stem.Length == 0) return null;
            return Path.Combine(dir, stem + "-MoldStress" + (ext.Length > 0 ? ext : ".zmx"));
        }

        /// <summary>
        /// The whole preparation: write the MOLDSTRESS catalogue, save the system
        /// as a -MoldStress sibling, attach the catalogue, and replace every
        /// recognised material. Returns the number of surfaces replaced; 0 means
        /// nothing was recognised and nothing was written or saved.
        ///
        /// Order matters and is deliberate: the sibling is saved BEFORE anything
        /// is modified, so every edit - catalogue attachment included - lands in
        /// the copy and the original file is never dirtied. The save is verified
        /// with File.Exists rather than trusted, because SaveAs on this API has
        /// been observed to return cleanly having written nothing (the path
        /// resolves in the server process; recorded 2026-08-19).
        /// </summary>
        public static int Prepare(ZOSAPI.IOpticalSystem sys, Action<string> say)
        {
            // ---- is there anything to convert? -----------------------------
            var lde = sys.LDE;
            var plan = new List<KeyValuePair<int, string>>();
            for (int i = 1; i < lde.NumberOfSurfaces - 1; i++)
            {
                string mat;
                try { mat = (lde.GetSurfaceAt(i).Material ?? "").Trim(); }
                catch { continue; }
                string ms = MsReplacement(mat);
                if (ms != null) plan.Add(new KeyValuePair<int, string>(i, ms));
            }
            if (plan.Count == 0) return 0;

            // THE WAVELENGTH GATE, before anything is written or saved. The MS
            // glasses are valid 0.4-1.0 um; a system with a wavelength outside
            // that band would convert into one whose every ray fails - measured
            // 2026-08-22 as FFT MTF refusing to compute, which is how this was
            // found. Refusing here, with the wavelength named, turns a blank
            // analysis window into an actionable message.
            var um = new List<double>();
            try
            {
                var wl = sys.SystemData.Wavelengths;
                for (int i = 1; i <= wl.NumberOfWavelengths; i++)
                    um.Add(wl.GetWavelength(i).Wavelength);
            }
            catch { }
            var bad = CatalogWriter.WavelengthsOutOfRange(um);
            if (bad.Count > 0)
                throw new Exception(string.Format(CultureInfo.InvariantCulture,
                    "this system uses wavelength(s) {0} um, outside the MOLDSTRESS " +
                    "catalogue's validity of {1:F1}-{2:F1} um. The MS_* glasses are an " +
                    "nd/vd fit - visible-band by construction - and converting would " +
                    "make every ray fail (FFT MTF refuses to compute). Nothing was " +
                    "converted or saved.",
                    string.Join(", ", bad.Select(w => w.ToString("F4", CultureInfo.InvariantCulture))),
                    CatalogWriter.LambdaMinUm, CatalogWriter.LambdaMaxUm));

            // ---- the catalogue, rewritten every time -----------------------
            // Always rewritten rather than written-if-absent: the deployed AGF
            // was once found carrying 4 of the 5 materials, and a stale
            // catalogue fails exactly like a missing BD record.
            string agf = CatalogWriter.Write(Path.Combine(
                CatalogWriter.DefaultDirectory(), CatalogWriter.CatalogName + ".AGF"));
            say("  wrote " + agf);

            // ---- the sibling file, saved before any edit -------------------
            string copy = SuffixPath(sys.SystemFile);
            if (copy == null)
                throw new Exception(
                    "this system has never been saved, so the -MoldStress copy has " +
                    "nowhere to live. Save the system once, then run again - the " +
                    "original file is never modified.");
            sys.SaveAs(copy);
            if (!File.Exists(copy))
                throw new Exception("SaveAs reported success but wrote nothing at " +
                                    copy + " - refusing to edit the original.");
            say("  working copy " + copy + " (original untouched)");

            // ---- attach the catalogue to the COPY --------------------------
            var cats = sys.SystemData.MaterialCatalogs;
            bool attached = false;
            try
            {
                attached = cats.GetCatalogsInUse().Any(c =>
                    string.Equals((c ?? "").Trim(), CatalogWriter.CatalogName,
                                  StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            if (!attached)
            {
                cats.AddCatalog(CatalogWriter.CatalogName);
                say("  attached the " + CatalogWriter.CatalogName + " material catalogue");
            }

            // ---- the replacements, each one named --------------------------
            foreach (var kv in plan)
            {
                var row = lde.GetSurfaceAt(kv.Key);
                string was = row.Material;
                row.Material = kv.Value;
                say(string.Format(CultureInfo.InvariantCulture,
                    "  surface {0}: {1} -> {2}  (constants are {2}'s, a substitution " +
                    "rather than an identification)", kv.Key, was, kv.Value));
            }
            sys.Save();
            return plan.Count;
        }
    }
}

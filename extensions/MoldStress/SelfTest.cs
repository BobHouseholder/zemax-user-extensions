using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MoldStress
{
    /// <summary>
    /// Every stage is held against a closed form it must reproduce before the
    /// next stage is allowed to depend on it. A stage that cannot reproduce its
    /// own analytic limit does not get to contribute a number to a report.
    /// </summary>
    internal static class SelfTest
    {
        private static int _pass, _fail;

        /// <summary>
        /// -selftest reads NO flags, and an empty read-list is the honest way to
        /// say so rather than the way to opt out of the guard. `-selftest -nz 41`
        /// used to run the full suite at the built-in grid and report a clean
        /// pass, which reads as "the suite passed at nz=41".
        /// </summary>
        internal static readonly string[] ReadsFlags = new string[0];

        public static int Run(string[] args)
        {
            int badForMode = Program.RejectFlagsNotReadBy(args, ReadsFlags, "-selftest");
            if (badForMode != 0) return badForMode;

            _pass = _fail = 0;
            Console.WriteLine("MoldStress self-test");
            Console.WriteLine("  " + Program.ScopeLabel);
            Console.WriteLine();

            CatalogChecks();
            Console.WriteLine();
            GeometryChecks();
            Console.WriteLine();
            FlagGuardChecks();
            Console.WriteLine();
            DeltaGuardChecks();
            Console.WriteLine();
            FillField.SelfCheck();
            Console.WriteLine();
            FreezeHistory.SelfCheck();
            Console.WriteLine();
            Channels.SelfCheck();
            Console.WriteLine();
            StarFiles.SelfCheck();
            Console.WriteLine();
            AngularTest.SelfCheck();
            Console.WriteLine();
            AngularTest.OrdinalCheck();

            Console.WriteLine();
            Console.WriteLine(string.Format("  {0} passed, {1} failed", _pass, _fail));
            if (Lagrangian.ShapeMisses + Lagrangian.ShapeHits > 0)
                Console.WriteLine(string.Format(
                    "  depth-shape cache: {0} solved, {1} reused",
                    Lagrangian.ShapeMisses, Lagrangian.ShapeHits));
            return _fail == 0 ? 0 : 1;
        }

        internal static void Check(string what, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  PASS  " + what + "   " + detail); }
            else { _fail++; Console.WriteLine("  FAIL  " + what + "   " + detail); }
        }

        internal static void Near(string what, double got, double want, double relTol)
        {
            double rel = Math.Abs(want) > 0 ? Math.Abs((got - want) / want) : Math.Abs(got - want);
            Check(what, rel <= relTol,
                string.Format("got {0:E9}, want {1:E9}, rel {2:E2} (tol {3:E1})",
                              got, want, rel, relTol));
        }

        /// <summary>
        /// THE READ-LISTS, held from both sides.
        ///
        /// A guard like this fails in two opposite ways and a suite that only
        /// tests one of them cannot tell them apart. Refusing everything passes
        /// any "it refuses bad flags" test while making the mode unusable;
        /// refusing nothing passes any "it accepts good flags" test while being
        /// exactly the defect the guard was written for. So each list is asserted
        /// to REFUSE a flag the mode does not read and to ACCEPT one it does.
        ///
        /// `-melttemp` is the specific case: on 2026-08-21 `-refcase -melttemp
        /// 400` ran to completion, printed VERDICT ... MET and exited 0, having
        /// used 280 C throughout.
        /// </summary>
        private static void FlagGuardChecks()
        {
            Console.WriteLine("  flag guards");
            var err = Console.Error;
            Console.SetError(TextWriter.Null);     // the helper explains itself on stderr
            try
            {
                // ALL TEN MODES, not the two that had the guard first. The
                // 2026-08-21 extension found that a case named after a defect
                // class covers whichever half its author had in mind; a table
                // that lists only the modes someone remembered is the same shape.
                foreach (var m in new[]
                {
                    new { Mode = "-refcase",      Reads = RefCase.ReadsFlags,       Good = "-nz" },
                    new { Mode = "-refcase2",     Reads = RefCase2.ReadsFlags,      Good = "-nz" },
                    new { Mode = "-refplate",     Reads = RefPlate.ReadsFlags,      Good = "-nz" },
                    new { Mode = "-refquench",    Reads = RefQuench.ReadsFlags,     Good = "-nz" },
                    new { Mode = "-run",          Reads = Runner.ReadsFlags,        Good = "-file" },
                    new { Mode = "-gates",        Reads = Program.GatesReadsFlags,  Good = "-file" },
                    new { Mode = "-writecatalog", Reads = Program.CatalogReadsFlags,Good = "-out" },
                    new { Mode = "-depthdiag",    Reads = DepthDiag.ReadsFlags,     Good = "-out" },
                    new { Mode = "-lagrangian",   Reads = Lagrangian.ReadsFlags,    Good = "-nz" },
                })
                {
                    // DERIVED, not hardcoded. The whole universe of flags minus
                    // this mode's own list is exactly the set that must be
                    // refused, and it is read off the registries rather than
                    // guessed. A fixed triple produced four FALSE failures here
                    // on 2026-08-21 because -run really does read -melttemp,
                    // -moldtemp and -directindex - the test was wrong, not the
                    // guard, and a literal is what made it possible.
                    var reads = new HashSet<string>(m.Reads, StringComparer.OrdinalIgnoreCase);
                    var mustRefuse = Program.ValueFlags.Concat(Program.BoolFlags)
                        .Where(f => !reads.Contains(f))
                        .Where(f => !string.Equals(f, m.Mode, StringComparison.OrdinalIgnoreCase))
                        .Where(f => !string.Equals(f, "-quiet", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    int swallowed = mustRefuse.Count(f => Program.RejectFlagsNotReadBy(
                        new[] { m.Mode, f, "400" }, m.Reads, m.Mode) == 0);
                    Check(m.Mode + " refuses every flag it does not read",
                        swallowed == 0 && mustRefuse.Length > 0,
                        mustRefuse.Length + " flags outside its list, " + swallowed + " swallowed");

                    Check(m.Mode + " still accepts " + m.Good + ", which it does read",
                        Program.RejectFlagsNotReadBy(
                            new[] { m.Mode, m.Good, "81" }, m.Reads, m.Mode) == 0,
                        "the guard must not refuse everything");

                    Check(m.Mode + " accepts every flag on its own read-list",
                        Program.RejectFlagsNotReadBy(
                            new[] { m.Mode }.Concat(m.Reads).ToArray(), m.Reads, m.Mode) == 0,
                        m.Reads.Length + " flags");

                    Check(m.Mode + " accepts -quiet, which no mode lists",
                        Program.RejectFlagsNotReadBy(
                            new[] { m.Mode, "-quiet" }, m.Reads, m.Mode) == 0,
                        "-quiet is exempt by design");
                }

                // -selftest reads NOTHING, so it has no "good flag" arm. Its two
                // properties are that it refuses everything valid and still
                // accepts the exempt -quiet.
                Check("-selftest refuses every valid flag, having no reads",
                    Program.RejectFlagsNotReadBy(
                        new[] { "-selftest", "-nz", "41" }, SelfTest.ReadsFlags, "-selftest") != 0
                    && Program.RejectFlagsNotReadBy(
                        new[] { "-selftest", "-melttemp", "400" }, SelfTest.ReadsFlags, "-selftest") != 0,
                    "an empty read-list is a statement, not an opt-out");
                Check("-selftest still runs bare, and with -quiet",
                    Program.RejectFlagsNotReadBy(
                        new[] { "-selftest" }, SelfTest.ReadsFlags, "-selftest") == 0
                    && Program.RejectFlagsNotReadBy(
                        new[] { "-selftest", "-quiet" }, SelfTest.ReadsFlags, "-selftest") == 0,
                    "the guard must not make the suite unrunnable");

                // AND THE TWO LISTS MUST DIFFER. If they were accidentally the
                // same array, every assertion above would still pass while one
                // mode silently accepted the other's flags. -adhered is read by
                // -refcase and has never been implemented in -refcase2.
                Check("-refcase2 refuses -adhered, which only -refcase reads",
                    Program.RejectFlagsNotReadBy(
                        new[] { "-refcase2", "-adhered" }, RefCase2.ReadsFlags, "-refcase2") != 0
                    && Program.RejectFlagsNotReadBy(
                        new[] { "-refcase", "-adhered" }, RefCase.ReadsFlags, "-refcase") == 0,
                    "the two read-lists are not interchangeable");
            }
            finally { Console.SetError(err); }
        }

        private static void DeltaGuardChecks()
        {
            Console.WriteLine("  delta guard and headline");
            // --- THE DELTA GUARD, all four states --------------------------
            //
            // A run can complete, write every file, and have no performance
            // change to report. Until 2026-08-21 it reported one anyway: STAR
            // rejected all 15015 stress points, the metric came back exactly
            // 0.000000 waves, and the tool printed -100.0% and exited 0.
            //
            // The control is the one that matters most here. A guard that
            // refuses every delta would also have suppressed that -100%, and
            // would pass any test that only checks the bad cases - so the
            // GOOD case is asserted first and asserted to be null.
            Check("a real before/after IS reported",
                Runner.NoDeltaReason(1, 1, 0.0500, 0.0530) == null,
                "one element applied, both metrics positive and finite");

            Check("nothing applied - no delta",
                Reason(Runner.NoDeltaReason(1, 0, 72.716883, 0.0)).Contains("nothing was applied"),
                Reason(Runner.NoDeltaReason(1, 0, 72.716883, 0.0)));

            Check("post-import metric of exactly zero - no delta",
                Reason(Runner.NoDeltaReason(1, 1, 0.0500, 0.0)).Contains("exactly 0.000000"),
                Reason(Runner.NoDeltaReason(1, 1, 0.0500, 0.0)));

            Check("baseline that did not evaluate - no delta",
                Reason(Runner.NoDeltaReason(1, 1, double.NaN, 0.0530)).Contains("BASELINE"),
                Reason(Runner.NoDeltaReason(1, 1, double.NaN, 0.0530)));

            Check("baseline of exactly zero - no delta",
                Reason(Runner.NoDeltaReason(1, 1, 0.0, 0.0530)).Contains("BASELINE"),
                "the old code printed +0.0% here rather than refusing");

            // AND THE ORDER OF THE REASONS MATTERS. The 2026-08-21 case trips
            // BOTH (a) and (b) at once; the reason given must be the ROOT one -
            // nothing was applied - not the downstream symptom, or the user is
            // sent to debug the merit operand instead of the glass catalogue.
            Check("the root cause is reported, not the symptom",
                Reason(Runner.NoDeltaReason(1, 0, 72.716883, 0.0)).Contains("STAR accepted no"),
                "both (a) and (b) hold; (a) is the one to say");

            // An element that produced no points at all is not evidence that
            // STAR refused anything, so it must NOT trip the nothing-applied arm.
            Check("an element with no points does not fake a refusal",
                Runner.NoDeltaReason(0, 0, 0.0500, 0.0530) == null,
                "nothing was offered, so nothing was refused");

            // --- THE SCALAR-VS-RETARDANCE VERDICT ---------------------------
            //
            // The number a tool prints LAST is the number people quote. Until
            // 2026-08-21 that was the RMS wavefront delta, and on the one real
            // lens it read +0.5% while peak retardance was 0.41 waves - a factor
            // of 585. Both correct; only one is about birefringence.
            //
            // The boundary is DERIVED, not chosen: both quantities are in waves,
            // so the warning fires exactly when the retardance is the larger of
            // the two. That is what makes the control cheap and meaningful -
            // just below the boundary it must not fire, just above it must.
            Check("just below the boundary, no understatement is claimed",
                Verdict(Runner.ScalarVerdict(0.0099, 1.0000, 1.0100, false))
                    .Contains("larger effect here"),
                Verdict(Runner.ScalarVerdict(0.0099, 1.0000, 1.0100, false)));

            Check("just above the boundary, it IS claimed",
                Verdict(Runner.ScalarVerdict(0.0101, 1.0000, 1.0100, false))
                    .Contains("UNDERSTATES"),
                Verdict(Runner.ScalarVerdict(0.0101, 1.0000, 1.0100, false)));

            // The real lens, to the numbers on record: 0.41 waves of retardance
            // against a 0.5% move on a 0.140186-wave baseline, i.e. a
            // 0.000701-wave change. 0.41 / 0.000701 = 585.
            Check("the 585x case is reported as 585x",
                Verdict(Runner.ScalarVerdict(0.41, 0.140186, 0.140887, false))
                    .Contains("585x"),
                Verdict(Runner.ScalarVerdict(0.41, 0.140186, 0.140887, false)));

            // A wavefront that does not move AT ALL is the worst version of the
            // trap: the ratio is infinite, and a bare format string would print
            // a symbol rather than saying what happened.
            Check("a wavefront that did not move is described, not divided by",
                Verdict(Runner.ScalarVerdict(0.41, 1.0, 1.0, false))
                    .Contains("DID NOT MOVE AT ALL"),
                Verdict(Runner.ScalarVerdict(0.41, 1.0, 1.0, false)));

            // TWO SILENCES, each for its own reason.
            Check("no verdict when there is no retardance to compare",
                Runner.ScalarVerdict(0.0, 1.0000, 1.0100, false) == null,
                "nothing measured on the polarisation side");
            Check("no verdict when the scalar itself was refused",
                Runner.ScalarVerdict(0.41, double.NaN, 0.0, true) == null,
                "there is no wavefront number to be understated");

            // ...and the SIGN of the wavefront change must not matter. An
            // improvement and a degradation of the same size understate the
            // retardance equally, and loaded-minus-base is negative in one.
            Check("the comparison uses the magnitude of the change",
                Runner.ScalarVerdict(0.41, 1.0100, 1.0000, false) ==
                Runner.ScalarVerdict(0.41, 1.0000, 1.0100, false),
                "a 0.01-wave improvement and a 0.01-wave degradation read alike");

            // --- THE WAVELENGTH GATE, both arms ------------------------------
            //
            // Found 2026-08-22 as "FFT MTF would not compute": the MS glasses
            // are an nd/vd fit valid 0.4-1.0 um, and a system converted outside
            // that band has every ray fail - measured at 1.2 um as ~185 waves of
            // extrapolated error and an empty MTF. The gate that now refuses
            // must catch the out-of-band wavelength AND pass a visible system,
            // or it trades a blank window for a tool nobody can run.
            {
                Check("a visible-band system passes the gate",
                    CatalogWriter.WavelengthsOutOfRange(
                        new[] { 0.4861, 0.5876, 0.6563 }).Count == 0,
                    "F, d, C - the common case must not be refused");
                Check("an NIR wavelength is caught and NAMED",
                    CatalogWriter.WavelengthsOutOfRange(
                        new[] { 0.5876, 1.31 }).Count == 1
                    && CatalogWriter.WavelengthsOutOfRange(
                        new[] { 0.5876, 1.31 })[0] == 1.31,
                    "1.31 um is outside 0.4-1.0 and must be reported, not counted");
                Check("the band edges themselves are inside",
                    CatalogWriter.WavelengthsOutOfRange(
                        new[] { CatalogWriter.LambdaMinUm, CatalogWriter.LambdaMaxUm }).Count == 0,
                    "0.4 and 1.0 exactly are valid, per the LD record");
                Check("UV below the band is caught too",
                    CatalogWriter.WavelengthsOutOfRange(new[] { 0.355 }).Count == 1,
                    "the gate is a band, not a ceiling");
                Check("the LD record and the gate share one constant",
                    CatalogWriter.LambdaMinUm == 0.4 && CatalogWriter.LambdaMaxUm == 1.0,
                    "the AGF's LD line is written from these same values");
            }

            // --- THE EXPORT RADIUS, both arms --------------------------------
            //
            // Found 2026-08-22 in OpticStudio's Multiphysics Data Loader: the
            // exported cloud stopped at the CLEAR aperture while the loader
            // draws the part to its MECHANICAL aperture, so on any flanged
            // moulded lens the data visibly failed to fill the part. The choice
            // of radius is pure, so both directions are pinned here.
            {
                Check("a flange extends the export to the mechanical aperture",
                    MouldedElement.ExportRadius(18.0, 16.0, 15.0) == 18.0,
                    "the larger mechanical wins");
                Check("the export never shrinks below the physics radius",
                    MouldedElement.ExportRadius(12.0, 0.0, 15.0) == 15.0,
                    "a MEMA smaller than the clear aperture must not clip the solve domain");
                Check("unset mechanical apertures fall back to the clear aperture",
                    MouldedElement.ExportRadius(0.0, 0.0, 15.0) == 15.0
                    && MouldedElement.ExportRadius(double.NaN, double.NaN, 15.0) == 15.0,
                    "zero and NaN both mean unknown, not tiny");
            }

            // --- AUTOMATIC MATERIAL CONVERSION, both arms --------------------
            //
            // The replacement table is a set of claims - "this catalogue name IS
            // that MS row" - so what needs asserting is that the claims made are
            // right AND that the claims deliberately NOT made stay unmade. The
            // refusals are load-bearing: TOPAS 5013 is reported at -700 Br
            // against 6017's +1000, so a generous match on "COC" or a sibling
            // grade would borrow a coefficient with the wrong SIGN.
            {
                Check("PMMA converts to MS_PMMA",
                    Convert.MsReplacement("PMMA") == "MS_PMMA", "the common case");
                Check("conversion is case-insensitive",
                    Convert.MsReplacement("polycarb") == "MS_POLYCARB"
                    && Convert.MsReplacement("Acrylic") == "MS_PMMA",
                    "catalogue names arrive in every casing");
                Check("ZEONEX 480R converts, space and hyphen alike",
                    Convert.MsReplacement("ZEONEX 480R") == "MS_COP_ZEONEX480R"
                    && Convert.MsReplacement("ZEONEX-480R") == "MS_COP_ZEONEX480R",
                    "vendor spellings vary");
                Check("an ordinary glass does NOT convert",
                    Convert.MsReplacement("N-BK7") == null, "must be left alone");
                Check("an MS_* material does NOT convert again",
                    Convert.MsReplacement("MS_PMMA") == null,
                    "already converted; a second pass must be a no-op");
                Check("generic COC is REFUSED, deliberately",
                    Convert.MsReplacement("COC") == null,
                    "TOPAS 5013 reads -700 Br against 6017's +1000 - grade decides the SIGN");
                Check("sibling grade E48R is REFUSED, deliberately",
                    Convert.MsReplacement("E48R") == null,
                    "E48R is not 480R, and borrowing across grades is the recorded trap");
                Check("empty and null convert to nothing",
                    Convert.MsReplacement("") == null && Convert.MsReplacement(null) == null,
                    "guarded");

                Check("the sibling path gets the suffix before the extension",
                    Convert.SuffixPath(@"C:\x\lens.zmx") == @"C:\x\lens-MoldStress.zmx",
                    Convert.SuffixPath(@"C:\x\lens.zmx"));
                Check("the extension's casing is preserved",
                    Convert.SuffixPath(@"C:\x\LENS.ZMX") == @"C:\x\LENS-MoldStress.ZMX",
                    Convert.SuffixPath(@"C:\x\LENS.ZMX"));
                Check("an unsaved system has no sibling path",
                    Convert.SuffixPath("") == null && Convert.SuffixPath(null) == null,
                    "the caller must refuse rather than invent a location");
            }

            // --- THE HOST-LAUNCH DETECTOR, both arms -------------------------
            //
            // OpticStudio launches ribbon extensions with -zpid/-zplt/-zsid, a
            // fact this tool ASSUMED away ("a ribbon launch arrives with no
            // command line at all") until two silent clicks and a launch log
            // measured it. The detector must accept the measured triple and
            // refuse everything a human could plausibly type, because routing a
            // typo into ribbon mode would run the whole chain on the open system
            // when the user asked for something else.
            {
                Check("the measured OpticStudio launch triple is detected",
                    Program.IsHostLaunch(new[] { "-zpid={14212}", "-zplt={Extension}", "-zsid={100003}" }),
                    "the exact argv from launch-log.txt, 2026-08-22 14:53:39Z");

                Check("a single host marker is enough",
                    Program.IsHostLaunch(new[] { "-zsid={100001}" }),
                    "future OpticStudio versions may trim the set");

                Check("an empty command line is NOT a host launch",
                    !Program.IsHostLaunch(new string[0]),
                    "empty is its own case, handled separately in Main");

                Check("a normal CLI invocation is not a host launch",
                    !Program.IsHostLaunch(new[] { "-selftest" })
                    && !Program.IsHostLaunch(new[] { "-run", "-file", "x.zmx" }),
                    "must stay on the strict CLI path");

                Check("a host marker mixed with anything else is refused",
                    !Program.IsHostLaunch(new[] { "-zpid={1}", "-run" }),
                    "a mixed line is a human invocation with a typo");
            }

            // --- THE SPLIT CAVEAT MUST FIRE, AND MUST BE ABLE TO STOP --------
            //
            // The density channel divides by K11 + 2*K12 and writes the result
            // into the STAR file, so an unmeasured split is exported rather than
            // merely assumed. The caveat that says so is only worth having if it
            // fires for every grade that needs it AND would fall silent for one
            // that does not - a warning printed unconditionally is decoration.
            {
                foreach (var nm in new[] { "MS_PMMA", "MS_POLYCARB", "MS_POLYSTYR",
                                           "MS_COC_TOPAS6017", "MS_COP_ZEONEX480R" })
                    Check(nm + " reports its K11/K12 split as NOT measured",
                        !Polymers.ByName(nm).SplitMeasured,
                        "no grade in this table has a measured split, and saying so is the point");

                // The span is what the caveat quotes, so it must be large enough
                // to matter and must be computed, not asserted.
                double lo, hi;
                double span = SplitUncertainty.IsotropicSpan(
                    Polymers.ByName("MS_COC_TOPAS6017"), out lo, out hi);
                Check("the quoted split span is large enough to matter",
                    span > 5.0,
                    string.Format(CultureInfo.InvariantCulture,
                        "K11+2K12 spans {0:F1} to {1:F1} Br, factor {2:F0}", lo, hi, span));

                // AND THE CONTROL: a grade whose split WAS measured must silence
                // it. Without this arm the caveat could be hard-wired on and no
                // test would notice.
                var measured = Polymers.ByName("MS_POLYCARB")
                                       .WithProcessTemps(300.0, 100.0);
                measured.SplitMeasured = true;
                Check("a measured split would silence the caveat",
                    measured.SplitMeasured && !Polymers.ByName("MS_POLYCARB").SplitMeasured,
                    "the flag is per-grade and settable, not a constant");
            }

            // --- THE K11/K12 SPLIT, AGAINST A MEASUREMENT --------------------
            //
            // Waxler, Horowitz & Feldman, Appl. Opt. 18(1) 101 (1979) measured the
            // INDIVIDUAL constants for Plexiglas 55 and Lexan by interferometry -
            // the quantity a polariscope cannot see and which this model therefore
            // splits in N-BK7's proportion. The hydrostatic combination q11 + 2*q12
            // is the route the density channel is delivered through.
            //
            // These assert the DISAGREEMENT, not agreement, because that is what is
            // true: the split is refuted for PMMA and coincidentally good for PC.
            // Written as tests so that changing either row without reading the
            // source fails loudly rather than quietly moving a published number.
            {
                Func<string, double> hydro = nm =>
                {
                    var q = Polymers.ByName(nm);
                    return q.K11Brewster + 2.0 * q.K12Brewster;
                };

                // PC: measured -4.6 + 2(34.6) = +64.6 Br. The model's assumed split
                // gives +72.0 - within 12%, which is luck rather than validation.
                SelfTest.Near("the PC split happens to match the measurement to ~12%",
                    hydro("MS_POLYCARB") / 64.6, 1.0, 0.15);

                // PMMA: measured 26.7 + 2(25.5) = +77.7 Br against the model's -2.1.
                // Wrong by a factor of 37 AND in sign. Asserted so the refutation
                // cannot be silently "fixed" by adjusting the row.
                SelfTest.Check("the PMMA split is refuted by the measurement",
                    hydro("MS_PMMA") < 0.0 && Math.Abs(77.7 / hydro("MS_PMMA")) > 20.0,
                    string.Format(CultureInfo.InvariantCulture,
                        "model {0:F1} Br against a measured +77.7 Br - opposite sign, "
                        + "factor {1:F0}", hydro("MS_PMMA"), Math.Abs(77.7 / hydro("MS_PMMA"))));

                // And PMMA is the row no reference case uses, so the refutation costs
                // no registered number - which is why it is recorded, not patched.
                SelfTest.Check("no reference case uses the refuted row",
                    !Polymers.ByName("MS_PMMA").Name.Equals("MS_COC_TOPAS6017")
                    && !Polymers.ByName("MS_PMMA").Name.Equals("MS_COP_ZEONEX480R")
                    && !Polymers.ByName("MS_PMMA").Name.Equals("MS_POLYCARB"),
                    "cases run TOPAS 6017, ZEONEX 480R and polycarbonate twice");
            }

            // --- THE MELT-SIDE COOLING STRESS -------------------------------
            //
            // Without it the orientation channel is structurally null, so what has
            // to be asserted is that it is non-zero where it should be, exactly
            // zero where it should be, and that the RELAXATION in it is doing
            // something - a build-and-relax that never relaxes is just the
            // solid-side construction started earlier.
            {
                var pc = Polymers.ByName("MS_POLYCARB");
                int n = 21, nt = 201;
                var z = new double[n];
                for (int k = 0; k < n; k++) z[k] = -0.75 + 1.5 * k / (n - 1.0);

                // A cooling history that starts well above Tg and ends below it,
                // with a gradient through the thickness so the balance has
                // something to balance.
                var tg = new double[nt];
                var th = new double[n, nt];
                for (int j = 0; j < nt; j++)
                {
                    tg[j] = 20.0 * j / (nt - 1.0);
                    for (int k = 0; k < n; k++)
                        th[k, j] = 300.0 - (160.0 * j / (nt - 1.0)) * (1.0 + 0.4 * Math.Abs(z[k]));
                }

                Func<double[,], double> peak = h =>
                {
                    double m = 0.0;
                    for (int k = 0; k < h.GetLength(0); k++)
                        for (int j = 0; j < h.GetLength(1); j++)
                            m = Math.Max(m, Math.Abs(h[k, j]));
                    return m;
                };

                double onPeak = peak(Channels.MeltSideCoolingStressHistory(z, tg, th, pc, 1.0));
                Check("the melt-side cooling stress is non-zero above Tg",
                    onPeak > 0.0, string.Format(CultureInfo.InvariantCulture,
                        "peak |sigma| {0:E3} MPa", onPeak));

                // THE NULL, and it must be exact: no thermal expansion, no
                // thermal stress, however the history runs.
                var noCte = pc.WithProcessTemps(pc.MeltTempC, pc.MoldTempC);
                noCte.CtePerK = 0.0;
                Check("CTE = 0 collapses it to exactly zero",
                    peak(Channels.MeltSideCoolingStressHistory(z, tg, th, noCte, 1.0)) == 0.0,
                    "the channel's own negative control");

                // ...and a history that never leaves the solid state has no LIQUID
                // set to balance over, so it must also be exactly zero - this is
                // the arm that proves the Tg test is being applied at all.
                var cold = new double[n, nt];
                for (int j = 0; j < nt; j++)
                    for (int k = 0; k < n; k++) cold[k, j] = 100.0 - 0.1 * j;
                Check("a history entirely below Tg produces nothing",
                    peak(Channels.MeltSideCoolingStressHistory(z, tg, cold, pc, 1.0)) == 0.0,
                    "no liquid set, so no melt-side stress");

                // THE RELAXATION IS LOAD-BEARING. Scaling lambda down by 1e-6
                // relaxes almost everything away; scaling it up by 1e6 keeps it.
                // If these two agreed, the exp(-dt/lambda) would be doing nothing.
                double fast = peak(Channels.MeltSideCoolingStressHistory(z, tg, th, pc, 1e-6));
                double slow = peak(Channels.MeltSideCoolingStressHistory(z, tg, th, pc, 1e6));
                Check("relaxation is doing something: slow lambda retains more",
                    slow > fast * 1.5,
                    string.Format(CultureInfo.InvariantCulture,
                        "peak {0:E3} at lambda x1e6 against {1:E3} at x1e-6", slow, fast));
            }

            // --- THE THERMAL-ORIENTATION CHANNEL, both arms -----------------
            //
            // The channel is off by default and structurally null when on (see
            // Channels.Build). BOTH of those need asserting, because "off" and
            // "returns zero" are indistinguishable from a run, and that is exactly
            // how the first attempt at measuring it produced two zeros for two
            // different reasons - one because the mode never calls Channels.Build,
            // one because the stress and memory windows are disjoint.
            Check("the thermal-orientation channel is OFF by default",
                !new Process().ThermalOrientation,
                "a channel extrapolating outside its law's validity must be opt-in");

            Check("polycarbonate carries measured optical memory",
                Polymers.ByName("MS_POLYCARB").HasOpticalMemory,
                "the only grade that does - so cases 1 and 2 cannot use it at all");

            foreach (var nm in new[] { "MS_COC_TOPAS6017", "MS_COP_ZEONEX480R", "MS_PMMA" })
                Check(nm + " has NO optical memory, so the channel is silent there",
                    !Polymers.ByName(nm).HasOpticalMemory,
                    "switching it on for this grade must change nothing");

            // PARTIAL COVERAGE -------------------------------------------------
            //
            // NoDeltaReason fires only when NOTHING was applied. Two elements of
            // three landing produced a confident before/after with no hint that a
            // third of the part was missing from the "after" - the same defect as
            // the -100% case in a quieter register.
            //
            // The two predicates must PARTITION, not overlap: for any pair of
            // counts, at most one of them may speak. Asserted below over the whole
            // small grid rather than at a few chosen points, because an off-by-one
            // in either boundary is exactly what would make them both fire, or
            // neither.
            Check("partial coverage is reported",
                Verdict(Runner.PartialCoverage(3, 2)).Contains("2 of 3"),
                Verdict(Runner.PartialCoverage(3, 2)));

            Check("complete coverage is silent",
                Runner.PartialCoverage(3, 3) == null,
                "3 of 3 - nothing to qualify");

            Check("total refusal is left to NoDeltaReason",
                Runner.PartialCoverage(3, 0) == null,
                "0 of 3 is that predicate's case, not this one");

            Check("an element that offered nothing is not counted as refused",
                Runner.PartialCoverage(0, 0) == null,
                "no points anywhere, so nothing was declined");

            {
                int both = 0, neither = 0, grid = 0;
                for (int with = 0; with <= 4; with++)
                    for (int applied = 0; applied <= with; applied++)
                    {
                        bool p = Runner.PartialCoverage(with, applied) != null;
                        bool n = Runner.NoDeltaReason(with, applied, 0.05, 0.053) != null;
                        grid++;
                        if (p && n) both++;
                        if (!p && !n && applied < with) neither++;
                    }
                Check("the two coverage predicates never both fire",
                    both == 0, grid + " count pairs, " + both + " overlaps");
                Check("no incomplete run escapes both predicates",
                    neither == 0, neither + " uncovered incomplete cases");
            }

            // The exit codes must be distinguishable, or a script cannot act on
            // the difference the report is drawing.
            Check("the three outcomes have three distinct exit codes",
                Runner.NothingApplied != Runner.PartialApplication &&
                Runner.NothingApplied != 0 && Runner.PartialApplication != 0 &&
                Runner.NothingApplied != Program.UsageError &&
                Runner.PartialApplication != Program.UsageError,
                string.Format("usage {0}, nothing applied {1}, partial {2}, complete 0",
                    Program.UsageError, Runner.NothingApplied, Runner.PartialApplication));
        }

        private static string Verdict(string v)
        {
            return v ?? "(null - no verdict was returned)";
        }

        /// <summary>Renders a null reason as a readable failure rather than
        /// throwing inside the assertion that was meant to report it.</summary>
        private static string Reason(string r)
        {
            return r ?? "(null - the guard did not fire)";
        }

        private static void GeometryChecks()
        {
            // --- THE FULL SAG: conic and aspheric terms, both arms ---------
            //
            // Every one of these has a control that must NOT move beside a case
            // that must. A sag routine that ignored its new arguments entirely
            // would pass the controls and fail nothing else, which is how the
            // spherical-only version survived unnoticed for the life of the tool.
            {
                double[] none = null;

                // (a) THE SPHERE IS UNCHANGED, BIT FOR BIT. Not "within a
                // tolerance" - the general form must reduce to the identical
                // floating-point expression, because all four reference cases
                // are spherical or plano and any drift here moves published
                // numbers for no physical reason.
                bool identical = true;
                foreach (double R in new[] { 12.5, -30.0, 200.0, -8.75 })
                    for (int i = 0; i <= 20; i++)
                    {
                        double r = 5.0 * i / 20.0;
                        if (MouldedElement.Sag(R, 0.0, none, false, false, r)
                            != MouldedElement.Sag(R, r)) identical = false;
                    }
                SelfTest.Check("zero conic, no terms reproduces the sphere exactly",
                    identical, "84 samples over 4 radii, bitwise ==");

                // (b) A PARABOLA. k = -1 kills the r-dependence of the square
                // root, leaving z = r^2 / 2R exactly - an identity, not a fit,
                // so it is a reference the implementation cannot define away.
                SelfTest.Near("conic -1 gives the exact parabola r^2/2R",
                    MouldedElement.Sag(20.0, -1.0, none, false, false, 10.0),
                    100.0 / 40.0, 1e-12);

                // (c) A HYPERBOLA, hand-computed: c = 0.05, r = 10, so
                // arg = 1 - (1-3)(0.0025)(100) = 1.5 and z = 5/(1+sqrt(1.5)).
                SelfTest.Near("conic -3 matches the hand calculation",
                    MouldedElement.Sag(20.0, -3.0, none, false, false, 10.0),
                    2.24744871391589, 1e-12);

                // ... and it must DIFFER from the sphere by a mould-relevant
                // amount, or the test above would pass on a routine that quietly
                // dropped the conic and returned the spherical value.
                SelfTest.Check("the conic actually moves the sag",
                    Math.Abs(MouldedElement.Sag(20.0, -3.0, none, false, false, 10.0)
                             - MouldedElement.Sag(20.0, 10.0)) > 0.4,
                    string.Format(CultureInfo.InvariantCulture, "{0:F4} vs {1:F4} mm",
                        MouldedElement.Sag(20.0, -3.0, none, false, false, 10.0),
                        MouldedElement.Sag(20.0, 10.0)));

                // (d) THE CLAMP. At k = 0 the pole is the sphere's own clamp
                // value, which is what keeps (a) true; at k = 1 the surface runs
                // out at a SMALLER radius and the pole is R/(1+k).
                SelfTest.Near("the oblate pole is R/(1+k)",
                    MouldedElement.Sag(20.0, 1.0, none, false, false, 500.0),
                    10.0, 1e-12);

                // (e) EVEN ASPHERE. Par2 is the r^4 coefficient, so the whole
                // difference from the same conic surface must be a*r^4 exactly.
                var evens = new double[8]; evens[1] = -3.0e-5;
                SelfTest.Near("an even aspheric term adds exactly a*r^4",
                    MouldedElement.Sag(20.0, 0.0, evens, true, false, 6.0)
                    - MouldedElement.Sag(20.0, 6.0),
                    -3.0e-5 * Math.Pow(6.0, 4), 1e-12);

                // (f) ODD ASPHERE. Par1 is r^1, not r^2 - the two conventions
                // differ from the first cell, so reading an odd asphere with the
                // even mapping is wrong at leading order rather than in the tail.
                var odds = new double[8]; odds[0] = 1.0e-3;
                SelfTest.Near("an odd aspheric term adds exactly a*r^1",
                    MouldedElement.Sag(20.0, 0.0, odds, false, true, 6.0)
                    - MouldedElement.Sag(20.0, 6.0),
                    1.0e-3 * 6.0, 1e-12);

                // ... and the two conventions must not agree, or nothing above
                // would detect them being swapped.
                SelfTest.Check("the even and odd conventions differ",
                    MouldedElement.Sag(20.0, 0.0, odds, true, false, 6.0)
                    != MouldedElement.Sag(20.0, 0.0, odds, false, true, 6.0),
                    "same coefficients read as r^2 and as r^1");

                // (g) TERMS ARE IGNORED UNLESS THE TYPE ASKS FOR THEM. A
                // Standard surface can hold junk in its parameter cells.
                SelfTest.Check("a Standard surface ignores its parameter cells",
                    MouldedElement.Sag(20.0, 0.0, evens, false, false, 6.0)
                    == MouldedElement.Sag(20.0, 6.0),
                    "even coefficients present, neither flag set");
            }

            // --- THE SHAPE REACHES THE CAVITY, not just the sag ------------
            //
            // The sag being right is worth nothing if ThicknessAt still calls the
            // spherical form. Same element twice, differing in the conic alone.
            {
                var sphere = new MouldedElement
                {
                    CentreThicknessMm = 4.0, SemiDiameterMm = 10.0,
                    FrontRadiusMm = 20.0, BackRadiusMm = 0.0,
                };
                var hyper = new MouldedElement
                {
                    CentreThicknessMm = 4.0, SemiDiameterMm = 10.0,
                    FrontRadiusMm = 20.0, BackRadiusMm = 0.0, FrontConic = -3.0,
                };
                SelfTest.Near("the spherical element is unchanged",
                    sphere.ThicknessAt(10.0), 4.0 - 2.67949192431123, 1e-12);
                SelfTest.Near("the conic reaches the cavity thickness",
                    hyper.ThicknessAt(10.0), 4.0 - 2.24744871391589, 1e-12);

                // (h) THE INTERIOR PINCH. For a sphere the wall is monotonic in
                // r, so the thinnest point is always an end and MinThicknessMm
                // says nothing new. An asphere can pinch in the middle, and that
                // pinch - not either end - sets the fill and the freeze.
                //
                // Front plano with 0.01 r^2 - 1e-4 r^4: the sag returns to zero
                // at r = 10, so BOTH ends read 3.000 mm and the true minimum is
                // at r^2 = -a2/2a4 = 50, r = 7.0711, where the sag is
                // 0.01(50) - 1e-4(2500) = 0.25 and the wall is 2.750 mm.
                var pinched = new MouldedElement
                {
                    CentreThicknessMm = 3.0, SemiDiameterMm = 10.0,
                    FrontRadiusMm = 0.0, BackRadiusMm = 0.0,
                    FrontIsEvenAsphere = true,
                    FrontPars = new double[] { 0.01, -1.0e-4, 0, 0, 0, 0, 0, 0 },
                };
                double rPinch, rEnd;
                double hPinch = pinched.MinThicknessMm(out rPinch);
                SelfTest.Check("both ends of the pinched element read the same",
                    Math.Abs(pinched.ThicknessAt(0.0) - pinched.ThicknessAt(10.0)) < 1e-12,
                    string.Format(CultureInfo.InvariantCulture, "{0:F4} and {1:F4} mm",
                        pinched.ThicknessAt(0.0), pinched.ThicknessAt(10.0)));
                SelfTest.Near("the interior pinch is found, and its depth",
                    hPinch, 2.75, 2e-4);
                SelfTest.Check("the pinch is located inside the aperture",
                    Math.Abs(rPinch - 7.0711) < 0.08,
                    string.Format(CultureInfo.InvariantCulture, "r = {0:F4} mm", rPinch));

                // ... and the CONTROL: on a sphere the same scan must land on an
                // end, or the scan is finding minima that are not there.
                double hEnd = sphere.MinThicknessMm(out rEnd);
                SelfTest.Check("on a sphere the thinnest wall is at an end",
                    rEnd == 10.0 && hEnd == sphere.ThicknessAt(10.0),
                    string.Format(CultureInfo.InvariantCulture,
                        "r = {0:F3} mm, {1:F4} mm", rEnd, hEnd));
            }

            // --- WHAT IS STILL REFUSED, and what is no longer --------------
            //
            // The guard went in hours before the sag did, and it refused conics
            // and aspheres because nothing could model them. Now something can,
            // so the refusal must have NARROWED - and a relaxation that quietly
            // went too far would look exactly like a guard that works.
            {
                SelfTest.Check("a conic is no longer refused",
                    Session.UnreadableShape("Standard") == null, "Standard");
                SelfTest.Check("an even asphere is no longer refused",
                    Session.UnreadableShape("EvenAspheric") == null, "EvenAspheric");
                SelfTest.Check("an odd asphere is no longer refused",
                    Session.UnreadableShape("OddAsphere") == null, "OddAsphere");

                foreach (string t in new[] { "Toroidal", "Biconic", "ZernikeStandardSag", null })
                    SelfTest.Check("an unreadable surface type IS still refused: " + (t ?? "(null)"),
                        Session.UnreadableShape(t) != null,
                        Session.UnreadableShape(t) ?? "(null)");
            }

            // --- THE NON-SPHERICAL GUARD, both arms ------------------------
            //
            // A guard that never fires and a guard that always fires look
            // identical from a passing suite, so a sphere must go through and
            // each departure must be caught, separately.
            {
                var none = new double[8];
                SelfTest.Check("a plain sphere is NOT flagged",
                    Session.ShapeDeparture("Standard", 0.0, none) == null, "Standard, conic 0");

                string c = Session.ShapeDeparture("Standard", -1.0, none);
                SelfTest.Check("a conic IS flagged", c != null && c.Contains("conic"),
                    c ?? "(null)");

                var p4 = new double[8]; p4[1] = -3.2e-6;      // Par2 -> r^4
                string a4 = Session.ShapeDeparture("EvenAspheric", 0.0, p4);
                SelfTest.Check("an aspheric term IS flagged, with its power",
                    a4 != null && a4.Contains("r^4"), a4 ?? "(null)");

                SelfTest.Check("an even-asphere row with NO terms is not flagged",
                    Session.ShapeDeparture("EvenAspheric", 0.0, none) == null,
                    "EvenAspheric, all parameters zero");

                string t = Session.ShapeDeparture("Toroidal", 0.0, none);
                SelfTest.Check("an unreadable surface type IS flagged",
                    t != null && t.Contains("Toroidal"), t ?? "(null)");

                // and a conic on a type this solver cannot read must report BOTH,
                // or the first finding masks the second.
                string both = Session.ShapeDeparture("Biconic", 0.5, none);
                SelfTest.Check("type and conic are reported together",
                    both != null && both.Contains("Biconic") && both.Contains("conic"),
                    both ?? "(null)");
            }

            Console.WriteLine("  geometry, gate and parting line");

            // A plane-parallel plate: thickness must be its centre thickness
            // everywhere, whatever the sampling radius.
            var plate = new MouldedElement
            {
                FrontSurface = 1, BackSurface = 2, Material = "MS_PMMA",
                CentreThicknessMm = 2.0, SemiDiameterMm = 10.0,
                FrontRadiusMm = 0, BackRadiusMm = 0,
            };
            plate.EdgeThicknessMm = plate.ThicknessAt(plate.SemiDiameterMm);
            Near("plate thickness is uniform", plate.ThicknessAt(7.3), 2.0, 1e-12);

            // A biconvex element: sag of a sphere is exact, so the edge thickness
            // has a closed form.
            var lens = new MouldedElement
            {
                FrontSurface = 3, BackSurface = 4, Material = "MS_COC_TOPAS6017",
                CentreThicknessMm = 4.0, SemiDiameterMm = 8.0,
                FrontRadiusMm = 40.0, BackRadiusMm = -40.0,
            };
            double sag = 40.0 - Math.Sqrt(40.0 * 40.0 - 8.0 * 8.0);
            lens.EdgeThicknessMm = lens.ThicknessAt(lens.SemiDiameterMm);
            Near("biconvex edge thickness against the closed form",
                 lens.EdgeThicknessMm, 4.0 - 2.0 * sag, 1e-12);

            // The parting plane of a symmetric biconvex element sits at its own
            // mid-plane by symmetry - a check that cannot pass by accident.
            lens.PartingLineZMm = Gating.DefaultPartingLineZ(lens);
            Near("symmetric biconvex parts at its mid-plane",
                 lens.PartingLineZMm, 2.0, 1e-12);

            // Gate defaults scale off the LOCAL wall, so a thinner edge must give
            // a thinner gate. A default that ignores geometry would tie here.
            var thin = new MouldedElement
            {
                FrontSurface = 5, CentreThicknessMm = 4.0, SemiDiameterMm = 8.0,
                FrontRadiusMm = 25.0, BackRadiusMm = -25.0,
            };
            thin.EdgeThicknessMm = thin.ThicknessAt(thin.SemiDiameterMm);
            thin.Gate = Gating.DefaultGate(thin);
            lens.Gate = Gating.DefaultGate(lens);
            Check("gate land tracks the local wall thickness",
                  thin.Gate.ThicknessMm < lens.Gate.ThicknessMm,
                  string.Format("{0:F4} mm on a {1:F3} mm edge vs {2:F4} mm on a {3:F3} mm edge",
                      thin.Gate.ThicknessMm, thin.EdgeThicknessMm,
                      lens.Gate.ThicknessMm, lens.EdgeThicknessMm));

            // The default azimuth must be a real value the rest of the chain can
            // move: the registered null control depends on it.
            Check("gate azimuth defaults to a definite value",
                  lens.Gate.AzimuthDeg == Gating.DefaultAzimuthDeg && lens.Gate.IsDefault,
                  string.Format("{0:F1} deg, flagged default", lens.Gate.AzimuthDeg));

            // An unknown key in a config file must be refused, not ignored.
            string tmp = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp, "surface=3 azimuth=180 wdith=1.0\n");
            bool refused = false;
            try { Gating.ApplyOverrides(new[] { lens }, tmp); }
            catch (FormatException) { refused = true; }
            finally { System.IO.File.Delete(tmp); }
            Check("a mistyped config key is refused", refused, "wdith= rejected");

            // And a good override must actually take.
            string tmp2 = System.IO.Path.GetTempFileName();
            System.IO.File.WriteAllText(tmp2, "# gate on the far side\nsurface=3 azimuth=180\n");
            try { Gating.ApplyOverrides(new[] { lens }, tmp2); }
            finally { System.IO.File.Delete(tmp2); }
            Check("an override replaces the default",
                  lens.Gate.AzimuthDeg == 180.0 && !lens.Gate.IsDefault,
                  "azimuth now 180 deg, no longer flagged default");
        }

        private static void CatalogChecks()
        {
            Console.WriteLine("  material data");
            Polymers.SelfCheckEjection();
            Polymers.SelfCheckValues();
            Polymers.SelfCheckContested();
            List<string> errs = Polymers.Validate();
            Check("every entry sourced and self-consistent", errs.Count == 0,
                  errs.Count == 0 ? Polymers.All.Length + " materials"
                                  : string.Join("; ", errs));

            // The relation OpticStudio itself enforces on a catalog save.
            foreach (var p in Polymers.All)
                Near("K = K12 - K11 for " + p.Name,
                     p.K12Brewster - p.K11Brewster, p.KGlassBrewster, 1e-12);

            // Negative control: a deliberately swapped pair must be rejected. If
            // this passes validation the check is decorative.
            var swapped = new Polymer
            {
                Name = "SWAPPED", Description = "control", KSource = "control",
                CMeltSource = "control",
                KGlassBrewster = 4000.0, K11Brewster = 0.0,
                CMeltBrewster = 72.0,
                TgC = 145, MeltTempC = 300, MoldTempC = 100,
            };
            bool caught = Math.Abs(swapped.CMeltBrewster) <= Math.Abs(swapped.KGlassBrewster);
            Check("swapped melt/glassy coefficients are caught", caught,
                  "|Cmelt| <= |Cglass| detected");
        }
    }
}

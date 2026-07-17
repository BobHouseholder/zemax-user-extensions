using System;

namespace CryoGlass
{
    // NASA GSFC CHARMS temperature-dependent Sellmeier (TSM) coefficients,
    // transcribed from the published papers (free NTRS/arXiv full texts).
    //
    //   n^2(lambda,T) - 1 = SUM_i  S_i(T) * lambda^2 / (lambda^2 - lambda_i(T)^2)
    //
    // with S_i(T) and lambda_i(T) each a 4th-order polynomial in T (Kelvin),
    // lambda in microns. Coefficient rows below are ordered constant, T, T^2,
    // T^3, T^4; columns S1 S2 S3 L1 L2 L3. Every material carries published
    // MEASURED index values as self-test anchors - the tool refuses to run if
    // its evaluator disagrees with the paper's own tables, so a transcription
    // error can never silently produce catalogs.
    class CharmsMaterial
    {
        public string Name;             // catalog-safe short name
        public string Description;
        public double LambdaMinUm, LambdaMaxUm;
        public double TminK, TmaxK;
        public double AccuracyAbs;      // published measurement uncertainty class
        public string Source;
        public double[][] S;            // [3][5] resonance strengths polynomials
        public double[][] L;            // [3][5] resonance wavelength polynomials
        public double[][] SelfTest;     // rows of {lambda_um, T_K, n_published}
    }

    static class CharmsData
    {
        public static readonly CharmsMaterial[] Materials =
        {
            new CharmsMaterial
            {
                Name = "SI_CHARMS",
                Description = "Silicon (single crystal), CHARMS TSM fit",
                LambdaMinUm = 1.1, LambdaMaxUm = 5.6, TminK = 20, TmaxK = 300,
                AccuracyAbs = 1e-4,
                Source = "Frey, Leviton & Madison, Proc. SPIE 6273, 62732J (2006), Table 5; NTRS 20070021411",
                S = new[]
                {
                    new[] { 10.4907, -2.08020E-04, 4.21694E-06, -5.82298E-09, 3.44688E-12 },
                    new[] { -1346.61, 29.1664, -0.278724, 1.05939E-03, -1.35089E-06 },
                    new[] { 4.42827E+07, -1.76213E+06, -7.61575E+04, 678.414, 103.243 },
                },
                L = new[]
                {
                    new[] { 0.299713, -1.14234E-05, 1.67134E-07, -2.51049E-10, 2.32484E-14 },
                    new[] { -3.51710E+03, 42.3892, -0.357957, 1.17504E-03, -1.13212E-06 },
                    new[] { 1.71400E+06, -1.44984E+05, -6.90744E+03, -39.3699, 23.5770 },
                },
                // Table 2 (measured absolute index), spread across the domain
                SelfTest = new[]
                {
                    new[] { 1.1, 30.0, 3.51113 },
                    new[] { 1.5, 100.0, 3.45609 },
                    new[] { 2.5, 295.0, 3.44011 },
                    new[] { 3.0, 150.0, 3.41240 },
                    new[] { 2.0, 50.0, 3.42508 },
                    new[] { 3.0, 295.0, 3.43293 },
                },
            },
            new CharmsMaterial
            {
                Name = "GE_CHARMS",
                Description = "Germanium (single crystal), CHARMS TSM fit",
                LambdaMinUm = 1.9, LambdaMaxUm = 5.5, TminK = 20, TmaxK = 300,
                AccuracyAbs = 1e-4,
                Source = "Frey, Leviton & Madison, Proc. SPIE 6273, 62732J (2006), Table 10; NTRS 20070021411",
                S = new[]
                {
                    new[] { 13.9723, 2.52809E-03, -5.02195E-06, 2.22604E-08, -4.86238E-12 },
                    new[] { 0.452096, -3.09197E-03, 2.16895E-05, -6.02290E-08, 4.12038E-11 },
                    new[] { 751.447, -14.2843, -0.238093, 2.96047E-03, -7.73454E-06 },
                },
                L = new[]
                {
                    new[] { 0.386367, 2.01871E-04, -5.93448E-07, -2.27923E-10, 5.37423E-12 },
                    new[] { 1.08843, 1.16510E-03, -4.97284E-06, 1.12357E-08, 9.40201E-12 },
                    new[] { -2893.19, -0.967948, -0.527016, 6.49364E-03, -1.95162E-05 },
                },
                // Table 7 (measured absolute index); 1.8 um row excluded (outside fit range)
                SelfTest = new[]
                {
                    new[] { 2.0, 30.0, 4.01922 },
                    new[] { 2.5, 100.0, 3.99502 },
                    new[] { 3.5, 150.0, 3.97985 },
                    new[] { 4.0, 200.0, 3.98949 },
                    new[] { 5.5, 295.0, 4.01404 },
                    new[] { 5.0, 60.0, 3.94349 },
                },
            },
        };

        public static CharmsMaterial Find(string name)
        {
            foreach (var m in Materials)
                if (m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || m.Name.Replace("_CHARMS", "").Equals(name, StringComparison.OrdinalIgnoreCase))
                    return m;
            return null;
        }
    }
}

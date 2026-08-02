using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

// Locates the OpticStudio installation the ZOS-API assemblies should be loaded from.
//
// ZOSAPI_Initializer.Initialize() with no argument resolves through a registration
// that is not necessarily the newest install. On a machine where an old release was
// never uninstalled it happily hands back that one - observed resolving to
// "c:\program files\zemax opticstudio 18.7" on a box running 2026 R1.00 - and every
// connection afterwards fails in a way that reads like a licence problem rather than
// a path problem: CreateNewApplication returns an application with
// LicenseStatus.Unknown, and ConnectAsExtension against a modern OpticStudio returns
// NotAuthorized. The extensions only ever saw the symptom.
//
// The no-argument call works from a host loaded out of the install directory itself
// (ZOSAPI.dll sits beside the helper), which is why this never shows up in a REPL and
// only bites the deployed .exe in {Zemax Data}\ZOS-API\Extensions.
//
// So pick the directory explicitly - newest ZOSAPI.dll wins, a ZEMAX_ROOT environment
// variable overrides everything - and pass it to the Initialize(string) overload. The
// bare call remains the last resort so nothing regresses on a layout this does not
// anticipate.
static class ZemaxLocator
{
    public static string ResolvedDirectory { get; private set; }

    public static bool Initialize()
    {
        foreach (string dir in Candidates())
        {
            try
            {
                if (!ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize(dir)) continue;
                ResolvedDirectory = ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory();
                return true;
            }
            catch { /* try the next candidate */ }
        }
        try
        {
            if (ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize())
            {
                ResolvedDirectory = ZOSAPI_NetHelper.ZOSAPI_Initializer.GetZemaxDirectory();
                return true;
            }
        }
        catch { }
        return false;
    }

    static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string env = Environment.GetEnvironmentVariable("ZEMAX_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string e = env.Trim().TrimEnd('\\');
            if (HasZosApi(e) && seen.Add(e)) yield return e;
        }

        var roots = new List<string>();
        foreach (var sf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string r = null;
            try { r = Environment.GetFolderPath(sf); } catch { }
            if (!string.IsNullOrEmpty(r)) roots.Add(r);
        }

        var found = new List<KeyValuePair<Version, string>>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root, "*OpticStudio*"); }
            catch { continue; }
            foreach (string d in dirs)
            {
                if (!HasZosApi(d)) continue;
                found.Add(new KeyValuePair<Version, string>(VersionOf(d), d));
            }
        }

        foreach (var kv in found.OrderByDescending(k => k.Key))
            if (seen.Add(kv.Value)) yield return kv.Value;
    }

    static bool HasZosApi(string dir)
    {
        try { return File.Exists(Path.Combine(dir, "ZOSAPI.dll")); }
        catch { return false; }
    }

    // Order by the file version of ZOSAPI.dll rather than by directory name: the
    // names do not sort usefully ("Ansys Zemax OpticStudio 2026 R1.00" sorts before
    // "Zemax OpticStudio 18.7"), and the file version is what actually identifies
    // the release.
    static Version VersionOf(string dir)
    {
        try
        {
            string v = FileVersionInfo.GetVersionInfo(Path.Combine(dir, "ZOSAPI.dll")).FileVersion;
            Version parsed;
            if (!string.IsNullOrWhiteSpace(v) && Version.TryParse(v.Trim(), out parsed)) return parsed;
        }
        catch { }
        return new Version(0, 0);
    }
}

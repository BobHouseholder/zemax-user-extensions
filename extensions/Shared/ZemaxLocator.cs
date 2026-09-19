using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO;
using System.Linq;

// ============================================================
// ZemaxLocator - find which OpticStudio to talk to
// ============================================================
// Picks the right OpticStudio install so ZOS-API loads from a
// real folder. The no-argument initializer can stick to an old
// install that was never uninstalled; we prefer a usable newest
// one. Shared by every extension in this repo.
// ============================================================

static class ZemaxLocator
{
    public static string ResolvedDirectory { get; private set; }

    // Call THIS from Main, not Initialize().
    //
    // Initialize() names ZOSAPI_NetHelper types, so the JIT must resolve that
    // assembly to compile it. If the helper is missing - the .exe copied somewhere
    // without it, or a damaged install - that throws FileNotFoundException at the
    // moment Initialize() is compiled, which is BEFORE its first statement runs and
    // outside any try block written inside it. Unhandled, the process does not exit,
    // it parks on a Windows Error Reporting dialog: observed as exit code
    // 0xE0434352 with a single line in the launch log and nothing else, and for a
    // ribbon-launched add-in it leaves OpticStudio waiting on a process that will
    // never finish.
    //
    // A try around the CALL works where a try inside the callee cannot, because the
    // handler is already established when the callee is compiled. That only holds if
    // the callee is not inlined into the caller - inlining would drag the ZOSAPI
    // references back into this method and move the failure to ITS compilation, one
    // frame further out and unprotected again. Hence NoInlining below; it is
    // load-bearing, not decoration.
        // Find OpticStudio and initialize the ZOS-API assemblies.
    public static bool TryInitialize(out string error)
    {
        error = null;
        try
        {
            return Initialize();
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    // Attach path only. Call from Run() AFTER Main has already TryInitialize'd,
    // or call this and let it TryInitialize first. Never call from Main itself:
    // the ZOSAPI.IZOSAPI_Application out-parameter would force that assembly to
    // load when Main is compiled, which is before any locator can run.
    //
    // standaloneFile true: locate only. The caller still CreateNewApplication
    // themselves so a -file standalone path is not rewritten as an attach.
    //
    // A stub with no PrimarySystem is NOT a connection: ConnectAsExtension
    // returns a live-looking application with PrimarySystem == null when no
    // OpticStudio is listening, and every later call then fails in a way that
    // reads like a licence fault instead of a missing host.
        // Connect to a running OpticStudio, or optionally start standalone.
    public static bool TryConnect(out ZOSAPI.IZOSAPI_Application app, out string error, bool standaloneFile)
    {
        app = null;
        if (!TryInitialize(out error))
            return false;
        if (standaloneFile)
            return true;
        return AttachToHost(out app, out error);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool AttachToHost(out ZOSAPI.IZOSAPI_Application app, out string error)
    {
        app = null;
        error = null;
        var connection = new ZOSAPI.ZOSAPI_Connection();
        try { app = connection.ConnectToApplication(); } catch { app = null; }
        if (app == null)
        {
            try { app = connection.ConnectAsExtension(0); } catch { app = null; }
        }
        if (app == null || app.PrimarySystem == null)
        {
            error = "could not connect to OpticStudio (use the Programming ribbon or Interactive Extension)";
            app = null;
            return false;
        }
        if (!app.IsValidLicenseForAPI)
        {
            error = "license is not valid for ZOS-API: " + app.LicenseStatus;
            app = null;
            return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
        // Initialize ZOS-API using the located install (throws-free wrapper).
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

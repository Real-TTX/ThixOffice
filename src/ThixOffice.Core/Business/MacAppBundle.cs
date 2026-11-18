using System.Diagnostics;
using System.Runtime.Versioning;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// macOS: an app of the suite is an app bundle (ThixWrite.app, usually in /Applications). There is no setup - the user drags
/// the app from the disk image into "Programme" and removes it by moving it to the bin.
/// An update replaces the whole bundle: the downloaded zip is unpacked with ditto, and a short shell script waits until
/// the app has ended, swaps the bundles (the old one comes back if that fails) and opens the app again when the user asked for it (update now, not at closing time).
/// </summary>
public static class MacAppBundle
{
    /// <summary>The bundle this process runs from (…/ThixWrite.app/Contents/MacOS/ThixWrite), or null.</summary>
    public static string? Current => BundleOf(Environment.ProcessPath);

    internal static string? BundleOf(string? executablePath)
    {
        var macOsFolder = Path.GetDirectoryName(executablePath ?? "");
        var contents = Path.GetDirectoryName(macOsFolder ?? "");
        var bundle = Path.GetDirectoryName(contents ?? "");
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetFileName(macOsFolder), "MacOS", StringComparison.Ordinal) &&
               string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)
            ? bundle
            : null;
    }

    [SupportedOSPlatform("macos")]
    internal static void StartReplacement(string zipFile, bool reopen)
    {
        var bundle = Current ?? throw new BusinessException($"Nur die installierte {AppInfo.ProductName}.app kann sich aktualisieren.");
        var unpacked = Path.Combine(AppPaths.UpdateDirectory, "unpacked");
        if (Directory.Exists(unpacked))
        {
            Directory.Delete(unpacked, recursive: true);
        }

        Run("/usr/bin/ditto", "-x", "-k", zipFile, unpacked);
        var newBundle = Directory.GetDirectories(unpacked, "*.app").FirstOrDefault()
                        ?? throw new BusinessException($"Das Update enthält keine {AppInfo.ProductName}.app.");

        var script = Path.Combine(AppPaths.UpdateDirectory, "replace-app.sh");
        File.WriteAllText(script, ReplacementScript(Environment.ProcessId, bundle, newBundle, Path.Combine(AppPaths.LogDirectory, "update.log"), reopen));
        Log.Info($"Replacing {bundle} after the exit of process {Environment.ProcessId}");
        Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { script }, UseShellExecute = false });
    }

    /// <summary>The script that swaps the bundles. Paths are passed as single-quoted shell words.</summary>
    internal static string ReplacementScript(int processId, string bundle, string newBundle, string logFile, bool reopen) => $"""
        #!/bin/sh
        # Written by {AppInfo.ProductName}: replaces the app bundle once it has ended.
        BUNDLE={Quote(bundle)}
        NEW={Quote(newBundle)}
        LOG={Quote(logFile)}
        while kill -0 {processId} 2>/dev/null; do sleep 0.5; done
        rm -rf "$BUNDLE.old"
        if mv "$BUNDLE" "$BUNDLE.old" && mv "$NEW" "$BUNDLE"; then
          rm -rf "$BUNDLE.old"
          echo "$(date) update installed" >> "$LOG"
        else
          [ -d "$BUNDLE" ] || mv "$BUNDLE.old" "$BUNDLE"
          echo "$(date) update failed, previous version kept" >> "$LOG"
        fi
        xattr -dr com.apple.quarantine "$BUNDLE" 2>/dev/null
        {(reopen ? "open \"$BUNDLE\" --args --restart" : "# the app was closed by its user: it stays closed")}

        """;

    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static void Run(string program, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new BusinessException($"Das Update konnte nicht entpackt werden: {errors.Trim()}");
        }
    }
}

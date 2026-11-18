using System.Security;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Core.Business;

/// <summary>
/// Start at sign-in for the current user – off unless the user turns it on (an editor does not start with the computer by
/// default; the setup does not offer it). The entry itself is the truth; the settings page shows what it finds.
///   Windows: a value named after the app in HKCU\...\Run (uninstalling removes it).
///   macOS:   a LaunchAgent ~/Library/LaunchAgents/&lt;bundle id&gt;.plist (RunAtLoad).
/// </summary>
public static class AutostartService
{
    public const string StartupArgument = "--autostart";

    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string? _launchAgentFolderForTests;

    /// <summary>The LaunchAgent file (macOS).</summary>
    public static string LaunchAgentFile => Path.Combine(
        _launchAgentFolderForTests ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
        AppInfo.Definition.MacBundleId + ".plist");

    /// <summary>macOS (or a test with its own folder): the LaunchAgent; otherwise the Run value.</summary>
    private static bool UsesLaunchAgent => OperatingSystem.IsMacOS() || _launchAgentFolderForTests is not null;

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static bool IsEnabled()
    {
        if (UsesLaunchAgent)
        {
            return File.Exists(LaunchAgentFile);
        }

        return UserRegistry.Current.GetValue(RunKeyPath, AppInfo.ProductName) is string command && command.Length > 0;
    }

    public static void Enable(string executablePath)
    {
        AccessPolicy.EnsureConfigurationEditable();
        WriteEntry(executablePath);
    }

    public static void Disable()
    {
        AccessPolicy.EnsureConfigurationEditable();
        RemoveEntry();
    }

    /// <summary>For the setup: installing and uninstalling the user's own program is not a configuration change.</summary>
    internal static void WriteEntry(string executablePath)
    {
        if (UsesLaunchAgent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentFile)!);
            File.WriteAllText(LaunchAgentFile, LaunchAgent(executablePath));
            return;
        }

        UserRegistry.Current.SetValue(RunKeyPath, AppInfo.ProductName, $"\"{executablePath}\" {StartupArgument}");
    }

    internal static void RemoveEntry()
    {
        if (UsesLaunchAgent)
        {
            if (File.Exists(LaunchAgentFile))
            {
                File.Delete(LaunchAgentFile);
            }

            return;
        }

        UserRegistry.Current.DeleteValue(RunKeyPath, AppInfo.ProductName);
    }

    /// <summary>launchd starts the program at sign-in; it does not restart it when the user ends it (no KeepAlive).</summary>
    internal static string LaunchAgent(string executablePath) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{AppInfo.Definition.MacBundleId}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executablePath)}</string>
            <string>{StartupArgument}</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>

        """;

    /// <summary>Tests use a folder of their own for the LaunchAgent (on every system; null: the real places).</summary>
    internal static void UseLaunchAgentFolderForTests(string? folder) => _launchAgentFolderForTests = folder;
}

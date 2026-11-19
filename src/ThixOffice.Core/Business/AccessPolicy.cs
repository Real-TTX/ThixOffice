using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// The permission checks of the business layer. Every service calls them itself before it changes something, whatever the
/// UI shows or hides (DESK-10).
///
/// An administrator restricts an app with machine policies (value 1 / true), per app:
///   Windows: HKLM\SOFTWARE\Policies\&lt;App&gt; (DWORD), e.g. HKLM\SOFTWARE\Policies\ThixWrite
///   macOS:   a configuration profile for the app's bundle id (/Library/Managed Preferences/info.schmoldt.thixwrite.plist)
/// Policies:
///   LockConfiguration       the settings are read-only (documents stay usable)
///   DisableUpdates          no update check, no update installation
///   DisableCloud            no connection to ThixCloud (later phase)
///   DisableFileAssociations the app does not register its document types with the system
///   DisablePrinting         the app does not print (PDF export stays)
/// </summary>
public static class AccessPolicy
{
    public const string LockConfiguration = "LockConfiguration";
    public const string DisableUpdates = "DisableUpdates";
    public const string DisableCloud = "DisableCloud";
    public const string DisableFileAssociations = "DisableFileAssociations";
    public const string DisablePrinting = "DisablePrinting";

    private static Func<string, bool> _readPolicy = ReadMachinePolicy;

    private static (string File, DateTime Written, Dictionary<string, bool> Values) _macPolicies = ("", DateTime.MinValue, []);

    /// <summary>Every policy name an administrator can set (the settings page lists them).</summary>
    public static IReadOnlyList<string> Names { get; } = [LockConfiguration, DisableUpdates, DisableCloud, DisableFileAssociations, DisablePrinting];

    public static bool IsConfigurationLocked => _readPolicy(LockConfiguration);

    public static bool AreUpdatesDisabled => _readPolicy(DisableUpdates);

    public static bool IsCloudDisabled => _readPolicy(DisableCloud);

    public static bool AreFileAssociationsDisabled => _readPolicy(DisableFileAssociations);

    public static bool IsPrintingDisabled => _readPolicy(DisablePrinting);

    /// <summary>Where an administrator sets them, for the settings page.</summary>
    public static string Location => $@"Windows: HKLM\{AppInfo.Definition.PolicyKey}, macOS: Profil für {AppInfo.Definition.MacBundleId}";

    public static void EnsureConfigurationEditable()
    {
        if (IsConfigurationLocked)
        {
            throw new BusinessException("Die Einstellungen wurden von einem Administrator gesperrt (Richtlinie LockConfiguration).");
        }
    }

    public static void EnsureUpdatesAllowed()
    {
        if (AreUpdatesDisabled)
        {
            throw new BusinessException("Updates wurden von einem Administrator deaktiviert (Richtlinie DisableUpdates).");
        }
    }

    public static void EnsureCloudAllowed()
    {
        if (IsCloudDisabled)
        {
            throw new BusinessException("Die Verbindung zur ThixCloud wurde von einem Administrator deaktiviert (Richtlinie DisableCloud).");
        }
    }

    public static void EnsureFileAssociationsAllowed()
    {
        if (AreFileAssociationsDisabled)
        {
            throw new BusinessException("Dateizuordnungen wurden von einem Administrator deaktiviert (Richtlinie DisableFileAssociations).");
        }
    }

    public static void EnsurePrintingAllowed()
    {
        if (IsPrintingDisabled)
        {
            throw new BusinessException("Das Drucken wurde von einem Administrator deaktiviert (Richtlinie DisablePrinting).");
        }
    }

    /// <summary>Tests replace the registry with their own answers (null: the machine's policies again).</summary>
    internal static void UsePolicyReaderForTests(Func<string, bool>? reader) => _readPolicy = reader ?? ReadMachinePolicy;

    private static bool ReadMachinePolicy(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsPolicy(name);
        }

        return OperatingSystem.IsMacOS() && ReadMacPolicies().GetValueOrDefault(name);
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadWindowsPolicy(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(AppInfo.Definition.PolicyKey);
        return key?.GetValue(name) is int value && value == 1;
    }

    /// <summary>The managed preferences are a (binary) plist; plutil turns it into JSON. Read again only after a change.</summary>
    private static Dictionary<string, bool> ReadMacPolicies()
    {
        var file = $"/Library/Managed Preferences/{AppInfo.Definition.MacBundleId}.plist";
        var written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (file == _macPolicies.File && written == _macPolicies.Written)
        {
            return _macPolicies.Values;
        }

        var values = new Dictionary<string, bool>();
        if (written != DateTime.MinValue)
        {
            try
            {
                var startInfo = new ProcessStartInfo("/usr/bin/plutil") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-convert", "json", "-o", "-", file })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo)!;
                var json = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                using var document = JsonDocument.Parse(json);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.True ||
                                            (property.Value.ValueKind == JsonValueKind.Number && property.Value.GetInt32() == 1);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Warning($"Could not read the policies in {file}: {exception.Message}");
            }
        }

        _macPolicies = (file, written, values);
        return values;
    }
}

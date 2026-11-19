using System.Runtime.InteropServices;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// The document types of an app on Windows (per user, HKCU\Software\Classes; macOS declares them in Info.plist,
/// scripts/package-macos.sh). Only SUPPORTED types are registered – a planned format never shows up before the app can read it.
///
/// Every supported type gets the app's ProgID (with the document icon) and the app is entered under "Öffnen mit"
/// (OpenWithProgids, Applications\&lt;exe&gt;) and in "Standard-Apps" (Capabilities, RegisteredApplications). Only the types the
/// user chose in the setup (the install options, SETUP-08: <c>defaults</c>) get the app as the extension's default
/// (HKCU\Software\Classes\.ext); the others are released when they had it. A choice the user made himself in "Öffnen mit"
/// (Windows' UserChoice) stays – no program may change it.
/// </summary>
public static class FileAssociations
{
    public const string ClassesPath = @"Software\Classes";
    public const string RegisteredApplicationsPath = @"Software\RegisteredApplications";

    /// <summary>The keys of "Standard-Apps": Software\ThixWrite\Capabilities.</summary>
    public static string CapabilitiesPath(OfficeAppDefinition definition) => $@"Software\{definition.Name}\Capabilities";

    /// <summary>The command Explorer runs for a document: the program with the file.</summary>
    public static string OpenCommand(string executable) => $"\"{executable}\" \"%1\"";

    /// <summary>
    /// Registers every supported type of <paramref name="definition"/> for <paramref name="executable"/> (see the class).
    /// <paramref name="defaults"/>: the extensions the app opens by default (the others lose the app as their default); null
    /// leaves the defaults as they are ("Erneut eintragen").
    /// </summary>
    public static void Register(IUserRegistry registry, OfficeAppDefinition definition, string executable, string? documentIcon, IReadOnlyCollection<string>? defaults)
    {
        var openCommand = OpenCommand(executable);
        var icon = documentIcon is not null ? $"\"{documentIcon}\"" : $"\"{executable}\",0";
        var supported = definition.SupportedTypes;
        foreach (var type in supported)
        {
            var progIdPath = $@"{ClassesPath}\{type.ProgId}";
            registry.SetValue(progIdPath, "", type.Description);
            registry.SetValue(progIdPath, "FriendlyTypeName", type.Description);
            registry.SetValue($@"{progIdPath}\DefaultIcon", "", icon);
            registry.SetValue($@"{progIdPath}\shell\open\command", "", openCommand);

            var extensionPath = $@"{ClassesPath}\{type.Extension}";
            registry.SetValue($@"{extensionPath}\OpenWithProgids", type.ProgId, "", RegistryValueType.None);
            if (defaults is null)
            {
                continue;
            }

            if (defaults.Contains(type.Extension, StringComparer.OrdinalIgnoreCase))
            {
                registry.SetValue(extensionPath, "", type.ProgId);
                if (type.MimeType is not null && registry.GetValue(extensionPath, "Content Type") is null)
                {
                    registry.SetValue(extensionPath, "Content Type", type.MimeType);
                }
            }
            else if (IsDefaultFor(registry, type))
            {
                registry.DeleteValue(extensionPath, "");
            }
        }

        var applicationPath = $@"{ClassesPath}\Applications\{definition.ExecutableName}";
        registry.SetValue(applicationPath, "FriendlyAppName", definition.Name);
        foreach (var type in supported)
        {
            registry.SetValue($@"{applicationPath}\SupportedTypes", type.Extension, "");
        }

        registry.SetValue($@"{applicationPath}\shell\open\command", "", openCommand);

        var capabilities = CapabilitiesPath(definition);
        registry.SetValue(capabilities, "ApplicationName", definition.Name);
        registry.SetValue(capabilities, "ApplicationDescription", definition.Description);
        foreach (var type in supported)
        {
            registry.SetValue($@"{capabilities}\FileAssociations", type.Extension, type.ProgId);
        }

        registry.SetValue(RegisteredApplicationsPath, definition.Name, capabilities);
        NotifyShell(registry);
    }

    /// <summary>Removes everything <see cref="Register"/> wrote; an extension keeps its default unless it was the app's.</summary>
    public static void Unregister(IUserRegistry registry, OfficeAppDefinition definition)
    {
        foreach (var type in definition.DocumentTypes)
        {
            var extensionPath = $@"{ClassesPath}\{type.Extension}";
            if (registry.GetValue(extensionPath, "") as string == type.ProgId)
            {
                registry.DeleteValue(extensionPath, "");
                registry.DeleteValue(extensionPath, "Content Type");
            }

            registry.DeleteValue($@"{extensionPath}\OpenWithProgids", type.ProgId);
            registry.DeleteTree($@"{ClassesPath}\{type.ProgId}");
        }

        registry.DeleteTree($@"{ClassesPath}\Applications\{definition.ExecutableName}");
        registry.DeleteTree($@"Software\{definition.Name}");
        registry.DeleteValue(RegisteredApplicationsPath, definition.Name);
        NotifyShell(registry);
    }

    /// <summary>True when the app's ProgID of every supported type is there (the setup ran).</summary>
    public static bool IsRegistered(IUserRegistry registry, OfficeAppDefinition definition) =>
        definition.SupportedTypes.Count > 0 &&
        definition.SupportedTypes.All(type => registry.KeyExists($@"{ClassesPath}\{type.ProgId}\shell\open\command"));

    /// <summary>True when the extension's default (HKCU) is the app's ProgID. Windows may still open it with the user's own choice.</summary>
    public static bool IsDefaultFor(IUserRegistry registry, DocumentType type) =>
        registry.GetValue($@"{ClassesPath}\{type.Extension}", "") as string == type.ProgId;

    /// <summary>The supported extensions whose default (HKCU) is the app – what the setup chose (the install options).</summary>
    public static IReadOnlyList<string> Defaults(IUserRegistry registry, OfficeAppDefinition definition) =>
        definition.SupportedTypes.Where(type => IsDefaultFor(registry, type)).Select(type => type.Extension).ToList();

    // ===== For the app of this process =====

    /// <summary>
    /// "Dateitypen erneut eintragen" (settings): the setup's registration again – for the installed program when there is
    /// one, otherwise for <paramref name="runningExecutable"/>; the defaults stay as they are. Policy-checked.
    /// </summary>
    public static void RepairForThisApp(string runningExecutable)
    {
        AccessPolicy.EnsureFileAssociationsAllowed();
        if (!OperatingSystem.IsWindows())
        {
            throw new BusinessException("Dateitypen liest macOS aus dem Programm selbst – hier ist nichts zu tun.");
        }

        var installed = InstallService.IsInstalled;
        var executable = installed ? InstallService.InstalledExecutable : runningExecutable;
        var icon = installed && File.Exists(InstallService.DocumentIcon) ? InstallService.DocumentIcon : null;
        Register(UserRegistry.Current, AppInfo.Definition, executable, icon, defaults: null);
    }

    /// <summary>Whether the app of this process is registered for its types.</summary>
    public static bool IsRegisteredForThisApp => OperatingSystem.IsWindows() && IsRegistered(UserRegistry.Current, AppInfo.Definition);

    /// <summary>Tells Explorer to reload file icons and associations at once (only for the real registry).</summary>
    private static void NotifyShell(IUserRegistry registry)
    {
        if (OperatingSystem.IsWindows() && registry is WindowsUserRegistry)
        {
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

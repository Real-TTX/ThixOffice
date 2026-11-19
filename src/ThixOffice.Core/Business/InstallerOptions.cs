using System.Reflection;
using System.Text.Json;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// The install options of every app of the suite (SETUP-08 of MatDesign) – one contract for every Thix app:
/// <list type="bullet">
/// <item><see cref="Config"/>: what the setup offers, from the app's <c>installer.json</c> (src/&lt;App&gt;/installer.json, embedded in
/// the app's assembly; CI puts the same file into the update manifest as <c>"installer"</c>, where ThixLauncher and ThixInstall read
/// it).</item>
/// <item><see cref="Parse"/>: the command line of SETUP-05 – <c>--quiet</c> (install), <c>--update</c>, <c>--configure</c> (only
/// apply), <c>--uninstall</c>, <c>--start</c> / <c>--restart</c>, and the options <c>--desktop-shortcut=yes|no</c>,
/// <c>--autostart=yes|no</c>, <c>--automatic-updates=yes|no</c>, <c>--file-types=.docx,.odt|none|default</c>. A wrong value or an
/// option the app does not offer fails at once (exit code 2, nothing installed).</item>
/// <item><see cref="Current"/> and <see cref="Apply"/>: what the installed app has, and making it so (desktop shortcut, autostart,
/// automatic updates, file types – Windows per user in HKCU\Software\Classes through <see cref="FileAssociations"/>, macOS with
/// Launch Services).</item>
/// </list>
/// The Office apps offer the desktop shortcut, automatic updates and their supported document types (their own format on by
/// default); no autostart – an editor does not start with the computer.
/// </summary>
public static class InstallerOptions
{
    public const string QuietArgument = UninstallEntry.QuietArgument;
    public const string UpdateArgument = UpdateService.WindowsUpdateArgument;
    public const string RestartArgument = UpdateService.RestartArgument;
    public const string ConfigureArgument = "--configure";
    public const string UninstallArgument = UninstallEntry.UninstallArgument;
    public const string StartArgument = "--start";
    public const string DesktopShortcutOption = "--desktop-shortcut";
    public const string AutostartOption = "--autostart";
    public const string AutomaticUpdatesOption = "--automatic-updates";
    public const string FileTypesOption = "--file-types";

    /// <summary>The name of the embedded resource in the app's assembly.</summary>
    public const string ResourceName = "installer.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static (Assembly? From, InstallerConfig Config)? _loaded;
    private static InstallerConfig? _configForTests;

    /// <summary>What the setup of the app of this process offers (its installer.json).</summary>
    public static InstallerConfig Config
    {
        get
        {
            if (_configForTests is not null)
            {
                return _configForTests;
            }

            var assembly = AppInfo.AppAssembly;
            if (_loaded is not { } loaded || loaded.From != assembly)
            {
                loaded = (assembly, Load(assembly));
                _loaded = loaded;
            }

            return loaded.Config;
        }
    }

    /// <summary>The choices of a first install that is told nothing: the defaults of <see cref="Config"/>.</summary>
    public static InstallChoices Defaults => new(
        Config.DesktopShortcut?.Default ?? false,
        Config.Autostart?.Default ?? false,
        Config.AutomaticUpdates?.Default ?? false,
        Config.FileTypes.Where(type => type.Default).Select(type => type.Extension).ToList());

    /// <summary>Another configuration (tests); null = the app's installer.json again.</summary>
    internal static void UseConfigForTests(InstallerConfig? config) => _configForTests = config;

    /// <summary>
    /// Reads the setup part of a command line. Throws <see cref="BusinessException"/> for an option or value it does not know – a
    /// quiet install must not guess (SETUP-05). A bare "--autostart" (no "=") is the start at sign-in and no option.
    /// </summary>
    public static SetupRequest Parse(IReadOnlyList<string> args)
    {
        var action = args.Contains(UninstallArgument) ? SetupAction.Uninstall
            : args.Contains(ConfigureArgument) ? SetupAction.Configure
            : args.Contains(UpdateArgument) ? SetupAction.Update
            : args.Contains(QuietArgument) ? SetupAction.Install
            : SetupAction.None;
        var choices = new InstallChoices();
        foreach (var arg in args)
        {
            var separator = arg.IndexOf('=', StringComparison.Ordinal);
            if (!arg.StartsWith("--", StringComparison.Ordinal) || separator < 0)
            {
                continue;
            }

            var (name, value) = (arg[..separator], arg[(separator + 1)..].Trim());
            choices = name switch
            {
                DesktopShortcutOption => choices with { DesktopShortcut = Switch(name, value, Config.DesktopShortcut) },
                AutostartOption => choices with { Autostart = Switch(name, value, Config.Autostart) },
                AutomaticUpdatesOption => choices with { AutomaticUpdates = Switch(name, value, Config.AutomaticUpdates) },
                FileTypesOption => choices with { FileTypes = FileTypes(value) },
                _ => throw new BusinessException($"Unbekannte Option „{name}“. Möglich: {string.Join(", ", OfferedOptions())}."),
            };
        }

        if (action is SetupAction.None or SetupAction.Uninstall && !choices.IsEmpty)
        {
            throw new BusinessException($"Optionen gelten nur mit {QuietArgument}, {UpdateArgument} oder {ConfigureArgument}.");
        }

        return new SetupRequest(action, choices, args.Contains(QuietArgument), args.Contains(StartArgument) || args.Contains(RestartArgument));
    }

    /// <summary>The arguments of SETUP-05 for these choices (what ThixLauncher passes; tests and documentation).</summary>
    public static IReadOnlyList<string> ToArguments(InstallChoices choices)
    {
        var arguments = new List<string>();
        static string YesNo(bool value) => value ? "yes" : "no";
        if (choices.DesktopShortcut is { } shortcut)
        {
            arguments.Add($"{DesktopShortcutOption}={YesNo(shortcut)}");
        }

        if (choices.Autostart is { } autostart)
        {
            arguments.Add($"{AutostartOption}={YesNo(autostart)}");
        }

        if (choices.AutomaticUpdates is { } updates)
        {
            arguments.Add($"{AutomaticUpdatesOption}={YesNo(updates)}");
        }

        if (choices.FileTypes is { } types)
        {
            arguments.Add($"{FileTypesOption}={(types.Count == 0 ? "none" : string.Join(',', types))}");
        }

        return arguments;
    }

    /// <summary>What the installed app has now (an update keeps it): on Windows the file types whose default is the app.</summary>
    public static InstallChoices Current() => new(
        OperatingSystem.IsWindows() && InstallService.HasDesktopShortcut,
        AutostartService.IsEnabled(),
        SettingsService.Get().InstallUpdatesAutomatically,
        FileAssociations.Defaults(UserRegistry.Current, AppInfo.Definition)
            .Where(extension => Offers(extension))
            .ToList());

    /// <summary>
    /// Makes the choices so for the program <paramref name="executable"/> (the installed one). Only offered options are touched; a
    /// value of null stays as it is. Each step is reported to <paramref name="log"/>. On Windows the app is always entered under
    /// „Öffnen mit“ for every supported type (also an update points it at the installed file); the chosen types get it as their
    /// default, the others are released when they had it.
    /// </summary>
    public static void Apply(InstallChoices choices, string executable, Action<string> log)
    {
        if (Config.DesktopShortcut is not null && choices.DesktopShortcut is { } shortcut && OperatingSystem.IsWindows())
        {
            InstallService.SetDesktopShortcut(shortcut);
            log(shortcut ? "Desktop-Verknüpfung angelegt" : "Keine Desktop-Verknüpfung");
        }

        if (Config.Autostart is not null && choices.Autostart is { } autostart)
        {
            if (autostart)
            {
                AutostartService.WriteEntry(executable);
                log(OperatingSystem.IsMacOS() ? "Startet bei der Anmeldung" : "Autostart bei der Windows-Anmeldung eingerichtet");
            }
            else
            {
                AutostartService.RemoveEntry();
                log("Kein Autostart");
            }
        }

        if (Config.AutomaticUpdates is not null && choices.AutomaticUpdates is { } updates)
        {
            try
            {
                SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = updates });
                log(updates ? "Updates werden beim Beenden automatisch installiert" : "Updates werden nur angeboten");
            }
            catch (BusinessException exception)
            {
                // a configuration locked by policy keeps its value
                log(exception.Message);
            }
        }

        if (Config.FileTypes.Count > 0 && choices.FileTypes is { } wanted)
        {
            ApplyFileTypes(wanted, executable, log);
        }
    }

    private static void ApplyFileTypes(IReadOnlyList<string> wanted, string executable, Action<string> log)
    {
        if (AccessPolicy.AreFileAssociationsDisabled)
        {
            log("Dateitypen: von einem Administrator deaktiviert");
            return;
        }

        var definition = AppInfo.Definition;
        var associate = definition.SupportedTypes
            .Where(type => Offers(type.Extension) && wanted.Contains(type.Extension, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (OperatingSystem.IsWindows())
        {
            var icon = InstallService.IsInstalledExecutable(executable) && File.Exists(InstallService.DocumentIcon) ? InstallService.DocumentIcon : null;
            FileAssociations.Register(UserRegistry.Current, definition, executable, icon, associate.Select(type => type.Extension).ToList());
            log($"{definition.Name} unter „Öffnen mit“ eingetragen ({string.Join(", ", definition.SupportedTypes.Select(type => type.Extension))})");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Platform.MacOS.MacFileAssociations.MakeDefault(definition.MacBundleId, associate);
        }

        log(associate.Count == 0
            ? $"{definition.Name} öffnet keinen Dateityp als Standard"
            : $"Mit {definition.Name} öffnen: {string.Join(", ", associate.Select(type => type.Extension))}");
    }

    private static bool Offers(string extension) =>
        Config.FileTypes.Any(type => type.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> OfferedOptions()
    {
        if (Config.DesktopShortcut is not null)
        {
            yield return DesktopShortcutOption;
        }

        if (Config.Autostart is not null)
        {
            yield return AutostartOption;
        }

        if (Config.AutomaticUpdates is not null)
        {
            yield return AutomaticUpdatesOption;
        }

        if (Config.FileTypes.Count > 0)
        {
            yield return FileTypesOption;
        }
    }

    private static bool Switch(string name, string value, InstallerSwitch? offered)
    {
        if (offered is null)
        {
            throw new BusinessException($"{AppInfo.ProductName} bietet „{name}“ nicht an.");
        }

        return value.ToLowerInvariant() switch
        {
            "yes" or "true" or "1" or "ja" => true,
            "no" or "false" or "0" or "nein" => false,
            _ => throw new BusinessException($"{name} erwartet yes oder no, nicht „{value}“."),
        };
    }

    private static IReadOnlyList<string> FileTypes(string value)
    {
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return Defaults.FileTypes!;
        }

        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var types = new List<string>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = part.StartsWith('.') ? part : "." + part;
            var offered = Config.FileTypes.FirstOrDefault(type => type.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                          ?? throw new BusinessException(Config.FileTypes.Count == 0
                              ? $"{AppInfo.ProductName} verknüpft keine Dateitypen."
                              : $"{AppInfo.ProductName} verknüpft „{extension}“ nicht. Möglich: {string.Join(", ", Config.FileTypes.Select(type => type.Extension))}.");
            if (!types.Contains(offered.Extension))
            {
                types.Add(offered.Extension);
            }
        }

        return types;
    }

    private static InstallerConfig Load(Assembly? assembly)
    {
        using var stream = assembly?.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} is not embedded in {assembly?.GetName().Name ?? "the app"}");
        return JsonSerializer.Deserialize<InstallerConfig>(stream, JsonOptions) ?? new InstallerConfig();
    }
}

namespace ThixOffice.Core.Models;

/// <summary>
/// What the setup of an app offers (SETUP-08 of MatDesign): the app's <c>installer.json</c> (src/&lt;App&gt;/installer.json),
/// embedded in the program and published as the block <c>"installer"</c> of the update manifest – so ThixLauncher and
/// ThixInstall ask the same questions as the setup assistant and pass the answers to the quiet install. An option that is null
/// is not offered.
/// </summary>
public sealed record InstallerConfig
{
    /// <summary>A shortcut on the desktop (Windows; the Start menu entry is always made).</summary>
    public InstallerSwitch? DesktopShortcut { get; init; }

    /// <summary>Start at sign-in (Windows: the Run key, macOS: a LaunchAgent). The Office apps do not offer it.</summary>
    public InstallerSwitch? Autostart { get; init; }

    /// <summary>Install updates without asking (when the app is closed).</summary>
    public InstallerSwitch? AutomaticUpdates { get; init; }

    /// <summary>The file types the app opens; each one may be associated with it (the app opens it by default).</summary>
    public IReadOnlyList<InstallerFileType> FileTypes { get; init; } = [];
}

/// <summary>An option that is on or off; <see cref="Default"/> is what a quiet install does when it is not given.</summary>
public sealed record InstallerSwitch
{
    public bool Default { get; init; }
}

/// <summary>A file type the app opens: ".docx", its name ("Word-Dokument") and whether it is associated by default.</summary>
public sealed record InstallerFileType
{
    public string Extension { get; init; } = "";

    public string Name { get; init; } = "";

    public bool Default { get; init; }
}

/// <summary>
/// The choices of one install: a value per option, null = not given (a first install takes the default of the
/// <see cref="InstallerConfig"/>, an installed app keeps what it has). <see cref="FileTypes"/> lists the extensions to associate.
/// </summary>
public sealed record InstallChoices(bool? DesktopShortcut = null, bool? Autostart = null, bool? AutomaticUpdates = null, IReadOnlyList<string>? FileTypes = null)
{
    /// <summary>These choices where given, <paramref name="fallback"/> where not.</summary>
    public InstallChoices Over(InstallChoices fallback) => new(
        DesktopShortcut ?? fallback.DesktopShortcut,
        Autostart ?? fallback.Autostart,
        AutomaticUpdates ?? fallback.AutomaticUpdates,
        FileTypes ?? fallback.FileTypes);

    /// <summary>True when no option was given.</summary>
    public bool IsEmpty => DesktopShortcut is null && Autostart is null && AutomaticUpdates is null && FileTypes is null;
}

/// <summary>What the command line of the program asks for (SETUP-05).</summary>
public enum SetupAction
{
    /// <summary>No setup argument: the program (or, started outside its folder, the setup assistant).</summary>
    None,

    /// <summary>"--quiet": install without a window.</summary>
    Install,

    /// <summary>"--update": update without a window (an installed app keeps its choices).</summary>
    Update,

    /// <summary>"--configure": apply the choices to the installed app, nothing is copied (macOS after the bundle was copied).</summary>
    Configure,

    /// <summary>"--uninstall" (with "--quiet" without a window).</summary>
    Uninstall,
}

/// <summary>
/// The setup part of a command line: what to do, with which choices, whether without a window, whether to start the app
/// afterwards ("--start" after a quiet install, "--restart" after an update).
/// </summary>
public sealed record SetupRequest(SetupAction Action, InstallChoices Choices, bool Quiet = false, bool Start = false);

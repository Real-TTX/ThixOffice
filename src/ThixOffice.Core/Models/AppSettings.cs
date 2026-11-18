namespace ThixOffice.Core.Models;

/// <summary>"Erscheinungsbild": Wie das System, Hell, Dunkel (DESK-02).</summary>
public enum ThemeChoice
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// The settings of an app on this computer (settings.json in its data folder). Only <see cref="Business.SettingsService"/>
/// writes them. The autostart is no value in here: the entry itself (Run key / LaunchAgent) is the truth, and it is off
/// unless the user turns it on (an editor does not start with the computer by default).
/// </summary>
public sealed record AppSettings
{
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    // ===== Updates =====

    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Install new versions without asking – when the app closes, never under the user's hands.</summary>
    public bool InstallUpdatesAutomatically { get; init; } = true;

    // ===== Documents =====

    /// <summary>How many documents "Zuletzt verwendet" keeps (pinned ones are not counted).</summary>
    public int RecentListSize { get; init; } = 25;

    /// <summary>Where "Speichern unter" starts; empty = the documents folder of the user.</summary>
    public string DefaultSaveFolder { get; init; } = "";

    /// <summary>Keep the previous version as "&lt;name&gt;.bak" next to the document when saving.</summary>
    public bool KeepBackupCopy { get; init; }

    /// <summary>How often unsaved changes are written to a recovery file (minutes, 0 = never).</summary>
    public int AutoRecoverMinutes { get; init; } = 5;

    // ===== Remembered by the app =====

    /// <summary>The ribbon shows only its tabs (Strg+F1).</summary>
    public bool IsRibbonCollapsed { get; init; }

    public double WindowWidth { get; init; } = 1180;

    public double WindowHeight { get; init; } = 760;
}

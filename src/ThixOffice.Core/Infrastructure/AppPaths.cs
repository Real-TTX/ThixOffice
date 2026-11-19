namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// Where an app keeps its own files (never the documents: they are wherever the user saves them). Program files and data
/// are separate: updates and uninstalling never touch the data unless the user decides so.
///   Windows: %LocalAppData%\ThixWrite (settings, recent documents, recovery files, updates, logs)
///   macOS:   ~/Library/Application Support/ThixWrite
/// A developer or a test points an app to a folder of its own with &lt;APP&gt;_DATA_DIR (THIXWRITE_DATA_DIR); such a run
/// is an instance of its own and never meets the installed app.
/// </summary>
public static class AppPaths
{
    private static string? _dataDirectoryForTests;

    public static string DataDirectory =>
        _dataDirectoryForTests ?? Override() ?? Path.Combine(UserFolder(), AppInfo.ProductName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>"Zuletzt verwendet".</summary>
    public static string RecentDocumentsFile => Path.Combine(DataDirectory, "recent.json");

    /// <summary>The automatic recovery files of documents with unsaved changes (deleted when the document is saved or closed).</summary>
    public static string RecoveryDirectory => Path.Combine(DataDirectory, "recovery");

    /// <summary>
    /// The local working copies of documents opened from ThixDrive (one folder per account and item, the document and its sidecar
    /// drive.json): a document saved while the cloud cannot be reached waits here until it is uploaded.
    /// </summary>
    public static string DriveDirectory => Path.Combine(DataDirectory, "drive");

    public static string UpdateDirectory => Path.Combine(DataDirectory, "updates");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>True when a developer or a test pointed the app to its own folder: it is then its own instance.</summary>
    public static bool HasOverride => _dataDirectoryForTests is not null || Override() is not null;

    /// <summary>The documents folder of the user (the start of "Speichern unter" unless the settings name another).</summary>
    public static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } documents
        ? documents
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Tests use a temporary folder (null: back to the real one).</summary>
    internal static void UseDirectoryForTests(string? directory) => _dataDirectoryForTests = directory;

    private static string? Override() =>
        AppInfo.IsInitialized && Environment.GetEnvironmentVariable(AppInfo.Definition.DataDirectoryVariable) is { Length: > 0 } folder && !string.IsNullOrWhiteSpace(folder)
            ? folder
            : null;

    private static string UserFolder() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}

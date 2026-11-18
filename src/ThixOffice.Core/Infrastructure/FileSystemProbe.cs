namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// The two questions the lists ask the file system (is the file / the folder still there?) in one place, so that the
/// snapshots can show a typical installation (C:\Users\Anna\Dokumente …) instead of the paths of the machine that draws them.
/// </summary>
internal static class FileSystemProbe
{
    private static Func<string, bool>? _fileExistsForSnapshots;
    private static Func<string, bool>? _folderExistsForSnapshots;
    private static string? _documentsFolderForSnapshots;

    public static bool FileExists(string path) => _fileExistsForSnapshots?.Invoke(path) ?? File.Exists(path);

    public static bool FolderExists(string path) => _folderExistsForSnapshots?.Invoke(path) ?? Directory.Exists(path);

    /// <summary>The documents folder of the user, or the one a snapshot shows.</summary>
    public static string DocumentsFolder => _documentsFolderForSnapshots ?? AppPaths.DocumentsFolder;

    /// <summary>Snapshots answer for themselves (null: the real file system again).</summary>
    public static void UseForSnapshots(Func<string, bool>? fileExists, Func<string, bool>? folderExists, string? documentsFolder)
    {
        _fileExistsForSnapshots = fileExists;
        _folderExistsForSnapshots = folderExists;
        _documentsFolderForSnapshots = documentsFolder;
    }
}

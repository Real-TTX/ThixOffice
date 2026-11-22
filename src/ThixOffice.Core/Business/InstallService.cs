using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// Windows only (macOS: the app bundle, <see cref="MacAppBundle"/>). Every app of the suite installs itself – like ThixNote –
/// without a separate installer: the published ThixWrite.exe is the setup. Started from anywhere else it copies itself to
/// %LocalAppData%\Programs\&lt;App&gt; (per user, no administrator rights), writes the document icon, creates the Start menu
/// entry, the entry in "Installierte Apps" (<see cref="UninstallEntry"/>, what ThixLauncher reads), the document types
/// (<see cref="FileAssociations"/>: "Öffnen mit") and – as the install options say (<see cref="InstallerOptions"/>, SETUP-08) –
/// a desktop shortcut, automatic updates and the types the app opens by default. "--update" replaces the program silently,
/// "--uninstall" removes every one of them, "--quiet" asks nothing.
/// Documents are never touched; the data folder (settings, recent list, logs) only when the user asks for it.
/// The registry goes through <see cref="UserRegistry"/>, so the tests run the whole install in memory.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallService
{
    public const string UpdateArgument = InstallerOptions.UpdateArgument;
    public const string RestartArgument = InstallerOptions.RestartArgument;
    public const string UninstallArgument = InstallerOptions.UninstallArgument;
    public const string QuietArgument = InstallerOptions.QuietArgument;

    private static TestLocations? _testLocations;

    /// <summary>A running instance listens for this signal, keeps its unsaved changes as recovery files and ends.</summary>
    public static string ExitSignalName => AppInfo.ProductName + ".Exit";

    public static string ProgramFolder => _testLocations?.ProgramFolder ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.ProductName);

    public static string InstalledExecutable => Path.Combine(ProgramFolder, AppInfo.Definition.ExecutableName);

    public static string DocumentIcon => Path.Combine(ProgramFolder, AppInfo.Definition.DocumentIconFileName);

    public static string StartMenuShortcut => Path.Combine(
        _testLocations?.ShortcutFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppInfo.ProductName}.lnk");

    public static string DesktopShortcut => Path.Combine(
        _testLocations?.DesktopFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppInfo.ProductName}.lnk");

    /// <summary>Installed means: the entry in "Installierte Apps" and the program file exist.</summary>
    public static bool IsInstalled => UninstallEntry.Exists(UserRegistry.Current, AppInfo.Definition) && File.Exists(InstalledExecutable);

    public static string? InstalledVersion => UninstallEntry.InstalledVersion(UserRegistry.Current, AppInfo.Definition);

    public static bool HasDesktopShortcut => File.Exists(DesktopShortcut);

    public static bool IsInstalledExecutable(string path) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(InstalledExecutable), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Installs or updates with the install options (SETUP-08): what <paramref name="choices"/> leaves open (null, or a value of
    /// null) an installed app keeps as it is and a first install takes from the app's installer.json.
    /// </summary>
    public static void Install(string sourceExecutable, InstallChoices? choices, Action<string> log)
    {
        var name = AppInfo.ProductName;
        var isUpdate = IsInstalled;
        var wanted = (choices ?? new InstallChoices()).Over(isUpdate ? InstallerOptions.Current() : InstallerOptions.Defaults);
        log(isUpdate ? $"Aktualisiere {name} in {ProgramFolder}" : $"Installiere {name} nach {ProgramFolder}");
        StopRunningInstances(log);

        Directory.CreateDirectory(ProgramFolder);
        if (!IsInstalledExecutable(sourceExecutable))
        {
            CopyWithRetry(sourceExecutable, InstalledExecutable);
            log("Programmdatei kopiert");
        }

        WriteDocumentIcon();

        CreateShortcut(StartMenuShortcut);
        log("Startmenü-Eintrag angelegt");
        if (AppInfo.Definition.ShowsNotifications)
        {
            RegisterUrlScheme(UserRegistry.Current, InstalledExecutable);
        }

        // Also an update applies them again: the file types point at the installed file and its document icon.
        InstallerOptions.Apply(wanted, InstalledExecutable, log);

        // The autostart is no install option of the Office apps (Einstellungen › Allgemein): an update keeps it as the user
        // left it, but points it at the installed file.
        if (InstallerOptions.Config.Autostart is null && isUpdate && AutostartService.IsEnabled())
        {
            AutostartService.WriteEntry(InstalledExecutable);
        }

        UninstallEntry.Write(UserRegistry.Current, AppInfo.Definition, InstalledExecutable, ProgramFolder, AppInfo.Version, new FileInfo(InstalledExecutable).Length);
        log("Eintrag unter „Installierte Apps“ geschrieben");
        log(isUpdate ? "Update abgeschlossen" : "Installation abgeschlossen");
    }

    /// <summary>
    /// Removes program, shortcuts, file types, autostart and the entry in "Installierte Apps"; the data folder only on
    /// request, documents never. Returns true when the program folder could not be deleted yet because this process runs
    /// from it – then call <see cref="ScheduleProgramFolderRemoval"/> right before the process ends.
    /// </summary>
    public static bool Uninstall(bool deleteUserData, Action<string> log)
    {
        log($"Entferne {AppInfo.ProductName}");
        StopRunningInstances(log);

        AutostartService.RemoveEntry();
        if (AppInfo.Definition.ShowsNotifications)
        {
            UserRegistry.Current.DeleteTree(UrlSchemeKey);
        }

        DeleteFile(StartMenuShortcut);
        DeleteFile(DesktopShortcut);
        log("Verknüpfungen entfernt");
        FileAssociations.Unregister(UserRegistry.Current, AppInfo.Definition);
        log("Dateitypen abgemeldet");
        UninstallEntry.Remove(UserRegistry.Current, AppInfo.Definition);
        log("Eintrag unter „Installierte Apps“ entfernt");

        if (deleteUserData)
        {
            DeleteFolder(AppPaths.DataDirectory);
            log("Einstellungen, „Zuletzt verwendet“ und Protokolle gelöscht");
        }

        var runsFromProgramFolder = (Environment.ProcessPath ?? "").StartsWith(ProgramFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (runsFromProgramFolder)
        {
            log("Programmordner wird nach dem Schließen gelöscht");
            return true;
        }

        DeleteFolder(ProgramFolder);
        log("Programmordner gelöscht");
        return false;
    }

    /// <summary>The program folder cannot go while this exe runs from it: a short-lived cmd removes it afterwards.</summary>
    public static void ScheduleProgramFolderRemoval() =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{ProgramFolder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });

    /// <summary>Starts the installed app (after setup or update).</summary>
    public static void StartInstalled(string arguments = "") =>
        Process.Start(new ProcessStartInfo(InstalledExecutable, arguments) { UseShellExecute = false, WorkingDirectory = ProgramFolder });

    /// <summary>Redirects program folder and shortcuts for tests (the registry: <see cref="UserRegistry.UseForTests"/>); null = the real places.</summary>
    internal static void UseLocationsForTests(string? programFolder) =>
        _testLocations = programFolder is null
            ? null
            : new TestLocations(programFolder, Path.Combine(programFolder, "StartMenu"), Path.Combine(programFolder, "Desktop"));

    /// <summary>The document icon is embedded in the app (logical name "&lt;id&gt;-document.ico"); the file types point to it.</summary>
    private static bool WriteDocumentIcon()
    {
        using var icon = Assembly.GetEntryAssembly()?.GetManifestResourceStream($"{AppInfo.Definition.Id}-document.ico");
        if (icon is null)
        {
            return false;
        }

        using var file = File.Create(DocumentIcon);
        icon.CopyTo(file);
        return true;
    }

    /// <summary>Asks a running instance from the program folder to end (it keeps its unsaved changes), and ends it when it does not react in time.</summary>
    private static void StopRunningInstances(Action<string> log)
    {
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppInfo.Definition.ExecutableName))
            .Where(process => process.Id != Environment.ProcessId && RunsFrom(process, InstalledExecutable))
            .ToList();
        if (running.Count == 0)
        {
            return;
        }

        log($"Beende das laufende {AppInfo.ProductName} (ungespeicherte Änderungen bleiben zur Wiederherstellung erhalten)");
        if (_testLocations is null && EventWaitHandle.TryOpenExisting(ExitSignalName, out var exitSignal))
        {
            exitSignal.Set();
            exitSignal.Dispose();
        }

        foreach (var process in running)
        {
            try
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(20)))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool RunsFrom(Process process, string executable)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The old program may need a moment to release its file after it ended.</summary>
    private static void CopyWithRetry(string source, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    internal static string UrlSchemeKey => $@"Software\Classes\{AppInfo.Definition.UrlScheme}";

    /// <summary>
    /// The app's URL scheme (<c>thixmail:</c>) for activations from its notifications: a click on a toast starts the program with the link, the
    /// running instance takes it over (single instance) – the only way an unpackaged app learns about the click without a COM activator.
    /// </summary>
    internal static void RegisterUrlScheme(IUserRegistry registry, string executable)
    {
        registry.SetValue(UrlSchemeKey, "", $"URL:{AppInfo.Definition.Name}");
        registry.SetValue(UrlSchemeKey, "URL Protocol", "");
        registry.SetValue($@"{UrlSchemeKey}\DefaultIcon", "", $"\"{executable}\",0");
        registry.SetValue($@"{UrlSchemeKey}\shell\open\command", "", $"\"{executable}\" \"%1\"");
    }

    /// <summary>The desktop shortcut on or off (install options).</summary>
    internal static void SetDesktopShortcut(bool wanted)
    {
        if (wanted)
        {
            CreateShortcut(DesktopShortcut);
        }
        else
        {
            DeleteFile(DesktopShortcut);
        }
    }

    private static void CreateShortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Platform.Windows.ShellLink.Write(path, InstalledExecutable, ProgramFolder, AppInfo.Definition.Description, InstalledExecutable,
            AppInfo.Definition.ShowsNotifications ? AppInfo.Definition.AppUserModelId : null);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not delete {path}: {exception.Message}");
        }
    }

    private static void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not delete {path}: {exception.Message}");
        }
    }

    private sealed record TestLocations(string ProgramFolder, string ShortcutFolder, string DesktopFolder);
}

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>The choices of the setup assistant; an update (null) keeps what the user has.</summary>
public sealed record InstallOptions(bool DesktopShortcut, bool InstallUpdatesAutomatically = true, bool MakeDefaultForFreeTypes = false);

/// <summary>
/// Windows only (macOS: the app bundle, <see cref="MacAppBundle"/>). Every app of the suite installs itself – like ThixNote –
/// without a separate installer: the published ThixWrite.exe is the setup. Started from anywhere else it copies itself to
/// %LocalAppData%\Programs\&lt;App&gt; (per user, no administrator rights), writes the document icon, creates the Start menu
/// entry, the entry in "Installierte Apps" (<see cref="UninstallEntry"/>, what ThixLauncher reads), the document types
/// (<see cref="FileAssociations"/>: "Öffnen mit", never taking a type from another program) and optionally a desktop
/// shortcut. "--update" replaces the program silently, "--uninstall" removes every one of them, "--quiet" asks nothing.
/// Documents are never touched; the data folder (settings, recent list, logs) only when the user asks for it.
/// The registry goes through <see cref="UserRegistry"/>, so the tests run the whole install in memory.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallService
{
    public const string UpdateArgument = UpdateService.WindowsUpdateArgument;
    public const string RestartArgument = UpdateService.RestartArgument;
    public const string UninstallArgument = UninstallEntry.UninstallArgument;
    public const string QuietArgument = UninstallEntry.QuietArgument;

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

    /// <summary>Installs or updates. <paramref name="options"/> null = update: shortcuts, file types and autostart stay as they are.</summary>
    public static void Install(string sourceExecutable, InstallOptions? options, Action<string> log)
    {
        var name = AppInfo.ProductName;
        var isUpdate = options is null;
        log(isUpdate ? $"Aktualisiere {name} in {ProgramFolder}" : $"Installiere {name} nach {ProgramFolder}");
        StopRunningInstances(log);

        Directory.CreateDirectory(ProgramFolder);
        if (!IsInstalledExecutable(sourceExecutable))
        {
            CopyWithRetry(sourceExecutable, InstalledExecutable);
            log("Programmdatei kopiert");
        }

        var documentIcon = WriteDocumentIcon() ? DocumentIcon : null;

        CreateShortcut(StartMenuShortcut);
        log("Startmenü-Eintrag angelegt");

        if (options?.DesktopShortcut ?? HasDesktopShortcut)
        {
            CreateShortcut(DesktopShortcut);
            log("Desktop-Verknüpfung angelegt");
        }

        if (AccessPolicy.AreFileAssociationsDisabled)
        {
            log("Dateitypen: von einem Administrator deaktiviert");
        }
        else
        {
            // An update keeps a default the user gave the app; it never takes a new one.
            FileAssociations.Register(UserRegistry.Current, AppInfo.Definition, InstalledExecutable, documentIcon, options?.MakeDefaultForFreeTypes ?? false);
            log($"{name} unter „Öffnen mit“ eingetragen ({string.Join(", ", AppInfo.Definition.SupportedTypes.Select(type => type.Extension))})");
        }

        // An update keeps the autostart as the user left it, but points it at the installed file.
        if (isUpdate && AutostartService.IsEnabled())
        {
            AutostartService.WriteEntry(InstalledExecutable);
        }

        if (options is not null)
        {
            ApplyUpdateChoice(options.InstallUpdatesAutomatically, log);
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

    /// <summary>"Updates automatisch installieren" of the setup; a locked configuration (policy) keeps its value.</summary>
    private static void ApplyUpdateChoice(bool installAutomatically, Action<string> log)
    {
        try
        {
            SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = installAutomatically });
            log(installAutomatically ? "Updates werden beim Beenden automatisch installiert" : "Updates werden nur angeboten");
        }
        catch (BusinessException exception)
        {
            log(exception.Message);
        }
    }

    private static void CreateShortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Platform.Windows.ShellLink.Write(path, InstalledExecutable, ProgramFolder, AppInfo.Definition.Description, InstalledExecutable);
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

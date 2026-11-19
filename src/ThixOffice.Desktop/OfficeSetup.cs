using System.Runtime.Versioning;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop;

/// <summary>
/// The Windows setup of every app in MatDesktop's setup window (SETUP-01, SETUP-02): one window for install, update and
/// uninstall, 600 × 560, the footer with "Version x · channel" (SETUP-07). Install: Welcome (per user, no administrator
/// rights, where to: SETUP-03) → Options (what the app's installer.json offers, SETUP-08: desktop shortcut, automatic
/// updates, a switch per document type – the same questions ThixLauncher asks; no autostart: an editor does not start with
/// the computer) → Progress → Finish. Update: Welcome with the installed and the new version → Progress → Finish. Uninstall:
/// what goes and "Auch meine Einstellungen löschen" (off; documents are never deleted) → Progress → Finish (SETUP-04).
/// The work (InstallService) runs on a background task and reports its steps.
/// </summary>
[SupportedOSPlatform("windows")]
public static class OfficeSetup
{
    /// <summary>Shows the assistant; <paramref name="finished"/> gets true when the program folder has to be removed after this process ended.</summary>
    public static void Run(OfficeApplication app, bool isUninstall, Action<bool> finished)
    {
        var removeProgramFolder = false;
        var isUpdate = !isUninstall && InstallService.IsInstalled;
        var source = app.ExecutablePath;
        MatSetupWindow? window = null;
        window = Create(app, isUninstall, isUpdate, InstallService.InstalledVersion, InstallService.ProgramFolder, async run =>
        {
            void Report(string step) => run.Progress.Report(new MatSetupReport(step, MatSetupStepState.Done));
            try
            {
                if (isUninstall)
                {
                    removeProgramFolder = await Task.Run(() => InstallService.Uninstall(run.DeleteData, Report));
                    window!.Steps[^1].Text = UninstalledText(run.DeleteData);
                }
                else
                {
                    var choices = isUpdate ? null : Choices(run.Options);
                    await Task.Run(() => InstallService.Install(source, choices, Report));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
            {
                Log.Error(isUninstall ? "Uninstall failed" : "Setup failed", exception);
                throw new InvalidOperationException($"{exception.Message}\n\nDetails stehen im Protokoll ({AppPaths.LogDirectory}).", exception);
            }
        });

        window.Finished += (_, _) =>
        {
            if (window.StartNow)
            {
                InstallService.StartInstalled();
            }

            finished(removeProgramFolder);
        };
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// The window for install (or update) or uninstall with what it shows decided once: the mode, the installed version and
    /// the program folder. <paramref name="work"/> is the work of the primary button; the self test and the snapshots pass
    /// work that touches nothing.
    /// </summary>
    public static MatSetupWindow Create(OfficeApplication app, bool isUninstall, bool isUpdate, string? installedVersion, string programFolder, Func<MatSetupRun, Task> work)
    {
        var definition = app.Definition;
        var mode = isUninstall ? MatSetupMode.Uninstall : isUpdate ? MatSetupMode.Update : MatSetupMode.Setup;
        var types = string.Join(", ", definition.SupportedTypes.Select(type => type.Extension));
        IReadOnlyList<MatSetupStep> steps = mode switch
        {
            MatSetupMode.Uninstall =>
            [
                MatSetupStep.Uninstall("Programm, Startmenü-Eintrag, Desktop-Verknüpfung und Dateitypen werden entfernt. Ihre Dokumente bleiben in jedem Fall erhalten."),
            ],
            MatSetupMode.Update =>
            [
                MatSetupStep.Welcome(string.Empty, Note($"Ein laufendes {definition.Name} wird beendet; ungespeicherte Änderungen werden beim nächsten Start wiederhergestellt. Ihre Dokumente und Einstellungen bleiben erhalten.")),
            ],
            _ =>
            [
                // SETUP-03: for the user, without administrator rights, and where to. The folder on a line of its own.
                MatSetupStep.Welcome(
                    $"{definition.Name}: {definition.Description}",
                    Note($"Wird für Ihr Benutzerkonto installiert – ohne Administratorrechte – nach\n{programFolder}")),
                MatSetupStep.WithOptions(null, [.. Options(definition)]),
            ],
        };

        var finish = isUninstall
            ? MatSetupStep.Finish(UninstalledText(deleteData: false), offersStart: false)
            : MatSetupStep.Finish($"{definition.Name} {AppInfo.Version} ist installiert. Sie finden es im Startmenü; unter „Öffnen mit“ steht es für {types} bereit.");
        var version = isUninstall ? installedVersion ?? AppInfo.Version : AppInfo.Version;
        return new MatSetupWindow(new MatSetup(definition.Name, version, AppInfo.Channel, mode, steps, work)
        {
            Logo = app.Logo,
            WordMark = Ui.WordMark(definition.Suffix, MatTokens.Type.Fs2xl),
            InstalledVersion = installedVersion ?? "eine ältere Version",
            Finish = finish,
            DeleteDataText = "Auch meine Einstellungen löschen",
            DeleteDataDescription = "Einstellungen, „Zuletzt verwendet“ und Protokolle auf diesem Computer. Ihre Dokumente bleiben in jedem Fall erhalten.",
        });
    }

    /// <summary>
    /// The switches of the Options step from the install options (installer.json, SETUP-08), each as its default: the same
    /// questions ThixLauncher asks before it installs quietly. A document type is "type:.docx".
    /// </summary>
    internal static IEnumerable<MatSetupOption> Options(OfficeAppDefinition definition)
    {
        var config = InstallerOptions.Config;
        if (config.DesktopShortcut is { } shortcut)
        {
            yield return MatSetupOption.DesktopShortcut("Ein Startmenü-Eintrag wird immer angelegt") with { IsOn = shortcut.Default };
        }

        if (config.AutomaticUpdates is { } updates)
        {
            yield return MatSetupOption.AutomaticUpdates($"Neue Versionen werden installiert, wenn Sie {definition.Name} schließen") with { IsOn = updates.Default };
        }

        if (config.Autostart is { } autostart)
        {
            yield return MatSetupOption.Autostart() with { IsOn = autostart.Default };
        }

        foreach (var type in config.FileTypes)
        {
            yield return new MatSetupOption(FileTypeKey + type.Extension, $"{type.Extension}-Dateien mit {definition.Name} öffnen", type.Name, type.Default);
        }
    }

    /// <summary>The answers of the Options step as install choices.</summary>
    internal static InstallChoices Choices(IReadOnlyDictionary<string, bool> options) => new(
        options.TryGetValue("shortcut", out var shortcut) ? shortcut : null,
        options.TryGetValue("autostart", out var autostart) ? autostart : null,
        options.TryGetValue("updates", out var updates) ? updates : null,
        InstallerOptions.Config.FileTypes.Count == 0
            ? null
            : InstallerOptions.Config.FileTypes.Where(type => options.GetValueOrDefault(FileTypeKey + type.Extension)).Select(type => type.Extension).ToList());

    private const string FileTypeKey = "type:";

    /// <summary>A note under the text of a step, in the tint of information.</summary>
    private static InfoBar Note(string text)
    {
        var note = new InfoBar { Margin = new Avalonia.Thickness(0) };
        note.Show(text, InfoSeverity.Info);
        return note;
    }

    private static string UninstalledText(bool deleteData) =>
        deleteData ? "Programm und Einstellungen wurden entfernt. Ihre Dokumente sind noch da." : "Das Programm wurde entfernt. Ihre Dokumente und Einstellungen bleiben erhalten.";
}

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Avalonia.Threading;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop;

/// <summary>
/// "&lt;App&gt;.exe --self-test &lt;report file&gt;": the published program checks itself – everything trimming or single-file
/// packaging could break silently: the JSON of the settings, of "Zuletzt verwendet" and of the update manifest, saving and
/// opening a document (atomic save, recovery file), the protection of secrets (DPAPI), the embedded document icon, Windows
/// shortcuts (COM), the MatCloud contract, and the document window with every backstage page and the setup window. It runs
/// in a fresh temporary data folder (OfficeProgram sets it), never touches the user's documents, writes a report and ends
/// with exit code 0 (all passed) or 1. CI runs it on every Windows build.
/// </summary>
public static class OfficeSelfTest
{
    public const string Argument = "--self-test";

    public static async Task<int> RunAsync(OfficeApplication app, string reportPath)
    {
        app.KeepsRunningWithoutWindows = true;
        var definition = app.Definition;
        var report = new StringBuilder($"{definition.Name} {AppInfo.Version} self test, data folder {AppPaths.DataDirectory}{Environment.NewLine}");
        var failures = 0;

        async Task Check(string name, Func<Task<string>> check)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var note = await check();
                report.AppendLine($"ok    {name} ({watch.ElapsedMilliseconds} ms){(note.Length > 0 ? " – " + note : "")}");
            }
            catch (Exception exception)
            {
                failures++;
                report.AppendLine($"FAIL  {name}: {exception}");
            }
        }

        await Check("settings (JSON)", () =>
        {
            SettingsService.Update(settings => settings with { Theme = ThemeChoice.Dark, KeepBackupCopy = true, RecentListSize = 10 });
            SettingsService.ResetCache();
            var read = SettingsService.Get();
            Ensure(read is { Theme: ThemeChoice.Dark, KeepBackupCopy: true, RecentListSize: 10 }, "settings did not survive a save");
            SettingsService.Update(_ => new AppSettings());
            return Task.FromResult("");
        });

        var folder = Path.Combine(AppPaths.DataDirectory, "Dokumente");
        Directory.CreateDirectory(folder);
        await Check("documents (atomic save, open, recent list, recovery)", () =>
        {
            var document = app.CreateDocument(DocumentService.NextUntitledName());
            var path = DocumentService.SaveAs(document, Path.Combine(folder, "Selbsttest"));
            var reopened = DocumentService.Open(path, app.CreateDocument);
            Ensure(reopened.FilePath == path && !reopened.IsDirty, "the saved document did not open again");
            DocumentService.SaveAs(reopened, path);
            RecentDocuments.ResetCache();
            Ensure(RecentDocuments.List().Any(item => item.Document.Path == path && item.Exists), "recent list not written or read");
            DocumentFiles.WriteRecovery(new RecoveryInfo(Guid.NewGuid(), "Selbsttest", null, definition.SaveType.Extension, DateTimeOffset.Now), stream => stream.WriteByte(42));
            Ensure(DocumentService.RecoveredDocuments().Count == 1, "recovery file not written or read");
            foreach (var info in DocumentService.RecoveredDocuments())
            {
                DocumentService.DiscardRecovery(info);
            }

            return Task.FromResult(Path.GetExtension(path));
        });

        await Check("update manifest (JSON)", async () =>
        {
            var package = $$"""{"file":"{{definition.SetupFileName}}","sha256":"AB"}""";
            var json = $$"""{"version":"0.1.9-20261118","channel":"release","buildNumber":9,"setup":"{{definition.SetupFileName}}","sha256":"AB","publishedAt":"2026-11-18T12:00:00Z","packages":{"win-x64":{{package}} } }""";
            var manifest = await new StringContent(json, Encoding.UTF8, "application/json").ReadFromJsonAsync<UpdateManifest>();
            Ensure(manifest is { BuildNumber: 9 } && UpdateService.PackageFor(manifest, "win-x64")?.File == definition.SetupFileName, "manifest not read");
            return UpdateService.ManifestFileName;
        });

        await Check("cloud contract (JSON)", () =>
        {
            const string json = """{"items":[{"collection":"documents","key":"k1","data":"{}","updatedAt":"2026-11-18T12:00:00Z","revision":3}],"latest":3,"hasMore":false}""";
            var changes = System.Text.Json.JsonSerializer.Deserialize<MatCloud.Contract.SyncChangeSet>(json, MatCloud.Contract.CloudContract.Json);
            Ensure(changes is { Latest: 3, Items.Count: 1 }, "change set not read");
            return Task.FromResult("");
        });

        if (OperatingSystem.IsWindows())
        {
            await Check("token protection (DPAPI), shortcut (COM), document icon", () =>
            {
                Ensure(TokenStore.Read("self-test", TokenStore.Save("self-test", "token-123")) == "token-123", "the token was not protected and read back");
                var link = Path.Combine(AppPaths.DataDirectory, $"{definition.Name}.lnk");
                Core.Platform.Windows.ShellLink.Write(link, app.ExecutablePath, AppPaths.DataDirectory, "Selbsttest", app.ExecutablePath);
                Ensure(File.Exists(link) && new FileInfo(link).Length > 0, "no .lnk written");
                using var icon = app.GetType().Assembly.GetManifestResourceStream($"{definition.Id}-document.ico");
                Ensure(icon is { Length: > 0 }, "the document icon is not embedded");
                return Task.FromResult("");
            });
        }

        await Check("document window, backstage, search", async () =>
        {
            var window = app.NewDocumentWindow();
            await Settle();
            Ensure(window.Commands.Search("speichern").Any(command => command.Id == Core.Commands.CommonCommandIds.Save), "the command search does not find Speichern");
            foreach (var page in new[] { OfficeWindow.NewPageId, OfficeWindow.OpenPageId, OfficeWindow.SaveAsPageId, OfficeWindow.SettingsPageId, OfficeWindow.AboutPageId })
            {
                window.Backstage.Open(page);
                await Settle();
                Ensure(window.Backstage.CurrentPageId == page && window.Backstage.CurrentPage is not null, $"backstage page {page} not shown");
            }

            foreach (var tab in new[] { Pages.SettingsPage.SaveTab, Pages.SettingsPage.UpdatesTab, Pages.SettingsPage.AdvancedTab })
            {
                window.ShowSettings(tab);
                await Settle();
            }

            window.Backstage.Close();
            if (OperatingSystem.IsWindows())
            {
                var setup = OfficeSetup.Create(app, isUninstall: false, isUpdate: false, null, InstallService.ProgramFolder, _ => Task.CompletedTask);
                setup.Show();
                await Settle();
                setup.Close();
            }

            ProblemReports.WriteHang(TimeSpan.FromSeconds(9), withDump: false);
            Dialogs.ProblemReportDialog.ShowUnseen()?.Close();
            window.Close();
            return "";
        });

        report.AppendLine(failures == 0 ? "PASSED" : $"FAILED ({failures})");
        await File.WriteAllTextAsync(reportPath, report.ToString(), Encoding.UTF8);

        // The temporary data folder goes again (unless the report was written into it).
        if (!Path.GetFullPath(reportPath).StartsWith(Path.GetFullPath(AppPaths.DataDirectory), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Delete(AppPaths.DataDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Lets the dispatcher and the layout run (the real platform, a real window).</summary>
    private static async Task Settle()
    {
        for (var round = 0; round < 5; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40);
        }
    }
}

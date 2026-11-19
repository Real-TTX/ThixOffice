using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop;

/// <summary>
/// "&lt;App&gt; --snapshot &lt;folder&gt;": draws the windows of an app into PNG files without a screen (Avalonia headless with
/// Skia, the real windows, theme and fonts of the machine), in light and dark – the pictures of the README and of the
/// reviews. It works in a temporary data folder (OfficeProgram sets it) with the app's sample document, a typical
/// installation (version 0.1.0 on the release channel, ANNA-PC, C:\Users\Anna\Dokumente) and a sample list of recent
/// documents: no real document, path, setting, registry value, network or update check shows up.
/// </summary>
public static class OfficeSnapshot
{
    public const string Argument = "--snapshot";

    public const string Version = "0.1.0";

    public const string Channel = "release";

    public const string MachineName = "ANNA-PC";

    private const string Documents = @"C:\Users\Anna\Dokumente";

    private static readonly string[] Themes = ["light", "dark"];

    private static readonly string[] WindowPictures =
    [
        "main", "main-ribbon-collapsed", "backstage-new", "backstage-open", "backstage-saveas", "backstage-settings",
        "backstage-settings-save", "backstage-settings-updates", "backstage-about", "dialog-save-changes",
    ];

    private static readonly string[] SetupPictures = ["setup-welcome", "setup-options", "setup-update", "setup-uninstall"];

    /// <summary>Every file a snapshot writes, by app id: thixwrite-main-light.png …</summary>
    public static IReadOnlyList<string> FileNames(string appId) => FileNames(appId, []);

    /// <summary>Every file a snapshot of <paramref name="app"/> writes, its own scenes (<see cref="OfficeApplication.SnapshotScenes"/>) included.</summary>
    public static IReadOnlyList<string> FileNames(OfficeApplication app) => FileNames(app.Definition.Id, app.SnapshotScenes);

    private static IReadOnlyList<string> FileNames(string appId, IEnumerable<string> scenes) =>
        [.. WindowPictures.Concat(scenes).Concat(SetupPictures).SelectMany(name => Themes.Select(theme => $"{appId}-{name}-{theme}.png"))];

    /// <summary>The command: sets Avalonia up headless, draws every picture into the folder, ends with 0 (all written) or 1.</summary>
    public static int Run<TApp>(string[] args)
        where TApp : OfficeApplication, new()
    {
        var index = Array.IndexOf(args, Argument);
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Usage: {AppInfo.ProductName} {Argument} <folder>");
            return 2;
        }

        var folder = Path.GetFullPath(args[index + 1]);
        AppBuilder.Configure<TApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        AvaloniaSynchronizationContext.InstallIfNeeded();

        int exitCode;
        try
        {
            var report = Render(OfficeApplication.Instance, folder);
            foreach (var file in report.Written)
            {
                Console.WriteLine($"written  {Path.Combine(folder, file)}");
            }

            foreach (var (file, reason) in report.Skipped)
            {
                Console.WriteLine($"skipped  {file}: {reason}");
            }

            exitCode = report.Written.Count + report.Skipped.Count == FileNames(OfficeApplication.Instance).Count ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Snapshot failed: {exception}");
            exitCode = 1;
        }

        try
        {
            Directory.Delete(AppPaths.DataDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return exitCode;
    }

    /// <summary>
    /// Draws every picture into <paramref name="folder"/> (Avalonia must run headless and the data folder must be a fresh one:
    /// the command and the UI tests take care of both). Existing files of the same names are replaced.
    /// </summary>
    public static SnapshotReport Render(OfficeApplication app, string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new SnapshotReport();
        var id = app.Definition.Id;
        var previousTheme = Application.Current!.RequestedThemeVariant;
        using (Isolate(app))
        {
            foreach (var theme in Themes)
            {
                Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                RenderWindow(app, folder, theme, report);
                RenderSetup(app, folder, theme, report);
            }
        }

        Application.Current.RequestedThemeVariant = previousTheme;
        return report;

        void RenderWindow(OfficeApplication application, string target, string theme, SnapshotReport written)
        {
            var window = application.ShowWindow(application.CreateSampleDocument(Path.Combine(Documents, "Projektbericht" + application.Definition.SaveType.Extension)));
            window.Width = 1180;
            window.Height = 760;
            Settle(window, waitLonger: true);
            Save(window, target, $"{id}-main-{theme}.png", written);

            window.Ribbon.IsCollapsed = true;
            Settle(window);
            Save(window, target, $"{id}-main-ribbon-collapsed-{theme}.png", written);
            window.Ribbon.IsCollapsed = false;
            Settle(window);

            foreach (var scene in application.SnapshotScenes)
            {
                var shown = application.ShowSnapshotScene(window, scene);
                Settle(shown, waitLonger: true);
                Save(shown, target, $"{id}-{scene}-{theme}.png", written);
                application.EndSnapshotScene(window, scene, shown);
                Settle(window);
            }

            foreach (var (page, name) in new[] { (OfficeWindow.NewPageId, "new"), (OfficeWindow.OpenPageId, "open"), (OfficeWindow.SaveAsPageId, "saveas") })
            {
                window.Backstage.Open(page);
                if (window.Backstage.CurrentPage is OpenPage open && open.Rows.Count > 1)
                {
                    open.Select(open.Rows[1].Path);
                }

                Settle(window);
                Save(window, target, $"{id}-backstage-{name}-{theme}.png", written);
            }

            foreach (var (tab, name) in new[] { (SettingsPage.GeneralTab, "settings"), (SettingsPage.SaveTab, "settings-save"), (SettingsPage.UpdatesTab, "settings-updates") })
            {
                window.ShowSettings(tab);
                Settle(window);
                Save(window, target, $"{id}-backstage-{name}-{theme}.png", written);
            }

            window.Backstage.Open(OfficeWindow.AboutPageId);
            (window.Backstage.CurrentPage as AboutPage)?.ShowMachine($"Microsoft Windows 10.0.26100 (win-x64), .NET 10.0.0", $@"C:\Users\Anna\AppData\Local\{application.Definition.Name}");
            Settle(window);
            Save(window, target, $"{id}-backstage-about-{theme}.png", written);
            window.Backstage.Close();

            _ = SaveChangesDialog.AskAsync(window, window.Document.Title);
            Settle(window);
            var dialog = window.OwnedWindows.OfType<QuestionDialog>().Single();
            Settle(dialog);
            Save(dialog, target, $"{id}-dialog-save-changes-{theme}.png", written);
            dialog.Close();
            Settle(window);

            window.CloseKeepingRecovery();
            DocumentService.DiscardRecovery(window.Document);
            Settle(window);
        }
    }

    private static void RenderSetup(OfficeApplication app, string folder, string theme, SnapshotReport report)
    {
        foreach (var name in SetupPictures)
        {
            var file = $"{app.Definition.Id}-{name}-{theme}.png";
            if (!OperatingSystem.IsWindows())
            {
                report.Skipped.Add((file, "the setup assistant exists on Windows only (macOS: the app bundle and the dmg)"));
                continue;
            }

            var programFolder = $@"C:\Users\Anna\AppData\Local\Programs\{app.Definition.Name}";
            var window = OfficeSetup.Create(app, name == "setup-uninstall", name == "setup-update", name == "setup-update" ? "0.1.4-20261112" : Version, programFolder, _ => Task.CompletedTask);
            window.Show();
            Settle(window);
            if (name == "setup-options")
            {
                window.GoTo(MatSetupStepKind.Options);
                Settle(window);
            }

            Save(window, folder, file, report);
            window.Close();
            Settle(window);
        }
    }

    /// <summary>Lets the dispatcher, the layout and the render timer run.</summary>
    internal static void Settle(TopLevel topLevel, bool waitLonger = false)
    {
        for (var round = 0; round < (waitLonger ? 30 : 8); round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(waitLonger ? 30 : 15);
        }

        Dispatcher.UIThread.RunJobs();
        topLevel.UpdateLayout();
    }

    private static void Save(TopLevel topLevel, string folder, string name, SnapshotReport report)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = topLevel.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name}: no frame was rendered");
        frame.Save(Path.Combine(folder, name));
        report.Written.Add(name);
    }

    /// <summary>Pins version and computer, a typical documents folder and a sample "Zuletzt verwendet"; keeps the policies out.</summary>
    private static IDisposable Isolate(OfficeApplication app)
    {
        var extension = app.Definition.SaveType.Extension;
        AppInfo.Pin(Version, Channel, buildNumber: 0, MachineName);
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        PrintService.System = new Core.Platform.SamplePrinterSystem(); // no printer of this machine in a picture
        var missing = Path.Combine(Documents, "Archiv", "Entwurf alt" + extension);
        FileSystemProbe.UseForSnapshots(path => path != missing, folder => folder.StartsWith(Documents, StringComparison.OrdinalIgnoreCase), Documents);
        var now = DateTimeOffset.Now;
        RecentDocuments.Add(missing, now.AddDays(-40));
        RecentDocuments.Add(Path.Combine(Documents, "Rechnungen", "Mahnung Kunde 0815" + extension), now.AddDays(-9));
        RecentDocuments.Add(Path.Combine(Documents, "Vereinsfest Einladung" + extension), now.AddDays(-2));
        RecentDocuments.Add(Path.Combine(Documents, "Angebot Müller GmbH" + extension), now.AddDays(-1).AddHours(-3));
        RecentDocuments.Add(Path.Combine(Documents, "Projektbericht" + extension), now.AddMinutes(-12));
        RecentDocuments.Pin(Path.Combine(Documents, "Vereinsfest Einladung" + extension), pinned: true);
        app.KeepsRunningWithoutWindows = true;
        return new Restore(() =>
        {
            app.KeepsRunningWithoutWindows = false;
            FileSystemProbe.UseForSnapshots(null, null, null);
            AccessPolicy.UsePolicyReaderForTests(null);
            PrintService.System = null!;
            AppInfo.Pin(null);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>What a snapshot wrote (file names) and what it left out, with the reason.</summary>
public sealed class SnapshotReport
{
    public List<string> Written { get; } = [];

    public List<(string File, string Reason)> Skipped { get; } = [];
}

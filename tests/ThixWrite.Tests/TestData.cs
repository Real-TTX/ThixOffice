using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixWrite.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixWrite.Tests;

/// <summary>The real ThixWrite (App.axaml, MatDesktop's theme, the shell) rendered headless with Skia.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>The file dialogs answer what the test says (never a real dialog).</summary>
public sealed class FakeFileDialogs : IFileDialogs
{
    public Queue<string?> SaveTargets { get; } = new();

    public Queue<IReadOnlyList<string>> OpenAnswers { get; } = new();

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder) =>
        Task.FromResult(OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : (IReadOnlyList<string>)[]);

    public Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder) =>
        Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);

    public Task<string?> PickFolderAsync(Window owner, string startFolder) => Task.FromResult<string?>(startFolder);
}

/// <summary>
/// Every test: its own data folder (settings, recent list, recovery files), a documents folder, no policies, an in-memory
/// registry, the fake file dialogs; afterwards every window closes without asking. No real document, setting, registry
/// value or dialog is touched.
/// </summary>
public sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(ThixWriteApp.Definition, typeof(App).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixwrite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DocumentsFolder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        DocumentService.ResetUntitledCountForTests();
        OfficeApplication.FileDialogs = Dialogs;
        Ui.OpenExternalForTests = _ => { };
    }

    public string Folder { get; }

    public string DocumentsFolder => Path.Combine(Folder, "Dokumente");

    public FakeFileDialogs Dialogs { get; } = new();

    public static App App => (App)Application.Current!;

    public string File(string name, string content)
    {
        var path = Path.Combine(DocumentsFolder, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        foreach (var window in (Application.Current as App)?.DocumentWindows.ToList() ?? [])
        {
            window.CloseKeepingRecovery();
            DocumentService.DiscardRecovery(window.Document);
        }

        if (Application.Current is not null)
        {
            Settle();
        }

        OfficeApplication.FileDialogs = new SystemFileDialogs();
        Ui.OpenExternalForTests = null;
        AccessPolicy.UsePolicyReaderForTests(null);
        UserRegistry.UseForTests(null);
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        AppPaths.UseDirectoryForTests(null);
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Lets the dispatcher, the layout and the render timer run, including the continuations of async handlers.</summary>
    public static async Task SettleAsync()
    {
        for (var round = 0; round < 6; round++)
        {
            Settle();
            await Task.Yield();
        }
    }

    public static void Settle()
    {
        for (var round = 0; round < 6; round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
}

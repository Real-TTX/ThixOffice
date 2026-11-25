using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ThixShow.Core;
using ThixShow.Core.Model;
using ThixShow.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixShow.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixShow.Tests;

/// <summary>The real ThixShow (App.axaml, MatDesktop's theme, the shell) rendered headless with Skia.</summary>
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

    public Task<string?> PickPictureAsync(Window owner, string startFolder) => Task.FromResult<string?>(null);
}

/// <summary>
/// Every test: its own data folder, a documents folder, no policies, an in-memory registry, the fake file dialogs; afterwards
/// every window closes without asking. No real document, setting, registry value or dialog is touched.
/// </summary>
public sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(ThixShowApp.Definition, typeof(App).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixshow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DocumentsFolder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        PrintService.System = Printers;
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        DocumentService.ResetUntitledCountForTests();
        OfficeApplication.FileDialogs = Dialogs;
        Ui.OpenExternalForTests = _ => { };
        TokenStore.UseMemoryForTests(true);
        CloudService.ResetForTests();
        MatCloud.Contract.CloudHandoff.Folder = Path.Combine(Folder, "handoff");
    }

    public string Folder { get; }

    public string DocumentsFolder => Path.Combine(Folder, "Dokumente");

    public FakeFileDialogs Dialogs { get; } = new();

    internal ThixOffice.Core.Platform.SamplePrinterSystem Printers { get; } = new();

    public static App App => (App)Application.Current!;

    public void Dispose()
    {
        // A show a failed test left running, the screens and the idle time it gave.
        Show.SlideShowWindow.Current?.Controller.End();
        Show.ShowScreens.ForTests = null;
        Show.SlideShowWindow.IdleDelay = TimeSpan.FromSeconds(3);
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
        PrintService.System = null!;
        UserRegistry.UseForTests(null);
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        MatCloud.Contract.CloudHandoff.Folder = MatCloud.Contract.CloudHandoff.DefaultFolder();
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

    /// <summary>The command key: Strg, ⌘ on the Mac.</summary>
    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public static ShowEditor EditorOf(OfficeWindow window) => (ShowEditor)window.Editor;

    /// <summary>A new window with the keyboard on its slide.</summary>
    public static async Task<OfficeWindow> NewWindowAsync()
    {
        var window = App.NewDocumentWindow();
        window.Width = 1280;
        window.Height = 820;
        await SettleAsync();
        EditorOf(window).FocusDocument();
        await SettleAsync();
        return window;
    }

    /// <summary>Types like the keyboard does (text input into the focused control).</summary>
    public static async Task TypeAsync(OfficeWindow window, string text)
    {
        window.KeyTextInput(text);
        await SettleAsync();
    }

    /// <summary>Presses a key with modifiers and lets the window settle.</summary>
    public static async Task PressAsync(OfficeWindow window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        await SettleAsync();
    }

    /// <summary>A point of the slide (points) in the window's coordinates.</summary>
    public static Point SlidePoint(OfficeWindow window, double x, double y)
    {
        var canvas = EditorOf(window).Canvas;
        return canvas.TranslatePoint(canvas.ToView(x, y), window)!.Value;
    }

    /// <summary>A sample presentation in a new window (the sample deck), on its first slide.</summary>
    public static async Task<OfficeWindow> SampleWindowAsync()
    {
        var window = await NewWindowAsync();
        EditorOf(window).Document.Load(SampleDeck.Create());
        await SettleAsync();
        EditorOf(window).FocusDocument();
        await SettleAsync();
        return window;
    }

    public static async Task ClickAsync(OfficeWindow window, Point point, RawInputModifiers modifiers = RawInputModifiers.None, int clicks = 1)
    {
        for (var click = 0; click < clicks; click++)
        {
            window.MouseDown(point, MouseButton.Left, modifiers);
            window.MouseUp(point, MouseButton.Left, modifiers);
        }

        await SettleAsync();
    }

    public static async Task DragAsync(OfficeWindow window, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(from, modifiers);
        window.MouseDown(from, MouseButton.Left, modifiers);
        for (var step = 1; step <= 4; step++)
        {
            window.MouseMove(from + (to - from) * (step / 4.0), modifiers | RawInputModifiers.LeftMouseButton);
            await SettleAsync();
        }

        window.MouseUp(to, MouseButton.Left, modifiers);
        await SettleAsync();
    }

}

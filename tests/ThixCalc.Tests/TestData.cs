using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ThixCalc.Core;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixCalc.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixCalc.Tests;

/// <summary>The real ThixCalc (App.axaml, MatDesktop's theme, the shell) rendered headless with Skia.</summary>
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
        AppInfo.Initialize(ThixCalcApp.Definition, typeof(App).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixcalc-tests", Guid.NewGuid().ToString("N"));
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
    }

    public string Folder { get; }

    public string DocumentsFolder => Path.Combine(Folder, "Dokumente");

    public FakeFileDialogs Dialogs { get; } = new();

    internal ThixOffice.Core.Platform.SamplePrinterSystem Printers { get; } = new();

    public static App App => (App)Application.Current!;

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
        PrintService.System = null!;
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

    /// <summary>The command key: Strg, ⌘ on the Mac.</summary>
    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public static CalcEditor EditorOf(OfficeWindow window) => (CalcEditor)window.Editor;

    /// <summary>A new window with the keyboard in its grid.</summary>
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

    /// <summary>Types an entry and Enter (like a user filling a cell).</summary>
    public static async Task EnterAsync(OfficeWindow window, string text)
    {
        await TypeAsync(window, text);
        await PressAsync(window, PhysicalKey.Enter);
    }

    /// <summary>The middle of a cell in the window's coordinates.</summary>
    public static Point PointOf(OfficeWindow window, CellAddress cell)
    {
        var editor = EditorOf(window);
        var viewport = editor.Surface.Viewport();
        var rect = viewport.CellRect(cell);
        var scaling = editor.Surface.Scale / (editor.Zoom / 100);
        var local = new Point((rect.X + rect.Width / 2) / scaling, (rect.Y + rect.Height / 2) / scaling);
        return editor.Surface.TranslatePoint(local, window)!.Value;
    }

    /// <summary>A point of the grid surface (DIPs) in the window's coordinates.</summary>
    public static Point SurfacePoint(OfficeWindow window, double x, double y) =>
        EditorOf(window).Surface.TranslatePoint(new Point(x, y), window)!.Value;

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

    public static CellValue ValueAt(OfficeWindow window, string address) => EditorOf(window).Session.ActiveSheet[address];
}

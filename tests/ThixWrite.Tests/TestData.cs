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
using ThixWrite.Editor;

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

    /// <summary>The pictures "Bild einfügen" answers with, in order (null: cancelled).</summary>
    public Queue<string?> Pictures { get; } = new();

    public Task<string?> PickPictureAsync(Window owner, string startFolder) => Task.FromResult(Pictures.Count > 0 ? Pictures.Dequeue() : null);
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

    /// <summary>The printers the tests print to (never a real one).</summary>
    internal ThixOffice.Core.Platform.SamplePrinterSystem Printers { get; } = new();

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

    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    /// <summary>The key that moves by words (Strg on Windows, ⌥ on macOS).</summary>
    public static RawInputModifiers WordKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Control;

    /// <summary>The editor of a window.</summary>
    public static WriteEditor EditorOf(OfficeWindow window) => (WriteEditor)window.Editor;

    /// <summary>A new window with the keyboard in its document.</summary>
    public static async Task<OfficeWindow> NewWindowAsync()
    {
        var window = App.NewDocumentWindow();
        window.Width = 1180;
        window.Height = 820;
        await SettleAsync();
        EditorOf(window).FocusDocument();
        await SettleAsync();
        return window;
    }

    /// <summary>Types like the keyboard does (text input into the focused document); "\n" is Enter.</summary>
    public static async Task TypeAsync(OfficeWindow window, string text)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            }

            if (lines[index].Length > 0)
            {
                window.KeyTextInput(lines[index]);
            }
        }

        await SettleAsync();
    }

    /// <summary>Presses a key with modifiers and lets the window settle.</summary>
    public static async Task PressAsync(OfficeWindow window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        await SettleAsync();
    }

    /// <summary>Wiederholen by its key: Strg+Y, ⇧⌘Z on the Mac.</summary>
    public static Task RedoAsync(OfficeWindow window) => OperatingSystem.IsMacOS()
        ? PressAsync(window, PhysicalKey.Z, CommandKey | RawInputModifiers.Shift)
        : PressAsync(window, PhysicalKey.Y, CommandKey);

    /// <summary>The text of the document, paragraphs joined with "\n".</summary>
    public static string TextOf(OfficeWindow window) =>
        string.Join("\n", EditorOf(window).Document.Content.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().Select(paragraph => paragraph.Text));

    /// <summary>The point in the window of a caret position (its middle).</summary>
    public static Point PointOf(OfficeWindow window, ThixWrite.Core.Model.DocumentPosition position)
    {
        var editor = EditorOf(window);
        var caret = editor.Layout.GetCaret(new ThixWrite.Core.Editing.CaretPlace(position));
        var rect = editor.Surface.ToSurface(caret.Page, caret.X, caret.Top, 0, caret.Height);
        return editor.Surface.TranslatePoint(new Point(rect.X, rect.Y + (rect.Height / 2)), window)!.Value;
    }
}

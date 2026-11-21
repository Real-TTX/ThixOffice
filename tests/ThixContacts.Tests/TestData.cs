using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using MatCloud.Contract;
using ThixContacts.Core;
using ThixContacts.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixContacts.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixContacts.Tests;

/// <summary>The real ThixContacts (App.axaml, MatDesktop's theme, the shell) rendered headless with Skia.</summary>
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

    public Queue<string?> Files { get; } = new();

    public Queue<string?> Pictures { get; } = new();

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder) => Task.FromResult((IReadOnlyList<string>)[]);

    public Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder) =>
        Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);

    public Task<string?> PickExportTargetAsync(Window owner, string suggestedFileName, string startFolder, string typeDescription, string extension) =>
        Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);

    public Task<string?> PickFolderAsync(Window owner, string startFolder) => Task.FromResult<string?>(startFolder);

    public Task<string?> PickPictureAsync(Window owner, string startFolder) => Task.FromResult(Pictures.Count > 0 ? Pictures.Dequeue() : null);

    public Task<string?> PickFileAsync(Window owner, string title, string startFolder, string typeDescription, IReadOnlyList<string> extensions) =>
        Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);
}

/// <summary>
/// Every test: its own data folder (so its own store of contacts), no policies, an in-memory registry, secrets in memory, a ThixCloud in memory, the
/// fake file dialogs; afterwards the window closes. Nothing real is touched.
/// </summary>
public sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(ThixContactsApp.Definition, typeof(App).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixcontacts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        TokenStore.UseMemoryForTests(true);
        PrintService.System = new SamplePrinterSystem();
        SettingsService.ResetCache();
        CloudService.ResetForTests();
        Cloud = new SampleCloud { App = CloudApps.Contacts };
        CloudService.HandlerForTests = () => Cloud;
        OfficeApplication.FileDialogs = Dialogs;
        Ui.OpenExternalForTests = address => Opened.Add(address);
    }

    public string Folder { get; }

    public FakeFileDialogs Dialogs { get; } = new();

    internal SampleCloud Cloud { get; }

    public List<string> Opened { get; } = [];

    public static App App => (App)Application.Current!;

    /// <summary>A file in the test's folder.</summary>
    public string File(string name, string content)
    {
        var path = Path.Combine(Folder, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        foreach (var window in (Application.Current as App)?.DocumentWindows.ToList() ?? [])
        {
            window.CloseKeepingRecovery();
        }

        if (Application.Current is not null)
        {
            Settle();
        }

        OfficeApplication.FileDialogs = new SystemFileDialogs();
        Ui.OpenExternalForTests = null;
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        AccessPolicy.UsePolicyReaderForTests(null);
        PrintService.System = null!;
        UserRegistry.UseForTests(null);
        SettingsService.ResetCache();
        AppPaths.UseDirectoryForTests(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static async Task SettleAsync()
    {
        for (var round = 0; round < 6; round++)
        {
            Settle();
            await Task.Delay(5);
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

    public static ContactsEditor EditorOf(OfficeWindow window) => (ContactsEditor)window.Editor;

    /// <summary>The main window (with the sample contacts when asked), the keyboard in its list.</summary>
    public static async Task<OfficeWindow> WindowAsync(bool sample = true)
    {
        var window = App.NewDocumentWindow();
        window.Width = 1280;
        window.Height = 820;
        if (sample)
        {
            SampleContacts.Fill(EditorOf(window).Session);
        }

        await SettleAsync();
        EditorOf(window).FocusDocument();
        await SettleAsync();
        return window;
    }

    public static async Task PressAsync(TopLevel window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        await SettleAsync();
    }

    public static async Task TypeAsync(TopLevel window, string text)
    {
        window.KeyTextInput(text);
        await SettleAsync();
    }

    /// <summary>The dialog of a type the window shows now (waits a little for it).</summary>
    public static async Task<T> DialogAsync<T>(OfficeWindow window)
        where T : Window
    {
        for (var round = 0; round < 40; round++)
        {
            if (window.OwnedWindows.OfType<T>().FirstOrDefault() is { } dialog)
            {
                await SettleAsync();
                return dialog;
            }

            await SettleAsync();
        }

        throw new InvalidOperationException($"No {typeof(T).Name} was shown.");
    }

    /// <summary>Clicks a control (its middle) with the mouse.</summary>
    public static async Task ClickAsync(TopLevel window, Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        await SettleAsync();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ThixMail.Core;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixMail.Core.Transport;
using ThixMail.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixMail.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixMail.Tests;

/// <summary>The real ThixMail (App.axaml, MatDesktop's theme, the shell) rendered headless with Skia.</summary>
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

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder) => Task.FromResult((IReadOnlyList<string>)[]);

    public Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder) => Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);

    public Task<string?> PickExportTargetAsync(Window owner, string suggestedFileName, string startFolder, string typeDescription, string extension) =>
        Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);

    public Task<string?> PickFolderAsync(Window owner, string startFolder) => Task.FromResult<string?>(startFolder);

    public Task<string?> PickPictureAsync(Window owner, string startFolder) => Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);

    public Task<string?> PickFileAsync(Window owner, string title, string startFolder, string typeDescription, IReadOnlyList<string> extensions) =>
        Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);
}

/// <summary>Notifications recorded instead of shown.</summary>
public sealed class RecordingNotifier : IMailNotifier
{
    public List<(string Title, string Text, Action Click)> Shown { get; } = [];

    public List<string> Links { get; } = [];

    public void Show(MailNotice notice)
    {
        Shown.Add((notice.Title, notice.Text, notice.Open));
        Links.Add(notice.Link);
    }
}

/// <summary>
/// Every test: its own data folder (its own mail store), an in-memory registry, no policies, secrets in memory, a mail server in memory (the sample
/// mailbox), no web requests (auto-configuration, DNS and remote pictures answered here), the fake file dialogs and a recording notifier.
/// </summary>
public sealed class TestData : IDisposable
{
    public TestData(bool sampleServer = true)
    {
        AppInfo.Initialize(ThixMailApp.Definition, typeof(App).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixmail-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        ThixOffice.Core.Spelling.SpellingService.UseForTests(Spelling); // never the checker (and dictionary) of this machine
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        AccessPolicy.UseTextReaderForTests(_ => null);
        TokenStore.UseMemoryForTests(true);
        PrintService.System = new SamplePrinterSystem();
        SettingsService.ResetCache();
        CloudService.ResetForTests();
        Server = sampleServer ? SampleMailbox.CreateServer() : new SampleMailServer();
        MailDocument.Transport = Server;
        MailDocument.ConfigureSyncForTests = sync =>
        {
            sync.Backoff = TimeSpan.FromMilliseconds(50);
            sync.MaximumBackoff = TimeSpan.FromMilliseconds(200);
            sync.IdleTime = TimeSpan.FromSeconds(20);
        };
        AutoConfig.FetchForTests = _ => Task.FromResult<string?>(null);
        DnsLookup.ResolverForTests = (_, _) => [];
        RemotePictures.FetchForTests = (_, _) => Task.FromResult<byte[]?>(Png);
        OfficeApplication.FileDialogs = Dialogs;
        MailNotifications.Notifier = Notifier;
        MailReminders.CalendarRunsForTests = () => true; // no reminder window unless a test asks for it
        Ui.OpenExternalForTests = address => Opened.Add(address);
    }

    /// <summary>The spelling check of the tests: German knows every word but "Fehlr" and "Tipfehler"; English knows every word.</summary>
    public static ThixOffice.Core.Spelling.MemorySpellingProvider Spelling => new(
        new ThixOffice.Core.Spelling.MemorySpellChecker("de-DE", unknown: new Dictionary<string, string[]> { ["Fehlr"] = ["Fehler", "Fehl"], ["Tipfehler"] = ["Tippfehler"] }),
        new ThixOffice.Core.Spelling.MemorySpellChecker("en-US"));

    /// <summary>A small PNG (the remote pictures of the tests).</summary>
    public static byte[] Png { get; } = MakePng();

    private static byte[] MakePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(64, 40);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(new SkiaSharp.SKColor(0x88, 0x99, 0xAA));
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public string Folder { get; }

    public SampleMailServer Server { get; }

    public FakeFileDialogs Dialogs { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    public List<string> Opened { get; } = [];

    public static App App => (App)Application.Current!;

    public string File(string name, byte[] content)
    {
        var path = Path.Combine(Folder, name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        foreach (var window in (Application.Current as App)?.DocumentWindows.ToList() ?? [])
        {
            foreach (var owned in window.OwnedWindows.ToList())
            {
                if (owned is Compose.ComposeWindow compose)
                {
                    compose.CloseDiscarding();
                }
                else
                {
                    owned.Close();
                }
            }

            window.CloseKeepingRecovery();
        }

        if (Application.Current is not null)
        {
            Settle();
        }

        MailDocument.Transport = MailKitTransport.Instance;
        MailDocument.ConfigureSyncForTests = null;
        MailReminders.CalendarRunsForTests = null;
        MailReminders.NowForTests = null;
        AutoConfig.FetchForTests = null;
        DnsLookup.ResolverForTests = null;
        RemotePictures.FetchForTests = null;
        OfficeApplication.FileDialogs = new SystemFileDialogs();
        Ui.OpenExternalForTests = null;
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        AccessPolicy.UsePolicyReaderForTests(null);
        AccessPolicy.UseTextReaderForTests(null);
        ThixMail.Core.Accounts.OAuthProviders.FindForTests = null;
        ThixMail.Core.Accounts.OAuthClients.ForTests = null;
        ThixMail.Core.Accounts.OAuthService.HandlerForTests = null;
        ThixMail.Pages.AccountForm.BrowserForTests = null;
        PrintService.System = null!;
        UserRegistry.UseForTests(null);
        ThixOffice.Core.Spelling.SpellingService.UseForTests(null);
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

    public static async Task SettleAsync(int rounds = 6)
    {
        for (var round = 0; round < rounds; round++)
        {
            Settle();
            await Task.Delay(10);
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

    /// <summary>Waits (up to ~8 s) until <paramref name="condition"/> holds, settling the UI between.</summary>
    public static Task UntilAsync(Func<bool> condition, string what) => UntilAsync(condition, () => what);

    /// <summary>The same with a description made at the failure (what the window shows then).</summary>
    public static async Task UntilAsync(Func<bool> condition, Func<string> what)
    {
        for (var round = 0; round < 300 && !condition(); round++)
        {
            await SettleAsync(1);
            await Task.Delay(15);
        }

        Assert.True(condition(), $"Timed out waiting for {what()}");
    }

    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    public static MailEditor EditorOf(OfficeWindow window) => (MailEditor)window.Editor;

    /// <summary>The main window; with <paramref name="sample"/> the sample account synced (its bodies cached).</summary>
    public async Task<OfficeWindow> WindowAsync(bool sample = true)
    {
        var window = App.NewDocumentWindow();
        window.Width = 1400;
        window.Height = 900;
        await SettleAsync();
        if (sample)
        {
            await SampleMailbox.FillAsync(EditorOf(window).Document, Server);
            await UntilAsync(() => EditorOf(window).Rows.Count > 0, "the sample messages in the list");
        }

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

    public static async Task ClickAsync(TopLevel window, Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        control.BringIntoView();
        await SettleAsync();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        await SettleAsync();
    }

    /// <summary>The owned window of a type (waits a little for it).</summary>
    public static async Task<T> OwnedAsync<T>(Window window)
        where T : Window
    {
        for (var round = 0; round < 60; round++)
        {
            if (window.OwnedWindows.OfType<T>().LastOrDefault() is { } found)
            {
                await SettleAsync();
                return found;
            }

            await SettleAsync(2);
        }

        throw new InvalidOperationException($"No {typeof(T).Name} was shown.");
    }

    public static MessageHeader Header(MailEditor editor, string subjectStart) =>
        editor.Document.Store.Headers([.. editor.Document.Store.List(new Core.Store.MessageQuery([.. editor.Document.Store.Folders().Select(f => f.Id)])).Select(e => e.Id)])
            .First(h => h.Subject.StartsWith(subjectStart, StringComparison.Ordinal));

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

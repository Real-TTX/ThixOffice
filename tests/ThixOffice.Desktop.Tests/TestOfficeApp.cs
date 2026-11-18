using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Shell;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ThixOffice.Desktop.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixOffice.Desktop.Tests;

/// <summary>The test app rendered headless with Skia: real pixels, no screen.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestOfficeApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>"ThixTest": the smallest app of the suite – a text document in a text box, one command of its own, a ribbon of two tabs.</summary>
public sealed class TestOfficeApp : OfficeApplication
{
    public static OfficeAppDefinition TestDefinition { get; } = new(
        "thixtest",
        "ThixTest",
        "#185ABD",
        "Ein Testprogramm.",
        [
            new DocumentType(".tsx", "ThixTest.Document", "Testdokument", DocumentTypeSupport.Planned, IsDefault: true),
            new DocumentType(".tst", "ThixTest.Text", "Testtext"),
        ])
    {
        UntitledBaseName = "Test",
    };

    public const string ShoutCommand = "test.shout";

    public override OfficeAppDefinition Definition => TestDefinition;

    public override RibbonLayout Ribbon { get; } = new(
    [
        new RibbonTab("home", "Start", [new RibbonGroup("doc", "Dokument", [new RibbonItem(CommonCommandIds.New, RibbonItemSize.Large), new RibbonItem(CommonCommandIds.Save), new RibbonItem(ShoutCommand)])]),
        new RibbonTab("view", "Ansicht", [new RibbonGroup("ribbon", "Menüband", [new RibbonItem(CommonCommandIds.ToggleRibbon)])]),
    ]);

    protected override string AssetsAssembly => "ThixOffice.Desktop.Tests";

    public override OfficeDocument CreateDocument(string untitledName) => new TestTextDocument(untitledName);

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document) => new TestEditor((TestTextDocument)document);

    public override void AddCommands(OfficeWindow window) =>
        window.Commands.Add(new OfficeCommand(ShoutCommand, "Großschreiben", () => ((TestTextDocument)window.Document).Set(((TestTextDocument)window.Document).Text.ToUpperInvariant()))
        {
            Icon = "type",
            Keywords = ["großbuchstaben", "versalien"],
        });

    protected override void FillSampleDocument(OfficeDocument document) => ((TestTextDocument)document).Set("Beispiel");
}

/// <summary>Text with an undo stack of whole texts.</summary>
public sealed class TestTextDocument(string untitledName) : OfficeDocument(untitledName)
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public string Text { get; private set; } = "";

    public override bool CanUndo => _undo.Count > 0;

    public override bool CanRedo => _redo.Count > 0;

    public void Set(string text)
    {
        _undo.Push(Text);
        _redo.Clear();
        Text = text;
        OnContentChanged();
    }

    public override void Undo()
    {
        _redo.Push(Text);
        Text = _undo.Pop();
        OnContentChanged();
    }

    public override void Redo()
    {
        _undo.Push(Text);
        Text = _redo.Pop();
        OnContentChanged();
    }

    protected override void Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Text = reader.ReadToEnd();
    }

    protected override void Write(Stream stream) => stream.Write(Encoding.UTF8.GetBytes(Text));
}

/// <summary>A text box on the document; status: the number of characters.</summary>
public sealed class TestEditor : OfficeEditor
{
    private readonly TestTextDocument _document;
    private bool _updating;

    public TestEditor(TestTextDocument document)
    {
        _document = document;
        Box = new TextBox { Text = document.Text, AcceptsReturn = true };
        Box.TextChanged += (_, _) =>
        {
            if (!_updating && Box.Text != _document.Text)
            {
                _document.Set(Box.Text ?? "");
            }
        };
        _document.Changed += OnChanged;
    }

    public TextBox Box { get; }

    public override Control View => Box;

    public override void FocusDocument() => Box.Focus();

    public override void UpdateStatus(IOfficeStatusBar statusBar) => statusBar.SetItem("chars", $"{_document.Text.Length} Zeichen");

    public override void Detach() => _document.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e)
    {
        _updating = true;
        Box.Text = _document.Text;
        _updating = false;
    }
}

/// <summary>The file dialogs answer what the test says (never a real dialog).</summary>
public sealed class FakeFileDialogs : IFileDialogs
{
    public Queue<string?> SaveTargets { get; } = new();

    public Queue<IReadOnlyList<string>> OpenAnswers { get; } = new();

    public int SaveAsked { get; private set; }

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(Window owner, string startFolder) =>
        Task.FromResult(OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : (IReadOnlyList<string>)[]);

    public Task<string?> PickSaveTargetAsync(Window owner, string suggestedFileName, string startFolder)
    {
        SaveAsked++;
        return Task.FromResult(SaveTargets.Count > 0 ? SaveTargets.Dequeue() : null);
    }

    public Task<string?> PickFolderAsync(Window owner, string startFolder) => Task.FromResult<string?>(startFolder);
}

/// <summary>
/// Every test: its own data folder, no policies, an in-memory registry and the fake file dialogs; afterwards every window
/// is closed without asking.
/// </summary>
public sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(TestOfficeApp.TestDefinition, typeof(TestData).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixoffice-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DocumentsFolder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(name => Policies.Contains(name));
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        DocumentService.ResetUntitledCountForTests();
        OfficeApplication.FileDialogs = Dialogs;
        Ui.OpenExternalForTests = Opened.Add;
    }

    public string Folder { get; }

    public string DocumentsFolder => Path.Combine(Folder, "Dokumente");

    public HashSet<string> Policies { get; } = [];

    public FakeFileDialogs Dialogs { get; } = new();

    public List<string> Opened { get; } = [];

    public static TestOfficeApp App => (TestOfficeApp)Application.Current!;

    public string File(string name, string content)
    {
        var path = Path.Combine(DocumentsFolder, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        foreach (var window in App.DocumentWindows.ToList())
        {
            window.CloseKeepingRecovery();
        }

        Settle();
        OfficeShellParts.CreateRibbon = () => new TemporaryCommandBar();
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

    /// <summary>Lets the dispatcher, the layout and the render timer run.</summary>
    public static void Settle()
    {
        for (var round = 0; round < 6; round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Ctrl on Windows and Linux, Cmd on macOS: the command key of the shortcuts.</summary>
    public static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
}

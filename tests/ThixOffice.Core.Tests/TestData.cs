using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixOffice.Core.Tests;

/// <summary>The app the core tests are: "ThixTest" with a supported type (.tst) and a planned default type (.tsx).</summary>
internal static class TestApp
{
    public static OfficeAppDefinition Definition { get; } = new(
        "thixtest",
        "ThixTest",
        "#185ABD",
        "Ein Testprogramm.",
        [
            new DocumentType(".tsx", "ThixTest.Document", "Testdokument", DocumentTypeSupport.Planned, IsDefault: true),
            new DocumentType(".tst", "ThixTest.Text", "Testtext", MimeType: "text/plain"),
        ])
    {
        UntitledBaseName = "Test",
        CloudAppId = MatCloud.Contract.CloudApps.Write,
    };
}

/// <summary>A document whose content is one string (UTF-8).</summary>
internal sealed class TestDocument(string untitledName) : OfficeDocument(untitledName)
{
    private readonly Stack<string> _undo = new();

    public string Content { get; private set; } = "";

    public override bool CanUndo => _undo.Count > 0;

    public override bool CanRedo => false;

    public void Type(string text)
    {
        _undo.Push(Content);
        Content = text;
        OnContentChanged();
    }

    public override void Undo() => Content = _undo.Pop();

    public override void Redo()
    {
    }

    protected override void Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Content = reader.ReadToEnd();
    }

    protected override void Write(Stream stream) => stream.Write(Encoding.UTF8.GetBytes(Content));
}

/// <summary>
/// Every test gets its own data folder, an in-memory registry, no machine policies and secrets in memory: nothing a test
/// does touches the real settings, registry values, Keychain, LaunchAgents or documents.
/// </summary>
internal sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(TestApp.Definition, typeof(TestData).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixoffice-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(DocumentsFolder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        Registry = new MemoryUserRegistry();
        UserRegistry.UseForTests(Registry);
        AccessPolicy.UsePolicyReaderForTests(name => Policies.Contains(name));
        TokenStore.UseMemoryForTests(true);
        SettingsService.ResetCache();
        RecentDocuments.ResetCache();
        DocumentService.ResetUntitledCountForTests();
        CloudService.ResetForTests();
        MatCloud.Contract.CloudHandoff.Folder = Path.Combine(Folder, "handoff");
    }

    public string Folder { get; }

    /// <summary>Where the tests keep their documents.</summary>
    public string DocumentsFolder => Path.Combine(Folder, "Dokumente");

    public MemoryUserRegistry Registry { get; }

    /// <summary>The machine policies that are on (names of AccessPolicy).</summary>
    public HashSet<string> Policies { get; } = [];

    /// <summary>A document file with this content.</summary>
    public string File(string name, string content = "Hallo")
    {
        var path = Path.Combine(DocumentsFolder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        AutostartService.UseLaunchAgentFolderForTests(null);
        InstallerOptions.UseConfigForTests(null);
        if (OperatingSystem.IsWindows())
        {
            InstallService.UseLocationsForTests(null);
        }

        CloudService.ResetForTests();
        MatCloud.Contract.CloudHandoff.Folder = MatCloud.Contract.CloudHandoff.DefaultFolder();
        TokenStore.UseMemoryForTests(false);
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
}

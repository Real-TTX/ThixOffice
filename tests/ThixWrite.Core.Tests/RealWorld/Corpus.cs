using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// The real-world corpus: Fixtures/RealWorld holds the generated documents (<see cref="CorpusDocuments"/>, written like Word writes
/// them) and in ApachePOI/ a few files Word itself wrote (Apache POI's test data, Apache License 2.0 – see LICENSE.txt there).
/// </summary>
public static class Corpus
{
    /// <summary>The corpus as copied next to the tests.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealWorld");

    public static string PathOf(string name) =>
        Directory.GetFiles(Folder, name, SearchOption.AllDirectories).FirstOrDefault() ?? throw new FileNotFoundException(name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    /// <summary>The folder of the corpus in the source tree (for regenerating it).</summary>
    internal static string SourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ThixWrite.Core.Tests.csproj")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException("ThixWrite.Core.Tests"), "Fixtures", "RealWorld");
    }
}

/// <summary>The generator writes valid packages; with THIXWRITE_CORPUS_WRITE=1 it rewrites the corpus in the source tree.</summary>
public sealed class CorpusGeneration
{
    public static TheoryData<string> Names => [.. CorpusDocuments.All.Select(document => document.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_generated_document_is_a_valid_Word_package(string name)
    {
        var bytes = CorpusDocuments.All.Single(document => document.Name == name).Build();
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package)
            .Where(error => !error.Description.Contains("mc:Ignorable", StringComparison.Ordinal))
            .Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));
    }

    [Fact]
    public void Regenerate_when_asked()
    {
        if (Environment.GetEnvironmentVariable("THIXWRITE_CORPUS_WRITE") != "1")
        {
            return;
        }

        var folder = Corpus.SourceFolder();
        Directory.CreateDirectory(folder);
        foreach (var (name, build) in CorpusDocuments.All)
        {
            File.WriteAllBytes(Path.Combine(folder, name), build());
        }
    }
}

namespace ThixOffice.Core.Tests;

/// <summary>
/// The contract of MatCloud is a copy that is never edited here: every file matches the hash its manifest names (SHA-256 of
/// the text with LF line ends), and no file is there that the manifest does not name. To change it, change it in MatCloud
/// and copy it again (CLAUDE.md). The copy of MatDesktop is held by ThixOffice.Desktop.Tests.
/// </summary>
public sealed class CopyTests
{
    [Fact]
    public void The_copy_of_the_MatCloud_contract_is_unchanged() =>
        Repository.AssertCopy(Path.Combine(Repository.Root, "src", "ThixOffice.Core", "MatCloud"), "matcloud.version", "MatCloud.Contract ", "*.cs",
            "copy the contract again from MatCloud (node ../MatCloud/tools/contract-copy.js src/ThixOffice.Core) instead of editing it");
}

/// <summary>The repository the tests run in, and the check of a copied folder against its manifest.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    public static void AssertCopy(string folder, string manifest, string header, string pattern, string advice)
    {
        var lines = File.ReadAllLines(Path.Combine(folder, manifest));
        Assert.StartsWith(header, lines[0]);
        var listed = new List<string>();
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var (hash, file) = (line[..64], line[66..]);
            listed.Add(file);
            var text = File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(hash == actual, $"{file} was changed: {advice}");
        }

        var present = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Where(file => file != manifest);
        Assert.Equal(listed.Order(StringComparer.Ordinal), present.Order(StringComparer.Ordinal));
    }

    private static string Find()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ThixOffice.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository was not found above the test folder.");
    }
}

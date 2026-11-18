using System.Text.RegularExpressions;

namespace ThixOffice.Desktop.Tests;

/// <summary>The house rules the code itself can be held to: colours only from MatDesktop, the copy of MatDesktop untouched.</summary>
public sealed class RulesTests
{
    /// <summary>A literal colour: #RRGGBB, #AARRGGBB, Color.Parse/FromRgb/FromArgb, Colors.X, Brushes.X.</summary>
    internal static readonly Regex LiteralColour = new(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|Colors\.\w+|Brushes\.\w+");

    [Fact]
    public void The_shared_desktop_writes_no_colour_of_its_own()
    {
        // DESK-02: every colour comes from MatDesktop's theme ({DynamicResource Mat.*}, control.Use(...)); MatDesktop itself is a copy.
        var source = Path.Combine(Repository.Root, "src", "ThixOffice.Desktop");
        Assert.Empty(Findings(source));
    }

    [Fact]
    public void The_copy_of_MatDesktop_is_unchanged() =>
        Repository.AssertCopy(Path.Combine(Repository.Root, "src", "ThixOffice.Desktop", "MatDesktop"), "matdesktop.version", "MatDesktop ", "*.*",
            "copy it again from MatDesign (node tools/matdesktop-copy.js there) instead of editing it");

    /// <summary>Every line with a literal colour in the .cs and .axaml files below <paramref name="source"/> (not MatDesktop, bin, obj); the app's accent constant is allowed.</summary>
    internal static List<string> Findings(string source) =>
        Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(file => !Segment(file, "MatDesktop") && !Segment(file, "obj") && !Segment(file, "bin"))
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(item => LiteralColour.IsMatch(item.line) && !item.line.Contains("public const string Accent", StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(source, item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToList();

    private static bool Segment(string file, string name) => file.Contains($"{Path.DirectorySeparatorChar}{name}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
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

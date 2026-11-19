using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using ThixOffice.Desktop;

namespace ThixWrite.Tests;

/// <summary>
/// The pictures of ThixWrite (the same as "ThixWrite --snapshot &lt;folder&gt;"): every window and backstage page in light and
/// dark. THIXWRITE_SCREENSHOTS=&lt;folder&gt; keeps them – look at them after a change of the UI.
/// </summary>
public sealed class SnapshotTests
{
    [AvaloniaFact]
    public void Every_picture_is_drawn_in_light_and_dark()
    {
        using var data = new TestData();
        var folder = Environment.GetEnvironmentVariable("THIXWRITE_SCREENSHOTS") is { Length: > 0 } kept ? kept : Path.Combine(data.Folder, "snapshots");

        var report = OfficeSnapshot.Render(TestData.App, folder);

        Assert.Equal(OfficeSnapshot.FileNames(TestData.App).Count, report.Written.Count + report.Skipped.Count);
        Assert.All(report.Written, file => Assert.True(new FileInfo(Path.Combine(folder, file)).Length > 5000, $"{file} is (nearly) empty"));
        Assert.Contains("thixwrite-main-dark.png", report.Written);
        Assert.Empty(TestData.App.DocumentWindows);
    }

    [Fact]
    public void ThixWrite_writes_no_colour_of_its_own()
    {
        // DESK-02: colours only from MatDesktop's theme; the one literal is the accent (ThixWriteApp.Accent). The one exemption:
        // the colours that are document data (paper, ink, the palettes of Schriftfarbe and Hervorhebung) in DocumentPalette.cs.
        var literal = new Regex(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|Colors\.\w+|Brushes\.\w+");
        var root = FindRoot();
        var palette = Path.Combine(root, "src", "ThixWrite", "Editor", "DocumentPalette.cs");
        Assert.True(File.Exists(palette));
        var findings = new[] { "ThixWrite", "ThixWrite.Core" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.*", SearchOption.AllDirectories))
            .Where(file => file != palette)
            .Where(file => (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(item => literal.IsMatch(item.line) && !item.line.Contains("public const string Accent", StringComparison.Ordinal))
            .Select(item => $"{Path.GetFileName(item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToList();
        Assert.Empty(findings);
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ThixOffice.slnx")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}

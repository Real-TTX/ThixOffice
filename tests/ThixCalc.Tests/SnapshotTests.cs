using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using ThixOffice.Desktop;

namespace ThixCalc.Tests;

/// <summary>
/// The pictures of ThixCalc (the same as "ThixCalc --snapshot &lt;folder&gt;"): every window and backstage page in light and
/// dark. THIXCALC_SCREENSHOTS=&lt;folder&gt; keeps them – look at them after a change of the UI.
/// </summary>
public sealed class SnapshotTests
{
    [AvaloniaFact]
    public void Every_picture_is_drawn_in_light_and_dark()
    {
        using var data = new TestData();
        var folder = Environment.GetEnvironmentVariable("THIXCALC_SCREENSHOTS") is { Length: > 0 } kept ? kept : Path.Combine(data.Folder, "snapshots");

        var report = OfficeSnapshot.Render(TestData.App, folder);

        Assert.Equal(OfficeSnapshot.FileNames(TestData.App).Count, report.Written.Count + report.Skipped.Count);
        Assert.All(report.Written, file => Assert.True(new FileInfo(Path.Combine(folder, file)).Length > 5000, $"{file} is (nearly) empty"));
        Assert.Contains("thixcalc-main-dark.png", report.Written);
        Assert.Empty(TestData.App.DocumentWindows);
    }

    [Fact]
    public void ThixCalc_writes_no_colour_of_its_own()
    {
        // DESK-02: colours only from MatDesktop's theme; the one literal is the accent (ThixCalcApp.Accent). The one exemption:
        // the colours that are document data (the sheet, grid lines, reference colours, palettes, samples) in GridPalette.cs.
        var literal = new Regex(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|(?<![A-Za-z])Colors\.\w+|SKColors\.\w+|Brushes\.\w+|FromHex\(""");
        var root = FindRoot();
        var palette = Path.Combine(root, "src", "ThixCalc", "Editor", "GridPalette.cs");
        Assert.True(File.Exists(palette));
        var findings = Directory.EnumerateFiles(Path.Combine(root, "src", "ThixCalc"), "*.*", SearchOption.AllDirectories)
            .Where(file => file != palette)
            .Where(file => (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(item => literal.IsMatch(item.line))
            .Select(item => $"{Path.GetFileName(item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToList();
        Assert.Empty(findings);
    }

    internal static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ThixOffice.slnx")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}

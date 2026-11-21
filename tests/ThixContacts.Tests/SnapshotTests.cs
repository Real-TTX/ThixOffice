using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using ThixOffice.Desktop;

namespace ThixContacts.Tests;

/// <summary>
/// The pictures of ThixContacts (the same as "ThixContacts --snapshot &lt;folder&gt;"): the main window, the scenes (edit, groups, merge, CSV columns,
/// signed out, print) and the backstage in light and dark. THIXCONTACTS_SCREENSHOTS=&lt;folder&gt; keeps them – LOOK at them.
/// </summary>
public sealed class SnapshotTests
{
    [AvaloniaFact]
    public void Every_picture_is_drawn()
    {
        using var data = new TestData();
        var folder = Environment.GetEnvironmentVariable("THIXCONTACTS_SCREENSHOTS") is { Length: > 0 } kept ? kept : Path.Combine(data.Folder, "snapshots");
        var report = OfficeSnapshot.Render(TestData.App, folder);
        Assert.Equal(OfficeSnapshot.FileNames(TestData.App).Count, report.Written.Count + report.Skipped.Count);
        Assert.Contains("thixcontacts-main-light.png", report.Written);
        Assert.DoesNotContain(OfficeSnapshot.FileNames(TestData.App), name => name.Contains("backstage-open", StringComparison.Ordinal));
    }

    [Fact]
    public void ThixContacts_writes_no_colour_of_its_own()
    {
        // DESK-02: colours only from MatDesktop's theme; the one literal is the accent (ThixContactsApp.Accent). The one exemption: the paper colours of the
        // printed contacts in PrintPalette.cs. ThixOffice.Pim.Desktop has none at all.
        var literal = new Regex(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|(?<![A-Za-z])Colors\.\w+|SKColors\.\w+|Brushes\.\w+|FromHex\(""|new RgbColor\(|RgbColor\(0x");
        var root = FindRoot();
        var palette = Path.Combine(root, "src", "ThixContacts", "Pages", "PrintPalette.cs");
        Assert.True(File.Exists(palette));
        var findings = new[] { Path.Combine(root, "src", "ThixContacts"), Path.Combine(root, "src", "ThixOffice.Pim.Desktop") }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
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
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ThixMail.Core.Mime;
using ThixMail.Editor;
using ThixOffice.Desktop;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>
/// The pictures of ThixMail (the same as "ThixMail --snapshot &lt;folder&gt;") in light and dark, and the 15 fixture mails as the reading pane shows them
/// (&lt;folder&gt;/mails). THIXMAIL_SCREENSHOTS=&lt;folder&gt; keeps them – LOOK at them.
/// </summary>
public sealed class SnapshotTests
{
    private static string Folder(TestData data) =>
        Environment.GetEnvironmentVariable("THIXMAIL_SCREENSHOTS") is { Length: > 0 } kept ? kept : Path.Combine(data.Folder, "snapshots");

    [AvaloniaFact]
    public void Every_picture_is_drawn()
    {
        using var data = new TestData();
        var report = OfficeSnapshot.Render(TestData.App, Folder(data));
        Assert.Equal(OfficeSnapshot.FileNames(TestData.App).Count, report.Written.Count + report.Skipped.Count);
        Assert.Contains("thixmail-main-light.png", report.Written);
        Assert.Contains("thixmail-compose-dark.png", report.Written);
    }

    [AvaloniaFact]
    public async Task The_fixture_mails_are_drawn_as_the_reading_pane_shows_them()
    {
        using var data = new TestData();
        var folder = Path.Combine(Folder(data), "mails");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.eml").Order())
        {
            foreach (var loaded in new[] { false, true })
            {
                var mail = MailReader.Read(System.IO.File.ReadAllBytes(file));
                var view = new MessageView();
                var window = new Window { Width = 900, Height = 1300, Content = view };
                window.Show();
                await SettleAsync();
                view.Show(mail, null, null);
                await SettleAsync();
                if (loaded && view.View is { HasBlockedImages: true } blocked)
                {
                    view.Show(mail, null, blocked.BlockedImages.ToDictionary(s => s, _ => Png));
                    await SettleAsync();
                }
                else if (loaded)
                {
                    window.Close();
                    continue;
                }

                Assert.NotNull(view.Surface);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame!.Save(Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + (loaded ? "-pictures" : "") + ".png"));
                window.Close();
            }
        }

        Assert.True(Directory.EnumerateFiles(folder, "*.png").Count() >= 15);
    }

    [Fact]
    public void ThixMail_writes_no_colour_of_its_own()
    {
        // DESK-02: colours only from MatDesktop's theme; the one literal is the accent (ThixMailApp.Accent). The paper of a mail is ThixWrite's
        // document colour (ThixWrite.Desktop's DocumentPalette, exempted there). ThixMail.Core holds none either.
        var literal = new Regex(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|(?<![A-Za-z])Colors\.\w+|SKColors\.\w+|Brushes\.\w+|FromHex\(""|new RgbColor\(|RgbColor\(0x");
        var root = FindRoot();
        var findings = new[] { Path.Combine(root, "src", "ThixMail"), Path.Combine(root, "src", "ThixMail.Core") }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            .Where(file => (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.EndsWith("SampleMailbox.cs", StringComparison.Ordinal))
            .SelectMany(file => System.IO.File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(item => literal.IsMatch(item.line) && !item.line.Contains("public const string Accent", StringComparison.Ordinal))
            .Select(item => $"{Path.GetFileName(item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToList();
        Assert.Empty(findings);
    }

    internal static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !System.IO.File.Exists(Path.Combine(folder.FullName, "ThixOffice.slnx")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}

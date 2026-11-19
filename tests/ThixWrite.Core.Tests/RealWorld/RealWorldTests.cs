using ThixOffice.Text;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// Regression facts of the real-world corpus: page counts, where things are, how they look (resolved formats, cell fills, text
/// box rectangles, columns), and the docx round trip of every file (re-read equals, unknown parts kept). Laid out with the
/// deterministic fixed-width measurer, so the numbers do not depend on the fonts of the machine.
/// </summary>
public sealed class RealWorldTests
{
    public static TheoryData<string> Files =>
    [
        .. Directory.GetFiles(Corpus.Folder, "*.docx", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal),
    ];

    private static (Document Document, DocumentLayout Layout) Open(string name)
    {
        var document = DocxReader.Read(new MemoryStream(Corpus.Bytes(name)));
        var layout = new DocumentLayout(document, new FixedWidthTextMeasurer());
        layout.Update();
        return (document, layout);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_file_opens_lays_out_and_survives_a_round_trip(string name)
    {
        var bytes = Corpus.Bytes(name);
        var document = DocxReader.Read(new MemoryStream(bytes));
        using (var layout = new DocumentLayout(document, new FixedWidthTextMeasurer()))
        {
            layout.Update();
            Assert.True(layout.PageCount >= 1);
        }

        var written = DocxTestSupport.Write(document);
        var reread = DocxTestSupport.Read(written);
        DocxTestSupport.AssertSameContent(document, reread);
        Assert.Equal(PartNames(bytes), PartNames(written)); // no part lost (pictures, charts, SmartArt, comments …)
    }

    private static IReadOnlyList<string> PartNames(byte[] bytes)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        return [.. zip.Entries.Select(entry => entry.FullName).Where(entry => !entry.EndsWith(".rels", StringComparison.Ordinal) && !entry.StartsWith("docProps/", StringComparison.Ordinal)
                                                                              && entry is not "[Content_Types].xml" and not "word/webSettings.xml" and not "word/fontTable.xml").Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Table_styles_give_the_heading_row_its_fill_and_white_bold_text_and_the_rows_their_bands()
    {
        var (document, layout) = Open("07-demo-tables.docx");
        var cells = layout.Pages[0].Cells;
        var first = cells.Where(cell => cell.TableStart == cells[0].TableStart).ToList();
        Assert.Equal("4472C4", first.Single(cell => cell.Row == 0 && cell.Cell == 1).Fill); // Überschrift: accent 1
        Assert.Equal(WordStyles.Tint("4472C4", 0.2), first.Single(cell => cell.Row == 1 && cell.Cell == 1).Fill); // band 1: the accent at 20 %
        Assert.Null(first.Single(cell => cell.Row == 2 && cell.Cell == 1).Fill); // band 2: none
        Assert.False(first.Single(cell => cell.Row == 0 && cell.Cell == 1).Left?.IsVisible ?? false); // no inside lines in the heading row

        var resolver = new StyleResolver(document);
        var header = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "2024");
        var format = resolver.ResolveCharacter(header, ((TextRun)header.Inlines[0]).Properties);
        Assert.True(format.Bold);
        Assert.Equal("FFFFFF", format.Color);
        var nord = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "Nord");
        Assert.True(resolver.ResolveCharacter(nord, ((TextRun)nord.Inlines[0]).Properties).Bold); // erste Spalte
        var value = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "4,3");
        Assert.False(resolver.ResolveCharacter(value, ((TextRun)value.Inlines[0]).Properties).Bold);
    }
}

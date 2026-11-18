using System.Text;
using ThixWrite.Core.Formats.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>.txt import (encoding detection, line ends) and export.</summary>
public class PlainTextFormatTests
{
    private const string Sample = "Größe ändern\tÜbel\r\nZweite Zeile €";

    public static TheoryData<string, byte[]> Encodings => new()
    {
        { "UTF-8 BOM", [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes(Sample)] },
        { "UTF-16 LE", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Sample)] },
        { "UTF-16 BE", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Sample)] },
        { "UTF-32 LE", [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes(Sample)] },
        { "UTF-32 BE", [.. new UTF32Encoding(true, true).GetPreamble(), .. new UTF32Encoding(true, true).GetBytes(Sample)] },
        { "UTF-8", Encoding.UTF8.GetBytes(Sample) },
        { "Windows-1252", PlainTextFormat.Windows1252.GetBytes(Sample) },
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Every_encoding_is_detected(string name, byte[] bytes)
    {
        var document = PlainTextFormat.Read(new MemoryStream(bytes), out var detected);

        Assert.Equal(["Größe ändern\tÜbel", "Zweite Zeile €"], document.Blocks.Select(block => block.ToString()));
        Assert.False(string.IsNullOrEmpty(detected.WebName), name);
    }

    [Fact]
    public void Windows_1252_is_the_fallback_for_invalid_utf8()
    {
        var bytes = PlainTextFormat.Windows1252.GetBytes("Größe");

        PlainTextFormat.Read(new MemoryStream(bytes), out var detected);

        Assert.Equal(1252, detected.CodePage);
        Assert.Equal("Größe", PlainTextFormat.Decode(bytes, out _));
    }

    [Fact]
    public void Valid_utf8_without_bom_is_utf8()
    {
        PlainTextFormat.Read(new MemoryStream(Encoding.UTF8.GetBytes("Größe")), out var detected);

        Assert.Equal(Encoding.UTF8.CodePage, detected.CodePage);
    }

    [Theory]
    [InlineData("a\r\nb\r\nc")]
    [InlineData("a\nb\nc")]
    [InlineData("a\rb\rc")]
    [InlineData("a\r\nb\nc")]
    public void Every_line_end_makes_a_paragraph(string text)
    {
        var document = PlainTextFormat.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.Equal(["a", "b", "c"], document.Blocks.Select(block => block.ToString()));
    }

    [Fact]
    public void A_form_feed_is_a_page_break_and_other_controls_go()
    {
        var document = PlainTextFormat.Read(new MemoryStream(Encoding.UTF8.GetBytes("Seite 1\fSeite 2\u0001")));

        Assert.Equal("Seite 1\fSeite 2", ((Paragraph)document.Blocks[0]).Text);
    }

    [Fact]
    public void Writing_and_reading_round_trips()
    {
        var original = PlainTextFormat.FromText("Erste Zeile\r\n\tEingerückt äöü ß\r\n\r\nLetzte 😀");

        using var stream = new MemoryStream();
        PlainTextFormat.Write(original, stream);
        var bytes = stream.ToArray();
        var read = PlainTextFormat.Read(new MemoryStream(bytes));

        Assert.Equal("Erste Zeile\r\n\tEingerückt äöü ß\r\n\r\nLetzte 😀", Encoding.UTF8.GetString(bytes));
        Assert.True(original.ContentEquals(read));
    }

    [Fact]
    public void Writing_turns_line_breaks_into_lines_and_drops_objects()
    {
        var document = new Document(
        [
            new Paragraph([
                new TextRun("Zeile\vneue", CharacterProperties.Empty),
                new OpaqueInline("<x/>", PreservedKind.Drawing, true, CharacterProperties.Empty),
                new TextRun(" Trenn­ung", CharacterProperties.Empty),
            ]),
            new OpaqueBlock("<w:tbl/>", PreservedKind.Table, ["A\tB", "C\tD"]),
            new Paragraph("Ende"),
        ]);

        Assert.Equal("Zeile\r\nneue Trennung\r\nA\tB\r\nC\tD\r\nEnde", PlainTextFormat.ToText(document));
    }

    [Fact]
    public void An_encoding_with_a_preamble_writes_it()
    {
        using var stream = new MemoryStream();
        PlainTextFormat.Write(PlainTextFormat.FromText("ä"), stream, new UTF8Encoding(true));

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA4 }, stream.ToArray());
    }
}

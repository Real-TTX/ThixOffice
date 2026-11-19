using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.Formats.DocxTestSupport;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>Files made like Word makes them: what is read, what is reported, and that everything comes back on save.</summary>
public class DocxFixtureTests
{
    private static readonly byte[] Rich = DocxFixtures.CreateRich();

    [Fact]
    public void The_fixture_itself_is_valid()
    {
        AssertValid(Rich);
    }

    [Fact]
    public void Styles_and_direct_formatting_are_read_apart()
    {
        var document = Read(Rich);

        var heading = Assert.IsType<Paragraph>(document.Blocks[0]);
        Assert.Equal("Heading1", heading.Properties.StyleId);
        Assert.Equal("Titel", heading.Text);
        var headingStyle = document.Styles.Find("Heading1")!;
        Assert.Equal("Normal", headingStyle.BasedOn);
        Assert.Equal(16, headingStyle.Character.FontSize);
        Assert.Equal(ThemeFontSlot.Major, headingStyle.Character.FontTheme);
        Assert.True(headingStyle.Paragraph.KeepWithNext);
        Assert.Equal(12, headingStyle.Paragraph.SpacingBefore);
        Assert.Equal("Überschrift 1", BuiltInStyles.DisplayName(headingStyle));
        Assert.Equal(11, document.Styles.CharacterDefaults.FontSize);
        Assert.Equal(ThemeFontSlot.Minor, document.Styles.CharacterDefaults.FontTheme);
        Assert.Contains("w:lang", document.Styles.CharacterDefaults.UnknownXml);
        Assert.Equal(259 / 240.0, document.Styles.ParagraphDefaults.LineSpacing!.Value);
        Assert.Equal(StyleType.Table, document.Styles.Find("TableGrid")!.Type);
        Assert.Equal(new BorderLine("single", 0.5, null), document.Styles.Find("TableGrid")!.Table!.Borders!.Top);

        var mixed = Assert.IsType<Paragraph>(document.Blocks[1]);
        Assert.Equal(ParagraphAlignment.Justify, mixed.Properties.Alignment);
        var runs = mixed.Inlines.OfType<TextRun>().ToList();
        Assert.Equal("normal ", runs[0].Text);
        Assert.True(runs[0].Properties.IsEmpty);
        Assert.True(runs[1].Properties.Bold);
        Assert.Equal("Betont", runs[2].Properties.StyleId);
        Assert.Null(runs[2].Properties.Italic);
        Assert.Equal("Arial", runs[3].Properties.FontFamily);
        Assert.Equal(14, runs[3].Properties.FontSize);
        Assert.Contains("MS Mincho", runs[3].Properties.FontsXml);
    }

    [Fact]
    public void Lists_are_read_with_levels_overrides_and_kept_level_details()
    {
        var document = Read(Rich);

        var items = document.Blocks.OfType<Paragraph>().Where(paragraph => paragraph.Properties.Numbering is not null).ToList();
        Assert.Equal(["Eins", "Unterpunkt", "Wieder eins"], items.Select(item => item.Text));
        Assert.Equal(new NumberingReference(1, 1), items[1].Properties.Numbering);
        var level = document.Numbering.ResolveLevel(1, 1)!;
        Assert.Equal(NumberFormat.LowerLetter, level.Format);
        Assert.Equal("%2)", level.Text);
        Assert.Equal(72, level.IndentLeft);
        Assert.Equal(-18, level.FirstLineIndent);
        Assert.True(level.LabelProperties.Bold);
        Assert.Contains("tentative=", level.UnknownAttributes);
        Assert.Contains("w:tabs", level.UnknownXml);
        Assert.Equal(1, document.Numbering.Instance(2)!.OverrideAt(0)!.StartOverride);
    }

    [Fact]
    public void Unsupported_content_is_reported()
    {
        var document = Read(Rich);

        var notes = document.UnsupportedContent.ToDictionary(note => note.Kind, note => note.Count);
        Assert.False(notes.ContainsKey(PreservedKind.Table)); // tables are real tables now
        Assert.False(notes.ContainsKey(PreservedKind.Drawing)); // an inline picture is a picture now
        Assert.Equal(1, notes[PreservedKind.Footnote]);
        Assert.Equal(1, notes[PreservedKind.Comment]);
        Assert.Equal(2, notes[PreservedKind.TrackedChange]);
        Assert.False(notes.ContainsKey(PreservedKind.Field)); // PAGE and NUMPAGES are fields ThixWrite updates
        Assert.Equal(2, notes[PreservedKind.ContentControl]);
        Assert.False(notes.ContainsKey(PreservedKind.Bookmark));
        Assert.All(document.UnsupportedContent, note => Assert.False(string.IsNullOrWhiteSpace(note.Message)));
    }

    [Fact]
    public void Preserved_content_is_shown_where_it_can_be()
    {
        var document = Read(Rich);
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

        var field = paragraphs.Single(paragraph => paragraph.Text.StartsWith("Seite", StringComparison.Ordinal));
        Assert.Contains(field.Inlines, inline => inline is FieldInline { Kind: FieldKind.NumPages, Result: "3" });
        Assert.Contains(field.Inlines, inline => inline is FieldInline { Kind: FieldKind.Page, Result: "1" });

        var changes = paragraphs.Single(paragraph => paragraph.Inlines.Any(inline => inline is OpaqueInline { Kind: PreservedKind.TrackedChange }));
        var tracked = changes.Inlines.OfType<OpaqueInline>().ToList();
        Assert.Equal("eingefügt", tracked[0].DisplayText);
        Assert.Equal("", tracked[1].DisplayText);

        var image = paragraphs.SelectMany(paragraph => paragraph.Inlines).OfType<ImageInline>().Single();
        Assert.Equal(ImageFormat.Png, image.Image.Format);
        Assert.False(image.IsFloating);
        Assert.Equal(75, image.Width, 3);
        Assert.Equal(37.5, image.Height, 3);

        var cells = paragraphs.Where(paragraph => paragraph.Cell is not null).ToList();
        Assert.Equal(["A1", "B1", "A2", "B2"], cells.Select(paragraph => paragraph.Text));
        Assert.Equal([(0, 0), (0, 1), (1, 0), (1, 1)], cells.Select(paragraph => (paragraph.Cell!.Value.Row, paragraph.Cell!.Value.Cell)));
        Assert.All(cells, paragraph => Assert.Same(cells[0].Cell!.Value.Table, paragraph.Cell!.Value.Table));
        var control = document.Blocks.OfType<OpaqueBlock>().Single(block => block.Kind == PreservedKind.ContentControl);
        Assert.Equal(["Block-Steuerelement"], control.Lines);

        Assert.Contains(paragraphs.SelectMany(paragraph => paragraph.Inlines), inline => inline is OpaqueInline { Kind: PreservedKind.Bookmark, IsMarker: true });
        Assert.Equal("Kopfzeile", ((Paragraph)document.FinalSection.Headers.Default!.Blocks[0]).Text); // the header is read
    }

    [Fact]
    public void Writing_keeps_every_preserved_part_and_relationship()
    {
        var document = Read(Rich);

        var bytes = Write(document);

        AssertValid(bytes);
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var main = package.MainDocumentPart!;
        var body = main.Document!.Body!;

        var blip = body.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Single();
        var image = Assert.IsAssignableFrom<ImagePart>(main.GetPartById(blip.Embed!.Value!));
        using (var imageStream = image.GetStream())
        {
            using var copy = new MemoryStream();
            imageStream.CopyTo(copy);
            Assert.Equal(DocxFixtures.Png, copy.ToArray());
        }

        var headerReference = body.Descendants<W.HeaderReference>().Single();
        Assert.Equal("Kopfzeile", Assert.IsType<HeaderPart>(main.GetPartById(headerReference.Id!.Value!)).Header!.InnerText);
        Assert.Contains(main.FootnotesPart!.Footnotes!.Elements<W.Footnote>(), footnote => footnote.Id?.Value == 1);
        Assert.Single(body.Descendants<W.FootnoteReference>());
        Assert.Single(main.WordprocessingCommentsPart!.Comments!.Elements<W.Comment>());
        Assert.Single(body.Descendants<W.CommentRangeStart>());
        Assert.Single(body.Descendants<W.CommentReference>());
        Assert.Equal("Marke", body.Descendants<W.BookmarkStart>().Single().Name!.Value);
        Assert.Single(body.Descendants<W.BookmarkEnd>());
        Assert.Empty(body.Descendants<W.FieldChar>()); // the complex PAGE field is written as a simple one
        Assert.Equal(2, body.Descendants<W.SimpleField>().Count());
        Assert.Single(body.Descendants<W.InsertedRun>());
        Assert.Equal("gelöscht", body.Descendants<W.DeletedText>().Single().Text);
        Assert.Single(body.Descendants<W.SdtRun>());
        Assert.Single(body.Descendants<W.SdtBlock>());
        Assert.Single(body.Elements<W.Table>());
        Assert.Equal(1, body.Descendants<W.Bold>().Count(bold => bold.Parent is W.RunProperties));
    }

    [Fact]
    public void Reading_what_was_written_gives_the_same_model()
    {
        var first = Read(Rich);

        var second = RoundTrip(first);
        var third = RoundTrip(second);

        AssertSameContent(first, second);
        AssertSameContent(first, third);
        Assert.Equal(first.UnsupportedContent, second.UnsupportedContent);
    }

    [Fact]
    public void An_edited_document_is_written_into_its_original_package()
    {
        var read = Read(Rich);
        var blocks = read.Blocks.ToList();
        blocks.Insert(1, new Paragraph("Neu eingefügt", new ParagraphProperties { Alignment = ParagraphAlignment.Center }));
        blocks.RemoveAt(blocks.Count - 1);
        var edited = new Document(blocks, read.Styles, read.Numbering, read.FinalSection, read.Theme, read.Settings) { Package = read.Package };

        var again = RoundTrip(edited);

        AssertSameContent(edited, again);
        Assert.Equal("Neu eingefügt", again.Blocks[1].ToString());
        using var package = WordprocessingDocument.Open(new MemoryStream(Write(edited)), false);
        Assert.NotNull(package.MainDocumentPart!.FootnotesPart);
        Assert.Single(package.MainDocumentPart.ImageParts);
    }

    [Fact]
    public void The_theme_gives_the_theme_fonts_and_colours()
    {
        var bytes = DocxFixtures.CreateRaw(
            "<w:document xmlns:w=\"" + DocxFixtures.WordNamespace + "\"><w:body><w:p><w:r><w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\"/>"
            + "<w:color w:val=\"4F81BD\" w:themeColor=\"accent1\"/></w:rPr><w:t>Thema</w:t></w:r></w:p></w:body></w:document>",
            themeXml: DocxFixtures.ThemeXml("Cambria", "Candara"));

        var document = Read(bytes);

        Assert.Equal("Cambria", document.Theme.MajorFont);
        Assert.Equal("Candara", document.Theme.MinorFont);
        Assert.Equal("4F81BD", document.Theme.ColorOf("accent1"));
        Assert.Equal("000000", document.Theme.ColorOf("text1"));
        var run = Assert.IsType<TextRun>(((Paragraph)document.Blocks[0]).Inlines.Single());
        Assert.Equal(ThemeFontSlot.Major, run.Properties.FontTheme);
        Assert.Null(run.Properties.FontsXml);
        Assert.Equal("accent1", run.Properties.Color!.ThemeColor);

        var written = Write(document);
        using var package = WordprocessingDocument.Open(new MemoryStream(written), false);
        Assert.NotNull(package.MainDocumentPart!.ThemePart);
        Assert.Equal(document.Theme, Read(written).Theme with { Palette = document.Theme.Palette });
    }
}

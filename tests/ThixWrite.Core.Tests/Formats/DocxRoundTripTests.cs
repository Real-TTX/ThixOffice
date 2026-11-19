using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.Formats.DocxTestSupport;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>model → docx → model: everything the model holds comes back equal, and every written file is valid.</summary>
public class DocxRoundTripTests
{
    [Fact]
    public void A_blank_document_round_trips_and_is_valid()
    {
        var document = Document.CreateBlank();

        var read = RoundTrip(document);

        AssertSameContent(document, read);
        Assert.Empty(read.UnsupportedContent);
        Assert.NotNull(read.Package);
    }

    [Fact]
    public void Character_properties_round_trip()
    {
        var all = new CharacterProperties
        {
            StyleId = BuiltInStyles.Strong,
            FontFamily = "Arial",
            FontSize = 10.5,
            Bold = true,
            Italic = false,
            Underline = UnderlineStyle.DotDash,
            Strikethrough = true,
            DoubleStrikethrough = false,
            VerticalPosition = VerticalPosition.Superscript,
            Color = new TextColor("2F5496", "accent1", null, "BF"),
            Highlight = HighlightColor.BrightGreen,
        };
        var document = new Document(
        [
            new Paragraph([
                new TextRun("alles ", all),
                new TextRun("Thema", new CharacterProperties { FontTheme = ThemeFontSlot.Major }),
                new TextRun(" tief", new CharacterProperties { VerticalPosition = VerticalPosition.Subscript, Highlight = HighlightColor.None }),
                new TextRun(" leer", new CharacterProperties { Underline = UnderlineStyle.None, Color = TextColor.Auto }),
            ]),
        ]);

        AssertSameContent(document, RoundTrip(document));
    }

    [Theory]
    [InlineData(UnderlineStyle.Single)]
    [InlineData(UnderlineStyle.Double)]
    [InlineData(UnderlineStyle.WavyDouble)]
    [InlineData(UnderlineStyle.DashDotDotHeavy)]
    [InlineData(UnderlineStyle.Words)]
    public void Every_underline_style_round_trips(UnderlineStyle style)
    {
        var document = new Document([new Paragraph("u", runProperties: new CharacterProperties { Underline = style })]);

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Paragraph_properties_round_trip()
    {
        var all = new ParagraphProperties
        {
            StyleId = BuiltInStyles.Quote,
            Alignment = ParagraphAlignment.Justify,
            IndentLeft = 36,
            IndentRight = 18,
            FirstLineIndent = -18,
            SpacingBefore = 6,
            SpacingAfter = 12,
            LineSpacing = new LineSpacing(LineSpacingRule.Multiple, 1.5),
            KeepWithNext = true,
            KeepLinesTogether = false,
            PageBreakBefore = true,
            WidowControl = false,
            ContextualSpacing = true,
            Tabs = [new TabStop(72), new TabStop(144, TabAlignment.Right, TabLeader.Dot), new TabStop(200, TabAlignment.Clear),
                new TabStop(250, TabAlignment.Decimal, TabLeader.Underscore), new TabStop(300, TabAlignment.Center)],
            OutlineLevel = 2,
        };
        var document = new Document(
        [
            new Paragraph([new TextRun("voll", CharacterProperties.Empty)], all, new CharacterProperties { Bold = true, FontSize = 14 }),
            new Paragraph("genau", new ParagraphProperties { LineSpacing = new LineSpacing(LineSpacingRule.Exact, 15), FirstLineIndent = 12 }),
            new Paragraph("mindestens", new ParagraphProperties { LineSpacing = new LineSpacing(LineSpacingRule.AtLeast, 14), Alignment = ParagraphAlignment.Right }),
            new Paragraph("mitte", new ParagraphProperties { Alignment = ParagraphAlignment.Center, Numbering = NumberingReference.None }),
        ]);

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Text_with_tabs_breaks_and_unicode_round_trips()
    {
        var document = new Document(
        [
            new Paragraph("Größe\tÄrger\vneue Zeile\fneue Seite"),
            new Paragraph("  führende und  doppelte Leerzeichen "),
            new Paragraph("Emoji 😀👍🏽, Zeichen: \u00A0geschützt, weich\u00ADtrennen, nicht\u2011trennen, €, ß, Ω, 中文"),
            new Paragraph(""),
        ]);

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Hyperlinks_round_trip_with_their_relationships()
    {
        var web = new Hyperlink("https://www.example.com/pfad?q=1", Tooltip: "Beispiel");
        var anchor = new Hyperlink(null, "_Kapitel2");
        var link = new CharacterProperties { StyleId = BuiltInStyles.HyperlinkStyle };
        var document = new Document(
        [
            new Paragraph([
                new TextRun("Siehe ", CharacterProperties.Empty),
                new TextRun("die ", link, web),
                new TextRun("Seite", link with { Bold = true }, web),
                new TextRun(" und ", CharacterProperties.Empty),
                new TextRun("Kapitel 2", link, anchor),
            ]),
        ]);

        var bytes = Write(document);
        AssertValid(bytes);
        var read = Read(bytes);

        AssertSameContent(document, read);
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var relationship = Assert.Single(package.MainDocumentPart!.HyperlinkRelationships);
        Assert.Equal("https://www.example.com/pfad?q=1", relationship.Uri.OriginalString);
        Assert.Equal(2, package.MainDocumentPart!.Document!.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Single()
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>().Count());
    }

    [Fact]
    public void Styles_with_inheritance_and_defaults_round_trip()
    {
        var styles = BuiltInStyles.CreateDefault()
            .With(new Style
            {
                Id = "Brief",
                Name = "Brief",
                BasedOn = BuiltInStyles.Normal,
                Next = "BriefText",
                Link = "BriefZchn",
                IsCustom = true,
                QuickFormat = true,
                UiPriority = 5,
                Paragraph = new ParagraphProperties { SpacingAfter = 0, KeepWithNext = true },
                Character = new CharacterProperties { FontFamily = "Georgia", FontSize = 12 },
            })
            .With(new Style
            {
                Id = "BriefText",
                Name = "Brief Text",
                BasedOn = "Brief",
                IsCustom = true,
                Paragraph = new ParagraphProperties { FirstLineIndent = 18 },
            })
            .With(new Style
            {
                Id = "BriefZchn",
                Name = "Brief Zchn",
                Type = StyleType.Character,
                Link = "Brief",
                IsCustom = true,
                SemiHidden = true,
                Character = new CharacterProperties { Italic = true },
            });
        var document = new Document(
        [
            new Paragraph("Anrede", new ParagraphProperties { StyleId = "Brief" }),
            new Paragraph("Text", new ParagraphProperties { StyleId = "BriefText" }),
            new Paragraph("Titel", new ParagraphProperties { StyleId = BuiltInStyles.Title }),
            new Paragraph("Überschrift", new ParagraphProperties { StyleId = "Heading2" }),
        ], styles);

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Lists_nested_three_levels_with_a_restart_round_trip()
    {
        var numbering = new NumberingDefinitions(
            [BuiltInLists.Create(ListKind.Bullet, 0), BuiltInLists.Create(ListKind.Numbered, 1)],
            [
                new NumberingInstance { Id = 1, AbstractId = 0 },
                new NumberingInstance { Id = 2, AbstractId = 1 },
                new NumberingInstance { Id = 3, AbstractId = 1, Overrides = [new LevelOverride(0, 1)] },
                new NumberingInstance
                {
                    Id = 4,
                    AbstractId = 1,
                    Overrides = [new LevelOverride(1, 5, new NumberingLevel { Level = 1, Start = 5, Format = NumberFormat.UpperRoman, Text = "%1.%2)" })],
                },
            ]);
        var list = new ParagraphProperties { StyleId = BuiltInStyles.ListParagraph };
        var document = new Document(
        [
            new Paragraph("Punkt", list with { Numbering = new NumberingReference(1, 0) }),
            new Paragraph("Unterpunkt", list with { Numbering = new NumberingReference(1, 1) }),
            new Paragraph("Eins", list with { Numbering = new NumberingReference(2, 0) }),
            new Paragraph("a", list with { Numbering = new NumberingReference(2, 1) }),
            new Paragraph("i", list with { Numbering = new NumberingReference(2, 2) }),
            new Paragraph("Neu ab eins", list with { Numbering = new NumberingReference(3, 0) }),
            new Paragraph("Ersetzte Ebene", list with { Numbering = new NumberingReference(4, 1) }),
        ], numbering: numbering);

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Sections_and_page_setup_round_trip()
    {
        var landscape = SectionProperties.Default.WithOrientation(PageOrientation.Landscape) with
        {
            MarginTop = 36,
            MarginLeft = 54,
            Gutter = 18,
            TitlePage = true,
        };
        var final = SectionProperties.Default with
        {
            Start = SectionStart.Continuous,
            ColumnCount = 2,
            ColumnSpacing = 18,
            HeaderDistance = 30,
            FooterDistance = 25,
        };
        var document = new Document(
        [
            new Paragraph("Hochformat"),
            new Paragraph([new TextRun("Ende Abschnitt 1", CharacterProperties.Empty)], sectionBreak: SectionProperties.Default),
            new Paragraph([new TextRun("Quer", CharacterProperties.Empty)], sectionBreak: landscape),
            new Paragraph("Letzter Abschnitt"),
        ], finalSection: final, settings: new DocumentSettings { DefaultTabStop = 36 });

        AssertSameContent(document, RoundTrip(document));
    }

    [Fact]
    public void Preserved_inline_and_block_xml_written_by_thixwrite_survives()
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var bookmarkStart = $"<w:bookmarkStart w:name=\"Marke\" w:id=\"0\" xmlns:w=\"{W}\" />";
        var bookmarkEnd = $"<w:bookmarkEnd w:id=\"0\" xmlns:w=\"{W}\" />";
        var control = $"<w:sdt xmlns:w=\"{W}\"><w:sdtPr><w:alias w:val=\"Feld\" /></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Zelle</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        var document = new Document(
        [
            new Paragraph([
                new OpaqueInline(bookmarkStart, PreservedKind.Bookmark, false, CharacterProperties.Empty),
                new TextRun("markiert", CharacterProperties.Empty),
                new OpaqueInline(bookmarkEnd, PreservedKind.Bookmark, false, CharacterProperties.Empty),
            ]),
            new OpaqueBlock(control, PreservedKind.ContentControl, ["Zelle"]),
            new Paragraph("danach"),
        ]);

        var read = RoundTrip(document);

        // The bookmarks stay as they were; the content control around a paragraph opens as that paragraph between two marks …
        Assert.Equal(((Paragraph)document.Blocks[0]).Inlines.ToList(), ((Paragraph)read.Blocks[0]).Inlines.ToList());
        var opened = (Paragraph)read.Blocks[1];
        Assert.Equal("Zelle", opened.Text.Replace("￼", "", StringComparison.Ordinal));
        Assert.Equal([false, true], opened.Inlines.OfType<ContentControlMarkInline>().Select(mark => mark.IsEnd));
        Assert.Empty(read.UnsupportedContent);

        // … and is written back as the same w:sdt, again and again.
        var again = RoundTrip(read);
        AssertSameContent(read, again);
        var xml = DocumentXml(Write(again));
        Assert.Contains("<w:sdt><w:sdtPr><w:alias w:val=\"Feld\" /></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Zelle</w:t></w:r></w:p></w:sdtContent></w:sdt>", xml);
    }

    [Fact]
    public void Writing_twice_from_the_read_package_is_stable()
    {
        var document = new Document(
        [
            new Paragraph("Eins", new ParagraphProperties { StyleId = BuiltInStyles.Heading1 }),
            new Paragraph([new TextRun("Link", CharacterProperties.Empty, new Hyperlink("https://example.org"))]),
        ]);

        var first = RoundTrip(document);
        var second = RoundTrip(first);
        var third = RoundTrip(second);

        AssertSameContent(document, third);
        using var package = WordprocessingDocument.Open(new MemoryStream(Write(third)), false);
        Assert.Single(package.MainDocumentPart!.HyperlinkRelationships);
    }

    [Fact]
    public void The_paragraph_mark_formatting_of_an_empty_paragraph_round_trips()
    {
        var document = new Document([Paragraph.Empty(new ParagraphProperties { StyleId = BuiltInStyles.Heading1 }, new CharacterProperties { FontSize = 20 })]);

        AssertSameContent(document, RoundTrip(document));
    }
}

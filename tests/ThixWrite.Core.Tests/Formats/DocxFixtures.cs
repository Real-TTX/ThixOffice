using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using Dw = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>
/// Fixture .docx files built with the Open XML SDK directly (never with ThixWrite's writer), like files from Word or
/// LibreOffice: a rich one with everything ThixWrite keeps without supporting it, raw-XML ones for tolerance.
/// </summary>
internal static class DocxFixtures
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>A 1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// Styles (direct formatting vs styles, a character style), lists (nested, restarted), mixed runs, a bookmark, a comment,
    /// a footnote, tracked changes, a complex and a simple field, inline and block content controls, an inline image, a table
    /// and a header reference.
    /// </summary>
    public static byte[] CreateRich()
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = CreateStyles();
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = CreateNumbering();

            var image = main.AddImagePart(ImagePartType.Png);
            image.FeedData(new MemoryStream(Png));
            var imageId = main.GetIdOfPart(image);

            var footnotes = main.AddNewPart<FootnotesPart>();
            footnotes.Footnotes = new Footnotes(
                new Footnote(new Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                new Footnote(new Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 },
                new Footnote(new Paragraph(new Run(new FootnoteReferenceMark()), new Run(new Text(" Die Fußnote.") { Space = SpaceProcessingModeValues.Preserve }))) { Id = 1 });

            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new Comments(new Comment(new Paragraph(new Run(new Text("Ein Kommentar")))) { Id = "0", Author = "Anna", Initials = "A" });

            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(new Run(new Text("Kopfzeile"))));
            var headerId = main.GetIdOfPart(header);

            main.Document = new Document(new Body(
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("Titel"))),
                new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Both }),
                    new Run(new Text("normal ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold()), new Text("fett")),
                    new Run(new RunProperties(new RunStyle { Val = "Betont" }), new Text(" betont")),
                    new Run(new RunProperties(new RunFonts { Ascii = "Arial", HighAnsi = "Arial", EastAsia = "MS Mincho" }, new FontSize { Val = "28" }), new Text(" gemischt")),
                    new BookmarkStart { Id = "1", Name = "Marke" },
                    new Run(new Text(" markiert") { Space = SpaceProcessingModeValues.Preserve }),
                    new BookmarkEnd { Id = "1" },
                    new CommentRangeStart { Id = "0" },
                    new Run(new Text(" kommentiert") { Space = SpaceProcessingModeValues.Preserve }),
                    new CommentRangeEnd { Id = "0" },
                    new Run(new RunProperties(new RunStyle { Val = "CommentReference" }), new CommentReference { Id = "0" }),
                    new Run(new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }), new FootnoteReference { Id = 1 })),
                ListItem("Eins", 1, 0),
                ListItem("Unterpunkt", 1, 1),
                ListItem("Wieder eins", 2, 0),
                new Paragraph(
                    new Run(new Text("Seite ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
                    new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
                    new Run(new Text("1")),
                    new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
                    new Run(new Text(" von ") { Space = SpaceProcessingModeValues.Preserve }),
                    new SimpleField(new Run(new Text("3"))) { Instruction = " NUMPAGES " }),
                new Paragraph(
                    new InsertedRun(new Run(new Text("eingefügt"))) { Id = "10", Author = "Anna", Date = new DateTime(2026, 11, 1) },
                    new DeletedRun(new Run(new DeletedText("gelöscht"))) { Id = "11", Author = "Anna", Date = new DateTime(2026, 11, 1) }),
                new Paragraph(
                    new Run(new Text("Feld: ") { Space = SpaceProcessingModeValues.Preserve }),
                    new SdtRun(new SdtProperties(new SdtAlias { Val = "Name" }, new SdtId { Val = 5 }), new SdtContentRun(new Run(new Text("Inhalt"))))),
                new Paragraph(new Run(CreateDrawing(imageId))),
                CreateTable(),
                new SdtBlock(new SdtProperties(new SdtId { Val = 6 }), new SdtContentBlock(new Paragraph(new Run(new Text("Block-Steuerelement"))))),
                new Paragraph(new Run(new Text("Ende"))),
                new SectionProperties(
                    new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId },
                    new PageSize { Width = 11906, Height = 16838 },
                    new PageMargin { Top = 1417, Right = 1417, Bottom = 1134, Left = 1417, Header = 708, Footer = 708, Gutter = 0 },
                    new Columns { Space = "708" },
                    new DocGrid { LinePitch = 360 })));
        }

        return stream.ToArray();
    }

    /// <summary>A package with document.xml only (no styles, numbering, settings, theme).</summary>
    public static byte[] CreateWithoutStyles() => CreateRaw(
        "<w:document xmlns:w=\"" + WordNamespace + "\"><w:body><w:p><w:r><w:t>Nur Text</w:t></w:r></w:p></w:body></w:document>");

    /// <summary>A package with the given document.xml and optional styles.xml / theme XML, written as raw parts.</summary>
    public static byte[] CreateRaw(string documentXml, string? stylesXml = null, string? themeXml = null, string? numberingXml = null)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            Feed(main, documentXml);
            if (stylesXml is not null)
            {
                Feed(main.AddNewPart<StyleDefinitionsPart>(), stylesXml);
            }

            if (themeXml is not null)
            {
                Feed(main.AddNewPart<ThemePart>(), themeXml);
            }

            if (numberingXml is not null)
            {
                Feed(main.AddNewPart<NumberingDefinitionsPart>(), numberingXml);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A minimal theme with the given heading / body fonts and a colour scheme (not a complete theme: fmtScheme is empty).</summary>
    public static string ThemeXml(string major, string minor) =>
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Test\"><a:themeElements>"
        + "<a:clrScheme name=\"Test\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>"
        + "<a:dk2><a:srgbClr val=\"1F497D\"/></a:dk2><a:lt2><a:srgbClr val=\"EEECE1\"/></a:lt2><a:accent1><a:srgbClr val=\"4F81BD\"/></a:accent1>"
        + "<a:accent2><a:srgbClr val=\"C0504D\"/></a:accent2><a:accent3><a:srgbClr val=\"9BBB59\"/></a:accent3><a:accent4><a:srgbClr val=\"8064A2\"/></a:accent4>"
        + "<a:accent5><a:srgbClr val=\"4BACC6\"/></a:accent5><a:accent6><a:srgbClr val=\"F79646\"/></a:accent6><a:hlink><a:srgbClr val=\"0000FF\"/></a:hlink>"
        + "<a:folHlink><a:srgbClr val=\"800080\"/></a:folHlink></a:clrScheme>"
        + $"<a:fontScheme name=\"Test\"><a:majorFont><a:latin typeface=\"{major}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>"
        + $"<a:minorFont><a:latin typeface=\"{minor}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>"
        + "<a:fmtScheme name=\"Test\"><a:fillStyleLst/><a:lnStyleLst/><a:effectStyleLst/><a:bgFillStyleLst/></a:fmtScheme>"
        + "</a:themeElements></a:theme>";

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create), new System.Text.UTF8Encoding(false));
        writer.Write(xml);
    }

    private static Paragraph ListItem(string text, int numberingId, int level) => new(
        new ParagraphProperties(
            new ParagraphStyleId { Val = "ListParagraph" },
            new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = numberingId })),
        new Run(new Text(text)));

    private static Styles CreateStyles() => new(
        new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { AsciiTheme = ThemeFontValues.MinorHighAnsi, HighAnsiTheme = ThemeFontValues.MinorHighAnsi },
                new FontSize { Val = "22" },
                new Languages { Val = "de-DE" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "160", Line = "259", LineRule = LineSpacingRuleValues.Auto }))),
        new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
        new Style(
            new StyleName { Val = "heading 1" },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "0" }, new OutlineLevel { Val = 0 }),
            new StyleRunProperties(new RunFonts { AsciiTheme = ThemeFontValues.MajorHighAnsi, HighAnsiTheme = ThemeFontValues.MajorHighAnsi }, new Color { Val = "2F5496" }, new FontSize { Val = "32" }))
        { Type = StyleValues.Paragraph, StyleId = "Heading1" },
        new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new Indentation { Left = "720" }, new ContextualSpacing()))
        { Type = StyleValues.Paragraph, StyleId = "ListParagraph" },
        new Style(new StyleName { Val = "Default Paragraph Font" }, new UIPriority { Val = 1 }, new SemiHidden(), new UnhideWhenUsed())
        { Type = StyleValues.Character, StyleId = "DefaultParagraphFont", Default = true },
        new Style(new StyleName { Val = "Betont" }, new BasedOn { Val = "DefaultParagraphFont" }, new StyleRunProperties(new Italic()))
        { Type = StyleValues.Character, StyleId = "Betont", CustomStyle = true },
        new Style(new StyleName { Val = "annotation reference" }, new BasedOn { Val = "DefaultParagraphFont" }, new StyleRunProperties(new FontSize { Val = "16" }))
        { Type = StyleValues.Character, StyleId = "CommentReference" },
        new Style(new StyleName { Val = "Table Grid" }, new UIPriority { Val = 39 },
            new StyleTableProperties(new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 })))
        { Type = StyleValues.Table, StyleId = "TableGrid" });

    private static Numbering CreateNumbering() => new(
        new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0, TemplateCode = "0407000F" },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.LowerLetter },
                new LevelText { Val = "%2)" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Tabs(new TabStop { Val = TabStopValues.Number, Position = 1440 }), new Indentation { Left = "1440", Hanging = "360" }),
                new NumberingSymbolRunProperties(new Bold()))
            { LevelIndex = 1, TemplateCode = "04070019", Tentative = true })
        { AbstractNumberId = 0 },
        new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 },
        new NumberingInstance(new AbstractNumId { Val = 0 }, new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 }) { NumberID = 2 });

    private static Table CreateTable() => new(
        new TableProperties(new TableStyle { Val = "TableGrid" }, new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto }),
        new TableGrid(new GridColumn { Width = "2000" }, new GridColumn { Width = "2000" }),
        new TableRow(Cell("A1"), Cell("B1")),
        new TableRow(Cell("A2"), Cell("B2")));

    private static TableCell Cell(string text) => new(
        new TableCellProperties(new TableCellWidth { Width = "2000", Type = TableWidthUnitValues.Dxa }),
        new Paragraph(new Run(new Text(text))));

    private static Drawing CreateDrawing(string imageId) => new(
        new Dw.Inline(
            new Dw.Extent { Cx = 952500, Cy = 476250 },
            new Dw.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Dw.DocProperties { Id = 1, Name = "Bild 1" },
            new Dw.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new Pic.Picture(
                    new Pic.NonVisualPictureProperties(
                        new Pic.NonVisualDrawingProperties { Id = 0, Name = "bild.png" },
                        new Pic.NonVisualPictureDrawingProperties()),
                    new Pic.BlipFill(new A.Blip { Embed = imageId }, new A.Stretch(new A.FillRectangle())),
                    new Pic.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 952500, Cy = 476250 }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });
}

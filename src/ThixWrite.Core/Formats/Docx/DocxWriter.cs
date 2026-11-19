using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Writes the model as .docx. A document read from a docx is written INTO A COPY OF ITS ORIGINAL PACKAGE: only the body of
/// document.xml, styles.xml and numbering.xml are replaced (their root elements with namespace declarations and
/// mc:Ignorable stay) and the default tab stop is patched in settings.xml – headers, footers, images, footnotes, comments,
/// the theme, custom XML and every relationship id inside preserved XML stay valid. A new document gets a fresh, minimal
/// package (document, styles, numbering when there are lists, settings, core properties).
/// </summary>
public static class DocxWriter
{
    public static void Write(Document document, Stream stream)
    {
        using var buffer = document.Package?.Open() ?? new MemoryStream();
        if (document.Package is null)
        {
            CreatePackage(document, buffer);
        }
        else
        {
            using var package = WordprocessingDocument.Open(buffer, true);
            Fill(document, package);
        }

        buffer.Position = 0;
        buffer.CopyTo(stream);
    }

    private static void CreatePackage(Document document, MemoryStream buffer)
    {
        using var package = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        main.AddNewPart<StyleDefinitionsPart>().Styles = new W.Styles();
        main.AddNewPart<DocumentSettingsPart>().Settings = DocxStyles.CreateSettings(document.Settings);
        package.PackageProperties.Created = DateTime.UtcNow;
        package.PackageProperties.Modified = DateTime.UtcNow;
        Fill(document, package);
    }

    private static void Fill(Document document, WordprocessingDocument package)
    {
        var main = package.MainDocumentPart ?? package.AddMainDocumentPart();
        main.Document ??= new W.Document();
        var root = main.Document;
        XmlFragments.DeclareBasicNamespaces(root);
        var context = new WriteContext(main);
        root.Body = WriteBody(document, context);
        XmlFragments.RemoveRedundantDeclarations(root);
        root.Save();

        var stylesPart = main.StyleDefinitionsPart;
        if (stylesPart is null)
        {
            stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new W.Styles();
        }

        var styles = stylesPart.Styles ?? (stylesPart.Styles = new W.Styles());
        XmlFragments.DeclareBasicNamespaces(styles);
        DocxStyles.WriteStyles(document.Styles, styles);
        XmlFragments.RemoveRedundantDeclarations(styles);
        styles.Save();

        WriteNumbering(document, main);

        if (main.DocumentSettingsPart?.Settings is { } settings)
        {
            DocxStyles.WriteSettings(document.Settings, settings);
            settings.Save();
        }
    }

    private static void WriteNumbering(Document document, MainDocumentPart main)
    {
        var part = main.NumberingDefinitionsPart;
        if (part is null && document.Numbering.IsEmpty)
        {
            return;
        }

        if (part is null)
        {
            part = main.AddNewPart<NumberingDefinitionsPart>();
            part.Numbering = new W.Numbering();
        }

        var numbering = part.Numbering ?? (part.Numbering = new W.Numbering());
        XmlFragments.DeclareBasicNamespaces(numbering);
        DocxStyles.WriteNumbering(document.Numbering, numbering);
        XmlFragments.RemoveRedundantDeclarations(numbering);
        numbering.Save();
    }

    private static W.Body WriteBody(Document document, WriteContext context)
    {
        var body = new W.Body();
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            switch (document.Blocks[index])
            {
                case Paragraph { Cell: not null }:
                    var span = TableSpan.At(document, index)!;
                    body.AppendChild(WriteTable(document, span, context));
                    index = span.End;
                    break;
                case Paragraph paragraph:
                    body.AppendChild(WriteParagraph(paragraph, context));
                    break;
                case OpaqueBlock opaque:
                    foreach (var element in XmlFragments.Parse<W.Body>(opaque.Xml))
                    {
                        body.AppendChild(element);
                    }

                    break;
            }
        }

        body.AppendChild(DocxPropertyWriter.WriteSection(document.FinalSection));
        return body;
    }

    /// <summary>A table: w:tblPr (always, as the schema wants), w:tblGrid, the rows and cells with the cells' paragraphs.</summary>
    private static W.Table WriteTable(Document document, TableSpan span, WriteContext context)
    {
        var table = span.Table;
        var element = new W.Table();
        element.AppendChild(DocxTables.WriteTable(table.Properties, new W.TableProperties()) ?? new W.TableProperties());
        var grid = table.Grid.Count > 0 ? table.Grid : new EquatableArray<double>(Enumerable.Repeat(72.0, table.ColumnCount));
        element.AppendChild(DocxTables.WriteGrid(grid));
        for (var row = 0; row < table.RowCount; row++)
        {
            var rowElement = new W.TableRow();
            var (exceptions, properties) = DocxTables.WriteRow(table.Rows[row].Properties);
            if (exceptions is not null)
            {
                rowElement.AppendChild(exceptions);
            }

            if (properties is not null)
            {
                rowElement.AppendChild(properties);
            }

            for (var cell = 0; cell < table.Rows[row].CellCount; cell++)
            {
                var cellElement = new W.TableCell();
                if (DocxTables.WriteCell(table.CellAt(row, cell)) is { } cellProperties)
                {
                    cellElement.AppendChild(cellProperties);
                }

                var (first, last) = span.BlocksOf(row, cell);
                for (var block = first; first >= 0 && block <= last; block++)
                {
                    cellElement.AppendChild(WriteParagraph((Paragraph)document.Blocks[block], context));
                }

                if (first < 0)
                {
                    cellElement.AppendChild(new W.Paragraph());
                }

                rowElement.AppendChild(cellElement);
            }

            element.AppendChild(rowElement);
        }

        return element;
    }

    private static W.Paragraph WriteParagraph(Paragraph paragraph, WriteContext context)
    {
        var element = new W.Paragraph();
        if (DocxPropertyWriter.WriteParagraph(paragraph.Properties, new W.ParagraphProperties(), paragraph.MarkProperties, paragraph.SectionBreak) is { } properties)
        {
            element.AppendChild(properties);
        }

        OpenXmlCompositeElement container = element;
        Hyperlink? currentLink = null;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline.Link != currentLink)
            {
                container = element;
                currentLink = inline.Link;
                if (currentLink is not null && context.CreateHyperlink(currentLink) is { } hyperlink)
                {
                    element.AppendChild(hyperlink);
                    container = hyperlink;
                }
            }

            foreach (var child in WriteInline(inline))
            {
                container.AppendChild(child);
            }
        }

        return element;
    }

    private static IEnumerable<OpenXmlElement> WriteInline(Inline inline)
    {
        switch (inline)
        {
            case TextRun run:
                yield return WriteRun(run);
                break;
            case OpaqueInline { IsRunContent: true } opaque:
                var element = NewRun(opaque.Properties);
                foreach (var child in XmlFragments.Parse<W.Run>(opaque.Xml))
                {
                    element.AppendChild(child);
                }

                yield return element;
                break;
            case OpaqueInline opaque:
                foreach (var child in XmlFragments.Parse<W.Paragraph>(opaque.Xml))
                {
                    yield return child;
                }

                break;
        }
    }

    private static W.Run WriteRun(TextRun run)
    {
        var element = NewRun(run.Properties);
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length == 0)
            {
                return;
            }

            var value = text.ToString();
            var needsPreserve = value[0] == ' ' || value[^1] == ' ' || value.Contains("  ", StringComparison.Ordinal);
            element.AppendChild(new W.Text(value) { Space = needsPreserve ? SpaceProcessingModeValues.Preserve : null });
            text.Clear();
        }

        void Add(OpenXmlElement child)
        {
            Flush();
            element.AppendChild(child);
        }

        var source = run.Text;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsSurrogate(character))
            {
                if (char.IsHighSurrogate(character) && index + 1 < source.Length && char.IsLowSurrogate(source[index + 1]))
                {
                    text.Append(character).Append(source[index + 1]);
                    index++;
                }

                continue;
            }

            switch (character)
            {
                case SpecialCharacters.Tab:
                    Add(new W.TabChar());
                    break;
                case SpecialCharacters.LineBreak or '\n' or '\r':
                    Add(new W.Break());
                    break;
                case SpecialCharacters.PageBreak:
                    Add(new W.Break { Type = W.BreakValues.Page });
                    break;
                case SpecialCharacters.SoftHyphen:
                    Add(new W.SoftHyphen());
                    break;
                case SpecialCharacters.NonBreakingHyphen:
                    Add(new W.NoBreakHyphen());
                    break;
                case SpecialCharacters.Object:
                    break;
                default:
                    if (IsXmlCharacter(character))
                    {
                        text.Append(character);
                    }

                    break;
            }
        }

        Flush();
        return element;
    }

    private static W.Run NewRun(CharacterProperties properties)
    {
        var run = new W.Run();
        if (DocxPropertyWriter.WriteCharacter(properties, new W.RunProperties()) is { } runProperties)
        {
            run.AppendChild(runProperties);
        }

        return run;
    }

    /// <summary>XML 1.0 characters outside the surrogate range (pairs are handled by the caller).</summary>
    private static bool IsXmlCharacter(char character) => character >= ' ' && character < (char)0xFFFE;

    private sealed class WriteContext(MainDocumentPart main)
    {
        private readonly Dictionary<string, string> _relationships = main.HyperlinkRelationships
            .GroupBy(relationship => relationship.Uri.OriginalString)
            .ToDictionary(group => group.Key, group => group.First().Id);

        public W.Hyperlink? CreateHyperlink(Hyperlink link)
        {
            string? id = null;
            if (!string.IsNullOrEmpty(link.Uri))
            {
                id = RelationshipFor(link.Uri);
            }

            if (id is null && string.IsNullOrEmpty(link.Anchor))
            {
                return null;
            }

            var hyperlink = DocxPropertyWriter.Element<W.Hyperlink>(
                ("tooltip", link.Tooltip),
                ("history", link.History ? "1" : null),
                ("anchor", link.Anchor));
            if (id is not null)
            {
                hyperlink.Id = id;
            }

            return hyperlink;
        }

        private string? RelationshipFor(string uri)
        {
            if (_relationships.TryGetValue(uri, out var id))
            {
                return id;
            }

            if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed))
            {
                return null;
            }

            var relationship = main.AddHyperlinkRelationship(parsed, parsed.IsAbsoluteUri);
            _relationships[uri] = relationship.Id;
            return relationship.Id;
        }
    }
}

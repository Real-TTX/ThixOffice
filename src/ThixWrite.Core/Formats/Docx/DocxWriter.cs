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
        var context = new WriteContext(main, main, document);
        context.Comments.Prepare(document);
        if (root.DocumentBackground?.Color?.Value is var color && !string.Equals(color ?? "FFFFFF", document.PageColor ?? "FFFFFF", StringComparison.OrdinalIgnoreCase))
        {
            // "Seitenfarbe" changed in ThixWrite (else the w:background stays as read, theme colours and all).
            root.DocumentBackground = document.PageColor is { } pageColor ? new W.DocumentBackground { Color = pageColor } : null;
            if (document.PageColor is not null && main.DocumentSettingsPart?.Settings is { } settingsElement && settingsElement.GetFirstChild<W.DisplayBackgroundShape>() is null)
            {
                XmlFragments.Add(settingsElement, new W.DisplayBackgroundShape());
            }
        }

        root.Body = WriteBody(document, context);
        XmlFragments.RemoveRedundantDeclarations(root);
        root.Save();
        DocxNotes.Write(document, main, context.Notes, (blocks, part) => WriteBlocks(document, blocks, new WriteContext(main, part, document)));
        DocxComments.Write(main, context.Comments, (blocks, part) => WriteBlocks(document, blocks, new WriteContext(main, part, document)));

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
        var nesting = new ControlNesting();
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            switch (document.Blocks[index])
            {
                case Paragraph { Cell: not null }:
                    var span = TableSpan.At(document, index)!;
                    nesting.Add(WriteTable(document, span, context), null);
                    index = span.End;
                    break;
                case Paragraph paragraph:
                    nesting.AddAll(WriteParagraphElements(paragraph, context), paragraph);
                    break;
                case OpaqueBlock opaque:
                    foreach (var element in XmlFragments.Parse<W.Body>(opaque.Xml))
                    {
                        nesting.Add(element, null);
                    }

                    break;
            }
        }

        foreach (var element in nesting.Elements)
        {
            body.AppendChild(element);
        }

        body.AppendChild(DocxPropertyWriter.WriteSection(document.FinalSection, context.ReferencesOf));
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
                    foreach (var written in WriteParagraphElements((Paragraph)document.Blocks[block], context))
                    {
                        cellElement.AppendChild(written);
                    }
                }

                if (first < 0 || cellElement.LastChild is W.Table)
                {
                    cellElement.AppendChild(new W.Paragraph()); // a cell ends with a paragraph
                }

                rowElement.AppendChild(cellElement);
            }

            element.AppendChild(rowElement);
        }

        return element;
    }

    /// <summary>
    /// A paragraph as its elements: the w:p, or – when it carries nested tables (<see cref="OpaqueInline.IsBlockContent"/>) – those tables
    /// as they were read with what else it holds in paragraphs around them.
    /// </summary>
    private static IEnumerable<OpenXmlElement> WriteParagraphElements(Paragraph paragraph, WriteContext context)
    {
        if (!paragraph.Inlines.Any(inline => inline is OpaqueInline { IsBlockContent: true }))
        {
            yield return WriteParagraph(paragraph, context);
            yield break;
        }

        var rest = new List<Inline>();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is not OpaqueInline { IsBlockContent: true } block)
            {
                rest.Add(inline);
                continue;
            }

            if (rest.Count > 0)
            {
                yield return WriteParagraph(paragraph.WithInlines(rest).WithSectionBreak(null), context);
                rest.Clear();
            }

            foreach (var element in XmlFragments.Parse<W.Body>(block.Xml))
            {
                yield return element;
            }
        }

        if (rest.Count > 0 || paragraph.SectionBreak is not null)
        {
            yield return WriteParagraph(paragraph.WithInlines(rest), context);
        }
    }

    private static W.Paragraph WriteParagraph(Paragraph paragraph, WriteContext context)
    {
        var element = new W.Paragraph();
        if (DocxPropertyWriter.WriteParagraph(paragraph.Properties, new W.ParagraphProperties(), paragraph.MarkProperties, paragraph.SectionBreak, context.ReferencesOf) is { } properties)
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

            foreach (var child in WriteInline(inline, context))
            {
                container.AppendChild(child);
            }
        }

        return element;
    }

    private static IEnumerable<OpenXmlElement> WriteInline(Inline inline, WriteContext context)
    {
        switch (inline)
        {
            case TextRun run:
                yield return WriteRun(run);
                break;
            case FieldInline field:
                var result = NewRun(field.Properties);
                result.AppendChild(new W.Text(field.Result) { Space = SpaceProcessingModeValues.Preserve });
                yield return DocxFields.Write(field, result);
                break;
            case NoteInline note:
                var noteRun = NewRun(note.Properties);
                var id = context.NoteId(note);
                noteRun.AppendChild<OpenXmlElement>(note.Kind == NoteKind.Footnote ? new W.FootnoteReference { Id = id } : new W.EndnoteReference { Id = id });
                yield return noteRun;
                break;
            case CommentAnchorInline:
                if (context.Comments.NextAnchor() is { } anchorId)
                {
                    yield return new W.CommentRangeStart { Id = anchorId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                }

                break;
            case CommentInline comment:
                if (context.Comments.NextReference() is { } commentId)
                {
                    var text = commentId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    yield return new W.CommentRangeEnd { Id = text };
                    var reference = NewRun(comment.Properties);
                    reference.AppendChild(new W.CommentReference { Id = text });
                    yield return reference;
                }

                break;
            case TocMarkInline { IsEnd: false } toc:
                yield return FieldCharRun(toc.Properties, W.FieldCharValues.Begin);
                var instruction = NewRun(toc.Properties);
                instruction.AppendChild(new W.FieldCode(toc.Instruction) { Space = SpaceProcessingModeValues.Preserve });
                yield return instruction;
                yield return FieldCharRun(toc.Properties, W.FieldCharValues.Separate);
                break;
            case TocMarkInline toc:
                yield return FieldCharRun(toc.Properties, W.FieldCharValues.End);
                break;
            case ContentControlMarkInline:
                break; // the w:sdt around the paragraphs is written by ControlNesting
            case NoteMarkInline mark:
                var markRun = NewRun(mark.Properties);
                markRun.AppendChild<OpenXmlElement>(mark.Kind == NoteKind.Footnote ? new W.FootnoteReferenceMark() : new W.EndnoteReferenceMark());
                yield return markRun;
                break;
            case ImageInline image:
                var pictureRun = NewRun(image.Properties);
                foreach (var child in DocxPictures.Write(image, context.Pictures))
                {
                    pictureRun.AppendChild(child);
                }

                yield return pictureRun;
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

    private static W.Run FieldCharRun(CharacterProperties properties, W.FieldCharValues type)
    {
        var run = NewRun(properties);
        run.AppendChild(new W.FieldChar { FieldCharType = type });
        return run;
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

    /// <summary>The blocks of a story (body, header, footer) as WordprocessingML elements.</summary>
    private static IEnumerable<OpenXmlElement> WriteBlocks(Document document, IReadOnlyList<Block> blocks, WriteContext context)
    {
        // A story that is not the body gets a document of its own (same styles), so that tables find their blocks.
        var story = new Document(blocks, document.Styles, document.Numbering, document.FinalSection, document.Theme, document.Settings);
        var nesting = new ControlNesting();
        for (var index = 0; index < story.Blocks.Count; index++)
        {
            switch (story.Blocks[index])
            {
                case Paragraph { Cell: not null }:
                    var span = TableSpan.At(story, index)!;
                    nesting.Add(WriteTable(story, span, context), null);
                    index = span.End;
                    break;
                case Paragraph paragraph:
                    nesting.AddAll(WriteParagraphElements(paragraph.WithSectionBreak(null), context), paragraph);
                    break;
                case OpaqueBlock opaque:
                    foreach (var element in XmlFragments.Parse<W.Body>(opaque.Xml))
                    {
                        nesting.Add(element, null);
                    }

                    break;
            }
        }

        return nesting.Elements;
    }

    /// <summary>
    /// Puts the elements of a story into the block-level content controls the marks of their paragraphs open and close
    /// (<see cref="ContentControlMarkInline"/>): a start mark opens a w:sdt before its paragraph, an end mark closes the innermost one
    /// after it; unbalanced marks are tolerated (an end without a start is ignored, an open control ends with the story).
    /// </summary>
    private sealed class ControlNesting
    {
        private readonly List<OpenXmlElement> _top = [];
        private readonly Stack<W.SdtContentBlock> _open = new();

        public IReadOnlyList<OpenXmlElement> Elements => _top;

        /// <summary>The elements of one paragraph (several when it carries nested tables: its control marks open before the first, close after the last).</summary>
        public void AddAll(IEnumerable<OpenXmlElement> elements, Paragraph paragraph)
        {
            var list = elements.ToList();
            if (list.Count == 1)
            {
                Add(list[0], paragraph);
                return;
            }

            var marks = paragraph.Inlines.OfType<ContentControlMarkInline>().ToList();
            for (var index = 0; index < list.Count; index++)
            {
                var carried = index == 0 ? marks.Where(mark => !mark.IsEnd) : index == list.Count - 1 ? marks.Where(mark => mark.IsEnd) : [];
                Add(list[index], Paragraph.Empty().WithInlines(carried));
            }
        }

        public void Add(OpenXmlElement element, Paragraph? paragraph)
        {
            var marks = paragraph?.Inlines.OfType<ContentControlMarkInline>().ToList() ?? [];
            foreach (var start in marks.Where(mark => !mark.IsEnd))
            {
                var control = new W.SdtBlock();
                foreach (var properties in XmlFragments.Parse<W.SdtBlock>(start.PropertiesXml))
                {
                    control.AppendChild(properties);
                }

                var content = new W.SdtContentBlock();
                control.AppendChild(content);
                Append(control);
                _open.Push(content);
            }

            Append(element);
            foreach (var _ in marks.Where(mark => mark.IsEnd))
            {
                if (_open.Count > 0)
                {
                    _open.Pop();
                }
            }
        }

        private void Append(OpenXmlElement element)
        {
            if (_open.Count > 0)
            {
                _open.Peek().AppendChild(element);
            }
            else
            {
                _top.Add(element);
            }
        }
    }

    private sealed class WriteContext(MainDocumentPart main, OpenXmlPart part, Document? document = null)
    {
        private readonly Dictionary<Story, string> _stories = new(ReferenceEqualityComparer.Instance);

        public DocxPictures.PictureContext Pictures { get; } = new(part);

        public Document? Document { get; set; } = document;

        /// <summary>The notes written so far (their ids allocated), in order of appearance.</summary>
        public DocxNotes.Allocation Notes { get; } = new(main, document);

        public long NoteId(NoteInline note) => Notes.Allocate(note);

        /// <summary>The comments of the body (their ids paired before the body is written).</summary>
        public DocxComments.Allocation Comments { get; } = new();

        /// <summary>The header and footer references of a section, their parts written (each story once, into the part it was read from).</summary>
        public IEnumerable<OpenXmlElement> ReferencesOf(SectionProperties section)
        {
            var references = new List<OpenXmlElement>();
            foreach (var (set, footer) in new[] { (section.Headers, false), (section.Footers, true) })
            {
                foreach (var kind in new[] { HeaderFooterKind.Default, HeaderFooterKind.First, HeaderFooterKind.Even })
                {
                    if (set[kind] is not { } story || Document is null)
                    {
                        continue;
                    }

                    var id = WriteStory(story, footer);
                    var type = kind switch { HeaderFooterKind.First => "first", HeaderFooterKind.Even => "even", _ => "default" };
                    OpenXmlElement reference = footer ? new W.FooterReference() : new W.HeaderReference();
                    reference.SetAttribute(new OpenXmlAttribute("w", "type", DocxValues.WordNamespace, type));
                    reference.SetAttribute(new OpenXmlAttribute("r", "id", DocxValues.RelationshipNamespace, id));
                    references.Add(reference);
                }
            }

            return references;
        }

        private string WriteStory(Story story, bool footer)
        {
            if (_stories.TryGetValue(story, out var known))
            {
                return known;
            }

            OpenXmlPart? existing = null;
            if (story.SourcePart is { } source)
            {
                try
                {
                    existing = main.GetPartById(source);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            OpenXmlPart target = footer
                ? existing as FooterPart ?? main.AddNewPart<FooterPart>()
                : existing as HeaderPart ?? main.AddNewPart<HeaderPart>();
            OpenXmlCompositeElement root = footer ? new W.Footer() : new W.Header();
            XmlFragments.DeclareBasicNamespaces(root);
            var context = new WriteContext(main, target, Document);
            foreach (var element in WriteBlocks(Document!, story.Blocks, context))
            {
                root.AppendChild(element);
            }

            XmlFragments.RemoveRedundantDeclarations(root);
            if (target is FooterPart footerPart)
            {
                footerPart.Footer = (W.Footer)root;
                footerPart.Footer.Save();
            }
            else
            {
                var headerPart = (HeaderPart)target;
                headerPart.Header = (W.Header)root;
                headerPart.Header.Save();
            }

            var id = main.GetIdOfPart(target);
            _stories[story] = id;
            return id;
        }

        private readonly Dictionary<string, string> _relationships = part.HyperlinkRelationships
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

            var relationship = part.AddHyperlinkRelationship(parsed, parsed.IsAbsoluteUri);
            _relationships[uri] = relationship.Id;
            return relationship.Id;
        }
    }
}

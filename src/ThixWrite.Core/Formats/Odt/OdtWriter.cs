using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;
using static ThixWrite.Core.Formats.Odt.OdtXml;

namespace ThixWrite.Core.Formats.Odt;

/// <summary>
/// Writes the model as OpenDocument Text (.odt): a ZIP with mimetype (first, stored), META-INF/manifest.xml, styles.xml (the
/// styles as named styles, the document defaults, the page layout and the master page with header and footer), content.xml
/// (automatic styles for direct formatting, the text: paragraphs and headings, lists, tables, pictures in Pictures/, links,
/// footnotes and endnotes, page number / page count / date fields, bookmarks, comments as office:annotation) and meta.xml.
/// What ODF has no place for in ThixWrite's model is left out: content preserved from a Word file (the save asks first).
/// </summary>
public static partial class OdtWriter
{
    public static void Write(Document document, Stream stream)
    {
        var writer = new Writer(document);
        var package = new OdfPackageWriter(MimeType);
        package.AddXml("content.xml", writer.Content());
        package.AddXml("styles.xml", writer.Styles());
        package.AddXml("meta.xml", OdfPackageWriter.Meta("ThixWrite"));
        foreach (var (path, bytes) in writer.Pictures)
        {
            package.AddBytes(path, bytes, OdfPackageWriter.MediaTypeOf(Path.GetExtension(path)));
        }

        package.WriteTo(stream);
    }

    /// <summary>A style name ODF accepts (an NCName): letters, digits, "_", "-", "."; other characters as "_".</summary>
    internal static string StyleName(string id)
    {
        var name = NotName().Replace(id, "_");
        return name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ? "S" + name : name;
    }

    [GeneratedRegex("[^A-Za-z0-9_.\\-]")]
    private static partial Regex NotName();

    private sealed class Writer(Document document)
    {
        private readonly Dictionary<string, string> _paragraphStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _textStyles = new(StringComparer.Ordinal);
        private readonly List<XElement> _automatic = [];
        private readonly Dictionary<int, string> _listStyles = [];
        private readonly Dictionary<string, string> _pictures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Id, Comment Comment)> _openComments = new(StringComparer.Ordinal);
        private readonly StyleResolver _resolver = new(document);
        private int _tables;
        private int _frames;
        private int _notes;
        private int _annotations;

        public List<(string Path, byte[] Bytes)> Pictures { get; } = [];

        // ------------------------------------------------------------ content.xml

        public XDocument Content()
        {
            var text = new XElement(OfficeNs + "text");
            WriteBlocks(document, text, isBody: true);
            MarkContentStyles();
            var root = new XElement(OfficeNs + "document-content", Declarations(), new XAttribute(OfficeNs + "version", "1.3"),
                new XElement(OfficeNs + "automatic-styles", _automatic.Concat(_listStyles.Select(pair => ListStyle(pair.Key, pair.Value)))),
                new XElement(OfficeNs + "body", text));
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        }

        /// <summary>The blocks of a document (or a story's document) into <paramref name="parent"/>: paragraphs, lists, tables.</summary>
        private void WriteBlocks(Document source, XElement parent, bool isBody)
        {
            var lists = new ListWriter(this, parent);
            for (var index = 0; index < source.Blocks.Count; index++)
            {
                switch (source.Blocks[index])
                {
                    case Paragraph { Cell: not null }:
                        lists.Close();
                        var span = TableSpan.At(source, index)!;
                        parent.Add(WriteTable(source, span));
                        index = span.End;
                        break;
                    case Paragraph paragraph when paragraph.Properties.Numbering is { IsNone: false } numbering && source.Numbering.Instance(numbering.NumberingId) is { } instance:
                        lists.Add(instance, numbering.Level, WriteParagraph(source, paragraph));
                        break;
                    case Paragraph paragraph:
                        lists.Close();
                        foreach (var element in WriteParagraphs(source, paragraph))
                        {
                            parent.Add(element);
                        }

                        break;
                    default:
                        lists.Close(); // preserved Word content has no place in ODF
                        break;
                }
            }

            lists.Close();
        }

        /// <summary>A paragraph – split where it has page breaks (ODF breaks before a paragraph, not inside one).</summary>
        private IEnumerable<XElement> WriteParagraphs(Document source, Paragraph paragraph)
        {
            if (!paragraph.Text.Contains(SpecialCharacters.PageBreak))
            {
                yield return WriteParagraph(source, paragraph);
                yield break;
            }

            var start = 0;
            var first = true;
            while (true)
            {
                var end = paragraph.Text.IndexOf(SpecialCharacters.PageBreak, start);
                var piece = paragraph.WithInlines(paragraph.Slice(start, end < 0 ? paragraph.Length : end));
                if (!first)
                {
                    piece = piece.WithProperties(piece.Properties with { PageBreakBefore = true });
                }

                yield return WriteParagraph(source, piece);
                if (end < 0)
                {
                    yield break;
                }

                start = end + 1;
                first = false;
            }
        }

        private XElement WriteParagraph(Document source, Paragraph paragraph)
        {
            var format = _resolver.ResolveParagraph(paragraph);
            var isHeading = format.OutlineLevel is >= 0 and < 9 && paragraph.Cell is null;
            var element = new XElement(TextNs + (isHeading ? "h" : "p"), new XAttribute(TextNs + "style-name", ParagraphStyleOf(paragraph)));
            if (isHeading)
            {
                element.Add(new XAttribute(TextNs + "outline-level", format.OutlineLevel + 1));
            }

            XElement container = element;
            Hyperlink? link = null;
            foreach (var inline in paragraph.Inlines)
            {
                if (inline.Link != link)
                {
                    link = inline.Link;
                    container = element;
                    if (link is not null && (link.Uri is { Length: > 0 } || link.Anchor is not null))
                    {
                        container = new XElement(TextNs + "a", new XAttribute(XLinkNs + "type", "simple"),
                            new XAttribute(XLinkNs + "href", link.Uri is { Length: > 0 } uri ? uri : "#" + link.Anchor));
                        element.Add(container);
                    }
                }

                WriteInline(source, inline, container);
            }

            return element;
        }

        private void WriteInline(Document source, Inline inline, XElement parent)
        {
            switch (inline)
            {
                case TextRun run:
                    var target = parent;
                    if (TextStyleOf(run.Properties) is { } style)
                    {
                        target = new XElement(TextNs + "span", new XAttribute(TextNs + "style-name", style));
                        parent.Add(target);
                    }

                    WriteText(run.Text, target);
                    break;
                case FieldInline field:
                    var fieldTarget = Spanned(parent, field.Properties);
                    fieldTarget.Add(field.Kind switch
                    {
                        FieldKind.Page => new XElement(TextNs + "page-number", new XAttribute(TextNs + "select-page", "current"), field.Result),
                        FieldKind.NumPages => new XElement(TextNs + "page-count", field.Result),
                        _ => new XElement(TextNs + "date", new XAttribute(StyleNs + "data-style-name", "N37"), field.Result),
                    });
                    break;
                case ImageInline image:
                    Spanned(parent, image.Properties).Add(WriteFrame(image));
                    break;
                case NoteInline note:
                    var label = (++_notes).ToString(CultureInfo.InvariantCulture);
                    var body = new XElement(TextNs + "note-body");
                    var story = new Document(note.Content.Blocks.Select(block => block is Paragraph paragraph ? paragraph.WithInlines(paragraph.Inlines.Where(item => item is not NoteMarkInline)) : block),
                        source.Styles, source.Numbering, source.FinalSection, source.Theme, source.Settings);
                    WriteBlocks(story, body, isBody: false);
                    Spanned(parent, note.Properties).Add(new XElement(TextNs + "note",
                        new XAttribute(TextNs + "id", "ftn" + label),
                        new XAttribute(TextNs + "note-class", note.Kind == NoteKind.Endnote ? "endnote" : "footnote"),
                        new XElement(TextNs + "note-citation", label),
                        body));
                    break;
                case OpaqueInline { Kind: PreservedKind.Bookmark } bookmark when BookmarkName().Match(bookmark.Xml) is { Success: true } name:
                    parent.Add(new XElement(TextNs + (bookmark.Xml.StartsWith("<w:bookmarkEnd", StringComparison.Ordinal) ? "bookmark-end" : "bookmark-start"), new XAttribute(TextNs + "name", name.Groups[1].Value)));
                    break;
                case OpaqueInline { Kind: PreservedKind.Bookmark, Xml: var xml } end when xml.StartsWith("<w:bookmarkEnd", StringComparison.Ordinal):
                    break; // an end without a name: matched by the start's name in ODF only when known
                case OpaqueInline opaque when opaque.DisplayText.Length > 0:
                    WriteText(opaque.DisplayText, Spanned(parent, opaque.Properties)); // a field result or tracked insertion: its text
                    break;
                case CommentAnchorInline anchor:
                    // The comment itself is written where it starts (ODF: office:annotation … office:annotation-end).
                    var placed = Model.Comments.All(source).FirstOrDefault(item => item.Comment.Id == anchor.Id);
                    if (placed is not null)
                    {
                        var name = "__Annotation__" + (++_annotations).ToString(CultureInfo.InvariantCulture);
                        _openComments[anchor.Id] = (name, placed.Comment);
                        parent.Add(Annotation(source, placed.Comment, name));
                    }

                    break;
                case CommentInline comment:
                    if (_openComments.Remove(comment.Comment.Id, out var open))
                    {
                        parent.Add(new XElement(OfficeNs + "annotation-end", new XAttribute(OfficeNs + "name", open.Id)));
                    }
                    else
                    {
                        parent.Add(Annotation(source, comment.Comment, null));
                    }

                    break;
            }
        }

        private XElement Annotation(Document source, Comment comment, string? name)
        {
            var element = new XElement(OfficeNs + "annotation");
            if (name is not null)
            {
                element.Add(new XAttribute(OfficeNs + "name", name));
            }

            element.Add(new XElement(DcNs + "creator", comment.Author));
            if (comment.Date is { } date)
            {
                element.Add(new XElement(DcNs + "date", date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
            }

            if (comment.Initials.Length > 0)
            {
                element.Add(new XElement(MetaNs + "creator-initials", comment.Initials));
            }

            foreach (var line in comment.Text.Split('\n'))
            {
                var paragraph = new XElement(TextNs + "p");
                WriteText(line, paragraph);
                element.Add(paragraph);
            }

            return element;
        }

        /// <summary>A span with the formatting of an object (a field, a picture, a note) when it has any, else the parent.</summary>
        private XElement Spanned(XElement parent, CharacterProperties properties)
        {
            if (TextStyleOf(properties) is not { } style)
            {
                return parent;
            }

            var span = new XElement(TextNs + "span", new XAttribute(TextNs + "style-name", style));
            parent.Add(span);
            return span;
        }

        /// <summary>Text with its tabs, line breaks and runs of spaces as ODF writes them (text:tab, text:line-break, text:s).</summary>
        private static void WriteText(string text, XElement parent)
        {
            var builder = new StringBuilder();
            void Flush()
            {
                if (builder.Length > 0)
                {
                    parent.Add(new XText(builder.ToString()));
                    builder.Clear();
                }
            }

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                switch (character)
                {
                    case SpecialCharacters.Tab:
                        Flush();
                        parent.Add(new XElement(TextNs + "tab"));
                        break;
                    case SpecialCharacters.LineBreak:
                        Flush();
                        parent.Add(new XElement(TextNs + "line-break"));
                        break;
                    case SpecialCharacters.PageBreak or SpecialCharacters.Object:
                        break;
                    case ' ' when index == 0 || (index > 0 && text[index - 1] == ' '):
                        var count = 1;
                        while (index + 1 < text.Length && text[index + 1] == ' ')
                        {
                            count++;
                            index++;
                        }

                        Flush();
                        parent.Add(count == 1 ? new XElement(TextNs + "s") : new XElement(TextNs + "s", new XAttribute(TextNs + "c", count)));
                        break;
                    default:
                        if (System.Xml.XmlConvert.IsXmlChar(character) || (char.IsHighSurrogate(character) && index + 1 < text.Length))
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            Flush();
        }

        private XElement WriteFrame(ImageInline image)
        {
            if (!_pictures.TryGetValue(image.Image.Key, out var path))
            {
                path = $"Pictures/{image.Image.Key[..Math.Min(32, image.Image.Key.Length)]}.{image.Image.Extension.TrimStart('.')}";
                _pictures[image.Image.Key] = path;
                Pictures.Add((path, image.Image.Bytes));
            }

            var name = "Bild" + (++_frames).ToString(CultureInfo.InvariantCulture);
            var frame = new XElement(DrawNs + "frame",
                new XAttribute(DrawNs + "name", name),
                new XAttribute(TextNs + "anchor-type", "as-char"),
                new XAttribute(SvgNs + "width", Length(image.Width)),
                new XAttribute(SvgNs + "height", Length(image.Height)),
                new XAttribute(DrawNs + "z-index", _frames),
                new XElement(DrawNs + "image", new XAttribute(XLinkNs + "href", path), new XAttribute(XLinkNs + "type", "simple"),
                    new XAttribute(XLinkNs + "show", "embed"), new XAttribute(XLinkNs + "actuate", "onLoad")));
            if (image.Description is { Length: > 0 } description)
            {
                frame.Add(new XElement(SvgNs + "desc", description));
            }

            return frame;
        }

        // ------------------------------------------------------------ tables

        private XElement WriteTable(Document source, TableSpan span)
        {
            var table = span.Table;
            var name = "Tabelle" + (++_tables).ToString(CultureInfo.InvariantCulture);
            var grid = table.Grid.Count > 0 ? table.Grid.ToList() : Enumerable.Repeat(source.FinalSection.ContentWidth / Math.Max(1, table.ColumnCount), table.ColumnCount).ToList();
            _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table"),
                new XElement(StyleNs + "table-properties",
                    new XAttribute(StyleNs + "width", Centimetres(grid.Sum())),
                    new XAttribute(TableNs + "align", table.Properties.Alignment switch { TableAlignment.Center => "center", TableAlignment.Right => "right", _ => "left" }))));
            var element = new XElement(TableNs + "table", new XAttribute(TableNs + "name", name), new XAttribute(TableNs + "style-name", name));
            for (var column = 0; column < grid.Count; column++)
            {
                var columnStyle = $"{name}.C{column + 1}";
                _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", columnStyle), new XAttribute(StyleNs + "family", "table-column"),
                    new XElement(StyleNs + "table-column-properties", new XAttribute(StyleNs + "column-width", Centimetres(grid[column])))));
                element.Add(new XElement(TableNs + "table-column", new XAttribute(TableNs + "style-name", columnStyle)));
            }

            var resolved = _resolver.ResolveTable(table);
            XElement? header = null;
            for (var row = 0; row < table.RowCount; row++)
            {
                var rowElement = new XElement(TableNs + "table-row");
                for (var cell = 0; cell < table.Rows[row].CellCount; cell++)
                {
                    var properties = table.CellAt(row, cell);
                    var spanned = Math.Max(1, properties.GridSpan);
                    if (properties.VerticalMerge == VerticalMerge.Continue)
                    {
                        for (var covered = 0; covered < spanned; covered++)
                        {
                            rowElement.Add(new XElement(TableNs + "covered-table-cell"));
                        }

                        continue;
                    }

                    var cellElement = new XElement(TableNs + "table-cell", new XAttribute(OfficeNs + "value-type", "string"));
                    if (CellStyle(name, row, cell, properties, resolved, table) is { } cellStyle)
                    {
                        cellElement.Add(new XAttribute(TableNs + "style-name", cellStyle));
                    }

                    if (spanned > 1)
                    {
                        cellElement.Add(new XAttribute(TableNs + "number-columns-spanned", spanned));
                    }

                    if (table.RowSpanOf(row, cell) is > 1 and var rows)
                    {
                        cellElement.Add(new XAttribute(TableNs + "number-rows-spanned", rows));
                    }

                    var (first, last) = span.BlocksOf(row, cell);
                    for (var block = first; first >= 0 && block <= last; block++)
                    {
                        var paragraph = ((Paragraph)source.Blocks[block]).WithCell(null);
                        foreach (var written in WriteParagraphs(source, paragraph))
                        {
                            cellElement.Add(written);
                        }
                    }

                    if (!cellElement.Elements().Any())
                    {
                        cellElement.Add(new XElement(TextNs + "p"));
                    }

                    rowElement.Add(cellElement);
                    for (var covered = 1; covered < spanned; covered++)
                    {
                        rowElement.Add(new XElement(TableNs + "covered-table-cell"));
                    }
                }

                if (table.Rows[row].Properties.IsHeader && (header is not null || row == 0))
                {
                    header ??= new XElement(TableNs + "table-header-rows");
                    header.Add(rowElement);
                    if (row == 0)
                    {
                        element.Add(header);
                    }

                    continue;
                }

                element.Add(rowElement);
            }

            return element;
        }

        private string? CellStyle(string table, int row, int cell, TableCellProperties properties, TableProperties resolved, Table model)
        {
            var borders = (resolved.Borders ?? BorderSet.Empty).Merge(properties.Borders);
            var lastRow = row + model.RowSpanOf(row, cell) - 1 >= model.RowCount - 1;
            var lastColumn = model.GridColumnOf(row, cell) + Math.Max(1, properties.GridSpan) >= model.ColumnCount;
            var top = properties.Borders?.Top ?? (row == 0 ? borders.Top : borders.InsideHorizontal);
            var bottom = properties.Borders?.Bottom ?? (lastRow ? borders.Bottom : borders.InsideHorizontal);
            var left = properties.Borders?.Left ?? (model.GridColumnOf(row, cell) == 0 ? borders.Left : borders.InsideVertical);
            var right = properties.Borders?.Right ?? (lastColumn ? borders.Right : borders.InsideVertical);
            var cellProperties = new XElement(StyleNs + "table-cell-properties");
            foreach (var (side, line) in new[] { ("top", top), ("bottom", bottom), ("left", left), ("right", right) })
            {
                cellProperties.Add(new XAttribute(FoNs + $"border-{side}", line is { IsVisible: true } visible
                    ? $"{Length(Math.Max(0.25, visible.Width))} {(visible.Style == "double" ? "double" : visible.Style is "dotted" ? "dotted" : visible.Style is "dashed" ? "dashed" : "solid")} {Hex(visible.Color is { Length: 6 } color && color != "auto" ? color : "000000")}"
                    : "none"));
            }

            if ((properties.Shading ?? resolved.Shading)?.VisibleFill is { } fill)
            {
                cellProperties.Add(new XAttribute(FoNs + "background-color", Hex(fill)));
            }

            var margins = (resolved.CellMargins ?? CellMargins.WordDefault).Merge(properties.Margins);
            cellProperties.Add(new XAttribute(FoNs + "padding-left", Length(margins.Left ?? 0)), new XAttribute(FoNs + "padding-right", Length(margins.Right ?? 0)),
                new XAttribute(FoNs + "padding-top", Length(margins.Top ?? 0)), new XAttribute(FoNs + "padding-bottom", Length(margins.Bottom ?? 0)));
            if (properties.VerticalAlignment is { } vertical)
            {
                cellProperties.Add(new XAttribute(StyleNs + "vertical-align", vertical switch { CellVerticalAlignment.Center => "middle", CellVerticalAlignment.Bottom => "bottom", _ => "top" }));
            }

            var name = $"{table}.R{row + 1}C{cell + 1}";
            _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table-cell"), cellProperties));
            return name;
        }

        // ------------------------------------------------------------ styles of the text

        /// <summary>The style of a paragraph: its named style, or an automatic style with its direct formatting on top.</summary>
        private string ParagraphStyleOf(Paragraph paragraph)
        {
            var named = StyleName(paragraph.Properties.StyleId ?? document.Styles.DefaultParagraphStyle?.Id ?? BuiltInStyles.Normal);
            var direct = paragraph.Properties with { StyleId = null, Numbering = null, OutlineLevel = null };
            var paragraphXml = OdtXml.ParagraphProperties(direct);
            var markXml = TextProperties(paragraph.MarkProperties with { StyleId = null }, document.Theme);
            if (paragraphXml is null && markXml is null)
            {
                return named;
            }

            var key = named + "|" + paragraphXml + "|" + markXml;
            if (!_paragraphStyles.TryGetValue(key, out var name))
            {
                name = "P" + (_paragraphStyles.Count + 1).ToString(CultureInfo.InvariantCulture);
                _paragraphStyles[key] = name;
                _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "paragraph"),
                    new XAttribute(StyleNs + "parent-style-name", named), paragraphXml, markXml));
            }

            return name;
        }

        /// <summary>The automatic text style of character properties (its character style as parent), or null for none.</summary>
        private string? TextStyleOf(CharacterProperties properties)
        {
            if (properties.IsEmpty)
            {
                return null;
            }

            var xml = TextProperties(properties with { StyleId = null }, document.Theme);
            var parent = properties.StyleId is { } style ? StyleName(style) : null;
            if (xml is null && parent is null)
            {
                return null;
            }

            var key = parent + "|" + xml;
            if (!_textStyles.TryGetValue(key, out var name))
            {
                name = "T" + (_textStyles.Count + 1).ToString(CultureInfo.InvariantCulture);
                _textStyles[key] = name;
                _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "text"),
                    parent is null ? null : new XAttribute(StyleNs + "parent-style-name", parent), xml));
            }

            return name;
        }

        /// <summary>The list style of a list definition (one per definition used).</summary>
        public string ListStyleName(int abstractId)
        {
            if (!_listStyles.TryGetValue(abstractId, out var name))
            {
                name = "L" + (_listStyles.Count + 1).ToString(CultureInfo.InvariantCulture);
                _listStyles[abstractId] = name;
            }

            return name;
        }

        private XElement ListStyle(int abstractId, string name)
        {
            var element = new XElement(TextNs + "list-style", new XAttribute(StyleNs + "name", name));
            var definition = document.Numbering.Definition(abstractId);
            for (var level = 0; level < 9; level++)
            {
                var numbering = definition?.LevelAt(level) ?? BuiltInLists.Create(ListKind.Bullet, 0).LevelAt(level)!;
                var properties = new XElement(StyleNs + "list-level-properties",
                    new XAttribute(TextNs + "list-level-position-and-space-mode", "label-alignment"),
                    new XElement(StyleNs + "list-level-label-alignment",
                        new XAttribute(TextNs + "label-followed-by", numbering.Suffix switch { LabelSuffix.Space => "space", LabelSuffix.Nothing => "nothing", _ => "listtab" }),
                        new XAttribute(TextNs + "list-tab-stop-position", Length(numbering.IndentLeft)),
                        new XAttribute(FoNs + "text-indent", Length(numbering.FirstLineIndent)),
                        new XAttribute(FoNs + "margin-left", Length(numbering.IndentLeft))));
                if (numbering.Format is NumberFormat.Bullet or NumberFormat.None)
                {
                    element.Add(new XElement(TextNs + "list-level-style-bullet", new XAttribute(TextNs + "level", level + 1),
                        new XAttribute(TextNs + "bullet-char", Bullet(numbering.Text)), properties));
                    continue;
                }

                var placeholder = $"%{level + 1}";
                var at = numbering.Text.IndexOf(placeholder, StringComparison.Ordinal);
                var prefix = at > 0 ? numbering.Text[..at] : "";
                var suffix = at >= 0 ? numbering.Text[(at + placeholder.Length)..] : "";
                element.Add(new XElement(TextNs + "list-level-style-number", new XAttribute(TextNs + "level", level + 1),
                    new XAttribute(StyleNs + "num-format", numbering.Format switch
                    {
                        NumberFormat.LowerLetter => "a",
                        NumberFormat.UpperLetter => "A",
                        NumberFormat.LowerRoman => "i",
                        NumberFormat.UpperRoman => "I",
                        _ => "1",
                    }),
                    prefix.Length > 0 ? new XAttribute(StyleNs + "num-prefix", prefix) : null,
                    suffix.Length > 0 ? new XAttribute(StyleNs + "num-suffix", suffix) : null,
                    numbering.Start != 1 ? new XAttribute(TextNs + "start-value", numbering.Start) : null,
                    properties));
            }

            return element;
        }

        private static string Bullet(string text) => text switch
        {
            "" or "" => "•",
            "o" => "◦",
            "" => "▪",
            "" => "➢",
            "" => "✓",
            _ => text[..1],
        };

        // ------------------------------------------------------------ styles.xml

        public XDocument Styles()
        {
            var styles = new XElement(OfficeNs + "styles");
            var defaults = document.Styles;
            styles.Add(new XElement(StyleNs + "default-style", new XAttribute(StyleNs + "family", "paragraph"),
                OdtXml.ParagraphProperties(defaults.ParagraphDefaults with { Numbering = null }),
                TextProperties(defaults.CharacterDefaults with { StyleId = null }, document.Theme) is { } text ? WithLanguage(text, document) : WithLanguage(new XElement(StyleNs + "text-properties"), document)));
            foreach (var style in defaults.Styles)
            {
                if (style.Type is not (StyleType.Paragraph or StyleType.Character))
                {
                    continue;
                }

                var element = new XElement(StyleNs + "style",
                    new XAttribute(StyleNs + "name", StyleName(style.Id)),
                    new XAttribute(StyleNs + "display-name", BuiltInStyles.DisplayName(style)),
                    new XAttribute(StyleNs + "family", style.Type == StyleType.Paragraph ? "paragraph" : "text"));
                if (style.BasedOn is { } basedOn && defaults.Find(basedOn) is not null)
                {
                    element.Add(new XAttribute(StyleNs + "parent-style-name", StyleName(basedOn)));
                }

                if (style.Type == StyleType.Paragraph && style.Next is { } next && defaults.Find(next) is not null)
                {
                    element.Add(new XAttribute(StyleNs + "next-style-name", StyleName(next)));
                }

                if (style.Paragraph.OutlineLevel is >= 0 and < 9 and var level)
                {
                    element.Add(new XAttribute(StyleNs + "default-outline-level", level + 1));
                }

                if (style.Type == StyleType.Paragraph)
                {
                    element.Add(OdtXml.ParagraphProperties(style.Paragraph with { Numbering = null, OutlineLevel = null }));
                }

                element.Add(TextProperties(style.Character with { StyleId = null }, document.Theme));
                styles.Add(element);
            }

            var section = document.FinalSection;
            var layout = new XElement(StyleNs + "page-layout", new XAttribute(StyleNs + "name", "pm1"),
                new XElement(StyleNs + "page-layout-properties",
                    new XAttribute(FoNs + "page-width", Centimetres(section.PageWidth)),
                    new XAttribute(FoNs + "page-height", Centimetres(section.PageHeight)),
                    new XAttribute(StyleNs + "print-orientation", section.Orientation == PageOrientation.Landscape ? "landscape" : "portrait"),
                    new XAttribute(FoNs + "margin-top", Centimetres(section.Headers.Default is null ? section.MarginTop : section.HeaderDistance)),
                    new XAttribute(FoNs + "margin-bottom", Centimetres(section.Footers.Default is null ? section.MarginBottom : section.FooterDistance)),
                    new XAttribute(FoNs + "margin-left", Centimetres(section.MarginLeft)),
                    new XAttribute(FoNs + "margin-right", Centimetres(section.MarginRight))),
                new XElement(StyleNs + "header-style", section.Headers.Default is null ? null : new XElement(StyleNs + "header-footer-properties",
                    new XAttribute(FoNs + "min-height", "0cm"), new XAttribute(FoNs + "margin-bottom", Centimetres(Math.Max(0, section.MarginTop - section.HeaderDistance - 14))))),
                new XElement(StyleNs + "footer-style", section.Footers.Default is null ? null : new XElement(StyleNs + "header-footer-properties",
                    new XAttribute(FoNs + "min-height", "0cm"), new XAttribute(FoNs + "margin-top", Centimetres(Math.Max(0, section.MarginBottom - section.FooterDistance - 14))))));
            var master = new XElement(StyleNs + "master-page", new XAttribute(StyleNs + "name", "Standard"), new XAttribute(StyleNs + "page-layout-name", "pm1"));
            foreach (var (story, name) in new[] { (section.Headers.Default, "header"), (section.Footers.Default, "footer"), (section.Headers.First, "header-first"), (section.Footers.First, "footer-first") })
            {
                if (story is null || (name.EndsWith("-first", StringComparison.Ordinal) && !section.TitlePage))
                {
                    continue;
                }

                var part = new XElement(StyleNs + name);
                WriteBlocks(new Document(story.Blocks, document.Styles, document.Numbering, section, document.Theme, document.Settings), part, isBody: false);
                master.Add(part);
            }

            var root = new XElement(OfficeNs + "document-styles", Declarations(), new XAttribute(OfficeNs + "version", "1.3"),
                styles,
                new XElement(OfficeNs + "automatic-styles", layout, _automatic.Skip(_automaticInContent)),
                new XElement(OfficeNs + "master-styles", master));
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        }

        /// <summary>How many automatic styles content.xml took (styles.xml gets those made for the header and footer).</summary>
        private int _automaticInContent;

        public void MarkContentStyles() => _automaticInContent = _automatic.Count;

        private static XElement WithLanguage(XElement text, Document document)
        {
            var language = Editing.DocumentLanguage.Of(document).Split('-');
            text.Add(new XAttribute(FoNs + "language", language[0]));
            if (language.Length > 1)
            {
                text.Add(new XAttribute(FoNs + "country", language[1]));
            }

            return text;
        }

        /// <summary>Paragraphs with a list instance as nested text:list / text:list-item (one text:list per run of the same instance).</summary>
        private sealed class ListWriter(Writer owner, XElement parent)
        {
            private readonly List<XElement> _lists = [];
            private int _instance = -1;

            public void Add(NumberingInstance instance, int level, XElement paragraph)
            {
                level = Math.Clamp(level, 0, 8);
                if (_instance != instance.Id)
                {
                    Close();
                    _instance = instance.Id;
                }

                if (_lists.Count == 0)
                {
                    var list = new XElement(TextNs + "list", new XAttribute(TextNs + "style-name", owner.ListStyleName(instance.AbstractId)));
                    parent.Add(list);
                    _lists.Add(list);
                }

                while (_lists.Count > level + 1)
                {
                    _lists.RemoveAt(_lists.Count - 1);
                }

                while (_lists.Count < level + 1)
                {
                    // A deeper level: a list inside the last item (an item without its own paragraph when there is none).
                    var last = _lists[^1].Elements(TextNs + "list-item").LastOrDefault() ?? AddItem(_lists[^1]);
                    var nested = new XElement(TextNs + "list");
                    last.Add(nested);
                    _lists.Add(nested);
                }

                AddItem(_lists[^1]).Add(paragraph);
            }

            public void Close()
            {
                _lists.Clear();
                _instance = -1;
            }

            private static XElement AddItem(XElement list)
            {
                var item = new XElement(TextNs + "list-item");
                list.Add(item);
                return item;
            }
        }
    }

    [GeneratedRegex("w:name=\"([^\"]*)\"")]
    private static partial Regex BookmarkName();
}

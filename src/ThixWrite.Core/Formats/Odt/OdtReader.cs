using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixWrite.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;
using static ThixWrite.Core.Formats.Odt.OdtXml;

namespace ThixWrite.Core.Formats.Odt;

/// <summary>
/// Reads OpenDocument Text (.odt, LibreOffice, OpenOffice, Google Docs, Word's export) into the model: the named styles (paragraph
/// and character, with their display names – "Heading 1" becomes Überschrift 1), the document defaults, automatic styles as direct
/// formatting, paragraphs and headings, spans, links, tabs, line breaks and spaces, lists (bullets and numbers by their list
/// styles), tables (spans, covered cells, borders, fills), pictures, footnotes and endnotes, page number / count / date fields,
/// bookmarks, comments, sections and tables of contents (their paragraphs), the page layout and the header and footer of the
/// standard master page. Whatever else it holds (drawings, formulas, embedded objects …) is not shown and cannot be written back:
/// it is listed in <see cref="Document.UnsupportedContent"/>, so the user is told when the file opens.
/// </summary>
public static class OdtReader
{
    public const string NotAnOdtMessage = "Die Datei ist kein gültiges OpenDocument-Textdokument (.odt).";

    /// <summary>True when the stream is a ZIP whose mimetype says OpenDocument Text.</summary>
    public static bool IsOdt(Stream stream) => OdfPackage.HasMimeType(stream, MimeType, "application/vnd.oasis.opendocument.text-template");

    public static Document Read(Stream stream) =>
        OdfPackage.Read(stream, NotAnOdtMessage, package => new Reader(package, package.Xml("content.xml")!, package.Xml("styles.xml")).Read());

    private sealed class Reader
    {
        private static readonly Dictionary<string, string> BuiltInByName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Standard"] = BuiltInStyles.Normal,
            ["Default Paragraph Style"] = BuiltInStyles.Normal,
            ["Default"] = BuiltInStyles.Normal,
            ["Text Body"] = "BodyText",
            ["Body Text"] = "BodyText",
            ["Title"] = BuiltInStyles.Title,
            ["Subtitle"] = BuiltInStyles.Subtitle,
            ["Quotations"] = BuiltInStyles.Quote,
            ["Quote"] = BuiltInStyles.Quote,
            ["Header"] = BuiltInStyles.Header,
            ["Footer"] = BuiltInStyles.Footer,
            ["Footnote"] = NoteNumbering.FootnoteText,
            ["Footnote Text"] = NoteNumbering.FootnoteText,
            ["Endnote"] = NoteNumbering.EndnoteText,
            ["Endnote Text"] = NoteNumbering.EndnoteText,
            ["Footnote Symbol"] = NoteNumbering.FootnoteReference,
            ["Footnote Characters"] = NoteNumbering.FootnoteReference,
            ["Endnote Symbol"] = NoteNumbering.EndnoteReference,
            ["Endnote Characters"] = NoteNumbering.EndnoteReference,
            ["Strong Emphasis"] = BuiltInStyles.Strong,
            ["Strong"] = BuiltInStyles.Strong,
            ["Emphasis"] = BuiltInStyles.Emphasis,
            ["Internet link"] = BuiltInStyles.HyperlinkStyle,
            ["Internet Link"] = BuiltInStyles.HyperlinkStyle,
            ["Hyperlink"] = BuiltInStyles.HyperlinkStyle,
            ["Contents Heading"] = BuiltInStyles.TocHeading,
            ["List Paragraph"] = BuiltInStyles.ListParagraph,
        };

        private readonly OdfPackage _package;
        private readonly XDocument _content;
        private readonly XDocument? _stylesXml;
        private readonly Dictionary<string, XElement> _named = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _automatic = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _listStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
        private readonly Dictionary<PreservedKind, int> _counts = [];
        private readonly List<AbstractNumbering> _definitions = [];
        private readonly List<NumberingInstance> _instances = [];
        private readonly Dictionary<string, int> _definitionOf = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Id, CharacterProperties Properties)> _openComments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _bookmarks = new(StringComparer.Ordinal);
        private int _comments;

        public Reader(OdfPackage package, XDocument content, XDocument? styles)
        {
            _package = package;
            _content = content;
            _stylesXml = styles;
            foreach (var root in new[] { styles?.Root, content.Root })
            {
                foreach (var style in root?.Element(OfficeNs + "styles")?.Elements(StyleNs + "style") ?? [])
                {
                    _named[(string?)style.Attribute(StyleNs + "name") ?? ""] = style;
                }

                foreach (var style in (root?.Element(OfficeNs + "automatic-styles")?.Elements(StyleNs + "style") ?? []).Concat(root?.Element(OfficeNs + "styles")?.Elements(StyleNs + "style") ?? []))
                {
                    _automatic.TryAdd((string?)style.Attribute(StyleNs + "name") ?? "", style);
                }

                foreach (var list in root?.Descendants(TextNs + "list-style") ?? [])
                {
                    _listStyles[(string?)list.Attribute(StyleNs + "name") ?? ""] = list;
                }
            }
        }

        public Document Read()
        {
            var styles = ReadStyles();
            var blocks = new List<Block>();
            if (_content.Root?.Element(OfficeNs + "body")?.Element(OfficeNs + "text") is { } text)
            {
                ReadBlocks(text.Elements(), blocks, null);
            }

            var section = ReadPageSetup();
            return new Document(blocks, styles, new NumberingDefinitions(_definitions, _instances), section)
            {
                UnsupportedContent = _counts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key).Select(pair => new UnsupportedContent(pair.Key, pair.Value)).ToList(),
            };
        }

        // ------------------------------------------------------------ styles

        private string IdOf(string odfName)
        {
            if (_ids.TryGetValue(odfName, out var id))
            {
                return id;
            }

            var display = (string?)(_named.GetValueOrDefault(odfName)?.Attribute(StyleNs + "display-name")) ?? odfName.Replace("_20_", " ", StringComparison.Ordinal);
            id = BuiltInByName.TryGetValue(display, out var builtIn) ? builtIn
                : display.StartsWith("Heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(display.AsSpan(8), out var level) && level is >= 1 and <= 9 ? $"Heading{level}"
                : display.StartsWith("Contents ", StringComparison.OrdinalIgnoreCase) && int.TryParse(display.AsSpan(9), out var toc) && toc is >= 1 and <= 9 ? $"TOC{toc}"
                : OdtWriter.StyleName(odfName);
            _ids[odfName] = id;
            return id;
        }

        private StyleSheet ReadStyles()
        {
            var defaults = _stylesXml?.Root?.Element(OfficeNs + "styles")?.Elements(StyleNs + "default-style")
                .FirstOrDefault(style => (string?)style.Attribute(StyleNs + "family") == "paragraph");
            var paragraphDefaults = ReadParagraph(defaults?.Element(StyleNs + "paragraph-properties"));
            var characterDefaults = ReadText(defaults?.Element(StyleNs + "text-properties"));
            if ((string?)defaults?.Element(StyleNs + "text-properties")?.Attribute(FoNs + "language") is { } language)
            {
                var country = (string?)defaults.Element(StyleNs + "text-properties")!.Attribute(FoNs + "country");
                characterDefaults = Editing.DocumentLanguage.With(characterDefaults, country is { Length: > 0 } ? $"{language}-{country}" : language);
            }

            var list = new List<Style>();
            foreach (var (name, element) in _named)
            {
                var family = (string?)element.Attribute(StyleNs + "family");
                if (family is not ("paragraph" or "text"))
                {
                    continue;
                }

                var id = IdOf(name);
                if (list.Any(style => style.Id == id))
                {
                    continue;
                }

                var paragraph = ReadParagraph(element.Element(StyleNs + "paragraph-properties"));
                if (int.TryParse((string?)element.Attribute(StyleNs + "default-outline-level"), out var outline) && outline is >= 1 and <= 9)
                {
                    paragraph = paragraph with { OutlineLevel = outline - 1 };
                }

                var parent = (string?)element.Attribute(StyleNs + "parent-style-name");
                var next = (string?)element.Attribute(StyleNs + "next-style-name");
                var builtIn = BuiltInStyles.Definition(id);
                list.Add(new Style
                {
                    Id = id,
                    Name = builtIn?.Name ?? ((string?)element.Attribute(StyleNs + "display-name") ?? name.Replace("_20_", " ", StringComparison.Ordinal)),
                    Type = family == "paragraph" ? StyleType.Paragraph : StyleType.Character,
                    BasedOn = parent is null ? null : IdOf(parent),
                    Next = next is null ? null : IdOf(next),
                    IsDefault = id == BuiltInStyles.Normal,
                    QuickFormat = builtIn?.QuickFormat ?? false,
                    UiPriority = builtIn?.UiPriority,
                    Paragraph = family == "paragraph" ? paragraph : Model.ParagraphProperties.Empty,
                    Character = ReadText(element.Element(StyleNs + "text-properties")),
                });
            }

            if (!list.Any(style => style.Id == BuiltInStyles.Normal))
            {
                list.Insert(0, BuiltInStyles.Definition(BuiltInStyles.Normal)!);
            }

            // A style whose parent is not in the file is based on Standard.
            list = list.Select(style => style.BasedOn is { } basedOn && list.All(other => other.Id != basedOn) ? style with { BasedOn = style.Id == BuiltInStyles.Normal ? null : BuiltInStyles.Normal } : style).ToList();
            return new StyleSheet(list, paragraphDefaults, characterDefaults);
        }

        /// <summary>A paragraph's style id and direct properties (an automatic style: its own properties over its named parent).</summary>
        private (ParagraphProperties Properties, CharacterProperties Mark) ParagraphStyle(string? name)
        {
            if (name is null)
            {
                return (Model.ParagraphProperties.Empty, CharacterProperties.Empty);
            }

            if (_named.ContainsKey(name))
            {
                return (new ParagraphProperties { StyleId = IdOf(name) }, CharacterProperties.Empty);
            }

            if (!_automatic.TryGetValue(name, out var automatic))
            {
                return (Model.ParagraphProperties.Empty, CharacterProperties.Empty);
            }

            var parent = (string?)automatic.Attribute(StyleNs + "parent-style-name");
            var properties = ReadParagraph(automatic.Element(StyleNs + "paragraph-properties")) with { StyleId = parent is null ? null : IdOf(parent) };
            return (properties, ReadText(automatic.Element(StyleNs + "text-properties")));
        }

        private CharacterProperties TextStyle(string? name)
        {
            if (name is null)
            {
                return CharacterProperties.Empty;
            }

            if (_named.ContainsKey(name))
            {
                return new CharacterProperties { StyleId = IdOf(name) };
            }

            if (!_automatic.TryGetValue(name, out var automatic))
            {
                return CharacterProperties.Empty;
            }

            var properties = ReadText(automatic.Element(StyleNs + "text-properties"));
            return (string?)automatic.Attribute(StyleNs + "parent-style-name") is { } parent ? properties with { StyleId = IdOf(parent) } : properties;
        }

        // ------------------------------------------------------------ blocks

        private void ReadBlocks(IEnumerable<XElement> elements, List<Block> blocks, (string Style, int Instance)? list, int level = -1)
        {
            foreach (var element in elements)
            {
                switch (element.Name.LocalName)
                {
                    case "p" or "h" when element.Name.Namespace == TextNs:
                        blocks.Add(ReadTextParagraph(element, list is { } current && level >= 0 ? new NumberingReference(current.Instance, Math.Clamp(level, 0, 8)) : null));
                        break;
                    case "list" when element.Name.Namespace == TextNs:
                        ReadList(element, blocks, list, level);
                        break;
                    case "table" when element.Name.Namespace == TableNs:
                        ReadTable(element, blocks);
                        break;
                    case "section" or "table-of-content" or "index-body" or "illustration-index" or "table-index" or "alphabetical-index" or "user-index" or "bibliography" or "object-index":
                        // Containers: their paragraphs are the document's (an index shows its last result).
                        ReadBlocks(element.Elements().Where(child => child.Name.LocalName != "table-of-content-source" && !child.Name.LocalName.EndsWith("-source", StringComparison.Ordinal)), blocks, list, level);
                        break;
                    case "sequence-decls" or "variable-decls" or "user-field-decls" or "tracked-changes" or "forms" or "soft-page-break" or "index-title-template":
                        break;
                    default:
                        Count(element.Name.Namespace == DrawNs ? PreservedKind.Drawing : PreservedKind.Other);
                        break;
                }
            }
        }

        private void ReadList(XElement list, List<Block> blocks, (string Style, int Instance)? outer, int level)
        {
            var styleName = (string?)list.Attribute(TextNs + "style-name") ?? outer?.Style ?? "";
            var current = outer;
            if (outer is null || ((string?)list.Attribute(TextNs + "style-name") is { } own && own != outer.Value.Style))
            {
                var continues = (string?)list.Attribute(TextNs + "continue-numbering") == "true" || list.Attribute(TextNs + "continue-list") is not null;
                current = (styleName, NewInstance(styleName, continues));
            }

            foreach (var item in list.Elements())
            {
                if (item.Name == TextNs + "list-item" || item.Name == TextNs + "list-header")
                {
                    ReadBlocks(item.Elements(), blocks, current, level + 1);
                }
            }
        }

        private int NewInstance(string listStyle, bool continues)
        {
            if (!_definitionOf.TryGetValue(listStyle, out var abstractId))
            {
                abstractId = _definitions.Count + 1;
                _definitionOf[listStyle] = abstractId;
                _definitions.Add(ListDefinition(abstractId, _listStyles.GetValueOrDefault(listStyle)));
            }

            if (continues && _instances.LastOrDefault(instance => instance.AbstractId == abstractId) is { } previous)
            {
                return previous.Id;
            }

            var id = _instances.Count + 1;
            _instances.Add(new NumberingInstance { Id = id, AbstractId = abstractId });
            return id;
        }

        private static AbstractNumbering ListDefinition(int id, XElement? style)
        {
            var bullets = BuiltInLists.Create(ListKind.Bullet, id);
            var numbers = BuiltInLists.Create(ListKind.Numbered, id);
            var levels = new List<NumberingLevel>();
            for (var level = 0; level < 9; level++)
            {
                var element = style?.Elements().FirstOrDefault(child => (int?)child.Attribute(TextNs + "level") == level + 1);
                var basis = element?.Name.LocalName == "list-level-style-number" ? numbers.LevelAt(level)! : bullets.LevelAt(level)!;
                if (element?.Name.LocalName == "list-level-style-number")
                {
                    var format = (string?)element.Attribute(StyleNs + "num-format") switch
                    {
                        "a" => NumberFormat.LowerLetter,
                        "A" => NumberFormat.UpperLetter,
                        "i" => NumberFormat.LowerRoman,
                        "I" => NumberFormat.UpperRoman,
                        "" => NumberFormat.None,
                        _ => NumberFormat.Decimal,
                    };
                    basis = basis with
                    {
                        Format = format,
                        Text = $"{(string?)element.Attribute(StyleNs + "num-prefix")}%{level + 1}{(string?)element.Attribute(StyleNs + "num-suffix")}",
                        Start = int.TryParse((string?)element.Attribute(TextNs + "start-value"), out var start) ? start : 1,
                        Alignment = ParagraphAlignment.Left,
                    };
                }
                else if (element?.Name.LocalName == "list-level-style-bullet" && (string?)element.Attribute(TextNs + "bullet-char") is { Length: > 0 } bullet)
                {
                    basis = basis with { Text = bullet, LabelProperties = CharacterProperties.Empty };
                }

                if (element?.Descendants(StyleNs + "list-level-label-alignment").FirstOrDefault() is { } alignment)
                {
                    basis = basis with
                    {
                        IndentLeft = Points((string?)alignment.Attribute(FoNs + "margin-left")) ?? basis.IndentLeft,
                        FirstLineIndent = Points((string?)alignment.Attribute(FoNs + "text-indent")) ?? basis.FirstLineIndent,
                        Suffix = (string?)alignment.Attribute(TextNs + "label-followed-by") switch { "space" => LabelSuffix.Space, "nothing" => LabelSuffix.Nothing, _ => LabelSuffix.Tab },
                    };
                }

                levels.Add(basis);
            }

            return new AbstractNumbering { Id = id, MultiLevelType = "hybridMultilevel", Levels = new EquatableArray<NumberingLevel>(levels) };
        }

        private Paragraph ReadTextParagraph(XElement element, NumberingReference? numbering)
        {
            var (properties, mark) = ParagraphStyle((string?)element.Attribute(TextNs + "style-name"));
            if (element.Name.LocalName == "h" && int.TryParse((string?)element.Attribute(TextNs + "outline-level"), out var level) && level is >= 1 and <= 9)
            {
                properties = properties.StyleId is null || !properties.StyleId.StartsWith("Heading", StringComparison.Ordinal)
                    ? properties with { StyleId = $"Heading{level}" }
                    : properties;
            }

            if (numbering is not null)
            {
                properties = properties with { Numbering = numbering };
            }

            var inlines = new List<Inline>();
            ReadInlines(element.Nodes(), inlines, CharacterProperties.Empty, null);
            return new Paragraph(inlines, properties, mark);
        }

        private void ReadInlines(IEnumerable<XNode> nodes, List<Inline> inlines, CharacterProperties properties, Hyperlink? link)
        {
            foreach (var node in nodes)
            {
                if (node is XText text)
                {
                    var value = Collapse(text.Value);
                    if (value.Length > 0)
                    {
                        inlines.Add(new TextRun(value, properties, link));
                    }

                    continue;
                }

                if (node is not XElement element)
                {
                    continue;
                }

                var name = element.Name.LocalName;
                if (element.Name.Namespace == TextNs)
                {
                    switch (name)
                    {
                        case "span":
                            ReadInlines(element.Nodes(), inlines, properties.Merge(TextStyle((string?)element.Attribute(TextNs + "style-name"))), link);
                            continue;
                        case "a":
                            var href = (string?)element.Attribute(XLinkNs + "href") ?? "";
                            var target = href.StartsWith('#') ? new Hyperlink(null, href[1..]) : new Hyperlink(href);
                            ReadInlines(element.Nodes(), inlines, properties.Merge(TextStyle((string?)element.Attribute(TextNs + "style-name"))), target);
                            continue;
                        case "s":
                            inlines.Add(new TextRun(new string(' ', Math.Clamp((int?)element.Attribute(TextNs + "c") ?? 1, 1, 1000)), properties, link));
                            continue;
                        case "tab":
                            inlines.Add(new TextRun("\t", properties, link));
                            continue;
                        case "line-break":
                            inlines.Add(new TextRun(SpecialCharacters.LineBreak.ToString(), properties, link));
                            continue;
                        case "soft-page-break" or "bookmark-ref" or "reference-mark-start" or "reference-mark-end" or "reference-mark" or "change-start" or "change-end" or "change":
                            ReadInlines(element.Nodes(), inlines, properties, link);
                            continue;
                        case "bookmark" or "bookmark-start" or "bookmark-end":
                            inlines.Add(Bookmark(element));
                            continue;
                        case "page-number":
                            inlines.Add(new FieldInline(FieldKind.Page, " PAGE ", element.Value, properties, link));
                            continue;
                        case "page-count":
                            inlines.Add(new FieldInline(FieldKind.NumPages, " NUMPAGES ", element.Value, properties, link));
                            continue;
                        case "date":
                            inlines.Add(FieldInline.Date(properties, DateTime.Today) with { Result = element.Value, Link = link });
                            continue;
                        case "note":
                            inlines.Add(ReadNote(element, properties, link));
                            continue;
                        case "sequence" or "chapter" or "title" or "subject" or "author-name" or "file-name" or "time" or "user-defined" or "variable-get" or "sequence-ref" or "bookmark-ref":
                            var shown = element.Value;
                            if (shown.Length > 0)
                            {
                                inlines.Add(new TextRun(shown, properties, link)); // a field ThixWrite does not update: its text
                            }

                            continue;
                    }
                }

                if (element.Name.Namespace == OfficeNs && name == "annotation")
                {
                    ReadAnnotation(element, inlines);
                    continue;
                }

                if (element.Name.Namespace == OfficeNs && name == "annotation-end")
                {
                    if ((string?)element.Attribute(OfficeNs + "name") is { } annotation && _openComments.Remove(annotation, out var open))
                    {
                        inlines.Add(new CommentInline(CommentOf(open.Id), CharacterProperties.Empty));
                    }

                    continue;
                }

                if (element.Name.Namespace == DrawNs && name == "frame" && ReadPicture(element, properties, link) is { } picture)
                {
                    inlines.Add(picture);
                    continue;
                }

                Count(element.Name.Namespace == DrawNs ? PreservedKind.Drawing : PreservedKind.Other);
            }
        }

        /// <summary>ODF text collapses runs of white space (spaces come as text:s).</summary>
        private static string Collapse(string value)
        {
            if (!value.Any(character => character is '\n' or '\r' or '\t' or ' '))
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            var space = false;
            foreach (var character in value)
            {
                if (character is ' ' or '\n' or '\r' or '\t')
                {
                    if (!space)
                    {
                        builder.Append(' ');
                    }

                    space = true;
                    continue;
                }

                space = false;
                builder.Append(character);
            }

            return builder.ToString();
        }

        private NoteInline ReadNote(XElement element, CharacterProperties properties, Hyperlink? link)
        {
            var kind = (string?)element.Attribute(TextNs + "note-class") == "endnote" ? NoteKind.Endnote : NoteKind.Footnote;
            var blocks = new List<Block>();
            ReadBlocks(element.Element(TextNs + "note-body")?.Elements() ?? [], blocks, null);
            if (blocks.FirstOrDefault() is Paragraph first)
            {
                var style = first.Properties.StyleId is null ? first.Properties with { StyleId = NoteNumbering.TextStyle(kind) } : first.Properties;
                var mark = new NoteMarkInline(kind, new CharacterProperties { StyleId = NoteNumbering.ReferenceStyle(kind) });
                var space = first.Text.StartsWith(' ') || first.Length == 0 ? [] : new Inline[] { new TextRun(" ", CharacterProperties.Empty) };
                blocks[0] = new Paragraph([mark, .. space, .. first.Inlines], style, first.MarkProperties);
            }

            var reference = properties.StyleId is null ? properties with { StyleId = NoteNumbering.ReferenceStyle(kind) } : properties;
            return new NoteInline(kind, new Story(blocks), reference, link);
        }

        private void ReadAnnotation(XElement element, List<Inline> inlines)
        {
            var id = (++_comments).ToString(CultureInfo.InvariantCulture);
            var author = element.Element(DcNs + "creator")?.Value ?? "";
            DateTime? date = DateTime.TryParse(element.Element(DcNs + "date")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
            var initials = element.Element(MetaNs + "creator-initials")?.Value ?? Comment.InitialsOf(author);
            var text = string.Join("\n", element.Elements(TextNs + "p").Select(paragraph => Collapse(string.Concat(paragraph.DescendantNodes().OfType<XText>().Select(node => node.Value)))));
            var comment = new Comment(id, author, initials, date, new Story([])).WithText(text);
            _annotationComments[id] = comment;
            if ((string?)element.Attribute(OfficeNs + "name") is { } name)
            {
                _openComments[name] = (id, CharacterProperties.Empty);
                inlines.Add(new CommentAnchorInline(id, CharacterProperties.Empty));
                return;
            }

            inlines.Add(new CommentInline(comment, CharacterProperties.Empty)); // a comment on a point
        }

        private readonly Dictionary<string, Comment> _annotationComments = new(StringComparer.Ordinal);

        private Comment CommentOf(string id) => _annotationComments[id];

        private OpaqueInline Bookmark(XElement element)
        {
            const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var name = System.Security.SecurityElement.Escape((string?)element.Attribute(TextNs + "name") ?? "");
            if (!_bookmarks.TryGetValue(name, out var id))
            {
                id = _bookmarks.Count;
                _bookmarks[name] = id;
            }

            var xml = element.Name.LocalName == "bookmark-end"
                ? $"<w:bookmarkEnd w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />"
                : $"<w:bookmarkStart w:name=\"{name}\" w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />";
            return new OpaqueInline(xml, PreservedKind.Bookmark, false, CharacterProperties.Empty);
        }

        private ImageInline? ReadPicture(XElement frame, CharacterProperties properties, Hyperlink? link)
        {
            var href = (string?)frame.Element(DrawNs + "image")?.Attribute(XLinkNs + "href");
            if (href is null || _package.Bytes(href) is not { } bytes || ImageData.FromBytes(bytes) is not { } image)
            {
                return null;
            }

            var (naturalWidth, naturalHeight) = image.NaturalSize;
            var width = Points((string?)frame.Attribute(SvgNs + "width")) ?? naturalWidth;
            var height = Points((string?)frame.Attribute(SvgNs + "height")) ?? naturalHeight;
            var description = frame.Element(SvgNs + "desc")?.Value ?? frame.Element(SvgNs + "title")?.Value;
            return new ImageInline(image, width, height, properties, string.IsNullOrWhiteSpace(description) ? null : description, link);
        }

        // ------------------------------------------------------------ tables

        private void ReadTable(XElement element, List<Block> blocks)
        {
            var widths = new List<double>();
            foreach (var column in element.Descendants(TableNs + "table-column").Where(column => column.Ancestors(TableNs + "table").First() == element))
            {
                var repeated = Math.Clamp((int?)column.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, 64);
                var width = (string?)column.Attribute(TableNs + "style-name") is { } style && _automatic.TryGetValue(style, out var columnStyle)
                    ? Points((string?)columnStyle.Element(StyleNs + "table-column-properties")?.Attribute(StyleNs + "column-width"))
                    : null;
                widths.AddRange(Enumerable.Repeat(width ?? 0, repeated));
            }

            var rows = element.Elements().SelectMany(child => child.Name == TableNs + "table-row" ? [child]
                : child.Name.LocalName is "table-header-rows" or "table-rows" or "table-row-group" ? child.Elements(TableNs + "table-row") : []).ToList();
            var headerRows = element.Element(TableNs + "table-header-rows")?.Elements(TableNs + "table-row").ToHashSet() ?? [];
            if (rows.Count == 0)
            {
                return;
            }

            var columnCount = Math.Max(widths.Count, rows.Max(row => row.Elements().Where(cell => cell.Name.LocalName is "table-cell" or "covered-table-cell")
                .Sum(cell => Math.Clamp((int?)cell.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, 64))));
            var spanning = new int[columnCount]; // rows still covered below a cell that spans rows
            var spanWidth = new int[columnCount];
            var modelRows = new List<TableRow>();
            var cells = new List<(int Row, int Cell, List<Paragraph> Paragraphs)>();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cellProperties = new List<TableCellProperties>();
                var column = 0;
                foreach (var cell in rows[rowIndex].Elements().Where(child => child.Name.LocalName is "table-cell" or "covered-table-cell"))
                {
                    var repeat = Math.Clamp((int?)cell.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, 64);
                    for (var copy = 0; copy < repeat && column < columnCount; copy++)
                    {
                        if (cell.Name.LocalName == "covered-table-cell")
                        {
                            if (spanning[column] > 0)
                            {
                                // Under a cell that spans rows: a covered cell of the model, as wide as the one above.
                                spanning[column]--;
                                var wide = Math.Max(1, spanWidth[column]);
                                cells.Add((rowIndex, cellProperties.Count, [Paragraph.Empty()]));
                                cellProperties.Add(new TableCellProperties { GridSpan = wide, VerticalMerge = VerticalMerge.Continue });
                                column += wide;
                                copy += wide - 1;
                            }
                            else
                            {
                                column++; // part of the cell before (it spans columns)
                            }

                            continue;
                        }

                        var columns = Math.Clamp((int?)cell.Attribute(TableNs + "number-columns-spanned") ?? 1, 1, columnCount - column);
                        var rowsSpanned = Math.Max(1, (int?)cell.Attribute(TableNs + "number-rows-spanned") ?? 1);
                        if (rowsSpanned > 1)
                        {
                            spanning[column] = rowsSpanned - 1;
                            spanWidth[column] = columns;
                        }

                        var paragraphs = new List<Block>();
                        ReadBlocks(cell.Elements(), paragraphs, null);
                        var cellParagraphs = paragraphs.OfType<Paragraph>().ToList();
                        if (paragraphs.Any(block => block is not Paragraph) || paragraphs.OfType<Paragraph>().Any(paragraph => paragraph.Cell is not null))
                        {
                            Count(PreservedKind.Table); // a table inside a cell: its text only
                            cellParagraphs = paragraphs.OfType<Paragraph>().Select(paragraph => paragraph.WithCell(null)).ToList();
                        }

                        if (cellParagraphs.Count == 0)
                        {
                            cellParagraphs.Add(Paragraph.Empty());
                        }

                        cells.Add((rowIndex, cellProperties.Count, cellParagraphs));
                        cellProperties.Add(CellProperties(cell, columns, rowsSpanned > 1));
                        column += columns;
                    }
                }

                modelRows.Add(new TableRow(new TableRowProperties { IsHeader = headerRows.Contains(rows[rowIndex]) }, new EquatableArray<TableCellProperties>(cellProperties)));
            }

            var grid = widths.Count == columnCount && widths.All(width => width > 0) ? widths : [];
            var tableStyle = (string?)element.Attribute(TableNs + "style-name") is { } name && _automatic.TryGetValue(name, out var found) ? found.Element(StyleNs + "table-properties") : null;
            var properties = new TableProperties
            {
                Alignment = (string?)tableStyle?.Attribute(TableNs + "align") switch { "center" => TableAlignment.Center, "right" => TableAlignment.Right, _ => null },
                Width = Points((string?)tableStyle?.Attribute(StyleNs + "width")) is { } tableWidth ? TableWidth.Points(tableWidth) : null,
                CellMargins = new CellMargins(0, 0, 0, 0),
            };
            var table = new Table(properties, new EquatableArray<double>(grid), new EquatableArray<TableRow>(modelRows));
            foreach (var (row, cell, paragraphs) in cells)
            {
                blocks.AddRange(paragraphs.Select(paragraph => paragraph.WithCell(new CellRef(table, row, cell))));
            }
        }

        private TableCellProperties CellProperties(XElement cell, int columns, bool restartsRows)
        {
            var properties = new TableCellProperties { GridSpan = columns, VerticalMerge = restartsRows ? VerticalMerge.Restart : VerticalMerge.None };
            if ((string?)cell.Attribute(TableNs + "style-name") is not { } name || !_automatic.TryGetValue(name, out var style)
                || style.Element(StyleNs + "table-cell-properties") is not { } element)
            {
                return properties;
            }

            BorderLine? Border(string side)
            {
                var value = (string?)element.Attribute(FoNs + $"border-{side}") ?? (string?)element.Attribute(FoNs + "border");
                if (value is null)
                {
                    return null;
                }

                if (value.Trim() == "none")
                {
                    return BorderLine.None;
                }

                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var width = parts.Select(Points).FirstOrDefault(points => points is not null) ?? 0.5;
                var color = parts.Select(ColorOf).FirstOrDefault(hex => hex is not null) ?? "auto";
                var kind = parts.Contains("double") ? "double" : parts.Contains("dotted") ? "dotted" : parts.Contains("dashed") ? "dashed" : "single";
                return new BorderLine(kind, Math.Round(width * 4) / 4, color);
            }

            var padding = Points((string?)element.Attribute(FoNs + "padding"));
            return properties with
            {
                Borders = new BorderSet(Border("top"), Border("left"), Border("bottom"), Border("right")),
                Shading = ColorOf((string?)element.Attribute(FoNs + "background-color")) is { } fill ? new Shading(fill) : null,
                Margins = new CellMargins(
                    Points((string?)element.Attribute(FoNs + "padding-top")) ?? padding,
                    Points((string?)element.Attribute(FoNs + "padding-left")) ?? padding,
                    Points((string?)element.Attribute(FoNs + "padding-bottom")) ?? padding,
                    Points((string?)element.Attribute(FoNs + "padding-right")) ?? padding),
                VerticalAlignment = (string?)element.Attribute(StyleNs + "vertical-align") switch
                {
                    "middle" => CellVerticalAlignment.Center,
                    "bottom" => CellVerticalAlignment.Bottom,
                    "top" => CellVerticalAlignment.Top,
                    _ => null,
                },
            };
        }

        // ------------------------------------------------------------ page

        private SectionProperties ReadPageSetup()
        {
            var masters = _stylesXml?.Root?.Element(OfficeNs + "master-styles");
            var master = masters?.Elements(StyleNs + "master-page").FirstOrDefault(page => (string?)page.Attribute(StyleNs + "name") == "Standard")
                ?? masters?.Elements(StyleNs + "master-page").FirstOrDefault();
            var section = SectionProperties.Default;
            if (master is null)
            {
                return section;
            }

            var layout = _stylesXml?.Root?.Element(OfficeNs + "automatic-styles")?.Elements(StyleNs + "page-layout")
                .FirstOrDefault(item => (string?)item.Attribute(StyleNs + "name") == (string?)master.Attribute(StyleNs + "page-layout-name"));
            if (layout?.Element(StyleNs + "page-layout-properties") is { } page)
            {
                section = section with
                {
                    PageWidth = Points((string?)page.Attribute(FoNs + "page-width")) ?? section.PageWidth,
                    PageHeight = Points((string?)page.Attribute(FoNs + "page-height")) ?? section.PageHeight,
                    Orientation = (string?)page.Attribute(StyleNs + "print-orientation") == "landscape" ? PageOrientation.Landscape : PageOrientation.Portrait,
                    MarginTop = Points((string?)page.Attribute(FoNs + "margin-top")) ?? section.MarginTop,
                    MarginBottom = Points((string?)page.Attribute(FoNs + "margin-bottom")) ?? section.MarginBottom,
                    MarginLeft = Points((string?)page.Attribute(FoNs + "margin-left")) ?? section.MarginLeft,
                    MarginRight = Points((string?)page.Attribute(FoNs + "margin-right")) ?? section.MarginRight,
                };
            }

            // ODF puts the header inside the page margin: the text starts below it (Word measures the margin to the text).
            var header = master.Element(StyleNs + "header");
            var footer = master.Element(StyleNs + "footer");
            if (header is not null)
            {
                var spacing = Points((string?)layout?.Element(StyleNs + "header-style")?.Element(StyleNs + "header-footer-properties")?.Attribute(FoNs + "margin-bottom")) ?? 14;
                section = section with { HeaderDistance = section.MarginTop, MarginTop = section.MarginTop + 14 + spacing };
            }

            if (footer is not null)
            {
                var spacing = Points((string?)layout?.Element(StyleNs + "footer-style")?.Element(StyleNs + "header-footer-properties")?.Attribute(FoNs + "margin-top")) ?? 14;
                section = section with { FooterDistance = section.MarginBottom, MarginBottom = section.MarginBottom + 14 + spacing };
            }

            Story? StoryOf(XElement? element)
            {
                if (element is null || (string?)element.Attribute(StyleNs + "display") == "false")
                {
                    return null;
                }

                var blocks = new List<Block>();
                ReadBlocks(element.Elements(), blocks, null);
                return new Story(blocks);
            }

            var first = StoryOf(master.Element(StyleNs + "header-first"));
            var firstFooter = StoryOf(master.Element(StyleNs + "footer-first"));
            return section with
            {
                Headers = new HeaderFooterSet(StoryOf(header), first),
                Footers = new HeaderFooterSet(StoryOf(footer), firstFooter),
                TitlePage = first is not null || firstFooter is not null,
            };
        }

        private void Count(PreservedKind kind) => _counts[kind] = _counts.GetValueOrDefault(kind) + 1;
    }
}

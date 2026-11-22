using System.Globalization;
using System.Net;
using System.Text;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Html;

/// <summary>
/// Pasting from other programs (browsers, Word, mail): the HTML of the clipboard (Windows "HTML Format" / CF_HTML, macOS
/// public.html) becomes a <see cref="DocumentFragment"/> – paragraphs and headings, bold, italic, underline, strikethrough,
/// sub/superscript, colour, size and font of text, lists, tables, links (only web and mail addresses) and pictures embedded as
/// data: URIs. Sanitized: scripts, styles, frames, objects, forms and comments are dropped; nothing is fetched (a picture from the
/// web is left out), nothing runs. A small tolerant parser – clipboard HTML is rarely well-formed.
/// </summary>
public static class HtmlImport
{
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "meta", "link", "noscript", "iframe", "frame", "frameset", "object", "embed", "applet", "svg",
        "math", "template", "form", "input", "button", "select", "textarea", "canvas", "video", "audio", "xml",
    };

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "img", "hr", "meta", "link", "input", "col", "area", "base", "wbr", "source", "param",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "pre", "section", "article", "header", "footer", "aside",
        "main", "nav", "address", "dd", "dt", "figure", "figcaption", "center",
    };

    /// <summary>The HTML of a Windows "HTML Format" clipboard entry (its header names where the HTML is), or the text itself.</summary>
    public static string FromClipboardFormat(string data)
    {
        if (!data.StartsWith("Version:", StringComparison.Ordinal))
        {
            return data;
        }

        var start = Offset(data, "StartHTML:");
        var end = Offset(data, "EndHTML:");
        var bytes = Encoding.UTF8.GetBytes(data);
        if (start is >= 0 && end is { } last && last > start && last <= bytes.Length)
        {
            return Encoding.UTF8.GetString(bytes, start.Value, last - start.Value);
        }

        var html = data.IndexOf('<');
        return html >= 0 ? data[html..] : data;

        static int? Offset(string text, string key)
        {
            var at = text.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            var digits = new string(text.Skip(at + key.Length).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
    }

    /// <summary>The content of <paramref name="html"/> as a fragment (null when it holds nothing to paste).</summary>
    public static DocumentFragment? Import(string html) => Import(html, HtmlImportOptions.Clipboard);

    /// <summary>
    /// The content of <paramref name="html"/> as a fragment, read with <paramref name="options"/> (ThixMail reads a mail with
    /// <see cref="HtmlImportOptions.Mail"/>: pictures by cid: or after consent, layout tables without lines, quotes as indented bars).
    /// </summary>
    public static DocumentFragment? Import(string html, HtmlImportOptions options)
    {
        var start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            html = html[(start + "<!--StartFragment-->".Length)..end];
        }

        var root = Parse(html);
        var builder = new Builder(options);
        builder.Block(root, new Style(), ParagraphContext.Body);
        return builder.Finish();
    }

    /// <summary>The whole text of <paramref name="html"/> as a reader sees it (scripts, styles and hidden parts dropped): search and previews.</summary>
    public static string PlainText(string html)
    {
        var text = new StringBuilder();
        Collect(Parse(html), text);
        return CollapseWhitespace(text.ToString());

        static void Collect(Node node, StringBuilder text)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    text.Append(child.Text);
                    continue;
                }

                if (IsHidden(child))
                {
                    continue;
                }

                if (child.Tag is "br" or "p" or "div" or "tr" or "li" or "h1" or "h2" or "h3" or "blockquote")
                {
                    text.Append('\n');
                }
                else if (child.Tag is "td" or "th")
                {
                    text.Append(' ');
                }

                Collect(child, text);
            }
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var lines = text.Replace("\r", "").Split('\n')
            .Select(line => string.Join(' ', line.Split([' ', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
        var result = new StringBuilder();
        var blank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blank = result.Length > 0;
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(blank ? "\n\n" : "\n");
            }

            result.Append(line);
            blank = false;
        }

        return result.ToString();
    }

    /// <summary>display:none (and Outlook's mso-hide:all): a newsletter's hidden preview text.</summary>
    private static bool IsHidden(Node node)
    {
        var css = node["style"];
        if (css is null)
        {
            return node.Attributes.ContainsKey("hidden");
        }

        var compact = css.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        return compact.Contains("display:none", StringComparison.Ordinal) || compact.Contains("mso-hide:all", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- parsing

    private sealed class Node(string tag)
    {
        public string Tag { get; } = tag;

        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<Node> Children { get; } = [];

        public string? Text { get; init; }

        public Node? Parent { get; set; }

        public bool IsText => Text is not null;

        public string? this[string name] => Attributes.TryGetValue(name, out var value) ? value : null;
    }

    private static Node Parse(string html)
    {
        var root = new Node("#root");
        var current = root;
        var index = 0;
        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0)
            {
                AddText(current, html[index..]);
                break;
            }

            if (open > index)
            {
                AddText(current, html[index..open]);
            }

            if (html.AsSpan(open).StartsWith("<!--"))
            {
                var close = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                index = close < 0 ? html.Length : close + 3;
                continue;
            }

            var tagEnd = FindTagEnd(html, open);
            if (tagEnd < 0)
            {
                AddText(current, html[open..]);
                break;
            }

            var tag = html[(open + 1)..tagEnd];
            index = tagEnd + 1;
            if (tag.StartsWith('!') || tag.StartsWith('?'))
            {
                continue;
            }

            if (tag.StartsWith('/'))
            {
                var name = TagName(tag[1..]);
                for (var node = current; node.Parent is not null; node = node.Parent)
                {
                    if (string.Equals(node.Tag, name, StringComparison.OrdinalIgnoreCase))
                    {
                        current = node.Parent;
                        break;
                    }
                }

                continue;
            }

            var element = new Node(TagName(tag)) { Parent = current };
            ReadAttributes(tag, element);
            if (Dropped.Contains(element.Tag))
            {
                // Its content goes too (a script, a style sheet …): skip to its end tag.
                var closing = html.IndexOf($"</{element.Tag}", index, StringComparison.OrdinalIgnoreCase);
                if (!tag.EndsWith('/') && !Void.Contains(element.Tag))
                {
                    index = closing < 0 ? html.Length : Math.Max(index, html.IndexOf('>', closing) + 1);
                    if (index == 0)
                    {
                        index = html.Length;
                    }
                }

                continue;
            }

            current.Children.Add(element);
            if (!tag.EndsWith('/') && !Void.Contains(element.Tag))
            {
                current = element;
            }
        }

        return root;
    }

    private static int FindTagEnd(string html, int open)
    {
        char? quote = null;
        for (var index = open + 1; index < html.Length; index++)
        {
            var character = html[index];
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static string TagName(string tag)
    {
        var name = new string(tag.TakeWhile(character => !char.IsWhiteSpace(character) && character != '/' && character != '>').ToArray());
        var colon = name.IndexOf(':');
        return (colon >= 0 ? name[(colon + 1)..] : name).ToLowerInvariant(); // o:p, w:… of Word's HTML
    }

    private static void ReadAttributes(string tag, Node element)
    {
        var index = tag.IndexOfAny([' ', '\t', '\n', '\r']);
        while (index >= 0 && index < tag.Length)
        {
            while (index < tag.Length && (char.IsWhiteSpace(tag[index]) || tag[index] == '/'))
            {
                index++;
            }

            var nameStart = index;
            while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] is not '=' and not '/')
            {
                index++;
            }

            if (index == nameStart)
            {
                break;
            }

            var name = tag[nameStart..index];
            var value = "";
            if (index < tag.Length && tag[index] == '=')
            {
                index++;
                if (index < tag.Length && tag[index] is '"' or '\'')
                {
                    var quote = tag[index];
                    var close = tag.IndexOf(quote, index + 1);
                    value = close < 0 ? tag[(index + 1)..] : tag[(index + 1)..close];
                    index = close < 0 ? tag.Length : close + 1;
                }
                else
                {
                    var valueStart = index;
                    while (index < tag.Length && !char.IsWhiteSpace(tag[index]))
                    {
                        index++;
                    }

                    value = tag[valueStart..index];
                }
            }

            element.Attributes[name] = WebUtility.HtmlDecode(value);
        }
    }

    private static void AddText(Node parent, string text)
    {
        if (text.Length > 0)
        {
            parent.Children.Add(new Node("#text") { Text = WebUtility.HtmlDecode(text), Parent = parent });
        }
    }

    // ---------------------------------------------------------------- building

    private enum ParagraphContext
    {
        Body,
        Cell,
    }

    /// <summary>The formatting inherited from the elements around text.</summary>
    private sealed record Style
    {
        public CharacterProperties Characters { get; init; } = CharacterProperties.Empty;

        public Hyperlink? Link { get; init; }

        public bool Preformatted { get; init; }
    }

    private sealed class Builder(HtmlImportOptions options)
    {
        private readonly List<Block> _blocks = [];
        private readonly List<Inline> _inlines = [];
        private ParagraphProperties _paragraph = ParagraphProperties.Empty;
        private NumberingDefinitions _numbering = NumberingDefinitions.Empty;
        private readonly List<(ListKind Kind, int Instance)> _lists = [];
        private readonly HashSet<string> _styles = new(StringComparer.Ordinal);
        private int _quoteDepth;
        private string? _fill;

        public void Block(Node node, Style style, ParagraphContext context)
        {
            foreach (var child in node.Children)
            {
                Visit(child, style, context);
            }
        }

        private void Visit(Node node, Style style, ParagraphContext context)
        {
            if (node.IsText)
            {
                AddText(node.Text!, style);
                return;
            }

            if (IsHidden(node))
            {
                return;
            }

            var tag = node.Tag;
            switch (tag)
            {
                case "br":
                    _inlines.Add(new TextRun(SpecialCharacters.LineBreak.ToString(), style.Characters, style.Link));
                    return;
                case "hr":
                    Flush();
                    if (options.RulesAsLines)
                    {
                        _blocks.Add(Paragraph.Empty(new ParagraphProperties
                        {
                            SpacingBefore = 3,
                            SpacingAfter = 6,
                            Borders = new BorderSet(Bottom: new BorderLine("single", 0.75, options.QuoteBarColor, 1)),
                        }));
                    }

                    return;
                case "blockquote" when options.QuotesAsBars:
                    Flush();
                    _quoteDepth++;
                    Block(node, Inherit(node, style), context);
                    Flush();
                    _quoteDepth--;
                    return;
                case "img":
                    AddImage(node, style);
                    return;
                case "table":
                    Flush();
                    AddTable(node, style, context);
                    return;
                case "ul" or "ol":
                    Flush();
                    _lists.Add((tag == "ul" ? ListKind.Bullet : ListKind.Numbered, -1));
                    Block(node, style, context);
                    _lists.RemoveAt(_lists.Count - 1);
                    Flush();
                    if (options.ParagraphSpacing && _lists.Count == 0 && _blocks.Count > 0 && _blocks[^1] is Paragraph { Properties.Numbering: not null } lastItem)
                    {
                        // a list ends with the space a browser gives it before the next paragraph
                        _blocks[^1] = lastItem.WithProperties(lastItem.Properties with { SpacingAfter = 11 });
                    }
                    return;
            }

            var inner = Inherit(node, style);
            if (BlockTags.Contains(tag))
            {
                Flush();
                _paragraph = ParagraphFor(node, tag);
                Block(node, inner, context);
                Flush();
                return;
            }

            Block(node, inner, context);
        }

        private ParagraphProperties ParagraphFor(Node node, string tag)
        {
            var properties = tag switch
            {
                "h1" => Styled(BuiltInStyles.Heading1),
                "h2" => Styled(BuiltInStyles.Heading2),
                "h3" or "h4" or "h5" or "h6" => Styled(BuiltInStyles.Heading3),
                "blockquote" => Styled(BuiltInStyles.Quote),
                "li" => ListItem(),
                // Word/Outlook's MsoNormal paragraphs have no margins (their style sheet says so; it is dropped with the head)
                "p" when options.ParagraphSpacing && !(node["class"] ?? "").StartsWith("Mso", StringComparison.OrdinalIgnoreCase) => new ParagraphProperties { SpacingAfter = 11 },
                _ => ParagraphProperties.Empty,
            };
            var align = (node["align"] ?? StyleValue(node, "text-align"))?.Trim().ToLowerInvariant();
            var alignment = tag == "center" ? ParagraphAlignment.Center : align switch
            {
                "center" => ParagraphAlignment.Center,
                "right" => ParagraphAlignment.Right,
                "justify" => ParagraphAlignment.Justify,
                _ => (ParagraphAlignment?)null,
            };
            return alignment is null ? properties : properties with { Alignment = alignment };
        }

        private ParagraphProperties Styled(string styleId)
        {
            _styles.Add(styleId);
            return new ParagraphProperties { StyleId = styleId };
        }

        private ParagraphProperties ListItem()
        {
            if (_lists.Count == 0)
            {
                return ParagraphProperties.Empty;
            }

            var level = _lists.Count - 1;
            var (kind, instance) = _lists[0];
            if (instance < 0)
            {
                var definition = BuiltInLists.Create(kind, _numbering.NextDefinitionId());
                _numbering = _numbering.WithDefinition(definition);
                instance = _numbering.NextInstanceId();
                _numbering = _numbering.WithInstance(new NumberingInstance { Id = instance, AbstractId = definition.Id });
                _lists[0] = (kind, instance);
            }

            _styles.Add(BuiltInStyles.ListParagraph);
            return new ParagraphProperties { StyleId = BuiltInStyles.ListParagraph, Numbering = new NumberingReference(instance, Math.Min(level, 8)) };
        }

        private void AddText(string text, Style style)
        {
            if (!style.Preformatted)
            {
                text = CollapseSpaces(text);
                if (_inlines.Count == 0 || (_inlines[^1] is TextRun previous && (previous.Text.EndsWith(' ') || previous.Text.EndsWith(SpecialCharacters.LineBreak))))
                {
                    text = text.TrimStart(' ');
                }

                if (text.Length == 0)
                {
                    return;
                }
            }
            else
            {
                text = text.Replace("\r\n", "\n").Replace('\n', SpecialCharacters.LineBreak);
            }

            text = new string(text.Where(character => !char.IsControl(character) || character is SpecialCharacters.Tab or SpecialCharacters.LineBreak).ToArray());
            _inlines.Add(new TextRun(text, style.Characters, style.Link));
        }

        private void AddImage(Node node, Style style)
        {
            var source = (node["src"] ?? "").Trim();
            var image = source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ? FromDataUri(source) : options.ResolveImage?.Invoke(source);
            if (image is null)
            {
                return; // a picture from the web is never fetched here (the host decides: cid: parts, remote pictures after consent)
            }

            var (width, height) = image.NaturalSize;
            var givenWidth = Length(node["width"]) ?? Length(StyleValue(node, "width"));
            var givenHeight = Length(node["height"]) ?? Length(StyleValue(node, "height"));
            if (givenWidth is > 0)
            {
                height = givenHeight ?? height * givenWidth.Value / width;
                width = givenWidth.Value;
            }
            else if (givenHeight is > 0)
            {
                width = width * givenHeight.Value / height;
                height = givenHeight.Value;
            }

            if (width <= 2 && height <= 2)
            {
                return; // a tracking pixel shows nothing
            }

            var widest = options.ContentWidth; // a picture wider than the text is fitted
            if (width > widest)
            {
                height *= widest / width;
                width = widest;
            }

            var description = node["alt"];
            _inlines.Add(new ImageInline(image, width, height, style.Characters, string.IsNullOrWhiteSpace(description) ? null : description, style.Link));
        }

        private static ImageData? FromDataUri(string source)
        {
            var comma = source.IndexOf(',');
            if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                return ImageData.FromBytes(Convert.FromBase64String(source[(comma + 1)..].Trim()));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private void AddTable(Node table, Style style, ParagraphContext context)
        {
            var rows = Descendants(table, "tr").ToList();
            if (rows.Count == 0)
            {
                return;
            }

            // A mail's layout table of one cell (a centring wrapper, a button) and a table inside a cell (ThixWrite has no nested tables) give their
            // content as paragraphs – a cell's background goes along as the paragraphs' shading, so light text keeps its dark background.
            var cellCount = rows.Sum(row => row.Children.Count(child => child.Tag is "td" or "th"));
            if (options.BorderlessTables && (cellCount == 1 || context == ParagraphContext.Cell))
            {
                Flush();
                var outer = ShadingOf(table);
                foreach (var cell in rows.SelectMany(row => row.Children.Where(child => child.Tag is "td" or "th")))
                {
                    var saved = _fill;
                    _fill = ShadingOf(cell) ?? ShadingOf(cell.Parent ?? cell) ?? outer ?? _fill;
                    Block(cell, Inherit(cell, cell.Tag == "th" ? style with { Characters = style.Characters with { Bold = true } } : style), context);
                    Flush();
                    _fill = saved;
                }

                return;
            }

            // A table of a mail is usually layout (a newsletter's columns): no lines unless the HTML asks for them.
            var border = int.TryParse(table["border"], NumberStyles.None, CultureInfo.InvariantCulture, out var given) ? given : 0;
            var lines = !options.BorderlessTables || border > 0;
            var styleId = lines ? BuiltInStyles.TableGrid : BuiltInStyles.TableNormal;
            var available = options.ContentWidth;
            var tableWidth = WidthOf(table, available);
            var properties = new TableProperties
            {
                StyleId = styleId,
                Width = tableWidth is { } points ? TableWidth.Points(points) : TableWidth.Auto,
                Shading = ShadingOf(table) is { } fill ? new Shading(fill) : null,
                Alignment = (table["align"] ?? "").Equals("center", StringComparison.OrdinalIgnoreCase) ? TableAlignment.Center : null,
            };
            var model = new TableEdits.TableModel { Properties = properties };
            _styles.Add(styleId);
            var columnWidths = new List<double?>();
            foreach (var row in rows)
            {
                var rowModel = new TableEdits.RowModel { Properties = TableRowProperties.Empty };
                var column = 0;
                foreach (var cell in row.Children.Where(child => child.Tag is "td" or "th"))
                {
                    var cellBuilder = new Builder(options with { ContentWidth = Math.Max(36, (tableWidth ?? available) / Math.Max(1, row.Children.Count(c => c.Tag is "td" or "th"))) });
                    var cellStyle = Inherit(cell, cell.Tag == "th" ? style with { Characters = style.Characters with { Bold = true } } : style);
                    cellBuilder.Block(cell, cellStyle, ParagraphContext.Cell);
                    var paragraphs = cellBuilder.Paragraphs();
                    var span = int.TryParse(cell["colspan"], NumberStyles.None, CultureInfo.InvariantCulture, out var colspan) ? Math.Clamp(colspan, 1, 63) : 1;
                    var cellProperties = TableCellProperties.Empty with { GridSpan = span };
                    if (ShadingOf(cell) is { } cellFill)
                    {
                        cellProperties = cellProperties with { Shading = new Shading(cellFill) };
                    }

                    if ((cell["valign"] ?? StyleValue(cell, "vertical-align"))?.Trim().ToLowerInvariant() is "middle" or "center")
                    {
                        cellProperties = cellProperties with { VerticalAlignment = CellVerticalAlignment.Center };
                    }

                    if (span == 1 && WidthOf(cell, tableWidth ?? available) is { } cellWidth)
                    {
                        while (columnWidths.Count <= column)
                        {
                            columnWidths.Add(null);
                        }

                        columnWidths[column] ??= cellWidth;
                    }

                    rowModel.Cells.Add(new TableEdits.CellModel { Properties = cellProperties, Paragraphs = paragraphs });
                    column += span;
                }

                if (rowModel.Cells.Count > 0)
                {
                    model.Rows.Add(rowModel);
                }
            }

            if (model.Rows.Count == 0)
            {
                return;
            }

            var columns = model.Rows.Max(row => row.Cells.Sum(cell => cell.Span));
            var total = tableWidth ?? (options.BorderlessTables ? available : 453.6);
            model.Grid = [.. DistributeColumns(columns, columnWidths, total)];
            _blocks.AddRange(model.Emit());
        }

        /// <summary>The widths of the grid: the cells' own widths where given, the rest shared equally, scaled to <paramref name="total"/>.</summary>
        private static IEnumerable<double> DistributeColumns(int columns, List<double?> given, double total)
        {
            var widths = Enumerable.Range(0, columns).Select(index => index < given.Count ? given[index] : null).ToList();
            var known = widths.Where(width => width is not null).Sum(width => width!.Value);
            var unknown = widths.Count(width => width is null);
            var rest = unknown == 0 ? 0 : Math.Max(18, (total - known) / unknown);
            var result = widths.Select(width => width ?? rest).ToList();
            var sum = result.Sum();
            return sum > total * 1.001 || unknown == 0 ? result.Select(width => width * total / sum) : result;
        }

        /// <summary>A width="…" or CSS width as points (percent of <paramref name="available"/>), at most the available width.</summary>
        private static double? WidthOf(Node node, double available)
        {
            var value = (node["width"] ?? StyleValue(node, "width"))?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.EndsWith('%') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                return Math.Clamp(percent, 1, 100) * available / 100;
            }

            return Length(value) is { } points && points > 0 ? Math.Min(points, available) : null;
        }

        /// <summary>bgcolor="…" or a CSS background(-color) as RRGGBB (white is no shading).</summary>
        private static string? ShadingOf(Node node)
        {
            var fill = Color(node["bgcolor"]) ?? Color(StyleValue(node, "background-color")) ?? Color(StyleValue(node, "background")?.Split(' ')[0]);
            return fill is null or "FFFFFF" ? null : fill;
        }

        /// <summary>The paragraphs a cell got (at least one, no tables inside a cell: their text).</summary>
        private List<Paragraph> Paragraphs()
        {
            Flush();
            var paragraphs = _blocks.OfType<Paragraph>().Select(paragraph => paragraph.WithCell(null)).ToList();
            return paragraphs.Count == 0 ? [Paragraph.Empty()] : paragraphs;
        }

        private void Flush()
        {
            if (_inlines.Count == 0)
            {
                _paragraph = _lists.Count > 0 ? _paragraph : ParagraphProperties.Empty;
                return;
            }

            // Trailing spaces of a paragraph go (HTML shows none).
            if (_inlines[^1] is TextRun last && last.Text.TrimEnd(' ').Length != last.Text.Length)
            {
                var trimmed = last.Text.TrimEnd(' ');
                _inlines[^1] = last with { Text = trimmed };
            }

            var paragraph = _paragraph;
            if (_fill is not null && paragraph.Shading is null)
            {
                paragraph = paragraph with { Shading = new Shading(_fill) };
            }

            if (_quoteDepth > 0)
            {
                // A quoted part of a reply: indented with a bar at the left, one step per level (as mail programs show it)
                paragraph = paragraph with
                {
                    IndentLeft = (paragraph.IndentLeft ?? 0) + (10 * _quoteDepth),
                    Borders = new BorderSet(Left: new BorderLine("single", 1.5, options.QuoteBarColor, 4)),
                };
            }

            _blocks.Add(new Paragraph(_inlines, paragraph));
            _inlines.Clear();
            _paragraph = ParagraphProperties.Empty;
        }

        public DocumentFragment? Finish()
        {
            Flush();
            if (_blocks.Count == 0)
            {
                return null;
            }

            var blocks = _blocks.ToList();
            if (blocks[0] is not Paragraph { Cell: null })
            {
                blocks.Insert(0, Paragraph.Empty());
            }

            if (blocks[^1] is not Paragraph { Cell: null })
            {
                blocks.Add(Paragraph.Empty());
            }

            var styles = BuiltInStyles.CreateDefault();
            foreach (var id in _styles)
            {
                styles = BuiltInStyles.Ensure(styles, id);
            }

            return new DocumentFragment(blocks, styles, _numbering);
        }

        /// <summary>The formatting an element adds: tags (b, i, u, s, sub, sup, a, font) and its style attribute.</summary>
        private static Style Inherit(Node node, Style style)
        {
            var characters = style.Characters;
            switch (node.Tag)
            {
                case "b" or "strong" or "th":
                    characters = characters with { Bold = true };
                    break;
                case "i" or "em" or "cite" or "var":
                    characters = characters with { Italic = true };
                    break;
                case "u" or "ins":
                    characters = characters with { Underline = UnderlineStyle.Single };
                    break;
                case "s" or "strike" or "del":
                    characters = characters with { Strikethrough = true };
                    break;
                case "sub":
                    characters = characters with { VerticalPosition = VerticalPosition.Subscript };
                    break;
                case "sup":
                    characters = characters with { VerticalPosition = VerticalPosition.Superscript };
                    break;
                case "code" or "tt" or "kbd" or "samp":
                    characters = characters.WithFont("Consolas");
                    break;
                case "font":
                    if (node["face"] is { Length: > 0 } face)
                    {
                        characters = characters.WithFont(face.Split(',')[0].Trim().Trim('"', '\''));
                    }

                    if (Color(node["color"]) is { } fontColor)
                    {
                        characters = characters with { Color = TextColor.FromHex(fontColor) };
                    }

                    break;
            }

            if (node["style"] is { } css)
            {
                characters = FromCss(css, characters);
            }

            var link = style.Link;
            if (node.Tag == "a" && node["href"] is { Length: > 0 } href)
            {
                link = Allowed(href) is { } address ? new Hyperlink(address) : null;
                if (link is not null)
                {
                    characters = characters with { StyleId = BuiltInStyles.HyperlinkStyle };
                }
            }

            return style with { Characters = characters, Link = link, Preformatted = style.Preformatted || node.Tag == "pre" };
        }

        private static CharacterProperties FromCss(string css, CharacterProperties characters)
        {
            foreach (var declaration in css.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                var name = declaration[..colon].Trim().ToLowerInvariant();
                var value = declaration[(colon + 1)..].Trim().ToLowerInvariant().Replace("!important", "").Trim();
                characters = name switch
                {
                    "font-weight" => characters with { Bold = value is "bold" or "bolder" || (int.TryParse(value, out var weight) && weight >= 600) },
                    "font-style" => characters with { Italic = value is "italic" or "oblique" },
                    "text-decoration" or "text-decoration-line" => characters with
                    {
                        Underline = value.Contains("underline", StringComparison.Ordinal) ? UnderlineStyle.Single : characters.Underline,
                        Strikethrough = value.Contains("line-through", StringComparison.Ordinal) ? true : characters.Strikethrough,
                    },
                    "color" when Color(value) is { } color => characters with { Color = TextColor.FromHex(color) },
                    "font-size" when Length(value) is { } size && size is > 1 and < 400 => characters with { FontSize = Math.Round(size * 2) / 2 },
                    "font-family" when value.Split(',')[0].Trim().Trim('"', '\'') is { Length: > 0 } family && family is not ("inherit" or "initial") =>
                        characters.WithFont(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(family)),
                    "vertical-align" when value == "super" => characters with { VerticalPosition = VerticalPosition.Superscript },
                    "vertical-align" when value == "sub" => characters with { VerticalPosition = VerticalPosition.Subscript },
                    _ => characters,
                };
            }

            return characters;
        }

        private static string? StyleValue(Node node, string property)
        {
            foreach (var declaration in (node["style"] ?? "").Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0 && declaration[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                {
                    return declaration[(colon + 1)..].Trim();
                }
            }

            return null;
        }

        /// <summary>A length as points: "12pt", "16px" (¾ pt), "1em" (12 pt), a bare number of pixels.</summary>
        private static double? Length(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim().ToLowerInvariant();
            var unit = new string(text.SkipWhile(character => char.IsDigit(character) || character is '.' or '-').ToArray()).Trim();
            if (!double.TryParse(text[..(text.Length - unit.Length)], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            return unit switch
            {
                "pt" => number,
                "px" or "" => number * 0.75,
                "em" or "rem" => number * 12,
                "cm" => number * 72 / 2.54,
                "mm" => number * 72 / 25.4,
                "in" => number * 72,
                _ => null,
            };
        }

        /// <summary>A colour as RRGGBB: #rgb, #rrggbb, rgb(r, g, b) and a few names.</summary>
        private static string? Color(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim().ToLowerInvariant();
            if (text.StartsWith('#'))
            {
                var hex = text[1..];
                if (hex.Length == 3)
                {
                    hex = string.Concat(hex.Select(character => $"{character}{character}"));
                }

                return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : null;
            }

            if (text.StartsWith("rgb", StringComparison.Ordinal))
            {
                var numbers = text[(text.IndexOf('(') + 1)..].TrimEnd(')').Split(',').Select(part => part.Trim()).ToList();
                if (numbers.Count >= 3 && numbers.Take(3).All(part => int.TryParse(part, out _)))
                {
                    return string.Concat(numbers.Take(3).Select(part => Math.Clamp(int.Parse(part, CultureInfo.InvariantCulture), 0, 255).ToString("X2", CultureInfo.InvariantCulture)));
                }

                return null;
            }

            return text switch
            {
                "black" => "000000",
                "white" => "FFFFFF",
                "red" => "FF0000",
                "green" => "008000",
                "blue" => "0000FF",
                "gray" or "grey" => "808080",
                _ => null,
            };
        }

        /// <summary>The address of a link when it may be one (web and mail; javascript:, file: … are dropped).</summary>
        private static string? Allowed(string href)
        {
            var text = href.Trim();
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" ? text : null;
        }

        private static string CollapseSpaces(string text)
        {
            var builder = new StringBuilder(text.Length);
            var space = false;
            foreach (var character in text)
            {
                var isSpace = character is ' ' or '\t' or '\n' or '\r';
                if (isSpace)
                {
                    if (!space)
                    {
                        builder.Append(' ');
                    }

                    space = true;
                    continue;
                }

                builder.Append(character);
                space = false;
            }

            return builder.ToString();
        }

        private static IEnumerable<Node> Descendants(Node node, string tag)
        {
            foreach (var child in node.Children)
            {
                if (child.Tag == tag)
                {
                    yield return child;
                    continue; // a nested table's rows are not this table's
                }

                if (child.Tag != "table")
                {
                    foreach (var found in Descendants(child, tag))
                    {
                        yield return found;
                    }
                }
            }
        }
    }
}

/// <summary>How <see cref="HtmlImport"/> reads HTML: the clipboard (the default) or a mail (<see cref="Mail"/>).</summary>
public sealed record HtmlImportOptions
{
    /// <summary>Pasting: only data: pictures, tables with lines, Word's paragraph spacing.</summary>
    public static HtmlImportOptions Clipboard { get; } = new();

    /// <summary>
    /// A mail: <paramref name="resolveImage"/> answers a picture's src (cid: parts of the message, remote pictures only after consent – null =
    /// left out), layout tables without lines, paragraphs with HTML's spacing, quotes as indented bars, rules as lines, text <paramref name="contentWidth"/> wide.
    /// </summary>
    public static HtmlImportOptions Mail(Func<string, ImageData?>? resolveImage, double contentWidth) => new()
    {
        ResolveImage = resolveImage,
        BorderlessTables = true,
        ParagraphSpacing = true,
        QuotesAsBars = true,
        RulesAsLines = true,
        ContentWidth = contentWidth,
    };

    /// <summary>The picture of a src that is not a data: URI (null = left out). Nothing is fetched by the import itself.</summary>
    public Func<string, ImageData?>? ResolveImage { get; init; }

    /// <summary>Tables without border="1" get no lines (newsletters lay out with tables).</summary>
    public bool BorderlessTables { get; init; }

    /// <summary>A &lt;p&gt; gets space below it as a browser shows it.</summary>
    public bool ParagraphSpacing { get; init; }

    /// <summary>A &lt;blockquote&gt; is indented with a bar at the left per level (a quoted reply), not Word's Zitat style.</summary>
    public bool QuotesAsBars { get; init; }

    /// <summary>A &lt;hr&gt; is a line.</summary>
    public bool RulesAsLines { get; init; }

    /// <summary>The colour of quote bars and rules ("RRGGBB": document data).</summary>
    public string QuoteBarColor { get; init; } = "B4B4B4";

    /// <summary>The width of the text in points: wider pictures and tables are fitted.</summary>
    public double ContentWidth { get; init; } = 6 * 72;
}
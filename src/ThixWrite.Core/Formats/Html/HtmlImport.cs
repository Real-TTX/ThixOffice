using System.Globalization;
using System.Net;
using System.Text;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Html;

/// <summary>
/// Pasting from other programs (browsers, Word, mail): the HTML of the clipboard (Windows "HTML Format" / CF_HTML, macOS
/// public.html) becomes a <see cref="DocumentFragment"/> – paragraphs and headings, bold, italic, underline, strikethrough,
/// sub/superscript, colour, size and font of text, lists, tables, links (only web and mail addresses) and pictures embedded as
/// data: URIs. Sanitized: scripts, styles, frames, objects, forms and comments are dropped; nothing is fetched (a picture from the
/// web is left out), nothing runs. A small tolerant parser – clipboard HTML is rarely well-formed.
/// </summary>
public static partial class HtmlImport
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

    /// <summary>display:none (the last display of its style, also from a style sheet) and Outlook's mso-hide:all: a newsletter's hidden preview text.</summary>
    private static bool IsHidden(Node node)
    {
        if (node.Attributes.ContainsKey("hidden"))
        {
            return true;
        }

        if (node["style"] is null)
        {
            return false;
        }

        return string.Equals(StyleValue(node, "display")?.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim(), "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(StyleValue(node, "mso-hide")?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The value of a CSS property in an element's style (the last declaration wins, as in CSS), or null.</summary>
    private static string? StyleValue(Node node, string property)
    {
        string? found = null;
        foreach (var declaration in (node["style"] ?? "").Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon > 0 && declaration[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                found = declaration[(colon + 1)..].Trim();
            }
        }

        return found;
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
        var sheets = new List<string>();
        var root = ParseTree(html, sheets);
        if (sheets.Count > 0)
        {
            HtmlCss.Apply(root, HtmlCss.ParseSheets(sheets));
        }

        return root;
    }

    private static Node ParseTree(string html, List<string> sheets)
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
                // Its content goes too (a script, a style sheet …): skip to its end tag. A style sheet is read first – only as rules for the
                // elements' looks (HtmlCss: simple selectors, no @media, no url() is ever fetched).
                var closing = html.IndexOf($"</{element.Tag}", index, StringComparison.OrdinalIgnoreCase);
                if (element.Tag == "style" && !tag.EndsWith('/'))
                {
                    sheets.Add(closing < 0 ? html[index..] : html[index..closing]);
                }
                else if (element.Tag == "head")
                {
                    CollectStyleSheets(closing < 0 ? html[index..] : html[index..closing], sheets);
                }

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

    /// <summary>The text of every &lt;style&gt; element in a part that is dropped as a whole (the head).</summary>
    private static void CollectStyleSheets(string part, List<string> sheets)
    {
        var index = 0;
        while (index < part.Length)
        {
            var open = part.IndexOf("<style", index, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                return;
            }

            var tagEnd = FindTagEnd(part, open);
            if (tagEnd < 0)
            {
                return;
            }

            var close = part.IndexOf("</style", tagEnd, StringComparison.OrdinalIgnoreCase);
            sheets.Add(close < 0 ? part[(tagEnd + 1)..] : part[(tagEnd + 1)..close]);
            index = close < 0 ? part.Length : close + 7;
        }
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
        var local = (colon >= 0 ? name[(colon + 1)..] : name).ToLowerInvariant(); // w:… of Word's HTML
        return colon >= 0 && local == "p" ? "o:p" : local; // Word's <o:p> is an inline wrapper, not a paragraph

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

    /// <summary>What every builder of one import shares (cells are built by builders of their own): the lists and the styles used.</summary>
    private sealed class SharedState
    {
        public NumberingDefinitions Numbering { get; set; } = NumberingDefinitions.Empty;

        public HashSet<string> Styles { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Builder(HtmlImportOptions options, SharedState? shared = null)
    {
        private readonly List<Block> _blocks = [];
        private readonly List<Inline> _inlines = [];
        private readonly SharedState _shared = shared ?? new SharedState();
        private ParagraphProperties _paragraph = ParagraphProperties.Empty;
        private readonly List<(ListKind Kind, int Instance)> _lists = [];
        private int _quoteDepth;
        private string? _fill;
        private bool _centerTables;
        private ParagraphAlignment? _inheritedAlignment;

        private NumberingDefinitions Numbering
        {
            get => _shared.Numbering;
            set => _shared.Numbering = value;
        }

        private HashSet<string> UsedStyles => _shared.Styles;

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
                var savedFill = _fill;
                if (options.BorderlessTables && ShadingOf(node) is { } blockFill)
                {
                    _fill = blockFill; // a coloured block of a mail: its paragraphs get its background
                }

                var savedAlignment = _inheritedAlignment;
                _inheritedAlignment = AlignmentOf(node) ?? _inheritedAlignment; // text-align is inherited by what is inside
                _paragraph = ParagraphFor(node, tag);
                Block(node, inner, context);
                Flush();
                _fill = savedFill;
                _inheritedAlignment = savedAlignment;
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
            if (options.ParagraphSpacing && tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                // the margins of its style (inline or from a style sheet: Outlook's p.MsoNormal {margin:0}) as spacing above and below
                var (top, bottom) = Margins(node);
                if (top is { } before)
                {
                    properties = properties with { SpacingBefore = before };
                }

                if (bottom is { } after)
                {
                    properties = properties with { SpacingAfter = after };
                }
            }

            var alignment = AlignmentOf(node);
            if (options.BorderlessTables && BordersOf(node) is { } lines)
            {
                properties = properties with { Borders = lines }; // a line above a quoted header (Outlook's "Von:" block), a bar beside a quote
            }

            return alignment is null ? properties : properties with { Alignment = alignment };
        }

        /// <summary>The text alignment an element sets (text-align, align=, &lt;center&gt;), or null.</summary>
        private static ParagraphAlignment? AlignmentOf(Node node)
        {
            if (node.Tag == "center")
            {
                return ParagraphAlignment.Center;
            }

            return (StyleValue(node, "text-align") ?? node["align"])?.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant() switch
            {
                "center" or "middle" => ParagraphAlignment.Center,
                "right" => ParagraphAlignment.Right,
                "justify" => ParagraphAlignment.Justify,
                "left" => ParagraphAlignment.Left,
                _ => null,
            };
        }

        private ParagraphProperties Styled(string styleId)
        {
            UsedStyles.Add(styleId);
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
                var definition = BuiltInLists.Create(kind, Numbering.NextDefinitionId());
                Numbering = Numbering.WithDefinition(definition);
                instance = Numbering.NextInstanceId();
                Numbering = Numbering.WithInstance(new NumberingInstance { Id = instance, AbstractId = definition.Id });
                _lists[0] = (kind, instance);
            }

            UsedStyles.Add(BuiltInStyles.ListParagraph);
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

            if (options.BorderlessTables && IsWrapper(table, rows))
            {
                // A mail's layout table that only stacks (a centring wrapper, one column of blocks without colours, a frame around other tables):
                // its content as paragraphs, so the text stays text of the mail; tables inside it come out at this level.
                Flush();
                var savedCenter = _centerTables;
                foreach (var cell in rows.SelectMany(Cells).Where(cell => !IsEmptyCell(cell)))
                {
                    _centerTables = savedCenter || (table["align"] ?? "").Equals("center", StringComparison.OrdinalIgnoreCase)
                        || (cell["align"] ?? StyleValue(cell, "text-align") ?? "").Trim().Equals("center", StringComparison.OrdinalIgnoreCase);
                    var savedAlignment = _inheritedAlignment;
                    _inheritedAlignment = AlignmentOf(cell) ?? _inheritedAlignment;
                    Block(cell, Inherit(cell, cell.Tag == "th" ? style with { Characters = style.Characters with { Bold = true } } : style), context);
                    Flush();
                    _inheritedAlignment = savedAlignment;
                }

                _centerTables = savedCenter;
                return;
            }

            if (context == ParagraphContext.Cell && !options.NestedTables)
            {
                // A table inside a cell where tables cannot nest (pasting): its cells' content as paragraphs of the cell.
                Flush();
                foreach (var cell in rows.SelectMany(Cells))
                {
                    var saved = _fill;
                    _fill = ShadingOf(cell) ?? ShadingOf(cell.Parent ?? cell) ?? ShadingOf(table) ?? _fill;
                    Block(cell, Inherit(cell, cell.Tag == "th" ? style with { Characters = style.Characters with { Bold = true } } : style), context);
                    Flush();
                    _fill = saved;
                }

                return;
            }

            var available = options.ContentWidth;
            var built = BuildTable(table, rows, style, available);
            if (built is null)
            {
                return;
            }

            var (blocks, width) = built.Value;
            if (context == ParagraphContext.Cell)
            {
                // A table in a cell of a mail (a newsletter's columns inside its frame): a paragraph carrying it, shown read-only side by side.
                Flush();
                var carrier = new OpaqueInline("", PreservedKind.Table, false, CharacterProperties.Empty, "", Math.Max(1, width)) { Nested = new Story(blocks) };
                _blocks.Add(new Paragraph([carrier], DocxReader.NestedCarrierProperties));
                return;
            }

            _blocks.AddRange(blocks);
        }

        /// <summary>The table as blocks (cells' paragraphs naming it) and the width it takes; null when it has no cells.</summary>
        private (List<Block> Blocks, double Width)? BuildTable(Node table, List<Node> rows, Style style, double available)
        {
            // A table of a mail is usually layout (a newsletter's columns): no lines unless the HTML asks for them.
            var border = int.TryParse(table["border"], NumberStyles.None, CultureInfo.InvariantCulture, out var given) ? given : 0;
            var lines = !options.BorderlessTables || border > 0;
            var styleId = lines ? BuiltInStyles.TableGrid : BuiltInStyles.TableNormal;
            var tableWidth = WidthOf(table, available);
            var isPercent = (table["width"] ?? StyleValue(table, "width"))?.Trim().EndsWith('%') == true;
            var padding = options.BorderlessTables
                ? (int.TryParse(table["cellpadding"], NumberStyles.None, CultureInfo.InvariantCulture, out var tablePadding) ? tablePadding * 0.75 : 0.75)
                : (double?)null;
            var margins = padding is { } pad ? new CellMargins(pad, pad, pad, pad) : null;
            var marginX = padding is { } side ? side * 2 : 10.8;
            var centered = (table["align"] ?? "").Equals("center", StringComparison.OrdinalIgnoreCase)
                || (StyleValue(table, "margin")?.Contains("auto", StringComparison.OrdinalIgnoreCase) ?? false)
                || (_centerTables && options.BorderlessTables);
            var styleIdUsed = styleId;
            UsedStyles.Add(styleIdUsed);

            // the widths the cells are built for: given widths, the rest shared (a picture or an inner table is fitted to them)
            var columnCount = rows.Max(row => Cells(row).Sum(SpanOf));
            var givenWidths = new double?[columnCount];
            foreach (var row in rows)
            {
                var column = 0;
                foreach (var cell in Cells(row))
                {
                    if (SpanOf(cell) == 1 && column < columnCount && WidthOf(cell, tableWidth ?? available) is { } cellWidth)
                    {
                        givenWidths[column] ??= cellWidth;
                    }

                    column += SpanOf(cell);
                }
            }

            var room = tableWidth ?? available;
            var unknownCount = givenWidths.Count(width => width is null);
            var shared = unknownCount == 0 ? 0 : Math.Max(18, (room - givenWidths.Sum(width => width ?? 0)) / unknownCount);
            var model = new TableEdits.TableModel
            {
                Properties = new TableProperties
                {
                    StyleId = styleIdUsed,
                    Width = TableWidth.Auto,
                    Shading = ShadingOf(table) is { } fill ? new Shading(fill) : null,
                    Alignment = centered ? TableAlignment.Center : null,
                    CellMargins = margins,
                    Indent = padding, // the table's edge (not its first text) lines up with the text, as in a browser
                },
            };
            var natural = new double[columnCount];
            foreach (var row in rows)
            {
                var rowModel = new TableEdits.RowModel { Properties = TableRowProperties.Empty };
                if ((Length(row["height"]) ?? Length(StyleValue(row, "height"))) is > 0 and var rowHeight)
                {
                    rowModel.Properties = rowModel.Properties with { Height = rowHeight };
                }

                var column = 0;
                foreach (var cell in Cells(row))
                {
                    var span = SpanOf(cell);
                    var cellPadding = options.BorderlessTables ? Sides(cell, "padding") : [null, null, null, null];
                    var paddingX = (cellPadding[1] ?? padding ?? 5.4) + (cellPadding[3] ?? padding ?? 5.4);
                    var builtFor = Enumerable.Range(column, Math.Min(span, Math.Max(0, columnCount - column))).Sum(index => givenWidths[index] ?? shared);
                    var cellBuilder = new Builder(options with { ContentWidth = Math.Max(36, builtFor - paddingX) }, _shared)
                    {
                        _centerTables = (cell["align"] ?? "").Equals("center", StringComparison.OrdinalIgnoreCase),
                        _inheritedAlignment = AlignmentOf(cell) ?? AlignmentOf(row), // not across a table: a centring frame does not centre the text of the tables it holds
                    };
                    var cellStyle = Inherit(cell, cell.Tag == "th" ? style with { Characters = style.Characters with { Bold = true } } : style);
                    cellBuilder.Block(cell, cellStyle, ParagraphContext.Cell);
                    var paragraphs = cellBuilder.Paragraphs();
                    var cellProperties = TableCellProperties.Empty with { GridSpan = span };
                    if ((ShadingOf(cell) ?? ShadingOf(row)) is { } cellFill)
                    {
                        cellProperties = cellProperties with { Shading = new Shading(cellFill) };
                    }

                    if (cellPadding.Any(side => side is not null))
                    {
                        cellProperties = cellProperties with { Margins = new CellMargins(cellPadding[0], cellPadding[3], cellPadding[2], cellPadding[1]) };
                    }

                    if (options.BorderlessTables && BordersOf(cell) is { } cellLines)
                    {
                        cellProperties = cellProperties with { Borders = cellLines };
                    }

                    var vertical = (StyleValue(cell, "vertical-align") ?? cell["valign"] ?? row["valign"])?.Trim().ToLowerInvariant();
                    cellProperties = vertical switch
                    {
                        "middle" or "center" => cellProperties with { VerticalAlignment = CellVerticalAlignment.Center },
                        "bottom" => cellProperties with { VerticalAlignment = CellVerticalAlignment.Bottom },
                        "top" => cellProperties,
                        // a browser centres a cell's content when nothing is said (mail layouts say valign="top" where they mean it)
                        _ when options.BorderlessTables => cellProperties with { VerticalAlignment = CellVerticalAlignment.Center },
                        _ => cellProperties,
                    };
                    if (span == 1 && column < columnCount)
                    {
                        natural[column] = Math.Max(natural[column], NaturalWidth(paragraphs) + paddingX);
                    }

                    if ((Length(cell["height"]) ?? Length(StyleValue(cell, "height"))) is > 0 and var cellHeight && (rowModel.Properties.Height ?? 0) < cellHeight)
                    {
                        rowModel.Properties = rowModel.Properties with { Height = cellHeight };
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
                return null;
            }

            var columns = model.Rows.Max(row => row.Cells.Sum(cell => cell.Span));
            List<double> grid;
            if (!options.BorderlessTables)
            {
                grid = [.. DistributeColumns(columns, [.. givenWidths], tableWidth ?? 453.6)];
            }
            else
            {
                grid = MailColumns(columns, givenWidths, natural, tableWidth, available, marginX);
            }

            model.Grid = grid;
            var total = grid.Sum();
            if (options.BorderlessTables && (isPercent || tableWidth is not null) && available > 0)
            {
                // a frame of 100 % (or 600 px in a narrower pane) follows the width it is shown in
                model.Properties = model.Properties with { Width = TableWidth.Percent(Math.Min(100, total * 100 / available)) };
            }
            else if (options.BorderlessTables)
            {
                model.Properties = model.Properties with { Width = TableWidth.Points(total) };
            }
            else
            {
                model.Properties = model.Properties with { Width = tableWidth is { } points ? TableWidth.Points(points) : TableWidth.Auto };
            }

            return ([.. model.Emit()], total);
        }

        /// <summary>
        /// The widths of a mail table's columns as a browser gives them: a table without a width is as wide as its content (given cell widths, else
        /// the content's natural width), at most the room; a table with a width shares what the given cells leave by the content of the others.
        /// </summary>
        private static List<double> MailColumns(int columns, double?[] given, double[] natural, double? tableWidth, double available, double marginX)
        {
            var widths = new double[columns];
            for (var index = 0; index < columns; index++)
            {
                var content = index < natural.Length ? Math.Max(natural[index], marginX + 6) : marginX + 6;
                widths[index] = index < given.Length && given[index] is { } fixedWidth ? Math.Max(fixedWidth, 6) : Math.Min(content, available);
            }

            var target = tableWidth ?? Math.Min(widths.Sum(), available);
            var fixedSum = Enumerable.Range(0, columns).Where(index => index < given.Length && given[index] is not null).Sum(index => widths[index]);
            var freeIndexes = Enumerable.Range(0, columns).Where(index => index >= given.Length || given[index] is null).ToList();
            if (tableWidth is not null && freeIndexes.Count > 0 && target - fixedSum > 0)
            {
                // what the given cells leave goes to the others by their content
                var free = target - fixedSum;
                var weights = freeIndexes.Sum(index => widths[index]);
                foreach (var index in freeIndexes)
                {
                    widths[index] = weights > 0 ? free * widths[index] / weights : free / freeIndexes.Count;
                }
            }

            var sum = widths.Sum();
            if (sum > 0 && (sum > target * 1.001 || (tableWidth is not null && sum < target * 0.999)))
            {
                var scale = target / sum;
                for (var index = 0; index < columns; index++)
                {
                    widths[index] *= scale;
                }
            }

            return [.. widths];
        }

        /// <summary>The width a cell's content wants on one line (text by an average letter width, pictures, inner tables).</summary>
        private static double NaturalWidth(IEnumerable<Paragraph> paragraphs)
        {
            double widest = 0;
            foreach (var paragraph in paragraphs)
            {
                var size = paragraph.Properties.StyleId switch
                {
                    BuiltInStyles.Heading1 => 18,
                    BuiltInStyles.Heading2 => 15,
                    BuiltInStyles.Heading3 => 13,
                    _ => 11.0,
                };
                double line = 0;
                foreach (var inline in paragraph.Inlines)
                {
                    switch (inline)
                    {
                        case TextRun run:
                            var runSize = run.Properties.FontSize ?? size;
                            var bold = run.Properties.Bold == true ? 1.07 : 1;
                            foreach (var character in run.Text)
                            {
                                if (character == SpecialCharacters.LineBreak)
                                {
                                    widest = Math.Max(widest, line);
                                    line = 0;
                                    continue;
                                }

                                line += runSize * bold * (character switch
                                {
                                    ' ' or ' ' => 0.28,
                                    _ when char.IsUpper(character) => 0.66,
                                    _ when char.IsDigit(character) => 0.56,
                                    'i' or 'l' or 'j' or 't' or 'f' or 'r' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' => 0.3,
                                    'm' or 'w' or '@' or '€' => 0.8,
                                    _ => 0.52,
                                });
                            }

                            break;
                        case ImageInline image:
                            line += image.Width;
                            break;
                        case OpaqueInline { Nested: not null } nested:
                            line += nested.Width;
                            break;
                    }
                }

                widest = Math.Max(widest, line + (paragraph.Properties.IndentLeft ?? 0) + 2);
            }

            return widest;
        }

        /// <summary>
        /// True for a layout table that only stacks blocks: every row has at most one cell with content, and no such cell (nor its row, nor the
        /// table) has a colour or a border of its own – or every content cell holds nothing but other tables (a frame, its colour only around them).
        /// </summary>
        private static bool IsWrapper(Node table, List<Node> rows)
        {
            var contentCells = new List<Node>();
            foreach (var row in rows)
            {
                var content = Cells(row).Where(cell => !IsEmptyCell(cell)).ToList();
                if (content.Count > 1)
                {
                    return false;
                }

                contentCells.AddRange(content);
            }

            if (contentCells.Count == 0)
            {
                return true;
            }

            if (contentCells.All(HoldsOnlyTables))
            {
                return true;
            }

            var border = int.TryParse(table["border"], NumberStyles.None, CultureInfo.InvariantCulture, out var given) ? given : 0;
            return border == 0 && ShadingOf(table) is null && StyleValue(table, "border") is null
                && contentCells.All(cell => ShadingOf(cell) is null && (cell.Parent is null || ShadingOf(cell.Parent) is null) && !HasBorder(cell));

            static bool HasBorder(Node cell) =>
                new[] { "border", "border-left", "border-right", "border-top", "border-bottom" }
                    .Any(property => StyleValue(cell, property) is { } value && !value.Contains("none", StringComparison.OrdinalIgnoreCase) && !value.StartsWith('0'));
        }

        /// <summary>A cell that shows nothing (a spacer): no text but spaces, no picture larger than a pixel, no table with content.</summary>
        private static bool IsEmptyCell(Node cell) => !HasContent(cell);

        private static bool HasContent(Node node)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    if (child.Text!.Any(character => !char.IsWhiteSpace(character) && character != ' ' && character != '‌' && character != '​'))
                    {
                        return true;
                    }

                    continue;
                }

                if (IsHidden(child))
                {
                    continue;
                }

                if (child.Tag == "img")
                {
                    var width = Length(child["width"]) ?? Length(StyleValue(child, "width"));
                    var height = Length(child["height"]) ?? Length(StyleValue(child, "height"));
                    if (!(width is <= 2 && height is <= 2))
                    {
                        return true;
                    }

                    continue;
                }

                if (child.Tag == "hr" || HasContent(child))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A cell whose content is only tables (and spaces, hidden parts, tracking pixels).</summary>
        private static bool HoldsOnlyTables(Node cell)
        {
            var tables = 0;
            foreach (var child in cell.Children)
            {
                if (child.Tag == "table")
                {
                    tables++;
                    continue;
                }

                if (child.Tag is "center" or "div" && !HasDirectContent(child))
                {
                    tables += Descendants(child, "table").Count();
                    continue;
                }

                if (HasContent(new Node("#x") { Children = { child } }))
                {
                    return false;
                }
            }

            return tables > 0;

            static bool HasDirectContent(Node node) => node.Children.Any(child => child.Tag != "table" && (child.IsText ? !string.IsNullOrWhiteSpace(child.Text) : child.Tag is not ("div" or "center") || HasDirectContent(child)));
        }

        private static IEnumerable<Node> Cells(Node row) => row.Children.Where(child => child.Tag is "td" or "th");

        private static int SpanOf(Node cell) => int.TryParse(cell["colspan"], NumberStyles.None, CultureInfo.InvariantCulture, out var colspan) ? Math.Clamp(colspan, 1, 63) : 1;

        /// <summary>The four sides (top, right, bottom, left) of margin or padding in points, from the shorthand and the single sides in order.</summary>
        private static double?[] Sides(Node node, string property)
        {
            var sides = new double?[4];
            foreach (var declaration in (node["style"] ?? "").Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var name = declaration[..colon].Trim().ToLowerInvariant();
                var value = declaration[(colon + 1)..].Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (name == property)
                {
                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Length).ToArray();
                    if (parts.Length is 0 or > 4)
                    {
                        continue;
                    }

                    var (top, right, bottom, left) = parts.Length switch
                    {
                        1 => (parts[0], parts[0], parts[0], parts[0]),
                        2 => (parts[0], parts[1], parts[0], parts[1]),
                        3 => (parts[0], parts[1], parts[2], parts[1]),
                        _ => (parts[0], parts[1], parts[2], parts[3]),
                    };
                    sides = [top, right, bottom, left];
                }
                else if (name.StartsWith(property + "-", StringComparison.Ordinal))
                {
                    var index = name[(property.Length + 1)..] switch
                    {
                        "top" => 0,
                        "right" => 1,
                        "bottom" => 2,
                        "left" => 3,
                        _ => -1,
                    };
                    if (index >= 0)
                    {
                        sides[index] = Length(value);
                    }
                }
            }

            return sides;
        }

        /// <summary>The CSS borders of an element (border, border-top/right/bottom/left: width, style, colour in any order), or null when it has none.</summary>
        private static BorderSet? BordersOf(Node node)
        {
            var sides = new BorderLine?[4];
            var any = false;
            foreach (var declaration in (node["style"] ?? "").Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var name = declaration[..colon].Trim().ToLowerInvariant();
                int[] indexes = name switch
                {
                    "border" => [0, 1, 2, 3],
                    "border-top" => [0],
                    "border-right" => [1],
                    "border-bottom" => [2],
                    "border-left" => [3],
                    _ => [],
                };
                if (indexes.Length == 0)
                {
                    continue;
                }

                var line = BorderLineOf(declaration[(colon + 1)..]);
                foreach (var index in indexes)
                {
                    sides[index] = line;
                }

                any = true;
            }

            return any && sides.Any(side => side is not null) ? new BorderSet(Top: sides[0], Right: sides[1], Bottom: sides[2], Left: sides[3]) : null;
        }

        private static BorderLine? BorderLineOf(string value)
        {
            double width = 0.75;
            string? color = "000000";
            var style = "single";
            foreach (var token in value.Replace("!important", "", StringComparison.OrdinalIgnoreCase).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var lower = token.ToLowerInvariant();
                if (lower is "none" or "hidden")
                {
                    return null;
                }

                if (lower is "solid" or "dashed" or "dotted" or "double")
                {
                    style = lower switch { "dashed" => "dashed", "dotted" => "dotted", "double" => "double", _ => "single" };
                }
                else if (Color(lower) is { } found)
                {
                    color = found;
                }
                else if (Length(lower) is { } length)
                {
                    width = length;
                }
            }

            return width <= 0 ? null : new BorderLine(style, Math.Min(width, 6), color);
        }

        private static (double? Top, double? Bottom) Margins(Node node)
        {
            var sides = Sides(node, "margin");
            return (sides[0] is { } top ? Math.Clamp(top, 0, 72) : null, sides[2] is { } bottom ? Math.Clamp(bottom, 0, 72) : null);
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
            var value = (StyleValue(node, "width") ?? node["width"])?.Trim();
            if (string.IsNullOrEmpty(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
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

            // Trailing spaces of a paragraph go (HTML shows none), and so does a last <br>: a browser ends the block there without a blank line –
            // a block of nothing but one <br> is one empty line.
            if (_inlines[^1] is TextRun last && last.Text.TrimEnd(' ').Length != last.Text.Length)
            {
                var trimmed = last.Text.TrimEnd(' ');
                _inlines[^1] = last with { Text = trimmed };
            }

            if (options.ParagraphSpacing && _inlines[^1] is TextRun { Text.Length: > 0 } ending && ending.Text[^1] == SpecialCharacters.LineBreak)
            {
                var shorter = ending.Text[..^1].TrimEnd(' ');
                if (shorter.Length > 0)
                {
                    _inlines[^1] = ending with { Text = shorter };
                }
                else
                {
                    _inlines.RemoveAt(_inlines.Count - 1);
                }
            }

            _inlines.RemoveAll(inline => inline is TextRun { Text.Length: 0 });

            var paragraph = _paragraph;
            if (paragraph.Alignment is null && _inheritedAlignment is { } inherited && options.BorderlessTables)
            {
                paragraph = paragraph with { Alignment = inherited };
            }
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
            foreach (var id in UsedStyles)
            {
                styles = BuiltInStyles.Ensure(styles, id);
            }

            return new DocumentFragment(blocks, styles, Numbering);
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
                        Underline = value.Contains("underline", StringComparison.Ordinal) ? UnderlineStyle.Single
                            : value.Contains("none", StringComparison.Ordinal) ? UnderlineStyle.None : characters.Underline,
                        Strikethrough = value.Contains("line-through", StringComparison.Ordinal) ? true : characters.Strikethrough,
                    },
                    "color" when Color(value) is { } color => characters with { Color = TextColor.FromHex(color) },
                    "font-size" when Length(value) is { } size && size is > 0.5 and < 400 => characters with { FontSize = Math.Max(1, Math.Round(size * 2) / 2) },
                    "font-family" when value.Split(',')[0].Trim().Trim('"', '\'') is { Length: > 0 } family && family is not ("inherit" or "initial") =>
                        characters.WithFont(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(family)),
                    "text-transform" => characters with { Caps = value == "uppercase" ? true : value == "none" ? null : characters.Caps },
                    "font-variant" when value == "small-caps" => characters with { SmallCaps = true },
                    "vertical-align" when value == "super" => characters with { VerticalPosition = VerticalPosition.Superscript },
                    "vertical-align" when value == "sub" => characters with { VerticalPosition = VerticalPosition.Subscript },
                    _ => characters,
                };
            }

            return characters;
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
                "ex" => number * 6,
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
        NestedTables = true,
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

    /// <summary>A table in a table cell stays a table (shown read-only beside its neighbours) instead of becoming the cell's paragraphs.</summary>
    public bool NestedTables { get; init; }

    /// <summary>A &lt;hr&gt; is a line.</summary>
    public bool RulesAsLines { get; init; }

    /// <summary>The colour of quote bars and rules ("RRGGBB": document data).</summary>
    public string QuoteBarColor { get; init; } = "B4B4B4";

    /// <summary>The width of the text in points: wider pictures and tables are fitted.</summary>
    public double ContentWidth { get; init; } = 6 * 72;
}
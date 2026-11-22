using System.Globalization;
using System.Net;
using System.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Html;

/// <summary>
/// A document as HTML (ThixMail sends what its composer wrote as text/html): paragraphs and headings, bold, italic, underline, strikethrough, sub/
/// superscript, colour, size and font of text, highlight, alignment and indents, lists (bulleted and numbered, nested), links (only web and mail
/// addresses), tables with their cells and pictures – each picture's src comes from the caller (a cid: of the message's part). Inline styles only, so
/// every mail program shows it the same; the text a reader sees is HTML-encoded, nothing of the document runs.
/// </summary>
public static class HtmlExport
{
    /// <summary>The body of <paramref name="document"/> as an HTML document; <paramref name="pictureSource"/> names each picture's src.</summary>
    public static string Export(Document document, Func<ImageData, string> pictureSource, string? defaultFont = null, double defaultSize = 11)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body style=\"font-family:")
            .Append(Css(defaultFont ?? "Calibri")).Append(",Arial,sans-serif;font-size:").Append(Number(defaultSize)).Append("pt\">");
        var writer = new Writer(document, pictureSource, html);
        writer.Blocks(document.Blocks);
        html.Append("</body></html>");
        return html.ToString();
    }

    private sealed class Writer(Document document, Func<ImageData, string> pictureSource, StringBuilder html)
    {
        private readonly List<(int Numbering, int Level, bool Ordered)> _open = [];

        public void Blocks(IReadOnlyList<Block> blocks)
        {
            for (var index = 0; index < blocks.Count; index++)
            {
                if (blocks[index] is not Paragraph paragraph)
                {
                    continue;
                }

                if (paragraph.Cell is { } cell)
                {
                    CloseLists(0);
                    var end = index;
                    while (end + 1 < blocks.Count && blocks[end + 1] is Paragraph { Cell: { } next } && ReferenceEquals(next.Table, cell.Table))
                    {
                        end++;
                    }

                    Table(cell.Table, blocks.Skip(index).Take(end - index + 1).Cast<Paragraph>().ToList());
                    index = end;
                    continue;
                }

                Paragraph(paragraph, index == blocks.Count - 1);
            }

            CloseLists(0);
        }

        private void Paragraph(Paragraph paragraph, bool isLast)
        {
            var properties = paragraph.Properties;
            var style = properties.StyleId is { } id ? document.Styles.Find(id) : null;
            if (properties.Numbering is { IsNone: false } numbering && document.Numbering.ResolveLevel(numbering.NumberingId, numbering.Level) is { } level)
            {
                OpenList(numbering.NumberingId, numbering.Level, level.Format != NumberFormat.Bullet);
                html.Append("<li").Append(BlockStyle(properties, inList: true)).Append('>');
                Inlines(paragraph);
                html.Append("</li>");
                return;
            }

            CloseLists(0);
            if (isLast && paragraph.Length == 0)
            {
                return; // the document's closing empty paragraph
            }

            if (paragraph.Inlines is [OpaqueInline { Nested: { } nested }])
            {
                // a table shown inside a table cell (a quoted newsletter): written as the table it is
                new Writer(document, pictureSource, html).Blocks(nested.Blocks.Take(nested.Blocks.Count - 1).ToList());
                return;
            }

            var tag = (properties.StyleId ?? style?.Id) switch
            {
                BuiltInStyles.Heading1 or BuiltInStyles.Title => "h1",
                BuiltInStyles.Heading2 or BuiltInStyles.Subtitle => "h2",
                BuiltInStyles.Heading3 => "h3",
                BuiltInStyles.Quote or BuiltInStyles.IntenseQuote => "blockquote",
                _ => "p",
            };
            html.Append('<').Append(tag).Append(BlockStyle(properties, inList: false)).Append('>');
            if (paragraph.Length == 0)
            {
                html.Append("<br>");
            }
            else
            {
                Inlines(paragraph);
            }

            html.Append("</").Append(tag).Append('>');
        }

        private static string BlockStyle(ParagraphProperties properties, bool inList)
        {
            var css = new List<string> { "margin:0" };
            if (properties.Alignment is { } alignment && alignment != ParagraphAlignment.Left)
            {
                css.Add("text-align:" + alignment switch
                {
                    ParagraphAlignment.Center => "center",
                    ParagraphAlignment.Right => "right",
                    _ => "justify",
                });
            }

            if (!inList && properties.IndentLeft is > 0 and var left)
            {
                css.Add($"margin-left:{Number(left)}pt");
            }

            if (properties.SpacingAfter is > 0 and var after)
            {
                css.Add($"margin-bottom:{Number(after)}pt");
            }

            if (properties.SpacingBefore is > 0 and var before)
            {
                css.Add($"margin-top:{Number(before)}pt");
            }

            return $" style=\"{string.Join(';', css)}\"";
        }

        private void OpenList(int numbering, int level, bool ordered)
        {
            while (_open.Count > 0 && (_open[^1].Level > level || (_open[^1].Level == level && (_open[^1].Numbering != numbering || _open[^1].Ordered != ordered))))
            {
                CloseLast();
            }

            while (_open.Count == 0 || _open[^1].Level < level)
            {
                var nextLevel = _open.Count == 0 ? Math.Min(level, 0) : _open[^1].Level + 1;
                if (_open.Count == 0 && level > 0)
                {
                    nextLevel = 0;
                }

                html.Append(ordered ? "<ol" : "<ul").Append(" style=\"margin-top:0;margin-bottom:0\">");
                _open.Add((numbering, nextLevel, ordered));
                if (nextLevel >= level)
                {
                    break;
                }
            }
        }

        private void CloseLists(int keep)
        {
            while (_open.Count > keep)
            {
                CloseLast();
            }
        }

        private void CloseLast()
        {
            html.Append(_open[^1].Ordered ? "</ol>" : "</ul>");
            _open.RemoveAt(_open.Count - 1);
        }

        private void Inlines(Paragraph paragraph)
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (inline.Revision is { IsRemoval: true })
                {
                    continue;
                }

                var opened = OpenLink(inline.Link);
                switch (inline)
                {
                    case TextRun run:
                        Run(run.Text, run.Properties);
                        break;
                    case ImageInline image:
                        html.Append("<img src=\"").Append(Attribute(pictureSource(image.Image))).Append("\" width=\"")
                            .Append(Number(Math.Round(image.Width * 4 / 3))).Append("\" height=\"").Append(Number(Math.Round(image.Height * 4 / 3)))
                            .Append("\" alt=\"").Append(Attribute(image.Description ?? "")).Append("\" style=\"border:0\">");
                        break;
                    case OpaqueInline { Nested: { } nested }:
                        new Writer(document, pictureSource, html).Blocks(nested.Blocks.Take(nested.Blocks.Count - 1).ToList());
                        break;
                    case OpaqueInline { DisplayText.Length: > 0 } opaque:
                        Run(opaque.DisplayText, opaque.Properties);
                        break;
                }

                if (opened)
                {
                    html.Append("</a>");
                }
            }
        }

        private bool OpenLink(Hyperlink? link)
        {
            if (link?.Uri is not { } uri || !Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https" or "mailto"))
            {
                return false;
            }

            html.Append("<a href=\"").Append(Attribute(uri.Trim())).Append("\">");
            return true;
        }

        private void Run(string text, CharacterProperties properties)
        {
            var css = new List<string>();
            if (properties.FontFamily is { Length: > 0 } font)
            {
                css.Add($"font-family:{Css(font)}");
            }

            if (properties.FontSize is { } size)
            {
                css.Add($"font-size:{Number(size)}pt");
            }

            if (properties.Color is { IsAuto: false } color && color.Value.Length == 6)
            {
                css.Add($"color:#{color.Value}");
            }

            if (properties.Highlight is { } highlight and not HighlightColor.None)
            {
                css.Add($"background-color:#{Layout.DisplayListBuilder.ColorOf(highlight).ToHex()}");
            }

            var tags = new List<string>();
            if (properties.Bold == true)
            {
                tags.Add("b");
            }

            if (properties.Italic == true)
            {
                tags.Add("i");
            }

            if (properties.Underline is { } underline && underline != UnderlineStyle.None)
            {
                tags.Add("u");
            }

            if (properties.Strikethrough == true)
            {
                tags.Add("s");
            }

            if (properties.VerticalPosition == VerticalPosition.Superscript)
            {
                tags.Add("sup");
            }
            else if (properties.VerticalPosition == VerticalPosition.Subscript)
            {
                tags.Add("sub");
            }

            if (css.Count > 0)
            {
                html.Append("<span style=\"").Append(string.Join(';', css)).Append("\">");
            }

            foreach (var tag in tags)
            {
                html.Append('<').Append(tag).Append('>');
            }

            Text(text);
            for (var index = tags.Count - 1; index >= 0; index--)
            {
                html.Append("</").Append(tags[index]).Append('>');
            }

            if (css.Count > 0)
            {
                html.Append("</span>");
            }
        }

        private void Text(string text)
        {
            foreach (var character in text)
            {
                switch (character)
                {
                    case SpecialCharacters.LineBreak or SpecialCharacters.PageBreak:
                        html.Append("<br>");
                        break;
                    case SpecialCharacters.Tab:
                        html.Append("&emsp;");
                        break;
                    case SpecialCharacters.Object:
                        break;
                    default:
                        html.Append(WebUtility.HtmlEncode(character.ToString()));
                        break;
                }
            }
        }

        private void Table(Table table, IReadOnlyList<Paragraph> paragraphs)
        {
            var lined = table.Properties.StyleId is null or BuiltInStyles.TableGrid || table.Properties.Borders is not null;
            html.Append("<table cellpadding=\"4\" cellspacing=\"0\"").Append(lined ? " border=\"1\" style=\"border-collapse:collapse\"" : " border=\"0\"").Append('>');
            for (var row = 0; row < table.Rows.Count; row++)
            {
                html.Append("<tr>");
                var cells = table.Rows[row].Cells;
                for (var cell = 0; cell < cells.Count; cell++)
                {
                    var properties = cells[cell];
                    if (properties.VerticalMerge == VerticalMerge.Continue)
                    {
                        continue;
                    }

                    html.Append("<td valign=\"top\"");
                    var firstColumn = table.GridColumnOf(row, cell);
                    var cellWidth = table.Grid.Skip(firstColumn).Take(Math.Max(1, properties.GridSpan)).Sum();
                    if (cellWidth > 0)
                    {
                        html.Append(" width=\"").Append(Number(Math.Round(cellWidth * 4 / 3))).Append('"');
                    }

                    if (properties.GridSpan > 1)
                    {
                        html.Append(" colspan=\"").Append(properties.GridSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                    }

                    if (properties.Shading?.Fill is { Length: 6 } fill && !fill.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        html.Append(" style=\"background-color:#").Append(fill).Append('"');
                    }

                    html.Append('>');
                    var content = paragraphs.Where(p => p.Cell is { } at && at.Row == row && at.Cell == cell).ToList();
                    for (var index = 0; index < content.Count; index++)
                    {
                        Paragraph(content[index].WithCell(null), isLast: false);
                    }

                    CloseLists(0);
                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</table>");
        }
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Css(string family) => family.Contains(' ', StringComparison.Ordinal) ? $"'{family.Replace("'", "", StringComparison.Ordinal)}'" : family;

    private static string Attribute(string value) => WebUtility.HtmlEncode(value);
}

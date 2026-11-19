using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Formats.Html;

/// <summary>
/// Cells as an HTML table for other programs (Word, a mail, a browser: displayed values with bold/italic, colours, fills,
/// alignment and column widths) and the first table of HTML from another program as rows of texts (a browser's or Word's
/// table pasted into the grid; each text is then read like typed input). Also the clipboard envelope of Windows ("HTML
/// Format" with its byte offsets). Tolerant, nothing is fetched, scripts and styles are dropped.
/// </summary>
public static partial class HtmlTables
{
    /// <summary>The cells of <paramref name="range"/> (bounded to the used range) as a &lt;table&gt;.</summary>
    public static string Export(Worksheet sheet, CellRange range, CalcLocale locale)
    {
        var used = sheet.UsedRange ?? CellRange.Cell(0, 0);
        range = range.Intersect(new CellRange(0, 0, Math.Max(used.LastRow, range.FirstRow), Math.Max(used.LastColumn, range.FirstColumn))) ?? CellRange.Cell(range.First);
        var html = new StringBuilder("<table style=\"border-collapse:collapse\">");
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            if (sheet.Rows.IsHidden(row))
            {
                continue;
            }

            html.Append("<tr>");
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (sheet.Columns.IsHidden(column))
                {
                    continue;
                }

                var merge = sheet.Merges.Find(row, column);
                if (merge is { } covered && covered.First != new CellAddress(row, column))
                {
                    continue;
                }

                var style = sheet.GetStyle(row, column);
                var value = sheet.GetValue(row, column);
                var text = NumberFormatter.Format(value, style.NumberFormat, locale, sheet.Workbook.Date1904).Text;
                html.Append("<td");
                if (merge is { } span)
                {
                    html.Append(CultureInfo.InvariantCulture, $" colspan=\"{span.ColumnCount}\" rowspan=\"{span.RowCount}\"");
                }

                html.Append(" style=\"").Append(Css(style, value, sheet.Workbook.Theme)).Append("\">");
                html.Append(WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal));
                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        return html.Append("</table>").ToString();
    }

    private static string Css(CellStyle style, CellValue value, ThemeColors theme)
    {
        var css = new StringBuilder();
        css.Append(CultureInfo.InvariantCulture, $"font-family:{style.Font.Name};font-size:{style.Font.Size}pt;");
        if (style.Font.Bold)
        {
            css.Append("font-weight:bold;");
        }

        if (style.Font.Italic)
        {
            css.Append("font-style:italic;");
        }

        if (style.Font.Underline != UnderlineKind.None)
        {
            css.Append("text-decoration:underline;");
        }

        if (style.Font.Color is { } color)
        {
            css.Append("color:#").Append(Hex(color.Resolve(theme, RgbColor.Black))).Append(';');
        }

        if (style.Fill.Pattern == FillPattern.Solid && (style.Fill.Foreground ?? style.Fill.Background) is { } fill)
        {
            css.Append("background:#").Append(Hex(fill.Resolve(theme, RgbColor.White))).Append(';');
        }

        var align = style.Alignment.Horizontal switch
        {
            HorizontalAlignment.Center => "center",
            HorizontalAlignment.Right => "right",
            HorizontalAlignment.Left => "left",
            _ => value.IsNumber ? "right" : null,
        };
        if (align is not null)
        {
            css.Append("text-align:").Append(align).Append(';');
        }

        return css.ToString();
    }

    private static string Hex(RgbColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Windows' clipboard envelope "HTML Format" (Version, StartHTML … byte offsets) around a fragment.</summary>
    public static string WrapForClipboard(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var prefix = "<html><body><!--StartFragment-->";
        var suffix = "<!--EndFragment--></body></html>";
        var headerLength = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        var startHtml = headerLength;
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(CultureInfo.InvariantCulture, header, startHtml, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
    }

    /// <summary>
    /// The rows of the first &lt;table&gt; of <paramref name="html"/> (a Windows envelope is stripped) as texts; cells spanning
    /// columns leave empty cells after them. Null when there is no table.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>>? Import(string html)
    {
        var start = html.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var end = html.IndexOf("</table>", start, StringComparison.OrdinalIgnoreCase);
        var table = end < 0 ? html[start..] : html[start..end];
        table = Dropped().Replace(table, string.Empty);
        var rows = new List<IReadOnlyList<string>>();
        foreach (Match row in RowPattern().Matches(table))
        {
            var cells = new List<string>();
            foreach (Match cell in CellPattern().Matches(row.Groups[1].Value))
            {
                var text = cell.Groups[2].Value;
                text = BreakPattern().Replace(text, "\n");
                text = ParagraphEnd().Replace(text, "\n");
                text = TagPattern().Replace(text, string.Empty);
                text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
                text = Whitespace().Replace(text, " ").Trim();
                cells.Add(text);
                var span = SpanPattern().Match(cell.Groups[1].Value);
                if (span.Success && int.TryParse(span.Groups[1].Value, out var columns))
                {
                    for (var extra = 1; extra < Math.Min(columns, 1000); extra++)
                    {
                        cells.Add(string.Empty);
                    }
                }
            }

            rows.Add(cells);
        }

        return rows.Count == 0 ? null : rows;
    }

    /// <summary>Rows as tab-separated text (what <see cref="Editing.EditingSession.PasteText"/> reads).</summary>
    public static string ToTabText(IReadOnlyList<IReadOnlyList<string>> rows) =>
        string.Join("\r\n", rows.Select(row => string.Join('\t', row.Select(Quote))));

    private static string Quote(string text) => text.Contains('\t') || text.Contains('\n') || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    [GeneratedRegex(@"<(script|style|head)[^>]*>.*?</\1>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<tr[^>]*>(.*?)(?=<tr|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"<t[dh]([^>]*)>(.*?)(?=<t[dh][\s>]|</tr|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpanPattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakPattern();

    [GeneratedRegex(@"</p>\s*(?=.)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParagraphEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[ \t\r]+")]
    private static partial Regex Whitespace();
}

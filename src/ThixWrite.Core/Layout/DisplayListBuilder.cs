using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// Turns a laid-out page into its display list: highlights, text (with the shaped glyphs), underlines and strikethroughs, tab
/// leaders, placeholder boxes of inline objects and the frames of preserved blocks. The same list is drawn on screen, printed
/// and exported to PDF. Selection, caret and formatting marks are not part of it (the editor draws them on top).
/// </summary>
public static class DisplayListBuilder
{
    private static readonly RgbColor PlaceholderStroke = new(0xA6, 0xA6, 0xA6);
    private static readonly RgbColor PlaceholderFill = new(0xF2, 0xF2, 0xF2);

    public static PageDisplayList Build(PageLayout page)
    {
        var items = new List<DisplayItem>();
        AddPreservedFrames(page, items);
        foreach (var placed in page.Lines)
        {
            AddLine(placed, items);
        }

        return new PageDisplayList(page.Index, page.Width, page.Height, items);
    }

    /// <summary>The colour of a resolved format ("auto" is black).</summary>
    public static RgbColor ColorOf(EffectiveCharacterFormat format) => format.IsAutoColor ? RgbColor.Black : RgbColor.Parse(format.Color);

    /// <summary>The colour of a highlight.</summary>
    public static RgbColor ColorOf(HighlightColor highlight) => highlight switch
    {
        HighlightColor.Yellow => new RgbColor(0xFF, 0xFF, 0x00),
        HighlightColor.BrightGreen => new RgbColor(0x00, 0xFF, 0x00),
        HighlightColor.Cyan => new RgbColor(0x00, 0xFF, 0xFF),
        HighlightColor.Magenta => new RgbColor(0xFF, 0x00, 0xFF),
        HighlightColor.Blue => new RgbColor(0x00, 0x00, 0xFF),
        HighlightColor.Red => new RgbColor(0xFF, 0x00, 0x00),
        HighlightColor.DarkBlue => new RgbColor(0x00, 0x00, 0x80),
        HighlightColor.DarkCyan => new RgbColor(0x00, 0x80, 0x80),
        HighlightColor.DarkGreen => new RgbColor(0x00, 0x80, 0x00),
        HighlightColor.DarkMagenta => new RgbColor(0x80, 0x00, 0x80),
        HighlightColor.DarkRed => new RgbColor(0x80, 0x00, 0x00),
        HighlightColor.DarkYellow => new RgbColor(0x80, 0x80, 0x00),
        HighlightColor.DarkGray => new RgbColor(0x80, 0x80, 0x80),
        HighlightColor.LightGray => new RgbColor(0xC0, 0xC0, 0xC0),
        HighlightColor.Black => RgbColor.Black,
        HighlightColor.White => RgbColor.White,
        _ => new RgbColor(0, 0, 0, 0),
    };

    private static void AddPreservedFrames(PageLayout page, List<DisplayItem> items)
    {
        var index = 0;
        var lines = page.Lines;
        while (index < lines.Count)
        {
            var first = lines[index];
            if (!first.Paragraph.IsPreserved)
            {
                index++;
                continue;
            }

            var bottom = first.Top + first.Line.Height;
            while (index + 1 < lines.Count && lines[index + 1].Block == first.Block)
            {
                index++;
                bottom = lines[index].Top + lines[index].Line.Height;
            }

            items.Add(new RectangleItem(first.Left, first.Top, first.Paragraph.Width, bottom - first.Top, PlaceholderFill, PlaceholderStroke, 0.5));
            index++;
        }
    }

    private static void AddLine(PlacedLine placed, List<DisplayItem> items)
    {
        var line = placed.Line;
        var baseline = placed.Top + line.Baseline;

        // Highlights first, under the text.
        foreach (var item in line.Items)
        {
            if (item is TextItem { Format.Highlight: not HighlightColor.None } text)
            {
                items.Add(new RectangleItem(placed.Left + text.X, placed.Top + line.Baseline - line.Ascent, text.Width, line.Ascent + line.Descent,
                    ColorOf(text.Format.Highlight)));
            }
        }

        foreach (var item in line.Items)
        {
            switch (item)
            {
                case LabelItem label:
                    AddText(items, placed.Left + label.X, baseline, label.Shaped, 0, label.Shaped.Text.Length, label.Font, ColorOf(label.Format), 0);
                    break;
                case TextItem text:
                    AddTextItem(items, placed, text, baseline);
                    break;
                case TabItem tab when tab.Leader != TabLeader.None && tab.Width > 1:
                    var style = tab.Leader is TabLeader.Hyphen ? LineStyle.Dashed : tab.Leader is TabLeader.Underscore or TabLeader.Heavy ? LineStyle.Solid : LineStyle.Dotted;
                    items.Add(new LineItem(placed.Left + tab.X + 1, baseline, placed.Left + tab.X + tab.Width - 1, baseline, ColorOf(tab.Format),
                        tab.Leader == TabLeader.Heavy ? 1.2 : 0.6, style));
                    break;
                case ObjectItem box:
                    AddObject(items, placed, box, baseline);
                    break;
            }
        }
    }

    private static void AddTextItem(List<DisplayItem> items, PlacedLine placed, TextItem text, double baseline)
    {
        var color = ColorOf(text.Format);
        var x = placed.Left + text.X;
        var y = baseline + text.BaselineShift;
        AddText(items, x, y, text.Shaped, text.ShapedStart, text.End - text.Start, text.Font, color, text.SpaceExtra);

        var metrics = text.Metrics;
        if (text.Format.Underline != UnderlineStyle.None)
        {
            var thickness = Math.Max(0.5, metrics.UnderlineThickness);
            var underlineY = y + Math.Max(metrics.UnderlinePosition, thickness);
            var width = VisibleWidth(text);
            items.Add(new LineItem(x, underlineY, x + width, underlineY, color, IsHeavy(text.Format.Underline) ? thickness * 2 : thickness,
                StyleOf(text.Format.Underline)));
        }

        if (text.Format.Strikethrough || text.Format.DoubleStrikethrough)
        {
            var thickness = Math.Max(0.5, metrics.StrikeoutThickness);
            var strikeY = y - metrics.StrikeoutPosition;
            var width = VisibleWidth(text);
            if (text.Format.DoubleStrikethrough)
            {
                items.Add(new LineItem(x, strikeY - thickness, x + width, strikeY - thickness, color, thickness));
                items.Add(new LineItem(x, strikeY + thickness, x + width, strikeY + thickness, color, thickness));
            }
            else
            {
                items.Add(new LineItem(x, strikeY, x + width, strikeY, color, thickness));
            }
        }
    }

    private static void AddObject(List<DisplayItem> items, PlacedLine placed, ObjectItem box, double baseline)
    {
        var x = placed.Left + box.X;
        if (box.Height > 0)
        {
            items.Add(new RectangleItem(x, baseline - box.Height, box.Width, box.Height, PlaceholderFill, PlaceholderStroke, 0.75));
            items.Add(new LineItem(x, baseline - box.Height, x + box.Width, baseline, PlaceholderStroke, 0.5));
            items.Add(new LineItem(x, baseline, x + box.Width, baseline - box.Height, PlaceholderStroke, 0.5));
            return;
        }

        if (box.Shaped is { } shaped)
        {
            AddText(items, x, baseline, shaped, 0, shaped.Text.Length, box.Font, ColorOf(box.Format), 0);
        }
    }

    /// <summary>Adds the glyph runs of characters [start, start + length) of <paramref name="shaped"/> at x (its first character).</summary>
    private static void AddText(List<DisplayItem> items, double x, double y, ShapedText shaped, int start, int length, FontSpec font, RgbColor color, double spaceExtra)
    {
        if (length <= 0)
        {
            return;
        }

        var text = shaped.Text.Substring(start, length);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // x of every character from the start of the item (justified spaces widened).
        var characterX = new double[length];
        var originOffset = 0.0;
        for (var index = 0; index < start; index++)
        {
            originOffset += shaped.Advances[index];
        }

        var position = 0.0;
        for (var index = 0; index < length; index++)
        {
            characterX[index] = position;
            position += shaped.Advances[start + index];
            if (shaped.Text[start + index] == ' ')
            {
                position += spaceExtra;
            }
        }

        if (shaped.GlyphRuns.Count == 0)
        {
            items.Add(new GlyphRunItem(x, y, text, font, new FontFace(font.Family, font.Bold, font.Italic), color, characterX, [], []));
            return;
        }

        foreach (var run in shaped.GlyphRuns)
        {
            var glyphs = new List<ushort>();
            var glyphX = new List<float>();
            for (var glyph = 0; glyph < run.Glyphs.Length; glyph++)
            {
                var cluster = run.Clusters[glyph];
                if (cluster < start || cluster >= start + length)
                {
                    continue;
                }

                var spaces = 0;
                for (var index = start; index < cluster; index++)
                {
                    if (shaped.Text[index] == ' ')
                    {
                        spaces++;
                    }
                }

                glyphs.Add(run.Glyphs[glyph]);
                glyphX.Add((float)(run.Positions[glyph] - originOffset + spaces * spaceExtra));
            }

            if (glyphs.Count == 0)
            {
                continue;
            }

            items.Add(new GlyphRunItem(x, y, text, font, run.Face, color, characterX, [.. glyphs], [.. glyphX]));
        }
    }

    private static double VisibleWidth(TextItem text)
    {
        // Underlines stop before trailing spaces of a line.
        var width = text.Width;
        var shapedEnd = text.ShapedStart + (text.End - text.Start);
        for (var index = shapedEnd - 1; index >= text.ShapedStart && text.Shaped.Text[index] == ' '; index--)
        {
            width -= text.Shaped.Advances[index] + text.SpaceExtra;
        }

        return Math.Max(0, width);
    }

    private static bool IsHeavy(UnderlineStyle style) =>
        style is UnderlineStyle.Thick or UnderlineStyle.DottedHeavy or UnderlineStyle.DashedHeavy or UnderlineStyle.DashLongHeavy
            or UnderlineStyle.DashDotHeavy or UnderlineStyle.DashDotDotHeavy or UnderlineStyle.WavyHeavy;

    private static LineStyle StyleOf(UnderlineStyle style) => style switch
    {
        UnderlineStyle.Double or UnderlineStyle.WavyDouble => style == UnderlineStyle.WavyDouble ? LineStyle.Wavy : LineStyle.Double,
        UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => LineStyle.Dotted,
        UnderlineStyle.Dash or UnderlineStyle.DashedHeavy or UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy or UnderlineStyle.DotDash
            or UnderlineStyle.DashDotHeavy or UnderlineStyle.DotDotDash or UnderlineStyle.DashDotDotHeavy => LineStyle.Dashed,
        UnderlineStyle.Wave or UnderlineStyle.WavyHeavy => LineStyle.Wavy,
        _ => LineStyle.Solid,
    };
}

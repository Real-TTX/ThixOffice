using System.Globalization;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Unicode;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>
/// What a text body is laid out with besides its text: autofit scales (PowerPoint's normAutofit: font scale and line-spacing
/// reduction, 1 and 0 = none), the slide number and date fields show, the measurer.
/// </summary>
public sealed record TextLayoutOptions(double FontScale = 1, double LineSpacingReduction = 0, int SlideNumber = 1, DateTime? Now = null);

/// <summary>One laid-out line of a text body (coordinates in points relative to the shape's box, unrotated).</summary>
public sealed class TextLine
{
    public required int Paragraph { get; init; }

    /// <summary>The offset of the first character in the paragraph's text.</summary>
    public required int Start { get; init; }

    /// <summary>The offset after the last character (trailing spaces and the line break included).</summary>
    public required int End { get; init; }

    public required double Top { get; init; }

    public required double Height { get; init; }

    public required double Baseline { get; init; }

    /// <summary>The x of every caret stop Start..End (End - Start + 1 values).</summary>
    public required double[] CaretX { get; init; }

    public required int Column { get; init; }

    /// <summary>The height of the caret (the line's ascent + descent).</summary>
    public required double CaretHeight { get; init; }

    public double Bottom => Top + Height;
}

/// <summary>
/// A text body laid out in a box: lines, display items (glyph runs, bullets, underlines, highlights) and the geometry an editor
/// needs (caret rectangles, hit testing). Pure: the same input gives the same result.
/// </summary>
public sealed class TextBodyLayout
{
    internal TextBodyLayout(IReadOnlyList<TextLine> lines, IReadOnlyList<DisplayItem> items, double contentHeight, double boxLeft, double boxTop, double boxWidth, double boxHeight, double fontScale, double reduction)
    {
        Lines = lines;
        Items = items;
        ContentHeight = contentHeight;
        BoxLeft = boxLeft;
        BoxTop = boxTop;
        BoxWidth = boxWidth;
        BoxHeight = boxHeight;
        FontScale = fontScale;
        LineSpacingReduction = reduction;
    }

    public IReadOnlyList<TextLine> Lines { get; }

    public IReadOnlyList<DisplayItem> Items { get; }

    /// <summary>The height of all lines with paragraph spacing (points).</summary>
    public double ContentHeight { get; }

    public double BoxLeft { get; }

    public double BoxTop { get; }

    public double BoxWidth { get; }

    public double BoxHeight { get; }

    /// <summary>The font scale used (normAutofit), 1 when the text fits.</summary>
    public double FontScale { get; }

    /// <summary>The line-spacing reduction used (0–0.2).</summary>
    public double LineSpacingReduction { get; }

    public bool Overflows => ContentHeight > BoxHeight + 0.01;

    /// <summary>The caret rectangle (x, top, height) before the character at <paramref name="offset"/> of paragraph <paramref name="paragraph"/>.</summary>
    public (double X, double Top, double Height) CaretAt(int paragraph, int offset)
    {
        TextLine? best = null;
        foreach (var line in Lines)
        {
            if (line.Paragraph != paragraph)
            {
                continue;
            }

            best = line;
            if (offset < line.End || (offset == line.End && line == Lines.Last(item => item.Paragraph == paragraph)))
            {
                break;
            }
        }

        if (best is null)
        {
            return (BoxLeft, BoxTop, 10);
        }

        var index = Math.Clamp(offset - best.Start, 0, best.CaretX.Length - 1);
        return (best.CaretX[index], best.Baseline - (best.CaretHeight * 0.8), best.CaretHeight);
    }

    /// <summary>The paragraph and offset nearest to (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public (int Paragraph, int Offset) HitTest(double x, double y)
    {
        if (Lines.Count == 0)
        {
            return (0, 0);
        }

        var line = Lines.FirstOrDefault(item => y < item.Bottom) ?? Lines[^1];
        if (Lines.Any(item => item.Column > 0))
        {
            line = Lines.Where(item => y >= item.Top - 0.01 && y < item.Bottom)
                .OrderBy(item => Math.Abs(((item.CaretX[0] + item.CaretX[^1]) / 2) - x))
                .FirstOrDefault() ?? line;
        }

        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        var last = line.CaretX.Length - 1;
        var nextLineStartsAfter = Lines.SkipWhile(item => item != line).Skip(1).FirstOrDefault() is { } next && next.Paragraph == line.Paragraph;
        if (nextLineStartsAfter && last > 0)
        {
            last--; // the caret at the end of a wrapped line belongs to the next line
        }

        for (var index = 0; index <= last; index++)
        {
            var distance = Math.Abs(line.CaretX[index] - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return (line.Paragraph, line.Start + bestIndex);
    }
}

/// <summary>
/// Lays out a text body for shapes and table cells on ThixOffice.Text (UAX #14 break opportunities, the measurer's advances): list
/// styles resolved through <see cref="ShapeResolver"/>, bullets and numbering, tabs, line breaks, alignment and justification, line
/// and paragraph spacing, vertical anchoring, columns, underline/strike/highlight, and normAutofit (shrink until it fits). A
/// focused counterpart of ThixWrite's ParagraphLayouter (different model; see the report's consolidation note).
/// </summary>
public static class TextBodyLayouter
{
    private const double MinimumFontScale = 0.25;

    /// <summary>
    /// Lays out <paramref name="shape"/>'s text in its text rectangle (<paramref name="left"/>, <paramref name="top"/>,
    /// <paramref name="width"/>, <paramref name="height"/> in points; the insets are applied here). With normAutofit the font
    /// scale and line spacing reduction are searched like PowerPoint does (line spacing first, up to 20 %, then the font in
    /// steps of 2.5 %) until the text fits.
    /// </summary>
    public static TextBodyLayout Layout(ResolvedShape shape, TextBody text, SlideContext context, ITextMeasurer measurer, double left, double top, double width, double height, TextLayoutOptions? options = null)
    {
        options ??= new TextLayoutOptions();
        var body = shape.Body;
        if (body.AutoFit is NormalAutoFit && options.FontScale >= 1 && options.LineSpacingReduction <= 0)
        {
            var fitted = Run(shape, text, context, measurer, left, top, width, height, options);
            if (!fitted.Overflows)
            {
                return fitted;
            }

            foreach (var (scale, reduction) in AutoFitSteps())
            {
                var attempt = Run(shape, text, context, measurer, left, top, width, height, options with { FontScale = scale, LineSpacingReduction = reduction });
                if (!attempt.Overflows || scale <= MinimumFontScale)
                {
                    return attempt;
                }
            }
        }

        return Run(shape, text, context, measurer, left, top, width, height, options);
    }

    /// <summary>The normAutofit search order: line spacing 10 %, 20 %, then the font in 2.5 % steps (with 20 % line reduction).</summary>
    public static IEnumerable<(double Scale, double Reduction)> AutoFitSteps()
    {
        yield return (1, 0.1);
        yield return (1, 0.2);
        for (var scale = 0.975; scale >= MinimumFontScale - 1e-9; scale -= 0.025)
        {
            yield return (Math.Round(scale, 3), 0.2);
        }
    }

    private sealed class Piece
    {
        public required int Start;
        public required int End;
        public required EffectiveRun Run;
        public required FontSpec Font;
        public required FontMetrics Metrics;
        public required ShapedText Shaped;
        public required int ShapedOffset;
        public string? LinkTarget;
    }

    private static TextBodyLayout Run(ResolvedShape shape, TextBody text, SlideContext context, ITextMeasurer measurer, double boxLeft, double boxTop, double boxWidth, double boxHeight, TextLayoutOptions options)
    {
        var body = shape.Body;
        var insetLeft = Units.EmuToPoints(body.LeftInset ?? ShapeResolver.DefaultHorizontalInset);
        var insetRight = Units.EmuToPoints(body.RightInset ?? ShapeResolver.DefaultHorizontalInset);
        var insetTop = Units.EmuToPoints(body.TopInset ?? ShapeResolver.DefaultVerticalInset);
        var insetBottom = Units.EmuToPoints(body.BottomInset ?? ShapeResolver.DefaultVerticalInset);
        var left = boxLeft + insetLeft;
        var top = boxTop + insetTop;
        var width = Math.Max(1, boxWidth - insetLeft - insetRight);
        var height = Math.Max(0, boxHeight - insetTop - insetBottom);
        var wrap = body.Wrap ?? true;
        var columns = Math.Max(1, body.Columns ?? 1);
        var columnSpacing = Units.EmuToPoints(body.ColumnSpacing ?? 0);
        var columnWidth = columns > 1 ? Math.Max(1, (width - ((columns - 1) * columnSpacing)) / columns) : width;

        var lines = new List<TextLine>();
        var items = new List<DisplayItem>();
        var pendingLines = new List<(TextLine Line, List<DisplayItem> Items)>();
        var y = 0.0;
        var numbering = new int[9];
        var metricsCache = new Dictionary<FontSpec, FontMetrics>();

        FontMetrics MetricsOf(FontSpec font)
        {
            if (!metricsCache.TryGetValue(font, out var metrics))
            {
                metrics = measurer.GetMetrics(font);
                metricsCache[font] = metrics;
            }

            return metrics;
        }

        for (var paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            var properties = ShapeResolver.ParagraphOf(shape, paragraph);
            var level = properties.Level ?? 0;
            for (var deeper = level + 1; deeper < numbering.Length; deeper++)
            {
                numbering[deeper] = 0;
            }

            var paragraphText = DisplayText(paragraph, options);
            var pieces = BuildPieces(paragraph, paragraphText, properties, context, measurer, options, MetricsOf);
            var endRun = ShapeResolver.Effective(ShapeResolver.RunOf(properties, paragraph.EndProperties ?? paragraph.Runs.FirstOrDefault()?.Properties ?? RunProperties.Empty), context, options.FontScale);
            var endFont = new FontSpec(endRun.Family, endRun.Size, endRun.Bold, endRun.Italic);
            var firstFont = pieces.Count > 0 ? pieces[0].Font : endFont;
            var firstRun = pieces.Count > 0 ? pieces[0].Run : endRun;

            var marginLeft = Units.EmuToPoints(properties.LeftMargin ?? 0);
            var marginRight = Units.EmuToPoints(properties.RightMargin ?? 0);
            var indent = Units.EmuToPoints(properties.Indent ?? 0);
            var spacingPercent = properties.LineSpacing is { IsPoints: false } percent ? Math.Max(0.1, (percent.Value / 100000.0) - options.LineSpacingReduction) : 1 - options.LineSpacingReduction;
            double? spacingPoints = properties.LineSpacing is { IsPoints: true } points ? points.Value / 100.0 * Math.Max(0.5, 1 - options.LineSpacingReduction) : null;
            var singleLine = MetricsOf(firstFont).LineHeight;

            if (paragraphIndex > 0)
            {
                y += Spacing(properties.SpaceBefore, singleLine, options.FontScale);
            }

            // The bullet: its text, font and colour.
            var bullet = BulletOf(properties, paragraph, level, numbering, firstRun, context, options, paragraphText.Length == 0);
            var bulletWidth = 0.0;
            ShapedText? bulletShaped = null;
            FontSpec bulletFont = default;
            if (bullet is { Text: { Length: > 0 } bulletText })
            {
                bulletFont = new FontSpec(bullet.Family, bullet.Size, firstRun.Bold && bullet.IsNumber, false);
                bulletShaped = measurer.Shape(bulletText, bulletFont);
                bulletWidth = bulletShaped.Width;
            }

            var lineStart = 0;
            var firstLine = true;
            var breaks = LineBreaker.GetBreaks(paragraphText.Replace(LineBreak.Character, '\n'));
            var tabSize = Units.EmuToPoints(properties.DefaultTabSize ?? 914400);
            var tabs = properties.Tabs?.Select(tab => (Position: Units.EmuToPoints(tab.Position), tab.Alignment)).OrderBy(tab => tab.Position).ToList() ?? [];

            do
            {
                var bulletX = marginLeft + indent;
                var lineLeft = firstLine ? marginLeft + indent : marginLeft;
                if (firstLine && bulletShaped is not null)
                {
                    lineLeft = indent < 0 && bulletX + bulletWidth <= marginLeft ? marginLeft : bulletX + bulletWidth + (bulletWidth > 0 ? bullet!.Size * 0.15 : 0);
                }

                var available = wrap ? Math.Max(1, columnWidth - lineLeft - marginRight) : double.MaxValue;
                var (lineEnd, caretX, forced) = FitLine(paragraphText, pieces, breaks, lineStart, lineLeft, available, wrap, tabSize, tabs);

                // Line metrics.
                double ascent = 0, descent = 0, gap = 0;
                foreach (var piece in pieces.Where(piece => piece.End > lineStart && piece.Start < Math.Max(lineEnd, lineStart + 1)))
                {
                    ascent = Math.Max(ascent, piece.Metrics.Ascent);
                    descent = Math.Max(descent, piece.Metrics.Descent);
                    gap = Math.Max(gap, piece.Metrics.LineGap);
                }

                if (ascent + descent <= 0)
                {
                    var metrics = MetricsOf(endFont);
                    (ascent, descent, gap) = (metrics.Ascent, metrics.Descent, metrics.LineGap);
                }

                var lineHeight = spacingPoints ?? ((ascent + descent + gap) * spacingPercent);
                var baseline = y + lineHeight - (spacingPoints is null ? descent * spacingPercent : descent);

                // Alignment: the visible width ends before trailing spaces and the line break.
                var visibleEnd = lineEnd;
                while (visibleEnd > lineStart && (paragraphText[visibleEnd - 1] == ' ' || paragraphText[visibleEnd - 1] == LineBreak.Character))
                {
                    visibleEnd--;
                }

                var lineWidth = caretX[visibleEnd - lineStart] - caretX[0];
                var room = columnWidth - marginRight - lineLeft - lineWidth;
                var alignment = properties.Alignment ?? TextAlignment.Left;
                var shift = alignment switch
                {
                    TextAlignment.Center => room / 2,
                    TextAlignment.Right => room,
                    _ => 0,
                };
                if (!wrap && alignment == TextAlignment.Center)
                {
                    shift = (columnWidth - lineWidth) / 2 - lineLeft;
                }

                var lastLine = lineEnd >= paragraphText.Length;
                var spaceExtra = 0.0;
                if (alignment is TextAlignment.Justify or TextAlignment.Distributed && room > 0 && (!lastLine || alignment == TextAlignment.Distributed) && !forced)
                {
                    var spaces = 0;
                    for (var index = lineStart; index < visibleEnd; index++)
                    {
                        spaces += paragraphText[index] == ' ' ? 1 : 0;
                    }

                    spaceExtra = spaces > 0 ? room / spaces : 0;
                }

                var positions = new double[caretX.Length];
                var extra = 0.0;
                for (var index = 0; index < caretX.Length; index++)
                {
                    positions[index] = left + caretX[index] + shift + extra;
                    if (index + lineStart < visibleEnd && paragraphText[index + lineStart] == ' ')
                    {
                        extra += spaceExtra;
                    }
                }

                var lineItems = new List<DisplayItem>();
                if (firstLine && bulletShaped is not null && bullet is not null)
                {
                    var bulletLeft = left + bulletX + (alignment == TextAlignment.Center && indent >= 0 ? shift : 0);
                    if (alignment is TextAlignment.Center or TextAlignment.Right)
                    {
                        bulletLeft = positions[0] - bulletWidth - (bullet.Size * 0.15);
                    }

                    if (bullet.Image is { IsDrawable: true } image)
                    {
                        var size = bullet.Size;
                        lineItems.Add(new ImageItem(bulletLeft, baseline - (size * 0.8), size, size, image.Key, image.Bytes));
                    }
                    else
                    {
                        AddText(lineItems, bulletLeft, baseline, bulletShaped, 0, bulletShaped.Text.Length, bulletFont, bullet.Color, 0);
                    }
                }

                EmitLine(lineItems, paragraphText, pieces, lineStart, visibleEnd, positions, baseline, spaceExtra);
                var line = new TextLine
                {
                    Paragraph = paragraphIndex,
                    Start = lineStart,
                    End = lineEnd,
                    Top = y,
                    Height = lineHeight,
                    Baseline = baseline,
                    CaretX = positions,
                    Column = 0,
                    CaretHeight = ascent + descent,
                };
                pendingLines.Add((line, lineItems));
                y += lineHeight;
                lineStart = lineEnd;
                firstLine = false;
            }
            while (lineStart < paragraphText.Length);

            y += Spacing(properties.SpaceAfter, singleLine, options.FontScale);
        }

        // Columns: lines flow into the next column when the box is full.
        var contentHeight = y;
        var columnOffsets = new double[pendingLines.Count];
        var columnOf = new int[pendingLines.Count];
        if (columns > 1 && height > 0)
        {
            var column = 0;
            var columnTop = 0.0;
            for (var index = 0; index < pendingLines.Count; index++)
            {
                var line = pendingLines[index].Line;
                if (line.Bottom - columnTop > height + 0.01 && column < columns - 1 && line.Top > columnTop)
                {
                    column++;
                    columnTop = line.Top;
                }

                columnOf[index] = column;
                columnOffsets[index] = columnTop;
            }

            contentHeight = pendingLines.Count == 0 ? 0 : pendingLines.Select((entry, index) => entry.Line.Bottom - columnOffsets[index]).Max();
        }

        var anchorShift = (body.Anchor ?? TextAnchor.Top) switch
        {
            TextAnchor.Center => (height - contentHeight) / 2,
            TextAnchor.Bottom => height - contentHeight,
            _ => 0,
        };
        if (body.Anchor is TextAnchor.Center or TextAnchor.Bottom && contentHeight > height && body.AutoFit is not ShapeAutoFit)
        {
            // Overflowing text grows away from the anchor like PowerPoint (centred text overflows both ways).
            anchorShift = body.Anchor == TextAnchor.Center ? (height - contentHeight) / 2 : height - contentHeight;
        }

        for (var index = 0; index < pendingLines.Count; index++)
        {
            var (line, lineItems) = pendingLines[index];
            var dx = columnOf[index] * (columnWidth + columnSpacing);
            var dy = top + anchorShift - columnOffsets[index];
            lines.Add(new TextLine
            {
                Paragraph = line.Paragraph,
                Start = line.Start,
                End = line.End,
                Top = line.Top + dy,
                Height = line.Height,
                Baseline = line.Baseline + dy,
                CaretX = line.CaretX.Select(x => x + dx).ToArray(),
                Column = columnOf[index],
                CaretHeight = line.CaretHeight,
            });
            foreach (var item in lineItems)
            {
                items.Add(Offset(item, dx, dy));
            }
        }

        return new TextBodyLayout(lines, items, contentHeight, left, top, width, height, options.FontScale, options.LineSpacingReduction);
    }

    private static double Spacing(TextSpacing? spacing, double singleLine, double fontScale) => spacing switch
    {
        { IsPoints: true } points => points.Value / 100.0,
        { } percent => singleLine * percent.Value / 100000.0,
        _ => 0,
    } * (spacing is { IsPoints: true } ? 1 : 1) * (fontScale < 1 && spacing is { IsPoints: true } ? fontScale : 1);

    /// <summary>The paragraph's text as shown: fields with their current values (slide number, date).</summary>
    private static string DisplayText(TextParagraph paragraph, TextLayoutOptions options) =>
        string.Concat(paragraph.Runs.Select(run => run is TextField field ? FieldText(field, options) : run.Text));

    /// <summary>What a field shows: the slide number, today's date, otherwise its stored text.</summary>
    public static string FieldText(TextField field, TextLayoutOptions options)
    {
        if (field.Type == "slidenum")
        {
            return options.SlideNumber.ToString(CultureInfo.InvariantCulture);
        }

        if (field.Type is { } type && type.StartsWith("datetime", StringComparison.Ordinal) && options.Now is { } now)
        {
            var german = CultureInfo.GetCultureInfo("de-DE");
            return type switch
            {
                "datetime2" => now.ToString("dddd, d. MMMM yyyy", german),
                "datetime3" => now.ToString("d. MMMM yyyy", german),
                "datetime4" => now.ToString("d. MMMM yyyy", german),
                "datetime10" or "datetime11" => now.ToString("HH:mm", german),
                "datetime12" or "datetime13" => now.ToString("HH:mm:ss", german),
                _ => now.ToString("dd.MM.yyyy", german),
            };
        }

        return field.Content;
    }

    private static List<Piece> BuildPieces(TextParagraph paragraph, string displayText, ParagraphProperties properties, SlideContext context, ITextMeasurer measurer, TextLayoutOptions options, Func<FontSpec, FontMetrics> metricsOf)
    {
        var pieces = new List<Piece>();
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var shown = run is TextField field ? FieldText(field, options) : run.Text;
            if (shown.Length == 0)
            {
                continue;
            }

            var effective = ShapeResolver.Effective(ShapeResolver.RunOf(properties, run.Properties), context, options.FontScale);
            var size = effective.Baseline != 0 ? effective.Size * 0.67 : effective.Size;
            var font = new FontSpec(effective.Family, size, effective.Bold, effective.Italic);
            var shapedText = effective.Caps == "all" ? string.Concat(shown.Select(char.ToUpperInvariant)) : shown;
            shapedText = shapedText.Replace(LineBreak.Character, ' ').Replace('\t', ' ').Replace(PreservedRun.Character, '□');
            pieces.Add(new Piece
            {
                Start = position,
                End = position + shown.Length,
                Run = effective,
                Font = font,
                Metrics = metricsOf(font),
                Shaped = measurer.Shape(shapedText, font),
                ShapedOffset = 0,
                LinkTarget = run.Properties.Click?.Target,
            });
            position += shown.Length;
        }

        return pieces;
    }

    private static double AdvanceAt(string text, List<Piece> pieces, int index, double x, double tabSize, List<(double Position, string Alignment)> tabs, double lineLeft)
    {
        var character = text[index];
        if (character == LineBreak.Character)
        {
            return 0;
        }

        if (character == '\t')
        {
            var absolute = lineLeft + x;
            foreach (var tab in tabs)
            {
                if (tab.Position > absolute + 0.01)
                {
                    return tab.Position - absolute;
                }
            }

            var next = (Math.Floor((absolute + 0.01) / tabSize) + 1) * tabSize;
            return next - absolute;
        }

        foreach (var piece in pieces)
        {
            if (index >= piece.Start && index < piece.End)
            {
                return piece.Shaped.Advances[index - piece.Start] + (piece.Run.Spacing != 0 && piece.Shaped.Advances[index - piece.Start] > 0 ? piece.Run.Spacing : 0);
            }
        }

        return 0;
    }

    /// <summary>Fits one line from <paramref name="start"/>: the end offset, caret x of every stop (relative to the column), whether a line break ended it.</summary>
    private static (int End, double[] CaretX, bool Forced) FitLine(string text, List<Piece> pieces, LineBreakKind[] breaks, int start, double lineLeft, double available, bool wrap, double tabSize, List<(double Position, string Alignment)> tabs)
    {
        var x = 0.0;
        var positions = new List<double> { lineLeft };
        var lastBreak = -1;
        var index = start;
        for (; index < text.Length; index++)
        {
            if (text[index] == LineBreak.Character)
            {
                positions.Add(lineLeft + x);
                return (index + 1, [.. positions], true);
            }

            if (index > start && breaks[index] != LineBreakKind.None)
            {
                lastBreak = index;
            }

            var advance = AdvanceAt(text, pieces, index, x, tabSize, tabs, lineLeft);
            var isSpace = text[index] == ' ';
            if (wrap && !isSpace && x + advance > available + 0.001 && index > start)
            {
                if (lastBreak > start)
                {
                    positions.RemoveRange(lastBreak - start + 1, positions.Count - (lastBreak - start + 1));
                    return (lastBreak, [.. positions], false);
                }

                // An over-long word breaks at a grapheme boundary.
                return (index, [.. positions], false);
            }

            x += advance;
            positions.Add(lineLeft + x);
        }

        return (index, [.. positions], false);
    }

    private static void EmitLine(List<DisplayItem> items, string text, List<Piece> pieces, int start, int end, double[] positions, double baseline, double spaceExtra)
    {
        foreach (var piece in pieces)
        {
            var from = Math.Max(piece.Start, start);
            var to = Math.Min(piece.End, end);
            if (from >= to)
            {
                continue;
            }

            // Split at tabs and line breaks (they move the text after them).
            var segmentStart = from;
            for (var index = from; index <= to; index++)
            {
                if (index < to && text[index] != '\t' && text[index] != LineBreak.Character)
                {
                    continue;
                }

                if (index > segmentStart)
                {
                    EmitSegment(items, piece, segmentStart, index, positions, start, baseline, spaceExtra);
                }

                segmentStart = index + 1;
            }
        }
    }

    private static void EmitSegment(List<DisplayItem> items, Piece piece, int from, int to, double[] positions, int lineStart, double baseline, double spaceExtra)
    {
        var x = positions[from - lineStart];
        var width = positions[to - lineStart] - x;
        var run = piece.Run;
        var y = baseline - (run.Baseline * run.Size);
        if (run.Highlight is { } highlight)
        {
            items.Add(new RectangleItem(x, baseline - (piece.Metrics.Ascent * 1.0), width, piece.Metrics.Ascent + piece.Metrics.Descent, highlight));
        }

        AddText(items, x, y, piece.Shaped, from - piece.Start, to - from, piece.Font, run.Color, spaceExtra);
        var visibleWidth = width;
        if (run.Underline)
        {
            var thickness = Math.Max(0.5, piece.Metrics.UnderlineThickness);
            items.Add(new LineItem(x, y + piece.Metrics.UnderlinePosition, x + visibleWidth, y + piece.Metrics.UnderlinePosition, run.Color, thickness));
        }

        if (run.Strike)
        {
            var thickness = Math.Max(0.5, piece.Metrics.StrikeoutThickness);
            items.Add(new LineItem(x, y - piece.Metrics.StrikeoutPosition, x + visibleWidth, y - piece.Metrics.StrikeoutPosition, run.Color, thickness));
        }
    }

    /// <summary>Adds the glyph runs of the code units [start, start + length) of a shaped text at (x, baseline y).</summary>
    internal static void AddText(List<DisplayItem> items, double x, double y, ShapedText shaped, int start, int length, FontSpec font, RgbColor color, double spaceExtra)
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

        var characterX = new double[length];
        var originOffset = shaped.WidthOf(0, start);
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
                    spaces += shaped.Text[index] == ' ' ? 1 : 0;
                }

                glyphs.Add(run.Glyphs[glyph]);
                glyphX.Add((float)(run.Positions[glyph] - originOffset + (spaces * spaceExtra)));
            }

            if (glyphs.Count > 0)
            {
                items.Add(new GlyphRunItem(x, y, text, font, run.Face, color, characterX, [.. glyphs], [.. glyphX]));
            }
        }
    }

    private sealed record BulletInfo(string Text, string Family, double Size, RgbColor Color, bool IsNumber, ImageData? Image);

    private static BulletInfo? BulletOf(ParagraphProperties properties, TextParagraph paragraph, int level, int[] numbering, EffectiveRun firstRun, SlideContext context, TextLayoutOptions options, bool empty)
    {
        var bullet = properties.Bullet;
        if (bullet is null or NoBullet)
        {
            numbering[level] = 0;
            return null;
        }

        if (empty)
        {
            // PowerPoint shows no bullet on an empty paragraph (numbering does not advance either).
            return null;
        }

        var size = firstRun.Size;
        if (properties.BulletSizeFollowsText != true && properties.BulletSize is { } bulletSize)
        {
            size = bulletSize.IsPoints ? bulletSize.Value / 100.0 * options.FontScale : firstRun.Size * bulletSize.Value / 100000.0;
        }

        var color = properties.BulletColorFollowsText != true && properties.BulletColor is { } bulletColor
            ? Drawing.ColorResolver.Resolve(bulletColor, context.Colors)
            : firstRun.Color;
        var family = properties.BulletFontFollowsText != true && properties.BulletFont is { } font ? context.Theme.Fonts.Resolve(font.Typeface) : firstRun.Family;
        switch (bullet)
        {
            case CharacterBullet character:
                numbering[level] = 0;
                return new BulletInfo(MapSymbol(character.Character, family), family, size, color, false, null);
            case AutoNumberBullet number:
                numbering[level] = numbering[level] == 0 ? number.StartAt ?? 1 : numbering[level] + 1;
                return new BulletInfo(Number(number.Scheme, numbering[level]), firstRun.Family, size, color, true, null);
            case PictureBullet picture:
                numbering[level] = 0;
                return new BulletInfo("•", firstRun.Family, size, color, false, picture.Image);
            default:
                return null;
        }
    }

    /// <summary>Wingdings/Symbol private-use characters as their Unicode look-alikes (fonts often miss the symbol fonts).</summary>
    private static string MapSymbol(string character, string family)
    {
        if (character.Length != 1)
        {
            return character;
        }

        var code = character[0];
        if (family.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase))
        {
            return code switch
            {
                '§' or '' => "▪",
                'Ø' or '' => "➢",
                'ü' or '' => "✔",
                'v' or '' => "❖",
                'q' or '' => "❏",
                'n' or '' => "■",
                'l' or '' => "●",
                _ => "•",
            };
        }

        if (family.Equals("Symbol", StringComparison.OrdinalIgnoreCase) && code is '·' or '')
        {
            return "•";
        }

        return character;
    }

    /// <summary>The label of number <paramref name="value"/> in an a:buAutoNum scheme ("arabicPeriod" → "3.").</summary>
    public static string Number(string scheme, int value)
    {
        string core;
        if (scheme.StartsWith("alphaLc", StringComparison.Ordinal))
        {
            core = Alpha(value, false);
        }
        else if (scheme.StartsWith("alphaUc", StringComparison.Ordinal))
        {
            core = Alpha(value, true);
        }
        else if (scheme.StartsWith("romanLc", StringComparison.Ordinal))
        {
            core = Roman(value).ToLowerInvariant();
        }
        else if (scheme.StartsWith("romanUc", StringComparison.Ordinal))
        {
            core = Roman(value);
        }
        else
        {
            core = value.ToString(CultureInfo.InvariantCulture);
        }

        if (scheme.EndsWith("ParenBoth", StringComparison.Ordinal))
        {
            return $"({core})";
        }

        if (scheme.EndsWith("ParenR", StringComparison.Ordinal))
        {
            return core + ")";
        }

        if (scheme.EndsWith("Plain", StringComparison.Ordinal))
        {
            return core;
        }

        if (scheme.EndsWith("Minus", StringComparison.Ordinal))
        {
            return $"- {core} -";
        }

        return core + ".";
    }

    private static string Alpha(int value, bool upper)
    {
        if (value <= 0)
        {
            return "";
        }

        var letter = (char)((upper ? 'A' : 'a') + ((value - 1) % 26));
        return new string(letter, ((value - 1) / 26) + 1);
    }

    private static string Roman(int value)
    {
        if (value <= 0 || value >= 4000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        (int Value, string Text)[] numerals = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var result = new System.Text.StringBuilder();
        foreach (var (number, text) in numerals)
        {
            while (value >= number)
            {
                result.Append(text);
                value -= number;
            }
        }

        return result.ToString();
    }

    private static DisplayItem Offset(DisplayItem item, double dx, double dy) => item switch
    {
        GlyphRunItem run => run with { X = run.X + dx, Y = run.Y + dy },
        RectangleItem rectangle => rectangle with { X = rectangle.X + dx, Y = rectangle.Y + dy },
        LineItem line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
        ImageItem image => image with { X = image.X + dx, Y = image.Y + dy },
        _ => item,
    };
}

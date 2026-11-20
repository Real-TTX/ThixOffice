using ThixOffice.Text;
using ThixOffice.Text.Unicode;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>What laying out a paragraph needs besides the paragraph: the measurer, the resolver, the default tab distance.</summary>
internal sealed class LayoutContext(ITextMeasurer measurer, StyleResolver resolver)
{
    private readonly Dictionary<FontSpec, FontMetrics> _metrics = [];

    public ITextMeasurer Measurer { get; } = measurer;

    public StyleResolver Resolver { get; } = resolver;

    public double DefaultTabStop => Math.Max(1, Resolver.Document.Settings.DefaultTabStop);

    public FontMetrics MetricsOf(FontSpec font)
    {
        if (!_metrics.TryGetValue(font, out var metrics))
        {
            metrics = Measurer.GetMetrics(font);
            _metrics[font] = metrics;
        }

        return metrics;
    }

    public void Clear() => _metrics.Clear();

    /// <summary>The text a field shows where it is laid out now (a header on page 3: "3"); null shows its last result.</summary>
    public Func<FieldInline, string>? FieldText { get; set; }

    /// <summary>The numbers of the first footnote and endnote of the paragraph being laid out (its references count on from them).</summary>
    public (int Footnote, int Endnote) NoteStart { get; set; } = (1, 1);

    /// <summary>What a note's own mark shows in the note being laid out ("3"); null shows nothing.</summary>
    public string? NoteMarkText { get; set; }

    /// <summary>The size of a nested table (<see cref="OpaqueInline.Nested"/>) laid out at a width; null leaves it a placeholder.</summary>
    public Func<Story, double, (double Width, double Height)>? MeasureNested { get; set; }

    /// <summary>How tracked changes show ("Markup" views of Überprüfen › Nachverfolgung).</summary>
    public RevisionView View { get; set; } = RevisionView.SimpleMarkup;

    /// <summary>True when content of this tracked change is not shown in the current view (deleted text in the final views, inserted in the original).</summary>
    public bool Hides(Revision? revision) => revision is not null && (revision.IsRemoval ? View is RevisionView.SimpleMarkup or RevisionView.NoMarkup : View == RevisionView.Original);

    /// <summary>The size of a formula (<see cref="OpaqueInline.Math"/>) at a font size: width, ascent, descent; null shows nothing.</summary>
    public Func<OpaqueInline, double, (double Width, double Ascent, double Descent)>? MeasureMath { get; set; }
}

/// <summary>
/// Lays out one paragraph into lines for a width: measures its text in pieces of one format (one shaping call per piece),
/// breaks lines at the Unicode line-break opportunities (UAX #14 essentials; markers stick to the text after them; an over-long
/// word breaks at a grapheme boundary), expands tabs to the paragraph's or the default stops (right / center / decimal stops
/// align the text after them), places the list label, aligns and justifies, and computes line heights by the line spacing rule
/// and the caret stops of every line. Pure: the same input gives the same lines.
/// </summary>
internal static class ParagraphLayouter
{
    private const double Epsilon = 0.001;
    private const double ScriptScale = 0.65;

    private enum SegmentKind
    {
        Text,
        Tab,
        LineBreak,
        PageBreak,
        Object,
        Marker,
    }

    private sealed class Segment
    {
        public int Start;
        public int End;
        public SegmentKind Kind;
        public required EffectiveCharacterFormat Format;
        public FontSpec Font;
        public FontMetrics Metrics;
        public double BaselineShift;
        public ShapedText? Shaped;
        public Inline? Object;
        public double ObjectWidth;
        public double ObjectHeight;

        /// <summary>How far an object reaches below the baseline (a formula's denominators and subscripts).</summary>
        public double ObjectDescent;
        public double LetterSpacing;
        public double Scale = 1;

        /// <summary>A block laid out on a line of its own (a nested table): the line is exactly as tall as it.</summary>
        public bool IsBlock;
    }

    /// <summary>The narrowest line text is set in beside a floating object; a narrower gap is skipped (the line moves below the object).</summary>
    private const double MinimumWrapWidth = 36;

    /// <summary>
    /// Lays out a paragraph at <paramref name="width"/>. <paramref name="exclusions"/>: the areas of floating objects the text keeps clear
    /// of, in the paragraph's coordinates (x from the left of the text area, y from the top of its first line) – a line beside one is
    /// set in the wider gap next to it, a line with no room left moves below it.
    /// </summary>
    public static ParagraphLayout Layout(Paragraph paragraph, EffectiveParagraphFormat format, ListLabel? label, double width, LayoutContext context,
        IReadOnlyList<(double Left, double Top, double Right, double Bottom)>? exclusions = null)
    {
        var text = paragraph.Text;
        var styleId = format.StyleKey;
        var markFormat = context.Resolver.ResolveCharacter(styleId, paragraph.MarkProperties);
        var segments = BuildSegments(paragraph, styleId, context, Math.Max(1, width - format.IndentLeft - format.IndentRight), out var advances, out var owner);
        var breaks = BreakOpportunities(paragraph);
        var graphemeStarts = TextBoundaries.GraphemeStarts(text);

        var markFont = FontOf(markFormat);
        var markMetrics = context.MetricsOf(markFont);
        var markWidth = markFont.Size * 0.3;

        var left = format.IndentLeft;
        var right = width - format.IndentRight;
        var firstStart = left + format.FirstLineIndent;
        LabelItem? labelItem = null;
        if (label is not null)
        {
            (labelItem, firstStart) = PlaceLabel(paragraph, label, format, styleId, context);
        }

        var tabWidths = new double[text.Length];
        var columnBreaks = new bool[text.Length];
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            if (paragraph.Inlines[index] is OpaqueInline { Kind: PreservedKind.ColumnBreak })
            {
                columnBreaks[paragraph.StartOf(index)] = true;
            }
        }

        var lines = new List<LineLayout>();
        var lineStart = 0;
        var y = 0.0;
        while (true)
        {
            var isFirst = lines.Count == 0;
            var baseStart = isFirst ? firstStart : left;
            var (startX, lineRight, skip) = (baseStart, right, 0.0);
            LineLayout line;
            LineEnding ending;
            var end = lineStart;
            for (var attempt = 0; ; attempt++)
            {
                (end, ending) = FillLine(text, lineStart, startX, lineRight, advances, breaks, graphemeStarts, tabWidths, format, context, columnBreaks);
                line = BuildLine(paragraph, lineStart, end, ending, startX, lineRight, isFirst, format, segments, owner, advances, tabWidths,
                    graphemeStarts, isFirst ? labelItem : null, markMetrics, markWidth);
                if (exclusions is not { Count: > 0 } || attempt >= 12)
                {
                    break;
                }

                var (newStart, newRight, below) = Room(exclusions, baseStart, right, y + skip, y + skip + line.Height);
                if (below > 0)
                {
                    skip = below - y;
                    (startX, lineRight) = (baseStart, right);
                    continue;
                }

                if (Math.Abs(newStart - startX) < Epsilon && Math.Abs(newRight - lineRight) < Epsilon)
                {
                    break;
                }

                (startX, lineRight) = (newStart, newRight);
            }

            if (skip > 0)
            {
                line = new LineLayout(line.Start, line.End, line.Height + skip, line.Baseline + skip, line.Ascent, line.Descent, line.Items, line.Stops,
                    line.StopX, line.Ending, line.IsFirst, line.IsLast, line.MarkWidth);
            }

            lines.Add(line);
            y += line.Height;
            lineStart = end;

            var paragraphEnds = ending == LineEnding.Paragraph || (lineStart >= text.Length && ending is LineEnding.PageBreak or LineEnding.ColumnBreak);
            if (paragraphEnds)
            {
                break;
            }
        }

        // Only the very last line is the paragraph's last.
        if (lines[^1].Ending != LineEnding.Paragraph)
        {
            var last = lines[^1];
            lines[^1] = new LineLayout(last.Start, last.End, last.Height, last.Baseline, last.Ascent, last.Descent, last.Items, last.Stops,
                last.StopX, last.Ending, last.IsFirst, true, last.MarkWidth);
        }

        return new ParagraphLayout(paragraph, width, label?.Text, format, lines) { IsWrapped = exclusions is { Count: > 0 }, Exclusions = exclusions is { Count: > 0 } ? exclusions : null };
    }

    /// <summary>
    /// Where a line between <paramref name="top"/> and <paramref name="bottom"/> has room beside the exclusions it meets: the wider
    /// side of each; when no gap of <see cref="MinimumWrapWidth"/> is left, the y below the lowest blocking exclusion (else 0).
    /// </summary>
    private static (double Start, double Right, double Below) Room(IReadOnlyList<(double Left, double Top, double Right, double Bottom)> exclusions,
        double start, double right, double top, double bottom)
    {
        var (newStart, newRight) = (start, right);
        var lowest = 0.0;
        foreach (var exclusion in exclusions)
        {
            if (exclusion.Bottom <= top + Epsilon || exclusion.Top >= bottom - Epsilon || exclusion.Right <= start || exclusion.Left >= right)
            {
                continue;
            }

            lowest = Math.Max(lowest, exclusion.Bottom);
            var roomLeft = exclusion.Left - start;
            var roomRight = right - exclusion.Right;
            if (roomRight >= roomLeft)
            {
                newStart = Math.Max(newStart, exclusion.Right);
            }
            else
            {
                newRight = Math.Min(newRight, exclusion.Left);
            }
        }

        return newRight - newStart < MinimumWrapWidth && lowest > 0 ? (start, right, lowest) : (newStart, newRight, 0);
    }

    /// <summary>The lines of a preserved block: a short German caption, then its text lines, in the body font, grey.</summary>
    public static ParagraphLayout LayoutPreserved(OpaqueBlock block, double width, LayoutContext context)
    {
        var resolver = context.Resolver;
        var styleId = resolver.Document.Styles.DefaultParagraphStyle?.Id ?? "";
        var grey = new CharacterProperties { Color = TextColor.FromHex("595959") };
        var caption = new CharacterProperties { Color = TextColor.FromHex("7F7F7F"), Italic = true, FontSize = 8 };
        var lines = new List<LineLayout>();
        var texts = new List<(string Text, CharacterProperties Properties)> { (CaptionOf(block.Kind), caption) };
        texts.AddRange(block.Lines.Select(line => (line, grey)));

        EffectiveParagraphFormat? format = null;
        foreach (var (line, properties) in texts)
        {
            var pseudo = new Paragraph(line.Replace('\n', ' '), new ParagraphProperties { SpacingAfter = 0, SpacingBefore = 0, LineSpacing = LineSpacing.Single }, properties);
            var pseudoFormat = resolver.ResolveParagraph(pseudo) with { IndentLeft = 4, IndentRight = 4, FirstLineIndent = 0, Alignment = ParagraphAlignment.Left, StyleId = styleId };
            format ??= pseudoFormat with { SpacingBefore = 4, SpacingAfter = 8, KeepWithNext = false, PageBreakBefore = false };
            var laidOut = Layout(pseudo, pseudoFormat, null, width, context);
            foreach (var item in laidOut.Lines)
            {
                // No caret inside a preserved block: one stop at its start.
                lines.Add(new LineLayout(0, 0, item.Height, item.Baseline, item.Ascent, item.Descent, item.Items, [0], [item.StopX.FirstOrDefault()],
                    LineEnding.LineBreak, lines.Count == 0, false, 0));
            }
        }

        return new ParagraphLayout(block, width, null, format!, lines);
    }

    /// <summary>The font a resolved format draws with (raised / lowered text smaller).</summary>
    public static FontSpec FontOf(EffectiveCharacterFormat format)
    {
        var size = format.VerticalPosition == VerticalPosition.Baseline ? format.FontSize : format.FontSize * ScriptScale;
        return new FontSpec(format.FontFamily, size, format.Bold, format.Italic);
    }

    private static string CaptionOf(PreservedKind kind) => kind switch
    {
        PreservedKind.Table => "Verschachtelte Tabelle – wird angezeigt, ist in dieser Version noch nicht bearbeitbar",
        PreservedKind.ContentControl => "Inhaltssteuerelement – nicht bearbeitbar",
        PreservedKind.Math => "Formel – nicht bearbeitbar",
        _ => "Nicht unterstützter Inhalt – wird beim Speichern erhalten",
    };

    private static (LabelItem Label, double TextStart) PlaceLabel(Paragraph paragraph, ListLabel label, EffectiveParagraphFormat format, string styleId, LayoutContext context)
    {
        var properties = paragraph.MarkProperties.Merge(label.Level.LabelProperties);
        var labelFormat = context.Resolver.ResolveCharacter(styleId, properties with { Underline = null, StyleId = null });
        var (text, labelFont) = DisplayLabel(label.Text, labelFormat);
        var font = FontOf(labelFont);
        var shaped = context.Measurer.Shape(text, font);
        var anchor = format.IndentLeft + format.FirstLineIndent;
        var x = label.Level.Alignment switch
        {
            ParagraphAlignment.Right => anchor - shaped.Width,
            ParagraphAlignment.Center => anchor - shaped.Width / 2,
            _ => anchor,
        };
        var labelEnd = x + shaped.Width;
        var textStart = label.Level.Suffix switch
        {
            LabelSuffix.Space => labelEnd + context.Measurer.Shape(" ", font).Width,
            LabelSuffix.Nothing => labelEnd,
            _ => NextTabStop(labelEnd, format, context, hangingStop: format.IndentLeft).Position,
        };
        return (new LabelItem(x, shaped.Width, shaped, labelFont, font, context.MetricsOf(font)), textStart);
    }

    /// <summary>Bullets of symbol fonts are private-use characters; they are shown as their Unicode equivalent in the text font.</summary>
    private static (string Text, EffectiveCharacterFormat Format) DisplayLabel(string text, EffectiveCharacterFormat format)
    {
        var symbolFont = format.FontFamily is "Symbol" or "Wingdings" or "Wingdings 2" or "Wingdings 3";
        if (!symbolFont && !text.Any(character => character is >= '\uF000' and <= '\uF0FF'))
        {
            return (text, format);
        }

        var mapped = new string(text.Select(character => character switch
        {
            '\uF0B7' => '•',
            '\uF0A7' => '▪',
            '\uF0D8' => '➢',
            '\uF0FC' => '✓',
            '\uF076' => '❖',
            '\uF0A8' => '◆',
            '\uF06F' => '○',
            '\uF06E' => '■',
            >= '\uF000' and <= '\uF0FF' => '•',
            _ => character,
        }).ToArray());
        return (mapped, format with { FontFamily = BuiltInStyles.BodyFont });
    }

    private static List<Segment> BuildSegments(Paragraph paragraph, string styleId, LayoutContext context, double textWidth, out double[] advances, out int[] owner)
    {
        var text = paragraph.Text;
        advances = new double[text.Length];
        owner = new int[text.Length];
        var segments = new List<Segment>();
        var (footnote, endnote) = context.NoteStart;
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            var inline = paragraph.Inlines[index];
            var start = paragraph.StartOf(index);
            var format = context.Resolver.ResolveCharacter(styleId, inline.Properties);
            var font = FontOf(format);
            var metrics = context.MetricsOf(font);
            var shift = format.VerticalPosition switch
            {
                VerticalPosition.Superscript => -format.FontSize * 0.33,
                VerticalPosition.Subscript => format.FontSize * 0.14,
                _ => 0,
            } - format.Position;

            var revisionHidden = context.Hides(inline.Revision);
            if (revisionHidden && inline is not TextRun)
            {
                // An object of a tracked change this view does not show: no width, no ink.
                owner[start] = segments.Count;
                segments.Add(new Segment { Start = start, End = start + 1, Kind = SegmentKind.Marker, Format = format, Font = font, Metrics = metrics, Object = inline });
                continue;
            }

            if (inline is ImageInline image)
            {
                var picture = new Segment
                {
                    Start = start,
                    End = start + 1,
                    Kind = image.Anchor is null ? SegmentKind.Object : SegmentKind.Marker, // a floating picture is placed by the page
                    Format = format,
                    Font = font,
                    Metrics = metrics,
                    Object = image,
                    ObjectWidth = image.Anchor is null ? image.Width : 0,
                    ObjectHeight = image.Anchor is null ? image.Height : 0,
                };
                advances[start] = picture.ObjectWidth;
                owner[start] = segments.Count;
                segments.Add(picture);
                continue;
            }

            if (inline is ShapeInline shape)
            {
                var drawn = new Segment
                {
                    Start = start,
                    End = start + 1,
                    Kind = shape.Anchor is null ? SegmentKind.Object : SegmentKind.Marker, // a floating shape is placed by the page
                    Format = format,
                    Font = font,
                    Metrics = metrics,
                    Object = shape,
                    ObjectWidth = shape.Anchor is null ? shape.Width : 0,
                    ObjectHeight = shape.Anchor is null ? shape.Height : 0,
                };
                advances[start] = drawn.ObjectWidth;
                owner[start] = segments.Count;
                segments.Add(drawn);
                continue;
            }

            if (inline is FieldInline or NoteInline or NoteMarkInline)
            {
                var shown = inline switch
                {
                    FieldInline field => context.FieldText?.Invoke(field) ?? field.Result,
                    NoteInline { Kind: NoteKind.Endnote } => NoteNumbering.Format(NoteKind.Endnote, endnote++),
                    NoteInline => NoteNumbering.Format(NoteKind.Footnote, footnote++),
                    _ => context.NoteMarkText ?? "",
                };
                var value = new Segment
                {
                    Start = start,
                    End = start + 1,
                    Kind = SegmentKind.Object,
                    Format = format,
                    Font = font,
                    Metrics = metrics,
                    Object = inline,
                    BaselineShift = shift,
                    Shaped = context.Measurer.Shape(shown.Length == 0 ? " " : shown, font),
                };
                value.ObjectWidth = value.Shaped.Width;
                advances[start] = value.ObjectWidth;
                owner[start] = segments.Count;
                segments.Add(value);
                continue;
            }

            if (inline is { IsMarker: true } and not OpaqueInline)
            {
                // A marker of the model (the start or end of a table of contents, of a comment): no width, no ink.
                owner[start] = segments.Count;
                segments.Add(new Segment { Start = start, End = start + 1, Kind = SegmentKind.Marker, Format = format, Font = font, Metrics = metrics, Object = inline });
                continue;
            }

            if (inline is OpaqueInline opaque)
            {
                var segment = new Segment
                {
                    Start = start,
                    End = start + 1,
                    Kind = opaque.IsMarker ? SegmentKind.Marker : SegmentKind.Object,
                    Format = format,
                    Font = font,
                    Metrics = metrics,
                    Object = opaque,
                };
                if (opaque.Nested is { } nested && context.MeasureNested is { } measure)
                {
                    (segment.ObjectWidth, segment.ObjectHeight) = measure(nested, textWidth);
                    segment.IsBlock = true;
                }
                else if (opaque.Math is not null && context.MeasureMath is { } measureMath)
                {
                    var (mathWidth, mathAscent, mathDescent) = measureMath(opaque, format.FontSize);
                    segment.ObjectWidth = opaque.IsDisplayMath ? Math.Max(mathWidth, textWidth) : mathWidth; // a display formula is centred in the line
                    segment.ObjectHeight = Math.Max(0.1, mathAscent);
                    segment.ObjectDescent = mathDescent;
                }
                else if (opaque.Width > 0)
                {
                    segment.ObjectWidth = opaque.Width;
                    segment.ObjectHeight = opaque.Height;
                }
                else if (opaque.DisplayText.Length > 0)
                {
                    segment.Shaped = context.Measurer.Shape(opaque.DisplayText.Replace('\n', ' '), font);
                    segment.ObjectWidth = segment.Shaped.Width;
                }

                advances[start] = segment.ObjectWidth;
                owner[start] = segments.Count;
                segments.Add(segment);
                continue;
            }

            var runText = inline.Characters;
            var pieceStart = 0;
            for (var position = 0; position <= runText.Length; position++)
            {
                var atEnd = position == runText.Length;
                var kind = atEnd ? SegmentKind.Text : KindOf(runText[position]);
                if (!atEnd && kind == SegmentKind.Text)
                {
                    continue;
                }

                if (position > pieceStart)
                {
                    if (format.Hidden || revisionHidden)
                    {
                        // Hidden text ("Ausgeblendet"): no width, no ink – a marker per character keeps the caret positions.
                        for (var unit = pieceStart; unit < position; unit++)
                        {
                            owner[start + unit] = segments.Count;
                            segments.Add(new Segment { Start = start + unit, End = start + unit + 1, Kind = SegmentKind.Marker, Format = format, Font = font, Metrics = metrics });
                        }
                    }
                    else
                    {
                        foreach (var (from, to, shown, pieceFont) in CasePieces(runText, pieceStart, position, format, font))
                        {
                            var shaped = context.Measurer.Shape(shown, pieceFont);
                            var scale = format.Scale / 100.0;
                            var segment = new Segment
                            {
                                Start = start + from,
                                End = start + to,
                                Kind = SegmentKind.Text,
                                Format = format,
                                Font = pieceFont,
                                Metrics = pieceFont == font ? metrics : context.MetricsOf(pieceFont),
                                BaselineShift = shift,
                                Shaped = shaped,
                                LetterSpacing = format.Spacing,
                                Scale = scale,
                            };
                            for (var unit = 0; unit < to - from; unit++)
                            {
                                advances[segment.Start + unit] = shaped.Advances[unit] * scale + format.Spacing;
                                owner[segment.Start + unit] = segments.Count;
                            }

                            segments.Add(segment);
                        }
                    }
                }

                if (!atEnd)
                {
                    owner[start + position] = segments.Count;
                    segments.Add(new Segment
                    {
                        Start = start + position,
                        End = start + position + 1,
                        Kind = kind,
                        Format = format,
                        Font = font,
                        Metrics = metrics,
                    });
                }

                pieceStart = position + 1;
            }
        }

        return segments;
    }

    /// <summary>
    /// The pieces of text [<paramref name="start"/>, <paramref name="end"/>) of a run as they are shaped: in capitals for
    /// "Großbuchstaben", for "Kapitälchen" the lower-case letters as capitals in a smaller size (each such stretch a piece of its own).
    /// </summary>
    private static IEnumerable<(int From, int To, string Shown, FontSpec Font)> CasePieces(string text, int start, int end, EffectiveCharacterFormat format, FontSpec font)
    {
        if (format.Caps || !format.SmallCaps)
        {
            var piece = text[start..end];
            yield return (start, end, format.Caps ? piece.ToUpperInvariant() : piece, font);
            yield break;
        }

        var small = font with { Size = font.Size * 0.8 };
        var from = start;
        while (from < end)
        {
            var lower = char.IsLower(text[from]);
            var to = from + 1;
            while (to < end && char.IsLower(text[to]) == lower)
            {
                to++;
            }

            var piece = text[from..to];
            yield return lower ? (from, to, piece.ToUpperInvariant(), small) : (from, to, piece, font);
            from = to;
        }
    }

    private static SegmentKind KindOf(char character) => character switch
    {
        SpecialCharacters.Tab => SegmentKind.Tab,
        SpecialCharacters.LineBreak => SegmentKind.LineBreak,
        SpecialCharacters.PageBreak => SegmentKind.PageBreak,
        _ => SegmentKind.Text,
    };

    /// <summary>Break opportunities of the paragraph text; invisible markers are left out and stick to what follows them.</summary>
    private static LineBreakKind[] BreakOpportunities(Paragraph paragraph)
    {
        var text = paragraph.Text;
        var markers = new bool[text.Length];
        var hasMarkers = false;
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            if (paragraph.Inlines[index] is { IsMarker: true })
            {
                markers[paragraph.StartOf(index)] = true;
                hasMarkers = true;
            }
        }

        if (!hasMarkers)
        {
            return LineBreaker.GetBreaks(text);
        }

        var filtered = new System.Text.StringBuilder(text.Length);
        var map = new int[text.Length + 1];
        for (var index = 0; index < text.Length; index++)
        {
            map[index] = filtered.Length;
            if (!markers[index])
            {
                filtered.Append(text[index]);
            }
        }

        map[text.Length] = filtered.Length;
        var filteredBreaks = LineBreaker.GetBreaks(filtered.ToString());
        var breaks = new LineBreakKind[text.Length + 1];
        for (var index = 1; index <= text.Length; index++)
        {
            breaks[index] = markers[index - 1] ? LineBreakKind.None : filteredBreaks[map[index]];
        }

        return breaks;
    }

    private static (int End, LineEnding Ending) FillLine(
        string text,
        int lineStart,
        double startX,
        double right,
        double[] advances,
        LineBreakKind[] breaks,
        bool[] graphemeStarts,
        double[] tabWidths,
        EffectiveParagraphFormat format,
        LayoutContext context,
        bool[] columnBreaks)
    {
        var x = startX;
        var lastBreak = -1;
        var index = lineStart;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == SpecialCharacters.LineBreak)
            {
                return (index + 1, LineEnding.LineBreak);
            }

            if (character == SpecialCharacters.PageBreak)
            {
                return (index + 1, LineEnding.PageBreak);
            }

            if (columnBreaks[index])
            {
                return (index + 1, LineEnding.ColumnBreak);
            }

            if (index > lineStart && breaks[index] == LineBreakKind.Allowed)
            {
                lastBreak = index;
            }

            var advance = advances[index];
            if (character == SpecialCharacters.Tab)
            {
                advance = TabWidth(text, index, x, advances, format, context, lineStart == 0);
                tabWidths[index] = advance;
            }

            var overflows = x + advance > right + Epsilon && character != ' ' && index > lineStart;
            if (overflows)
            {
                if (lastBreak > lineStart)
                {
                    return (lastBreak, LineEnding.Wrap);
                }

                var end = index;
                while (end > lineStart && !graphemeStarts[end])
                {
                    end--;
                }

                return (end > lineStart ? end : TextBoundaries.NextGrapheme(text, lineStart), LineEnding.Wrap);
            }

            x += advance;
            index++;
        }

        return (text.Length, LineEnding.Paragraph);
    }

    private static double TabWidth(string text, int index, double x, double[] advances, EffectiveParagraphFormat format, LayoutContext context, bool firstLine)
    {
        var hanging = firstLine && format.FirstLineIndent < 0 ? format.IndentLeft : (double?)null;
        var (stop, alignment) = NextTabStop(x, format, context, hanging);
        if (alignment is TabAlignment.Left or TabAlignment.Bar)
        {
            return Math.Max(0, stop - x);
        }

        // Right / center / decimal stops align the text after the tab (up to the next tab or break).
        var following = 0.0;
        var decimalWidth = -1.0;
        for (var next = index + 1; next < text.Length; next++)
        {
            var character = text[next];
            if (character is SpecialCharacters.Tab or SpecialCharacters.LineBreak or SpecialCharacters.PageBreak)
            {
                break;
            }

            if (decimalWidth < 0 && character is '.' or ',')
            {
                decimalWidth = following;
            }

            following += advances[next];
        }

        var aligned = alignment switch
        {
            TabAlignment.Center => following / 2,
            TabAlignment.Decimal => decimalWidth >= 0 ? decimalWidth : following,
            _ => following,
        };
        return Math.Max(0, stop - x - aligned);
    }

    private static (double Position, TabAlignment Alignment) NextTabStop(double x, EffectiveParagraphFormat format, LayoutContext context, double? hangingStop)
    {
        foreach (var tab in format.Tabs)
        {
            if (tab.Position > x + Epsilon && (hangingStop is not { } hanging || tab.Position < hanging || hanging <= x + Epsilon))
            {
                return (tab.Position, tab.Alignment);
            }
        }

        if (hangingStop is { } implicitStop && implicitStop > x + Epsilon)
        {
            return (implicitStop, TabAlignment.Left);
        }

        var distance = context.DefaultTabStop;
        var next = (Math.Floor((x + Epsilon) / distance) + 1) * distance;
        return (next, TabAlignment.Left);
    }

    private static LineLayout BuildLine(
        Paragraph paragraph,
        int start,
        int end,
        LineEnding ending,
        double startX,
        double right,
        bool isFirst,
        EffectiveParagraphFormat format,
        List<Segment> segments,
        int[] owner,
        double[] advances,
        double[] tabWidths,
        bool[] graphemeStarts,
        LabelItem? label,
        FontMetrics markMetrics,
        double markWidth)
    {
        var text = paragraph.Text;

        // The visible end: without trailing spaces and the break character.
        var contentEnd = end;
        if (ending is LineEnding.LineBreak or LineEnding.PageBreak or LineEnding.ColumnBreak)
        {
            contentEnd--;
        }

        while (contentEnd > start && text[contentEnd - 1] == ' ')
        {
            contentEnd--;
        }

        var lastTab = -1;
        var spaces = 0;
        var xs = new double[end - start + 1];
        var x = startX;
        for (var index = start; index < end; index++)
        {
            xs[index - start] = x;
            x += text[index] == SpecialCharacters.Tab ? tabWidths[index] : advances[index];
            if (text[index] == SpecialCharacters.Tab && index < contentEnd)
            {
                lastTab = index;
                spaces = 0;
            }
            else if (text[index] == ' ' && index < contentEnd)
            {
                spaces++;
            }
        }

        xs[end - start] = x;
        var visibleWidth = xs[contentEnd - start] - startX;
        var free = right - startX - visibleWidth;
        var shift = 0.0;
        var spaceExtra = 0.0;
        var justify = format.Alignment == ParagraphAlignment.Justify && ending is LineEnding.Wrap or LineEnding.LineBreak && spaces > 0 && free > 0;
        if (justify)
        {
            spaceExtra = free / spaces;
        }
        else if (free > 0)
        {
            shift = format.Alignment switch
            {
                ParagraphAlignment.Center => free / 2,
                ParagraphAlignment.Right => free,
                _ => 0,
            };
        }

        if (shift != 0 || spaceExtra != 0)
        {
            var added = 0.0;
            for (var index = start; index <= end; index++)
            {
                xs[index - start] += shift + added;
                if (index < end && index > lastTab && index < contentEnd && text[index] == ' ')
                {
                    added += spaceExtra;
                }
            }
        }

        // Items and metrics.
        var items = new List<LayoutItem>();
        var ascent = 0.0;
        var descent = 0.0;
        var gap = 0.0;
        var blockHeight = 0.0;
        if (label is not null)
        {
            items.Add(label);
            ascent = label.Metrics.Ascent;
            descent = label.Metrics.Descent;
            gap = label.Metrics.LineGap;
        }

        var segmentIndex = start < text.Length ? owner[start] : segments.Count;
        for (; segmentIndex < segments.Count && segments[segmentIndex].Start < end; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var from = Math.Max(start, segment.Start);
            var to = Math.Min(end, segment.End);
            if (from >= to)
            {
                continue;
            }

            var itemX = xs[from - start];
            var itemWidth = xs[to - start] - itemX;
            switch (segment.Kind)
            {
                case SegmentKind.Text:
                    items.Add(new TextItem(from, to, itemX, itemWidth, segment.Shaped!, from - segment.Start, segment.Format, segment.Font,
                        segment.Metrics, segment.BaselineShift, from > lastTab ? spaceExtra : 0) { LetterSpacing = segment.LetterSpacing, Scale = segment.Scale });
                    ascent = Math.Max(ascent, segment.Metrics.Ascent - segment.BaselineShift);
                    descent = Math.Max(descent, segment.Metrics.Descent + segment.BaselineShift);
                    gap = Math.Max(gap, segment.Metrics.LineGap);
                    break;
                case SegmentKind.Tab:
                    items.Add(new TabItem(from, itemX, itemWidth, LeaderAt(format, xs[to - start]), segment.Format, segment.Font));
                    ascent = Math.Max(ascent, segment.Metrics.Ascent);
                    descent = Math.Max(descent, segment.Metrics.Descent);
                    break;
                case SegmentKind.Object:
                    items.Add(new ObjectItem(from, itemX, itemWidth, segment.ObjectHeight, segment.Object!, segment.Shaped, segment.Format, segment.Font, segment.Metrics, segment.BaselineShift));
                    ascent = Math.Max(ascent, segment.ObjectHeight > 0 ? segment.ObjectHeight : segment.Metrics.Ascent - segment.BaselineShift);
                    descent = Math.Max(descent, Math.Max(segment.Metrics.Descent, segment.ObjectDescent));
                    if (segment.IsBlock)
                    {
                        blockHeight = Math.Max(blockHeight, segment.ObjectHeight);
                    }

                    break;
            }
        }

        if (ascent <= 0 && descent <= 0)
        {
            ascent = markMetrics.Ascent;
            descent = markMetrics.Descent;
            gap = markMetrics.LineGap;
        }

        if (blockHeight > 0 && blockHeight >= ascent)
        {
            // A nested table: the line is the table, no line spacing, no descent below it.
            (ascent, descent, gap) = (blockHeight, 0, 0);
        }

        var natural = ascent + descent + gap;
        var height = blockHeight > 0 && blockHeight >= ascent ? natural : format.LineSpacing.Rule switch
        {
            LineSpacingRule.Exact => format.LineSpacing.Value,
            LineSpacingRule.AtLeast => Math.Max(natural, format.LineSpacing.Value),
            _ => natural * format.LineSpacing.Value,
        };
        var baseline = height - descent - gap;

        // Caret stops: grapheme starts, not right after an invisible marker; the break character itself is no stop.
        var lastStop = ending is LineEnding.LineBreak or LineEnding.PageBreak or LineEnding.ColumnBreak ? end - 1 : end;
        var stops = new List<int>();
        var stopX = new List<double>();
        for (var offset = start; offset <= lastStop; offset++)
        {
            var isGrapheme = offset >= text.Length || graphemeStarts[offset];
            var afterMarker = offset > 0 && offset < text.Length && offset > start && paragraph.InlineAt(offset - 1) is { IsMarker: true };
            if (!isGrapheme || afterMarker)
            {
                continue;
            }

            stops.Add(offset);
            stopX.Add(xs[offset - start]);
        }

        if (stops.Count == 0)
        {
            stops.Add(start);
            stopX.Add(xs[0]);
        }

        return new LineLayout(start, end, height, baseline, ascent, descent, items, [.. stops], [.. stopX], ending, isFirst,
            ending == LineEnding.Paragraph, markWidth);
    }

    private static TabLeader LeaderAt(EffectiveParagraphFormat format, double stopX)
    {
        foreach (var tab in format.Tabs)
        {
            if (Math.Abs(tab.Position - stopX) < 0.5)
            {
                return tab.Leader;
            }
        }

        return TabLeader.None;
    }
}

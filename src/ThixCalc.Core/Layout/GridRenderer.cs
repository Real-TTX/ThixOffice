using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Layout;

/// <summary>How a cell looks (format, conditional formats and alignment resolved) – the grid's per-cell query.</summary>
public sealed record CellDisplay(
    CellAddress Cell,
    string Text,
    CellValue Value,
    CellStyle Style,
    RgbColor TextColor,
    RgbColor? Fill,
    HorizontalAlignment Horizontal,
    VerticalAlignment Vertical,
    bool Wrap,
    char FillCharacter,
    int FillIndex,
    bool CannotShow,
    ConditionalLook? Conditional)
{
    /// <summary>Numbers, dates and times never overflow into neighbours: too narrow, they show ####.</summary>
    public bool IsNumeric => Value.IsNumber;
}

/// <summary>Colours and options of the grid drawing (the app passes its theme's colours).</summary>
public sealed record GridRenderOptions
{
    public RgbColor Background { get; init; } = RgbColor.White;

    public RgbColor GridLine { get; init; } = RgbColor.FromHex("E1E1E1");

    public RgbColor Text { get; init; } = RgbColor.Black;

    public RgbColor HeaderBackground { get; init; } = RgbColor.FromHex("F5F5F5");

    public RgbColor HeaderText { get; init; } = RgbColor.FromHex("444444");

    public RgbColor HeaderLine { get; init; } = RgbColor.FromHex("D0D0D0");

    public RgbColor HeaderHighlight { get; init; } = RgbColor.FromHex("D3F0E0");

    /// <summary>Rows/columns whose headers are highlighted (the selection's).</summary>
    public IReadOnlyList<CellRange> Selection { get; init; } = [];

    public bool DrawHeaders { get; init; } = true;
}

/// <summary>
/// Turns a viewport into a renderer-neutral display list (ThixOffice.Text's items in pixels): cell fills and conditional
/// colours, grid lines, data bars, texts laid out the Excel way – overflow into empty neighbours, wrapping, alignment,
/// indent, * fill, #### when a number does not fit (General numbers lose decimals first), merged cells as one – borders and
/// the row/column headers. Clip items keep texts inside their cells.
/// </summary>
public static class GridRenderer
{
    private const double PaddingPixels = 2;
    private const int OverflowScan = 30;

    public static CellDisplay Present(Worksheet sheet, int row, int column, GridMetrics metrics, ConditionalFormatEvaluator? conditional = null, RgbColor? text = null)
    {
        var workbook = sheet.Workbook;
        var value = sheet.GetValue(row, column);
        var style = sheet.GetStyle(row, column);
        var look = conditional?.Evaluate(row, column);
        var code = look?.NumberFormat ?? style.NumberFormat;
        var formatted = look?.HideValue == true ? new FormattedValue(string.Empty) : NumberFormatter.Format(value, code, metrics.Locale, workbook.Date1904);
        if (sheet.View.ShowFormulas && sheet.GetFormula(row, column) is { } formula)
        {
            formatted = new FormattedValue("=" + Formulas.FormulaFormatter.Format(formula, workbook, new CellAddress(row, column), new Formulas.FormulaSyntax(metrics.Locale)));
        }
        else if (!sheet.View.ShowZeros && value.IsNumber && value.Number == 0)
        {
            formatted = new FormattedValue(string.Empty);
        }

        var font = style.Font with
        {
            Bold = look?.Bold ?? style.Font.Bold,
            Italic = look?.Italic ?? style.Font.Italic,
            Strikethrough = look?.Strikethrough ?? style.Font.Strikethrough,
            Underline = look?.Underline is { } underline ? (underline ? UnderlineKind.Single : UnderlineKind.None) : style.Font.Underline,
        };
        style = style with { Font = font };
        var defaultText = text ?? RgbColor.Black;
        var textColor = formatted.Color ?? (look?.FontColor ?? font.Color)?.Resolve(workbook.Theme, defaultText) ?? defaultText;
        if (sheet.Hyperlinks.Any(link => link.Range.Contains(row, column)) && font.Color is null && look?.FontColor is null)
        {
            textColor = workbook.Theme[10];
        }

        RgbColor? fill = look?.ScaleColor ?? (look?.FillColor is { } conditionalFill ? conditionalFill.Resolve(workbook.Theme, RgbColor.White) : null);
        if (fill is null && style.Fill.Pattern != FillPattern.None)
        {
            fill = style.Fill.Pattern == FillPattern.Solid
                ? (style.Fill.Foreground ?? style.Fill.Background)?.Resolve(workbook.Theme, RgbColor.White)
                : (style.Fill.Background ?? style.Fill.Foreground)?.Resolve(workbook.Theme, RgbColor.White);
        }

        var horizontal = style.Alignment.Horizontal;
        if (horizontal == HorizontalAlignment.General)
        {
            horizontal = value.Kind switch
            {
                CellValueKind.Number => HorizontalAlignment.Right,
                CellValueKind.Boolean or CellValueKind.Error => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Left,
            };
        }

        return new CellDisplay(new CellAddress(row, column), formatted.Text, value, style, textColor, fill, horizontal, style.Alignment.Vertical,
            style.Alignment.WrapText || horizontal is HorizontalAlignment.Justify or HorizontalAlignment.Distributed,
            formatted.FillCharacter, formatted.FillIndex, formatted.IsOverflow, look);
    }

    /// <summary>The display list of a viewport (pixels; fonts sized in pixels at the viewport's zoom).</summary>
    public static PageDisplayList Render(GridViewport viewport, GridMetrics metrics, GridRenderOptions? options = null)
    {
        options ??= new GridRenderOptions();
        var sheet = viewport.Layout.Sheet;
        var items = new List<DisplayItem>
        {
            new RectangleItem(0, 0, viewport.Width, viewport.Height, options.Background),
        };
        var conditional = sheet.ConditionalFormats.Count > 0 ? new ConditionalFormatEvaluator(sheet) : null;
        var rows = viewport.AllRows.ToList();
        var columns = viewport.AllColumns.ToList();
        var merges = rows.Count > 0 && columns.Count > 0
            ? sheet.Merges.Intersecting(new CellRange(rows.Min(row => row.Index), columns.Min(column => column.Index), rows.Max(row => row.Index), columns.Max(column => column.Index))).ToList()
            : [];

        // Panes: frozen corner, frozen rows, frozen columns, scrolling area – each clipped to its rectangle.
        var panes = new List<(IReadOnlyList<AxisSegment> Rows, IReadOnlyList<AxisSegment> Columns)>
        {
            (viewport.Rows, viewport.Columns),
            (viewport.FrozenRows, viewport.Columns),
            (viewport.Rows, viewport.FrozenColumns),
            (viewport.FrozenRows, viewport.FrozenColumns),
        };
        foreach (var (paneRows, paneColumns) in panes)
        {
            if (paneRows.Count == 0 || paneColumns.Count == 0)
            {
                continue;
            }

            var clipX = paneColumns[0].Start;
            var clipY = paneRows[0].Start;
            items.Add(new ClipBeginItem(clipX, clipY, paneColumns[^1].End - clipX, paneRows[^1].End - clipY));
            RenderPane(items, viewport, metrics, options, sheet, paneRows, paneColumns, merges, conditional);
            items.Add(ClipEndItem.Instance);
        }

        if (options.DrawHeaders && (viewport.HeaderWidth > 0 || viewport.HeaderHeight > 0))
        {
            RenderHeaders(items, viewport, metrics, options, rows, columns);
        }

        return new PageDisplayList(0, viewport.Width, viewport.Height, items);
    }

    private static void RenderPane(List<DisplayItem> items, GridViewport viewport, GridMetrics metrics, GridRenderOptions options, Worksheet sheet,
        IReadOnlyList<AxisSegment> rows, IReadOnlyList<AxisSegment> columns, List<CellRange> merges, ConditionalFormatEvaluator? conditional)
    {
        var range = new CellRange(rows[0].Index, columns[0].Index, rows[^1].Index, columns[^1].Index);
        var displays = new Dictionary<CellAddress, CellDisplay>();
        foreach (var entry in sheet.GetCells(range))
        {
            if (sheet.Rows.IsHidden(entry.Row) || sheet.Columns.IsHidden(entry.Column))
            {
                continue;
            }

            displays[entry.Address] = Present(sheet, entry.Row, entry.Column, metrics, conditional, options.Text);
        }

        // Row and column styles colour empty cells too.
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var address = new CellAddress(row.Index, column.Index);
                if (!displays.ContainsKey(address) && (sheet.Rows[row.Index].StyleId is not null || sheet.Columns[column.Index].StyleId is not null || conditional is not null))
                {
                    var display = Present(sheet, row.Index, column.Index, metrics, conditional, options.Text);
                    if (display.Fill is not null || display.Conditional is not null)
                    {
                        displays[address] = display;
                    }
                }
            }
        }

        var paneRect = new PixelRect(columns[0].Start, rows[0].Start, columns[^1].End - columns[0].Start, rows[^1].End - rows[0].Start);

        // Grid lines.
        if (sheet.View.ShowGridLines)
        {
            foreach (var row in rows)
            {
                items.Add(new LineItem(paneRect.X, row.End - 0.5, paneRect.Right, row.End - 0.5, options.GridLine, 1));
            }

            foreach (var column in columns)
            {
                items.Add(new LineItem(column.End - 0.5, paneRect.Y, column.End - 0.5, paneRect.Bottom, options.GridLine, 1));
            }
        }

        // Fills (merged areas as one; their inner grid lines covered with the background).
        foreach (var merge in merges)
        {
            if (merge.Intersects(range))
            {
                var rect = viewport.RangeRect(merge);
                var display = displays.GetValueOrDefault(merge.First) ?? Present(sheet, merge.FirstRow, merge.FirstColumn, metrics, conditional, options.Text);
                items.Add(new RectangleItem(rect.X, rect.Y, rect.Width - 1, rect.Height - 1, display.Fill ?? options.Background));
            }
        }

        foreach (var display in displays.Values)
        {
            if (display.Fill is { } fill && sheet.Merges.Find(display.Cell.Row, display.Cell.Column) is null)
            {
                var rect = viewport.CellRect(display.Cell);
                items.Add(new RectangleItem(rect.X - 0.5, rect.Y - 0.5, rect.Width, rect.Height, fill));
            }

            if (display.Conditional?.DataBar is { } bar)
            {
                var rect = viewport.CellRect(display.Cell);
                var inset = 2 * viewport.Zoom;
                items.Add(new RectangleItem(rect.X + inset, rect.Y + inset, Math.Max(0, (rect.Width - 2 * inset) * bar.Fraction), rect.Height - 2 * inset, bar.Color with { A = 200 }));
            }
        }

        // Texts (overflowing texts from cells left/right of the pane included).
        var occupied = new HashSet<CellAddress>(displays.Where(pair => !pair.Value.Value.IsBlank).Select(pair => pair.Key));
        foreach (var row in rows)
        {
            for (var column = Math.Max(0, columns[0].Index - OverflowScan); column <= Math.Min(SheetLimits.LastColumn, columns[^1].Index + OverflowScan); column++)
            {
                var address = new CellAddress(row.Index, column);
                if (column < columns[0].Index || column > columns[^1].Index)
                {
                    if (!sheet.HasContent(row.Index, column))
                    {
                        continue;
                    }

                    displays[address] = Present(sheet, row.Index, column, metrics, conditional, options.Text);
                    occupied.Add(address);
                }

                if (!displays.TryGetValue(address, out var display) || display.Text.Length == 0)
                {
                    continue;
                }

                if (sheet.Merges.Find(row.Index, column) is { } merge && merge.First != address)
                {
                    continue;
                }

                RenderText(items, viewport, metrics, sheet, display, occupied, paneRect);
            }
        }

        // Borders.
        foreach (var display in displays.Values)
        {
            RenderBorders(items, viewport, display, sheet.Workbook.Theme);
        }
    }

    private static FontSpec PixelFont(CellFont font, double zoom) => new(font.Name, font.Size * 96 / 72 * zoom, font.Bold, font.Italic);

    private static void RenderText(List<DisplayItem> items, GridViewport viewport, GridMetrics metrics, Worksheet sheet, CellDisplay display,
        HashSet<CellAddress> occupied, PixelRect pane)
    {
        var zoom = viewport.Zoom;
        var cell = viewport.CellRect(display.Cell);
        var font = PixelFont(display.Style.Font, zoom);
        var padding = PaddingPixels * zoom;
        var indent = display.Style.Alignment.Indent * metrics.MaxDigitWidthPixels * 3 * zoom;
        var available = cell.Width - 2 * padding - indent;
        var text = display.Text;
        var measurer = metrics.Measurer;
        var fontMetrics = measurer.GetMetrics(font);

        if (display.CannotShow)
        {
            text = Hashes(measurer, font, available);
        }
        else if (display.IsNumeric && !display.Wrap)
        {
            var width = measurer.Shape(text, font).Width;
            if (width > available)
            {
                text = NumberFormat.Parse(display.Style.NumberFormat).IsGeneral
                    ? Shrink(display.Value.Number, metrics, measurer, font, available)
                    : Hashes(measurer, font, available);
            }
        }

        if (display.FillCharacter != ' ' || display.FillIndex >= 0 && display.Style.Alignment.Horizontal != HorizontalAlignment.Fill)
        {
            text = ExpandFill(text, display, measurer, font, available);
        }

        if (display.Horizontal == HorizontalAlignment.Fill && text.Length > 0)
        {
            var unit = measurer.Shape(text, font).Width;
            if (unit > 0)
            {
                text = string.Concat(Enumerable.Repeat(text, Math.Max(1, (int)(available / unit))));
            }
        }

        var lines = display.Wrap ? Wrap(text, measurer, font, available) : [text.Replace('\n', ' ')];
        var lineHeight = fontMetrics.LineHeight;
        var blockHeight = lineHeight * lines.Count;
        var top = display.Vertical switch
        {
            VerticalAlignment.Top => cell.Y + padding,
            VerticalAlignment.Center or VerticalAlignment.Justify or VerticalAlignment.Distributed => cell.Y + (cell.Height - blockHeight) / 2,
            _ => cell.Bottom - padding - blockHeight,
        };

        // The clip: the cell, widened over empty neighbours for overflowing text (not for numbers or wrapped text).
        var clip = cell;
        if (!display.Wrap && !display.IsNumeric && lines.Count == 1 && sheet.Merges.Find(display.Cell.Row, display.Cell.Column) is null)
        {
            var width = measurer.Shape(lines[0], font).Width;
            if (width > available)
            {
                clip = Overflow(viewport, sheet, display, occupied, width + 2 * padding + indent);
            }
        }

        clip = new PixelRect(Math.Max(clip.X, pane.X), clip.Y, Math.Min(clip.Right, pane.Right) - Math.Max(clip.X, pane.X), clip.Height);
        if (clip.Width <= 0)
        {
            return;
        }

        items.Add(new ClipBeginItem(clip.X, clip.Y, clip.Width, clip.Height));
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            var shaped = measurer.Shape(line, font);
            var x = display.Horizontal switch
            {
                HorizontalAlignment.Right => cell.Right - padding - indent - shaped.Width,
                HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous => cell.X + (cell.Width - shaped.Width) / 2,
                _ => cell.X + padding + indent,
            };
            var baseline = top + lineHeight * index + fontMetrics.Ascent;
            AddRun(items, x, baseline, shaped, display.TextColor);
            if (display.Style.Font.Underline != UnderlineKind.None)
            {
                var y = baseline + fontMetrics.UnderlinePosition;
                items.Add(new LineItem(x, y, x + shaped.Width, y, display.TextColor, Math.Max(1, fontMetrics.UnderlineThickness),
                    display.Style.Font.Underline is UnderlineKind.Double or UnderlineKind.DoubleAccounting ? LineStyle.Double : LineStyle.Solid));
            }

            if (display.Style.Font.Strikethrough)
            {
                var y = baseline - fontMetrics.StrikeoutPosition;
                items.Add(new LineItem(x, y, x + shaped.Width, y, display.TextColor, Math.Max(1, fontMetrics.StrikeoutThickness)));
            }
        }

        items.Add(ClipEndItem.Instance);
    }

    private static void AddRun(List<DisplayItem> items, double x, double baseline, ShapedText shaped, RgbColor color)
    {
        var characterX = new double[shaped.Text.Length];
        var position = 0.0;
        for (var index = 0; index < shaped.Text.Length; index++)
        {
            characterX[index] = position;
            position += shaped.Advances[index];
        }

        if (shaped.GlyphRuns.Count == 0)
        {
            items.Add(new GlyphRunItem(x, baseline, shaped.Text, shaped.Font, new FontFace(shaped.Font.Family, shaped.Font.Bold, shaped.Font.Italic), color, characterX, [], []));
            return;
        }

        foreach (var run in shaped.GlyphRuns)
        {
            items.Add(new GlyphRunItem(x, baseline, shaped.Text, shaped.Font, run.Face, color, characterX, run.Glyphs, run.Positions));
        }
    }

    /// <summary>How far text may run: over empty neighbours in its direction(s), up to the first filled cell.</summary>
    private static PixelRect Overflow(GridViewport viewport, Worksheet sheet, CellDisplay display, HashSet<CellAddress> occupied, double needed)
    {
        var cell = viewport.CellRect(display.Cell);
        var row = display.Cell.Row;
        var left = cell.X;
        var right = cell.Right;
        bool Free(int column) => column is >= 0 and <= SheetLimits.LastColumn && !occupied.Contains(new CellAddress(row, column))
            && !sheet.HasContent(row, column) && sheet.Merges.Find(row, column) is null;

        var extendRight = display.Horizontal is HorizontalAlignment.Left or HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous;
        var extendLeft = display.Horizontal is HorizontalAlignment.Right or HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous;
        var excess = needed - cell.Width;
        var share = display.Horizontal is HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous ? excess / 2 : excess;
        for (var column = display.Cell.Column + 1; extendRight && right - cell.Right < share && Free(column); column++)
        {
            right = viewport.ColumnX(column + 1);
        }

        for (var column = display.Cell.Column - 1; extendLeft && cell.X - left < share && Free(column); column--)
        {
            left = viewport.ColumnX(column);
        }

        return new PixelRect(left, cell.Y, right - left, cell.Height);
    }

    private static string Hashes(ITextMeasurer measurer, FontSpec font, double available)
    {
        var hash = measurer.Shape("#", font).Width;
        return new string('#', Math.Max(1, hash > 0 ? (int)(available / hash) : 1));
    }

    /// <summary>A General number that does not fit: fewer decimals, then scientific, then ####.</summary>
    private static string Shrink(double value, GridMetrics metrics, ITextMeasurer measurer, FontSpec font, double available)
    {
        for (var length = 10; length >= 1; length--)
        {
            var text = NumberFormatter.FormatGeneral(value, metrics.Locale, length);
            if (measurer.Shape(text, font).Width <= available && !(text.Contains('E') && length < 5))
            {
                return text;
            }
        }

        return Hashes(measurer, font, available);
    }

    private static string ExpandFill(string text, CellDisplay display, ITextMeasurer measurer, FontSpec font, double available)
    {
        if (display.FillIndex < 0 || display.FillIndex > text.Length)
        {
            return text;
        }

        var unit = measurer.Shape(display.FillCharacter.ToString(), font).Width;
        var used = measurer.Shape(text, font).Width;
        if (unit <= 0 || used >= available)
        {
            return text;
        }

        var count = (int)((available - used) / unit);
        return text.Insert(display.FillIndex, new string(display.FillCharacter, count));
    }

    /// <summary>Breaks a text into lines that fit (at spaces and line breaks; long words are cut).</summary>
    private static List<string> Wrap(string text, ITextMeasurer measurer, FontSpec font, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (measurer.Shape(candidate, font).Width <= width || line.Length == 0 && word.Length <= 1)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                line = word;
                while (line.Length > 1 && measurer.Shape(line, font).Width > width)
                {
                    var cut = line.Length - 1;
                    while (cut > 1 && measurer.Shape(line[..cut], font).Width > width)
                    {
                        cut--;
                    }

                    lines.Add(line[..cut]);
                    line = line[cut..];
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    private static void RenderBorders(List<DisplayItem> items, GridViewport viewport, CellDisplay display, ThemeColors theme)
    {
        var borders = display.Style.Borders;
        if (borders == CellBorders.None)
        {
            return;
        }

        var rect = viewport.CellRect(display.Cell);
        void Edge(BorderEdge edge, double x1, double y1, double x2, double y2)
        {
            if (!edge.IsVisible)
            {
                return;
            }

            var color = edge.Color?.Resolve(theme, RgbColor.Black) ?? RgbColor.Black;
            var (thickness, style) = edge.Style switch
            {
                BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => (2.0, LineStyle.Solid),
                BorderStyle.Thick => (3.0, LineStyle.Solid),
                BorderStyle.Double => (3.0, LineStyle.Double),
                BorderStyle.Dotted or BorderStyle.Hair => (1.0, LineStyle.Dotted),
                BorderStyle.Dashed or BorderStyle.DashDot or BorderStyle.DashDotDot => (1.0, LineStyle.Dashed),
                _ => (1.0, LineStyle.Solid),
            };
            if (edge.Style is BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot)
            {
                style = LineStyle.Dashed;
            }

            items.Add(new LineItem(x1, y1, x2, y2, color, thickness, style));
        }

        Edge(borders.Top, rect.X - 0.5, rect.Y - 0.5, rect.Right - 0.5, rect.Y - 0.5);
        Edge(borders.Bottom, rect.X - 0.5, rect.Bottom - 0.5, rect.Right - 0.5, rect.Bottom - 0.5);
        Edge(borders.Left, rect.X - 0.5, rect.Y - 0.5, rect.X - 0.5, rect.Bottom - 0.5);
        Edge(borders.Right, rect.Right - 0.5, rect.Y - 0.5, rect.Right - 0.5, rect.Bottom - 0.5);
        if (borders.DiagonalDown)
        {
            Edge(borders.Diagonal, rect.X, rect.Y, rect.Right, rect.Bottom);
        }

        if (borders.DiagonalUp)
        {
            Edge(borders.Diagonal, rect.X, rect.Bottom, rect.Right, rect.Y);
        }
    }

    private static void RenderHeaders(List<DisplayItem> items, GridViewport viewport, GridMetrics metrics, GridRenderOptions options,
        List<AxisSegment> rows, List<AxisSegment> columns)
    {
        var zoom = viewport.Zoom;
        var font = PixelFont(metrics.Workbook.Styles.Default.Font, zoom);
        var measurer = metrics.Measurer;
        var fontMetrics = measurer.GetMetrics(font);
        items.Add(new RectangleItem(0, 0, viewport.Width, viewport.HeaderHeight, options.HeaderBackground));
        items.Add(new RectangleItem(0, 0, viewport.HeaderWidth, viewport.Height, options.HeaderBackground));
        bool Selected(Func<CellRange, bool> test) => options.Selection.Any(test);

        foreach (var column in columns)
        {
            if (Selected(range => column.Index >= range.FirstColumn && column.Index <= range.LastColumn))
            {
                items.Add(new RectangleItem(column.Start, 0, column.Size, viewport.HeaderHeight, options.HeaderHighlight));
            }

            var shaped = measurer.Shape(A1.ColumnName(column.Index), font);
            AddRun(items, column.Start + (column.Size - shaped.Width) / 2, (viewport.HeaderHeight + fontMetrics.Ascent - fontMetrics.Descent) / 2, shaped, options.HeaderText);
            items.Add(new LineItem(column.End - 0.5, 0, column.End - 0.5, viewport.HeaderHeight, options.HeaderLine, 1));
        }

        foreach (var row in rows)
        {
            if (Selected(range => row.Index >= range.FirstRow && row.Index <= range.LastRow))
            {
                items.Add(new RectangleItem(0, row.Start, viewport.HeaderWidth, row.Size, options.HeaderHighlight));
            }

            var label = (row.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var shaped = measurer.Shape(label, font);
            AddRun(items, (viewport.HeaderWidth - shaped.Width) / 2, row.Start + (row.Size + fontMetrics.Ascent - fontMetrics.Descent) / 2, shaped, options.HeaderText);
            items.Add(new LineItem(0, row.End - 0.5, viewport.HeaderWidth, row.End - 0.5, options.HeaderLine, 1));
        }

        items.Add(new LineItem(viewport.HeaderWidth - 0.5, 0, viewport.HeaderWidth - 0.5, viewport.Height, options.HeaderLine, 1));
        items.Add(new LineItem(0, viewport.HeaderHeight - 0.5, viewport.Width, viewport.HeaderHeight - 0.5, options.HeaderLine, 1));
    }
}

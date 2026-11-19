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

    /// <summary>The header of a whole selected row/column (Excel: dark green with white text).</summary>
    public RgbColor HeaderSelected { get; init; } = RgbColor.FromHex("107C41");

    public RgbColor HeaderSelectedText { get; init; } = RgbColor.White;

    /// <summary>The line at the cell side of a highlighted header.</summary>
    public RgbColor HeaderMarker { get; init; } = RgbColor.FromHex("107C41");

    /// <summary>The line along frozen rows/columns.</summary>
    public RgbColor FreezeLine { get; init; } = RgbColor.FromHex("9B9B9B");

    /// <summary>The red triangle of a cell with a note.</summary>
    public RgbColor NoteMarker { get; init; } = RgbColor.FromHex("C00000");

    /// <summary>The autofilter's arrow buttons (face, frame) and the colour of an active filter.</summary>
    public RgbColor FilterButton { get; init; } = RgbColor.FromHex("F3F3F3");

    public RgbColor FilterButtonLine { get; init; } = RgbColor.FromHex("ABABAB");

    public RgbColor FilterActive { get; init; } = RgbColor.FromHex("107C41");

    public RgbColor FilterArrow { get; init; } = RgbColor.FromHex("595959");

    /// <summary>Rows/columns whose headers are highlighted (the selection's).</summary>
    public IReadOnlyList<CellRange> Selection { get; init; } = [];

    public bool DrawHeaders { get; init; } = true;

    /// <summary>Grid lines on or off (null: the sheet view's setting; printing passes the page setup's).</summary>
    public bool? GridLines { get; init; }

    /// <summary>The sheet's charts over the cells.</summary>
    public bool DrawCharts { get; init; } = true;

    /// <summary>The size of a device pixel in the display list's units (chart hairlines snap to it); 0 for print and PDF.</summary>
    public double ChartPixelSize { get; init; } = 1;
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

        var table = sheet.Tables.Count > 0 ? TableStyles.LookAt(sheet, row, column) : null;
        var font = style.Font with
        {
            Bold = look?.Bold ?? (style.Font.Bold || table?.Bold == true),
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

        // A table's style comes after the cell's own format (direct formatting wins, as in Excel).
        if (table is { } tableLook)
        {
            fill ??= tableLook.Fill;
            if (tableLook.Text is { } tableText && formatted.Color is null && font.Color is null && look?.FontColor is null)
            {
                textColor = tableText;
            }
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

            // A scrolled pane starts at its boundary (its first row/column may be cut off there).
            var clipX = Math.Max(paneColumns[0].Start, ReferenceEquals(paneColumns, viewport.Columns) ? viewport.ScrollLeft : viewport.HeaderWidth);
            var clipY = Math.Max(paneRows[0].Start, ReferenceEquals(paneRows, viewport.Rows) ? viewport.ScrollTop : viewport.HeaderHeight);
            items.Add(new ClipBeginItem(clipX, clipY, Math.Min(viewport.Width, paneColumns[^1].End) - clipX, Math.Min(viewport.Height, paneRows[^1].End) - clipY));
            RenderPane(items, viewport, metrics, options, sheet, paneRows, paneColumns, merges, conditional);
            items.Add(ClipEndItem.Instance);
        }

        if (options.DrawCharts)
        {
            ChartLayer.Render(items, viewport, metrics, options.ChartPixelSize);
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
                if (!displays.ContainsKey(address) && (sheet.Rows[row.Index].StyleId is not null || sheet.Columns[column.Index].StyleId is not null || conditional is not null
                    || (sheet.Tables.Count > 0 && sheet.Tables.Any(table => table.Range.Contains(address)))))
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
        if (options.GridLines ?? sheet.View.ShowGridLines)
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
                items.Add(new RectangleItem(rect.X - 1, rect.Y - 1, rect.Width, rect.Height, display.Fill ?? options.Background));
            }
        }

        foreach (var display in displays.Values)
        {
            if (display.Fill is { } fill && sheet.Merges.Find(display.Cell.Row, display.Cell.Column) is null)
            {
                var rect = viewport.CellRect(display.Cell);
                // The cell covers its own grid lines (the right and bottom pixel, and its neighbours' at the left and top), whole pixels.
                items.Add(new RectangleItem(rect.X - 1, rect.Y - 1, rect.Width, rect.Height, fill));
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

        // Notes: Excel's red triangle in the top right corner.
        foreach (var note in sheet.Comments.Keys)
        {
            if (range.Contains(note) && !sheet.Rows.IsHidden(note.Row) && !sheet.Columns.IsHidden(note.Column))
            {
                var rect = viewport.CellRect(note);
                var size = Math.Max(4, Math.Round(5 * Math.Max(1, viewport.Zoom)));
                for (var step = 0; step < size; step++)
                {
                    items.Add(new LineItem(rect.Right - size + step - 0.5, rect.Y + 0.5 + step - 1, rect.Right - 1, rect.Y + 0.5 + step - 1, options.NoteMarker, 1));
                }
            }
        }

        RenderTableLines(items, viewport, sheet, range);

        // The arrows of an autofilter or a table in its header row.
        foreach (var (headerRow, firstColumn, lastColumn, filter) in FilterHeaders(sheet))
        {
            if (headerRow < range.FirstRow || headerRow > range.LastRow)
            {
                continue;
            }

            for (var column = Math.Max(firstColumn, range.FirstColumn); column <= Math.Min(lastColumn, range.LastColumn); column++)
            {
                if (FilterButtonRect(viewport, new CellAddress(headerRow, column)) is not { } button)
                {
                    continue;
                }

                var active = filter is not null && filter.Columns.Any(entry => entry.ColumnOffset == column - firstColumn);
                items.Add(new RectangleItem(button.X, button.Y, button.Width, button.Height, options.FilterButton, options.FilterButtonLine, 1));
                var middle = Math.Round(button.X + button.Width / 2);
                var top = Math.Round(button.Y + button.Height * 0.38);
                var half = Math.Max(2, Math.Round(button.Width * 0.22));
                for (var step = 0; step <= half; step++)
                {
                    items.Add(new LineItem(middle - half + step - 0.5, top + step + 0.5, middle + half - step + 0.5, top + step + 0.5, active ? options.FilterActive : options.FilterArrow, 1));
                }

                if (active)
                {
                    items.Add(new LineItem(middle - half, top - 2.5, middle + half + 1, top - 2.5, options.FilterActive, 1));
                }
            }
        }
    }

    /// <summary>The header rows with filter arrows: the sheet's autofilter and the tables that show their buttons (row, columns, filter).</summary>
    public static IEnumerable<(int Row, int FirstColumn, int LastColumn, AutoFilter? Filter)> FilterHeaders(Worksheet sheet)
    {
        if (sheet.AutoFilter is { } autoFilter)
        {
            yield return (autoFilter.Range.FirstRow, autoFilter.Range.FirstColumn, autoFilter.Range.LastColumn, autoFilter);
        }

        foreach (var table in sheet.Tables)
        {
            if (table.HasHeader && table.ShowFilterButton)
            {
                yield return (table.Range.FirstRow, table.Range.FirstColumn, table.Range.LastColumn, table.Filter);
            }
        }
    }

    /// <summary>The filter column (offset in its range) whose arrow sits in <paramref name=cell/>, or null.</summary>
    public static int? FilterColumnAt(Worksheet sheet, CellAddress cell)
    {
        foreach (var (row, first, last, _) in FilterHeaders(sheet))
        {
            if (cell.Row == row && cell.Column >= first && cell.Column <= last)
            {
                return cell.Column - first;
            }
        }

        return null;
    }

    /// <summary>The arrow button of the sheet's autofilter column in its header cell (pixels of the viewport), or null.</summary>
    public static PixelRect? FilterButtonRect(GridViewport viewport, int column) =>
        viewport.Layout.Sheet.AutoFilter is { } filter ? FilterButtonRect(viewport, new CellAddress(filter.Range.FirstRow, column)) : null;

    /// <summary>The arrow button in a filter header cell (autofilter or table), or null when the cell has none.</summary>
    public static PixelRect? FilterButtonRect(GridViewport viewport, CellAddress address)
    {
        if (FilterColumnAt(viewport.Layout.Sheet, address) is null)
        {
            return null;
        }

        var cell = viewport.CellRect(address);
        var size = Math.Round(Math.Min(cell.Height - 3, 16 * viewport.Zoom));
        if (size < 6 || cell.Width < size + 2)
        {
            return null;
        }

        return new PixelRect(cell.Right - size - 2, cell.Bottom - size - 2, size, size);
    }

    /// <summary>The lines of table styles: under the header (light styles) and above the total row.</summary>
    private static void RenderTableLines(List<DisplayItem> items, GridViewport viewport, Worksheet sheet, CellRange pane)
    {
        foreach (var table in sheet.Tables)
        {
            if (!table.Range.Intersects(pane) || TableStyles.Resolve(table.Style, sheet.Workbook.Theme) is not { } look)
            {
                continue;
            }

            var left = viewport.ColumnX(Math.Max(table.Range.FirstColumn, pane.FirstColumn)) - 1;
            var right = viewport.ColumnX(Math.Min(table.Range.LastColumn, pane.LastColumn) + 1) - 1;
            if (look.HeaderLine && table.HasHeader && pane.Contains(table.Range.FirstRow, pane.FirstColumn))
            {
                var y = viewport.RowY(table.Range.FirstRow + 1) - 0.5;
                items.Add(new LineItem(left, y, right, y, look.Line, 1));
            }

            if (table.HasTotals && pane.Contains(table.Range.LastRow, pane.FirstColumn))
            {
                var y = viewport.RowY(table.Range.LastRow) - 0.5;
                items.Add(new LineItem(left, y - 2, right, y - 2, look.Line, 1));
                items.Add(new LineItem(left, y, right, y, look.Line, 1));
            }
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

        var alignment = display.Style.Alignment;
        if (alignment.ShrinkToFit && !display.Wrap && !display.CannotShow && text.Length > 0)
        {
            // An zelle anpassen: the font shrinks until the text fits (never grows).
            var natural = measurer.Shape(text.Replace('\n', ' '), font).Width;
            if (natural > available && available > 0)
            {
                font = font.WithSize(Math.Max(1, font.Size * available / natural));
                fontMetrics = measurer.GetMetrics(font);
            }
        }

        if (alignment.TextRotation != 0 && !display.CannotShow && text.Length > 0)
        {
            RenderRotated(items, cell, pane, text.Replace('\n', ' '), font, fontMetrics, measurer, display, alignment.TextRotation);
            return;
        }

        if (display.CannotShow)
        {
            text = Hashes(measurer, font, available);
        }
        else if (display.IsNumeric && !display.Wrap && !alignment.ShrinkToFit)
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

        // A filter arrow in the cell: the text stops before it (as in Excel).
        var right = Math.Min(clip.Right, pane.Right);
        if ((sheet.AutoFilter is not null || sheet.Tables.Count > 0) && FilterButtonRect(viewport, display.Cell) is { } button)
        {
            right = Math.Min(right, button.X - 1);
        }

        clip = new PixelRect(Math.Max(clip.X, pane.X), clip.Y, right - Math.Max(clip.X, pane.X), clip.Height);
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

    /// <summary>
    /// Text at an angle (Ausrichtung › Orientierung): 1–90 counter-clockwise, 91–180 clockwise, 255 letters stacked; turned
    /// around the cell's centre and kept inside the cell.
    /// </summary>
    private static void RenderRotated(List<DisplayItem> items, PixelRect cell, PixelRect pane, string text, FontSpec font, FontMetrics fontMetrics,
        ITextMeasurer measurer, CellDisplay display, int rotation)
    {
        var clipX = Math.Max(cell.X, pane.X);
        var clipWidth = Math.Min(cell.Right, pane.Right) - clipX;
        if (clipWidth <= 0)
        {
            return;
        }

        items.Add(new ClipBeginItem(clipX, cell.Y, clipWidth, cell.Height));
        if (rotation == 255)
        {
            var lineHeight = fontMetrics.LineHeight;
            var top = cell.Y + Math.Max(0, (cell.Height - lineHeight * text.Length) / 2);
            for (var index = 0; index < text.Length; index++)
            {
                var letter = measurer.Shape(text[index].ToString(), font);
                AddRun(items, cell.X + (cell.Width - letter.Width) / 2, top + index * lineHeight + fontMetrics.Ascent, letter, display.TextColor);
            }
        }
        else
        {
            var degrees = rotation <= 90 ? -rotation : rotation - 90;
            var shaped = measurer.Shape(text, font);
            items.Add(TransformBeginItem.At(cell.X + cell.Width / 2, cell.Y + cell.Height / 2, degrees));
            AddRun(items, -shaped.Width / 2, fontMetrics.Ascent - fontMetrics.LineHeight / 2, shaped, display.TextColor);
            items.Add(TransformEndItem.Instance);
        }

        items.Add(ClipEndItem.Instance);
    }

    internal static void AddRun(List<DisplayItem> items, double x, double baseline, ShapedText shaped, RgbColor color)
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
                BorderStyle.Double => (1.0, LineStyle.Double),
                BorderStyle.Dotted or BorderStyle.Hair => (1.0, LineStyle.Dotted),
                BorderStyle.Dashed or BorderStyle.DashDot or BorderStyle.DashDotDot => (1.0, LineStyle.Dashed),
                _ => (1.0, LineStyle.Solid),
            };
            if (edge.Style is BorderStyle.MediumDashed or BorderStyle.MediumDashDot or BorderStyle.MediumDashDotDot)
            {
                style = LineStyle.Dashed;
            }

            // Whole pixels: an even thickness sits between pixels, a double line's second stroke goes into the cell.
            var horizontal = Math.Abs(y1 - y2) < 0.01;
            var shift = thickness % 2 == 0 ? -0.5 : 0;
            if (style == LineStyle.Double)
            {
                shift = -2;
            }

            if (horizontal)
            {
                (y1, y2) = (y1 + shift, y2 + shift);
            }
            else if (Math.Abs(x1 - x2) < 0.01)
            {
                (x1, x2) = (x1 + shift, x2 + shift);
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
        var wholeRows = options.Selection.Where(range => range.IsWholeRows).ToList();
        var wholeColumns = options.Selection.Where(range => range.IsWholeColumns).ToList();

        // Column headers: the frozen ones, then the scrolled ones from the pane's left edge (a cut-off header stays behind it).
        foreach (var (group, from, to) in new[] { (viewport.FrozenColumns, viewport.HeaderWidth, viewport.ScrollLeft), (viewport.Columns, viewport.ScrollLeft, viewport.Width) })
        {
            if (group.Count == 0 || to <= from)
            {
                continue;
            }

            items.Add(new ClipBeginItem(from, 0, to - from, viewport.HeaderHeight));
            foreach (var column in group)
            {
                if (Selected(range => column.Index >= range.FirstColumn && column.Index <= range.LastColumn))
                {
                    var whole = wholeColumns.Any(range => column.Index >= range.FirstColumn && column.Index <= range.LastColumn);
                    items.Add(new RectangleItem(column.Start, 0, column.Size, viewport.HeaderHeight, whole ? options.HeaderSelected : options.HeaderHighlight));
                    items.Add(new LineItem(column.Start, viewport.HeaderHeight - 1, column.End, viewport.HeaderHeight - 1, options.HeaderMarker, 2));
                }

                var shaped = measurer.Shape(A1.ColumnName(column.Index), font);
                var selectedText = wholeColumns.Any(range => column.Index >= range.FirstColumn && column.Index <= range.LastColumn);
                AddRun(items, column.Start + (column.Size - shaped.Width) / 2, (viewport.HeaderHeight + fontMetrics.Ascent - fontMetrics.Descent) / 2, shaped, selectedText ? options.HeaderSelectedText : options.HeaderText);
                items.Add(new LineItem(column.End - 0.5, 0, column.End - 0.5, viewport.HeaderHeight, options.HeaderLine, 1));
            }

            items.Add(ClipEndItem.Instance);
        }

        foreach (var (group, from, to) in new[] { (viewport.FrozenRows, viewport.HeaderHeight, viewport.ScrollTop), (viewport.Rows, viewport.ScrollTop, viewport.Height) })
        {
            if (group.Count == 0 || to <= from)
            {
                continue;
            }

            items.Add(new ClipBeginItem(0, from, viewport.HeaderWidth, to - from));
            foreach (var row in group)
            {
                if (Selected(range => row.Index >= range.FirstRow && row.Index <= range.LastRow))
                {
                    var whole = wholeRows.Any(range => row.Index >= range.FirstRow && row.Index <= range.LastRow);
                    items.Add(new RectangleItem(0, row.Start, viewport.HeaderWidth, row.Size, whole ? options.HeaderSelected : options.HeaderHighlight));
                    items.Add(new LineItem(viewport.HeaderWidth - 1, row.Start, viewport.HeaderWidth - 1, row.End, options.HeaderMarker, 2));
                }

                var label = (row.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var shaped = measurer.Shape(label, font);
                var selectedText = wholeRows.Any(range => row.Index >= range.FirstRow && row.Index <= range.LastRow);
                AddRun(items, (viewport.HeaderWidth - shaped.Width) / 2, row.Start + (row.Size + fontMetrics.Ascent - fontMetrics.Descent) / 2, shaped, selectedText ? options.HeaderSelectedText : options.HeaderText);
                items.Add(new LineItem(0, row.End - 0.5, viewport.HeaderWidth, row.End - 0.5, options.HeaderLine, 1));
            }

            items.Add(ClipEndItem.Instance);
        }

        items.Add(new LineItem(viewport.HeaderWidth - 0.5, 0, viewport.HeaderWidth - 0.5, viewport.Height, options.HeaderLine, 1));
        items.Add(new LineItem(0, viewport.HeaderHeight - 0.5, viewport.Width, viewport.HeaderHeight - 0.5, options.HeaderLine, 1));

        // The corner: Excel's small triangle (select all).
        var size = Math.Min(viewport.HeaderWidth, viewport.HeaderHeight) * 0.55;
        if (size >= 4)
        {
            var right = viewport.HeaderWidth - 4;
            var bottom = viewport.HeaderHeight - 4;
            for (var step = 0; step < size; step++)
            {
                items.Add(new LineItem(right - step, bottom - 0.5 - (size - step - 1), right - step, bottom, options.HeaderLine, 1));
            }
        }

        // Frozen panes: the lines along the frozen rows/columns.
        if (viewport.FrozenRows.Count > 0)
        {
            items.Add(new LineItem(0, viewport.ScrollTop - 0.5, viewport.Width, viewport.ScrollTop - 0.5, options.FreezeLine, 1));
        }

        if (viewport.FrozenColumns.Count > 0)
        {
            items.Add(new LineItem(viewport.ScrollLeft - 0.5, 0, viewport.ScrollLeft - 0.5, viewport.Height, options.FreezeLine, 1));
        }
    }
}

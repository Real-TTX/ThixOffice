using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>A cell of a laid-out table: its grid position, rectangle (points, frame coordinates) and the resolved look.</summary>
public sealed record TableCellBox(int Row, int Column, double X, double Y, double Width, double Height, TableCell Cell, TableStyleFormat Look);

/// <summary>A table laid out: column x positions and row y positions (points; one more than columns/rows) and the visible cells.</summary>
public sealed record TableGeometry(IReadOnlyList<double> ColumnX, IReadOnlyList<double> RowY, IReadOnlyList<TableCellBox> Cells)
{
    public double Width => ColumnX[^1];

    public double Height => RowY[^1];

    /// <summary>The cell at a point (frame coordinates), or null.</summary>
    public TableCellBox? CellAt(double x, double y) =>
        Cells.FirstOrDefault(cell => x >= cell.X && x < cell.X + cell.Width && y >= cell.Y && y < cell.Y + cell.Height);
}

/// <summary>
/// Lays out and draws DrawingML tables: grid widths, rows as tall as their height or their text, merged cells, the table style's
/// parts in PowerPoint's order (whole table, banded columns and rows, first/last column, last row, header row) under each cell's own
/// properties, cell text with margins and anchoring, borders on top.
/// </summary>
public static class TableRenderer
{
    /// <summary>PowerPoint's default table style id ("Mittlere Formatvorlage 2 – Akzent 1"); drawn with ThixShow's look of it.</summary>
    public const string DefaultStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";

    private const long DefaultHorizontalMargin = 91440;
    private const long DefaultVerticalMargin = 45720;

    /// <summary>
    /// ThixShow's built-in table style for a style id the presentation does not define: an accent header row, light banded rows and
    /// white inner lines.
    /// </summary>
    public static TableStyle BuiltInStyle(string id)
    {
        var white = new Outline(12700, new SolidFill(ColorSpec.Scheme("lt1")));
        return new TableStyle(id, "Mittlere Formatvorlage 2 – Akzent 1",
        [
            (TableStylePart.WholeTable, new TableStyleFormat(TextColor: ColorSpec.Scheme("dk1"), FontReference: "minor",
                Fill: new SolidFill(ColorSpec.Scheme("accent1").Tint(20000)),
                LeftBorder: white, RightBorder: white, TopBorder: white, BottomBorder: white, InsideHorizontalBorder: white, InsideVerticalBorder: white)),
            (TableStylePart.Band1Horizontal, new TableStyleFormat(Fill: new SolidFill(ColorSpec.Scheme("accent1").Tint(40000)))),
            (TableStylePart.Band1Vertical, new TableStyleFormat(Fill: new SolidFill(ColorSpec.Scheme("accent1").Tint(40000)))),
            (TableStylePart.LastColumn, new TableStyleFormat(Bold: true, TextColor: ColorSpec.Scheme("lt1"), Fill: new SolidFill(ColorSpec.Scheme("accent1")))),
            (TableStylePart.FirstColumn, new TableStyleFormat(Bold: true, TextColor: ColorSpec.Scheme("lt1"), Fill: new SolidFill(ColorSpec.Scheme("accent1")))),
            (TableStylePart.LastRow, new TableStyleFormat(Bold: true, TextColor: ColorSpec.Scheme("lt1"), Fill: new SolidFill(ColorSpec.Scheme("accent1")), TopBorder: new Outline(38100, new SolidFill(ColorSpec.Scheme("lt1"))))),
            (TableStylePart.FirstRow, new TableStyleFormat(Bold: true, TextColor: ColorSpec.Scheme("lt1"), Fill: new SolidFill(ColorSpec.Scheme("accent1")), BottomBorder: new Outline(38100, new SolidFill(ColorSpec.Scheme("lt1"))))),
        ]);
    }

    /// <summary>The style a table uses: from the presentation, else the built-in look.</summary>
    public static TableStyle StyleOf(Presentation presentation, Table table)
    {
        var id = table.Properties.StyleId ?? presentation.DefaultTableStyleId ?? DefaultStyleId;
        return presentation.TableStyles.FirstOrDefault(style => style.Id == id) ?? BuiltInStyle(id);
    }

    /// <summary>The geometry of a table (rows grow to fit their text).</summary>
    public static TableGeometry Measure(SlideContext context, Table table, ITextMeasurer measurer)
    {
        var style = StyleOf(context.Presentation, table);
        var columnX = new List<double> { 0 };
        foreach (var width in table.ColumnWidths)
        {
            columnX.Add(columnX[^1] + Units.EmuToPoints(width));
        }

        var heights = table.Rows.Select(row => Units.EmuToPoints(row.Height)).ToArray();
        for (var row = 0; row < table.RowCount; row++)
        {
            for (var column = 0; column < Math.Min(table.ColumnCount, table.Rows[row].Cells.Count); column++)
            {
                var cell = table.Rows[row].Cells[column];
                if (cell.IsCovered || cell.RowSpan > 1)
                {
                    continue;
                }

                var width = columnX[Math.Min(columnX.Count - 1, column + cell.GridSpan)] - columnX[column];
                var look = LookOf(table, style, row, column);
                var layout = LayoutCell(context, cell, look, measurer, 0, 0, width, 0);
                var needed = layout.ContentHeight + Units.EmuToPoints((cell.Properties.TopMargin ?? DefaultVerticalMargin) + (cell.Properties.BottomMargin ?? DefaultVerticalMargin));
                heights[row] = Math.Max(heights[row], needed);
            }
        }

        var rowY = new List<double> { 0 };
        foreach (var height in heights)
        {
            rowY.Add(rowY[^1] + height);
        }

        var cells = new List<TableCellBox>();
        for (var row = 0; row < table.RowCount; row++)
        {
            for (var column = 0; column < Math.Min(table.ColumnCount, table.Rows[row].Cells.Count); column++)
            {
                var cell = table.Rows[row].Cells[column];
                if (cell.IsCovered)
                {
                    continue;
                }

                var right = Math.Min(columnX.Count - 1, column + Math.Max(1, cell.GridSpan));
                var bottom = Math.Min(rowY.Count - 1, row + Math.Max(1, cell.RowSpan));
                cells.Add(new TableCellBox(row, column, columnX[column], rowY[row], columnX[right] - columnX[column], rowY[bottom] - rowY[row], cell, LookOf(table, style, row, column)));
            }
        }

        return new TableGeometry(columnX, rowY, cells);
    }

    public static void Render(List<DisplayItem> items, SlideContext context, Table table, ITextMeasurer measurer, double frameWidth)
    {
        var geometry = Measure(context, table, measurer);
        var colors = context.Colors;
        foreach (var box in geometry.Cells)
        {
            var fill = box.Cell.Properties.Fill ?? box.Look.Fill ?? table.Properties.Fill;
            if (Paints.Fill(fill, colors, box.Width, box.Height) is { } paint)
            {
                items.Add(new VectorPathItem(VectorPath.Rectangle(box.X, box.Y, box.Width, box.Height), paint));
            }
        }

        foreach (var box in geometry.Cells)
        {
            var layout = LayoutCell(context, box.Cell, box.Look, measurer, box.X, box.Y, box.Width, box.Height);
            items.Add(new ClipBeginItem(box.X, box.Y, box.Width, box.Height));
            items.AddRange(layout.Items);
            items.Add(ClipEndItem.Instance);
        }

        foreach (var box in geometry.Cells)
        {
            var properties = box.Cell.Properties;
            var lastColumn = Math.Abs(box.X + box.Width - geometry.Width) < 0.01;
            var lastRow = Math.Abs(box.Y + box.Height - geometry.Height) < 0.01;
            AddBorder(items, properties.TopBorder ?? (box.Row == 0 ? box.Look.TopBorder : box.Look.InsideHorizontalBorder ?? box.Look.TopBorder), colors, box.X, box.Y, box.X + box.Width, box.Y);
            AddBorder(items, properties.LeftBorder ?? (box.Column == 0 ? box.Look.LeftBorder : box.Look.InsideVerticalBorder ?? box.Look.LeftBorder), colors, box.X, box.Y, box.X, box.Y + box.Height);
            AddBorder(items, properties.BottomBorder ?? (lastRow ? box.Look.BottomBorder : box.Look.InsideHorizontalBorder ?? box.Look.BottomBorder), colors, box.X, box.Y + box.Height, box.X + box.Width, box.Y + box.Height);
            AddBorder(items, properties.RightBorder ?? (lastColumn ? box.Look.RightBorder : box.Look.InsideVerticalBorder ?? box.Look.RightBorder), colors, box.X + box.Width, box.Y, box.X + box.Width, box.Y + box.Height);
        }
    }

    private static void AddBorder(List<DisplayItem> items, Outline? outline, ColorContext colors, double x1, double y1, double x2, double y2)
    {
        if (Paints.Stroke(outline, colors) is not { } stroke)
        {
            return;
        }

        items.Add(new VectorPathItem(new VectorPathBuilder().MoveTo(x1, y1).LineTo(x2, y2).Build(), null, stroke with { Cap = StrokeCap.Square }));
    }

    /// <summary>The style's look of a cell: the parts that apply, in PowerPoint's order of precedence.</summary>
    public static TableStyleFormat LookOf(Table table, TableStyle style, int row, int column)
    {
        var properties = table.Properties;
        var parts = new List<TableStylePart> { TableStylePart.WholeTable };
        var bodyRow = row - (properties.FirstRow ? 1 : 0);
        var bodyColumn = column - (properties.FirstColumn ? 1 : 0);
        if (properties.BandColumns && bodyColumn >= 0)
        {
            parts.Add(bodyColumn % 2 == 0 ? TableStylePart.Band1Vertical : TableStylePart.Band2Vertical);
        }

        if (properties.BandRows && bodyRow >= 0)
        {
            parts.Add(bodyRow % 2 == 0 ? TableStylePart.Band1Horizontal : TableStylePart.Band2Horizontal);
        }

        if (properties.LastColumn && column == table.ColumnCount - 1)
        {
            parts.Add(TableStylePart.LastColumn);
        }

        if (properties.FirstColumn && column == 0)
        {
            parts.Add(TableStylePart.FirstColumn);
        }

        if (properties.LastRow && row == table.RowCount - 1)
        {
            parts.Add(TableStylePart.LastRow);
        }

        if (properties.FirstRow && row == 0)
        {
            parts.Add(TableStylePart.FirstRow);
        }

        var look = new TableStyleFormat();
        foreach (var part in parts)
        {
            if (style.FormatOf(part) is not { } format)
            {
                continue;
            }

            look = new TableStyleFormat(
                format.Bold ?? look.Bold,
                format.Italic ?? look.Italic,
                format.TextColor ?? look.TextColor,
                format.FontReference ?? look.FontReference,
                format.Fill ?? look.Fill,
                format.LeftBorder ?? look.LeftBorder,
                format.RightBorder ?? look.RightBorder,
                format.TopBorder ?? look.TopBorder,
                format.BottomBorder ?? look.BottomBorder,
                format.InsideHorizontalBorder ?? look.InsideHorizontalBorder,
                format.InsideVerticalBorder ?? look.InsideVerticalBorder);
        }

        return look;
    }

    /// <summary>Lays out a cell's text in its rectangle (margins, anchoring, the style's text look under the cell's own runs).</summary>
    public static TextBodyLayout LayoutCell(SlideContext context, TableCell cell, TableStyleFormat look, ITextMeasurer measurer, double x, double y, double width, double height)
    {
        var properties = cell.Properties;
        var body = new BodyProperties(
            properties.LeftMargin ?? DefaultHorizontalMargin,
            properties.TopMargin ?? DefaultVerticalMargin,
            properties.RightMargin ?? DefaultHorizontalMargin,
            properties.BottomMargin ?? DefaultVerticalMargin,
            properties.Anchor ?? TextAnchor.Top,
            Wrap: true);
        var styleRun = new RunProperties(
            Bold: look.Bold,
            Italic: look.Italic,
            Fill: look.TextColor is null ? null : new SolidFill(look.TextColor),
            Latin: look.FontReference is null ? null : new TextFont(look.FontReference == "major" ? "+mj-lt" : "+mn-lt"));
        var styleList = new ListStyle(new ParagraphProperties(DefaultRun: styleRun), ValueList<ParagraphProperties?>.Empty);
        var frame = new AutoShape { Id = 0, Text = cell.Text };
        var resolved = new ResolvedShape(frame, new Transform2D(0, 0, 0, 0), ShapeGeometry.Rectangle, null, null, null, context.Colors, body,
            [context.Presentation.DefaultTextStyle, styleList, cell.Text.ListStyle], null, cell.Text);
        return TextBodyLayouter.Layout(resolved, cell.Text, context, measurer, x, y, width, height);
    }
}

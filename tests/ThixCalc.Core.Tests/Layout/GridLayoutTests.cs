using System.Diagnostics;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using Xunit.Abstractions;

namespace ThixCalc.Core.Tests.Layout;

/// <summary>The grid's viewport layout with the deterministic measurer: geometry, panes, hit testing, cell texts, display list.</summary>
public class GridLayoutTests(ITestOutputHelper output)
{
    private static (Workbook Workbook, Worksheet Sheet, GridMetrics Metrics, SheetLayout Layout) Setup()
    {
        var workbook = TestBooks.Create();
        var sheet = workbook.Sheets[0];
        var metrics = new GridMetrics(new FixedWidthTextMeasurer(), workbook, CalcLocale.German);
        return (workbook, sheet, metrics, new SheetLayout(sheet, metrics));
    }

    private static List<string> Texts(PageDisplayList list) => list.Items.OfType<GlyphRunItem>().Select(item => item.Text).ToList();

    [Fact]
    public void Column_widths_follow_Excels_pixel_rule()
    {
        var (_, sheet, metrics, layout) = Setup();
        Assert.Equal(7, metrics.MaxDigitWidthPixels);
        Assert.Equal(64, metrics.ColumnPixels(8.43));
        Assert.Equal(8.43, metrics.WidthInCharactersFromPixels(64), 2);
        Assert.Equal(64, layout.ColumnWidth(0, 1));
        Assert.Equal(20, layout.RowHeight(0, 1));
        sheet.Columns.Set(1, new AxisInfo(Size: 20, CustomSize: true));
        sheet.Columns.Set(2, new AxisInfo(Hidden: true));
        Assert.Equal(145, layout.ColumnWidth(1, 1));
        Assert.Equal(0, layout.ColumnWidth(2, 1));
        Assert.Equal(64 + 145, layout.ColumnLeft(2, 1));
        Assert.Equal(3, layout.ColumnAt(64 + 145 + 1, 1));
        Assert.Equal(128, layout.ColumnWidth(0, 2));
        Assert.Equal(1_048_576 * 20.0, layout.TotalHeight(1));
    }

    [Fact]
    public void Viewport_with_frozen_panes_and_scrolling()
    {
        var (_, sheet, _, layout) = Setup();
        sheet.View = sheet.View with { FrozenRows = 2, FrozenColumns = 1, TopLeft = new CellAddress(100, 10) };
        var viewport = layout.Viewport(800, 400);
        Assert.Equal([0, 1], viewport.FrozenRows.Select(row => row.Index));
        Assert.Equal(0, viewport.FrozenColumns.Single().Index);
        Assert.Equal(100, viewport.Rows[0].Index);
        Assert.Equal(10, viewport.Columns[0].Index);
        Assert.Equal(viewport.HeaderHeight + 40, viewport.Rows[0].Start);
        Assert.Equal(viewport.HeaderWidth + 64, viewport.Columns[0].Start);
        Assert.Equal(viewport.Rows[0].Start, viewport.RowY(100));
        Assert.Equal(viewport.HeaderHeight + 20, viewport.RowY(1));

        var hit = viewport.HitTest(viewport.Columns[1].Start + 5, viewport.Rows[2].Start + 5);
        Assert.Equal(new HitResult(HitKind.Cell, new CellAddress(102, 11)), hit);
        Assert.Equal(HitKind.ColumnHeader, viewport.HitTest(viewport.Columns[1].Start + 5, 2).Kind);
        Assert.Equal(HitKind.ColumnResize, viewport.HitTest(viewport.Columns[1].End, 2).Kind);
        Assert.Equal(HitKind.RowHeader, viewport.HitTest(2, viewport.Rows[0].Start + 5).Kind);
        Assert.Equal(HitKind.Corner, viewport.HitTest(1, 1).Kind);
        var selection = CellRange.Parse("K101:L102");
        var rect = viewport.RangeRect(selection);
        Assert.Equal(HitKind.FillHandle, viewport.HitTest(rect.Right, rect.Bottom, selection).Kind);
        Assert.Equal(new CellAddress(100, 10), viewport.ScrollTarget(new CellAddress(105, 12)));
        Assert.True(viewport.ScrollTarget(new CellAddress(500, 10)).Row > 400);
    }

    [Fact]
    public void Merged_cells_are_one_rectangle_and_hit_as_their_first_cell()
    {
        var (_, sheet, _, layout) = Setup();
        sheet.Merges.Add(CellRange.Parse("B2:C4"));
        var viewport = layout.Viewport(600, 300);
        var rect = viewport.CellRect(CellAddress.Parse("C3"));
        Assert.Equal(128, rect.Width);
        Assert.Equal(60, rect.Height);
        Assert.Equal(CellAddress.Parse("B2"), viewport.HitTest(rect.X + 100, rect.Y + 50).Cell);
    }

    [Fact]
    public void Texts_overflow_numbers_show_hashes_and_alignment_follows_the_type()
    {
        var (workbook, sheet, metrics, layout) = Setup();
        TestBooks.Fill(sheet, ("A1", "Ein sehr langer Text, der überläuft"), ("A2", "Lang und blockiert"), ("B2", "x"),
            ("A3", 123456789012.5), ("A4", 12345678), ("A5", 42), ("A6", true));
        sheet.SetStyleId(3, 0, workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "#,##0.00" }));
        workbook.Calculation.Recalculate();
        var viewport = layout.Viewport(600, 300);
        var list = GridRenderer.Render(viewport, metrics);
        var texts = Texts(list);
        Assert.Contains("Ein sehr langer Text, der überläuft", texts);
        Assert.Contains(texts, text => text.StartsWith('#') && text.All(character => character == '#'));
        Assert.Contains("1,23E+11", texts);
        Assert.Contains("WAHR", texts);

        // The overflowing text gets a clip wider than its cell; the blocked one stays in its cell.
        var clips = list.Items.OfType<ClipBeginItem>().ToList();
        Assert.Contains(clips, clip => clip.Width > 100 && clip.Y == viewport.RowY(0));
        Assert.Contains(clips, clip => clip.Width == 64 && clip.Y == viewport.RowY(1));

        var number = list.Items.OfType<GlyphRunItem>().First(item => item.Text == "42");
        var cell = viewport.CellRect(CellAddress.Parse("A5"));
        Assert.True(number.X > cell.X + cell.Width / 2, "Numbers are right-aligned");
        var display = GridRenderer.Present(sheet, 5, 0, metrics);
        Assert.Equal(HorizontalAlignment.Center, display.Horizontal);
    }

    [Fact]
    public void Wrapped_text_breaks_into_lines()
    {
        var (workbook, sheet, metrics, layout) = Setup();
        TestBooks.Set(sheet, "A1", "eins zwei drei vier fünf sechs");
        sheet.SetStyleId(0, 0, workbook.Styles.Intern(CellStyle.Default with { Alignment = CellAlignment.Default with { WrapText = true } }));
        sheet.Rows.Set(0, new AxisInfo(Size: 60, CustomSize: true));
        var list = GridRenderer.Render(layout.Viewport(600, 300), metrics);
        Assert.True(Texts(list).Count(text => text.Length > 0 && "eins zwei drei vier fünf sechs".Contains(text, StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public void Fills_borders_conditional_formats_and_headers_are_drawn()
    {
        var (workbook, sheet, metrics, layout) = Setup();
        TestBooks.Fill(sheet, ("A1", 5), ("A2", 50), ("A3", 500));
        sheet.SetStyleId(0, 1, workbook.Styles.Intern(CellStyle.Default with { Fill = CellFill.Solid(CalcColor.FromRgb(255, 0, 0)), Borders = CellBorders.All(BorderStyle.Thick) }));
        workbook.Styles.DifferentialFormats.Add(new DifferentialFormat { FillColor = CalcColor.FromRgb(0, 255, 0), Bold = true });
        sheet.ConditionalFormats.Add(new ConditionalFormat([CellRange.Parse("A1:A3")],
            [new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.GreaterThan, Formulas = ["100"], FormatId = 0 }]));
        sheet.ConditionalFormats.Add(new ConditionalFormat([CellRange.Parse("A1:A3")],
            [new ConditionalFormatRule { Type = ConditionalFormatType.DataBar, Priority = 2, Thresholds = [new(ConditionalValueType.Min), new(ConditionalValueType.Max)], Colors = [CalcColor.FromRgb(0, 0, 255)] }]));
        workbook.Calculation.Recalculate();
        var list = GridRenderer.Render(layout.Viewport(600, 300), metrics, new GridRenderOptions { Selection = [CellRange.Parse("A1")] });
        Assert.Contains(list.Items.OfType<RectangleItem>(), item => item.Fill == RgbColor.FromHex("FF0000"));
        Assert.Contains(list.Items.OfType<RectangleItem>(), item => item.Fill == RgbColor.FromHex("00FF00"));
        Assert.Contains(list.Items.OfType<RectangleItem>(), item => item.Fill is { R: 0, G: 0, B: 255 });
        Assert.Contains(list.Items.OfType<LineItem>(), item => item.Thickness == 3);
        Assert.Contains("A", Texts(list));
        Assert.Contains("1", Texts(list));
        var look = new ConditionalFormatEvaluator(sheet).Evaluate(2, 0);
        Assert.True(look!.Bold);
        Assert.Equal(1, look.DataBar!.Value.Fraction, 6);
    }

    [Fact]
    public void Viewport_queries_are_fast_on_a_large_sheet()
    {
        var (workbook, sheet, metrics, layout) = Setup();
        for (var row = 0; row < 200_000; row++)
        {
            sheet.SetValue(row, 0, row);
            sheet.SetValue(row, 1, "Zeile " + row);
            sheet.SetValue(row, 2, row * 1.5);
        }

        sheet.View = sheet.View with { TopLeft = new CellAddress(150_000, 0) };
        var stopwatch = Stopwatch.StartNew();
        const int frames = 50;
        PageDisplayList? list = null;
        for (var frame = 0; frame < frames; frame++)
        {
            var viewport = layout.Viewport(1600, 1000);
            list = GridRenderer.Render(viewport, metrics);
        }

        var perFrame = stopwatch.Elapsed.TotalMilliseconds / frames;
        output.WriteLine($"Viewport 1600×1000 px at row 150,000 of 200,000 filled rows: layout + display list {perFrame:F2} ms per frame, {list!.Items.Count} items");
        Assert.Contains("Zeile 150000", Texts(list));
        Assert.True(perFrame < 200);
        _ = workbook;
    }
}

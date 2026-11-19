using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Tests.Layout;

/// <summary>Pixel scrolling (cut-off rows behind the frozen panes), the header highlight and the selection layer.</summary>
public class ScrollAndSelectionTests
{
    private static (Worksheet Sheet, GridMetrics Metrics, SheetLayout Layout) Setup()
    {
        var workbook = Workbook.Create();
        var sheet = workbook.Sheets[0];
        var metrics = new GridMetrics(new FixedWidthTextMeasurer(), workbook, CalcLocale.German);
        return (sheet, metrics, new SheetLayout(sheet, metrics));
    }

    [Fact]
    public void A_pixel_scroll_cuts_the_first_row_and_clips_it_behind_the_frozen_rows()
    {
        var (sheet, metrics, layout) = Setup();
        sheet.View = sheet.View with { FrozenRows = 2 };
        var viewport = layout.Viewport(800, 400, 1, 0, layout.RowTop(10, 1) + 7);
        Assert.Equal(10, viewport.Rows[0].Index);
        Assert.Equal(viewport.ScrollTop - 7, viewport.Rows[0].Start);
        Assert.Equal(11, viewport.FirstWholeRow);
        var list = GridRenderer.Render(viewport, metrics);
        var clip = list.Items.OfType<ClipBeginItem>().First();
        Assert.Equal(viewport.ScrollTop, clip.Y);
        var (x, y) = layout.ScrollToShow(viewport, new CellAddress(10, 0));
        Assert.Equal(layout.RowTop(10, 1), y);
        Assert.Equal(0, x);
    }

    [Fact]
    public void Frozen_rows_and_columns_keep_their_cells_when_the_sheet_is_scrolled_both_ways()
    {
        // The last frozen row/column used to end at the scrolled position of the next one: its fill went up into the row above
        // and its text was clipped away (the budget sample scrolled down showed a green band in row 1 and an empty row 2).
        var (sheet, metrics, layout) = Setup();
        var workbook = sheet.Workbook;
        sheet.View = sheet.View with { FrozenRows = 2, FrozenColumns = 1 };
        TestBooks.Fill(sheet, ("A1", "Titel"), ("A2", "Kopf"));
        sheet.SetStyleId(1, 0, workbook.Styles.Intern(CellStyle.Default with { Fill = CellFill.Solid(CalcColor.FromRgb(16, 124, 65)) }));
        workbook.Calculation.Recalculate();
        var viewport = layout.Viewport(800, 400, 1, layout.ColumnLeft(4, 1), layout.RowTop(12, 1));

        var header = viewport.CellRect(CellAddress.Parse("A2"));
        Assert.Equal(viewport.RowY(1), header.Y);
        Assert.Equal(viewport.ScrollTop, header.Bottom);
        Assert.Equal(viewport.HeaderWidth, header.X);
        Assert.Equal(viewport.ScrollLeft, header.Right);

        var list = GridRenderer.Render(viewport, metrics);
        var fill = Assert.Single(list.Items.OfType<RectangleItem>(), item => item.Fill == RgbColor.FromHex("107C41"));
        Assert.True(fill.Y >= viewport.RowY(1) - 1 && fill.Height > 0, $"the fill stays in row 2: {fill.Y} {fill.Height}");
        var texts = list.Items.OfType<GlyphRunItem>().ToList();
        Assert.Contains(texts, run => run.Text == "Titel" && run.Y > viewport.RowY(0) && run.Y < viewport.RowY(1));
        Assert.Contains(texts, run => run.Text == "Kopf" && run.Y > viewport.RowY(1) && run.Y < viewport.ScrollTop);
        Assert.DoesNotContain(list.Items.OfType<ClipBeginItem>(), clip => clip.Height < 0 || clip.Width < 0);
    }

    [Fact]
    public void Selected_rows_and_columns_are_highlighted_in_both_headers()
    {
        var (sheet, metrics, layout) = Setup();
        sheet.View = sheet.View with { FrozenRows = 2, FrozenColumns = 1 };
        var viewport = layout.Viewport(800, 400, 1, 0, 0);
        var options = new GridRenderOptions { Selection = [new CellRange(3, 2, 5, 4)] };
        var list = GridRenderer.Render(viewport, metrics, options);
        var highlights = list.Items.OfType<RectangleItem>().Where(item => item.Fill == options.HeaderHighlight).ToList();
        Assert.Equal(3, highlights.Count(item => item.X == 0));
        Assert.Equal(3, highlights.Count(item => item.Y == 0));
    }

    [Fact]
    public void The_selection_layer_frames_the_range_and_shows_the_fill_handle()
    {
        var (_, _, layout) = Setup();
        var viewport = layout.Viewport(800, 400, 1, 0, 0);
        var look = new SelectionLook { Ranges = [new CellRange(1, 1, 3, 2)], ActiveCell = new CellAddress(1, 1) };
        var items = SelectionRenderer.Render(viewport, look);
        Assert.True(4 == items.OfType<LineItem>().Count(line => line.Color == look.Border && line.Thickness == 2), "the frame has four thick edges");
        Assert.Contains(items.OfType<RectangleItem>(), item => item.Fill == look.Border);
        // The active cell is the corner: the fill is the band right of it and the one below.
        Assert.Equal(2, items.OfType<RectangleItem>().Count(item => item.Fill == look.Fill));
    }
}

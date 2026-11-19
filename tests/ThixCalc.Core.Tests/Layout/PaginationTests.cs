using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Core.Tests.Formats;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Tests.Layout;

/// <summary>Seitenlayout: pages of the grid, fitting, title rows, header/footer codes, the print area, the xlsx round trip.</summary>
public class PaginationTests
{
    private static (Workbook Workbook, EditingSession Session, GridMetrics Metrics) Setup(int rows, int columns)
    {
        var workbook = Workbook.Create();
        var sheet = workbook.Sheets[0];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                sheet.SetValue(row, column, row * 100 + column);
            }
        }

        return (workbook, new EditingSession(workbook), new GridMetrics(new FixedWidthTextMeasurer(), workbook, CalcLocale.German));
    }

    [Fact]
    public void A_long_sheet_breaks_into_pages_down_then_over()
    {
        var (workbook, _, metrics) = Setup(120, 14);
        var pages = SheetPagination.Paginate(workbook.Sheets[0], metrics);
        // A4 portrait at 100 %: 15 pt rows → about 50 per page; 48 pt columns → 9 per page.
        Assert.True(pages.Count >= 4, $"{pages.Count} pages");
        Assert.Equal(0, pages[0].Cells.FirstRow);
        Assert.Equal(pages[0].Cells.FirstColumn, pages[1].Cells.FirstColumn);
        Assert.True(pages[1].Cells.FirstRow > pages[0].Cells.LastRow);
        Assert.All(pages, page => Assert.Equal(595.3, page.Width, 0));
    }

    [Fact]
    public void Fit_to_one_page_scales_down()
    {
        var (workbook, session, metrics) = Setup(120, 14);
        session.SetPageSetup(setup => setup with { FitToPage = true, FitToWidth = 1, FitToHeight = 1, Landscape = true });
        var pages = SheetPagination.Paginate(workbook.Sheets[0], metrics);
        Assert.Single(pages);
        Assert.True(pages[0].Scale < 0.6);
        Assert.True(pages[0].Width > pages[0].Height);
        session.Undo();
        Assert.False(workbook.Sheets[0].PageSetup.FitToPage);
    }

    [Fact]
    public void Title_rows_repeat_and_the_print_area_limits()
    {
        var (workbook, session, metrics) = Setup(150, 3);
        session.SetPrintTitles(0, 0);
        var pages = SheetPagination.Paginate(workbook.Sheets[0], metrics);
        Assert.Null(pages[0].TitleRows);
        Assert.Equal(0, pages[1].TitleRows?.FirstRow);
        var list = SheetPagination.Render(pages[1], metrics, 1, 2, pages.Count, "Mappe.xlsx", new DateTime(2026, 11, 19));
        Assert.Contains(list.Items.OfType<GlyphRunItem>(), run => run.Text == "1");

        session.Select(new CellRange(0, 0, 9, 1));
        session.SetPrintArea(session.Selection);
        Assert.Equal(new CellRange(0, 0, 9, 1), EditingSession.PrintAreaOf(workbook.Sheets[0]));
        Assert.Single(SheetPagination.Paginate(workbook.Sheets[0], metrics));
        session.Undo();
        Assert.Null(EditingSession.PrintAreaOf(workbook.Sheets[0]));
    }

    [Fact]
    public void Header_and_footer_codes_are_resolved()
    {
        var (left, center, right) = SheetPagination.Sections("&L&\"Arial,Fett\"&A&CSeite &P von &N&R&D", 2, 5, "Budget", "Mappe.xlsx", new DateTime(2026, 11, 19));
        Assert.Equal("Budget", left);
        Assert.Equal("Seite 2 von 5", center);
        Assert.Equal("19.11.2026", right);
    }

    [Fact]
    public void The_page_setup_survives_xlsx()
    {
        var (workbook, session, _) = Setup(5, 5);
        session.SetPageSetup(setup => setup with
        {
            Landscape = true, PaperSize = 8, FitToPage = true, FitToWidth = 1, FitToHeight = 0, LeftMargin = 0.25, PrintGridLines = true,
            PrintHeadings = true, CenterHorizontally = true, Footer = "&CSeite &P",
        });
        session.SetPrintTitles(0, 1);
        session.SetPrintArea(new CellRange(0, 0, 4, 3));
        var read = XlsxTests.RoundTrip(workbook);
        var setup = read.Sheets[0].PageSetup;
        Assert.True(setup.Landscape);
        Assert.Equal(8, setup.PaperSize);
        Assert.True(setup.FitToPage);
        Assert.Equal(0, setup.FitToHeight);
        Assert.Equal(0.25, setup.LeftMargin);
        Assert.True(setup.PrintGridLines && setup.PrintHeadings && setup.CenterHorizontally);
        Assert.Equal("&CSeite &P", setup.Footer);
        Assert.Equal(new CellRange(0, 0, 4, 3), EditingSession.PrintAreaOf(read.Sheets[0]));
        Assert.Equal(CellRange.Rows(0, 1), EditingSession.PrintTitlesOf(read.Sheets[0]));
    }
}

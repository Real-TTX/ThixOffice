using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Charts;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Core.Tests.Formats;
using ThixOffice.Charts;
using ThixOffice.Core.Models;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Charts;

/// <summary>
/// Charts in a workbook: series from a range by Excel's rules, live data, undo of every chart command, references and anchors
/// that follow inserted rows and renamed sheets, xlsx round trips (valid, unchanged charts byte for byte), printing.
/// </summary>
public class ChartWorkbookTests
{
    private static readonly ChartAnchor Anchor = new(new CellAddress(1, 4), 0, 0, new CellAddress(16, 11), 0, 0);

    private static (Workbook Workbook, EditingSession Session) Budget()
    {
        var workbook = Create("Budget");
        Fill(workbook["Budget"],
            ("A1", "Monat"), ("B1", "Einnahmen"), ("C1", "Ausgaben"),
            ("A2", "Januar"), ("B2", 2400), ("C2", 1900),
            ("A3", "Februar"), ("B3", 2400), ("C3", 2100),
            ("A4", "März"), ("B4", 2600), ("C4", 1750),
            ("A5", "April"), ("B5", 2500), ("C5", 2300));
        workbook.Calculation.Recalculate();
        var session = new EditingSession(workbook) { Locale = CalcLocale.German };
        return (workbook, session);
    }

    [Fact]
    public void A_range_with_headers_becomes_series_in_columns()
    {
        var (workbook, _) = Budget();

        var layout = ChartSources.Analyze(workbook["Budget"], CellRange.Parse("A1:C5"));

        Assert.False(layout.SeriesInRows);
        Assert.Equal(2, layout.Series.Count);
        Assert.Equal("Budget!$B$1", layout.Series[0].Name?.Reference?.Formula);
        Assert.Equal("Budget!$A$2:$A$5", layout.Series[0].Categories?.Formula);
        Assert.Equal("Budget!$C$2:$C$5", layout.Series[1].Values.Formula);
    }

    [Fact]
    public void One_cell_takes_its_region_and_wide_ranges_become_series_in_rows()
    {
        var workbook = Create("T");
        Fill(workbook["T"], ("B1", "Q1"), ("C1", "Q2"), ("D1", "Q3"), ("E1", "Q4"), ("A2", "Nord"), ("B2", 1), ("C2", 2), ("D2", 3), ("E2", 4), ("A3", "Süd"), ("B3", 5), ("C3", 6), ("D3", 7), ("E3", 8));

        var layout = ChartSources.Analyze(workbook["T"], CellRange.Parse("C2"));

        Assert.True(layout.SeriesInRows);
        Assert.Equal(["T!$B$2:$E$2", "T!$B$3:$E$3"], layout.Series.Select(series => series.Values.Formula));
        Assert.Equal("T!$B$1:$E$1", layout.Series[0].Categories?.Formula);
    }

    [Fact]
    public void Dates_in_the_first_column_make_a_time_axis()
    {
        var workbook = Create("T");
        var sheet = workbook["T"];
        Fill(sheet, ("A1", "Datum"), ("B1", "Wert"), ("A2", 45292), ("B2", 1), ("A3", 45323), ("B3", 2));
        var dateStyle = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "dd.mm.yyyy" });
        sheet.SetStyleId(1, 0, dateStyle);
        sheet.SetStyleId(2, 0, dateStyle);

        var layout = ChartSources.Analyze(sheet, CellRange.Parse("A1:B3"));
        var chart = ChartFactory.Create(ChartTemplates.Find("line")!, layout.Series, layout.DateCategories);

        Assert.True(layout.DateCategories);
        Assert.Contains(chart.Axes, axis => axis.Kind == AxisKind.Date);
    }

    [Fact]
    public void An_empty_selection_is_refused_in_German()
    {
        var workbook = Create("T");

        var error = Assert.Throws<BusinessException>(() => ChartSources.Analyze(workbook["T"], CellRange.Parse("A1")));

        Assert.Contains("Daten", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chart_shows_the_cells_live()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        var entry = session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        var source = new WorkbookChartSource(workbook, CalcLocale.German);

        Assert.Equal([2400, 2400, 2600, 2500], ChartDataResolver.Resolve(entry.Chart, source).Plots[0].Series[0].Values);
        session.Select(new CellAddress(1, 1));
        session.EnterInput("3000");

        var data = ChartDataResolver.Resolve(workbook["Budget"].Charts[0].Chart, source);
        Assert.Equal(3000, data.Plots[0].Series[0].Values[0]);
        Assert.Equal(["Januar", "Februar", "März", "April"], data.Categories);
        Assert.Equal("Einnahmen", data.Plots[0].Series[0].Name);
    }

    [Fact]
    public void Every_chart_command_undoes_and_redoes()
    {
        var (workbook, session) = Budget();
        var sheet = workbook["Budget"];
        session.Select(CellRange.Parse("A1:C5"));
        var entry = session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        Assert.Equal(entry.Id, session.SelectedChart?.Id);

        var steps = new List<Action>
        {
            () => session.ChangeChartType(entry.Id, ChartTemplates.Find("line-markers")!),
            () => session.ChangeChart(entry.Id, "Diagrammtitel", chart => ChartEdits.SetTitle(chart, true, "Haushalt")),
            () => session.ChangeChart(entry.Id, "Legende", chart => ChartEdits.SetLegend(chart, LegendPosition.Right)),
            () => session.ChangeChart(entry.Id, "Datenbeschriftungen", chart => ChartEdits.SetDataLabels(chart, true)),
            () => session.ChangeChart(entry.Id, "Farben", chart => ChartEdits.SetColorSet(chart, 2)),
            () => session.SwitchRowColumn(entry.Id),
            () => session.SetChartAnchor(entry.Id, Anchor with { From = new CellAddress(3, 6), To = new CellAddress(20, 14) }),
            () => session.SetChartData(entry.Id, "=Budget!$A$1:$B$5"),
            () => session.DeleteChart(entry.Id),
        };
        var states = new List<string> { State(sheet) };
        foreach (var step in steps)
        {
            step();
            states.Add(State(sheet));
            Assert.NotEqual(states[^2], states[^1]);
        }

        for (var index = steps.Count; index > 0; index--)
        {
            session.Undo();
            Assert.Equal(states[index - 1], State(sheet));
        }

        session.Undo();
        Assert.Empty(sheet.Charts);
        for (var index = 0; index <= steps.Count; index++)
        {
            session.Redo();
        }

        Assert.Equal(states[^1], State(sheet));
    }

    [Fact]
    public void Switching_rows_and_columns_turns_the_series()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        var entry = session.InsertChart(ChartTemplates.Find("column")!, Anchor);

        session.SwitchRowColumn(entry.Id);

        var chart = workbook["Budget"].Charts[0].Chart;
        Assert.Equal(4, chart.AllSeries.Count());
        Assert.Equal("Budget!$B$2:$C$2", chart.AllSeries.First().Values.Formula);
        Assert.Equal("Budget!$B$1:$C$1", chart.AllSeries.First().Categories?.Formula);
    }

    [Fact]
    public void Inserted_rows_move_the_chart_and_its_references()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        session.InsertChart(ChartTemplates.Find("column")!, Anchor);

        session.InsertRows(0, 2);

        var entry = workbook["Budget"].Charts[0];
        Assert.Equal(new CellAddress(3, 4), entry.Anchor.From);
        Assert.Equal("Budget!$B$4:$B$7", entry.Chart.AllSeries.First().Values.Formula);
        session.Undo();
        Assert.Equal("Budget!$B$2:$B$5", workbook["Budget"].Charts[0].Chart.AllSeries.First().Values.Formula);
    }

    [Fact]
    public void Renaming_the_sheet_renames_the_references()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        session.InsertChart(ChartTemplates.Find("column")!, Anchor);

        session.RenameSheet(0, "Mein Budget");

        Assert.All(ChartEdits.References(workbook.Sheets[0].Charts[0].Chart), formula => Assert.StartsWith("'Mein Budget'!", formula, StringComparison.Ordinal));
        Assert.Equal(2400, ChartDataResolver.Resolve(workbook.Sheets[0].Charts[0].Chart, new WorkbookChartSource(workbook, CalcLocale.German)).Plots[0].Series[0].Values[0]);
        session.Undo();
        Assert.All(ChartEdits.References(workbook.Sheets[0].Charts[0].Chart), formula => Assert.StartsWith("Budget!", formula, StringComparison.Ordinal));
    }

    [Fact]
    public void Charts_round_trip_through_xlsx_and_validate()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        var first = session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        session.ChangeChart(first.Id, "Titel", chart => ChartEdits.SetTitle(chart, true, "Haushalt 2026"));
        session.Select(CellRange.Parse("A1:B5"));
        session.InsertChart(ChartTemplates.Find("pie")!, Anchor with { From = new CellAddress(18, 4), To = new CellAddress(34, 11) });

        var bytes = XlsxTests.Write(workbook);
        XlsxTests.AssertValid(bytes);
        var read = XlsxTests.Read(bytes);

        var charts = read["Budget"].Charts;
        Assert.Equal(2, charts.Count);
        Assert.Equal("Haushalt 2026", charts[0].Chart.Title?.Text?.Literal);
        Assert.Equal(ChartType.Pie, charts[1].Chart.Plots[0].Type);
        Assert.Equal(Anchor.From, charts[0].Anchor.From);
        Assert.Equal(Anchor.To, charts[0].Anchor.To);
        Assert.Empty(read.UnsupportedContent);
        Assert.Equal([2400, 2400, 2600, 2500], charts[0].Chart.AllSeries.First().Values.Cache!.Select(value => value.Number));
    }

    [Fact]
    public void An_unchanged_chart_keeps_its_part_and_a_changed_one_is_rewritten()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        var saved = XlsxTests.Write(workbook);
        var opened = XlsxTests.Read(saved);
        var originalXml = ChartPartXml(saved);

        // Edit a cell only: the chart part stays byte for byte (its cache is Excel's to refresh).
        new EditingSession(opened).SetValue(new CellAddress(1, 1), CellValue.FromNumber(99));
        var again = XlsxTests.Write(opened);
        Assert.Equal(originalXml, ChartPartXml(again));

        // Change the chart: the part is written anew, still valid, the drawing still points to it.
        var editing = new EditingSession(opened);
        editing.ChangeChart(opened.Sheets[0].Charts[0].Id, "Titel", chart => ChartEdits.SetTitle(chart, true, "Neu"));
        var changed = XlsxTests.Write(opened);
        XlsxTests.AssertValid(changed);
        Assert.Contains(">Neu<", ChartPartXml(changed), StringComparison.Ordinal);
        Assert.Single(XlsxTests.Read(changed).Sheets[0].Charts);

        // Delete it: the drawing goes with it.
        editing.DeleteChart(opened.Sheets[0].Charts[0].Id);
        var deleted = XlsxTests.Write(opened);
        XlsxTests.AssertValid(deleted);
        using var package = SpreadsheetDocument.Open(new MemoryStream(deleted), false);
        Assert.Null(package.WorkbookPart!.WorksheetParts.First().DrawingsPart);
    }

    [Fact]
    public void Pictures_beside_charts_are_kept()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        var bytes = XlsxTests.Write(workbook);

        // Add a shape anchor to the drawing as another program would.
        using (var stream = new MemoryStream())
        {
            stream.Write(bytes);
            using (var package = SpreadsheetDocument.Open(stream, true))
            {
                var drawing = package.WorkbookPart!.WorksheetParts.First().DrawingsPart!;
                string xml;
                using (var reader = new StreamReader(drawing.GetStream()))
                {
                    xml = reader.ReadToEnd();
                }

                const string shape = "<xdr:twoCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>24</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"7\" name=\"Rechteck 1\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp><xdr:clientData/></xdr:twoCellAnchor>";
                xml = xml.Replace("</xdr:wsDr>", shape + "</xdr:wsDr>", StringComparison.Ordinal);
                using var output = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
                drawing.FeedData(output);
            }

            bytes = stream.ToArray();
        }

        var opened = XlsxTests.Read(bytes);
        new EditingSession(opened).ChangeChart(opened.Sheets[0].Charts[0].Id, "Titel", chart => ChartEdits.SetTitle(chart, true, "Mit Form"));
        var written = XlsxTests.Write(opened);
        XlsxTests.AssertValid(written);

        using var check = SpreadsheetDocument.Open(new MemoryStream(written), false);
        using var drawingReader = new StreamReader(check.WorkbookPart!.WorksheetParts.First().DrawingsPart!.GetStream());
        var drawingXml = drawingReader.ReadToEnd();
        Assert.Contains("Rechteck 1", drawingXml, StringComparison.Ordinal);
        Assert.Contains("c:chart", drawingXml, StringComparison.Ordinal);
    }

    [Fact]
    public void Charts_are_drawn_on_the_grid_and_on_printed_pages()
    {
        var (workbook, session) = Budget();
        session.Select(CellRange.Parse("A1:C5"));
        session.InsertChart(ChartTemplates.Find("column")!, Anchor);
        var metrics = new GridMetrics(new FixedWidthTextMeasurer(), workbook, CalcLocale.German);
        var layout = new SheetLayout(workbook["Budget"], metrics);

        var grid = GridRenderer.Render(layout.Viewport(1200, 800, 1), metrics);
        Assert.Contains(grid.Items.OfType<GlyphRunItem>(), run => run.Text == "Einnahmen");

        var pages = SheetPagination.Paginate(workbook["Budget"], metrics);
        Assert.True(SheetPagination.PrintedRange(workbook["Budget"])!.Value.Contains(Anchor.To));
        var printed = pages.Select((page, index) => SheetPagination.Render(page, metrics, index, index + 1, pages.Count, "Budget.xlsx", Now)).SelectMany(page => page.Items);
        Assert.Contains(printed.OfType<GlyphRunItem>(), run => run.Text == "Ausgaben");
    }

    [Fact]
    public void Anchors_convert_to_pixels_and_back()
    {
        var (workbook, _) = Budget();
        var metrics = new GridMetrics(new FixedWidthTextMeasurer(), workbook, CalcLocale.German);
        var layout = new SheetLayout(workbook["Budget"], metrics);
        var anchor = ChartLayer.AnchorAt(layout, new CellAddress(2, 3), 360, 216);

        var rect = ChartLayer.SheetRect(layout, anchor, 1);

        Assert.Equal(480, rect.Width, 0);
        Assert.Equal(288, rect.Height, 0);
        Assert.Equal(anchor, ChartLayer.AnchorOf(layout, rect, 1));
    }

    private static string State(Worksheet sheet) =>
        string.Join("; ", sheet.Charts.Select(chart => $"{chart.Id} {chart.Anchor} {ChartTemplates.Of(chart.Chart)?.Id} {chart.Chart.Title?.Text?.Literal} {chart.Chart.Legend?.Position} {chart.Chart.ColorSet} {chart.Chart.Plots.FirstOrDefault()?.Labels?.ShowValue} {string.Join("|", ChartEdits.References(chart.Chart))}"));

    private static string ChartPartXml(byte[] bytes)
    {
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var part = package.WorkbookPart!.WorksheetParts.First().DrawingsPart!.ChartParts.First();
        using var reader = new StreamReader(part.GetStream());
        return reader.ReadToEnd();
    }
}

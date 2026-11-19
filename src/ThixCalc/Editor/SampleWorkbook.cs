using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Editor;

/// <summary>
/// The workbook of the snapshots and the screenshots: a household budget for the first half of 2026 – a header row in the
/// accent with frozen panes, incomes and expenses per month in euros, totals with SUMME, shares in percent, a SVERWEIS into a
/// second sheet of categories, a data bar and a highlight rule – entered the way a user types it (German input).
/// </summary>
public static class SampleWorkbook
{
    public const string Title = "Haushaltsbudget 2026";

    /// <summary>The cell the caret is on in the pictures (the first expense of January).</summary>
    public static readonly CellAddress StartCell = new(6, 1);

    public static Workbook Create()
    {
        var workbook = Workbook.Create("Budget");
        var session = new EditingSession(workbook) { Locale = CalcLocale.German };
        var categories = session.AddSheet("Kategorien");
        session.ActivateSheet(0);

        void Row(int row, params string[] values)
        {
            for (var column = 0; column < values.Length; column++)
            {
                if (values[column].Length > 0)
                {
                    session.Select(new CellAddress(row, column));
                    session.EnterInput(values[column]);
                }
            }
        }

        string[] months = ["Januar", "Februar", "März", "April", "Mai", "Juni"];
        Row(0, Title);
        Row(1, ["Kategorie", .. months, "Summe", "Anteil", "Gruppe"]);
        Row(2, "Einnahmen");
        Row(3, "Gehalt", "3.450", "3.450", "3.450", "3.520", "3.520", "3.520");
        Row(4, "Nebenjob", "420", "380", "510", "460", "390", "540");
        Row(5, "Ausgaben");
        Row(6, "Miete", "1.150", "1.150", "1.150", "1.150", "1.150", "1.150");
        Row(7, "Strom", "86", "86", "86", "92", "92", "92");
        Row(8, "Lebensmittel", "612,40", "588,15", "640,90", "601,25", "655,30", "629,80");
        Row(9, "Versicherungen", "214", "214", "214", "214", "214", "214");
        Row(10, "Mobilität", "185,50", "172,20", "199,90", "240,10", "188,60", "205,00");
        Row(11, "Freizeit", "240", "310", "195", "420", "285", "360");
        Row(12, "Sparen", "500", "500", "500", "500", "500", "500");
        Row(14, "Summe Ausgaben");
        Row(15, "Überschuss");

        for (var row = 3; row <= 12; row++)
        {
            if (row == 5)
            {
                continue;
            }

            session.Select(new CellAddress(row, 7));
            session.EnterInput($"=SUMME(B{row + 1}:G{row + 1})");
            if (row >= 6)
            {
                session.Select(new CellAddress(row, 8));
                session.EnterInput($"=H{row + 1}/$H$15");
                session.Select(new CellAddress(row, 9));
                session.EnterInput($"=WENNFEHLER(SVERWEIS(A{row + 1};Kategorien!$A$2:$B$9;2;FALSCH);\"–\")");
            }
        }

        for (var column = 1; column <= 7; column++)
        {
            var letter = A1.ColumnName(column);
            session.Select(new CellAddress(14, column));
            session.EnterInput($"=SUMME({letter}7:{letter}13)");
            session.Select(new CellAddress(15, column));
            session.EnterInput($"=SUMME({letter}4:{letter}5)-{letter}15");
        }

        // Formats: the title, the header row in the accent, groups bold, euros, percent, totals with lines.
        session.Select(new CellRange(0, 0, 0, 0));
        session.ApplyStyle(style => style with { Font = style.Font with { Size = 16, Bold = true, Color = GridPalette.SampleAccent } });
        session.Select(new CellRange(1, 0, 1, 9));
        session.ApplyStyle(style => style with
        {
            Font = style.Font with { Bold = true, Color = GridPalette.SampleHeaderText },
            Fill = CellFill.Solid(GridPalette.SampleAccent),
            Alignment = style.Alignment with { Horizontal = HorizontalAlignment.Center },
        });
        foreach (var row in new[] { 2, 5 })
        {
            session.Select(new CellRange(row, 0, row, 9));
            session.ApplyStyle(style => style with { Font = style.Font with { Bold = true }, Fill = CellFill.Solid(GridPalette.SampleGroupFill) });
        }

        session.Select(new CellRange(3, 1, 15, 7));
        session.SetLocalNumberFormat("#.##0,00 €");
        session.Select(new CellRange(6, 8, 12, 8));
        session.SetLocalNumberFormat("0,0%");
        session.Select(new CellRange(14, 0, 15, 9));
        session.ApplyStyle(style => style with { Font = style.Font with { Bold = true } });
        session.Select(new CellRange(14, 0, 14, 9));
        session.SetBorderPreset(BorderPreset.Top);
        session.Select(new CellRange(15, 0, 15, 9));
        session.SetBorderPreset(BorderPreset.DoubleBottom);
        session.SetColumnWidth(0, 0, 18);
        session.SetColumnWidth(1, 7, 12.5);
        session.SetColumnWidth(8, 8, 9);
        session.SetColumnWidth(9, 9, 12);
        session.SetRowHeight(0, 0, 24);

        // A data bar over the shares, a highlight on months over budget for leisure.
        session.Select(new CellRange(6, 8, 12, 8));
        session.AddDataBar(GridPalette.SampleBar);
        session.Select(new CellRange(11, 1, 11, 6));
        session.AddHighlightRule(HighlightRule.GreaterThan, "300", null, GridPalette.SampleAlertFill, GridPalette.SampleAlertText);
        session.FreezePanes(new CellAddress(2, 1));

        // The categories sheet (the SVERWEIS source).
        session.ActivateSheet(categories.Index);
        Row(0, "Posten", "Gruppe");
        Row(1, "Miete", "Wohnen");
        Row(2, "Strom", "Wohnen");
        Row(3, "Lebensmittel", "Alltag");
        Row(4, "Versicherungen", "Vorsorge");
        Row(5, "Mobilität", "Alltag");
        Row(6, "Freizeit", "Freizeit");
        Row(7, "Sparen", "Vorsorge");
        session.Select(new CellRange(0, 0, 0, 1));
        session.SetBold(true);
        session.SetColumnWidth(0, 1, 16);
        session.SetTabColor(categories.Index, GridPalette.SampleTab);
        session.ActivateSheet(0);
        session.Select(StartCell);
        return workbook;
    }

    /// <summary>
    /// The charts of the pictures below the budget: "Einnahmen und Ausgaben" as clustered columns (salary, expenses, surplus per
    /// month) and "Ausgaben nach Kategorie" as a pie with percentages – live on the budget's cells.
    /// </summary>
    public static (SheetChart Columns, SheetChart Pie) AddCharts(EditingSession session, Core.Layout.SheetLayout layout)
    {
        var sheet = session.ActiveSheet;
        ThixOffice.Charts.ChartDataRef Range(int firstRow, int firstColumn, int lastRow, int lastColumn) =>
            new(ChartReferences.Of(sheet, new CellRange(firstRow, firstColumn, lastRow, lastColumn))) { IsNumeric = false };
        ThixOffice.Charts.ChartText Name(int row) => ThixOffice.Charts.ChartText.Ref(ChartReferences.Of(sheet, CellRange.Cell(row, 0)));
        var months = Range(1, 1, 1, 6);
        ThixOffice.Charts.SeriesSpec Series(int row) => new(Name(row), months, Range(row, 1, row, 6) with { IsNumeric = true });

        var columns = ThixOffice.Charts.ChartFactory.Create(ThixOffice.Charts.ChartTemplates.Find("column")!, [Series(3), Series(14), Series(15)]);
        columns = ThixOffice.Charts.ChartEdits.SetTitle(columns, true, "Einnahmen und Ausgaben");
        var pie = ThixOffice.Charts.ChartFactory.Create(ThixOffice.Charts.ChartTemplates.Find("pie")!,
            [new ThixOffice.Charts.SeriesSpec(ThixOffice.Charts.ChartText.Of("Ausgaben"), Range(6, 0, 12, 0), Range(6, 7, 12, 7) with { IsNumeric = true })]);
        pie = ThixOffice.Charts.ChartEdits.SetTitle(pie, true, "Ausgaben nach Kategorie");
        pie = pie with { Plots = [pie.Plots[0] with { Labels = new ThixOffice.Charts.DataLabels(ShowValue: false, ShowPercent: true, Position: ThixOffice.Charts.DataLabelPosition.BestFit) }] };

        var columnAnchor = Core.Layout.ChartLayer.AnchorAt(layout, new CellAddress(17, 1), 380, 205);
        var pieAnchor = Core.Layout.ChartLayer.AnchorAt(layout, new CellAddress(17, 7), 320, 205);
        var first = session.PasteChart(columns, columnAnchor, "Einnahmen und Ausgaben");
        var second = session.PasteChart(pie, pieAnchor, "Ausgaben nach Kategorie");
        return (first, second);
    }
}

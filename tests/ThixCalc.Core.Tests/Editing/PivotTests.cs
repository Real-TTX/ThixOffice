using System.Diagnostics;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using ThixOffice.Core.Models;
using Xunit.Abstractions;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>
/// PivotTables in the core: aggregation against hand-computed fixtures (blanks, errors, text in number columns, dates), the
/// three layouts, grouping, filters, sorting, Werte anzeigen als, refresh after the source changed, undo/redo of every edit,
/// PIVOTDATENZUORDNEN, Details anzeigen, the guard against editing pivot cells, and 100 000 records under a second.
/// </summary>
public class PivotTests(ITestOutputHelper output)
{
    /// <summary>Sales: Region, Produkt, Datum, Umsatz, Menge – 10 records with a blank Umsatz, a text "n/a" and fixed dates.</summary>
    internal static EditingSession Sales()
    {
        var workbook = Create("Daten");
        var sheet = workbook["Daten"];
        object?[][] rows =
        [
            ["Region", "Produkt", "Datum", "Umsatz", "Menge"],
            ["Nord", "Äpfel", new DateTime(2024, 1, 15), 100, 1],
            ["Süd", "Äpfel", new DateTime(2024, 2, 10), 200, 2],
            ["Nord", "Birnen", new DateTime(2024, 4, 5), 50, 3],
            ["West", "Birnen", new DateTime(2024, 7, 20), 80, 4],
            ["Süd", "Birnen", new DateTime(2025, 1, 3), 120, 5],
            ["Nord", "Äpfel", new DateTime(2025, 3, 30), 70, 6],
            ["West", "Äpfel", new DateTime(2025, 6, 1), null, 7],
            ["Süd", "Kirschen", new DateTime(2025, 8, 12), 30, 8],
            ["nord", "Kirschen", new DateTime(2025, 11, 2), "n/a", 9],
            ["West", "Birnen", new DateTime(2025, 12, 24), 40, 10],
        ];
        var date = workbook.Styles.Intern(workbook.Styles[0] with { NumberFormat = "dd.mm.yyyy" });
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] is { } value)
                {
                    sheet.SetValue(row, column, ToValue(value));
                }
            }

            if (row > 0)
            {
                sheet.SetStyleId(row, 2, date);
            }
        }

        workbook.Calculation.Recalculate();
        return new EditingSession(workbook) { Locale = CalcLocale.German };
    }

    private static string Text(Worksheet sheet, string address) => sheet[address] is var value && value.IsNumber
        ? value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : value.IsError ? CellErrors.NameOf(value.Error) : value.Text;

    /// <summary>The pivot's cells as rows of text (for compact assertions).</summary>
    private static List<string> Grid(Worksheet sheet, PivotTable pivot)
    {
        var range = pivot.Range!.Value;
        var lines = new List<string>();
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            var cells = new List<string>();
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                cells.Add(Text(sheet, new CellAddress(row, column).ToString()));
            }

            lines.Add(string.Join("|", cells).TrimEnd('|'));
        }

        return lines;
    }

    /// <summary>Umsatz (with a text in it, so Excel would count it) as Summe.</summary>
    private static PivotTable AddSum(EditingSession session, PivotTable pivot, int field)
    {
        pivot = session.AddPivotField(pivot, field, asValue: true);
        return session.ChangePivotDataField(pivot, pivot.DataFields.Length - 1, data => data with { Function = PivotFunction.Sum, Name = PivotEngine.DataFieldName(PivotFunction.Sum, "Umsatz") });
    }

    private static (EditingSession Session, PivotTable Pivot, Worksheet Sheet) NewPivot()
    {
        var session = Sales();
        var pivot = session.CreatePivotTable("Daten!$A$1:$E$11", new PivotDestination(NewSheet: true));
        return (session, pivot, session.ActiveSheet);
    }

    [Fact]
    public void A_new_pivot_goes_to_a_new_sheet_at_A3_and_fields_land_like_in_excel()
    {
        var (session, pivot, sheet) = NewPivot();
        Assert.StartsWith("Tabelle", sheet.Name, StringComparison.Ordinal);
        Assert.Equal(0, session.Workbook.IndexOf(sheet));
        Assert.Equal(new CellAddress(2, 0), pivot.Location);
        Assert.Same(pivot, session.ActivePivot);

        pivot = session.AddPivotField(pivot, 0); // Region: text → Zeilen
        pivot = AddSum(session, pivot, 3); // Umsatz: numbers → Werte (Summe)
        Assert.Equal([0], pivot.RowFields.ToArray());
        Assert.Equal("Summe von Umsatz", pivot.DataFields[0].Name);
        Assert.Equal(
        [
            "Zeilenbeschriftungen|Summe von Umsatz",
            "Nord|220",
            "Süd|350",
            "West|120",
            "Gesamtergebnis|690",
        ], Grid(sheet, pivot));
        Assert.Equal(CellRange.Parse("A3:B7"), pivot.Range);
    }

    [Fact]
    public void Rows_and_columns_cross_with_totals_and_blank_combinations_stay_empty()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Column);
        pivot = AddSum(session, pivot, 3);
        Assert.Equal(
        [
            "Summe von Umsatz|Spaltenbeschriftungen",
            "Zeilenbeschriftungen|Äpfel|Birnen|Kirschen|Gesamtergebnis",
            "Nord|170|50|0|220",
            "Süd|200|120|30|350",
            "West||120||120",
            "Gesamtergebnis|370|290|30|690",
        ], Grid(sheet, pivot));
    }

    [Theory]
    [InlineData(PivotFunction.Count, "Anzahl von Umsatz", new[] { "4", "3", "2", "9" })]
    [InlineData(PivotFunction.CountNumbers, "Anzahl Zahlen von Umsatz", new[] { "3", "3", "2", "8" })]
    [InlineData(PivotFunction.Average, "Mittelwert von Umsatz", new[] { "73.3333333333333", "116.666666666667", "60", "86.25" })]
    [InlineData(PivotFunction.Max, "Max. von Umsatz", new[] { "100", "200", "80", "200" })]
    [InlineData(PivotFunction.Min, "Min. von Umsatz", new[] { "50", "30", "40", "30" })]
    [InlineData(PivotFunction.Product, "Produkt von Umsatz", new[] { "350000", "720000", "3200", "806400000000000" })]
    public void Value_fields_summarize_by_every_function(PivotFunction function, string name, string[] expected)
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        pivot = session.ChangePivotDataField(pivot, 0, data => data with { Function = function, Name = PivotEngine.DataFieldName(function, "Umsatz") });
        var grid = Grid(sheet, pivot);
        Assert.Equal("Zeilenbeschriftungen|" + name, grid[0]);
        for (var index = 0; index < expected.Length; index++)
        {
            var value = double.Parse(grid[index + 1].Split('|')[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(double.Parse(expected[index], System.Globalization.CultureInfo.InvariantCulture), value, 9);
        }
    }

    [Fact]
    public void Standard_deviation_and_variance_follow_excel()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = AddSum(session, pivot, 3);
        pivot = AddSum(session, pivot, 3);
        pivot = AddSum(session, pivot, 3);
        pivot = session.ChangePivotDataField(pivot, 0, data => data with { Function = PivotFunction.StdDev, Name = "s" });
        pivot = session.ChangePivotDataField(pivot, 1, data => data with { Function = PivotFunction.Var, Name = "v" });
        pivot = session.ChangePivotDataField(pivot, 2, data => data with { Function = PivotFunction.VarP, Name = "vp" });

        // Umsatz numbers: 100, 200, 50, 80, 120, 70, 30, 40 (blank and "n/a" left out).
        double[] numbers = [100, 200, 50, 80, 120, 70, 30, 40];
        var mean = numbers.Average();
        var squares = numbers.Sum(value => (value - mean) * (value - mean));
        var cells = pivot.Range!.Value;
        Assert.Equal(Math.Sqrt(squares / 7), sheet.GetValue(cells.LastRow, cells.FirstColumn + 1).Number, 9);
        Assert.Equal(squares / 7, sheet.GetValue(cells.LastRow, cells.FirstColumn + 2).Number, 9);
        Assert.Equal(squares / 8, sheet.GetValue(cells.LastRow, cells.FirstColumn + 3).Number, 9);
    }

    [Fact]
    public void Errors_in_the_source_show_in_sums_but_are_counted()
    {
        var session = Sales();
        session.ActiveSheet.SetValue("D3", CellError.DivisionByZero);
        session.Workbook.Calculation.Recalculate();
        var pivot = session.CreatePivotTable("Daten!A1:E11", new PivotDestination(NewSheet: true));
        var sheet = session.ActiveSheet;
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        Assert.Equal("Süd|#DIV/0!", Grid(sheet, pivot)[2]);
        pivot = session.ChangePivotDataField(pivot, 0, data => data with { Function = PivotFunction.Count });
        Assert.Equal("Süd|3", Grid(sheet, pivot)[2]);
    }

    [Fact]
    public void Dates_group_by_years_and_quarters()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 2, PivotAxis.Row);
        pivot = AddSum(session, pivot, 3);
        pivot = session.GroupPivotField(pivot, 2, PivotDateGroups.Years | PivotDateGroups.Quarters);
        Assert.Equal(
        [
            "Zeilenbeschriftungen|Summe von Umsatz",
            "2024|430",
            "Qrtl1|300",
            "Qrtl2|50",
            "Qrtl3|80",
            "2025|260",
            "Qrtl1|190",
            "Qrtl2",
            "Qrtl3|30",
            "Qrtl4|40",
            "Gesamtergebnis|690",
        ], Grid(sheet, pivot));
        Assert.Equal(1, PivotStyles.IndentAt(sheet, pivot.Range!.Value.FirstRow + 2, 0));
        Assert.Throws<BusinessException>(() => session.GroupPivotField(pivot, 0, PivotDateGroups.Months));
    }

    [Fact]
    public void Numbers_group_in_bands()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 4, PivotAxis.Row);
        pivot = AddSum(session, pivot, 3);
        pivot = session.GroupPivotField(pivot, 4, PivotDateGroups.None, new PivotNumberGroup(1, 10, 5));
        Assert.Equal(["Zeilenbeschriftungen|Summe von Umsatz", "1-5|550", "6-10|140", "Gesamtergebnis|690"], Grid(sheet, pivot));
        Assert.Throws<BusinessException>(() => session.GroupPivotField(pivot, 4, PivotDateGroups.None, new PivotNumberGroup(1, 10, 0)));
    }

    [Fact]
    public void Filters_hidden_items_label_and_value_filters()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Filter);

        // The filter field stands above the body (the body moves down to make room).
        Assert.Equal("Produkt|(Alle)", Grid(sheet, pivot)[0]);
        pivot = session.ChangePivotField(pivot, 1, field => field with { SelectedItem = PivotItems.KeyOf("Äpfel") });
        var grid = Grid(sheet, pivot);
        Assert.Equal("Produkt|Äpfel", grid[0]);
        Assert.Contains("Nord|170", grid);
        Assert.Contains("Gesamtergebnis|370", grid);

        pivot = session.ChangePivotField(pivot, 1, field => field with { SelectedItem = null });
        pivot = session.ChangePivotField(pivot, 0, field => field with { HiddenItems = [PivotItems.KeyOf("West")] });
        Assert.DoesNotContain(Grid(sheet, pivot), line => line.StartsWith("West", StringComparison.Ordinal));
        Assert.Contains("Gesamtergebnis|570", Grid(sheet, pivot));

        pivot = session.ChangePivotField(pivot, 0, field => field with { HiddenItems = [], LabelFilter = new PivotFilter(PivotFilterKind.BeginsWith, "S") });
        Assert.Equal(["Süd|350", "Gesamtergebnis|350"], Grid(sheet, pivot).Skip(3).ToList());

        pivot = session.ChangePivotField(pivot, 0, field => field with { LabelFilter = null, ValueFilter = new PivotFilter(PivotFilterKind.Top, "2", DataField: 0) });
        Assert.Equal(["Nord|220", "Süd|350", "Gesamtergebnis|570"], Grid(sheet, pivot).Skip(3).ToList());

        pivot = session.ChangePivotField(pivot, 0, field => field with { ValueFilter = new PivotFilter(PivotFilterKind.GreaterThan, "200", DataField: 0) });
        Assert.Equal(["Nord|220", "Süd|350", "Gesamtergebnis|570"], Grid(sheet, pivot).Skip(3).ToList());
    }

    [Fact]
    public void Items_sort_by_label_or_by_value()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        pivot = session.ChangePivotField(pivot, 0, field => field with { Sort = new PivotSort(Descending: true) });
        Assert.Equal(["West|120", "Süd|350", "Nord|220"], Grid(sheet, pivot).Skip(1).Take(3).ToList());
        pivot = session.ChangePivotField(pivot, 0, field => field with { Sort = new PivotSort(Descending: true, DataField: 0) });
        Assert.Equal(["Süd|350", "Nord|220", "West|120"], Grid(sheet, pivot).Skip(1).Take(3).ToList());
    }

    [Fact]
    public void Show_values_as_shares_differences_and_running_totals()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        pivot = session.ChangePivotDataField(pivot, 0, data => data with { ShowAs = PivotShowAs.PercentOfGrandTotal });
        var range = pivot.Range!.Value;
        Assert.Equal(220.0 / 690, sheet.GetValue(range.FirstRow + 1, 1).Number, 12);
        Assert.Equal(1.0, sheet.GetValue(range.LastRow, 1).Number, 12);
        Assert.Equal("0.00%", sheet.GetStyle(range.FirstRow + 1, 1).NumberFormat);

        pivot = session.ChangePivotDataField(pivot, 0, data => data with { ShowAs = PivotShowAs.Difference, BaseField = 0, BaseItem = PivotDataField.PreviousItem });
        Assert.Equal(["Nord", "Süd|130", "West|-230"], Grid(sheet, pivot).Skip(1).Take(3).ToList());

        pivot = session.ChangePivotDataField(pivot, 0, data => data with { ShowAs = PivotShowAs.RunningTotal, BaseField = 0 });
        Assert.Equal(["Nord|220", "Süd|570", "West|690"], Grid(sheet, pivot).Skip(1).Take(3).ToList());

        pivot = session.AddPivotField(pivot, 1, PivotAxis.Column);
        pivot = session.ChangePivotDataField(pivot, 0, data => data with { ShowAs = PivotShowAs.PercentOfRowTotal, BaseField = -1 });
        Assert.Equal(1.0, sheet.GetValue(range.FirstRow + 2, 4).Number, 12); // Nord's total column
        Assert.Equal(170.0 / 220, sheet.GetValue(range.FirstRow + 2, 1).Number, 12);
    }

    [Fact]
    public void Outline_and_tabular_layouts_with_subtotals_and_repeated_labels()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Row);
        pivot = AddSum(session, pivot, 3);
        Assert.Equal(["Zeilenbeschriftungen|Summe von Umsatz", "Nord|220", "Äpfel|170", "Birnen|50", "Kirschen|0"], Grid(sheet, pivot).Take(5).ToList());

        pivot = session.SetPivotOptions(pivot, table => table with { Layout = PivotLayout.Outline });
        Assert.Equal(["Region|Produkt|Summe von Umsatz", "Nord||220", "|Äpfel|170", "|Birnen|50"], Grid(sheet, pivot).Take(4).ToList());

        pivot = session.SetPivotOptions(pivot, table => table with { Layout = PivotLayout.Tabular });
        var tabular = Grid(sheet, pivot);
        Assert.Equal(["Region|Produkt|Summe von Umsatz", "Nord|Äpfel|170", "|Birnen|50", "|Kirschen|0", "Nord Ergebnis||220"], tabular.Take(5).ToList());

        pivot = session.SetPivotOptions(pivot, table => table with { RepeatLabels = true });
        Assert.Equal("Nord|Birnen|50", Grid(sheet, pivot)[2]);

        pivot = session.ChangePivotField(pivot, 0, field => field with { Subtotals = false });
        Assert.DoesNotContain(Grid(sheet, pivot), line => line.Contains("Ergebnis", StringComparison.Ordinal) && !line.StartsWith("Gesamt", StringComparison.Ordinal));
        pivot = session.SetPivotOptions(pivot, table => table with { ColumnGrandTotals = false });
        Assert.DoesNotContain(Grid(sheet, pivot), line => line.StartsWith("Gesamtergebnis", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_value_fields_go_side_by_side_or_below_each_other()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        pivot = session.AddPivotField(pivot, 4);
        Assert.Equal(["Zeilenbeschriftungen|Summe von Umsatz|Summe von Menge", "Nord|220|19"], Grid(sheet, pivot).Take(2).ToList());

        pivot = session.MovePivotField(pivot, PivotTable.ValuesField, PivotAxis.Row);
        Assert.Equal(["Zeilenbeschriftungen", "Nord", "Summe von Umsatz|220", "Summe von Menge|19"], Grid(sheet, pivot).Take(4).ToList());
    }

    [Fact]
    public void Refresh_reads_the_changed_source_and_keeps_the_layout()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        var data = session.Workbook["Daten"];
        data.SetValue("D2", 1000);
        data.SetValue("A2", "Ost");
        Assert.Contains("Nord|220", Grid(sheet, pivot)); // not before Aktualisieren
        pivot = session.RefreshPivot(pivot);
        Assert.Contains("Ost|1000", Grid(sheet, pivot));
        Assert.Contains("Nord|120", Grid(sheet, pivot));
        session.Undo();
        Assert.Contains("Nord|220", Grid(sheet, session.FindPivot(pivot.Name)!.Value.Pivot));

        // A bigger source through Datenquelle ändern.
        data.SetValue("A12", "Nord");
        data.SetValue("D12", 5);
        pivot = session.ChangePivotSource(session.FindPivot(pivot.Name)!.Value.Pivot, "Daten!A1:E12");
        Assert.Contains("Gesamtergebnis|1595", Grid(sheet, pivot));
    }

    [Fact]
    public void Every_edit_undoes_and_redoes_exactly()
    {
        var (session, pivot, sheet) = NewPivot();
        var states = new List<(List<string> Grid, PivotTable Pivot)>();
        void Snapshot() => states.Add((Grid(sheet, session.FindPivot("PivotTable1")!.Value.Pivot), session.FindPivot("PivotTable1")!.Value.Pivot));
        Snapshot();
        Func<PivotTable, PivotTable>[] edits =
        [
            table => session.AddPivotField(table, 0),
            table => session.AddPivotField(table, 4),
            table => session.AddPivotField(table, 1, PivotAxis.Column),
            table => session.AddPivotField(table, 2, PivotAxis.Filter),
            table => session.GroupPivotField(table, 2, PivotDateGroups.Years),
            table => session.ChangePivotDataField(table, 0, data => data with { Function = PivotFunction.Average }),
            table => session.ChangePivotField(table, 0, field => field with { HiddenItems = [PivotItems.KeyOf("Süd")] }),
            table => session.SetPivotOptions(table, options => options with { Layout = PivotLayout.Tabular, Style = "PivotStyleMedium9" }),
            table => session.MovePivotField(table, 1, PivotAxis.Row),
            table => session.RemovePivotField(table, 0),
            table => session.MovePivot(table, sheet.Name, new CellAddress(5, 3)),
        ];
        foreach (var edit in edits)
        {
            pivot = edit(session.FindPivot("PivotTable1")!.Value.Pivot);
            Snapshot();
        }

        for (var index = states.Count - 2; index >= 0; index--)
        {
            session.Undo();
            var current = session.FindPivot("PivotTable1")!.Value.Pivot;
            Assert.Equal(states[index].Pivot.RowFields.ToArray(), current.RowFields.ToArray());
            Assert.Equal(states[index].Pivot.Location, current.Location);
            Assert.Equal(states[index].Grid, Grid(sheet, current));
        }

        for (var index = 1; index < states.Count; index++)
        {
            session.Redo();
            var current = session.FindPivot("PivotTable1")!.Value.Pivot;
            Assert.Equal(states[index].Grid, Grid(sheet, current));
        }

        session.DeletePivot(session.FindPivot("PivotTable1")!.Value.Pivot);
        Assert.Empty(sheet.PivotTables);
        Assert.Empty(session.Workbook.PivotCaches);
        session.Undo();
        Assert.Single(sheet.PivotTables);
        Assert.Equal(states[^1].Grid, Grid(sheet, sheet.PivotTables[0]));
    }

    [Fact]
    public void Pivot_cells_refuse_edits_but_formulas_and_getpivotdata_read_them()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Column);
        pivot = AddSum(session, pivot, 3);
        session.Select(new CellAddress(3, 1));
        Assert.Throws<BusinessException>(() => session.EnterInput("5"));
        Assert.Throws<BusinessException>(() => session.ClearContents());

        session.Select(new CellAddress(0, 8));
        session.EnterInput("=PIVOTDATENZUORDNEN(\"Umsatz\";$A$3;\"Region\";\"Süd\";\"Produkt\";\"Birnen\")");
        Assert.Equal(120.0, sheet["I1"].Number);
        session.Select(new CellAddress(1, 8));
        session.EnterInput("=PIVOTDATENZUORDNEN(\"Summe von Umsatz\";$A$3;\"Region\";\"Nord\")");
        Assert.Equal(220.0, sheet["I2"].Number);
        session.Select(new CellAddress(2, 8));
        session.EnterInput("=PIVOTDATENZUORDNEN(\"Umsatz\";$A$3)");
        Assert.Equal(690.0, sheet["I3"].Number);
        session.Select(new CellAddress(3, 8));
        session.EnterInput("=PIVOTDATENZUORDNEN(\"Umsatz\";$A$3;\"Region\";\"Ost\")");
        Assert.Equal(CellError.Reference, sheet["I4"].Error);
        session.Select(new CellAddress(4, 8));
        session.EnterInput("=B5*2");
        Assert.Equal(sheet["B5"].Number * 2, sheet["I5"].Number);
    }

    [Fact]
    public void Details_show_the_records_behind_a_value_on_a_new_sheet()
    {
        var (session, pivot, sheet) = NewPivot();
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        var details = session.ShowPivotDetails(new CellAddress(3, 1)); // Nord (Nord and nord are one item)
        Assert.Equal("Region", details["A1"].Text);
        Assert.Equal(4.0, Enumerable.Range(1, 10).Count(row => details.GetValue(row, 0).IsText));
        Assert.Single(details.Tables);
        Assert.Throws<BusinessException>(() => session.ShowPivotDetails(new CellAddress(2, 0)));
    }

    [Fact]
    public void A_pivot_over_a_table_follows_the_table_on_refresh()
    {
        var session = Sales();
        session.Select(new CellAddress(1, 0));
        session.CreateTable(CellRange.Parse("A1:E11"), hasHeaders: true);
        Assert.Equal("Tabelle1", session.ProposedPivotSource());
        var pivot = session.CreatePivotTable("Tabelle1", new PivotDestination(NewSheet: false, "Daten", new CellAddress(0, 7)));
        pivot = session.AddPivotField(pivot, 0);
        pivot = AddSum(session, pivot, 3);
        var sheet = session.Workbook["Daten"];
        session.Select(new CellAddress(11, 0));
        session.EnterInput("Ost"); // the table grows
        session.Select(new CellAddress(11, 3));
        session.EnterInput("9");
        pivot = session.RefreshPivot(pivot);
        Assert.Contains("Ost|9", Grid(sheet, pivot));
    }

    [Fact]
    public void A_source_without_headers_or_rows_is_refused()
    {
        var session = Sales();
        Assert.Throws<BusinessException>(() => session.CreatePivotTable("Daten!A1:E1", new PivotDestination(true)));
        Assert.Throws<BusinessException>(() => session.CreatePivotTable("Nirgendwo!A1:B5", new PivotDestination(true)));
        session.ActiveSheet.SetValue("B1", CellValue.Blank);
        Assert.Throws<BusinessException>(() => session.CreatePivotTable("Daten!A1:E11", new PivotDestination(true)));
    }

    [Fact]
    public void Hundred_thousand_records_compute_in_under_a_second()
    {
        var workbook = Create("Daten");
        var sheet = workbook["Daten"];
        sheet.SetValue(0, 0, "Region");
        sheet.SetValue(0, 1, "Produkt");
        sheet.SetValue(0, 2, "Monat");
        sheet.SetValue(0, 3, "Umsatz");
        string[] regions = ["Nord", "Süd", "Ost", "West", "Mitte"];
        for (var row = 1; row <= 100_000; row++)
        {
            sheet.SetValue(row, 0, regions[row % regions.Length]);
            sheet.SetValue(row, 1, "Produkt " + (row % 37));
            sheet.SetValue(row, 2, row % 12 + 1);
            sheet.SetValue(row, 3, row % 1000);
        }

        var session = new EditingSession(workbook) { Locale = CalcLocale.German };
        var watch = Stopwatch.StartNew();
        var cache = PivotCache.Build(workbook, 1, "Daten", new CellRange(0, 0, 100_000, 3));
        var build = watch.Elapsed;
        var pivot = new PivotTable("P", 1, new CellAddress(0, 6))
        {
            Fields = [new PivotField(0) { Axis = PivotAxis.Row }, new PivotField(1) { Axis = PivotAxis.Row }, new PivotField(2) { Axis = PivotAxis.Column }, new PivotField(3)],
            RowFields = [0, 1],
            ColumnFields = [2],
            DataFields = [new PivotDataField(3, "Summe von Umsatz")],
        };
        watch.Restart();
        var result = PivotEngine.Compute(pivot, cache)!;
        var compute = watch.Elapsed;
        output.WriteLine($"cache {build.TotalMilliseconds:0} ms, compute {compute.TotalMilliseconds:0} ms, {result.Cells.Count} cells");
        Assert.True(build + compute < TimeSpan.FromSeconds(1), $"cache {build.TotalMilliseconds} ms + compute {compute.TotalMilliseconds} ms");
        var total = result.Values.Last(entry => entry.Conditions.Count == 0).Value.Number;
        Assert.Equal(Enumerable.Range(1, 100_000).Sum(row => (double)(row % 1000)), total);
        Assert.NotNull(session);
    }
}

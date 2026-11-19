using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>The editing session: input in German, undo/redo, structure, clipboard, fill, sort, filter, find, sheets, queries.</summary>
public class EditingSessionTests
{
    private static EditingSession Session(params (string Address, object? Value)[] cells)
    {
        var workbook = TestBooks.Create();
        workbook.Calculation.Locale = CalcLocale.German;
        TestBooks.Fill(workbook.Sheets[0], cells);
        workbook.Calculation.Recalculate();
        return new EditingSession(workbook);
    }

    private static void Type(EditingSession session, string address, string text)
    {
        session.Select(CellAddress.Parse(address));
        session.EnterInput(text);
    }

    private static string Formula(EditingSession session, string address) =>
        FormulaFormatter.Format(session.ActiveSheet.GetFormula(CellAddress.Parse(address).Row, CellAddress.Parse(address).Column)!, session.Workbook, CellAddress.Parse(address));

    [Fact]
    public void German_input_is_recognized_and_formulas_are_calculated()
    {
        var session = Session();
        var sheet = session.ActiveSheet;
        Type(session, "A1", "1,5");
        Type(session, "A2", "2.500");
        Type(session, "A3", "=SUMME(A1:A2;10)");
        Type(session, "B1", "31.12.2024");
        Type(session, "B2", "12%");
        Type(session, "B3", "WAHR");
        Assert.Equal(1.5, sheet["A1"].Number);
        Assert.Equal(2500, sheet["A2"].Number);
        Assert.Equal(2511.5, sheet["A3"].Number);
        Assert.Equal("SUM(A1:A2,10)", Formula(session, "A3"));
        Assert.Equal(45657, sheet["B1"].Number);
        Assert.Equal("dd.mm.yyyy", sheet.GetStyle(0, 1).NumberFormat);
        Assert.Equal("0%", sheet.GetStyle(1, 1).NumberFormat);
        Assert.True(sheet["B3"].Boolean);

        session.Select(CellAddress.Parse("A3"));
        Assert.Equal("=SUMME(A1:A2;10)", session.FormulaBarText);
        session.Select(CellAddress.Parse("A1"));
        Assert.Equal("1,5", session.FormulaBarText);
        session.Select(CellAddress.Parse("B1"));
        Assert.Equal("31.12.2024", session.FormulaBarText);
        session.Select(CellAddress.Parse("B2"));
        Assert.Equal("12%", session.FormulaBarText);
        Assert.Equal("B2", session.NameBoxText);
    }

    [Fact]
    public void Invalid_formulas_are_refused_in_German()
    {
        var session = Session();
        session.Select(CellAddress.Parse("A1"));
        var error = Assert.Throws<BusinessException>(() => session.EnterInput("=SUMME(1;"));
        Assert.Contains("Formel", error.Message, StringComparison.Ordinal);
        Assert.True(session.ActiveSheet["A1"].IsBlank);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_and_redo_restore_cells_and_values()
    {
        var session = Session(("A1", 1), ("A2", "=A1*2"));
        Type(session, "A1", "5");
        Assert.Equal(10, session.ActiveSheet["A2"].Number);
        Assert.Equal("Eingabe", session.UndoLabel);
        session.Undo();
        Assert.Equal(1, session.ActiveSheet["A1"].Number);
        Assert.Equal(2, session.ActiveSheet["A2"].Number);
        session.Redo();
        Assert.Equal(10, session.ActiveSheet["A2"].Number);
    }

    [Fact]
    public void Inserting_and_deleting_rows_rewrites_references_and_undoes_exactly()
    {
        var session = Session(("A1", 1), ("A2", 2), ("A3", 3), ("B1", "=SUM(A1:A3)"), ("B2", "=A3*10"), ("C5", "=$A$2"));
        var sheet = session.ActiveSheet;
        session.ActiveSheet.Merges.Add(CellRange.Parse("D4:E5"));
        session.InsertRows(1, 2);
        Assert.Equal("SUM(A1:A5)", Formula(session, "B1"));
        Assert.Equal("A5*10", Formula(session, "B4"));
        Assert.Equal("$A$4", Formula(session, "C7"));
        Assert.Equal(6, sheet["B1"].Number);
        Assert.Equal([CellRange.Parse("D6:E7")], sheet.Merges.Ranges);

        session.DeleteRows(4, 1);
        Assert.Equal("SUM(A1:A4)", Formula(session, "B1"));
        Assert.Equal("#REF!*10", Formula(session, "B4"));
        Assert.Equal(CellError.Reference, sheet["B4"].Error);

        session.Undo();
        Assert.Equal("A5*10", Formula(session, "B4"));
        Assert.Equal(30, sheet["B4"].Number);
        session.Undo();
        Assert.Equal("SUM(A1:A3)", Formula(session, "B1"));
        Assert.Equal("A3*10", Formula(session, "B2"));
        Assert.Equal("$A$2", Formula(session, "C5"));
        Assert.Equal([CellRange.Parse("D4:E5")], sheet.Merges.Ranges);
        Assert.Equal(6, sheet["B1"].Number);
    }

    [Fact]
    public void Columns_and_cell_shifts()
    {
        var session = Session(("A1", 1), ("B1", 2), ("C1", "=A1+B1"), ("A2", 5), ("A3", 6));
        session.InsertColumns(1, 1);
        Assert.Equal("A1+C1", Formula(session, "D1"));
        session.DeleteColumns(0, 1);
        Assert.Equal("#REF!+B1", Formula(session, "C1"));
        session.Undo();
        session.Undo();
        session.InsertCells(CellRange.Parse("A2"), CellShift.Down);
        Assert.Equal(5, session.ActiveSheet["A3"].Number);
        Assert.Equal(6, session.ActiveSheet["A4"].Number);
        session.DeleteCells(CellRange.Parse("A2"), CellShift.Up);
        Assert.Equal(5, session.ActiveSheet["A2"].Number);
    }

    [Fact]
    public void Copy_paste_adjusts_relative_references_and_cut_paste_moves()
    {
        var session = Session(("A1", 1), ("A2", 2), ("B1", "=A1*2"), ("C1", "=B1+$A$1"));
        var sheet = session.ActiveSheet;
        session.Select(CellRange.Parse("B1:C1"));
        var copy = session.Copy();
        session.Select(CellAddress.Parse("B2"));
        session.Paste(copy);
        Assert.Equal("A2*2", Formula(session, "B2"));
        Assert.Equal("B2+$A$1", Formula(session, "C2"));
        Assert.Equal(5, sheet["C2"].Number);

        session.Select(CellAddress.Parse("A1"));
        var cut = session.Cut();
        session.Select(CellAddress.Parse("E5"));
        session.Paste(cut);
        Assert.True(sheet["A1"].IsBlank);
        Assert.Equal(1, sheet["E5"].Number);
        Assert.Equal("E5*2", Formula(session, "B1"));
        Assert.Equal("B1+$E$5", Formula(session, "C1"));
        session.Undo();
        Assert.Equal("A1*2", Formula(session, "B1"));
        Assert.Equal(1, sheet["A1"].Number);

        session.Select(CellRange.Parse("A1:A2"));
        var values = session.Copy();
        session.Select(CellAddress.Parse("G1"));
        session.Paste(values, new PasteOptions(Transpose: true));
        Assert.Equal(1, sheet["G1"].Number);
        Assert.Equal(2, sheet["H1"].Number);
        Assert.Equal("1\r\n2\r\n", values.Text);
    }

    [Fact]
    public void Pasting_text_from_other_programs()
    {
        var session = Session();
        session.Select(CellAddress.Parse("A1"));
        session.PasteText("Name\tBetrag\r\nMüller\t1.234,50\r\n");
        Assert.Equal("Müller", session.ActiveSheet["A2"].Text);
        Assert.Equal(1234.5, session.ActiveSheet["B2"].Number);
    }

    [Fact]
    public void Fill_and_autofill_series()
    {
        var session = Session(("A1", 1), ("A2", 3), ("B1", "Montag"), ("C1", "Jan"), ("D1", "Artikel 9"), ("E1", 45658), ("F1", "=A1*10"), ("G1", 7));
        var sheet = session.ActiveSheet;
        sheet.SetStyleId(0, 4, session.Workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "dd.mm.yyyy" }));
        session.AutoFill(CellRange.Parse("A1:A2"), CellRange.Parse("A1:A5"));
        Assert.Equal(9, sheet["A5"].Number);
        session.AutoFill(CellRange.Parse("B1"), CellRange.Parse("B1:B3"));
        Assert.Equal("Mittwoch", sheet["B3"].Text);
        session.AutoFill(CellRange.Parse("C1"), CellRange.Parse("C1:C13"));
        Assert.Equal("Jan", sheet["C13"].Text);
        Assert.Equal("Dez", sheet["C12"].Text);
        session.AutoFill(CellRange.Parse("D1"), CellRange.Parse("D1:D3"));
        Assert.Equal("Artikel 11", sheet["D3"].Text);
        session.AutoFill(CellRange.Parse("E1"), CellRange.Parse("E1:E3"));
        Assert.Equal(45660, sheet["E3"].Number);
        session.AutoFill(CellRange.Parse("F1"), CellRange.Parse("F1:F3"));
        Assert.Equal("A3*10", Formula(session, "F3"));
        session.AutoFill(CellRange.Parse("G1"), CellRange.Parse("G1:G3"));
        Assert.Equal(7, sheet["G3"].Number);
        session.Select(CellRange.Parse("G1:I1"));
        session.FillRight();
        Assert.Equal(7, sheet["I1"].Number);
    }

    [Fact]
    public void Sort_by_two_keys_keeps_blank_rows_last()
    {
        var session = Session(("A1", "Name"), ("B1", "Wert"), ("A2", "Bert"), ("B2", 2), ("A3", "anna"), ("B3", 5), ("A4", "Bert"), ("B4", 1), ("A5", null), ("B5", 9), ("C2", "=B2*10"));
        var sheet = session.ActiveSheet;
        session.Sort(CellRange.Parse("A1:C5"), [new SortKey(0), new SortKey(1, Descending: true)]);
        Assert.Equal(["Name", "anna", "Bert", "Bert"], new[] { "A1", "A2", "A3", "A4" }.Select(cell => sheet[cell].Text));
        Assert.Equal(new[] { 5.0, 2, 1, 9 }, new[] { "B2", "B3", "B4", "B5" }.Select(cell => sheet[cell].Number));
        Assert.Equal(20, sheet["C3"].Number);
        session.Undo();
        Assert.Equal("Bert", sheet["A2"].Text);
        Assert.Equal(20, sheet["C2"].Number);
    }

    [Fact]
    public void Autofilter_hides_rows_and_status_bar_ignores_them()
    {
        var session = Session(("A1", "Stadt"), ("B1", "Betrag"), ("A2", "Berlin"), ("B2", 10), ("A3", "Bonn"), ("B3", 20), ("A4", "Berlin"), ("B4", 30));
        var sheet = session.ActiveSheet;
        session.Select(CellAddress.Parse("A1"));
        session.ToggleAutoFilter();
        Assert.Equal(CellRange.Parse("A1:B4"), sheet.AutoFilter!.Range);
        Assert.Equal(["Berlin", "Bonn"], session.FilterValues(0));
        session.SetFilter(0, new FilterColumn { Values = ["Berlin"] });
        Assert.True(sheet.Rows.IsHidden(2));
        Assert.False(sheet.Rows.IsHidden(3));
        session.Select(CellRange.Parse("B2:B4"));
        Assert.Equal(40, session.Statistics.Sum);
        Assert.Equal(2, session.Statistics.Count);
        session.SetFilter(1, new FilterColumn { Custom = [new CustomFilter(FilterOperator.GreaterThan, "15")] });
        Assert.True(sheet.Rows.IsHidden(1));
        session.ClearFilters();
        Assert.False(sheet.Rows.IsHidden(1));
        Assert.False(sheet.Rows.IsHidden(2));
    }

    [Fact]
    public void Find_and_replace()
    {
        var session = Session(("A1", "Apfel"), ("A2", "Apfelsaft"), ("B1", "=SUM(A3:A4)"), ("A3", 1), ("A4", 2));
        Assert.Equal(2, session.FindAll("apfel").Count);
        Assert.Single(session.FindAll("apfel", new FindOptions(WholeCell: true)));
        Assert.Single(session.FindAll("SUMME"));
        Assert.Equal(CellAddress.Parse("A2"), session.FindNext("Apf*"));
        Assert.Equal(2, session.ReplaceAll("Apfel", "Birne"));
        Assert.Equal("Birnesaft", session.ActiveSheet["A2"].Text);
        Assert.Equal(1, session.ReplaceAll("A4", "A3"));
        Assert.Equal("SUM(A3:A3)", Formula(session, "B1"));
    }

    [Fact]
    public void Merges_styles_and_borders()
    {
        var session = Session(("A1", "Titel"), ("B1", "weg"));
        var sheet = session.ActiveSheet;
        session.Select(CellRange.Parse("A1:C1"));
        session.Merge(center: true);
        Assert.Single(sheet.Merges.Ranges);
        Assert.True(sheet["B1"].IsBlank);
        Assert.Equal(HorizontalAlignment.Center, sheet.GetStyle(0, 0).Alignment.Horizontal);
        session.Select(CellAddress.Parse("B1"));
        Assert.Equal(CellAddress.Parse("A1"), session.ActiveCell);

        session.Select(CellRange.Parse("A3:B4"));
        session.SetBold(true);
        session.SetBorders(BorderStyle.Thin, outlineOnly: true);
        Assert.True(sheet.GetStyle(3, 1).Font.Bold);
        Assert.Equal(BorderStyle.Thin, sheet.GetStyle(2, 0).Borders.Top.Style);
        Assert.False(sheet.GetStyle(2, 0).Borders.Bottom.IsVisible);
        session.Undo();
        session.Undo();
        Assert.False(sheet.GetStyle(3, 1).Font.Bold);

        session.SelectColumns(4, 4);
        session.SetFill(CalcColor.FromRgb(255, 255, 0));
        Assert.Equal(FillPattern.Solid, sheet.GetStyle(500_000, 4).Fill.Pattern);
    }

    [Fact]
    public void Navigation_with_control_arrows_and_selection()
    {
        var session = Session(("A1", 1), ("A2", 2), ("A3", 3), ("A6", 6), ("C1", "x"));
        session.Select(CellAddress.Parse("A1"));
        session.Move(Direction.Down, toEdge: true);
        Assert.Equal(CellAddress.Parse("A3"), session.ActiveCell);
        session.Move(Direction.Down, toEdge: true);
        Assert.Equal(CellAddress.Parse("A6"), session.ActiveCell);
        session.Move(Direction.Down, toEdge: true);
        Assert.Equal(new CellAddress(SheetLimits.LastRow, 0), session.ActiveCell);
        session.Select(CellAddress.Parse("A1"));
        session.Move(Direction.Right, toEdge: true);
        Assert.Equal(CellAddress.Parse("C1"), session.ActiveCell);
        session.Select(CellAddress.Parse("A1"));
        session.Move(Direction.Down, extend: true, toEdge: true);
        Assert.Equal(CellRange.Parse("A1:A3"), session.Selection);
        session.Select(CellAddress.Parse("A2"));
        session.SelectAll();
        Assert.Equal(CellRange.Parse("A1:A3"), session.Selection);
        session.SelectAll();
        Assert.Equal(CellRange.Whole, session.Selection);
        session.Select(CellRange.Parse("A1:B2"));
        session.MoveWithinSelection(backwards: false, byRows: true);
        Assert.Equal(CellAddress.Parse("B1"), session.ActiveCell);
        session.MoveWithinSelection(backwards: false, byRows: true);
        Assert.Equal(CellAddress.Parse("A2"), session.ActiveCell);
    }

    [Fact]
    public void Sheets_and_names()
    {
        var session = Session(("A1", 5));
        var second = session.AddSheet();
        Assert.Equal("Tabelle2", second.Name);
        session.ActivateSheet(1);
        Type(session, "A1", "=Tabelle1!A1*2");
        session.RenameSheet(0, "Daten");
        Assert.Equal("Daten!A1*2", Formula(session, "A1"));
        session.DefineName("Faktor", "=Daten!$A$1");
        Type(session, "A2", "=Faktor+1");
        Assert.Equal(6, second["A2"].Number);
        session.Select(CellAddress.Parse("A1"));
        session.DeleteSheet(0);
        Assert.Equal(CellError.Reference, second["A1"].Error);
        session.Undo();
        Assert.Equal(10, second["A1"].Number);
        Assert.Equal("Daten!A1*2", Formula(session, "A1"));
        var copy = session.CopySheet(0);
        Assert.Equal("Daten (2)", copy.Name);
        Assert.Equal(5, copy["A1"].Number);
        Assert.Throws<BusinessException>(() => session.DefineName("A1", "=1"));
        Assert.Throws<BusinessException>(() => session.RenameSheet(1, "Daten"));
    }

    [Fact]
    public void Validation_refuses_invalid_input()
    {
        var session = Session();
        session.Select(CellRange.Parse("A1:A5"));
        session.AddValidation(new DataValidation { Ranges = [CellRange.Parse("A1:A5")], Type = ValidationType.WholeNumber, Operator = ComparisonOperator.Between, Formula1 = "1", Formula2 = "10", Error = "Nur 1 bis 10" });
        session.AddValidation(new DataValidation { Ranges = [CellRange.Parse("B1")], Type = ValidationType.List, Formula1 = "\"rot,grün,blau\"" });
        Type(session, "A1", "5");
        var error = Assert.Throws<BusinessException>(() => Type(session, "A2", "11"));
        Assert.Equal("Nur 1 bis 10", error.Message);
        Type(session, "B1", "grün");
        Assert.Throws<BusinessException>(() => Type(session, "B1", "gelb"));
        Assert.Equal(3, Validation.ListEntries(session.ActiveSheet, CellAddress.Parse("B1"), session.ActiveSheet.Validations[1]).Count);
    }

    [Fact]
    public void Status_bar_statistics()
    {
        var session = Session(("A1", 2), ("A2", 4), ("A3", "Text"), ("A4", "=A1*3"));
        session.Select(CellRange.Parse("A1:A5"));
        var statistics = session.Statistics;
        Assert.Equal(12, statistics.Sum);
        Assert.Equal(4, statistics.Average);
        Assert.Equal(4, statistics.Count);
        Assert.Equal(3, statistics.NumericCount);
        Assert.Equal(2, statistics.Minimum);
        Assert.Equal(6, statistics.Maximum);
    }

    [Fact]
    public void Changes_are_published_once_per_command_with_their_regions()
    {
        var session = Session(("A1", 1), ("B1", "=A1+1"));
        var events = new List<WorkbookChangedEventArgs>();
        session.CellsChanged += (_, args) => events.Add(args);
        Type(session, "A1", "7");
        Assert.Single(events);
        Assert.Contains(events[0].Regions, region => region.Range.Contains(CellAddress.Parse("A1")));
        Assert.Contains(events[0].Regions, region => region.Range.Contains(CellAddress.Parse("B1")));
        session.InsertRows(0, 1);
        Assert.True(events[^1].StructureChanged);
    }
}

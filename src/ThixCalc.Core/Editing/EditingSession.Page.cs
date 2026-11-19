using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Editing;

public sealed partial class EditingSession
{
    private const string PrintAreaName = "_xlnm.Print_Area";
    private const string PrintTitlesName = "_xlnm.Print_Titles";

    /// <summary>Seitenlayout of the active sheet changed (paper, orientation, margins, scaling, options, header/footer): one undo step.</summary>
    public void SetPageSetup(Func<PageSetup, PageSetup> change, string label = "Seite einrichten")
    {
        var sheet = ActiveSheet;
        Execute(label, () => ChangeSheetState(sheet, () => sheet.PageSetup = change(sheet.PageSetup)));
    }

    /// <summary>The print area of a sheet (its built-in name _xlnm.Print_Area), or null.</summary>
    public static CellRange? PrintAreaOf(Worksheet sheet) => ReferenceOfName(sheet, PrintAreaName);

    /// <summary>The rows to repeat on every page (_xlnm.Print_Titles), or null.</summary>
    public static CellRange? PrintTitlesOf(Worksheet sheet) => ReferenceOfName(sheet, PrintTitlesName) is { IsWholeRows: true } rows ? rows : null;

    /// <summary>Druckbereich festlegen (the selection) or aufheben (null).</summary>
    public void SetPrintArea(CellRange? range) => SetSheetName(PrintAreaName, range, range is null ? "Druckbereich aufheben" : "Druckbereich festlegen");

    /// <summary>Drucktitel: the rows that repeat at the top of every page (null: none).</summary>
    public void SetPrintTitles(int? firstRow, int? lastRow) =>
        SetSheetName(PrintTitlesName, firstRow is { } first ? CellRange.Rows(first, lastRow ?? first) : null, "Drucktitel");

    private static CellRange? ReferenceOfName(Worksheet sheet, string name)
    {
        var defined = sheet.Workbook.Names.FirstOrDefault(candidate => candidate.SheetId == sheet.Id && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (defined is null)
        {
            return null;
        }

        // Print_Titles may list rows and columns ("Tabelle1!$1:$2,Tabelle1!$A:$A"): the rows count here.
        var references = FormulaTokens.Scan(defined.Formula, CalcLocale.Invariant).Where(token => token.Kind == FormulaTokenKind.Reference && token.Range is not null).ToList();
        var rows = references.FirstOrDefault(token => token.Range!.Value.IsWholeRows);
        return (name == PrintTitlesName ? rows ?? references.FirstOrDefault() : references.FirstOrDefault())?.Range;
    }

    private void SetSheetName(string name, CellRange? range, string label)
    {
        var sheet = ActiveSheet;
        var names = Workbook.Names;
        Execute(label, () =>
        {
            var before = names.ToList();
            names.RemoveAll(candidate => candidate.SheetId == sheet.Id && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (range is { } target)
            {
                var text = target.IsWholeRows && !target.IsWholeColumns
                    ? $"${target.FirstRow + 1}:${target.LastRow + 1}"
                    : $"${A1.ColumnName(target.FirstColumn)}${target.FirstRow + 1}:${A1.ColumnName(target.LastColumn)}${target.LastRow + 1}";
                names.Add(new DefinedName(name, FormulaTokens.QuoteSheet(sheet.Name) + "!" + text, sheet.Id));
            }

            var after = names.ToList();
            Record(new ActionStep(() => Restore(names, before), () => Restore(names, after)));
            Workbook.Calculation.Rebuild();
        });
    }

    private static void Restore(List<DefinedName> names, List<DefinedName> state)
    {
        names.Clear();
        names.AddRange(state);
    }
}

using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>
/// Where and how a formula is evaluated: the engine, the formula's sheet and cell, array mode (dynamic arrays / array
/// formulas evaluate ranges element by element; legacy formulas intersect them implicitly), LET/LAMBDA variables.
/// </summary>
internal sealed class EvaluationContext
{
    public EvaluationContext(CalcEngine engine, Worksheet sheet, int row, int column, bool arrayMode, FormulaCell? cell = null)
    {
        Engine = engine;
        Sheet = sheet;
        Row = row;
        Column = column;
        ArrayMode = arrayMode;
        Cell = cell;
    }

    public CalcEngine Engine { get; }

    public Workbook Workbook => Engine.Workbook;

    public Worksheet Sheet { get; }

    public int Row { get; }

    public int Column { get; }

    public CellAddress Origin => new(Row, Column);

    public FormulaCell? Cell { get; }

    public bool ArrayMode { get; set; }

    public Scope? Scope { get; set; }

    public CalcLocale Locale => Engine.Locale;

    public bool Date1904 => Engine.Workbook.Date1904;

    /// <summary>Set when the evaluation needed array semantics (a range or array where one value was expected).</summary>
    public bool UsedArrays { get; set; }

    /// <summary>Nesting of LAMBDA calls (recursion guard).</summary>
    public int Depth { get; set; }
}

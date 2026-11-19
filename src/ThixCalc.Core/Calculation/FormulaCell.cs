using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

internal enum FormulaState : byte
{
    Clean,
    Dirty,
    Computing,
}

/// <summary>
/// A formula in a cell: the shared <see cref="Formula"/>, the cell it sits in (kept current when rows/columns move), the
/// cached result and – for arrays – the whole array with the range it covers (a fixed legacy array formula
/// <see cref="ArrayRange"/> or a dynamic array's spill <see cref="SpillRange"/>). A node of the dependency graph.
/// </summary>
internal sealed class FormulaCell
{
    public FormulaCell(Worksheet sheet, int row, int column, Formula formula, bool isDynamicArray)
    {
        Sheet = sheet;
        Row = row;
        Column = column;
        Formula = formula;
        IsDynamicArray = isDynamicArray;
    }

    public Worksheet Sheet { get; set; }

    public int Row { get; set; }

    public int Column { get; set; }

    public CellAddress Address => new(Row, Column);

    public Formula Formula { get; set; }

    /// <summary>Evaluated with dynamic-array semantics (formulas typed in ThixCalc, files' dynamic arrays); false: legacy implicit intersection.</summary>
    public bool IsDynamicArray { get; set; }

    /// <summary>The value of the cell (the top-left element of an array result).</summary>
    public CellValue Value { get; set; }

    /// <summary>The array result of an array formula or a spilling dynamic array.</summary>
    public ArrayValue? ArrayResult { get; set; }

    /// <summary>The fixed range of a legacy (Ctrl+Shift+Enter) array formula, anchored here.</summary>
    public CellRange? ArrayRange { get; set; }

    /// <summary>The range the dynamic array currently spills into (anchor included), or null.</summary>
    public CellRange? SpillRange { get; set; }

    /// <summary>The range a dynamic array would spill into while something blocks it (#SPILL!).</summary>
    public CellRange? BlockedSpill { get; set; }

    /// <summary>The last evaluation needed array semantics (a range or array where a single value was expected).</summary>
    public bool UsedArrayEvaluation { get; set; }

    /// <summary>The last evaluation hit a circular reference.</summary>
    public bool IsCircular { get; set; }

    internal FormulaState State { get; set; } = FormulaState.Dirty;

    /// <summary>Position in the engine's list of formulas (O(1) removal).</summary>
    internal int EngineIndex { get; set; } = -1;

    public override string ToString() => $"{Sheet.Name}!{Address}";
}

using System.Collections.Immutable;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// One coordinate pair of a reference, stored the R1C1 way: an absolute coordinate is the row/column index, a relative one
/// the offset from the formula's cell. So a formula filled down a column is ONE shared object, and copying a formula does
/// not change it. <see cref="Resolve"/> gives the cell for a formula at <paramref name="origin"/>.
/// </summary>
public readonly record struct CellReference(int Row, int Column, bool RowAbsolute, bool ColumnAbsolute)
{
    public static CellReference Absolute(int row, int column) => new(row, column, true, true);

    /// <summary>The reference to <paramref name="target"/> from <paramref name="origin"/> with the given absoluteness.</summary>
    public static CellReference To(CellAddress target, CellAddress origin, bool rowAbsolute, bool columnAbsolute) => new(
        rowAbsolute ? target.Row : target.Row - origin.Row,
        columnAbsolute ? target.Column : target.Column - origin.Column,
        rowAbsolute,
        columnAbsolute);

    public int RowAt(int originRow) => RowAbsolute ? Row : originRow + Row;

    public int ColumnAt(int originColumn) => ColumnAbsolute ? Column : originColumn + Column;

    public CellAddress Resolve(CellAddress origin) => new(RowAt(origin.Row), ColumnAt(origin.Column));
}

/// <summary>What an area reference names.</summary>
public enum AreaKind : byte
{
    /// <summary>One cell (A1): only <see cref="AreaReference.First"/> counts.</summary>
    Cell,

    /// <summary>A rectangle (A1:B3).</summary>
    Range,

    /// <summary>Whole rows (1:3): only the row parts count.</summary>
    Rows,

    /// <summary>Whole columns (A:C): only the column parts count.</summary>
    Columns,
}

/// <summary>A cell, range, whole rows or whole columns, relative parts as offsets (see <see cref="CellReference"/>).</summary>
public readonly record struct AreaReference(CellReference First, CellReference Last, AreaKind Kind)
{
    public static AreaReference Cell(CellReference cell) => new(cell, cell, AreaKind.Cell);

    /// <summary>The cells for a formula at <paramref name="origin"/>, or null when a part falls outside the sheet.</summary>
    public CellRange? Resolve(CellAddress origin)
    {
        switch (Kind)
        {
            case AreaKind.Rows:
            {
                var first = First.RowAt(origin.Row);
                var last = Last.RowAt(origin.Row);
                return InRows(first) && InRows(last) ? CellRange.Rows(Math.Min(first, last), Math.Max(first, last)) : null;
            }

            case AreaKind.Columns:
            {
                var first = First.ColumnAt(origin.Column);
                var last = Last.ColumnAt(origin.Column);
                return InColumns(first) && InColumns(last) ? CellRange.Columns(Math.Min(first, last), Math.Max(first, last)) : null;
            }

            default:
            {
                var first = First.Resolve(origin);
                var last = Kind == AreaKind.Cell ? first : Last.Resolve(origin);
                return first.IsValid && last.IsValid ? new CellRange(first, last) : null;
            }
        }
    }

    private static bool InRows(int row) => row is >= 0 and < SheetLimits.MaxRows;

    private static bool InColumns(int column) => column is >= 0 and < SheetLimits.MaxColumns;
}

/// <summary>
/// The sheet part of a reference: one sheet or a 3D span (Tabelle1:Tabelle3!A1), by stable sheet ids so renaming a sheet
/// changes no formula. A sheet the formula names but the workbook lacks keeps its name (<see cref="MissingName"/>) and
/// evaluates to #REF!.
/// </summary>
public sealed record SheetReference(int FirstSheetId, int LastSheetId, string? MissingName = null)
{
    public static SheetReference Single(int sheetId) => new(sheetId, sheetId);

    public bool Is3D => FirstSheetId != LastSheetId;

    public bool IsMissing => MissingName is not null;
}

public enum UnaryOperator : byte
{
    Plus,
    Minus,

    /// <summary>The implicit intersection operator @.</summary>
    ImplicitIntersection,
}

public enum BinaryOperator : byte
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Concatenate,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,

    /// <summary>The range operator ":" between two references.</summary>
    Range,

    /// <summary>The intersection operator (a space).</summary>
    Intersect,

    /// <summary>The union operator ("," in parentheses; ";" in German).</summary>
    Union,
}

/// <summary>A node of a parsed formula. Immutable; equal formulas may share nodes.</summary>
public abstract record FormulaNode;

public sealed record NumberNode(double Value) : FormulaNode;

public sealed record TextNode(string Value) : FormulaNode;

public sealed record BooleanNode(bool Value) : FormulaNode;

public sealed record ErrorNode(CellError Error) : FormulaNode;

/// <summary>An empty argument (=IF(A1,,2)).</summary>
public sealed record MissingArgumentNode : FormulaNode
{
    public static MissingArgumentNode Instance { get; } = new();
}

/// <summary>A reference to cells, on the formula's own sheet (<see cref="Sheet"/> null) or on others.</summary>
public sealed record ReferenceNode(SheetReference? Sheet, AreaReference Area) : FormulaNode;

/// <summary>A reference that became invalid (its cells were deleted): #REF! or Tabelle1!#REF!.</summary>
public sealed record ReferenceErrorNode(SheetReference? Sheet) : FormulaNode;

/// <summary>A defined name, a LET/LAMBDA variable or a table name; <see cref="SheetId"/> when qualified (Tabelle1!Name).</summary>
public sealed record NameNode(string Name, int? SheetId = null) : FormulaNode;

/// <summary>
/// A function call. <see cref="Name"/> is canonical (upper case, without _xlfn./_xlws.); <see cref="Function"/> is null for
/// a function ThixCalc does not know (it stays in the formula as written and evaluates to #NAME?). A call of a LAMBDA
/// (=LAMBDA(x,x+1)(2) or a name holding one) has <see cref="Callee"/>.
/// </summary>
public sealed record FunctionNode(string Name, FunctionDefinition? Function, ImmutableArray<FormulaNode> Arguments) : FormulaNode
{
    /// <summary>The expression that yields the called lambda (null for a named function).</summary>
    public FormulaNode? Callee { get; init; }
}

public sealed record UnaryNode(UnaryOperator Operator, FormulaNode Operand) : FormulaNode;

/// <summary>The postfix percent operator (5% = 0.05).</summary>
public sealed record PercentNode(FormulaNode Operand) : FormulaNode;

public sealed record BinaryNode(BinaryOperator Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

/// <summary>Parentheses the author wrote (kept so the text round-trips).</summary>
public sealed record ParenthesesNode(FormulaNode Inner) : FormulaNode;

/// <summary>An array constant {1,2;3,4}: <see cref="Values"/> row-major.</summary>
public sealed record ArrayNode(int Rows, int Columns, ImmutableArray<CellValue> Values) : FormulaNode
{
    public CellValue this[int row, int column] => Values[row * Columns + column];
}

/// <summary>
/// A structured reference (Tabelle1[Umsatz], [@Menge], Tabelle1[[#Kopfzeilen],[Name]]): the table name (or null for the
/// table around the cell) and the specifier text as written.
/// </summary>
public sealed record StructuredReferenceNode(string? TableName, string Specifier) : FormulaNode;

/// <summary>A reference into another workbook ([1]Tabelle1!A1): kept as text, evaluates to #REF! (cached values stay).</summary>
public sealed record ExternalReferenceNode(string Text) : FormulaNode;

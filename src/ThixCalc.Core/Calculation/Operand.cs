using System.Collections.Immutable;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>A rectangle of values (array constants, array results, materialized ranges), row-major.</summary>
internal sealed class ArrayValue
{
    public ArrayValue(int rows, int columns, CellValue[] values)
    {
        Rows = rows;
        Columns = columns;
        Values = values;
    }

    public ArrayValue(int rows, int columns)
        : this(rows, columns, new CellValue[rows * columns])
    {
    }

    public int Rows { get; }

    public int Columns { get; }

    public CellValue[] Values { get; }

    public int Count => Values.Length;

    public CellValue this[int row, int column]
    {
        get => Values[row * Columns + column];
        set => Values[row * Columns + column] = value;
    }

    public static ArrayValue Scalar(CellValue value) => new(1, 1, [value]);

    public static ArrayValue Filled(int rows, int columns, CellValue value)
    {
        var values = new CellValue[rows * columns];
        Array.Fill(values, value);
        return new ArrayValue(rows, columns, values);
    }

    public static ArrayValue FromNode(ArrayNode node) => new(node.Rows, node.Columns, node.Values.ToArray());

    /// <summary>The element at (row, column) with broadcasting: a single row/column/value repeats; outside → #N/A.</summary>
    public CellValue Broadcast(int row, int column)
    {
        var r = Rows == 1 ? 0 : row;
        var c = Columns == 1 ? 0 : column;
        return r < Rows && c < Columns ? this[r, c] : CellValue.FromError(CellError.NotAvailable);
    }

    public ArrayValue Transpose()
    {
        var result = new ArrayValue(Columns, Rows);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                result[column, row] = this[row, column];
            }
        }

        return result;
    }
}

/// <summary>Cells of one worksheet.</summary>
internal readonly record struct SheetArea(Worksheet Sheet, CellRange Range);

/// <summary>A reference value: one or more areas (a union, a 3D reference spans sheets).</summary>
internal sealed class ReferenceValue
{
    public ReferenceValue(IReadOnlyList<SheetArea> areas) => Areas = areas;

    public ReferenceValue(Worksheet sheet, CellRange range)
        : this([new SheetArea(sheet, range)])
    {
    }

    public IReadOnlyList<SheetArea> Areas { get; }

    public bool IsSingleArea => Areas.Count == 1;

    public SheetArea First => Areas[0];

    public bool IsSingleCell => Areas.Count == 1 && Areas[0].Range.IsSingleCell;
}

/// <summary>A LAMBDA value: parameters, body and the LET/LAMBDA variables it closed over.</summary>
internal sealed class LambdaValue(ImmutableArray<string> parameters, FormulaNode body, Scope? scope)
{
    public ImmutableArray<string> Parameters { get; } = parameters;

    public FormulaNode Body { get; } = body;

    public Scope? Scope { get; } = scope;
}

/// <summary>Variables of LET and LAMBDA, chained outward.</summary>
internal sealed class Scope(string name, Operand value, Scope? parent)
{
    public string Name { get; } = name;

    public Operand Value { get; } = value;

    public Scope? Parent { get; } = parent;

    public bool TryFind(string name, out Operand value)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (string.Equals(scope.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = scope.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

internal enum OperandKind : byte
{
    Value,
    Array,
    Reference,
    Lambda,
}

/// <summary>What evaluating an expression gives: a single value, an array, a reference or a lambda.</summary>
internal readonly struct Operand
{
    private readonly object? _payload;

    private Operand(OperandKind kind, CellValue value, object? payload)
    {
        Kind = kind;
        Value = value;
        _payload = payload;
    }

    public OperandKind Kind { get; }

    /// <summary>The value of a <see cref="OperandKind.Value"/> operand.</summary>
    public CellValue Value { get; }

    public ArrayValue Array => (ArrayValue)_payload!;

    public ReferenceValue Reference => (ReferenceValue)_payload!;

    public LambdaValue Lambda => (LambdaValue)_payload!;

    public bool IsValue => Kind == OperandKind.Value;

    public bool IsError => Kind == OperandKind.Value && Value.IsError;

    public static Operand From(CellValue value) => new(OperandKind.Value, value, null);

    public static Operand From(ArrayValue array) => new(OperandKind.Array, default, array);

    public static Operand From(ReferenceValue reference) => new(OperandKind.Reference, default, reference);

    public static Operand From(LambdaValue lambda) => new(OperandKind.Lambda, default, lambda);

    public static Operand Error(CellError error) => From(CellValue.FromError(error));

    public static implicit operator Operand(CellValue value) => From(value);

    public static implicit operator Operand(double number) => From(CellValue.FromNumber(number));

    public static implicit operator Operand(string text) => From(CellValue.FromText(text));

    public static implicit operator Operand(bool value) => From(CellValue.FromBoolean(value));

    public static implicit operator Operand(CellError error) => From(CellValue.FromError(error));

    public static implicit operator Operand(ArrayValue array) => From(array);

    public static implicit operator Operand(ReferenceValue reference) => From(reference);

    public override string ToString() => Kind switch
    {
        OperandKind.Value => Value.ToString(),
        OperandKind.Array => $"{{{Array.Rows}x{Array.Columns}}}",
        OperandKind.Reference => string.Join(",", Reference.Areas.Select(area => $"{area.Sheet.Name}!{area.Range}")),
        _ => "LAMBDA",
    };
}

using System.Text;

namespace ThixCalc.Core.Model;

/// <summary>The size of a worksheet (Excel 2007+): 1,048,576 rows × 16,384 columns (A … XFD).</summary>
public static class SheetLimits
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const int LastRow = MaxRows - 1;
    public const int LastColumn = MaxColumns - 1;
}

/// <summary>A cell position, zero-based (A1 = row 0, column 0). Orders row-major.</summary>
public readonly record struct CellAddress(int Row, int Column) : IComparable<CellAddress>
{
    public bool IsValid => Row is >= 0 and < SheetLimits.MaxRows && Column is >= 0 and < SheetLimits.MaxColumns;

    public CellAddress Offset(int rows, int columns) => new(Row + rows, Column + columns);

    public int CompareTo(CellAddress other) => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);

    /// <summary>"B3" (or null for text that is no A1 address).</summary>
    public static CellAddress? TryParse(string text)
    {
        var index = 0;
        text = text.Trim();
        if (!A1.TryReadCell(text, ref index, out var address, out _, out _) || index != text.Length)
        {
            return null;
        }

        return address;
    }

    public static CellAddress Parse(string text) => TryParse(text) ?? throw new FormatException($"No cell address: {text}");

    public override string ToString() => A1.ColumnName(Column) + (Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A rectangle of cells, inclusive on both ends and always normalized (first ≤ last). Whole columns are rows 0 … LastRow,
/// whole rows columns 0 … LastColumn.
/// </summary>
public readonly record struct CellRange
{
    public CellRange(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        FirstRow = Math.Min(firstRow, lastRow);
        LastRow = Math.Max(firstRow, lastRow);
        FirstColumn = Math.Min(firstColumn, lastColumn);
        LastColumn = Math.Max(firstColumn, lastColumn);
    }

    public CellRange(CellAddress first, CellAddress last)
        : this(first.Row, first.Column, last.Row, last.Column)
    {
    }

    public static CellRange Whole { get; } = new(0, 0, SheetLimits.LastRow, SheetLimits.LastColumn);

    public int FirstRow { get; }

    public int FirstColumn { get; }

    public int LastRow { get; }

    public int LastColumn { get; }

    public CellAddress First => new(FirstRow, FirstColumn);

    public CellAddress Last => new(LastRow, LastColumn);

    public int RowCount => LastRow - FirstRow + 1;

    public int ColumnCount => LastColumn - FirstColumn + 1;

    public long CellCount => (long)RowCount * ColumnCount;

    public bool IsSingleCell => FirstRow == LastRow && FirstColumn == LastColumn;

    /// <summary>All rows of some columns (A:C).</summary>
    public bool IsWholeColumns => FirstRow == 0 && LastRow == SheetLimits.LastRow;

    /// <summary>All columns of some rows (1:3).</summary>
    public bool IsWholeRows => FirstColumn == 0 && LastColumn == SheetLimits.LastColumn;

    public static CellRange Cell(int row, int column) => new(row, column, row, column);

    public static CellRange Cell(CellAddress address) => new(address.Row, address.Column, address.Row, address.Column);

    public static CellRange Rows(int first, int last) => new(first, 0, last, SheetLimits.LastColumn);

    public static CellRange Columns(int first, int last) => new(0, first, SheetLimits.LastRow, last);

    public bool Contains(int row, int column) => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

    public bool Contains(CellAddress address) => Contains(address.Row, address.Column);

    public bool Contains(CellRange other) =>
        other.FirstRow >= FirstRow && other.LastRow <= LastRow && other.FirstColumn >= FirstColumn && other.LastColumn <= LastColumn;

    public bool Intersects(CellRange other) =>
        other.FirstRow <= LastRow && other.LastRow >= FirstRow && other.FirstColumn <= LastColumn && other.LastColumn >= FirstColumn;

    public CellRange? Intersect(CellRange other)
    {
        if (!Intersects(other))
        {
            return null;
        }

        return new CellRange(
            Math.Max(FirstRow, other.FirstRow),
            Math.Max(FirstColumn, other.FirstColumn),
            Math.Min(LastRow, other.LastRow),
            Math.Min(LastColumn, other.LastColumn));
    }

    /// <summary>The smallest range that contains both.</summary>
    public CellRange Union(CellRange other) => new(
        Math.Min(FirstRow, other.FirstRow),
        Math.Min(FirstColumn, other.FirstColumn),
        Math.Max(LastRow, other.LastRow),
        Math.Max(LastColumn, other.LastColumn));

    public CellRange Offset(int rows, int columns) => new(FirstRow + rows, FirstColumn + columns, LastRow + rows, LastColumn + columns);

    /// <summary>"$A$1:$B$3" ("$B$2" for a single cell).</summary>
    public string ToAbsoluteString()
    {
        static string Cell(CellAddress cell) => $"${A1.ColumnName(cell.Column)}${(cell.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return IsSingleCell ? Cell(First) : $"{Cell(First)}:{Cell(Last)}";
    }

    /// <summary>"A1:B3", "B2" for a single cell, "A:C" for whole columns, "1:3" for whole rows.</summary>
    public override string ToString()
    {
        if (IsWholeColumns && !IsWholeRows)
        {
            return $"{A1.ColumnName(FirstColumn)}:{A1.ColumnName(LastColumn)}";
        }

        if (IsWholeRows && !IsWholeColumns)
        {
            return $"{FirstRow + 1}:{LastRow + 1}";
        }

        return IsSingleCell ? First.ToString() : $"{First}:{Last}";
    }

    /// <summary>"A1:B3", "B2", "A:C", "1:3" with or without $ (null for anything else).</summary>
    public static CellRange? TryParse(string text)
    {
        text = text.Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return CellAddress.TryParse(text) is { } single ? Cell(single) : null;
        }

        var left = text[..colon];
        var right = text[(colon + 1)..];
        if (CellAddress.TryParse(left) is { } first && CellAddress.TryParse(right) is { } last)
        {
            return new CellRange(first, last);
        }

        if (A1.TryParseColumn(left, out var firstColumn) && A1.TryParseColumn(right, out var lastColumn))
        {
            return Columns(firstColumn, lastColumn);
        }

        if (int.TryParse(left, out var firstRow) && int.TryParse(right, out var lastRow)
            && firstRow is >= 1 and <= SheetLimits.MaxRows && lastRow is >= 1 and <= SheetLimits.MaxRows)
        {
            return Rows(firstRow - 1, lastRow - 1);
        }

        return null;
    }

    public static CellRange Parse(string text) => TryParse(text) ?? throw new FormatException($"No cell range: {text}");

    /// <summary>A space-separated list of ranges as in sqref attributes ("A1:B2 D4").</summary>
    public static IReadOnlyList<CellRange> ParseList(string text)
    {
        var ranges = new List<CellRange>();
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(part) is { } range)
            {
                ranges.Add(range);
            }
        }

        return ranges;
    }

    public static string FormatList(IEnumerable<CellRange> ranges) => string.Join(' ', ranges.Select(range => range.ToString()));
}

/// <summary>A1 notation: column letters and cell addresses.</summary>
public static class A1
{
    /// <summary>"A" for 0, "Z" for 25, "AA" for 26, "XFD" for 16383.</summary>
    public static string ColumnName(int column)
    {
        Span<char> buffer = stackalloc char[4];
        var position = buffer.Length;
        var value = column + 1;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            buffer[--position] = (char)('A' + remainder);
            value = (value - 1) / 26;
        }

        return new string(buffer[position..]);
    }

    public static bool TryParseColumn(string text, out int column)
    {
        var index = 0;
        return TryReadColumn(text, ref index, out column) && index == text.Length;
    }

    /// <summary>Reads column letters at <paramref name="index"/> (case-insensitive); false when none or out of range.</summary>
    public static bool TryReadColumn(string text, ref int index, out int column)
    {
        column = 0;
        var start = index;
        var value = 0;
        while (index < text.Length && index - start < 4 && char.IsAsciiLetter(text[index]))
        {
            value = value * 26 + (char.ToUpperInvariant(text[index]) - 'A' + 1);
            index++;
        }

        if (index == start || value > SheetLimits.MaxColumns || (index < text.Length && char.IsAsciiLetter(text[index])))
        {
            index = start;
            return false;
        }

        column = value - 1;
        return true;
    }

    /// <summary>Reads a row number at <paramref name="index"/> (1-based in the text, 0-based out).</summary>
    public static bool TryReadRow(string text, ref int index, out int row)
    {
        row = 0;
        var start = index;
        long value = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]) && index - start < 8)
        {
            value = value * 10 + (text[index] - '0');
            index++;
        }

        if (index == start || value < 1 || value > SheetLimits.MaxRows || (index < text.Length && char.IsAsciiDigit(text[index])))
        {
            index = start;
            return false;
        }

        row = (int)value - 1;
        return true;
    }

    /// <summary>Reads "$B$3", "B3", "b$3" at <paramref name="index"/>.</summary>
    public static bool TryReadCell(string text, ref int index, out CellAddress address, out bool columnAbsolute, out bool rowAbsolute)
    {
        address = default;
        var start = index;
        columnAbsolute = index < text.Length && text[index] == '$';
        if (columnAbsolute)
        {
            index++;
        }

        if (!TryReadColumn(text, ref index, out var column))
        {
            index = start;
            rowAbsolute = false;
            return false;
        }

        rowAbsolute = index < text.Length && text[index] == '$';
        if (rowAbsolute)
        {
            index++;
        }

        if (!TryReadRow(text, ref index, out var row))
        {
            index = start;
            return false;
        }

        address = new CellAddress(row, column);
        return true;
    }

    /// <summary>"$B$3" etc.</summary>
    public static string Format(CellAddress address, bool columnAbsolute, bool rowAbsolute)
    {
        var builder = new StringBuilder(8);
        if (columnAbsolute)
        {
            builder.Append('$');
        }

        builder.Append(ColumnName(address.Column));
        if (rowAbsolute)
        {
            builder.Append('$');
        }

        builder.Append(address.Row + 1);
        return builder.ToString();
    }
}

using System.Globalization;

namespace ThixCalc.Core.Model;

/// <summary>The kind of a cell value.</summary>
public enum CellValueKind : byte
{
    Blank,
    Number,
    Text,
    Boolean,
    Error,
}

/// <summary>
/// Excel's error values; the numbers are those of ERROR.TYPE. The canonical names (#DIV/0! …) are in
/// <see cref="CellErrors"/>, the localized ones in the locale.
/// </summary>
public enum CellError : byte
{
    Null = 1,
    DivisionByZero = 2,
    Value = 3,
    Reference = 4,
    Name = 5,
    Number = 6,
    NotAvailable = 7,
    GettingData = 8,
    Spill = 9,
    Calc = 14,
}

/// <summary>The canonical (file format, English) names of the error values.</summary>
public static class CellErrors
{
    private static readonly (CellError Error, string Name)[] Names =
    [
        (CellError.Null, "#NULL!"),
        (CellError.DivisionByZero, "#DIV/0!"),
        (CellError.Value, "#VALUE!"),
        (CellError.Reference, "#REF!"),
        (CellError.Name, "#NAME?"),
        (CellError.Number, "#NUM!"),
        (CellError.NotAvailable, "#N/A"),
        (CellError.GettingData, "#GETTING_DATA"),
        (CellError.Spill, "#SPILL!"),
        (CellError.Calc, "#CALC!"),
    ];

    public static IEnumerable<CellError> All => Names.Select(entry => entry.Error);

    public static string NameOf(CellError error)
    {
        foreach (var entry in Names)
        {
            if (entry.Error == error)
            {
                return entry.Name;
            }
        }

        return "#VALUE!";
    }

    /// <summary>The error a canonical name stands for (case-insensitive), or null.</summary>
    public static CellError? Parse(string name)
    {
        foreach (var entry in Names)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Error;
            }
        }

        return null;
    }
}

/// <summary>
/// A value as a cell holds it: blank, a number (IEEE double, never NaN or infinite – those become #NUM!), a text, a boolean
/// or an error. Immutable, 16 bytes plus the text reference. Equality is exact (texts ordinal); Excel's comparison rules
/// live in <see cref="Calculation.ValueComparer"/>.
/// </summary>
public readonly struct CellValue : IEquatable<CellValue>
{
    public static readonly CellValue Blank = default;
    public static readonly CellValue True = new(CellValueKind.Boolean, 1, null);
    public static readonly CellValue False = new(CellValueKind.Boolean, 0, null);
    public static readonly CellValue EmptyText = new(CellValueKind.Text, 0, string.Empty);
    public static readonly CellValue Zero = new(CellValueKind.Number, 0, null);

    private readonly double _number;
    private readonly string? _text;

    private CellValue(CellValueKind kind, double number, string? text)
    {
        Kind = kind;
        _number = number;
        _text = text;
    }

    public CellValueKind Kind { get; }

    public bool IsBlank => Kind == CellValueKind.Blank;

    public bool IsNumber => Kind == CellValueKind.Number;

    public bool IsText => Kind == CellValueKind.Text;

    public bool IsBoolean => Kind == CellValueKind.Boolean;

    public bool IsError => Kind == CellValueKind.Error;

    /// <summary>The number (booleans 1/0, everything else 0).</summary>
    public double Number => Kind is CellValueKind.Number or CellValueKind.Boolean ? _number : 0;

    /// <summary>The text ("" for anything but a text).</summary>
    public string Text => _text ?? string.Empty;

    public bool Boolean => Kind == CellValueKind.Boolean && _number != 0;

    public CellError Error => Kind == CellValueKind.Error ? (CellError)(int)_number : 0;

    public static CellValue FromNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return FromError(CellError.Number);
        }

        return new CellValue(CellValueKind.Number, number == 0 ? 0 : number, null);
    }

    public static CellValue FromText(string text) => text.Length == 0 ? EmptyText : new CellValue(CellValueKind.Text, 0, text);

    public static CellValue FromBoolean(bool value) => value ? True : False;

    public static CellValue FromError(CellError error) => new(CellValueKind.Error, (int)error, null);

    public static implicit operator CellValue(double number) => FromNumber(number);

    public static implicit operator CellValue(string text) => FromText(text);

    public static implicit operator CellValue(bool value) => FromBoolean(value);

    public static implicit operator CellValue(CellError error) => FromError(error);

    public static bool operator ==(CellValue left, CellValue right) => left.Equals(right);

    public static bool operator !=(CellValue left, CellValue right) => !left.Equals(right);

    public bool Equals(CellValue other) =>
        Kind == other.Kind && _number.Equals(other._number) && string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _number, _text);

    /// <summary>A debugging / test form: 1.5, "Text", TRUE, #N/A, (blank).</summary>
    public override string ToString() => Kind switch
    {
        CellValueKind.Blank => "(blank)",
        CellValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        CellValueKind.Text => $"\"{_text}\"",
        CellValueKind.Boolean => Boolean ? "TRUE" : "FALSE",
        _ => CellErrors.NameOf(Error),
    };
}

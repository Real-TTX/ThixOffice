using System.Globalization;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>An Excel error raised inside a function (caught where the function is called and returned as its value).</summary>
internal sealed class CalcErrorException(CellError error) : Exception(CellErrors.NameOf(error))
{
    public CellError Error { get; } = error;
}

/// <summary>
/// Excel's conversions between value kinds: to a number (blank 0, TRUE 1, numeric text in the locale incl. dates, percent,
/// currency), to text (15 significant digits, TRUE/FALSE in the locale), to a boolean (numbers ≠ 0, "TRUE"/"FALSE").
/// </summary>
internal static class Coercion
{
    public static bool TryNumber(CellValue value, CalcLocale locale, bool date1904, out double number, out CellError error)
    {
        error = 0;
        switch (value.Kind)
        {
            case CellValueKind.Number:
            case CellValueKind.Boolean:
                number = value.Number;
                return true;
            case CellValueKind.Blank:
                number = 0;
                return true;
            case CellValueKind.Text:
                if (ValueParser.TryParseNumber(value.Text, locale, date1904, out number))
                {
                    return true;
                }

                error = CellError.Value;
                return false;
            default:
                number = 0;
                error = value.Error;
                return false;
        }
    }

    public static double ToNumber(CellValue value, CalcLocale locale, bool date1904)
    {
        if (TryNumber(value, locale, date1904, out var number, out var error))
        {
            return number;
        }

        throw new CalcErrorException(error);
    }

    public static string ToText(CellValue value, CalcLocale locale) => value.Kind switch
    {
        CellValueKind.Number => NumberText.ToText(value.Number, locale),
        CellValueKind.Text => value.Text,
        CellValueKind.Boolean => locale.BooleanName(value.Boolean),
        CellValueKind.Blank => string.Empty,
        _ => throw new CalcErrorException(value.Error),
    };

    public static bool ToBoolean(CellValue value, CalcLocale locale)
    {
        switch (value.Kind)
        {
            case CellValueKind.Number:
            case CellValueKind.Boolean:
                return value.Number != 0;
            case CellValueKind.Blank:
                return false;
            case CellValueKind.Text:
                var text = value.Text.Trim();
                if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase) || string.Equals(text, locale.TrueName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase) || string.Equals(text, locale.FalseName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                throw new CalcErrorException(CellError.Value);
            default:
                throw new CalcErrorException(value.Error);
        }
    }

    /// <summary>Truncates toward zero as Excel does for integer arguments (after rounding away float noise).</summary>
    public static double Truncate(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 1e-9 ? rounded : Math.Truncate(value);
    }

    public static int ToInt(double value)
    {
        var truncated = Truncate(value);
        if (truncated > int.MaxValue || truncated < int.MinValue)
        {
            throw new CalcErrorException(CellError.Number);
        }

        return (int)truncated;
    }
}

/// <summary>
/// Excel's comparison of values: numbers &lt; texts &lt; booleans; blank equals 0, "" and FALSE; numbers equal when they agree in
/// 15 significant digits; texts case-insensitively in the workbook's culture. Errors are not ordered (callers propagate them).
/// </summary>
internal static class ValueComparer
{
    private static readonly CompareInfo Collation = CultureInfo.GetCultureInfo("de-DE").CompareInfo;

    /// <summary>Negative, zero or positive like <see cref="IComparer{T}.Compare"/>.</summary>
    public static int Compare(CellValue left, CellValue right, bool caseSensitive = false)
    {
        if (left.IsBlank)
        {
            left = BlankLike(right);
        }

        if (right.IsBlank)
        {
            right = BlankLike(left);
        }

        var leftRank = Rank(left);
        var rightRank = Rank(right);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return left.Kind switch
        {
            CellValueKind.Number => CompareNumbers(left.Number, right.Number),
            CellValueKind.Text => CompareText(left.Text, right.Text, caseSensitive),
            CellValueKind.Boolean => left.Number.CompareTo(right.Number),
            CellValueKind.Error => ((int)left.Error).CompareTo((int)right.Error),
            _ => 0,
        };
    }

    public static bool AreEqual(CellValue left, CellValue right) => Compare(left, right) == 0;

    public static int CompareNumbers(double left, double right)
    {
        if (left == right)
        {
            return 0;
        }

        var scale = Math.Max(Math.Abs(left), Math.Abs(right));
        if (Math.Abs(left - right) <= scale * 1e-15)
        {
            return 0;
        }

        return left < right ? -1 : 1;
    }

    public static int CompareText(string left, string right, bool caseSensitive = false) =>
        Collation.Compare(left, right, caseSensitive ? CompareOptions.None : CompareOptions.IgnoreCase);

    public static bool TextEquals(string left, string right) => CompareText(left, right) == 0;

    private static CellValue BlankLike(CellValue other) => other.Kind switch
    {
        CellValueKind.Text => CellValue.EmptyText,
        CellValueKind.Boolean => CellValue.False,
        _ => CellValue.Zero,
    };

    private static int Rank(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => 0,
        CellValueKind.Text => 1,
        CellValueKind.Boolean => 2,
        CellValueKind.Error => 3,
        _ => 0,
    };
}

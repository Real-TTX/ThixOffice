using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>Excel's wildcards: * any text, ? one character, ~ escapes the next one; case-insensitive.</summary>
internal static class Wildcard
{
    public static bool HasWildcards(string pattern) => pattern.IndexOfAny(['*', '?', '~']) >= 0;

    public static bool IsMatch(string pattern, string text)
    {
        // Tokens: (character, isLiteral); '*' and '?' unescaped are wildcards.
        var tokens = new List<(char Character, bool Wild)>(pattern.Length);
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '~' && index + 1 < pattern.Length && pattern[index + 1] is '*' or '?' or '~')
            {
                tokens.Add((pattern[++index], false));
                continue;
            }

            tokens.Add((character, character is '*' or '?'));
        }

        int t = 0, p = 0, starToken = -1, starText = 0;
        while (t < text.Length)
        {
            if (p < tokens.Count && tokens[p].Wild && tokens[p].Character == '*')
            {
                starToken = p++;
                starText = t;
                continue;
            }

            if (p < tokens.Count && ((tokens[p].Wild && tokens[p].Character == '?') || SameCharacter(tokens[p].Character, text[t])))
            {
                p++;
                t++;
                continue;
            }

            if (starToken >= 0)
            {
                p = starToken + 1;
                t = ++starText;
                continue;
            }

            return false;
        }

        while (p < tokens.Count && tokens[p].Wild && tokens[p].Character == '*')
        {
            p++;
        }

        return p == tokens.Count;
    }

    private static bool SameCharacter(char left, char right) =>
        left == right || char.ToUpperInvariant(left) == char.ToUpperInvariant(right) || char.ToLowerInvariant(left) == char.ToLowerInvariant(right);
}

/// <summary>
/// A criterion of COUNTIF, SUMIF, AVERAGEIFS, the D-functions …: "&gt;5", "&lt;&gt;Berlin", "Mü*", "=" (blank), "" (blank),
/// a number, a date text in the locale ("&gt;=01.01.2024"), TRUE, an error. Numbers match numbers (and numeric texts for
/// equality), texts match texts with wildcards, case-insensitively.
/// </summary>
internal sealed class Criterion
{
    private enum Operator
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    private readonly Operator _operator;
    private readonly CellValue _operand;
    private readonly bool _matchBlank;
    private readonly bool _wildcard;
    private readonly bool _matchesNothing;

    private Criterion(Operator op, CellValue operand, bool matchBlank, bool wildcard, bool matchesNothing = false)
    {
        _operator = op;
        _operand = operand;
        _matchBlank = matchBlank;
        _wildcard = wildcard;
        _matchesNothing = matchesNothing;
    }

    public static Criterion Parse(CellValue criteria, CalcLocale locale, bool date1904)
    {
        switch (criteria.Kind)
        {
            case CellValueKind.Blank:
                return new Criterion(Operator.Equal, CellValue.Blank, false, false, matchesNothing: true);
            case CellValueKind.Number:
            case CellValueKind.Boolean:
            case CellValueKind.Error:
                return new Criterion(Operator.Equal, criteria, false, false);
        }

        var text = criteria.Text;
        var op = Operator.Equal;
        var length = 0;
        if (text.StartsWith("<=", StringComparison.Ordinal))
        {
            (op, length) = (Operator.LessOrEqual, 2);
        }
        else if (text.StartsWith(">=", StringComparison.Ordinal))
        {
            (op, length) = (Operator.GreaterOrEqual, 2);
        }
        else if (text.StartsWith("<>", StringComparison.Ordinal))
        {
            (op, length) = (Operator.NotEqual, 2);
        }
        else if (text.StartsWith('<'))
        {
            (op, length) = (Operator.Less, 1);
        }
        else if (text.StartsWith('>'))
        {
            (op, length) = (Operator.Greater, 1);
        }
        else if (text.StartsWith('='))
        {
            (op, length) = (Operator.Equal, 1);
        }

        var rest = text[length..];
        if (rest.Length == 0)
        {
            // "" and "=" match blank cells; "<>" matches non-blank cells.
            return new Criterion(op, CellValue.Blank, true, false);
        }

        if (ValueParser.TryParseNumber(rest, locale, date1904, out var number))
        {
            return new Criterion(op, CellValue.FromNumber(number), false, false);
        }

        var upper = rest.Trim();
        if (string.Equals(upper, locale.TrueName, StringComparison.OrdinalIgnoreCase) || string.Equals(upper, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new Criterion(op, CellValue.True, false, false);
        }

        if (string.Equals(upper, locale.FalseName, StringComparison.OrdinalIgnoreCase) || string.Equals(upper, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new Criterion(op, CellValue.False, false, false);
        }

        if (locale.ParseError(upper) is { } error)
        {
            return new Criterion(op, CellValue.FromError(error), false, false);
        }

        var wildcard = op is Operator.Equal or Operator.NotEqual && Wildcard.HasWildcards(rest);
        return new Criterion(op, CellValue.FromText(rest), false, wildcard);
    }

    public bool Matches(CellValue value)
    {
        if (_matchesNothing)
        {
            return false;
        }

        if (_matchBlank)
        {
            var blank = value.IsBlank || (value.IsText && value.Text.Length == 0);
            return _operator == Operator.NotEqual ? !blank : _operator == Operator.Equal && blank;
        }

        var outcome = Compare(value);
        return _operator switch
        {
            Operator.Equal => outcome == 0,
            Operator.NotEqual => outcome != 0,
            Operator.Less => outcome is < 0 and not int.MinValue,
            Operator.LessOrEqual => outcome is <= 0 and not int.MinValue,
            Operator.Greater => outcome is > 0 and not int.MaxValue,
            _ => outcome is >= 0 and not int.MaxValue,
        };
    }

    /// <summary>The comparison of the cell with the operand; int.MinValue/int.MaxValue when the kinds do not compare.</summary>
    private int Compare(CellValue value)
    {
        switch (_operand.Kind)
        {
            case CellValueKind.Number:
                if (value.IsNumber)
                {
                    return ValueComparer.CompareNumbers(value.Number, _operand.Number);
                }

                if (value.IsText && _operator is Operator.Equal or Operator.NotEqual
                    && double.TryParse(value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return ValueComparer.CompareNumbers(parsed, _operand.Number);
                }

                return int.MinValue;
            case CellValueKind.Boolean:
                return value.IsBoolean ? value.Number.CompareTo(_operand.Number) : int.MinValue;
            case CellValueKind.Error:
                return value.IsError && value.Error == _operand.Error ? 0 : int.MinValue;
            default:
                if (!value.IsText)
                {
                    return int.MinValue;
                }

                if (_wildcard)
                {
                    return Wildcard.IsMatch(_operand.Text, value.Text) ? 0 : 1;
                }

                return ValueComparer.CompareText(value.Text, _operand.Text);
        }
    }
}

using System.Globalization;
using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Formatting;

/// <summary>
/// A value formatted for display: the text, the format's colour ([Red]), and where a * fill repeats its character to the
/// cell's width (<see cref="FillIndex"/> in <see cref="Text"/>, −1 when none). <see cref="IsOverflow"/> marks a value the
/// format cannot show (a negative date): the grid shows ####.
/// </summary>
public readonly record struct FormattedValue(string Text, RgbColor? Color = null, char FillCharacter = ' ', int FillIndex = -1, bool IsOverflow = false);

/// <summary>
/// Formats cell values with Excel number format codes in a locale (decimal and group symbols, month and day names, the
/// locale-dependent built-in formats), the General format with its 11-character rule, dates in the 1900 or 1904 system.
/// </summary>
public static class NumberFormatter
{
    /// <summary>Formats a value with a canonical format code.</summary>
    public static FormattedValue Format(CellValue value, string code, CalcLocale locale, bool date1904 = false) =>
        Format(value, NumberFormat.Parse(BuiltInFormats.Localize(code, locale)), locale, date1904);

    public static FormattedValue Format(CellValue value, NumberFormat format, CalcLocale locale, bool date1904 = false)
    {
        switch (value.Kind)
        {
            case CellValueKind.Blank:
                return new FormattedValue(string.Empty);
            case CellValueKind.Boolean:
                return new FormattedValue(locale.BooleanName(value.Boolean));
            case CellValueKind.Error:
                return new FormattedValue(locale.ErrorName(value.Error));
            case CellValueKind.Text:
                return FormatText(value.Text, format);
            default:
                return FormatNumber(value.Number, format, locale, date1904);
        }
    }

    /// <summary>The General format: up to 11 characters, scientific for very large or small numbers.</summary>
    public static string FormatGeneral(double value, CalcLocale locale, int maxLength = 11)
    {
        if (value == 0)
        {
            return "0";
        }

        var magnitude = Math.Abs(value);
        string text;
        if (magnitude >= 1e11 || magnitude < 1e-9)
        {
            text = Scientific(value, maxLength);
        }
        else
        {
            text = string.Empty;
            for (var decimals = 10; decimals >= 0; decimals--)
            {
                var rounded = Math.Round(NumberText.RoundToSignificant(value, 15), decimals, MidpointRounding.AwayFromZero);
                text = rounded.ToString("0." + new string('#', Math.Max(decimals, 0)), CultureInfo.InvariantCulture).TrimEnd('.');
                if (text.Length <= maxLength)
                {
                    break;
                }
            }

            if (text.Length > maxLength || text == "0" || text == "-0")
            {
                text = Scientific(value, maxLength);
            }
        }

        return locale.DecimalSeparator == '.' ? text : text.Replace('.', locale.DecimalSeparator);
    }

    private static string Scientific(double value, int maxLength)
    {
        for (var decimals = 5; decimals >= 0; decimals--)
        {
            var text = value.ToString((decimals > 0 ? "0." + new string('#', decimals) : "0") + "E+00", CultureInfo.InvariantCulture);
            if (text.Length <= maxLength)
            {
                return text;
            }
        }

        return value.ToString("0E+00", CultureInfo.InvariantCulture);
    }

    private static FormattedValue FormatText(string text, NumberFormat format)
    {
        var sections = format.Sections;
        var section = sections.Count >= 4 ? sections[3] : sections.FirstOrDefault(candidate => candidate.Kind == FormatSectionKind.Text);
        if (section is null)
        {
            return new FormattedValue(text);
        }

        var builder = new StringBuilder();
        var fill = -1;
        var fillCharacter = ' ';
        foreach (var token in section.Tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Text:
                    builder.Append(text);
                    break;
                case TokenKind.Literal:
                    builder.Append(token.Text);
                    break;
                case TokenKind.Space:
                    builder.Append(' ');
                    break;
                case TokenKind.Fill:
                    fill = builder.Length;
                    fillCharacter = token.Text[0];
                    break;
            }
        }

        return new FormattedValue(builder.ToString(), section.Color, fillCharacter, fill);
    }

    private static FormatSection? ChooseSection(NumberFormat format, double value, out bool useAbsolute)
    {
        useAbsolute = false;
        var numeric = format.Sections.Take(Math.Min(3, format.Sections.Count)).ToList();
        if (format.Sections.Count == 4 || (numeric.Count > 1 && numeric[^1].Kind == FormatSectionKind.Text))
        {
            numeric = format.Sections.Take(Math.Min(3, format.Sections.Count - (numeric[^1].Kind == FormatSectionKind.Text ? 1 : 0))).ToList();
        }

        if (numeric.Any(section => section.Condition is not null))
        {
            for (var index = 0; index < numeric.Count; index++)
            {
                var condition = numeric[index].Condition;
                if (condition is { } explicitCondition)
                {
                    if (explicitCondition.Matches(value))
                    {
                        useAbsolute = index > 0 && value < 0 && explicitCondition.Value <= 0;
                        return numeric[index];
                    }

                    continue;
                }

                // A section without a condition after conditional ones takes the rest.
                useAbsolute = index > 0 && value < 0;
                return numeric[index];
            }

            return null;
        }

        switch (numeric.Count)
        {
            case 1:
                return numeric[0];
            case 2:
                useAbsolute = value < 0;
                return value < 0 ? numeric[1] : numeric[0];
            default:
                if (value > 0)
                {
                    return numeric[0];
                }

                if (value < 0)
                {
                    useAbsolute = true;
                    return numeric[1];
                }

                return numeric[2];
        }
    }

    private static FormattedValue FormatNumber(double value, NumberFormat format, CalcLocale locale, bool date1904)
    {
        var section = ChooseSection(format, value, out var useAbsolute);
        if (section is null)
        {
            return new FormattedValue(new string('#', 10), IsOverflow: true);
        }

        var effectiveLocale = section.LocaleId switch
        {
            0x407 or 0x807 or 0xC07 => CalcLocale.German,
            0x409 or 0x809 => CalcLocale.English,
            _ => locale,
        };

        // The separators follow the user's locale; names follow the format's [$-407] locale when it has one.
        switch (section.Kind)
        {
            case FormatSectionKind.Text:
                return FormatText(NumberText.ToText(value, locale), format);
            case FormatSectionKind.General:
                return FormatGeneralSection(section, useAbsolute ? Math.Abs(value) : value, locale);
            case FormatSectionKind.DateTime:
                return FormatDateTime(section, value, locale, effectiveLocale, date1904);
            default:
                return FormatDigits(section, useAbsolute ? Math.Abs(value) : value, locale, negativeSign: !useAbsolute && value < 0);
        }
    }

    private static FormattedValue FormatGeneralSection(FormatSection section, double value, CalcLocale locale)
    {
        var builder = new StringBuilder();
        var fill = -1;
        var fillCharacter = ' ';
        foreach (var token in section.Tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.General:
                    builder.Append(FormatGeneral(value, locale));
                    break;
                case TokenKind.Literal:
                    builder.Append(token.Text);
                    break;
                case TokenKind.Space:
                    builder.Append(' ');
                    break;
                case TokenKind.Fill:
                    fill = builder.Length;
                    fillCharacter = token.Text[0];
                    break;
            }
        }

        return new FormattedValue(builder.ToString(), section.Color, fillCharacter, fill);
    }

    // ---------------------------------------------------------------- numbers

    private static FormattedValue FormatDigits(FormatSection section, double value, CalcLocale locale, bool negativeSign)
    {
        var tokens = section.Tokens;
        var number = Math.Abs(value) * Math.Pow(100, section.PercentCount) / Math.Pow(1000, section.ScaleCommas);
        var decimalIndex = tokens.FindIndex(token => token.Kind == TokenKind.DecimalPoint);
        var exponentIndex = tokens.FindIndex(token => token.Kind == TokenKind.Exponent);
        var slashIndex = tokens.FindIndex(token => token.Kind == TokenKind.FractionSlash);
        var mantissaEnd = exponentIndex >= 0 ? exponentIndex : tokens.Count;

        string integerDigits;
        string fractionDigits = string.Empty;
        var exponent = 0;
        var decimals = decimalIndex >= 0 ? CountDigits(tokens, decimalIndex + 1, mantissaEnd) : 0;
        (long Whole, long Numerator, long Denominator)? fraction = null;

        if (slashIndex >= 0)
        {
            fraction = Fraction(number, section, tokens, slashIndex);
            integerDigits = fraction.Value.Whole == 0 ? string.Empty : fraction.Value.Whole.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            if (exponentIndex >= 0 && number != 0)
            {
                var integerPlaces = Math.Max(1, CountDigits(tokens, 0, decimalIndex >= 0 ? decimalIndex : mantissaEnd));
                exponent = (int)Math.Floor(Math.Log10(number));
                if (integerPlaces > 1)
                {
                    // Engineering notation: the exponent is a multiple of the integer places ("##0.0E+0").
                    exponent = (int)(Math.Floor((double)exponent / integerPlaces) * integerPlaces);
                }

                number /= Math.Pow(10, exponent);
                var rounded = RoundDecimal(number, decimals);
                if (rounded >= Math.Pow(10, integerPlaces))
                {
                    exponent += integerPlaces;
                    number /= Math.Pow(10, integerPlaces);
                }
            }

            var text = RoundDecimal(number, decimals).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var point = text.IndexOf('.');
            integerDigits = point >= 0 ? text[..point] : text;
            fractionDigits = point >= 0 ? text[(point + 1)..] : string.Empty;
            if (integerDigits == "0")
            {
                integerDigits = string.Empty;
            }
        }

        var allZero = integerDigits.TrimStart('0').Length == 0 && fractionDigits.TrimStart('0').Length == 0
            && (fraction is null || fraction.Value.Numerator == 0);
        var builder = new StringBuilder();
        if (negativeSign && !allZero)
        {
            builder.Append('-');
        }

        var fill = -1;
        var fillCharacter = ' ';
        var integerEnd = decimalIndex >= 0 ? decimalIndex : (slashIndex >= 0 ? NumeratorStart(tokens, slashIndex) : mantissaEnd);
        WriteInteger(builder, tokens, 0, integerEnd, integerDigits, section.Grouping, locale, ref fill, ref fillCharacter);

        for (var index = integerEnd; index < tokens.Count; index++)
        {
            var token = tokens[index];
            switch (token.Kind)
            {
                case TokenKind.DecimalPoint:
                    var nextDigits = CountDigits(tokens, index + 1, mantissaEnd);
                    if (nextDigits > 0 || true)
                    {
                        builder.Append(locale.DecimalSeparator);
                    }

                    index = WriteDecimals(builder, tokens, index + 1, mantissaEnd, fractionDigits, ref fill, ref fillCharacter) - 1;
                    break;
                case TokenKind.Exponent:
                    builder.Append('E');
                    builder.Append(exponent < 0 ? "-" : token.Text == "+" ? "+" : string.Empty);
                    var exponentPlaces = CountDigits(tokens, index + 1, tokens.Count);
                    var exponentText = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, exponentPlaces), '0');
                    builder.Append(exponentText);
                    while (index + 1 < tokens.Count && tokens[index + 1].Kind == TokenKind.Digit)
                    {
                        index++;
                    }

                    break;
                case TokenKind.FractionSlash:
                    break;
                case TokenKind.Digit when fraction is not null:
                    index = WriteFraction(builder, tokens, index, fraction.Value, section) - 1;
                    break;
                default:
                    AppendOther(builder, token, ref fill, ref fillCharacter);
                    break;
            }
        }

        return new FormattedValue(builder.ToString(), section.Color, fillCharacter, fill);
    }

    private static int NumeratorStart(List<FormatToken> tokens, int slashIndex)
    {
        var index = slashIndex - 1;
        while (index >= 0 && tokens[index].Kind == TokenKind.Digit)
        {
            index--;
        }

        // Skip the literal (space) between the whole number and the numerator.
        return index + 1;
    }

    private static double RoundDecimal(double value, int decimals)
    {
        var precise = NumberText.RoundToSignificant(value, 15);
        if (precise < 7.9e27 && decimals <= 28)
        {
            return (double)Math.Round((decimal)precise, decimals, MidpointRounding.AwayFromZero);
        }

        return Math.Round(precise, Math.Min(decimals, 15), MidpointRounding.AwayFromZero);
    }

    private static int CountDigits(List<FormatToken> tokens, int start, int end)
    {
        var count = 0;
        for (var index = start; index < end && index < tokens.Count; index++)
        {
            if (tokens[index].Kind == TokenKind.Digit)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Writes the integer part: digits fill the placeholders from the right, the leftmost takes what is left.</summary>
    private static void WriteInteger(StringBuilder builder, List<FormatToken> tokens, int start, int end, string digits, bool grouping,
        CalcLocale locale, ref int fill, ref char fillCharacter)
    {
        var placeholders = new List<int>();
        for (var index = start; index < end; index++)
        {
            if (tokens[index].Kind == TokenKind.Digit)
            {
                placeholders.Add(index);
            }
        }

        // Assign digits right to left.
        var assigned = new Dictionary<int, string>();
        var remaining = digits;
        for (var position = placeholders.Count - 1; position >= 0; position--)
        {
            var token = tokens[placeholders[position]];
            string piece;
            if (position == 0)
            {
                piece = remaining;
                if (piece.Length == 0)
                {
                    piece = token.Placeholder switch { '0' => "0", '?' => " ", _ => string.Empty };
                }
            }
            else if (remaining.Length > 0)
            {
                piece = remaining[^1].ToString();
                remaining = remaining[..^1];
            }
            else
            {
                piece = token.Placeholder switch { '0' => "0", '?' => " ", _ => string.Empty };
            }

            assigned[placeholders[position]] = piece;
        }

        // The whole integer text (for grouping) is the concatenation of the placeholder pieces.
        var groupSize = 0;
        var totalDigits = assigned.Values.Sum(piece => piece.Count(char.IsAsciiDigit));
        var written = 0;
        for (var index = start; index < end; index++)
        {
            var token = tokens[index];
            if (token.Kind != TokenKind.Digit)
            {
                AppendOther(builder, token, ref fill, ref fillCharacter);
                continue;
            }

            foreach (var character in assigned[index])
            {
                builder.Append(character);
                if (!char.IsAsciiDigit(character))
                {
                    continue;
                }

                written++;
                var left = totalDigits - written;
                if (grouping && left > 0 && left % 3 == 0)
                {
                    builder.Append(locale.GroupSeparator);
                }
            }
        }

        _ = groupSize;
    }

    private static int WriteDecimals(StringBuilder builder, List<FormatToken> tokens, int start, int end, string digits, ref int fill, ref char fillCharacter)
    {
        var digitIndex = 0;
        var index = start;
        var placeholderCount = CountDigits(tokens, start, end);
        // Trailing zeros of '#' placeholders disappear; '?' turns them into spaces.
        var significant = digits.TrimEnd('0').Length;
        for (; index < end; index++)
        {
            var token = tokens[index];
            if (token.Kind != TokenKind.Digit)
            {
                if (token.Kind is TokenKind.Exponent or TokenKind.FractionSlash)
                {
                    break;
                }

                AppendOther(builder, token, ref fill, ref fillCharacter);
                continue;
            }

            var digit = digitIndex < digits.Length ? digits[digitIndex] : '0';
            if (digitIndex >= significant)
            {
                switch (token.Placeholder)
                {
                    case '0':
                        builder.Append(digit);
                        break;
                    case '?':
                        builder.Append(' ');
                        break;
                }
            }
            else
            {
                builder.Append(digit);
            }

            digitIndex++;
        }

        _ = placeholderCount;
        return index;
    }

    private static (long Whole, long Numerator, long Denominator) Fraction(double number, FormatSection section, List<FormatToken> tokens, int slashIndex)
    {
        var hasWhole = NumeratorStart(tokens, slashIndex) > 0 && tokens.Take(NumeratorStart(tokens, slashIndex)).Any(token => token.Kind == TokenKind.Digit);
        var whole = hasWhole ? (long)Math.Floor(number) : 0;
        var rest = number - whole;
        long numerator;
        long denominator;
        if (section.FixedDenominator > 0)
        {
            denominator = section.FixedDenominator;
            numerator = (long)Math.Round(rest * denominator, MidpointRounding.AwayFromZero);
        }
        else
        {
            var places = 0;
            for (var index = slashIndex + 1; index < tokens.Count && tokens[index].Kind == TokenKind.Digit; index++)
            {
                places++;
            }

            var maxDenominator = (long)Math.Pow(10, Math.Max(1, places)) - 1;
            (numerator, denominator) = BestFraction(rest, maxDenominator);
        }

        if (numerator == denominator && hasWhole)
        {
            whole++;
            numerator = 0;
        }

        return (whole, numerator, denominator);
    }

    /// <summary>The closest fraction with a denominator ≤ <paramref name="maxDenominator"/> (Stern–Brocot / mediants).</summary>
    internal static (long Numerator, long Denominator) BestFraction(double value, long maxDenominator)
    {
        var whole = (long)Math.Floor(value);
        var rest = value - whole;
        long bestNumerator = 0;
        long bestDenominator = 1;
        var bestError = rest;
        for (long denominator = 1; denominator <= maxDenominator; denominator++)
        {
            var numerator = (long)Math.Round(rest * denominator);
            var error = Math.Abs(rest - (double)numerator / denominator);
            if (error < bestError - 1e-12)
            {
                bestError = error;
                bestNumerator = numerator;
                bestDenominator = denominator;
            }
        }

        return (whole * bestDenominator + bestNumerator, bestDenominator);
    }

    private static int WriteFraction(StringBuilder builder, List<FormatToken> tokens, int index, (long Whole, long Numerator, long Denominator) fraction, FormatSection section)
    {
        // Numerator placeholders, the slash, denominator placeholders (or the fixed denominator literal).
        var numeratorPlaces = 0;
        while (index < tokens.Count && tokens[index].Kind == TokenKind.Digit)
        {
            numeratorPlaces++;
            index++;
        }

        var slash = index < tokens.Count && tokens[index].Kind == TokenKind.FractionSlash;
        if (!slash)
        {
            builder.Append(fraction.Numerator);
            return index;
        }

        index++;
        var denominatorPlaces = 0;
        if (section.FixedDenominator > 0)
        {
            denominatorPlaces = tokens[index].Length;
            index++;
        }
        else
        {
            while (index < tokens.Count && tokens[index].Kind == TokenKind.Digit)
            {
                denominatorPlaces++;
                index++;
            }
        }

        if (fraction.Numerator == 0 && fraction.Whole != 0)
        {
            builder.Append(new string(' ', numeratorPlaces + 1 + denominatorPlaces));
            return index;
        }

        builder.Append(fraction.Numerator.ToString(CultureInfo.InvariantCulture).PadLeft(numeratorPlaces));
        builder.Append('/');
        builder.Append(fraction.Denominator.ToString(CultureInfo.InvariantCulture).PadRight(denominatorPlaces));
        return index;
    }

    private static void AppendOther(StringBuilder builder, FormatToken token, ref int fill, ref char fillCharacter)
    {
        switch (token.Kind)
        {
            case TokenKind.Literal:
                builder.Append(token.Text);
                break;
            case TokenKind.Percent:
                builder.Append('%');
                break;
            case TokenKind.Space:
                builder.Append(' ');
                break;
            case TokenKind.Fill:
                fill = builder.Length;
                fillCharacter = token.Text[0];
                break;
        }
    }

    // ---------------------------------------------------------------- dates and times

    private static FormattedValue FormatDateTime(FormatSection section, double value, CalcLocale locale, CalcLocale names, bool date1904)
    {
        if (value < 0 || value >= DateSerial.MaxSerial(date1904) + 1)
        {
            return new FormattedValue(new string('#', 10), IsOverflow: true);
        }

        var fractionalPlaces = section.Tokens.Where(token => token.Kind == TokenKind.FractionalSecond).Select(token => token.Length).DefaultIfEmpty(0).Max();
        // Round to what is shown: whole seconds, or the fractional digits.
        var unit = Math.Pow(10, fractionalPlaces);
        var totalMilliseconds = Math.Round(value * 86_400_000);
        var roundedSeconds = Math.Round(value * 86400 * unit, MidpointRounding.AwayFromZero) / unit;
        var serial = roundedSeconds / 86400;
        var (year, month, day) = DateSerial.ToDate(serial, date1904);
        var dayOfWeek = DateSerial.DayOfWeek(serial, date1904);
        var secondsOfDay = roundedSeconds - Math.Floor(serial) * 86400;
        var hour = (int)(secondsOfDay / 3600);
        var minute = (int)(secondsOfDay % 3600 / 60);
        var second = (int)Math.Floor(secondsOfDay % 60 + 1e-9);
        var fractionOfSecond = secondsOfDay - Math.Floor(secondsOfDay + 1e-9);
        _ = totalMilliseconds;

        var builder = new StringBuilder();
        var fill = -1;
        var fillCharacter = ' ';
        
        foreach (var token in section.Tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Year:
                    builder.Append(token.Length <= 2 ? (year % 100).ToString("00", CultureInfo.InvariantCulture) : year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
                case TokenKind.Month:
                    builder.Append(token.Length switch
                    {
                        1 => month.ToString(CultureInfo.InvariantCulture),
                        2 => month.ToString("00", CultureInfo.InvariantCulture),
                        3 => names.MonthAbbreviations[month - 1],
                        5 => names.MonthNames[month - 1][..1],
                        _ => names.MonthNames[month - 1],
                    });
                    break;
                case TokenKind.Day:
                    builder.Append(token.Length switch
                    {
                        1 => day.ToString(CultureInfo.InvariantCulture),
                        2 => day.ToString("00", CultureInfo.InvariantCulture),
                        3 => names.DayAbbreviations[dayOfWeek],
                        _ => names.DayNames[dayOfWeek],
                    });
                    break;
                case TokenKind.Hour:
                    var shown = section.HasAmPm ? (hour % 12 == 0 ? 12 : hour % 12) : hour;
                    builder.Append(token.Length >= 2 ? shown.ToString("00", CultureInfo.InvariantCulture) : shown.ToString(CultureInfo.InvariantCulture));
                    break;
                case TokenKind.Minute:
                    builder.Append(token.Length >= 2 ? minute.ToString("00", CultureInfo.InvariantCulture) : minute.ToString(CultureInfo.InvariantCulture));
                    break;
                case TokenKind.Second:
                    builder.Append(token.Length >= 2 ? second.ToString("00", CultureInfo.InvariantCulture) : second.ToString(CultureInfo.InvariantCulture));
                    break;
                case TokenKind.FractionalSecond:
                    builder.Append(locale.DecimalSeparator);
                    builder.Append(((long)Math.Round(fractionOfSecond * Math.Pow(10, token.Length))).ToString(CultureInfo.InvariantCulture).PadLeft(token.Length, '0'));
                    break;
                case TokenKind.ElapsedHours:
                    builder.Append(((long)Math.Floor(roundedSeconds / 3600 + 1e-9)).ToString(CultureInfo.InvariantCulture).PadLeft(token.Length, '0'));
                    break;
                case TokenKind.ElapsedMinutes:
                    var minutes = section.Tokens.Any(other => other.Kind == TokenKind.ElapsedHours)
                        ? minute
                        : (long)Math.Floor(roundedSeconds / 60 + 1e-9);
                    builder.Append(minutes.ToString(CultureInfo.InvariantCulture).PadLeft(token.Length, '0'));
                    break;
                case TokenKind.ElapsedSeconds:
                    builder.Append(((long)Math.Floor(roundedSeconds + 1e-9)).ToString(CultureInfo.InvariantCulture).PadLeft(token.Length, '0'));
                    break;
                case TokenKind.AmPm:
                    var am = hour < 12;
                    if (token.Length == 5)
                    {
                        builder.Append(am ? "AM" : "PM");
                    }
                    else
                    {
                        var letter = am ? token.Text[0] : token.Text[2];
                        builder.Append(letter);
                    }

                    break;
                default:
                    AppendOther(builder, token, ref fill, ref fillCharacter);
                    break;
            }
        }

        return new FormattedValue(builder.ToString(), section.Color, fillCharacter, fill);
    }
}

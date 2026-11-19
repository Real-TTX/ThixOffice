using System.Globalization;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formatting;

/// <summary>What the user typed into a cell, recognized.</summary>
public enum InputKind : byte
{
    Empty,
    Number,
    Text,
    Boolean,
    Error,
    Formula,
}

/// <summary>
/// The result of <see cref="ValueParser.ParseInput"/>: the value (or the formula text), and the number format Excel would give
/// the cell for it (a percent, currency, date, time, scientific or fraction input) – null to keep the cell's format.
/// </summary>
public sealed record ParsedInput(InputKind Kind, CellValue Value, string? NumberFormat = null, string? FormulaText = null);

/// <summary>
/// Recognizes typed input like Excel: numbers with the locale's decimal and group separators (1.234,56 in German), signs and
/// parentheses, percent, currency (€ / $), scientific notation, fractions ("1 1/2"), dates (31.12.2024, 31.12., 31. Dez 2024,
/// 2024-12-31, 12/31/2024 in English), times (14:30, 2:30 PM, 25:00:00), date and time, TRUE/FALSE (WAHR/FALSCH), error
/// names, formulas (=, +, −) and the leading apostrophe that forces text.
/// </summary>
public static class ValueParser
{
    /// <summary>Recognizes cell input.</summary>
    public static ParsedInput ParseInput(string input, CalcLocale locale, bool date1904 = false, Func<DateTime>? today = null)
    {
        if (input.Length == 0)
        {
            return new ParsedInput(InputKind.Empty, CellValue.Blank);
        }

        if (input[0] == '\'')
        {
            return new ParsedInput(InputKind.Text, CellValue.FromText(input[1..]));
        }

        if (input[0] == '=' && input.Length > 1)
        {
            return new ParsedInput(InputKind.Formula, CellValue.Blank, FormulaText: input);
        }

        var trimmed = input.Trim();
        if ((trimmed.StartsWith('+') || trimmed.StartsWith('-')) && trimmed.Length > 1 && !TryParseNumber(trimmed, locale, date1904, out _, out _, today))
        {
            // "+A1" / "-A1*2" are formulas (Lotus habit); "-5" is a number.
            if (char.IsLetter(trimmed[1]) || trimmed[1] == '(' || trimmed[1] == '$')
            {
                return new ParsedInput(InputKind.Formula, CellValue.Blank, FormulaText: "=" + trimmed);
            }
        }

        if (string.Equals(trimmed, locale.TrueName, StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(InputKind.Boolean, CellValue.True);
        }

        if (string.Equals(trimmed, locale.FalseName, StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedInput(InputKind.Boolean, CellValue.False);
        }

        if (trimmed.StartsWith('#') && locale.ParseError(trimmed) is { } error)
        {
            return new ParsedInput(InputKind.Error, CellValue.FromError(error));
        }

        if (TryParseNumber(trimmed, locale, date1904, out var number, out var format, today))
        {
            return new ParsedInput(InputKind.Number, CellValue.FromNumber(number), format);
        }

        return new ParsedInput(InputKind.Text, CellValue.FromText(input));
    }

    /// <summary>Converts numeric text as Excel's coercion does (="1,5"*2 in German): numbers, percent, currency, dates, times.</summary>
    public static bool TryParseNumber(string text, CalcLocale locale, bool date1904, out double value) =>
        TryParseNumber(text.Trim(), locale, date1904, out value, out _, null);

    public static bool TryParseNumber(string text, CalcLocale locale, bool date1904, out double value, out string? format, Func<DateTime>? today)
    {
        format = null;
        if (TryParsePlainNumber(text, locale, out value, out format))
        {
            return true;
        }

        if (TryParseFraction(text, out value))
        {
            format = "# ?/?";
            return true;
        }

        if (TryParseDateTime(text, locale, date1904, today ?? (() => DateTime.Today), out value, out format))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>[sign] [currency] digits [group] [decimal] [E exponent] [%] [currency], or in parentheses for negatives.</summary>
    private static bool TryParsePlainNumber(string text, CalcLocale locale, out double value, out string? format)
    {
        value = 0;
        format = null;
        if (text.Length == 0)
        {
            return false;
        }

        var negative = false;
        var span = text.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '(' && span[^1] == ')')
        {
            negative = true;
            span = span[1..^1].Trim();
        }

        var currency = false;
        var percent = false;
        if (span.Length > 0 && (span[0] == '+' || span[0] == '-'))
        {
            negative ^= span[0] == '-';
            span = span[1..].Trim();
        }

        if (StartsWithCurrency(span, locale, out var symbolLength))
        {
            currency = true;
            span = span[symbolLength..].Trim();
            if (span.Length > 0 && span[0] == '-')
            {
                negative = !negative;
                span = span[1..].Trim();
            }
        }

        if (span.Length > 0 && span[^1] == '%')
        {
            percent = true;
            span = span[..^1].Trim();
        }

        if (EndsWithCurrency(span, locale, out symbolLength))
        {
            currency = true;
            span = span[..^symbolLength].Trim();
        }

        if (span.Length > 0 && span[^1] == '-' && !currency)
        {
            // Trailing minus (SAP exports: "123-").
            negative = !negative;
            span = span[..^1];
        }

        if (span.Length == 0)
        {
            return false;
        }

        var digits = new System.Text.StringBuilder(span.Length);
        var groupUsed = false;
        var decimals = -1;
        var exponent = false;
        var lastGroup = -1;
        var integerDigits = 0;
        for (var index = 0; index < span.Length; index++)
        {
            var character = span[index];
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                if (decimals >= 0 && !exponent)
                {
                    decimals++;
                }
                else if (!exponent)
                {
                    integerDigits++;
                }

                continue;
            }

            if (character == locale.GroupSeparator && decimals < 0 && !exponent)
            {
                // A group needs exactly three digits after it.
                if (integerDigits == 0 || index + 3 > span.Length - 1 || !char.IsAsciiDigit(span[index + 1])
                    || !char.IsAsciiDigit(span[index + 2]) || !char.IsAsciiDigit(span[index + 3]))
                {
                    return false;
                }

                if ((lastGroup >= 0 && integerDigits - lastGroup != 3) || (lastGroup < 0 && integerDigits > 3))
                {
                    return false;
                }

                lastGroup = integerDigits;
                groupUsed = true;
                continue;
            }

            if (character == locale.DecimalSeparator && decimals < 0 && !exponent)
            {
                if (groupUsed && integerDigits - lastGroup != 3)
                {
                    return false;
                }

                decimals = 0;
                digits.Append('.');
                continue;
            }

            if ((character == 'E' || character == 'e') && !exponent && digits.Length > 0 && index + 1 < span.Length)
            {
                exponent = true;
                digits.Append('E');
                if (span[index + 1] is '+' or '-')
                {
                    digits.Append(span[++index]);
                }

                continue;
            }

            return false;
        }

        if (groupUsed && decimals < 0 && integerDigits - lastGroup != 3)
        {
            return false;
        }

        var number = digits.ToString();
        if (number.Length == 0 || number == "." || number.EndsWith('E') || number.EndsWith('+') || number.EndsWith('-'))
        {
            return false;
        }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value))
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        if (percent)
        {
            value /= 100;
            format = decimals > 0 ? "0." + new string('0', decimals) + "%" : "0%";
        }
        else if (currency)
        {
            format = locale.CurrencyFormat;
        }
        else if (exponent)
        {
            format = "0.00E+00";
        }
        else if (groupUsed)
        {
            format = decimals > 0 ? "#,##0." + new string('0', decimals) : "#,##0";
        }

        return true;
    }

    private static bool StartsWithCurrency(ReadOnlySpan<char> span, CalcLocale locale, out int length)
    {
        foreach (var symbol in CurrencySymbols(locale))
        {
            if (span.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
            {
                length = symbol.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    private static bool EndsWithCurrency(ReadOnlySpan<char> span, CalcLocale locale, out int length)
    {
        foreach (var symbol in CurrencySymbols(locale))
        {
            if (span.EndsWith(symbol, StringComparison.OrdinalIgnoreCase))
            {
                length = symbol.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    private static string[] CurrencySymbols(CalcLocale locale) => [locale.CurrencySymbol, "€", "$"];

    /// <summary>"1 1/2", "0 3/4" (a whole part is required, as in Excel).</summary>
    private static bool TryParseFraction(string text, out double value)
    {
        value = 0;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var negative = parts[0].StartsWith('-');
        var whole = parts[0].TrimStart('-', '+');
        var slash = parts[1].IndexOf('/');
        if (slash <= 0 || !int.TryParse(whole, out var integer) || !int.TryParse(parts[1][..slash], out var numerator)
            || !int.TryParse(parts[1][(slash + 1)..], out var denominator) || denominator == 0 || numerator < 0)
        {
            return false;
        }

        value = integer + (double)numerator / denominator;
        if (negative)
        {
            value = -value;
        }

        return true;
    }

    // ---------------------------------------------------------------- dates and times

    private static bool TryParseDateTime(string text, CalcLocale locale, bool date1904, Func<DateTime> today, out double value, out string? format)
    {
        value = 0;
        format = null;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        // Time only.
        if (TryParseTime(text, out var time, out var timeFormat))
        {
            value = time;
            format = timeFormat;
            return true;
        }

        // Date and time separated by a space: the time is the last part with a colon.
        var space = text.LastIndexOf(' ');
        if (space > 0 && text.IndexOf(':', space) > space)
        {
            var datePart = text[..space].Trim();
            var timePart = text[(space + 1)..];
            if (datePart.EndsWith(" AM", StringComparison.OrdinalIgnoreCase) || datePart.EndsWith(" PM", StringComparison.OrdinalIgnoreCase))
            {
                // never: AM/PM belongs to the time
            }

            if (TryParseTime(timePart, out time, out _) && TryParseDate(datePart, locale, date1904, today, out var day, out _))
            {
                value = day + time;
                format = locale.DateTimeFormat;
                return true;
            }

            // "14:30 PM" style: the AM/PM is the last word.
            var secondSpace = datePart.LastIndexOf(' ');
            if (secondSpace > 0 && (timePart.Equals("AM", StringComparison.OrdinalIgnoreCase) || timePart.Equals("PM", StringComparison.OrdinalIgnoreCase)))
            {
                if (TryParseTime(datePart[(secondSpace + 1)..] + " " + timePart, out time, out _)
                    && TryParseDate(datePart[..secondSpace], locale, date1904, today, out day, out _))
                {
                    value = day + time;
                    format = locale.DateTimeFormat;
                    return true;
                }
            }
        }

        if (TryParseDate(text, locale, date1904, today, out var date, out format))
        {
            value = date;
            return true;
        }

        return false;
    }

    /// <summary>"14:30", "14:30:15", "14:30:15,5", "2:30 PM", "25:00" (elapsed), "1:30:00 AM".</summary>
    public static bool TryParseTime(string text, out double value, out string? format)
    {
        value = 0;
        format = null;
        text = text.Trim();
        var pm = false;
        var am = false;
        if (text.EndsWith("PM", StringComparison.OrdinalIgnoreCase) || text.EndsWith("AM", StringComparison.OrdinalIgnoreCase))
        {
            pm = text.EndsWith("PM", StringComparison.OrdinalIgnoreCase);
            am = !pm;
            text = text[..^2].Trim();
        }

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 59 || parts[1].Length is 0 or > 2)
        {
            return false;
        }

        double seconds = 0;
        if (parts.Length == 3)
        {
            var secondText = parts[2].Replace(',', '.');
            if (!double.TryParse(secondText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds) || seconds >= 60)
            {
                return false;
            }
        }

        if (am || pm)
        {
            if (hours is < 1 or > 12)
            {
                return false;
            }

            hours %= 12;
            if (pm)
            {
                hours += 12;
            }
        }
        else if (hours > 9999)
        {
            return false;
        }

        value = (hours * 3600 + minutes * 60 + seconds) / 86400;
        format = am || pm ? (parts.Length == 3 ? "h:mm:ss AM/PM" : "h:mm AM/PM")
            : hours >= 24 ? (parts.Length == 3 ? "[h]:mm:ss" : "[h]:mm")
            : parts.Length == 3 ? "hh:mm:ss" : "hh:mm";
        return true;
    }

    /// <summary>A date in the locale's forms (see the type); the format Excel would apply.</summary>
    public static bool TryParseDate(string text, CalcLocale locale, bool date1904, Func<DateTime> today, out double value, out string? format)
    {
        value = 0;
        format = null;
        text = text.Trim();
        if (text.Length < 3)
        {
            return false;
        }

        // ISO 2024-12-31
        var iso = text.Split('-');
        if (iso.Length == 3 && iso[0].Length == 4 && AllDigits(iso[0]) && AllDigits(iso[1]) && AllDigits(iso[2]))
        {
            return Build(int.Parse(iso[0], CultureInfo.InvariantCulture), int.Parse(iso[1], CultureInfo.InvariantCulture),
                int.Parse(iso[2], CultureInfo.InvariantCulture), date1904, out value, ref format, "yyyy-mm-dd");
        }

        if (text.Contains(',') && !text.Any(char.IsLetter))
        {
            return false;
        }

        var tokens = Tokenize(text);
        if (tokens.Count is < 2 or > 3)
        {
            return false;
        }

        var numbers = new List<int>();
        int? month = null;
        var monthPosition = -1;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (AllDigits(token))
            {
                if (token.Length > 4)
                {
                    return false;
                }

                numbers.Add(int.Parse(token, CultureInfo.InvariantCulture));
                continue;
            }

            var named = MonthByName(token, locale);
            if (named is null || month is not null)
            {
                return false;
            }

            month = named;
            monthPosition = index;
        }

        var year = today().Year;
        int day;
        if (month is { } namedMonth)
        {
            // "31. Dez 2024", "31-Dez-24", "Dez 2024", "Dec 31, 2024", "31 Dec"
            if (numbers.Count == 2)
            {
                if (monthPosition == 0)
                {
                    day = numbers[0];
                    year = ExpandYear(numbers[1], tokens[2].Length);
                }
                else
                {
                    day = numbers[0];
                    year = ExpandYear(numbers[1], tokens[2].Length);
                }

                return Build(year, namedMonth, day, date1904, out value, ref format, locale.Name == "de-DE" ? "dd. mmm yyyy" : "d-mmm-yy");
            }

            if (numbers.Count == 1)
            {
                if (monthPosition == 0 && tokens[1].Length >= 3)
                {
                    // "Dez 2024" = first of the month
                    return Build(ExpandYear(numbers[0], tokens[1].Length), namedMonth, 1, date1904, out value, ref format, "mmm yy");
                }

                return Build(year, namedMonth, numbers[0], date1904, out value, ref format, locale.Name == "de-DE" ? "dd. mmm" : "d-mmm");
            }

            return false;
        }

        if (numbers.Count == 3)
        {
            var dayFirst = locale.DateSeparator != '/' || text.Contains('.');
            var (first, second) = dayFirst ? (numbers[1], numbers[0]) : (numbers[0], numbers[1]);
            year = ExpandYear(numbers[2], tokens[2].Length);
            if (tokens[0].Length == 4)
            {
                // 2024/12/31
                return Build(numbers[0], numbers[1], numbers[2], date1904, out value, ref format, locale.ShortDateFormat);
            }

            return Build(year, first, second, date1904, out value, ref format, locale.ShortDateFormat);
        }

        if (numbers.Count == 2)
        {
            // "31.12." / "12/31": the current year (only when the separator says it is a date).
            var dayFirst = locale.DateSeparator != '/' || text.Contains('.');
            if (dayFirst && !text.Contains('.'))
            {
                return false;
            }

            if (!dayFirst && !text.Contains('/'))
            {
                return false;
            }

            var (first, second) = dayFirst ? (numbers[1], numbers[0]) : (numbers[0], numbers[1]);
            if (first > 12 && !dayFirst)
            {
                // "12/2024" style month/year
                return Build(ExpandYear(numbers[1], tokens[1].Length), numbers[0], 1, date1904, out value, ref format, "mmm yy");
            }

            return Build(year, first, second, date1904, out value, ref format, locale.Name == "de-DE" ? "dd. mmm" : "d-mmm");
        }

        return false;
    }

    private static bool Build(int year, int month, int day, bool date1904, out double value, ref string? format, string? suggested)
    {
        value = 0;
        if (DateSerial.FromDate(year, month, day, date1904) is not { } serial)
        {
            if (!date1904 && year == 1900 && month == 2 && day == 29)
            {
                value = 60;
                format = suggested;
                return true;
            }

            return false;
        }

        value = serial;
        format = suggested;
        return true;
    }

    private static int ExpandYear(int year, int digits) => digits <= 2 ? (year < 30 ? 2000 + year : 1900 + year) : year;

    private static bool AllDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    /// <summary>Splits on date separators (. / - space ,) dropping empty parts.</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in text)
        {
            if (character is '.' or '/' or '-' or ' ' or ',')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static int? MonthByName(string token, CalcLocale locale)
    {
        foreach (var culture in new[] { locale.Culture, CultureInfo.GetCultureInfo("en-US") })
        {
            var names = culture.DateTimeFormat.MonthNames;
            var abbreviations = culture.DateTimeFormat.AbbreviatedMonthNames;
            for (var month = 0; month < 12; month++)
            {
                if (string.Equals(names[month], token, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(abbreviations[month].TrimEnd('.'), token.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
                    || (token.Length >= 3 && names[month].StartsWith(token, StringComparison.OrdinalIgnoreCase)))
                {
                    return month + 1;
                }
            }
        }

        return null;
    }
}

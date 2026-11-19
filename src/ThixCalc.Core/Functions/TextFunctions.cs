using System.Globalization;
using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Text functions (Text). TEXT, FIXED and DOLLAR format in the workbook's locale (German: TT.MM.JJJJ, 1.234,56).</summary>
internal static class TextFunctions
{
    private const int MaxLength = 32767;

    private static readonly Encoding Windows1252 = Latin1WithWindowsCharacters.Instance;

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Text;
        r.Add("LEN", 1, 1, call => call.Text(0).Length);
        r.Alias("LENB", "LEN");
        r.Add("LEFT", 1, 2, call => Left(call.Text(0), Count(call, 1)));
        r.Alias("LEFTB", "LEFT");
        r.Add("RIGHT", 1, 2, call =>
        {
            var text = call.Text(0);
            var count = Count(call, 1);
            return count >= text.Length ? text : text[^count..];
        });
        r.Alias("RIGHTB", "RIGHT");
        r.Add("MID", 3, 3, call =>
        {
            var text = call.Text(0);
            var start = call.Integer(1);
            var count = call.Integer(2);
            Require(start >= 1 && count >= 0, CellError.Value);
            return start > text.Length ? string.Empty : text.Substring(start - 1, Math.Min(count, text.Length - start + 1));
        });
        r.Alias("MIDB", "MID");
        r.Add("UPPER", 1, 1, call => call.Text(0).ToUpper(call.Context.Locale.Culture));
        r.Add("LOWER", 1, 1, call => call.Text(0).ToLower(call.Context.Locale.Culture));
        r.Add("PROPER", 1, 1, call => Proper(call.Text(0), call.Context.Locale.Culture));
        r.Add("TRIM", 1, 1, call => Trim(call.Text(0)));
        r.Add("CLEAN", 1, 1, call => new string(call.Text(0).Where(character => character >= 32).ToArray()));
        r.Add("EXACT", 2, 2, call => string.Equals(call.Text(0), call.Text(1), StringComparison.Ordinal));
        r.Add("FIND", 2, 3, call => Find(call, caseSensitive: true));
        r.Alias("FINDB", "FIND");
        r.Add("SEARCH", 2, 3, call => Find(call, caseSensitive: false));
        r.Alias("SEARCHB", "SEARCH");
        r.Add("SUBSTITUTE", 3, 4, Substitute);
        r.Add("REPLACE", 4, 4, call =>
        {
            var text = call.Text(0);
            var start = call.Integer(1);
            var count = call.Integer(2);
            Require(start >= 1 && count >= 0, CellError.Value);
            var from = Math.Min(start - 1, text.Length);
            return Limit(text[..from] + call.Text(3) + text[Math.Min(text.Length, from + count)..]);
        });
        r.Alias("REPLACEB", "REPLACE");
        r.Add("REPT", 2, 2, call =>
        {
            var text = call.Text(0);
            var times = call.Integer(1);
            Require(times >= 0, CellError.Value);
            Require((long)text.Length * times <= MaxLength, CellError.Value);
            return new StringBuilder().Insert(0, text, times).ToString();
        });
        r.Add("CHAR", 1, 1, call =>
        {
            var code = call.Integer(0);
            Require(code is >= 1 and <= 255, CellError.Value);
            return Windows1252.GetString([(byte)code]);
        });
        r.Add("CODE", 1, 1, call =>
        {
            var text = call.Text(0);
            Require(text.Length > 0, CellError.Value);
            var bytes = Windows1252.GetBytes(text[..1]);
            return bytes.Length > 0 ? bytes[0] : 63;
        });
        r.Add("UNICHAR", 1, 1, call =>
        {
            var code = call.Integer(0);
            Require(code is >= 1 and <= 0x10FFFF && code is < 0xD800 or > 0xDFFF, CellError.Value);
            return char.ConvertFromUtf32(code);
        });
        r.Add("UNICODE", 1, 1, call =>
        {
            var text = call.Text(0);
            Require(text.Length > 0, CellError.Value);
            return char.ConvertToUtf32(text, 0);
        });
        r.Add("CONCATENATE", 1, 255, call => Limit(string.Concat(Enumerable.Range(0, call.Count).Select(call.Text))));
        r.Add("CONCAT", 1, 254, call =>
        {
            var builder = new StringBuilder();
            for (var index = 0; index < call.Count; index++)
            {
                foreach (var value in call.Values(index))
                {
                    builder.Append(Coercion.ToText(value, call.Context.Locale));
                }
            }

            return Limit(builder.ToString());
        }, [ArgumentKind.Any]);
        r.Add("TEXTJOIN", 3, 252, TextJoin, [ArgumentKind.Any, ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("T", 1, 1, call => call.Value(0) switch
        {
            { IsError: true } error => error,
            { IsText: true } text => text,
            _ => CellValue.EmptyText,
        });
        r.Add("TEXT", 2, 2, call =>
        {
            var value = call.ValueOrThrow(0);
            var context = call.Context;
            var code = FormatCodes.ToCanonical(call.Text(1), context.Locale);
            if (value.IsText && Coercion.TryNumber(value, context.Locale, context.Date1904, out var number, out _))
            {
                value = number;
            }

            return NumberFormatter.Format(value, code, context.Locale, context.Date1904).Text;
        });
        r.Add("FIXED", 1, 3, call =>
        {
            var decimals = call.Integer(1, 2);
            var noGroups = call.Boolean(2, false);
            Require(decimals <= 127, CellError.Value);
            var rounded = MathFunctions.Round(call.Number(0), decimals);
            var code = (noGroups ? "0" : "#,##0") + (decimals > 0 ? "." + new string('0', decimals) : string.Empty);
            return NumberFormatter.Format(rounded, code, call.Context.Locale).Text;
        });
        r.Add("DOLLAR", 1, 2, call =>
        {
            var decimals = call.Integer(1, 2);
            var rounded = MathFunctions.Round(call.Number(0), decimals);
            var locale = call.Context.Locale;
            var places = decimals > 0 ? "." + new string('0', decimals) : string.Empty;
            var code = locale.Name.StartsWith("de", StringComparison.Ordinal)
                ? $"#,##0{places} \"€\";-#,##0{places} \"€\""
                : $"\"$\"#,##0{places}_);(\"$\"#,##0{places})";
            return NumberFormatter.Format(rounded, code, locale).Text.TrimEnd();
        });
        r.Add("VALUE", 1, 1, call =>
        {
            var value = call.ValueOrThrow(0);
            if (value.IsNumber || value.IsBlank)
            {
                return value.Number;
            }

            Require(value.IsText, CellError.Value);
            return ValueParser.TryParseNumber(value.Text, call.Context.Locale, call.Context.Date1904, out var number) ? number : CellError.Value;
        });
        r.Add("NUMBERVALUE", 1, 3, NumberValue);
        r.Add("TEXTBEFORE", 2, 6, call => TextAround(call, before: true));
        r.Add("TEXTAFTER", 2, 6, call => TextAround(call, before: false));
        r.Add("TEXTSPLIT", 2, 6, TextSplit, [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("VALUETOTEXT", 1, 2, call => ValueToText(call.Value(0), call.Integer(1, 0) == 1, call));
        r.Add("ARRAYTOTEXT", 1, 2, call =>
        {
            var array = call.Array(0);
            var strict = call.Integer(1, 0) == 1;
            if (!strict)
            {
                return string.Join(", ", array.Values.Select(value => ValueToText(value, false, call)));
            }

            var rows = Enumerable.Range(0, array.Rows).Select(row => string.Join(call.Context.Locale.ArrayColumnSeparator.ToString(),
                Enumerable.Range(0, array.Columns).Select(column => ValueToText(array[row, column], true, call))));
            return "{" + string.Join(call.Context.Locale.ArrayRowSeparator.ToString(), rows) + "}";
        }, [ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("ASC", 1, 1, call => call.Text(0));
        r.Add("DBCS", 1, 1, call => call.Text(0));
        r.Add("ENCODEURL", 1, 1, call => Uri.EscapeDataString(call.Text(0)));
    }

    private static int Count(FunctionCall call, int index)
    {
        var count = call.Has(index) ? call.Integer(index) : 1;
        Require(count >= 0, CellError.Value);
        return count;
    }

    private static string Left(string text, int count) => count >= text.Length ? text : text[..count];

    private static Operand Limit(string text) => text.Length > MaxLength ? CellError.Value : text;

    private static string Proper(string text, CultureInfo culture)
    {
        var builder = new StringBuilder(text.Length);
        var previousLetter = false;
        foreach (var character in text)
        {
            builder.Append(previousLetter ? char.ToLower(character, culture) : char.ToUpper(character, culture));
            previousLetter = char.IsLetter(character);
        }

        return builder.ToString();
    }

    /// <summary>TRIM: no spaces at the ends, single spaces inside (only the ASCII space, as Excel).</summary>
    private static string Trim(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static Operand Find(FunctionCall call, bool caseSensitive)
    {
        var pattern = call.Text(0);
        var text = call.Text(1);
        var start = call.Integer(2, 1);
        Require(start >= 1 && start <= text.Length + 1, CellError.Value);
        if (pattern.Length == 0)
        {
            return start;
        }

        if (caseSensitive)
        {
            var index = text.IndexOf(pattern, start - 1, StringComparison.Ordinal);
            return index < 0 ? CellError.Value : index + 1;
        }

        if (!Wildcard.HasWildcards(pattern))
        {
            var index = text.IndexOf(pattern, start - 1, StringComparison.OrdinalIgnoreCase);
            return index < 0 ? CellError.Value : index + 1;
        }

        // Wildcards: the first position where the pattern matches a prefix of the rest.
        var anchored = pattern + "*";
        for (var index = start - 1; index < text.Length; index++)
        {
            if (Wildcard.IsMatch(anchored, text[index..]))
            {
                return index + 1;
            }
        }

        return CellError.Value;
    }

    private static Operand Substitute(FunctionCall call)
    {
        var text = call.Text(0);
        var old = call.Text(1);
        var replacement = call.Text(2);
        if (old.Length == 0)
        {
            return text;
        }

        if (!call.Has(3))
        {
            return Limit(text.Replace(old, replacement, StringComparison.Ordinal));
        }

        var instance = call.Integer(3);
        Require(instance >= 1, CellError.Value);
        var position = -1;
        for (var count = 0; count < instance; count++)
        {
            position = text.IndexOf(old, position + 1, StringComparison.Ordinal);
            if (position < 0)
            {
                return text;
            }
        }

        return Limit(text[..position] + replacement + text[(position + old.Length)..]);
    }

    private static Operand TextJoin(FunctionCall call)
    {
        var delimiters = call.Values(0).Select(value => Coercion.ToText(value, call.Context.Locale)).ToList();
        if (delimiters.Count == 0)
        {
            delimiters.Add(string.Empty);
        }

        var ignoreEmpty = call.Boolean(1);
        var parts = new List<string>();
        for (var index = 2; index < call.Count; index++)
        {
            var operand = call[index];
            IEnumerable<CellValue> values = operand.Kind switch
            {
                OperandKind.Reference => call.ToArray(operand).Values,
                _ => call.ValuesOf(operand),
            };
            foreach (var value in values)
            {
                var text = Coercion.ToText(value, call.Context.Locale);
                if (ignoreEmpty && text.Length == 0)
                {
                    continue;
                }

                parts.Add(text);
            }
        }

        var builder = new StringBuilder();
        for (var index = 0; index < parts.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(delimiters[(index - 1) % delimiters.Count]);
            }

            builder.Append(parts[index]);
        }

        return Limit(builder.ToString());
    }

    private static Operand NumberValue(FunctionCall call)
    {
        var text = call.Text(0);
        var locale = call.Context.Locale;
        var decimalSeparator = call.Has(1) ? call.Text(1) : locale.DecimalSeparator.ToString();
        var groupSeparator = call.Has(2) ? call.Text(2) : locale.GroupSeparator.ToString();
        Require(decimalSeparator.Length > 0 && groupSeparator.Length > 0 && decimalSeparator[0] != groupSeparator[0], CellError.Value);
        var cleaned = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (cleaned.Length == 0)
        {
            return 0;
        }

        var percent = 0;
        while (cleaned.EndsWith('%'))
        {
            percent++;
            cleaned = cleaned[..^1];
        }

        var decimalIndex = cleaned.IndexOf(decimalSeparator[0]);
        var integerPart = (decimalIndex < 0 ? cleaned : cleaned[..decimalIndex]).Replace(groupSeparator[0].ToString(), string.Empty, StringComparison.Ordinal);
        var fractionPart = decimalIndex < 0 ? string.Empty : cleaned[(decimalIndex + 1)..];
        Require(!fractionPart.Contains(groupSeparator[0]) && !fractionPart.Contains(decimalSeparator[0]), CellError.Value);
        var invariant = integerPart + (fractionPart.Length > 0 ? "." + fractionPart : string.Empty);
        Require(double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out var value), CellError.Value);
        return Result(value / Math.Pow(100, percent));
    }

    private static Operand TextAround(FunctionCall call, bool before)
    {
        var text = call.Text(0);
        var delimiters = call.Values(1).Select(value => Coercion.ToText(value, call.Context.Locale)).Where(delimiter => delimiter.Length > 0).ToList();
        var instance = call.Integer(2, 1);
        var ignoreCase = call.Integer(3, 0) == 1;
        var matchEnd = call.Integer(4, 0) == 1;
        Require(instance != 0 && Math.Abs(instance) <= Math.Max(text.Length, 1), CellError.Value);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (delimiters.Count == 0)
        {
            return before ? (instance > 0 ? string.Empty : text) : (instance > 0 ? text : string.Empty);
        }

        // Occurrences of any delimiter, left to right.
        var occurrences = new List<(int Index, int Length)>();
        var position = 0;
        while (position <= text.Length)
        {
            var best = (Index: -1, Length: 0);
            foreach (var delimiter in delimiters)
            {
                var found = text.IndexOf(delimiter, position, comparison);
                if (found >= 0 && (best.Index < 0 || found < best.Index))
                {
                    best = (found, delimiter.Length);
                }
            }

            if (best.Index < 0)
            {
                break;
            }

            occurrences.Add(best);
            position = best.Index + Math.Max(best.Length, 1);
        }

        if (matchEnd)
        {
            occurrences.Add(instance > 0 ? (text.Length, 0) : (0, 0));
            if (instance < 0)
            {
                occurrences.Sort();
            }
        }

        var chosen = instance > 0 ? instance - 1 : occurrences.Count + instance;
        if (chosen < 0 || chosen >= occurrences.Count)
        {
            return call.Has(5) ? call.Value(5) : CellError.NotAvailable;
        }

        var (index, length) = occurrences[chosen];
        return before ? text[..index] : text[(index + length)..];
    }

    private static Operand TextSplit(FunctionCall call)
    {
        var text = call.Text(0);
        var locale = call.Context.Locale;
        var columnDelimiters = call.Has(1) ? call.Values(1).Select(value => Coercion.ToText(value, locale)).Where(text => text.Length > 0).ToArray() : [];
        var rowDelimiters = call.Has(2) ? call.Values(2).Select(value => Coercion.ToText(value, locale)).Where(text => text.Length > 0).ToArray() : [];
        var ignoreEmpty = call.Boolean(3, false);
        var comparison = call.Integer(4, 0) == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var pad = call.Has(5) ? call.Value(5) : CellValue.FromError(CellError.NotAvailable);
        Require(columnDelimiters.Length > 0 || rowDelimiters.Length > 0, CellError.Value);
        var rows = rowDelimiters.Length > 0 ? Split(text, rowDelimiters, comparison) : [text];
        if (ignoreEmpty)
        {
            rows = rows.Where(row => row.Length > 0).ToList();
        }

        var cells = rows.Select(row =>
        {
            var parts = columnDelimiters.Length > 0 ? Split(row, columnDelimiters, comparison) : [row];
            return ignoreEmpty ? parts.Where(part => part.Length > 0).ToList() : parts;
        }).ToList();
        var columns = Math.Max(1, cells.Max(row => row.Count));
        var result = ArrayValue.Filled(Math.Max(1, cells.Count), columns, pad);
        for (var row = 0; row < cells.Count; row++)
        {
            for (var column = 0; column < cells[row].Count; column++)
            {
                result[row, column] = cells[row][column];
            }
        }

        return result;
    }

    private static List<string> Split(string text, string[] delimiters, StringComparison comparison)
    {
        var parts = new List<string>();
        var start = 0;
        var position = 0;
        while (position <= text.Length)
        {
            var best = -1;
            var length = 0;
            foreach (var delimiter in delimiters)
            {
                var found = text.IndexOf(delimiter, position, comparison);
                if (found >= 0 && (best < 0 || found < best))
                {
                    best = found;
                    length = delimiter.Length;
                }
            }

            if (best < 0)
            {
                break;
            }

            parts.Add(text[start..best]);
            start = position = best + length;
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static string ValueToText(CellValue value, bool strict, FunctionCall call) => value.Kind switch
    {
        CellValueKind.Text => strict ? "\"" + value.Text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value.Text,
        CellValueKind.Error => call.Context.Locale.ErrorName(value.Error),
        _ => Coercion.ToText(value, call.Context.Locale),
    };

    /// <summary>Windows-1252 without depending on code page providers (CHAR/CODE are defined by it).</summary>
    private sealed class Latin1WithWindowsCharacters : Encoding
    {
        public static readonly Latin1WithWindowsCharacters Instance = new();

        private static readonly char[] High =
        [
            '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
            '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
        ];

        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            for (var index = 0; index < charCount; index++)
            {
                var character = chars[charIndex + index];
                var position = Array.IndexOf(High, character);
                bytes[byteIndex + index] = position >= 0 ? (byte)(0x80 + position) : character <= 0xFF ? (byte)character : (byte)'?';
            }

            return charCount;
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            for (var index = 0; index < byteCount; index++)
            {
                var value = bytes[byteIndex + index];
                chars[charIndex + index] = value is >= 0x80 and <= 0x9F ? High[value - 0x80] : (char)value;
            }

            return byteCount;
        }

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}

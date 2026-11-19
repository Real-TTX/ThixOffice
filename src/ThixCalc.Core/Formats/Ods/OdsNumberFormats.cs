using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ThixCalc.Core.Formatting;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>
/// Number format codes (Excel's, as the model keeps them) ⇄ ODF data styles: number, percentage, scientific, fraction, currency
/// ([$€-407] → number:currency-symbol with language and country), date and time (incl. elapsed hours [h]), text (@), literals,
/// colours ([Red]) and sections (positive;negative;zero;text and [&gt;100] conditions → style:map onto further data styles). What
/// ODF has no place for becomes the closest thing: "_x" spacing is a space, "?" placeholders are digits.
/// </summary>
internal static class OdsNumberFormats
{
    private static readonly Dictionary<int, (string Language, string Country)> Locales = new()
    {
        [0x407] = ("de", "DE"), [0xC07] = ("de", "AT"), [0x807] = ("de", "CH"), [0x409] = ("en", "US"), [0x809] = ("en", "GB"),
        [0x40C] = ("fr", "FR"), [0x410] = ("it", "IT"), [0x40A] = ("es", "ES"), [0x413] = ("nl", "NL"), [0x415] = ("pl", "PL"),
        [0x411] = ("ja", "JP"), [0x804] = ("zh", "CN"), [0x419] = ("ru", "RU"), [0x416] = ("pt", "BR"), [0x816] = ("pt", "PT"),
    };

    // ---------------------------------------------------------------- code → data styles

    /// <summary>
    /// The data styles of a format code: the main style named <paramref name="name"/> last, the styles its maps apply before it
    /// (name + "P0", "P1" …). Empty for "General".
    /// </summary>
    public static List<XElement> Write(string code, string name)
    {
        if (string.IsNullOrEmpty(code) || NumberFormat.Parse(code).IsGeneral)
        {
            return [];
        }

        var sections = SplitSections(code);
        var conditions = sections.Select(ConditionOf).ToList();
        var result = new List<XElement>();
        if (sections.Count == 1)
        {
            result.Add(Section(sections[0], name));
            return result;
        }

        // Excel: pos;neg → [>=0] pos, neg; pos;neg;zero → [>0], [<0], zero; four sections: the text section is the main one.
        var hasConditions = conditions.Any(condition => condition is not null);
        var defaults = sections.Count switch
        {
            2 => new[] { "value()>=0" },
            3 => ["value()>0", "value()<0"],
            _ => ["value()>0", "value()<0", "value()=0"],
        };
        var mapped = sections.Count >= 4 ? 3 : sections.Count - 1;
        var maps = new List<XElement>();
        for (var index = 0; index < mapped; index++)
        {
            var styleName = $"{name}P{index}";
            result.Add(Section(sections[index], styleName));
            var condition = conditions[index] is { } own ? "value()" + own : hasConditions && index == 1 ? "value()<0" : defaults[Math.Min(index, defaults.Length - 1)];
            maps.Add(new XElement(StyleNs + "map", new XAttribute(StyleNs + "condition", condition), new XAttribute(StyleNs + "apply-style-name", styleName)));
        }

        var main = Section(sections[mapped], name);
        main.Add(maps);
        result.Add(main);
        return result;
    }

    private static XElement Section(string section, string name)
    {
        var parts = Scan(section);
        var kind = parts.Any(part => part.Kind is PartKind.Year or PartKind.Month or PartKind.Day or PartKind.DayOfWeek) ? "date-style"
            : parts.Any(part => part.Kind is PartKind.Hours or PartKind.Minutes or PartKind.Seconds or PartKind.AmPm) ? "time-style"
            : parts.Any(part => part.Kind == PartKind.TextContent) && !parts.Any(part => part.Kind is PartKind.Number or PartKind.Scientific or PartKind.Fraction) ? "text-style"
            : parts.Any(part => part.Kind == PartKind.Currency) ? "currency-style"
            : parts.Any(part => part.Kind == PartKind.Percent) ? "percentage-style"
            : "number-style";
        var element = new XElement(NumberNs + kind, new XAttribute(StyleNs + "name", name));
        if (parts.Any(part => part.Kind == PartKind.ElapsedHours))
        {
            element.Add(new XAttribute(NumberNs + "truncate-on-overflow", "false"));
        }

        foreach (var part in parts)
        {
            switch (part.Kind)
            {
                case PartKind.Color:
                    element.AddFirst(new XElement(StyleNs + "text-properties", new XAttribute(FoNs + "color", "#" + part.Text)));
                    break;
                case PartKind.Literal or PartKind.Percent:
                    if (element.LastNode is XElement { Name.LocalName: "text" } previous && previous.Name == NumberNs + "text")
                    {
                        previous.Value += part.Text;
                    }
                    else
                    {
                        element.Add(new XElement(NumberNs + "text", part.Text));
                    }

                    break;
                case PartKind.Fill:
                    element.Add(new XElement(NumberNs + "fill-character", part.Text));
                    break;
                case PartKind.TextContent:
                    element.Add(new XElement(NumberNs + "text-content"));
                    break;
                case PartKind.Currency:
                    var currency = new XElement(NumberNs + "currency-symbol", part.Text);
                    if (part.Value is { } lcid && Locales.TryGetValue(lcid, out var locale))
                    {
                        currency.Add(new XAttribute(NumberNs + "language", locale.Language), new XAttribute(NumberNs + "country", locale.Country));
                    }

                    element.Add(currency);
                    break;
                case PartKind.General:
                    element.Add(new XElement(NumberNs + "number"));
                    break;
                case PartKind.Number or PartKind.Scientific or PartKind.Fraction:
                    element.Add(part.Element!);
                    break;
                default:
                    element.Add(DatePart(part));
                    break;
            }
        }

        return element;
    }

    private static XElement DatePart(Part part)
    {
        var longStyle = new XAttribute(NumberNs + "style", "long");
        return part.Kind switch
        {
            PartKind.Year => new XElement(NumberNs + "year", part.Length > 2 ? longStyle : null),
            PartKind.Month => new XElement(NumberNs + "month", part.Length is 2 or 4 ? longStyle : null, part.Length >= 3 ? new XAttribute(NumberNs + "textual", "true") : null),
            PartKind.Day => new XElement(NumberNs + "day", part.Length == 2 ? longStyle : null),
            PartKind.DayOfWeek => new XElement(NumberNs + "day-of-week", part.Length >= 4 ? longStyle : null),
            PartKind.Hours or PartKind.ElapsedHours => new XElement(NumberNs + "hours", part.Length >= 2 ? longStyle : null),
            PartKind.Minutes => new XElement(NumberNs + "minutes", part.Length >= 2 ? longStyle : null),
            PartKind.Seconds => new XElement(NumberNs + "seconds", part.Length >= 2 ? longStyle : null,
                part.Value is > 0 ? new XAttribute(NumberNs + "decimal-places", part.Value.Value) : null),
            _ => new XElement(NumberNs + "am-pm"),
        };
    }

    private enum PartKind
    {
        Literal,
        Fill,
        Color,
        Percent,
        TextContent,
        Currency,
        General,
        Number,
        Scientific,
        Fraction,
        Year,
        Month,
        Day,
        DayOfWeek,
        Hours,
        ElapsedHours,
        Minutes,
        Seconds,
        AmPm,
    }

    private sealed record Part(PartKind Kind, string Text = "", int Length = 1, int? Value = null, XElement? Element = null);

    private static List<string> SplitSections(string code)
    {
        var sections = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        var inBracket = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            if (character == '\\' && !inQuote && index + 1 < code.Length)
            {
                current.Append(character).Append(code[++index]);
                continue;
            }

            if (character == '"' && !inBracket)
            {
                inQuote = !inQuote;
            }
            else if (character == '[' && !inQuote)
            {
                inBracket = true;
            }
            else if (character == ']' && !inQuote)
            {
                inBracket = false;
            }
            else if (character == ';' && !inQuote && !inBracket)
            {
                sections.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        sections.Add(current.ToString());
        return sections;
    }

    /// <summary>"&gt;=100" of a section's [&gt;=100], or null.</summary>
    private static string? ConditionOf(string section)
    {
        var index = 0;
        while ((index = section.IndexOf('[', index)) >= 0)
        {
            var end = section.IndexOf(']', index);
            if (end < 0)
            {
                return null;
            }

            var content = section[(index + 1)..end];
            if (content.Length > 1 && content[0] is '<' or '>' or '=')
            {
                return content.Replace("<>", "!=", StringComparison.Ordinal);
            }

            index = end;
        }

        return null;
    }

    /// <summary>The parts of one section, in order.</summary>
    private static List<Part> Scan(string text)
    {
        var parts = new List<Part>();
        var index = 0;
        var isDate = IsDateSection(text);
        while (index < text.Length)
        {
            var character = text[index];
            switch (character)
            {
                case '"':
                {
                    var end = text.IndexOf('"', index + 1);
                    end = end < 0 ? text.Length : end;
                    parts.Add(new Part(PartKind.Literal, text[(index + 1)..end]));
                    index = end + 1;
                    continue;
                }

                case '\\':
                    if (index + 1 < text.Length)
                    {
                        parts.Add(new Part(PartKind.Literal, text[index + 1].ToString()));
                    }

                    index += 2;
                    continue;
                case '_':
                    parts.Add(new Part(PartKind.Literal, " "));
                    index += 2;
                    continue;
                case '*':
                    parts.Add(new Part(PartKind.Fill, index + 1 < text.Length ? text[index + 1].ToString() : " "));
                    index += 2;
                    continue;
                case '[':
                {
                    var end = text.IndexOf(']', index);
                    end = end < 0 ? text.Length - 1 : end;
                    Bracket(text[(index + 1)..end], parts);
                    index = end + 1;
                    continue;
                }

                case '@':
                    parts.Add(new Part(PartKind.TextContent));
                    index++;
                    continue;
                case '%':
                    parts.Add(new Part(PartKind.Percent, "%"));
                    index++;
                    continue;
            }

            if (!isDate && (character is '0' or '#' or '?' || (character == '.' && index + 1 < text.Length && text[index + 1] is '0' or '#' or '?')))
            {
                index = NumberPart(text, index, parts);
                continue;
            }

            if (string.Compare(text, index, "General", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
            {
                parts.Add(new Part(PartKind.General));
                index += 7;
                continue;
            }

            if (string.Compare(text, index, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 || string.Compare(text, index, "A/P", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                parts.Add(new Part(PartKind.AmPm));
                index += string.Compare(text, index, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 ? 5 : 3;
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (isDate && lower is 'y' or 'e' or 'm' or 'd' or 'h' or 's')
            {
                var run = 1;
                while (index + run < text.Length && char.ToLowerInvariant(text[index + run]) == lower)
                {
                    run++;
                }

                index += run;
                switch (lower)
                {
                    case 'y' or 'e':
                        parts.Add(new Part(PartKind.Year, Length: run));
                        break;
                    case 'd':
                        parts.Add(new Part(run >= 3 ? PartKind.DayOfWeek : PartKind.Day, Length: run));
                        break;
                    case 'h':
                        parts.Add(new Part(PartKind.Hours, Length: run));
                        break;
                    case 's':
                        var decimals = 0;
                        if (index + 1 < text.Length && text[index] == '.' && text[index + 1] == '0')
                        {
                            index++;
                            while (index < text.Length && text[index] == '0')
                            {
                                decimals++;
                                index++;
                            }
                        }

                        parts.Add(new Part(PartKind.Seconds, Length: run, Value: decimals));
                        break;
                    default:
                        parts.Add(new Part(PartKind.Month, Length: Math.Min(run, 4)));
                        break;
                }

                continue;
            }

            parts.Add(new Part(PartKind.Literal, character.ToString()));
            index++;
        }

        ResolveMinutes(parts);
        return parts;
    }

    private static bool IsDateSection(string text)
    {
        var inQuote = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                inQuote = !inQuote;
            }
            else if (character == '\\' || character == '_' || character == '*')
            {
                index++;
            }
            else if (character == '[' && !inQuote)
            {
                var end = text.IndexOf(']', index);
                var content = end < 0 ? "" : text[(index + 1)..end].ToLowerInvariant();
                if (content.Length > 0 && content.All(letter => letter is 'h' or 'm' or 's'))
                {
                    return true;
                }

                index = end < 0 ? text.Length : end;
            }
            else if (!inQuote && char.ToLowerInvariant(character) is 'y' or 'd' or 'h' or 's' or 'm')
            {
                if (string.Compare(text, index, "General", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    index += 6;
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static void Bracket(string content, List<Part> parts)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Length > 0 && lower.All(character => character == 'h'))
        {
            parts.Add(new Part(PartKind.ElapsedHours, Length: lower.Length));
            return;
        }

        if (lower.Length > 0 && lower.All(character => character is 'm' or 's'))
        {
            // [mm] / [ss]: elapsed minutes or seconds – ODF only truncates hours; written as minutes/seconds.
            parts.Add(new Part(lower[0] == 'm' ? PartKind.Minutes : PartKind.Seconds, Length: lower.Length, Value: 0));
            return;
        }

        if (content.StartsWith('$'))
        {
            var dash = content.IndexOf('-', 1);
            var symbol = dash < 0 ? content[1..] : content[1..dash];
            int? lcid = dash >= 0 && int.TryParse(content[(dash + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value & 0xFFFF : null;
            if (symbol.Length > 0)
            {
                parts.Add(new Part(PartKind.Currency, symbol, Value: lcid));
            }

            return;
        }

        if (content.Length > 0 && content[0] is '<' or '>' or '=')
        {
            return; // the section's condition (style:map)
        }

        if (FormatColors.Find(content) is { } color)
        {
            parts.Add(new Part(PartKind.Color, color.ToHex()));
        }
    }

    private static int NumberPart(string text, int index, List<Part> parts)
    {
        // The run of placeholders, separators, the decimal point, the exponent and a fraction's slash.
        var start = index;
        var end = index;
        while (end < text.Length)
        {
            var character = text[end];
            if (character is '0' or '#' or '?' or ',' || (character == '.' && !text[start..end].Contains('.')))
            {
                end++;
                continue;
            }

            if (character is 'E' or 'e' && end + 1 < text.Length && text[end + 1] is '+' or '-')
            {
                end += 2;
                continue;
            }

            if (character == '/' || (character == ' ' && end + 1 < text.Length && Rest(text, end + 1).Contains('/') && text[end + 1] is '0' or '#' or '?'))
            {
                end++;
                continue;
            }

            if (char.IsAsciiDigit(character) && text[start..end].Contains('/'))
            {
                end++;
                continue;
            }

            break;
        }

        var run = text[start..end];
        var scale = 0;
        while (run.EndsWith(','))
        {
            scale++;
            run = run[..^1];
        }

        if (run.Contains('/'))
        {
            var space = run.LastIndexOf(' ');
            var whole = space < 0 ? "" : run[..space];
            var fraction = space < 0 ? run : run[(space + 1)..];
            var slash = fraction.IndexOf('/');
            var numerator = fraction[..slash];
            var denominator = fraction[(slash + 1)..];
            var element = new XElement(NumberNs + "fraction",
                whole.Length == 0 ? null : new XAttribute(NumberNs + "min-integer-digits", whole.Count(character => character == '0')),
                new XAttribute(NumberNs + "min-numerator-digits", Math.Max(1, numerator.Count(character => character is '0' or '?' or '#'))));
            if (int.TryParse(denominator, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fixedDenominator))
            {
                element.Add(new XAttribute(NumberNs + "denominator-value", fixedDenominator), new XAttribute(NumberNs + "min-denominator-digits", denominator.Length));
            }
            else
            {
                element.Add(new XAttribute(NumberNs + "min-denominator-digits", Math.Max(1, denominator.Length)));
            }

            parts.Add(new Part(PartKind.Fraction, Element: element));
            return end;
        }

        var exponent = run.IndexOfAny(['E', 'e']);
        var mantissa = exponent < 0 ? run : run[..exponent];
        var point = mantissa.IndexOf('.');
        var integer = point < 0 ? mantissa : mantissa[..point];
        var decimals = point < 0 ? "" : mantissa[(point + 1)..];
        var minInteger = integer.Count(character => character == '0');
        var decimalPlaces = decimals.Count(character => character is '0' or '#' or '?');
        var minDecimals = decimals.Count(character => character is '0' or '?');
        var grouping = integer.Contains(',');
        XElement number;
        if (exponent >= 0)
        {
            number = new XElement(NumberNs + "scientific-number",
                new XAttribute(NumberNs + "decimal-places", decimalPlaces),
                new XAttribute(NumberNs + "min-decimal-places", minDecimals),
                new XAttribute(NumberNs + "min-integer-digits", minInteger),
                new XAttribute(NumberNs + "min-exponent-digits", Math.Max(1, run[(exponent + 2)..].Length)));
        }
        else
        {
            number = new XElement(NumberNs + "number",
                new XAttribute(NumberNs + "decimal-places", decimalPlaces),
                new XAttribute(NumberNs + "min-decimal-places", minDecimals),
                new XAttribute(NumberNs + "min-integer-digits", minInteger));
            if (point >= 0 && decimalPlaces == 0)
            {
                number.Add(new XAttribute(LoExt + "decimal-point", "true"));
            }
        }

        if (grouping)
        {
            number.Add(new XAttribute(NumberNs + "grouping", "true"));
        }

        if (scale > 0)
        {
            number.Add(new XAttribute(NumberNs + "display-factor", Math.Pow(1000, scale).ToString("0", CultureInfo.InvariantCulture)));
        }

        parts.Add(new Part(exponent >= 0 ? PartKind.Scientific : PartKind.Number, Element: number));
        return end;

        static string Rest(string text, int from) => text[from..];
    }

    private static readonly XNamespace LoExt = LoExtNs;

    private static void ResolveMinutes(List<Part> parts)
    {
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].Kind != PartKind.Month || parts[index].Length > 2)
            {
                continue;
            }

            var previous = parts.Take(index).LastOrDefault(part => part.Kind != PartKind.Literal)?.Kind;
            var next = parts.Skip(index + 1).FirstOrDefault(part => part.Kind != PartKind.Literal)?.Kind;
            if (previous is PartKind.Hours or PartKind.ElapsedHours || next is PartKind.Seconds)
            {
                parts[index] = parts[index] with { Kind = PartKind.Minutes };
            }
        }
    }

    // ---------------------------------------------------------------- data styles → code

    /// <summary>The format code of a data style (its maps followed through <paramref name="styles"/>), or null for none.</summary>
    public static string? Read(XElement style, IReadOnlyDictionary<string, XElement> styles)
    {
        var maps = style.Elements(StyleNs + "map").ToList();
        var main = SectionCode(style);
        if (maps.Count == 0)
        {
            return main;
        }

        var mapped = new List<(string Condition, string Code)>();
        foreach (var map in maps)
        {
            var condition = ((string?)map.Attribute(StyleNs + "condition") ?? "").Replace(" ", "", StringComparison.Ordinal);
            if ((string?)map.Attribute(StyleNs + "apply-style-name") is { } name && styles.TryGetValue(name, out var target) && condition.StartsWith("value()", StringComparison.Ordinal))
            {
                mapped.Add((condition[7..], SectionCode(target)));
            }
        }

        var conditions = mapped.Select(entry => entry.Condition).ToList();
        var standard = (mapped.Count == 1 && conditions[0] == ">=0")
            || (mapped.Count == 2 && conditions.SequenceEqual([">0", "<0"]))
            || (mapped.Count == 3 && conditions.SequenceEqual([">0", "<0", "=0"]));
        var sections = mapped.Select(entry => standard ? entry.Code : $"[{entry.Condition.Replace("!=", "<>", StringComparison.Ordinal)}]{entry.Code}").ToList();
        sections.Add(main);
        return string.Join(';', sections);
    }

    private static string SectionCode(XElement style)
    {
        var builder = new StringBuilder();
        var isDate = style.Name.LocalName is "date-style" or "time-style";
        var elapsed = (string?)style.Attribute(NumberNs + "truncate-on-overflow") == "false";
        if (ColorOf((string?)style.Element(StyleNs + "text-properties")?.Attribute(FoNs + "color")) is { } color)
        {
            builder.Append('[').Append(ColorName(color)).Append(']');
        }

        foreach (var part in style.Elements())
        {
            var isLong = (string?)part.Attribute(NumberNs + "style") == "long";
            switch (part.Name.LocalName)
            {
                case "text":
                    builder.Append(Literal(part.Value, isDate, style.Name.LocalName == "percentage-style"));
                    break;
                case "fill-character":
                    builder.Append('*').Append(part.Value.Length > 0 ? part.Value[0] : ' ');
                    break;
                case "text-content":
                    builder.Append('@');
                    break;
                case "currency-symbol":
                    var symbol = part.Value;
                    var language = (string?)part.Attribute(NumberNs + "language");
                    var country = (string?)part.Attribute(NumberNs + "country");
                    var lcid = Locales.FirstOrDefault(pair => pair.Value.Language == language && pair.Value.Country == country).Key;
                    builder.Append("[$").Append(symbol).Append(lcid != 0 ? "-" + lcid.ToString("X", CultureInfo.InvariantCulture) : "").Append(']');
                    break;
                case "number":
                    builder.Append(NumberCode(part));
                    break;
                case "scientific-number":
                    builder.Append(NumberCode(part)).Append("E+").Append(new string('0', Math.Max(1, (int?)part.Attribute(NumberNs + "min-exponent-digits") ?? 2)));
                    break;
                case "fraction":
                    var minInteger = (int?)part.Attribute(NumberNs + "min-integer-digits");
                    var numerator = Math.Max(1, (int?)part.Attribute(NumberNs + "min-numerator-digits") ?? 1);
                    var denominatorDigits = Math.Max(1, (int?)part.Attribute(NumberNs + "min-denominator-digits") ?? 1);
                    if (minInteger is not null)
                    {
                        builder.Append(minInteger > 0 ? new string('0', minInteger.Value) : "#").Append(' ');
                    }

                    builder.Append(new string('?', numerator)).Append('/');
                    builder.Append((int?)part.Attribute(NumberNs + "denominator-value") is { } fixedDenominator ? fixedDenominator.ToString(CultureInfo.InvariantCulture) : new string('?', denominatorDigits));
                    break;
                case "year":
                    builder.Append(isLong ? "yyyy" : "yy");
                    break;
                case "month":
                    var textual = (string?)part.Attribute(NumberNs + "textual") == "true";
                    builder.Append(textual ? isLong ? "mmmm" : "mmm" : isLong ? "mm" : "m");
                    break;
                case "day":
                    builder.Append(isLong ? "dd" : "d");
                    break;
                case "day-of-week":
                    builder.Append(isLong ? "dddd" : "ddd");
                    break;
                case "hours":
                    builder.Append(elapsed ? isLong ? "[hh]" : "[h]" : isLong ? "hh" : "h");
                    break;
                case "minutes":
                    builder.Append(isLong ? "mm" : "m");
                    break;
                case "seconds":
                    builder.Append(isLong ? "ss" : "s");
                    if ((int?)part.Attribute(NumberNs + "decimal-places") is > 0 and var places)
                    {
                        builder.Append('.').Append(new string('0', places));
                    }

                    break;
                case "am-pm":
                    builder.Append("AM/PM");
                    break;
                case "boolean":
                    builder.Append("\"TRUE\";\"TRUE\";\"FALSE\"");
                    break;
                case "quarter":
                    builder.Append("\"Q\"");
                    break;
            }
        }

        var code = builder.ToString();
        if (style.Name.LocalName == "percentage-style" && !code.Contains('%'))
        {
            code += "%";
        }

        return code.Length == 0 ? "General" : code;
    }

    private static string NumberCode(XElement part)
    {
        var places = (int?)part.Attribute(NumberNs + "decimal-places");
        var minInteger = (int?)part.Attribute(NumberNs + "min-integer-digits");
        if (part.Name.LocalName == "number" && places is null && minInteger is null)
        {
            return "General";
        }

        var minDecimals = Math.Min(places ?? 0, (int?)part.Attribute(NumberNs + "min-decimal-places") ?? places ?? 0);
        var grouping = (string?)part.Attribute(NumberNs + "grouping") == "true";
        var integerDigits = minInteger ?? 1;
        string integer;
        if (grouping)
        {
            var pattern = new string('#', Math.Max(0, 4 - integerDigits)) + new string('0', integerDigits);
            integer = pattern.Insert(pattern.Length - 3, ",");
        }
        else
        {
            integer = integerDigits == 0 ? "#" : new string('0', integerDigits);
        }

        var builder = new StringBuilder(integer);
        if ((places ?? 0) > 0)
        {
            builder.Append('.').Append(new string('0', minDecimals)).Append(new string('#', places!.Value - minDecimals));
        }
        else if ((string?)part.Attribute(LoExt + "decimal-point") == "true")
        {
            builder.Append('.');
        }

        if (double.TryParse((string?)part.Attribute(NumberNs + "display-factor"), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) && factor >= 1000)
        {
            builder.Append(',', (int)Math.Round(Math.Log10(factor) / 3));
        }

        return builder.ToString();
    }

    /// <summary>A literal of a format code: plain where Excel shows such characters as they are, else in quotes.</summary>
    private static string Literal(string text, bool isDate, bool isPercent)
    {
        if (text.Length == 0)
        {
            return "";
        }

        if (text.All(character => character is ' ' or '$' or '-' or '+' or '/' or '(' or ')' or ':' or '!' or '^' or '&' or '\'' or '~' or '{' or '}' or '<' or '>' or '='
            || (isDate && character is '.' or ',') || (isPercent && character == '%')))
        {
            return text;
        }

        // Leading and trailing characters Excel shows plainly stay outside the quotes ("#,##0.00 "€"").
        var start = 0;
        while (start < text.Length && text[start] == ' ')
        {
            start++;
        }

        return text[..start] + "\"" + text[start..].Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string ColorName(string hex) => hex.ToUpperInvariant() switch
    {
        "000000" => "Black",
        "0000FF" => "Blue",
        "00FFFF" => "Cyan",
        "00FF00" => "Green",
        "FF00FF" => "Magenta",
        "FF0000" => "Red",
        "FFFFFF" => "White",
        "FFFF00" => "Yellow",
        _ => "Red",
    };
}

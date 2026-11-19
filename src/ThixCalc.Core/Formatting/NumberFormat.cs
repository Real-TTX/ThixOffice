using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Formatting;

/// <summary>The kind of a format section.</summary>
public enum FormatSectionKind : byte
{
    General,
    Number,
    DateTime,
    Text,
}

internal enum TokenKind : byte
{
    Literal,
    Digit,
    DecimalPoint,
    Group,
    Percent,
    Exponent,
    FractionSlash,
    Text,
    Fill,
    Space,
    General,
    Year,
    Month,
    Day,
    Hour,
    Minute,
    Second,
    FractionalSecond,
    ElapsedHours,
    ElapsedMinutes,
    ElapsedSeconds,
    AmPm,
}

/// <summary>One token of a format section: a literal text, a placeholder (0 # ?) or a date/time part with its length.</summary>
internal readonly record struct FormatToken(TokenKind Kind, string Text = "", int Length = 1, char Placeholder = '0');

/// <summary>A condition of a section ([&gt;100]).</summary>
internal readonly record struct FormatCondition(string Operator, double Value)
{
    public bool Matches(double value) => Operator switch
    {
        "<" => value < Value,
        "<=" => value <= Value,
        ">" => value > Value,
        ">=" => value >= Value,
        "=" => value == Value,
        "<>" => value != Value,
        _ => false,
    };
}

/// <summary>A section of a format code (up to four, separated by ;).</summary>
internal sealed class FormatSection
{
    public List<FormatToken> Tokens { get; } = [];

    public FormatSectionKind Kind { get; set; } = FormatSectionKind.Number;

    public FormatCondition? Condition { get; set; }

    public RgbColor? Color { get; set; }

    /// <summary>The locale of [$-407] (LCID), or null.</summary>
    public int? LocaleId { get; set; }

    public int PercentCount { get; set; }

    /// <summary>Thousands scaling: each trailing comma divides by 1000.</summary>
    public int ScaleCommas { get; set; }

    public bool Grouping { get; set; }

    public bool HasAmPm { get; set; }

    public bool HasExponent { get; set; }

    public bool HasFraction { get; set; }

    /// <summary>The fixed denominator of "# ?/8" (or 0).</summary>
    public int FixedDenominator { get; set; }

    public bool HasElapsed { get; set; }

    public bool HasDigits => Tokens.Any(token => token.Kind == TokenKind.Digit);
}

/// <summary>
/// A parsed Excel number format code: sections with conditions and colours, digit placeholders, grouping and scaling,
/// percent, scientific, fractions, dates/times incl. elapsed time and fractional seconds, text, _ spacing and * fill, locale
/// and currency codes ([$€-407]). Parsing is cached per code; <see cref="NumberFormatter"/> applies it.
/// </summary>
public sealed class NumberFormat
{
    private static readonly ConcurrentDictionary<string, NumberFormat> Cache = new(StringComparer.Ordinal);

    private NumberFormat(string code, List<FormatSection> sections)
    {
        Code = code;
        Sections = sections;
    }

    public static NumberFormat General { get; } = Parse("General");

    public string Code { get; }

    internal List<FormatSection> Sections { get; }

    /// <summary>The format shows dates or times (for the grid's alignment and input parsing).</summary>
    public bool IsDateTime => Sections.Count > 0 && Sections[0].Kind == FormatSectionKind.DateTime;

    public bool IsGeneral => Sections.Count > 0 && Sections[0].Kind == FormatSectionKind.General && Sections[0].Tokens.Count == 1;

    public bool IsText => Sections.Count == 1 && Sections[0].Kind == FormatSectionKind.Text;

    public bool IsPercent => Sections.Count > 0 && Sections[0].PercentCount > 0;

    /// <summary>Parses (and caches) a canonical format code.</summary>
    public static NumberFormat Parse(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            code = "General";
        }

        return Cache.GetOrAdd(code, static text => new NumberFormat(text, ParseSections(text)));
    }

    private static List<FormatSection> ParseSections(string code)
    {
        var sections = new List<FormatSection>();
        var current = new StringBuilder();
        var inQuote = false;
        var inBracket = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
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
            else if (character == '\\' && !inQuote && index + 1 < code.Length)
            {
                current.Append(character).Append(code[++index]);
                continue;
            }
            else if (character == ';' && !inQuote && !inBracket)
            {
                sections.Add(ParseSection(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        sections.Add(ParseSection(current.ToString()));
        return sections;
    }

    private static FormatSection ParseSection(string text)
    {
        var section = new FormatSection();
        var tokens = section.Tokens;
        var index = 0;
        var afterDecimal = false;
        var sawDigit = false;
        while (index < text.Length)
        {
            var character = text[index];
            switch (character)
            {
                case '"':
                {
                    var end = text.IndexOf('"', index + 1);
                    if (end < 0)
                    {
                        end = text.Length;
                    }

                    tokens.Add(new FormatToken(TokenKind.Literal, text[(index + 1)..end]));
                    index = end + 1;
                    continue;
                }

                case '\\':
                    if (index + 1 < text.Length)
                    {
                        tokens.Add(new FormatToken(TokenKind.Literal, text[index + 1].ToString()));
                    }

                    index += 2;
                    continue;
                case '_':
                    tokens.Add(new FormatToken(TokenKind.Space, index + 1 < text.Length ? text[index + 1].ToString() : " "));
                    index += 2;
                    continue;
                case '*':
                    tokens.Add(new FormatToken(TokenKind.Fill, index + 1 < text.Length ? text[index + 1].ToString() : " "));
                    index += 2;
                    continue;
                case '[':
                {
                    var end = text.IndexOf(']', index);
                    if (end < 0)
                    {
                        end = text.Length - 1;
                    }

                    ParseBracket(section, text[(index + 1)..end]);
                    index = end + 1;
                    continue;
                }

                case '0' or '#' or '?':
                    tokens.Add(new FormatToken(TokenKind.Digit, Placeholder: character));
                    sawDigit = true;
                    index++;
                    continue;
                case '.':
                    if (LastDateToken(tokens) is TokenKind.Second && index + 1 < text.Length && text[index + 1] == '0')
                    {
                        var start = index + 1;
                        var end = start;
                        while (end < text.Length && text[end] == '0')
                        {
                            end++;
                        }

                        tokens.Add(new FormatToken(TokenKind.FractionalSecond, Length: end - start));
                        index = end;
                        continue;
                    }

                    if (!afterDecimal && (sawDigit || (index + 1 < text.Length && text[index + 1] is '0' or '#' or '?')) && !section.HasExponent)
                    {
                        tokens.Add(new FormatToken(TokenKind.DecimalPoint));
                        afterDecimal = true;
                    }
                    else if (LastDateToken(tokens) is TokenKind.Second && index + 1 < text.Length && text[index + 1] == '0')
                    {
                        var start = index + 1;
                        var end = start;
                        while (end < text.Length && text[end] == '0')
                        {
                            end++;
                        }

                        tokens.Add(new FormatToken(TokenKind.FractionalSecond, Length: end - start));
                        index = end;
                        continue;
                    }
                    else
                    {
                        tokens.Add(new FormatToken(TokenKind.Literal, "."));
                    }

                    index++;
                    continue;
                case ',':
                    if (sawDigit && index + 1 < text.Length && text[index + 1] is '0' or '#' or '?' && !afterDecimal)
                    {
                        section.Grouping = true;
                    }
                    else if (sawDigit)
                    {
                        // A comma after the last digit placeholder scales by 1000.
                        var rest = text[(index + 1)..];
                        if (!rest.Any(next => next is '0' or '#' or '?'))
                        {
                            section.ScaleCommas++;
                        }
                        else
                        {
                            section.Grouping = true;
                        }
                    }
                    else
                    {
                        tokens.Add(new FormatToken(TokenKind.Literal, ","));
                    }

                    index++;
                    continue;
                case '%':
                    tokens.Add(new FormatToken(TokenKind.Percent, "%"));
                    section.PercentCount++;
                    index++;
                    continue;
                case 'E' or 'e' when index + 1 < text.Length && text[index + 1] is '+' or '-' && sawDigit:
                    tokens.Add(new FormatToken(TokenKind.Exponent, text[index + 1].ToString()));
                    section.HasExponent = true;
                    afterDecimal = false;
                    index += 2;
                    continue;
                case '/' when sawDigit && !section.HasExponent && NextIsDenominator(text, index + 1, out var denominator, out var length):
                    tokens.Add(new FormatToken(TokenKind.FractionSlash));
                    section.HasFraction = true;
                    section.FixedDenominator = denominator;
                    if (denominator > 0)
                    {
                        tokens.Add(new FormatToken(TokenKind.Literal, string.Empty, Length: length));
                        index += 1 + length;
                        continue;
                    }

                    index++;
                    continue;
                case '@':
                    tokens.Add(new FormatToken(TokenKind.Text));
                    index++;
                    continue;
            }

            if (MatchWord(text, index, "General") || MatchWord(text, index, "Standard"))
            {
                tokens.Add(new FormatToken(TokenKind.General));
                index += 7 + (MatchWord(text, index, "Standard") ? 1 : 0);
                continue;
            }

            if (MatchWord(text, index, "AM/PM") || MatchWord(text, index, "A/P"))
            {
                var length = MatchWord(text, index, "AM/PM") ? 5 : 3;
                tokens.Add(new FormatToken(TokenKind.AmPm, text.Substring(index, length), length));
                section.HasAmPm = true;
                index += length;
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is 'y' or 'm' or 'd' or 'h' or 's' or 'e' or 'b' or 'g')
            {
                var run = 1;
                while (index + run < text.Length && char.ToLowerInvariant(text[index + run]) == lower)
                {
                    run++;
                }

                var kind = lower switch
                {
                    'y' or 'e' or 'b' => TokenKind.Year,
                    'd' => TokenKind.Day,
                    'h' => TokenKind.Hour,
                    's' => TokenKind.Second,
                    'g' => TokenKind.Literal,
                    _ => TokenKind.Month,
                };
                if (kind != TokenKind.Literal)
                {
                    tokens.Add(new FormatToken(kind, Length: run));
                }

                index += run;
                continue;
            }

            tokens.Add(new FormatToken(TokenKind.Literal, character.ToString()));
            index++;
        }

        ResolveMinutes(tokens);
        if (tokens.Any(token => token.Kind is TokenKind.Year or TokenKind.Month or TokenKind.Day or TokenKind.Hour or TokenKind.Minute
                or TokenKind.Second or TokenKind.ElapsedHours or TokenKind.ElapsedMinutes or TokenKind.ElapsedSeconds or TokenKind.AmPm))
        {
            section.Kind = FormatSectionKind.DateTime;
        }
        else if (tokens.Count == 1 && tokens[0].Kind == TokenKind.General)
        {
            section.Kind = FormatSectionKind.General;
        }
        else if (tokens.Any(token => token.Kind == TokenKind.Text) && !tokens.Any(token => token.Kind == TokenKind.Digit))
        {
            section.Kind = FormatSectionKind.Text;
        }
        else if (tokens.Any(token => token.Kind == TokenKind.General))
        {
            section.Kind = FormatSectionKind.General;
        }

        return section;
    }

    private static TokenKind? LastDateToken(List<FormatToken> tokens)
    {
        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            if (tokens[index].Kind is TokenKind.Second or TokenKind.ElapsedSeconds)
            {
                return TokenKind.Second;
            }

            if (tokens[index].Kind != TokenKind.Literal)
            {
                return tokens[index].Kind;
            }
        }

        return null;
    }

    private static bool NextIsDenominator(string text, int index, out int denominator, out int length)
    {
        denominator = 0;
        length = 0;
        var end = index;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (end > index)
        {
            length = end - index;
            denominator = int.Parse(text.AsSpan(index, length), CultureInfo.InvariantCulture);
            return denominator > 0;
        }

        return index < text.Length && text[index] is '0' or '#' or '?';
    }

    private static bool MatchWord(string text, int index, string word) =>
        string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>"m" right after an hour or right before a second is a minute, otherwise a month.</summary>
    private static void ResolveMinutes(List<FormatToken> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Kind != TokenKind.Month || tokens[index].Length > 2)
            {
                continue;
            }

            var previous = PreviousDatePart(tokens, index);
            var next = NextDatePart(tokens, index);
            if (previous is TokenKind.Hour or TokenKind.ElapsedHours || next is TokenKind.Second or TokenKind.ElapsedSeconds)
            {
                tokens[index] = tokens[index] with { Kind = TokenKind.Minute };
            }
        }
    }

    private static TokenKind? PreviousDatePart(List<FormatToken> tokens, int index)
    {
        for (var position = index - 1; position >= 0; position--)
        {
            if (tokens[position].Kind is not (TokenKind.Literal or TokenKind.Space))
            {
                return tokens[position].Kind;
            }
        }

        return null;
    }

    private static TokenKind? NextDatePart(List<FormatToken> tokens, int index)
    {
        for (var position = index + 1; position < tokens.Count; position++)
        {
            if (tokens[position].Kind is not (TokenKind.Literal or TokenKind.Space))
            {
                return tokens[position].Kind;
            }
        }

        return null;
    }

    private static void ParseBracket(FormatSection section, string content)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Length > 0 && lower.All(character => character is 'h'))
        {
            section.Tokens.Add(new FormatToken(TokenKind.ElapsedHours, Length: lower.Length));
            section.HasElapsed = true;
            return;
        }

        if (lower.Length > 0 && lower.All(character => character is 'm'))
        {
            section.Tokens.Add(new FormatToken(TokenKind.ElapsedMinutes, Length: lower.Length));
            section.HasElapsed = true;
            return;
        }

        if (lower.Length > 0 && lower.All(character => character is 's'))
        {
            section.Tokens.Add(new FormatToken(TokenKind.ElapsedSeconds, Length: lower.Length));
            section.HasElapsed = true;
            return;
        }

        if (content.StartsWith('$'))
        {
            // [$€-407]: currency text and locale; [$-407]: locale only.
            var dash = content.IndexOf('-', 1);
            var symbol = dash < 0 ? content[1..] : content[1..dash];
            if (symbol.Length > 0)
            {
                section.Tokens.Add(new FormatToken(TokenKind.Literal, symbol));
            }

            if (dash >= 0 && int.TryParse(content[(dash + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
            {
                section.LocaleId = lcid & 0xFFFF;
            }

            return;
        }

        if (content.Length >= 2 && content[0] is '<' or '>' or '=')
        {
            var op = content.Length > 1 && content[1] is '=' or '>' ? content[..2] : content[..1];
            if (double.TryParse(content[op.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                section.Condition = new FormatCondition(op, value);
            }

            return;
        }

        if (FormatColors.Find(content) is { } color)
        {
            section.Color = color;
        }
    }

    public override string ToString() => Code;
}

/// <summary>The colour names of format codes ([Red], German [Rot]) and [ColorN] / [FarbeN] from the legacy palette.</summary>
public static class FormatColors
{
    private static readonly (string English, string German, RgbColor Color)[] Named =
    [
        ("Black", "Schwarz", RgbColor.FromHex("000000")),
        ("Blue", "Blau", RgbColor.FromHex("0000FF")),
        ("Cyan", "Zyan", RgbColor.FromHex("00FFFF")),
        ("Green", "Grün", RgbColor.FromHex("00FF00")),
        ("Magenta", "Magenta", RgbColor.FromHex("FF00FF")),
        ("Red", "Rot", RgbColor.FromHex("FF0000")),
        ("White", "Weiß", RgbColor.FromHex("FFFFFF")),
        ("Yellow", "Gelb", RgbColor.FromHex("FFFF00")),
    ];

    public static RgbColor? Find(string name)
    {
        foreach (var (english, german, color) in Named)
        {
            if (string.Equals(name, english, StringComparison.OrdinalIgnoreCase) || string.Equals(name, german, StringComparison.OrdinalIgnoreCase))
            {
                return color;
            }
        }

        foreach (var prefix in new[] { "Color", "Farbe" })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 56)
            {
                return Model.IndexedColors.Get(number + 7);
            }
        }

        return null;
    }

    internal static string? German(string english) =>
        Named.FirstOrDefault(entry => string.Equals(entry.English, english, StringComparison.OrdinalIgnoreCase)).German;

    internal static string? English(string german) =>
        Named.FirstOrDefault(entry => string.Equals(entry.German, german, StringComparison.OrdinalIgnoreCase)).English;
}

using System.Text;
using ThixCalc.Core.Globalization;

namespace ThixCalc.Core.Formatting;

/// <summary>
/// The built-in number formats of SpreadsheetML (numFmtId 0–49, ECMA-376 Part 1, 18.8.30). Cells refer to them by id without
/// a code; ids 5–8, 14, 15–17, 20–22 and 37–44 depend on the reader's locale. The model keeps the canonical en-US code;
/// <see cref="Localize"/> gives the code a locale shows (id 14 is TT.MM.JJJJ in German), <see cref="IdOf"/> the id to write.
/// </summary>
public static class BuiltInFormats
{
    private static readonly Dictionary<int, string> Codes = new()
    {
        [0] = "General",
        [1] = "0",
        [2] = "0.00",
        [3] = "#,##0",
        [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)",
        [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)",
        [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%",
        [10] = "0.00%",
        [11] = "0.00E+00",
        [12] = "# ?/?",
        [13] = "# ??/??",
        [14] = "m/d/yyyy",
        [15] = "d-mmm-yy",
        [16] = "d-mmm",
        [17] = "mmm-yy",
        [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM",
        [20] = "h:mm",
        [21] = "h:mm:ss",
        [22] = "m/d/yyyy h:mm",
        [37] = "#,##0 ;(#,##0)",
        [38] = "#,##0 ;[Red](#,##0)",
        [39] = "#,##0.00;(#,##0.00)",
        [40] = "#,##0.00;[Red](#,##0.00)",
        [41] = "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)",
        [42] = "_(\"$\"* #,##0_);_(\"$\"* \\(#,##0\\);_(\"$\"* \"-\"_);_(@_)",
        [43] = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)",
        [44] = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)",
        [45] = "mm:ss",
        [46] = "[h]:mm:ss",
        [47] = "mmss.0",
        [48] = "##0.0E+0",
        [49] = "@",
    };

    private static readonly Dictionary<int, string> German = new()
    {
        [5] = "#,##0 \"€\";-#,##0 \"€\"",
        [6] = "#,##0 \"€\";[Red]-#,##0 \"€\"",
        [7] = "#,##0.00 \"€\";-#,##0.00 \"€\"",
        [8] = "#,##0.00 \"€\";[Red]-#,##0.00 \"€\"",
        [14] = "dd.mm.yyyy",
        [15] = "dd. mmm yy",
        [16] = "dd. mmm",
        [17] = "mmm yy",
        [20] = "hh:mm",
        [21] = "hh:mm:ss",
        [22] = "dd.mm.yyyy hh:mm",
        [37] = "#,##0;-#,##0",
        [38] = "#,##0;[Red]-#,##0",
        [39] = "#,##0.00;-#,##0.00",
        [40] = "#,##0.00;[Red]-#,##0.00",
        [41] = "_-* #,##0_-;-* #,##0_-;_-* \"-\"_-;_-@_-",
        [42] = "_-* #,##0 \"€\"_-;-* #,##0 \"€\"_-;_-* \"-\" \"€\"_-;_-@_-",
        [43] = "_-* #,##0.00_-;-* #,##0.00_-;_-* \"-\"??_-;_-@_-",
        [44] = "_-* #,##0.00 \"€\"_-;-* #,##0.00 \"€\"_-;_-* \"-\"?? \"€\"_-;_-@_-",
    };

    /// <summary>The canonical code of a built-in id, or null when the id is not built in.</summary>
    public static string? Code(int id) => Codes.TryGetValue(id, out var code) ? code : null;

    /// <summary>The built-in id of a canonical code, or null.</summary>
    public static int? IdOf(string code)
    {
        foreach (var (id, builtIn) in Codes)
        {
            if (string.Equals(builtIn, code, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return code is "mm-dd-yy" ? 14 : null;
    }

    /// <summary>The code a locale shows for <paramref name="code"/> (built-in, locale-dependent formats change).</summary>
    public static string Localize(string code, CalcLocale locale)
    {
        if (!locale.Name.StartsWith("de", StringComparison.Ordinal) || IdOf(code) is not { } id)
        {
            return code;
        }

        return German.TryGetValue(id, out var german) ? german : code;
    }

    public static IReadOnlyDictionary<int, string> All => Codes;
}

/// <summary>
/// Format codes as a user of a locale types and reads them: German "#.##0,00 €", "TT.MM.JJJJ", "Standard", [Rot] ↔ the
/// canonical "#,##0.00 €", "dd.mm.yyyy", "General", [Red]. Separators swap only in number sections (a date's "." stays).
/// </summary>
public static class FormatCodes
{
    public static string ToLocal(string code, CalcLocale locale) => Translate(code, locale, toLocal: true);

    public static string ToCanonical(string code, CalcLocale locale) => Translate(code, locale, toLocal: false);

    private static string Translate(string code, CalcLocale locale, bool toLocal)
    {
        if (locale.IsInvariant || locale.Name.StartsWith("en", StringComparison.Ordinal))
        {
            return code;
        }

        var sections = Split(code);
        return string.Join(";", sections.Select(section => TranslateSection(section, toLocal)));
    }

    private static List<string> Split(string code)
    {
        var sections = new List<string>();
        var builder = new StringBuilder();
        var quote = false;
        var bracket = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            if (character == '\\' && index + 1 < code.Length && !quote)
            {
                builder.Append(character).Append(code[++index]);
                continue;
            }

            if (character == '"' && !bracket)
            {
                quote = !quote;
            }
            else if (!quote && character == '[')
            {
                bracket = true;
            }
            else if (!quote && character == ']')
            {
                bracket = false;
            }
            else if (!quote && !bracket && character == ';')
            {
                sections.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        sections.Add(builder.ToString());
        return sections;
    }

    private static string TranslateSection(string section, bool toLocal)
    {
        var isDate = IsDateSection(section, toLocal);
        var builder = new StringBuilder();
        for (var index = 0; index < section.Length; index++)
        {
            var character = section[index];
            if (character == '"')
            {
                var end = section.IndexOf('"', index + 1);
                end = end < 0 ? section.Length - 1 : end;
                builder.Append(section, index, end - index + 1);
                index = end;
                continue;
            }

            if (character is '\\' or '_' or '*' && index + 1 < section.Length)
            {
                builder.Append(character).Append(section[++index]);
                continue;
            }

            if (character == '[')
            {
                var end = section.IndexOf(']', index);
                end = end < 0 ? section.Length - 1 : end;
                var content = section[(index + 1)..end];
                var color = toLocal ? FormatColors.German(content) : FormatColors.English(content);
                if (color is null && content.StartsWith(toLocal ? "Color" : "Farbe", StringComparison.OrdinalIgnoreCase))
                {
                    color = (toLocal ? "Farbe" : "Color") + content[5..];
                }

                builder.Append('[').Append(color ?? TranslateElapsed(content, toLocal)).Append(']');
                index = end;
                continue;
            }

            if (Matches(section, index, toLocal ? "General" : "Standard"))
            {
                builder.Append(toLocal ? "Standard" : "General");
                index += (toLocal ? "General" : "Standard").Length - 1;
                continue;
            }

            if (!isDate && character is '.' or ',')
            {
                builder.Append(character == '.' ? ',' : '.');
                continue;
            }

            if (isDate)
            {
                builder.Append(TranslateDateLetter(character, toLocal));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string TranslateElapsed(string content, bool toLocal) =>
        content.All(character => character is 'h' or 'H' or 'm' or 'M' or 's' or 'S') ? content : content;

    private static char TranslateDateLetter(char character, bool toLocal)
    {
        if (toLocal)
        {
            return character switch
            {
                'y' or 'Y' => 'J',
                'd' or 'D' => 'T',
                _ => character,
            };
        }

        return character switch
        {
            'J' or 'j' => 'y',
            'T' or 't' => 'd',
            'M' => 'm',
            _ => character,
        };
    }

    private static bool Matches(string text, int index, string word) =>
        string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsDateSection(string section, bool canonicalInput)
    {
        var quote = false;
        var bracket = false;
        for (var index = 0; index < section.Length; index++)
        {
            var character = section[index];
            if (character == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (character is '\\' or '_' or '*')
            {
                index++;
                continue;
            }

            if (character == '[')
            {
                bracket = true;
                continue;
            }

            if (character == ']')
            {
                bracket = false;
                continue;
            }

            if (bracket)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (Matches(section, index, "General") || Matches(section, index, "Standard"))
            {
                return false;
            }

            if (lower is 'y' or 'm' or 'd' or 'h' or 's' || (!canonicalInput && lower is 'j' or 't'))
            {
                return true;
            }
        }

        return false;
    }
}

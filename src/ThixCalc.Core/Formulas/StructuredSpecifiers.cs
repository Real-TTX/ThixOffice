using System.Text;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// The item names of structured references in both languages: stored (and written to files) in English – [#All], [#Data],
/// [#Headers], [#Totals], [#This Row] with "," between items – shown and typed in German as [#Alle], [#Daten], [#Kopfzeilen],
/// [#Ergebnisse], [#Diese Zeile] with ";". Column names (escaped with ') are left alone.
/// </summary>
public static class StructuredSpecifiers
{
    private static readonly (string English, string German)[] Items =
    [
        ("#All", "#Alle"), ("#Data", "#Daten"), ("#Headers", "#Kopfzeilen"), ("#Totals", "#Ergebnisse"), ("#This Row", "#Diese Zeile"),
    ];

    /// <summary>The stored form of a specifier typed in any language.</summary>
    public static string ToCanonical(string specifier) => Translate(specifier, toGerman: false, ',');

    /// <summary>The specifier as a locale with <paramref name="listSeparator"/> shows it (German item names with ';').</summary>
    public static string ToLocal(string specifier, char listSeparator) =>
        listSeparator == ';' ? Translate(specifier, toGerman: true, ';') : specifier;

    private static string Translate(string specifier, bool toGerman, char separator)
    {
        var builder = new StringBuilder(specifier.Length + 8);
        var index = 0;
        while (index < specifier.Length)
        {
            var character = specifier[index];
            if (character == '\'' && index + 1 < specifier.Length)
            {
                builder.Append(character).Append(specifier[index + 1]);
                index += 2;
                continue;
            }

            if (character == '#' && Match(specifier, index, toGerman) is { } item)
            {
                builder.Append(toGerman ? item.German : item.English);
                index += (toGerman ? item.English : item.German).Length;
                continue;
            }

            if (character == '#' && Match(specifier, index, !toGerman) is { } same)
            {
                // Already in the target language: keep its spelling.
                var length = (toGerman ? same.German : same.English).Length;
                builder.Append(toGerman ? same.German : same.English);
                index += length;
                continue;
            }

            if (character is ',' or ';' && Depth(specifier, index) == 1)
            {
                builder.Append(separator);
                index++;
                continue;
            }

            builder.Append(character);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>The item whose source spelling (German when translating to English, English when to German) starts at <paramref name="index"/>.</summary>
    private static (string English, string German)? Match(string text, int index, bool fromEnglish)
    {
        foreach (var item in Items)
        {
            var source = fromEnglish ? item.English : item.German;
            if (string.Compare(text, index, source, 0, source.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (index + source.Length == text.Length || text[index + source.Length] is ']' or ',' or ';' or ' '))
            {
                return item;
            }
        }

        return null;
    }

    private static int Depth(string text, int position)
    {
        var depth = 0;
        for (var index = 0; index < position; index++)
        {
            switch (text[index])
            {
                case '\'':
                    index++;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
            }
        }

        return depth;
    }
}

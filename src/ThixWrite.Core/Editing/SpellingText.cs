using System.Text;
using System.Text.RegularExpressions;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// The text of a paragraph as the spelling check sees it: invisible markers (bookmarks, comment and table-of-contents marks) left
/// out, so a word with a mark inside is one word; objects, tabs and breaks as spaces. <see cref="Offsets"/> maps every character
/// back to the paragraph (one more entry: the end).
/// </summary>
public sealed record SpellingText(string Text, int[] Offsets)
{
    public static SpellingText Of(Paragraph paragraph)
    {
        var builder = new StringBuilder(paragraph.Length);
        var offsets = new List<int>(paragraph.Length + 1);
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            var inline = paragraph.Inlines[index];
            var start = paragraph.StartOf(index);
            if (inline.IsMarker)
            {
                continue;
            }

            if (inline is not TextRun run)
            {
                builder.Append(' ');
                offsets.Add(start);
                continue;
            }

            for (var position = 0; position < run.Text.Length; position++)
            {
                var character = run.Text[position];
                builder.Append(character is SpecialCharacters.Tab or SpecialCharacters.LineBreak or SpecialCharacters.PageBreak ? ' ' : character);
                offsets.Add(start + position);
            }
        }

        offsets.Add(paragraph.Length);
        return new SpellingText(builder.ToString(), [.. offsets]);
    }

    /// <summary>The paragraph range of characters [<paramref name="start"/>, + <paramref name="length"/>) of the checked text.</summary>
    public (int Start, int End) ToParagraph(int start, int length) =>
        (Offsets[Math.Clamp(start, 0, Offsets.Length - 1)], Offsets[Math.Clamp(start + length - 1, 0, Offsets.Length - 1)] + 1);
}

/// <summary>The language of a document's text: the w:lang of its document defaults ("de-DE" when it names none).</summary>
public static partial class DocumentLanguage
{
    public const string Default = "de-DE";

    public static string Of(Document document) =>
        document.Styles.CharacterDefaults.UnknownXml is { } xml && LanguagePattern().Match(xml) is { Success: true } match ? match.Groups[1].Value : Default;

    /// <summary>The default character properties with <paramref name="language"/> as their w:lang (east Asian and complex script languages kept).</summary>
    public static CharacterProperties With(CharacterProperties defaults, string language)
    {
        const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var xml = defaults.UnknownXml ?? "";
        var replaced = LanguagePattern().IsMatch(xml)
            ? LanguagePattern().Replace(xml, $"w:val=\"{language}\"", 1)
            : xml + $"<w:lang w:val=\"{language}\" xmlns:w=\"{WordNamespace}\" />";
        return defaults with { UnknownXml = replaced };
    }

    [GeneratedRegex("(?<=<w:lang\\b[^>]*?)\\bw:val=\"([^\"]+)\"")]
    private static partial Regex LanguagePattern();
}

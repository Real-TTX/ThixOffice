using System.Runtime.CompilerServices;
using ThixOffice.Text.Unicode;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Counts for the status bar and "Wörter zählen". <see cref="Pages"/> comes from the layout (0 when unknown).</summary>
public sealed record DocumentStatistics(int Words, int Characters, int CharactersWithoutSpaces, int Paragraphs, int Pages = 0);

/// <summary>Counts words and characters like Word; per-paragraph results are cached by paragraph instance, so typing recounts one paragraph.</summary>
public static class StatisticsCounter
{
    private sealed record Counts(int Words, int Characters, int CharactersWithoutSpaces);

    private static readonly ConditionalWeakTable<Paragraph, Counts> Cache = new();

    /// <summary>The counts of the whole document.</summary>
    public static DocumentStatistics Count(Document document, int pages = 0)
    {
        int words = 0, characters = 0, withoutSpaces = 0, paragraphs = 0;
        foreach (var block in document.Blocks)
        {
            if (block is not Paragraph paragraph)
            {
                continue;
            }

            var counts = Cache.GetValue(paragraph, item => CountText(item.Text));
            words += counts.Words;
            characters += counts.Characters;
            withoutSpaces += counts.CharactersWithoutSpaces;
            if (paragraph.Length > 0)
            {
                paragraphs++;
            }
        }

        return new DocumentStatistics(words, characters, withoutSpaces, paragraphs, pages);
    }

    /// <summary>The counts of the text in <paramref name="range"/>.</summary>
    public static DocumentStatistics Count(Document document, DocumentRange range)
    {
        int words = 0, characters = 0, withoutSpaces = 0, paragraphs = 0;
        for (var index = range.Start.Block; index <= range.End.Block; index++)
        {
            if (document.Blocks[index] is not Paragraph paragraph)
            {
                continue;
            }

            var from = index == range.Start.Block ? range.Start.Offset : 0;
            var to = index == range.End.Block ? range.End.Offset : paragraph.Length;
            var counts = CountText(paragraph.Text[from..to]);
            words += counts.Words;
            characters += counts.Characters;
            withoutSpaces += counts.CharactersWithoutSpaces;
            if (to > from)
            {
                paragraphs++;
            }
        }

        return new DocumentStatistics(words, characters, withoutSpaces, paragraphs);
    }

    private static Counts CountText(string text)
    {
        var characters = 0;
        var withoutSpaces = 0;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            index += TextBoundaries.GraphemeLength(text, index);
            if (character is SpecialCharacters.Object or SpecialCharacters.LineBreak or SpecialCharacters.PageBreak or SpecialCharacters.SoftHyphen)
            {
                continue;
            }

            characters++;
            if (!char.IsWhiteSpace(character))
            {
                withoutSpaces++;
            }
        }

        return new Counts(TextBoundaries.CountWords(text), characters, withoutSpaces);
    }
}

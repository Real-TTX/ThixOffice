using System.Globalization;

namespace ThixOffice.Text.Unicode;

/// <summary>What kind of character a word boundary search sees.</summary>
public enum WordCharKind
{
    /// <summary>Letters, digits, marks, connector punctuation – and apostrophes / dots between them.</summary>
    Word,
    Space,
    Punctuation,
    /// <summary>Tab, line or page break, embedded object: always a "word" of its own.</summary>
    Special,
}

/// <summary>
/// Grapheme clusters (UAX #29 extended grapheme clusters, by .NET's <see cref="StringInfo"/>) and the word boundaries a word
/// processor uses for Ctrl+Arrow, Ctrl+Backspace and the double click.
/// </summary>
public static class TextBoundaries
{
    /// <summary>Object replacement character: an embedded object or a preserved element in the text.</summary>
    public const char ObjectReplacement = '￼';

    /// <summary>The length of the grapheme cluster starting at <paramref name="index"/>.</summary>
    public static int GraphemeLength(string text, int index) =>
        index >= text.Length ? 0 : Math.Max(1, StringInfo.GetNextTextElementLength(text, index));

    /// <summary>All grapheme boundaries of <paramref name="text"/>, 0 and text.Length included.</summary>
    public static int[] GraphemeBoundaries(string text)
    {
        var boundaries = new List<int>(text.Length + 1) { 0 };
        var index = 0;
        while (index < text.Length)
        {
            index += GraphemeLength(text, index);
            boundaries.Add(index);
        }

        return [.. boundaries];
    }

    /// <summary>A bit per code unit: true where a grapheme cluster starts (and at text.Length).</summary>
    public static bool[] GraphemeStarts(string text)
    {
        var starts = new bool[text.Length + 1];
        var index = 0;
        while (index < text.Length)
        {
            starts[index] = true;
            index += GraphemeLength(text, index);
        }

        starts[text.Length] = true;
        return starts;
    }

    /// <summary>The grapheme boundary after <paramref name="index"/> (text.Length at the end).</summary>
    public static int NextGrapheme(string text, int index)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        var position = 0;
        while (position <= index)
        {
            position += GraphemeLength(text, position);
        }

        return position;
    }

    /// <summary>The grapheme boundary before <paramref name="index"/> (0 at the start).</summary>
    public static int PreviousGrapheme(string text, int index)
    {
        if (index <= 0)
        {
            return 0;
        }

        var position = 0;
        var previous = 0;
        while (position < index && position < text.Length)
        {
            previous = position;
            position += GraphemeLength(text, position);
        }

        return previous;
    }

    /// <summary>The kind of the character at <paramref name="index"/>, with apostrophes and number separators inside words.</summary>
    public static WordCharKind KindAt(string text, int index)
    {
        var character = text[index];
        if (character is '\t' or '\v' or '\f' or '\n' or '\r' or ObjectReplacement)
        {
            return WordCharKind.Special;
        }

        if (char.IsWhiteSpace(character))
        {
            return WordCharKind.Space;
        }

        if (IsWordCharacter(character))
        {
            return WordCharKind.Word;
        }

        // "don't", "geht's", "1.000,50": joiners between word characters belong to the word.
        var isJoiner = character is '\'' or '’' or '.' or ',' or '­' or '‑';
        if (isJoiner && index > 0 && index + 1 < text.Length && IsWordCharacter(text[index - 1]) && IsWordCharacter(text[index + 1]))
        {
            var numberOnly = character is '.' or ',';
            if (!numberOnly || (char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1])))
            {
                return WordCharKind.Word;
            }
        }

        return WordCharKind.Punctuation;
    }

    /// <summary>
    /// The start of the next word after <paramref name="index"/> (Ctrl+Right): skips the rest of the current word or punctuation
    /// group and the spaces after it. Returns text.Length at the end.
    /// </summary>
    public static int NextWordStart(string text, int index)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        var kind = KindAt(text, index);
        var position = index;
        if (kind == WordCharKind.Special)
        {
            position++;
        }
        else if (kind != WordCharKind.Space)
        {
            while (position < text.Length && KindAt(text, position) == kind)
            {
                position++;
            }
        }

        while (position < text.Length && KindAt(text, position) == WordCharKind.Space)
        {
            position++;
        }

        return position;
    }

    /// <summary>The start of the word before <paramref name="index"/> (Ctrl+Left, Ctrl+Backspace): skips spaces, then the word.</summary>
    public static int PreviousWordStart(string text, int index)
    {
        var position = Math.Min(index, text.Length);
        while (position > 0 && KindAt(text, position - 1) == WordCharKind.Space)
        {
            position--;
        }

        if (position == 0)
        {
            return 0;
        }

        var kind = KindAt(text, position - 1);
        if (kind == WordCharKind.Special)
        {
            return position - 1;
        }

        while (position > 0 && KindAt(text, position - 1) == kind)
        {
            position--;
        }

        return position;
    }

    /// <summary>The end of the word at <paramref name="index"/> without the spaces after it (Ctrl+Delete deletes to the next start).</summary>
    public static int WordEnd(string text, int index)
    {
        if (index >= text.Length)
        {
            return text.Length;
        }

        var kind = KindAt(text, index);
        if (kind == WordCharKind.Special)
        {
            return index + 1;
        }

        var position = index;
        while (position < text.Length && KindAt(text, position) == kind)
        {
            position++;
        }

        return position;
    }

    /// <summary>
    /// The word a double click at <paramref name="index"/> selects, like Word: the word and the spaces after it (or the spaces and
    /// nothing else when the click is on spaces at the end).
    /// </summary>
    public static (int Start, int End) WordAt(string text, int index)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var at = Math.Clamp(index, 0, text.Length - 1);
        if (index == text.Length && at > 0 && KindAt(text, at) == WordCharKind.Space)
        {
            at = text.Length - 1;
        }

        var kind = KindAt(text, at);
        if (kind == WordCharKind.Special)
        {
            return (at, at + 1);
        }

        var start = at;
        while (start > 0 && KindAt(text, start - 1) == kind)
        {
            start--;
        }

        var end = WordEnd(text, at);
        if (kind != WordCharKind.Space)
        {
            while (end < text.Length && KindAt(text, end) == WordCharKind.Space)
            {
                end++;
            }
        }

        return (start, end);
    }

    /// <summary>Words as a word processor counts them: groups of characters between spaces and breaks.</summary>
    public static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var character in text)
        {
            var separates = char.IsWhiteSpace(character) || character is '\v' or '\f' or ObjectReplacement;
            if (separates)
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    private static bool IsWordCharacter(char character)
    {
        if (char.IsLetterOrDigit(character) || char.IsSurrogate(character))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.Format;
    }
}

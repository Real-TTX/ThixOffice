using System.Globalization;

namespace ThixOffice.Core.Spelling;

/// <summary>
/// A spelling check in memory, for tests and snapshots: either every word is known except <paramref name="unknown"/> (with the
/// suggestions given for it), or – with <paramref name="known"/> – only the words of that list are. Words with digits are always known.
/// </summary>
public sealed class MemorySpellChecker(string language, IEnumerable<string>? known = null, IReadOnlyDictionary<string, string[]>? unknown = null) : ISpellChecker
{
    private readonly HashSet<string>? _known = known?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _unknown = unknown?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? [];
    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public string Language { get; } = language;

    /// <summary>How many texts were checked (tests: only what is visible or changed is checked).</summary>
    public int Checks { get; private set; }

    public IReadOnlyList<SpellingError> Check(string text)
    {
        lock (_lock)
        {
            Checks++;
            var errors = new List<SpellingError>();
            foreach (var (start, end) in Words(text))
            {
                var word = text[start..end];
                if (!IsKnown(word))
                {
                    errors.Add(new SpellingError(start, end - start));
                }
            }

            return errors;
        }
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        lock (_lock)
        {
            return _unknown.TryGetValue(word, out var suggestions) ? suggestions
                : _known?.Where(candidate => Distance(candidate, word) <= 2).OrderBy(candidate => Distance(candidate, word)).Take(5).ToList() ?? [];
        }
    }

    public void Add(string word)
    {
        lock (_lock)
        {
            _added.Add(word);
        }
    }

    private bool IsKnown(string word) =>
        _added.Contains(word) || word.Any(char.IsDigit)
        || (_known is null ? !_unknown.ContainsKey(word) : _known.Contains(word) && !_unknown.ContainsKey(word));

    /// <summary>The words of a text: runs of letters (with inner apostrophes and hyphens).</summary>
    internal static IEnumerable<(int Start, int End)> Words(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsLetter(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || (text[index] is '\'' or '’' or '-' && index + 1 < text.Length && char.IsLetter(text[index + 1]))
                || CharUnicodeInfo.GetUnicodeCategory(text[index]) is UnicodeCategory.NonSpacingMark))
            {
                index++;
            }

            yield return (start, index);
        }
    }

    private static int Distance(string first, string second)
    {
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var row = 1; row <= first.Length; row++)
        {
            var current = new int[second.Length + 1];
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var cost = char.ToLowerInvariant(first[row - 1]) == char.ToLowerInvariant(second[column - 1]) ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
            }

            previous = current;
        }

        return previous[second.Length];
    }
}

/// <summary>Spelling checks from memory (tests, snapshots): the checkers given, by language.</summary>
public sealed class MemorySpellingProvider(params MemorySpellChecker[] checkers) : ISpellingProvider
{
    public IReadOnlyList<string> Languages { get; } = checkers.Select(checker => checker.Language).ToList();

    public ISpellChecker? Create(string language) => checkers.FirstOrDefault(checker => string.Equals(checker.Language, language, StringComparison.OrdinalIgnoreCase));
}

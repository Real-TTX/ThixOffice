namespace ThixOffice.Core.Spelling;

/// <summary>A word the spelling check does not know: characters [<see cref="Start"/>, + <see cref="Length"/>) of the text checked.</summary>
public readonly record struct SpellingError(int Start, int Length);

/// <summary>
/// The spelling check of one language, UI-free (every app can use it): which words of a text are unknown, suggestions for a word,
/// adding a word to the user's dictionary. Implementations are the system's (Windows: the Spell Checking API; macOS: NSSpellChecker)
/// or <see cref="MemorySpellChecker"/> (tests, snapshots). Calls may block for a moment: call them off the UI thread when there are many.
/// Thread-safe.
/// </summary>
public interface ISpellChecker
{
    /// <summary>The language checked, as a BCP 47 tag ("de-DE").</summary>
    string Language { get; }

    /// <summary>The unknown words of <paramref name="text"/>.</summary>
    IReadOnlyList<SpellingError> Check(string text);

    /// <summary>Suggestions for a word, best first (empty when there are none).</summary>
    IReadOnlyList<string> Suggest(string word);

    /// <summary>"Zum Wörterbuch hinzufügen": the word counts as known from now on (in the user's dictionary of the system).</summary>
    void Add(string word);
}

/// <summary>Where spelling checks come from: the languages there are and a checker for one of them.</summary>
public interface ISpellingProvider
{
    /// <summary>The languages a checker can be made for (BCP 47 tags).</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>A checker for <paramref name="language"/> (one of <see cref="Languages"/>), or null.</summary>
    ISpellChecker? Create(string language);
}

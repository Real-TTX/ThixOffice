using System.Globalization;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Platform.MacOS;
using ThixOffice.Core.Platform.Windows;

namespace ThixOffice.Core.Spelling;

/// <summary>
/// The spelling check of the apps (business layer): the system's checker for a language – the one asked for, else one of the same
/// language ("de-AT" for "de-DE"), else the system's own language, else none (then nothing is underlined) – and the words the user
/// told it to ignore in this session ("Alle ignorieren"), for every checker. No dictionaries are bundled: the system's are used.
/// </summary>
public static class SpellingService
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ISpellChecker?> Checkers = new(StringComparer.OrdinalIgnoreCase);
    private static ISpellingProvider? _provider;
    private static ISpellingProvider? _testProvider;

    /// <summary>Raised when the words to ignore or the dictionary changed (the views check again).</summary>
    public static event EventHandler? WordsChanged;

    /// <summary>The languages the system can check (BCP 47 tags; empty when it has no spelling check).</summary>
    public static IReadOnlyList<string> Languages => Provider.Languages;

    /// <summary>The checker for <paramref name="language"/> or the nearest language the system has; null when it has none.</summary>
    public static ISpellChecker? CheckerFor(string language)
    {
        lock (Gate)
        {
            var resolved = Resolve(language);
            if (resolved is null)
            {
                return null;
            }

            if (!Checkers.TryGetValue(resolved, out var checker))
            {
                try
                {
                    checker = Provider.Create(resolved) is { } created ? new IgnoringChecker(created) : null;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
                {
                    Log.Warning($"The spelling check for {resolved} could not be made: {exception.GetType().Name}");
                    checker = null;
                }

                Checkers[resolved] = checker;
            }

            return checker;
        }
    }

    /// <summary>The language a check for <paramref name="language"/> uses: the same tag, the same language, the system's, the first one; null: none.</summary>
    public static string? Resolve(string language)
    {
        var languages = Provider.Languages;
        if (languages.Count == 0)
        {
            return null;
        }

        string? Find(string tag) =>
            languages.FirstOrDefault(candidate => string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(candidate => string.Equals(Primary(candidate), Primary(tag), StringComparison.OrdinalIgnoreCase));

        return Find(language) ?? Find(CultureInfo.CurrentUICulture.Name) ?? languages[0];
    }

    /// <summary>"Alle ignorieren": the word is not underlined any more until the app closes.</summary>
    public static void Ignore(string word)
    {
        lock (Gate)
        {
            Ignored.Add(word);
        }

        WordsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>"Zum Wörterbuch hinzufügen" through the checker (the system's user dictionary).</summary>
    public static void AddToDictionary(ISpellChecker checker, string word)
    {
        checker.Add(word);
        WordsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The German name of a language tag ("Deutsch (Deutschland)").</summary>
    public static string DisplayName(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            var german = CultureInfo.GetCultureInfo("de-DE");
            return culture.DisplayName is { Length: > 0 } name && !culture.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? German(culture, german) : tag;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    /// <summary>Tests and snapshots check with a provider of their own (null: the system's again).</summary>
    public static void UseForTests(ISpellingProvider? provider)
    {
        lock (Gate)
        {
            _testProvider = provider;
            Checkers.Clear();
            Ignored.Clear();
        }
    }

    private static ISpellingProvider Provider
    {
        get
        {
            if (_testProvider is { } test)
            {
                return test;
            }

            lock (Gate)
            {
                return _provider ??= CreateProvider();
            }
        }
    }

    private static ISpellingProvider CreateProvider()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsSpellChecking.CreateProvider();
            }

            if (OperatingSystem.IsMacOS())
            {
                return MacSpellChecking.CreateProvider();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning($"The system's spelling check is not available: {exception.GetType().Name}");
        }

        return new MemorySpellingProvider();
    }

    private static string Primary(string tag) => tag.Split('-', '_')[0];

    /// <summary>The name in German when .NET has it (invariant globalization: the English name).</summary>
    private static string German(CultureInfo culture, CultureInfo german) => culture.Name switch
    {
        _ when culture.Name.StartsWith("de", StringComparison.OrdinalIgnoreCase) => "Deutsch" + Region(culture),
        _ when culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) => "Englisch" + Region(culture),
        _ when culture.Name.StartsWith("fr", StringComparison.OrdinalIgnoreCase) => "Französisch" + Region(culture),
        _ when culture.Name.StartsWith("es", StringComparison.OrdinalIgnoreCase) => "Spanisch" + Region(culture),
        _ when culture.Name.StartsWith("it", StringComparison.OrdinalIgnoreCase) => "Italienisch" + Region(culture),
        _ when culture.Name.StartsWith("nl", StringComparison.OrdinalIgnoreCase) => "Niederländisch" + Region(culture),
        _ => culture.NativeName,
    };

    private static string Region(CultureInfo culture) => culture.Name.Split('-') is [_, var region, ..] ? region.ToUpperInvariant() switch
    {
        "DE" => " (Deutschland)",
        "AT" => " (Österreich)",
        "CH" => " (Schweiz)",
        "US" => " (USA)",
        "GB" => " (Großbritannien)",
        "FR" => " (Frankreich)",
        _ => $" ({region})",
    } : "";

    /// <summary>A checker that leaves out the words of "Alle ignorieren".</summary>
    private sealed class IgnoringChecker(ISpellChecker inner) : ISpellChecker
    {
        public string Language => inner.Language;

        public IReadOnlyList<SpellingError> Check(string text)
        {
            var errors = inner.Check(text);
            lock (Gate)
            {
                return Ignored.Count == 0 ? errors : errors.Where(error => !Ignored.Contains(text.Substring(error.Start, error.Length))).ToList();
            }
        }

        public IReadOnlyList<string> Suggest(string word) => inner.Suggest(word);

        public void Add(string word) => inner.Add(word);
    }
}

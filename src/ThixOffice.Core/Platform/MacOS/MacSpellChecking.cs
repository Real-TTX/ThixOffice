using System.Runtime.Versioning;
using ThixOffice.Core.Spelling;

namespace ThixOffice.Core.Platform.MacOS;

/// <summary>
/// The spelling check of macOS (NSSpellChecker, AppKit) through the Objective-C runtime: the shared checker, its languages
/// ("de", "de_AT" → "de-AT"), -checkSpellingOfString:startingAt:language:… word by word, -guessesForWordRange:…, -learnWord:. Every
/// call is serialized and wrapped in an autorelease pool (the editor calls it from a worker thread). Without AppKit (no checker
/// class) there are no languages and nothing is underlined. Built and tested by CI on macOS only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacSpellChecking
{
    private static readonly Lock Gate = new();

    public static ISpellingProvider CreateProvider()
    {
        var shared = Shared();
        if (shared == IntPtr.Zero)
        {
            return new MemorySpellingProvider();
        }

        var languages = Call(() =>
        {
            var array = ObjectiveC.Send(shared, ObjectiveC.Selector("availableLanguages"));
            return Strings(array).Select(name => name.Replace('_', '-')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        });
        return new Provider(shared, languages);
    }

    private static IntPtr Shared()
    {
        var type = ObjectiveC.objc_getClass("NSSpellChecker");
        return type == IntPtr.Zero ? IntPtr.Zero : Call(() => ObjectiveC.Send(type, ObjectiveC.Selector("sharedSpellChecker")));
    }

    private static T Call<T>(Func<T> function)
    {
        lock (Gate)
        {
            return ObjectiveC.InPool(function);
        }
    }

    private static List<string> Strings(IntPtr array)
    {
        var result = new List<string>();
        if (array == IntPtr.Zero)
        {
            return result;
        }

        var count = ObjectiveC.SendInteger(array, ObjectiveC.Selector("count"));
        for (nint index = 0; index < count; index++)
        {
            if (ObjectiveC.Text(ObjectiveC.SendIndex(array, ObjectiveC.Selector("objectAtIndex:"), index)) is { } text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private sealed class Provider(IntPtr shared, IReadOnlyList<string> languages) : ISpellingProvider
    {
        public IReadOnlyList<string> Languages { get; } = languages;

        public Spelling.ISpellChecker? Create(string language) => new Checker(shared, language);
    }

    private sealed class Checker(IntPtr shared, string language) : Spelling.ISpellChecker
    {
        private readonly string _cocoaLanguage = language.Replace('-', '_');

        public string Language { get; } = language;

        public IReadOnlyList<SpellingError> Check(string text)
        {
            if (text.Length == 0)
            {
                return [];
            }

            return Call(() =>
            {
                var errors = new List<SpellingError>();
                var value = ObjectiveC.String(text);
                var languageName = ObjectiveC.String(_cocoaLanguage);
                var selector = ObjectiveC.Selector("checkSpellingOfString:startingAt:language:wrap:inSpellDocumentWithTag:wordCount:");
                nint start = 0;
                while (start < text.Length && errors.Count < 2000)
                {
                    var range = ObjectiveC.SendRange(shared, selector, value, start, languageName, false, 0, IntPtr.Zero);
                    if (range.Length <= 0 || range.Location < start || range.Location + range.Length > text.Length)
                    {
                        break;
                    }

                    errors.Add(new SpellingError((int)range.Location, (int)range.Length));
                    start = range.Location + range.Length;
                }

                return (IReadOnlyList<SpellingError>)errors;
            });
        }

        public IReadOnlyList<string> Suggest(string word) => Call(() =>
        {
            var value = ObjectiveC.String(word);
            var guesses = ObjectiveC.Send(shared, ObjectiveC.Selector("guessesForWordRange:inString:language:inSpellDocumentWithTag:"),
                new ObjectiveC.NSRange(0, word.Length), value, ObjectiveC.String(_cocoaLanguage), IntPtr.Zero);
            return (IReadOnlyList<string>)Strings(guesses).Take(8).ToList();
        });

        public void Add(string word) => Call(() =>
        {
            ObjectiveC.Send(shared, ObjectiveC.Selector("learnWord:"), ObjectiveC.String(word));
            return true;
        });
    }
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using ThixOffice.Core.Spelling;

namespace ThixOffice.Core.Platform.Windows;

/// <summary>
/// The spelling check of Windows (the Spell Checking API, Windows 8 and later: ISpellCheckerFactory / ISpellChecker) through
/// source-generated COM – no built-in COM, no reflection, so it works in the trimmed single-file build. Every call runs on one
/// thread of its own that joined the multithreaded apartment; callers wait for it (checks are quick, the editor checks off the
/// UI thread). The dictionaries are those the user installed in Windows (language packs).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsSpellChecking
{
    private static readonly Guid FactoryClass = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
    private static readonly StrategyBasedComWrappers Wrappers = new();
    private static readonly Lazy<ComThread> Thread = new(() => new ComThread());

    public static ISpellingProvider CreateProvider() => Thread.Value.Invoke(() =>
    {
        var interfaceId = typeof(ISpellCheckerFactory).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(FactoryClass, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, interfaceId, out var instance));
        try
        {
            var factory = (ISpellCheckerFactory)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);
            factory.GetSupportedLanguages(out var languages);
            return new Provider(factory, ReadAll(languages));
        }
        finally
        {
            Marshal.Release(instance);
        }
    });

    private sealed class Provider(ISpellCheckerFactory factory, IReadOnlyList<string> languages) : ISpellingProvider
    {
        public IReadOnlyList<string> Languages { get; } = languages;

        public Spelling.ISpellChecker? Create(string language) => Thread.Value.Invoke<Spelling.ISpellChecker?>(() =>
        {
            factory.IsSupported(language, out var supported);
            if (!supported)
            {
                return null;
            }

            factory.CreateSpellChecker(language, out var checker);
            return new Checker(checker, language);
        });
    }

    private sealed class Checker(ISpellCheckerCom checker, string language) : Spelling.ISpellChecker
    {
        public string Language { get; } = language;

        public IReadOnlyList<SpellingError> Check(string text) => text.Length == 0 ? [] : Thread.Value.Invoke(() =>
        {
            var errors = new List<SpellingError>();
            checker.Check(text, out var found);
            while (found.Next(out var error) == 0 && error is not null)
            {
                error.GetStartIndex(out var start);
                error.GetLength(out var length);
                error.GetCorrectiveAction(out var action);
                if (action != 0 /* CORRECTIVE_ACTION_NONE */ && start + length <= text.Length)
                {
                    errors.Add(new SpellingError((int)start, (int)length));
                }
            }

            return (IReadOnlyList<SpellingError>)errors;
        });

        public IReadOnlyList<string> Suggest(string word) => Thread.Value.Invoke(() =>
        {
            checker.Suggest(word, out var suggestions);
            return ReadAll(suggestions);
        });

        public void Add(string word) => Thread.Value.Invoke(() =>
        {
            checker.Add(word);
            return true;
        });
    }

    private static IReadOnlyList<string> ReadAll(IEnumString strings)
    {
        var result = new List<string>();
        while (strings.Next(1, out var text, out var fetched) == 0 && fetched == 1)
        {
            if (text != IntPtr.Zero)
            {
                result.Add(Marshal.PtrToStringUni(text) ?? "");
                Marshal.FreeCoTaskMem(text);
            }
        }

        return result;
    }

    /// <summary>The one thread the COM objects live on (multithreaded apartment).</summary>
    private sealed class ComThread
    {
        private readonly BlockingCollection<Action> _work = [];

        public ComThread()
        {
            var thread = new System.Threading.Thread(Run) { IsBackground = true, Name = "Spelling (Windows)" };
            thread.Start();
        }

        public T Invoke<T>(Func<T> function)
        {
            T result = default!;
            Exception? failure = null;
            using var done = new ManualResetEventSlim();
            _work.Add(() =>
            {
                try
                {
                    result = function();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
            if (failure is not null)
            {
                throw failure is COMException or InvalidCastException ? new InvalidOperationException("The Windows spelling check failed.", failure) : failure;
            }

            return result;
        }

        private void Run()
        {
            _ = CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);
            foreach (var work in _work.GetConsumingEnumerable())
            {
                work();
            }
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    internal partial interface ISpellCheckerFactory
    {
        void GetSupportedLanguages(out IEnumString value);

        void IsSupported(string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool value);

        void CreateSpellChecker(string languageTag, out ISpellCheckerCom value);
    }

    /// <summary>ISpellChecker (the methods in vtable order; only the first ones are used).</summary>
    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
    internal partial interface ISpellCheckerCom
    {
        void GetLanguageTag(out IntPtr value);

        void Check(string text, out IEnumSpellingError value);

        void Suggest(string word, out IEnumString value);

        void Add(string word);

        void Ignore(string word);

        void AutoCorrect(string from, string to);
    }

    [GeneratedComInterface]
    [Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
    internal partial interface IEnumSpellingError
    {
        /// <summary>S_OK with an error, S_FALSE at the end.</summary>
        [PreserveSig]
        int Next(out ISpellingError? value);
    }

    [GeneratedComInterface]
    [Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
    internal partial interface ISpellingError
    {
        void GetStartIndex(out uint value);

        void GetLength(out uint value);

        void GetCorrectiveAction(out int value);

        void GetReplacement(out IntPtr value);
    }

    [GeneratedComInterface]
    [Guid("00000101-0000-0000-C000-000000000046")]
    internal partial interface IEnumString
    {
        [PreserveSig]
        int Next(uint count, out IntPtr text, out uint fetched);

        [PreserveSig]
        int Skip(uint count);

        void Reset();

        void Clone(out IEnumString value);
    }
}

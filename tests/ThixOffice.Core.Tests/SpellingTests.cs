using ThixOffice.Core.Spelling;

namespace ThixOffice.Core.Tests;

/// <summary>The spelling seam: the memory checker, choosing a language, ignoring words, the background queue, the system's checker.</summary>
[Collection("Spelling")]
public sealed class SpellingTests : IDisposable
{
    public void Dispose() => SpellingService.UseForTests(null);

    [Fact]
    public void A_memory_checker_finds_unknown_words_and_suggests_near_ones()
    {
        var checker = new MemorySpellChecker("de-DE", ["Haus", "Maus", "Garten", "groß"]);
        var errors = checker.Check("Das Haus ist groß, der Gartn auch.");
        Assert.Equal(["Das", "ist", "der", "Gartn", "auch"], errors.Select(error => "Das Haus ist groß, der Gartn auch."[error.Start..(error.Start + error.Length)]));
        Assert.Equal("Garten", checker.Suggest("Gartn")[0]);
        checker.Add("Gartn");
        Assert.DoesNotContain(checker.Check("Gartn"), _ => true);
        Assert.Empty(checker.Check("Haus 42 B2B"));
    }

    [Fact]
    public void The_language_falls_back_to_the_same_language_then_the_system_then_the_first()
    {
        SpellingService.UseForTests(new MemorySpellingProvider(new MemorySpellChecker("en-US"), new MemorySpellChecker("de-AT")));
        Assert.Equal("de-AT", SpellingService.Resolve("de-DE"));
        Assert.Equal("en-US", SpellingService.Resolve("en-GB"));
        Assert.NotNull(SpellingService.Resolve("fi-FI"));
        Assert.Equal("de-AT", SpellingService.CheckerFor("de-DE")!.Language);

        SpellingService.UseForTests(new MemorySpellingProvider());
        Assert.Null(SpellingService.CheckerFor("de-DE"));
        Assert.Equal("Deutsch (Deutschland)", SpellingService.DisplayName("de-DE"));
    }

    [Fact]
    public void Ignored_words_are_not_reported_for_any_checker()
    {
        SpellingService.UseForTests(new MemorySpellingProvider(new MemorySpellChecker("de-DE", unknown: new Dictionary<string, string[]> { ["Tipfehler"] = ["Tippfehler"] })));
        var checker = SpellingService.CheckerFor("de-DE")!;
        Assert.Single(checker.Check("Ein Tipfehler."));
        var changed = false;
        SpellingService.WordsChanged += (_, _) => changed = true;
        SpellingService.Ignore("Tipfehler");
        Assert.True(changed);
        Assert.Empty(checker.Check("Ein Tipfehler."));
    }

    [Fact]
    public async Task The_queue_checks_in_the_background_and_remembers_by_owner()
    {
        var checker = new MemorySpellChecker("de-DE", unknown: new Dictionary<string, string[]> { ["Fehlr"] = ["Fehler"] });
        using var queue = new SpellingQueue(checker);
        var owner = new object();
        var checkedOnce = new TaskCompletionSource();
        queue.Checked += (_, _) => checkedOnce.TrySetResult();

        Assert.False(queue.TryGet(owner, "Ein Fehlr hier", out _));
        await checkedOnce.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(queue.TryGet(owner, "Ein Fehlr hier", out var errors));
        Assert.Equal(new SpellingError(4, 5), Assert.Single(errors));
        Assert.Equal(1, checker.Checks);

        queue.Clear();
        Assert.False(queue.TryGet(owner, "Ein Fehlr hier", out _));
    }

    [Fact]
    public void The_system_checker_of_Windows_finds_a_typing_error()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // macOS: CI builds it; the checker is used by the apps
        }

        var language = SpellingService.Languages.FirstOrDefault(tag => tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? SpellingService.Languages.FirstOrDefault(tag => tag.StartsWith("de", StringComparison.OrdinalIgnoreCase));
        if (language is null)
        {
            return; // a Windows without English or German proofing tools
        }

        var checker = SpellingService.CheckerFor(language)!;
        var text = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "This sentense has an error." : "Dieser Satz hat einen Fehlerr.";
        var error = Assert.Single(checker.Check(text));
        var word = text.Substring(error.Start, error.Length);
        Assert.Contains(word, new[] { "sentense", "Fehlerr" });
        Assert.NotEmpty(checker.Suggest(word));
    }
}

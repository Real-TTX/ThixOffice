using ThixOffice.Text.Unicode;

namespace ThixWrite.Core.Tests.TextEngine;

public class LineBreakerTests
{
    /// <summary>The indexes before which a line may (or must) end, text end excluded.</summary>
    private static int[] Breaks(string text)
    {
        var breaks = LineBreaker.GetBreaks(text);
        return Enumerable.Range(1, text.Length - 1).Where(index => breaks[index] != LineBreakKind.None).ToArray();
    }

    [Fact]
    public void The_end_of_the_text_is_mandatory_and_the_start_never_breaks()
    {
        var breaks = LineBreaker.GetBreaks("Hallo");
        Assert.Equal(LineBreakKind.None, breaks[0]);
        Assert.Equal(LineBreakKind.Mandatory, breaks[5]);
        Assert.Equal(LineBreakKind.Mandatory, LineBreaker.GetBreaks("")[0]);
    }

    [Fact]
    public void Breaks_after_spaces_not_before_them()
    {
        Assert.Equal([4, 9], Breaks("Ein Satz hier"));
        Assert.Equal([5], Breaks("Ein  Satz"));
    }

    [Fact]
    public void Breaks_after_a_hyphen_between_letters()
    {
        Assert.Equal([2], Breaks("E-Mail"));
    }

    [Fact]
    public void A_hyphen_before_a_number_stays_with_it()
    {
        // "-5" is a negative number: no break between hyphen and digit.
        Assert.Equal([2], Breaks("a -5"));
    }

    [Theory]
    [InlineData("Wort.")]
    [InlineData("Wort,")]
    [InlineData("Wort!")]
    [InlineData("Wort?")]
    [InlineData("Wort)")]
    [InlineData("Wort]")]
    [InlineData("Wort:")]
    [InlineData("Wort;")]
    public void Never_breaks_before_closing_punctuation(string text)
    {
        Assert.Empty(Breaks(text));
    }

    [Fact]
    public void Never_breaks_before_closing_punctuation_after_a_space()
    {
        var breaks = LineBreaker.GetBreaks("Wort !");
        Assert.Equal(LineBreakKind.None, breaks[5]);
    }

    [Fact]
    public void Never_breaks_after_opening_brackets_and_German_opening_quotes()
    {
        Assert.Equal([4], Breaks("ein (Wort)"));
        Assert.Equal([4], Breaks("ein „Wort“"));
        var breaks = LineBreaker.GetBreaks("( Wort");
        Assert.Equal(LineBreakKind.None, breaks[2]);
    }

    [Fact]
    public void Non_breaking_space_glues()
    {
        Assert.Empty(Breaks("10\u00A0kg"));
        Assert.Equal([3], Breaks("ab 10\u00A0kg"));
    }

    [Fact]
    public void Numbers_with_separators_stay_together()
    {
        Assert.Empty(Breaks("1.000,50"));
        Assert.Empty(Breaks("50%"));
        Assert.Empty(Breaks("$50"));
    }

    [Fact]
    public void A_percent_sign_after_a_space_may_start_a_line()
    {
        // UAX #14: SP ÷ (LB18) comes before the number rules, so "50 %" may break after the space.
        Assert.Equal([3], Breaks("50 %"));
    }

    [Fact]
    public void Breaks_between_ideographs()
    {
        Assert.Equal([1, 2], Breaks("漢字文"));
    }

    [Fact]
    public void Ideographic_full_stop_does_not_start_a_line()
    {
        Assert.Equal([1], Breaks("漢字。"));
    }

    [Fact]
    public void Combining_marks_and_surrogate_pairs_are_never_split()
    {
        // e + combining acute, then an emoji (surrogate pair) between ideographs.
        Assert.Empty(Breaks("e\u0301"));
        var text = "漢😀字";
        var breaks = LineBreaker.GetBreaks(text);
        Assert.Equal(LineBreakKind.None, breaks[2]);
        Assert.NotEqual(LineBreakKind.None, breaks[1]);
        Assert.NotEqual(LineBreakKind.None, breaks[3]);
    }

    [Fact]
    public void A_mark_without_a_base_after_a_space_may_start_a_line()
    {
        Assert.Equal([2], Breaks("a \u0301b"));
    }

    [Fact]
    public void Zero_width_space_soft_hyphen_and_tab_allow_a_break_after_them()
    {
        Assert.Equal([6], Breaks("Donau\u200Bschiff"));
        Assert.Equal([6], Breaks("Donau\u00ADschiff"));
        Assert.Equal([2], Breaks("a\tb"));
    }

    [Fact]
    public void Hard_breaks_are_mandatory()
    {
        var breaks = LineBreaker.GetBreaks("ab\vcd\fe");
        Assert.Equal(LineBreakKind.Mandatory, breaks[3]);
        Assert.Equal(LineBreakKind.Mandatory, breaks[6]);
        Assert.Equal(LineBreakKind.None, breaks[2]);
    }

    [Fact]
    public void Object_replacement_breaks_on_both_sides()
    {
        Assert.Equal([1, 2], Breaks("a\uFFFCb"));
    }

    [Fact]
    public void A_slash_does_not_start_a_line()
    {
        var breaks = LineBreaker.GetBreaks("und/oder");
        Assert.Equal(LineBreakKind.None, breaks[3]);
    }
}

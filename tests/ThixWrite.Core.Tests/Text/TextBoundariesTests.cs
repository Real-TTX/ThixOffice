using ThixOffice.Text;
using ThixOffice.Text.Unicode;

namespace ThixWrite.Core.Tests.Text;

public class TextBoundariesTests
{
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";
    private const string Flags = "\U0001F1E9\U0001F1EA\U0001F1EB\U0001F1F7";

    [Fact]
    public void Combining_marks_belong_to_their_base()
    {
        Assert.Equal([0, 2, 3], TextBoundaries.GraphemeBoundaries("éa"));
        Assert.Equal(2, TextBoundaries.NextGrapheme("éa", 0));
        Assert.Equal(0, TextBoundaries.PreviousGrapheme("éa", 2));
    }

    [Fact]
    public void An_emoji_zwj_sequence_is_one_grapheme()
    {
        Assert.Equal(8, Family.Length);
        Assert.Equal([0, 8], TextBoundaries.GraphemeBoundaries(Family));
        Assert.Equal(8, TextBoundaries.NextGrapheme(Family + "x", 0));
        Assert.Equal(0, TextBoundaries.PreviousGrapheme(Family + "x", 8));
    }

    [Fact]
    public void Flags_are_pairs_of_regional_indicators()
    {
        Assert.Equal([0, 4, 8], TextBoundaries.GraphemeBoundaries(Flags));
    }

    [Fact]
    public void Grapheme_starts_mark_each_cluster_and_the_end()
    {
        var starts = TextBoundaries.GraphemeStarts("é" + Family);
        Assert.True(starts[0]);
        Assert.False(starts[1]);
        Assert.True(starts[2]);
        Assert.False(starts[3]);
        Assert.True(starts[10]);
    }

    [Theory]
    [InlineData("Hallo Welt", 0, 6)]
    [InlineData("Hallo Welt", 6, 10)]
    [InlineData("Hallo Welt", 3, 6)]
    [InlineData("geht's los", 0, 7)]
    [InlineData("1.000,50 Euro", 0, 9)]
    [InlineData("Hallo, Welt", 0, 5)]
    [InlineData("Hallo, Welt", 5, 7)]
    [InlineData("a\tb", 1, 2)]
    [InlineData("Ende", 4, 4)]
    public void Next_word_start(string text, int index, int expected)
    {
        Assert.Equal(expected, TextBoundaries.NextWordStart(text, index));
    }

    [Theory]
    [InlineData("Hallo Welt", 10, 6)]
    [InlineData("Hallo Welt", 6, 0)]
    [InlineData("Hallo Welt", 8, 6)]
    [InlineData("geht's", 6, 0)]
    [InlineData("Hallo, Welt", 6, 5)]
    [InlineData("a\tb", 2, 1)]
    [InlineData("  ", 2, 0)]
    public void Previous_word_start(string text, int index, int expected)
    {
        Assert.Equal(expected, TextBoundaries.PreviousWordStart(text, index));
    }

    [Theory]
    [InlineData("Hallo Welt", 2, 0, 6)]
    [InlineData("Hallo Welt", 8, 6, 10)]
    [InlineData("Hallo Welt", 10, 6, 10)]
    [InlineData("Hallo  ", 6, 5, 7)]
    [InlineData("geht's los", 3, 0, 7)]
    [InlineData("a\tb", 1, 1, 2)]
    [InlineData("", 0, 0, 0)]
    public void Word_at_selects_the_word_and_its_trailing_spaces(string text, int index, int start, int end)
    {
        Assert.Equal((start, end), TextBoundaries.WordAt(text, index));
    }

    [Fact]
    public void Word_end_stops_before_spaces()
    {
        Assert.Equal(5, TextBoundaries.WordEnd("Hallo Welt", 0));
        Assert.Equal(8, TextBoundaries.WordEnd("1.000,50 Euro", 2));
    }

    [Fact]
    public void Kinds_join_apostrophes_and_number_separators_only_inside_words()
    {
        Assert.Equal(WordCharKind.Word, TextBoundaries.KindAt("geht's", 4));
        Assert.Equal(WordCharKind.Punctuation, TextBoundaries.KindAt("'s", 0));
        Assert.Equal(WordCharKind.Word, TextBoundaries.KindAt("1.000", 1));
        Assert.Equal(WordCharKind.Punctuation, TextBoundaries.KindAt("Satz.Ende", 4));
        Assert.Equal(WordCharKind.Special, TextBoundaries.KindAt("￼", 0));
    }

    [Theory]
    [InlineData("Hallo  Welt, wie geht's?", 4)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a\vb\fc", 3)]
    [InlineData("Wort – Wort", 3)]
    [InlineData("Bild￼Text", 2)]
    public void Counts_words_between_spaces_and_breaks(string text, int expected)
    {
        Assert.Equal(expected, TextBoundaries.CountWords(text));
    }
}

public class FixedWidthTextMeasurerTests
{
    private readonly FixedWidthTextMeasurer _measurer = new();

    [Fact]
    public void Widths_are_deterministic()
    {
        var font = new FontSpec("Calibri", 10);
        var first = _measurer.Shape("Hallo Welt", font);
        var second = _measurer.Shape("Hallo Welt", font);
        Assert.Equal(first.Advances, second.Advances);
        Assert.Equal(9 * 5 + 2.5, first.Width, 6);
    }

    [Fact]
    public void Advances_sit_on_grapheme_starts_only()
    {
        var text = "é\U0001F600x";
        var shaped = _measurer.Shape(text, new FontSpec("Calibri", 10));
        Assert.Equal(text.Length, shaped.Advances.Length);
        Assert.Equal(5, shaped.Advances[0]);
        Assert.Equal(0, shaped.Advances[1]);
        Assert.Equal(10, shaped.Advances[2]);
        Assert.Equal(0, shaped.Advances[3]);
        Assert.Equal(5, shaped.Advances[4]);
    }

    [Fact]
    public void Bold_is_wider_and_metrics_fill_the_size()
    {
        var regular = _measurer.Shape("abc", new FontSpec("Calibri", 10));
        var bold = _measurer.Shape("abc", new FontSpec("Calibri", 10, Bold: true));
        Assert.True(bold.Width > regular.Width);
        Assert.Equal(10, _measurer.GetMetrics(new FontSpec("Calibri", 10)).LineHeight, 6);
        Assert.Equal(0, _measurer.Shape("\t", new FontSpec("Calibri", 10)).Width);
        Assert.NotEmpty(_measurer.GetFamilies());
    }
}

using System.Text;
using SkiaSharp;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixWrite.Core.Tests.TextEngine;

public sealed class SkiaTextMeasurerTests : IDisposable
{
    // Arial exists on Windows and macOS; when it does not, the default typeface measures (behaviour is still asserted).
    private static readonly FontSpec Font = new("Arial", 12);

    private readonly SkiaTextMeasurer _measurer = new();

    public void Dispose() => _measurer.Dispose();

    [Fact]
    public void A_missing_font_is_measured_with_its_first_installed_substitute_and_the_metric_compatible_ones_come_first()
    {
        Assert.Equal(["Calibri", "Carlito", "Aptos"], FontSubstitutes.CandidatesFor("Calibri").Take(3));
        Assert.Equal("Caladea", FontSubstitutes.CandidatesFor("Cambria")[1]);
        Assert.Equal("Liberation Sans", FontSubstitutes.CandidatesFor("Arial")[1]);
        Assert.Equal("Liberation Serif", FontSubstitutes.CandidatesFor("Times New Roman")[1]);
        Assert.Equal("Liberation Mono", FontSubstitutes.CandidatesFor("Courier New")[1]);
        Assert.Equal(["Unbekannt"], FontSubstitutes.CandidatesFor("Unbekannt"));

        // Aptos is missing on most machines: the face that shapes it is Aptos or one of its substitutes when any is installed.
        var installed = _measurer.GetFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = FontSubstitutes.CandidatesFor("Aptos");
        var expected = candidates.FirstOrDefault(installed.Contains);
        var shaped = _measurer.Shape("Hallo", new FontSpec("Aptos", 11));
        var face = shaped.GlyphRuns[0].Face.Family;
        if (expected is not null)
        {
            Assert.Equal(expected, face, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void One_advance_per_code_unit_summing_to_the_width()
    {
        var shaped = _measurer.Shape("Hallo Welt", Font);
        Assert.Equal(10, shaped.Advances.Length);
        Assert.Equal(shaped.Advances.Sum(), shaped.Width, 6);
        Assert.True(shaped.Width > 0);
        Assert.NotEmpty(shaped.GlyphRuns);
    }

    [Fact]
    public void Inner_code_units_of_a_grapheme_have_no_advance()
    {
        var text = "e\u0301x\U0001F468\u200D\U0001F469";
        var shaped = _measurer.Shape(text, Font);
        Assert.Equal(text.Length, shaped.Advances.Length);
        Assert.True(shaped.Advances[0] > 0);
        Assert.Equal(0, shaped.Advances[1]);
        for (var index = 4; index < text.Length; index++)
        {
            Assert.Equal(0, shaped.Advances[index]);
        }
    }

    [Fact]
    public void Longer_text_is_wider_and_bold_not_narrower()
    {
        Assert.True(_measurer.Shape("Hallo Welt", Font).Width > _measurer.Shape("Hallo", Font).Width);
        var regular = _measurer.Shape("Mmmm", Font).Width;
        var bold = _measurer.Shape("Mmmm", Font with { Bold = true }).Width;
        Assert.True(bold >= regular - 0.01);
    }

    [Fact]
    public void Width_scales_with_the_size()
    {
        var small = _measurer.Shape("Hallo", Font).Width;
        var large = _measurer.Shape("Hallo", Font with { Size = 24 }).Width;
        Assert.Equal(small * 2, large, 1);
    }

    [Fact]
    public void Metrics_are_positive()
    {
        var metrics = _measurer.GetMetrics(Font);
        Assert.True(metrics.Ascent > 0);
        Assert.True(metrics.Descent > 0);
        Assert.True(metrics.LineGap >= 0);
        Assert.True(metrics.LineHeight > Font.Size * 0.9);
        Assert.True(metrics.UnderlineThickness > 0);
        Assert.True(metrics.StrikeoutPosition > 0);
    }

    [Fact]
    public void Kerning_never_makes_a_pair_wider()
    {
        var pair = _measurer.Shape("AV", Font).Width;
        var single = _measurer.Shape("A", Font).Width + _measurer.Shape("V", Font).Width;
        Assert.True(pair <= single + 0.01);
    }

    [Fact]
    public void Controls_take_no_width()
    {
        var shaped = _measurer.Shape("a\tb", Font);
        Assert.Equal(0, shaped.Advances[1]);
    }

    [Fact]
    public void Characters_the_font_lacks_fall_back_to_another_font()
    {
        var shaped = _measurer.Shape("ab漢字", Font);
        Assert.True(shaped.Advances[2] > 0);
        Assert.True(shaped.Advances[3] > 0);
        var faces = shaped.GlyphRuns.Select(run => run.Face).Distinct().ToList();
        Assert.All(shaped.GlyphRuns, run => Assert.Equal(run.Glyphs.Length, run.Positions.Length));
        if (faces.Count < 2)
        {
            // No CJK fallback font on this machine (or the font has the glyphs): nothing more to check.
            return;
        }

        var cjkRun = shaped.GlyphRuns.Single(run => run.Start == 2);
        Assert.All(cjkRun.Glyphs, glyph => Assert.NotEqual(0, glyph));
    }

    [Fact]
    public void Glyph_positions_follow_the_advances()
    {
        var shaped = _measurer.Shape("Text", Font);
        var run = Assert.Single(shaped.GlyphRuns);
        Assert.Equal(4, run.Glyphs.Length);
        for (var index = 1; index < run.Positions.Length; index++)
        {
            Assert.True(run.Positions[index] > run.Positions[index - 1]);
        }
    }

    [Fact]
    public void Lists_the_installed_families()
    {
        var families = _measurer.GetFamilies();
        Assert.NotEmpty(families);
        Assert.Equal(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

public sealed class PdfExportTests : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = new();

    public void Dispose() => _measurer.Dispose();

    private PageDisplayList Page(int index, bool withGlyphs)
    {
        var font = new FontSpec("Arial", 24);
        var shaped = _measurer.Shape("Seite " + (index + 1), font);
        var run = shaped.GlyphRuns[0];
        var x = new double[shaped.Text.Length];
        for (var position = 1; position < x.Length; position++)
        {
            x[position] = x[position - 1] + shaped.Advances[position - 1];
        }

        var text = new GlyphRunItem(
            72,
            100,
            shaped.Text,
            font,
            run.Face,
            RgbColor.Black,
            x,
            withGlyphs ? run.Glyphs : [],
            withGlyphs ? run.Positions : []);
        return new PageDisplayList(index, 595.3, 841.9,
        [
            new RectangleItem(70, 70, 200, 40, RgbColor.FromHex("FFFF00")),
            text,
            new LineItem(72, 105, 200, 105, RgbColor.Black, 1),
            new LineItem(72, 110, 200, 110, RgbColor.FromHex("FF0000"), 1, LineStyle.Wavy),
            new LineItem(72, 115, 200, 115, RgbColor.Black, 1, LineStyle.Dotted),
        ]);
    }

    [Fact]
    public void Exports_one_pdf_page_per_display_list()
    {
        using var output = new MemoryStream();
        PdfExport.Export(Enumerable.Range(0, 3).Select(index => Page(index, index != 1)), output, new PdfMetadata("Test"), _measurer);

        var bytes = output.ToArray();
        Assert.True(bytes.Length > 100);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        var content = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Count 3", content);
    }

    [Fact]
    public void Draws_text_onto_a_bitmap()
    {
        using var bitmap = new SKBitmap(300, 150);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            var page = Page(0, withGlyphs: true) with { Items = [Page(0, withGlyphs: true).Items[1]] };
            SkiaPageRenderer.Draw(canvas, page, _measurer);
        }

        Assert.True(HasDarkPixel(bitmap, 72, 76, 160, 104));
    }

    [Fact]
    public void Draws_text_without_glyphs_by_its_character_positions()
    {
        using var bitmap = new SKBitmap(300, 150);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            var page = Page(0, withGlyphs: false) with { Items = [Page(0, withGlyphs: false).Items[1]] };
            SkiaPageRenderer.Draw(canvas, page);
        }

        Assert.True(HasDarkPixel(bitmap, 72, 76, 160, 104));
    }

    private static bool HasDarkPixel(SKBitmap bitmap, int left, int top, int right, int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

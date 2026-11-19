using System.Text;
using ThixOffice.Text.Unicode;

namespace ThixOffice.Text;

/// <summary>
/// A deterministic measurer for tests and headless runs: every grapheme cluster is <see cref="CharacterWidth"/> × size wide
/// (bold <see cref="BoldCharacterWidth"/>), a space <see cref="SpaceWidth"/> × size, an ideograph or emoji a full em, marks
/// and controls nothing. Ascent 0.8 em, descent 0.2 em, no line gap: a 10 pt line is exactly 10 pt high.
/// </summary>
public sealed class FixedWidthTextMeasurer : ITextMeasurer
{
    public const double CharacterWidth = 0.5;
    public const double BoldCharacterWidth = 0.6;
    public const double SpaceWidth = 0.25;

    private static readonly string[] Families = ["Arial", "Calibri", "Calibri Light", "Cambria", "Courier New", "Times New Roman"];

    public FontMetrics GetMetrics(FontSpec font) =>
        new(font.Size * 0.8, font.Size * 0.2, 0, font.Size * 0.1, font.Size * 0.05, font.Size * 0.3, font.Size * 0.05);

    public ShapedText Shape(string text, FontSpec font)
    {
        var advances = new double[text.Length];
        var index = 0;
        while (index < text.Length)
        {
            var length = TextBoundaries.GraphemeLength(text, index);
            advances[index] = AdvanceOf(Rune.GetRuneAt(text, index), font);
            index += length;
        }

        return new ShapedText(text, font, advances, []);
    }

    public IReadOnlyList<string> GetFamilies() => Families;

    private static double AdvanceOf(Rune rune, FontSpec font)
    {
        var value = rune.Value;
        if (value == ' ' || value == 0x00A0)
        {
            return font.Size * SpaceWidth;
        }

        if (value < 0x20 || value is 0x00AD or 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF)
        {
            return 0;
        }

        if (LineBreaker.ClassOf(rune) == LineBreaker.BreakClass.ID)
        {
            return font.Size;
        }

        return font.Size * (font.Bold ? BoldCharacterWidth : CharacterWidth);
    }
}

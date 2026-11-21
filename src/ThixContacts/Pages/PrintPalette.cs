using ThixOffice.Text.Drawing;

namespace ThixContacts.Pages;

/// <summary>
/// The colours of the printed contacts (paper, not the UI: the window takes its colours from MatDesktop's tokens): ink, the muted ink of details and
/// the hairlines. The one file of ThixContacts with literal colours (the colour rules test exempts it).
/// </summary>
public static class PrintPalette
{
    public static RgbColor Ink { get; } = new(0x1F, 0x23, 0x28);

    public static RgbColor Muted { get; } = new(0x5F, 0x67, 0x70);

    public static RgbColor Rule { get; } = new(0xD0, 0xD5, 0xDB);

    public static SkiaSharp.SKColor Paper => SkiaSharp.SKColors.White;
}
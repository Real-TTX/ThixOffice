using Avalonia.Media;

namespace ThixOffice.Pim.Desktop.Calendar;

/// <summary>
/// The colours of calendars – user data ("#RRGGBB", chosen in ThixCalendar, travelling with the calendar), never colours of the program: the bar of an
/// appointment in its calendar's colour, its block a light tint of it. The one file of ThixOffice.Pim.Desktop that turns colours into brushes (the rules
/// tests exempt it); everything else of the views takes Mat tokens.
/// </summary>
public static class CalendarPalette
{
    /// <summary>The calendar's colour as a brush; null for no valid colour (then the view takes Mat.Accent).</summary>
    public static IBrush? Solid(string? color) => Parse(color) is { } parsed ? new SolidColorBrush(parsed) : null;

    /// <summary>The block of an appointment: the calendar's colour, translucent (dark and light themes alike show the text in Mat.Text above it).</summary>
    public static IBrush? Tint(string? color, bool tentative = false) =>
        Parse(color) is { } parsed ? new SolidColorBrush(parsed, tentative ? 0.12 : 0.26) : null;

    /// <summary>Nothing to see but something to click: the free areas of the grids take the pointer.</summary>
    public static IBrush HitArea { get; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    // ---- paper (CalendarPrinting): ink, muted ink, hairlines, the days of other months, and the calendars' colours on white ----

    public static ThixOffice.Text.Drawing.RgbColor PrintInk { get; } = new(0x1F, 0x23, 0x28);

    public static ThixOffice.Text.Drawing.RgbColor PrintMuted { get; } = new(0x5F, 0x67, 0x70);

    public static ThixOffice.Text.Drawing.RgbColor PrintRule { get; } = new(0xC8, 0xCE, 0xD5);

    public static ThixOffice.Text.Drawing.RgbColor PrintOtherMonth { get; } = new(0xF3, 0xF4, 0xF6);

    /// <summary>White paper of the printed calendar.</summary>
    public static SkiaSharp.SKColor PrintPaper => SkiaSharp.SKColors.White;

    /// <summary>A calendar's colour on paper (the bar); a blue for none.</summary>
    public static ThixOffice.Text.Drawing.RgbColor PrintColor(string? color) =>
        Parse(color) is { } parsed ? new(parsed.R, parsed.G, parsed.B) : new(0x0F, 0x6C, 0xBD);

    /// <summary>The block of an appointment on paper: its calendar's colour, light (mixed with white).</summary>
    public static ThixOffice.Text.Drawing.RgbColor PrintTint(string? color)
    {
        var solid = PrintColor(color);
        static byte Mix(byte value) => (byte)(value + ((255 - value) * 0.78));
        return new(Mix(solid.R), Mix(solid.G), Mix(solid.B));
    }

    private static Color? Parse(string? color) => color is { Length: 7 } && Color.TryParse(color, out var parsed) ? parsed : null;
}

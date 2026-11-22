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

    private static Color? Parse(string? color) => color is { Length: 7 } && Color.TryParse(color, out var parsed) ? parsed : null;
}

using System.Globalization;
using ThixOffice.Pim.Calendar;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Pim.Desktop.Calendar;

/// <summary>Outlook's print styles of the calendar.</summary>
public enum CalendarPrintStyle
{
    /// <summary>Tagesformat: one day, its hours.</summary>
    Day,

    /// <summary>Wochenformat: the week (or the work week) as columns of hours.</summary>
    Week,

    /// <summary>Monatsformat: the month as six weeks.</summary>
    Month,
}

/// <summary>
/// The calendar on paper (A4 landscape) for the shared print page and the PDF: Tagesformat and Wochenformat as a grid of hours (7–19 h, wider when
/// appointments are earlier or later; all-day appointments in lanes above it; overlapping ones side by side), Monatsformat as six weeks with the
/// appointments of each day as lines ("+n weitere"). Appointments in their calendar's colour (a bar and a light tint of it); everything else in the
/// print colours of <see cref="CalendarPalette"/>. Display items in points – the same pages for the printer, the preview and the PDF.
/// </summary>
public static class CalendarPrinting
{
    public const double PageWidth = 841.89;
    public const double PageHeight = 595.28;
    private const double Margin = 36;
    private const double TitleHeight = 34;
    private const string Font = "Segoe UI";
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The days a style prints around <paramref name="anchor"/> (Wochenformat: Monday–Friday when <paramref name="workWeek"/>).</summary>
    public static IReadOnlyList<DateTime> DaysOf(CalendarPrintStyle style, DateTime anchor, bool workWeek)
    {
        var day = anchor.Date;
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        switch (style)
        {
            case CalendarPrintStyle.Day:
                return [day];
            case CalendarPrintStyle.Week:
                return [.. Enumerable.Range(0, workWeek ? 5 : 7).Select(i => monday.AddDays(i))];
            default:
                var first = new DateTime(day.Year, day.Month, 1);
                var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
                return [.. Enumerable.Range(0, 42).Select(i => start.AddDays(i))];
        }
    }

    /// <summary>The page of <paramref name="style"/> with <paramref name="appointments"/> (those of the shown calendars; others of the period are ignored).</summary>
    public static IReadOnlyList<PageDisplayList> Pages(CalendarPrintStyle style, DateTime anchor, bool workWeek, IReadOnlyList<CalendarAppointment> appointments,
        Func<string, string?> colorOf, ITextMeasurer measurer)
    {
        var days = DaysOf(style, anchor, workWeek);
        var items = new List<DisplayItem>();
        Text(items, measurer, Title(style, anchor, days), Margin, Margin + 16, 16, true, CalendarPalette.PrintInk);
        Text(items, measurer, "ThixMail – Kalender", PageWidth - Margin, Margin + 16, 8, false, CalendarPalette.PrintMuted, alignRight: true);
        items.Add(new LineItem(Margin, Margin + TitleHeight - 6, PageWidth - Margin, Margin + TitleHeight - 6, CalendarPalette.PrintRule, 0.75));
        var shown = appointments.Where(a => days.Any(a.Touches)).OrderBy(a => a.Start).ToList();
        if (style == CalendarPrintStyle.Month)
        {
            Month(items, measurer, anchor, days, shown, colorOf);
        }
        else
        {
            Hours(items, measurer, days, shown, colorOf);
        }

        return [new PageDisplayList(0, PageWidth, PageHeight, items)];
    }

    private static string Title(CalendarPrintStyle style, DateTime anchor, IReadOnlyList<DateTime> days) => style switch
    {
        CalendarPrintStyle.Day => anchor.ToString("dddd, d. MMMM yyyy", German),
        CalendarPrintStyle.Month => anchor.ToString("MMMM yyyy", German),
        _ => days[0].Month == days[^1].Month
            ? $"{days[0].Day}.–{days[^1].ToString("d. MMMM yyyy", German)}"
            : $"{days[0].ToString("d. MMMM", German)} – {days[^1].ToString("d. MMMM yyyy", German)}",
    };

    // ===== Tagesformat, Wochenformat =====

    private static void Hours(List<DisplayItem> items, ITextMeasurer measurer, IReadOnlyList<DateTime> days, List<CalendarAppointment> shown, Func<string, string?> colorOf)
    {
        const double timeColumn = 40;
        var left = Margin + timeColumn;
        var width = PageWidth - Margin - left;
        var dayWidth = width / days.Count;
        var top = Margin + TitleHeight;

        // the day names
        for (var i = 0; i < days.Count; i++)
        {
            Text(items, measurer, Fit(measurer, days[i].ToString(days.Count == 1 ? "dddd, d. MMMM" : "ddd d.", German), dayWidth - 8, 10, true), left + (i * dayWidth) + 4, top + 13, 10, true, CalendarPalette.PrintInk);
        }

        top += 20;

        // all-day (and longer) appointments in lanes, at most three; the rest counted
        var allDay = shown.Where(a => a.IsAllDay || a.EndDay - a.FirstDay > TimeSpan.FromDays(1)).ToList();
        var lanes = new List<DateTime>();
        var hidden = 0;
        foreach (var appointment in allDay)
        {
            var first = Math.Max(0, (int)(appointment.FirstDay - days[0]).TotalDays);
            var last = Math.Min(days.Count - 1, Math.Max(first, (int)(appointment.EndDay - days[0]).TotalDays - 1));
            var lane = lanes.FindIndex(free => free <= days[first]);
            if (lane < 0)
            {
                if (lanes.Count == 3)
                {
                    hidden++;
                    continue;
                }

                lane = lanes.Count;
                lanes.Add(DateTime.MinValue);
            }

            lanes[lane] = days[last].AddDays(1);
            Block(items, measurer, appointment.Title, null, left + (first * dayWidth) + 2, top + (lane * 15), ((last - first + 1) * dayWidth) - 4, 13, colorOf(appointment.CalendarKey));
        }

        var laneHeight = Math.Max(1, lanes.Count) * 15;
        Text(items, measurer, hidden > 0 ? $"+{hidden}" : "ganztägig", Margin + timeColumn - 4, top + 10, 7, false, CalendarPalette.PrintMuted, alignRight: true);
        top += laneHeight + 4;
        items.Add(new LineItem(Margin, top, PageWidth - Margin, top, CalendarPalette.PrintRule, 0.75));

        // the hours: 7–19, wider for earlier or later appointments
        var timed = shown.Where(a => !allDay.Contains(a)).ToList();
        var firstHour = Math.Min(7, timed.Count == 0 ? 7 : timed.Min(a => a.StartLocal.Date < days[0] ? 0 : a.StartLocal.Hour));
        var lastHour = Math.Max(19, timed.Count == 0 ? 19 : timed.Max(a => a.EndLocal.Date > a.StartLocal.Date ? 24 : (int)Math.Ceiling(a.EndLocal.TimeOfDay.TotalHours)));
        var bottom = PageHeight - Margin;
        var hourHeight = (bottom - top) / (lastHour - firstHour);
        for (var hour = firstHour; hour <= lastHour; hour++)
        {
            var y = top + ((hour - firstHour) * hourHeight);
            items.Add(new LineItem(left, y, PageWidth - Margin, y, CalendarPalette.PrintRule, hour == firstHour ? 0.75 : 0.4));
            if (hour < lastHour)
            {
                Text(items, measurer, $"{hour:00}:00", left - 4, y + 9, 7.5, false, CalendarPalette.PrintMuted, alignRight: true);
            }
        }

        for (var i = 0; i <= days.Count; i++)
        {
            var x = left + (i * dayWidth);
            items.Add(new LineItem(x, Margin + TitleHeight, x, bottom, CalendarPalette.PrintRule, 0.4));
        }

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var ofDay = timed.Where(a => a.FirstDay == day).OrderBy(a => a.Start).ThenByDescending(a => a.End).ToList();
            foreach (var (appointment, column, columns) in CalendarView.Columns(ofDay))
            {
                var start = Math.Max(firstHour, (appointment.StartLocal - day).TotalHours);
                var end = Math.Min(lastHour, Math.Max(start + 0.25, ((appointment.EndLocal > appointment.StartLocal ? appointment.EndLocal : appointment.StartLocal.AddMinutes(30)) - day).TotalHours));
                var columnWidth = (dayWidth - 4) / columns;
                var when = $"{appointment.StartLocal:HH:mm}–{appointment.EndLocal:HH:mm}";
                Block(items, measurer, appointment.Title, appointment.Location.Length > 0 ? $"{when} · {appointment.Location}" : when,
                    left + (i * dayWidth) + 2 + (column * columnWidth), top + ((start - firstHour) * hourHeight) + 1, columnWidth - 2, ((end - start) * hourHeight) - 2,
                    colorOf(appointment.CalendarKey));
            }
        }
    }

    // ===== Monatsformat =====

    private static void Month(List<DisplayItem> items, ITextMeasurer measurer, DateTime anchor, IReadOnlyList<DateTime> days, List<CalendarAppointment> shown, Func<string, string?> colorOf)
    {
        var top = Margin + TitleHeight;
        var cellWidth = (PageWidth - (2 * Margin)) / 7;
        for (var column = 0; column < 7; column++)
        {
            Text(items, measurer, days[column].ToString("dddd", German), Margin + (column * cellWidth) + 4, top + 11, 9, true, CalendarPalette.PrintInk);
        }

        top += 16;
        var cellHeight = (PageHeight - Margin - top) / 6;
        var lines = Math.Max(1, (int)((cellHeight - 16) / 11));
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var (x, y) = (Margin + ((index % 7) * cellWidth), top + ((index / 7) * cellHeight));
            if (day.Month != anchor.Month)
            {
                items.Add(new RectangleItem(x, y, cellWidth, cellHeight, CalendarPalette.PrintOtherMonth));
            }

            items.Add(new RectangleItem(x, y, cellWidth, cellHeight, null, CalendarPalette.PrintRule, 0.5));
            Text(items, measurer, day.Day == 1 ? day.ToString("d. MMM", German) : day.Day.ToString(German), x + 4, y + 11, 8.5, day.Month == anchor.Month, day.Month == anchor.Month ? CalendarPalette.PrintInk : CalendarPalette.PrintMuted);
            var ofDay = shown.Where(a => a.Touches(day)).ToList();
            for (var i = 0; i < Math.Min(lines, ofDay.Count); i++)
            {
                var lineY = y + 15 + (i * 11);
                if (i == lines - 1 && ofDay.Count > lines)
                {
                    Text(items, measurer, $"+{ofDay.Count - lines + 1} weitere", x + 6, lineY + 8, 7, false, CalendarPalette.PrintMuted);
                    break;
                }

                var appointment = ofDay[i];
                var color = CalendarPalette.PrintColor(colorOf(appointment.CalendarKey));
                items.Add(new RectangleItem(x + 3, lineY + 1, 2, 9, color));
                var text = appointment.IsAllDay ? appointment.Title : $"{appointment.StartLocal:HH:mm} {appointment.Title}";
                Text(items, measurer, Fit(measurer, text, cellWidth - 12, 7, false), x + 7, lineY + 8, 7, false, CalendarPalette.PrintInk);
            }
        }
    }

    // ===== pieces =====

    private static void Block(List<DisplayItem> items, ITextMeasurer measurer, string title, string? detail, double x, double y, double width, double height, string? color)
    {
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var solid = CalendarPalette.PrintColor(color);
        items.Add(new RectangleItem(x, y, width, height, CalendarPalette.PrintTint(color)));
        items.Add(new RectangleItem(x, y, 2.5, height, solid));
        items.Add(new ClipBeginItem(x, y, width, height));
        Text(items, measurer, Fit(measurer, title, width - 8, 7.5, true), x + 5, y + Math.Min(height - 3, 9.5), 7.5, true, CalendarPalette.PrintInk);
        if (detail is not null && height >= 22)
        {
            Text(items, measurer, Fit(measurer, detail, width - 8, 7, false), x + 5, y + 19, 7, false, CalendarPalette.PrintMuted);
        }

        items.Add(new ClipEndItem());
    }

    /// <summary>Shortens a text with "…" until it fits.</summary>
    private static string Fit(ITextMeasurer measurer, string text, double width, double size, bool bold)
    {
        var font = new FontSpec(Font, size, bold);
        if (text.Length == 0 || measurer.Shape(text, font).Width <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && measurer.Shape(cut + "…", font).Width > width)
        {
            cut = cut[..^1];
        }

        return cut.Length > 1 ? cut + "…" : "";
    }

    private static void Text(List<DisplayItem> items, ITextMeasurer measurer, string text, double x, double baseline, double size, bool bold, RgbColor color, bool alignRight = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        var font = new FontSpec(Font, size, bold);
        var shaped = measurer.Shape(text.Replace('\n', ' '), font);
        var left = alignRight ? x - shaped.Width : x;
        var characterX = new double[shaped.Text.Length];
        var position = 0.0;
        for (var i = 0; i < shaped.Text.Length; i++)
        {
            characterX[i] = position;
            position += shaped.Advances[i];
        }

        if (shaped.GlyphRuns.Count == 0)
        {
            items.Add(new GlyphRunItem(left, baseline, shaped.Text, font, new FontFace(Font, bold, false), color, characterX, [], []));
            return;
        }

        foreach (var run in shaped.GlyphRuns)
        {
            items.Add(new GlyphRunItem(left, baseline, shaped.Text, font, run.Face, color, characterX, run.Glyphs, run.Positions));
        }
    }
}

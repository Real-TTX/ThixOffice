using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MatCloud.Contract.Calendar;

namespace ThixOffice.Pim.Calendar;

/// <summary>What an .ics import found: the appointments (keys from their UIDs) and how many events were series (only their first appointment comes in).</summary>
public sealed record IcsImport(IReadOnlyList<CalendarAppointment> Appointments, int Series, int Skipped);

/// <summary>
/// Appointments as an iCalendar file (.ics) and back – how data moves between ThixMail's calendar and other calendars (ThixCalendar, Outlook, Google)
/// without ThixCloud. Export: one VEVENT per appointment (UID from its key, times in UTC or as dates, place, notes, the meeting address as URL, the reminder
/// as VALARM). Import: every VEVENT with a start; the key comes from the UID, so importing the same file twice gives the same appointments, not doubles; a
/// series brings its first appointment (ThixMail's own calendars have no series); cancelled events are left out.
/// </summary>
public static class CalendarIcs
{
    public const string ProductId = "-//Thix//ThixMail Kalender//DE";

    public static string Export(IEnumerable<CalendarAppointment> appointments, string calendarName, DateTimeOffset? now = null)
    {
        var calendar = new ICalComponent("VCALENDAR").Add("PRODID", ProductId).Add("VERSION", "2.0").Add("CALSCALE", "GREGORIAN").Add("METHOD", "PUBLISH");
        calendar.AddText("X-WR-CALNAME", calendarName);
        var stamp = ICalendar.Utc(now ?? DateTimeOffset.UtcNow);
        foreach (var appointment in appointments.OrderBy(a => a.Start))
        {
            var item = new ICalComponent("VEVENT").AddText("UID", UidOf(appointment.Key)).Add("DTSTAMP", stamp);
            if (appointment.IsAllDay)
            {
                item.Add("DTSTART", ICalendar.Date(appointment.Start.UtcDateTime.Date), ("VALUE", "DATE"));
                item.Add("DTEND", ICalendar.Date(appointment.End.UtcDateTime.Date), ("VALUE", "DATE"));
            }
            else
            {
                item.Add("DTSTART", ICalendar.Utc(appointment.Start));
                item.Add("DTEND", ICalendar.Utc(appointment.End));
            }

            item.AddText("SUMMARY", appointment.Title);
            if (appointment.Location.Length > 0)
            {
                item.AddText("LOCATION", appointment.Location);
            }

            if (appointment.Description.Length > 0)
            {
                item.AddText("DESCRIPTION", appointment.Description);
            }

            if (appointment.MeetingUrl.Length > 0)
            {
                item.Add("URL", appointment.MeetingUrl);
            }

            item.Add("STATUS", appointment.Status == CalendarEventStatus.Tentative ? "TENTATIVE" : "CONFIRMED");
            if (appointment.ReminderMinutes is { } minutes)
            {
                var alarm = new ICalComponent("VALARM").Add("ACTION", "DISPLAY").AddText("DESCRIPTION", appointment.Title)
                    .Add("TRIGGER", "-PT" + minutes.ToString(CultureInfo.InvariantCulture) + "M");
                item.Children.Add(alarm);
            }

            calendar.Children.Add(item);
        }

        return ICalendar.Write(calendar);
    }

    /// <summary>The events of an .ics text as appointments of <paramref name="calendarKey"/> (not saved: the caller saves them, one undo step).</summary>
    public static IcsImport Import(string text, string calendarKey)
    {
        var result = new List<CalendarAppointment>();
        var series = 0;
        var skipped = 0;
        foreach (var calendar in ICalendar.Parse(text).Where(component => component.Name == "VCALENDAR"))
        {
            var zones = calendar.Children.Where(component => component.Name == "VTIMEZONE").ToList();
            foreach (var item in calendar.Children.Where(component => component.Name == "VEVENT"))
            {
                if (item.Property("RECURRENCE-ID") is not null)
                {
                    skipped++; // a changed occurrence of a series: the series brings its first appointment only
                    continue;
                }

                if (ICalendar.TimeOf(item.Property("DTSTART"), zones) is not { } start || "CANCELLED".Equals(item.Text("STATUS")?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                var end = ICalendar.TimeOf(item.Property("DTEND"), zones)?.Time
                    ?? (item.Text("DURATION") is { } duration && Invitations.Duration(duration) is { } length ? start.Time + length : start.IsDate ? start.Time.AddDays(1) : start.Time);
                if (item.Text("RRULE") is { Length: > 0 })
                {
                    series++;
                }

                var uid = item.Text("UID")?.Trim();
                var appointment = new CalendarAppointment
                {
                    Key = KeyOf(uid, item),
                    CalendarKey = calendarKey,
                    Title = (item.Text("SUMMARY")?.Trim() is { Length: > 0 } summary ? summary : "(Ohne Titel)") is var title && title.Length > 255 ? title[..255] : title,
                    Start = start.Time,
                    End = end < start.Time ? start.Time : end,
                    IsAllDay = start.IsDate,
                    Location = item.Text("LOCATION")?.Trim() ?? "",
                    Description = item.Text("DESCRIPTION")?.Trim() ?? "",
                    MeetingUrl = item.Text("URL")?.Trim() is { } url && MeetingLinks.IsWebAddress(url) ? url : "",
                    ReminderMinutes = ReminderOf(item),
                    Status = "TENTATIVE".Equals(item.Text("STATUS")?.Trim(), StringComparison.OrdinalIgnoreCase) ? CalendarEventStatus.Tentative : CalendarEventStatus.Confirmed,
                };
                if (appointment.End - appointment.Start > TimeSpan.FromDays(366))
                {
                    skipped++;
                    continue;
                }

                result.Add(appointment);
            }
        }

        return new IcsImport(result, series, skipped);
    }

    /// <summary>The UID an appointment is exported with: an imported one gives back its UID, every other its key at ThixMail.</summary>
    private static string UidOf(string key) => key + "@thixmail";

    private static string KeyOf(string? uid, ICalComponent item)
    {
        if (uid is { Length: > 0 })
        {
            return uid.EndsWith("@thixmail", StringComparison.Ordinal) ? uid[..^"@thixmail".Length] : CalendarBook.KeyOfInvitation(uid);
        }

        // no UID: the same event in the same file still gives the same key
        var identity = $"{item.Text("SUMMARY")}|{item.Property("DTSTART")?.Value}|{item.Property("DTEND")?.Value}";
        return "ics-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
    }

    private static int? ReminderOf(ICalComponent item)
    {
        var alarm = item.Children.FirstOrDefault(component => component.Name == "VALARM");
        if (alarm?.Text("TRIGGER") is not { } trigger || alarm.Property("TRIGGER")?["VALUE"] is "DATE-TIME")
        {
            return null;
        }

        var text = trigger.Trim();
        var before = text.StartsWith('-');
        if (Invitations.Duration(text.TrimStart('-', '+')) is not { } length || (!before && length > TimeSpan.Zero))
        {
            return null;
        }

        var minutes = (int)Math.Round(length.TotalMinutes);
        return minutes is >= 0 and <= 60 * 24 * 28 ? minutes : null;
    }
}

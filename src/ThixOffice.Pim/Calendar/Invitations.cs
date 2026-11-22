using System.Globalization;
using MatCloud.Contract.Calendar;

namespace ThixOffice.Pim.Calendar;

/// <summary>What an iTIP message asks (RFC 5546).</summary>
public enum InvitationMethod
{
    Other,
    Request,
    Reply,
    Cancel,
    Publish,
}

/// <summary>The answer to an invitation: Zusagen, Mit Vorbehalt, Absagen.</summary>
public enum InvitationAnswer
{
    Accept,
    Tentative,
    Decline,
}

/// <summary>A participant of a meeting: address, name, how they answered (PARTSTAT), whether an answer is asked for.</summary>
public sealed record InvitationAttendee(string Email, string Name, string Status = "NEEDS-ACTION", bool Rsvp = true);

/// <summary>
/// A meeting as an invitation carries it: method, UID, sequence, title, times, place, text, organizer, participants, the series rule (kept as text:
/// ThixCalendar's own calendars have no series – only the first meeting is entered).
/// </summary>
public sealed record Invitation
{
    public InvitationMethod Method { get; init; }

    public string Uid { get; init; } = "";

    public int Sequence { get; init; }

    public string Summary { get; init; } = "";

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public bool IsAllDay { get; init; }

    public string Location { get; init; } = "";

    public string Description { get; init; } = "";

    public InvitationAttendee Organizer { get; init; } = new("", "");

    public IReadOnlyList<InvitationAttendee> Attendees { get; init; } = [];

    public string? RecurrenceRule { get; init; }

    public bool IsRecurring => !string.IsNullOrEmpty(RecurrenceRule);

    /// <summary>The participant with <paramref name="email"/>, or null.</summary>
    public InvitationAttendee? Attendee(string email) => Attendees.FirstOrDefault(attendee => attendee.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Invitations by mail (iMIP): reading a text/calendar part, the appointment an answer enters into the calendar, and the iCalendar texts ThixMail sends
/// – REPLY (the answer to the organizer), REQUEST (a meeting made in ThixMail's calendar) and CANCEL (a meeting called off). BCL only.
/// </summary>
public static class Invitations
{
    public const string ProductId = "-//Thix//ThixMail//DE";

    /// <summary>The first meeting of an iCalendar text, or null when it holds none (or no UID / start).</summary>
    public static Invitation? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var calendar = ICalendar.Parse(text).FirstOrDefault(component => component.Name == "VCALENDAR");
        var meeting = calendar?.Children.FirstOrDefault(component => component.Name == "VEVENT");
        if (calendar is null || meeting is null || meeting.Text("UID") is not { Length: > 0 } uid)
        {
            return null;
        }

        var zones = calendar.Children.Where(component => component.Name == "VTIMEZONE").ToList();
        if (ICalendar.TimeOf(meeting.Property("DTSTART"), zones) is not { } start)
        {
            return null;
        }

        var end = ICalendar.TimeOf(meeting.Property("DTEND"), zones)?.Time
            ?? (meeting.Text("DURATION") is { } duration && Duration(duration) is { } length ? start.Time + length : start.IsDate ? start.Time.AddDays(1) : start.Time);
        return new Invitation
        {
            Method = calendar.Text("METHOD")?.Trim().ToUpperInvariant() switch
            {
                "REQUEST" => InvitationMethod.Request,
                "REPLY" => InvitationMethod.Reply,
                "CANCEL" => InvitationMethod.Cancel,
                "PUBLISH" => InvitationMethod.Publish,
                _ => InvitationMethod.Other,
            },
            Uid = uid.Trim(),
            Sequence = int.TryParse(meeting.Text("SEQUENCE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) ? sequence : 0,
            Summary = meeting.Text("SUMMARY")?.Trim() ?? "",
            Start = start.Time,
            End = end < start.Time ? start.Time : end,
            IsAllDay = start.IsDate,
            Location = meeting.Text("LOCATION")?.Trim() ?? "",
            Description = meeting.Text("DESCRIPTION")?.Trim() ?? "",
            Organizer = Person(meeting.Property("ORGANIZER")) ?? new InvitationAttendee("", ""),
            Attendees = [.. meeting.All("ATTENDEE").Select(Person).OfType<InvitationAttendee>()],
            RecurrenceRule = meeting.Text("RRULE"),
        };
    }

    /// <summary>
    /// The appointment an answer enters (Zusagen: confirmed, Mit Vorbehalt: tentative) into <paramref name="calendarKey"/>; its key comes from the UID, so the
    /// same meeting is one appointment on every computer. A series enters its first meeting.
    /// </summary>
    public static CalendarAppointment AppointmentOf(Invitation invitation, InvitationAnswer answer, string calendarKey, int? reminderMinutes = 15)
    {
        var description = invitation.Description;
        if (invitation.Organizer.Email.Length > 0)
        {
            var organizer = invitation.Organizer.Name.Length > 0 ? $"{invitation.Organizer.Name} <{invitation.Organizer.Email}>" : invitation.Organizer.Email;
            description = (description.Length > 0 ? description + "\n\n" : "") + "Organisator: " + organizer;
        }

        return new CalendarAppointment
        {
            Key = CalendarBook.KeyOfInvitation(invitation.Uid),
            CalendarKey = calendarKey,
            Title = invitation.Summary.Length > 0 ? invitation.Summary : "Besprechung",
            Start = invitation.Start,
            End = invitation.End,
            IsAllDay = invitation.IsAllDay,
            Location = invitation.Location,
            Description = description,
            ReminderMinutes = invitation.IsAllDay ? null : reminderMinutes,
            Status = answer == InvitationAnswer.Tentative ? CalendarEventStatus.Tentative : CalendarEventStatus.Confirmed,
        };
    }

    /// <summary>The answer to the organizer (METHOD:REPLY): the meeting's UID, sequence and times, the one participant with their PARTSTAT.</summary>
    public static string Reply(Invitation invitation, string email, string name, InvitationAnswer answer, DateTimeOffset? now = null)
    {
        var meeting = new ICalComponent("VEVENT")
            .AddText("UID", invitation.Uid)
            .Add("SEQUENCE", invitation.Sequence.ToString(CultureInfo.InvariantCulture))
            .Add("DTSTAMP", ICalendar.Utc(now ?? DateTimeOffset.UtcNow));
        AddTimes(meeting, invitation);
        meeting.AddText("SUMMARY", invitation.Summary);
        if (invitation.Organizer.Email.Length > 0)
        {
            meeting.Add("ORGANIZER", "mailto:" + invitation.Organizer.Email, Named(invitation.Organizer.Name));
        }

        meeting.Add("ATTENDEE", "mailto:" + email, [.. Named(name), ("PARTSTAT", PartStat(answer))]);
        return Calendar("REPLY", meeting);
    }

    /// <summary>A meeting made in ThixMail (METHOD:REQUEST): organizer, participants (each asked for an answer), times, place, text.</summary>
    public static string Request(Invitation meeting, DateTimeOffset? now = null)
    {
        var component = Meeting(meeting, now);
        component.Add("STATUS", "CONFIRMED");
        return Calendar("REQUEST", component);
    }

    /// <summary>A meeting called off (METHOD:CANCEL; the sequence one higher).</summary>
    public static string Cancel(Invitation meeting, DateTimeOffset? now = null)
    {
        var component = Meeting(meeting with { Sequence = meeting.Sequence + 1 }, now);
        component.Add("STATUS", "CANCELLED");
        return Calendar("CANCEL", component);
    }

    /// <summary>The words for a participation state in German ("Zugesagt", "Mit Vorbehalt" …).</summary>
    public static string StatusText(string status) => status.ToUpperInvariant() switch
    {
        "ACCEPTED" => "Zugesagt",
        "TENTATIVE" => "Mit Vorbehalt",
        "DECLINED" => "Abgesagt",
        "DELEGATED" => "Weitergeleitet",
        _ => "Keine Antwort",
    };

    public static string PartStat(InvitationAnswer answer) => answer switch
    {
        InvitationAnswer.Accept => "ACCEPTED",
        InvitationAnswer.Tentative => "TENTATIVE",
        _ => "DECLINED",
    };

    /// <summary>The subject of an answer as Outlook writes it ("Zugesagt: Projektbesprechung").</summary>
    public static string ReplySubject(Invitation invitation, InvitationAnswer answer) => answer switch
    {
        InvitationAnswer.Accept => "Zugesagt: ",
        InvitationAnswer.Tentative => "Mit Vorbehalt angenommen: ",
        _ => "Abgelehnt: ",
    } + invitation.Summary;

    private static ICalComponent Meeting(Invitation meeting, DateTimeOffset? now)
    {
        var component = new ICalComponent("VEVENT")
            .AddText("UID", meeting.Uid)
            .Add("SEQUENCE", meeting.Sequence.ToString(CultureInfo.InvariantCulture))
            .Add("DTSTAMP", ICalendar.Utc(now ?? DateTimeOffset.UtcNow));
        AddTimes(component, meeting);
        component.AddText("SUMMARY", meeting.Summary);
        if (meeting.Location.Length > 0)
        {
            component.AddText("LOCATION", meeting.Location);
        }

        if (meeting.Description.Length > 0)
        {
            component.AddText("DESCRIPTION", meeting.Description);
        }

        component.Add("ORGANIZER", "mailto:" + meeting.Organizer.Email, Named(meeting.Organizer.Name));
        foreach (var attendee in meeting.Attendees)
        {
            component.Add("ATTENDEE", "mailto:" + attendee.Email, [.. Named(attendee.Name), ("ROLE", "REQ-PARTICIPANT"), ("PARTSTAT", "NEEDS-ACTION"), ("RSVP", "TRUE")]);
        }

        return component;
    }

    private static void AddTimes(ICalComponent component, Invitation meeting)
    {
        if (meeting.IsAllDay)
        {
            component.Add("DTSTART", ICalendar.Date(meeting.Start.UtcDateTime.Date), ("VALUE", "DATE"));
            component.Add("DTEND", ICalendar.Date(meeting.End.UtcDateTime.Date), ("VALUE", "DATE"));
        }
        else
        {
            component.Add("DTSTART", ICalendar.Utc(meeting.Start));
            component.Add("DTEND", ICalendar.Utc(meeting.End));
        }
    }

    private static string Calendar(string method, ICalComponent meeting)
    {
        var calendar = new ICalComponent("VCALENDAR").Add("PRODID", ProductId).Add("VERSION", "2.0").Add("CALSCALE", "GREGORIAN").Add("METHOD", method);
        calendar.Children.Add(meeting);
        return ICalendar.Write(calendar);
    }

    private static (string Name, string Value)[] Named(string name) => name.Length > 0 ? [("CN", name)] : [];

    private static InvitationAttendee? Person(ICalProperty? property)
    {
        if (property is null)
        {
            return null;
        }

        var value = property.Value.Trim();
        var email = value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
        if (email.Length == 0)
        {
            return null;
        }

        return new InvitationAttendee(email.Trim(), (property["CN"] ?? "").Trim('"', ' '), (property["PARTSTAT"] ?? "NEEDS-ACTION").ToUpperInvariant(),
            !string.Equals(property["RSVP"], "FALSE", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An iCalendar duration ("PT1H30M", "P1D").</summary>
    private static TimeSpan? Duration(string text)
    {
        var value = text.Trim().ToUpperInvariant();
        var negative = value.StartsWith('-');
        value = value.TrimStart('+', '-');
        if (!value.StartsWith('P'))
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var number = "";
        var inTime = false;
        foreach (var character in value[1..])
        {
            if (char.IsAsciiDigit(character))
            {
                number += character;
                continue;
            }

            if (character == 'T')
            {
                inTime = true;
                continue;
            }

            if (!int.TryParse(number, CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            total += character switch
            {
                'W' => TimeSpan.FromDays(7 * amount),
                'D' => TimeSpan.FromDays(amount),
                'H' when inTime => TimeSpan.FromHours(amount),
                'M' when inTime => TimeSpan.FromMinutes(amount),
                'S' when inTime => TimeSpan.FromSeconds(amount),
                _ => TimeSpan.Zero,
            };
            number = "";
        }

        return negative ? -total : total;
    }
}

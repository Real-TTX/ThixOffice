using MatCloud.Contract.Calendar;

namespace ThixOffice.Pim.Calendar;

/// <summary>
/// An account of the calendar as ThixCalendar keeps it in the general sync (collection accounts, <see cref="CalendarAccountItem"/>): its SyncId, its
/// kind, its name, shown or hidden and who keeps it in step. <see cref="IsOwn"/>: ThixCalendar's own calendars ("Eigene Kalender") – the only ones
/// whose events are items everyone writes. <see cref="IsKeptByCloud"/>: an online account MatCloud keeps in step (its events come as cloud-events).
/// </summary>
public sealed record CalendarAccountEntry(string SyncId, CalendarAccountKind Kind, string Name, bool IsVisible, CalendarSyncMode? SyncMode)
{
    public bool IsOwn => Kind == CalendarAccountKind.Local;

    public bool IsKeptByCloud => Kind != CalendarAccountKind.Local && SyncMode == CalendarSyncMode.Cloud;
}

/// <summary>
/// A calendar to show: its key (<see cref="CalendarContract.CalendarKey"/>), its account, name, colour ("#RRGGBB" – the user's data), shown or not,
/// own (events can be written) or read-only (a calendar MatCloud keeps in step: shown, changed only in ThixCalendar or at its provider).
/// </summary>
public sealed record CalendarEntry(
    string Key, string AccountSyncId, string AccountName, string Name, string Color, bool IsVisible, bool IsOwn, bool IsReadOnly, int? DefaultReminderMinutes,
    int SortOrder);

/// <summary>
/// An appointment as ThixMail shows it: an event of an own calendar (collection events, <see cref="CalendarEventItem"/>) or an occurrence MatCloud
/// keeps (cloud-events, <see cref="CalendarCloudEvent"/>, read-only). Times are absolute; an all-day appointment has its dates at midnight UTC and
/// <see cref="End"/> is the midnight after its last day (as ThixCalendar and MatCloud write them).
/// </summary>
public sealed record CalendarAppointment
{
    /// <summary>The key of its item (own events: the event's key; cloud: <see cref="CalendarContract.EventKey"/>); empty for a new one.</summary>
    public string Key { get; init; } = "";

    public string CalendarKey { get; init; } = "";

    public string Title { get; init; } = "";

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public bool IsAllDay { get; init; }

    public string Location { get; init; } = "";

    public string Description { get; init; } = "";

    public string MeetingUrl { get; init; } = "";

    public int? ReminderMinutes { get; init; }

    public CalendarEventStatus Status { get; init; }

    /// <summary>An occurrence of a calendar MatCloud keeps in step: shown, not changed here.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Part of a series (only known for events MatCloud keeps; ThixCalendar's own events have no series).</summary>
    public bool IsRecurring { get; init; }

    public string Organizer { get; init; } = "";

    public CalendarResponse Response { get; init; }

    /// <summary>The first day it shows on (all-day: its date; else the local date of its start).</summary>
    public DateTime FirstDay => IsAllDay ? Start.UtcDateTime.Date : Start.ToLocalTime().Date;

    /// <summary>The day after the last day it shows on.</summary>
    public DateTime EndDay => IsAllDay ? End.UtcDateTime.Date : (End.ToLocalTime().TimeOfDay == TimeSpan.Zero && End > Start ? End.ToLocalTime().Date : End.ToLocalTime().Date.AddDays(1));

    /// <summary>Start and end in the local wall clock (all-day: the dates).</summary>
    public DateTime StartLocal => IsAllDay ? DateTime.SpecifyKind(Start.UtcDateTime.Date, DateTimeKind.Local) : Start.ToLocalTime().DateTime;

    public DateTime EndLocal => IsAllDay ? DateTime.SpecifyKind(End.UtcDateTime.Date, DateTimeKind.Local) : End.ToLocalTime().DateTime;

    /// <summary>True when it shows on <paramref name="day"/> (local date).</summary>
    public bool Touches(DateTime day)
    {
        var after = EndDay > FirstDay ? EndDay : FirstDay.AddDays(1);
        return FirstDay <= day.Date && day.Date < after;
    }

    /// <summary>True when it overlaps [<paramref name="from"/>, <paramref name="to"/>) (absolute times).</summary>
    public bool Overlaps(DateTimeOffset from, DateTimeOffset to) => Start < to && (End > from || (End == Start && Start >= from));

    /// <summary>The date of a local day as the start of an all-day appointment (midnight UTC).</summary>
    public static DateTimeOffset AllDayDate(DateTime date) => new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A local wall-clock time as an absolute time.</summary>
    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }
}

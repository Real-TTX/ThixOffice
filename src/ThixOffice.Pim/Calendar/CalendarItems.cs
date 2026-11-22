using System.Text.Json;
using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Calendar;

/// <summary>
/// ThixCalendar's items of the general sync (app <c>calendar</c>, reached by ThixMail through the grant <c>?app=calendar</c>) read from a store and
/// written into it. The shapes are the contract's (<see cref="CalendarContract"/>) serialized with <see cref="CloudContract.Json"/> – byte for byte
/// what ThixCalendar writes for the same values (it compares items by their hash: anything else would make it write the item back). Unreadable items
/// are left out, never thrown. Only own calendars (accounts of kind Local) and their events are written; the collections cloud-* are MatCloud's.
/// </summary>
public static class CalendarItems
{
    /// <summary>The colours ThixCalendar gives new calendars, in its order (user data: the calendar's colour travels with it).</summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#2F6FED", "#F97316", "#16A34A", "#9333EA", "#DC2626", "#0EA5E9", "#EAB308", "#DB2777", "#14B8A6", "#64748B",
    ];

    public static IReadOnlyList<CalendarAccountEntry> Accounts(PimStore store)
        => [.. store.Items(CalendarContract.Accounts)
            .Select(item => Read<CalendarAccountItem>(item.Data) is { } data ? new CalendarAccountEntry(item.Key, data.Kind, data.Name ?? "", data.IsVisible, data.SyncMode) : null)
            .OfType<CalendarAccountEntry>()];

    /// <summary>
    /// The calendars to show: the own calendars (collection calendars of a Local account) and those MatCloud keeps in step (cloud-calendars, with the
    /// user's choices of colour and visibility from calendars). Calendars of online accounts each computer keeps itself have no events in the cloud and
    /// are not listed.
    /// </summary>
    public static IReadOnlyList<CalendarEntry> Calendars(PimStore store)
    {
        var accounts = Accounts(store).ToDictionary(account => account.SyncId, StringComparer.Ordinal);
        var choices = store.Items(CalendarContract.Calendars)
            .Select(item => (item.Key, Data: Read<CalendarCalendarItem>(item.Data)))
            .Where(pair => pair.Data is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Data!, StringComparer.Ordinal);
        var result = new List<CalendarEntry>();
        foreach (var (key, data) in choices)
        {
            if (accounts.TryGetValue(AccountOf(key), out var account) && account.IsOwn)
            {
                result.Add(new CalendarEntry(key, account.SyncId, account.Name, data.Name ?? "", ValidColor(data.Color) ?? Palette[result.Count % Palette.Count],
                    data.IsVisible && account.IsVisible, IsOwn: true, IsReadOnly: false, data.DefaultReminderMinutes, data.SortOrder));
            }
        }

        foreach (var item in store.Items(CalendarContract.CloudCalendars))
        {
            if (Read<CalendarCloudCalendar>(item.Data) is not { } cloud || !accounts.TryGetValue(cloud.Account ?? AccountOf(item.Key), out var account))
            {
                continue;
            }

            choices.TryGetValue(item.Key, out var choice);
            result.Add(new CalendarEntry(item.Key, account.SyncId, account.Name, cloud.Name ?? "", ValidColor(choice?.Color) ?? ValidColor(cloud.Color) ?? Palette[result.Count % Palette.Count],
                (choice?.IsVisible ?? true) && account.IsVisible, IsOwn: false, IsReadOnly: true, cloud.DefaultReminderMinutes, 1000 + result.Count));
        }

        return [.. result.OrderBy(calendar => calendar.IsOwn ? 0 : 1).ThenBy(calendar => calendar.SortOrder).ThenBy(calendar => calendar.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Every appointment of the calendars <paramref name="calendars"/> (own events and MatCloud's occurrences).</summary>
    public static IReadOnlyList<CalendarAppointment> Appointments(PimStore store, IReadOnlyCollection<CalendarEntry> calendars)
    {
        var known = calendars.ToDictionary(calendar => calendar.Key, StringComparer.Ordinal);
        var result = new List<CalendarAppointment>();
        foreach (var item in store.Items(CalendarContract.Events))
        {
            if (Read<CalendarEventItem>(item.Data) is { } data && known.TryGetValue(data.Calendar ?? "", out var calendar) && calendar.IsOwn)
            {
                result.Add(FromItem(item.Key, data));
            }
        }

        foreach (var item in store.Items(CalendarContract.CloudEvents))
        {
            if (Read<CalendarCloudEvent>(item.Data) is { } cloud && known.ContainsKey(cloud.Calendar ?? ""))
            {
                result.Add(new CalendarAppointment
                {
                    Key = item.Key,
                    CalendarKey = cloud.Calendar ?? "",
                    Title = cloud.Title ?? "",
                    Start = cloud.Start,
                    End = cloud.End,
                    IsAllDay = cloud.IsAllDay,
                    Location = cloud.Location ?? "",
                    Description = cloud.Description ?? "",
                    MeetingUrl = cloud.MeetingUrl ?? "",
                    ReminderMinutes = cloud.ReminderMinutes,
                    Status = cloud.Status,
                    IsReadOnly = true,
                    IsRecurring = cloud.IsRecurring,
                    Organizer = cloud.Organizer ?? "",
                    Response = cloud.Response,
                });
            }
        }

        return result;
    }

    /// <summary>An own event as its item's data (the contract's <see cref="CalendarEventItem"/>, times in UTC as ThixCalendar writes them).</summary>
    public static string Serialize(CalendarAppointment appointment)
        => JsonSerializer.Serialize(new CalendarEventItem(appointment.CalendarKey, appointment.Title, appointment.Start.ToUniversalTime(), appointment.End.ToUniversalTime(),
            appointment.IsAllDay, appointment.Location, appointment.Description, appointment.MeetingUrl, appointment.ReminderMinutes, appointment.Status), CloudContract.Json);

    public static string Serialize(CalendarAccountItem account) => JsonSerializer.Serialize(account, CloudContract.Json);

    public static string Serialize(CalendarCalendarItem calendar) => JsonSerializer.Serialize(calendar, CloudContract.Json);

    internal static CalendarAppointment FromItem(string key, CalendarEventItem data) => new()
    {
        Key = key,
        CalendarKey = data.Calendar ?? "",
        Title = data.Title ?? "",
        Start = data.Start,
        End = data.End,
        IsAllDay = data.IsAllDay,
        Location = data.Location ?? "",
        Description = data.Description ?? "",
        MeetingUrl = data.MeetingUrl ?? "",
        ReminderMinutes = data.ReminderMinutes,
        Status = data.Status,
    };

    /// <summary>The SyncId of the account a calendar key names ("&lt;account&gt;/&lt;hash&gt;").</summary>
    public static string AccountOf(string calendarKey)
    {
        var slash = calendarKey.IndexOf('/');
        return slash > 0 ? calendarKey[..slash] : calendarKey;
    }

    /// <summary>"#RRGGBB" in upper case, or null for anything else.</summary>
    public static string? ValidColor(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit) ? text.ToUpperInvariant() : null;
    }

    internal static T? Read<T>(string? data) where T : class
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(data, CloudContract.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

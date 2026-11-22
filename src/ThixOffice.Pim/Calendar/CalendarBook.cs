using System.Security.Cryptography;
using System.Text;
using MatCloud.Contract.Calendar;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Calendar;

/// <summary>
/// The calendar of a ThixCloud account in a PIM app's store (ThixMail's view "Kalender"): the calendars and appointments read from ThixCalendar's
/// items, cached until the store changes, and the changes ThixMail may make – appointments of own calendars (new, changed, deleted: items of
/// collection events, synced by <c>PimSync</c> with <c>?app=calendar</c>), an own calendar made when the account has none (an account of kind Local
/// "Eigene Kalender" with a calendar "Kalender", as ThixCalendar makes them), the answers to invitations. Validation is ThixCalendar's (title, times).
/// Thread: the UI's; the store's own lock covers the sync loop.
/// </summary>
public sealed class CalendarBook : IDisposable
{
    private IReadOnlyList<CalendarEntry>? _calendars;
    private IReadOnlyList<CalendarAppointment>? _appointments;

    public CalendarBook(PimStore store)
    {
        Store = store;
        Store.RemoteChanged += OnRemoteChanged;
    }

    public PimStore Store { get; }

    /// <summary>The calendars or appointments changed (here or by the sync – then on the sync's thread).</summary>
    public event EventHandler? Changed;

    /// <summary>A change of this computer was written (the sync takes it up at once).</summary>
    public event EventHandler? LocalChanged;

    public IReadOnlyList<CalendarEntry> Calendars => _calendars ??= CalendarItems.Calendars(Store);

    /// <summary>The calendars appointments can be written into (own calendars).</summary>
    public IReadOnlyList<CalendarEntry> WritableCalendars => [.. Calendars.Where(calendar => calendar.IsOwn)];

    public IReadOnlyList<CalendarAppointment> Appointments => _appointments ??= CalendarItems.Appointments(Store, Calendars);

    public CalendarEntry? Calendar(string? key) => Calendars.FirstOrDefault(calendar => calendar.Key == key);

    public CalendarAppointment? Find(string? key) => key is null ? null : Appointments.FirstOrDefault(appointment => appointment.Key == key);

    /// <summary>The appointments of the shown calendars that touch the local days [<paramref name="from"/>, <paramref name="to"/>), by start.</summary>
    public IReadOnlyList<CalendarAppointment> Between(DateTime from, DateTime to)
    {
        var shown = Calendars.Where(calendar => calendar.IsVisible).Select(calendar => calendar.Key).ToHashSet(StringComparer.Ordinal);
        return [.. Appointments
            .Where(appointment => shown.Contains(appointment.CalendarKey) && appointment.Status != CalendarEventStatus.Cancelled)
            .Where(appointment => appointment.FirstDay < to.Date && (appointment.EndDay > from.Date || appointment.FirstDay >= from.Date))
            .OrderBy(appointment => appointment.IsAllDay ? 0 : 1).ThenBy(appointment => appointment.Start).ThenBy(appointment => appointment.Title, StringComparer.CurrentCulture)];
    }

    /// <summary>The appointments of the shown calendars that overlap [<paramref name="start"/>, <paramref name="end"/>) – except <paramref name="exceptKey"/>.</summary>
    public IReadOnlyList<CalendarAppointment> Conflicts(DateTimeOffset start, DateTimeOffset end, string? exceptKey = null)
    {
        var shown = Calendars.Where(calendar => calendar.IsVisible).Select(calendar => calendar.Key).ToHashSet(StringComparer.Ordinal);
        return [.. Appointments
            .Where(appointment => appointment.Key != exceptKey && shown.Contains(appointment.CalendarKey) && appointment.Status != CalendarEventStatus.Cancelled)
            .Where(appointment => appointment.Start < end && appointment.End > start)
            .OrderBy(appointment => appointment.Start)];
    }

    /// <summary>The calendar new appointments go into: the first own calendar (made when there is none).</summary>
    public CalendarEntry DefaultCalendar() => WritableCalendars.FirstOrDefault() ?? EnsureOwnCalendar();

    /// <summary>
    /// True while the store has not yet been in step with the cloud once (set by the app whose store syncs): no own calendar is made then – the account's
    /// own calendars of ThixCalendar may still be on their way, and a second "Eigene Kalender" would appear in ThixCalendar on every computer.
    /// </summary>
    public Func<bool>? AwaitsFirstSync { get; init; }

    /// <summary>An own calendar: the first there is, or a new account "Eigene Kalender" with a calendar "Kalender" (both synced like ThixCalendar's).</summary>
    public CalendarEntry EnsureOwnCalendar()
    {
        if (WritableCalendars.FirstOrDefault() is { } existing)
        {
            return existing;
        }

        if (AwaitsFirstSync?.Invoke() == true)
        {
            throw new BusinessException("Der Kalender Ihres ThixCloud-Kontos wird noch zum ersten Mal abgeglichen. Bitte versuchen Sie es gleich noch einmal.");
        }

        var accounts = CalendarItems.Accounts(Store);
        var account = accounts.FirstOrDefault(candidate => candidate.IsOwn);
        var changes = new List<(string, string, string?)>();
        var accountId = account?.SyncId ?? NewKey();
        if (account is null)
        {
            changes.Add((CalendarContract.Accounts, accountId, CalendarItems.Serialize(new CalendarAccountItem(CalendarAccountKind.Local, "Eigene Kalender", "", "", true))));
        }

        var remoteId = NewKey();
        var key = CalendarContract.CalendarKey(accountId, remoteId);
        changes.Add((CalendarContract.Calendars, key, CalendarItems.Serialize(new CalendarCalendarItem(remoteId, "Kalender", CalendarItems.Palette[0], true, true, null, 0))));
        Store.Write(changes);
        Invalidate(local: true);
        return Calendar(key)!;
    }

    /// <summary>Another own calendar ("Privat", "Sport" …) in the own account (made when there is none), its colour the next of ThixCalendar's palette.</summary>
    public CalendarEntry AddOwnCalendar(string name, string? color = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie dem Kalender einen Namen.", nameof(CalendarEntry.Name));
        }

        var accountId = EnsureOwnCalendar().AccountSyncId;
        var remoteId = NewKey();
        var key = CalendarContract.CalendarKey(accountId, remoteId);
        var count = WritableCalendars.Count;
        Store.Write([(CalendarContract.Calendars, key, CalendarItems.Serialize(new CalendarCalendarItem(remoteId, trimmed,
            CalendarItems.ValidColor(color) ?? CalendarItems.Palette[count % CalendarItems.Palette.Count], true, true, null, count)))]);
        Invalidate(local: true);
        return Calendar(key)!;
    }

    /// <summary>
    /// Saves an appointment of an own calendar (a new one gets a key: <paramref name="appointment"/>'s Key empty) after ThixCalendar's checks; all-day
    /// dates are kept at midnight UTC. Answers the saved appointment.
    /// </summary>
    public CalendarAppointment Save(CalendarAppointment appointment)
    {
        var calendar = Calendar(appointment.CalendarKey) ?? throw new BusinessException("Bitte wählen Sie einen Kalender.", nameof(CalendarAppointment.CalendarKey));
        if (!calendar.IsOwn)
        {
            throw new BusinessException("Termine dieses Kalenders ändern Sie in ThixCalendar oder bei seinem Anbieter.", nameof(CalendarAppointment.CalendarKey));
        }

        if (appointment.Key.Length > 0 && Find(appointment.Key) is { IsReadOnly: true })
        {
            throw new BusinessException("Dieser Termin kann hier nicht geändert werden.");
        }

        var normalized = Normalize(appointment);
        Validate(normalized);
        var saved = normalized with { Key = normalized.Key.Length > 0 ? normalized.Key : NewKey(), IsReadOnly = false };
        Store.Write([(MatCloud.Contract.Calendar.CalendarContract.Events, saved.Key, CalendarItems.Serialize(saved))]);
        Invalidate(local: true);
        return Find(saved.Key) ?? saved;
    }

    /// <summary>Deletes an appointment of an own calendar (a tombstone goes up). False when there is none with the key.</summary>
    public bool Delete(string key)
    {
        if (Find(key) is not { } appointment)
        {
            return false;
        }

        if (appointment.IsReadOnly)
        {
            throw new BusinessException("Dieser Termin kann hier nicht gelöscht werden – er gehört zu einem Kalender, den ThixCalendar über die ThixCloud abgleicht.");
        }

        Store.Write([(CalendarContract.Events, key, null)]);
        Invalidate(local: true);
        return true;
    }

    /// <summary>
    /// A meeting this computer sent the invitations of (its UID, sequence and participants – ThixCalendar's events carry no participants, so this stays
    /// on this computer, in the store's meta values): an update or a cancellation reaches the same people.
    /// </summary>
    public void RememberMeeting(string key, Invitation meeting) =>
        Store.SetMeta("meeting:" + key, System.Text.Json.JsonSerializer.Serialize(meeting, MatCloud.Contract.CloudContract.Json));

    /// <summary>The meeting remembered for an appointment, or null.</summary>
    public Invitation? MeetingOf(string key) => CalendarItems.Read<Invitation>(Store.GetMeta("meeting:" + key));

    public void ForgetMeeting(string key) => Store.SetMeta("meeting:" + key, "");

    /// <summary>Reads everything again (after a change of the store).</summary>
    public void Invalidate(bool local = false)
    {
        _calendars = null;
        _appointments = null;
        if (local)
        {
            LocalChanged?.Invoke(this, EventArgs.Empty);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ThixCalendar's checks of an event: a title of at most 255 characters, the end not before the start, at most a year long.</summary>
    public static void Validate(CalendarAppointment appointment)
    {
        if (appointment.Title.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie einen Titel ein.", nameof(CalendarAppointment.Title));
        }

        if (appointment.Title.Length > 255)
        {
            throw new BusinessException("Der Titel ist zu lang (höchstens 255 Zeichen).", nameof(CalendarAppointment.Title));
        }

        if (appointment.IsAllDay ? appointment.End <= appointment.Start : appointment.End < appointment.Start)
        {
            throw new BusinessException(appointment.IsAllDay ? "Der letzte Tag liegt vor dem ersten." : "Das Ende liegt vor dem Beginn.", nameof(CalendarAppointment.End));
        }

        if (appointment.End - appointment.Start > TimeSpan.FromDays(366))
        {
            throw new BusinessException("Ein Termin dauert höchstens ein Jahr.", nameof(CalendarAppointment.End));
        }

        if (appointment.ReminderMinutes is < 0 or > 60 * 24 * 28)
        {
            throw new BusinessException("Die Erinnerung liegt höchstens vier Wochen vor dem Termin.", nameof(CalendarAppointment.ReminderMinutes));
        }

        if (appointment.MeetingUrl.Length > 0 && !MeetingLinks.IsWebAddress(appointment.MeetingUrl))
        {
            throw new BusinessException("Der Besprechungslink muss eine Webadresse sein (https://…).", nameof(CalendarAppointment.MeetingUrl));
        }
    }

    /// <summary>
    /// Trimmed texts, all-day dates at midnight UTC, the meeting address from location or notes when there is none (ThixCalendar's Normalize: what it
    /// would store for the same appointment, so it never writes ThixMail's appointments back).
    /// </summary>
    public static CalendarAppointment Normalize(CalendarAppointment appointment)
    {
        var normalized = appointment with
        {
            Title = appointment.Title.Trim(),
            Location = appointment.Location.Trim(),
            Description = appointment.Description.TrimEnd(),
            MeetingUrl = appointment.MeetingUrl.Trim(),
        };
        if (normalized.IsAllDay)
        {
            normalized = normalized with { Start = CalendarAppointment.AllDayDate(normalized.Start.UtcDateTime.Date), End = CalendarAppointment.AllDayDate(normalized.End.UtcDateTime.Date) };
        }

        return normalized.MeetingUrl.Length > 0 ? normalized : normalized with { MeetingUrl = MeetingLinks.Find(normalized.Location, normalized.Description) };
    }

    /// <summary>A new key as ThixCalendar makes them (a GUID without dashes).</summary>
    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>The key of the appointment an invitation becomes: the same on every computer (an answer given twice is one appointment).</summary>
    public static string KeyOfInvitation(string uid) => "ical-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uid)))[..32];

    public void Dispose() => Store.RemoteChanged -= OnRemoteChanged;

    private void OnRemoteChanged(object? sender, IReadOnlyCollection<string> collections) => Invalidate();
}

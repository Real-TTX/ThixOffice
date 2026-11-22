using System.Globalization;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Calendar;

/// <summary>A reminder that is due: the appointment and the moment its reminder came (start − lead time).</summary>
public sealed record DueReminder(CalendarAppointment Appointment, DateTimeOffset Trigger);

/// <summary>
/// The reminders of the calendar as ThixMail shows them while it runs (ThixCalendar's way): an appointment with a lead time reminds from start − lead
/// until it ends; "Schließen" ends the reminder of that time, "Erneut erinnern" waits until the chosen moment. What was answered is kept on this computer
/// (meta values of the calendar's store, never synced) and refers to the trigger: an appointment moved to another time reminds again.
/// </summary>
public static class CalendarReminders
{
    /// <summary>The choices of "Erneut erinnern" (Outlook's): in 5, 10, 15 minutes, 1 hour; before the start: 5 minutes before it.</summary>
    public static IReadOnlyList<(string Label, TimeSpan Delay)> SnoozeChoices { get; } =
    [
        ("5 Minuten", TimeSpan.FromMinutes(5)),
        ("10 Minuten", TimeSpan.FromMinutes(10)),
        ("15 Minuten", TimeSpan.FromMinutes(15)),
        ("1 Stunde", TimeSpan.FromHours(1)),
    ];

    /// <summary>The due reminders at <paramref name="now"/>, the earliest start first.</summary>
    public static IReadOnlyList<DueReminder> Due(IEnumerable<CalendarAppointment> appointments, DateTimeOffset now, PimStore store)
    {
        var due = new List<DueReminder>();
        foreach (var appointment in appointments)
        {
            if (appointment.ReminderMinutes is not { } minutes || minutes < 0 || appointment.Status == MatCloud.Contract.Calendar.CalendarEventStatus.Cancelled)
            {
                continue;
            }

            var start = appointment.IsAllDay ? CalendarAppointment.FromLocal(appointment.StartLocal) : appointment.Start;
            var end = appointment.IsAllDay ? CalendarAppointment.FromLocal(appointment.EndLocal) : (appointment.End > appointment.Start ? appointment.End : appointment.Start.AddMinutes(30));
            var trigger = start.AddMinutes(-minutes);
            if (trigger > now || now >= end)
            {
                continue;
            }

            var (dismissed, snoozedUntil, snoozedTrigger) = StateOf(store, appointment.Key);
            if (dismissed == Ticks(trigger) || (snoozedTrigger == Ticks(trigger) && Parse(snoozedUntil ?? "") > now))
            {
                continue;
            }

            due.Add(new DueReminder(appointment, trigger));
        }

        return [.. due.OrderBy(d => d.Appointment.Start)];
    }

    /// <summary>"Schließen": this reminder (of this trigger) does not come again.</summary>
    public static void Dismiss(PimStore store, DueReminder reminder) =>
        store.SetMeta(Key(reminder.Appointment.Key), "dismissed:" + Ticks(reminder.Trigger));

    /// <summary>"Erneut erinnern": again at <paramref name="until"/>.</summary>
    public static void Snooze(PimStore store, DueReminder reminder, DateTimeOffset until) =>
        store.SetMeta(Key(reminder.Appointment.Key), $"snoozed:{Ticks(until)}:{Ticks(reminder.Trigger)}");

    /// <summary>"in 12 Minuten", "jetzt", "seit 5 Minuten" – when the appointment starts, seen from <paramref name="now"/>.</summary>
    public static string StartsText(CalendarAppointment appointment, DateTimeOffset now)
    {
        if (appointment.IsAllDay)
        {
            return "ganztägig";
        }

        var minutes = (int)Math.Round((appointment.Start - now).TotalMinutes);
        return minutes switch
        {
            0 => "jetzt",
            > 0 and < 60 => $"in {minutes} {(minutes == 1 ? "Minute" : "Minuten")}",
            >= 60 => $"in {minutes / 60} Std. {minutes % 60} Min.",
            > -60 => $"seit {-minutes} {(minutes == -1 ? "Minute" : "Minuten")}",
            _ => $"seit {-minutes / 60} Std. {-minutes % 60} Min.",
        };
    }

    private static (string? Dismissed, string? SnoozedUntil, string? SnoozedTrigger) StateOf(PimStore store, string key)
    {
        var value = store.GetMeta(Key(key)) ?? "";
        var parts = value.Split(':');
        return parts switch
        {
            ["dismissed", var trigger] => (trigger, null, null),
            ["snoozed", var until, var trigger] => (null, until, trigger),
            _ => (null, null, null),
        };
    }

    private static string Key(string appointmentKey) => "reminder:" + appointmentKey;

    private static string Ticks(DateTimeOffset moment) => moment.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? Parse(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}

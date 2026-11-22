namespace ThixOffice.Pim.Calendar;

/// <summary>
/// What dragging an appointment does, like Outlook: times snap to quarter hours, a move keeps the length, the lower edge changes the end (at least a quarter
/// hour after the start), a move in the month keeps the time of day (all-day appointments keep their number of days).
/// </summary>
public static class CalendarMoves
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    /// <summary>The nearest quarter hour.</summary>
    public static DateTime Snap(DateTime local)
    {
        var quarters = Math.Round(local.TimeOfDay.TotalMinutes / Step.TotalMinutes, MidpointRounding.AwayFromZero);
        return local.Date.AddMinutes(quarters * Step.TotalMinutes);
    }

    /// <summary>The appointment starting at <paramref name="startLocal"/> (snapped), as long as before.</summary>
    public static CalendarAppointment Moved(CalendarAppointment appointment, DateTime startLocal)
    {
        if (appointment.IsAllDay)
        {
            return MovedToDay(appointment, startLocal.Date);
        }

        var start = Snap(startLocal);
        var length = appointment.End - appointment.Start;
        var newStart = CalendarAppointment.FromLocal(start);
        return appointment with { Start = newStart, End = newStart + length };
    }

    /// <summary>The appointment ending at <paramref name="endLocal"/> (snapped; at least a quarter hour after the start).</summary>
    public static CalendarAppointment Resized(CalendarAppointment appointment, DateTime endLocal)
    {
        if (appointment.IsAllDay)
        {
            return appointment;
        }

        var end = Snap(endLocal);
        var earliest = appointment.StartLocal + Step;
        return appointment with { End = CalendarAppointment.FromLocal(end < earliest ? earliest : end) };
    }

    /// <summary>The appointment on another day: the same time of day (all-day: the same number of days).</summary>
    public static CalendarAppointment MovedToDay(CalendarAppointment appointment, DateTime day)
    {
        var shift = day.Date - appointment.FirstDay;
        if (shift == TimeSpan.Zero)
        {
            return appointment;
        }

        if (appointment.IsAllDay)
        {
            return appointment with { Start = appointment.Start + shift, End = appointment.End + shift };
        }

        var length = appointment.End - appointment.Start;
        var start = CalendarAppointment.FromLocal(appointment.StartLocal + shift); // the wall clock stays (also across a change of summer time)
        return appointment with { Start = start, End = start + length };
    }

    /// <summary>True when the drag changed nothing.</summary>
    public static bool Same(CalendarAppointment a, CalendarAppointment b) => a.Start == b.Start && a.End == b.End;
}

using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Tests;

/// <summary>7d: dragging appointments (quarter hours, length kept, the end at least a quarter after the start, days in the month) and reminders.</summary>
public sealed class CalendarMovesAndRemindersTests
{
    private static readonly DateTime Monday = new(2026, 11, 23);

    private static CalendarAppointment At(DateTime start, DateTime end, int? reminder = null) => new()
    {
        Key = "k", CalendarKey = "a/b", Title = "Termin", Start = CalendarAppointment.FromLocal(start), End = CalendarAppointment.FromLocal(end), ReminderMinutes = reminder,
    };

    [Fact]
    public void A_move_snaps_to_quarter_hours_and_keeps_the_length()
    {
        var appointment = At(Monday.AddHours(10), Monday.AddHours(11.5));
        Assert.Equal(Monday.AddHours(11.25), CalendarMoves.Snap(Monday.AddHours(11).AddMinutes(19)));
        Assert.Equal(Monday.AddHours(11.5), CalendarMoves.Snap(Monday.AddHours(11).AddMinutes(23))); // 22.5 rounds up
        var moved = CalendarMoves.Moved(appointment, Monday.AddDays(1).AddHours(14).AddMinutes(7));
        Assert.Equal((Monday.AddDays(1).AddHours(14), Monday.AddDays(1).AddHours(15.5)), (moved.StartLocal, moved.EndLocal));
        Assert.True(CalendarMoves.Same(appointment, CalendarMoves.Moved(appointment, Monday.AddHours(10).AddMinutes(5))));
    }

    [Fact]
    public void The_lower_edge_sets_the_end_but_never_before_a_quarter_after_the_start()
    {
        var appointment = At(Monday.AddHours(10), Monday.AddHours(11));
        Assert.Equal(Monday.AddHours(12.75), CalendarMoves.Resized(appointment, Monday.AddHours(12).AddMinutes(41)).EndLocal);
        Assert.Equal(Monday.AddHours(10.25), CalendarMoves.Resized(appointment, Monday.AddHours(9)).EndLocal);
        Assert.Equal(Monday.AddHours(10), CalendarMoves.Resized(appointment, Monday.AddHours(13)).StartLocal);
    }

    [Fact]
    public void In_the_month_a_move_keeps_the_time_of_day_and_all_day_appointments_their_days()
    {
        var timed = At(Monday.AddHours(8.5), Monday.AddHours(9));
        var thursday = CalendarMoves.MovedToDay(timed, Monday.AddDays(3));
        Assert.Equal((Monday.AddDays(3).AddHours(8.5), Monday.AddDays(3).AddHours(9)), (thursday.StartLocal, thursday.EndLocal));

        var fair = new CalendarAppointment { Key = "f", IsAllDay = true, Start = CalendarAppointment.AllDayDate(Monday), End = CalendarAppointment.AllDayDate(Monday.AddDays(2)) };
        var later = CalendarMoves.MovedToDay(fair, Monday.AddDays(7));
        Assert.Equal((Monday.AddDays(7), Monday.AddDays(9)), (later.StartLocal, later.EndLocal));
        Assert.Same(fair, CalendarMoves.MovedToDay(fair, Monday));
    }

    [Fact]
    public void Across_the_change_of_summer_time_the_wall_clock_stays()
    {
        // 2026-10-25: the clocks go back in Central Europe; a move from Saturday 9:00 to Monday stays 9:00 (where the local zone has such a change)
        var saturday = new DateTime(2026, 10, 24, 9, 0, 0);
        var moved = CalendarMoves.MovedToDay(At(saturday, saturday.AddHours(1)), new DateTime(2026, 10, 26));
        Assert.Equal(new DateTime(2026, 10, 26, 9, 0, 0), moved.StartLocal);
    }

    [Fact]
    public void A_reminder_is_due_from_its_lead_time_until_the_end_and_answers_are_kept_per_trigger()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("reminders"));
        var now = CalendarAppointment.FromLocal(Monday.AddHours(9).AddMinutes(50));
        var soon = At(Monday.AddHours(10), Monday.AddHours(11), reminder: 15);
        var later = At(Monday.AddHours(12), Monday.AddHours(13), reminder: 15) with { Key = "later" };
        var none = At(Monday.AddHours(10), Monday.AddHours(11)) with { Key = "none" };

        var due = CalendarReminders.Due([soon, later, none], now, store);
        Assert.Equal(["k"], due.Select(d => d.Appointment.Key));
        Assert.Equal("in 10 Minuten", CalendarReminders.StartsText(soon, now));

        CalendarReminders.Snooze(store, due[0], now.AddMinutes(5));
        Assert.Empty(CalendarReminders.Due([soon], now.AddMinutes(4), store));
        Assert.Single(CalendarReminders.Due([soon], now.AddMinutes(5), store));

        CalendarReminders.Dismiss(store, due[0]);
        Assert.Empty(CalendarReminders.Due([soon], now.AddMinutes(20), store));
        Assert.Empty(CalendarReminders.Due([soon], CalendarAppointment.FromLocal(Monday.AddHours(11)), store)); // over
        var moved = soon with { Start = soon.Start.AddHours(1), End = soon.End.AddHours(1) };
        Assert.Single(CalendarReminders.Due([moved], CalendarAppointment.FromLocal(Monday.AddHours(10).AddMinutes(50)), store)); // a new time reminds again
    }
}

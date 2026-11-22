using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using ThixMail.Editor;
using ThixMail.Pages;
using ThixOffice.Core.Business;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop.Pages;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Desktop.Calendar;
using ThixOffice.Text.Drawing;
using static ThixMail.Tests.TestData;
using CalendarMode = ThixOffice.Pim.Desktop.Calendar.CalendarMode;

namespace ThixMail.Tests;

/// <summary>7d in the real ThixMail through the mouse: dragging appointments (quarter hours, one undo step, read-only refused), reminders, printing the calendar.</summary>
public sealed class CalendarUiTests
{
    private static readonly DateTime Monday = NextMonday();

    private static DateTime NextMonday()
    {
        var today = DateTime.Today;
        return today.AddDays(7 - (((int)today.DayOfWeek + 6) % 7));
    }

    private static async Task<(ThixOffice.Desktop.Shell.OfficeWindow Window, MailEditor Editor, CalendarView View, CalendarBook Book)> CalendarAsync(TestData data, CalendarMode mode)
    {
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        await UntilAsync(() => editor.Document.Pim.Calendar?.Store.LastSync is not null, "the first sync of the calendar");
        await PressAsync(window, PhysicalKey.Digit2, CommandKey);
        var view = editor.CalendarView!;
        view.SetMode(mode);
        view.GoTo(Monday);
        await SettleAsync();
        return (window, editor, view, editor.Document.Pim.Calendar!);
    }

    private static Border BlockOf(CalendarView view, string title) =>
        view.GetVisualDescendants().OfType<Border>().First(b => b.Tag is CalendarAppointment a && a.Title == title && b.IsEffectivelyVisible);

    private static async Task DragAsync(Avalonia.Controls.Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X, from.Y + 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        await SettleAsync();
    }

    [AvaloniaFact]
    public async Task An_appointment_dragged_moves_by_quarter_hours_and_days_its_lower_edge_changes_the_end_and_Strg_Z_puts_it_back()
    {
        using var data = new TestData();
        var (window, editor, view, book) = await CalendarAsync(data, CalendarMode.WorkWeek);
        var key = book.Save(new CalendarAppointment
        {
            CalendarKey = book.DefaultCalendar().Key, Title = "Abstimmung", Start = CalendarAppointment.FromLocal(Monday.AddHours(10)), End = CalendarAppointment.FromLocal(Monday.AddHours(11)),
        }).Key;
        view.Refresh();
        await SettleAsync();

        // one hour and a quarter later (the pointer lands between quarters: it snaps), one day to the right
        var block = BlockOf(view, "Abstimmung");
        var from = block.TranslatePoint(new Point(20, 10), window)!.Value;
        var dayWidth = block.Bounds.Width + 6;
        await DragAsync(window, from, new Point(from.X + dayWidth, from.Y + (1.27 * CalendarView.HourHeight)));
        var moved = book.Find(key)!;
        Assert.Equal((Monday.AddDays(1).AddHours(11.25), Monday.AddDays(1).AddHours(12.25)), (moved.StartLocal, moved.EndLocal));
        Assert.Equal("Termin verschieben", editor.Document.Session.UndoLabel);

        // the lower edge: the end to 13:00, the start stays
        block = BlockOf(view, "Abstimmung");
        var edge = block.TranslatePoint(new Point(20, block.Bounds.Height - 2), window)!.Value;
        await DragAsync(window, edge, new Point(edge.X, edge.Y + (0.76 * CalendarView.HourHeight)));
        var longer = book.Find(key)!;
        Assert.Equal((Monday.AddDays(1).AddHours(11.25), Monday.AddDays(1).AddHours(13)), (longer.StartLocal, longer.EndLocal));
        Assert.Equal("Termin verlängern", editor.Document.Session.UndoLabel);

        // each drag is one step back
        view.Focus();
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal(Monday.AddDays(1).AddHours(12.25), book.Find(key)!.EndLocal);
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal(Monday.AddHours(10), book.Find(key)!.StartLocal);
        await UntilAsync(() => view.Shown.Single(a => a.Key == key).StartLocal == Monday.AddHours(10), "the view showing it back at Monday 10:00");
    }

    [AvaloniaFact]
    public async Task In_the_month_an_appointment_dragged_onto_another_day_keeps_its_time_and_a_read_only_calendar_refuses()
    {
        using var data = new TestData();
        var (window, editor, view, book) = await CalendarAsync(data, CalendarMode.Month);
        var key = book.Save(new CalendarAppointment
        {
            CalendarKey = book.DefaultCalendar().Key, Title = "Zahnarzt", Start = CalendarAppointment.FromLocal(Monday.AddHours(8.5)), End = CalendarAppointment.FromLocal(Monday.AddHours(9)),
        }).Key;

        // a calendar MatCloud keeps in step (read-only here) with an appointment on the same Monday
        var account = "m365";
        var calendarKey = CalendarContract.CalendarKey(account, "work");
        book.Store.ApplyRemote(
        [
            Item(CalendarContract.Accounts, account, CalendarItems.Serialize(new CalendarAccountItem(CalendarAccountKind.Microsoft, "Firma", "anna@firma.de", "", true, CalendarSyncMode.Cloud))),
            Item(CalendarContract.CloudCalendars, calendarKey, System.Text.Json.JsonSerializer.Serialize(new CalendarCloudCalendar(account, "work", "Firma", "#0078D4", false, true, 15), CloudContract.Json)),
            Item(CalendarContract.CloudEvents, CalendarContract.EventKey(calendarKey, "e1"), System.Text.Json.JsonSerializer.Serialize(new CalendarCloudEvent
            {
                Calendar = calendarKey, RemoteId = "e1", Title = "Jour fixe", Start = CalendarAppointment.FromLocal(Monday.AddHours(14)), End = CalendarAppointment.FromLocal(Monday.AddHours(14.5)),
            }, CloudContract.Json)),
        ]);
        book.Invalidate();
        view.Refresh();
        await SettleAsync();

        var block = BlockOf(view, "Zahnarzt");
        var from = block.TranslatePoint(new Point(10, 9), window)!.Value;
        var cell = block.Bounds.Width + 6;
        await DragAsync(window, from, new Point(from.X + (2 * cell), from.Y));
        var moved = book.Find(key)!;
        Assert.Equal((Monday.AddDays(2).AddHours(8.5), Monday.AddDays(2).AddHours(9)), (moved.StartLocal, moved.EndLocal));

        var fixedBlock = BlockOf(view, "Jour fixe");
        var fixedFrom = fixedBlock.TranslatePoint(new Point(10, 9), window)!.Value;
        await Task.Delay(800); // not a double click on the same spot (the read-only appointment now stands where the moved one stood)
        await DragAsync(window, fixedFrom, new Point(fixedFrom.X + cell, fixedFrom.Y));
        Assert.Contains("ThixCalendar", window.FindDescendantMessage(), StringComparison.Ordinal);
        Assert.Equal(Monday.AddHours(14), view.Shown.Single(a => a.Title == "Jour fixe").StartLocal);
        Assert.Equal(Monday.AddHours(14), BlockOfAppointment(view, "Jour fixe").StartLocal); // drawn where it was
    }

    private static CalendarAppointment BlockOfAppointment(CalendarView view, string title) => (CalendarAppointment)BlockOf(view, title).Tag!;

    [AvaloniaFact]
    public async Task A_reminder_shows_while_ThixMail_runs_Erneut_erinnern_waits_Schliessen_ends_it_and_nothing_shows_while_ThixCalendar_runs()
    {
        using var data = new TestData();
        var (window, editor, view, book) = await CalendarAsync(data, CalendarMode.Day);
        var now = DateTimeOffset.Now;
        MailReminders.NowForTests = () => now;
        var key = book.Save(new CalendarAppointment
        {
            CalendarKey = book.DefaultCalendar().Key, Title = "Telefonat Nordlicht", Location = "Büro", ReminderMinutes = 15, Start = now.AddMinutes(10), End = now.AddMinutes(40),
        }).Key;

        // ThixCalendar runs: it reminds itself, ThixMail stays quiet
        var reminders = editor.Reminders!;
        reminders.Check();
        Assert.Null(reminders.Shown);

        MailReminders.CalendarRunsForTests = () => false;
        reminders.Check();
        var shown = Assert.IsType<ReminderWindow>(reminders.Shown);
        await SettleAsync();
        var row = Assert.Single(shown.Rows);
        Assert.Equal(key, row.Reminder.Appointment.Key);
        Assert.Contains("in 10 Minuten", row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").First(t => t.StartsWith("in ", StringComparison.Ordinal)), StringComparison.Ordinal);

        // Erneut erinnern in 5 Minuten: gone, back after five minutes
        row.SnoozeChoice.SelectedIndex = 0;
        await ClickAsync(shown, row.SnoozeButton);
        Assert.Null(reminders.Shown);
        now = now.AddMinutes(4);
        reminders.Check();
        Assert.Null(reminders.Shown);
        now = now.AddMinutes(2);
        reminders.Check();
        shown = Assert.IsType<ReminderWindow>(reminders.Shown);
        await SettleAsync();

        // Schließen: not again – until the appointment moves to another time (a new reminder)
        await ClickAsync(shown, Assert.Single(shown.Rows).DismissButton);
        Assert.Null(reminders.Shown);
        reminders.Check();
        Assert.Null(reminders.Shown);
        book.Save(book.Find(key)! with { Start = now.AddMinutes(5), End = now.AddMinutes(35) });
        reminders.Check();
        Assert.NotNull(reminders.Shown);
        reminders.Shown!.Close();
    }

    [AvaloniaFact]
    public async Task Datei_Drucken_in_the_calendar_prints_the_week_shown_and_offers_day_and_month()
    {
        using var data = new TestData();
        var (window, editor, view, book) = await CalendarAsync(data, CalendarMode.WorkWeek);
        book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Quartalsplanung", Start = CalendarAppointment.FromLocal(Monday.AddHours(9)), End = CalendarAppointment.FromLocal(Monday.AddHours(11)) });
        book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Messe", IsAllDay = true, Start = CalendarAppointment.AllDayDate(Monday.AddDays(2)), End = CalendarAppointment.AllDayDate(Monday.AddDays(4)) });
        view.Refresh();

        view.Focus();
        await PressAsync(window, PhysicalKey.P, CommandKey);
        await UntilAsync(() => window.Backstage.CurrentPage is MailPrintPage, () => $"the print page (open {window.Backstage.IsOpen}, page {window.Backstage.CurrentPageId}, print enabled {window.Commands[MailCommandIds.Print].IsEnabled})");
        var page = Assert.IsType<MailPrintPage>(window.Backstage.CurrentPage);
        Assert.Equal(MailPrintPage.CalendarScopes, page.ScopeChoice.ItemsSource!.Cast<string>());
        Assert.Equal(1, page.ScopeChoice.SelectedIndex); // Wochenformat for the work week shown
        Assert.Equal(1, page.Pages.Count);
        Assert.True(page.Pages.SizeOf(0).Width > page.Pages.SizeOf(0).Height); // landscape

        var week = view.PrintPages(CalendarPrintStyle.Week, ThixOffice.Desktop.Rendering.OfficeFonts.Skia);
        var texts = week[0].Items.OfType<GlyphRunItem>().Select(g => g.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Quartals", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Messe", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Mo", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.StartsWith("Sa", StringComparison.Ordinal)); // the work week

        page.ScopeChoice.SelectedIndex = 2; // Monatsformat
        await SettleAsync();
        Assert.Equal(1, page.Pages.Count);
        var month = view.PrintPages(CalendarPrintStyle.Month, ThixOffice.Desktop.Rendering.OfficeFonts.Skia);
        Assert.Contains(month[0].Items.OfType<GlyphRunItem>(), g => g.Text.Contains("Quartals", StringComparison.Ordinal));
        Assert.Equal(42, month[0].Items.OfType<RectangleItem>().Count(r => r.Stroke is not null)); // six weeks of days
    }

    private static SyncItemDto Item(string collection, string key, string data) => new() { Collection = collection, Key = key, Data = data, UpdatedAt = DateTimeOffset.UtcNow, Revision = 1 };
}

internal static class WindowMessages
{
    /// <summary>The text of the window's message bar.</summary>
    public static string FindDescendantMessage(this Avalonia.Controls.Window window) => ((ThixOffice.Desktop.Shell.OfficeWindow)window).MessageBar.Text;
}

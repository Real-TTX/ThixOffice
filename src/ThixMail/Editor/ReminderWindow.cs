using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixMail.Core;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Calendar;

namespace ThixMail.Editor;

/// <summary>
/// The reminders of the calendar while ThixMail runs, like Outlook's reminder window: a small window above the others with each due appointment (title,
/// when it starts, where), "Schließen" and "Erneut erinnern" in a chosen time (5, 10, 15 minutes, 1 hour), "Alle schließen" when there are several.
/// Checked every 30 s (and when the calendar changes) by <see cref="MailReminders"/>; nothing shows while ThixCalendar runs – it reminds itself.
/// </summary>
public sealed class ReminderWindow : MatWindow
{
    private readonly MailReminders _owner;
    private readonly StackPanel _rows = new() { Spacing = 10 };
    private readonly Button _dismissAll;

    public ReminderWindow(MailReminders owner)
    {
        _owner = owner;
        Title = "Erinnerungen – ThixMail";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _dismissAll = new Button { Content = new IconLabel { Icon = "x", Text = "Alle schließen" } };
        _dismissAll.Click += (_, _) => _owner.DismissAll();
        var heading = new TextBlock { Text = "Erinnerungen", Margin = new Thickness(0, 0, 0, 4) }.Themed("MatText.SectionTitle");
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(18, 14, 18, 16), Children = { heading, new ScrollViewer { Content = _rows, MaxHeight = 420 }, new ActionBar { Children = { _dismissAll } } } };
        Content = new Border { Child = content }.Use(Border.BackgroundProperty, "Mat.Bg");
    }

    /// <summary>The rows shown (tests).</summary>
    public IReadOnlyList<ReminderRow> Rows => [.. _rows.Children.OfType<ReminderRow>()];

    public Button DismissAllButton => _dismissAll;

    public void Show(IReadOnlyList<DueReminder> due, DateTimeOffset now)
    {
        _rows.Children.Clear();
        foreach (var reminder in due)
        {
            _rows.Children.Add(new ReminderRow(reminder, now, _owner));
        }

        _dismissAll.IsVisible = due.Count > 1;
    }
}

/// <summary>One due reminder: what, when, where – Schließen · Erneut erinnern in [Zeit].</summary>
public sealed class ReminderRow : Border
{
    public ReminderRow(DueReminder reminder, DateTimeOffset now, MailReminders owner)
    {
        Reminder = reminder;
        var appointment = reminder.Appointment;
        var title = new TextBlock { Text = appointment.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var when = new TextBlock
        {
            Text = $"{CalendarReminders.StartsText(appointment, now)} · {(appointment.IsAllDay ? appointment.StartLocal.ToString("dddd, d. MMMM", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : appointment.StartLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))}"
                   + (appointment.Location.Length > 0 ? $" · {appointment.Location}" : ""),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        when.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        DismissButton = new Button { Content = new IconLabel { Icon = "check", Text = "Schließen" } }.Themed("MatButton.Primary");
        DismissButton.Click += (_, _) => owner.Dismiss(reminder);
        SnoozeChoice = new ComboBox { ItemsSource = CalendarReminders.SnoozeChoices.Select(c => c.Label).ToList(), SelectedIndex = 0, MinWidth = 120 };
        AutomationProperties.SetName(SnoozeChoice, "Erneut erinnern in");
        SnoozeButton = new Button { Content = new IconLabel { Icon = "clock", Text = "Erneut erinnern" } };
        SnoozeButton.Click += (_, _) => owner.Snooze(reminder, CalendarReminders.SnoozeChoices[Math.Max(0, SnoozeChoice.SelectedIndex)].Delay);
        var icon = new MatIcon { Icon = "calendar", Size = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
        icon.Use(MatIcon.ForegroundProperty, "Mat.Accent");
        var text = new StackPanel { Spacing = 2, Children = { title, when } };
        var head = new DockPanel { Children = { icon, text } };
        DockPanel.SetDock(icon, Dock.Left);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DismissButton, SnoozeButton, SnoozeChoice } };
        Child = new StackPanel { Spacing = 10, Children = { head, actions } };
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        this.Use(BorderBrushProperty, "Mat.Border");
        this.Use(BackgroundProperty, "Mat.Surface");
    }

    public DueReminder Reminder { get; }

    public Button DismissButton { get; }

    public Button SnoozeButton { get; }

    public ComboBox SnoozeChoice { get; }
}

/// <summary>
/// ThixMail's reminders: every 30 s (and when the calendar changes) the due reminders of the account's calendar (<see cref="CalendarReminders"/>) go into
/// the <see cref="ReminderWindow"/> – unless ThixCalendar runs on this computer (it shows the same reminders itself: ThixMail never doubles them up; it
/// holds the named mutex "ThixCalendar.SingleInstance" while it runs) or notifications are switched off in ThixMail's settings.
/// </summary>
public sealed class MailReminders : IDisposable
{
    private readonly OfficeWindow _window;
    private readonly MailDocument _document;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private ReminderWindow? _shown;

    public MailReminders(OfficeWindow window, MailDocument document)
    {
        _window = window;
        _document = document;
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        _document.Pim.Changed += OnPimChanged;
    }

    /// <summary>The clock of tests and snapshots (null: the real one).</summary>
    public static Func<DateTimeOffset>? NowForTests { get; set; }

    /// <summary>Whether ThixCalendar runs, as tests, snapshots and the self test say (null: ask the system). True keeps ThixMail's reminders quiet.</summary>
    public static Func<bool>? CalendarRunsForTests { get; set; }

    private static DateTimeOffset Now() => NowForTests?.Invoke() ?? DateTimeOffset.Now;

    private static bool CalendarRuns() => CalendarRunsForTests?.Invoke() ?? ThixCalendarRuns();

    /// <summary>The window of reminders while it shows, or null.</summary>
    public ReminderWindow? Shown => _shown;

    /// <summary>Looks for due reminders now and shows, updates or closes the window.</summary>
    public void Check()
    {
        if (_document.IsClosed || _document.Pim.Calendar is not { } book
            || ThixOffice.Core.Business.SettingsService.AppValue(MailDocument.Settings.Notifications) == "false" || CalendarRuns())
        {
            Close();
            return;
        }

        var now = Now();
        var due = CalendarReminders.Due(book.Between(now.LocalDateTime.Date.AddDays(-1), now.LocalDateTime.Date.AddDays(30)), now, book.Store);
        if (due.Count == 0)
        {
            Close();
            return;
        }

        if (_shown is null)
        {
            _shown = new ReminderWindow(this);
            _shown.Closed += (_, _) => _shown = null;
            _shown.Show(due, now);
            _shown.Show();
        }
        else
        {
            _shown.Show(due, now);
        }
    }

    /// <summary>The window with these reminders, whatever the clock says (the snapshot of the sample).</summary>
    internal ReminderWindow ShowForSample(IReadOnlyList<DueReminder> due, DateTimeOffset now)
    {
        Close();
        _shown = new ReminderWindow(this);
        _shown.Closed += (_, _) => _shown = null;
        _shown.Show(due, now);
        _shown.Show();
        return _shown;
    }

    public void Dismiss(DueReminder reminder)
    {
        if (_document.Pim.Calendar is { } book)
        {
            CalendarReminders.Dismiss(book.Store, reminder);
        }

        Check();
    }

    public void Snooze(DueReminder reminder, TimeSpan delay)
    {
        if (_document.Pim.Calendar is { } book)
        {
            CalendarReminders.Snooze(book.Store, reminder, Now() + delay);
        }

        Check();
    }

    public void DismissAll()
    {
        foreach (var row in _shown?.Rows ?? [])
        {
            if (_document.Pim.Calendar is { } book)
            {
                CalendarReminders.Dismiss(book.Store, row.Reminder);
            }
        }

        Check();
    }

    private void OnPimChanged(object? sender, EventArgs e) => Ui.Post(Check);

    private void Close()
    {
        var shown = _shown;
        _shown = null;
        shown?.Close();
    }

    /// <summary>ThixCalendar holds this mutex while it runs (its installed instance; a development run with its own data folder adds a suffix).</summary>
    private static bool ThixCalendarRuns()
    {
        try
        {
            if (Mutex.TryOpenExisting("ThixCalendar.SingleInstance", out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return true; // it exists, but belongs to someone we may not open: ThixCalendar runs
        }

        return false;
    }

    public void Dispose()
    {
        _timer.Stop();
        _document.Pim.Changed -= OnPimChanged;
        Close();
    }
}

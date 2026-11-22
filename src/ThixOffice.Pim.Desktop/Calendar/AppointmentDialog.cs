using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatCloud.Contract.Calendar;
using MatDesktop;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Pim.Calendar;

namespace ThixOffice.Pim.Desktop.Calendar;

/// <summary>
/// "Termin" (new or to change) like Outlook's: Titel, Ort, Beginn and Ende (date and time), Ganztägig, Kalender (the own calendars), Erinnerung, and –
/// where the host can send invitations – Teilnehmer (then OK says "Senden": a meeting); Notizen. A series is not offered: ThixCalendar's own calendars
/// carry none. An appointment of a calendar MatCloud keeps in step is shown read-only. OK checks through the business layer and saves through
/// <paramref name="save"/> (the host's: the book, or a meeting with invitations); a refusal is shown at its field.
/// </summary>
public sealed class AppointmentDialog : OfficeDialog
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The reminders Outlook offers (minutes; null = none).</summary>
    public static readonly IReadOnlyList<(int? Minutes, string Label)> Reminders =
    [
        (null, "Keine"), (0, "Zu Beginn"), (5, "5 Minuten vorher"), (10, "10 Minuten vorher"), (15, "15 Minuten vorher"), (30, "30 Minuten vorher"),
        (60, "1 Stunde vorher"), (120, "2 Stunden vorher"), (1440, "1 Tag vorher"), (10080, "1 Woche vorher"),
    ];

    private readonly CalendarBook _book;
    private readonly CalendarAppointment _original;
    private readonly IReadOnlyList<CalendarEntry> _calendars;
    private readonly Func<CalendarAppointment, IReadOnlyList<string>, CalendarAppointment> _save;

    public AppointmentDialog(CalendarBook book, CalendarAppointment appointment, Func<CalendarAppointment, IReadOnlyList<string>, CalendarAppointment>? save = null,
        Func<string, IReadOnlyList<string>>? attendeeSuggestions = null, bool asMeeting = false)
        : base(appointment.Key.Length == 0 ? (asMeeting ? "Neue Besprechung" : "Neuer Termin") : appointment.IsReadOnly ? "Termin" : "Termin bearbeiten", 560)
    {
        _book = book;
        _original = appointment;
        _save = save ?? ((value, _) => book.Save(value));
        IsReadOnly = appointment.IsReadOnly;
        _calendars = IsReadOnly ? book.Calendars.Where(c => c.Key == appointment.CalendarKey).ToList() : book.WritableCalendars;

        TitleBox = new TextBox { Text = appointment.Title, IsReadOnly = IsReadOnly };
        LocationBox = new TextBox { Text = appointment.Location, IsReadOnly = IsReadOnly };
        AllDay = new CheckBox { Content = "Ganztägig", IsChecked = appointment.IsAllDay, IsEnabled = !IsReadOnly };
        AutomationProperties.SetName(AllDay, "Ganztägig");
        var lastDay = appointment.IsAllDay ? appointment.EndLocal.AddDays(-1) : appointment.EndLocal;
        StartDate = new TextBox { Text = appointment.StartLocal.ToString("dd.MM.yyyy", German), Width = 120, IsReadOnly = IsReadOnly };
        StartTime = new TextBox { Text = appointment.StartLocal.ToString("HH:mm", German), Width = 80, IsReadOnly = IsReadOnly };
        EndDate = new TextBox { Text = lastDay.ToString("dd.MM.yyyy", German), Width = 120, IsReadOnly = IsReadOnly };
        EndTime = new TextBox { Text = appointment.EndLocal.ToString("HH:mm", German), Width = 80, IsReadOnly = IsReadOnly };
        AutomationProperties.SetName(StartDate, "Beginn (Datum)");
        AutomationProperties.SetName(StartTime, "Beginn (Uhrzeit)");
        AutomationProperties.SetName(EndDate, "Ende (Datum)");
        AutomationProperties.SetName(EndTime, "Ende (Uhrzeit)");
        AllDay.IsCheckedChanged += (_, _) => ShowTimes();

        CalendarChoice = new ComboBox
        {
            ItemsSource = _calendars.Select(c => c.Name).ToList(),
            SelectedIndex = Math.Max(0, _calendars.ToList().FindIndex(c => c.Key == appointment.CalendarKey)),
            IsEnabled = !IsReadOnly && _calendars.Count > 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ReminderChoice = new ComboBox
        {
            ItemsSource = Reminders.Select(r => r.Label).ToList(),
            SelectedIndex = Math.Max(0, Reminders.ToList().FindIndex(r => r.Minutes == appointment.ReminderMinutes)),
            IsEnabled = !IsReadOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Tentative = new CheckBox { Content = "Mit Vorbehalt", IsChecked = appointment.Status == CalendarEventStatus.Tentative, IsEnabled = !IsReadOnly };
        AutomationProperties.SetName(Tentative, "Mit Vorbehalt");
        Attendees = new AddressField { EntrySuggestions = attendeeSuggestions ?? (_ => []) };
        Attendees.Box.Watermark = "Teilnehmer einladen (E-Mail-Adressen, mit ; getrennt)";
        AutomationProperties.SetName(Attendees.Box, "Teilnehmer");
        Attendees.Box.TextChanged += (_, _) => ShowOkLabel();
        Notes = new TextBox { Text = appointment.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 200, IsReadOnly = IsReadOnly };

        var start = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { StartDate, StartTime } };
        var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { EndDate, EndTime, AllDay } };
        AllDay.Margin = new Thickness(8, 0, 0, 0);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Field("Titel", TitleBox, nameof(CalendarAppointment.Title)));
        body.Children.Add(Field("Ort", LocationBox, nameof(CalendarAppointment.Location)));
        body.Children.Add(Field("Beginn", start, nameof(CalendarAppointment.Start)));
        body.Children.Add(Field("Ende", end, nameof(CalendarAppointment.End)));
        var choices = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var calendarField = Field("Kalender", CalendarChoice, nameof(CalendarAppointment.CalendarKey));
        var reminderField = Field("Erinnerung", ReminderChoice, nameof(CalendarAppointment.ReminderMinutes));
        Grid.SetColumn(reminderField, 2);
        choices.Children.Add(calendarField);
        choices.Children.Add(reminderField);
        body.Children.Add(choices);
        body.Children.Add(Tentative);
        if (save is not null && !IsReadOnly && (appointment.Key.Length == 0 || asMeeting))
        {
            body.Children.Add(Field("Teilnehmer", Attendees, "Attendees"));
            Attendees.IsVisible = true;
        }
        else
        {
            Attendees.IsVisible = false;
        }

        body.Children.Add(Field("Notizen", Notes, nameof(CalendarAppointment.Description)));
        if (IsReadOnly)
        {
            var note = new InfoBar { Margin = new Thickness(0, 4, 0, 0) };
            note.Show("Dieser Termin gehört zu einem Kalender, den ThixCalendar über die ThixCloud abgleicht – ändern Sie ihn dort.", InfoSeverity.Info);
            body.Children.Insert(0, note);
            OkButton.IsVisible = false;
            CancelButton.Content = new IconLabel { Icon = "x", Text = "Schließen" };
        }

        if (_calendars.Count == 0 && !IsReadOnly)
        {
            var note = new TextBlock { Text = "Der Termin kommt in einen neuen Kalender „Kalender“ (Eigene Kalender).", TextWrapping = TextWrapping.Wrap };
            note.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            body.Children.Add(note);
        }

        SetBody(body);
        ShowTimes();
        ShowOkLabel();
        Opened += (_, _) => TitleBox.Focus();
    }

    public bool IsReadOnly { get; }

    public TextBox TitleBox { get; }

    public TextBox LocationBox { get; }

    public CheckBox AllDay { get; }

    public TextBox StartDate { get; }

    public TextBox StartTime { get; }

    public TextBox EndDate { get; }

    public TextBox EndTime { get; }

    public ComboBox CalendarChoice { get; }

    public ComboBox ReminderChoice { get; }

    public CheckBox Tentative { get; }

    public AddressField Attendees { get; }

    public TextBox Notes { get; }

    /// <summary>The message shown at its field after a refused OK (tests).</summary>
    public string? ErrorText { get; private set; }

    /// <summary>The appointment after OK.</summary>
    public CalendarAppointment? Saved { get; private set; }

    private void ShowTimes()
    {
        var allDay = AllDay.IsChecked == true;
        StartTime.IsVisible = !allDay;
        EndTime.IsVisible = !allDay;
    }

    private void ShowOkLabel() =>
        OkButton.Content = Attendees.IsVisible && Attendees.Addresses.Count > 0 ? new IconLabel { Icon = "arrow-right", Text = "Senden" } : new IconLabel { Icon = "check", Text = "Speichern" };

    protected override bool Apply()
    {
        ErrorText = null;
        FormErrors.ShowError(this, null, "");
        var allDay = AllDay.IsChecked == true;
        if (ParseDate(StartDate.Text) is not { } startDate)
        {
            return Fail(nameof(CalendarAppointment.Start), "Bitte ein Datum wie 26.11.2026 eingeben.");
        }

        if (ParseDate(EndDate.Text) is not { } endDate)
        {
            return Fail(nameof(CalendarAppointment.End), "Bitte ein Datum wie 26.11.2026 eingeben.");
        }

        DateTimeOffset start;
        DateTimeOffset end;
        if (allDay)
        {
            start = CalendarAppointment.AllDayDate(startDate);
            end = CalendarAppointment.AllDayDate(endDate.AddDays(1));
        }
        else
        {
            if (ParseTime(StartTime.Text) is not { } startTime)
            {
                return Fail(nameof(CalendarAppointment.Start), "Bitte eine Uhrzeit wie 14:30 eingeben.");
            }

            if (ParseTime(EndTime.Text) is not { } endTime)
            {
                return Fail(nameof(CalendarAppointment.End), "Bitte eine Uhrzeit wie 15:00 eingeben.");
            }

            start = CalendarAppointment.FromLocal(startDate + startTime);
            end = CalendarAppointment.FromLocal(endDate + endTime);
        }

        var calendar = _calendars.Count == 0 ? _book.EnsureOwnCalendar() : _calendars[Math.Clamp(CalendarChoice.SelectedIndex, 0, _calendars.Count - 1)];
        var value = _original with
        {
            CalendarKey = calendar.Key,
            Title = TitleBox.Text ?? "",
            Location = LocationBox.Text ?? "",
            Start = start,
            End = end,
            IsAllDay = allDay,
            ReminderMinutes = Reminders[Math.Clamp(ReminderChoice.SelectedIndex, 0, Reminders.Count - 1)].Minutes,
            Status = Tentative.IsChecked == true ? CalendarEventStatus.Tentative : CalendarEventStatus.Confirmed,
            Description = Notes.Text ?? "",
        };
        try
        {
            CalendarBook.Validate(CalendarBook.Normalize(value));
            Saved = _save(value, Attendees.IsVisible ? Attendees.Addresses : []);
            return true;
        }
        catch (BusinessException exception)
        {
            return Fail(exception.Field, exception.Message);
        }
    }

    private bool Fail(string? field, string message)
    {
        ErrorText = message;
        if (!FormErrors.ShowError(this, field, message))
        {
            FormErrors.ShowError(this, nameof(CalendarAppointment.Title), message);
        }

        return false;
    }

    private static MatFormField Field(string label, Control control, string field)
    {
        var form = new MatFormField { Label = label, Content = control };
        FormErrors.SetField(form, field);
        if (control is TextBox or ComboBox)
        {
            AutomationProperties.SetName(control, label);
        }

        return form;
    }

    /// <summary>"26.11.2026", "26.11.26", "26.11." (this year).</summary>
    internal static DateTime? ParseDate(string? text)
    {
        var value = (text ?? "").Trim();
        foreach (var format in new[] { "d.M.yyyy", "d.M.yy" })
        {
            if (DateTime.TryParseExact(value, format, German, DateTimeStyles.None, out var date))
            {
                return date.Date;
            }
        }

        return DateTime.TryParseExact(value.TrimEnd('.'), "d.M", German, DateTimeStyles.None, out var shortDate) ? new DateTime(DateTime.Today.Year, shortDate.Month, shortDate.Day) : null;
    }

    /// <summary>"14:30", "14.30", "1430", "9" (o'clock).</summary>
    internal static TimeSpan? ParseTime(string? text)
    {
        var value = (text ?? "").Trim().Replace('.', ':');
        if (value.Length is 3 or 4 && value.All(char.IsAsciiDigit))
        {
            value = value[..^2] + ":" + value[^2..];
        }

        if (int.TryParse(value, NumberStyles.None, German, out var hour) && hour is >= 0 and < 24)
        {
            return TimeSpan.FromHours(hour);
        }

        return TimeSpan.TryParseExact(value, ["h\\:mm", "hh\\:mm"], German, out var time) && time < TimeSpan.FromDays(1) ? time : null;
    }
}

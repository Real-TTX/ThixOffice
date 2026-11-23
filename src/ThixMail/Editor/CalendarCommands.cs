using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Model;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Desktop.Calendar;
using ThixOffice.Pim.Desktop.People;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixMail.Editor;

/// <summary>The ids of the view commands and of the calendar's commands.</summary>
public static class CalendarCommandIds
{
    public const string ShowMail = "mail.view.mail";
    public const string ShowCalendar = "mail.view.calendar";
    public const string ShowPeople = "mail.view.people";
    public const string New = "calendar.new";
    public const string NewMeeting = "calendar.newMeeting";
    public const string Open = "calendar.open";
    public const string Delete = "calendar.delete";
    public const string Today = "calendar.today";
    public const string Previous = "calendar.previous";
    public const string Next = "calendar.next";
    public const string Day = "calendar.view.day";
    public const string WorkWeek = "calendar.view.workweek";
    public const string Week = "calendar.view.week";
    public const string Month = "calendar.view.month";
    public const string Sync = "calendar.sync";
    public const string Import = "calendar.import";
    public const string Export = "calendar.export";
}

/// <summary>
/// The commands of ThixMail's views: E-Mail, Kalender, Personen (Strg+1/2/3 like Outlook), the calendar's (Neuer Termin Strg+N, Neue Besprechung
/// Strg+Umschalt+Q, Öffnen, Löschen Entf, Heute, Zurück/Weiter, Tag/Arbeitswoche/Woche/Monat Strg+Alt+1–4, Jetzt synchronisieren F9) – each acts while
/// the calendar is shown – and the people commands of ThixOffice.Pim.Desktop (the same as ThixContacts', E-Mail senden opens ThixMail's composer).
/// </summary>
public static class CalendarCommands
{
    public static void Add(OfficeWindow window)
    {
        MailEditor? Editor() => window.Editor as MailEditor;
        CalendarView? View() => Editor() is { ActiveView: MailArea.Calendar, CalendarView: { } view } editor && editor.Document.Pim.IsAvailable ? view : null;
        bool InCalendar() => View() is not null;
        var commands = window.Commands;

        foreach (var (id, label, icon, shortcut, view) in new[]
                 {
                     (CalendarCommandIds.ShowMail, "E-Mail", "mail", "Ctrl+D1", MailArea.Mail),
                     (CalendarCommandIds.ShowCalendar, "Kalender", "calendar", "Ctrl+D2", MailArea.Calendar),
                     (CalendarCommandIds.ShowPeople, "Personen", "users", "Ctrl+D3", MailArea.People),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Editor()?.ShowView(view))
            {
                Icon = icon,
                Shortcut = shortcut,
                Description = $"Wechselt zur Ansicht „{label}“.",
                Keywords = ["ansicht", "wechseln", label.ToLowerInvariant()],
                IsChecked = () => Editor()?.ActiveView == view,
            });
        }

        commands.Add(new OfficeCommand(CalendarCommandIds.New, "Neuer Termin", () => NewAsync(window, Editor()!, View()!.NewStart(), allDay: false, meeting: false))
        {
            Icon = "plus",
            Shortcut = "Ctrl+N",
            Description = "Trägt einen neuen Termin in den Kalender ein.",
            Keywords = ["termin", "neu", "eintragen", "kalender"],
            CanExecute = InCalendar,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.NewMeeting, "Neue Besprechung", () => NewAsync(window, Editor()!, View()!.NewStart(), allDay: false, meeting: true))
        {
            Icon = "users",
            Description = "Lädt zu einer Besprechung ein: der Termin kommt in Ihren Kalender, die Teilnehmer bekommen eine Einladung per E-Mail.",
            Keywords = ["besprechung", "einladung", "meeting", "teilnehmer"],
            CanExecute = () => InCalendar() && Editor()!.Document.Accounts.Accounts.Count > 0,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Open, "Öffnen", () => OpenAsync(window, Editor()!, View()!.Selected!))
        {
            Icon = "edit",
            Shortcut = "Ctrl+O",
            Description = "Öffnet den gewählten Termin.",
            Keywords = ["öffnen", "bearbeiten", "termin"],
            CanExecute = () => View() is { Selected: not null },
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Delete, "Löschen", () => DeleteAsync(window, Editor()!, View()!.Selected!))
        {
            Icon = "trash",
            Shortcut = "Delete",
            Description = "Löscht den gewählten Termin (eine Besprechung, zu der Sie eingeladen haben, wird den Teilnehmern abgesagt).",
            Keywords = ["löschen", "termin", "entfernen", "absagen"],
            CanExecute = () => View() is { Selected.IsReadOnly: false },
            YieldsToTextField = true,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Today, "Heute", () => View()!.GoToday())
        {
            Icon = "calendar",
            Description = "Zeigt den heutigen Tag.",
            Keywords = ["heute", "jetzt", "gehe zu"],
            CanExecute = InCalendar,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Previous, "Zurück", () => View()!.Previous())
        {
            Icon = "chevron-left",
            Shortcut = "Alt+Left",
            Description = "Zeigt den vorigen Zeitraum.",
            Keywords = ["zurück", "vorher"],
            CanExecute = InCalendar,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Next, "Weiter", () => View()!.Next())
        {
            Icon = "chevron-right",
            Shortcut = "Alt+Right",
            Description = "Zeigt den nächsten Zeitraum.",
            Keywords = ["weiter", "vor", "nächste"],
            CanExecute = InCalendar,
        });
        foreach (var (id, label, mode, key) in new[]
                 {
                     (CalendarCommandIds.Day, "Tag", CalendarMode.Day, "Ctrl+Alt+D1"),
                     (CalendarCommandIds.WorkWeek, "Arbeitswoche", CalendarMode.WorkWeek, "Ctrl+Alt+D2"),
                     (CalendarCommandIds.Week, "Woche", CalendarMode.Week, "Ctrl+Alt+D3"),
                     (CalendarCommandIds.Month, "Monat", CalendarMode.Month, "Ctrl+Alt+D4"),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => View()!.SetMode(mode))
            {
                Icon = mode == CalendarMode.Month ? "grid" : "columns",
                Shortcut = key,
                Description = $"Zeigt den Kalender als {label}.",
                Keywords = ["ansicht", "anordnen", label.ToLowerInvariant()],
                CanExecute = InCalendar,
                IsChecked = () => View()?.Mode == mode,
            });
        }

        commands.Add(new OfficeCommand(CalendarCommandIds.Sync, "Jetzt synchronisieren", () => SyncAsync(window, Editor()!))
        {
            Icon = "refresh",
            Shortcut = "F9",
            Description = "Gleicht den Kalender und die Personen jetzt mit der ThixCloud ab.",
            Keywords = ["synchronisieren", "sync", "cloud", "abgleichen", "aktualisieren"],
            CanExecute = () => InCalendar() && Editor()!.Document.Pim.IsSyncing,
        });

        commands.Add(new OfficeCommand(CalendarCommandIds.Import, "Importieren …", () => ImportAsync(window, Editor()!))
        {
            Icon = "download",
            Description = "Übernimmt Termine aus einer iCalendar-Datei (.ics) – etwa aus ThixCalendar, Outlook oder Google – in Ihren Kalender.",
            Keywords = ["importieren", "ics", "icalendar", "übernehmen", "termine"],
            CanExecute = InCalendar,
        });
        commands.Add(new OfficeCommand(CalendarCommandIds.Export, "Exportieren …", () => ExportAsync(window, Editor()!))
        {
            Icon = "upload",
            Description = "Speichert die Termine Ihrer eigenen Kalender als iCalendar-Datei (.ics) – für ThixCalendar, Outlook oder Google.",
            Keywords = ["exportieren", "ics", "icalendar", "speichern", "sichern"],
            CanExecute = () => InCalendar() && Editor()!.Document.Pim.Calendar!.WritableCalendars.Count > 0,
        });

        PeopleCommands.Add(window, () => Editor() is { ActiveView: MailArea.People, PeopleView: { } view } editor && editor.Document.Pim.IsAvailable ? view : null, new PeopleCommandOptions
        {
            Email = addresses => MailCommands.ComposeToAddressesAsync(window, addresses),
            Sync = () => SyncAsync(window, Editor()!),
            CanSync = () => Editor()?.Document.Pim.IsSyncing == true,
        });
    }

    /// <summary>
    /// The tabs of the module "Kalender" (shown instead of the mail tabs while it is active, like Outlook): Start (Neu, Gehe zu, Anordnen, Aktionen,
    /// Kalender verwalten) | Ansicht (Anordnung, Navigation, Menüband).
    /// </summary>
    public static IReadOnlyList<MatRibbonTab> Tabs() =>
    [
        new MatRibbonTab("calendar.home", "Start").Add(
            new MatRibbonGroup("calendar.home.new", "Neu") { Icon = "plus", ShrinkOrder = 9 }.Add(
                Button(CalendarCommandIds.New, MatRibbonSize.Large),
                Button(CalendarCommandIds.NewMeeting, MatRibbonSize.Large),
                MailNewItems.MenuOf("calendar")),
            new MatRibbonGroup("calendar.home.goto", "Gehe zu") { Icon = "calendar", ShrinkOrder = 4 }.Add(
                Button(CalendarCommandIds.Today, MatRibbonSize.Large),
                Button(CalendarCommandIds.Previous),
                Button(CalendarCommandIds.Next)),
            new MatRibbonGroup("calendar.home.arrange", "Anordnen") { Icon = "columns", ShrinkOrder = 3 }.Add(
                Toggle(CalendarCommandIds.Day, "arrange", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.WorkWeek, "arrange", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.Week, "arrange", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.Month, "arrange", MatRibbonSize.Large)),
            new MatRibbonGroup("calendar.home.actions", "Aktionen") { Icon = "edit", ShrinkOrder = 2 }.Add(
                Button(CalendarCommandIds.Open, MatRibbonSize.Large),
                Button(CalendarCommandIds.Delete, MatRibbonSize.Large)),
            new MatRibbonGroup("calendar.home.manage", "Kalender verwalten") { Icon = "calendar", ShrinkOrder = 1 }.Add(
                Button(CalendarCommandIds.Import),
                Button(CalendarCommandIds.Export),
                Button(MailCommandIds.Print + ".calendar"))),
        new MatRibbonTab("calendar.view", "Ansicht").Add(
            new MatRibbonGroup("calendar.view.arrange", "Anordnung") { Icon = "columns" }.Add(
                Toggle(CalendarCommandIds.Day + ".view", "arrange.view", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.WorkWeek + ".view", "arrange.view", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.Week + ".view", "arrange.view", MatRibbonSize.Large),
                Toggle(CalendarCommandIds.Month + ".view", "arrange.view", MatRibbonSize.Large)),
            MailRibbon.NavigationGroup("calendar"),
            MailRibbon.RibbonGroup("calendar")),
    ];

    /// <summary>A new appointment (or meeting) at <paramref name="start"/>: the dialog, then the book – or the invitations through the outbox.</summary>
    public static async Task NewAsync(OfficeWindow window, MailEditor editor, DateTime start, bool allDay, bool meeting, IReadOnlyList<string>? attendees = null)
    {
        if (editor.Document.Pim.Calendar is not { } book)
        {
            return;
        }

        var calendar = book.WritableCalendars.FirstOrDefault()?.Key ?? "";
        var appointment = new CalendarAppointment
        {
            CalendarKey = calendar,
            Start = allDay ? CalendarAppointment.AllDayDate(start.Date) : CalendarAppointment.FromLocal(start),
            End = allDay ? CalendarAppointment.AllDayDate(start.Date.AddDays(1)) : CalendarAppointment.FromLocal(start.AddMinutes(30)),
            IsAllDay = allDay,
            ReminderMinutes = allDay ? null : 15,
        };
        var dialog = Dialog(editor, book, appointment, meeting);
        if (attendees is { Count: > 0 })
        {
            dialog.Attendees.Box.Text = string.Join("; ", attendees);
        }

        if (await dialog.ShowAsync(window) && dialog.Saved is { } saved)
        {
            editor.CalendarView?.Refresh();
            editor.CalendarView?.Select(book.Find(saved.Key));
        }
    }

    /// <summary>Kalender › Importieren: the appointments of an .ics file go into the first own calendar (made when there is none), as one undo step.</summary>
    public static async Task ImportAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Document.Pim.Calendar is not { } book
            || await OfficeApplication.FileDialogs.PickFileAsync(window, "Termine importieren", SettingsService.SaveFolder, "iCalendar-Datei", [".ics", ".ical", ".ifb", ".icalendar"]) is not { } path)
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var target = book.DefaultCalendar();
            var found = CalendarIcs.Import(text, target.Key);
            if (found.Appointments.Count == 0)
            {
                await QuestionDialog.ShowMessageAsync(window, "Termine importieren", "Die Datei enthält keine Termine, die ThixMail übernehmen kann.");
                return;
            }

            var saved = editor.Document.ImportAppointments(found.Appointments);
            editor.CalendarView?.Refresh();
            if (saved.Count > 0)
            {
                editor.CalendarView?.GoTo(saved.Min(a => a.StartLocal).Date);
            }

            var series = found.Series > 0 ? $" {found.Series} Serien kamen mit ihrem ersten Termin (Serien kennt ThixMail noch nicht)." : "";
            window.ShowMessage((saved.Count == 1 ? "1 Termin wurde" : $"{saved.Count} Termine wurden") + $" in „{target.Name}“ übernommen.{series}", InfoSeverity.Success, isClosable: true,
                "Rückgängig", () => window.Document.Undo());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await QuestionDialog.ShowMessageAsync(window, "Termine importieren", $"Die Datei konnte nicht gelesen werden: {exception.Message}");
        }
    }

    /// <summary>Kalender › Exportieren: the appointments of the own calendars as one .ics file (written atomically).</summary>
    public static async Task ExportAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Document.Pim.Calendar is not { } book
            || await OfficeApplication.FileDialogs.PickExportTargetAsync(window, "Kalender.ics", SettingsService.SaveFolder, "iCalendar-Datei", ".ics") is not { } path)
        {
            return;
        }

        var own = book.WritableCalendars.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var appointments = book.Appointments.Where(a => own.Contains(a.CalendarKey)).ToList();
        try
        {
            var text = new System.Text.UTF8Encoding(false).GetBytes(CalendarIcs.Export(appointments, book.WritableCalendars.FirstOrDefault()?.Name ?? "Kalender"));
            DocumentFiles.SaveAtomic(path, stream => stream.Write(text));
            window.ShowMessage((appointments.Count == 1 ? "1 Termin wurde" : $"{appointments.Count} Termine wurden") + $" als „{Path.GetFileName(path)}“ gespeichert.", InfoSeverity.Success, isClosable: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await QuestionDialog.ShowMessageAsync(window, "Kalender exportieren", $"Die Datei konnte nicht gespeichert werden: {exception.Message}");
        }
    }

    /// <summary>Opens an appointment: its dialog (read-only for a calendar MatCloud keeps in step).</summary>
    public static async Task OpenAsync(OfficeWindow window, MailEditor editor, CalendarAppointment appointment)
    {
        if (editor.Document.Pim.Calendar is not { } book)
        {
            return;
        }

        var meeting = book.MeetingOf(appointment.Key) is not null;
        var dialog = Dialog(editor, book, appointment, meeting);
        if (meeting && book.MeetingOf(appointment.Key) is { } known)
        {
            dialog.Attendees.Box.Text = string.Join("; ", known.Attendees.Select(a => a.Name.Length > 0 ? $"{a.Name} <{a.Email}>" : a.Email));
        }

        if (await dialog.ShowAsync(window) && dialog.Saved is { } saved)
        {
            editor.CalendarView?.Refresh();
            editor.CalendarView?.Select(book.Find(saved.Key));
        }
    }

    private static AppointmentDialog Dialog(MailEditor editor, CalendarBook book, CalendarAppointment appointment, bool meeting)
    {
        var account = editor.Document.Accounts.Accounts.FirstOrDefault();
        return new AppointmentDialog(book, appointment,
            (value, attendees) =>
            {
                if (attendees.Count == 0 || account is null)
                {
                    return book.Save(value);
                }

                var addresses = attendees.Select(Address).ToList();
                return MailInvitations.SendMeeting(editor.Document, value, addresses, account);
            },
            text => [.. editor.Document.SuggestAddresses(text).Select(a => a.Full)],
            asMeeting: meeting && account is not null);

        static MailAddress Address(string entry) =>
            MimeKit.MailboxAddress.TryParse(entry, out var mailbox) ? new MailAddress(mailbox.Name ?? "", mailbox.Address) : new MailAddress("", entry.Trim());
    }

    private static async Task DeleteAsync(OfficeWindow window, MailEditor editor, CalendarAppointment appointment)
    {
        var meeting = editor.Document.Pim.Calendar?.MeetingOf(appointment.Key);
        var question = meeting is { Attendees.Count: > 0 }
            ? $"„{appointment.Title}“ löschen? Die {meeting.Attendees.Count} Teilnehmer bekommen eine Absage."
            : $"„{appointment.Title}“ löschen?";
        if (!await QuestionDialog.ConfirmAsync(window, "Termin löschen", question, "Löschen", isDestructive: true))
        {
            return;
        }

        MailInvitations.DeleteAppointment(editor.Document, appointment.Key, editor.Document.Accounts.Accounts.FirstOrDefault(a => a.Address.Equals(meeting?.Organizer.Email, StringComparison.OrdinalIgnoreCase)) ?? editor.Document.Accounts.Accounts.FirstOrDefault());
        editor.CalendarView?.Select(null);
        editor.CalendarView?.Refresh();
        window.ShowMessage($"„{appointment.Title}“ wurde gelöscht.", InfoSeverity.Info, isClosable: true);
    }

    private static async Task SyncAsync(OfficeWindow window, MailEditor editor)
    {
        await editor.Document.Pim.SyncNowAsync();
        editor.UpdateStatus(window.StatusBar);
    }
}

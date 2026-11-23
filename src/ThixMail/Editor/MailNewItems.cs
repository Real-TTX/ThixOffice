using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop.People;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixMail.Editor;

/// <summary>
/// Outlook's "Neue Elemente" from every module: E-Mail-Nachricht (Strg+Umschalt+M), Termin (Strg+Umschalt+A), Besprechung (Strg+Umschalt+Q), Kontakt
/// (Strg+Umschalt+C) – the keys work in E-Mail, Kalender and Personen alike, the menu is on each module's Start tab. Also the second places of a command
/// in another module's tabs (aliases: the ribbon's ids are unique), and "Besprechung" with the people selected in Personen.
/// </summary>
public static class MailNewItems
{
    public const string Menu = "mail.newItems";
    public const string Mail = "mail.newItem.mail";
    public const string Appointment = "mail.newItem.appointment";
    public const string Meeting = "mail.newItem.meeting";
    public const string Contact = "mail.newItem.contact";

    /// <summary>The modules besides E-Mail whose tabs carry a second place of a command (the alias id is the command's id + "." + the module).</summary>
    public static readonly string[] OtherModules = ["calendar", "people"];

    public static void Add(OfficeWindow window)
    {
        MailEditor? Editor() => window.Editor as MailEditor;
        bool HasAccount() => Editor() is { Document.IsClosed: false } editor && editor.Document.Accounts.Accounts.Count > 0;
        bool HasCalendar() => Editor() is { Document.IsClosed: false, Document.Pim.Calendar: not null };
        var commands = window.Commands;

        commands.Add(new OfficeCommand(Mail, "E-Mail-Nachricht", () => MailCommands.NewMailAsync(window))
        {
            Icon = "mail",
            Shortcut = "Ctrl+Shift+M",
            MacShortcut = "Ctrl+Alt+N", // ⇧⌘M moves messages on the Mac (Outlook for Mac's key)
            Description = "Schreibt eine neue E-Mail – aus jeder Ansicht.",
            Keywords = ["neu", "e-mail", "nachricht", "schreiben", "neue elemente"],
            CanExecute = HasAccount,
        });
        commands.Add(new OfficeCommand(Appointment, "Termin", () => CalendarCommands.NewAsync(window, Editor()!, NextStart(Editor()!), allDay: false, meeting: false))
        {
            Icon = "calendar",
            Shortcut = "Ctrl+Shift+A",
            Description = "Trägt einen neuen Termin in den Kalender ein – aus jeder Ansicht.",
            Keywords = ["neu", "termin", "kalender", "neue elemente"],
            CanExecute = HasCalendar,
        });
        commands.Add(new OfficeCommand(Meeting, "Besprechung", () => CalendarCommands.NewAsync(window, Editor()!, NextStart(Editor()!), allDay: false, meeting: true))
        {
            Icon = "users",
            Shortcut = "Ctrl+Shift+Q",
            Description = "Lädt zu einer Besprechung ein – aus jeder Ansicht: der Termin kommt in Ihren Kalender, die Teilnehmer bekommen eine Einladung.",
            Keywords = ["neu", "besprechung", "einladung", "meeting", "neue elemente"],
            CanExecute = () => HasCalendar() && HasAccount(),
        });
        commands.Add(new OfficeCommand(Contact, "Kontakt", () => NewContactAsync(window, Editor()!))
        {
            Icon = "user",
            Shortcut = "Ctrl+Shift+C",
            MacShortcut = "Ctrl+Alt+C",
            Description = "Legt einen neuen Kontakt an – aus jeder Ansicht.",
            Keywords = ["neu", "kontakt", "person", "neue elemente"],
            CanExecute = () => Editor() is { Document.IsClosed: false, Document.Pim.People: not null },
        });
        commands.Add(new OfficeCommand(PeopleRibbon.MeetingCommandId, "Besprechung", () => MeetingWithPeopleAsync(window, Editor()!))
        {
            Icon = "calendar",
            Description = "Lädt die ausgewählten Personen zu einer Besprechung ein.",
            Keywords = ["besprechung", "einladen", "termin", "kontakt"],
            CanExecute = () => Editor() is { ActiveView: MailArea.People, PeopleView.Selected.Count: > 0 } && HasCalendar() && HasAccount(),
            IsSearchable = false,
        });
    }

    /// <summary>The "Neue Elemente" menu of a module's Start tab (<paramref name="module"/>: null for E-Mail, else the alias suffix).</summary>
    public static MatRibbonMenuButton MenuOf(string? module)
    {
        string Id(string id) => module is null ? id : $"{id}.{module}";
        var items = new List<MatRibbonItem> { Button(Id(Mail)), Button(Id(Appointment)), Button(Id(Meeting)), Button(Id(Contact)) };
        if (module is null)
        {
            items.Add(new MatRibbonSeparator());
            items.Add(Button(MailCommandIds.NewFromTemplate));
        }

        return new MatRibbonMenuButton(Id(Menu), "Neue Elemente", "plus", [.. items])
        {
            Size = MatRibbonSize.Large,
            ToolTip = "Eine neue E-Mail, einen Termin, eine Besprechung oder einen Kontakt anlegen.",
        };
    }

    /// <summary>
    /// Second places of commands in the tabs of another module (Kalender, Personen): commands of their own with the original's label, icon, state and
    /// action – the ribbon's ids are unique. Not searchable (the original is).
    /// </summary>
    public static void AddAliases(OfficeWindow window)
    {
        var commands = window.Commands;
        var shared = new[] { Mail, Appointment, Meeting, Contact, CalendarCommandIds.ShowMail, CalendarCommandIds.ShowCalendar, CalendarCommandIds.ShowPeople, CommonCommandIds.ToggleRibbon };
        foreach (var module in OtherModules)
        {
            foreach (var id in shared)
            {
                Alias(commands, id, $"{id}.{module}");
            }
        }

        Alias(commands, MailCommandIds.Print, MailCommandIds.Print + ".calendar");
        foreach (var id in new[] { CalendarCommandIds.Day, CalendarCommandIds.WorkWeek, CalendarCommandIds.Week, CalendarCommandIds.Month })
        {
            Alias(commands, id, id + ".view");
        }
    }

    private static void Alias(OfficeCommandRegistry commands, string id, string alias)
    {
        if (commands.Find(id) is { } command && commands.Find(alias) is null)
        {
            commands.Add(new OfficeCommand(alias, command.Label, () => command.ExecuteAsync())
            {
                Icon = command.Icon,
                CanExecute = () => command.IsEnabled,
                IsChecked = command.IsChecked,
                Description = command.Description,
                IsSearchable = false,
            });
        }
    }

    /// <summary>The start of a new appointment: the calendar's chosen time while it is shown, else the next half hour today.</summary>
    private static DateTime NextStart(MailEditor editor)
    {
        if (editor is { ActiveView: MailArea.Calendar, CalendarView: { } view })
        {
            return view.NewStart();
        }

        var now = DateTime.Now;
        return now.Date.AddHours(now.Hour).AddMinutes(now.Minute < 30 ? 30 : 60);
    }

    private static async Task NewContactAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Document.Pim.People is not { } people)
        {
            return;
        }

        var dialog = new ContactDialog(people, null);
        if (await dialog.ShowAsync(window))
        {
            editor.PeopleView?.Refresh();
            window.ShowMessage("Der Kontakt wurde gespeichert.", InfoSeverity.Success, isClosable: true);
        }
    }

    /// <summary>Personen › Besprechung: a meeting with the selected people (their first addresses) as participants.</summary>
    private static async Task MeetingWithPeopleAsync(OfficeWindow window, MailEditor editor)
    {
        var people = editor.PeopleView?.Selected ?? [];
        var addresses = people.Where(p => p.Email.Length > 0).Select(p => $"{p.Name} <{p.Email}>").ToList();
        if (addresses.Count == 0)
        {
            await QuestionDialog.ShowMessageAsync(window, "Besprechung", "Die ausgewählten Personen haben keine E-Mail-Adresse.");
            return;
        }

        await CalendarCommands.NewAsync(window, editor, NextStart(editor), allDay: false, meeting: true, attendees: addresses);
    }
}

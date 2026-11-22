using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using MatCloud.Contract.Contacts;
using ThixMail.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Desktop.Calendar;
using CalendarMode = ThixOffice.Pim.Desktop.Calendar.CalendarMode;
using ThixOffice.Pim.Desktop.People;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>7c in the real ThixMail through keys and the mouse: Mail | Kalender | Personen, the sign-in without ThixCloud, appointments, contacts, invitations.</summary>
public sealed class EmbeddingUiTests
{
    private static async Task<SampleCloud> SignInAsync(OfficeWindowLike window)
    {
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        await UntilAsync(() => window.Editor.Document.Pim.IsAvailable, "the calendar and people of the account");
        return cloud;
    }

    private sealed record OfficeWindowLike(MailEditor Editor);

    [AvaloniaFact]
    public async Task Without_ThixCloud_Kalender_and_Personen_explain_and_offer_the_sign_in_and_Strg_1_brings_the_mail_back()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        Assert.Equal(3, editor.NavigationButtons.Count(b => b.IsVisible));

        await PressAsync(window, PhysicalKey.Digit2, CommandKey);
        Assert.Equal(MailArea.Calendar, editor.ActiveView);
        Assert.NotNull(editor.SignInPanel);
        Assert.Null(editor.CalendarView);
        Assert.Equal("Nicht bei der ThixCloud angemeldet", window.StatusBar.TextOf(MailEditor.CountItem));

        await ClickAsync(window, editor.NavigationButtons[2]);
        Assert.Equal(MailArea.People, editor.ActiveView);
        Assert.NotNull(editor.SignInPanel);

        await PressAsync(window, PhysicalKey.Digit1, CommandKey);
        Assert.Equal(MailArea.Mail, editor.ActiveView);
        Assert.True(editor.List.IsEffectivelyVisible);
        await PressAsync(window, PhysicalKey.N, CommandKey);
        await OwnedAsync<Compose.ComposeWindow>(window); // Strg+N is a new mail again
    }

    [AvaloniaFact]
    public async Task An_appointment_is_made_with_Strg_N_in_the_calendar_shown_in_the_week_synced_and_deleted_with_Entf()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var cloud = await SignInAsync(new OfficeWindowLike(editor));

        await PressAsync(window, PhysicalKey.Digit2, CommandKey);
        var view = editor.CalendarView!;
        Assert.Contains(MailEditor.CalendarContext, window.RibbonDefinition.ActiveContexts);
        Assert.DoesNotContain(MailEditor.PeopleContext, window.RibbonDefinition.ActiveContexts);

        await PressAsync(window, PhysicalKey.Digit3, CommandKey | RawInputModifiers.Alt); // Woche
        Assert.Equal(CalendarMode.Week, view.Mode);
        Assert.Equal(7, view.Days.Count);

        await PressAsync(window, PhysicalKey.N, CommandKey);
        var dialog = await OwnedAsync<AppointmentDialog>(window);
        dialog.TitleBox.Focus();
        await TypeAsync(dialog, "Lesung im Rathaus");
        dialog.LocationBox.Focus();
        await TypeAsync(dialog, "Rathaus");
        await ClickAsync(dialog, dialog.OkButton);
        await UntilAsync(() => view.Shown.Any(a => a.Title == "Lesung im Rathaus"), "the appointment in the week");
        var saved = view.Shown.Single(a => a.Title == "Lesung im Rathaus");
        Assert.Equal(DateTime.Today.AddHours(9), saved.StartLocal); // today (in the week shown) at 9:00 – no time was chosen
        Assert.Equal("Rathaus", saved.Location);
        Assert.Same(saved.Key, view.Selected?.Key ?? saved.Key);

        await UntilAsync(() => cloud.SyncItem(CloudApps.Calendar, CalendarContract.Events, saved.Key) is { IsDeleted: false }, "the appointment in ThixCloud");

        view.Select(saved);
        view.Focus();
        await PressAsync(window, PhysicalKey.Delete);
        var question = await OwnedAsync<QuestionDialog>(window);
        question.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Löschen
        await UntilAsync(() => view.Shown.All(a => a.Title != "Lesung im Rathaus"), "the appointment gone");
        await UntilAsync(() => cloud.SyncItem(CloudApps.Calendar, CalendarContract.Events, saved.Key) is { IsDeleted: true }, "the tombstone in ThixCloud");
    }

    [AvaloniaFact]
    public async Task A_double_click_in_the_hours_starts_an_appointment_there_and_a_wrong_time_is_shown_at_its_field()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        await SignInAsync(new OfficeWindowLike(editor));
        await PressAsync(window, PhysicalKey.Digit2, CommandKey);
        var view = editor.CalendarView!;
        view.SetMode(CalendarMode.Day);
        await SettleAsync();

        var hours = view.GetVisualDescendants().OfType<Canvas>().Single(c => c.Bounds.Height >= 24 * CalendarView.HourHeight - 1 && c.Background is not null);
        var point = hours.TranslatePoint(new Avalonia.Point(200, (15 * CalendarView.HourHeight) + 5), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await SettleAsync();
        var dialog = await OwnedAsync<AppointmentDialog>(window);
        Assert.Equal("15:00", dialog.StartTime.Text);
        Assert.Equal("15:30", dialog.EndTime.Text);

        dialog.TitleBox.Text = "Kaffee";
        dialog.EndTime.Text = "14:00";
        dialog.Confirm();
        Assert.Equal("Das Ende liegt vor dem Beginn.", dialog.ErrorText);
        dialog.EndTime.Text = "nachher";
        dialog.Confirm();
        Assert.Equal("Bitte eine Uhrzeit wie 15:00 eingeben.", dialog.ErrorText);
        dialog.EndTime.Text = "1600";
        dialog.Confirm();
        await UntilAsync(() => view.Shown.Any(a => a.Title == "Kaffee" && a.EndLocal.Hour == 16), "the appointment until 16:00");
    }

    [AvaloniaFact]
    public async Task A_contact_made_in_Personen_reaches_ThixContacts_data_in_ThixCloud_and_suggests_its_address_in_the_composer()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var cloud = await SignInAsync(new OfficeWindowLike(editor));

        await PressAsync(window, PhysicalKey.Digit3, CommandKey);
        Assert.Contains(MailEditor.PeopleContext, window.RibbonDefinition.ActiveContexts);
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var dialog = await OwnedAsync<ContactDialog>(window);
        dialog.GivenName.Text = "Ida";
        dialog.FamilyName.Text = "Sommer";
        dialog.Emails.Rows[0].Box.Text = "ida.sommer@beispiel.de";
        await ClickAsync(dialog, dialog.OkButton);
        await UntilAsync(() => editor.PeopleView!.Shown.Any(p => p.Name == "Ida Sommer"), "the contact in the list");
        Assert.Equal("1 Kontakt", window.StatusBar.TextOf(MailEditor.CountItem));
        await UntilAsync(() => cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts).Count == 1, "the contact in ThixCloud");

        await PressAsync(window, PhysicalKey.Digit1, CommandKey);
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var compose = await OwnedAsync<Compose.ComposeWindow>(window);
        Assert.Contains(editor.Document.SuggestAddresses("ida"), address => address.Address == "ida.sommer@beispiel.de");
        compose.CloseDiscarding();
    }

    [AvaloniaFact]
    public async Task An_invitation_shows_its_bar_with_the_overlap_and_Zusagen_enters_it_and_answers_the_organizer()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        await SignInAsync(new OfficeWindowLike(editor));
        var book = editor.Document.Pim.Calendar!;
        var workshop = SampleMailbox.WorkshopStart();
        book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Angebot fertigstellen", Start = workshop.AddMinutes(30), End = workshop.AddMinutes(90) });

        var invitation = Header(editor, "Einladung: Workshop");
        editor.Select(invitation.Id);
        await UntilAsync(() => editor.Reader.Invitation is not null, "the bar of the invitation");
        Assert.Contains("Überschneidet sich mit: Angebot fertigstellen", editor.Reader.InvitationText, StringComparison.Ordinal);
        Assert.Equal(["Zusagen", "Mit Vorbehalt", "Absagen"], editor.Reader.InvitationButtons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));

        await ClickAsync(window, editor.Reader.InvitationButtons[0]);
        await UntilAsync(() => book.Find(CalendarBook.KeyOfInvitation("workshop-nordlicht@beispiel.de")) is not null, "the workshop in the calendar");
        Assert.Contains("Im Kalender: zugesagt.", editor.Reader.InvitationText, StringComparison.Ordinal);
        Assert.Contains("Zugesagt", window.MessageBar.Text, StringComparison.Ordinal);
        await UntilAsync(() => data.Server.Sent.Any(m => ThixMail.Core.Mime.MailReader.Read(m.Raw).Subject == "Zugesagt: Workshop Nordlicht"), "the answer at the server");
        var reply = ThixMail.Core.Mime.MailReader.Read(data.Server.Sent.Last().Raw);
        Assert.Equal(("REPLY", "jonas.weber@beispiel.de"), (reply.Calendar!.Method, reply.To.Single().Address));

        await PressAsync(window, PhysicalKey.Digit2, CommandKey);
        editor.CalendarView!.GoTo(workshop.LocalDateTime.Date);
        await SettleAsync();
        Assert.Contains(editor.CalendarView.Shown, a => a.Title == "Workshop Nordlicht");
    }

    [AvaloniaFact]
    public async Task Zu_Kontakten_hinzufuegen_makes_the_sender_a_contact_with_name_and_address()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var command = window.Commands[MailCommandIds.AddSenderToContacts];
        editor.Select(Header(editor, "Präsentation für Donnerstag").Id);
        await SettleAsync();
        Assert.False(command.IsEnabled); // no ThixCloud: no contacts

        await SignInAsync(new OfficeWindowLike(editor));
        editor.Select(Header(editor, "Präsentation für Donnerstag").Id);
        await SettleAsync();
        Assert.True(command.IsEnabled);
        var running = ThixOffice.Desktop.Shell.OfficeWindow.RunCommandAsync(window, command);
        var dialog = await OwnedAsync<ContactDialog>(window);
        Assert.Equal(("Maria", "Schulz", "maria.schulz@kunde.example"), (dialog.GivenName.Text, dialog.FamilyName.Text, dialog.Emails.Rows[0].Box.Text));
        await ClickAsync(dialog, dialog.OkButton);
        await running;
        Assert.Contains(editor.Document.Pim.People!.People, p => p.Name == "Maria Schulz" && p.Email == "maria.schulz@kunde.example");
    }
}

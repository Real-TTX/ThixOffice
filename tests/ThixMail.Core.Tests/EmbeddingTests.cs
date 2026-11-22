using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>
/// 7c: ThixMail's calendar and people through ThixCloud's general sync (grants mail → contacts / calendar) against SampleCloud and SampleMailServer –
/// two computers, a contact made in ThixMail in ThixContacts' model, an invitation answered (calendar + REPLY), a meeting sent and called off.
/// </summary>
public sealed class EmbeddingTests
{
    private static async Task<SampleCloud> SignInAsync()
    {
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        return cloud;
    }

    [Fact]
    public async Task Without_ThixCloud_there_is_no_calendar_and_no_people_and_mail_works()
    {
        using var data = new TestData();
        var document = new MailDocument("E-Mail");
        try
        {
            Assert.False(document.Pim.IsAvailable);
            Assert.Null(document.Pim.Calendar);
            await Assert.ThrowsAsync<BusinessException>(document.Pim.SyncNowAsync);
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task A_contact_made_in_ThixMail_arrives_in_ThixContacts_model_on_another_computer_and_back()
    {
        using var data = new TestData();
        var cloud = await SignInAsync();
        var document = new MailDocument("E-Mail");
        try
        {
            Assert.True(document.Pim.IsAvailable);
            var id = document.Pim.People!.Save(null, new ContactData { GivenName = "Lena", FamilyName = "Koch", Emails = [new ContactValue("work", "lena.koch@beispiel.de")] });
            await document.Pim.SyncNowAsync();
            Assert.NotNull(cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, id));

            // ThixContacts on the laptop: its own app's sync, its own session over its store
            using var laptop = PimStore.Open(Path.Combine(data.Folder, "laptop-contacts.db"));
            await using var laptopSync = new PimSync(laptop, CloudService.CreateClient, app: CloudApps.Contacts);
            await laptopSync.SyncOnceAsync();
            using var contacts = new ContactsSession(laptop);
            var lena = Assert.Single(contacts.People);
            Assert.Equal(("Lena Koch", "lena.koch@beispiel.de"), (lena.Name, lena.Email));

            contacts.Save(lena.Id, lena.Data with { Company = "Beispiel GmbH" });
            await laptopSync.SyncOnceAsync();
            await document.Pim.SyncNowAsync();
            Assert.Equal("Beispiel GmbH", document.Pim.People.Find(id)!.Data.Company);
            Assert.Contains(document.SuggestAddresses("len"), address => address.Address == "lena.koch@beispiel.de");
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task An_invitation_is_answered_into_the_calendar_and_the_organizer_gets_the_REPLY()
    {
        using var data = new TestData();
        var cloud = await SignInAsync();
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(Account, Password, null);
            var mail = MailReader.Read(Fixture("07-calendar-invite.eml"));
            var before = document.Pim.Calendar!.DefaultCalendar();
            document.Pim.Calendar.Save(new CalendarAppointment { CalendarKey = before.Key, Title = "Zahnarzt", Start = new DateTimeOffset(2026, 11, 26, 13, 30, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, 11, 26, 14, 30, 0, TimeSpan.Zero) });

            var view = MailInvitations.View(document, mail, Account.Id)!;
            Assert.True(view.CanAnswer);
            Assert.Equal("Projektbesprechung Nordlicht", view.Invitation.Summary);
            Assert.Equal("Zahnarzt", Assert.Single(view.Conflicts).Title);
            Assert.Null(view.Entered);

            var entered = MailInvitations.Answer(document, view, InvitationAnswer.Accept)!;
            Assert.Equal(("Projektbesprechung Nordlicht", "Raum 2.14", CalendarEventStatus.Confirmed), (entered.Title, entered.Location, entered.Status));
            Assert.NotNull(MailInvitations.View(document, mail, Account.Id)!.Entered);

            await UntilAsync(() => data.Server.Sent.Count == 1, "the answer at the server");
            var reply = MailReader.Read(data.Server.Sent[0].Raw);
            Assert.Equal("Zugesagt: Projektbesprechung Nordlicht", reply.Subject);
            Assert.Equal("jonas.weber@example.de", reply.To.Single().Address);
            Assert.Equal("REPLY", reply.Calendar!.Method);
            var answer = Invitations.Read(reply.Calendar.Text)!;
            Assert.Equal(("termin-07@example.de", "ACCEPTED"), (answer.Uid, answer.Attendees.Single().Status));

            await document.Pim.SyncNowAsync();
            var item = cloud.SyncItem(CloudApps.Calendar, CalendarContract.Events, CalendarBook.KeyOfInvitation("termin-07@example.de"));
            Assert.Contains("Projektbesprechung Nordlicht", item!.Data, StringComparison.Ordinal);

            // declining later takes it out again; answering twice stays one appointment
            MailInvitations.Answer(document, MailInvitations.View(document, mail, Account.Id)!, InvitationAnswer.Tentative);
            Assert.Single(document.Pim.Calendar.Appointments, a => a.Title == "Projektbesprechung Nordlicht");
            MailInvitations.Answer(document, MailInvitations.View(document, mail, Account.Id)!, InvitationAnswer.Decline);
            Assert.DoesNotContain(document.Pim.Calendar.Appointments, a => a.Title == "Projektbesprechung Nordlicht");
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task A_meeting_made_in_the_calendar_invites_its_participants_and_deleting_it_calls_it_off()
    {
        using var data = new TestData();
        await SignInAsync();
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(Account, Password, null);
            var book = document.Pim.Calendar!;
            var start = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
            Assert.Throws<BusinessException>(() => MailInvitations.SendMeeting(document, new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Planung", Start = start, End = start.AddHours(1) }, [], Account));

            var saved = MailInvitations.SendMeeting(document, new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Planung", Start = start, End = start.AddHours(1), Location = "Raum 1" },
                [Jonas, new MailAddress("Lena Koch", "lena.koch@beispiel.de")], Account);
            await UntilAsync(() => data.Server.Sent.Count == 1, "the invitation at the server");
            var request = MailReader.Read(data.Server.Sent[0].Raw);
            Assert.Equal("Einladung: Planung", request.Subject);
            Assert.Equal(2, request.To.Count);
            var meeting = Invitations.Read(request.Calendar!.Text)!;
            Assert.Equal((InvitationMethod.Request, "anna@example.de", 2), (meeting.Method, meeting.Organizer.Email, meeting.Attendees.Count));

            MailInvitations.DeleteAppointment(document, saved.Key, Account);
            await UntilAsync(() => data.Server.Sent.Count == 2, "the cancellation at the server");
            var cancel = MailReader.Read(data.Server.Sent[1].Raw);
            Assert.Equal(("Abgesagt: Planung", "CANCEL"), (cancel.Subject, cancel.Calendar!.Method));
            Assert.Equal(meeting.Uid, Invitations.Read(cancel.Calendar.Text)!.Uid);
            Assert.Null(book.Find(saved.Key));
        }
        finally
        {
            await document.CloseAsync();
        }
    }
}

using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixMail.Core.Mime;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>
/// Kalender and Personen work without ThixCloud, like Outlook: the contacts are ThixContacts' on this computer (one file, both apps), the calendar is
/// ThixMail's own; signing in takes both into the account without doubling, signing out keeps a copy (or not), and .ics moves appointments.
/// </summary>
public sealed class LocalFirstTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 30, 9, 0, 0, TimeSpan.Zero);

    private static async Task<SampleCloud> SignInAsync(SampleCloud? cloud = null)
    {
        cloud ??= new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        return cloud;
    }

    [Fact]
    public async Task Without_ThixCloud_the_calendar_and_the_people_of_this_computer_work_and_the_contacts_are_ThixContacts_own()
    {
        using var data = new TestData();
        var document = new MailDocument("E-Mail");
        try
        {
            Assert.True(document.Pim.IsAvailable);
            Assert.False(document.Pim.IsAccountData);
            await Assert.ThrowsAsync<BusinessException>(document.Pim.SyncNowAsync); // nothing to sync with

            var id = document.Pim.People!.Save(null, new ContactData { GivenName = "Lena", FamilyName = "Koch", Emails = [new ContactValue("work", "lena.koch@beispiel.de")] });
            Assert.Equal(PimStores.LocalPath(ThixContactsApp.StoreName, MailPim.ContactsDirectory), document.Pim.People.Store.Path);
            Assert.Contains(document.SuggestAddresses("len"), address => address.Address == "lena.koch@beispiel.de");

            // ThixContacts on the same computer opens the same file and sees Lena; its change comes back to ThixMail
            using var contactsStore = PimStore.Open(PimStores.LocalPath(ThixContactsApp.StoreName, MailPim.ContactsDirectory));
            using var contacts = new ContactsSession(contactsStore);
            Assert.Equal("Lena Koch", Assert.Single(contacts.People).Name);
            contacts.Save(id, contacts.Find(id)!.Data with { Company = "Beispiel GmbH" });
            document.Pim.CheckForOtherChanges();
            Assert.Equal("Beispiel GmbH", document.Pim.People.Find(id)!.Data.Company);

            // the calendar: "Kalender" on first use
            var book = document.Pim.Calendar!;
            Assert.Empty(book.Calendars);
            var saved = book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Zahnarzt", Start = Monday, End = Monday.AddHours(1) });
            Assert.Equal("Kalender", Assert.Single(book.Calendars).Name);
            Assert.Equal("Zahnarzt", book.Find(saved.Key)!.Title);
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task Without_ThixCloud_an_invitation_is_answered_into_the_calendar_of_this_computer_with_its_conflicts()
    {
        using var data = new TestData();
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(Account, Password, null);
            var mail = MailReader.Read(Fixture("07-calendar-invite.eml"));
            var book = document.Pim.Calendar!;
            book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Zahnarzt", Start = new DateTimeOffset(2026, 11, 26, 13, 30, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, 11, 26, 14, 30, 0, TimeSpan.Zero) });

            var view = MailInvitations.View(document, mail, Account.Id)!;
            Assert.True(view.CanAnswer && view.HasCalendar);
            Assert.Equal("Zahnarzt", Assert.Single(view.Conflicts).Title);
            var entered = MailInvitations.Answer(document, view, InvitationAnswer.Accept)!;
            Assert.Equal("Projektbesprechung Nordlicht", book.Find(entered.Key)!.Title);
            await UntilAsync(() => data.Server.Sent.Count == 1, "the answer at the server");
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task Signing_in_takes_the_local_contacts_and_calendar_into_the_account_once_and_same_named_calendars_become_one()
    {
        using var data = new TestData();
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;

        // the account already has ThixCalendar's own calendar "Kalender" (made on the laptop) with an appointment
        await SignInAsync(cloud);
        using (var laptop = PimStore.Open(Path.Combine(data.Folder, "laptop-calendar.db")))
        {
            await using var laptopSync = new PimSync(laptop, CloudService.CreateClient, app: CloudApps.Calendar);
            using var laptopBook = new CalendarBook(laptop);
            laptopBook.Save(new CalendarAppointment { CalendarKey = laptopBook.DefaultCalendar().Key, Title = "Vom Laptop", Start = Monday, End = Monday.AddHours(1) });
            await laptopSync.SyncOnceAsync();
        }

        await CloudService.DisconnectAsync();

        // this computer, without ThixCloud: a contact and an appointment in its own "Kalender"
        string contact;
        string appointment;
        await using (var local = new MailPim())
        {
            contact = local.People!.Save(null, new ContactData { GivenName = "Lena", FamilyName = "Koch" });
            appointment = local.Calendar!.Save(new CalendarAppointment { CalendarKey = local.Calendar.DefaultCalendar().Key, Title = "Von hier", Start = Monday.AddDays(1), End = Monday.AddDays(1).AddHours(1) }).Key;
        }

        await SignInAsync(cloud);
        await using (var pim = new MailPim())
        {
            Assert.True(pim.IsAccountData);
            Assert.NotNull(pim.People!.Find(contact)); // at once
            await pim.SyncNowAsync(); // the calendar merges after the account's first sync
            await pim.SyncNowAsync();
            var book = pim.Calendar!;
            Assert.Equal("Kalender", Assert.Single(book.WritableCalendars).Name); // one "Kalender", not two
            Assert.Equal(["Vom Laptop", "Von hier"], book.Appointments.Select(a => a.Title).Order());
            Assert.Equal(book.WritableCalendars[0].Key, book.Find(appointment)!.CalendarKey);
            Assert.NotNull(cloud.SyncItem(CloudApps.Calendar, CalendarContract.Events, appointment));
            Assert.NotNull(cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, contact));
        }

        await using (var again = new MailPim()) // a restart: nothing doubles
        {
            await again.SyncNowAsync();
            Assert.Equal(2, again.Calendar!.Appointments.Count);
            Assert.Single(again.People!.People);
            Assert.Single(again.Calendar.WritableCalendars);
        }
    }

    [Fact]
    public async Task Signing_out_with_a_copy_keeps_the_account_data_here_and_without_one_shows_the_local_data_as_it_was()
    {
        using var data = new TestData();
        await using (var local = new MailPim())
        {
            local.People!.Save(null, new ContactData { GivenName = "Nur", FamilyName = "Lokal" });
        }

        await SignInAsync();
        var pim = new MailPim();
        try
        {
            await pim.SyncNowAsync();
            pim.People!.Save(null, new ContactData { GivenName = "Im", FamilyName = "Konto" });
            pim.Calendar!.Save(new CalendarAppointment { CalendarKey = pim.Calendar.DefaultCalendar().Key, Title = "Kontotermin", Start = Monday, End = Monday.AddHours(1) });
            await pim.SyncNowAsync();

            await pim.SignOutAsync(keepLocalCopy: true);
            Assert.False(pim.IsAccountData);
            Assert.Equal(["Im Konto", "Nur Lokal"], pim.People!.People.Select(p => p.Name).Order());
            Assert.Equal("Kontotermin", Assert.Single(pim.Calendar!.Appointments).Title);
            Assert.Equal("Kalender", Assert.Single(pim.Calendar.WritableCalendars).Name);
        }
        finally
        {
            await pim.DisposeAsync();
        }

        // signing in and out again without a copy: the local data stays as it was, the account's store stays for the next sign-in
        await SignInAsync();
        var second = new MailPim();
        try
        {
            await second.SyncNowAsync();
            second.People!.Save(null, new ContactData { GivenName = "Nur", FamilyName = "Im Konto" });
            await second.SignOutAsync(keepLocalCopy: false);
            Assert.Equal(["Im Konto", "Nur Lokal"], second.People!.People.Select(p => p.Name).Order());
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_contacts_ThixMail_kept_for_an_account_before_go_into_the_shared_store_with_their_waiting_changes()
    {
        using var data = new TestData();
        var cloud = await SignInAsync();
        var former = PimStores.PathFor(MailPim.FormerContactsStoreName);
        using (var old = PimStore.Open(former))
        using (var session = new ContactsSession(old))
        {
            session.Save(null, new ContactData { GivenName = "Wartet", FamilyName = "Noch" }); // never went up
        }

        await using var pim = new MailPim();
        Assert.Equal("Wartet Noch", Assert.Single(pim.People!.People).Name);
        Assert.False(File.Exists(former));
        Assert.True(File.Exists(former + ".taken-over"));
        await pim.SyncNowAsync();
        Assert.Single(cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts));
    }

    [Fact]
    public void Appointments_travel_as_ics_and_importing_twice_gives_no_doubles()
    {
        using var data = new TestData();
        var store = PimStore.Open(Path.Combine(data.Folder, "here.db"));
        using var book = new CalendarBook(store);
        var calendar = book.DefaultCalendar();
        book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "Planung", Start = Monday, End = Monday.AddHours(1), Location = "Raum 1", Description = "Zeile 1\nZeile 2; mit, Zeichen", ReminderMinutes = 15 });
        book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "Urlaub", Start = CalendarAppointment.AllDayDate(new DateTime(2026, 12, 21)), End = CalendarAppointment.AllDayDate(new DateTime(2026, 12, 24)), IsAllDay = true });
        var text = CalendarIcs.Export(book.Appointments, "Kalender");
        Assert.Contains("BEGIN:VALARM", text, StringComparison.Ordinal);

        using var otherStore = PimStore.Open(Path.Combine(data.Folder, "other.db"));
        using var other = new CalendarBook(otherStore);
        var target = other.DefaultCalendar().Key;
        var imported = CalendarIcs.Import(text, target);
        Assert.Equal(2, other.SaveAll(imported.Appointments).Count);
        other.SaveAll(CalendarIcs.Import(text, target).Appointments);
        Assert.Equal(2, other.Appointments.Count);
        var planning = other.Appointments.Single(a => a.Title == "Planung");
        Assert.Equal(("Raum 1", "Zeile 1\nZeile 2; mit, Zeichen", 15, Monday), (planning.Location, planning.Description, planning.ReminderMinutes, planning.Start));
        var holiday = other.Appointments.Single(a => a.Title == "Urlaub");
        Assert.True(holiday.IsAllDay);
        Assert.Equal((new DateTime(2026, 12, 21), new DateTime(2026, 12, 24)), (holiday.FirstDay, holiday.EndDay));

        // an Outlook file: a series and a time zone
        const string outlook = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:040000008200E00074C5B7101A82E008\r\nSUMMARY:Jour fixe\r\nDTSTART;TZID=W. Europe Standard Time:20261201T100000\r\nDTEND;TZID=W. Europe Standard Time:20261201T103000\r\nRRULE:FREQ=WEEKLY;BYDAY=TU\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var series = CalendarIcs.Import(outlook, target);
        Assert.Equal(1, series.Series);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), Assert.Single(series.Appointments).Start);
        store.Dispose();
    }
}

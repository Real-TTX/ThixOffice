using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixOffice.Pim.Tests;

/// <summary>ThixCalendar's items in a store (the same bytes ThixCalendar writes), the calendar book, iCalendar and invitations, the sync of two computers.</summary>
public sealed class CalendarTests
{
    private static readonly DateTimeOffset Thursday = new(2026, 11, 26, 14, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void An_own_event_is_written_byte_for_byte_as_ThixCalendar_writes_it()
    {
        var appointment = new CalendarAppointment
        {
            CalendarKey = "acc1/0123456789abcdef01234567",
            Title = "Zahnarzt",
            Start = Thursday,
            End = Thursday.AddHours(1),
            Location = "Praxis Dr. Lang",
            ReminderMinutes = 30,
        };

        Assert.Equal(
            """{"calendar":"acc1/0123456789abcdef01234567","title":"Zahnarzt","start":"2026-11-26T13:00:00+00:00","end":"2026-11-26T14:00:00+00:00","isAllDay":false,"location":"Praxis Dr. Lang","description":"","meetingUrl":"","reminderMinutes":30,"status":"Confirmed"}""",
            CalendarItems.Serialize(appointment));
        Assert.Equal("""{"kind":"Local","name":"Eigene Kalender","userName":"","url":"","isVisible":true}""",
            CalendarItems.Serialize(new CalendarAccountItem(CalendarAccountKind.Local, "Eigene Kalender", "", "", true)));
    }

    [Fact]
    public void A_meeting_address_in_location_or_notes_becomes_the_meeting_address_and_a_wrong_one_is_refused_as_ThixCalendar_does()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("calendar"));
        using var book = new CalendarBook(store);
        var calendar = book.DefaultCalendar();

        // the same rule as ThixCalendar's Normalize: otherwise ThixCalendar would write the appointment back with the address on its next save
        var teams = book.Save(new CalendarAppointment
        {
            CalendarKey = calendar.Key, Title = "Abstimmung", Start = Thursday, End = Thursday.AddHours(1),
            Description = "Beitreten: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0. Bis gleich",
        });
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0", teams.MeetingUrl);
        Assert.Contains("\"meetingUrl\":\"https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0\"", store.Get(CalendarContract.Events, teams.Key)!.Data, StringComparison.Ordinal);
        Assert.Equal("https://zoom.us/j/123", book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "Zoom", Start = Thursday, End = Thursday, Location = "https://zoom.us/j/123" }).MeetingUrl);
        Assert.Equal("", book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "Ohne", Start = Thursday, End = Thursday, Description = "https://example.com/meeting" }).MeetingUrl);

        var error = Assert.Throws<BusinessException>(() => book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "x", Start = Thursday, End = Thursday, MeetingUrl = "javascript:alert(1)" }));
        Assert.Equal(nameof(CalendarAppointment.MeetingUrl), error.Field);
    }

    [Fact]
    public void A_store_that_waits_for_its_first_sync_makes_no_own_calendar_so_ThixCalendar_never_gets_a_second_Eigene_Kalender()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("calendar"));
        using var book = new CalendarBook(store) { AwaitsFirstSync = () => store.LastSync is null };
        Assert.Throws<BusinessException>(() => book.DefaultCalendar());
        Assert.Equal(0, store.DirtyCount);

        // the first sync brought ThixCalendar's own account and calendar: that one is used, nothing new is made
        store.ApplyRemote([
            new SyncItemDto { Collection = CalendarContract.Accounts, Key = "acc1", Data = CalendarItems.Serialize(new CalendarAccountItem(CalendarAccountKind.Local, "Eigene Kalender", "", "", true)), UpdatedAt = DateTimeOffset.UtcNow, Revision = 1 },
            new SyncItemDto { Collection = CalendarContract.Calendars, Key = CalendarContract.CalendarKey("acc1", "r1"), Data = CalendarItems.Serialize(new CalendarCalendarItem("r1", "Kalender", "#2F6FED", true, true, null, 0)), UpdatedAt = DateTimeOffset.UtcNow, Revision = 2 },
        ]);
        store.LastSync = DateTimeOffset.UtcNow;
        book.Invalidate();
        Assert.Equal(CalendarContract.CalendarKey("acc1", "r1"), book.DefaultCalendar().Key);
        Assert.Equal(0, store.DirtyCount);

        // an account without calendars of ThixCalendar's: an own one is made now (after the first sync), its calendar without a default reminder like ThixCalendar's
        using var empty = PimStore.Open(data.StorePath("empty"));
        using var fresh = new CalendarBook(empty) { AwaitsFirstSync = () => empty.LastSync is null };
        empty.LastSync = DateTimeOffset.UtcNow;
        var made = fresh.DefaultCalendar();
        Assert.Null(made.DefaultReminderMinutes);
        Assert.Equal(2, empty.DirtyCount);
    }

    [Fact]
    public void The_book_makes_an_own_calendar_when_there_is_none_and_keeps_appointments_with_ThixCalendars_checks()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("calendar"));
        using var book = new CalendarBook(store);
        Assert.Empty(book.Calendars);

        var calendar = book.DefaultCalendar();
        Assert.True(calendar.IsOwn);
        Assert.Equal("Kalender", calendar.Name);
        Assert.Equal(2, store.DirtyCount); // the account "Eigene Kalender" and its calendar go up
        Assert.Same(book.Calendars[0].Key, book.DefaultCalendar().Key);

        var error = Assert.Throws<BusinessException>(() => book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = " ", Start = Thursday, End = Thursday }));
        Assert.Equal(nameof(CalendarAppointment.Title), error.Field);
        Assert.Throws<BusinessException>(() => book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "x", Start = Thursday, End = Thursday.AddHours(-1) }));

        var saved = book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "  Sport ", Start = Thursday, End = Thursday.AddHours(1) });
        Assert.Equal(32, saved.Key.Length);
        Assert.Equal("Sport", saved.Title);
        var holiday = book.Save(new CalendarAppointment { CalendarKey = calendar.Key, Title = "Urlaub", IsAllDay = true, Start = new DateTimeOffset(2026, 11, 27, 9, 30, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, 11, 29, 0, 0, 0, TimeSpan.Zero) });
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), holiday.Start); // all-day dates at midnight UTC

        Assert.Equal(["Sport"], book.Between(new DateTime(2026, 11, 26), new DateTime(2026, 11, 27)).Select(a => a.Title));
        Assert.Equal(["Urlaub"], book.Between(new DateTime(2026, 11, 28), new DateTime(2026, 11, 29)).Select(a => a.Title));
        Assert.Equal(["Sport"], book.Conflicts(Thursday.AddMinutes(30), Thursday.AddHours(2)).Select(a => a.Title));
        Assert.Empty(book.Conflicts(Thursday.AddMinutes(30), Thursday.AddHours(2), saved.Key));

        Assert.True(book.Delete(saved.Key));
        Assert.Null(book.Find(saved.Key));
        Assert.True(store.Get(CalendarContract.Events, saved.Key)!.IsDeleted);
    }

    [Fact]
    public void Calendars_MatCloud_keeps_in_step_show_their_occurrences_read_only()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("calendar"));
        var account = "m365";
        var calendarKey = CalendarContract.CalendarKey(account, "work");
        store.ApplyRemote(
        [
            Item(CalendarContract.Accounts, account, CalendarItems.Serialize(new CalendarAccountItem(CalendarAccountKind.Microsoft, "Firma", "anna@firma.de", "", true, CalendarSyncMode.Cloud))),
            Item(CalendarContract.CloudCalendars, calendarKey, System.Text.Json.JsonSerializer.Serialize(new CalendarCloudCalendar(account, "work", "Kalender", "#0078D4", false, true, 15), CloudContract.Json)),
            Item(CalendarContract.CloudEvents, CalendarContract.EventKey(calendarKey, "e1"), System.Text.Json.JsonSerializer.Serialize(new CalendarCloudEvent
            {
                Calendar = calendarKey, RemoteId = "e1", Title = "Jour fixe", Start = Thursday, End = Thursday.AddMinutes(30), IsRecurring = true, Organizer = "chef@firma.de",
            }, CloudContract.Json)),
        ]);
        using var book = new CalendarBook(store);

        var calendar = Assert.Single(book.Calendars);
        Assert.True(calendar.IsReadOnly);
        Assert.Equal("#0078D4", calendar.Color);
        var occurrence = Assert.Single(book.Appointments);
        Assert.True(occurrence.IsReadOnly && occurrence.IsRecurring);
        Assert.Throws<BusinessException>(() => book.Delete(occurrence.Key));
        Assert.Throws<BusinessException>(() => book.Save(occurrence with { Title = "anders" }));
        Assert.True(book.DefaultCalendar().IsOwn); // a read-only calendar is never where new appointments go
    }

    [Fact]
    public async Task An_appointment_made_on_one_computer_arrives_on_the_other_through_the_calendars_grant()
    {
        using var data = new PimTestData();
        data.Cloud.App = CloudApps.Mail;
        await data.SignInAsync();
        using var first = PimStore.Open(data.StorePath("first"));
        using var second = PimStore.Open(data.StorePath("second"));
        await using var firstSync = new PimSync(first, CloudService.CreateClient, app: CloudApps.Calendar);
        await using var secondSync = new PimSync(second, CloudService.CreateClient, app: CloudApps.Calendar);
        using var book = new CalendarBook(first);
        using var other = new CalendarBook(second);

        var saved = book.Save(new CalendarAppointment { CalendarKey = book.DefaultCalendar().Key, Title = "Lesung", Start = Thursday, End = Thursday.AddHours(2) });
        await firstSync.SyncOnceAsync();
        await secondSync.SyncOnceAsync();

        Assert.Equal(3, data.Cloud.SyncItems(CloudApps.Calendar, CalendarContract.Accounts).Count + data.Cloud.SyncItems(CloudApps.Calendar, CalendarContract.Calendars).Count
            + data.Cloud.SyncItems(CloudApps.Calendar, CalendarContract.Events).Count);
        Assert.Equal("Lesung", Assert.Single(other.Appointments).Title);
        Assert.Equal(CalendarItems.Serialize(saved), data.Cloud.SyncItem(CloudApps.Calendar, CalendarContract.Events, saved.Key)!.Data);

        Assert.True(other.Delete(saved.Key));
        await secondSync.SyncOnceAsync();
        await firstSync.SyncOnceAsync();
        Assert.Empty(book.Appointments);
    }

    [Fact]
    public void ICalendar_unfolds_unescapes_and_folds_at_75_octets()
    {
        var text = "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:abc\r\nSUMMARY:Lange Besprechung mit Kommas\\, Semikolons\\; und einem\r\n  Zeilenumbruch\\nzweite Zeile\r\n"
            + "ORGANIZER;CN=\"Weber, Jonas\":mailto:jonas@example.de\r\nDTSTART;TZID=W. Europe Standard Time:20260715T100000\r\nDTEND;TZID=Europe/Berlin:20260715T110000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var meeting = ICalendar.Parse(text).Single().Children.Single();
        Assert.Equal("Lange Besprechung mit Kommas, Semikolons; und einem Zeilenumbruch\nzweite Zeile", meeting.Text("SUMMARY"));
        Assert.Equal("Weber, Jonas", meeting.Property("ORGANIZER")!["CN"]);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero), ICalendar.TimeOf(meeting.Property("DTSTART"))!.Value.Time.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero), ICalendar.TimeOf(meeting.Property("DTEND"))!.Value.Time.ToUniversalTime());

        var written = ICalendar.Write(new ICalComponent("VEVENT").AddText("DESCRIPTION", new string('ä', 100)));
        Assert.All(written.Split("\r\n").Where(line => line.Length > 0), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75));
        Assert.Equal(new string('ä', 100), ICalendar.Parse(written).Single().Text("DESCRIPTION"));
    }

    [Fact]
    public void A_time_zone_only_the_calendar_defines_is_read_from_its_VTIMEZONE()
    {
        const string text = """
            BEGIN:VCALENDAR
            BEGIN:VTIMEZONE
            TZID:Mitteleuropa (eigene)
            BEGIN:STANDARD
            DTSTART:16011028T030000
            RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10
            TZOFFSETFROM:+0200
            TZOFFSETTO:+0100
            END:STANDARD
            BEGIN:DAYLIGHT
            DTSTART:16010325T020000
            RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3
            TZOFFSETFROM:+0100
            TZOFFSETTO:+0200
            END:DAYLIGHT
            END:VTIMEZONE
            BEGIN:VEVENT
            UID:tz-1
            DTSTART;TZID=Mitteleuropa (eigene):20260715T100000
            DTEND;TZID=Mitteleuropa (eigene):20261215T100000
            SUMMARY:x
            END:VEVENT
            END:VCALENDAR
            """;
        var invitation = Invitations.Read(text)!;
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero), invitation.Start.ToUniversalTime()); // summer: +02:00
        Assert.Equal(new DateTimeOffset(2026, 12, 15, 9, 0, 0, TimeSpan.Zero), invitation.End.ToUniversalTime()); // winter: +01:00
    }

    [Fact]
    public void An_invitation_is_read_answered_and_entered_once_whatever_computer_answers()
    {
        const string request = "BEGIN:VCALENDAR\r\nPRODID:-//Example//DE\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:termin-07@example.de\r\nSEQUENCE:2\r\n"
            + "DTSTART:20261126T130000Z\r\nDTEND:20261126T140000Z\r\nSUMMARY:Projektbesprechung Nordlicht\r\nLOCATION:Raum 2.14\r\nRRULE:FREQ=WEEKLY;COUNT=4\r\n"
            + "ORGANIZER;CN=Jonas Weber:mailto:jonas.weber@example.de\r\nATTENDEE;CN=Anna Berger;RSVP=TRUE:mailto:anna@example.de\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var invitation = Invitations.Read(request)!;
        Assert.Equal((InvitationMethod.Request, 2, "Raum 2.14", "Jonas Weber"), (invitation.Method, invitation.Sequence, invitation.Location, invitation.Organizer.Name));
        Assert.True(invitation.IsRecurring);
        Assert.Equal("NEEDS-ACTION", invitation.Attendee("ANNA@example.de")!.Status);

        var appointment = Invitations.AppointmentOf(invitation, InvitationAnswer.Tentative, "acc/cal");
        Assert.Equal(CalendarBook.KeyOfInvitation("termin-07@example.de"), appointment.Key);
        Assert.Equal(CalendarEventStatus.Tentative, appointment.Status);
        Assert.Contains("Organisator: Jonas Weber <jonas.weber@example.de>", appointment.Description, StringComparison.Ordinal);

        var reply = Invitations.Read(Invitations.Reply(invitation, "anna@example.de", "Anna Berger", InvitationAnswer.Accept))!;
        Assert.Equal((InvitationMethod.Reply, "termin-07@example.de", 2), (reply.Method, reply.Uid, reply.Sequence));
        Assert.Equal("ACCEPTED", Assert.Single(reply.Attendees).Status);
        Assert.Equal("Zugesagt: Projektbesprechung Nordlicht", Invitations.ReplySubject(invitation, InvitationAnswer.Accept));

        var meeting = invitation with { Organizer = new InvitationAttendee("anna@example.de", "Anna Berger"), Attendees = [new InvitationAttendee("lena@example.org", "Lena Koch")] };
        var sent = Invitations.Read(Invitations.Request(meeting))!;
        Assert.Equal((InvitationMethod.Request, "lena@example.org", true), (sent.Method, sent.Attendees.Single().Email, sent.Attendees.Single().Rsvp));
        var cancelled = Invitations.Read(Invitations.Cancel(meeting))!;
        Assert.Equal((InvitationMethod.Cancel, 3), (cancelled.Method, cancelled.Sequence));
    }

    private static SyncItemDto Item(string collection, string key, string data) => new() { Collection = collection, Key = key, Data = data, UpdatedAt = DateTimeOffset.UtcNow, Revision = 1 };
}

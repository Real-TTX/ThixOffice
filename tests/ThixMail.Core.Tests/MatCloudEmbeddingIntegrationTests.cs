using System.Net;
using System.Text.Json;
using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixOffice.Core.Business;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixMail.Core.Tests;

/// <summary>
/// ThixMail's calendar and people (7c) against a REAL MatCloud (a throwaway instance, see CLAUDE.md): THIXOFFICE_MATCLOUD_URL, THIXOFFICE_MATCLOUD_USER
/// and THIXOFFICE_MATCLOUD_PASSWORD name it; without them the tests do nothing. ThixMail signs in as app <c>mail</c> (CloudService, the app's own way)
/// and its <see cref="MailPim"/> syncs both ways with a device of ThixContacts (app <c>contacts</c>, its own sync) and a device of ThixCalendar (app
/// <c>calendar</c>, its own sync, the items in ThixCalendar's own shapes – <see cref="ThixCalendarShapes"/>); what ThixMail writes is held to
/// ThixCalendar's rules (<see cref="ThixCalendarShapes.Problems"/>). The grants, 401 and 403 are MatCloud's answers, not SampleCloud's.
/// </summary>
public sealed class MatCloudEmbeddingIntegrationTests
{
    private static string? Url => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_URL");

    private static string? User => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_USER");

    private static string? Password => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_PASSWORD");

    private static bool Configured => Url is not null && User is not null && Password is not null;

    [Fact]
    public async Task ThixMail_and_ThixContacts_share_the_contacts_of_the_account_both_ways()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAIL-PEOPLE");
        await using var pim = new MailPim();
        Assert.True(pim.IsAvailable);
        await pim.SyncNowAsync();
        var people = pim.People!;
        var run = Guid.NewGuid().ToString("N")[..6];

        // ThixMail writes a contact; ThixContacts (its own sync) reads it in its own model
        var id = people.Save(null, new ContactData { GivenName = "Lena", FamilyName = "Koch " + run, Emails = [new ContactValue("work", $"lena.{run}@beispiel.de")] });
        await pim.SyncNowAsync();
        using var contactsStore = PimStore.Open(Path.Combine(data.Folder, "thixcontacts.db"));
        var contactsToken = await LoginAsync("IT-CONTACTS", CloudApps.Contacts);
        await using var contactsSync = new PimSync(contactsStore, () => Client(contactsToken));
        await contactsSync.SyncOnceAsync();
        using var contacts = new ContactsSession(contactsStore);
        var lena = contacts.Find(id)!;
        Assert.Equal(($"Lena Koch {run}", $"lena.{run}@beispiel.de"), (lena.Name, lena.Email));

        // ThixContacts changes it and writes one of its own; ThixMail sees both
        contacts.Save(id, lena.Data with { Company = "Beispiel GmbH" });
        var other = contacts.Save(null, new ContactData { GivenName = "Tom", FamilyName = "Aus ThixContacts " + run });
        await contactsSync.SyncOnceAsync();
        await pim.SyncNowAsync();
        Assert.Equal("Beispiel GmbH", people.Find(id)!.Data.Company);
        Assert.Equal($"Tom Aus ThixContacts {run}", people.Find(other)!.Name);

        // deletions travel both ways
        people.Delete([id]);
        await pim.SyncNowAsync();
        contacts.Delete([other]);
        await contactsSync.SyncOnceAsync();
        await pim.SyncNowAsync();
        Assert.Null(contacts.Find(id));
        Assert.Null(people.Find(other));
        Assert.Equal(0, pim.People!.Store.DirtyCount);
        await CloudService.DisconnectAsync();
    }

    [Fact]
    public async Task ThixMail_and_ThixCalendar_share_the_calendar_and_what_ThixMail_writes_keeps_ThixCalendars_rules()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        var run = Guid.NewGuid().ToString("N")[..6];

        // ThixCalendar's computer: its own sync (app calendar); an own account and calendar as ThixCalendar makes them – the one it has, else a new one
        using var calendarStore = PimStore.Open(Path.Combine(data.Folder, "thixcalendar.db"));
        var calendarToken = await LoginAsync("IT-CALENDAR", CloudApps.Calendar);
        await using var calendarSync = new PimSync(calendarStore, () => Client(calendarToken));
        await calendarSync.SyncOnceAsync();
        var calendarKey = ThixCalendarShapes.EnsureOwnCalendar(calendarStore);
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(5).AddHours(9), TimeSpan.Zero);
        var fromCalendar = Guid.NewGuid().ToString("N");
        calendarStore.Write([(CalendarContract.Events, fromCalendar, ThixCalendarShapes.Event(calendarKey, $"Aus ThixCalendar {run}", start, start.AddHours(1), false, "Raum 1", "", "", 10))]);
        await calendarSync.SyncOnceAsync();

        // ThixMail signs in and sees ThixCalendar's calendar and appointment
        await SignInAsMailAsync("IT-MAIL-CALENDAR");
        await using var pim = new MailPim();
        await pim.SyncNowAsync();
        var book = pim.Calendar!;
        var calendar = Assert.Single(book.Calendars, c => c.Key == calendarKey);
        Assert.True(calendar.IsOwn);
        var seen = book.Find(fromCalendar)!;
        Assert.Equal(($"Aus ThixCalendar {run}", start, start.AddHours(1), "Raum 1", 10), (seen.Title, seen.Start, seen.End, seen.Location, seen.ReminderMinutes));
        Assert.True(calendar.IsVisible);

        // every event of an own calendar – also those a real ThixCalendar wrote into the same account – shows with ThixCalendar's values and on its days
        foreach (var item in calendarStore.Items(CalendarContract.Events))
        {
            var shape = ThixCalendarShapes.ReadEvent(item.Data!)!;
            if (book.Calendar(shape.Calendar) is not { IsOwn: true })
            {
                continue;
            }

            var shown = book.Find(item.Key)!;
            Assert.Equal((shape.Title, shape.Start, shape.End, shape.IsAllDay, shape.Location, shape.Description, shape.MeetingUrl, shape.ReminderMinutes),
                (shown.Title, shown.Start, shown.End, shown.IsAllDay, shown.Location, shown.Description, shown.MeetingUrl, shown.ReminderMinutes));
            Assert.Equal(shape.IsAllDay ? shape.Start.UtcDateTime.Date : shape.Start.ToLocalTime().Date, shown.FirstDay);
        }

        // ThixMail writes a timed and an all-day appointment and an online meeting into that calendar
        var timed = book.Save(new CalendarAppointment { CalendarKey = calendarKey, Title = $"  Aus ThixMail {run} ", Start = start.AddHours(3), End = start.AddHours(4), Location = "Café", ReminderMinutes = 15 });
        var day = DateTime.UtcNow.Date.AddDays(6);
        var allDay = book.Save(new CalendarAppointment { CalendarKey = calendarKey, Title = $"Ganztags aus ThixMail {run}", IsAllDay = true, Start = CalendarAppointment.AllDayDate(day), End = CalendarAppointment.AllDayDate(day.AddDays(2)) });
        var online = book.Save(new CalendarAppointment { CalendarKey = calendarKey, Title = $"Online aus ThixMail {run}", Start = start.AddHours(5), End = start.AddHours(6), Description = "Beitreten: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0 – bis gleich" });
        await pim.SyncNowAsync();

        // ThixCalendar's own sync reads them – byte for byte the items ThixCalendar would write for the same values, valid by its rules
        await calendarSync.SyncOnceAsync();
        foreach (var key in new[] { timed.Key, allDay.Key, online.Key })
        {
            var item = calendarStore.Get(CalendarContract.Events, key)!;
            var shaped = ThixCalendarShapes.ReadEvent(item.Data!)!;
            Assert.Equal(calendarKey, shaped.Calendar);
            Assert.Equal([], ThixCalendarShapes.Problems(shaped));
            Assert.Equal(ThixCalendarShapes.Serialize(shaped), item.Data);
        }

        var allDayShape = ThixCalendarShapes.ReadEvent(calendarStore.Get(CalendarContract.Events, allDay.Key)!.Data!)!;
        Assert.Equal((CalendarAppointment.AllDayDate(day), CalendarAppointment.AllDayDate(day.AddDays(2)), true), (allDayShape.Start, allDayShape.End, allDayShape.IsAllDay));
        Assert.Equal($"Aus ThixMail {run}", ThixCalendarShapes.ReadEvent(calendarStore.Get(CalendarContract.Events, timed.Key)!.Data!)!.Title);

        // ThixCalendar changes ThixMail's appointment and deletes its own; ThixMail follows
        var changed = ThixCalendarShapes.ReadEvent(calendarStore.Get(CalendarContract.Events, timed.Key)!.Data!)! with { Location = "Bistro" };
        calendarStore.Write([(CalendarContract.Events, timed.Key, ThixCalendarShapes.Serialize(changed)), (CalendarContract.Events, fromCalendar, null)]);
        await calendarSync.SyncOnceAsync();
        await pim.SyncNowAsync();
        Assert.Equal("Bistro", book.Find(timed.Key)!.Location);
        Assert.Null(book.Find(fromCalendar));
        if (Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_KEEP_APPOINTMENTS") == "1")
        {
            await CloudService.DisconnectAsync();
            return; // left in the account for a look with ThixCalendar itself
        }

        // ThixMail deletes its appointments; ThixCalendar's sync gets the tombstones
        foreach (var key in new[] { timed.Key, allDay.Key, online.Key })
        {
            Assert.True(book.Delete(key));
        }

        await pim.SyncNowAsync();
        await calendarSync.SyncOnceAsync();
        Assert.All([timed.Key, allDay.Key, online.Key], key => Assert.True(calendarStore.Get(CalendarContract.Events, key) is null or { IsDeleted: true }));
        Assert.Equal(0, book.Store.DirtyCount);
        await CloudService.DisconnectAsync();
    }

    [Fact]
    public async Task A_device_of_ThixMail_reaches_only_what_MatCloud_grants_it()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        var mailToken = await LoginAsync("IT-MAIL-GRANTS", CloudApps.Mail);
        using var mail = Client(mailToken);

        // granted: contacts and calendar, reading and writing
        await mail.GetChangesAsync(CloudApps.Contacts, 0, 1, CancellationToken.None);
        await mail.GetChangesAsync(CloudApps.Calendar, 0, 1, CancellationToken.None);

        // not granted: the data of every other app of the general sync, reading or writing
        foreach (var app in new[] { CloudApps.Note, CloudApps.Calendar + "x", CloudApps.Clip })
        {
            var refused = await Assert.ThrowsAsync<CloudException>(() => mail.GetChangesAsync(app, 0, 1, CancellationToken.None));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        var push = await Assert.ThrowsAsync<CloudException>(() => mail.PushAsync(CloudApps.Note, new SyncPushRequest([new SyncItemDto { Collection = "notes", Key = "x", Data = "{}", UpdatedAt = DateTimeOffset.UtcNow }]), CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);

        // the collections only MatCloud writes stay MatCloud's, also for a guest
        var serverOnly = await mail.PushAsync(CloudApps.Calendar, new SyncPushRequest([new SyncItemDto
        {
            Collection = CalendarContract.CloudEvents, Key = "it-" + Guid.NewGuid().ToString("N"), Data = "{}", UpdatedAt = DateTimeOffset.UtcNow,
        }]), CancellationToken.None);
        Assert.False(Assert.Single(serverOnly.Results).Applied);

        // the other way round: ThixContacts has no grant for the calendar, ThixCalendar may only read the contacts
        using var contacts = Client(await LoginAsync("IT-CONTACTS-GRANTS", CloudApps.Contacts));
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<CloudException>(() => contacts.GetChangesAsync(CloudApps.Calendar, 0, 1, CancellationToken.None))).StatusCode);
        using var calendar = Client(await LoginAsync("IT-CALENDAR-GRANTS", CloudApps.Calendar));
        await calendar.GetChangesAsync(CloudApps.Contacts, 0, 1, CancellationToken.None);
        var write = await Assert.ThrowsAsync<CloudException>(() => calendar.PushAsync(CloudApps.Contacts, new SyncPushRequest([new SyncItemDto
        {
            Collection = ContactsContract.Contacts, Key = "x", Data = ContactsContract.Serialize(new ContactData { GivenName = "X" }), UpdatedAt = DateTimeOffset.UtcNow,
        }]), CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        // a PimSync of ThixMail into an app without a grant: 403 is "Forbidden", the data stays and waits
        using var store = PimStore.Open(Path.Combine(data.Folder, "notgranted.db"));
        store.Write([("notes", "x", "{}")]);
        await using var sync = new PimSync(store, () => Client(mailToken), app: CloudApps.Note);
        await Assert.ThrowsAsync<CloudException>(() => sync.SyncOnceAsync());
        Assert.Equal(SyncState.Forbidden, sync.Status.State);
        Assert.Equal(1, store.DirtyCount);
    }

    [Fact]
    public async Task When_MatCloud_ends_ThixMails_sign_in_the_calendar_and_people_stay_and_their_changes_go_up_after_the_next_sign_in()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAIL-401");
        var pim = new MailPim();
        try
        {
            await pim.SyncNowAsync();
            var run = Guid.NewGuid().ToString("N")[..6];
            var calendarKey = pim.Calendar!.DefaultCalendar().Key;
            await pim.SyncNowAsync();

            // the device is removed in MatCloud (its token ends): 401
            using (var client = CloudService.CreateClient())
            {
                await client.LogoutAsync(CancellationToken.None);
            }

            var appointment = pim.Calendar.Save(new CalendarAppointment { CalendarKey = calendarKey, Title = $"Offline aus ThixMail {run}", Start = DateTimeOffset.UtcNow.AddDays(2), End = DateTimeOffset.UtcNow.AddDays(2).AddHours(1) });
            var contact = pim.People!.Save(null, new ContactData { GivenName = "Abgemeldet", FamilyName = run });
            await Assert.ThrowsAsync<ThixOffice.Core.Models.BusinessException>(pim.SyncNowAsync);
            Assert.Equal(CloudState.SignedOut, CloudService.Status.State);
            Assert.True(CloudService.HasAccount);
            Assert.False(CloudService.IsConnected);

            // as in ThixContacts: the syncs stop, the data stays, the changes wait
            await TestData.UntilAsync(() => !pim.IsSyncing, "the syncs to stop after the 401");
            await pim.ReconnectAsync();
            Assert.True(pim.IsAvailable);
            Assert.NotNull(pim.Calendar.Find(appointment.Key));
            Assert.True(pim.Calendar.Store.DirtyCount > 0);
            Assert.True(pim.People.Store.DirtyCount > 0);

            // signed in again (the same account): the same stores, the syncs run again and the changes go up
            await SignInAsMailAsync("IT-MAIL-401B");
            await pim.ReconnectAsync();
            Assert.True(pim.IsSyncing);
            await TestData.UntilAsync(() => pim.Calendar!.Store.DirtyCount == 0 && pim.People!.Store.DirtyCount == 0, "the waiting changes to go up");
            using var reader = Client(await LoginAsync("IT-CALENDAR-401", CloudApps.Calendar));
            var changes = await AllChangesAsync(reader, null);
            Assert.Contains(changes, item => item.Collection == CalendarContract.Events && item.Key == appointment.Key && !item.IsDeleted);
            pim.Calendar!.Delete(appointment.Key);
            pim.People!.Delete([contact]);
            await pim.SyncNowAsync();
        }
        finally
        {
            await pim.DisposeAsync();
            await CloudService.DisconnectAsync();
        }
    }

    [Fact]
    public async Task A_ThixCalendar_switched_off_in_MatCloud_stops_only_ThixMails_calendar_and_ThixMail_stays_signed_in()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAIL-403");
        var pim = new MailPim();
        try
        {
            await pim.SyncNowAsync();
            var run = Guid.NewGuid().ToString("N")[..6];
            var calendarKey = pim.Calendar!.DefaultCalendar().Key;
            await SetAppEnabledAsync(CloudApps.Calendar, false);
            try
            {
                var appointment = pim.Calendar.Save(new CalendarAppointment { CalendarKey = calendarKey, Title = $"Wartet aus ThixMail {run}", Start = DateTimeOffset.UtcNow.AddDays(3), End = DateTimeOffset.UtcNow.AddDays(3).AddHours(1) });
                var contact = pim.People!.Save(null, new ContactData { GivenName = "Trotzdem", FamilyName = run });
                var refused = await Assert.ThrowsAsync<ThixOffice.Core.Models.BusinessException>(pim.SyncNowAsync);
                Assert.Equal("ThixCalendar ist in der ThixCloud abgeschaltet.", refused.Message);
                Assert.Equal(SyncState.Forbidden, pim.CalendarSyncStatus.State);
                Assert.NotEqual(SyncState.Forbidden, pim.PeopleSyncStatus.State); // (the loop may be in a round of its own)
                Assert.Equal(0, pim.People.Store.DirtyCount); // the contacts went up
                Assert.True(pim.Calendar.Store.DirtyCount > 0); // the appointment waits

                // ThixMail itself is on: the connection stays Online (its account route answers), not "von der Verwaltung abgeschaltet"
                await CloudService.RefreshAccountAsync();
                Assert.Equal(CloudState.Online, CloudService.Status.State);

                await SetAppEnabledAsync(CloudApps.Calendar, true);
                await pim.SyncNowAsync();
                Assert.Equal(0, pim.Calendar.Store.DirtyCount);
                pim.Calendar.Delete(appointment.Key);
                pim.People.Delete([contact]);
                await pim.SyncNowAsync();
            }
            finally
            {
                await SetAppEnabledAsync(CloudApps.Calendar, true);
            }
        }
        finally
        {
            await pim.DisposeAsync();
            await CloudService.DisconnectAsync();
        }
    }

    // ===== helpers =====

    /// <summary>Switches an app on or off as MatCloud's administration does (Verwaltung › Apps; THIXOFFICE_MATCLOUD_USER is the administrator of the throwaway instance).</summary>
    private static async Task SetAppEnabledAsync(string app, bool enabled)
    {
        var cookies = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = new Uri(Url!) };
        if (!AdminCookies.TryGetValue(Url!, out var known))
        {
            await RetryWhenThrottledAsync(async () =>
            {
                var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = AntiforgeryToken(await http.GetStringAsync("/Account/Login")), ["UserName"] = User!, ["Password"] = Password! };
                var login = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
                if (login.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new CloudException(HttpStatusCode.TooManyRequests, "throttled");
                }

                Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            });
            AdminCookies[Url!] = cookies;
        }
        else
        {
            cookies = known;
        }

        using var admin = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = new Uri(Url!) };
        var toggle = await admin.PostAsync("/Admin/Apps?handler=Toggle", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken(await admin.GetStringAsync("/Admin/Apps")),
            ["app"] = app,
            ["enabled"] = enabled ? "true" : "false",
        }));
        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);
    }

    private static readonly Dictionary<string, CookieContainer> AdminCookies = [];

    private static string AntiforgeryToken(string html)
        => System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private static readonly Dictionary<(string Device, string App), string> Tokens = [];

    private static async Task SignInAsMailAsync(string device)
    {
        CloudService.HandlerForTests = null;
        await RetryWhenThrottledAsync(() => CloudService.ConnectAsync(Url!, User!, Password!, device));
    }

    /// <summary>A device token (one sign-in per device and app for the whole run: MatCloud lets ten sign-ins a minute through).</summary>
    private static async Task<string> LoginAsync(string device, string app)
    {
        if (Tokens.TryGetValue((device, app), out var known))
        {
            return known;
        }

        using var client = new MatCloudClient(Url!, null, "ThixOfficeIntegration/1.0.0 (windows)");
        var token = "";
        await RetryWhenThrottledAsync(async () => token = (await client.LoginAsync(new DeviceLoginRequest(User!, Password!, device, "windows", "1.0.0", app), CancellationToken.None)).Token);
        Tokens[(device, app)] = token;
        return token;
    }

    /// <summary>MatCloud answers 429 after ten sign-ins a minute from one address: wait for the next window (at most twice).</summary>
    private static async Task RetryWhenThrottledAsync(Func<Task> signIn)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await signIn();
                return;
            }
            catch (Exception exception) when (attempt < 2 && IsThrottled(exception))
            {
                await Task.Delay(TimeSpan.FromSeconds(61));
            }
        }
    }

    private static bool IsThrottled(Exception exception) =>
        exception is CloudException { StatusCode: HttpStatusCode.TooManyRequests }
        || (exception is ThixOffice.Core.Models.BusinessException business && business.Message.Contains("Anmeldeversuche", StringComparison.Ordinal));

    private static MatCloudClient Client(string token) => new(Url!, token, "ThixOfficeIntegration/1.0.0 (windows)");

    private static async Task<List<SyncItemDto>> AllChangesAsync(MatCloudClient client, string? app)
    {
        var result = new List<SyncItemDto>();
        long since = 0;
        while (true)
        {
            var changes = await client.GetChangesAsync(app, since, SyncContract.MaxBatchSize, CancellationToken.None);
            result.AddRange(changes.Items);
            since = changes.Latest;
            if (!changes.HasMore || changes.Items.Count == 0)
            {
                return result;
            }
        }
    }
}

/// <summary>
/// ThixCalendar's own shapes of its items and its rules for events, as its code has them (ThixCalendar.Core: Business/CloudItems.cs – AccountData,
/// CalendarData, EventData –, Business/CalendarService.cs – Normalize, Validate –, Providers/ICalendarProvider.cs – MeetingLinks). Kept here, not taken
/// from ThixOffice, so a test compares ThixOffice's items with ThixCalendar's and not with themselves.
/// </summary>
internal static partial class ThixCalendarShapes
{
    public enum AccountKind { Local = 0, Microsoft = 1, Google = 2, Ics = 3 }

    public enum AccountSyncMode { Computers = 0, Cloud = 1 }

    public enum EventStatus { Confirmed = 0, Tentative = 1, Cancelled = 2 }

    public sealed record AccountData(AccountKind Kind, string Name, string UserName, string Url, bool IsVisible, AccountSyncMode? SyncMode = null);

    public sealed record CalendarData(string RemoteId, string Name, string Color, bool IsVisible, bool RemindersEnabled, int? DefaultReminderMinutes, int SortOrder);

    public sealed record EventData(
        string Calendar, string Title, DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, string Location, string Description,
        string MeetingUrl, int? ReminderMinutes, EventStatus Status);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, CloudContract.Json);

    public static EventData? ReadEvent(string data) => JsonSerializer.Deserialize<EventData>(data, CloudContract.Json);

    public static string Event(string calendar, string title, DateTimeOffset start, DateTimeOffset end, bool allDay, string location, string description, string meetingUrl, int? reminder)
        => Serialize(new EventData(calendar, title, start.ToUniversalTime(), end.ToUniversalTime(), allDay, location, description, meetingUrl, reminder, EventStatus.Confirmed));

    /// <summary>The key of an own calendar in the store (ThixCalendar's AddLocal: account "Eigene Kalender", calendar "Kalender", colour of the palette, no default reminder).</summary>
    public static string EnsureOwnCalendar(PimStore store)
    {
        var own = store.Items(CalendarContract.Accounts)
            .Where(item => JsonSerializer.Deserialize<AccountData>(item.Data!, CloudContract.Json)?.Kind == AccountKind.Local)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (store.Items(CalendarContract.Calendars).FirstOrDefault(item => own.Contains(item.Key[..item.Key.IndexOf('/')])) is { } existing)
        {
            return existing.Key;
        }

        var account = own.FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        var remoteId = Guid.NewGuid().ToString("N");
        var key = CalendarContract.CalendarKey(account, remoteId);
        store.Write([
            (CalendarContract.Accounts, account, Serialize(new AccountData(AccountKind.Local, "Eigene Kalender", "", "", true))),
            (CalendarContract.Calendars, key, Serialize(new CalendarData(remoteId, "Kalender", "#2F6FED", true, true, null, 0))),
        ]);
        return key;
    }

    /// <summary>
    /// What ThixCalendar would refuse or change in an event: its Validate (title 1–255, the end not before the start – all-day: after it –, at most a year,
    /// a reminder of 0 to 4 weeks, a meeting address that is a web address) and its Normalize (trimmed texts, all-day dates at midnight UTC, the meeting
    /// address found in location or notes) – an event that Normalize changes would be written back by ThixCalendar on its next save.
    /// </summary>
    public static IReadOnlyList<string> Problems(EventData calendarEvent)
    {
        var problems = new List<string>();
        if (calendarEvent.Title.Length == 0 || calendarEvent.Title.Length > 255)
        {
            problems.Add("title");
        }

        if (calendarEvent.IsAllDay ? calendarEvent.End <= calendarEvent.Start : calendarEvent.End < calendarEvent.Start)
        {
            problems.Add("end");
        }

        if (calendarEvent.End - calendarEvent.Start > TimeSpan.FromDays(366))
        {
            problems.Add("length");
        }

        if (calendarEvent.ReminderMinutes is < 0 or > 40320)
        {
            problems.Add("reminder");
        }

        if (calendarEvent.MeetingUrl.Length > 0 && !(Uri.TryCreate(calendarEvent.MeetingUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
        {
            problems.Add("meetingUrl");
        }

        var normalized = calendarEvent with
        {
            Title = calendarEvent.Title.Trim(),
            Location = calendarEvent.Location.Trim(),
            Description = calendarEvent.Description.TrimEnd(),
            MeetingUrl = calendarEvent.MeetingUrl.Trim(),
        };
        if (normalized.IsAllDay)
        {
            normalized = normalized with
            {
                Start = new DateTimeOffset(normalized.Start.UtcDateTime.Date, TimeSpan.Zero),
                End = new DateTimeOffset(normalized.End.UtcDateTime.Date, TimeSpan.Zero),
            };
        }

        if (normalized.MeetingUrl.Length == 0)
        {
            normalized = normalized with { MeetingUrl = FindMeeting(normalized.Location, normalized.Description) };
        }

        if (normalized != calendarEvent)
        {
            problems.Add("normalized differently: " + Serialize(normalized));
        }

        return problems;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"https://(?:teams\.microsoft\.com/l/meetup-join/|teams\.live\.com/meet/|meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}|(?:[\w-]+\.)?zoom\.us/(?:j|my|w)/|[\w-]+\.webex\.com/|whereby\.com/|meet\.jit\.si/|[\w-]+\.gotomeeting\.com/|app\.chime\.aws/)[^\s""'<>)\]]*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MeetingPattern();

    private static string FindMeeting(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text) && MeetingPattern().Match(text) is { Success: true } match)
            {
                return match.Value.TrimEnd('.', ',', ';');
            }
        }

        return "";
    }
}

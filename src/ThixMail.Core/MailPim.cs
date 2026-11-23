using MatCloud.Contract;
using ThixContacts.Core;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Calendar;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixMail.Core;

/// <summary>
/// The calendar and the people inside ThixMail (Outlook's "Kalender" and "Personen") – local first, like Outlook: they always work, with or without
/// ThixCloud.
/// <list type="bullet">
/// <item><b>Personen</b> are ThixContacts' contacts on this computer: the SAME store file in ThixContacts' data folder (<c>contacts-local.db</c>, or
/// <c>contacts-&lt;account&gt;.db</c> while signed in) – one address book for both apps. The file is watched for the other app's changes; while signed in,
/// only one of the two apps' syncs talks to the cloud for it (the lease of <see cref="PimSync"/>), ThixMail's through the grant <c>mail</c> →
/// <c>contacts</c>. The session is ThixContacts' <see cref="ContactsSession"/> (the same rules, the same undo).</item>
/// <item><b>Kalender</b> is ThixMail's own store (ThixCalendar keeps its local calendars in a database of its own, so the two are not shared on one
/// computer; .ics import/export moves appointments): <c>mailcalendar-local.db</c> with an own calendar "Kalender" made on first use, or
/// <c>mailcalendar-&lt;account&gt;.db</c> – ThixCalendar's items of the account through the grant <c>mail</c> → <c>calendar</c>. After the account's first
/// sync the local calendar goes into it (<see cref="CalendarTransfer.MergeInto"/>: same-named calendars become one, nothing doubles).</item>
/// </list>
/// <see cref="ReconnectAsync"/> follows a change of the sign-in, <see cref="SignOutAsync"/> leaves the account (optionally keeping a copy here).
/// </summary>
public sealed class MailPim : IAsyncDisposable
{
    /// <summary>The stem of ThixMail's calendar stores (in ThixMail's data folder).</summary>
    public const string CalendarStoreName = "mailcalendar";

    /// <summary>The stem of ThixMail's own contact stores before 2026-11-22 (only an account's; taken over into the shared store once).</summary>
    public const string FormerContactsStoreName = "mailcontacts";

    private PimSync? _peopleSync;
    private PimSync? _calendarSync;
    private bool _calendarMergePending;

    public MailPim()
    {
        Open();
    }

    /// <summary>The contacts (of this computer, or of the account), or null when the store could not be opened.</summary>
    public ContactsSession? People { get; private set; }

    /// <summary>The calendar (of this computer, or of the account), or null when the store could not be opened.</summary>
    public CalendarBook? Calendar { get; private set; }

    /// <summary>True when both stores are open (they are, unless the disk refused them).</summary>
    public bool IsAvailable => People is not null && Calendar is not null;

    /// <summary>True while the data is the ThixCloud account's (signed in, or signed out by the cloud – the data stays until "Abmelden").</summary>
    public bool IsAccountData { get; private set; }

    /// <summary>True while the syncs run (a sync the cloud stopped – 401 – does not count: <see cref="ReconnectAsync"/> starts it again).</summary>
    public bool IsSyncing => _peopleSync is { IsRunning: true } || _calendarSync is { IsRunning: true };

    /// <summary>The folder of the contacts: ThixContacts' data folder (one address book on this computer).</summary>
    public static string ContactsDirectory => AppPaths.DataDirectoryOf(ThixContactsApp.Definition);

    /// <summary>The people or the calendar changed (here, by the sync or by ThixContacts: then on a worker thread), or another account came in.</summary>
    public event EventHandler? Changed;

    /// <summary>The state of one of the syncs changed (any thread): the status bar of the view.</summary>
    public event EventHandler? SyncStatusChanged;

    /// <summary>The state of the calendar's sync (the status bar of the view).</summary>
    public SyncStatus CalendarSyncStatus => _calendarSync?.Status ?? SyncStatus.Off;

    public SyncStatus PeopleSyncStatus => _peopleSync?.Status ?? SyncStatus.Off;

    /// <summary>One round of both syncs now (F9 in a view); refused without a sign-in.</summary>
    public async Task SyncNowAsync()
    {
        if (_peopleSync is null && _calendarSync is null)
        {
            throw new BusinessException("ThixMail ist nicht bei der ThixCloud angemeldet: Kalender und Kontakte sind nur auf diesem Computer (Datei › Einstellungen › Cloud).");
        }

        string? switchedOff = null;
        foreach (var sync in new[] { _peopleSync, _calendarSync })
        {
            if (sync is null)
            {
                continue;
            }

            try
            {
                await sync.SyncOnceAsync();
            }
            catch (CloudException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // ThixCalendar or ThixContacts switched off in the cloud: only that part stops (its sync says so); ThixMail stays signed in
                switchedOff ??= sync.Status.Message;
            }
            catch (Exception exception) when (CloudService.IsCloudFailure(exception, CancellationToken.None))
            {
                throw CloudService.Explain(exception);
            }
        }

        MergeLocalCalendarWhenReady();
        if (switchedOff is not null)
        {
            throw new BusinessException(switchedOff + ".");
        }
    }

    /// <summary>The sign-in changed: the stores of the account now (or of this computer), the syncs follow. No-op when nothing changed.</summary>
    public async Task ReconnectAsync()
    {
        var path = PimStores.PathFor(CalendarStoreName);
        if (Calendar is not null && string.Equals(path, Calendar.Store.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (CloudService.IsConnected && !IsSyncing)
            {
                await StopSyncsAsync(); // a sync the cloud ended (401) is not taken up again: new ones on the same stores
                StartSyncs();
            }
            else if (!CloudService.IsConnected && IsSyncing)
            {
                await StopSyncsAsync();
            }

            return;
        }

        await CloseAsync();
        Open();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "Abmelden" (Einstellungen › Cloud): the syncs stop; with <paramref name="keepLocalCopy"/> the account's contacts and own calendars become those of
    /// this computer (what only this computer had goes into the account first – nothing is lost); ThixMail signs out and the local data comes in.
    /// Without a copy the local data shows as it was; the account's stores stay for its next sign-in.
    /// </summary>
    public async Task SignOutAsync(bool keepLocalCopy)
    {
        await StopSyncsAsync();
        if (keepLocalCopy && IsAccountData && People is { } people && Calendar is { } calendar)
        {
            PimStores.KeepLocalCopy(ThixContactsApp.StoreName, people.Store, ContactsDirectory);
            CalendarTransfer.KeepLocalCopy(calendar.Store, PimStores.LocalPath(CalendarStoreName));
        }

        await CloudService.DisconnectAsync();
        await ReconnectAsync();
    }

    /// <summary>Stops the syncs (the cloud ended the sign-in): the data stays, changes wait.</summary>
    public async Task StopSyncsAsync()
    {
        foreach (var sync in new[] { _peopleSync, _calendarSync })
        {
            if (sync is not null)
            {
                sync.StatusChanged -= OnSyncStatusChanged;
                try
                {
                    await sync.DisposeAsync();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        _peopleSync = null;
        _calendarSync = null;
    }

    /// <summary>Looks at once whether ThixContacts changed the contacts (the window came to the front); the watch does it every two seconds anyway.</summary>
    public void CheckForOtherChanges() => People?.Store.CheckForOtherChanges();

    public async ValueTask DisposeAsync() => await CloseAsync();

    private void Open()
    {
        try
        {
            IsAccountData = CloudService.HasAccount && !AccessPolicy.IsCloudDisabled;
            if (IsAccountData)
            {
                TakeOverFormerContacts();
            }

            var people = PimStores.Open(ThixContactsApp.StoreName, ContactsDirectory);
            people.WatchOtherProcesses();
            var calendar = PimStores.Open(CalendarStoreName, mergeLocal: false);
            People = new ContactsSession(people);
            Calendar = new CalendarBook(calendar) { AwaitsFirstSync = IsAccountData ? () => calendar.LastSync is null : null };
            People.Changed += OnChanged;
            Calendar.Changed += OnChanged;
            People.LocalChanged += (_, _) => _peopleSync?.Trigger();
            Calendar.LocalChanged += (_, _) => _calendarSync?.Trigger();
            _calendarMergePending = IsAccountData;
            MergeLocalCalendarWhenReady();
            StartSyncs();
        }
        catch (Exception exception) when (exception is BusinessException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Warning($"The calendar and people could not be opened: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// The account's contacts ThixMail kept in a store of its own until 2026-11-22 (<c>mailcontacts-&lt;account&gt;.db</c>): their changes that have not
    /// gone up yet go into the shared store (the rest is in the cloud and comes with the sync); the old file is renamed, never deleted.
    /// </summary>
    private static void TakeOverFormerContacts()
    {
        var former = PimStores.PathFor(FormerContactsStoreName);
        if (!File.Exists(former))
        {
            return;
        }

        using (var old = PimStore.Open(former))
        {
            var shared = PimStores.PathFor(ThixContactsApp.StoreName, ContactsDirectory);
            using var target = PimStore.Open(shared);
            var waiting = old.AllItems(withDeleted: true).Where(item => item.IsDirty).ToList();
            var known = target.AllItems(withDeleted: true).ToDictionary(item => (item.Collection, item.Key), item => item.UpdatedAt);
            var newer = waiting.Where(item => !known.TryGetValue((item.Collection, item.Key), out var there) || there < item.UpdatedAt).ToList();
            target.Import(newer);
            foreach (var id in old.PhotosToUpload())
            {
                if (old.Photo(id) is { } bytes && !target.HasPhoto(id))
                {
                    target.PutPhoto(id, bytes, isUploaded: false);
                }
            }

            Log.Info($"The former contacts of ThixMail: {newer.Count} waiting changes went into the shared store");
        }

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(former + suffix))
            {
                File.Move(former + suffix, former + ".taken-over" + suffix, overwrite: true);
            }
        }
    }

    /// <summary>After the account's first sync the calendar of this computer goes into it (once per opening; nothing doubles when it runs again).</summary>
    private void MergeLocalCalendarWhenReady()
    {
        if (!_calendarMergePending || Calendar is not { } book || book.Store.LastSync is null)
        {
            return;
        }

        _calendarMergePending = false;
        var local = PimStores.LocalPath(CalendarStoreName);
        if (!File.Exists(local))
        {
            return;
        }

        try
        {
            using var source = PimStore.Open(local);
            if (CalendarTransfer.MergeInto(source, book.Store) > 0)
            {
                Log.Info("The calendar of this computer went into the account's calendar");
                book.Invalidate(local: true);
            }
        }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            _calendarMergePending = true;
            Log.Warning($"The local calendar could not go into the account's: {exception.GetType().Name}");
        }
    }

    private void StartSyncs()
    {
        if (!CloudService.IsConnected || AccessPolicy.IsCloudDisabled || People is null || Calendar is null)
        {
            return;
        }

        if (_peopleSync is null)
        {
            _peopleSync = new PimSync(People.Store, CloudService.CreateClient, app: CloudApps.Contacts);
            _peopleSync.StatusChanged += OnSyncStatusChanged;
        }

        if (_calendarSync is null)
        {
            _calendarSync = new PimSync(Calendar.Store, CloudService.CreateClient, app: CloudApps.Calendar);
            _calendarSync.StatusChanged += OnSyncStatusChanged;
        }

        _peopleSync.Start();
        _calendarSync.Start();
    }

    private void OnSyncStatusChanged(object? sender, SyncStatus status)
    {
        if (sender == _calendarSync && status.State == SyncState.Idle && _calendarMergePending)
        {
            MergeLocalCalendarWhenReady();
        }

        SyncStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CloseAsync()
    {
        await StopSyncsAsync();
        if (People is { } people)
        {
            people.Changed -= OnChanged;
            people.Dispose();
            people.Store.Dispose();
        }

        if (Calendar is { } calendar)
        {
            calendar.Changed -= OnChanged;
            calendar.Dispose();
            calendar.Store.Dispose();
        }

        People = null;
        Calendar = null;
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

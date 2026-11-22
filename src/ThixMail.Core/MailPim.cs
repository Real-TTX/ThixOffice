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
/// The calendar and the people of the ThixCloud account inside ThixMail (Outlook's "Kalender" and "Personen"): two stores of this app – the contacts
/// (grant <c>mail</c> → <c>contacts</c>) and ThixCalendar's items (grant <c>mail</c> → <c>calendar</c>) – each with its <see cref="PimSync"/> while
/// signed in. Without an account there is nothing (the views offer the sign-in; mail works without the cloud). The contacts are ThixContacts'
/// <see cref="ContactsSession"/> (the same rules, the same undo), the calendar a <see cref="CalendarBook"/>. <see cref="ReconnectAsync"/> follows a
/// change of the sign-in.
/// </summary>
public sealed class MailPim : IAsyncDisposable
{
    public const string ContactsStoreName = "mailcontacts";
    public const string CalendarStoreName = "mailcalendar";

    private PimSync? _peopleSync;
    private PimSync? _calendarSync;

    public MailPim()
    {
        Open();
    }

    /// <summary>The contacts of the account, or null without one.</summary>
    public ContactsSession? People { get; private set; }

    /// <summary>The calendar of the account, or null without one.</summary>
    public CalendarBook? Calendar { get; private set; }

    /// <summary>True while ThixMail has an account of ThixCloud (signed in, or signed out by the cloud: the data stays until "Abmelden").</summary>
    public bool IsAvailable => People is not null && Calendar is not null;

    /// <summary>True while the two syncs run.</summary>
    public bool IsSyncing => _peopleSync is not null || _calendarSync is not null;

    /// <summary>The people or the calendar changed (here or by the sync: then on its thread), or another account came in.</summary>
    public event EventHandler? Changed;

    /// <summary>The state of the calendar's sync (the status bar of the view).</summary>
    public SyncStatus CalendarSyncStatus => _calendarSync?.Status ?? SyncStatus.Off;

    public SyncStatus PeopleSyncStatus => _peopleSync?.Status ?? SyncStatus.Off;

    /// <summary>One round of both syncs now (F9 in a view); refused without a sign-in.</summary>
    public async Task SyncNowAsync()
    {
        if (_peopleSync is null && _calendarSync is null)
        {
            throw new BusinessException("ThixMail ist nicht bei der ThixCloud angemeldet (Datei › Einstellungen › Cloud).");
        }

        try
        {
            if (_peopleSync is { } people)
            {
                await people.SyncOnceAsync();
            }

            if (_calendarSync is { } calendar)
            {
                await calendar.SyncOnceAsync();
            }
        }
        catch (Exception exception) when (CloudService.IsCloudFailure(exception, CancellationToken.None))
        {
            throw CloudService.Explain(exception);
        }
    }

    /// <summary>The sign-in changed: the stores of the account now (or none), the syncs follow. No-op when nothing changed.</summary>
    public async Task ReconnectAsync()
    {
        var path = CloudService.HasAccount ? PimStores.PathFor(CalendarStoreName) : null;
        if (path is not null && Calendar is not null && string.Equals(path, Calendar.Store.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (CloudService.IsConnected && !IsSyncing)
            {
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

    /// <summary>Stops the syncs (the cloud ended the sign-in): the data stays, changes wait.</summary>
    public async Task StopSyncsAsync()
    {
        foreach (var sync in new[] { _peopleSync, _calendarSync })
        {
            if (sync is not null)
            {
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

    public async ValueTask DisposeAsync() => await CloseAsync();

    private void Open()
    {
        try
        {
            if (!CloudService.HasAccount || AccessPolicy.IsCloudDisabled)
            {
                return;
            }

            var people = PimStores.Open(ContactsStoreName);
            var calendar = PimStores.Open(CalendarStoreName);
            People = new ContactsSession(people);
            Calendar = new CalendarBook(calendar);
            People.Changed += OnChanged;
            Calendar.Changed += OnChanged;
            People.LocalChanged += (_, _) => _peopleSync?.Trigger();
            Calendar.LocalChanged += (_, _) => _calendarSync?.Trigger();
            StartSyncs();
        }
        catch (Exception exception) when (exception is BusinessException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Warning($"The calendar and people of the account could not be opened: {exception.GetType().Name}");
        }
    }

    private void StartSyncs()
    {
        if (!CloudService.IsConnected || People is null || Calendar is null)
        {
            return;
        }

        _peopleSync ??= new PimSync(People.Store, CloudService.CreateClient, app: CloudApps.Contacts);
        _calendarSync ??= new PimSync(Calendar.Store, CloudService.CreateClient, app: CloudApps.Calendar);
        _peopleSync.Start();
        _calendarSync.Start();
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

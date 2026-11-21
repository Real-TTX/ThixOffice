using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixContacts.Core;

/// <summary>
/// The "document" of ThixContacts' one window – the workspace: the contacts of the store of the account the app is signed in to (or of this computer
/// only), their <see cref="ContactsSession"/>, and while signed in the sync with ThixCloud (<see cref="PimSync"/>, started at once, taken up after every
/// change of this computer). It never has a file and never unsaved changes: every change is in the store at once. Undo and redo are the session's.
/// When the sign-in changes, <see cref="ReconnectAsync"/> opens the store of the new account.
/// </summary>
public sealed class ContactsDocument : OfficeDocument
{
    private PimStore _store;
    private PimSync? _sync;

    public ContactsDocument(string untitledName)
        : base(untitledName)
    {
        (_store, Session) = OpenSession();
        StartSync();
    }

    /// <summary>The contacts of the current store (a new one after <see cref="ReconnectAsync"/>).</summary>
    public ContactsSession Session { get; private set; }

    /// <summary>Raised when the store and so the session changed (another account); the views read <see cref="Session"/> again.</summary>
    public event EventHandler? SessionChanged;

    /// <summary>Raised when the sync's state changes (any thread).</summary>
    public event EventHandler<SyncStatus>? SyncStatusChanged;

    public SyncStatus SyncStatus => _sync?.Status ?? SyncStatus.Off;

    /// <summary>True while the contacts sync with ThixCloud.</summary>
    public bool IsSyncing => _sync is not null;

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    /// <summary>
    /// "Jetzt synchronisieren": one round now (push, pull). Refused when the app is not signed in; a failure of the cloud as a German
    /// <see cref="BusinessException"/>.
    /// </summary>
    public async Task<SyncReport> SyncNowAsync()
    {
        if (_sync is null)
        {
            throw new BusinessException("ThixContacts ist nicht bei der ThixCloud angemeldet. Die Kontakte sind nur auf diesem Computer (Datei › Einstellungen › Cloud).");
        }

        try
        {
            return await _sync.SyncOnceAsync();
        }
        catch (ObjectDisposedException)
        {
            throw new BusinessException("Die Synchronisierung wurde beendet (abgemeldet). Bitte melden Sie sich erneut an.");
        }
        catch (Exception exception) when (CloudService.IsCloudFailure(exception, CancellationToken.None))
        {
            throw CloudService.Explain(exception);
        }
    }

    /// <summary>The sign-in changed: the store of the account now (or of this computer) is opened, the sync follows. No-op when it is the same store.</summary>
    public async Task ReconnectAsync()
    {
        var path = PimStores.PathFor(ThixContactsApp.StoreName);
        if (string.Equals(path, _store.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (CloudService.IsConnected && _sync is not { IsRunning: true })
            {
                await StopSyncAsync();
                StartSync();
            }

            return;
        }

        await CloseAsync();
        (_store, Session) = OpenSession();
        StartSync();
        SessionChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    /// <summary>Stops the sync; the contacts stay (the cloud ended the sign-in: they wait for the next one).</summary>
    public async Task StopSyncAsync()
    {
        if (_sync is { } sync)
        {
            _sync = null;
            sync.StatusChanged -= OnSyncStatusChanged;
            await sync.DisposeAsync();
            SyncStatusChanged?.Invoke(this, SyncStatus);
        }
    }

    /// <summary>Stops the sync and closes the store (the window closes, the app ends).</summary>
    public async Task CloseAsync()
    {
        await StopSyncAsync();
        Session.LocalChanged -= OnLocalChanged;
        Session.Changed -= OnSessionChanged;
        Session.Dispose();
        _store.Dispose();
    }

    protected override void Read(Stream stream) => throw new NotSupportedException("ThixContacts keeps its contacts in its store.");

    protected override void Write(Stream stream) => throw new NotSupportedException("ThixContacts keeps its contacts in its store.");

    private (PimStore Store, ContactsSession Session) OpenSession()
    {
        var store = PimStores.Open(ThixContactsApp.StoreName);
        var session = new ContactsSession(store);
        session.LocalChanged += OnLocalChanged;
        session.Changed += OnSessionChanged;
        Log.Info($"Contacts: {session.Count} in {Path.GetFileName(store.Path)}");
        return (store, session);
    }

    private void StartSync()
    {
        if (!CloudService.IsConnected || AccessPolicy.IsCloudDisabled || _sync is not null)
        {
            return;
        }

        _sync = new PimSync(_store, CloudService.CreateClient);
        _sync.StatusChanged += OnSyncStatusChanged;
        _sync.Start();
    }

    private void OnLocalChanged(object? sender, EventArgs e) => _sync?.Trigger();

    private void OnSessionChanged(object? sender, EventArgs e) => RaiseChanged();

    private void OnSyncStatusChanged(object? sender, SyncStatus status) => SyncStatusChanged?.Invoke(this, status);
}

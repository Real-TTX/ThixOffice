using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixMail.Core;

/// <summary>
/// The "document" of ThixMail's one window – the workspace: the message store and the settings store of this computer, the session of actions, one
/// <see cref="MailSync"/> per account (started at once unless the person works offline), the address suggestions (recent recipients, and the contacts
/// of the ThixCloud account read through the grant <c>mail</c> → <c>contacts</c> while signed in). Never dirty, no file. Undo and redo are the session's.
/// </summary>
public sealed class MailDocument : OfficeDocument
{
    /// <summary>How ThixMail reaches mail servers (tests, snapshots and the self test plug the <see cref="SampleMailServer"/> in).</summary>
    public static IMailTransport Transport { get; set; } = MailKitTransport.Instance;

    /// <summary>Tests make the sync's waits short.</summary>
    internal static Action<MailSync>? ConfigureSyncForTests { get; set; }

    private readonly Dictionary<string, MailSync> _syncs = new(StringComparer.Ordinal);
    private string? _viewName;

    public MailDocument(string untitledName)
        : base(untitledName)
    {
        Store = MailStore.Open(Path.Combine(AppPaths.DataDirectory, ThixMailApp.StoreFileName));
        Accounts = AccountStore.Open(PimStores.LocalPath(ThixMailApp.SettingsStoreName));
        Session = new MailSession(Store, Accounts, Wake);
        Session.Changed += (_, _) => RaiseChanged();
        IsWorkingOffline = SettingsService.AppValue(Settings.WorkOffline) == "true";
        StartSyncs();
        Pim = new MailPim();
    }

    public MailStore Store { get; }

    public AccountStore Accounts { get; }

    public MailSession Session { get; }

    /// <summary>The calendar and the people of the ThixCloud account (null inside while not signed in).</summary>
    public MailPim Pim { get; }

    /// <summary>The name of the view the window shows – "E-Mail", "Kalender", "Personen" (set by the editor); the title bar names it, as Outlook does.</summary>
    public string ViewName
    {
        get => _viewName ?? UntitledName;
        set
        {
            if (value != ViewName)
            {
                _viewName = value;
                RaiseChanged();
            }
        }
    }

    public override string Title => ViewName;

    /// <summary>Raised when an account's sync state changes (any thread).</summary>
    public event EventHandler<AccountSyncStatus>? SyncStatusChanged;

    /// <summary>New unread mail arrived (any thread): the notification.</summary>
    public event EventHandler<NewMailEventArgs>? NewMail;

    /// <summary>"Offline arbeiten": no connection at all; actions and messages to send wait in the queue.</summary>
    public bool IsWorkingOffline { get; private set; }

    public IReadOnlyCollection<MailSync> Syncs => _syncs.Values;

    public MailSync? SyncOf(string accountId) => _syncs.GetValueOrDefault(accountId);

    public AccountSyncStatus StatusOf(string accountId) =>
        IsWorkingOffline ? new AccountSyncStatus(accountId, SyncPhase.WorkingOffline, "Offline arbeiten", 0, null) : _syncs.GetValueOrDefault(accountId)?.Status ?? AccountSyncStatus.Off(accountId);

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    // ===== accounts =====

    /// <summary>Adds an account (validated; the connection test ran before) with its passwords, and starts its sync.</summary>
    public MailAccount AddAccount(MailAccount account, string imapPassword, string? smtpPassword)
    {
        var valid = AccountService.Validate(account) with { Order = Accounts.Accounts.Count };
        if (Accounts.Accounts.Any(a => a.Address.Equals(valid.Address, StringComparison.OrdinalIgnoreCase) && a.ImapUser == valid.ImapUser && a.Imap.Host == valid.Imap.Host))
        {
            throw new BusinessException("Dieses Konto ist schon eingerichtet.", nameof(MailAccount.Address));
        }

        Accounts.Save(valid);
        AccountService.SetPassword(Accounts, valid.Id, AccountService.ImapSecret, imapPassword);
        if (!valid.SmtpUsesImapPassword && smtpPassword is not null)
        {
            AccountService.SetPassword(Accounts, valid.Id, AccountService.SmtpSecret, smtpPassword);
        }

        StartSync(valid);
        RaiseChanged();
        return valid;
    }

    /// <summary>Changes an account (and its passwords when given); the sync uses the new settings at once.</summary>
    public MailAccount UpdateAccount(MailAccount account, string? imapPassword = null, string? smtpPassword = null)
    {
        var valid = AccountService.Validate(account);
        Accounts.Save(valid);
        if (imapPassword is not null)
        {
            AccountService.SetPassword(Accounts, valid.Id, AccountService.ImapSecret, imapPassword);
        }

        if (smtpPassword is not null)
        {
            AccountService.SetPassword(Accounts, valid.Id, AccountService.SmtpSecret, smtpPassword);
        }

        if (_syncs.TryGetValue(valid.Id, out var sync))
        {
            sync.Update(valid);
        }
        else
        {
            StartSync(valid);
        }

        RaiseChanged();
        return valid;
    }

    /// <summary>Removes an account from this computer: its sync stops, its messages, queue and passwords go (the server keeps everything).</summary>
    public async Task RemoveAccountAsync(string accountId)
    {
        if (_syncs.Remove(accountId, out var sync))
        {
            sync.StatusChanged -= OnSyncStatus;
            sync.NewMail -= OnNewMail;
            await sync.DisposeAsync();
        }

        Store.RemoveAccount(accountId);
        AccountService.ForgetPasswords(Accounts, accountId);
        Accounts.Remove(accountId);
        RaiseChanged();
    }

    // ===== sync =====

    /// <summary>"Alle Ordner senden/empfangen" (F9): every account runs a full round now.</summary>
    public void SendReceiveAll()
    {
        if (IsWorkingOffline)
        {
            throw new BusinessException("ThixMail arbeitet offline. Schalten Sie „Offline arbeiten“ aus, um zu senden und zu empfangen.");
        }

        foreach (var account in Accounts.Accounts)
        {
            if (_syncs.TryGetValue(account.Id, out var sync))
            {
                sync.Trigger(full: true);
                if (!sync.IsRunning)
                {
                    sync.Start();
                }
            }
            else
            {
                StartSync(account);
            }
        }
    }

    /// <summary>"Offline arbeiten" on or off (remembered).</summary>
    public async Task SetWorkingOfflineAsync(bool offline)
    {
        IsWorkingOffline = offline;
        SettingsService.RememberAppValue(Settings.WorkOffline, offline ? "true" : null);
        if (offline)
        {
            foreach (var sync in _syncs.Values)
            {
                await sync.StopAsync();
            }

            foreach (var account in Accounts.Accounts)
            {
                SyncStatusChanged?.Invoke(this, StatusOf(account.Id));
            }
        }
        else
        {
            StartSyncs();
        }

        RaiseChanged();
    }

    /// <summary>The body of a message: from the cache, else fetched (null offline / when the server lost it).</summary>
    public async Task<byte[]?> BodyAsync(long messageId, CancellationToken cancellationToken)
    {
        if (Store.Raw(messageId) is { } cached)
        {
            return cached;
        }

        var header = Store.Header(messageId);
        if (header is null || IsWorkingOffline || !_syncs.TryGetValue(header.AccountId, out var sync))
        {
            return null;
        }

        var raw = await sync.FetchBodyAsync(messageId, cancellationToken);
        Store.EnforceBudget(CacheBudget);
        return raw;
    }

    /// <summary>The size budget of cached messages (Einstellungen › Erweitert; default 500 MB).</summary>
    public static long CacheBudget =>
        long.TryParse(SettingsService.AppValue(Settings.CacheBudgetMb), out var megabytes) && megabytes >= 50 ? megabytes * 1024 * 1024 : 500L * 1024 * 1024;

    private void StartSyncs()
    {
        if (IsWorkingOffline)
        {
            return;
        }

        foreach (var account in Accounts.Accounts)
        {
            StartSync(account);
        }
    }

    private void StartSync(MailAccount account)
    {
        if (IsWorkingOffline)
        {
            return;
        }

        if (!_syncs.TryGetValue(account.Id, out var sync))
        {
            sync = new MailSync(Store, account, Transport, (a, kind) => AccountService.Password(Accounts, a, kind));
            ConfigureSyncForTests?.Invoke(sync);
            sync.StatusChanged += OnSyncStatus;
            sync.NewMail += OnNewMail;
            _syncs[account.Id] = sync;
        }

        sync.Start();
    }

    private void Wake(string accountId)
    {
        if (!IsWorkingOffline && _syncs.TryGetValue(accountId, out var sync))
        {
            sync.Trigger();
        }
    }

    private void OnSyncStatus(object? sender, AccountSyncStatus status) => SyncStatusChanged?.Invoke(this, status);

    private void OnNewMail(object? sender, NewMailEventArgs e) => NewMail?.Invoke(this, e);

    // ===== address suggestions =====

    /// <summary>Suggestions for an address field: recent recipients first, then contacts of the ThixCloud account (when signed in).</summary>
    public IReadOnlyList<MailAddress> SuggestAddresses(string text, int limit = 8)
    {
        var result = Store.SuggestRecipients(text, limit).ToList();
        if (Pim.People?.Store is { } contacts && result.Count < limit)
        {
            foreach (var (person, email) in PersonItems.Suggest(contacts, text, limit))
            {
                var address = new MailAddress(person.Name, email);
                if (address.Address.Length > 0 && !result.Any(r => r.Address.Equals(address.Address, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(address);
                }
            }
        }

        return [.. result.Take(limit)];
    }

    // ===== closing =====

    /// <summary>True after <see cref="CloseAsync"/> (posted callbacks of the views do nothing then).</summary>
    public bool IsClosed { get; private set; }

    public async Task CloseAsync()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        foreach (var sync in _syncs.Values)
        {
            sync.StatusChanged -= OnSyncStatus;
            sync.NewMail -= OnNewMail;
            await sync.DisposeAsync();
        }

        _syncs.Clear();
        await Pim.DisposeAsync();
        Store.Dispose();
        Accounts.Dispose();
    }

    protected override void Read(Stream stream) => throw new NotSupportedException("ThixMail keeps its mail in its store.");

    protected override void Write(Stream stream) => throw new NotSupportedException("ThixMail keeps its mail in its store.");

    /// <summary>The keys of ThixMail's settings (AppValues of settings.json).</summary>
    public static class Settings
    {
        public const string WorkOffline = "mail.offline";
        public const string ReadingPane = "mail.readingPane";
        public const string Conversations = "mail.conversations";
        public const string Density = "mail.density";
        public const string FolderPane = "mail.folderPane";
        public const string MarkReadSeconds = "mail.markReadSeconds";
        public const string RemoteImages = "mail.remoteImages";
        public const string ComposeFont = "mail.composeFont";
        public const string ComposeSize = "mail.composeSize";
        public const string Notifications = "mail.notifications";
        public const string NotificationSound = "mail.notificationSound";
        public const string KeepRunning = "mail.keepRunning";
        public const string CacheBudgetMb = "mail.cacheBudgetMb";
        public const string ConfirmDelete = "mail.confirmDelete";
    }
}

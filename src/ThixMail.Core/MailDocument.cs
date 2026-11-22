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
        Accounts.Changed += OnAccountsChanged;
        StartSettingsSync();
    }

    private PimSync? _settingsSync;
    private bool _savingAccount;

    /// <summary>The state of the settings' sync through ThixCloud (accounts without passwords, signatures, categories, rules, templates).</summary>
    public SyncStatus SettingsSyncStatus => _settingsSync?.Status ?? SyncStatus.Off;

    /// <summary>The sign-in of ThixCloud changed: the settings' sync follows (started, stopped, or – another ThixCloud account – started afresh).</summary>
    public async Task ReconnectSettingsAsync()
    {
        // signed out, another account – or a loop that ended (ThixCloud ended the sign-in: 401) and must run again after the new sign-in
        if (_settingsSync is { } running && (!CloudService.IsConnected || Accounts.Store.GetMeta(CloudAccountMeta) != CloudIdentity() || !running.IsRunning))
        {
            await running.DisposeAsync();
            _settingsSync = null;
        }

        StartSettingsSync();
    }

    private const string CloudAccountMeta = "cloudAccount";

    private static string CloudIdentity()
    {
        var cloud = SettingsService.Get().Cloud;
        return $"{cloud.UserName}@{cloud.ServerUrl}";
    }

    /// <summary>
    /// ThixCloud's general sync of the settings store as the app <c>mail</c> (contract 1.5.0 takes any collection of the app's own: mail-accounts,
    /// mail-signatures, mail-categories, mail-rules, mail-templates). Passwords and OAuth tokens are meta values of the store and never items: they stay on
    /// this computer. Another ThixCloud account than last time starts the pull from the beginning.
    /// </summary>
    private void StartSettingsSync()
    {
        if (_settingsSync is not null || IsClosed || !CloudService.IsConnected || AccessPolicy.IsCloudDisabled)
        {
            return;
        }

        var identity = CloudIdentity();
        if (Accounts.Store.GetMeta(CloudAccountMeta) != identity)
        {
            // the settings are this computer's and follow the sign-in: everything here goes up into the (other) account, its settings come down and join
            // them (like ThixContacts' local store taken into a new account) – nothing of this computer is lost by switching ThixCloud accounts
            Accounts.Store.MarkAllDirty();
            Accounts.Store.SetMeta(CloudAccountMeta, identity);
        }

        _settingsSync = new PimSync(Accounts.Store, CloudService.CreateClient);
        _settingsSync.Start();
    }

    /// <summary>The settings changed – here (they go up) or from another computer through ThixCloud (accounts that came start, accounts that went stop).</summary>
    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        if (IsClosed)
        {
            return;
        }

        _settingsSync?.Trigger();
        if (_savingAccount)
        {
            return; // AddAccount / UpdateAccount / RemoveAccountAsync take care of the syncs themselves
        }

        var accounts = Accounts.Accounts;
        foreach (var account in accounts)
        {
            if (!_syncs.TryGetValue(account.Id, out var running))
            {
                StartSync(account);
            }
            else if (!Accounts.IsConfirmed(account))
            {
                _ = StopUnconfirmedAsync(account.Id); // another computer changed its servers: nothing goes there before the test here
            }
            else if (!SameSettings(running.Account, account))
            {
                running.Update(account); // a name, a signature, the order – from another computer, at once
            }
        }

        // gone: a running sync, or mail kept here of an account whose sync does not run (waiting for the test, working offline)
        var here = _syncs.Keys.Concat(Store.Folders().Select(f => f.AccountId)).Distinct(StringComparer.Ordinal);
        foreach (var gone in here.Where(id => accounts.All(a => a.Id != id)).ToList())
        {
            _ = RemoveSyncAsync(gone);
        }
    }

    /// <summary>The servers of a running account changed on another computer: its sync stops (the mail here stays) until the test passes here.</summary>
    private async Task StopUnconfirmedAsync(string accountId)
    {
        if (!_syncs.Remove(accountId, out var sync))
        {
            return;
        }

        sync.StatusChanged -= OnSyncStatus;
        sync.NewMail -= OnNewMail;
        sync.Arrived -= OnArrived;
        await sync.DisposeAsync();
        SyncStatusChanged?.Invoke(this, StatusOf(accountId));
        RaiseChanged();
    }

    /// <summary>An account removed on another computer: its sync stops, its mail and secrets on this computer go.</summary>
    private async Task RemoveSyncAsync(string accountId)
    {
        if (_syncs.Remove(accountId, out var sync))
        {
            sync.StatusChanged -= OnSyncStatus;
            sync.NewMail -= OnNewMail;
            sync.Arrived -= OnArrived;
            await sync.DisposeAsync();
        }

        if (IsClosed)
        {
            return;
        }

        Store.RemoveAccount(accountId);
        AccountService.ForgetPasswords(Accounts, accountId);
        RaiseChanged();
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
        IsWorkingOffline ? new AccountSyncStatus(accountId, SyncPhase.WorkingOffline, "Offline arbeiten", 0, null)
        : _syncs.GetValueOrDefault(accountId)?.Status
          ?? (Accounts.Account(accountId) is { } account && !Accounts.IsConfirmed(account)
              ? new AccountSyncStatus(accountId, SyncPhase.NeedsConfirmation, NeedsConfirmationMessage, 0, null)
              : AccountSyncStatus.Off(accountId));

    /// <summary>What an account from another computer (or with servers changed there) says until the connection test passed here.</summary>
    public const string NeedsConfirmationMessage = "Von einem anderen Computer – bitte prüfen Sie die Verbindung (Konten › Bearbeiten)";

    /// <summary>The account still waits for the connection test on this computer (<see cref="AccountStore.IsConfirmed"/>).</summary>
    public bool NeedsConfirmation(MailAccount account) => !Accounts.IsConfirmed(account);

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    // ===== accounts =====

    /// <summary>Adds an account (validated; the connection test ran before) with its passwords, and starts its sync.</summary>
    public MailAccount AddAccount(MailAccount account, string imapPassword, string? smtpPassword, OAuthTokens? tokens = null)
    {
        var valid = AccountService.Validate(account) with { Order = Accounts.Accounts.Count };
        if (Accounts.Accounts.Any(a => a.Address.Equals(valid.Address, StringComparison.OrdinalIgnoreCase) && a.ImapUser == valid.ImapUser && a.Imap.Host == valid.Imap.Host))
        {
            throw new BusinessException("Dieses Konto ist schon eingerichtet.", nameof(MailAccount.Address));
        }

        SaveQuietly(() => Accounts.Save(valid));
        Accounts.Confirm(valid); // the form's connection test passed on this computer
        if (valid.UsesOAuth)
        {
            AccountService.SetOAuthTokens(Accounts, valid.Id, tokens ?? throw new BusinessException("Bitte melden Sie sich zuerst im Browser an.", "Password"));
        }
        else
        {
            AccountService.SetPassword(Accounts, valid.Id, AccountService.ImapSecret, imapPassword);
        }

        if (!valid.UsesOAuth && !valid.SmtpUsesImapPassword && smtpPassword is not null)
        {
            AccountService.SetPassword(Accounts, valid.Id, AccountService.SmtpSecret, smtpPassword);
        }

        StartSync(valid);
        RaiseChanged();
        return valid;
    }

    /// <summary>Changes an account (and its passwords when given); the sync uses the new settings at once.</summary>
    public MailAccount UpdateAccount(MailAccount account, string? imapPassword = null, string? smtpPassword = null, OAuthTokens? tokens = null)
    {
        var valid = AccountService.Validate(account);
        SaveQuietly(() => Accounts.Save(valid));
        Accounts.Confirm(valid); // saved through the form after its connection test on this computer
        if (tokens is not null)
        {
            AccountService.SetOAuthTokens(Accounts, valid.Id, tokens);
        }

        if (valid.UsesOAuth)
        {
            // a password the account had before the browser sign-in is of no use any more
            imapPassword = "";
            smtpPassword = "";
        }
        else
        {
            AccountService.SetOAuthTokens(Accounts, valid.Id, null);
        }

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

    /// <summary>Writes an account without the reconciliation of remote changes (the caller starts or stops its sync itself).</summary>
    private void SaveQuietly(Action write)
    {
        _savingAccount = true;
        try
        {
            write();
        }
        finally
        {
            _savingAccount = false;
        }
    }

    /// <summary>Removes an account from this computer: its sync stops, its messages, queue and passwords go (the server keeps everything).</summary>
    public async Task RemoveAccountAsync(string accountId)
    {
        if (_syncs.Remove(accountId, out var sync))
        {
            sync.StatusChanged -= OnSyncStatus;
            sync.NewMail -= OnNewMail;
            sync.Arrived -= OnArrived;
            await sync.DisposeAsync();
        }

        Store.RemoveAccount(accountId);
        if (Accounts.Accounts.FirstOrDefault(a => a.Id == accountId) is { } account)
        {
            await AccountService.ForgetAsync(Accounts, account, CancellationToken.None);
        }
        else
        {
            AccountService.ForgetPasswords(Accounts, accountId);
        }

        foreach (var rule in Accounts.Rules.Where(r => r.AccountId == accountId))
        {
            SaveQuietly(() => Accounts.RemoveRule(rule.Id)); // its folders are gone with the account
        }

        SaveQuietly(() => Accounts.Remove(accountId));
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

        if (!Accounts.IsConfirmed(account))
        {
            SyncStatusChanged?.Invoke(this, StatusOf(account.Id)); // from another computer: waits for the connection test here
            return;
        }

        if (!_syncs.TryGetValue(account.Id, out var sync))
        {
            sync = new MailSync(Store, account, Transport, (a, kind, renew, cancellationToken) => AccountService.CredentialAsync(Accounts, a, kind, renew, cancellationToken));
            ConfigureSyncForTests?.Invoke(sync);
            sync.StatusChanged += OnSyncStatus;
            sync.NewMail += OnNewMail;
            sync.Arrived += OnArrived;
            _syncs[account.Id] = sync;
        }

        sync.Start();
    }

    private static bool SameSettings(MailAccount a, MailAccount b) =>
        a with { TrustedCertificates = [] } == b with { TrustedCertificates = [] } && a.TrustedCertificates.SequenceEqual(b.TrustedCertificates, StringComparer.Ordinal);

    private void Wake(string accountId)
    {
        if (!IsWorkingOffline && _syncs.TryGetValue(accountId, out var sync))
        {
            sync.Trigger();
        }
    }

    private void OnSyncStatus(object? sender, AccountSyncStatus status) => SyncStatusChanged?.Invoke(this, status);

    private void OnNewMail(object? sender, NewMailEventArgs e) => NewMail?.Invoke(this, e);

    /// <summary>New mail in a Posteingang: the enabled rules run on it at once (on the sync's thread, not undoable – like Outlook's rules on arrival).</summary>
    private void OnArrived(object? sender, ArrivedEventArgs e)
    {
        var rules = Accounts.Rules;
        if (rules.Count == 0 || IsClosed)
        {
            return;
        }

        var outcomes = MailRules.Plan(rules, e.Messages, Store);
        if (outcomes.Count > 0)
        {
            Session.ApplyRules(outcomes, undoable: false);
        }
    }

    // ===== address suggestions =====

    /// <summary>Suggestions for an address field: recent recipients first, then contacts of the ThixCloud account (when signed in).</summary>
    public IReadOnlyList<MailAddress> SuggestAddresses(string text, int limit = 8)
    {
        if (IsClosed)
        {
            return []; // a text change of a compose window posted before the main window closed runs after the store is gone
        }

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

    /// <summary>
    /// Raised on the closing thread before anything closes (the stores are still open): windows that use the document – compose windows – keep what they
    /// hold and let go of it. After it, <see cref="IsClosed"/> is true and every view's posted callback must do nothing.
    /// </summary>
    public event EventHandler? Closing;

    public async Task CloseAsync()
    {
        if (IsClosed)
        {
            return;
        }

        try
        {
            Closing?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsClosed = true;
        }

        Accounts.Changed -= OnAccountsChanged;
        if (_settingsSync is { } settingsSync)
        {
            await settingsSync.DisposeAsync();
            _settingsSync = null;
        }

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

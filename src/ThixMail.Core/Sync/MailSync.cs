using System.Text.Json;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Transport;
using ThixOffice.Core.Infrastructure;

namespace ThixMail.Core.Sync;

/// <summary>Where the sync of an account stands (the status bar and the folder pane say it in German).</summary>
public enum SyncPhase
{
    Off,
    Connecting,
    Syncing,
    Idle,
    Offline,
    /// <summary>The password is wrong or missing: the sync waits for a new one (never retries by itself).</summary>
    SignInFailed,
    /// <summary>The server shows a certificate the person has not accepted: the sync waits.</summary>
    CertificateProblem,
    WorkingOffline,
}

/// <summary>The state of one account's sync.</summary>
public sealed record AccountSyncStatus(string AccountId, SyncPhase Phase, string Message, int Pending, DateTimeOffset? LastSync, CertificateInfo? Certificate = null)
{
    public static AccountSyncStatus Off(string accountId) => new(accountId, SyncPhase.Off, "", 0, null);
}

/// <summary>What one round did (tests, "Alle Ordner senden/empfangen").</summary>
public sealed record SyncReport(int NewMessages, int Removed, int FlagChanges, int OperationsReplayed, int Sent);

/// <summary>New unread messages arrived in a Posteingang (a notification shows them).</summary>
public sealed record NewMailEventArgs(MailAccount Account, IReadOnlyList<MessageHeader> Messages);

/// <summary>
/// The sync of one account: a background loop that connects (back-off 5 s → 5 min while the server cannot be reached), lists the folders, replays the
/// operations queue in order (each operation idempotent), sends the Postausgang through SMTP (a copy into Gesendete Elemente unless the server keeps one
/// itself), then brings every folder up to date – UIDVALIDITY checked (changed: the folder is fetched again), new headers newest first (the first
/// round 1,000 per folder, older ones 1,000 per round after that), expunges by comparing the UIDs, flags by CHANGEDSINCE when the server offers
/// CONDSTORE, else polled in windows –, and waits in IDLE on the Posteingang until the server or the person (a local action: <see cref="Trigger"/>)
/// wakes it. Bodies are fetched on demand on a second connection (<see cref="FetchBodyAsync"/>). A wrong password or an unaccepted certificate
/// stops the loop until the account changes.
/// </summary>
public sealed class MailSync : IAsyncDisposable
{
    /// <summary>Headers fetched per folder in the first round (the newest), and per later round for the older ones.</summary>
    public const int Window = 1000;

    /// <summary>Messages whose flags are polled per round when the server has no CONDSTORE (the newest, plus a moving window over the rest).</summary>
    public const int FlagPoll = 300;

    private readonly MailStore _store;
    private readonly IMailTransport _transport;
    private readonly Func<MailAccount, string, string?> _password;
    private readonly SemaphoreSlim _roundGate = new(1, 1);
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _loop;
    private CancellationTokenSource? _wake;
    private Task? _loopTask;
    private IImapConnection? _fetch;
    private bool _fullRequested = true;
    private DateTimeOffset _lastFull = DateTimeOffset.MinValue;
    private bool _firstRound = true;
    private AccountSyncStatus _status;

    public MailSync(MailStore store, MailAccount account, IMailTransport transport, Func<MailAccount, string, string?> password)
    {
        _store = store;
        Account = account;
        _transport = transport;
        _password = password;
        _status = AccountSyncStatus.Off(account.Id);
    }

    public MailAccount Account { get; private set; }

    public AccountSyncStatus Status => _status;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Raised when <see cref="Status"/> changes (any thread).</summary>
    public event EventHandler<AccountSyncStatus>? StatusChanged;

    /// <summary>New unread mail in the Posteingang (not in the first round; any thread).</summary>
    public event EventHandler<NewMailEventArgs>? NewMail;

    /// <summary>Waits between rounds of the loop (tests make it short).</summary>
    public TimeSpan IdleTime { get; set; } = TimeSpan.FromMinutes(9);

    /// <summary>The first back-off while the server cannot be reached (doubles up to <see cref="MaximumBackoff"/>).</summary>
    public TimeSpan Backoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The account changed (a new password, an accepted certificate): the loop uses the new one from its next connection.</summary>
    public void Update(MailAccount account)
    {
        Account = account;
        Trigger(full: true);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _loop = new CancellationTokenSource();
            var token = _loop.Token;
            _loopTask = Task.Run(() => LoopAsync(token), CancellationToken.None);
        }
    }

    /// <summary>Something changed here (an action, a message to send) or the person pressed F9: the loop leaves IDLE and runs a round now.</summary>
    public void Trigger(bool full = false)
    {
        lock (_gate)
        {
            _fullRequested |= full;
            _wake?.Cancel();
        }
    }

    public async Task StopAsync()
    {
        Task? running;
        lock (_gate)
        {
            _loop?.Cancel();
            _wake?.Cancel();
            running = _loopTask;
        }

        if (running is not null)
        {
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await CloseFetchAsync();
        SetStatus(AccountSyncStatus.Off(Account.Id) with { Pending = PendingCount() });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _roundGate.Dispose();
        _fetchGate.Dispose();
    }

    // ===== the loop =====

    private async Task LoopAsync(CancellationToken stop)
    {
        var backoff = Backoff;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                SetStatus(_status with { Phase = SyncPhase.Connecting, Message = "Verbindung wird hergestellt …", Certificate = null });
                await using var imap = await ConnectImapAsync(stop);
                while (!stop.IsCancellationRequested)
                {
                    bool full;
                    lock (_gate)
                    {
                        full = _fullRequested || DateTimeOffset.UtcNow - _lastFull > TimeSpan.FromMinutes(10);
                        _fullRequested = false;
                        _wake = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    }

                    await RoundAsync(imap, full, stop);
                    backoff = Backoff;
                    SetStatus(new AccountSyncStatus(Account.Id, SyncPhase.Idle, "Alle Ordner sind auf dem neuesten Stand.", PendingCount(), DateTimeOffset.Now));
                    CancellationToken wake;
                    lock (_gate)
                    {
                        wake = _wake!.Token;
                    }

                    if (PendingWork())
                    {
                        continue;
                    }

                    var inbox = _store.FolderByRole(Account.Id, FolderRole.Inbox);
                    if (inbox is null)
                    {
                        await DelayAsync(IdleTime, wake);
                    }
                    else
                    {
                        await imap.IdleAsync(inbox.Path, IdleTime, wake);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (MailConnectionException exception) when (exception.Kind is MailErrorKind.Authentication or MailErrorKind.AppPasswordRequired)
            {
                SetStatus(_status with { Phase = SyncPhase.SignInFailed, Message = "Anmeldung abgelehnt – bitte das Kennwort des Kontos prüfen.", Pending = PendingCount() });
                await WaitForTriggerAsync(stop);
            }
            catch (MailConnectionException exception) when (exception.Kind == MailErrorKind.Certificate)
            {
                SetStatus(_status with { Phase = SyncPhase.CertificateProblem, Message = "Das Zertifikat des Servers ist nicht vertrauenswürdig.", Certificate = exception.Certificate, Pending = PendingCount() });
                await WaitForTriggerAsync(stop);
            }
            catch (Exception exception) when (exception is MailConnectionException or IOException or ObjectDisposedException or Microsoft.Data.Sqlite.SqliteException)
            {
                Log.Warning($"Mail sync of an account: {exception.GetType().Name}; retrying in {backoff.TotalSeconds:0} s");
                SetStatus(_status with { Phase = SyncPhase.Offline, Message = "Offline – der Mailserver ist nicht erreichbar.", Pending = PendingCount() });
                await CloseFetchAsync();
                await WaitAsync(backoff, stop);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaximumBackoff.Ticks));
            }
        }
    }

    private bool PendingWork()
    {
        lock (_gate)
        {
            return _fullRequested || _wake?.IsCancellationRequested == true;
        }
    }

    private async Task WaitForTriggerAsync(CancellationToken stop)
    {
        CancellationToken wake;
        lock (_gate)
        {
            _wake = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wake = _wake.Token;
        }

        await DelayAsync(Timeout.InfiniteTimeSpan, wake);
        stop.ThrowIfCancellationRequested();
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken stop)
    {
        CancellationToken wake;
        lock (_gate)
        {
            _wake = CancellationTokenSource.CreateLinkedTokenSource(stop);
            wake = _wake.Token;
        }

        await DelayAsync(delay, wake);
        stop.ThrowIfCancellationRequested();
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IImapConnection> ConnectImapAsync(CancellationToken cancellationToken)
    {
        var password = _password(Account, Accounts.AccountService.ImapSecret)
                       ?? throw new MailConnectionException(MailErrorKind.Authentication, "Für dieses Konto ist kein Kennwort gespeichert.");
        return await _transport.ConnectImapAsync(Account, password, cancellationToken);
    }

    /// <summary>One complete round now on its own connection (tests, the self test; F9 uses <see cref="Trigger"/> while the loop runs).</summary>
    public async Task<SyncReport> SyncOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var imap = await ConnectImapAsync(cancellationToken);
        var report = await RoundAsync(imap, full: true, cancellationToken);
        SetStatus(new AccountSyncStatus(Account.Id, SyncPhase.Idle, "Alle Ordner sind auf dem neuesten Stand.", PendingCount(), DateTimeOffset.Now));
        return report;
    }

    private async Task<SyncReport> RoundAsync(IImapConnection imap, bool full, CancellationToken cancellationToken)
    {
        await _roundGate.WaitAsync(cancellationToken);
        try
        {
            SetStatus(_status with { Phase = SyncPhase.Syncing, Message = "Senden/Empfangen …", Pending = PendingCount(), Certificate = null });
            // the queue first: a folder made offline exists at the server before the folders are listed (else the list would remove it here)
            var replayed = await ReplayAsync(imap, cancellationToken);
            if (full || replayed > 0 || _store.Folders(Account.Id).Count == 0)
            {
                _store.SyncFolders(Account.Id, await imap.ListFoldersAsync(cancellationToken));
            }

            var sent = await SendOutboxAsync(cancellationToken);
            var folders = _store.Folders(Account.Id).Where(f => f.IsSelectable).OrderBy(f => f.RoleOrder).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
            if (!full)
            {
                folders = [.. folders.Where(f => f.Role is FolderRole.Inbox or FolderRole.Sent or FolderRole.Drafts or FolderRole.Trash)];
            }

            int added = 0, removed = 0, flags = 0;
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (a, r, f) = await SyncFolderAsync(imap, folder, cancellationToken);
                added += a;
                removed += r;
                flags += f;
            }

            if (full)
            {
                _lastFull = DateTimeOffset.UtcNow;
            }

            _firstRound = false;
            return new SyncReport(added, removed, flags, replayed, sent);
        }
        finally
        {
            _roundGate.Release();
        }
    }

    private async Task<(int Added, int Removed, int Flags)> SyncFolderAsync(IImapConnection imap, MailFolder folder, CancellationToken cancellationToken)
    {
        var state = await imap.OpenAsync(folder.Path, cancellationToken);
        if (folder.UidValidity != 0 && state.UidValidity != folder.UidValidity)
        {
            Log.Info("UIDVALIDITY of a folder changed: fetching it again");
            _store.ClearFolder(folder.Id);
            folder = _store.Folder(folder.Id)!;
        }

        var known = _store.Uids(folder.Id);
        var oldest = _store.OldestUid(folder.Id);
        var unchanged = folder.UidNext != 0 && state.UidNext == folder.UidNext && state.Count == known.Count
                        && (state.HighestModSeq == 0 || state.HighestModSeq == folder.HighestModSeq) && oldest == 1;
        int added = 0, removed = 0, flagChanges = 0;
        var newUnread = new List<long>();
        if (!unchanged)
        {
            var server = await imap.UidsAsync(folder.Path, cancellationToken);
            var serverSet = server.ToHashSet();
            var knownSet = known.ToHashSet();
            var gone = known.Where(uid => !serverSet.Contains(uid)).ToList();
            removed = _store.RemoveByUid(folder.Id, gone);

            var missing = server.Where(uid => !knownSet.Contains(uid)).OrderByDescending(uid => uid).ToList();
            var newer = folder.UidNext == 0 ? [] : missing.Where(uid => uid >= folder.UidNext).ToList();
            var olderAll = missing.Except(newer).ToList();
            var older = olderAll.Take(Window).ToList();
            var toFetch = newer.Concat(older).ToList();
            foreach (var batch in toFetch.Chunk(200))
            {
                var messages = await imap.FetchAsync(folder.Path, batch, cancellationToken);
                added += _store.AddMessages(folder.Id, Account.Id, messages);
                if (folder.Role == FolderRole.Inbox && !_firstRound && folder.UidNext != 0)
                {
                    newUnread.AddRange(messages.Where(m => newer.Contains(m.Uid) && !m.Flags.HasFlag(MessageFlags.Seen)).Select(m => (long)m.Uid));
                }
            }

            // 1 = everything older is here; else the lowest fetched (the next round goes on below it)
            oldest = olderAll.Count <= Window ? 1 : older.Min();
        }

        // flags
        var current = _store.Uids(folder.Id);
        if (imap.SupportsCondStore && state.HighestModSeq > 0 && folder.HighestModSeq > 0)
        {
            if (state.HighestModSeq > folder.HighestModSeq && current.Count > 0)
            {
                var changes = await imap.FlagsAsync(folder.Path, current, folder.HighestModSeq, cancellationToken);
                flagChanges += _store.ApplyFlags(folder.Id, changes) ? changes.Count : 0;
            }
        }
        else if (current.Count > 0)
        {
            var newest = current.Skip(Math.Max(0, current.Count - FlagPoll)).ToList();
            var start = _store.FlagWindow(folder.Id);
            if (start >= current.Count)
            {
                start = 0;
            }

            var window = current.Skip(start).Take(FlagPoll).ToList();
            _store.SetFlagWindow(folder.Id, start + FlagPoll >= current.Count ? 0 : start + FlagPoll);
            var poll = newest.Union(window).ToList();
            var states = await imap.FlagsAsync(folder.Path, poll, 0, cancellationToken);
            flagChanges += _store.ApplyFlags(folder.Id, states) ? 1 : 0;
        }

        _store.SetFolderState(folder.Id, state.UidValidity, state.UidNext, state.HighestModSeq, oldest == 0 ? null : oldest);
        if (newUnread.Count > 0 && NewMail is { } handler)
        {
            var ids = _store.List(new MessageQuery([folder.Id], ListFilter.Unread)).Take(newUnread.Count).Select(e => e.Id).ToList();
            handler(this, new NewMailEventArgs(Account, [.. _store.Headers(ids).OrderByDescending(h => h.Date)]));
        }

        return (added, removed, flagChanges);
    }

    // ===== the queue =====

    private async Task<int> ReplayAsync(IImapConnection imap, CancellationToken cancellationToken)
    {
        var replayed = 0;
        foreach (var operation in _store.PendingOperations(Account.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ApplyAsync(imap, operation, cancellationToken);
                _store.CompleteOperation(operation.Id);
                replayed++;
            }
            catch (MailConnectionException exception) when (exception.Kind is MailErrorKind.Protocol or MailErrorKind.Rejected)
            {
                // the server refuses it (the folder is gone, the messages too): tried a few times, then dropped – never blocks the queue
                Log.Warning($"A mail operation ({operation.Kind}) was refused: {exception.Message}");
                if (operation.Attempts >= 2)
                {
                    _store.CompleteOperation(operation.Id);
                }
                else
                {
                    _store.FailOperation(operation.Id, exception.Message);
                }
            }
        }

        return replayed;
    }

    private async Task ApplyAsync(IImapConnection imap, PendingOperation operation, CancellationToken cancellationToken)
    {
        var data = operation.Data;
        var uids = data.Uids.Where(uid => uid > 0).ToList();
        switch (operation.Kind)
        {
            case OperationKind.Flags when uids.Count > 0:
                await imap.SetFlagsAsync(data.Folder, uids, (MessageFlags)data.Add, (MessageFlags)data.Remove, data.AddKeywords, data.RemoveKeywords, cancellationToken);
                break;
            case OperationKind.Move when uids.Count > 0 && data.Target is { } target:
                var moved = await imap.MoveAsync(data.Folder, uids, target, cancellationToken);
                if (moved is not null)
                {
                    var ids = data.MessageIds.Where((_, index) => index < data.Uids.Count && data.Uids[index] > 0).ToList();
                    _store.SetUids(ids, moved);
                }

                break;
            case OperationKind.Expunge when uids.Count > 0:
                await imap.ExpungeAsync(data.Folder, uids, cancellationToken);
                break;
            case OperationKind.Append when data.RawPath is { } path:
                if (_store.ReadFile(path) is { } raw)
                {
                    await imap.AppendAsync(data.Folder, raw, (MessageFlags)data.Add, DateTimeOffset.Now, cancellationToken);
                    _store.DeleteFile(path);
                }

                break;
            case OperationKind.CreateFolder when data.Name is { } name:
                await imap.CreateFolderAsync(data.Folder.Length == 0 ? null : data.Folder, name, cancellationToken);
                break;
            case OperationKind.RenameFolder when data.Name is { } newName:
                await imap.RenameFolderAsync(data.Folder, newName, cancellationToken);
                break;
            case OperationKind.DeleteFolder:
                await imap.DeleteFolderAsync(data.Folder, cancellationToken);
                break;
        }
    }

    // ===== the outbox =====

    private async Task<int> SendOutboxAsync(CancellationToken cancellationToken)
    {
        var waiting = _store.Outbox(Account.Id).Where(item => item.State != OutboxState.Failed).ToList();
        if (waiting.Count == 0)
        {
            return 0;
        }

        var password = _password(Account, Accounts.AccountService.SmtpSecret)
                       ?? throw new MailConnectionException(MailErrorKind.Authentication, "Für den Postausgangsserver ist kein Kennwort gespeichert.");
        var sent = 0;
        await using var smtp = await _transport.ConnectSmtpAsync(Account, password, cancellationToken);
        foreach (var item in waiting)
        {
            var raw = _store.ReadFile(item.RawPath);
            if (raw is null)
            {
                _store.RemoveFromOutbox(item.Id);
                continue;
            }

            var recipients = JsonSerializer.Deserialize(item.Recipients, MailJson.Default.ListMailAddress) ?? [];
            _store.SetOutboxState(item.Id, OutboxState.Sending);
            try
            {
                await smtp.SendAsync(raw, Account.Sender, recipients, cancellationToken);
            }
            catch (MailConnectionException exception) when (exception.Kind is MailErrorKind.Rejected or MailErrorKind.Protocol)
            {
                _store.SetOutboxState(item.Id, OutboxState.Failed, exception.Message, countAttempt: true);
                continue;
            }
            catch (MailConnectionException exception)
            {
                _store.SetOutboxState(item.Id, OutboxState.Waiting, exception.Message, countAttempt: true);
                throw;
            }

            AfterSending(item, raw);
            sent++;
        }

        return sent;
    }

    /// <summary>A message went out: a copy into Gesendete Elemente (unless the server keeps one), the original marked answered/forwarded, the outbox entry goes.</summary>
    private void AfterSending(OutboxItem item, byte[] raw)
    {
        if (!Account.ServerSavesSent && _store.FolderByRole(Account.Id, FolderRole.Sent) is { } sentFolder)
        {
            var path = _store.WriteFile("sent", raw);
            _store.Enqueue(Account.Id, OperationKind.Append, new OperationData { Folder = sentFolder.Path, RawPath = path, Add = (int)MessageFlags.Seen, MessageId = item.MessageId });
            var summary = MailMime.Summarize(0, raw, MessageFlags.Seen, [], 0);
            var id = _store.AddLocalMessage(sentFolder.Id, Account.Id, summary);
            _store.SetBody(id, raw, MailReader.Read(raw).Text);
        }

        if (item.OriginalMessage is { } original && _store.Header(original) is { } header && _store.Folder(header.FolderId) is { } folder)
        {
            var flag = item.OriginKind == (int)DraftOrigin.Forward ? MessageFlags.Forwarded : MessageFlags.Answered;
            _store.SetFlags([original], flag, MessageFlags.None);
            _store.Enqueue(Account.Id, OperationKind.Flags, new OperationData { Folder = folder.Path, Uids = [header.Uid], MessageIds = [original], Add = (int)flag });
        }

        _store.RemoveFromOutbox(item.Id);
    }

    // ===== bodies =====

    /// <summary>The raw message from the server (cached in the store with its text for the search); null when the server no longer has it.</summary>
    public async Task<byte[]?> FetchBodyAsync(long messageId, CancellationToken cancellationToken)
    {
        if (_store.Raw(messageId) is { } cached)
        {
            return cached;
        }

        var header = _store.Header(messageId);
        var uid = _store.RawUid(messageId);
        if (header is null || uid <= 0 || _store.Folder(header.FolderId) is not { } folder)
        {
            return null;
        }

        await _fetchGate.WaitAsync(cancellationToken);
        try
        {
            _fetch ??= await ConnectImapAsync(cancellationToken);
            var raw = await _fetch.GetRawAsync(folder.Path, (uint)uid, cancellationToken);
            if (raw is not null)
            {
                _store.SetBody(messageId, raw, MailReader.Read(raw).Text);
            }

            return raw;
        }
        catch (MailConnectionException)
        {
            await CloseFetchCoreAsync();
            throw;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private async Task CloseFetchAsync()
    {
        await _fetchGate.WaitAsync();
        try
        {
            await CloseFetchCoreAsync();
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private async Task CloseFetchCoreAsync()
    {
        if (_fetch is { } fetch)
        {
            _fetch = null;
            try
            {
                await fetch.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private int PendingCount() => _store.PendingOperations(Account.Id).Count + _store.Outbox(Account.Id).Count;

    private void SetStatus(AccountSyncStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}

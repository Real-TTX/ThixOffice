using System.Net;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Sync;

/// <summary>Where the sync of a PIM app stands (the status bar, Einstellungen › Cloud).</summary>
public enum SyncState
{
    /// <summary>Not signed in: the app works only on this computer.</summary>
    Off,

    /// <summary>In step with the cloud (or waiting for the next change).</summary>
    Idle,

    /// <summary>A sync runs.</summary>
    Syncing,

    /// <summary>The cloud cannot be reached: changes wait on this computer and go up later.</summary>
    Offline,

    /// <summary>The cloud ended the sign-in (401).</summary>
    SignedOut,

    /// <summary>The administration switched the app off (403).</summary>
    Forbidden,
}

/// <summary>The state with its words for people and the number of changes that wait.</summary>
public sealed record SyncStatus(SyncState State, string Message, DateTimeOffset? LastSync, int Pending)
{
    public static SyncStatus Off { get; } = new(SyncState.Off, "Nur auf diesem Computer", null, 0);
}

/// <summary>What one round did.</summary>
public sealed record SyncReport(int Pushed, int Pulled, int Conflicts, int Photos);

/// <summary>
/// The sync client of MatCloud's general sync for a PIM app (<see cref="SyncContract"/>), the same as ThixCalendar's and ThixNote's:
/// 1. push: the changes of this computer (<see cref="PimStore.Dirty"/>) go up in batches; an item the cloud took is clean; one the cloud had a newer
///    change of comes back as <see cref="SyncPushResult.Current"/> and is written here (the last writer wins, by <see cref="SyncItemDto.UpdatedAt"/>).
///    The pictures of contacts go up before them.
/// 2. pull: everything above the cursor, in revision order (tombstones too), while <see cref="SyncChangeSet.HasMore"/>; a change of this computer that is
///    newer stays. Pictures named by pulled contacts are fetched.
/// 3. the loop (<see cref="Start"/>): a round, then the long poll (<see cref="SyncContract.Wait"/>) or a local change (<see cref="Trigger"/>), again; offline
///    it retries after 5 s, doubling up to 5 minutes – the changes wait in the store meanwhile (the offline queue). 401 stops it (signed out), 403 waits.
/// <paramref name="app"/> names another app's sync (a grant: ThixMail in the contacts); null is the device's own.
/// </summary>
public sealed class PimSync(PimStore store, Func<MatCloudClient> client, string? app = null) : IAsyncDisposable
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(5);
    public const int WaitSeconds = 25;

    private readonly SemaphoreSlim _round = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _loop;
    private CancellationTokenSource? _wait;
    private Task? _loopTask;
    private SyncStatus _status = new(SyncState.Idle, "Bereit", store.LastSync, store.DirtyCount);

    /// <summary>Raised when <see cref="Status"/> changes (any thread).</summary>
    public event EventHandler<SyncStatus>? StatusChanged;

    public SyncStatus Status => _status;

    public PimStore Store => store;

    /// <summary>True while the background loop runs.</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>One round: push, then pull. Throws the cloud's failure (the caller or the loop decides what it means).</summary>
    public async Task<SyncReport> SyncOnceAsync(CancellationToken ct = default)
    {
        await _round.WaitAsync(ct);
        try
        {
            SetStatus(new SyncStatus(SyncState.Syncing, "Wird synchronisiert …", store.LastSync, store.DirtyCount));
            using var cloud = client();
            var photos = await UploadPhotosAsync(cloud, ct);
            var (pushed, conflicts) = await PushAsync(cloud, ct);
            var pulled = await PullAsync(cloud, ct);
            photos += await DownloadPhotosAsync(cloud, ct);
            store.LastSync = DateTimeOffset.UtcNow;
            SetStatus(new SyncStatus(SyncState.Idle, "Synchronisiert", store.LastSync, store.DirtyCount));
            return new SyncReport(pushed, pulled, conflicts, photos);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            SetStatus(StatusOf(exception));
            throw;
        }
        finally
        {
            _round.Release();
        }
    }

    /// <summary>Starts the background loop (once).</summary>
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

    /// <summary>A local change, or "Jetzt synchronisieren": the loop runs a round at once (it stops waiting).</summary>
    public void Trigger()
    {
        lock (_gate)
        {
            _wait?.Cancel();
        }
    }

    /// <summary>Stops the loop and waits for it.</summary>
    public async Task StopAsync()
    {
        Task? running;
        lock (_gate)
        {
            _loop?.Cancel();
            _wait?.Cancel();
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
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _round.Dispose();
    }

    // ---- the loop ----

    private async Task LoopAsync(CancellationToken stop)
    {
        var retry = TimeSpan.Zero;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stop);
                retry = TimeSpan.Zero;
                await WaitForChangeAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var status = StatusOf(exception);
                SetStatus(status);
                if (status.State == SyncState.SignedOut)
                {
                    Log.Warning("Sync stopped: the cloud ended the sign-in");
                    return;
                }

                retry = status.State == SyncState.Forbidden ? LongestRetry : retry == TimeSpan.Zero ? FirstRetry : TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
                Log.Info($"Sync failed ({exception.GetType().Name}); next try in {retry.TotalSeconds:0} s");
                await DelayOrTriggerAsync(retry, stop);
            }
        }
    }

    /// <summary>The long poll of the cloud, ended early by a local change (<see cref="Trigger"/>).</summary>
    private async Task WaitForChangeAsync(CancellationToken stop)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_gate)
        {
            _wait = wait;
        }

        try
        {
            if (store.DirtyCount > 0)
            {
                return; // something changed during the round
            }

            using var cloud = client();
            var answer = await cloud.WaitAsync(app, store.Cursor, WaitSeconds, wait.Token);
            CloudService.ReportOnline();
            _ = answer;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            // a local change: sync at once
        }
        finally
        {
            lock (_gate)
            {
                _wait = null;
            }
        }
    }

    private async Task DelayOrTriggerAsync(TimeSpan delay, CancellationToken stop)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_gate)
        {
            _wait = wait;
        }

        try
        {
            await Task.Delay(delay, wait.Token);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _wait = null;
            }
        }
    }

    // ---- the round ----

    private async Task<(int Pushed, int Conflicts)> PushAsync(MatCloudClient cloud, CancellationToken ct)
    {
        var pushed = 0;
        var conflicts = 0;
        while (store.Dirty() is { Count: > 0 } dirty)
        {
            var request = new SyncPushRequest(dirty.Select(item => new SyncItemDto
            {
                Collection = item.Collection,
                Key = item.Key,
                Data = item.IsDeleted ? null : item.Data,
                IsDeleted = item.IsDeleted,
                UpdatedAt = item.UpdatedAt,
            }).ToList());
            var response = await cloud.PushAsync(app, request, ct);
            CloudService.ReportOnline();
            var winners = new List<SyncItemDto>();
            foreach (var (item, result) in dirty.Zip(response.Results))
            {
                if (result.Applied)
                {
                    store.MarkClean(item.Collection, item.Key, item.UpdatedAt, result.Revision);
                    pushed++;
                }
                else if (result.Current is { } current)
                {
                    winners.Add(current);
                    conflicts++;
                }
                else
                {
                    // refused without a winner (a collection only the cloud writes): the cloud's state comes with the next pull
                    store.MarkClean(item.Collection, item.Key, item.UpdatedAt, result.Revision);
                }
            }

            if (winners.Count > 0)
            {
                // the cloud had a newer change: it wins here too (ApplyRemote keeps a local change only when it is newer, which it is not)
                foreach (var winner in winners)
                {
                    var local = store.Get(winner.Collection, winner.Key);
                    if (local is not null)
                    {
                        store.MarkClean(local.Collection, local.Key, local.UpdatedAt, winner.Revision);
                    }
                }

                store.ApplyRemote(winners);
            }

            if (response.Results.Count == 0)
            {
                break;
            }
        }

        return (pushed, conflicts);
    }

    private async Task<int> PullAsync(MatCloudClient cloud, CancellationToken ct)
    {
        var pulled = 0;
        while (true)
        {
            var changes = await cloud.GetChangesAsync(app, store.Cursor, SyncContract.MaxBatchSize, ct);
            CloudService.ReportOnline();
            pulled += store.ApplyRemote(changes.Items);
            store.Cursor = Math.Max(store.Cursor, changes.Latest);
            if (!changes.HasMore || changes.Items.Count == 0)
            {
                return pulled;
            }
        }
    }

    private async Task<int> UploadPhotosAsync(MatCloudClient cloud, CancellationToken ct)
    {
        var count = 0;
        foreach (var id in store.PhotosToUpload())
        {
            if (store.Photo(id) is not { } bytes)
            {
                continue;
            }

            var answer = await cloud.UploadContactPhotoAsync(bytes, Rules.VCard.ImageTypeOf(bytes) ?? "application/octet-stream", ct);
            if (answer.Id == id)
            {
                store.MarkPhotoUploaded(id);
                count++;
            }
        }

        return count;
    }

    private async Task<int> DownloadPhotosAsync(MatCloudClient cloud, CancellationToken ct)
    {
        var count = 0;
        var wanted = store.Items(ContactsContract.Contacts)
            .Select(item => ContactsContract.ReadContact(item.Data)?.PhotoId)
            .Where(id => ContactsContract.IsValidPhotoId(id) && !store.HasPhoto(id!))
            .Distinct()
            .ToList();
        foreach (var id in wanted)
        {
            if (await cloud.DownloadContactPhotoAsync(id!, ct) is { } bytes)
            {
                store.PutPhoto(id!, bytes, isUploaded: true);
                count++;
            }
        }

        return count;
    }

    // ---- status ----

    private SyncStatus StatusOf(Exception exception)
    {
        var pending = store.DirtyCount;
        _ = CloudService.Explain(exception); // the state of the connection (title bar, Einstellungen › Cloud)
        return exception switch
        {
            CloudException { StatusCode: HttpStatusCode.Unauthorized } => new SyncStatus(SyncState.SignedOut, "Abgemeldet – bitte erneut anmelden", store.LastSync, pending),
            CloudException { StatusCode: HttpStatusCode.Forbidden } => new SyncStatus(SyncState.Forbidden, "Von der Verwaltung der ThixCloud abgeschaltet", store.LastSync, pending),
            Core.Models.BusinessException business => new SyncStatus(SyncState.Offline, business.Message, store.LastSync, pending),
            _ => new SyncStatus(SyncState.Offline, pending > 0 ? $"Offline – {pending} Änderungen warten" : "Offline", store.LastSync, pending),
        };
    }

    private void SetStatus(SyncStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}

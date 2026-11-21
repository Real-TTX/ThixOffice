using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using MatCloud.Contract.Drive;

namespace ThixOffice.Core.Platform;

/// <summary>
/// A ThixCloud in memory: the routes of the contract an office app uses (sign-in by password, browser link and launcher hand-over,
/// logout, account, and the Drive: folders, items, search, recent, quota, new folders, rename, trash, download, upload with If-Match,
/// new files with a name conflict). The snapshots draw it (no real account shows), the self test of the published program opens and
/// saves through it, and the tests drive it – also into failures: <see cref="IsOffline"/> (no network), <see cref="FailWith"/> (a status
/// for every Drive call: 401, 403, 413, 507 …) and <see cref="SaveElsewhere"/> (somebody saves a file in between).
/// Plug it in with <c>CloudService.HandlerForTests = () =&gt; cloud</c>.
/// </summary>
internal sealed class SampleCloud : HttpMessageHandler
{
    public const string ServerUrl = "https://cloud.example.com";
    public const string UserName = "anna";
    public const string Password = "geheim";
    public const string DisplayName = "Anna Beispiel";

    private static readonly DateTimeOffset BaseTime = new(2026, 11, 18, 9, 30, 0, TimeSpan.FromHours(1));

    private readonly object _gate = new();
    private readonly Dictionary<long, Entry> _items = [];
    private readonly HashSet<string> _tokens = [];
    private readonly Dictionary<string, string> _pollTokens = []; // poll token → device name
    private readonly List<long> _recent = [];
    private long _nextId = 100;
    private long _nextVersion = 5000;
    private readonly Dictionary<(string App, string Collection, string Key), SyncItemDto> _syncItems = [];
    private readonly Dictionary<string, byte[]> _photos = [];
    private long _revision;

    /// <summary>The app the devices sign in as (the account's answer); ThixContacts sets <c>contacts</c>.</summary>
    public string App { get; set; } = CloudApps.Write;

    /// <summary>Every request of the general sync and of the pictures answers this status (null: normal): 401, 403, 500 …</summary>
    public HttpStatusCode? SyncFailsWith { get; set; }

    /// <summary>
    /// Another device of the account changes an item of the general sync (last writer wins like the real cloud; a tombstone when
    /// <paramref name="data"/> is null). Answers its revision.
    /// </summary>
    public long PushElsewhere(string app, string collection, string key, string? data, DateTimeOffset updatedAt, string source = "LAPTOP-BERT")
    {
        lock (_gate)
        {
            return Apply(app, new SyncItemDto { Collection = collection, Key = key, Data = data, IsDeleted = data is null, UpdatedAt = updatedAt }, source).Revision;
        }
    }

    /// <summary>The live items of an app's collection (tests).</summary>
    public IReadOnlyList<SyncItemDto> SyncItems(string app, string collection)
    {
        lock (_gate)
        {
            return _syncItems.Where(pair => pair.Key.App == app && pair.Key.Collection == collection && !pair.Value.IsDeleted).Select(pair => pair.Value).ToList();
        }
    }

    /// <summary>The item of an app's collection (a tombstone too), or null.</summary>
    public SyncItemDto? SyncItem(string app, string collection, string key)
    {
        lock (_gate)
        {
            return _syncItems.GetValueOrDefault((app, collection, key));
        }
    }

    /// <summary>The bytes of a picture of a contact, or null.</summary>
    public byte[]? Photo(string id)
    {
        lock (_gate)
        {
            return _photos.GetValueOrDefault(id);
        }
    }

    /// <summary>No network: every request fails like a cable that was pulled.</summary>
    public bool IsOffline { get; set; }

    /// <summary>Every Drive request answers this status (null: normal).</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>A started browser sign-in is confirmed at its first poll (false: it stays pending).</summary>
    public bool ApprovesLinks { get; set; } = true;

    /// <summary>The requests as "METHOD /path" (tests).</summary>
    public List<string> Requests { get; } = [];

    /// <summary>The device name the last sign-in used.</summary>
    public string? LastDeviceName { get; private set; }

    /// <summary>A cloud with a few folders and files (the snapshots' Drive).</summary>
    public static SampleCloud WithSampleDrive(byte[]? documentContent = null)
    {
        var cloud = new SampleCloud();
        var content = documentContent ?? [1, 2, 3];
        var projects = cloud.AddFolder(null, "Projekte", BaseTime.AddDays(-1));
        cloud.AddFolder(null, "Rechnungen", BaseTime.AddDays(-6));
        cloud.AddFolder(null, "Privat", BaseTime.AddDays(-20));
        cloud.AddFile(null, "Quartalsbericht.docx", content, BaseTime.AddHours(-2), 48_211);
        cloud.AddFile(null, "Protokoll Teamsitzung.docx", content, BaseTime.AddDays(-2), 22_904);
        cloud.AddFile(null, "Angebot Müller GmbH.docx", content, BaseTime.AddDays(-4), 31_540);
        cloud.AddFile(null, "Notizen.txt", content, BaseTime.AddDays(-9), 1_284);
        cloud.AddFile(null, "Budget 2027.xlsx", content, BaseTime.AddDays(-3), 18_022);
        cloud.AddFile(null, "Kundenpräsentation.pptx", content, BaseTime.AddDays(-12), 2_340_118);
        cloud.AddFile(projects.Id, "Projektplan.docx", content, BaseTime.AddDays(-1), 64_330);
        return cloud;
    }

    /// <summary>A token the cloud accepts (tests that start signed in).</summary>
    public string IssueToken()
    {
        lock (_gate)
        {
            var token = "token-" + Guid.NewGuid().ToString("N");
            _tokens.Add(token);
            return token;
        }
    }

    /// <summary>A hand-over of ThixLauncher: the poll token of a confirmed sign-in.</summary>
    public string IssueHandoff()
    {
        lock (_gate)
        {
            var poll = "handoff-" + Guid.NewGuid().ToString("N");
            _pollTokens[poll] = "ANNA-PC";
            return poll;
        }
    }

    /// <summary>Forgets every device (the person removed them in the browser): the next request answers 401.</summary>
    public void RemoveDevices()
    {
        lock (_gate)
        {
            _tokens.Clear();
        }
    }

    public DriveItem AddFolder(long? parentId, string name, DateTimeOffset? modified = null)
    {
        lock (_gate)
        {
            return Add(parentId, name, DriveItemKind.Folder, [], modified ?? BaseTime, null).Item;
        }
    }

    public DriveItem AddFile(long? parentId, string name, byte[] content, DateTimeOffset? modified = null, long? shownSize = null)
    {
        lock (_gate)
        {
            return Add(parentId, name, DriveItemKind.File, content, modified ?? BaseTime, shownSize).Item;
        }
    }

    /// <summary>Somebody saves the file on another device: a new version, a new ETag.</summary>
    public DriveItem SaveElsewhere(long id, byte[] content)
    {
        lock (_gate)
        {
            var entry = _items[id];
            entry.Content = content;
            entry.Item = entry.Item with { ETag = NextETag(), Size = content.Length, Version = (entry.Item.Version ?? 0) + 1, ModifiedAt = BaseTime.AddHours(1), ModifiedBy = "LAPTOP-BERT" };
            return entry.Item;
        }
    }

    /// <summary>The bytes of the current version of a file.</summary>
    public byte[] ContentOf(long id)
    {
        lock (_gate)
        {
            return _items[id].Content;
        }
    }

    /// <summary>The item, or null.</summary>
    public DriveItem? Find(long id)
    {
        lock (_gate)
        {
            return _items.TryGetValue(id, out var entry) ? entry.Item : null;
        }
    }

    /// <summary>The item of that name in a folder, or null.</summary>
    public DriveItem? FindByName(long? parentId, string name)
    {
        lock (_gate)
        {
            return _items.Values.Select(entry => entry.Item).FirstOrDefault(item => item.ParentId == parentId && !item.IsTrashed && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        lock (_gate)
        {
            Requests.Add($"{request.Method} {path}");
        }

        if (IsOffline)
        {
            throw new HttpRequestException("No connection (sample cloud offline).");
        }

        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        lock (_gate)
        {
            return Route(request, path, query, body);
        }
    }

    private HttpResponseMessage Route(HttpRequestMessage request, string path, System.Collections.Specialized.NameValueCollection query, byte[] body)
    {
        var method = request.Method.Method;
        switch (method, path)
        {
            case ("POST", CloudContract.DeviceLogin):
                return Login(Read<DeviceLoginRequest>(body));
            case ("POST", CloudContract.DeviceLink):
                return StartLink(Read<DeviceLinkRequest>(body));
            case ("POST", CloudContract.DeviceLinkPoll):
                return Poll(Read<DeviceLinkPollRequest>(body).PollToken);
        }

        if (!IsAuthorized(request))
        {
            return Error(HttpStatusCode.Unauthorized, "Dieses Gerät ist nicht angemeldet.");
        }

        switch (method, path)
        {
            case ("POST", CloudContract.DeviceLogout):
                _tokens.Remove(request.Headers.Authorization!.Parameter!);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            case ("GET", CloudContract.Account):
                return Json(new AccountInfo(UserName, DisplayName, LastDeviceName ?? "ANNA-PC", App, "1.5.0"));
        }

        if (path.StartsWith("/api/v1/sync/", StringComparison.Ordinal) || path.StartsWith(ContactsContract.Root, StringComparison.Ordinal))
        {
            return SyncFailsWith is { } failure ? Error(failure, failure == HttpStatusCode.Forbidden ? "ThixContacts ist abgeschaltet." : "Fehler.") : Sync(method, path, query, body);
        }

        if (!path.StartsWith(DriveContract.Root, StringComparison.Ordinal))
        {
            return Error(HttpStatusCode.NotFound, "Nicht gefunden.");
        }

        if (FailWith is { } status)
        {
            return Error(status, status switch
            {
                HttpStatusCode.Forbidden => "ThixDrive ist abgeschaltet.",
                HttpStatusCode.RequestEntityTooLarge => "Die Datei ist zu groß.",
                HttpStatusCode.InsufficientStorage => "Der Speicherplatz ist voll.",
                _ => "Fehler.",
            });
        }

        return Drive(method, path, query, body, request);
    }

    private HttpResponseMessage Drive(string method, string path, System.Collections.Specialized.NameValueCollection query, byte[] body, HttpRequestMessage request)
    {
        switch (method, path)
        {
            case ("GET", DriveContract.Items):
                return List(ParseId(query["parentId"]), query["sort"], query["descending"] == "true", Number(query["offset"], 0), Number(query["limit"], 200));
            case ("GET", DriveContract.Search):
                return Page(Live().Where(item => item.Name.Contains(query["q"] ?? "", StringComparison.CurrentCultureIgnoreCase)).OrderBy(item => item.Kind).ThenBy(item => item.Name), query);
            case ("GET", DriveContract.Recent):
                return Page(_recent.AsEnumerable().Reverse().Select(id => _items[id].Item).Where(item => !item.IsTrashed), query);
            case ("GET", DriveContract.Quota):
                return Json(new DriveQuota(_items.Values.Sum(entry => entry.Item.Size), 5L * 1024 * 1024 * 1024, 0, 0, 2L * 1024 * 1024 * 1024, DriveContract.MaxSimpleUploadBytes, DriveContract.ChunkBytes));
            case ("POST", DriveContract.Folders):
                var folder = Read<DriveFolderRequest>(body);
                return Taken(folder.ParentId, folder.Name) is { } taken
                    ? Conflict(HttpStatusCode.Conflict, "Diesen Namen gibt es in diesem Ordner schon.", taken)
                    : Json(Add(folder.ParentId, folder.Name, DriveItemKind.Folder, [], BaseTime, null).Item, HttpStatusCode.Created);
            case ("POST", DriveContract.Files):
                return NewFile(ParseId(query["parentId"]), query["name"] ?? "", query["onConflict"], body);
        }

        var parts = path[(DriveContract.Items.Length + 1)..].Split('/');
        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out var id) || !_items.TryGetValue(id, out var entry) || entry.Item.IsTrashed && parts.Length == 1)
        {
            return Error(HttpStatusCode.NotFound, "Nicht gefunden.");
        }

        var action = parts.Length > 1 ? parts[1] : "";
        switch (method, action)
        {
            case ("GET", ""):
                return Json(entry.Item with { Ancestors = Ancestors(entry.Item.ParentId) });
            case ("GET", "content"):
                _recent.Remove(id);
                _recent.Add(id);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(entry.Content) };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(entry.Item.ETag!);
                return response;
            case ("PUT", "content"):
                var ifMatch = request.Headers.TryGetValues("If-Match", out var values) ? values.FirstOrDefault() : null;
                if (ifMatch is null)
                {
                    return Error((HttpStatusCode)428, "If-Match fehlt.");
                }

                if (ifMatch != DriveContract.AnyVersion && ifMatch != entry.Item.ETag)
                {
                    return Conflict(HttpStatusCode.PreconditionFailed, "Die Datei wurde inzwischen geändert.", entry.Item);
                }

                entry.Content = body;
                entry.Item = entry.Item with { ETag = NextETag(), Size = body.Length, Version = (entry.Item.Version ?? 0) + 1, ModifiedAt = BaseTime.AddHours(2), ModifiedBy = LastDeviceName };
                _recent.Remove(id);
                _recent.Add(id);
                return Json(entry.Item);
            case ("POST", "rename"):
                var name = Read<DriveRenameRequest>(body).Name;
                if (Taken(entry.Item.ParentId, name) is { } other && other.Id != id)
                {
                    return Conflict(HttpStatusCode.Conflict, "Diesen Namen gibt es in diesem Ordner schon.", other);
                }

                entry.Item = entry.Item with { Name = name };
                return Json(entry.Item);
            case ("POST", "trash"):
                entry.Item = entry.Item with { IsTrashed = true, TrashedAt = BaseTime };
                return Json(entry.Item);
        }

        return Error(HttpStatusCode.NotFound, "Nicht gefunden.");
    }

    // ---- the general sync and the pictures of contacts ----

    private HttpResponseMessage Sync(string method, string path, System.Collections.Specialized.NameValueCollection query, byte[] body)
    {
        var app = query[SyncContract.AppParameter] ?? App;
        switch (method, path)
        {
            case ("GET", SyncContract.Changes):
                var since = long.TryParse(query["since"], CultureInfo.InvariantCulture, out var s) ? s : 0;
                var limit = Math.Clamp(Number(query["limit"], 100), 1, SyncContract.MaxBatchSize);
                var changes = _syncItems.Where(pair => pair.Key.App == app && pair.Value.Revision > since).Select(pair => pair.Value).OrderBy(item => item.Revision).ToList();
                var page = changes.Take(limit).ToList();
                return Json(new SyncChangeSet(page, page.Count > 0 ? page[^1].Revision : since, changes.Count > limit));
            case ("GET", SyncContract.Wait):
                return Json(new WaitResult(LatestOf(app)));
            case ("POST", SyncContract.Push):
                var push = Read<SyncPushRequest>(body);
                var results = push.Items.Select(item => Apply(app, item, LastDeviceName ?? "ANNA-PC")).ToList();
                return Json(new SyncPushResponse(results, LatestOf(app)));
            case ("POST", ContactsContract.Photos):
                if (body.Length > ContactsContract.MaxPhotoBytes)
                {
                    return Error(HttpStatusCode.RequestEntityTooLarge, "Das Bild ist zu groß.", "photo");
                }

                var id = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body));
                _photos[id] = body;
                return Json(new ContactPhotoInfo(id, "image/png", body.Length));
        }

        if (method == "GET" && path.StartsWith(ContactsContract.Photos + "/", StringComparison.Ordinal))
        {
            return _photos.TryGetValue(path[(ContactsContract.Photos.Length + 1)..], out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : Error(HttpStatusCode.NotFound, "Nicht gefunden.");
        }

        return Error(HttpStatusCode.NotFound, "Nicht gefunden.");
    }

    private long LatestOf(string app) => _syncItems.Where(pair => pair.Key.App == app).Select(pair => pair.Value.Revision).DefaultIfEmpty(0).Max();

    /// <summary>The rule of the real cloud: the last writer wins by UpdatedAt; a push that loses gets the item that won.</summary>
    private SyncPushResult Apply(string app, SyncItemDto item, string source)
    {
        var key = (app, item.Collection, item.Key);
        if (_syncItems.TryGetValue(key, out var existing) && existing.UpdatedAt > item.UpdatedAt)
        {
            return new SyncPushResult(item.Collection, item.Key, existing.Revision, false, existing);
        }

        var stored = item with { Data = item.IsDeleted ? null : item.Data, Source = source, Revision = ++_revision };
        _syncItems[key] = stored;
        return new SyncPushResult(item.Collection, item.Key, stored.Revision, true);
    }

    private HttpResponseMessage Login(DeviceLoginRequest login)
    {
        if (login.UserName != UserName || login.Password != Password)
        {
            return Error(HttpStatusCode.Unauthorized, "Benutzername oder Passwort stimmen nicht.");
        }

        if (!CloudApps.IsKnown(login.App))
        {
            return Error(HttpStatusCode.BadRequest, "Unbekannte App.", "app");
        }

        LastDeviceName = login.DeviceName;
        return Json(SignIn(login.DeviceName));
    }

    private HttpResponseMessage StartLink(DeviceLinkRequest link)
    {
        var poll = "poll-" + Guid.NewGuid().ToString("N");
        _pollTokens[poll] = link.DeviceName;
        return Json(new DeviceLinkStart("ABCD-EFGH", $"{ServerUrl}{CloudContract.ConnectPage}?code=ABCD-EFGH", poll, 600, 1));
    }

    private HttpResponseMessage Poll(string pollToken)
    {
        if (!_pollTokens.TryGetValue(pollToken, out var device))
        {
            return Json(new DeviceLinkPollResponse(DeviceLinkState.Expired));
        }

        if (!ApprovesLinks && !pollToken.StartsWith("handoff-", StringComparison.Ordinal))
        {
            return Json(new DeviceLinkPollResponse(DeviceLinkState.Pending));
        }

        _pollTokens.Remove(pollToken);
        LastDeviceName = device;
        return Json(new DeviceLinkPollResponse(DeviceLinkState.Approved, SignIn(device)));
    }

    private DeviceLoginResponse SignIn(string deviceName)
    {
        var token = "token-" + Guid.NewGuid().ToString("N");
        _tokens.Add(token);
        return new DeviceLoginResponse(token, 7, new AccountInfo(UserName, DisplayName, deviceName, App, "1.5.0"));
    }

    private bool IsAuthorized(HttpRequestMessage request) =>
        request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } token } && _tokens.Contains(token);

    private HttpResponseMessage List(long? parentId, string? sort, bool descending, int offset, int limit)
    {
        if (parentId is { } folderId && (!_items.TryGetValue(folderId, out var folder) || folder.Item.Kind != DriveItemKind.Folder))
        {
            return Error(HttpStatusCode.NotFound, "Nicht gefunden.");
        }

        var children = Live().Where(item => item.ParentId == parentId);
        IOrderedEnumerable<DriveItem> folderFirst = children.OrderBy(item => item.Kind);
        folderFirst = sort switch
        {
            "Modified" => descending ? folderFirst.ThenByDescending(item => item.ModifiedAt) : folderFirst.ThenBy(item => item.ModifiedAt),
            "Size" => descending ? folderFirst.ThenByDescending(item => item.Size) : folderFirst.ThenBy(item => item.Size),
            "Type" => descending ? folderFirst.ThenByDescending(item => Path.GetExtension(item.Name)) : folderFirst.ThenBy(item => Path.GetExtension(item.Name)),
            _ => descending ? folderFirst.ThenByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase) : folderFirst.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var all = folderFirst.ToList();
        var current = parentId is { } id ? _items[id].Item : null;
        return Json(new DriveListing(current, Ancestors(parentId) ?? [], all.Skip(offset).Take(limit).ToList(), all.Count, offset, limit));
    }

    private HttpResponseMessage Page(IEnumerable<DriveItem> items, System.Collections.Specialized.NameValueCollection query)
    {
        var all = items.ToList();
        var offset = Number(query["offset"], 0);
        var limit = Number(query["limit"], 100);
        return Json(new DriveItemPage(all.Skip(offset).Take(limit).ToList(), all.Count, offset, limit));
    }

    private HttpResponseMessage NewFile(long? parentId, string name, string? onConflict, byte[] body)
    {
        if (Taken(parentId, name) is { } taken)
        {
            if (onConflict != nameof(DriveNameConflict.Rename))
            {
                return Conflict(HttpStatusCode.Conflict, $"„{name}“ gibt es in diesem Ordner schon.", taken);
            }

            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            var number = 2;
            while (Taken(parentId, $"{stem} ({number}){extension}") is not null)
            {
                number++;
            }

            name = $"{stem} ({number}){extension}";
        }

        var entry = Add(parentId, name, DriveItemKind.File, body, BaseTime.AddHours(3), null);
        entry.Item = entry.Item with { ModifiedBy = LastDeviceName };
        _recent.Add(entry.Item.Id);
        return Json(entry.Item, HttpStatusCode.Created);
    }

    private Entry Add(long? parentId, string name, DriveItemKind kind, byte[] content, DateTimeOffset modified, long? shownSize)
    {
        var id = _nextId++;
        var item = new DriveItem
        {
            Id = id,
            ParentId = parentId,
            Kind = kind,
            Name = name,
            Size = kind == DriveItemKind.File ? shownSize ?? content.Length : 0,
            ETag = kind == DriveItemKind.File ? NextETag() : null,
            Version = kind == DriveItemKind.File ? 1 : null,
            CreatedAt = modified,
            ModifiedAt = modified,
            ModifiedBy = "ANNA-PC",
            Location = LocationOf(parentId),
        };
        var entry = new Entry(item, content);
        _items[id] = entry;
        return entry;
    }

    private string NextETag() => $"\"{_nextVersion++}\"";

    private IEnumerable<DriveItem> Live() => _items.Values.Select(entry => entry.Item).Where(item => !item.IsTrashed);

    private DriveItem? Taken(long? parentId, string name) =>
        Live().FirstOrDefault(item => item.ParentId == parentId && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private List<DriveCrumb>? Ancestors(long? folderId)
    {
        var crumbs = new List<DriveCrumb>();
        var current = folderId;
        while (current is { } id && _items.TryGetValue(id, out var entry))
        {
            crumbs.Insert(0, new DriveCrumb(id, entry.Item.Name));
            current = entry.Item.ParentId;
        }

        return crumbs;
    }

    private string LocationOf(long? parentId) => "/" + string.Join("/", (Ancestors(parentId) ?? []).Select(crumb => crumb.Name));

    private static long? ParseId(string? text) => long.TryParse(text, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static int Number(string? text, int fallback) => int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static T Read<T>(byte[] body) => JsonSerializer.Deserialize<T>(body, CloudContract.Json)!;

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value, options: CloudContract.Json) };

    private static HttpResponseMessage Error(HttpStatusCode status, string message, string? field = null) => Json(new ApiError(message, field), status);

    private static HttpResponseMessage Conflict(HttpStatusCode status, string message, DriveItem current) => Json(new DriveConflict(message, "name", current), status);

    private sealed class Entry(DriveItem item, byte[] content)
    {
        public DriveItem Item { get; set; } = item;

        public byte[] Content { get; set; } = content;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MatCloud.Contract;
using MatCloud.Contract.Drive;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>How a save into ThixDrive ended.</summary>
public enum DriveSaveOutcome
{
    /// <summary>The Drive has the new version.</summary>
    Uploaded,

    /// <summary>Saved in the local working copy, not in the Drive yet (offline, signed out, switched off): it is tried again.</summary>
    NotUploaded,

    /// <summary>Somebody saved the file in between (412): Überschreiben, Als Kopie speichern or later.</summary>
    ChangedMeanwhile,

    /// <summary>"Speichern unter": the folder has a file of that name already (409): Ersetzen or another name.</summary>
    NameTaken,
}

/// <summary>The end of a save into ThixDrive: the outcome, its words for people and – after a conflict – the item as the Drive has it now.</summary>
public sealed record DriveSaveResult(DriveSaveOutcome Outcome, string Message, DriveItem? Current = null)
{
    public bool IsUploaded => Outcome == DriveSaveOutcome.Uploaded;
}

/// <summary>
/// The documents of an office app in ThixDrive (the Drive of the account in ThixCloud, <see cref="DriveContract"/>), the same for
/// every app of the suite:
///  - browsing: folders with paging and sort, search, recent, new folder, rename, into the trash, the quota;
///  - open: the file is downloaded into a LOCAL WORKING COPY below the app's data folder (<see cref="AppPaths.DriveDirectory"/>), next to
///    it a sidecar (drive.json: server, account, item id, ETag) – the document's <see cref="OfficeDocument.FilePath"/> is the working
///    copy, its <see cref="OfficeDocument.Drive"/> the item;
///  - save: the working copy is written atomically first (nothing is lost whatever the network does), then uploaded as a new version
///    with If-Match = the ETag it was based on. Somebody saved in between → <see cref="DriveSaveOutcome.ChangedMeanwhile"/> (the UI
///    offers Überschreiben / Als Kopie speichern); no network, a server error, signed out or switched off →
///    <see cref="DriveSaveOutcome.NotUploaded"/> (the sidecar says "pending", a download never replaces such a working copy, the
///    window tries again); too large, quota full → a <see cref="BusinessException"/> with the reason;
///  - save as: a new file in a folder, a taken name → <see cref="DriveSaveOutcome.NameTaken"/> (Ersetzen or another name).
/// Every call checks the policy (DisableCloud) and the sign-in through <see cref="CloudService"/>.
/// </summary>
public static class DriveService
{
    /// <summary>How many items a page of a folder or a search brings.</summary>
    public const int PageSize = 100;

    private const string SidecarName = "drive.json";

    private static readonly JsonSerializerOptions SidecarJson = new() { WriteIndented = true };

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    // ===== Browsing =====

    /// <summary>One page of a folder (null: the top of the Drive), folders first.</summary>
    public static Task<DriveListing> ListAsync(long? folderId, DriveSort sort = DriveSort.Name, bool descending = false, int offset = 0, CancellationToken cancellationToken = default) =>
        RunAsync(client => client.ListDriveFolderAsync(folderId, cancellationToken, sort, descending, offset, PageSize), cancellationToken);

    /// <summary>Items whose name contains <paramref name="text"/>, anywhere in the Drive.</summary>
    public static Task<DriveItemPage> SearchAsync(string text, int offset = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessException("Bitte geben Sie ein, wonach Sie suchen.", "Search");
        }

        return RunAsync(client => client.SearchDriveAsync(text.Trim(), cancellationToken, offset, PageSize), cancellationToken);
    }

    /// <summary>The files the account opened or saved last, on any device and in any app.</summary>
    public static Task<DriveItemPage> RecentAsync(int limit = 30, CancellationToken cancellationToken = default) =>
        RunAsync(client => client.GetDriveRecentAsync(cancellationToken, limit), cancellationToken);

    /// <summary>One item with the folders above it.</summary>
    public static Task<DriveItem> GetItemAsync(long id, CancellationToken cancellationToken = default) =>
        RunAsync(client => client.GetDriveItemAsync(id, cancellationToken), cancellationToken);

    /// <summary>The space of the account and the limits of the cloud.</summary>
    public static Task<DriveQuota> QuotaAsync(CancellationToken cancellationToken = default) =>
        RunAsync(client => client.GetDriveQuotaAsync(cancellationToken), cancellationToken);

    /// <summary>"Neuer Ordner" in <paramref name="parentId"/> (null: the top).</summary>
    public static Task<DriveItem> CreateFolderAsync(long? parentId, string name, CancellationToken cancellationToken = default)
    {
        var checkedName = CheckName(name, "Name");
        return RunAsync(client => client.CreateDriveFolderAsync(parentId, checkedName, cancellationToken), cancellationToken);
    }

    /// <summary>"Umbenennen" of a file or folder.</summary>
    public static Task<DriveItem> RenameAsync(long id, string name, CancellationToken cancellationToken = default)
    {
        var checkedName = CheckName(name, "Name");
        return RunAsync(client => client.RenameDriveItemAsync(id, checkedName, cancellationToken), cancellationToken);
    }

    /// <summary>"In den Papierkorb" (30 days there, the browser of ThixDrive restores it).</summary>
    public static Task<DriveItem> TrashAsync(long id, CancellationToken cancellationToken = default) =>
        RunAsync(client => client.TrashDriveItemAsync(id, cancellationToken), cancellationToken);

    /// <summary>A file this app opens (a supported type).</summary>
    public static bool CanOpen(DriveItem item) => item.Kind == DriveItemKind.File && AppInfo.Definition.SupportedTypeOf(item.Name) is not null;

    /// <summary>
    /// The type filter of the browser: a folder always; a file when its extension is one of <paramref name="types"/> (null: every file).
    /// </summary>
    public static bool Matches(DriveItem item, IReadOnlyList<DocumentType>? types) =>
        item.Kind == DriveItemKind.Folder || types is null ||
        types.Any(type => string.Equals(Path.GetExtension(item.Name), type.Extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A name as the Drive takes it (checked here before asking, with the rules of the cloud): no <c>/ \ : * ? " &lt; &gt; |</c>, no control
    /// characters, not "." or "..", no space or dot at the end, none of Windows' reserved names, at most 255 characters.
    /// </summary>
    public static string CheckName(string? name, string field = "FileName")
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie einen Namen an.", field);
        }

        if (text.Length > DriveContract.MaxNameLength)
        {
            throw new BusinessException($"Der Name ist zu lang (höchstens {DriveContract.MaxNameLength} Zeichen).", field);
        }

        if (text.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || text.Any(char.IsControl))
        {
            throw new BusinessException("Ein Name darf diese Zeichen nicht enthalten: / \\ : * ? \" < > |", field);
        }

        if (text is "." or ".." || text.EndsWith('.'))
        {
            throw new BusinessException("Ein Name darf nicht mit einem Punkt enden.", field);
        }

        var stem = text.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException($"„{stem}“ ist als Name nicht erlaubt.", field);
        }

        return text;
    }

    /// <summary>The name a "Speichern unter" into the Drive writes: with the save type's extension when it has none; a type the app cannot write is refused.</summary>
    public static string TargetName(string? fileName)
    {
        var name = CheckName(fileName);
        if (Path.GetExtension(name).Length == 0)
        {
            name += AppInfo.Definition.SaveType.Extension;
        }

        if (AppInfo.Definition.SupportedTypeOf(name) is null && AppInfo.Definition.ExportTypeOf(name) is null)
        {
            throw new BusinessException($"Als „{Path.GetExtension(name)}“ kann {AppInfo.ProductName} (noch) nicht speichern. Möglich: {DocumentService.SaveAsList()}.", "FileName");
        }

        return name;
    }

    /// <summary>
    /// "Speichern unter" › ThixDrive as PDF: <paramref name="write"/> fills a new file <paramref name="fileName"/> in a folder of the Drive
    /// (null: the top) – a copy; the document keeps its own file, type and unsaved state. <paramref name="replacing"/>: the file of that name
    /// the person chose to replace after <see cref="DriveSaveOutcome.NameTaken"/>.
    /// </summary>
    public static async Task<DriveSaveResult> ExportAsync(long? folderId, string fileName, Action<Stream> write, DriveItem? replacing = null,
        CancellationToken cancellationToken = default)
    {
        var name = TargetName(fileName);
        var type = AppInfo.Definition.ExportTypeOf(name) ?? throw new BusinessException($"„{name}“ ist kein Exportformat.", "FileName");
        Directory.CreateDirectory(AppPaths.DriveDirectory);
        var temporary = Path.Combine(AppPaths.DriveDirectory, $".export-{Guid.NewGuid():N}{type.Extension}");
        try
        {
            DocumentFiles.SaveAtomic(temporary, write);
            DriveItem item;
            try
            {
                await using var stream = File.OpenRead(temporary);
                item = replacing is not null
                    ? await RunAsync(client => client.SaveDriveFileAsync(replacing.Id, stream, replacing.ETag ?? DriveContract.AnyVersion, cancellationToken), cancellationToken, passConflicts: true)
                    : await RunAsync(client => client.UploadDriveFileAsync(folderId, name, stream, DriveNameConflict.Fail, cancellationToken), cancellationToken, passConflicts: true);
            }
            catch (DriveConflictException conflict) when (replacing is null && !conflict.IsChangedMeanwhile)
            {
                return new DriveSaveResult(DriveSaveOutcome.NameTaken, $"In diesem Ordner gibt es schon „{name}“.", conflict.Current);
            }
            catch (DriveConflictException conflict)
            {
                return new DriveSaveResult(DriveSaveOutcome.ChangedMeanwhile, $"„{name}“ wurde inzwischen geändert.", conflict.Current);
            }

            Log.Info($"Document exported into ThixDrive ({type.Name})");
            return new DriveSaveResult(DriveSaveOutcome.Uploaded, $"„{item.Name}“ ist in ThixDrive gespeichert.");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // ===== Opening =====

    /// <summary>
    /// Opens a file of the Drive: downloads it into its working copy and reads it into a document made by <paramref name="create"/>.
    /// A working copy with changes that were never uploaded is opened as it is (never replaced by the download): the document shows
    /// "Nicht hochgeladen" and is uploaded with its next save.
    /// </summary>
    public static async Task<T> OpenAsync<T>(long itemId, Func<string, T> create, IProgress<long>? progress = null, CancellationToken cancellationToken = default, string? password = null)
        where T : OfficeDocument
    {
        var item = await GetItemAsync(itemId, cancellationToken);
        var type = AppInfo.Definition.SupportedTypeOf(item.Name)
                   ?? throw new BusinessException($"{AppInfo.ProductName} kann „{item.Name}“ (noch) nicht öffnen.");
        var cloud = CloudService.Settings;
        var folder = WorkingFolder(cloud.ServerUrl, cloud.UserName, item.Id);
        if (ReadSidecar(folder) is { IsUploadPending: true } pending && File.Exists(Path.Combine(folder, pending.Name)))
        {
            Log.Info("A ThixDrive document with changes not uploaded was opened from its working copy");
            return OpenFrom(Path.Combine(folder, pending.Name), pending, DriveSyncState.NotUploaded, create, password);
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, item.Name);
        var download = Path.Combine(folder, ".download" + type.Extension);
        string eTag;
        try
        {
            await using (var stream = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                eTag = await RunAsync(client => client.DownloadDriveFileAsync(item.Id, new ProgressStream(stream, progress), cancellationToken), cancellationToken);
            }

            RemoveOtherFiles(folder, keep: item.Name);
            File.Move(download, target, overwrite: true);
        }
        finally
        {
            File.Delete(download);
        }

        var origin = new DriveOrigin
        {
            ServerUrl = cloud.ServerUrl,
            UserName = cloud.UserName,
            ItemId = item.Id,
            ParentId = item.ParentId,
            Name = item.Name,
            Location = item.Location,
            ETag = eTag.Length > 0 ? eTag : item.ETag ?? "",
        };
        WriteSidecar(folder, origin);
        return OpenFrom(target, origin, DriveSyncState.Uploaded, create, password);
    }

    /// <summary>
    /// "Zuletzt verwendet" of a ThixDrive document: from the Drive when it can be reached, otherwise its working copy (the document
    /// is then marked "Nicht hochgeladen" when it holds changes, else it opens as it was last seen; saving uploads when the cloud is back).
    /// </summary>
    public static async Task<T> OpenRecentAsync<T>(RecentDocument entry, Func<string, T> create, CancellationToken cancellationToken = default, string? password = null)
        where T : OfficeDocument
    {
        var drive = entry.Drive ?? throw new BusinessException("Dieses Dokument liegt nicht in ThixDrive.");
        if (CloudService.IsSignedInTo(drive.ServerUrl, drive.UserName))
        {
            try
            {
                return await OpenAsync(drive.ItemId, create, null, cancellationToken, password);
            }
            catch (BusinessException) when (CloudService.Status.State == CloudState.Offline && File.Exists(entry.Path))
            {
                // offline: the working copy below
            }
        }

        if (!File.Exists(entry.Path) || ReadSidecar(Path.GetDirectoryName(entry.Path)!) is not { } origin)
        {
            throw new BusinessException(CloudService.IsConnected
                ? $"„{entry.FileName}“ kann gerade nicht aus ThixDrive geladen werden, und es gibt keine Kopie auf diesem Computer."
                : $"„{entry.FileName}“ liegt in ThixDrive. Bitte melden Sie sich bei ThixCloud an, um es zu öffnen.");
        }

        Log.Info("A ThixDrive document was opened from its working copy (the cloud could not be reached)");
        return OpenFrom(entry.Path, origin, origin.IsUploadPending ? DriveSyncState.NotUploaded : DriveSyncState.Uploaded, create, password);
    }

    // ===== Saving =====

    /// <summary>
    /// "Speichern" of a ThixDrive document: the working copy is written atomically (from here on nothing is lost), then the new version
    /// is uploaded with If-Match. See <see cref="DriveSaveOutcome"/>; too large or quota full throw (the working copy is kept, pending).
    /// </summary>
    public static async Task<DriveSaveResult> SaveAsync(OfficeDocument document, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var origin = document.Drive ?? throw new BusinessException("Dieses Dokument liegt nicht in ThixDrive.");
        var path = document.FilePath ?? throw new BusinessException("Dieses Dokument hat keine Arbeitskopie.");
        var type = document.FileType ?? AppInfo.Definition.SupportedTypeOf(path) ?? AppInfo.Definition.SaveType;
        DocumentFiles.SaveAtomic(path, stream => document.WriteContentAs(stream, type));
        var pending = origin with { IsUploadPending = true };
        WriteSidecar(Path.GetDirectoryName(path)!, pending);
        document.MarkSaved(path, type);
        DocumentFiles.DeleteRecovery(document.RecoveryId);
        document.SetDrive(pending, DriveSyncState.Uploading);
        return await UploadAsync(document, progress, cancellationToken);
    }

    /// <summary>Uploads the working copy of a document that was saved but not uploaded ("Erneut hochladen", the window's retry).</summary>
    public static Task<DriveSaveResult> UploadAsync(OfficeDocument document, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var origin = document.Drive ?? throw new BusinessException("Dieses Dokument liegt nicht in ThixDrive.");
        return UploadVersionAsync(document, origin.ETag.Length > 0 ? origin.ETag : DriveContract.AnyVersion, progress, cancellationToken);
    }

    /// <summary>"Überschreiben" after a conflict: the working copy becomes the current version, over the one saved in between (it stays a version of the file).</summary>
    public static Task<DriveSaveResult> OverwriteAsync(OfficeDocument document, DriveItem? current, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        UploadVersionAsync(document, current?.ETag ?? DriveContract.AnyVersion, progress, cancellationToken);

    /// <summary>
    /// "Als Kopie speichern" after a conflict: the working copy becomes a NEW file next to the original ("Bericht (2).docx"); the document
    /// belongs to the copy from now on, the original stays as the other person saved it.
    /// </summary>
    public static async Task<DriveSaveResult> SaveCopyAsync(OfficeDocument document, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var origin = document.Drive ?? throw new BusinessException("Dieses Dokument liegt nicht in ThixDrive.");
        var path = document.FilePath ?? throw new BusinessException("Dieses Dokument hat keine Arbeitskopie.");
        document.SetDrive(origin, DriveSyncState.Uploading);
        DriveItem item;
        try
        {
            await using var stream = File.OpenRead(path);
            item = await RunAsync(client => client.UploadDriveFileAsync(origin.ParentId, origin.Name, stream, DriveNameConflict.Rename, cancellationToken, progress), cancellationToken);
        }
        catch (BusinessException exception)
        {
            document.SetDrive(origin, DriveSyncState.Conflict);
            throw new BusinessException($"{exception.Message} Ihr Dokument ist auf diesem Computer gespeichert.", exception.Field);
        }

        var oldFolder = Path.GetDirectoryName(path)!;
        Adopt(document, path, item, deleteSource: false);
        TryDeleteFolder(oldFolder); // its content is the copy now; the original is downloaded anew when it is opened
        return new DriveSaveResult(DriveSaveOutcome.Uploaded, $"Als „{item.Name}“ in ThixDrive gespeichert.");
    }

    /// <summary>
    /// "Speichern unter" into a folder of the Drive (null: the top): a new file <paramref name="fileName"/> (the save type's extension is
    /// added when it has none). <paramref name="replacing"/>: the file of that name the person chose to replace (after
    /// <see cref="DriveSaveOutcome.NameTaken"/>) – it gets a new version. Nothing is saved when the cloud cannot be reached (the document
    /// stays as it is, with its recovery file); the document belongs to the new item afterwards.
    /// </summary>
    public static async Task<DriveSaveResult> SaveAsAsync(OfficeDocument document, long? folderId, string fileName, DriveItem? replacing = null,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var name = TargetName(fileName);
        var type = AppInfo.Definition.SupportedTypeOf(name)
                   ?? throw new BusinessException($"„{name}“ wird als Kopie gespeichert, nicht als das Dokument selbst.", "FileName");
        Directory.CreateDirectory(AppPaths.DriveDirectory);
        var temporary = Path.Combine(AppPaths.DriveDirectory, $".upload-{Guid.NewGuid():N}{type.Extension}");
        try
        {
            DocumentFiles.SaveAtomic(temporary, stream => document.WriteContentAs(stream, type));
            DriveItem item;
            try
            {
                await using var stream = File.OpenRead(temporary);
                item = replacing is not null
                    ? await RunAsync(client => client.SaveDriveFileAsync(replacing.Id, stream, replacing.ETag ?? DriveContract.AnyVersion, cancellationToken, progress), cancellationToken, passConflicts: true)
                    : await RunAsync(client => client.UploadDriveFileAsync(folderId, name, stream, DriveNameConflict.Fail, cancellationToken, progress), cancellationToken, passConflicts: true);
            }
            catch (DriveConflictException conflict) when (replacing is null && !conflict.IsChangedMeanwhile)
            {
                return new DriveSaveResult(DriveSaveOutcome.NameTaken, $"In diesem Ordner gibt es schon „{name}“.", conflict.Current);
            }
            catch (DriveConflictException conflict)
            {
                return new DriveSaveResult(DriveSaveOutcome.ChangedMeanwhile, $"„{name}“ wurde inzwischen geändert.", conflict.Current);
            }

            Adopt(document, temporary, item, deleteSource: true);
            return new DriveSaveResult(DriveSaveOutcome.Uploaded, $"„{item.Name}“ ist in ThixDrive gespeichert.");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>The working copies whose changes were never uploaded (the app offers them; tests).</summary>
    public static IReadOnlyList<(string WorkingCopy, DriveOrigin Origin)> PendingUploads()
    {
        if (!Directory.Exists(AppPaths.DriveDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(AppPaths.DriveDirectory, SidecarName, SearchOption.AllDirectories)
            .Select(file => (Folder: Path.GetDirectoryName(file)!, Origin: ReadSidecar(Path.GetDirectoryName(file)!)))
            .Where(entry => entry.Origin is { IsUploadPending: true })
            .Select(entry => (Path.Combine(entry.Folder, entry.Origin!.Name), entry.Origin!))
            .ToList();
    }

    /// <summary>The folder of an item's working copy: drive/&lt;account&gt;/&lt;item id&gt; below the data folder.</summary>
    internal static string WorkingFolder(string serverUrl, string userName, long itemId) =>
        Path.Combine(AppPaths.DriveDirectory, AccountFolder(serverUrl, userName), itemId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The sidecar of a working copy, or null.</summary>
    internal static DriveOrigin? ReadSidecar(string folder)
    {
        var file = Path.Combine(folder, SidecarName);
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<DriveOrigin>(File.ReadAllText(file), SidecarJson) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"A ThixDrive sidecar could not be read ({exception.GetType().Name})");
            return null;
        }
    }

    // ===== Helpers =====

    private static async Task<DriveSaveResult> UploadVersionAsync(OfficeDocument document, string ifMatch, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var origin = document.Drive!;
        var path = document.FilePath ?? throw new BusinessException("Dieses Dokument hat keine Arbeitskopie.");
        document.SetDrive(origin, DriveSyncState.Uploading);
        DriveItem item;
        try
        {
            using var client = CloudService.CreateClient();
            await using var stream = File.OpenRead(path);
            item = await client.SaveDriveFileAsync(origin.ItemId, stream, ifMatch, cancellationToken, progress);
        }
        catch (DriveConflictException conflict) when (conflict.IsChangedMeanwhile)
        {
            CloudService.ReportOnline();
            document.SetDrive(origin, DriveSyncState.Conflict);
            var by = conflict.Current?.ModifiedBy is { Length: > 0 } device ? $" (von „{device}“)" : "";
            return new DriveSaveResult(DriveSaveOutcome.ChangedMeanwhile, $"„{origin.Name}“ wurde in ThixDrive inzwischen geändert{by}.", conflict.Current);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            document.SetDrive(origin, DriveSyncState.NotUploaded);
            return new DriveSaveResult(DriveSaveOutcome.NotUploaded, "Das Hochladen wurde abgebrochen. Das Dokument ist auf diesem Computer gespeichert.");
        }
        catch (BusinessException exception)
        {
            document.SetDrive(origin, DriveSyncState.NotUploaded);
            return new DriveSaveResult(DriveSaveOutcome.NotUploaded, $"{exception.Message} Das Dokument ist auf diesem Computer gespeichert.");
        }
        catch (Exception exception) when (CloudService.IsCloudFailure(exception, cancellationToken) || exception is DriveConflictException)
        {
            var reason = exception is DriveConflictException conflict ? new BusinessException(conflict.Message) : CloudService.Explain(exception);
            document.SetDrive(origin, DriveSyncState.NotUploaded);
            var text = $"{reason.Message} Das Dokument ist auf diesem Computer gespeichert und noch nicht in ThixDrive.";
            if (CloudService.IsTransient(exception))
            {
                Log.Warning($"A ThixDrive document was not uploaded ({exception.GetType().Name}); it waits in its working copy");
                return new DriveSaveResult(DriveSaveOutcome.NotUploaded, text);
            }

            throw new BusinessException(text);
        }

        CloudService.ReportOnline();
        var uploaded = origin with
        {
            ETag = item.ETag ?? "",
            Name = item.Name,
            ParentId = item.ParentId,
            Location = item.Location,
            IsUploadPending = false,
            UploadedAt = DateTimeOffset.Now,
        };
        WriteSidecar(Path.GetDirectoryName(path)!, uploaded);
        document.SetDrive(uploaded, DriveSyncState.Uploaded);
        RecentDocuments.AddDrive(path, uploaded);
        Log.Info("A ThixDrive document was uploaded");
        return new DriveSaveResult(DriveSaveOutcome.Uploaded, $"„{item.Name}“ ist in ThixDrive gespeichert.");
    }

    /// <summary>The document belongs to <paramref name="item"/> from now on: its file goes into the item's working folder with a sidecar.</summary>
    private static void Adopt(OfficeDocument document, string source, DriveItem item, bool deleteSource)
    {
        var cloud = CloudService.Settings;
        var folder = WorkingFolder(cloud.ServerUrl, cloud.UserName, item.Id);
        Directory.CreateDirectory(folder);
        RemoveOtherFiles(folder, keep: item.Name);
        var target = Path.Combine(folder, item.Name);
        if (deleteSource)
        {
            File.Move(source, target, overwrite: true);
        }
        else
        {
            File.Copy(source, target, overwrite: true);
        }

        var origin = new DriveOrigin
        {
            ServerUrl = cloud.ServerUrl,
            UserName = cloud.UserName,
            ItemId = item.Id,
            ParentId = item.ParentId,
            Name = item.Name,
            Location = item.Location,
            ETag = item.ETag ?? "",
            UploadedAt = DateTimeOffset.Now,
        };
        WriteSidecar(folder, origin);
        document.MarkSaved(target, AppInfo.Definition.SupportedTypeOf(item.Name) ?? AppInfo.Definition.SaveType);
        DocumentFiles.DeleteRecovery(document.RecoveryId);
        document.SetDrive(origin, DriveSyncState.Uploaded);
        RecentDocuments.AddDrive(target, origin);
        Log.Info("A document was saved into ThixDrive");
    }

    private static T OpenFrom<T>(string workingCopy, DriveOrigin origin, DriveSyncState state, Func<string, T> create, string? password)
        where T : OfficeDocument
    {
        var type = AppInfo.Definition.SupportedTypeOf(workingCopy) ?? throw new BusinessException($"{AppInfo.ProductName} kann „{origin.Name}“ (noch) nicht öffnen.");
        var document = DocumentService.Load(workingCopy, type, create, password);
        document.SetDrive(origin, state);
        RecentDocuments.AddDrive(workingCopy, origin);
        return document;
    }

    /// <summary>
    /// Calls the cloud; its failures become a <see cref="BusinessException"/> in the person's words (and the state follows). A conflict
    /// (409 name taken, 412 changed) is passed on as it is when <paramref name="passConflicts"/>, else it is a refusal at the name.
    /// </summary>
    private static async Task<T> RunAsync<T>(Func<MatCloudClient, Task<T>> call, CancellationToken cancellationToken, bool passConflicts = false)
    {
        using var client = CloudService.CreateClient();
        try
        {
            var result = await call(client);
            CloudService.ReportOnline();
            return result;
        }
        catch (DriveConflictException conflict)
        {
            CloudService.ReportOnline();
            if (passConflicts)
            {
                throw;
            }

            throw new BusinessException(conflict.IsChangedMeanwhile ? conflict.Message : "Diesen Namen gibt es in diesem Ordner schon.", "Name");
        }
        catch (Exception exception) when (CloudService.IsCloudFailure(exception, cancellationToken))
        {
            throw CloudService.Explain(exception);
        }
    }

    private static string AccountFolder(string serverUrl, string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{serverUrl.ToLowerInvariant()}|{userName.ToLowerInvariant()}"));
        return Convert.ToHexStringLower(hash)[..16];
    }

    private static void WriteSidecar(string folder, DriveOrigin origin)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, SidecarName);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(origin, SidecarJson));
        File.Move(temporary, file, overwrite: true);
    }

    /// <summary>A renamed item leaves its old file behind: only the sidecar and the current name stay.</summary>
    private static void RemoveOtherFiles(string folder, string keep)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name != SidecarName && !name.StartsWith('.') && !string.Equals(name, keep, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"An old ThixDrive working copy could not be removed ({exception.GetType().Name})");
        }
    }

    /// <summary>Reports the bytes written so far (the download's progress).</summary>
    private sealed class ProgressStream(Stream inner, IProgress<long>? progress) : Stream
    {
        private long _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Report(count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Report(buffer.Length);
        }

        private void Report(int count)
        {
            _written += count;
            progress?.Report(_written);
        }
    }
}

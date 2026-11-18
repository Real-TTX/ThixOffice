using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// "Zuletzt verwendet" of an app (recent.json in its data folder): every document opened or saved comes first, pinned
/// ones stay at the top and never fall out, the others are cut to the length of the settings. A file that is gone
/// (moved, deleted, on a drive that is not connected) stays in the list, marked, until the user removes it.
/// </summary>
public static class RecentDocuments
{
    private static readonly JsonFileStore<RecentDocumentList> Store = new(() => AppPaths.RecentDocumentsFile, () => new RecentDocumentList());

    /// <summary>Raised after every change of the list (any thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>The list as the Open page shows it: pinned first, then by <paramref name="order"/>; filtered by <paramref name="search"/> (name or folder).</summary>
    public static IReadOnlyList<RecentDocumentItem> List(string? search = null, RecentDocumentOrder order = RecentDocumentOrder.LastOpened)
    {
        var documents = Store.Read().Documents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            documents = documents.Where(document => words.All(word => document.Path.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
        }

        var pinnedFirst = documents.OrderByDescending(document => document.IsPinned);
        var ordered = order == RecentDocumentOrder.Name
            ? pinnedFirst.ThenBy(document => document.FileName, StringComparer.CurrentCultureIgnoreCase)
            : pinnedFirst.ThenByDescending(document => document.LastOpened);
        return ordered.Select(document => new RecentDocumentItem(document, FileSystemProbe.FileExists(document.Path))).ToList();
    }

    /// <summary>
    /// Where "Speichern unter" offers to start: the default save folder first, then the folders of the documents used last
    /// that still exist, without repeats.
    /// </summary>
    public static IReadOnlyList<string> RecentFolders(int maximum = 6)
    {
        var folders = new List<string> { SettingsService.SaveFolder };
        foreach (var document in Store.Read().Documents.OrderByDescending(document => document.LastOpened))
        {
            var folder = document.Folder;
            if (folders.Count >= maximum)
            {
                break;
            }

            if (folder.Length > 0 && !folders.Any(known => SamePath(known, folder)) && FileSystemProbe.FolderExists(folder))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    /// <summary>A document was opened or saved: it comes first (a pinned one keeps its pin).</summary>
    public static void Add(string path, DateTimeOffset? openedAt = null)
    {
        var fullPath = FullPath(path);
        var limit = SettingsService.Get().RecentListSize;
        Update(list =>
        {
            var existing = list.Documents.FirstOrDefault(document => SamePath(document.Path, fullPath));
            var entry = new RecentDocument(fullPath, openedAt ?? DateTimeOffset.Now, existing?.IsPinned ?? false);
            var others = list.Documents.Where(document => !SamePath(document.Path, fullPath)).ToList();
            return list with { Documents = Trim([entry, .. others], limit) };
        });
    }

    /// <summary>Pins or unpins a document (a pinned one stays at the top and is never cut).</summary>
    public static void Pin(string path, bool pinned)
    {
        var fullPath = FullPath(path);
        Update(list =>
        {
            if (!list.Documents.Any(document => SamePath(document.Path, fullPath)))
            {
                throw new BusinessException("Dieses Dokument steht nicht in der Liste „Zuletzt verwendet“.");
            }

            return list with
            {
                Documents = [.. list.Documents.Select(document => SamePath(document.Path, fullPath) ? document with { IsPinned = pinned } : document)],
            };
        });
    }

    /// <summary>"Aus Liste entfernen": only the entry goes, never the file.</summary>
    public static void Remove(string path)
    {
        var fullPath = FullPath(path);
        Update(list => list with { Documents = [.. list.Documents.Where(document => !SamePath(document.Path, fullPath))] });
    }

    /// <summary>Removes the entries whose files are gone; returns how many.</summary>
    public static int RemoveMissing()
    {
        var removed = 0;
        Update(list =>
        {
            var kept = list.Documents.Where(document => FileSystemProbe.FileExists(document.Path)).ToList();
            removed = list.Documents.Count - kept.Count;
            return list with { Documents = kept };
        });
        return removed;
    }

    /// <summary>Drops the cache (tests).</summary>
    internal static void ResetCache() => Store.Reset();

    /// <summary>Pinned ones always stay; of the others the newest <paramref name="limit"/>.</summary>
    internal static List<RecentDocument> Trim(IReadOnlyList<RecentDocument> documents, int limit)
    {
        var kept = new List<RecentDocument>();
        var unpinned = 0;
        foreach (var document in documents)
        {
            if (document.IsPinned || unpinned++ < limit)
            {
                kept.Add(document);
            }
        }

        return kept;
    }

    private static void Update(Func<RecentDocumentList, RecentDocumentList> change)
    {
        Store.Update(change);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BusinessException("Kein Dokument angegeben.");
        }

        return Path.GetFullPath(path);
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(first, second, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

/// <summary>The content of recent.json.</summary>
internal sealed record RecentDocumentList
{
    public List<RecentDocument> Documents { get; init; } = [];
}

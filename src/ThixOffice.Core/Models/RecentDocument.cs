namespace ThixOffice.Core.Models;

/// <summary>
/// A document in "Zuletzt verwendet": where it is, when it was opened last, pinned or not. A document of ThixDrive has
/// <see cref="Drive"/>: its <see cref="Path"/> is the local working copy, and it opens from the Drive again (the working copy when
/// the cloud cannot be reached).
/// </summary>
public sealed record RecentDocument(string Path, DateTimeOffset LastOpened, bool IsPinned = false)
{
    /// <summary>The ThixDrive item, or null for a file on this computer.</summary>
    public RecentDriveItem? Drive { get; init; }

    public bool IsInDrive => Drive is not null;

    /// <summary>The file name without the folder ("Bericht.docx").</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The folder the document lies in (for a ThixDrive document: "ThixDrive › /Projekte").</summary>
    public string Folder => Drive is { } drive ? $"ThixDrive › {drive.Location}" : System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>The ThixDrive item of a recent document: the cloud, the account, the item and its folder for people.</summary>
public sealed record RecentDriveItem(string ServerUrl, string UserName, long ItemId, string Location);

/// <summary>
/// A row of the list: the entry and whether it can be opened (a file: it is still there; a ThixDrive document: its working copy is
/// there or this app is signed in to its account).
/// </summary>
public sealed record RecentDocumentItem(RecentDocument Document, bool Exists);

/// <summary>The order of "Zuletzt verwendet".</summary>
public enum RecentDocumentOrder
{
    /// <summary>Pinned first, then the most recently opened.</summary>
    LastOpened = 0,

    /// <summary>Pinned first, then by file name.</summary>
    Name = 1,
}

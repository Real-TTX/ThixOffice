namespace ThixOffice.Core.Models;

/// <summary>A document in "Zuletzt verwendet": where it is, when it was opened last, pinned or not.</summary>
public sealed record RecentDocument(string Path, DateTimeOffset LastOpened, bool IsPinned = false)
{
    /// <summary>The file name without the folder ("Bericht.docx").</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The folder the document lies in.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>A row of the list: the entry and whether its file is still there (moved, deleted, on a drive that is not connected).</summary>
public sealed record RecentDocumentItem(RecentDocument Document, bool Exists);

/// <summary>The order of "Zuletzt verwendet".</summary>
public enum RecentDocumentOrder
{
    /// <summary>Pinned first, then the most recently opened.</summary>
    LastOpened = 0,

    /// <summary>Pinned first, then by file name.</summary>
    Name = 1,
}

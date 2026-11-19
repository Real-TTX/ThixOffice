namespace ThixOffice.Core.Models;

/// <summary>
/// Where a document opened from ThixDrive belongs: the cloud and account, the item (its id never changes), the version that was
/// opened or saved last (<see cref="ETag"/>, sent as If-Match with the next save) and whether the local working copy holds changes
/// that are not uploaded yet. It is kept next to the working copy (drive.json, the sidecar) so that it outlives the app.
/// </summary>
public sealed record DriveOrigin
{
    public required string ServerUrl { get; init; }

    public required string UserName { get; init; }

    public required long ItemId { get; init; }

    /// <summary>The folder of the item; null at the top of the Drive.</summary>
    public long? ParentId { get; init; }

    /// <summary>The name in the Drive ("Bericht.docx").</summary>
    public required string Name { get; init; }

    /// <summary>The folder for people ("/Projekte/2026").</summary>
    public string Location { get; init; } = "/";

    /// <summary>The version the working copy is based on, with its quotes.</summary>
    public string ETag { get; init; } = "";

    /// <summary>The working copy was saved but not uploaded (offline, refused, conflict): it must not be replaced by a download.</summary>
    public bool IsUploadPending { get; init; }

    public DateTimeOffset? UploadedAt { get; init; }

    /// <summary>"/Projekte/Bericht.docx" – the place in the Drive for people.</summary>
    public string DisplayPath => Location.TrimEnd('/') + "/" + Name;
}

/// <summary>Where the upload of a ThixDrive document stands (the title bar shows it).</summary>
public enum DriveSyncState
{
    /// <summary>The Drive has what the working copy has.</summary>
    Uploaded = 0,

    /// <summary>A save is being uploaded.</summary>
    Uploading = 1,

    /// <summary>Saved here, not in the Drive yet (offline, signed out, refused): it is tried again.</summary>
    NotUploaded = 2,

    /// <summary>Somebody saved the file in between: the person decides (Überschreiben, Als Kopie speichern).</summary>
    Conflict = 3,
}

namespace ThixMail.Core.Sync;

/// <summary>What an action of the person does at the server, replayed in order by the sync (each idempotent: a replay after a lost answer does no harm).</summary>
public enum OperationKind
{
    /// <summary>Add/remove flags and keywords (gelesen, gekennzeichnet, Kategorien).</summary>
    Flags = 1,

    /// <summary>Move to another folder (Verschieben, Löschen into Gelöschte Elemente, Archivieren).</summary>
    Move = 2,

    /// <summary>Delete for good (in Gelöschte Elemente, Ordner leeren).</summary>
    Expunge = 3,

    /// <summary>Put a message into a folder (a draft into Entwürfe, a sent copy into Gesendete Elemente).</summary>
    Append = 4,

    /// <summary>Make, rename or delete a folder.</summary>
    CreateFolder = 5,

    RenameFolder = 6,

    DeleteFolder = 7,
}

/// <summary>
/// The data of an operation, as the server knows the messages: the folder's path and the UIDs at the time of the action (the local ids follow for the
/// store's bookkeeping), the target, the flags, the file of a message to append.
/// </summary>
public sealed record OperationData
{
    public string Folder { get; init; } = "";

    public List<uint> Uids { get; init; } = [];

    public List<long> MessageIds { get; init; } = [];

    public string? Target { get; init; }

    public int Add { get; init; }

    public int Remove { get; init; }

    public List<string> AddKeywords { get; init; } = [];

    public List<string> RemoveKeywords { get; init; } = [];

    /// <summary>The file with the message to append (in the store's folder).</summary>
    public string? RawPath { get; init; }

    /// <summary>The Message-ID of an appended message (a newer save of the same draft replaces a waiting append).</summary>
    public string? MessageId { get; init; }

    /// <summary>A folder name (create, rename).</summary>
    public string? Name { get; init; }
}

/// <summary>An operation waiting in the queue.</summary>
public sealed record PendingOperation(long Id, string AccountId, OperationKind Kind, OperationData Data, DateTimeOffset CreatedAt, int Attempts, string? LastError);

/// <summary>The state of a message in the outbox (Postausgang).</summary>
public enum OutboxState
{
    /// <summary>Waits for the next connection.</summary>
    Waiting = 0,

    /// <summary>Being sent now.</summary>
    Sending = 1,

    /// <summary>The server refused it (a recipient, the size): the person must change it; never retried by itself.</summary>
    Failed = 2,
}

/// <summary>A message in the Postausgang.</summary>
public sealed record OutboxItem(long Id, string AccountId, string RawPath, string Subject, string Recipients, DateTimeOffset CreatedAt, OutboxState State,
    int Attempts, string? LastError, string MessageId, long? OriginalMessage, int OriginKind, DateTimeOffset? SendAt = null)
{
    /// <summary>"Später senden": the time has not come yet.</summary>
    public bool IsWaitingForItsTime(DateTimeOffset now) => SendAt is { } at && at > now;
}

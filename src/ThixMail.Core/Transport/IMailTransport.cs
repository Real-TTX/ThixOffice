using ThixMail.Core.Model;

namespace ThixMail.Core.Transport;

/// <summary>
/// The protocol seam of ThixMail: how the sync engine, the sender and the connection test reach a mail server. <see cref="MailKitTransport"/> is the
/// real one (IMAP and SMTP through MailKit), <see cref="SampleMailServer"/> a server in memory (tests, snapshots, the self test). Everything above it
/// – store, sync, UI – never sees MailKit.
/// </summary>
public interface IMailTransport
{
    /// <summary>Connects and signs in at the IMAP server of <paramref name="account"/>. Failures as <see cref="MailConnectionException"/>.</summary>
    Task<IImapConnection> ConnectImapAsync(MailAccount account, string password, CancellationToken cancellationToken);

    /// <summary>Connects and signs in at the SMTP server of <paramref name="account"/>.</summary>
    Task<ISmtpConnection> ConnectSmtpAsync(MailAccount account, string password, CancellationToken cancellationToken);
}

/// <summary>A folder as the server lists it.</summary>
public sealed record RemoteFolder(string Path, string Name, string? ParentPath, char Delimiter, FolderRole Role, bool IsSelectable);

/// <summary>What selecting a folder tells: its UIDVALIDITY, the next UID, the highest MODSEQ (0 without CONDSTORE), how many messages, how many unread.</summary>
public sealed record FolderState(uint UidValidity, uint UidNext, ulong HighestModSeq, int Count, int Unread);

/// <summary>The header of a message as the server sends it (ENVELOPE, FLAGS, size, body structure, references, a preview).</summary>
public sealed record RemoteMessage
{
    public uint Uid { get; init; }

    public string MessageId { get; init; } = "";

    public string InReplyTo { get; init; } = "";

    public IReadOnlyList<string> References { get; init; } = [];

    public string Subject { get; init; } = "";

    public MailAddress From { get; init; } = new("", "");

    public IReadOnlyList<MailAddress> To { get; init; } = [];

    public IReadOnlyList<MailAddress> Cc { get; init; } = [];

    public IReadOnlyList<MailAddress> ReplyTo { get; init; } = [];

    public DateTimeOffset Date { get; init; }

    public long Size { get; init; }

    public MessageFlags Flags { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public bool HasAttachments { get; init; }

    public string Preview { get; init; } = "";

    public ulong ModSeq { get; init; }
}

/// <summary>The flags of one message now (a flag poll or CHANGEDSINCE).</summary>
public sealed record FlagState(uint Uid, MessageFlags Flags, IReadOnlyList<string> Keywords, ulong ModSeq);

/// <summary>One signed-in IMAP connection. Not thread-safe: the sync engine uses it from one task.</summary>
public interface IImapConnection : IAsyncDisposable
{
    bool SupportsCondStore { get; }

    bool SupportsQResync { get; }

    bool SupportsIdle { get; }

    bool SupportsMove { get; }

    Task<IReadOnlyList<RemoteFolder>> ListFoldersAsync(CancellationToken cancellationToken);

    /// <summary>Selects the folder (read-write) and tells its state.</summary>
    Task<FolderState> OpenAsync(string path, CancellationToken cancellationToken);

    /// <summary>Every UID of the folder (UID SEARCH ALL): what is not in it was expunged.</summary>
    Task<IReadOnlyList<uint>> UidsAsync(string path, CancellationToken cancellationToken);

    /// <summary>The headers of <paramref name="uids"/> (missing ones are left out).</summary>
    Task<IReadOnlyList<RemoteMessage>> FetchAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken);

    /// <summary>The flags of <paramref name="uids"/>; with <paramref name="changedSince"/> &gt; 0 (CONDSTORE) only those changed since.</summary>
    Task<IReadOnlyList<FlagState>> FlagsAsync(string path, IReadOnlyList<uint> uids, ulong changedSince, CancellationToken cancellationToken);

    /// <summary>The whole message (RFC 5322 bytes).</summary>
    Task<byte[]?> GetRawAsync(string path, uint uid, CancellationToken cancellationToken);

    Task SetFlagsAsync(string path, IReadOnlyList<uint> uids, MessageFlags add, MessageFlags remove, IReadOnlyList<string> addKeywords,
        IReadOnlyList<string> removeKeywords, CancellationToken cancellationToken);

    /// <summary>Moves messages (MOVE, else COPY + delete); the new UIDs in order when the server tells (UIDPLUS), else null.</summary>
    Task<IReadOnlyList<uint>?> MoveAsync(string path, IReadOnlyList<uint> uids, string target, CancellationToken cancellationToken);

    /// <summary>Deletes for good (\Deleted + UID EXPUNGE).</summary>
    Task ExpungeAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken);

    /// <summary>Adds a message to a folder (sent copy, draft); its UID when the server tells.</summary>
    Task<uint?> AppendAsync(string path, byte[] raw, MessageFlags flags, DateTimeOffset date, CancellationToken cancellationToken);

    /// <summary>Makes a folder below <paramref name="parentPath"/> (null = top level); its path.</summary>
    Task<string> CreateFolderAsync(string? parentPath, string name, CancellationToken cancellationToken);

    Task<string> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken);

    Task DeleteFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Waits in IDLE on <paramref name="path"/> (or polls with NOOP when the server cannot) until the server reports a change (true), the time is up or
    /// <paramref name="stop"/> ends the wait (false).
    /// </summary>
    Task<bool> IdleAsync(string path, TimeSpan maximum, CancellationToken stop);
}

/// <summary>One signed-in SMTP connection.</summary>
public interface ISmtpConnection : IAsyncDisposable
{
    /// <summary>Sends the message to every recipient (the envelope), from the account's address.</summary>
    Task SendAsync(byte[] raw, MailAddress from, IReadOnlyList<MailAddress> recipients, CancellationToken cancellationToken);
}

/// <summary>What went wrong talking to a mail server, for the German explanation and the decision (retry, ask for the password, show the certificate).</summary>
public enum MailErrorKind
{
    /// <summary>The server cannot be reached (name unknown, refused, no network, timeout).</summary>
    Network,

    /// <summary>User name or password wrong.</summary>
    Authentication,

    /// <summary>The provider wants an app password (two-factor accounts of Gmail, iCloud, Yahoo …).</summary>
    AppPasswordRequired,

    /// <summary>The server's certificate is not trusted (expired, other name, self-signed): show it, never accept silently.</summary>
    Certificate,

    /// <summary>The server said something unexpected or refused the command.</summary>
    Protocol,

    /// <summary>The server refused a recipient or the message (SMTP 5xx).</summary>
    Rejected,

    /// <summary>The browser sign-in (OAuth) of the account has ended (refresh token expired or revoked): the person signs in again; the mail stays.</summary>
    SignInRequired,
}

/// <summary>The certificate of a server as the person sees it before deciding.</summary>
public sealed record CertificateInfo(string Host, string Subject, string Issuer, DateTimeOffset NotBefore, DateTimeOffset NotAfter, string Thumbprint,
    string Problem);

/// <summary>A failure of a mail server in German words, with its kind (and the certificate when that is the problem).</summary>
public sealed class MailConnectionException(MailErrorKind kind, string message, CertificateInfo? certificate = null, Exception? inner = null)
    : Exception(message, inner)
{
    public MailErrorKind Kind { get; } = kind;

    public CertificateInfo? Certificate { get; } = certificate;

    /// <summary>Worth trying again later by itself (no network, server busy) – not a wrong password or certificate.</summary>
    public bool IsTransient => Kind is MailErrorKind.Network;
}

using System.Globalization;

namespace ThixMail.Core.Model;

/// <summary>An address of a mail: the name as written (may be empty) and the address itself.</summary>
public sealed record MailAddress(string Name, string Address)
{
    /// <summary>"Anna Berger" – or the address when there is no name.</summary>
    public string Display => Name.Length > 0 ? Name : Address;

    /// <summary>"Anna Berger &lt;anna@example.com&gt;" (the form of an address field).</summary>
    public string Full => Name.Length > 0 ? $"{Name} <{Address}>" : Address;

    public override string ToString() => Full;
}

/// <summary>How a connection is protected.</summary>
public enum ConnectionSecurity
{
    /// <summary>TLS from the first byte (IMAPS 993, SMTPS 465).</summary>
    SslOnConnect,

    /// <summary>Plain first, then STARTTLS – refused when the server does not offer it (143, 587).</summary>
    StartTls,

    /// <summary>No encryption at all (only for a server on this computer or in tests; the account page warns).</summary>
    None,
}

/// <summary>How an account signs in at its servers.</summary>
public enum MailAuthentication
{
    /// <summary>User name and password (or the provider's app password).</summary>
    Password,

    /// <summary>OAuth 2.0: the person signs in in the browser, ThixMail keeps the tokens (SASL XOAUTH2).</summary>
    OAuth,
}

/// <summary>Host, port and protection of one server of an account.</summary>
public sealed record ServerSettings(string Host, int Port, ConnectionSecurity Security)
{
    public static ServerSettings Empty { get; } = new("", 0, ConnectionSecurity.SslOnConnect);

    public string Display => $"{Host}:{Port.ToString(CultureInfo.InvariantCulture)} ({SecurityText(Security)})";

    public static string SecurityText(ConnectionSecurity security) => security switch
    {
        ConnectionSecurity.SslOnConnect => "SSL/TLS",
        ConnectionSecurity.StartTls => "STARTTLS",
        _ => "unverschlüsselt",
    };
}

/// <summary>
/// A mail account of this computer: its name in the folder pane, the sender (name and address), the IMAP and SMTP servers with their user names, the
/// default signatures and the certificates the person accepted explicitly (SHA-256 per host). NO password: those live only in the TokenStore
/// (<see cref="Accounts.AccountService"/>). Kept as an item of the settings store, ready to travel through ThixCloud's general sync.
/// </summary>
public sealed record MailAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>The name in the folder pane ("Arbeit"); the address when empty.</summary>
    public string Name { get; init; } = "";

    /// <summary>The name recipients see.</summary>
    public string SenderName { get; init; } = "";

    public string Address { get; init; } = "";

    public ServerSettings Imap { get; init; } = ServerSettings.Empty;

    public string ImapUser { get; init; } = "";

    public ServerSettings Smtp { get; init; } = ServerSettings.Empty;

    /// <summary>The user name at the SMTP server (usually the IMAP one).</summary>
    public string SmtpUser { get; init; } = "";

    /// <summary>The SMTP server takes the IMAP password (no second one is asked for).</summary>
    public bool SmtpUsesImapPassword { get; init; } = true;

    /// <summary>The signature of new messages / of replies and forwards (null = none).</summary>
    public string? NewSignatureId { get; init; }

    public string? ReplySignatureId { get; init; }

    /// <summary>Certificates the person accepted after seeing them: "host|sha256" (never accepted silently).</summary>
    public IReadOnlyList<string> TrustedCertificates { get; init; } = [];

    /// <summary>Where the account stands in the folder pane.</summary>
    public int Order { get; init; }

    /// <summary>The server copies what SMTP sends into Gesendete Elemente itself (Gmail, Outlook.com): no APPEND.</summary>
    public bool ServerSavesSent { get; init; }

    /// <summary>How the account signs in: a password (or app password), or OAuth 2.0 in the browser (XOAUTH2; Gmail, Microsoft).</summary>
    public MailAuthentication Authentication { get; init; }

    /// <summary>The OAuth provider ("google", "microsoft") when <see cref="Authentication"/> is OAuth (<see cref="Accounts.OAuthProviders"/>).</summary>
    public string? OAuthProvider { get; init; }

    public bool UsesOAuth => Authentication == MailAuthentication.OAuth;

    public string DisplayName => Name.Length > 0 ? Name : Address;

    public MailAddress Sender => new(SenderName, Address);

    public bool Trusts(string host, string thumbprint) =>
        TrustedCertificates.Contains($"{host.ToLowerInvariant()}|{thumbprint.ToUpperInvariant()}", StringComparer.Ordinal);
}

/// <summary>The role of a folder (RFC 6154 special-use, or recognized by its name): where Outlook shows it and what ThixMail does with it.</summary>
public enum FolderRole
{
    None = 0,
    Inbox = 1,
    Drafts = 2,
    Sent = 3,
    Trash = 4,
    Junk = 5,
    Archive = 6,
    All = 7,
    Flagged = 8,
}

/// <summary>A folder of an account in the store.</summary>
public sealed record MailFolder
{
    public long Id { get; init; }

    public string AccountId { get; init; } = "";

    /// <summary>The full name at the server ("INBOX/Projekte").</summary>
    public string Path { get; init; } = "";

    /// <summary>The last part of the path ("Projekte").</summary>
    public string Name { get; init; } = "";

    public string? ParentPath { get; init; }

    public char Delimiter { get; init; } = '/';

    public FolderRole Role { get; init; }

    public bool IsSelectable { get; init; } = true;

    public uint UidValidity { get; init; }

    public uint UidNext { get; init; }

    public ulong HighestModSeq { get; init; }

    public int Total { get; init; }

    public int Unread { get; init; }

    /// <summary>The German name Outlook shows for a special folder ("Posteingang"), else the folder's own name.</summary>
    public string DisplayName => Role switch
    {
        FolderRole.Inbox => "Posteingang",
        FolderRole.Drafts => "Entwürfe",
        FolderRole.Sent => "Gesendete Elemente",
        FolderRole.Trash => "Gelöschte Elemente",
        FolderRole.Junk => "Junk-E-Mail",
        FolderRole.Archive => "Archiv",
        _ => Name,
    };

    /// <summary>The order of the special folders in the pane (Outlook's), then the others by name.</summary>
    public int RoleOrder => Role switch
    {
        FolderRole.Inbox => 0,
        FolderRole.Drafts => 1,
        FolderRole.Sent => 2,
        FolderRole.Trash => 3,
        FolderRole.Junk => 4,
        FolderRole.Archive => 5,
        _ => 10,
    };
}

/// <summary>The flags of a message (IMAP system flags + $Forwarded).</summary>
[Flags]
public enum MessageFlags
{
    None = 0,
    Seen = 1,
    Answered = 2,
    Flagged = 4,
    Deleted = 8,
    Draft = 16,
    Forwarded = 32,
}

/// <summary>A message in the store: what the list and the reading pane's header need (the body is fetched on demand).</summary>
public sealed record MessageHeader
{
    public long Id { get; init; }

    public long FolderId { get; init; }

    public string AccountId { get; init; } = "";

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

    /// <summary>The IMAP keywords that are categories ("Rot", "Projekt") – not the $-keywords of the system.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    public bool HasAttachments { get; init; }

    /// <summary>The first words of the text (the list's second line).</summary>
    public string Preview { get; init; } = "";

    /// <summary>The conversation (the id of its first message in the store).</summary>
    public long ThreadId { get; init; }

    public bool IsRead => Flags.HasFlag(MessageFlags.Seen);

    public bool IsFlagged => Flags.HasFlag(MessageFlags.Flagged);
}

/// <summary>A signature: a name and its text as HTML (and the plain text made from it).</summary>
public sealed record MailSignature(string Id, string Name, string Html, string Text);

/// <summary>A template (Outlook's "Vorlage"): a name, the subject and the body a new message starts with (no recipients, no attachments).</summary>
/// <summary>A template: subject, text and the recipients it was saved with (attachments are kept on the computer that saved it: <see cref="Accounts.AccountStore.TemplateAttachments"/>).</summary>
public sealed record MailTemplate(string Id, string Name, string Subject, string Html, string Text, IReadOnlyList<MailAddress>? To = null, IReadOnlyList<MailAddress>? Cc = null);

/// <summary>A category (an IMAP keyword) with its colour slot (the UI maps the slot to a Mat token).</summary>
public sealed record MailCategory(string Name, int ColorSlot);

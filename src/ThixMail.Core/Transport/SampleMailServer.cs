using System.Text;
using MimeKit;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;

namespace ThixMail.Core.Transport;

/// <summary>
/// A mail server in memory (IMAP and SMTP behind <see cref="IMailTransport"/>) for the tests, the snapshots and the self test: mailboxes by address
/// with a password, folders with special-use roles, UIDVALIDITY/UIDNEXT, messages as RFC 5322 bytes with flags, keywords and MODSEQ (CONDSTORE can be
/// switched off to test the flag poll), MOVE, APPEND, IDLE that wakes up when something changes, an SMTP that delivers to the mailboxes it knows
/// (and keeps every sent message), and faults: unreachable, a wrong password, a certificate problem, the next N calls failing, slow links.
/// Thread-safe (one lock).
/// </summary>
public sealed class SampleMailServer : IMailTransport
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Mailbox> _mailboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(MailAddress From, IReadOnlyList<MailAddress> To, byte[] Raw)> _sent = [];
    private int _failures;
    private MailErrorKind _failureKind = MailErrorKind.Network;

    /// <summary>False = every connection fails as unreachable (offline).</summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>The server offers CONDSTORE (MODSEQ); off = the client must poll flags.</summary>
    public bool SupportsCondStore { get; set; } = true;

    /// <summary>The server offers MOVE (off = COPY + EXPUNGE).</summary>
    public bool SupportsMove { get; set; } = true;

    /// <summary>A delay of every call (a slow link).</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>The certificate the server shows is not trusted (self-signed): the connection fails until the account trusts <see cref="CertificateThumbprint"/>.</summary>
    public bool HasUntrustedCertificate { get; set; }

    public const string CertificateThumbprint = "3F1A9C0E5B7D2468ACE13579BDF02468ACE13579BDF02468ACE13579BDF0246";

    /// <summary>How many IMAP connections were opened (tests: reconnects).</summary>
    public int Connections { get; private set; }

    /// <summary>Every message SMTP took, in order.</summary>
    public IReadOnlyList<(MailAddress From, IReadOnlyList<MailAddress> To, byte[] Raw)> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>The next <paramref name="count"/> calls fail with <paramref name="kind"/>.</summary>
    public void FailNext(int count, MailErrorKind kind = MailErrorKind.Network)
    {
        lock (_gate)
        {
            _failures = count;
            _failureKind = kind;
        }
    }

    /// <summary>Adds a mailbox with the usual folders (Posteingang, Entwürfe, Gesendet, Papierkorb, Spam, Archiv).</summary>
    public void AddMailbox(string address, string password)
    {
        lock (_gate)
        {
            var mailbox = new Mailbox(password);
            mailbox.Add("INBOX", FolderRole.Inbox);
            mailbox.Add("Drafts", FolderRole.Drafts);
            mailbox.Add("Sent", FolderRole.Sent);
            mailbox.Add("Trash", FolderRole.Trash);
            mailbox.Add("Junk", FolderRole.Junk);
            mailbox.Add("Archive", FolderRole.Archive);
            _mailboxes[address] = mailbox;
        }
    }

    /// <summary>The mailbox accepts <paramref name=accessToken/> through XOAUTH2 (what a provider's token endpoint issued).</summary>
    public void AcceptAccessToken(string address, string accessToken)
    {
        lock (_gate)
        {
            _mailboxes[address].AccessTokens.Add(accessToken);
        }
    }

    /// <summary>Every access token of the mailbox stops working (revoked, expired at the provider).</summary>
    public void RevokeAccessTokens(string address)
    {
        lock (_gate)
        {
            _mailboxes[address].AccessTokens.Clear();
        }
    }

    /// <summary>How many sign-ins went through XOAUTH2 (tests).</summary>
    public int XOAuth2SignIns { get; private set; }

    /// <summary>Adds a folder (no role) to a mailbox.</summary>
    public void AddFolder(string address, string path)
    {
        lock (_gate)
        {
            _mailboxes[address].Add(path, FolderRole.None);
        }
    }

    /// <summary>Delivers a message into a folder (as if it arrived); its UID.</summary>
    public uint Deliver(string address, string path, byte[] raw, MessageFlags flags = MessageFlags.None, params string[] keywords)
    {
        lock (_gate)
        {
            var folder = _mailboxes[address].Folders[path];
            var uid = folder.Append(raw, flags, keywords);
            folder.Changed();
            return uid;
        }
    }

    /// <summary>Delivers a message built from the parts (tests).</summary>
    public uint Deliver(string address, string path, MailAddress from, string subject, string text, DateTimeOffset? date = null, MessageFlags flags = MessageFlags.None,
        string? messageId = null, string? inReplyTo = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(from.Name, from.Address));
        message.To.Add(new MailboxAddress("", address));
        message.Subject = subject;
        message.Date = date ?? DateTimeOffset.Now;
        message.MessageId = messageId ?? MimeKit.Utils.MimeUtils.GenerateMessageId("sample.example");
        if (inReplyTo is not null)
        {
            message.InReplyTo = inReplyTo;
            message.References.Add(inReplyTo);
        }

        message.Body = new TextPart("plain") { Text = text };
        return Deliver(address, path, MailMime.ToBytes(message), flags);
    }

    /// <summary>The UIDs of a folder now.</summary>
    public IReadOnlyList<uint> UidsOf(string address, string path)
    {
        lock (_gate)
        {
            return [.. _mailboxes[address].Folders[path].Messages.Keys];
        }
    }

    /// <summary>The flags of a message now.</summary>
    public MessageFlags FlagsOf(string address, string path, uint uid)
    {
        lock (_gate)
        {
            return _mailboxes[address].Folders[path].Messages[uid].Flags;
        }
    }

    public IReadOnlyList<string> KeywordsOf(string address, string path, uint uid)
    {
        lock (_gate)
        {
            return [.. _mailboxes[address].Folders[path].Messages[uid].Keywords];
        }
    }

    /// <summary>The subjects of a folder (tests).</summary>
    public IReadOnlyList<string> SubjectsOf(string address, string path)
    {
        lock (_gate)
        {
            return [.. _mailboxes[address].Folders[path].Messages.Values.Select(m => MailMime.Parse(m.Raw).Subject ?? "")];
        }
    }

    /// <summary>Another client changes flags (tests: flags from the server side).</summary>
    public void SetFlags(string address, string path, uint uid, MessageFlags flags)
    {
        lock (_gate)
        {
            var folder = _mailboxes[address].Folders[path];
            var message = folder.Messages[uid];
            message.Flags = flags;
            message.ModSeq = ++folder.HighestModSeq;
            folder.Changed();
        }
    }

    /// <summary>Another client deletes a message for good.</summary>
    public void Expunge(string address, string path, uint uid)
    {
        lock (_gate)
        {
            var folder = _mailboxes[address].Folders[path];
            folder.Messages.Remove(uid);
            folder.HighestModSeq++;
            folder.Changed();
        }
    }

    /// <summary>The server rebuilt a folder: a new UIDVALIDITY, every message gets a new UID.</summary>
    public void ResetUidValidity(string address, string path)
    {
        lock (_gate)
        {
            var folder = _mailboxes[address].Folders[path];
            var messages = folder.Messages.Values.ToList();
            folder.Messages.Clear();
            folder.UidValidity += 1000;
            folder.UidNext = 1;
            foreach (var message in messages)
            {
                folder.Append(message.Raw, message.Flags, [.. message.Keywords]);
            }

            folder.Changed();
        }
    }

    // ===== IMailTransport =====

    public async Task<IImapConnection> ConnectImapAsync(MailAccount account, string password, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        var mailbox = SignIn(account, account.ImapUser, password);
        lock (_gate)
        {
            Connections++;
        }

        return new Connection(this, mailbox);
    }

    public async Task<ISmtpConnection> ConnectSmtpAsync(MailAccount account, string password, CancellationToken cancellationToken)
    {
        await DelayAsync(cancellationToken);
        SignIn(account, account.SmtpUser.Length > 0 ? account.SmtpUser : account.ImapUser, password);
        return new Smtp(this);
    }

    private Mailbox SignIn(MailAccount account, string user, string password)
    {
        Fault();
        if (HasUntrustedCertificate && !account.Trusts(account.Imap.Host, CertificateThumbprint))
        {
            throw new MailConnectionException(MailErrorKind.Certificate, "Das Zertifikat des Servers ist nicht vertrauenswürdig (selbst signiert).",
                new CertificateInfo(account.Imap.Host, $"CN={account.Imap.Host}", $"CN={account.Imap.Host}", DateTimeOffset.Now.AddYears(-1),
                    DateTimeOffset.Now.AddYears(1), CertificateThumbprint, "Selbst signiert – keine bekannte Zertifizierungsstelle hat es ausgestellt."));
        }

        lock (_gate)
        {
            if (account.UsesOAuth)
            {
                // SASL XOAUTH2: the access token must be one the (fake) provider issued for this mailbox
                if (!_mailboxes.TryGetValue(user, out var byToken) || !byToken.AccessTokens.Contains(password))
                {
                    throw new MailConnectionException(MailErrorKind.Authentication, "Die Anmeldung (XOAUTH2) wurde abgelehnt.");
                }

                XOAuth2SignIns++;
                return byToken;
            }

            if (!_mailboxes.TryGetValue(user, out var mailbox) || mailbox.Password != password)
            {
                throw new MailConnectionException(MailErrorKind.Authentication, "Benutzername oder Kennwort stimmen nicht.");
            }

            return mailbox;
        }
    }

    private void Fault()
    {
        lock (_gate)
        {
            if (!IsReachable)
            {
                throw new MailConnectionException(MailErrorKind.Network, "Der Mailserver ist nicht erreichbar.");
            }

            if (_failures > 0)
            {
                _failures--;
                throw new MailConnectionException(_failureKind, _failureKind == MailErrorKind.Network ? "Die Verbindung zum Mailserver wurde unterbrochen." : "Der Mailserver hat den Befehl abgelehnt.");
            }
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, cancellationToken);
        }
    }

    private void Deliver(MailAddress from, IReadOnlyList<MailAddress> recipients, byte[] raw)
    {
        lock (_gate)
        {
            _sent.Add((from, recipients, raw));
            foreach (var recipient in recipients)
            {
                if (_mailboxes.TryGetValue(recipient.Address, out var mailbox))
                {
                    var inbox = mailbox.Folders["INBOX"];
                    inbox.Append(raw, MessageFlags.None, []);
                    inbox.Changed();
                }
            }
        }
    }

    // ===== the data =====

    private sealed class Mailbox(string password)
    {
        public string Password { get; } = password;

        public HashSet<string> AccessTokens { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);

        public void Add(string path, FolderRole role) => Folders[path] = new Folder(path, role, (uint)(Folders.Count + 1) * 17);
    }

    private sealed class Folder(string path, FolderRole role, uint uidValidity)
    {
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Path { get; set; } = path;

        public FolderRole Role { get; } = role;

        public uint UidValidity { get; set; } = uidValidity;

        public uint UidNext { get; set; } = 1;

        public ulong HighestModSeq { get; set; } = 1;

        public SortedDictionary<uint, Message> Messages { get; } = [];

        public Task WhenChanged => _changed.Task;

        public uint Append(byte[] raw, MessageFlags flags, string[] keywords)
        {
            var uid = UidNext++;
            Messages[uid] = new Message(raw) { Flags = flags, Keywords = [.. keywords], ModSeq = ++HighestModSeq };
            return uid;
        }

        public void Changed()
        {
            var previous = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }

    private sealed class Message(byte[] raw)
    {
        public byte[] Raw { get; } = raw;

        public MessageFlags Flags { get; set; }

        public List<string> Keywords { get; set; } = [];

        public ulong ModSeq { get; set; }
    }

    // ===== a connection =====

    private sealed class Connection(SampleMailServer server, Mailbox mailbox) : IImapConnection
    {
        private bool _closed;

        public bool SupportsCondStore => server.SupportsCondStore;

        public bool SupportsQResync => false;

        public bool SupportsIdle => true;

        public bool SupportsMove => server.SupportsMove;

        public async Task<IReadOnlyList<RemoteFolder>> ListFoldersAsync(CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                return [.. mailbox.Folders.Values.Select(f =>
                {
                    var slash = f.Path.LastIndexOf('/');
                    return new RemoteFolder(f.Path, slash < 0 ? f.Path : f.Path[(slash + 1)..], slash < 0 ? null : f.Path[..slash], '/', f.Role, true);
                })];
            }
        }

        public async Task<FolderState> OpenAsync(string path, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                return new FolderState(folder.UidValidity, folder.UidNext, server.SupportsCondStore ? folder.HighestModSeq : 0, folder.Messages.Count,
                    folder.Messages.Values.Count(m => !m.Flags.HasFlag(MessageFlags.Seen)));
            }
        }

        public async Task<IReadOnlyList<uint>> UidsAsync(string path, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                return [.. FolderOf(path).Messages.Keys];
            }
        }

        public async Task<IReadOnlyList<RemoteMessage>> FetchAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            List<(uint Uid, Message Message)> found;
            lock (server._gate)
            {
                var folder = FolderOf(path);
                found = [.. uids.Where(folder.Messages.ContainsKey).Select(uid => (uid, folder.Messages[uid]))];
            }

            return [.. found.Select(item => MailMime.Summarize(item.Uid, item.Message.Raw, item.Message.Flags, item.Message.Keywords, item.Message.ModSeq))];
        }

        public async Task<IReadOnlyList<FlagState>> FlagsAsync(string path, IReadOnlyList<uint> uids, ulong changedSince, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                return [.. uids.Where(folder.Messages.ContainsKey)
                    .Select(uid => (uid, message: folder.Messages[uid]))
                    .Where(item => changedSince == 0 || item.message.ModSeq > changedSince)
                    .Select(item => new FlagState(item.uid, item.message.Flags, [.. item.message.Keywords], item.message.ModSeq))];
            }
        }

        public async Task<byte[]?> GetRawAsync(string path, uint uid, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                return FolderOf(path).Messages.TryGetValue(uid, out var message) ? message.Raw : null;
            }
        }

        public async Task SetFlagsAsync(string path, IReadOnlyList<uint> uids, MessageFlags add, MessageFlags remove, IReadOnlyList<string> addKeywords,
            IReadOnlyList<string> removeKeywords, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                foreach (var uid in uids)
                {
                    if (!folder.Messages.TryGetValue(uid, out var message))
                    {
                        continue;
                    }

                    message.Flags = (message.Flags | add) & ~remove;
                    message.Keywords = [.. message.Keywords.Union(addKeywords, StringComparer.OrdinalIgnoreCase).Except(removeKeywords, StringComparer.OrdinalIgnoreCase)];
                    message.ModSeq = ++folder.HighestModSeq;
                }

                folder.Changed();
            }
        }

        public async Task<IReadOnlyList<uint>?> MoveAsync(string path, IReadOnlyList<uint> uids, string target, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var source = FolderOf(path);
                var destination = FolderOf(target);
                var result = new List<uint>();
                foreach (var uid in uids)
                {
                    if (source.Messages.Remove(uid, out var message))
                    {
                        result.Add(destination.Append(message.Raw, message.Flags & ~MessageFlags.Deleted, [.. message.Keywords]));
                    }
                }

                source.HighestModSeq++;
                source.Changed();
                destination.Changed();
                return result;
            }
        }

        public async Task ExpungeAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                foreach (var uid in uids)
                {
                    folder.Messages.Remove(uid);
                }

                folder.HighestModSeq++;
                folder.Changed();
            }
        }

        public async Task<uint?> AppendAsync(string path, byte[] raw, MessageFlags flags, DateTimeOffset date, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                var uid = folder.Append(raw, flags, []);
                folder.Changed();
                return uid;
            }
        }

        public async Task<string> CreateFolderAsync(string? parentPath, string name, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var path = parentPath is null ? name : $"{parentPath}/{name}";
                if (mailbox.Folders.ContainsKey(path))
                {
                    throw new MailConnectionException(MailErrorKind.Protocol, $"Den Ordner „{name}“ gibt es schon.");
                }

                mailbox.Add(path, FolderRole.None);
                return path;
            }
        }

        public async Task<string> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                var folder = FolderOf(path);
                var slash = path.LastIndexOf('/');
                var newPath = slash < 0 ? newName : $"{path[..slash]}/{newName}";
                mailbox.Folders.Remove(path);
                folder.Path = newPath;
                mailbox.Folders[newPath] = folder;
                return newPath;
            }
        }

        public async Task DeleteFolderAsync(string path, CancellationToken cancellationToken)
        {
            await EnterAsync(cancellationToken);
            lock (server._gate)
            {
                mailbox.Folders.Remove(path);
            }
        }

        public async Task<bool> IdleAsync(string path, TimeSpan maximum, CancellationToken stop)
        {
            await EnterAsync(stop);
            Task changed;
            lock (server._gate)
            {
                changed = FolderOf(path).WhenChanged;
            }

            var finished = await Task.WhenAny(changed, Task.Delay(maximum, stop)).ConfigureAwait(false);
            return finished == changed;
        }

        public ValueTask DisposeAsync()
        {
            _closed = true;
            return ValueTask.CompletedTask;
        }

        private async Task EnterAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            await server.DelayAsync(cancellationToken);
            server.Fault();
        }

        private Folder FolderOf(string path) =>
            mailbox.Folders.TryGetValue(path, out var folder) ? folder : throw new MailConnectionException(MailErrorKind.Protocol, $"Den Ordner „{path}“ gibt es auf dem Server nicht.");
    }

    private sealed class Smtp(SampleMailServer server) : ISmtpConnection
    {
        public async Task SendAsync(byte[] raw, MailAddress from, IReadOnlyList<MailAddress> recipients, CancellationToken cancellationToken)
        {
            await server.DelayAsync(cancellationToken);
            server.Fault();
            var refused = recipients.FirstOrDefault(r => !r.Address.Contains('@', StringComparison.Ordinal));
            if (refused is not null)
            {
                throw new MailConnectionException(MailErrorKind.Rejected, $"Der Server hat den Empfänger „{refused.Address}“ abgelehnt.");
            }

            server.Deliver(from, recipients, raw);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A plain message as bytes (tests).</summary>
    public static byte[] PlainMessage(MailAddress from, MailAddress to, string subject, string text) =>
        Encoding.UTF8.GetBytes($"From: {from.Full}\r\nTo: {to.Full}\r\nSubject: {subject}\r\nDate: {DateTimeOffset.Now:R}\r\nMessage-ID: <{Guid.NewGuid():N}@sample.example>\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{text}\r\n");
}

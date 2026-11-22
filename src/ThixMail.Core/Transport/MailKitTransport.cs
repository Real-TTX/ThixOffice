using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using MkFlags = MailKit.MessageFlags;
using MessageFlags = ThixMail.Core.Model.MessageFlags;

namespace ThixMail.Core.Transport;

/// <summary>
/// The real transport: IMAP and SMTP through MailKit. Certificates are checked by the system; one the system does not trust is accepted ONLY when the
/// account names its host and SHA-256 (the person looked at it and accepted it) – otherwise the connection fails with the certificate to show.
/// Every failure becomes a <see cref="MailConnectionException"/> with German words.
/// </summary>
public sealed class MailKitTransport : IMailTransport
{
    public static MailKitTransport Instance { get; } = new();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<IImapConnection> ConnectImapAsync(MailAccount account, string password, CancellationToken cancellationToken)
    {
        var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        var check = new CertificateCheck(account, account.Imap.Host);
        client.ServerCertificateValidationCallback = check.Validate;
        try
        {
            await client.ConnectAsync(account.Imap.Host, account.Imap.Port, Options(account.Imap.Security), cancellationToken);
            await SignInAsync(client, account, account.ImapUser, password, cancellationToken);
            return new ImapConnection(client);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw Translate(exception, check);
        }
    }

    public async Task<ISmtpConnection> ConnectSmtpAsync(MailAccount account, string password, CancellationToken cancellationToken)
    {
        var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        var check = new CertificateCheck(account, account.Smtp.Host);
        client.ServerCertificateValidationCallback = check.Validate;
        try
        {
            await client.ConnectAsync(account.Smtp.Host, account.Smtp.Port, Options(account.Smtp.Security), cancellationToken);
            if (client.Capabilities.Contains(SmtpCapability.Authentication))
            {
                await SignInAsync(client, account, account.SmtpUser.Length > 0 ? account.SmtpUser : account.ImapUser, password, cancellationToken);
            }

            return new SmtpConnection(client);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw Translate(exception, check);
        }
    }

    /// <summary>
    /// Signs in: an OAuth account with SASL XOAUTH2 (the access token; Google and Microsoft both take it), any other with the password – and then never
    /// through an OAuth mechanism MailKit would otherwise try first.
    /// </summary>
    private static async Task SignInAsync(MailService client, MailAccount account, string user, string secret, CancellationToken cancellationToken)
    {
        if (account.UsesOAuth)
        {
            await client.AuthenticateAsync(new SaslMechanismOAuth2(user, secret), cancellationToken);
            return;
        }

        client.AuthenticationMechanisms.Remove("XOAUTH2");
        client.AuthenticationMechanisms.Remove("OAUTHBEARER");
        await client.AuthenticateAsync(user, secret, cancellationToken);
    }

    private static SecureSocketOptions Options(ConnectionSecurity security) => security switch
    {
        ConnectionSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        ConnectionSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None,
    };

    /// <summary>A MailKit/.NET failure in German, with its kind.</summary>
    internal static MailConnectionException Translate(Exception exception, CertificateCheck? check = null) => exception switch
    {
        MailConnectionException mail => mail,
        SslHandshakeException or System.Security.Authentication.AuthenticationException when check?.Rejected is { } certificate =>
            new MailConnectionException(MailErrorKind.Certificate, "Das Zertifikat des Servers ist nicht vertrauenswürdig.", certificate, exception),
        MailKit.Security.AuthenticationException auth when LooksLikeAppPassword(auth.Message) =>
            new MailConnectionException(MailErrorKind.AppPasswordRequired, "Der Anbieter verlangt ein App-Passwort.", inner: exception),
        MailKit.Security.AuthenticationException => new MailConnectionException(MailErrorKind.Authentication, "Benutzername oder Kennwort stimmen nicht.", inner: exception),
        SslHandshakeException => new MailConnectionException(MailErrorKind.Network, "Die verschlüsselte Verbindung kam nicht zustande (passen Port und Verschlüsselung?).", inner: exception),
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } =>
            new MailConnectionException(MailErrorKind.Network, "Den Server gibt es nicht (der Name ist unbekannt).", inner: exception),
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
            new MailConnectionException(MailErrorKind.Network, "Der Server lehnt Verbindungen an diesem Port ab.", inner: exception),
        SocketException or IOException or TimeoutException or OperationCanceledException or ServiceNotConnectedException or ImapProtocolException or SmtpProtocolException =>
            new MailConnectionException(MailErrorKind.Network, "Der Mailserver ist nicht erreichbar.", inner: exception),
        SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } smtp =>
            new MailConnectionException(MailErrorKind.Rejected, $"Der Server hat den Empfänger „{smtp.Mailbox?.Address}“ abgelehnt.", inner: exception),
        SmtpCommandException { ErrorCode: SmtpErrorCode.MessageNotAccepted } smtp =>
            new MailConnectionException(MailErrorKind.Rejected, $"Der Server hat die Nachricht abgelehnt: {smtp.Message}", inner: exception),
        SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } =>
            new MailConnectionException(MailErrorKind.Rejected, "Der Server nimmt Nachrichten von dieser Absenderadresse nicht an.", inner: exception),
        CommandException command => new MailConnectionException(MailErrorKind.Protocol, $"Der Mailserver hat den Befehl abgelehnt: {command.Message}", inner: exception),
        FolderNotFoundException => new MailConnectionException(MailErrorKind.Protocol, "Den Ordner gibt es auf dem Server nicht (mehr).", inner: exception),
        _ => new MailConnectionException(MailErrorKind.Protocol, $"Unerwartete Antwort des Mailservers: {exception.Message}", inner: exception),
    };

    private static bool LooksLikeAppPassword(string message) =>
        message.Contains("application-specific", StringComparison.OrdinalIgnoreCase) || message.Contains("app password", StringComparison.OrdinalIgnoreCase)
        || message.Contains("app-specific", StringComparison.OrdinalIgnoreCase) || message.Contains("WebLoginRequired", StringComparison.OrdinalIgnoreCase);

    /// <summary>The certificate check of one connection: what the system trusts, or what the account accepted; the rejected one is kept to be shown.</summary>
    internal sealed class CertificateCheck(MailAccount account, string host)
    {
        public CertificateInfo? Rejected { get; private set; }

        public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            if (certificate is null)
            {
                return false;
            }

            using var x509 = new X509Certificate2(certificate);
            var thumbprint = Convert.ToHexString(SHA256.HashData(x509.RawData));
            if (account.Trusts(host, thumbprint))
            {
                return true;
            }

            var problem = errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ? $"Es ist für einen anderen Namen ausgestellt als „{host}“."
                : x509.NotAfter < DateTime.Now ? $"Es ist am {x509.NotAfter:d} abgelaufen."
                : chain?.ChainStatus.Any(s => s.Status == X509ChainStatusFlags.UntrustedRoot || s.Status == X509ChainStatusFlags.PartialChain) == true
                    ? "Keine bekannte Zertifizierungsstelle hat es ausgestellt (selbst signiert?)."
                : "Das System vertraut ihm nicht.";
            Rejected = new CertificateInfo(host, x509.Subject, x509.Issuer, x509.NotBefore, x509.NotAfter, thumbprint, problem);
            return false;
        }
    }

    // ===== IMAP =====

    private sealed class ImapConnection(ImapClient client) : IImapConnection
    {
        private ImapFolder? _open;

        public bool SupportsCondStore => client.Capabilities.Contains(ImapCapability.CondStore);

        public bool SupportsQResync => client.Capabilities.Contains(ImapCapability.QuickResync);

        public bool SupportsIdle => client.Capabilities.Contains(ImapCapability.Idle);

        public bool SupportsMove => client.Capabilities.Contains(ImapCapability.Move);

        public Task<IReadOnlyList<RemoteFolder>> ListFoldersAsync(CancellationToken cancellationToken) => Run(async () =>
        {
            var folders = new List<RemoteFolder>();
            var inbox = client.Inbox;
            folders.Add(new RemoteFolder(inbox.FullName, inbox.Name, null, inbox.DirectorySeparator, FolderRole.Inbox, true));
            foreach (var space in client.PersonalNamespaces)
            {
                foreach (var folder in await client.GetFoldersAsync(space, StatusItems.None, false, cancellationToken))
                {
                    if (folder.FullName.Equals(inbox.FullName, StringComparison.OrdinalIgnoreCase) || !folder.Exists)
                    {
                        continue;
                    }

                    var parent = folder.ParentFolder is { FullName.Length: > 0 } p ? p.FullName : null;
                    folders.Add(new RemoteFolder(folder.FullName, folder.Name, parent, folder.DirectorySeparator, RoleOf(folder.Attributes),
                        !folder.Attributes.HasFlag(FolderAttributes.NoSelect) && !folder.Attributes.HasFlag(FolderAttributes.NonExistent)));
                }
            }

            return (IReadOnlyList<RemoteFolder>)folders;
        });

        private static FolderRole RoleOf(FolderAttributes attributes) =>
            attributes.HasFlag(FolderAttributes.Inbox) ? FolderRole.Inbox
            : attributes.HasFlag(FolderAttributes.Drafts) ? FolderRole.Drafts
            : attributes.HasFlag(FolderAttributes.Sent) ? FolderRole.Sent
            : attributes.HasFlag(FolderAttributes.Trash) ? FolderRole.Trash
            : attributes.HasFlag(FolderAttributes.Junk) ? FolderRole.Junk
            : attributes.HasFlag(FolderAttributes.Archive) ? FolderRole.Archive
            : attributes.HasFlag(FolderAttributes.All) ? FolderRole.All
            : attributes.HasFlag(FolderAttributes.Flagged) ? FolderRole.Flagged
            : FolderRole.None;

        public Task<FolderState> OpenAsync(string path, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken, reopen: true);
            return new FolderState(folder.UidValidity, folder.UidNext?.Id ?? 0, folder.HighestModSeq, folder.Count, folder.Unread);
        });

        public Task<IReadOnlyList<uint>> UidsAsync(string path, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken);
            var uids = await folder.SearchAsync(SearchQuery.All, cancellationToken);
            return (IReadOnlyList<uint>)[.. uids.Select(u => u.Id)];
        });

        public Task<IReadOnlyList<RemoteMessage>> FetchAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken) => Run(async () =>
        {
            if (uids.Count == 0)
            {
                return (IReadOnlyList<RemoteMessage>)[];
            }

            var folder = await SelectAsync(path, cancellationToken);
            var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.Size
                        | MessageSummaryItems.BodyStructure | MessageSummaryItems.References | MessageSummaryItems.PreviewText | MessageSummaryItems.InternalDate;
            if (SupportsCondStore)
            {
                items |= MessageSummaryItems.ModSeq;
            }

            var summaries = await folder.FetchAsync([.. uids.Select(u => new UniqueId(folder.UidValidity, u))], items, cancellationToken);
            return (IReadOnlyList<RemoteMessage>)[.. summaries.Select(ToRemote)];
        });

        private static RemoteMessage ToRemote(IMessageSummary summary)
        {
            var envelope = summary.Envelope;
            return new RemoteMessage
            {
                Uid = summary.UniqueId.Id,
                MessageId = envelope?.MessageId ?? "",
                InReplyTo = envelope?.InReplyTo ?? "",
                References = summary.References is { } references ? [.. references] : [],
                Subject = envelope?.Subject ?? "",
                From = envelope?.From.Mailboxes.Select(MailMime.ToAddress).FirstOrDefault() ?? new MailAddress("", ""),
                To = envelope is null ? [] : MailMime.ToAddresses(envelope.To),
                Cc = envelope is null ? [] : MailMime.ToAddresses(envelope.Cc),
                ReplyTo = envelope is null ? [] : MailMime.ToAddresses(envelope.ReplyTo),
                Date = envelope?.Date ?? summary.InternalDate ?? DateTimeOffset.Now,
                Size = summary.Size ?? 0,
                Flags = FromMailKit(summary.Flags ?? MkFlags.None, summary.Keywords),
                Keywords = summary.Keywords is { } keywords ? [.. keywords] : [],
                HasAttachments = summary.Body is not null && summary.Attachments.Any(),
                Preview = MailMime.Shorten(summary.PreviewText ?? "", 200),
                ModSeq = summary.ModSeq ?? 0,
            };
        }

        public Task<IReadOnlyList<FlagState>> FlagsAsync(string path, IReadOnlyList<uint> uids, ulong changedSince, CancellationToken cancellationToken) => Run(async () =>
        {
            if (uids.Count == 0)
            {
                return (IReadOnlyList<FlagState>)[];
            }

            var folder = await SelectAsync(path, cancellationToken);
            var list = uids.Select(u => new UniqueId(folder.UidValidity, u)).ToList();
            var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Flags | (SupportsCondStore ? MessageSummaryItems.ModSeq : 0);
            var summaries = changedSince > 0 && SupportsCondStore
                ? await folder.FetchAsync(list, changedSince, items, cancellationToken)
                : await folder.FetchAsync(list, items, cancellationToken);
            return (IReadOnlyList<FlagState>)[.. summaries.Select(s => new FlagState(s.UniqueId.Id, FromMailKit(s.Flags ?? MkFlags.None, s.Keywords),
                s.Keywords is { } k ? [.. k] : [], s.ModSeq ?? 0))];
        });

        public Task<byte[]?> GetRawAsync(string path, uint uid, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken);
            try
            {
                await using var stream = await folder.GetStreamAsync(new UniqueId(folder.UidValidity, uid), "", cancellationToken);
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, cancellationToken);
                return (byte[]?)copy.ToArray();
            }
            catch (MessageNotFoundException)
            {
                return null;
            }
        });

        public Task SetFlagsAsync(string path, IReadOnlyList<uint> uids, MessageFlags add, MessageFlags remove, IReadOnlyList<string> addKeywords,
            IReadOnlyList<string> removeKeywords, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken);
            var list = uids.Select(u => new UniqueId(folder.UidValidity, u)).ToList();
            if (add != MessageFlags.None || addKeywords.Count > 0)
            {
                await folder.AddFlagsAsync(list, ToMailKit(add), [.. addKeywords, .. ForwardedKeyword(add)], true, cancellationToken);
            }

            if (remove != MessageFlags.None || removeKeywords.Count > 0)
            {
                await folder.RemoveFlagsAsync(list, ToMailKit(remove), [.. removeKeywords, .. ForwardedKeyword(remove)], true, cancellationToken);
            }

            return true;
        });

        public Task<IReadOnlyList<uint>?> MoveAsync(string path, IReadOnlyList<uint> uids, string target, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken);
            var destination = await client.GetFolderAsync(target, cancellationToken);
            var list = uids.Select(u => new UniqueId(folder.UidValidity, u)).ToList();
            UniqueIdMap map;
            if (SupportsMove)
            {
                map = await folder.MoveToAsync(list, destination, cancellationToken);
            }
            else
            {
                map = await folder.CopyToAsync(list, destination, cancellationToken);
                await folder.AddFlagsAsync(list, MkFlags.Deleted, true, cancellationToken);
                await ExpungeUidsAsync(folder, list, cancellationToken);
            }

            return map.Destination.Count == list.Count ? (IReadOnlyList<uint>?)[.. map.Destination.Select(u => u.Id)] : null;
        });

        public Task ExpungeAsync(string path, IReadOnlyList<uint> uids, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await SelectAsync(path, cancellationToken);
            var list = uids.Select(u => new UniqueId(folder.UidValidity, u)).ToList();
            await folder.AddFlagsAsync(list, MkFlags.Deleted, true, cancellationToken);
            await ExpungeUidsAsync(folder, list, cancellationToken);
            return true;
        });

        private async Task ExpungeUidsAsync(ImapFolder folder, IList<UniqueId> list, CancellationToken cancellationToken)
        {
            if (client.Capabilities.Contains(ImapCapability.UidPlus))
            {
                await folder.ExpungeAsync(list, cancellationToken);
            }
            else
            {
                await folder.ExpungeAsync(cancellationToken);
            }
        }

        public Task<uint?> AppendAsync(string path, byte[] raw, MessageFlags flags, DateTimeOffset date, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = (ImapFolder)await client.GetFolderAsync(path, cancellationToken);
            var message = MailMime.Parse(raw);
            var uid = await folder.AppendAsync(message, ToMailKit(flags), date, cancellationToken);
            _open = null; // APPEND may leave the selected folder as it was; select again next time to be sure
            return uid?.Id;
        });

        public Task<string> CreateFolderAsync(string? parentPath, string name, CancellationToken cancellationToken) => Run(async () =>
        {
            var parent = parentPath is null ? client.GetFolder(client.PersonalNamespaces[0]) : await client.GetFolderAsync(parentPath, cancellationToken);
            var created = await parent.CreateAsync(name, true, cancellationToken)
                          ?? throw new MailConnectionException(MailErrorKind.Protocol, $"Der Ordner „{name}“ konnte nicht angelegt werden.");
            await created.SubscribeAsync(cancellationToken);
            return created.FullName;
        });

        public Task<string> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await client.GetFolderAsync(path, cancellationToken);
            if (_open is not null && ReferenceEquals(_open, folder))
            {
                await folder.CloseAsync(false, cancellationToken);
                _open = null;
            }

            await folder.RenameAsync(folder.ParentFolder ?? client.GetFolder(client.PersonalNamespaces[0]), newName, cancellationToken);
            return folder.FullName;
        });

        public Task DeleteFolderAsync(string path, CancellationToken cancellationToken) => Run(async () =>
        {
            var folder = await client.GetFolderAsync(path, cancellationToken);
            if (_open is not null && _open.FullName == folder.FullName)
            {
                // select another folder first (some servers drop the connection when the selected folder is deleted or closed and deleted)
                await SelectAsync(client.Inbox.FullName, cancellationToken, reopen: true);
            }

            if (folder.IsSubscribed)
            {
                await folder.UnsubscribeAsync(cancellationToken);
            }

            await folder.DeleteAsync(cancellationToken);
            return true;
        });

        public async Task<bool> IdleAsync(string path, TimeSpan maximum, CancellationToken stop)
        {
            try
            {
                var folder = await SelectAsync(path, stop);
                var changed = false;
                using var done = CancellationTokenSource.CreateLinkedTokenSource(stop);
                done.CancelAfter(maximum);
                void OnChange(object? sender, EventArgs e)
                {
                    changed = true;
                    done.Cancel();
                }

                folder.CountChanged += OnChange;
                folder.MessageExpunged += OnChange;
                folder.MessageFlagsChanged += OnChange;
                try
                {
                    if (SupportsIdle)
                    {
                        await client.IdleAsync(done.Token, stop);
                    }
                    else
                    {
                        while (!done.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30), done.Token).ContinueWith(_ => { }, TaskScheduler.Default);
                            if (!done.IsCancellationRequested)
                            {
                                await client.NoOpAsync(stop);
                            }
                        }
                    }
                }
                finally
                {
                    folder.CountChanged -= OnChange;
                    folder.MessageExpunged -= OnChange;
                    folder.MessageFlagsChanged -= OnChange;
                }

                return changed;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Translate(exception);
            }
        }

        private async Task<ImapFolder> SelectAsync(string path, CancellationToken cancellationToken, bool reopen = false)
        {
            if (!reopen && _open is { IsOpen: true } open && open.FullName == path)
            {
                return open;
            }

            var folder = (ImapFolder)await client.GetFolderAsync(path, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            _open = folder;
            return folder;
        }

        private static async Task<T> Run<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not MailConnectionException)
            {
                throw Translate(exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.DisconnectAsync(true, timeout.Token);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ImapProtocolException or ServiceNotConnectedException)
            {
            }

            client.Dispose();
        }
    }

    private static MessageFlags FromMailKit(MkFlags flags, IEnumerable<string>? keywords) =>
        FromMailKitCore(flags, keywords?.Contains("$Forwarded", StringComparer.OrdinalIgnoreCase) == true);

    private static MessageFlags FromMailKitCore(MkFlags flags, bool forwarded)
    {
        var result = MessageFlags.None;
        if (flags.HasFlag(MkFlags.Seen))
        {
            result |= MessageFlags.Seen;
        }

        if (flags.HasFlag(MkFlags.Answered))
        {
            result |= MessageFlags.Answered;
        }

        if (flags.HasFlag(MkFlags.Flagged))
        {
            result |= MessageFlags.Flagged;
        }

        if (flags.HasFlag(MkFlags.Deleted))
        {
            result |= MessageFlags.Deleted;
        }

        if (flags.HasFlag(MkFlags.Draft))
        {
            result |= MessageFlags.Draft;
        }

        return forwarded ? result | MessageFlags.Forwarded : result;
    }

    private static MkFlags ToMailKit(MessageFlags flags)
    {
        var result = MkFlags.None;
        if (flags.HasFlag(MessageFlags.Seen))
        {
            result |= MkFlags.Seen;
        }

        if (flags.HasFlag(MessageFlags.Answered))
        {
            result |= MkFlags.Answered;
        }

        if (flags.HasFlag(MessageFlags.Flagged))
        {
            result |= MkFlags.Flagged;
        }

        if (flags.HasFlag(MessageFlags.Deleted))
        {
            result |= MkFlags.Deleted;
        }

        if (flags.HasFlag(MessageFlags.Draft))
        {
            result |= MkFlags.Draft;
        }

        return result;
    }

    private static IEnumerable<string> ForwardedKeyword(MessageFlags flags) => flags.HasFlag(MessageFlags.Forwarded) ? ["$Forwarded"] : [];

    // ===== SMTP =====

    private sealed class SmtpConnection(SmtpClient client) : ISmtpConnection
    {
        public async Task SendAsync(byte[] raw, MailAddress from, IReadOnlyList<MailAddress> recipients, CancellationToken cancellationToken)
        {
            try
            {
                var message = MailMime.Parse(raw);
                await client.SendAsync(message, MailMime.ToMailbox(from), recipients.Select(MailMime.ToMailbox), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Translate(exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.DisconnectAsync(true, timeout.Token);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or SmtpProtocolException or ServiceNotConnectedException)
            {
            }

            client.Dispose();
        }
    }
}

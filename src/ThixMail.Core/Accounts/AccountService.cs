using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixMail.Core.Accounts;

/// <summary>The outcome of a connection test: fine, or a German explanation with its kind (and the certificate to show when that is the problem).</summary>
public sealed record ConnectionTestResult(bool Succeeded, string Message, MailErrorKind? Kind = null, CertificateInfo? Certificate = null, int Folders = 0)
{
    public static ConnectionTestResult Success(int folders) =>
        new(true, $"Verbindung hergestellt: Posteingang und Postausgang funktionieren ({folders} Ordner).", Folders: folders);
}

/// <summary>
/// The business rules of accounts (static, like every service of the suite): validation of an account's fields (errors at their field), the passwords
/// – ONLY in the TokenStore (DPAPI on Windows, the Keychain on macOS; the store keeps the protected value locally, never in the settings that may go to
/// ThixCloud) –, the connection test that must pass before an account is saved (IMAP: sign in, list the folders; SMTP: sign in), with clear German
/// errors (wrong password, an app password needed, a certificate problem – shown, never accepted silently), and accepting a shown certificate.
/// </summary>
public static class AccountService
{
    public const string ImapSecret = "imap";

    public const string SmtpSecret = "smtp";

    /// <summary>Checks the fields of an account; the first problem as a <see cref="BusinessException"/> at its field.</summary>
    public static MailAccount Validate(MailAccount account)
    {
        var address = account.Address.Trim();
        if (address.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie die E-Mail-Adresse an.", nameof(MailAccount.Address));
        }

        if (!IsAddress(address))
        {
            throw new BusinessException("Das ist keine gültige E-Mail-Adresse (z. B. anna@example.de).", nameof(MailAccount.Address));
        }

        CheckServer(account.Imap, "Posteingangsserver (IMAP)", nameof(MailAccount.Imap));
        CheckServer(account.Smtp, "Postausgangsserver (SMTP)", nameof(MailAccount.Smtp));
        if (account.ImapUser.Trim().Length == 0)
        {
            throw new BusinessException("Bitte geben Sie den Benutzernamen für den Posteingangsserver an.", nameof(MailAccount.ImapUser));
        }

        return account with
        {
            Address = address,
            Name = account.Name.Trim(),
            SenderName = account.SenderName.Trim(),
            ImapUser = account.ImapUser.Trim(),
            SmtpUser = account.SmtpUser.Trim().Length > 0 ? account.SmtpUser.Trim() : account.ImapUser.Trim(),
            Imap = account.Imap with { Host = account.Imap.Host.Trim().ToLowerInvariant() },
            Smtp = account.Smtp with { Host = account.Smtp.Host.Trim().ToLowerInvariant() },
        };
    }

    /// <summary>A plausible address: one @, a local part, a domain with a dot, no spaces.</summary>
    public static bool IsAddress(string address)
    {
        var at = address.IndexOf('@');
        return at > 0 && at == address.LastIndexOf('@') && at < address.Length - 3 && address[(at + 1)..].Contains('.', StringComparison.Ordinal)
               && !address.Any(char.IsWhiteSpace) && !address.EndsWith('.');
    }

    private static void CheckServer(ServerSettings server, string label, string field)
    {
        if (server.Host.Trim().Length == 0)
        {
            throw new BusinessException($"Bitte geben Sie den {label} an.", field);
        }

        if (Uri.CheckHostName(server.Host.Trim()) == UriHostNameType.Unknown)
        {
            throw new BusinessException($"„{server.Host}“ ist kein gültiger Servername.", field);
        }

        if (server.Port is < 1 or > 65535)
        {
            throw new BusinessException($"Der Port des {label}s muss zwischen 1 und 65535 liegen.", field);
        }
    }

    // ===== passwords =====

    /// <summary>Keeps a password of the account in the TokenStore (empty = forget it).</summary>
    public static void SetPassword(AccountStore store, string accountId, string kind, string password)
    {
        var key = TokenKey(accountId, kind);
        if (password.Length == 0)
        {
            TokenStore.Delete(key);
            store.SetProtectedSecret(accountId, kind, "");
            return;
        }

        store.SetProtectedSecret(accountId, kind, TokenStore.Save(key, password));
    }

    /// <summary>The password (the SMTP one falls back to the IMAP one when the account says so); null when none is stored.</summary>
    public static string? Password(AccountStore store, MailAccount account, string kind)
    {
        if (kind == SmtpSecret && account.SmtpUsesImapPassword)
        {
            kind = ImapSecret;
        }

        return store.ProtectedSecret(account.Id, kind) is { } value ? TokenStore.Read(TokenKey(account.Id, kind), value) : null;
    }

    /// <summary>The account's passwords go (the account was removed).</summary>
    public static void ForgetPasswords(AccountStore store, string accountId)
    {
        SetPassword(store, accountId, ImapSecret, "");
        SetPassword(store, accountId, SmtpSecret, "");
    }

    private static string TokenKey(string accountId, string kind) => $"mail-{accountId}-{kind}";

    // ===== the connection test =====

    /// <summary>Signs in at both servers (nothing is sent, nothing changed) and explains a failure.</summary>
    public static async Task<ConnectionTestResult> TestAsync(IMailTransport transport, MailAccount account, string imapPassword, string smtpPassword,
        CancellationToken cancellationToken)
    {
        int folders;
        try
        {
            await using var imap = await transport.ConnectImapAsync(account, imapPassword, cancellationToken);
            folders = (await imap.ListFoldersAsync(cancellationToken)).Count;
        }
        catch (MailConnectionException exception)
        {
            return Explain(exception, account, "Posteingangsserver");
        }

        try
        {
            await using var smtp = await transport.ConnectSmtpAsync(account, smtpPassword, cancellationToken);
        }
        catch (MailConnectionException exception)
        {
            return Explain(exception, account, "Postausgangsserver");
        }

        return ConnectionTestResult.Success(folders);
    }

    /// <summary>A failure in the words of the account page; for a provider that needs an app password the wrong-password message says so.</summary>
    public static ConnectionTestResult Explain(MailConnectionException exception, MailAccount account, string server)
    {
        var provider = AutoConfig.ProviderOf(account.Address);
        return exception.Kind switch
        {
            MailErrorKind.Authentication when provider is { NeedsAppPassword: true } =>
                new(false, $"{server}: Die Anmeldung wurde abgelehnt. {provider.Name} verlangt ein App-Passwort statt Ihres normalen Kennworts. {provider.Hint}", MailErrorKind.AppPasswordRequired),
            MailErrorKind.Authentication => new(false, $"{server}: Benutzername oder Kennwort stimmen nicht.", MailErrorKind.Authentication),
            MailErrorKind.AppPasswordRequired => new(false, $"{server}: {exception.Message} {provider?.Hint}".Trim(), MailErrorKind.AppPasswordRequired),
            MailErrorKind.Certificate => new(false, $"{server}: Das Zertifikat des Servers ist nicht vertrauenswürdig. {exception.Certificate?.Problem}".Trim(),
                MailErrorKind.Certificate, exception.Certificate),
            MailErrorKind.Network => new(false, $"{server}: {exception.Message} Prüfen Sie den Servernamen, den Port und die Internetverbindung.", MailErrorKind.Network),
            _ => new(false, $"{server}: {exception.Message}", exception.Kind),
        };
    }

    /// <summary>The person looked at the certificate and accepts it for this host (stored as host + SHA-256; another certificate is asked again).</summary>
    public static MailAccount TrustCertificate(MailAccount account, CertificateInfo certificate)
    {
        var entry = $"{certificate.Host.ToLowerInvariant()}|{certificate.Thumbprint.ToUpperInvariant()}";
        return account.TrustedCertificates.Contains(entry, StringComparer.Ordinal) ? account : account with { TrustedCertificates = [.. account.TrustedCertificates, entry] };
    }
}

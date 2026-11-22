using System.Text.Json;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixOffice.Pim.Store;

namespace ThixMail.Core.Accounts;

/// <summary>
/// The settings of ThixMail that may one day travel through ThixCloud's general sync – the accounts WITHOUT passwords, the signatures and the categories –
/// kept as items of collections (<see cref="AccountsCollection"/>, <see cref="SignaturesCollection"/>, <see cref="CategoriesCollection"/>) in a
/// <see cref="PimStore"/> of this computer (dirty items, ready to go up when the mail module of the cloud takes them; mail content never goes there).
/// The protected passwords are NOT items: they are the TokenStore's values, kept in the store's local meta values only.
/// </summary>
public sealed class AccountStore : IDisposable
{
    public const string AccountsCollection = "mail-accounts";

    public const string SignaturesCollection = "mail-signatures";

    public const string CategoriesCollection = "mail-categories";

    public const string RulesCollection = "mail-rules";

    public const string TemplatesCollection = "mail-templates";

    private readonly PimStore _store;

    public AccountStore(PimStore store)
    {
        _store = store;
        MoveTrustToThisComputer();
        _store.RemoteChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty); // what ThixCloud brought (another computer's settings)
    }

    /// <summary>The store under it – the one ThixCloud's general sync keeps in step (app <c>mail</c>; never the protected secrets, they are meta values).</summary>
    internal PimStore Store => _store;

    /// <summary>Raised after an account, a signature or a category changed.</summary>
    public event EventHandler? Changed;

    public static AccountStore Open(string path) => new(PimStore.Open(path));

    /// <summary>The accounts in the order of the folder pane.</summary>
    /// <summary>
    /// The accounts in the order of the folder pane – each with the certificates accepted on THIS computer (<see cref="MailAccount.TrustedCertificates"/>
    /// is a local meta value, never part of the item: what another computer accepted is not trusted here).
    /// </summary>
    public IReadOnlyList<MailAccount> Accounts =>
        [.. _store.Items(AccountsCollection).Select(i => Read(i.Data, MailJson.Default.MailAccount)).Where(a => a is not null)
            .Select(a => Complete(a!) with { TrustedCertificates = TrustOf(a!.Id) }).Where(a => a.Id.Length > 0).OrderBy(a => a.Order).ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public MailAccount? Account(string id) => Accounts.FirstOrDefault(a => a.Id == id);

    /// <summary>An item of another build (or another computer) may lack fields: they are empty, never null.</summary>
    private static MailAccount Complete(MailAccount account) => account with
    {
        Id = account.Id ?? "",
        Name = account.Name ?? "",
        SenderName = account.SenderName ?? "",
        Address = account.Address ?? "",
        ImapUser = account.ImapUser ?? "",
        SmtpUser = account.SmtpUser ?? "",
        Imap = account.Imap ?? ServerSettings.Empty,
        Smtp = account.Smtp ?? ServerSettings.Empty,
    };

    /// <summary>Keeps an account: the item (what travels, without accepted certificates) and the certificates accepted here (local).</summary>
    public void Save(MailAccount account)
    {
        _store.SetMeta(TrustKey(account.Id), string.Join('\n', account.TrustedCertificates));
        _store.Write([(AccountsCollection, account.Id, JsonSerializer.Serialize(account with { TrustedCertificates = [] }, MailJson.Default.MailAccount))]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string accountId)
    {
        _store.Write([(AccountsCollection, accountId, null)]);
        foreach (var kind in new[] { "imap", "smtp" })
        {
            _store.SetMeta(SecretKey(accountId, kind), "");
        }

        _store.SetMeta(TrustKey(accountId), "");
        _store.SetMeta(ConfirmedKey(accountId), "");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- what only this computer decided: accepted certificates, confirmed server settings ----

    /// <summary>
    /// True when the account's servers (hosts, ports, protection, user names, the way it signs in) are the ones confirmed on THIS computer by a
    /// connection test – added or saved here. An account that came from another computer, or whose servers another computer changed, is not: its sync
    /// does not start and no password of this computer goes to its servers until the person runs the connection test (Konten › Bearbeiten).
    /// </summary>
    public bool IsConfirmed(MailAccount account) => _store.GetMeta(ConfirmedKey(account.Id)) == ServerFingerprint(account);

    /// <summary>The connection test passed for these servers on this computer (AddAccount / UpdateAccount after the form's test).</summary>
    public void Confirm(MailAccount account) => _store.SetMeta(ConfirmedKey(account.Id), ServerFingerprint(account));

    private static string ServerFingerprint(MailAccount account) =>
        string.Join('|', account.Imap.Host.ToLowerInvariant(), account.Imap.Port, account.Imap.Security, account.ImapUser,
            account.Smtp.Host.ToLowerInvariant(), account.Smtp.Port, account.Smtp.Security, account.SmtpUser, account.SmtpUsesImapPassword,
            account.Authentication, account.OAuthProvider ?? "");

    private IReadOnlyList<string> TrustOf(string accountId) =>
        _store.GetMeta(TrustKey(accountId)) is { Length: > 0 } trust ? trust.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];

    private static string TrustKey(string accountId) => $"trust:{accountId}";

    private static string ConfirmedKey(string accountId) => $"confirmed:{accountId}";

    /// <summary>
    /// Once per store (builds before 7d kept accepted certificates in the item, and had no confirmation): the certificates of the accounts here move into
    /// this computer's meta values and leave the items (they go up without them), and the accounts already here count as confirmed.
    /// </summary>
    private void MoveTrustToThisComputer()
    {
        if (_store.GetMeta("trustIsLocal") == "1")
        {
            return;
        }

        foreach (var item in _store.Items(AccountsCollection))
        {
            if (Read(item.Data, MailJson.Default.MailAccount) is not { } account)
            {
                continue;
            }

            Confirm(account);
            if (account.TrustedCertificates.Count > 0)
            {
                _store.SetMeta(TrustKey(account.Id), string.Join('\n', account.TrustedCertificates));
                _store.Write([(AccountsCollection, account.Id, JsonSerializer.Serialize(account with { TrustedCertificates = [] }, MailJson.Default.MailAccount))]);
            }
        }

        _store.SetMeta("trustIsLocal", "1");
    }

    public IReadOnlyList<MailSignature> Signatures =>
        [.. _store.Items(SignaturesCollection).Select(i => Read(i.Data, MailJson.Default.MailSignature)).Where(s => s is not null).Select(s => s!).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)];

    public MailSignature? Signature(string? id) => id is null ? null : Signatures.FirstOrDefault(s => s.Id == id);

    public void Save(MailSignature signature)
    {
        _store.Write([(SignaturesCollection, signature.Id, JsonSerializer.Serialize(signature, MailJson.Default.MailSignature))]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSignature(string id)
    {
        _store.Write([(SignaturesCollection, id, null)]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<MailCategory> Categories =>
        [.. _store.Items(CategoriesCollection).Select(i => Read(i.Data, MailJson.Default.MailCategory)).Where(c => c is not null).Select(c => c!).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];

    public void Save(MailCategory category)
    {
        _store.Write([(CategoriesCollection, category.Name.ToLowerInvariant(), JsonSerializer.Serialize(category, MailJson.Default.MailCategory))]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveCategory(string name)
    {
        _store.Write([(CategoriesCollection, name.ToLowerInvariant(), null)]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The templates by name.</summary>
    public IReadOnlyList<MailTemplate> Templates =>
        [.. _store.Items(TemplatesCollection).Select(i => Read(i.Data, MailJson.Default.MailTemplate)).Where(t => t is not null).Select(t => t!).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Keeps a template (a name given twice replaces the older one: Outlook's "Speichern unter" a template's name).</summary>
    public MailTemplate SaveTemplate(string name, string subject, string html, string text, IReadOnlyList<MailAddress>? to = null, IReadOnlyList<MailAddress>? cc = null,
        IReadOnlyList<Mime.DraftAttachment>? attachments = null)
    {
        var clean = name.Trim();
        if (clean.Length == 0)
        {
            throw new ThixOffice.Core.Models.BusinessException("Bitte geben Sie der Vorlage einen Namen.", "Name");
        }

        var id = Templates.FirstOrDefault(t => t.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase))?.Id ?? Guid.NewGuid().ToString("N");
        var template = new MailTemplate(id, clean, subject, html, text, to is { Count: > 0 } ? to : null, cc is { Count: > 0 } ? cc : null);
        _store.Write([(TemplatesCollection, id, JsonSerializer.Serialize(template, MailJson.Default.MailTemplate))]);
        // the attachments stay on this computer (meta values never travel): files can be large and private
        var kept = (attachments ?? []).Where(a => a.ContentId is null).Select(a => new TemplateAttachment(a.FileName, a.ContentType, Convert.ToBase64String(a.Content))).ToList();
        _store.SetMeta(TemplateAttachmentsKey(id), kept.Count == 0 ? "" : JsonSerializer.Serialize(kept, MailJson.Default.ListTemplateAttachment));
        Changed?.Invoke(this, EventArgs.Empty);
        return template;
    }

    /// <summary>The attachments a template was saved with on THIS computer (none on another computer, which gets the template through ThixCloud).</summary>
    public IReadOnlyList<Mime.DraftAttachment> TemplateAttachments(string id)
    {
        if (_store.GetMeta(TemplateAttachmentsKey(id)) is not { Length: > 0 } json)
        {
            return [];
        }

        try
        {
            return [.. (JsonSerializer.Deserialize(json, MailJson.Default.ListTemplateAttachment) ?? []).Select(a => new Mime.DraftAttachment(a.FileName, a.ContentType, Convert.FromBase64String(a.Content)))];
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return [];
        }
    }

    private static string TemplateAttachmentsKey(string id) => "template-attachments:" + id;

    public void RemoveTemplate(string id)
    {
        _store.Write([(TemplatesCollection, id, null)]);
        _store.SetMeta(TemplateAttachmentsKey(id), "");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The rules in their order.</summary>
    public IReadOnlyList<MailRule> Rules =>
        [.. _store.Items(RulesCollection).Select(i => Read(i.Data, MailJson.Default.MailRule)).Where(r => r is not null).Select(r => r!).OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];

    public void Save(MailRule rule)
    {
        _store.Write([(RulesCollection, rule.Id, JsonSerializer.Serialize(rule, MailJson.Default.MailRule))]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRule(string id)
    {
        _store.Write([(RulesCollection, id, null)]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How many settings wait to go to ThixCloud (prepared: nothing sends them yet).</summary>
    public int PendingCloudChanges => _store.DirtyCount;

    // ---- protected passwords (local only) ----

    internal string? ProtectedSecret(string accountId, string kind) => _store.GetMeta(SecretKey(accountId, kind)) is { Length: > 0 } value ? value : null;

    internal void SetProtectedSecret(string accountId, string kind, string value) => _store.SetMeta(SecretKey(accountId, kind), value);

    private static string SecretKey(string accountId, string kind) => $"secret:{accountId}:{kind}";

    private static T? Read<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _store.Dispose();
}

/// <summary>An attachment of a template as kept in the local meta values (base64).</summary>
public sealed record TemplateAttachment(string FileName, string ContentType, string Content);

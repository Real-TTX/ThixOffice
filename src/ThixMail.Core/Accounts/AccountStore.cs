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

    private readonly PimStore _store;

    public AccountStore(PimStore store)
    {
        _store = store;
    }

    /// <summary>Raised after an account, a signature or a category changed.</summary>
    public event EventHandler? Changed;

    public static AccountStore Open(string path) => new(PimStore.Open(path));

    /// <summary>The accounts in the order of the folder pane.</summary>
    public IReadOnlyList<MailAccount> Accounts =>
        [.. _store.Items(AccountsCollection).Select(i => Read(i.Data, MailJson.Default.MailAccount)).Where(a => a is not null).Select(a => a!).OrderBy(a => a.Order).ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public MailAccount? Account(string id) => Accounts.FirstOrDefault(a => a.Id == id);

    public void Save(MailAccount account)
    {
        _store.Write([(AccountsCollection, account.Id, JsonSerializer.Serialize(account, MailJson.Default.MailAccount))]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string accountId)
    {
        _store.Write([(AccountsCollection, accountId, null)]);
        foreach (var kind in new[] { "imap", "smtp" })
        {
            _store.SetMeta(SecretKey(accountId, kind), "");
        }

        Changed?.Invoke(this, EventArgs.Empty);
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

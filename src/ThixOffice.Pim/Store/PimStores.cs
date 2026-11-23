using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Pim.Store;

/// <summary>
/// Where the stores of a PIM app live: one SQLite file per app and account in a data folder (the app's own, or the folder of the app that owns the data –
/// ThixMail keeps its contacts in ThixContacts' folder, so both apps show the same address book) – <c>&lt;stem&gt;-local.db</c> while the app is not signed
/// in (it works only on this computer), <c>&lt;stem&gt;-&lt;hash of server and user&gt;.db</c> for an account of ThixCloud.
/// <para>
/// Local → account: whenever an account's store is opened, what this computer kept locally and the account does not have yet goes into it as changes of
/// this computer (<see cref="MergeLocalInto"/>: an item the account has no record of, or a newer local version; each item keeps its time). Account → local:
/// "Abmelden" with a copy (<see cref="KeepLocalCopy"/>) first merges the local store into the account, then makes the local store a copy of the account –
/// nothing that was only local is lost, nothing is doubled (the items keep their keys).
/// </para>
/// </summary>
public static class PimStores
{
    /// <summary>
    /// The file of the store of <paramref name="stem"/> ("contacts") for the account of the app (<see cref="CloudService.HasAccount"/>: signed in, or
    /// signed out by the cloud – the account's data stays until the person signs out), or the local one; in <paramref name="directory"/> (default: the
    /// app's data folder).
    /// </summary>
    public static string PathFor(string stem, string? directory = null)
    {
        var cloud = CloudService.Settings;
        return CloudService.HasAccount
            ? Path.Combine(directory ?? AppPaths.DataDirectory, $"{stem}-{AccountHash(cloud.ServerUrl, cloud.UserName)}.db")
            : LocalPath(stem, directory);
    }

    public static string LocalPath(string stem, string? directory = null) => Path.Combine(directory ?? AppPaths.DataDirectory, $"{stem}-local.db");

    /// <summary>
    /// Opens the store of the current account (or the local one). With <paramref name="mergeLocal"/> an account's store takes what the local store has
    /// and the account lacks (<see cref="MergeLocalInto"/>) – the contacts; the calendar merges itself after its first sync (its own calendars are mapped).
    /// </summary>
    public static PimStore Open(string stem, string? directory = null, bool mergeLocal = true)
    {
        var path = PathFor(stem, directory);
        var local = LocalPath(stem, directory);
        var store = PimStore.Open(path);
        if (mergeLocal && !string.Equals(path, local, StringComparison.OrdinalIgnoreCase) && File.Exists(local))
        {
            using var source = PimStore.Open(local);
            var merged = MergeLocalInto(source, store);
            if (merged > 0)
            {
                Log.Info($"{merged} local {stem} items went into the account's store");
            }
        }

        return store;
    }

    /// <summary>
    /// The local items the account lacks go into <paramref name="account"/> as changes of this computer, keeping their time: an item counts when it changed
    /// locally since the last merge into this account (meta <c>merged:&lt;account file&gt;</c> of the local store) and the account has no record of its key
    /// (not even a tombstone) or an older one. Pictures named by them come along. Answers how many items went in.
    /// </summary>
    public static int MergeLocalInto(PimStore local, PimStore account)
    {
        var mark = MergeMark(account);
        var since = long.TryParse(local.GetMeta(mark), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;
        var items = local.AllItems();
        var known = account.AllItems(withDeleted: true).ToDictionary(item => (item.Collection, item.Key), item => item.UpdatedAt);
        var missing = items
            .Where(item => item.UpdatedAt > since)
            .Where(item => !known.TryGetValue((item.Collection, item.Key), out var there) || there < item.UpdatedAt)
            .ToList();
        if (missing.Count > 0)
        {
            account.Import(missing);
            foreach (var (id, bytes) in local.AllPhotos())
            {
                if (!account.HasPhoto(id))
                {
                    account.PutPhoto(id, bytes, isUploaded: false);
                }
            }
        }

        if (items.Count > 0)
        {
            local.SetMeta(mark, items.Max(item => item.UpdatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        return missing.Count;
    }

    /// <summary>
    /// "Abmelden" with a local copy: the local store (in <paramref name="directory"/>) takes what only it had into the account first, then becomes a copy of
    /// <paramref name="account"/> – every live item (<paramref name="filter"/>: those to keep) and picture as data of this computer, with their times. The
    /// file stays in place (another app may have it open; it sees the change).
    /// </summary>
    public static void KeepLocalCopy(string stem, PimStore account, string? directory = null, Func<StoredItem, bool>? filter = null, bool mergeFirst = true)
    {
        using var local = PimStore.Open(LocalPath(stem, directory));
        if (mergeFirst)
        {
            MergeLocalInto(local, account);
        }

        var items = account.AllItems().Where(item => filter?.Invoke(item) ?? true).ToList();
        local.ReplaceContent(items, account.AllPhotos());
        if (items.Count > 0)
        {
            local.SetMeta(MergeMark(account), items.Max(item => item.UpdatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        Log.Info($"The account's {stem} were kept as the local store");
    }

    /// <summary>16 hex digits of the SHA-256 of server and user (no name in a file name).</summary>
    public static string AccountHash(string serverUrl, string userName)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serverUrl.TrimEnd('/').ToLowerInvariant() + "|" + userName.ToLowerInvariant())))[..16];

    private static string MergeMark(PimStore account) => "merged:" + Path.GetFileNameWithoutExtension(account.Path);
}

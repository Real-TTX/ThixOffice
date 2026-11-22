using System.Security.Cryptography;
using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Pim.Store;

/// <summary>
/// Where the stores of a PIM app live: one SQLite file per app and account in the data folder – <c>&lt;stem&gt;-local.db</c> while the app is not signed
/// in (it works only on this computer), <c>&lt;stem&gt;-&lt;hash of server and user&gt;.db</c> for an account of ThixCloud. The first time an account's
/// store is opened, what this computer kept locally goes into it as its own changes (they go up with the first sync); the local store stays as it was.
/// </summary>
public static class PimStores
{
    /// <summary>
    /// The file of the store of <paramref name="stem"/> ("contacts") for the account of the app (<see cref="CloudService.HasAccount"/>: signed in, or
    /// signed out by the cloud – the account's data stays until the person signs out), or the local one.
    /// </summary>
    public static string PathFor(string stem)
    {
        var cloud = CloudService.Settings;
        return CloudService.HasAccount
            ? Path.Combine(AppPaths.DataDirectory, $"{stem}-{AccountHash(cloud.ServerUrl, cloud.UserName)}.db")
            : LocalPath(stem);
    }

    /// <summary>
    /// "Abmelden" with a local copy: the local store becomes a copy of <paramref name="source"/> (the account's), every item and picture as data of this
    /// computer. Written into a new file first, then put in place (the old local store is replaced only when the copy is complete).
    /// </summary>
    public static void ReplaceLocalWith(string stem, PimStore source)
    {
        var local = LocalPath(stem);
        var temporary = local + ".new";
        DeleteDatabase(temporary);
        using (var target = PimStore.Open(temporary))
        {
            CopyInto(source, target);
        }

        DeleteDatabase(local);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(temporary + suffix))
            {
                File.Move(temporary + suffix, local + suffix);
            }
        }

        Log.Info($"The account's {stem} were kept as the local store");
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    public static string LocalPath(string stem) => Path.Combine(AppPaths.DataDirectory, $"{stem}-local.db");

    /// <summary>Opens the store of the current account (or the local one); a new account store starts with the local data as changes of this computer.</summary>
    public static PimStore Open(string stem)
    {
        var path = PathFor(stem);
        var local = LocalPath(stem);
        var isNew = !File.Exists(path);
        var store = PimStore.Open(path);
        if (isNew && path != local && File.Exists(local))
        {
            using var source = PimStore.Open(local);
            CopyInto(source, store);
            Log.Info($"The local {stem} went into the account's store");
        }

        return store;
    }

    /// <summary>Every live item and picture of <paramref name="source"/> as changes of this computer in <paramref name="target"/>.</summary>
    internal static void CopyInto(PimStore source, PimStore target)
    {
        foreach (var collection in new[] { MatCloud.Contract.Contacts.ContactsContract.Contacts, MatCloud.Contract.Contacts.ContactsContract.Groups })
        {
            var items = source.Items(collection);
            if (items.Count > 0)
            {
                target.Write(items.Select(i => (i.Collection, i.Key, i.Data)));
            }
        }

        foreach (var id in source.PhotosToUpload().Concat(PhotoIds(source)).Distinct())
        {
            if (source.Photo(id) is { } bytes)
            {
                target.PutPhoto(id, bytes, isUploaded: false);
            }
        }
    }

    private static IEnumerable<string> PhotoIds(PimStore store)
        => store.Items(MatCloud.Contract.Contacts.ContactsContract.Contacts)
            .Select(i => MatCloud.Contract.Contacts.ContactsContract.ReadContact(i.Data)?.PhotoId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!);

    /// <summary>16 hex digits of the SHA-256 of server and user (no name in a file name).</summary>
    public static string AccountHash(string serverUrl, string userName)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serverUrl.TrimEnd('/').ToLowerInvariant() + "|" + userName.ToLowerInvariant())))[..16];
}

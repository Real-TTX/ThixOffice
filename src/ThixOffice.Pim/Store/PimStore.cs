using System.Globalization;
using MatCloud.Contract;
using Microsoft.Data.Sqlite;

namespace ThixOffice.Pim.Store;

/// <summary>
/// One item of the local store, as the general sync knows it: collection, key, data (null for a tombstone), when it changed, whether this computer
/// changed it and the change waits to go up (<see cref="IsDirty"/>), and the revision of the cloud it was last in step with.
/// </summary>
public sealed record StoredItem(string Collection, string Key, string? Data, DateTimeOffset UpdatedAt, bool IsDeleted, bool IsDirty, long Revision);

/// <summary>
/// The local store of a PIM app: one SQLite file per app and account (<c>Microsoft.Data.Sqlite</c>), the offline copy of its items of MatCloud's general
/// sync, the pictures, and the sync's cursor. It works without any cloud (a local-only store simply never syncs). The schema has a version
/// (<c>PRAGMA user_version</c>) and grows by migrations (<see cref="Migrations"/>), each in one transaction. One connection, guarded by a lock: the UI
/// thread reads and writes, the sync loop on a worker thread too. Writes of this computer mark an item dirty; the sync marks it clean when the cloud
/// has it (<see cref="MarkClean"/>) and writes what came from the cloud (<see cref="ApplyRemote"/>: a newer change of this computer is kept).
/// </summary>
public sealed class PimStore : SqliteDatabase
{
    /// <summary>The migrations, in order: index + 1 is the version a store has after it.</summary>
    internal static readonly IReadOnlyList<string> Migrations =
    [
        // 1: the items of the general sync, the meta values (cursor, account), the pictures
        """
        CREATE TABLE Item (
            Collection TEXT NOT NULL,
            Key TEXT NOT NULL,
            Data TEXT NULL,
            UpdatedAt INTEGER NOT NULL,
            IsDeleted INTEGER NOT NULL DEFAULT 0,
            IsDirty INTEGER NOT NULL DEFAULT 0,
            Revision INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (Collection, Key));
        CREATE INDEX ItemDirty ON Item (IsDirty) WHERE IsDirty = 1;
        CREATE TABLE Meta (Name TEXT PRIMARY KEY, Value TEXT NOT NULL);
        CREATE TABLE Photo (Id TEXT PRIMARY KEY, Bytes BLOB NOT NULL, IsUploaded INTEGER NOT NULL DEFAULT 0);
        """,

        // 2: when the store was synced last (the status bar)
        """
        INSERT OR IGNORE INTO Meta (Name, Value) VALUES ('created', strftime('%s','now'));
        """,
    ];

    private PimStore(string path)
        : base(path, Migrations, "Die Kontakte auf diesem Computer stammen von einer neueren Version. Bitte aktualisieren Sie die App.")
    {
    }

    /// <summary>Raised after the sync wrote items that came from the cloud (the collections it touched); any thread.</summary>
    public event EventHandler<IReadOnlyCollection<string>>? RemoteChanged;

    /// <summary>The version a store has after every migration of this build.</summary>
    public static int CurrentVersion => Migrations.Count;

    /// <summary>Opens (or creates) the store at <paramref name="path"/> and brings its schema up to date.</summary>
    public static PimStore Open(string path) => new(path);

    // ---- items ----

    /// <summary>The items of a collection (tombstones only when asked for).</summary>
    public IReadOnlyList<StoredItem> Items(string collection, bool withDeleted = false)
    {
        lock (Gate)
        {
            using var command = Command("SELECT Collection, Key, Data, UpdatedAt, IsDeleted, IsDirty, Revision FROM Item WHERE Collection = $c" + (withDeleted ? "" : " AND IsDeleted = 0"));
            command.Parameters.AddWithValue("$c", collection);
            return ReadItems(command);
        }
    }

    public StoredItem? Get(string collection, string key)
    {
        lock (Gate)
        {
            using var command = Command("SELECT Collection, Key, Data, UpdatedAt, IsDeleted, IsDirty, Revision FROM Item WHERE Collection = $c AND Key = $k");
            command.Parameters.AddWithValue("$c", collection);
            command.Parameters.AddWithValue("$k", key);
            return ReadItems(command).FirstOrDefault();
        }
    }

    /// <summary>Changes of this computer (a write, or a delete with <paramref name="data"/> null): dirty until the cloud has them. One transaction.</summary>
    public void Write(IEnumerable<(string Collection, string Key, string? Data)> changes, DateTimeOffset? at = null)
    {
        var when = at ?? DateTimeOffset.UtcNow;
        lock (Gate)
        {
            using var transaction = Connection.BeginTransaction();
            foreach (var (collection, key, data) in changes)
            {
                using var command = Command("""
                    INSERT INTO Item (Collection, Key, Data, UpdatedAt, IsDeleted, IsDirty, Revision) VALUES ($c, $k, $d, $u, $x, 1, 0)
                    ON CONFLICT (Collection, Key) DO UPDATE SET Data = $d, UpdatedAt = $u, IsDeleted = $x, IsDirty = 1
                    """);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$c", collection);
                command.Parameters.AddWithValue("$k", key);
                command.Parameters.AddWithValue("$d", (object?)data ?? DBNull.Value);
                command.Parameters.AddWithValue("$u", when.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$x", data is null ? 1 : 0);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>The changes of this computer that wait to go up, oldest first.</summary>
    public IReadOnlyList<StoredItem> Dirty(int limit = SyncContract.MaxBatchSize)
    {
        lock (Gate)
        {
            using var command = Command("SELECT Collection, Key, Data, UpdatedAt, IsDeleted, IsDirty, Revision FROM Item WHERE IsDirty = 1 ORDER BY UpdatedAt LIMIT $l");
            command.Parameters.AddWithValue("$l", limit);
            return ReadItems(command);
        }
    }

    /// <summary>How many changes wait to go up.</summary>
    public int DirtyCount
    {
        get
        {
            lock (Gate)
            {
                return (int)Scalar<long>("SELECT COUNT(*) FROM Item WHERE IsDirty = 1");
            }
        }
    }

    /// <summary>The cloud has the change of <paramref name="updatedAt"/>: clean – unless this computer changed the item again since.</summary>
    public void MarkClean(string collection, string key, DateTimeOffset updatedAt, long revision)
    {
        lock (Gate)
        {
            using var command = Command("UPDATE Item SET IsDirty = 0, Revision = $r WHERE Collection = $c AND Key = $k AND UpdatedAt = $u");
            command.Parameters.AddWithValue("$c", collection);
            command.Parameters.AddWithValue("$k", key);
            command.Parameters.AddWithValue("$u", updatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$r", revision);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Writes items that came from the cloud (pulled, or the winner of a push that lost): the last writer wins – a change of this computer that is
    /// newer stays (it goes up next). Answers how many were written; raises <see cref="RemoteChanged"/>.
    /// </summary>
    public int ApplyRemote(IEnumerable<SyncItemDto> items)
    {
        var written = 0;
        var collections = new HashSet<string>(StringComparer.Ordinal);
        lock (Gate)
        {
            using var transaction = Connection.BeginTransaction();
            foreach (var item in items)
            {
                using (var check = Command("SELECT UpdatedAt, IsDirty FROM Item WHERE Collection = $c AND Key = $k"))
                {
                    check.Transaction = transaction;
                    check.Parameters.AddWithValue("$c", item.Collection);
                    check.Parameters.AddWithValue("$k", item.Key);
                    using var reader = check.ExecuteReader();
                    if (reader.Read() && reader.GetInt64(1) == 1 && reader.GetInt64(0) > item.UpdatedAt.ToUnixTimeMilliseconds())
                    {
                        continue; // this computer changed it later: its change wins and goes up
                    }
                }

                using var command = Command("""
                    INSERT INTO Item (Collection, Key, Data, UpdatedAt, IsDeleted, IsDirty, Revision) VALUES ($c, $k, $d, $u, $x, 0, $r)
                    ON CONFLICT (Collection, Key) DO UPDATE SET Data = $d, UpdatedAt = $u, IsDeleted = $x, IsDirty = 0, Revision = $r
                    """);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$c", item.Collection);
                command.Parameters.AddWithValue("$k", item.Key);
                command.Parameters.AddWithValue("$d", item.IsDeleted ? DBNull.Value : (object?)item.Data ?? DBNull.Value);
                command.Parameters.AddWithValue("$u", item.UpdatedAt.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$x", item.IsDeleted ? 1 : 0);
                command.Parameters.AddWithValue("$r", item.Revision);
                command.ExecuteNonQuery();
                collections.Add(item.Collection);
                written++;
            }

            transaction.Commit();
        }

        if (collections.Count > 0)
        {
            RemoteChanged?.Invoke(this, collections);
        }

        return written;
    }

    /// <summary>Every live item becomes a change of this computer (a local store that is taken into an account: everything goes up).</summary>
    public void MarkAllDirty()
    {
        lock (Gate)
        {
            Execute("UPDATE Item SET IsDirty = 1, Revision = 0 WHERE IsDeleted = 0; DELETE FROM Item WHERE IsDeleted = 1; UPDATE Photo SET IsUploaded = 0;");
            SetMeta("cursor", "0");
        }
    }

    /// <summary>Tombstones the cloud has seen long ago go (a year, like MatCloud's).</summary>
    public int RemoveOldTombstones(TimeSpan age)
    {
        lock (Gate)
        {
            using var command = Command("DELETE FROM Item WHERE IsDeleted = 1 AND IsDirty = 0 AND UpdatedAt < $u");
            command.Parameters.AddWithValue("$u", (DateTimeOffset.UtcNow - age).ToUnixTimeMilliseconds());
            return command.ExecuteNonQuery();
        }
    }

    // ---- meta ----

    /// <summary>The revision of the cloud everything up to which is here (the cursor of the pull).</summary>
    public long Cursor
    {
        get => long.TryParse(GetMeta("cursor"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cursor) ? cursor : 0;
        set => SetMeta("cursor", value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>When the last sync finished, or null.</summary>
    public DateTimeOffset? LastSync
    {
        get => long.TryParse(GetMeta("lastSync"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
        set => SetMeta("lastSync", value?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "");
    }

    public string? GetMeta(string name)
    {
        lock (Gate)
        {
            using var command = Command("SELECT Value FROM Meta WHERE Name = $n");
            command.Parameters.AddWithValue("$n", name);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string name, string value)
    {
        lock (Gate)
        {
            using var command = Command("INSERT INTO Meta (Name, Value) VALUES ($n, $v) ON CONFLICT (Name) DO UPDATE SET Value = $v");
            command.Parameters.AddWithValue("$n", name);
            command.Parameters.AddWithValue("$v", value);
            command.ExecuteNonQuery();
        }
    }

    // ---- pictures ----

    /// <summary>Keeps a picture by its id (the SHA-256 of the bytes); <paramref name="isUploaded"/>: the cloud has it already.</summary>
    public void PutPhoto(string id, byte[] bytes, bool isUploaded)
    {
        lock (Gate)
        {
            using var command = Command("INSERT INTO Photo (Id, Bytes, IsUploaded) VALUES ($i, $b, $u) ON CONFLICT (Id) DO UPDATE SET IsUploaded = MAX(IsUploaded, $u)");
            command.Parameters.AddWithValue("$i", id);
            command.Parameters.AddWithValue("$b", bytes);
            command.Parameters.AddWithValue("$u", isUploaded ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    public byte[]? Photo(string id)
    {
        lock (Gate)
        {
            using var command = Command("SELECT Bytes FROM Photo WHERE Id = $i");
            command.Parameters.AddWithValue("$i", id);
            return command.ExecuteScalar() as byte[];
        }
    }

    public bool HasPhoto(string id)
    {
        lock (Gate)
        {
            using var command = Command("SELECT 1 FROM Photo WHERE Id = $i");
            command.Parameters.AddWithValue("$i", id);
            return command.ExecuteScalar() is not null;
        }
    }

    /// <summary>The ids of the pictures the cloud does not have yet.</summary>
    public IReadOnlyList<string> PhotosToUpload()
    {
        lock (Gate)
        {
            using var command = Command("SELECT Id FROM Photo WHERE IsUploaded = 0");
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }
    }

    public void MarkPhotoUploaded(string id)
    {
        lock (Gate)
        {
            using var command = Command("UPDATE Photo SET IsUploaded = 1 WHERE Id = $i");
            command.Parameters.AddWithValue("$i", id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Pictures no live item names any more (<paramref name="named"/>) go; answers how many.</summary>
    public int RemovePhotosExcept(IReadOnlySet<string> named)
    {
        lock (Gate)
        {
            var all = new List<string>();
            using (var command = Command("SELECT Id FROM Photo"))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    all.Add(reader.GetString(0));
                }
            }

            var removed = 0;
            foreach (var id in all.Where(id => !named.Contains(id)))
            {
                using var delete = Command("DELETE FROM Photo WHERE Id = $i");
                delete.Parameters.AddWithValue("$i", id);
                removed += delete.ExecuteNonQuery();
            }

            return removed;
        }
    }

    private static List<StoredItem> ReadItems(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<StoredItem>();
        while (reader.Read())
        {
            items.Add(new StoredItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.GetInt64(4) == 1,
                reader.GetInt64(5) == 1,
                reader.GetInt64(6)));
        }

        return items;
    }
}

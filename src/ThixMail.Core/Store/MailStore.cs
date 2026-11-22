using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using ThixOffice.Pim.Store;

namespace ThixMail.Core.Store;

/// <summary>Which messages a list shows.</summary>
public enum ListFilter
{
    All,
    Unread,
    Flagged,
    Attachments,
}

/// <summary>How a list is sorted.</summary>
public enum ListSort
{
    Date,
    From,
    Subject,
    Size,
}

/// <summary>A query of the message list: the folders (one, or every inbox), a filter, a search (full text over headers and cached bodies) and the order.</summary>
public sealed record MessageQuery(IReadOnlyList<long> FolderIds, ListFilter Filter = ListFilter.All, string Search = "", ListSort Sort = ListSort.Date, bool Descending = true);

/// <summary>A row of a list before its header is read: enough to group by day and by conversation for 200,000 messages without loading them.</summary>
public readonly record struct ListEntry(long Id, long Date, long Thread);

/// <summary>
/// ThixMail's offline store: one SQLite file (<see cref="SqliteDatabase"/> of ThixOffice.Pim) for every account of this computer – folders with their
/// special-use roles and UIDVALIDITY/UIDNEXT/MODSEQ, message headers and flags, conversations (References/In-Reply-To, the subject as fallback), the
/// bodies fetched on demand (the raw message as a file, its text in the FTS5 index), the operations queue of offline actions, the Postausgang, recent
/// recipients, the senders whose pictures load always, and a size budget for the cached files. Thread-safe (the base's lock).
/// </summary>
public sealed class MailStore : SqliteDatabase
{
    internal static readonly IReadOnlyList<string> Migrations =
    [
        """
        CREATE TABLE Folder (
            Id INTEGER PRIMARY KEY,
            Account TEXT NOT NULL,
            Path TEXT NOT NULL,
            Name TEXT NOT NULL,
            ParentPath TEXT NULL,
            Delimiter TEXT NOT NULL DEFAULT '/',
            Role INTEGER NOT NULL DEFAULT 0,
            Selectable INTEGER NOT NULL DEFAULT 1,
            UidValidity INTEGER NOT NULL DEFAULT 0,
            UidNext INTEGER NOT NULL DEFAULT 0,
            HighestModSeq INTEGER NOT NULL DEFAULT 0,
            Total INTEGER NOT NULL DEFAULT 0,
            Unread INTEGER NOT NULL DEFAULT 0,
            FlagWindow INTEGER NOT NULL DEFAULT 0,
            OldestUid INTEGER NOT NULL DEFAULT 0,
            UNIQUE (Account, Path));
        CREATE TABLE Message (
            Id INTEGER PRIMARY KEY,
            Folder INTEGER NOT NULL REFERENCES Folder(Id) ON DELETE CASCADE,
            Uid INTEGER NOT NULL,
            MessageId TEXT NOT NULL DEFAULT '',
            InReplyTo TEXT NOT NULL DEFAULT '',
            Refs TEXT NOT NULL DEFAULT '',
            Subject TEXT NOT NULL DEFAULT '',
            NormSubject TEXT NOT NULL DEFAULT '',
            IsReply INTEGER NOT NULL DEFAULT 0,
            FromName TEXT NOT NULL DEFAULT '',
            FromAddress TEXT NOT NULL DEFAULT '',
            ToList TEXT NOT NULL DEFAULT '[]',
            CcList TEXT NOT NULL DEFAULT '[]',
            ReplyToList TEXT NOT NULL DEFAULT '[]',
            Date INTEGER NOT NULL,
            Size INTEGER NOT NULL DEFAULT 0,
            Flags INTEGER NOT NULL DEFAULT 0,
            Keywords TEXT NOT NULL DEFAULT '',
            HasAttachments INTEGER NOT NULL DEFAULT 0,
            Preview TEXT NOT NULL DEFAULT '',
            Thread INTEGER NOT NULL DEFAULT 0,
            ModSeq INTEGER NOT NULL DEFAULT 0,
            UNIQUE (Folder, Uid));
        CREATE INDEX MessageByDate ON Message (Folder, Date DESC);
        CREATE INDEX MessageByThread ON Message (Thread);
        CREATE INDEX MessageByMessageId ON Message (MessageId);
        CREATE INDEX MessageBySubject ON Message (NormSubject, Date);
        CREATE TABLE MessageRef (Ref TEXT NOT NULL, Message INTEGER NOT NULL REFERENCES Message(Id) ON DELETE CASCADE);
        CREATE INDEX MessageRefByRef ON MessageRef (Ref);
        CREATE TABLE Body (Message INTEGER PRIMARY KEY REFERENCES Message(Id) ON DELETE CASCADE, Path TEXT NOT NULL, Size INTEGER NOT NULL, CachedAt INTEGER NOT NULL);
        CREATE VIRTUAL TABLE MessageText USING fts5(Subject, Sender, Recipients, Body, tokenize = 'unicode61 remove_diacritics 2');
        CREATE TABLE Operation (Id INTEGER PRIMARY KEY, Account TEXT NOT NULL, Kind INTEGER NOT NULL, Payload TEXT NOT NULL, CreatedAt INTEGER NOT NULL,
            Attempts INTEGER NOT NULL DEFAULT 0, LastError TEXT NULL);
        CREATE TABLE Outbox (Id INTEGER PRIMARY KEY, Account TEXT NOT NULL, Path TEXT NOT NULL, Subject TEXT NOT NULL, Recipients TEXT NOT NULL,
            CreatedAt INTEGER NOT NULL, State INTEGER NOT NULL DEFAULT 0, Attempts INTEGER NOT NULL DEFAULT 0, LastError TEXT NULL, MessageId TEXT NOT NULL DEFAULT '',
            OriginalMessage INTEGER NULL, OriginKind INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE Recipient (Address TEXT PRIMARY KEY COLLATE NOCASE, Name TEXT NOT NULL DEFAULT '', Count INTEGER NOT NULL DEFAULT 0, LastUsed INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE TrustedSender (Address TEXT PRIMARY KEY COLLATE NOCASE);
        CREATE TABLE Meta (Name TEXT PRIMARY KEY, Value TEXT NOT NULL);
        """,
    ];

    private MailStore(string path, string filesFolder)
        : base(path, Migrations, "Die E-Mails auf diesem Computer stammen von einer neueren Version von ThixMail. Bitte aktualisieren Sie die App.")
    {
        FilesFolder = filesFolder;
    }

    /// <summary>Raised after messages of folders changed (inserted, flags, removed, moved): the folder ids; any thread.</summary>
    public event EventHandler<IReadOnlyCollection<long>>? MessagesChanged;

    /// <summary>Raised after the folders of an account changed (listed, created, renamed, counts); any thread.</summary>
    public event EventHandler? FoldersChanged;

    /// <summary>Raised after the queue or the outbox changed (the status bar counts them); any thread.</summary>
    public event EventHandler? QueueChanged;

    /// <summary>Where the cached raw messages and the outbox's messages are (one file per message).</summary>
    public string FilesFolder { get; }

    /// <summary>Opens (or creates) the store at <paramref name="path"/>; files next to it in "&lt;name&gt;-files".</summary>
    public static MailStore Open(string path)
    {
        var folder = path == ":memory:" ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thixmail-" + Guid.NewGuid().ToString("N")) : path + "-files";
        Directory.CreateDirectory(folder);
        return new MailStore(path, folder);
    }

    // ===== folders =====

    public IReadOnlyList<MailFolder> Folders(string? accountId = null) =>
        Query("SELECT * FROM Folder" + (accountId is null ? "" : " WHERE Account = $a") + " ORDER BY Account, Path", ReadFolder, ("$a", accountId));

    public MailFolder? Folder(long id) => Query("SELECT * FROM Folder WHERE Id = $i", ReadFolder, ("$i", id)).FirstOrDefault();

    public MailFolder? FolderByPath(string accountId, string path) =>
        Query("SELECT * FROM Folder WHERE Account = $a AND Path = $p", ReadFolder, ("$a", accountId), ("$p", path)).FirstOrDefault();

    public MailFolder? FolderByRole(string accountId, FolderRole role) =>
        Query("SELECT * FROM Folder WHERE Account = $a AND Role = $r ORDER BY Id LIMIT 1", ReadFolder, ("$a", accountId), ("$r", (int)role)).FirstOrDefault();

    /// <summary>
    /// The folders as the server lists them: new ones are added, known ones keep their state (a changed role is taken), vanished ones go with their
    /// messages. A folder without a special-use role gets one by its name (Outlook's and the common names: "Sent", "Gesendete Elemente" …).
    /// </summary>
    public void SyncFolders(string accountId, IReadOnlyList<RemoteFolder> remote)
    {
        var withRoles = AssignRoles(remote);
        InTransaction(transaction =>
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in withRoles)
            {
                known.Add(folder.Path);
                using var command = Command("""
                    INSERT INTO Folder (Account, Path, Name, ParentPath, Delimiter, Role, Selectable) VALUES ($a, $p, $n, $pp, $d, $r, $s)
                    ON CONFLICT (Account, Path) DO UPDATE SET Name = $n, ParentPath = $pp, Delimiter = $d, Role = $r, Selectable = $s
                    """, ("$a", accountId), ("$p", folder.Path), ("$n", folder.Name), ("$pp", folder.ParentPath), ("$d", folder.Delimiter.ToString()),
                    ("$r", (int)folder.Role), ("$s", folder.IsSelectable ? 1 : 0));
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }

            var existing = new List<(long Id, string Path)>();
            using (var list = Command("SELECT Id, Path FROM Folder WHERE Account = $a", ("$a", accountId)))
            {
                list.Transaction = transaction;
                using var reader = list.ExecuteReader();
                while (reader.Read())
                {
                    existing.Add((reader.GetInt64(0), reader.GetString(1)));
                }
            }

            foreach (var (id, path) in existing.Where(item => !known.Contains(item.Path)))
            {
                DeleteFolderRows(transaction, id);
            }

            return 0;
        });
        FoldersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The special-use role of each folder, or one recognized by its name when the server names none (only one folder per role).</summary>
    internal static IReadOnlyList<RemoteFolder> AssignRoles(IReadOnlyList<RemoteFolder> folders)
    {
        var taken = folders.Where(f => f.Role != FolderRole.None).Select(f => f.Role).ToHashSet();
        var result = new List<RemoteFolder>();
        foreach (var folder in folders)
        {
            if (folder.Role != FolderRole.None)
            {
                result.Add(folder);
                continue;
            }

            var role = RoleByName(folder.Path, folder.Name);
            if (role != FolderRole.None && taken.Add(role))
            {
                result.Add(folder with { Role = role });
            }
            else
            {
                result.Add(folder);
            }
        }

        return result;
    }

    private static FolderRole RoleByName(string path, string name)
    {
        if (path.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return FolderRole.Inbox;
        }

        return name.Trim().ToLowerInvariant() switch
        {
            "drafts" or "draft" or "entwürfe" or "entwurf" => FolderRole.Drafts,
            "sent" or "sent items" or "sent messages" or "sent mail" or "gesendet" or "gesendete elemente" or "gesendete objekte" or "gesendete nachrichten" => FolderRole.Sent,
            "trash" or "deleted" or "deleted items" or "deleted messages" or "papierkorb" or "gelöschte elemente" or "gelöschte objekte" or "bin" => FolderRole.Trash,
            "junk" or "spam" or "junk e-mail" or "junk-e-mail" or "junk email" or "bulk mail" => FolderRole.Junk,
            "archive" or "archiv" or "archives" => FolderRole.Archive,
            _ => FolderRole.None,
        };
    }

    /// <summary>The state after a sync of the folder (UIDVALIDITY, UIDNEXT, MODSEQ, the oldest UID fetched) and the counts of what the store has.</summary>
    public void SetFolderState(long folderId, uint uidValidity, uint uidNext, ulong highestModSeq, uint? oldestUid = null)
    {
        Execute("""
            UPDATE Folder SET UidValidity = $v, UidNext = $n, HighestModSeq = $m, OldestUid = COALESCE($o, OldestUid),
                Total = (SELECT COUNT(*) FROM Message WHERE Folder = $i AND (Flags & 8) = 0),
                Unread = (SELECT COUNT(*) FROM Message WHERE Folder = $i AND (Flags & 9) = 0)
            WHERE Id = $i
            """, ("$i", folderId), ("$v", (long)uidValidity), ("$n", (long)uidNext), ("$m", (long)highestModSeq), ("$o", oldestUid is null ? null : (long)oldestUid.Value));
        FoldersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The flag poll's window (where the next round of polling starts).</summary>
    public int FlagWindow(long folderId) => (int)Scalar<long>("SELECT FlagWindow FROM Folder WHERE Id = $i", ("$i", folderId));

    public void SetFlagWindow(long folderId, int start) => Execute("UPDATE Folder SET FlagWindow = $w WHERE Id = $i", ("$i", folderId), ("$w", start));

    /// <summary>Counts again (after local actions).</summary>
    public void Recount(IEnumerable<long> folderIds)
    {
        foreach (var id in folderIds.Distinct())
        {
            Execute("""
                UPDATE Folder SET Total = (SELECT COUNT(*) FROM Message WHERE Folder = $i AND (Flags & 8) = 0),
                    Unread = (SELECT COUNT(*) FROM Message WHERE Folder = $i AND (Flags & 9) = 0) WHERE Id = $i
                """, ("$i", id));
        }

        FoldersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The server's UIDVALIDITY changed: everything the store knew of the folder is no longer valid (fetched again).</summary>
    public void ClearFolder(long folderId)
    {
        InTransaction(transaction =>
        {
            RemoveBodies(transaction, $"SELECT Id FROM Message WHERE Folder = {folderId.ToString(CultureInfo.InvariantCulture)}");
            using var command = Command("DELETE FROM MessageText WHERE rowid IN (SELECT Id FROM Message WHERE Folder = $f); DELETE FROM Message WHERE Folder = $f; UPDATE Folder SET UidNext = 0, HighestModSeq = 0, OldestUid = 0, FlagWindow = 0 WHERE Id = $f", ("$f", folderId));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
            return 0;
        });
        Raise([folderId]);
    }

    /// <summary>A folder made, renamed or deleted here (the server follows through the queue).</summary>
    public long AddFolder(string accountId, string path, string name, string? parentPath, char delimiter)
    {
        var id = Scalar<long>("""
            INSERT INTO Folder (Account, Path, Name, ParentPath, Delimiter) VALUES ($a, $p, $n, $pp, $d)
            ON CONFLICT (Account, Path) DO UPDATE SET Name = $n RETURNING Id
            """, ("$a", accountId), ("$p", path), ("$n", name), ("$pp", parentPath), ("$d", delimiter.ToString()));
        FoldersChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public void RenameFolder(long folderId, string newPath, string newName)
    {
        var folder = Folder(folderId) ?? throw new InvalidOperationException("No such folder.");
        Execute("UPDATE Folder SET Path = $p, Name = $n WHERE Id = $i", ("$i", folderId), ("$p", newPath), ("$n", newName));
        var prefix = folder.Path + folder.Delimiter;
        foreach (var child in Folders(folder.AccountId).Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var childPath = newPath + folder.Delimiter + child.Path[prefix.Length..];
            Execute("UPDATE Folder SET Path = $p, ParentPath = $pp WHERE Id = $i", ("$i", child.Id), ("$p", childPath),
                ("$pp", childPath[..childPath.LastIndexOf(folder.Delimiter)]));
        }

        FoldersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFolder(long folderId)
    {
        var folder = Folder(folderId);
        if (folder is null)
        {
            return;
        }

        var prefix = folder.Path + folder.Delimiter;
        var ids = Folders(folder.AccountId).Where(f => f.Id == folderId || f.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(f => f.Id).ToList();
        InTransaction(transaction =>
        {
            foreach (var id in ids)
            {
                DeleteFolderRows(transaction, id);
            }

            return 0;
        });
        FoldersChanged?.Invoke(this, EventArgs.Empty);
        Raise(ids);
    }

    /// <summary>Every folder and message of an account (the account was removed).</summary>
    public void RemoveAccount(string accountId)
    {
        var ids = Folders(accountId).Select(f => f.Id).ToList();
        InTransaction(transaction =>
        {
            foreach (var id in ids)
            {
                DeleteFolderRows(transaction, id);
            }

            using var command = Command("DELETE FROM Operation WHERE Account = $a; DELETE FROM Outbox WHERE Account = $a", ("$a", accountId));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
            return 0;
        });
        FoldersChanged?.Invoke(this, EventArgs.Empty);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        Raise(ids);
    }

    private void DeleteFolderRows(SqliteTransaction transaction, long folderId)
    {
        RemoveBodies(transaction, $"SELECT Id FROM Message WHERE Folder = {folderId.ToString(CultureInfo.InvariantCulture)}");
        using var command = Command("DELETE FROM MessageText WHERE rowid IN (SELECT Id FROM Message WHERE Folder = $f); DELETE FROM Message WHERE Folder = $f; DELETE FROM Folder WHERE Id = $f", ("$f", folderId));
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static MailFolder ReadFolder(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        AccountId = reader.GetString(reader.GetOrdinal("Account")),
        Path = reader.GetString(reader.GetOrdinal("Path")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        ParentPath = reader.IsDBNull(reader.GetOrdinal("ParentPath")) ? null : reader.GetString(reader.GetOrdinal("ParentPath")),
        Delimiter = reader.GetString(reader.GetOrdinal("Delimiter")) is { Length: > 0 } delimiter ? delimiter[0] : '/',
        Role = (FolderRole)reader.GetInt32(reader.GetOrdinal("Role")),
        IsSelectable = reader.GetInt32(reader.GetOrdinal("Selectable")) == 1,
        UidValidity = (uint)reader.GetInt64(reader.GetOrdinal("UidValidity")),
        UidNext = (uint)reader.GetInt64(reader.GetOrdinal("UidNext")),
        HighestModSeq = (ulong)reader.GetInt64(reader.GetOrdinal("HighestModSeq")),
        Total = reader.GetInt32(reader.GetOrdinal("Total")),
        Unread = reader.GetInt32(reader.GetOrdinal("Unread")),
    };

    // ===== messages =====

    /// <summary>The UIDs the store has of a folder (only those the server gave; local placeholders are not).</summary>
    public IReadOnlyList<uint> Uids(long folderId) => Query("SELECT Uid FROM Message WHERE Folder = $f AND Uid > 0 ORDER BY Uid", r => (uint)r.GetInt64(0), ("$f", folderId));

    /// <summary>The lowest UID the store fetched of a folder (older ones wait for "load more" rounds), 0 when none.</summary>
    public uint OldestUid(long folderId) => (uint)Scalar<long>("SELECT OldestUid FROM Folder WHERE Id = $f", ("$f", folderId));

    /// <summary>
    /// Headers that came from the server: inserted with their conversation; a local placeholder of the same message (moved or saved here before the
    /// server told its UID) takes the UID instead of a second row. Answers how many were new.
    /// </summary>
    public int AddMessages(long folderId, string accountId, IReadOnlyList<RemoteMessage> messages)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        var added = InTransaction(transaction =>
        {
            var count = 0;
            foreach (var message in messages)
            {
                if (AdoptPlaceholder(transaction, folderId, message))
                {
                    continue;
                }

                var id = InsertMessage(transaction, folderId, accountId, message, message.Uid);
                if (id > 0)
                {
                    count++;
                }
            }

            return count;
        });
        Raise([folderId]);
        return added;
    }

    /// <summary>A message added here before the server knows it (a draft saved offline, a copy): a placeholder with a negative UID until the sync adopts it.</summary>
    public long AddLocalMessage(long folderId, string accountId, RemoteMessage message)
    {
        var id = InTransaction(transaction => InsertMessage(transaction, folderId, accountId, message, null));
        Raise([folderId]);
        return id;
    }

    private bool AdoptPlaceholder(SqliteTransaction transaction, long folderId, RemoteMessage message)
    {
        if (message.MessageId.Length == 0)
        {
            return false;
        }

        using var command = Command("UPDATE Message SET Uid = $u, Flags = $fl, ModSeq = $m WHERE Id = (SELECT Id FROM Message WHERE Folder = $f AND Uid < 0 AND MessageId = $mid LIMIT 1)",
            ("$u", (long)message.Uid), ("$fl", (int)message.Flags), ("$m", (long)message.ModSeq), ("$f", folderId), ("$mid", message.MessageId));
        command.Transaction = transaction;
        return command.ExecuteNonQuery() > 0;
    }

    private long InsertMessage(SqliteTransaction transaction, long folderId, string accountId, RemoteMessage message, uint? uid)
    {
        var references = message.References.Select(Clean).Where(r => r.Length > 0).ToList();
        var inReplyTo = Clean(message.InReplyTo);
        if (inReplyTo.Length > 0 && !references.Contains(inReplyTo, StringComparer.Ordinal))
        {
            references.Add(inReplyTo);
        }

        var messageId = Clean(message.MessageId);
        var normalized = MailMime.NormalizeSubject(message.Subject).ToLowerInvariant();
        var isReply = MailMime.IsReplySubject(message.Subject);
        var thread = FindThread(transaction, accountId, messageId, references, normalized, isReply, message.Date);
        var categories = message.Keywords.Where(IsCategory).ToList();

        using var insert = Command("""
            INSERT OR IGNORE INTO Message (Folder, Uid, MessageId, InReplyTo, Refs, Subject, NormSubject, IsReply, FromName, FromAddress, ToList, CcList, ReplyToList, Date,
                Size, Flags, Keywords, HasAttachments, Preview, Thread, ModSeq)
            VALUES ($f, $u, $mid, $irt, $refs, $s, $ns, $reply, $fn, $fa, $to, $cc, $rt, $d, $size, $fl, $kw, $att, $pv, $t, $m)
            RETURNING Id
            """,
            ("$f", folderId), ("$u", uid is null ? -DateTime.UtcNow.Ticks : (long)uid.Value), ("$mid", messageId), ("$irt", inReplyTo), ("$refs", string.Join(' ', references)),
            ("$s", message.Subject), ("$ns", normalized), ("$reply", isReply ? 1 : 0), ("$fn", message.From.Name), ("$fa", message.From.Address), ("$to", Addresses(message.To)),
            ("$cc", Addresses(message.Cc)), ("$rt", Addresses(message.ReplyTo)), ("$d", message.Date.ToUnixTimeSeconds()), ("$size", message.Size),
            ("$fl", (int)message.Flags), ("$kw", string.Join('\u001F', categories)), ("$att", message.HasAttachments ? 1 : 0), ("$pv", message.Preview),
            ("$t", thread ?? 0), ("$m", (long)message.ModSeq));
        insert.Transaction = transaction;
        var result = insert.ExecuteScalar();
        if (result is not long id)
        {
            return 0; // the UID is known already
        }

        if (uid is null)
        {
            Run(transaction, "UPDATE Message SET Uid = $u WHERE Id = $i", ("$u", -id), ("$i", id));
        }

        if (thread is null)
        {
            Run(transaction, "UPDATE Message SET Thread = $i WHERE Id = $i", ("$i", id));
        }

        foreach (var reference in references)
        {
            Run(transaction, "INSERT INTO MessageRef (Ref, Message) VALUES ($r, $m)", ("$r", reference), ("$m", id));
        }

        Run(transaction, "INSERT INTO MessageText (rowid, Subject, Sender, Recipients, Body) VALUES ($i, $s, $from, $to, $pv)",
            ("$i", id), ("$s", message.Subject), ("$from", $"{message.From.Name} {message.From.Address}"),
            ("$to", string.Join(' ', message.To.Concat(message.Cc).Select(a => $"{a.Name} {a.Address}"))), ("$pv", message.Preview));
        return id;
    }

    /// <summary>
    /// The conversation of a new message: the one of a message it refers to (In-Reply-To, References); else the one of a message that refers to it (it
    /// arrived after its reply); else the conversation of the same subject within 30 days when one of the two is an "AW:"/"WG:" (either may come first); else a new one.
    /// </summary>
    private long? FindThread(SqliteTransaction transaction, string accountId, string messageId, IReadOnlyList<string> references, string normalizedSubject,
        bool isReply, DateTimeOffset date)
    {
        foreach (var reference in references.AsEnumerable().Reverse())
        {
            using var byReference = Command("SELECT m.Thread FROM Message m JOIN Folder f ON f.Id = m.Folder WHERE m.MessageId = $r AND f.Account = $a LIMIT 1",
                ("$r", reference), ("$a", accountId));
            byReference.Transaction = transaction;
            if (byReference.ExecuteScalar() is long thread && thread > 0)
            {
                return thread;
            }
        }

        if (messageId.Length > 0)
        {
            using var children = Command("SELECT m.Thread FROM MessageRef r JOIN Message m ON m.Id = r.Message JOIN Folder f ON f.Id = m.Folder WHERE r.Ref = $r AND f.Account = $a LIMIT 1",
                ("$r", messageId), ("$a", accountId));
            children.Transaction = transaction;
            if (children.ExecuteScalar() is long thread && thread > 0)
            {
                return thread;
            }
        }

        if (normalizedSubject.Length > 0)
        {
            using var bySubject = Command("""
                SELECT m.Thread FROM Message m JOIN Folder f ON f.Id = m.Folder
                WHERE m.NormSubject = $s AND f.Account = $a AND m.Date BETWEEN $from AND $to AND ($reply = 1 OR m.IsReply = 1) ORDER BY m.Date DESC LIMIT 1
                """, ("$s", normalizedSubject), ("$a", accountId), ("$from", date.AddDays(-30).ToUnixTimeSeconds()), ("$to", date.AddDays(30).ToUnixTimeSeconds()), ("$reply", isReply ? 1 : 0));
            bySubject.Transaction = transaction;
            if (bySubject.ExecuteScalar() is long thread && thread > 0)
            {
                return thread;
            }
        }

        return null;
    }

    /// <summary>The flags the server reports now (a poll or CHANGEDSINCE); answers whether anything changed.</summary>
    public bool ApplyFlags(long folderId, IReadOnlyList<FlagState> states)
    {
        if (states.Count == 0)
        {
            return false;
        }

        var changed = InTransaction(transaction =>
        {
            var count = 0;
            foreach (var state in states)
            {
                using var command = Command("UPDATE Message SET Flags = $fl, Keywords = $kw, ModSeq = $m WHERE Folder = $f AND Uid = $u AND (Flags <> $fl OR Keywords <> $kw)",
                    ("$fl", (int)state.Flags), ("$kw", string.Join('\u001F', state.Keywords.Where(IsCategory))), ("$m", (long)state.ModSeq), ("$f", folderId), ("$u", (long)state.Uid));
                command.Transaction = transaction;
                count += command.ExecuteNonQuery();
            }

            return count;
        });
        if (changed > 0)
        {
            Recount([folderId]);
            Raise([folderId]);
        }

        return changed > 0;
    }

    /// <summary>Messages the server no longer has (expunged elsewhere).</summary>
    public int RemoveByUid(long folderId, IReadOnlyCollection<uint> uids)
    {
        if (uids.Count == 0)
        {
            return 0;
        }

        var ids = Query($"SELECT Id FROM Message WHERE Folder = $f AND Uid IN ({string.Join(',', uids)})", r => r.GetInt64(0), ("$f", folderId));
        RemoveMessages(ids);
        return ids.Count;
    }

    /// <summary>Removes messages from the store (deleted, expunged).</summary>
    public void RemoveMessages(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var list = string.Join(',', ids);
        var folders = Query($"SELECT DISTINCT Folder FROM Message WHERE Id IN ({list})", r => r.GetInt64(0));
        InTransaction(transaction =>
        {
            RemoveBodies(transaction, $"SELECT {string.Join(" UNION SELECT ", ids)}");
            Run(transaction, $"DELETE FROM MessageText WHERE rowid IN ({list}); DELETE FROM Message WHERE Id IN ({list})");
            return 0;
        });
        Recount(folders);
        Raise(folders);
    }

    /// <summary>Flags set here (the server follows through the queue); answers the folders touched.</summary>
    public void SetFlags(IReadOnlyCollection<long> ids, MessageFlags add, MessageFlags remove, IReadOnlyList<string>? addCategories = null, IReadOnlyList<string>? removeCategories = null)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var headers = Headers(ids);
        InTransaction(transaction =>
        {
            foreach (var header in headers)
            {
                var categories = header.Categories.Union(addCategories ?? [], StringComparer.OrdinalIgnoreCase)
                    .Except(removeCategories ?? [], StringComparer.OrdinalIgnoreCase).ToList();
                Run(transaction, "UPDATE Message SET Flags = $fl, Keywords = $kw WHERE Id = $i",
                    ("$fl", (int)((header.Flags | add) & ~remove)), ("$kw", string.Join('\u001F', categories)), ("$i", header.Id));
            }

            return 0;
        });
        var folders = headers.Select(h => h.FolderId).Distinct().ToList();
        Recount(folders);
        Raise(folders);
    }

    /// <summary>Messages moved here: they go to the target folder at once (placeholders until the server tells their new UIDs).</summary>
    public void MoveMessages(IReadOnlyCollection<long> ids, long targetFolderId)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var folders = Query($"SELECT DISTINCT Folder FROM Message WHERE Id IN ({string.Join(',', ids)})", r => r.GetInt64(0));
        InTransaction(transaction =>
        {
            foreach (var id in ids)
            {
                Run(transaction, "UPDATE OR IGNORE Message SET Folder = $t, Uid = $u WHERE Id = $i", ("$t", targetFolderId), ("$u", -id), ("$i", id));
            }

            return 0;
        });
        folders.Add(targetFolderId);
        Recount(folders);
        Raise(folders);
    }

    /// <summary>The server told the new UIDs of moved messages (UIDPLUS): the placeholders take them.</summary>
    public void SetUids(IReadOnlyList<long> ids, IReadOnlyList<uint> uids)
    {
        InTransaction(transaction =>
        {
            for (var index = 0; index < Math.Min(ids.Count, uids.Count); index++)
            {
                Run(transaction, "UPDATE OR IGNORE Message SET Uid = $u WHERE Id = $i AND Uid < 0", ("$u", (long)uids[index]), ("$i", ids[index]));
            }

            return 0;
        });
    }

    public MessageHeader? Header(long id) => Headers([id]).FirstOrDefault();

    /// <summary>The headers of <paramref name="ids"/> (in no particular order; missing ones are left out).</summary>
    public IReadOnlyList<MessageHeader> Headers(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var result = new List<MessageHeader>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
        {
            result.AddRange(Query($"SELECT m.*, f.Account FROM Message m JOIN Folder f ON f.Id = m.Folder WHERE m.Id IN ({string.Join(',', chunk)})", ReadHeader));
        }

        return result;
    }

    /// <summary>The messages of a conversation, oldest first.</summary>
    public IReadOnlyList<MessageHeader> Conversation(long threadId) =>
        Query("SELECT m.*, f.Account FROM Message m JOIN Folder f ON f.Id = m.Folder WHERE m.Thread = $t AND (m.Flags & 8) = 0 ORDER BY m.Date", ReadHeader, ("$t", threadId));

    /// <summary>
    /// The rows of a list (ids, days, conversations), sorted; the search is a full-text match of every word (as prefix) over subject, sender,
    /// recipients and the cached text. Fast enough for 200,000 messages (indexes; only three numbers per row).
    /// </summary>
    public IReadOnlyList<ListEntry> List(MessageQuery query)
    {
        if (query.FolderIds.Count == 0)
        {
            return [];
        }

        var sql = new StringBuilder($"SELECT Id, Date, Thread FROM Message WHERE Folder IN ({string.Join(',', query.FolderIds)}) AND (Flags & 8) = 0");
        sql.Append(query.Filter switch
        {
            ListFilter.Unread => " AND (Flags & 1) = 0",
            ListFilter.Flagged => " AND (Flags & 4) <> 0",
            ListFilter.Attachments => " AND HasAttachments = 1",
            _ => "",
        });
        var parameters = new List<(string, object?)>();
        if (FullTextQuery(query.Search) is { } match)
        {
            sql.Append(" AND Id IN (SELECT rowid FROM MessageText WHERE MessageText MATCH $q)");
            parameters.Add(("$q", match));
        }

        var direction = query.Descending ? "DESC" : "ASC";
        sql.Append(query.Sort switch
        {
            ListSort.From => $" ORDER BY COALESCE(NULLIF(FromName, ''), FromAddress) COLLATE NOCASE {direction}, Date DESC",
            ListSort.Subject => $" ORDER BY NormSubject COLLATE NOCASE {direction}, Date DESC",
            ListSort.Size => $" ORDER BY Size {direction}",
            _ => $" ORDER BY Date {direction}, Id {direction}",
        });
        return Query(sql.ToString(), r => new ListEntry(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)), [.. parameters]);
    }

    /// <summary>"anna angebot" → "anna"* AND "angebot"* (quotes doubled; nothing for an empty search).</summary>
    internal static string? FullTextQuery(string search)
    {
        var words = search.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string([.. word.Where(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' or '_')]))
            .Where(word => word.Length > 0)
            .ToList();
        return words.Count == 0 ? null : string.Join(" AND ", words.Select(word => $"\"{word.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*"));
    }

    private static MessageHeader ReadHeader(SqliteDataReader reader)
    {
        var keywords = reader.GetString(reader.GetOrdinal("Keywords"));
        return new MessageHeader
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            FolderId = reader.GetInt64(reader.GetOrdinal("Folder")),
            AccountId = reader.GetString(reader.GetOrdinal("Account")),
            Uid = Math.Max(0, reader.GetInt64(reader.GetOrdinal("Uid"))) is var uid && uid <= uint.MaxValue ? (uint)uid : 0,
            MessageId = reader.GetString(reader.GetOrdinal("MessageId")),
            InReplyTo = reader.GetString(reader.GetOrdinal("InReplyTo")),
            References = reader.GetString(reader.GetOrdinal("Refs")).Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Subject = reader.GetString(reader.GetOrdinal("Subject")),
            From = new MailAddress(reader.GetString(reader.GetOrdinal("FromName")), reader.GetString(reader.GetOrdinal("FromAddress"))),
            To = ReadAddresses(reader.GetString(reader.GetOrdinal("ToList"))),
            Cc = ReadAddresses(reader.GetString(reader.GetOrdinal("CcList"))),
            ReplyTo = ReadAddresses(reader.GetString(reader.GetOrdinal("ReplyToList"))),
            Date = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(reader.GetOrdinal("Date"))),
            Size = reader.GetInt64(reader.GetOrdinal("Size")),
            Flags = (MessageFlags)reader.GetInt32(reader.GetOrdinal("Flags")),
            Categories = keywords.Length == 0 ? [] : keywords.Split('\u001F'),
            HasAttachments = reader.GetInt32(reader.GetOrdinal("HasAttachments")) == 1,
            Preview = reader.GetString(reader.GetOrdinal("Preview")),
            ThreadId = reader.GetInt64(reader.GetOrdinal("Thread")),
        };
    }

    /// <summary>The local UID of a message (negative = a placeholder the server does not know yet).</summary>
    public long RawUid(long id) => ScalarOrNull("SELECT Uid FROM Message WHERE Id = $i", ("$i", id)) is long uid ? uid : 0;

    /// <summary>Keywords that are categories: not the $-keywords of the system or other clients ($Forwarded, $Junk, NonJunk …).</summary>
    public static bool IsCategory(string keyword) =>
        keyword.Length > 0 && !keyword.StartsWith('$') && !keyword.StartsWith('\\') && !keyword.Equals("NonJunk", StringComparison.OrdinalIgnoreCase)
        && !keyword.Equals("Junk", StringComparison.OrdinalIgnoreCase) && !keyword.Equals("Old", StringComparison.OrdinalIgnoreCase);

    // ===== bodies =====

    /// <summary>The cached raw message, or null when it was never fetched (or the budget let it go).</summary>
    public byte[]? Raw(long id)
    {
        var path = ScalarOrNull("SELECT Path FROM Body WHERE Message = $i", ("$i", id)) as string;
        if (path is null)
        {
            return null;
        }

        var full = System.IO.Path.Combine(FilesFolder, path);
        if (File.Exists(full))
        {
            return File.ReadAllBytes(full);
        }

        Execute("DELETE FROM Body WHERE Message = $i", ("$i", id));
        return null;
    }

    public bool HasBody(long id) => ScalarOrNull("SELECT 1 FROM Body WHERE Message = $i", ("$i", id)) is not null;

    /// <summary>Keeps the raw message (a file) and its text for the search.</summary>
    public void SetBody(long id, byte[] raw, string text)
    {
        var name = $"{id.ToString(CultureInfo.InvariantCulture)}.eml";
        var full = System.IO.Path.Combine(FilesFolder, name);
        var temporary = full + ".tmp";
        File.WriteAllBytes(temporary, raw);
        File.Move(temporary, full, overwrite: true);
        InTransaction(transaction =>
        {
            Run(transaction, "INSERT INTO Body (Message, Path, Size, CachedAt) VALUES ($i, $p, $s, $t) ON CONFLICT (Message) DO UPDATE SET Path = $p, Size = $s, CachedAt = $t",
                ("$i", id), ("$p", name), ("$s", raw.LongLength), ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            Run(transaction, "UPDATE MessageText SET Body = $b WHERE rowid = $i", ("$i", id), ("$b", text.Length > 200_000 ? text[..200_000] : text));
            return 0;
        });
    }

    /// <summary>How many bytes the cached messages take.</summary>
    public long CachedBytes => ScalarOrNull("SELECT SUM(Size) FROM Body") is long size ? size : 0;

    /// <summary>The cache within <paramref name="budget"/> bytes: the longest unused messages go first (their headers and search text stay). Answers how many went.</summary>
    public int EnforceBudget(long budget)
    {
        var removed = 0;
        while (CachedBytes > budget)
        {
            var oldest = Query("SELECT Message, Path FROM Body ORDER BY CachedAt LIMIT 50", r => (Id: r.GetInt64(0), Path: r.GetString(1)));
            if (oldest.Count == 0)
            {
                break;
            }

            foreach (var (id, path) in oldest)
            {
                TryDelete(System.IO.Path.Combine(FilesFolder, path));
                Execute("DELETE FROM Body WHERE Message = $i", ("$i", id));
                removed++;
                if (CachedBytes <= budget)
                {
                    break;
                }
            }
        }

        return removed;
    }

    private void RemoveBodies(SqliteTransaction transaction, string idsSql)
    {
        var paths = new List<string>();
        using (var command = Command($"SELECT Path FROM Body WHERE Message IN ({idsSql})"))
        {
            command.Transaction = transaction;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                paths.Add(reader.GetString(0));
            }
        }

        foreach (var path in paths)
        {
            TryDelete(System.IO.Path.Combine(FilesFolder, path));
        }

        Run(transaction, $"DELETE FROM Body WHERE Message IN ({idsSql})");
    }

    // ===== the queue =====

    public long Enqueue(string accountId, OperationKind kind, OperationData data)
    {
        if (kind == OperationKind.Append && data.MessageId is { Length: > 0 } messageId)
        {
            // a newer save of the same draft: the waiting append of the older one is not needed any more
            foreach (var waiting in PendingOperations(accountId).Where(op => op.Kind == OperationKind.Append && op.Data.MessageId == messageId && op.Data.Folder == data.Folder))
            {
                CompleteOperation(waiting.Id);
            }
        }

        var id = Scalar<long>("INSERT INTO Operation (Account, Kind, Payload, CreatedAt) VALUES ($a, $k, $p, $t) RETURNING Id",
            ("$a", accountId), ("$k", (int)kind), ("$p", JsonSerializer.Serialize(data, MailJson.Default.OperationData)), ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public IReadOnlyList<PendingOperation> PendingOperations(string? accountId = null) =>
        Query("SELECT Id, Account, Kind, Payload, CreatedAt, Attempts, LastError FROM Operation" + (accountId is null ? "" : " WHERE Account = $a") + " ORDER BY Id",
            r => new PendingOperation(r.GetInt64(0), r.GetString(1), (OperationKind)r.GetInt32(2),
                JsonSerializer.Deserialize(r.GetString(3), MailJson.Default.OperationData) ?? new OperationData(),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4)), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)),
            ("$a", accountId));

    public void CompleteOperation(long id)
    {
        Execute("DELETE FROM Operation WHERE Id = $i", ("$i", id));
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FailOperation(long id, string error)
    {
        Execute("UPDATE Operation SET Attempts = Attempts + 1, LastError = $e WHERE Id = $i", ("$i", id), ("$e", error));
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A message file of the store's folder (the outbox's, an append's).</summary>
    public string WriteFile(string prefix, byte[] content)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}.eml";
        File.WriteAllBytes(System.IO.Path.Combine(FilesFolder, name), content);
        return name;
    }

    public byte[]? ReadFile(string name)
    {
        var full = System.IO.Path.Combine(FilesFolder, System.IO.Path.GetFileName(name));
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    public void DeleteFile(string name) => TryDelete(System.IO.Path.Combine(FilesFolder, System.IO.Path.GetFileName(name)));

    // ===== the outbox =====

    public long AddToOutbox(string accountId, byte[] raw, string subject, string recipients, string messageId, long? originalMessage, int originKind)
    {
        var path = WriteFile("outbox", raw);
        var id = Scalar<long>("""
            INSERT INTO Outbox (Account, Path, Subject, Recipients, CreatedAt, MessageId, OriginalMessage, OriginKind) VALUES ($a, $p, $s, $r, $t, $m, $o, $k) RETURNING Id
            """, ("$a", accountId), ("$p", path), ("$s", subject), ("$r", recipients), ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$m", messageId),
            ("$o", originalMessage), ("$k", originKind));
        QueueChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public IReadOnlyList<OutboxItem> Outbox(string? accountId = null) =>
        Query("SELECT Id, Account, Path, Subject, Recipients, CreatedAt, State, Attempts, LastError, MessageId, OriginalMessage, OriginKind FROM Outbox" +
              (accountId is null ? "" : " WHERE Account = $a") + " ORDER BY Id",
            r => new OutboxItem(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)),
                (OutboxState)r.GetInt32(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9), r.IsDBNull(10) ? null : r.GetInt64(10), r.GetInt32(11)),
            ("$a", accountId));

    public void SetOutboxState(long id, OutboxState state, string? error = null, bool countAttempt = false)
    {
        Execute("UPDATE Outbox SET State = $s, LastError = $e, Attempts = Attempts + $c WHERE Id = $i", ("$i", id), ("$s", (int)state), ("$e", error), ("$c", countAttempt ? 1 : 0));
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromOutbox(long id)
    {
        var item = Outbox().FirstOrDefault(o => o.Id == id);
        Execute("DELETE FROM Outbox WHERE Id = $i", ("$i", id));
        if (item is not null)
        {
            DeleteFile(item.RawPath);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    // ===== recipients, senders, meta =====

    /// <summary>Addresses written to (suggested in the address fields, the most used first).</summary>
    public void RememberRecipients(IEnumerable<MailAddress> addresses)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var address in addresses.Where(a => a.Address.Contains('@', StringComparison.Ordinal)))
        {
            Execute("""
                INSERT INTO Recipient (Address, Name, Count, LastUsed) VALUES ($a, $n, 1, $t)
                ON CONFLICT (Address) DO UPDATE SET Count = Count + 1, LastUsed = $t, Name = CASE WHEN $n <> '' THEN $n ELSE Name END
                """, ("$a", address.Address), ("$n", address.Name), ("$t", now));
        }
    }

    public IReadOnlyList<MailAddress> SuggestRecipients(string prefix, int limit = 8)
    {
        var text = prefix.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        return Query("SELECT Name, Address FROM Recipient WHERE Address LIKE $p OR Name LIKE $p OR Name LIKE $w ORDER BY Count DESC, LastUsed DESC LIMIT $l",
            r => new MailAddress(r.GetString(0), r.GetString(1)), ("$p", text + "%"), ("$w", "% " + text + "%"), ("$l", limit));
    }

    /// <summary>"Bilder immer für diesen Absender laden".</summary>
    public void TrustSender(string address) => Execute("INSERT OR IGNORE INTO TrustedSender (Address) VALUES ($a)", ("$a", address.Trim()));

    public bool IsTrustedSender(string address) => ScalarOrNull("SELECT 1 FROM TrustedSender WHERE Address = $a", ("$a", address.Trim())) is not null;

    public string? GetMeta(string name) => ScalarOrNull("SELECT Value FROM Meta WHERE Name = $n", ("$n", name)) as string;

    public void SetMeta(string name, string value) =>
        Execute("INSERT INTO Meta (Name, Value) VALUES ($n, $v) ON CONFLICT (Name) DO UPDATE SET Value = $v", ("$n", name), ("$v", value));

    /// <summary>How many messages are in the store (tests, the status bar).</summary>
    public long MessageCount => Scalar<long>("SELECT COUNT(*) FROM Message");

    // ===== helpers =====

    private void Raise(IReadOnlyCollection<long> folders) => MessagesChanged?.Invoke(this, folders);

    private void Run(SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static string Clean(string id) => id.Trim().Trim('<', '>').Trim();

    private static string Addresses(IReadOnlyList<MailAddress> addresses) =>
        JsonSerializer.Serialize(addresses.ToList(), MailJson.Default.ListMailAddress);

    private static IReadOnlyList<MailAddress> ReadAddresses(string json)
    {
        if (json.Length <= 2)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, MailJson.Default.ListMailAddress) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

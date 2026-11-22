using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ThixOffice.Pim.Store;

/// <summary>
/// The SQLite infrastructure of the PIM apps' local stores (ThixContacts' <see cref="PimStore"/>, ThixMail's message store): one connection to one file
/// (no pooling, WAL, foreign keys), guarded by a lock so the UI thread and a sync loop on a worker thread can both use it, and a schema that grows by
/// migrations (<c>PRAGMA user_version</c> = the number applied, each in one transaction). A store from a newer build is refused in German.
/// </summary>
public abstract class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    /// <summary>Opens (or creates) the file at <paramref name="path"/> (":memory:" for tests) and applies <paramref name="migrations"/> it lacks.</summary>
    protected SqliteDatabase(string path, IReadOnlyList<string> migrations, string newerVersionMessage)
    {
        if (path != ":memory:")
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        }

        Path = path;
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;");
        Migrate(migrations, newerVersionMessage);
    }

    /// <summary>The file of the store (":memory:" for tests).</summary>
    public string Path { get; }

    /// <summary>True after <see cref="Dispose"/> (the connection is closed).</summary>
    public bool IsDisposed => _disposed;

    /// <summary>The schema version of the store (the number of migrations applied).</summary>
    public int SchemaVersion => (int)Scalar<long>("PRAGMA user_version");

    /// <summary>The lock every access holds (a caller that runs several statements as one unit takes it too).</summary>
    protected object Gate { get; } = new();

    protected SqliteConnection Connection => _connection;

    private void Migrate(IReadOnlyList<string> migrations, string newerVersionMessage)
    {
        lock (Gate)
        {
            var version = SchemaVersion;
            if (version > migrations.Count)
            {
                _connection.Dispose();
                throw new Core.Models.BusinessException(newerVersionMessage);
            }

            for (var next = version; next < migrations.Count; next++)
            {
                using var transaction = _connection.BeginTransaction();
                using (var command = Command(migrations[next]))
                {
                    command.Transaction = transaction;
                    command.ExecuteNonQuery();
                }

                using (var bump = Command($"PRAGMA user_version = {next + 1}"))
                {
                    bump.Transaction = transaction;
                    bump.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }
    }

    /// <summary>A command on the connection (the caller holds <see cref="Gate"/>).</summary>
    protected SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>Runs SQL without a result.</summary>
    protected int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (Gate)
        {
            using var command = Command(sql, parameters);
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>The first column of the first row, converted.</summary>
    protected T Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (Gate)
        {
            using var command = Command(sql, parameters);
            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The first column of the first row, or null.</summary>
    protected object? ScalarOrNull(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (Gate)
        {
            using var command = Command(sql, parameters);
            var value = command.ExecuteScalar();
            return value is DBNull ? null : value;
        }
    }

    /// <summary>Every row, read by <paramref name="read"/>.</summary>
    protected List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
    {
        lock (Gate)
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();
            var rows = new List<T>();
            while (reader.Read())
            {
                rows.Add(read(reader));
            }

            return rows;
        }
    }

    /// <summary>Runs <paramref name="work"/> in one transaction (commands made inside get it through <see cref="Command"/> on this connection).</summary>
    protected T InTransaction<T>(Func<SqliteTransaction, T> work)
    {
        lock (Gate)
        {
            using var transaction = _connection.BeginTransaction();
            var result = work(transaction);
            transaction.Commit();
            return result;
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connection.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

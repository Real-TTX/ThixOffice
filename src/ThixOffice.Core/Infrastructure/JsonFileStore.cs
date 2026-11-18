using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// One JSON file of the data folder (settings.json, recent.json), cached in memory. Only the business layer uses it. A
/// write goes to a temporary file first and replaces the old one, so a crash never leaves half a file; a file that cannot
/// be read is kept next to it ("*.broken-&lt;time&gt;") instead of being overwritten silently.
/// </summary>
internal sealed class JsonFileStore<T>(Func<string> path, Func<T> create)
    where T : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private T? _cache;
    private string? _cachedPath;

    /// <summary>The value (records: a change is a new object through <see cref="Update"/>).</summary>
    public T Read()
    {
        lock (_gate)
        {
            return Current(path());
        }
    }

    /// <summary>Changes the value in one step and saves it; returns the new value.</summary>
    public T Update(Func<T, T> change)
    {
        lock (_gate)
        {
            var file = path();
            var value = change(Current(file));
            Save(file, value);
            _cache = value;
            return value;
        }
    }

    /// <summary>Drops the cache (tests, the self test).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _cache = null;
            _cachedPath = null;
        }
    }

    /// <summary>The cached value – read again when the data folder changed (tests switch it).</summary>
    private T Current(string file)
    {
        if (_cache is null || _cachedPath != file)
        {
            _cache = Load(file);
            _cachedPath = file;
        }

        return _cache;
    }

    private T Load(string file)
    {
        if (!File.Exists(file))
        {
            return create();
        }

        try
        {
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<T>(stream, SerializerOptions) ?? create();
        }
        catch (JsonException exception)
        {
            Log.Warning($"{Path.GetFileName(file)} could not be read ({exception.Message}); using the defaults");
            File.Copy(file, file + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return create();
        }
    }

    private static void Save(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporaryPath = file + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, SerializerOptions));
        File.Move(temporaryPath, file, overwrite: true);
    }
}

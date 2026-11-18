using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ThixOffice.Core.Infrastructure;

/// <summary>The kind of a registry value the setup writes.</summary>
public enum RegistryValueType
{
    String = 0,
    DWord = 1,

    /// <summary>An empty value without data (the entries of OpenWithProgids).</summary>
    None = 2,
}

/// <summary>
/// The registry of the current user (HKCU) as far as the setup and the file types need it. The business layer writes through
/// this abstraction only: on Windows it is the real registry, in the tests an in-memory one (<see cref="MemoryUserRegistry"/>),
/// so the install and file-type rules are tested on every system without touching a real key.
/// </summary>
public interface IUserRegistry
{
    /// <summary>The value, or null; <paramref name="name"/> "" is the default value of the key.</summary>
    object? GetValue(string keyPath, string name);

    void SetValue(string keyPath, string name, object value, RegistryValueType type = RegistryValueType.String);

    void DeleteValue(string keyPath, string name);

    bool KeyExists(string keyPath);

    IReadOnlyList<string> ValueNames(string keyPath);

    /// <summary>Removes the key with everything below it (nothing happens when it is missing).</summary>
    void DeleteTree(string keyPath);
}

/// <summary>Which registry the business layer uses: the real one of Windows, or one a test put in.</summary>
public static class UserRegistry
{
    private static IUserRegistry? _current;

    /// <summary>HKCU on Windows; an empty in-memory registry elsewhere (nothing there reads it).</summary>
    public static IUserRegistry Current => _current ??= OperatingSystem.IsWindows() ? new WindowsUserRegistry() : new MemoryUserRegistry();

    /// <summary>Tests put in their own (null: back to the real one).</summary>
    internal static void UseForTests(IUserRegistry? registry) => _current = registry;
}

/// <summary>HKEY_CURRENT_USER.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUserRegistry : IUserRegistry
{
    public object? GetValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name);
    }

    public void SetValue(string keyPath, string name, object value, RegistryValueType type = RegistryValueType.String)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        switch (type)
        {
            case RegistryValueType.DWord:
                key.SetValue(name, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegistryValueType.None:
                key.SetValue(name, Array.Empty<byte>(), RegistryValueKind.None);
                break;
            default:
                key.SetValue(name, value.ToString() ?? "", RegistryValueKind.String);
                break;
        }
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool KeyExists(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key is not null;
    }

    public IReadOnlyList<string> ValueNames(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValueNames() ?? [];
    }

    public void DeleteTree(string keyPath) => Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}

/// <summary>A registry in memory (the tests; any system). Key paths and value names ignore case like Windows does.</summary>
public sealed class MemoryUserRegistry : IUserRegistry
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every key that exists (for assertions).</summary>
    public IReadOnlyCollection<string> Keys => _keys.Keys;

    public object? GetValue(string keyPath, string name) =>
        _keys.TryGetValue(Normalize(keyPath), out var values) ? values.GetValueOrDefault(name) : null;

    public void SetValue(string keyPath, string name, object value, RegistryValueType type = RegistryValueType.String) =>
        Create(keyPath)[name] = type switch
        {
            RegistryValueType.DWord => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueType.None => Array.Empty<byte>(),
            _ => value.ToString() ?? "",
        };

    public void DeleteValue(string keyPath, string name)
    {
        if (_keys.TryGetValue(Normalize(keyPath), out var values))
        {
            values.Remove(name);
        }
    }

    public bool KeyExists(string keyPath) => _keys.ContainsKey(Normalize(keyPath));

    public IReadOnlyList<string> ValueNames(string keyPath) =>
        _keys.TryGetValue(Normalize(keyPath), out var values) ? [.. values.Keys] : [];

    public void DeleteTree(string keyPath)
    {
        var root = Normalize(keyPath);
        foreach (var key in _keys.Keys.Where(key => key.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                                                    key.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _keys.Remove(key);
        }
    }

    /// <summary>Creates the key and its parents (like CreateSubKey).</summary>
    private Dictionary<string, object> Create(string keyPath)
    {
        var path = Normalize(keyPath);
        var parts = path.Split('\\');
        for (var length = 1; length < parts.Length; length++)
        {
            var parent = string.Join('\\', parts.Take(length));
            if (!_keys.ContainsKey(parent))
            {
                _keys[parent] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (!_keys.TryGetValue(path, out var values))
        {
            values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _keys[path] = values;
        }

        return values;
    }

    private static string Normalize(string keyPath) => keyPath.Trim('\\');
}

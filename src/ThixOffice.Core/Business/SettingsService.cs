using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// Reading and changing the settings of this computer. Every change is checked here (values, policy) before it is
/// saved; the UI only offers. <see cref="Changed"/> tells the app (theme, ribbon, recent list) what is new.
/// </summary>
public static class SettingsService
{
    /// <summary>The lengths of "Zuletzt verwendet" offered.</summary>
    public static readonly IReadOnlyList<int> RecentListSizeChoices = [10, 25, 50];

    /// <summary>The intervals of the recovery files offered (minutes, 0 = off).</summary>
    public static readonly IReadOnlyList<int> AutoRecoverChoices = [0, 1, 5, 10, 30];

    public const int MaximumRecentListSize = 100;

    private static readonly JsonFileStore<AppSettings> Store = new(() => AppPaths.SettingsFile, () => new AppSettings());

    /// <summary>Raised after every saved change (any thread).</summary>
    public static event EventHandler<AppSettings>? Changed;

    public static AppSettings Get() => Store.Read();

    /// <summary>Changes settings (checked and policy-protected).</summary>
    public static AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AccessPolicy.EnsureConfigurationEditable();
        var next = Normalize(change(Store.Read()));
        Validate(next);
        return Save(_ => next);
    }

    /// <summary>What the app remembers by itself (window size, folded ribbon): no configuration lock, values clamped.</summary>
    public static AppSettings Remember(Func<AppSettings, AppSettings> change)
    {
        var current = Store.Read();
        var next = change(current);
        return Save(_ => current with
        {
            WindowWidth = Math.Clamp(next.WindowWidth, 760, 4000),
            WindowHeight = Math.Clamp(next.WindowHeight, 460, 3000),
            IsRibbonCollapsed = next.IsRibbonCollapsed,
            AppValues = next.AppValues.Count <= MaxAppValues ? next.AppValues : current.AppValues,
        });
    }

    /// <summary>How many remembered app values a settings file keeps at most.</summary>
    public const int MaxAppValues = 64;

    /// <summary>A value the app remembered (null when none).</summary>
    public static string? AppValue(string key) => Get().AppValues.TryGetValue(key, out var value) ? value : null;

    /// <summary>Remembers a value of the app (null forgets it); no configuration lock – like the window size.</summary>
    public static void RememberAppValue(string key, string? value) => Remember(settings =>
    {
        var values = new Dictionary<string, string>(settings.AppValues);
        if (value is null)
        {
            values.Remove(key);
        }
        else
        {
            values[key] = value;
        }

        return settings with { AppValues = values };
    });

    /// <summary>Where "Speichern unter" starts: the chosen folder when it still exists, otherwise the documents folder.</summary>
    public static string SaveFolder => Get().DefaultSaveFolder is { Length: > 0 } folder && FileSystemProbe.FolderExists(folder) ? folder : FileSystemProbe.DocumentsFolder;

    /// <summary>The sign-in at ThixCloud (CloudService only; no configuration lock: DisableCloud governs the cloud).</summary>
    internal static AppSettings UpdateCloud(Func<CloudSettings, CloudSettings> change) => Save(settings => settings with { Cloud = change(settings.Cloud) });

    /// <summary>Drops the cached settings (tests, the self test read the file again).</summary>
    internal static void ResetCache() => Store.Reset();

    private static AppSettings Normalize(AppSettings settings) => settings with { DefaultSaveFolder = settings.DefaultSaveFolder.Trim() };

    private static AppSettings Save(Func<AppSettings, AppSettings> change)
    {
        var saved = Store.Update(change);
        Changed?.Invoke(null, saved);
        return saved;
    }

    private static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            throw new BusinessException("Dieses Erscheinungsbild gibt es nicht.", nameof(AppSettings.Theme));
        }

        if (settings.RecentListSize is < 1 or > MaximumRecentListSize)
        {
            throw new BusinessException($"Die Liste „Zuletzt verwendet“ hält zwischen 1 und {MaximumRecentListSize} Dokumente.", nameof(AppSettings.RecentListSize));
        }

        if (settings.AutoRecoverMinutes is < 0 or > 120)
        {
            throw new BusinessException("Wiederherstellungsdaten werden höchstens alle 120 Minuten gespeichert (0 = nie).", nameof(AppSettings.AutoRecoverMinutes));
        }

        if (settings.DefaultSaveFolder.Length > 0 && (!Path.IsPathFullyQualified(settings.DefaultSaveFolder) || !Directory.Exists(settings.DefaultSaveFolder)))
        {
            throw new BusinessException("Diesen Ordner gibt es nicht.", nameof(AppSettings.DefaultSaveFolder));
        }
    }
}

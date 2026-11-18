using System.Reflection;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// The app this process is (its <see cref="OfficeAppDefinition"/>) and the version CI built into the app's assembly
/// (Directory.Build.props: channel, build number, repository, app id). <see cref="Initialize"/> is the first call of every
/// app's Main (and of every test); the shared libraries read everything from here.
/// </summary>
public static class AppInfo
{
    private static OfficeAppDefinition? _definition;
    private static Assembly? _assembly;
    private static Pinned? _pinned;

    /// <summary>The app of this process.</summary>
    public static OfficeAppDefinition Definition =>
        _definition ?? throw new InvalidOperationException("AppInfo.Initialize was not called: the app must name its OfficeAppDefinition first.");

    public static bool IsInitialized => _definition is not null;

    /// <summary>The product name: ThixWrite.</summary>
    public static string ProductName => Definition.Name;

    /// <summary>e.g. 0.1.42-20261118, nightly-43-20261118 or local-20261118.</summary>
    public static string Version => _pinned?.Version ??
        _assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";

    /// <summary>release, nightly or local.</summary>
    public static string Channel => _pinned?.Channel ?? ReadMetadata("ThixOfficeChannel") ?? "local";

    public static int BuildNumber => _pinned?.BuildNumber ?? (int.TryParse(ReadMetadata("ThixOfficeBuildNumber"), out var number) ? number : 0);

    /// <summary>owner/name of the GitHub repository that publishes the builds.</summary>
    public static string Repository => ReadMetadata("ThixOfficeRepository") ?? OfficeAppDefinition.Repository;

    /// <summary>The app id built into the assembly (thixwrite); "thixoffice" for the shared projects.</summary>
    public static string BuiltAppId => ReadMetadata("ThixOfficeAppId") ?? Definition.Id;

    public static bool IsLocalBuild => Channel == "local";

    /// <summary>The name of this computer (pinned in snapshots).</summary>
    public static string MachineName => _pinned?.MachineName ?? Environment.MachineName;

    /// <summary>Makes <paramref name="definition"/> the app of this process; its version comes from <paramref name="appAssembly"/>.</summary>
    public static void Initialize(OfficeAppDefinition definition, Assembly appAssembly)
    {
        _definition = definition;
        _assembly = appAssembly;
    }

    /// <summary>Snapshots show a fixed version, channel and computer; null releases it.</summary>
    internal static void Pin(string? version, string channel = "release", int buildNumber = 0, string machineName = "ANNA-PC") =>
        _pinned = version is null ? null : new Pinned(version, channel, buildNumber, machineName);

    private static string? ReadMetadata(string key) =>
        _assembly?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value is { Length: > 0 } value
            ? value
            : null;

    private sealed record Pinned(string Version, string Channel, int BuildNumber, string MachineName);
}

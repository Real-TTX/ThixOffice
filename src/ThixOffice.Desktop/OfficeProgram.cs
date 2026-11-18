using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Desktop;

/// <summary>
/// The Main of every app of the suite, in one line: <c>OfficeProgram.Run&lt;App&gt;(args, ThixWriteApp.Definition)</c>.
/// First of all it names the app (AppInfo) and installs the crash reports; the self test and the snapshots work in a fresh
/// temporary data folder (&lt;APP&gt;_DATA_DIR, before anything reads the data folder); the snapshots draw headless; everything
/// else starts Avalonia with the classic desktop lifetime – the app ends itself when its last window closes.
/// </summary>
public static class OfficeProgram
{
    /// <summary>True in the published single-file program (the setup mode applies only there; a development build is always the app).</summary>
    public static bool IsSingleFile => string.IsNullOrEmpty(typeof(OfficeProgram).Assembly.Location);

    public static int Run<TApp>(string[] args, OfficeAppDefinition definition)
        where TApp : OfficeApplication, new()
    {
        AppInfo.Initialize(definition, typeof(TApp).Assembly);
        var isSnapshot = args.Contains(OfficeSnapshot.Argument);
        var isSelfTest = args.Contains(OfficeSelfTest.Argument);
        ProblemReports.Install(showMessageBox: !isSelfTest && !isSnapshot);

        if (isSelfTest || isSnapshot)
        {
            Environment.SetEnvironmentVariable(definition.DataDirectoryVariable,
                Path.Combine(Path.GetTempPath(), $"{definition.Id}-{(isSnapshot ? "snapshot" : "self-test")}", Guid.NewGuid().ToString("N")));
        }

        if (isSnapshot)
        {
            return OfficeSnapshot.Run<TApp>(args);
        }

        return BuildAvaloniaApp<TApp>().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>The Avalonia set-up of every app (also used by the XAML previewer).</summary>
    public static AppBuilder BuildAvaloniaApp<TApp>()
        where TApp : Application, new() =>
        AppBuilder.Configure<TApp>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();

    /// <summary>The version of an assembly as built (for the about page of a library).</summary>
    internal static string VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";
}

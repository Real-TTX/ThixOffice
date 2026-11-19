using Avalonia;
using ThixOffice.Desktop;
using ThixShow.Core;

namespace ThixShow;

internal static class Program
{
    /// <summary>
    /// One program for everything (OfficeApplication decides): the presentation program and – on Windows – its own setup, the self
    /// test (--self-test) and the snapshots (--snapshot). Closing the last window ends ThixShow.
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => OfficeProgram.Run<App>(args, ThixShowApp.Definition);

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => OfficeProgram.BuildAvaloniaApp<App>();
}

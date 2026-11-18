using Avalonia;
using ThixOffice.Desktop;
using ThixWrite.Core;

namespace ThixWrite;

internal static class Program
{
    /// <summary>
    /// One program for everything (OfficeApplication decides): the word processor and – on Windows – its own setup, the self
    /// test (--self-test) and the snapshots (--snapshot). Closing the last window ends ThixWrite.
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => OfficeProgram.Run<App>(args, ThixWriteApp.Definition);

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => OfficeProgram.BuildAvaloniaApp<App>();
}

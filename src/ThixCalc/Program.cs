using Avalonia;
using ThixOffice.Desktop;
using ThixCalc.Core;

namespace ThixCalc;

internal static class Program
{
    /// <summary>
    /// One program for everything (OfficeApplication decides): the spreadsheet and – on Windows – its own setup, the self
    /// test (--self-test) and the snapshots (--snapshot). Closing the last window ends ThixCalc.
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => OfficeProgram.Run<App>(args, ThixCalcApp.Definition);

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => OfficeProgram.BuildAvaloniaApp<App>();
}

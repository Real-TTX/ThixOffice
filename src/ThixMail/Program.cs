using Avalonia;
using ThixMail.Core;
using ThixOffice.Desktop;

namespace ThixMail;

internal static class Program
{
    /// <summary>
    /// One program for everything (OfficeApplication decides): the mail and – on Windows – its own setup, the self test (--self-test) and the
    /// snapshots (--snapshot). A workspace app: one main window; a second start brings it to the front (and opens the .eml files it was given).
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => OfficeProgram.Run<App>(args, ThixMailApp.Definition);

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => OfficeProgram.BuildAvaloniaApp<App>();
}

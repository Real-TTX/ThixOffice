using Avalonia;
using ThixContacts.Core;
using ThixOffice.Desktop;

namespace ThixContacts;

internal static class Program
{
    /// <summary>
    /// One program for everything (OfficeApplication decides): the contacts and – on Windows – their own setup, the self test (--self-test) and the
    /// snapshots (--snapshot). A workspace app: one main window; a second start brings it to the front (and imports the vCards it was given).
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => OfficeProgram.Run<App>(args, ThixContactsApp.Definition);

    /// <summary>Used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() => OfficeProgram.BuildAvaloniaApp<App>();
}
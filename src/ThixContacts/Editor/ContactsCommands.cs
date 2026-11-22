using ThixContacts.Pages;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop.People;

namespace ThixContacts.Editor;

/// <summary>
/// ThixContacts' commands: the people commands of ThixOffice.Pim.Desktop (<see cref="PeopleCommands"/>, the same in ThixMail's view "Personen") with
/// what the app adds – Drucken (Datei › Drucken: list or cards) and Jetzt synchronisieren (F9, the document's sync).
/// </summary>
public static class ContactsCommands
{
    public static void Add(OfficeWindow window) =>
        PeopleCommands.Add(window, () => (window.Editor as ContactsEditor)?.People, new PeopleCommandOptions
        {
            PrintPageId = ContactsPrintPage.PageId,
            Sync = () => SyncAsync(window),
            CanSync = () => window.Editor is ContactsEditor { Document.IsSyncing: true },
        });

    /// <summary>Imports one file (also a .vcf the system handed over).</summary>
    public static Task ImportFileAsync(OfficeWindow window, ContactsEditor editor, string path) => PeopleCommands.ImportFileAsync(window, editor.People, path);

    private static async Task SyncAsync(OfficeWindow window)
    {
        var editor = (ContactsEditor)window.Editor;
        var report = await editor.Document.SyncNowAsync();
        editor.UpdateStatus(window.StatusBar);
        if (report.Conflicts > 0)
        {
            window.ShowMessage($"Synchronisiert. {report.Conflicts} Kontakte wurden inzwischen auf einem anderen Gerät geändert – die neuere Fassung gilt.", InfoSeverity.Info, isClosable: true);
        }
    }
}

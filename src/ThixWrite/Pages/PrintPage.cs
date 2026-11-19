using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixWrite.Dialogs;
using ThixWrite.Editor;

namespace ThixWrite.Pages;

/// <summary>
/// Datei › Drucken of ThixWrite: the shared <see cref="OfficePrintPage"/> with the layout's pages, the caret's page as
/// "Aktuelle Seite" and Seite einrichten (<see cref="PageSetupDialog"/>).
/// </summary>
public sealed class PrintPage(OfficeWindow window, WriteEditor editor) : OfficePrintPage(window, new WritePrintSource(window, editor))
{
    /// <summary>The backstage entry of the page (between Speichern unter and Exportieren, like Word).</summary>
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Drucken", "print", () => new PrintPage(window, (WriteEditor)window.Editor), Shortcut: window.Commands.Find(WriteCommandIds.Print)?.CurrentShortcut);

    private sealed class WritePrintSource(OfficeWindow window, WriteEditor editor) : IPrintSource
    {
        public IReadOnlyList<string> Scopes => [];

        public int Scope { get; set; }

        public int CurrentPage => editor.CaretPage;

        public Func<Task<bool>>? PageSetup => () => PageSetupDialog.ShowAsync(window, editor);

        public ThixOffice.Desktop.Rendering.PrintablePages Snapshot()
        {
            editor.Layout.Update();
            return new PrintablePages([.. editor.Layout.GetDisplayLists()], WriteFonts.Measurer, window.Document.Title);
        }
    }
}

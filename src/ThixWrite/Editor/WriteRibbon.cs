using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixWrite.Editor;

/// <summary>
/// ThixWrite's ribbon (data, plan section 4) and its own commands. Phase 0: the shell's commands in Word's places plus
/// "Datum und Uhrzeit"; Start, Einfügen, Layout and Ansicht fill up with the editor in phase 1a.
/// </summary>
public static class WriteRibbon
{
    public const string InsertDateTime = "write.insertDateTime";

    public static RibbonLayout Layout { get; } = new(
    [
        new RibbonTab("home", "Start",
        [
            new RibbonGroup("document", "Dokument",
            [
                new RibbonItem(CommonCommandIds.New, RibbonItemSize.Large),
                new RibbonItem(CommonCommandIds.Open),
                new RibbonItem(CommonCommandIds.Save),
            ]),
            new RibbonGroup("edit", "Bearbeiten",
            [
                new RibbonItem(CommonCommandIds.Undo),
                new RibbonItem(CommonCommandIds.Redo),
            ]),
        ]),
        new RibbonTab("insert", "Einfügen",
        [
            new RibbonGroup("text", "Text",
            [
                new RibbonItem(InsertDateTime, RibbonItemSize.Large),
            ]),
        ]),
        new RibbonTab("view", "Ansicht",
        [
            new RibbonGroup("ribbon", "Menüband",
            [
                new RibbonItem(CommonCommandIds.ToggleRibbon),
            ]),
            new RibbonGroup("program", "Programm",
            [
                new RibbonItem(CommonCommandIds.Settings),
                new RibbonItem(CommonCommandIds.About),
            ]),
        ]),
    ]);

    /// <summary>ThixWrite's commands of a window (the shell's are there already).</summary>
    public static void AddCommands(OfficeWindow window)
    {
        window.Commands.Add(new OfficeCommand(InsertDateTime, "Datum und Uhrzeit", () =>
        {
            if (window.Editor is PlaceholderEditor editor)
            {
                editor.InsertAtCaret(DateTime.Now.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
            }
        })
        {
            Icon = "clock",
            Shortcut = "Alt+Shift+D",
            Description = "Fügt das heutige Datum und die Uhrzeit an der Einfügemarke ein.",
            Keywords = ["datum", "uhrzeit", "heute", "einfügen", "zeit"],
        });
    }
}

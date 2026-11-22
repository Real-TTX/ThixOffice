using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixMail.Editor;

/// <summary>
/// ThixMail's ribbon like the German Outlook's: Datei | Start (Neu, Löschen, Antworten, Verschieben, Kategorien/Kennzeichnung, Suchen) |
/// Senden/Empfangen | Ordner | Ansicht (Lesebereich, Unterhaltungen, Dichte, Layout). Every item's id is a command id (<see cref="MailCommandIds"/>).
/// </summary>
public static class MailRibbon
{
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(HomeTab(), SendReceiveTab(), FolderTab(), ViewTab());
        return definition;
    }

    private static MatRibbonTab HomeTab() =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("new", "Neu") { Icon = "mail" }.Add(
                Button(MailCommandIds.NewMail, MatRibbonSize.Large)),
            new MatRibbonGroup("delete", "Löschen") { Icon = "trash", ShrinkOrder = 4 }.Add(
                Button(MailCommandIds.Delete, MatRibbonSize.Large),
                Button(MailCommandIds.Archive, MatRibbonSize.Large),
                Button(CommonCommandIds.Undo, MatRibbonSize.Small)),
            new MatRibbonGroup("respond", "Antworten") { Icon = "back", ShrinkOrder = 3 }.Add(
                Button(MailCommandIds.Reply, MatRibbonSize.Large),
                Button(MailCommandIds.ReplyAll, MatRibbonSize.Large),
                Button(MailCommandIds.Forward, MatRibbonSize.Large)),
            new MatRibbonGroup("move", "Verschieben") { Icon = "folder", ShrinkOrder = 2 }.Add(
                Button(MailCommandIds.Move, MatRibbonSize.Large)),
            new MatRibbonGroup("tags", "Kategorien") { Icon = "tag", ShrinkOrder = 1 }.Add(
                Button(MailCommandIds.MarkRead),
                Button(MailCommandIds.MarkUnread),
                Button(MailCommandIds.Categorize),
                Toggle(MailCommandIds.Flag, size: MatRibbonSize.Large)),
            new MatRibbonGroup("find", "Suchen") { Icon = "search", ShrinkOrder = 5 }.Add(
                Button(MailCommandIds.Search, MatRibbonSize.Large)));

    private static MatRibbonTab SendReceiveTab() =>
        new MatRibbonTab("sendreceive", "Senden/Empfangen").Add(
            new MatRibbonGroup("sendreceive", "Senden und Empfangen") { Icon = "refresh" }.Add(
                Button(MailCommandIds.SendReceive, MatRibbonSize.Large)),
            new MatRibbonGroup("preferences", "Einstellungen") { Icon = "power" }.Add(
                Toggle(MailCommandIds.WorkOffline, size: MatRibbonSize.Large)),
            new MatRibbonGroup("accounts", "Konten") { Icon = "user" }.Add(
                Button(MailCommandIds.AddAccount, MatRibbonSize.Large)));

    private static MatRibbonTab FolderTab() =>
        new MatRibbonTab("folder", "Ordner").Add(
            new MatRibbonGroup("new", "Neu") { Icon = "folder-open" }.Add(
                Button(MailCommandIds.NewFolder, MatRibbonSize.Large)),
            new MatRibbonGroup("actions", "Aktionen") { Icon = "edit" }.Add(
                Button(MailCommandIds.RenameFolder),
                Button(MailCommandIds.DeleteFolder)),
            new MatRibbonGroup("clean", "Aufräumen") { Icon = "broom" }.Add(
                Button(MailCommandIds.MarkAllRead, MatRibbonSize.Large),
                Button(MailCommandIds.EmptyFolder, MatRibbonSize.Large)));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("pane", "Lesebereich") { Icon = "columns" }.Add(
                Toggle(MailCommandIds.PaneRight, "pane", MatRibbonSize.Large),
                Toggle(MailCommandIds.PaneBottom, "pane", MatRibbonSize.Large),
                Toggle(MailCommandIds.PaneOff, "pane", MatRibbonSize.Large)),
            new MatRibbonGroup("messages", "Nachrichten") { Icon = "message-square" }.Add(
                Toggle(MailCommandIds.Conversations, size: MatRibbonSize.Large)),
            new MatRibbonGroup("density", "Dichte") { Icon = "list" }.Add(
                Toggle(MailCommandIds.DensityComfortable, "density"),
                Toggle(MailCommandIds.DensityCompact, "density")),
            new MatRibbonGroup("layout", "Layout") { Icon = "sidebar" }.Add(
                Toggle(MailCommandIds.FolderPane, size: MatRibbonSize.Large),
                Toggle(CommonCommandIds.ToggleRibbon)));
}

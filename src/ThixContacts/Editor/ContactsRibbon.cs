using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixContacts.Editor;

/// <summary>
/// ThixContacts' ribbon like the German Outlook's "Personen": Datei | Start (Neu, Löschen, Kommunizieren, Kategorien, Suchen, Synchronisieren) |
/// Ordner (Kontaktgruppen, Mitglieder, Aufräumen, Importieren/Exportieren) | Ansicht (Anzeigen, Sortieren, Layout). Every item's id is a command id
/// (<see cref="ContactsCommandIds"/>); labels, icons, shortcuts and tooltips come from the commands.
/// </summary>
public static class ContactsRibbon
{
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(HomeTab(), FolderTab(), ViewTab());
        return definition;
    }

    private static MatRibbonTab HomeTab() =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("new", "Neu") { Icon = "plus" }.Add(
                Button(ContactsCommandIds.New, MatRibbonSize.Large),
                Button(ContactsCommandIds.NewGroup, MatRibbonSize.Large)),
            new MatRibbonGroup("delete", "Löschen") { Icon = "trash", ShrinkOrder = 3 }.Add(
                Button(ContactsCommandIds.Delete, MatRibbonSize.Large),
                Button(CommonCommandIds.Undo, MatRibbonSize.Small),
                Button(CommonCommandIds.Redo, MatRibbonSize.Small)),
            new MatRibbonGroup("communicate", "Kommunizieren") { Icon = "mail", ShrinkOrder = 2 }.Add(
                Button(ContactsCommandIds.Email, MatRibbonSize.Large),
                Button(ContactsCommandIds.Edit),
                Button(ContactsCommandIds.Merge)),
            new MatRibbonGroup("tags", "Kategorien") { Icon = "tag", ShrinkOrder = 1 }.Add(
                new MatRibbonMenuButton("contacts.categorizeMenu", "Kategorisieren", "tag",
                    Button(ContactsCommandIds.Categorize), Button(ContactsCommandIds.Uncategorize), Button(ContactsCommandIds.ClearCategories)) { Size = MatRibbonSize.Large },
                Toggle(ContactsCommandIds.Favorite, size: MatRibbonSize.Large)),
            new MatRibbonGroup("find", "Suchen") { Icon = "search", ShrinkOrder = 4 }.Add(
                Button(ContactsCommandIds.Search, MatRibbonSize.Large)),
            new MatRibbonGroup("sync", "ThixCloud") { Icon = "refresh", ShrinkOrder = 5 }.Add(
                Button(ContactsCommandIds.Sync, MatRibbonSize.Large)));

    private static MatRibbonTab FolderTab() =>
        new MatRibbonTab("folder", "Ordner").Add(
            new MatRibbonGroup("groups", "Kontaktgruppen") { Icon = "users" }.Add(
                Button(ContactsCommandIds.RenameGroup, MatRibbonSize.Large),
                Button(ContactsCommandIds.DeleteGroup)),
            new MatRibbonGroup("members", "Mitglieder") { Icon = "user-circle" }.Add(
                Button(ContactsCommandIds.AddMembers, MatRibbonSize.Large),
                Button(ContactsCommandIds.RemoveMembers),
                Button(ContactsCommandIds.EmailGroup)),
            new MatRibbonGroup("tidy", "Aufräumen") { Icon = "copy" }.Add(
                Button(ContactsCommandIds.Duplicates, MatRibbonSize.Large)),
            new MatRibbonGroup("transfer", "Importieren/Exportieren") { Icon = "upload" }.Add(
                Button(ContactsCommandIds.Import, MatRibbonSize.Large),
                Button(ContactsCommandIds.Export, MatRibbonSize.Large),
                Button(ContactsCommandIds.Print)));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("show", "Anzeigen") { Icon = "users" }.Add(
                Toggle(ContactsCommandIds.ShowAll, "show", MatRibbonSize.Large),
                Toggle(ContactsCommandIds.ShowFavorites, "show", MatRibbonSize.Large)),
            new MatRibbonGroup("sort", "Sortieren nach") { Icon = "sort" }.Add(
                Toggle(ContactsCommandIds.SortGiven, "sort"),
                Toggle(ContactsCommandIds.SortFamily, "sort"),
                Toggle(ContactsCommandIds.SortCompany, "sort")),
            new MatRibbonGroup("layout", "Layout") { Icon = "sidebar" }.Add(
                Toggle(ContactsCommandIds.ShowFolders, size: MatRibbonSize.Large),
                Toggle(ContactsCommandIds.ShowCard, size: MatRibbonSize.Large),
                Toggle(ContactsCommandIds.ShowIndex, size: MatRibbonSize.Large)),
            new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(
                Toggle(CommonCommandIds.ToggleRibbon)));
}

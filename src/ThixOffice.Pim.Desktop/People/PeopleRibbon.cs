using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixOffice.Pim.Desktop.People;

/// <summary>
/// The ribbon of Outlook's "Personen": Start (Neu, Löschen, Kommunizieren, Kategorien, Suchen, Synchronisieren) | Ordner (Kontaktgruppen, Mitglieder,
/// Aufräumen, Importieren/Exportieren) | Ansicht (Anzeigen, Sortieren, Layout). ThixContacts' whole ribbon (<see cref="Create"/>), and ThixMail's
/// contextual tabs while its view "Personen" is active (<see cref="Tabs"/>). Every item's id is a command id (<see cref="ContactsCommandIds"/>); labels,
/// icons, shortcuts and tooltips come from the commands; items whose command the window lacks (Drucken, Synchronisieren) are left out by the binding.
/// </summary>
public static class PeopleRibbon
{
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add([.. Tabs()]);
        return definition;
    }

    /// <summary>
    /// The three tabs. <paramref name="context"/>: contextual tabs (ThixMail), their ids prefixed with it and labelled "Personen", "Kontaktgruppen",
    /// "Personenansicht", without the items the host's own tabs have already (Rückgängig, Menüband).
    /// </summary>
    public static IReadOnlyList<MatRibbonTab> Tabs(string? context = null)
    {
        var hosted = context is not null;
        var home = new MatRibbonTab(Id("home"), hosted ? "Personen" : "Start") { Context = context }.Add(
            new MatRibbonGroup("new", "Neu") { Icon = "plus" }.Add(
                Button(ContactsCommandIds.New, MatRibbonSize.Large),
                Button(ContactsCommandIds.NewGroup, MatRibbonSize.Large)),
            hosted
                ? new MatRibbonGroup("delete", "Löschen") { Icon = "trash", ShrinkOrder = 3 }.Add(Button(ContactsCommandIds.Delete, MatRibbonSize.Large))
                : new MatRibbonGroup("delete", "Löschen") { Icon = "trash", ShrinkOrder = 3 }.Add(
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

        var folder = new MatRibbonTab(Id("folder"), hosted ? "Kontaktgruppen" : "Ordner") { Context = context }.Add(
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

        var layout = new MatRibbonGroup("layout", "Layout") { Icon = "sidebar" }.Add(
            Toggle(ContactsCommandIds.ShowFolders, size: MatRibbonSize.Large),
            Toggle(ContactsCommandIds.ShowCard, size: MatRibbonSize.Large),
            Toggle(ContactsCommandIds.ShowIndex, size: MatRibbonSize.Large));
        var view = new MatRibbonTab(Id("view"), hosted ? "Personenansicht" : "Ansicht") { Context = context }.Add(
            new MatRibbonGroup("show", "Anzeigen") { Icon = "users" }.Add(
                Toggle(ContactsCommandIds.ShowAll, "show", MatRibbonSize.Large),
                Toggle(ContactsCommandIds.ShowFavorites, "show", MatRibbonSize.Large)),
            new MatRibbonGroup("sort", "Sortieren nach") { Icon = "sort" }.Add(
                Toggle(ContactsCommandIds.SortGiven, "sort"),
                Toggle(ContactsCommandIds.SortFamily, "sort"),
                Toggle(ContactsCommandIds.SortCompany, "sort")),
            layout);
        if (!hosted)
        {
            view.Add(new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(Toggle(CommonCommandIds.ToggleRibbon)));
        }

        return [home, folder, view];

        string Id(string id) => context is null ? id : $"{context}.{id}";
    }
}

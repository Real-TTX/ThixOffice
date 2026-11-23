using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixOffice.Pim.Desktop.People;

/// <summary>
/// The ribbon of Outlook's "Personen": Start (Neu, Löschen, Kommunizieren, Kategorien, Suchen, Synchronisieren) | Ordner (Kontaktgruppen, Mitglieder,
/// Aufräumen, Importieren/Exportieren) | Ansicht (Anzeigen, Sortieren, Layout). ThixContacts' whole ribbon (<see cref="Create"/>), and the tabs of
/// ThixMail's module "Personen" (<see cref="Tabs"/> with a prefix: the window shows them instead of the mail tabs while Personen is active, like Outlook).
/// Every item's id is a command id (<see cref="ContactsCommandIds"/>); labels, icons, shortcuts and tooltips come from the commands; items whose command
/// the window lacks (Drucken, Besprechung) are left out by the binding.
/// </summary>
public static class PeopleRibbon
{
    /// <summary>"Besprechung" with the selected people: a command only a host with a calendar registers (ThixMail).</summary>
    public const string MeetingCommandId = "contacts.meeting";

    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add([.. Tabs()]);
        return definition;
    }

    /// <summary>
    /// The three tabs Start | Ordner | Ansicht. <paramref name="prefix"/>: hosted in another app's window (ThixMail) – tab and group ids prefixed with it,
    /// without what the host has elsewhere (Rückgängig/Wiederholen in the quick access, Synchronisieren automatic and on F9, Menüband in the host's own
    /// group).
    /// </summary>
    public static IReadOnlyList<MatRibbonTab> Tabs(string? prefix = null)
    {
        var hosted = prefix is not null;
        var home = new MatRibbonTab(Id("home"), "Start").Add(
            new MatRibbonGroup(Id("new"), "Neu") { Icon = "plus", ShrinkOrder = 9 }.Add(
                Button(ContactsCommandIds.New, MatRibbonSize.Large),
                Button(ContactsCommandIds.NewGroup, MatRibbonSize.Large)),
            hosted
                ? new MatRibbonGroup(Id("delete"), "Löschen") { Icon = "trash", ShrinkOrder = 3 }.Add(Button(ContactsCommandIds.Delete, MatRibbonSize.Large))
                : new MatRibbonGroup(Id("delete"), "Löschen") { Icon = "trash", ShrinkOrder = 3 }.Add(
                    Button(ContactsCommandIds.Delete, MatRibbonSize.Large),
                    Button(CommonCommandIds.Undo, MatRibbonSize.Small),
                    Button(CommonCommandIds.Redo, MatRibbonSize.Small)),
            new MatRibbonGroup(Id("communicate"), "Kommunizieren") { Icon = "mail", ShrinkOrder = 2 }.Add(
                Button(ContactsCommandIds.Email, MatRibbonSize.Large),
                Button(MeetingCommandId, MatRibbonSize.Large),
                Button(ContactsCommandIds.Edit),
                Button(ContactsCommandIds.Merge)),
            new MatRibbonGroup(Id("tags"), "Kategorien") { Icon = "tag", ShrinkOrder = 1 }.Add(
                new MatRibbonMenuButton(hosted ? $"{prefix}.categorizeMenu" : "contacts.categorizeMenu", "Kategorisieren", "tag",
                    Button(ContactsCommandIds.Categorize), Button(ContactsCommandIds.Uncategorize), Button(ContactsCommandIds.ClearCategories)) { Size = MatRibbonSize.Large },
                Toggle(ContactsCommandIds.Favorite, size: MatRibbonSize.Large)),
            new MatRibbonGroup(Id("find"), "Suchen") { Icon = "search", ShrinkOrder = 4 }.Add(
                Button(ContactsCommandIds.Search, MatRibbonSize.Large)));
        if (!hosted)
        {
            home.Add(new MatRibbonGroup("sync", "ThixCloud") { Icon = "refresh", ShrinkOrder = 5 }.Add(Button(ContactsCommandIds.Sync, MatRibbonSize.Large)));
        }

        var folder = new MatRibbonTab(Id("folder"), "Ordner").Add(
            new MatRibbonGroup(Id("groups"), "Kontaktgruppen") { Icon = "users" }.Add(
                Button(ContactsCommandIds.RenameGroup, MatRibbonSize.Large),
                Button(ContactsCommandIds.DeleteGroup)),
            new MatRibbonGroup(Id("members"), "Mitglieder") { Icon = "user-circle" }.Add(
                Button(ContactsCommandIds.AddMembers, MatRibbonSize.Large),
                Button(ContactsCommandIds.RemoveMembers),
                Button(ContactsCommandIds.EmailGroup)),
            new MatRibbonGroup(Id("tidy"), "Aufräumen") { Icon = "copy" }.Add(
                Button(ContactsCommandIds.Duplicates, MatRibbonSize.Large)),
            new MatRibbonGroup(Id("transfer"), "Importieren/Exportieren") { Icon = "upload" }.Add(
                Button(ContactsCommandIds.Import, MatRibbonSize.Large),
                Button(ContactsCommandIds.Export, MatRibbonSize.Large),
                Button(ContactsCommandIds.Print)));

        var layout = new MatRibbonGroup(Id("layout"), "Layout") { Icon = "sidebar" }.Add(
            Toggle(ContactsCommandIds.ShowFolders, size: MatRibbonSize.Large),
            Toggle(ContactsCommandIds.ShowCard, size: MatRibbonSize.Large),
            Toggle(ContactsCommandIds.ShowIndex, size: MatRibbonSize.Large));
        var view = new MatRibbonTab(Id("view"), "Ansicht").Add(
            new MatRibbonGroup(Id("show"), "Anzeigen") { Icon = "users" }.Add(
                Toggle(ContactsCommandIds.ShowAll, "show", MatRibbonSize.Large),
                Toggle(ContactsCommandIds.ShowFavorites, "show", MatRibbonSize.Large)),
            new MatRibbonGroup(Id("sort"), "Sortieren nach") { Icon = "sort" }.Add(
                Toggle(ContactsCommandIds.SortGiven, "sort"),
                Toggle(ContactsCommandIds.SortFamily, "sort"),
                Toggle(ContactsCommandIds.SortCompany, "sort")),
            layout);
        if (!hosted)
        {
            view.Add(new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(Toggle(CommonCommandIds.ToggleRibbon)));
        }

        return [home, folder, view];

        string Id(string id) => prefix is null ? id : $"{prefix}.{id}";
    }
}

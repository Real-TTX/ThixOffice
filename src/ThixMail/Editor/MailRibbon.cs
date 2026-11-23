using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop.People;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixMail.Editor;

/// <summary>
/// ThixMail's ribbon like the German Outlook's, per module – the window shows the tabs of the active one (<see cref="ModuleOf"/>,
/// <see cref="OfficeRibbonBinding.ShowTabs"/>):
/// <list type="bullet">
/// <item>E-Mail: Start (Neu, Löschen, Antworten, Verschieben + Regeln, Kategorien, Suchen) | Senden/Empfangen | Ordner | Ansicht.</item>
/// <item>Kalender: Start (Neu, Gehe zu, Anordnen, Aktionen, Kalender verwalten) | Ansicht (<see cref="CalendarCommands.Tabs"/>).</item>
/// <item>Personen: Start | Ordner | Ansicht – ThixContacts' tabs (<see cref="PeopleRibbon.Tabs"/>) with "Neue Elemente" and the navigation.</item>
/// </list>
/// Every item's id is a command id (<see cref="MailCommandIds"/>); a command in a second module's tabs has an alias (<see cref="MailNewItems.AddAliases"/>).
/// The main commands are large and the groups shrink in Outlook's order (Suchen first, Neu last); the Kategorien keep their labels.
/// </summary>
public static class MailRibbon
{
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(HomeTab(), SendReceiveTab(), FolderTab(), ViewTab());
        definition.Add([.. CalendarCommands.Tabs()]);
        definition.Add([.. PeopleTabs()]);
        return definition;
    }

    /// <summary>The module a tab belongs to (by its id: "calendar.…", "people.…", else E-Mail).</summary>
    public static MailArea ModuleOf(string tabId) =>
        tabId.StartsWith("calendar.", StringComparison.Ordinal) ? MailArea.Calendar
        : tabId.StartsWith("people.", StringComparison.Ordinal) ? MailArea.People
        : MailArea.Mail;

    /// <summary>The Start tab of a module.</summary>
    public static string HomeTabOf(MailArea module) => module switch
    {
        MailArea.Calendar => "calendar.home",
        MailArea.People => "people.home",
        _ => "home",
    };

    /// <summary>Ansicht › Navigation: E-Mail, Kalender, Personen (<paramref name="module"/>: the alias suffix of another module's tab, null for E-Mail).</summary>
    public static MatRibbonGroup NavigationGroup(string? module) =>
        new MatRibbonGroup(module is null ? "view.navigation" : $"{module}.view.navigation", "Navigation") { Icon = "compass" }.Add(
            Toggle(Id(CalendarCommandIds.ShowMail, module), "navigation", MatRibbonSize.Large),
            Toggle(Id(CalendarCommandIds.ShowCalendar, module), "navigation", MatRibbonSize.Large),
            Toggle(Id(CalendarCommandIds.ShowPeople, module), "navigation", MatRibbonSize.Large));

    /// <summary>Ansicht › Menüband: reduce the ribbon.</summary>
    public static MatRibbonGroup RibbonGroup(string? module) =>
        new MatRibbonGroup(module is null ? "view.ribbon" : $"{module}.view.ribbon", "Menüband") { Icon = "chevron-up" }.Add(
            Toggle(Id(CommonCommandIds.ToggleRibbon, module), size: MatRibbonSize.Large));

    /// <summary>An item that keeps its label in a narrow window (Outlook's Kategorien: "Ungelesen/Gelesen", "Kategorisieren", "Zur Nachverfolgung").</summary>
    private static T KeepLabel<T>(T item) where T : MatRibbonButton
    {
        item.MinSize = MatRibbonSize.Medium;
        return item;
    }

    private static string Id(string id, string? module) => module is null ? id : $"{id}.{module}";

    private static MatRibbonTab HomeTab() =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("home.new", "Neu") { Icon = "mail", ShrinkOrder = 9 }.Add(
                Button(MailCommandIds.NewMail, MatRibbonSize.Large),
                MailNewItems.MenuOf(null)),
            new MatRibbonGroup("home.delete", "Löschen") { Icon = "trash", ShrinkOrder = 5 }.Add(
                Button(MailCommandIds.Delete, MatRibbonSize.Large),
                Button(MailCommandIds.Archive, MatRibbonSize.Large)),
            new MatRibbonGroup("home.respond", "Antworten") { Icon = "back", ShrinkOrder = 4 }.Add(
                Button(MailCommandIds.Reply, MatRibbonSize.Large),
                Button(MailCommandIds.ReplyAll, MatRibbonSize.Large),
                Button(MailCommandIds.Forward, MatRibbonSize.Large)),
            new MatRibbonGroup("home.move", "Verschieben") { Icon = "folder", ShrinkOrder = 2 }.Add(
                Button(MailCommandIds.Move, MatRibbonSize.Large),
                new MatRibbonMenuButton(MailCommandIds.RulesMenu, "Regeln", "filter",
                    Button(MailCommandIds.CreateRule), Button(MailCommandIds.ManageRules), new MatRibbonSeparator(), Button(MailCommandIds.RunRules))
                {
                    Size = MatRibbonSize.Large,
                    ToolTip = "Regeln erstellen, verwalten und jetzt anwenden – sie ordnen neue E-Mails von selbst.",
                }),
            new MatRibbonGroup("home.tags", "Kategorien") { Icon = "tag", ShrinkOrder = 3 }.Add(
                KeepLabel(Button(MailCommandIds.ToggleRead)),
                KeepLabel(Button(MailCommandIds.Categorize)),
                KeepLabel(Toggle(MailCommandIds.Flag))),
            new MatRibbonGroup("home.find", "Suchen") { Icon = "search", ShrinkOrder = 1 }.Add(
                Button(MailCommandIds.Search),
                Button(MailCommandIds.AddSenderToContacts)));

    private static MatRibbonTab SendReceiveTab() =>
        new MatRibbonTab("sendreceive", "Senden/Empfangen").Add(
            new MatRibbonGroup("sendreceive.all", "Senden und Empfangen") { Icon = "refresh" }.Add(
                Button(MailCommandIds.SendReceive, MatRibbonSize.Large)),
            new MatRibbonGroup("sendreceive.preferences", "Einstellungen") { Icon = "power" }.Add(
                Toggle(MailCommandIds.WorkOffline, size: MatRibbonSize.Large)),
            new MatRibbonGroup("sendreceive.accounts", "Konten") { Icon = "user" }.Add(
                Button(MailCommandIds.AddAccount, MatRibbonSize.Large)));

    private static MatRibbonTab FolderTab() =>
        new MatRibbonTab("folder", "Ordner").Add(
            new MatRibbonGroup("folder.new", "Neu") { Icon = "folder-open" }.Add(
                Button(MailCommandIds.NewFolder, MatRibbonSize.Large)),
            new MatRibbonGroup("folder.actions", "Aktionen") { Icon = "edit" }.Add(
                Button(MailCommandIds.RenameFolder),
                Button(MailCommandIds.DeleteFolder)),
            new MatRibbonGroup("folder.clean", "Aufräumen") { Icon = "broom" }.Add(
                Button(MailCommandIds.MarkAllRead, MatRibbonSize.Large),
                Button(MailCommandIds.EmptyFolder, MatRibbonSize.Large)));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("view.pane", "Lesebereich") { Icon = "columns" }.Add(
                Toggle(MailCommandIds.PaneRight, "pane", MatRibbonSize.Large),
                Toggle(MailCommandIds.PaneBottom, "pane", MatRibbonSize.Large),
                Toggle(MailCommandIds.PaneOff, "pane", MatRibbonSize.Large)),
            new MatRibbonGroup("view.messages", "Nachrichten") { Icon = "message-square" }.Add(
                Toggle(MailCommandIds.Conversations, size: MatRibbonSize.Large)),
            new MatRibbonGroup("view.density", "Dichte") { Icon = "list" }.Add(
                Toggle(MailCommandIds.DensityComfortable, "density"),
                Toggle(MailCommandIds.DensityCompact, "density")),
            new MatRibbonGroup("view.layout", "Layout") { Icon = "sidebar" }.Add(
                Toggle(MailCommandIds.FolderPane, size: MatRibbonSize.Large)),
            NavigationGroup(null),
            RibbonGroup(null));

    /// <summary>ThixContacts' tabs in the module "Personen", with "Neue Elemente" in Neu and the navigation and the ribbon in Ansicht.</summary>
    private static IReadOnlyList<MatRibbonTab> PeopleTabs()
    {
        var tabs = PeopleRibbon.Tabs("people");
        tabs[0].Groups[0].Items.Add(MailNewItems.MenuOf("people"));
        tabs[2].Add(NavigationGroup("people"), RibbonGroup("people"));
        return tabs;
    }
}

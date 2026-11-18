namespace ThixOffice.Core.Commands;

/// <summary>How much room a command takes in a ribbon group.</summary>
public enum RibbonItemSize
{
    /// <summary>A small button with icon and label in a column of three.</summary>
    Small = 0,

    /// <summary>A large button: the icon above the label, the full height of the group.</summary>
    Large = 1,
}

/// <summary>One command in a group (by id: the ribbon finds it in the window's <see cref="OfficeCommandRegistry"/>).</summary>
public sealed record RibbonItem(string CommandId, RibbonItemSize Size = RibbonItemSize.Small);

/// <summary>A labelled group of a tab ("Zwischenablage", "Schriftart").</summary>
public sealed record RibbonGroup(string Id, string Header, IReadOnlyList<RibbonItem> Items);

/// <summary>A tab of the ribbon ("Start", "Einfügen"). "Datei" is no tab: it opens the backstage.</summary>
public sealed record RibbonTab(string Id, string Header, IReadOnlyList<RibbonGroup> Groups);

/// <summary>
/// The ribbon of an app, declared as data (plan, section 4): the desktop ribbon (MatRibbon) and later the web apps draw the
/// same definition. Every item names a command id; a command the window does not have is left out (and reported by the tests).
/// </summary>
public sealed record RibbonLayout(IReadOnlyList<RibbonTab> Tabs)
{
    /// <summary>Every command id the layout names, in order, without repeats.</summary>
    public IReadOnlyList<string> CommandIds =>
        Tabs.SelectMany(tab => tab.Groups).SelectMany(group => group.Items).Select(item => item.CommandId).Distinct(StringComparer.Ordinal).ToList();
}

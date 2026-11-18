using Avalonia.Controls;
using ThixOffice.Core.Commands;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// The ribbon slot of a document window (below the title bar). It draws the window's <see cref="RibbonLayout"/> and runs
/// the commands of its <see cref="OfficeCommandRegistry"/> by id; "Datei" opens the backstage.
/// Now: <see cref="TemporaryCommandBar"/> (a placeholder). Next: an adapter around MatRibbon of MatDesktop 0.3.0 –
/// set <see cref="OfficeShellParts.CreateRibbon"/> and nothing else in the shell changes.
/// </summary>
public interface IOfficeRibbon
{
    /// <summary>The control that stands in the slot.</summary>
    Control View { get; }

    /// <summary>The tab shown (null: none, e.g. while collapsed and nothing is open).</summary>
    string? SelectedTabId { get; set; }

    /// <summary>Only the tab headers show (Strg+F1); remembered in the settings by the window.</summary>
    bool IsCollapsed { get; set; }

    /// <summary>"Datei" was chosen: the window opens the backstage.</summary>
    event EventHandler? FileTabInvoked;

    /// <summary>The user folded or unfolded the ribbon (its own button); the value is <see cref="IsCollapsed"/>.</summary>
    event EventHandler<bool>? CollapsedChanged;

    /// <summary>Draws <paramref name="layout"/>; every item runs <paramref name="commands"/>[id] and follows its enabled / checked state.</summary>
    void Bind(RibbonLayout layout, OfficeCommandRegistry commands);
}

/// <summary>Where a text of the status bar stands.</summary>
public enum StatusBarSide
{
    Left = 0,
    Right = 1,
}

/// <summary>
/// The status bar slot at the foot of a document window: texts by id ("Wörter: 120" left, "100 %" right). The editor
/// writes them (<see cref="OfficeEditor.UpdateStatus"/>). Now: <see cref="TemporaryStatusBar"/>. Next: MatStatusBar of
/// MatDesktop 0.3.0 behind <see cref="OfficeShellParts.CreateStatusBar"/>.
/// </summary>
public interface IOfficeStatusBar
{
    Control View { get; }

    /// <summary>Shows or replaces the text with this id (a new id is added at the end of its side).</summary>
    void SetItem(string id, string text, StatusBarSide side = StatusBarSide.Left);

    void RemoveItem(string id);

    /// <summary>The text with this id, or null (tests).</summary>
    string? TextOf(string id);
}

/// <summary>
/// An entry of the backstage's menu: a page (<see cref="CreatePage"/>, shown beside the menu) or an action (<see cref="Run"/>:
/// "Speichern", "Schließen" – it runs and the backstage closes).
/// </summary>
public sealed record BackstageItem(string Id, string Label, string Icon, Func<Control>? CreatePage = null, Func<Task>? Run = null, string? Shortcut = null)
{
    public bool IsPage => CreatePage is not null;
}

/// <summary>What a backstage shows: the entries of its menu, Einstellungen below a line, and Info behind the version at the foot.</summary>
public sealed record BackstageContent(IReadOnlyList<BackstageItem> Items, BackstageItem Settings, BackstageItem About, string Version, string Channel);

/// <summary>
/// "Datei" – the backstage over the whole window below the title bar: Neu, Öffnen, Speichern, Speichern unter, Schließen,
/// Einstellungen, and at the foot the version that opens Info. Now: <see cref="TemporaryBackstage"/> (a page host beside a
/// MatNav). Next: MatBackstage of MatDesktop 0.3.0 behind <see cref="OfficeShellParts.CreateBackstage"/>; the pages stay.
/// </summary>
public interface IOfficeBackstage
{
    Control View { get; }

    bool IsOpen { get; }

    /// <summary>The page shown (an item id), or null while closed.</summary>
    string? CurrentPageId { get; }

    /// <summary>The control of the page shown (tests, snapshots).</summary>
    Control? CurrentPage { get; }

    /// <summary>Opened or closed (the window hides the ribbon and the editor behind it and gives the focus back).</summary>
    event EventHandler<bool>? OpenChanged;

    void Configure(BackstageContent content);

    /// <summary>Opens on the page with this id (null: the first page; an action id runs it).</summary>
    void Open(string? pageId = null);

    void Close();
}

/// <summary>
/// The three parts that MatDesktop 0.3.0 brings (MatRibbon, MatBackstage, MatStatusBar), as factories: every document
/// window takes its parts from here. Today they make the clearly marked placeholders; when the copy of MatDesktop 0.3.0 is
/// in, an adapter per part is set here once (in OfficeApplication) and the windows, pages and commands stay as they are.
/// </summary>
public static class OfficeShellParts
{
    public static Func<IOfficeRibbon> CreateRibbon { get; set; } = () => new TemporaryCommandBar();

    public static Func<IOfficeBackstage> CreateBackstage { get; set; } = () => new TemporaryBackstage();

    public static Func<IOfficeStatusBar> CreateStatusBar { get; set; } = () => new TemporaryStatusBar();
}

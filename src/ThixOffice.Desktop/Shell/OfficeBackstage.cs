using Avalonia.Controls;
using MatDesktop;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// An entry of the backstage: a page (<see cref="CreatePage"/>, made anew each time it is shown, so that "Zuletzt
/// verwendet" or "Speichern unter" show what is true now) or an action (<see cref="Run"/>: "Speichern", "Schließen" – it
/// runs and the backstage closes).
/// </summary>
public sealed record BackstageItem(string Id, string Label, string Icon, Func<Control>? CreatePage = null, Func<Task>? Run = null, string? Shortcut = null)
{
    public bool IsPage => CreatePage is not null;
}

/// <summary>What a backstage shows: the entries, Einstellungen below the line, and Info behind the version at the foot.</summary>
public sealed record BackstageContent(IReadOnlyList<BackstageItem> Items, BackstageItem Settings, BackstageItem About, string Version, string Channel);

/// <summary>
/// "Datei": MatDesktop's <see cref="MatBackstage"/> over the window below the title bar – the arrow back, Neu, Öffnen,
/// Speichern, Speichern unter, Schließen, a line, Einstellungen, and at the foot the version that opens Info – with the
/// shell's pages by id (<see cref="OfficeWindow.NewPageId"/> …). Escape and the arrow close it.
/// </summary>
public sealed class OfficeBackstage
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private BackstageContent? _content;

    public OfficeBackstage()
    {
        View.Opened += (_, _) => OpenChanged?.Invoke(this, true);
        View.Closed += (_, _) =>
        {
            CurrentPageId = null;
            OpenChanged?.Invoke(this, false);
        };
        View.ItemInvoked += (_, navItem) =>
        {
            var entry = _entries.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.NavItem, navItem));
            if (entry?.Item.IsPage == true)
            {
                Show(entry); // a fresh page each time it is chosen
            }
        };
        View.AboutRequested += (_, _) => Open(_content?.About.Id);
    }

    private Control? _aboutPage;

    /// <summary>Opened (true) or closed (false): the window gives the focus back to the document.</summary>
    public event EventHandler<bool>? OpenChanged;

    /// <summary>The control in the window.</summary>
    public MatBackstage View { get; } = new();

    public bool IsOpen => View.IsOpen;

    /// <summary>The id of the page shown, or null while closed.</summary>
    public string? CurrentPageId { get; private set; }

    /// <summary>The page shown (tests, snapshots).</summary>
    public Control? CurrentPage => CurrentPageId is null ? null
        : CurrentPageId == _content?.About.Id ? _aboutPage
        : _entries.TryGetValue(CurrentPageId, out var entry) ? entry.Host.Content as Control : null;

    public void Configure(BackstageContent content)
    {
        _content = content;
        View.AppName = Core.Infrastructure.AppInfo.ProductName;
        View.Version = content.Version;
        View.Channel = content.Channel;
        foreach (var item in content.Items)
        {
            Add(item, lower: false);
        }

        Add(content.Settings, lower: true);

        // Info has no entry of its own: the version at the foot opens it (house rule), shown by MatBackstage.ShowPage.
    }

    /// <summary>
    /// Opens on the page with this id (null: the first page); an action id runs the action instead. <paramref name="byKeyboard"/>:
    /// the keyboard opened it (the file tab by Enter or its key tip, a shortcut) – only then the entry shows the focus ring.
    /// </summary>
    public void Open(string? pageId = null, bool byKeyboard = false)
    {
        if (_content is null)
        {
            return;
        }

        if (pageId == _content.About.Id)
        {
            _aboutPage = _content.About.CreatePage!();
            CurrentPageId = pageId;
            View.ShowPage(_aboutPage, byKeyboard);
            return;
        }

        var entry = pageId is not null && _entries.TryGetValue(pageId, out var found) ? found : _entries[(_content.Items.FirstOrDefault(item => item.IsPage) ?? _content.Settings).Id];
        if (!entry.Item.IsPage)
        {
            Close();
            _ = entry.Item.Run!();
            return;
        }

        Show(entry);
        View.Open(entry.NavItem, byKeyboard);
    }

    public void Close() => View.Close();

    /// <summary>The ids of the pages of the upper part (the app's; a workspace app has only its own), for the self test.</summary>
    public IReadOnlyList<string> PageIds => _content?.Items.Where(item => item.IsPage).Select(item => item.Id).ToList() ?? [];

    private Entry Add(BackstageItem item, bool lower)
    {
        var navItem = new MatNavItem(item.Icon, item.Label, item.Shortcut is null ? null : Ui.ShortcutText(item.Shortcut));
        var entry = new Entry(item, navItem, new ContentControl());
        _entries[item.Id] = entry;
        if (item.IsPage)
        {
            View.AddPage(navItem, entry.Host, lower);
        }
        else
        {
            View.AddCommand(navItem, () => _ = item.Run!(), lower);
        }

        return entry;
    }

    private void Show(Entry entry)
    {
        entry.Host.Content = entry.Item.CreatePage!();
        CurrentPageId = entry.Item.Id;
        View.Select(entry.NavItem);
    }

    private sealed record Entry(BackstageItem Item, MatNavItem NavItem, ContentControl Host);
}

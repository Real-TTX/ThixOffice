using Avalonia;
using Avalonia.Controls;
using MatDesktop;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// PLACEHOLDER until MatBackstage (MatDesktop 0.3.0) is copied in: MatDesktop's menu (MatNav) at the left – "Zurück" first,
/// the entries, Einstellungen below a line, the version at the foot that opens Info (house rule) – and the page beside it.
/// The pages themselves (Pages/*) are real and stay when MatBackstage replaces this host.
/// </summary>
public sealed class TemporaryBackstage : Grid, IOfficeBackstage
{
    public const string BackId = "back";

    private readonly MatNav _menu = new();
    private readonly ContentControl _pageHost = new();
    private readonly Dictionary<string, (BackstageItem Item, MatNavItem NavItem)> _entries = new(StringComparer.Ordinal);
    private BackstageContent? _content;
    private MatNavItem? _currentNavItem;

    public TemporaryBackstage()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        IsVisible = false;
        _menu.FoldButton.IsVisible = false;
        _menu.ItemInvoked += (_, navItem) => Invoke(navItem);
        _menu.AboutRequested += (_, _) =>
        {
            if (_content is not null)
            {
                ShowPage(_content.About, null);
            }
        };

        var pageArea = new Border { Child = _pageHost, ClipToBounds = true }.Use(Border.BackgroundProperty, "Mat.Surface");
        SetColumn(pageArea, 1);
        Children.Add(_menu);
        Children.Add(pageArea);
    }

    public event EventHandler<bool>? OpenChanged;

    public Control View => this;

    public bool IsOpen => IsVisible;

    public string? CurrentPageId { get; private set; }

    public Control? CurrentPage => _pageHost.Content as Control;

    /// <summary>The menu at the left (tests, snapshots).</summary>
    public MatNav Menu => _menu;

    public void Configure(BackstageContent content)
    {
        _content = content;
        _entries.Clear();
        _menu.Items.Clear();
        var back = new MatNavItem("back", "Zurück", "Esc");
        _menu.Items.Add(back);
        _entries[BackId] = (new BackstageItem(BackId, "Zurück", "back", Run: () =>
        {
            Close();
            return Task.CompletedTask;
        }), back);
        foreach (var item in content.Items)
        {
            var navItem = new MatNavItem(item.Icon, item.Label, item.Shortcut is null ? null : Ui.ShortcutText(item.Shortcut));
            _menu.Items.Add(navItem);
            _entries[item.Id] = (item, navItem);
        }

        var settings = new MatNavItem(content.Settings.Icon, content.Settings.Label);
        _menu.Settings = settings;
        _entries[content.Settings.Id] = (content.Settings, settings);
        _menu.AppName = Core.Infrastructure.AppInfo.ProductName;
        _menu.Version = content.Version;
        _menu.Channel = content.Channel;
    }

    public void Open(string? pageId = null)
    {
        if (_content is null)
        {
            return;
        }

        var item = pageId is null
            ? _content.Items.First(entry => entry.IsPage)
            : pageId == _content.About.Id ? _content.About : _entries.TryGetValue(pageId, out var entry) ? entry.Item : _content.Items.First(candidate => candidate.IsPage);
        if (!item.IsPage)
        {
            _ = item.Run!();
            return;
        }

        var wasOpen = IsVisible;
        IsVisible = true;
        ShowPage(item, _entries.TryGetValue(item.Id, out var found) ? found.NavItem : null);
        if (!wasOpen)
        {
            OpenChanged?.Invoke(this, true);
        }
    }

    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        CurrentPageId = null;
        _pageHost.Content = null;
        OpenChanged?.Invoke(this, false);
    }

    private void Invoke(MatNavItem navItem)
    {
        var entry = _entries.Values.FirstOrDefault(pair => ReferenceEquals(pair.NavItem, navItem));
        if (entry.Item is null)
        {
            return;
        }

        if (entry.Item.IsPage)
        {
            ShowPage(entry.Item, navItem);
            return;
        }

        // An action ("Speichern", "Schließen", "Zurück"): the page shown stays chosen in the menu.
        _menu.Select(_currentNavItem);
        _ = RunAsync(entry.Item);
    }

    private async Task RunAsync(BackstageItem item)
    {
        if (item.Id != BackId)
        {
            Close();
        }

        await item.Run!();
    }

    private void ShowPage(BackstageItem item, MatNavItem? navItem)
    {
        _currentNavItem = navItem;
        _menu.Select(navItem);
        CurrentPageId = item.Id;
        _pageHost.Content = item.CreatePage!();
    }
}

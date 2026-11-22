using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixContacts.Core;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;
using ThixOffice.Pim.Sync;

namespace ThixContacts.Editor;

/// <summary>An entry of the folder pane: all contacts, the favourites, a group or a category.</summary>
public sealed record FolderEntry(ContactFilter Filter, string Label, string Icon, int Count, string? Color = null, bool IsHeading = false);

/// <summary>
/// ThixContacts' part of the window, laid out like Outlook's "Personen": on the left the folder pane (Alle Kontakte, Favoriten, the groups, the
/// categories with their colours), in the middle the list (search above, the letters of the alphabet beside it, virtualized for 50,000 contacts,
/// several can be selected), on the right the card of the chosen contact (ThixOffice.Pim.Desktop's <see cref="PersonCard"/>) with Bearbeiten,
/// E-Mail and Favorit. Every change goes through the <see cref="ContactsSession"/> (undoable); the list follows its <c>Changed</c> (also what the
/// sync brought). The status bar says how many contacts there are and how the sync stands.
/// </summary>
public sealed class ContactsEditor : OfficeEditor
{
    public const string CountItem = "contacts.count";
    public const string SyncItem = "contacts.sync";

    private static readonly string[] Letters = [.. "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()), "#"];

    private readonly OfficeWindow _window;
    private readonly Grid _root = new();
    private readonly Border _folderPane = new();
    private readonly Border _cardPane = new();
    private readonly StackPanel _index = new() { Spacing = 0, Margin = new Thickness(2, 6) };
    private readonly Dictionary<string, Button> _letterButtons = [];
    private readonly ContentControl _cardHost = new();
    private readonly StackPanel _empty = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _emptyTitle = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _emptyText = new() { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _selectionText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _editButton;
    private readonly Button _mailButton;
    private readonly Button _favoriteButton;
    private IReadOnlyList<Person> _shown = [];
    private bool _refreshing;
    private bool _refreshQueued;
    private bool _stateQueued;
    private bool _showsSignedOut;

    public ContactsEditor(OfficeWindow window, ContactsDocument document)
    {
        _window = window;
        Document = document;
        _window.StatusBar.View.ShowZoom = false;

        // the folder pane
        Folders = new ListBox { SelectionMode = SelectionMode.Single, ItemTemplate = new FuncDataTemplate<FolderEntry>((entry, _) => entry is null ? new TextBlock() : FolderRow(entry), supportsRecycling: false) };
        Folders.SelectionChanged += (_, _) => OnFolderChosen();
        AutomationProperties.SetName(Folders, "Ordner");
        _folderPane.Child = new DockPanel
        {
            Children =
            {
                new TextBlock { Text = "Meine Kontakte", Margin = new Thickness(16, 14, 12, 6) }.Themed("MatText.SectionTitle"),
                Folders,
            },
        };
        DockPanel.SetDock(((DockPanel)_folderPane.Child).Children[0], Dock.Top);
        _folderPane.Use(Border.BackgroundProperty, "Mat.NavBg");
        _folderPane.Use(Border.BorderBrushProperty, "Mat.Border");
        _folderPane.BorderThickness = new Thickness(0, 0, 1, 0);

        // the list with its search and the letters
        SearchBox = new TextBox { Watermark = "Kontakte durchsuchen", Margin = new Thickness(12, 10, 12, 8), InnerLeftContent = new MatIcon { Icon = "search", Margin = new Thickness(8, 0, 0, 0) } };
        AutomationProperties.SetName(SearchBox, "Kontakte durchsuchen");
        ToolTip.SetTip(SearchBox, Ui.Tooltip("Kontakte durchsuchen", "Ctrl+E"));
        SearchBox.TextChanged += (_, _) => QueueRefresh();
        SearchBox.KeyDown += OnSearchKeyDown;
        List = new ListBox { SelectionMode = SelectionMode.Multiple, ItemTemplate = new FuncDataTemplate<Person>((_, _) => new PersonRow(this)) };
        AutomationProperties.SetName(List, "Kontakte");
        List.SelectionChanged += (_, _) => OnSelectionChanged();
        List.DoubleTapped += async (_, _) => await RunAsync(ContactsCommandIds.Edit);
        List.KeyDown += OnListKeyDown;
        List.ContextMenu = OfficeCommandMenu.Create(window,
        [
            ContactsCommandIds.Edit, ContactsCommandIds.Email, null, ContactsCommandIds.Favorite, ContactsCommandIds.Categorize, ContactsCommandIds.AddMembers,
            ContactsCommandIds.RemoveMembers, ContactsCommandIds.Merge, null, ContactsCommandIds.Export, ContactsCommandIds.Delete,
        ], [ContactsCommandIds.RemoveMembers, ContactsCommandIds.Merge]);
        foreach (var letter in Letters)
        {
            var button = new Button { Content = letter, Padding = new Thickness(4, 0), MinHeight = 0, FontSize = MatTokens.Type.FsXs, HorizontalAlignment = HorizontalAlignment.Center }.Themed("MatButton.Ghost");
            AutomationProperties.SetName(button, "Zu " + letter);
            button.Click += (_, _) => SelectLetter(letter);
            _letterButtons[letter] = button;
            _index.Children.Add(button);
        }

        var emptyNew = new Button { Content = new IconLabel { Icon = "plus", Text = "Neuer Kontakt" } }.Themed("MatButton.Primary");
        emptyNew.Click += async (_, _) => await RunAsync(ContactsCommandIds.New);
        var emptyImport = new Button { Content = new IconLabel { Icon = "upload", Text = "Importieren …" } };
        emptyImport.Click += async (_, _) => await RunAsync(ContactsCommandIds.Import);
        _emptyTitle.Themed("MatText.SectionTitle");
        _emptyText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _empty.Children.Add(new MatIcon { Icon = "users", Size = 40, HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyText);
        _empty.Children.Add(new ActionBar { HorizontalAlignment = HorizontalAlignment.Center, Children = { emptyNew, emptyImport } });

        var listArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        listArea.Children.Add(List);
        listArea.Children.Add(_empty);
        Grid.SetColumn(_index, 1);
        listArea.Children.Add(_index);
        var listPane = new DockPanel();
        DockPanel.SetDock(SearchBox, Dock.Top);
        listPane.Children.Add(SearchBox);
        listPane.Children.Add(listArea);
        var listBorder = new Border { Child = listPane, BorderThickness = new Thickness(0, 0, 1, 0) };
        listBorder.Use(Border.BorderBrushProperty, "Mat.Border");
        listBorder.Use(Border.BackgroundProperty, "Mat.Surface");

        // the card with its actions
        Card = new PersonCard { PhotoSource = id => Document.Session.Photo(id), CategoryColor = CategoryColors.TokenOf };
        _editButton = new Button { Content = new IconLabel { Icon = "edit", Text = "Bearbeiten" } }.Themed("MatButton.Primary");
        _editButton.Click += async (_, _) => await RunAsync(ContactsCommandIds.Edit);
        ToolTip.SetTip(_editButton, Ui.Tooltip("Kontakt bearbeiten", "F2"));
        _mailButton = new Button { Content = new IconLabel { Icon = "mail", Text = "E-Mail" } };
        _mailButton.Click += async (_, _) => await RunAsync(ContactsCommandIds.Email);
        _favoriteButton = new Button { Content = new IconLabel { Icon = "star", Text = "Favorit" } };
        _favoriteButton.Click += async (_, _) => await RunAsync(ContactsCommandIds.Favorite);
        var cardActions = new ActionBar { Margin = new Thickness(24, 16, 24, 0), Children = { _editButton, _mailButton, _favoriteButton } };
        var cardPanel = new DockPanel();
        DockPanel.SetDock(cardActions, Dock.Top);
        cardPanel.Children.Add(cardActions);
        cardPanel.Children.Add(Card);
        _selectionText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _cardHost.Content = cardPanel;
        _cardPane.Child = new Panel { Children = { _cardHost, _selectionText } };
        _cardPane.Use(Border.BackgroundProperty, "Mat.Bg");

        _root.ColumnDefinitions = new ColumnDefinitions("220,360,*");
        Grid.SetColumn(listBorder, 1);
        Grid.SetColumn(_cardPane, 2);
        _root.Children.Add(_folderPane);
        _root.Children.Add(listBorder);
        _root.Children.Add(_cardPane);
        View = _root;

        Document.SessionChanged += OnSessionChanged;
        Document.Changed += OnDocumentChanged;
        Document.SyncStatusChanged += OnSyncStatusChanged;
        CloudService.StatusChanged += OnCloudStatusChanged;
        Sort = Enum.TryParse<PersonSort>(SettingsService.AppValue("contacts.sort"), out var sort) ? sort : PersonSort.GivenName;
        ShowsCard = SettingsService.AppValue("contacts.card") != "false";
        ShowsIndex = SettingsService.AppValue("contacts.index") != "false";
        ShowsFolders = SettingsService.AppValue("contacts.folders") != "false";
        ApplyLayout();
        Refresh();
        Ui.Post(ShowSignedOutMessage);
    }

    /// <summary>The words of the message bar while the cloud has ended the sign-in (the account's contacts stay, changes wait).</summary>
    public const string SignedOutMessage = "Bei ThixCloud abgemeldet – Änderungen werden nach der Anmeldung synchronisiert";

    public ContactsDocument Document { get; }

    public ContactsSession Session => Document.Session;

    public override Control View { get; }

    public ListBox Folders { get; }

    public TextBox SearchBox { get; }

    public ListBox List { get; }

    public PersonCard Card { get; }

    /// <summary>The filter of the folder pane.</summary>
    public ContactFilter Filter { get; private set; } = ContactFilter.All;

    public PersonSort Sort { get; private set; }

    public bool ShowsCard { get; private set; }

    public bool ShowsIndex { get; private set; }

    public bool ShowsFolders { get; private set; }

    /// <summary>The contacts the list shows, in its order.</summary>
    public IReadOnlyList<Person> Shown => _shown;

    /// <summary>The contacts selected (in the order of the list).</summary>
    public IReadOnlyList<Person> Selected => List.SelectedItems?.OfType<Person>().OrderBy(p => IndexOf(p)).ToList() ?? [];

    /// <summary>The contact the card shows (the focused one of the selection).</summary>
    public Person? Current => List.SelectedItem as Person;

    /// <summary>The group of the folder pane, or null.</summary>
    public PersonGroup? CurrentGroup => Filter.Kind == ContactFilterKind.Group ? Session.FindGroup(Filter.Value) : null;

    /// <summary>The letters that have contacts in the list (tests).</summary>
    public IReadOnlyList<string> ActiveLetters => _letterButtons.Where(pair => pair.Value.IsEnabled).Select(pair => pair.Key).ToList();

    // ---- what the commands do ----

    public void SetFilter(ContactFilter filter)
    {
        Filter = filter;
        SelectFolder();
        Refresh();
    }

    public void SetSort(PersonSort sort)
    {
        Sort = sort;
        SettingsService.RememberAppValue("contacts.sort", sort.ToString());
        Refresh();
    }

    public void ToggleCard()
    {
        ShowsCard = !ShowsCard;
        SettingsService.RememberAppValue("contacts.card", ShowsCard ? "true" : "false");
        ApplyLayout();
    }

    public void ToggleIndex()
    {
        ShowsIndex = !ShowsIndex;
        SettingsService.RememberAppValue("contacts.index", ShowsIndex ? "true" : "false");
        ApplyLayout();
    }

    public void ToggleFolders()
    {
        ShowsFolders = !ShowsFolders;
        SettingsService.RememberAppValue("contacts.folders", ShowsFolders ? "true" : "false");
        ApplyLayout();
    }

    /// <summary>Selects one contact by id (and shows it).</summary>
    public void Select(string? id)
    {
        var index = _shown.ToList().FindIndex(p => p.Id == id);
        List.Selection.Clear();
        if (index >= 0)
        {
            List.Selection.Select(index);
            List.ScrollIntoView(index);
        }
    }

    /// <summary>The letters of the index: the first contact under the letter is chosen and shown.</summary>
    public void SelectLetter(string letter)
    {
        var person = _shown.FirstOrDefault(p => ContactsSession.LetterOf(p, Sort) == letter);
        if (person is not null)
        {
            Select(person.Id);
            List.Focus();
        }
    }

    public void SelectAll() => List.SelectAll();

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public override void FocusDocument()
    {
        if (List.SelectedItem is null && _shown.Count > 0)
        {
            List.SelectedIndex = 0;
        }

        List.Focus();
    }

    /// <summary>Runs one of the window's commands (buttons of the card, the empty state, keys).</summary>
    public async Task RunAsync(string id)
    {
        if (_window.Commands.Find(id) is { } command)
        {
            await OfficeWindow.RunCommandAsync(_window, command);
        }
    }

    // ---- the list ----

    /// <summary>Queries the session again (search, filter, order), keeps the selection by id.</summary>
    public void Refresh()
    {
        _refreshQueued = false;
        var selected = Selected.Select(p => p.Id).ToHashSet();
        var current = Current?.Id;
        _shown = Session.Query(new ContactQuery(SearchBox.Text, Filter, Sort));
        _refreshing = true;
        try
        {
            List.ItemsSource = _shown;
            List.Selection.Clear();
            var anchor = -1;
            for (var i = 0; i < _shown.Count; i++)
            {
                if (_shown[i].Id == current)
                {
                    anchor = i;
                }
                else if (selected.Contains(_shown[i].Id))
                {
                    List.Selection.Select(i);
                }
            }

            if (anchor >= 0)
            {
                List.Selection.Select(anchor); // the focused one last: it is the one the card shows
            }
        }
        finally
        {
            _refreshing = false;
        }

        var letters = _shown.Select(p => ContactsSession.LetterOf(p, Sort)).ToHashSet();
        foreach (var (letter, button) in _letterButtons)
        {
            button.IsEnabled = letters.Contains(letter);
        }

        var total = Session.Count;
        _empty.IsVisible = _shown.Count == 0;
        _emptyTitle.Text = total == 0 ? "Noch keine Kontakte" : "Nichts gefunden";
        _emptyText.Text = total == 0
            ? "Legen Sie den ersten Kontakt an oder importieren Sie eine vCard- oder CSV-Datei (Outlook, Apple, Google)."
            : "Kein Kontakt passt zur Suche oder zum Ordner.";
        ((Control)_empty.Children[^1]).IsVisible = total == 0;
        RefreshFolders();
        OnSelectionChanged();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    private int IndexOf(Person person)
    {
        for (var i = 0; i < _shown.Count; i++)
        {
            if (ReferenceEquals(_shown[i], person))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private void OnSelectionChanged()
    {
        if (_refreshing)
        {
            return;
        }

        var selected = Selected;
        var current = Current ?? selected.FirstOrDefault();
        Card.Show(selected.Count == 1 ? current : selected.Count == 0 ? null : current);
        _cardHost.IsVisible = selected.Count == 1;
        _selectionText.IsVisible = selected.Count != 1;
        _selectionText.Text = selected.Count == 0 ? (_shown.Count == 0 ? "" : "Wählen Sie einen Kontakt.") : $"{selected.Count} Kontakte ausgewählt";
        _mailButton.IsEnabled = current?.Email.Length > 0;
        if (_favoriteButton.Content is IconLabel label)
        {
            label.Text = current?.Data.IsFavorite == true ? "Kein Favorit" : "Favorit";
            label.IsIconFilled = current?.Data.IsFavorite == true;
        }

        ScheduleState();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && Current is not null)
        {
            e.Handled = true;
            _ = RunAsync(ContactsCommandIds.Edit);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Enter:
                e.Handled = true;
                FocusDocument();
                break;
            case Key.Escape when (SearchBox.Text ?? "").Length > 0:
                e.Handled = true;
                SearchBox.Text = "";
                break;
        }
    }

    // ---- the folder pane ----

    private void RefreshFolders()
    {
        var people = Session.People;
        var entries = new List<FolderEntry>
        {
            new(ContactFilter.All, "Alle Kontakte", "users", people.Count),
            new(ContactFilter.Favorites, "Favoriten", "star", people.Count(p => p.Data.IsFavorite)),
        };
        var groups = Session.Groups;
        if (groups.Count > 0)
        {
            entries.Add(new FolderEntry(ContactFilter.All, "Kontaktgruppen", "", 0, IsHeading: true));
            entries.AddRange(groups.Select(g => new FolderEntry(new ContactFilter(ContactFilterKind.Group, g.Id), g.Name, "user-circle", g.Data.Members.Count)));
        }

        var categories = Session.Categories;
        if (categories.Count > 0)
        {
            entries.Add(new FolderEntry(ContactFilter.All, "Kategorien", "", 0, IsHeading: true));
            entries.AddRange(categories.Select(c => new FolderEntry(new ContactFilter(ContactFilterKind.Category, c), c, "tag",
                people.Count(p => p.Data.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)), CategoryColors.TokenOf(c))));
        }

        if (!entries.Any(e => !e.IsHeading && e.Filter == Filter))
        {
            Filter = ContactFilter.All; // the group or category is gone
        }

        _refreshing = true;
        try
        {
            Folders.ItemsSource = entries;
            SelectFolder();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SelectFolder()
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        Folders.SelectedItem = Folders.Items.OfType<FolderEntry>().FirstOrDefault(e => !e.IsHeading && e.Filter == Filter);
        _refreshing = wasRefreshing;
    }

    private void OnFolderChosen()
    {
        if (_refreshing)
        {
            return;
        }

        if (Folders.SelectedItem is FolderEntry { IsHeading: false } entry)
        {
            Filter = entry.Filter;
            Refresh();
        }
        else
        {
            SelectFolder();
        }
    }

    private static Control FolderRow(FolderEntry entry)
    {
        if (entry.IsHeading)
        {
            var heading = new TextBlock { Text = entry.Label, Margin = new Thickness(4, 10, 0, 2), FontSize = MatTokens.Type.FsXs, FontWeight = FontWeight.SemiBold, IsHitTestVisible = false };
            heading.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            return heading;
        }

        var icon = new MatIcon { Icon = entry.Icon, VerticalAlignment = VerticalAlignment.Center };
        if (entry.Color is { } color)
        {
            icon.Use(MatIcon.ForegroundProperty, color);
        }

        var count = new TextBlock { Text = entry.Count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")), VerticalAlignment = VerticalAlignment.Center };
        count.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(2, 1) };
        var label = new TextBlock { Text = entry.Label, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(count, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(count);
        AutomationProperties.SetName(row, entry.Label);
        return row;
    }

    // ---- layout, state ----

    private void ApplyLayout()
    {
        _root.ColumnDefinitions[0].Width = ShowsFolders ? new GridLength(220) : new GridLength(0);
        _folderPane.IsVisible = ShowsFolders;
        _root.ColumnDefinitions[1].Width = ShowsCard ? new GridLength(360) : new GridLength(1, GridUnitType.Star);
        _root.ColumnDefinitions[2].Width = ShowsCard ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _cardPane.IsVisible = ShowsCard;
        _index.IsVisible = ShowsIndex;
        ScheduleState();
    }

    /// <summary>The ribbon's state and the status bar, once per round.</summary>
    private void ScheduleState()
    {
        if (_stateQueued)
        {
            return;
        }

        _stateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _stateQueued = false;
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
        }, DispatcherPriority.Background);
    }

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var total = Session.Count;
        var text = total == 1 ? "1 Kontakt" : $"{total.ToString("N0", culture)} Kontakte";
        if (_shown.Count != total)
        {
            text = $"{_shown.Count.ToString("N0", culture)} von {text}";
        }

        var selected = List.SelectedItems?.Count ?? 0;
        if (selected > 1)
        {
            text += $" · {selected} ausgewählt";
        }

        statusBar.SetItem(CountItem, text);
        statusBar.SetItem(SyncItem, CloudService.Status.State == CloudState.SignedOut ? "Abgemeldet" : SyncText(Document.SyncStatus), StatusBarSide.Right);
    }

    /// <summary>The words of the status bar for the sync.</summary>
    public static string SyncText(SyncStatus status) => status.State switch
    {
        SyncState.Off => "Nur auf diesem Computer",
        SyncState.Syncing => "Wird synchronisiert …",
        SyncState.Idle when status.Pending > 0 => $"{status.Pending} Änderungen warten",
        SyncState.Idle => "Mit der ThixCloud synchronisiert",
        SyncState.Offline => status.Pending > 0 ? $"Offline – {status.Pending} Änderungen warten" : "Offline",
        SyncState.SignedOut => "Abgemeldet",
        SyncState.Forbidden => "In der ThixCloud abgeschaltet",
        _ => status.Message,
    };

    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(QueueRefresh);

    private void OnSessionChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        Filter = ContactFilter.All;
        Refresh();
    });

    private void OnSyncStatusChanged(object? sender, SyncStatus status) => Ui.Post(() =>
    {
        UpdateStatus(_window.StatusBar);
        if (status.State == SyncState.SignedOut)
        {
            ShowSignedOutMessage(force: true);
        }
    });

    /// <summary>
    /// The cloud ended the sign-in (now or before a restart): the account's contacts stay in the window and can be changed – the changes wait – and the
    /// message bar offers "Anmelden" (Einstellungen › Cloud).
    /// </summary>
    private void ShowSignedOutMessage() => ShowSignedOutMessage(force: false);

    private void ShowSignedOutMessage(bool force)
    {
        if (!force && CloudService.Status.State != CloudState.SignedOut)
        {
            return;
        }

        _showsSignedOut = true;
        _window.ShowMessage(SignedOutMessage, InfoSeverity.Warning, isClosable: true, "Anmelden",
            () => _window.ShowSettings(ThixOffice.Desktop.Pages.SettingsPage.CloudTab));
        UpdateStatus(_window.StatusBar);
    }

    /// <summary>Signed in or out: the store of the account (or of this computer) comes in.</summary>
    private void OnCloudStatusChanged(object? sender, CloudStatus status) => Ui.Post(() =>
    {
        if (status.State == CloudState.SignedOut)
        {
            // the cloud ended the sign-in: the account's contacts stay in the window (offline) until the person signs in again or signs out
            _ = Document.StopSyncAsync();
            ShowSignedOutMessage(force: true);
            return;
        }

        if (_showsSignedOut && status.State != CloudState.Connecting)
        {
            _showsSignedOut = false;
            _window.ShowMessage(null);
        }

        _ = ReconnectAsync();
    });

    private async Task ReconnectAsync()
    {
        try
        {
            await Document.ReconnectAsync();
        }
        catch (Exception exception) when (exception is ThixOffice.Core.Models.BusinessException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }

        UpdateStatus(_window.StatusBar);
    }

    public override void Detach()
    {
        Document.SessionChanged -= OnSessionChanged;
        Document.Changed -= OnDocumentChanged;
        Document.SyncStatusChanged -= OnSyncStatusChanged;
        CloudService.StatusChanged -= OnCloudStatusChanged;
    }

    /// <summary>A row of the list: the avatar, the name (by the order: "Nachname, Vorname"), the e-mail address or the company, a star for a favourite.</summary>
    private sealed class PersonRow : Grid
    {
        private readonly PersonAvatar _avatar = new(32);
        private readonly TextBlock _name = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _detail = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = MatTokens.Type.FsSm };
        private readonly MatIcon _star = new() { Icon = "star", IsFilled = true, VerticalAlignment = VerticalAlignment.Center, Size = 14 };

        private readonly ContactsEditor _editor;

        public PersonRow(ContactsEditor editor)
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            Margin = new Thickness(0, 2);
            _editor = editor;
            _avatar.PhotoSource = id => editor.Session.Photo(id);
            _detail.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            _star.Use(MatIcon.ForegroundProperty, "Mat.Warning");
            var texts = new StackPanel { Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _name, _detail } };
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(_star, 2);
            Children.Add(_avatar);
            Children.Add(texts);
            Children.Add(_star);
            DataContextChanged += (_, _) => Show(DataContext as Person);
        }

        private void Show(Person? person)
        {
            _avatar.Show(person);
            _name.Text = person is null ? "" : PersonRules.ListName(person.Data, _editor.Sort);
            _detail.Text = person is null ? "" : person.Email.Length > 0 ? person.Email : person.Data.Company.Length > 0 ? person.Data.Company : person.Phone;
            _star.IsVisible = person?.Data.IsFavorite == true;
            AutomationProperties.SetName(this, _name.Text);
        }
    }
}

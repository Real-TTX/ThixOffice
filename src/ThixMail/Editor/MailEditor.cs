using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;

namespace ThixMail.Editor;

/// <summary>Where the reading pane is (Ansicht › Lesebereich).</summary>
public enum ReadingPanePosition
{
    Right,
    Bottom,
    Off,
}

/// <summary>
/// ThixMail's part of the window, laid out like the German Outlook: the navigation bar at the far left (Mail; Kalender and Personen appear when they
/// exist), the folder pane (Favoriten with "Alle Posteingänge", per account its folders as a tree with unread counts), the message list (search with
/// scope, filter, sort above it; date groups; conversations; virtualized for 200,000 messages; several can be selected; quick actions on hover;
/// drag onto a folder moves) and the reading pane right, below or off (<see cref="MessageView"/>). Every action goes through the
/// <see cref="MailSession"/> (undoable, queued for the server); the views follow the store's events. The status bar counts and tells how the
/// accounts stand.
/// </summary>
public sealed class MailEditor : OfficeEditor
{
    public const string CountItem = "mail.count";
    public const string SyncItem = "mail.sync";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly OfficeWindow _window;
    private readonly Grid _root = new();
    private readonly Border _rail = new();
    private readonly Border _folderPane = new();
    private readonly Border _listPane = new();
    private readonly Grid _mainArea = new();
    private readonly Dictionary<long, MessageHeader> _headers = [];
    private readonly HashSet<long> _expanded = [];
    private readonly HashSet<long> _imagesAllowed = [];
    private readonly Dictionary<long, IReadOnlyDictionary<string, byte[]>> _loadedImages = [];
    private readonly Dictionary<long, string> _sentFolders = [];
    private readonly StackPanel _emptyList = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _emptyTitle = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _emptyText = new() { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, TextAlignment = TextAlignment.Center };
    private readonly Button _emptyAction;
    private readonly DispatcherTimer _markRead = new();
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<ListRow> _rows = [];
    private IReadOnlyList<ListEntry> _entries = [];
    private FolderNode? _currentNode;
    private bool _refreshQueued;
    private bool _foldersQueued;
    private bool _refreshing;
    private CancellationTokenSource? _reading;
    private long? _shownId;
    private Point? _dragStart;
    private List<long>? _dragIds;
    private ReadingPanePosition _pane;
    private bool _detached;

    public MailEditor(OfficeWindow window, MailDocument document)
    {
        _window = window;
        Document = document;
        _window.StatusBar.View.ShowZoom = false;

        // the navigation bar: Mail now; Kalender and Personen when they exist (7c)
        MailNavButton = new Button { Content = new MatIcon { Icon = "mail", Size = 20 }, Padding = new Thickness(10), Margin = new Thickness(4, 8, 4, 2) }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(MailNavButton, "E-Mail");
        ToolTip.SetTip(MailNavButton, "E-Mail");
        MailNavButton.Use(Button.BackgroundProperty, "Mat.AccentSoft");
        var calendar = new Button { Content = new MatIcon { Icon = "calendar", Size = 20 }, Padding = new Thickness(10), Margin = new Thickness(4, 2), IsVisible = false }.Themed("MatButton.Ghost");
        var people = new Button { Content = new MatIcon { Icon = "users", Size = 20 }, Padding = new Thickness(10), Margin = new Thickness(4, 2), IsVisible = false }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(calendar, "Kalender");
        AutomationProperties.SetName(people, "Personen");
        NavigationButtons = [MailNavButton, calendar, people];
        _rail.Child = new StackPanel { Children = { MailNavButton, calendar, people } };
        _rail.Use(Border.BackgroundProperty, "Mat.NavBg");
        _rail.Use(Border.BorderBrushProperty, "Mat.Border");
        _rail.BorderThickness = new Thickness(0, 0, 1, 0);

        // the folder pane
        Folders = new ListBox { SelectionMode = SelectionMode.Single, ItemTemplate = new FuncDataTemplate<FolderNode>((node, _) => node is null ? new TextBlock() : new FolderRow(node), supportsRecycling: false) };
        AutomationProperties.SetName(Folders, "Ordner");
        Folders.SelectionChanged += (_, _) => OnFolderChosen();
        Folders.ContextMenu = OfficeCommandMenu.Create(window, [MailCommandIds.NewFolder, MailCommandIds.RenameFolder, null, MailCommandIds.MarkAllRead, MailCommandIds.EmptyFolder, null, MailCommandIds.DeleteFolder]);
        _folderPane.Child = Folders;
        _folderPane.Use(Border.BackgroundProperty, "Mat.NavBg");
        _folderPane.Use(Border.BorderBrushProperty, "Mat.Border");
        _folderPane.BorderThickness = new Thickness(0, 0, 1, 0);

        // the list with its toolbar: search (scope), filter, sort
        SearchBox = new TextBox { Watermark = "Suchen", InnerLeftContent = new MatIcon { Icon = "search", Margin = new Thickness(8, 0, 0, 0) } };
        AutomationProperties.SetName(SearchBox, "E-Mail durchsuchen");
        ToolTip.SetTip(SearchBox, Ui.Tooltip("Suchen", OperatingSystem.IsMacOS() ? "Ctrl+Alt+F" : "Ctrl+E"));
        SearchBox.TextChanged += (_, _) =>
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = "";
                FocusDocument();
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Down)
            {
                QueueRefresh();
                FocusDocument();
                e.Handled = true;
            }
        };
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            QueueRefresh();
        };
        ScopeChoice = new ComboBox { ItemsSource = new[] { "Aktueller Ordner", "Alle Ordner" }, SelectedIndex = 0, MinWidth = 130 };
        AutomationProperties.SetName(ScopeChoice, "Suchbereich");
        ScopeChoice.SelectionChanged += (_, _) => QueueRefresh();
        FilterChoice = new ComboBox { ItemsSource = new[] { "Alle", "Ungelesen", "Gekennzeichnet", "Mit Anlagen" }, SelectedIndex = 0, MinWidth = 110 };
        AutomationProperties.SetName(FilterChoice, "Filter");
        FilterChoice.SelectionChanged += (_, _) => QueueRefresh();
        SortChoice = new ComboBox { ItemsSource = new[] { "Datum", "Von", "Betreff", "Größe" }, SelectedIndex = 0, MinWidth = 100 };
        AutomationProperties.SetName(SortChoice, "Sortieren nach");
        SortChoice.SelectionChanged += (_, _) => QueueRefresh();
        var toolbarTop = new DockPanel { Margin = new Thickness(10, 10, 10, 4) };
        DockPanel.SetDock(ScopeChoice, Dock.Right);
        ScopeChoice.Margin = new Thickness(6, 0, 0, 0);
        toolbarTop.Children.Add(ScopeChoice);
        toolbarTop.Children.Add(SearchBox);
        var toolbarBottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 2, 10, 6), Children = { FilterChoice, SortChoice } };

        List = new ListBox { SelectionMode = SelectionMode.Multiple, ItemTemplate = new FuncDataTemplate<ListRow>((_, _) => new MessageRow(this)) };
        AutomationProperties.SetName(List, "Nachrichten");
        List.SelectionChanged += (_, _) => OnSelectionChanged();
        List.DoubleTapped += async (_, _) => await RunAsync(MailCommandIds.OpenMessage);
        List.AddHandler(InputElement.KeyDownEvent, OnListKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.AddHandler(InputElement.PointerPressedEvent, OnListPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.ContextMenu = OfficeCommandMenu.Create(window,
        [
            MailCommandIds.Reply, MailCommandIds.ReplyAll, MailCommandIds.Forward, null, MailCommandIds.ToggleRead, MailCommandIds.Categorize, MailCommandIds.Flag,
            null, MailCommandIds.Move, MailCommandIds.Archive, MailCommandIds.Delete, null, MailCommandIds.SaveAs, MailCommandIds.Print,
        ]);

        _emptyAction = new Button { Content = new IconLabel { Icon = "plus", Text = "Konto hinzufügen" } }.Themed("MatButton.Primary");
        _emptyAction.Click += async (_, _) => await RunAsync(MailCommandIds.AddAccount);
        _emptyTitle.Themed("MatText.SectionTitle");
        _emptyText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _emptyList.Children.Add(new MatIcon { Icon = "mail", Size = 40, HorizontalAlignment = HorizontalAlignment.Center });
        _emptyList.Children.Add(_emptyTitle);
        _emptyList.Children.Add(_emptyText);
        _emptyList.Children.Add(_emptyAction);

        var listArea = new Panel { Children = { List, _emptyList } };
        var listDock = new DockPanel();
        DockPanel.SetDock(toolbarTop, Dock.Top);
        DockPanel.SetDock(toolbarBottom, Dock.Top);
        listDock.Children.Add(toolbarTop);
        listDock.Children.Add(toolbarBottom);
        listDock.Children.Add(listArea);
        _listPane.Child = listDock;
        _listPane.Use(Border.BackgroundProperty, "Mat.Surface");
        _listPane.Use(Border.BorderBrushProperty, "Mat.Border");

        // the reading pane
        Reader = new MessageView();
        Reader.LoadImagesRequested += async (_, always) => await LoadImagesAsync(always);
        Reader.LinkRequested += async (_, address) => await OpenLinkAsync(address);
        Reader.AttachmentRequested += async (_, request) => await AttachmentAsync(request.Attachment, request.Open);
        Reader.SaveAllRequested += async (_, _) => await SaveAllAsync();
        Reader.Use(UserControl.BackgroundProperty, "Mat.Bg");

        _root.ColumnDefinitions = new ColumnDefinitions("Auto,240,*");
        Grid.SetColumn(_folderPane, 1);
        Grid.SetColumn(_mainArea, 2);
        _root.Children.Add(_rail);
        _root.Children.Add(_folderPane);
        _root.Children.Add(_mainArea);
        _root.AddHandler(InputElement.PointerMovedEvent, OnDragMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _root.AddHandler(InputElement.PointerReleasedEvent, OnDragReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        View = _root;

        _markRead.Tick += (_, _) => MarkShownRead();
        Pane = Enum.TryParse<ReadingPanePosition>(SettingsService.AppValue(MailDocument.Settings.ReadingPane), out var pane) ? pane : ReadingPanePosition.Right;
        ShowsConversations = SettingsService.AppValue(MailDocument.Settings.Conversations) != "false";
        IsCompact = SettingsService.AppValue(MailDocument.Settings.Density) == "compact";
        ShowsFolderPane = SettingsService.AppValue(MailDocument.Settings.FolderPane) != "false";

        Document.Store.MessagesChanged += OnMessagesChanged;
        Document.Store.FoldersChanged += OnFoldersChanged;
        Document.Store.QueueChanged += OnQueueChanged;
        Document.Accounts.Changed += OnFoldersChanged;
        Document.SyncStatusChanged += OnSyncStatusChanged;
        Document.Changed += OnDocumentChanged;
        RefreshFolders();
        SelectFirstInbox();
        Refresh();
    }

    public MailDocument Document { get; }

    public MailSession Session => Document.Session;

    public override Control View { get; }

    public ListBox Folders { get; }

    public ListBox List { get; }

    public TextBox SearchBox { get; }

    public ComboBox ScopeChoice { get; }

    public ComboBox FilterChoice { get; }

    public ComboBox SortChoice { get; }

    public MessageView Reader { get; }

    public Button MailNavButton { get; }

    /// <summary>Mail, Kalender, Personen (the last two hidden until ThixMail has them).</summary>
    public IReadOnlyList<Button> NavigationButtons { get; }

    /// <summary>The rows of the list now.</summary>
    public IReadOnlyList<ListRow> Rows => _rows;

    /// <summary>The folder (or the unified inbox) chosen.</summary>
    public FolderNode? CurrentNode => _currentNode;

    public MailFolder? CurrentFolder => !Gone && _currentNode?.FolderId is { } id ? Document.Store.Folder(id) : null;

    /// <summary>The ids of the selected messages (a collapsed conversation's row stands for its messages in the list).</summary>
    public IReadOnlyList<long> SelectedIds
    {
        get
        {
            var ids = new List<long>();
            foreach (var row in (List.SelectedItems?.OfType<ListRow>() ?? []).Where(r => r.IsMessage))
            {
                if (row.Kind == RowKind.Message && row.Count > 1 && !row.IsExpanded)
                {
                    ids.AddRange(_entries.Where(e => e.Thread == row.Thread).Select(e => e.Id));
                }
                else
                {
                    ids.Add(row.Id);
                }
            }

            return [.. ids.Distinct()];
        }
    }

    /// <summary>The one message selected (the newest of a conversation row), or null.</summary>
    public MessageHeader? Current => !Gone && List.SelectedItems?.Count == 1 && List.SelectedItem is ListRow { IsMessage: true } row ? HeaderOf(row.Id) : null;

    /// <summary>The remote pictures loaded for the message shown (printing takes them along), or null.</summary>
    public IReadOnlyDictionary<string, byte[]>? LoadedImagesOfShown => _shownId is { } id ? _loadedImages.GetValueOrDefault(id) : null;

    public bool IsCompact { get; private set; }

    public bool ShowsConversations { get; private set; }

    public bool ShowsFolderPane { get; private set; }

    public ReadingPanePosition Pane
    {
        get => _pane;
        set
        {
            _pane = value;
            SettingsService.RememberAppValue(MailDocument.Settings.ReadingPane, value == ReadingPanePosition.Right ? null : value.ToString());
            ApplyLayout();
        }
    }

    // ===== layout =====

    private void ApplyLayout()
    {
        _mainArea.Children.Clear();
        _mainArea.ColumnDefinitions.Clear();
        _mainArea.RowDefinitions.Clear();
        _listPane.BorderThickness = default;
        switch (_pane)
        {
            case ReadingPanePosition.Right:
                _mainArea.ColumnDefinitions = new ColumnDefinitions("380,*");
                _listPane.BorderThickness = new Thickness(0, 0, 1, 0);
                Grid.SetColumn(_listPane, 0);
                Grid.SetRow(_listPane, 0);
                Grid.SetColumn(Reader, 1);
                Grid.SetRow(Reader, 0);
                _mainArea.Children.Add(_listPane);
                _mainArea.Children.Add(Reader);
                break;
            case ReadingPanePosition.Bottom:
                _mainArea.RowDefinitions = new RowDefinitions("2*,3*");
                _listPane.BorderThickness = new Thickness(0, 0, 0, 1);
                Grid.SetColumn(_listPane, 0);
                Grid.SetRow(_listPane, 0);
                Grid.SetColumn(Reader, 0);
                Grid.SetRow(Reader, 1);
                _mainArea.Children.Add(_listPane);
                _mainArea.Children.Add(Reader);
                break;
            default:
                Grid.SetColumn(_listPane, 0);
                Grid.SetRow(_listPane, 0);
                _mainArea.Children.Add(_listPane);
                break;
        }

        _window.Commands.Invalidate();
    }

    public void SetConversations(bool on)
    {
        ShowsConversations = on;
        SettingsService.RememberAppValue(MailDocument.Settings.Conversations, on ? null : "false");
        Refresh();
    }

    public void SetCompact(bool compact)
    {
        IsCompact = compact;
        SettingsService.RememberAppValue(MailDocument.Settings.Density, compact ? "compact" : null);
        Refresh(force: true);
    }

    public void SetFolderPane(bool show)
    {
        ShowsFolderPane = show;
        SettingsService.RememberAppValue(MailDocument.Settings.FolderPane, show ? null : "false");
        _folderPane.IsVisible = show;
        _root.ColumnDefinitions[1].Width = show ? new GridLength(240) : new GridLength(0);
        _window.Commands.Invalidate();
    }

    // ===== folders =====

    private void RefreshFolders()
    {
        var accounts = Document.Accounts.Accounts;
        var nodes = new List<FolderNode>();
        var store = Document.Store;
        _sentFolders.Clear();
        if (accounts.Count > 0)
        {
            nodes.Add(new FolderNode(FolderNodeKind.FavoritesHeading, "Favoriten", "star", 0, 0));
            var inboxes = accounts.Select(a => store.FolderByRole(a.Id, FolderRole.Inbox)).Where(f => f is not null).Select(f => f!).ToList();
            nodes.Add(new FolderNode(FolderNodeKind.Unified, accounts.Count > 1 ? "Alle Posteingänge" : "Posteingang", "download", inboxes.Sum(f => f.Unread), 1));
        }

        foreach (var account in accounts)
        {
            var status = Document.StatusOf(account.Id);
            var state = status.Phase switch
            {
                SyncPhase.Offline or SyncPhase.WorkingOffline => "offline",
                SyncPhase.SignInFailed => "Kennwort?",
                SyncPhase.CertificateProblem => "Zertifikat?",
                _ => null,
            };
            nodes.Add(new FolderNode(FolderNodeKind.Account, account.DisplayName, "user-circle", 0, 0, AccountId: account.Id, State: state));
            var folders = store.Folders(account.Id);
            foreach (var folder in folders.Where(f => f.Role == FolderRole.Sent))
            {
                _sentFolders[folder.Id] = folder.Path;
            }

            void AddChildren(string? parent, int depth)
            {
                var children = folders.Where(f => f.ParentPath == parent && (depth > 1 || f.Role == FolderRole.None || parent is not null))
                    .OrderBy(f => f.RoleOrder).ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var folder in children)
                {
                    nodes.Add(new FolderNode(FolderNodeKind.Folder, folder.DisplayName, IconOf(folder.Role), folder.Role is FolderRole.Drafts ? folder.Total : folder.Unread, depth, folder.Id, account.Id));
                    AddChildren(folder.Path, depth + 1);
                }
            }

            // the special folders first, in Outlook's order, then the others as a tree
            foreach (var folder in folders.Where(f => f.Role is not (FolderRole.None or FolderRole.All or FolderRole.Flagged)).OrderBy(f => f.RoleOrder))
            {
                nodes.Add(new FolderNode(FolderNodeKind.Folder, folder.DisplayName, IconOf(folder.Role), folder.Role is FolderRole.Drafts ? folder.Total : folder.Unread, 1, folder.Id, account.Id));
                if (folder.Role == FolderRole.Inbox)
                {
                    foreach (var child in folders.Where(f => f.ParentPath == folder.Path && f.Role == FolderRole.None).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        nodes.Add(new FolderNode(FolderNodeKind.Folder, child.DisplayName, "folder", child.Unread, 2, child.Id, account.Id));
                        AddChildren(child.Path, 3);
                    }
                }
            }

            foreach (var folder in folders.Where(f => f.ParentPath is null && f.Role is FolderRole.None or FolderRole.All or FolderRole.Flagged).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                nodes.Add(new FolderNode(FolderNodeKind.Folder, folder.DisplayName, "folder", folder.Unread, 1, folder.Id, account.Id));
                AddChildren(folder.Path, 2);
            }
        }

        var current = _currentNode;
        Folders.ItemsSource = nodes;
        if (current is not null)
        {
            var again = nodes.FirstOrDefault(n => n.Kind == current.Kind && n.FolderId == current.FolderId && n.AccountId == current.AccountId && !n.IsHeading);
            if (again is not null)
            {
                _currentNode = again;
                Folders.SelectedItem = again;
            }
        }
    }

    private static string IconOf(FolderRole role) => role switch
    {
        FolderRole.Inbox => "download",
        FolderRole.Drafts => "edit",
        FolderRole.Sent => "upload",
        FolderRole.Trash => "trash",
        FolderRole.Junk => "shield",
        FolderRole.Archive => "archive",
        _ => "folder",
    };

    private void SelectFirstInbox()
    {
        if (Folders.ItemsSource is IEnumerable<FolderNode> nodes && nodes.FirstOrDefault(n => n.Kind == FolderNodeKind.Unified) is { } unified)
        {
            Folders.SelectedItem = unified;
            _currentNode = unified;
        }
    }

    private void OnFolderChosen()
    {
        if (Folders.SelectedItem is not FolderNode node)
        {
            return;
        }

        if (node.IsHeading)
        {
            Folders.SelectedItem = _currentNode;
            return;
        }

        if (node == _currentNode)
        {
            return;
        }

        _currentNode = node;
        _expanded.Clear();
        Refresh();
        _window.Commands.Invalidate();
    }

    /// <summary>Shows a folder (tests, notifications); the unified inbox for null.</summary>
    public void SelectFolder(long? folderId)
    {
        var nodes = Folders.ItemsSource as IEnumerable<FolderNode> ?? [];
        var node = folderId is null ? nodes.FirstOrDefault(n => n.Kind == FolderNodeKind.Unified) : nodes.FirstOrDefault(n => n.FolderId == folderId);
        if (node is not null)
        {
            Folders.SelectedItem = node;
        }
    }

    public bool IsSentFolder(long folderId) => _sentFolders.ContainsKey(folderId);

    /// <summary>The folders the list shows: the chosen one, every inbox, or – searching in all folders – every folder.</summary>
    private IReadOnlyList<long> ListedFolders()
    {
        var store = Document.Store;
        if (ScopeChoice.SelectedIndex == 1 && (SearchBox.Text ?? "").Trim().Length > 0)
        {
            return [.. store.Folders().Where(f => f.IsSelectable && f.Role != FolderRole.Junk && f.Role != FolderRole.Trash).Select(f => f.Id)];
        }

        return _currentNode switch
        {
            { Kind: FolderNodeKind.Unified } => [.. Document.Accounts.Accounts.Select(a => store.FolderByRole(a.Id, FolderRole.Inbox)).Where(f => f is not null).Select(f => f!.Id)],
            { FolderId: { } id } => [id],
            _ => [],
        };
    }

    // ===== the list =====

    /// <summary>The header of a message (cached for the rows; read from the store when not).</summary>
    public MessageHeader? HeaderOf(long id)
    {
        if (Gone)
        {
            return null;
        }

        if (_headers.TryGetValue(id, out var header))
        {
            return header;
        }

        // read the rows around it at once (the list asks row by row while scrolling)
        var index = _rows.ToList().FindIndex(r => r.Id == id);
        var ids = index < 0 ? [id] : _rows.Skip(Math.Max(0, index - 20)).Take(80).Where(r => r.IsMessage && !_headers.ContainsKey(r.Id)).Select(r => r.Id).Append(id).Distinct().ToList();
        foreach (var each in Document.Store.Headers(ids))
        {
            _headers[each.Id] = each;
        }

        if (_headers.Count > 20_000)
        {
            _headers.Clear();
            return Document.Store.Header(id);
        }

        return _headers.GetValueOrDefault(id);
    }

    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (Gone)
            {
                return;
            }

            Refresh();
        }, DispatcherPriority.Background);
    }

    /// <summary>The list again (another folder, a filter, a search, a change in the store); the selection stays where it can.</summary>
    public void Refresh(bool force = false)
    {
        _refreshing = true;
        try
        {
            var selected = List.SelectedItems?.OfType<ListRow>().Where(r => r.IsMessage).Select(r => r.Id).ToHashSet() ?? [];
            _headers.Clear();
            var sort = (ListSort)Math.Max(0, SortChoice.SelectedIndex);
            var query = new MessageQuery(ListedFolders(), (ListFilter)Math.Max(0, FilterChoice.SelectedIndex), (SearchBox.Text ?? "").Trim(), sort, sort is ListSort.Date or ListSort.Size);
            _entries = Document.Store.List(query);
            var rows = MessageList.Build(_entries, ShowsConversations, sort == ListSort.Date, _expanded, DateTimeOffset.Now);
            if (force || !rows.SequenceEqual(_rows))
            {
                var hadFocus = List.IsKeyboardFocusWithin;
                _rows = rows;
                List.ItemsSource = rows;
                foreach (var row in rows.Where(r => r.IsMessage && selected.Contains(r.Id)))
                {
                    List.SelectedItems?.Add(row);
                }

                if (hadFocus)
                {
                    Dispatcher.UIThread.Post(FocusDocument, DispatcherPriority.Loaded);
                }
            }
            else
            {
                // the same rows: their headers may have changed (read, flagged, categories)
                foreach (var row in List.GetRealizedContainers().Select(c => c.FindDescendantOfType<MessageRow>()).OfType<MessageRow>())
                {
                    row.Show(row.DataContext as ListRow);
                }
            }

            UpdateEmpty();
        }
        finally
        {
            _refreshing = false;
        }

        OnSelectionChanged();
        UpdateStatus(_window.StatusBar);
    }

    private void UpdateEmpty()
    {
        var noAccounts = Document.Accounts.Accounts.Count == 0;
        var searching = (SearchBox.Text ?? "").Trim().Length > 0;
        _emptyList.IsVisible = _rows.Count == 0;
        _emptyAction.IsVisible = noAccounts;
        _emptyTitle.Text = noAccounts ? "Noch kein E-Mail-Konto" : searching ? "Keine Treffer" : "Hier ist nichts";
        _emptyText.Text = noAccounts
            ? "Fügen Sie Ihr E-Mail-Konto hinzu – ThixMail findet die Einstellungen meist selbst."
            : searching ? "Versuchen Sie andere Wörter oder suchen Sie in allen Ordnern." : "Dieser Ordner ist leer.";
    }

    private void OnSelectionChanged()
    {
        if (_refreshing)
        {
            return;
        }

        _window.Commands.Invalidate();
        UpdateStatus(_window.StatusBar);
        var current = Current;
        if (current?.Id == _shownId && current is not null)
        {
            return;
        }

        _markRead.Stop();
        _reading?.Cancel();
        _shownId = current?.Id;
        if (current is null)
        {
            var count = List.SelectedItems?.OfType<ListRow>().Count(r => r.IsMessage) ?? 0;
            Reader.ShowNothing(count > 1 ? $"{count} Elemente ausgewählt" : "Wählen Sie ein Element aus, um es zu lesen.");
            return;
        }

        _ = ShowMessageAsync(current);
    }

    /// <summary>The reading pane shows a message: the header at once, the body from the cache or the server.</summary>
    private async Task ShowMessageAsync(MessageHeader header)
    {
        if (_pane == ReadingPanePosition.Off)
        {
            return;
        }

        var reading = new CancellationTokenSource();
        _reading = reading;
        Reader.ShowHeader(header, "Die Nachricht wird geladen …");
        byte[]? raw;
        try
        {
            raw = await Document.BodyAsync(header.Id, reading.Token);
        }
        catch (Exception exception) when (exception is ThixMail.Core.Transport.MailConnectionException or OperationCanceledException or IOException)
        {
            if (!reading.IsCancellationRequested)
            {
                Reader.ShowHeader(header, "Die Nachricht kann gerade nicht geladen werden – der Mailserver ist nicht erreichbar.");
            }

            return;
        }

        if (reading.IsCancellationRequested || _shownId != header.Id)
        {
            return;
        }

        if (raw is null)
        {
            Reader.ShowHeader(header, Document.IsWorkingOffline ? "Offline: der Text dieser Nachricht ist noch nicht auf diesem Computer." : "Diese Nachricht gibt es auf dem Server nicht mehr.");
            return;
        }

        var mail = MailReader.Read(raw);
        var remote = _loadedImages.GetValueOrDefault(header.Id);
        Reader.Show(mail, header, remote);
        if (remote is null && Reader.View is { HasBlockedImages: true } view && Document.Store.IsTrustedSender(header.From.Address))
        {
            await FetchImagesAsync(header.Id, view.BlockedImages);
        }

        if (!header.IsRead)
        {
            var seconds = int.TryParse(SettingsService.AppValue(MailDocument.Settings.MarkReadSeconds), out var value) ? value : 0;
            if (seconds <= 0)
            {
                MarkShownRead();
            }
            else
            {
                _markRead.Interval = TimeSpan.FromSeconds(seconds);
                _markRead.Start();
            }
        }
    }

    private void MarkShownRead()
    {
        _markRead.Stop();
        if (_shownId is { } id && Document.Store.Header(id) is { IsRead: false })
        {
            Session.SetRead([id], true);
        }
    }

    /// <summary>"Bilder laden" (this message) or "Immer für diesen Absender".</summary>
    public async Task LoadImagesAsync(bool always)
    {
        if (Reader.Header is not { } header || Reader.View is not { } view)
        {
            return;
        }

        if (always)
        {
            Document.Store.TrustSender(header.From.Address);
        }

        _imagesAllowed.Add(header.Id);
        await FetchImagesAsync(header.Id, view.BlockedImages);
    }

    private async Task FetchImagesAsync(long id, IReadOnlyList<string> sources)
    {
        var loaded = await RemotePictures.FetchAsync(sources, CancellationToken.None);
        _loadedImages[id] = loaded.Count > 0 ? loaded : sources.ToDictionary(s => s, _ => Array.Empty<byte>());
        if (_shownId == id && Reader.Mail is { } mail)
        {
            Reader.Show(mail, Reader.Header, _loadedImages[id]);
        }
    }

    private async Task OpenLinkAsync(string address)
    {
        if (MailHtml.DescribeLink(address) is not { } target)
        {
            _window.ShowMessage("Dieser Link wird aus Sicherheitsgründen nicht geöffnet (nur Web- und E-Mail-Adressen).", InfoSeverity.Warning, isClosable: true);
            return;
        }

        if (address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            await MailCommands.ComposeToAsync(_window, address);
            return;
        }

        if (await ThixOffice.Desktop.Dialogs.QuestionDialog.ConfirmAsync(_window, "Link öffnen?", $"Der Link führt zu:\n{target}\n\nÖffnen Sie ihn nur, wenn Sie dem Absender vertrauen.", "Öffnen"))
        {
            LinkService.Open(address, Ui.OpenExternal);
        }
    }

    private async Task AttachmentAsync(MailAttachment attachment, bool open)
    {
        try
        {
            if (!open)
            {
                var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(_window, MailReader.Sanitize(attachment.FileName), SettingsService.SaveFolder, "Anlage", Path.GetExtension(attachment.FileName));
                if (target is not null)
                {
                    EmlFiles.Save(target, attachment.Content);
                    _window.ShowMessage($"„{Path.GetFileName(target)}“ wurde gespeichert.", InfoSeverity.Success, isClosable: true);
                }

                return;
            }

            if (AttachmentSafety.IsExecutable(attachment.FileName)
                && !await ThixOffice.Desktop.Dialogs.QuestionDialog.ConfirmAsync(_window, "Anlage öffnen?",
                    $"„{attachment.FileName}“ kann ein Programm oder Makros starten und Ihren Computer schädigen. Öffnen Sie die Datei nur, wenn Sie sie erwartet haben und dem Absender vertrauen.",
                    "Trotzdem öffnen", isDestructive: true))
            {
                return;
            }

            Ui.OpenExternal(EmlFiles.ForOpening(attachment));
        }
        catch (BusinessException exception)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    private async Task SaveAllAsync()
    {
        if (Reader.Mail is not { } mail)
        {
            return;
        }

        var folder = await OfficeApplication.FileDialogs.PickFolderAsync(_window, SettingsService.SaveFolder);
        if (folder is null)
        {
            return;
        }

        try
        {
            var paths = EmlFiles.SaveAll(folder, mail.Attachments);
            _window.ShowMessage($"{paths.Count} Anlagen wurden in „{folder}“ gespeichert.", InfoSeverity.Success, isClosable: true);
        }
        catch (BusinessException exception)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    // ===== keys, pointer, drag =====

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = RunAsync(MailCommandIds.OpenMessage);
                break;
            case Key.Right when List.SelectedItem is ListRow { Kind: RowKind.Message, Count: > 1, IsExpanded: false } row:
                e.Handled = true;
                ToggleConversation(row.Thread, true);
                break;
            case Key.Left when List.SelectedItem is ListRow { IsMessage: true } row && _expanded.Contains(row.Thread):
                e.Handled = true;
                ToggleConversation(row.Thread, false);
                break;
        }
    }

    /// <summary>A conversation opened or closed (its other messages shown indented under it).</summary>
    public void ToggleConversation(long thread, bool expand)
    {
        if (expand)
        {
            _expanded.Add(thread);
        }
        else
        {
            _expanded.Remove(thread);
        }

        Refresh(force: true);
        if (_rows.FirstOrDefault(r => r.Kind == RowKind.Message && r.Thread == thread) is { } row)
        {
            List.SelectedItems?.Clear();
            List.SelectedItem = row;
        }
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = point.Position;
        _dragIds = null;
        if (e.Source is Visual visual && visual.FindAncestorOfType<MessageRow>(includeSelf: true) is { DataContext: ListRow { Count: > 1 } row } && e.ClickCount == 2)
        {
            ToggleConversation(row.Thread, !row.IsExpanded);
            e.Handled = true;
        }
    }

    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || !e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
        {
            _dragStart = null;
            return;
        }

        var position = e.GetPosition(_root);
        if (_dragIds is null && (Math.Abs(position.X - start.X) > 8 || Math.Abs(position.Y - start.Y) > 8) && SelectedIds.Count > 0)
        {
            _dragIds = [.. SelectedIds];
            _root.Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        var ids = _dragIds;
        _dragStart = null;
        _dragIds = null;
        _root.Cursor = null;
        if (ids is null)
        {
            return;
        }

        var target = FolderAt(e.GetPosition(Folders));
        if (target?.FolderId is { } folderId)
        {
            e.Handled = true;
            Run(() => Session.Move(ids, folderId));
        }
    }

    /// <summary>The folder entry under a point of the folder pane (drag and drop).</summary>
    public FolderNode? FolderAt(Point point)
    {
        foreach (var item in Folders.GetRealizedContainers().OfType<ListBoxItem>())
        {
            var origin = item.TranslatePoint(new Point(0, 0), Folders);
            if (origin is { } at && new Rect(at, item.Bounds.Size).Contains(point))
            {
                return item.DataContext as FolderNode;
            }
        }

        return null;
    }

    /// <summary>A quick action of a row (hover buttons) on its message.</summary>
    public async Task QuickActionAsync(long id, string command)
    {
        var header = HeaderOf(id);
        if (header is null)
        {
            return;
        }

        switch (command)
        {
            case MailCommandIds.Delete:
                await MailCommands.DeleteAsync(_window, this, [id]);
                break;
            case MailCommandIds.Flag:
                Run(() => Session.SetFlagged([id], !header.IsFlagged));
                break;
            case MailCommandIds.ToggleRead:
                Run(() => Session.SetRead([id], !header.IsRead));
                break;
        }
    }

    /// <summary>Runs an action; a refusal of the business layer in the message bar.</summary>
    public void Run(Action action)
    {
        try
        {
            action();
        }
        catch (BusinessException exception)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    private Task<bool> RunAsync(string commandId) =>
        _window.Commands.Find(commandId) is { } command ? OfficeWindow.RunCommandAsync(_window, command) : Task.FromResult(false);

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public override void FocusDocument()
    {
        if (List.SelectedItem is null && _rows.FirstOrDefault(r => r.IsMessage) is { } first && Current is null)
        {
            List.SelectedItem = first;
        }

        // the keyboard goes into the selected row (the list itself would let arrow keys move the focus elsewhere)
        if (List.SelectedItem is { } selected && List.ContainerFromItem(selected) is Control container)
        {
            container.Focus();
        }
        else
        {
            List.Focus();
        }
    }

    /// <summary>Selects a message by id (tests, a notification).</summary>
    public void Select(long id)
    {
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            var header = Document.Store.Header(id);
            if (header is not null && _rows.Any(r => r.Thread == header.ThreadId && r.Kind == RowKind.Message))
            {
                ToggleConversation(header.ThreadId, true);
                row = _rows.FirstOrDefault(r => r.Id == id);
            }
        }

        if (row is not null)
        {
            List.SelectedItems?.Clear();
            List.SelectedItem = row;
            List.ScrollIntoView(row);
        }
    }

    // ===== events =====

    private bool Gone => _detached || Document.IsClosed;

    private void OnMessagesChanged(object? sender, IReadOnlyCollection<long> folders) => Ui.Post(() =>
    {
        if (!Gone)
        {
            QueueRefresh();
        }
    });

    private void OnFoldersChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (!Gone)
        {
            QueueFolders();
        }
    });

    private void OnQueueChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (!Gone)
        {
            UpdateStatus(_window.StatusBar);
        }
    });

    private void OnSyncStatusChanged(object? sender, AccountSyncStatus status) => Ui.Post(() =>
    {
        if (Gone)
        {
            return;
        }

        QueueFolders();
        UpdateStatus(_window.StatusBar);
    });

    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (!Gone)
        {
            _window.Commands.Invalidate();
        }
    });

    private void QueueFolders()
    {
        if (_foldersQueued)
        {
            return;
        }

        _foldersQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _foldersQueued = false;
            if (Gone)
            {
                return;
            }

            var hadNone = _currentNode is null;
            RefreshFolders();
            if (hadNone || Folders.SelectedItem is null)
            {
                SelectFirstInbox();
            }

            Refresh();
        }, DispatcherPriority.Background);
    }

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        if (Gone)
        {
            return;
        }

        var total = _entries.Count;
        var unread = _currentNode?.Unread ?? 0;
        var text = total == 1 ? "1 Element" : $"{total.ToString("N0", German)} Elemente";
        if (unread > 0)
        {
            text += $" · {unread.ToString("N0", German)} ungelesen";
        }

        var selected = List.SelectedItems?.OfType<ListRow>().Count(r => r.IsMessage) ?? 0;
        if (selected > 1)
        {
            text += $" · {selected} ausgewählt";
        }

        statusBar.SetItem(CountItem, text);
        statusBar.SetItem(SyncItem, SyncText(), StatusBarSide.Right);
    }

    /// <summary>The state of every account in one line ("Alle Ordner sind auf dem neuesten Stand." when all are idle).</summary>
    public string SyncText()
    {
        var accounts = Document.Accounts.Accounts;
        if (accounts.Count == 0)
        {
            return "Kein Konto eingerichtet";
        }

        if (Document.IsWorkingOffline)
        {
            var waiting = Document.Store.Outbox().Count;
            return waiting > 0 ? $"Offline arbeiten · Postausgang: {waiting}" : "Offline arbeiten";
        }

        var states = accounts.Select(a => (Account: a, Status: Document.StatusOf(a.Id))).ToList();
        var outbox = Document.Store.Outbox().Count;
        var suffix = outbox > 0 ? $" · Postausgang: {outbox}" : "";
        if (states.All(s => s.Status.Phase == SyncPhase.Idle))
        {
            return "Alle Ordner sind auf dem neuesten Stand." + suffix;
        }

        var problem = states.FirstOrDefault(s => s.Status.Phase is SyncPhase.SignInFailed or SyncPhase.CertificateProblem or SyncPhase.Offline);
        if (problem.Account is not null)
        {
            return $"{problem.Account.DisplayName}: {problem.Status.Message}{suffix}";
        }

        return states.Any(s => s.Status.Phase is SyncPhase.Syncing or SyncPhase.Connecting) ? "Senden/Empfangen …" + suffix : "Verbunden" + suffix;
    }

    public override void Detach()
    {
        _detached = true;
        Document.Store.MessagesChanged -= OnMessagesChanged;
        Document.Store.FoldersChanged -= OnFoldersChanged;
        Document.Store.QueueChanged -= OnQueueChanged;
        Document.Accounts.Changed -= OnFoldersChanged;
        Document.SyncStatusChanged -= OnSyncStatusChanged;
        Document.Changed -= OnDocumentChanged;
        _markRead.Stop();
        _searchDelay.Stop();
    }
}

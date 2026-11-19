using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MatCloud.Contract.Drive;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>What the ThixDrive place does with a file: open it, or save the window's document into a folder.</summary>
public enum DriveBrowserMode
{
    Open,
    Save,
}

/// <summary>A row of the ThixDrive list.</summary>
public sealed record DriveRow(DriveItem Item, string Icon, string Name, string Location, bool ShowsLocation, string Modified, string Size)
{
    public bool IsFolder => Item.Kind == DriveItemKind.Folder;
}

/// <summary>
/// The place ThixDrive (Datei › Öffnen and Datei › Speichern unter): browse the folders of the Drive with paging, search across it,
/// filter by type, sort; open a file (<see cref="OfficeApplication.OpenFromDriveAsync"/>) or save the document into the folder shown
/// (<see cref="OfficeWindow.SaveIntoDriveAsync"/>); new folder, rename, into the trash. Everything goes through
/// <see cref="DriveService"/>; not signed in, it shows the compact <see cref="CloudSignInPanel"/>. Several instances may live at once.
/// </summary>
public partial class DriveBrowser : UserControl
{
    private readonly OfficeWindow? _window;
    private readonly List<DriveItem> _loaded = [];
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly IReadOnlyList<DocumentType> _documentTypes = [];
    private readonly IReadOnlyList<DocumentType> _saveTypes = [];
    private IReadOnlyList<DriveCrumb> _ancestors = [];
    private CancellationTokenSource? _loading;
    private string? _search;
    private int _total;
    private bool _isBusy;

    /// <summary>For the XAML previewer.</summary>
    public DriveBrowser()
    {
        InitializeComponent();
    }

    public DriveBrowser(OfficeWindow window, DriveBrowserMode mode)
        : this()
    {
        _window = window;
        Mode = mode;
        var definition = AppInfo.Definition;
        _documentTypes = definition.SupportedTypes;
        _saveTypes = definition.SaveAsTypes; // PDF last: a copy, the document stays as it is
        var extensions = string.Join(", ", _documentTypes.Select(type => type.Extension));
        TypeChoice.ItemsSource = new[] { $"Dokumente ({extensions})", "Alle Dateien" };
        TypeChoice.SelectedIndex = 0;
        SortChoice.SelectedIndex = 0;

        if (mode == DriveBrowserMode.Save)
        {
            SaveFields.IsVisible = true;
            PrimaryLabel.Icon = "save";
            PrimaryLabel.Text = "Speichern";
            SaveTypeChoice.ItemsSource = _saveTypes.Select(type => $"{type.Description} (*{type.Extension})").ToList();
            var document = window.Document;
            var type = document.FileType ?? definition.SaveType;
            SaveTypeChoice.SelectedIndex = Math.Max(0, _saveTypes.ToList().IndexOf(type));
            FileNameBox.Text = Path.GetFileNameWithoutExtension(document.Drive?.Name ?? DocumentService.SuggestedFileName(document));
            FolderId = document.Drive?.ParentId;
            SaveTypeChoice.SelectionChanged += (_, _) => OnSaveTypeChanged();
        }

        SearchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = SearchAsync(SearchBox.Text);
        };
        SortChoice.SelectionChanged += (_, _) => _ = ReloadAsync();
        TypeChoice.SelectionChanged += (_, _) => ShowRows();
        List.SelectionChanged += (_, _) => OnSelectionChanged();
        List.DoubleTapped += async (_, _) => await ActivateSelectedAsync();
        List.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ActivateSelectedAsync();
            }
        };
        SignIn.SignedIn += (_, _) => RefreshPlace();
        CloudService.StatusChanged += OnCloudStatusChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            CloudService.StatusChanged -= OnCloudStatusChanged;
            _searchTimer.Stop();
            _loading?.Cancel();
        };
        RefreshPlace();
    }

    public DriveBrowserMode Mode { get; }

    /// <summary>The folder shown (null: the top of the Drive).</summary>
    public long? FolderId { get; private set; }

    /// <summary>The rows shown (tests, snapshots).</summary>
    public IReadOnlyList<DriveRow> Rows { get; private set; } = [];

    /// <summary>The loading that runs or ran last (tests wait for it).</summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>True while the sign-in shows instead of the list.</summary>
    public bool ShowsSignIn => SignInPlace.IsVisible;

    public CloudSignInPanel SignInPanel => SignIn;

    public TextBox FileNameField => FileNameBox;

    /// <summary>The type to save as (save mode; the document's types, then PDF).</summary>
    public ComboBox SaveTypeField => SaveTypeChoice;

    public InfoBar MessageBar => Message;

    /// <summary>The breadcrumbs as text: "ThixDrive › Projekte" (tests).</summary>
    public string CrumbText => string.Join(" › ", Crumbs.Children.Select(child => child switch
    {
        Button { Content: TextBlock text } => text.Text,
        TextBlock { Text: not "›" } text => text.Text,
        _ => null,
    }).OfType<string>());

    /// <summary>Shows a folder (null: the top); a search ends.</summary>
    public Task NavigateAsync(long? folderId)
    {
        FolderId = folderId;
        _search = null;
        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            _searchTimer.Stop();
            SearchBox.Text = "";
            _searchTimer.Stop();
        }

        return ReloadAsync();
    }

    /// <summary>Searches the whole Drive (empty: back to the folder).</summary>
    public Task SearchAsync(string? text)
    {
        _search = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return ReloadAsync();
    }

    /// <summary>Chooses the row named <paramref name="name"/> (tests, snapshots).</summary>
    public void Select(string name) => List.SelectedItem = Rows.FirstOrDefault(row => row.Name == name);

    /// <summary>A double click or Enter: a folder opens, a file is opened (or chosen as the name to save as and saved).</summary>
    public async Task ActivateSelectedAsync()
    {
        if (List.SelectedItem is not DriveRow row || _isBusy)
        {
            return;
        }

        if (row.IsFolder)
        {
            await NavigateAsync(row.Item.Id);
            return;
        }

        if (Mode == DriveBrowserMode.Save)
        {
            FileNameBox.Text = row.Name;
            await SaveAsync();
            return;
        }

        await OpenAsync(row.Item);
    }

    /// <summary>"Speichern": the document into the folder shown, under the name typed (a taken name is asked about).</summary>
    public async Task SaveAsync()
    {
        if (_window is null || _isBusy)
        {
            return;
        }

        await BusyAsync(async () =>
        {
            FormErrors.ShowError(this, null, "");
            try
            {
                if (await _window.SaveIntoDriveAsync(FolderId, NameWithType()))
                {
                    _window.Backstage.Close();
                }
            }
            catch (BusinessException exception)
            {
                ShowError(exception);
            }
        });
    }

    // ===== Loading =====

    private void OnCloudStatusChanged(object? sender, CloudStatus status) => Ui.Post(() =>
    {
        if (ShowsSignIn == CloudService.IsConnected)
        {
            RefreshPlace();
        }
    });

    /// <summary>The list when signed in, the sign-in otherwise.</summary>
    private void RefreshPlace()
    {
        var connected = CloudService.IsConnected;
        SignInPlace.IsVisible = !connected;
        BrowserPlace.IsVisible = connected;
        if (connected)
        {
            _ = ReloadAsync();
        }
    }

    private Task ReloadAsync()
    {
        LoadTask = LoadAsync(append: false);
        return LoadTask;
    }

    private async Task LoadAsync(bool append)
    {
        if (!CloudService.IsConnected)
        {
            return;
        }

        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        var offset = append ? _loaded.Count : 0;
        Progress.IsVisible = true;
        try
        {
            IReadOnlyList<DriveItem> items;
            if (_search is { } text)
            {
                var page = await DriveService.SearchAsync(text, offset, loading.Token);
                (items, _total) = (page.Items, page.Total);
            }
            else
            {
                var (sort, descending) = SelectedSort();
                var listing = await DriveService.ListAsync(FolderId, sort, descending, offset, loading.Token);
                (items, _total, _ancestors) = (listing.Items, listing.Total, listing.Ancestors);
            }

            if (loading.IsCancellationRequested)
            {
                return;
            }

            if (!append)
            {
                _loaded.Clear();
            }

            _loaded.AddRange(items);
            Message.Hide();
        }
        catch (BusinessException exception)
        {
            if (loading.IsCancellationRequested)
            {
                return;
            }

            if (!append)
            {
                _loaded.Clear();
                _total = 0;
            }

            Message.Show(exception.Message, InfoSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                Progress.IsVisible = false;
            }
        }

        ShowRows();
        ShowCrumbs();
    }

    private (DriveSort Sort, bool Descending) SelectedSort() => SortChoice.SelectedIndex switch
    {
        1 => (DriveSort.Modified, true),
        2 => (DriveSort.Size, true),
        3 => (DriveSort.Type, false),
        _ => (DriveSort.Name, false),
    };

    private void ShowRows()
    {
        var selectedId = (List.SelectedItem as DriveRow)?.Item.Id;
        var types = TypeChoice.SelectedIndex == 1 ? null : _documentTypes;
        IEnumerable<DriveItem> items = _loaded.Where(item => DriveService.Matches(item, types));
        if (_search is not null)
        {
            items = SortSearchResults(items);
        }

        Rows = items.Select(item => ToRow(item, showsLocation: _search is not null)).ToList();
        List.ItemsSource = Rows;
        List.SelectedItem = Rows.FirstOrDefault(row => row.Item.Id == selectedId);
        EmptyText.Text = _search is not null ? $"Nichts in ThixDrive passt zu „{_search}“."
            : _loaded.Count > 0 ? "In diesem Ordner liegt nichts von diesem Typ. „Alle Dateien“ zeigt alles."
            : FolderId is null ? "Ihr ThixDrive ist noch leer. Was Sie hier speichern, erscheint in dieser Liste." : "Dieser Ordner ist leer.";
        EmptyText.IsVisible = Rows.Count == 0 && !Message.IsVisible;
        MoreButton.IsVisible = _loaded.Count < _total;
        CountText.Text = _loaded.Count < _total ? $"{_loaded.Count} von {_total} geladen" : "";
        UpdateActions();
    }

    private IEnumerable<DriveItem> SortSearchResults(IEnumerable<DriveItem> items)
    {
        var folderFirst = items.OrderBy(item => item.Kind);
        return SelectedSort() switch
        {
            (DriveSort.Modified, _) => folderFirst.ThenByDescending(item => item.ModifiedAt),
            (DriveSort.Size, _) => folderFirst.ThenByDescending(item => item.Size),
            (DriveSort.Type, _) => folderFirst.ThenBy(item => Path.GetExtension(item.Name), StringComparer.OrdinalIgnoreCase),
            _ => folderFirst.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }

    private void ShowCrumbs()
    {
        Crumbs.Children.Clear();
        var trail = new List<(string Name, long? Id)> { ("ThixDrive", null) };
        if (_search is null)
        {
            trail.AddRange(_ancestors.Select(crumb => (crumb.Name, (long?)crumb.Id)));
        }

        for (var index = 0; index < trail.Count; index++)
        {
            var (name, id) = trail[index];
            var isLast = index == trail.Count - 1 && _search is null;
            if (index > 0)
            {
                Crumbs.Children.Add(Separator());
            }

            if (isLast)
            {
                Crumbs.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(8, 4) });
                continue;
            }

            var button = new Button { Content = new TextBlock { Text = name }, Theme = Ui.Theme("MatButton.Ghost"), Padding = new Avalonia.Thickness(8, 4) };
            Avalonia.Automation.AutomationProperties.SetName(button, $"Zu {name}");
            button.Click += async (_, _) => await NavigateAsync(id);
            Crumbs.Children.Add(button);
        }

        if (_search is not null)
        {
            Crumbs.Children.Add(Separator());
            Crumbs.Children.Add(new TextBlock { Text = $"Suche: „{_search}“", FontWeight = FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(8, 4) });
        }

        static TextBlock Separator() => new TextBlock { Text = "›", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
    }

    private static DriveRow ToRow(DriveItem item, bool showsLocation) => new(
        item,
        IconOf(item),
        item.Name,
        item.Location,
        showsLocation,
        Ui.FormatDateTime(item.ModifiedAt),
        item.Kind == DriveItemKind.Folder ? "" : FormatSize(item.Size));

    /// <summary>The type icon of an item (the sprite has no icon per format: text, table, slides, picture, any file).</summary>
    internal static string IconOf(DriveItem item) => item.Kind == DriveItemKind.Folder
        ? "folder"
        : Path.GetExtension(item.Name).ToLowerInvariant() switch
        {
            ".docx" or ".doc" or ".odt" or ".rtf" or ".txt" or ".md" or ".pdf" => "file-text",
            ".xlsx" or ".xls" or ".ods" or ".csv" => "table",
            ".pptx" or ".ppt" or ".odp" => "presentation",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" => "image",
            _ => "file",
        };

    /// <summary>"820 Byte", "48 KB", "2,2 MB".</summary>
    internal static string FormatSize(long bytes)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        return bytes switch
        {
            < 1024 => $"{bytes} Byte",
            < 1024 * 1024 => $"{Math.Max(1, (int)Math.Round(bytes / 1024.0))} KB",
            < 1024L * 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString("0.#", culture) + " MB",
            _ => (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#", culture) + " GB",
        };
    }

    // ===== Actions =====

    private void OnSelectionChanged()
    {
        if (Mode == DriveBrowserMode.Save && List.SelectedItem is DriveRow { IsFolder: false } row)
        {
            FileNameBox.Text = Path.GetFileNameWithoutExtension(row.Name);
            var type = AppInfo.Definition.SupportedTypeOf(row.Name) ?? AppInfo.Definition.ExportTypeOf(row.Name);
            if (type is not null)
            {
                SaveTypeChoice.SelectedIndex = _saveTypes.ToList().IndexOf(type);
            }
        }

        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = List.SelectedItem as DriveRow;
        PrimaryButton.IsEnabled = !_isBusy && (Mode == DriveBrowserMode.Save || selected is not null && (selected.IsFolder || DriveService.CanOpen(selected.Item)));
        PrimaryLabel.Text = Mode == DriveBrowserMode.Save ? "Speichern" : selected?.IsFolder == true ? "Ordner öffnen" : "Öffnen";
        NewFolderButton.IsEnabled = !_isBusy && _search is null;
        RenameButton.IsEnabled = !_isBusy && selected is not null;
        TrashButton.IsEnabled = !_isBusy && selected is not null;
    }

    private void OnSaveTypeChanged()
    {
        if (SaveTypeChoice.SelectedIndex >= 0 && SaveTypeChoice.SelectedIndex < _saveTypes.Count)
        {
            FileNameBox.Text = Path.GetFileNameWithoutExtension(FileNameBox.Text ?? "");
        }
    }

    /// <summary>The file name with the extension of the chosen type (a name typed with a supported extension keeps it).</summary>
    private string NameWithType()
    {
        var name = (FileNameBox.Text ?? "").Trim();
        if (name.Length == 0 || AppInfo.Definition.SupportedTypeOf(name) is not null || AppInfo.Definition.ExportTypeOf(name) is not null)
        {
            return name;
        }

        var type = SaveTypeChoice.SelectedIndex >= 0 && SaveTypeChoice.SelectedIndex < _saveTypes.Count ? _saveTypes[SaveTypeChoice.SelectedIndex] : AppInfo.Definition.SaveType;
        return name + type.Extension;
    }

    private async Task OpenAsync(DriveItem item)
    {
        if (_window is null)
        {
            return;
        }

        if (!DriveService.CanOpen(item))
        {
            Message.Show($"{AppInfo.ProductName} kann „{item.Name}“ nicht öffnen.", InfoSeverity.Warning);
            return;
        }

        await BusyAsync(async () =>
        {
            Message.Hide();
            if (await OfficeApplication.Instance.OpenFromDriveAsync(item.Id, _window) is not null)
            {
                _window.Backstage.Close();
            }
        });
    }

    private async void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (Mode == DriveBrowserMode.Save)
        {
            await SaveAsync();
            return;
        }

        await ActivateSelectedAsync();
    }

    private async void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        LoadTask = LoadAsync(append: true);
        await LoadTask;
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Ui.WindowOf(this) is not { } owner)
        {
            return;
        }

        string? error = null;
        var name = "Neuer Ordner";
        while (await TextInputDialog.AskAsync(owner, "Neuer Ordner", "Name des Ordners", name, "Anlegen", error) is { } typed)
        {
            name = typed;
            try
            {
                var folder = await DriveService.CreateFolderAsync(FolderId, typed);
                await ReloadAsync();
                List.SelectedItem = Rows.FirstOrDefault(row => row.Item.Id == folder.Id);
                return;
            }
            catch (BusinessException exception)
            {
                error = exception.Message;
            }
        }
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (Ui.WindowOf(this) is not { } owner || List.SelectedItem is not DriveRow row)
        {
            return;
        }

        string? error = null;
        var name = row.Name;
        while (await TextInputDialog.AskAsync(owner, "Umbenennen", row.IsFolder ? "Name des Ordners" : "Name der Datei", name, "Umbenennen", error) is { } typed)
        {
            name = typed;
            try
            {
                await DriveService.RenameAsync(row.Item.Id, typed);
                await ReloadAsync();
                List.SelectedItem = Rows.FirstOrDefault(candidate => candidate.Item.Id == row.Item.Id);
                return;
            }
            catch (BusinessException exception)
            {
                error = exception.Message;
            }
        }
    }

    private async void OnTrashClick(object? sender, RoutedEventArgs e)
    {
        if (Ui.WindowOf(this) is not { } owner || List.SelectedItem is not DriveRow row)
        {
            return;
        }

        var what = row.IsFolder ? $"Den Ordner „{row.Name}“ mit allem, was darin liegt," : $"„{row.Name}“";
        if (!await QuestionDialog.ConfirmAsync(owner, "In den Papierkorb legen?",
                $"{what} in den Papierkorb von ThixDrive legen? Dort bleibt es {DriveContract.TrashDays} Tage und lässt sich im Browser wiederherstellen.",
                "In den Papierkorb", isDestructive: true))
        {
            return;
        }

        try
        {
            await DriveService.TrashAsync(row.Item.Id);
            await ReloadAsync();
            Message.Show($"„{row.Name}“ liegt im Papierkorb von ThixDrive.", InfoSeverity.Info);
        }
        catch (BusinessException exception)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
        }
    }

    private async Task BusyAsync(Func<Task> work)
    {
        _isBusy = true;
        Progress.IsVisible = true;
        UpdateActions();
        try
        {
            await work();
        }
        finally
        {
            _isBusy = false;
            Progress.IsVisible = false;
            UpdateActions();
        }
    }

    private void ShowError(BusinessException exception)
    {
        if (!FormErrors.ShowError(this, exception.Field, exception.Message))
        {
            Message.Show(exception.Message, InfoSeverity.Error);
        }
    }
}

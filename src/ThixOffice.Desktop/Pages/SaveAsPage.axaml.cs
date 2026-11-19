using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>
/// Datei › Speichern unter: the places "Zuletzt verwendet" (recent folders as starting points of the system's dialog), "ThixDrive"
/// (<see cref="DriveBrowser"/> in its save mode) and "Dieser PC" (the document's place and format, Durchsuchen). A ThixDrive document
/// starts in ThixDrive, any other in "Zuletzt verwendet".
/// </summary>
public partial class SaveAsPage : UserControl
{
    private const int FolderCount = 6;

    private readonly OfficeWindow? _window;

    /// <summary>For the XAML previewer.</summary>
    public SaveAsPage()
    {
        InitializeComponent();
    }

    public SaveAsPage(OfficeWindow window)
        : this()
    {
        _window = window;
        var document = window.Document;
        NameText.Text = DocumentService.SuggestedFileName(document);
        LocationText.Text = document.IsInDrive ? document.LocationText : document.FilePath is { } path ? Path.GetDirectoryName(path) : "Noch nicht gespeichert";
        var type = document.FileType ?? AppInfo.Definition.SaveType;
        FormatText.Text = $"{type.Description} ({type.Extension})";

        var folders = RecentDocuments.RecentFolders(FolderCount);
        NoFoldersText.IsVisible = folders.Count == 0;
        Folders.ItemsSource = folders.Select(folder => FolderButton(folder, $"Speichern in {folder}", () => SaveInAsync(folder))).ToList();

        DrivePlaceItem.IsVisible = CloudService.IsAvailable;
        Places.SelectionChanged += (_, _) => ShowPlace(ReferenceEquals(Places.SelectedItem, DrivePlaceItem) ? DocumentPlace.Drive
            : ReferenceEquals(Places.SelectedItem, ComputerPlaceItem) ? DocumentPlace.Computer : DocumentPlace.Recent);
        ShowPlace(document.IsInDrive ? DocumentPlace.Drive : DocumentPlace.Recent);
    }

    /// <summary>The folders offered (tests, snapshots).</summary>
    public IReadOnlyList<string> FolderPaths => (Folders.ItemsSource as IEnumerable<Button>)?.Select(button => (string)button.Tag!).ToList() ?? [];

    /// <summary>The place shown.</summary>
    public DocumentPlace Place { get; private set; }

    /// <summary>The ThixDrive place once it was shown (tests, snapshots).</summary>
    public DriveBrowser? Drive => DrivePlace.Content as DriveBrowser;

    /// <summary>Shows a place (the list of places follows).</summary>
    public void ShowPlace(DocumentPlace place)
    {
        if (place == DocumentPlace.Drive && !DrivePlaceItem.IsVisible)
        {
            place = DocumentPlace.Recent;
        }

        Place = place;
        Places.SelectedItem = place switch
        {
            DocumentPlace.Drive => DrivePlaceItem,
            DocumentPlace.Computer => ComputerPlaceItem,
            _ => RecentPlaceItem,
        };
        RecentPlace.IsVisible = place == DocumentPlace.Recent;
        ComputerPlace.IsVisible = place == DocumentPlace.Computer;
        DrivePlace.IsVisible = place == DocumentPlace.Drive;
        if (place == DocumentPlace.Drive && DrivePlace.Content is null && _window is not null)
        {
            DrivePlace.Content = new DriveBrowser(_window, DriveBrowserMode.Save);
        }
    }

    /// <summary>A folder as a row to click (name, full path, folder icon) – recent folders of Öffnen and Speichern unter.</summary>
    internal static Button FolderButton(string folder, string automationName, Func<Task> click)
    {
        var name = new TextBlock { Text = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } leaf ? leaf : folder, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var full = new TextBlock { Text = folder, TextTrimming = Avalonia.Media.TextTrimming.PrefixCharacterEllipsis }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var icon = new MatIcon { Icon = "folder", Size = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }.Use(MatIcon.ForegroundProperty, "Mat.Accent");
        var content = new DockPanel { Children = { icon, new StackPanel { Spacing = 2, Children = { name, full } } } };
        DockPanel.SetDock(icon, Dock.Left);
        var button = new Button
        {
            Content = content,
            Tag = folder,
            Theme = Ui.Theme("MatButton.Ghost"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinWidth = 420,
            Padding = new Thickness(10, 8),
        };
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await click();
        return button;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null && await _window.SaveAsAsync())
        {
            _window.Backstage.Close();
        }
    }

    /// <summary>The dialog of the system, starting in <paramref name="folder"/>.</summary>
    private async Task SaveInAsync(string folder)
    {
        if (_window is null)
        {
            return;
        }

        var target = await OfficeApplication.FileDialogs.PickSaveTargetAsync(_window, DocumentService.SuggestedFileName(_window.Document), folder);
        if (target is not null && await _window.SaveToAsync(target))
        {
            _window.Backstage.Close();
        }
    }
}

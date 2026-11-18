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

/// <summary>Datei › Speichern unter: the document's place and format, "Durchsuchen", and recent folders as starting points.</summary>
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
        LocationText.Text = document.FilePath is { } path ? Path.GetDirectoryName(path) : "Noch nicht gespeichert";
        var type = document.FileType ?? AppInfo.Definition.SaveType;
        FormatText.Text = $"{type.Description} ({type.Extension})";

        var folders = RecentDocuments.RecentFolders(FolderCount);
        FoldersSection.IsVisible = folders.Count > 0;
        Folders.ItemsSource = folders.Select(FolderButton).ToList();
    }

    /// <summary>The folders offered (tests, snapshots).</summary>
    public IReadOnlyList<string> FolderPaths => (Folders.ItemsSource as IEnumerable<Button>)?.Select(button => (string)button.Tag!).ToList() ?? [];

    private Button FolderButton(string folder)
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
        AutomationProperties.SetName(button, $"Speichern in {folder}");
        button.Click += async (_, _) => await SaveInAsync(folder);
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

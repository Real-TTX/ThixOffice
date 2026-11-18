using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>A row of "Zuletzt verwendet" as the list shows it.</summary>
public sealed record RecentRow(string Path, string FileName, string Folder, string State, bool IsPinned, bool Exists)
{
    public string Icon => Exists ? "file-text" : "alert";
}

/// <summary>
/// Datei › Öffnen: "Durchsuchen" and "Zuletzt verwendet" (search, sort, pin, show in the folder, remove from the list,
/// remove the ones that are gone). Every change of the list goes through <see cref="RecentDocuments"/>.
/// </summary>
public partial class OpenPage : UserControl
{
    private readonly OfficeWindow? _window;

    /// <summary>For the XAML previewer.</summary>
    public OpenPage()
    {
        InitializeComponent();
    }

    public OpenPage(OfficeWindow window)
        : this()
    {
        _window = window;
        SortChoice.SelectedIndex = 0;
        SearchBox.TextChanged += (_, _) => Refresh();
        SortChoice.SelectionChanged += (_, _) => Refresh();
        List.SelectionChanged += (_, _) => UpdateActions();
        List.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        List.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSelectedAsync();
            }
        };
        RecentDocuments.Changed += OnRecentChanged;
        DetachedFromVisualTree += (_, _) => RecentDocuments.Changed -= OnRecentChanged;
        Refresh();
    }

    /// <summary>The rows shown (tests).</summary>
    public IReadOnlyList<RecentRow> Rows { get; private set; } = [];

    /// <summary>Chooses the row of <paramref name="path"/> (tests, snapshots).</summary>
    public void Select(string path) => List.SelectedItem = Rows.FirstOrDefault(row => row.Path == path);

    private RecentRow? Selected => List.SelectedItem as RecentRow;

    private void OnRecentChanged(object? sender, EventArgs e) => Ui.Post(Refresh);

    private void Refresh()
    {
        var selectedPath = Selected?.Path;
        var order = SortChoice.SelectedIndex == 1 ? RecentDocumentOrder.Name : RecentDocumentOrder.LastOpened;
        Rows = RecentDocuments.List(SearchBox.Text, order).Select(ToRow).ToList();
        List.ItemsSource = Rows;
        List.SelectedItem = Rows.FirstOrDefault(row => row.Path == selectedPath);
        var hasAny = RecentDocuments.List().Count > 0;
        EmptyText.Text = hasAny
            ? "Kein zuletzt verwendetes Dokument passt zur Suche."
            : "Noch keine Dokumente. Was Sie öffnen oder speichern, erscheint hier – angeheftete Dokumente bleiben oben.";
        EmptyText.IsVisible = Rows.Count == 0;
        RemoveMissingButton.IsVisible = Rows.Any(row => !row.Exists);
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = Selected;
        OpenButton.IsEnabled = selected is { Exists: true };
        PinButton.IsEnabled = selected is not null;
        RevealButton.IsEnabled = selected is { Exists: true };
        RemoveButton.IsEnabled = selected is not null;
        PinLabel.Text = selected?.IsPinned == true ? "Lösen" : "Anheften";
    }

    private static RecentRow ToRow(RecentDocumentItem item) => new(
        item.Document.Path,
        item.Document.FileName,
        item.Document.Folder,
        item.Exists ? Ui.FormatDateTime(item.Document.LastOpened) : "Nicht gefunden",
        item.Document.IsPinned,
        item.Exists);

    private async Task OpenSelectedAsync()
    {
        if (Selected is not { Exists: true } row || _window is null)
        {
            return;
        }

        _window.Backstage.Close();
        await OfficeApplication.Instance.OpenFilesAsync([row.Path], _window);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            await _window.OpenWithDialogAsync();
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private void OnPinClick(object? sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Selected is { } row)
        {
            RecentDocuments.Pin(row.Path, !row.IsPinned);
        }
    });

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { Exists: true } row)
        {
            Ui.RevealFile(row.Path);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Selected is { } row)
        {
            RecentDocuments.Remove(row.Path);
            Message.Show($"„{row.FileName}“ steht nicht mehr in der Liste. Die Datei selbst bleibt, wo sie ist.", InfoSeverity.Info);
        }
    });

    private void OnRemoveMissingClick(object? sender, RoutedEventArgs e) => Guard(() =>
    {
        var removed = RecentDocuments.RemoveMissing();
        Message.Show(removed == 1 ? "Ein nicht gefundenes Dokument wurde aus der Liste entfernt." : $"{removed} nicht gefundene Dokumente wurden aus der Liste entfernt.", InfoSeverity.Info);
    });

    private void Guard(Action action)
    {
        try
        {
            Message.Hide();
            action();
        }
        catch (BusinessException exception)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
        }
    }
}

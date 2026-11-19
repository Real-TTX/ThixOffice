using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// Einfügen › Tabelle of every app (Word's and PowerPoint's grid): a large ribbon button that opens a grid of 10 × 8 cells – pointing at
/// a cell shows the size ("3 × 4 Tabelle"), a click (or Enter after the arrow keys) inserts that table through the app's callback –
/// and below it the app's dialog command ("Tabelle einfügen …"), when it has one. Enabled while the app's picker command is.
/// </summary>
public sealed class TablePicker : Button
{
    public const int GridColumns = 10;
    public const int GridRows = 8;
    private const double CellSize = 18;

    private readonly OfficeWindow _window;
    private readonly string _commandId;
    private readonly Action<int, int> _insert;
    private readonly Border[,] _cells = new Border[GridRows, GridColumns];
    private readonly TextBlock _caption = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 0, 0, 8) };
    private readonly Flyout _flyout;
    private (int Rows, int Columns) _chosen = (1, 1);

    public TablePicker(OfficeWindow window, string commandId, Action<int, int> insert, string? dialogCommandId = null)
    {
        _window = window;
        _commandId = commandId;
        _insert = insert;
        this.Themed("MatRibbon.Item");
        Height = MatRibbon.ContentHeight;
        MinWidth = 46;
        Padding = new Thickness(4, 0, 4, 4);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Top;
        Content = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new MatIcon { Icon = "table", Size = 26, Margin = new Thickness(0, 6, 0, 2), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Tabelle", HorizontalAlignment = HorizontalAlignment.Center },
                new MatIcon { Icon = "chevron-down", Size = 12, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        AutomationProperties.SetName(this, "Tabelle");
        ToolTip.SetTip(this, "Tabelle – fügt eine Tabelle ein: Größe im Raster wählen.");

        Grid = BuildGrid();
        DialogButton = new Button { Content = new IconLabel { Icon = "table", Text = "Tabelle einfügen …" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0) };
        DialogButton.Click += async (_, _) =>
        {
            _flyout!.Hide();
            if (dialogCommandId is not null && _window.Commands.Find(dialogCommandId) is { } command)
            {
                await OfficeWindow.RunCommandAsync(_window, command);
            }
        };
        DialogButton.IsVisible = dialogCommandId is not null;
        var panel = new StackPanel { Margin = new Thickness(10), Children = { _caption, Grid, DialogButton } };
        panel.KeyDown += OnGridKey;
        _flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        Click += (_, _) => Open();
        window.Commands.StateChanged += (_, _) => Ui.Post(SyncEnabled);
        SyncEnabled();
        Highlight(1, 1);
    }

    /// <summary>The grid of cells (tests point at it).</summary>
    public Grid Grid { get; }

    public Button DialogButton { get; }

    /// <summary>True while the grid is open.</summary>
    public bool IsOpen => _flyout.IsOpen;

    /// <summary>The size shown ("3 × 4 Tabelle": rows, columns).</summary>
    public (int Rows, int Columns) Chosen => _chosen;

    /// <summary>The cell of the grid at (row, column), 0-based.</summary>
    public Border CellAt(int row, int column) => _cells[row, column];

    public void Open()
    {
        Highlight(1, 1);
        _flyout.ShowAt(this);
        Grid.Focus(NavigationMethod.Tab);
    }

    /// <summary>Inserts a table of <paramref name="rows"/> × <paramref name="columns"/> and closes the grid.</summary>
    public void Choose(int rows, int columns)
    {
        _flyout.Hide();
        _insert(rows, columns);
        _window.Editor.FocusDocument();
    }

    private Grid BuildGrid()
    {
        var grid = new Grid { Focusable = true };
        AutomationProperties.SetName(grid, "Tabellengröße");
        for (var column = 0; column < GridColumns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(CellSize + 3, GridUnitType.Pixel));
        }

        for (var row = 0; row < GridRows; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(CellSize + 3, GridUnitType.Pixel));
            for (var column = 0; column < GridColumns; column++)
            {
                var cell = new Border { Width = CellSize, Height = CellSize, BorderThickness = new Thickness(1) }.Use(Border.BorderBrushProperty, "Mat.BorderStrong");
                var (r, c) = (row + 1, column + 1);
                cell.PointerEntered += (_, _) => Highlight(r, c);
                cell.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    Choose(r, c);
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
                _cells[row, column] = cell;
            }
        }

        return grid;
    }

    private void Highlight(int rows, int columns)
    {
        _chosen = (rows, columns);
        _caption.Text = $"{columns} × {rows} Tabelle";
        for (var row = 0; row < GridRows; row++)
        {
            for (var column = 0; column < GridColumns; column++)
            {
                var inside = row < rows && column < columns;
                _cells[row, column].Use(Border.BackgroundProperty, inside ? "Mat.Selection" : "Mat.Surface");
                _cells[row, column].Use(Border.BorderBrushProperty, inside ? "Mat.Accent" : "Mat.BorderStrong");
            }
        }
    }

    private void OnGridKey(object? sender, KeyEventArgs e)
    {
        if (!Grid.IsFocused)
        {
            return;
        }

        var (rows, columns) = _chosen;
        switch (e.Key)
        {
            case Key.Right:
                columns = Math.Min(GridColumns, columns + 1);
                break;
            case Key.Left:
                columns = Math.Max(1, columns - 1);
                break;
            case Key.Down:
                rows = Math.Min(GridRows, rows + 1);
                break;
            case Key.Up:
                rows = Math.Max(1, rows - 1);
                break;
            case Key.Enter or Key.Space:
                e.Handled = true;
                Choose(rows, columns);
                return;
            default:
                return;
        }

        e.Handled = true;
        Highlight(rows, columns);
    }

    private void SyncEnabled() => IsEnabled = _window.Commands.Find(_commandId)?.IsEnabled ?? false;
}

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Layout › Seite einrichten (like Word's dialog): the tab Ränder (top, bottom, left, right in cm; Hochformat / Querformat) and
/// the tab Papier (the paper formats or a size of its own). OK applies everything as one undo step to the whole document.
/// </summary>
public sealed class PageSetupDialog : WriteDialog
{
    public const int MarginsTab = 0;
    public const int PaperTab = 1;

    private const double PointsPerCm = 72 / 2.54;
    private static readonly NumberFormatInfo German = CultureInfo.GetCultureInfo("de-DE").NumberFormat;

    private readonly WriteEditor _editor;
    private readonly RadioButton _portrait = new() { Content = "Hochformat", GroupName = "orientation" };
    private readonly RadioButton _landscape = new() { Content = "Querformat", GroupName = "orientation" };
    private readonly ComboBox _paper = new() { MinWidth = 260 };
    private bool _isUpdating;

    public PageSetupDialog(WriteEditor editor, int tab = MarginsTab)
        : base("Seite einrichten", 560)
    {
        _editor = editor;
        var setup = editor.Document.Content.FinalSection;
        MarginTop = Centimetres(setup.MarginTop);
        MarginBottom = Centimetres(setup.MarginBottom);
        MarginLeft = Centimetres(setup.MarginLeft);
        MarginRight = Centimetres(setup.MarginRight);
        PaperWidth = Centimetres(setup.PageWidth, 0.1, 60);
        PaperHeight = Centimetres(setup.PageHeight, 0.1, 60);
        (setup.Orientation == PageOrientation.Landscape ? _landscape : _portrait).IsChecked = true;
        _portrait.IsCheckedChanged += (_, _) => OnOrientationChanged();

        _paper.ItemsSource = WriteCommands.PaperSizes.Select(size => size.Label).Append("Benutzerdefiniert").ToList();
        AutomationProperties.SetName(_paper, "Papierformat");
        SelectPaperOf(setup.PageWidth, setup.PageHeight);
        _paper.SelectionChanged += (_, _) => OnPaperChosen();
        PaperWidth.ValueChanged += (_, _) => OnPaperSizeTyped();
        PaperHeight.ValueChanged += (_, _) => OnPaperSizeTyped();

        var margins = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        Place(margins, Field("Oben (cm)", MarginTop), 0, 0);
        Place(margins, Field("Unten (cm)", MarginBottom), 0, 2);
        Place(margins, Field("Links (cm)", MarginLeft), 1, 0);
        Place(margins, Field("Rechts (cm)", MarginRight), 1, 2);
        var orientation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { _portrait, _landscape } };
        var marginsPage = new StackPanel { Spacing = 12, Margin = new Thickness(0, 14, 0, 0), Children = { margins, Field("Ausrichtung", orientation) } };

        var size = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        Place(size, Field("Breite (cm)", PaperWidth), 0, 0);
        Place(size, Field("Höhe (cm)", PaperHeight), 0, 2);
        var paperPage = new StackPanel { Spacing = 12, Margin = new Thickness(0, 14, 0, 0), Children = { Field("Papierformat", _paper), size } };

        Tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Ränder", Content = marginsPage },
                new TabItem { Header = "Papier", Content = paperPage },
            },
            SelectedIndex = tab,
        };
        SetBody(Tabs);
    }

    public TabControl Tabs { get; }

    public NumericUpDown MarginTop { get; }

    public NumericUpDown MarginBottom { get; }

    public NumericUpDown MarginLeft { get; }

    public NumericUpDown MarginRight { get; }

    public NumericUpDown PaperWidth { get; }

    public NumericUpDown PaperHeight { get; }

    public RadioButton Landscape => _landscape;

    public ComboBox Paper => _paper;

    /// <summary>Shows the dialog for the window's document; OK changes its page setup.</summary>
    public static Task<bool> ShowAsync(Window owner, WriteEditor editor, int tab = MarginsTab) => new PageSetupDialog(editor, tab).ShowAsync(owner);

    protected override bool Apply()
    {
        var values = new[] { MarginTop.Value, MarginBottom.Value, MarginLeft.Value, MarginRight.Value, PaperWidth.Value, PaperHeight.Value };
        if (values.Any(value => value is null))
        {
            return false;
        }

        var width = (double)PaperWidth.Value!.Value * PointsPerCm;
        var height = (double)PaperHeight.Value!.Value * PointsPerCm;
        var top = (double)MarginTop.Value!.Value * PointsPerCm;
        var bottom = (double)MarginBottom.Value!.Value * PointsPerCm;
        var left = (double)MarginLeft.Value!.Value * PointsPerCm;
        var right = (double)MarginRight.Value!.Value * PointsPerCm;
        if (left + right >= width - 36 || top + bottom >= height - 36)
        {
            Tabs.SelectedIndex = MarginsTab;
            return false; // the margins leave no room for text (NumericUpDown shows the values; Word refuses the same way)
        }

        var landscape = _landscape.IsChecked == true;
        _editor.Session.SetPageSetup(setup => setup with
        {
            PageWidth = width,
            PageHeight = height,
            Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait,
            MarginTop = top,
            MarginBottom = bottom,
            MarginLeft = left,
            MarginRight = right,
        });
        return true;
    }

    /// <summary>Hochformat / Querformat swaps width and height (the paper stays the same).</summary>
    private void OnOrientationChanged()
    {
        var landscape = _landscape.IsChecked == true;
        var (width, height) = (PaperWidth.Value ?? 0, PaperHeight.Value ?? 0);
        if ((landscape && width < height) || (!landscape && width > height))
        {
            _isUpdating = true;
            (PaperWidth.Value, PaperHeight.Value) = (height, width);
            _isUpdating = false;
        }
    }

    private void OnPaperChosen()
    {
        if (_isUpdating || _paper.SelectedIndex < 0 || _paper.SelectedIndex >= WriteCommands.PaperSizes.Count)
        {
            return;
        }

        var (_, _, width, height) = WriteCommands.PaperSizes[_paper.SelectedIndex];
        var landscape = _landscape.IsChecked == true;
        _isUpdating = true;
        PaperWidth.Value = (decimal)Math.Round((landscape ? height : width) / PointsPerCm, 2);
        PaperHeight.Value = (decimal)Math.Round((landscape ? width : height) / PointsPerCm, 2);
        _isUpdating = false;
    }

    private void OnPaperSizeTyped()
    {
        if (_isUpdating)
        {
            return;
        }

        SelectPaperOf((double)(PaperWidth.Value ?? 0) * PointsPerCm, (double)(PaperHeight.Value ?? 0) * PointsPerCm);
    }

    private void SelectPaperOf(double width, double height)
    {
        var setup = new SectionProperties { PageWidth = width, PageHeight = height };
        var index = WriteCommands.PaperSizes.ToList().FindIndex(size => WriteCommands.IsPaper(setup, size.Width, size.Height));
        _isUpdating = true;
        _paper.SelectedIndex = index >= 0 ? index : WriteCommands.PaperSizes.Count;
        _isUpdating = false;
    }

    private static NumericUpDown Centimetres(double points, double minimum = 0, double maximum = 30) => new()
    {
        Value = (decimal)Math.Round(points / PointsPerCm, 2),
        Minimum = (decimal)minimum,
        Maximum = (decimal)maximum,
        Increment = 0.1m,
        FormatString = "0.00",
        NumberFormat = German,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Charts;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Charts;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;

namespace ThixCalc.Dialogs;

/// <summary>A chart drawn small (the dialogs' previews): the workbook's data, theme and number formats, scaled to the control.</summary>
public sealed class ChartPreview : Control
{
    private readonly CalcEditor _editor;
    private Chart? _chart;

    public ChartPreview(CalcEditor editor)
    {
        _editor = editor;
        ClipToBounds = true;
    }

    public Chart? Chart
    {
        get => _chart;
        set
        {
            _chart = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_chart is not { } chart || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var workbook = _editor.Session.Workbook;
        var rect = new ThixCalc.Core.Layout.PixelRect(0, 0, Bounds.Width, Bounds.Height);
        var items = ChartLayer.RenderChart(chart, workbook, _editor.Metrics, new WorkbookChartSource(workbook, _editor.Session.Locale), rect, Bounds.Width / 360, 0);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas => SkiaPageRenderer.Draw(canvas, items, CalcFonts.Skia)));
    }
}

/// <summary>
/// Diagrammtyp ändern: the families at the left, the types of the chosen family as choices, a preview of the chart in the
/// chosen type (its own data) below them.
/// </summary>
public sealed class ChangeChartTypeDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly SheetChart _chart;
    private readonly ListBox _families;
    private readonly WrapPanel _types = new() { Orientation = Orientation.Horizontal };
    private readonly ChartPreview _preview;
    private ChartTemplate _chosen;

    private ChangeChartTypeDialog(CalcEditor editor, SheetChart chart)
        : base("Diagrammtyp ändern", 720)
    {
        _editor = editor;
        _chart = chart;
        _chosen = ChartTemplates.Of(chart.Chart) ?? ChartTemplates.All[0];
        var families = Enum.GetValues<ChartFamily>();
        _families = new ListBox
        {
            ItemsSource = families.Select(family => new IconLabelItem(CalcChartCommands.IconOf(family), ChartTemplates.FamilyLabel(family))).ToList(),
            SelectedIndex = Array.IndexOf(families, _chosen.Family),
            Width = 170,
        };
        _families.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<IconLabelItem>((item, _) => new ThixOffice.Desktop.Controls.IconLabel { Icon = item.Icon, Text = item.Label });
        AutomationProperties.SetName(_families, "Diagrammfamilie");
        _families.SelectionChanged += (_, _) =>
        {
            if (_families.SelectedIndex >= 0)
            {
                var family = families[_families.SelectedIndex];
                if (_chosen.Family != family)
                {
                    _chosen = ChartTemplates.All.First(template => template.Family == family);
                }

                ShowTypes();
            }
        };
        _preview = new ChartPreview(editor) { Width = 480, Height = 288, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var right = new StackPanel { Spacing = 14, Children = { _types, _preview } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*") };
        Grid.SetColumn(right, 2);
        body.Children.Add(_families);
        body.Children.Add(right);
        SetBody(body);
        ShowTypes();
    }

    /// <summary>The type chosen now (tests).</summary>
    public ChartTemplate Chosen => _chosen;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, SheetChart chart) => Create(editor, chart).ShowAsync(owner);

    public static ChangeChartTypeDialog Create(CalcEditor editor, SheetChart chart) => new(editor, chart);

    /// <summary>Chooses a type (tests and the type buttons).</summary>
    public void Choose(ChartTemplate template)
    {
        _chosen = template;
        _families.SelectedIndex = Array.IndexOf(Enum.GetValues<ChartFamily>(), template.Family);
        ShowTypes();
    }

    private void ShowTypes()
    {
        _types.Children.Clear();
        foreach (var template in ChartTemplates.All.Where(template => template.Family == _chosen.Family))
        {
            var choice = template;
            var button = new RadioButton { Content = template.Label, GroupName = "chartType", IsChecked = template == _chosen, Margin = new Thickness(0, 0, 16, 6) };
            ToolTip.SetTip(button, template.Description.Length > 0 ? template.Description : template.Label);
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true && _chosen != choice)
                {
                    _chosen = choice;
                    _preview.Chart = ChartFactory.WithType(_chart.Chart, _chosen);
                }
            };
            _types.Children.Add(button);
        }

        _preview.Chart = ChartFactory.WithType(_chart.Chart, _chosen);
    }

    protected override bool Apply()
    {
        var template = _chosen;
        _ = _editor.RunAsync("Diagrammtyp ändern", () => _editor.Session.ChangeChartType(_chart.Id, template));
        return true;
    }

    private sealed record IconLabelItem(string Icon, string Label);
}

/// <summary>
/// Datenquelle auswählen: the chart's data range (with its sheet), whether the series run along rows, and – for a look before
/// OK – the series and categories that range gives.
/// </summary>
public sealed class SelectDataDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly SheetChart _chart;
    private readonly TextBox _range = new() { MinWidth = 320 };
    private readonly CheckBox _rows = new() { Content = "Datenreihen in Zeilen" };
    private readonly MatFormField _rangeField;
    private readonly TextBlock _series = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _categories = new() { TextWrapping = TextWrapping.Wrap };

    private SelectDataDialog(CalcEditor editor, SheetChart chart)
        : base("Datenquelle auswählen", 560)
    {
        _editor = editor;
        _chart = chart;
        AutomationProperties.SetName(_range, "Diagrammdatenbereich");
        if (ChartSources.RangeOf(editor.Session.Workbook, chart.Chart) is { } source)
        {
            _range.Text = "=" + ChartReferences.Of(source.Sheet, source.Range);
        }

        var first = chart.Chart.AllSeries.FirstOrDefault()?.Values.Formula is { } formula
            ? ChartReferences.Resolve(editor.Session.Workbook, formula).FirstOrDefault()
            : default;
        _rows.IsChecked = first.Sheet is not null && first.Range.RowCount == 1 && first.Range.ColumnCount > 1;
        _rangeField = Field("Diagrammdatenbereich", _range);
        _range.TextChanged += (_, _) => Describe();
        _rows.IsCheckedChanged += (_, _) => Describe();
        _series.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _categories.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children = { _rangeField, _rows, Field("Legendeneinträge (Reihen)", _series), Field("Horizontale Achsenbeschriftungen (Kategorien)", _categories) },
        });
        Describe();
    }

    public TextBox RangeBox => _range;

    public CheckBox RowsBox => _rows;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, SheetChart chart) => Create(editor, chart).ShowAsync(owner);

    public static SelectDataDialog Create(CalcEditor editor, SheetChart chart) => new(editor, chart);

    /// <summary>The series and categories the range gives, or the reason it gives none.</summary>
    private void Describe()
    {
        _rangeField.Error = null;
        try
        {
            var areas = Areas();
            var scatter = _chart.Chart.Plots.Any(plot => plot.Type == ChartType.Scatter);
            var layout = ChartSources.Analyze(areas.Sheet, areas.Range, _rows.IsChecked == true, scatter);
            var workbook = _editor.Session.Workbook;
            var source = new WorkbookChartSource(workbook, _editor.Session.Locale);
            _series.Text = string.Join(", ", layout.Series.Select((series, index) => ChartDataResolver.TextOf(series.Name, source) is { Length: > 0 } name ? name : $"Datenreihe{index + 1}"));
            var categories = layout.Series.FirstOrDefault()?.Categories is { Formula: { } formula } ? source.Resolve(formula) : null;
            _categories.Text = categories is null ? "1, 2, 3 …" : string.Join(", ", categories.Take(12).Select(value => value.Text ?? string.Empty)) + (categories.Count > 12 ? " …" : string.Empty);
        }
        catch (BusinessException exception)
        {
            _series.Text = string.Empty;
            _categories.Text = string.Empty;
            _rangeField.Error = exception.Message;
        }
    }

    private (Worksheet Sheet, CellRange Range) Areas()
    {
        var text = (_range.Text ?? string.Empty).Trim().TrimStart('=');
        if (CellRange.TryParse(text.Replace("$", string.Empty, StringComparison.Ordinal)) is { } local)
        {
            return (_editor.Session.ActiveSheet, local);
        }

        var areas = ChartReferences.Resolve(_editor.Session.Workbook, text);
        return areas.Count == 1 ? areas[0] : throw new BusinessException("Geben Sie einen zusammenhängenden Bereich ein, z. B. =Tabelle1!$A$1:$C$13.", "range");
    }

    protected override bool Apply()
    {
        try
        {
            _editor.Session.SetChartData(_chart.Id, _range.Text ?? string.Empty, _rows.IsChecked == true);
            return true;
        }
        catch (BusinessException exception)
        {
            _rangeField.Error = exception.Message;
            return false;
        }
    }
}

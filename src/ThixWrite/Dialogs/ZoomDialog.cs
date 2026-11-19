using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixOffice.Desktop.Shell;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Ansicht › Zoom (and a click on the percentage of the status bar), like Word's dialog: 200 %, 100 %, 75 %, Seitenbreite,
/// Ganze Seite, or a percentage of its own (10–500 %).
/// </summary>
public sealed class ZoomDialog : WriteDialog
{
    private readonly WriteEditor _editor;
    private readonly List<(RadioButton Button, double? Percent, Action? Fit)> _choices = [];

    public ZoomDialog(WriteEditor editor)
        : base("Zoom", 420)
    {
        _editor = editor;
        Percent = new NumericUpDown
        {
            Value = (decimal)editor.Zoom,
            Minimum = (decimal)OfficeEditor.MinZoom,
            Maximum = (decimal)OfficeEditor.MaxZoom,
            Increment = 10,
            FormatString = "0",
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(Percent, "Prozent");

        var presets = new StackPanel { Spacing = 6 };
        foreach (var (label, percent, fit) in new (string, double?, Action?)[]
                 {
                     ("200 %", 200, null), ("100 %", 100, null), ("75 %", 75, null),
                     ("Seitenbreite", null, editor.ZoomToPageWidth), ("Ganze Seite", null, editor.ZoomToWholePage),
                 })
        {
            var button = new RadioButton { Content = label, GroupName = "zoom", IsChecked = percent == editor.Zoom };
            var value = percent;
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true && value is { } chosen)
                {
                    Percent.Value = (decimal)chosen;
                }
            };
            _choices.Add((button, percent, fit));
            presets.Children.Add(button);
        }

        Percent.ValueChanged += (_, _) =>
        {
            foreach (var (button, percent, _) in _choices)
            {
                if (button.IsChecked == true && percent is { } chosen && (decimal)chosen != Percent.Value)
                {
                    button.IsChecked = false;
                }
            }
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var custom = Field("Prozent", Percent);
        Grid.SetColumn(custom, 2);
        body.Children.Add(Field("Zoomen auf", presets));
        body.Children.Add(custom);
        SetBody(body);
    }

    public NumericUpDown Percent { get; }

    /// <summary>The preset buttons (tests).</summary>
    public IReadOnlyList<RadioButton> Presets => [.. _choices.Select(choice => choice.Button)];

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new ZoomDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var fit = _choices.FirstOrDefault(choice => choice.Button.IsChecked == true && choice.Fit is not null).Fit;
        if (fit is not null)
        {
            fit();
            return true;
        }

        if (Percent.Value is not { } percent)
        {
            return false;
        }

        _editor.Zoom = (double)percent;
        return true;
    }
}

using MatDesktop;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Functions;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// Funktionsargumente, like Excel's: one field per argument of the chosen function (required ones in bold, a repeating last
/// argument gets a new field once the last one is filled), beside each field what it gives now ("= 42", "= {10;20;30}"), below
/// what the function does, the argument the caret is in, and the result of the whole formula. OK gives the call's text.
/// </summary>
public sealed class FunctionArgumentsDialog : OfficeDialog
{
    private const int MaximumFields = 255;

    private readonly EditingSession _session;
    private readonly FunctionInfo _function;
    private readonly int _maximum;
    private readonly Grid _fields = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowSpacing = 6, ColumnSpacing = 10 };
    private readonly List<TextBox> _boxes = [];
    private readonly List<TextBlock> _previews = [];
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _argument = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { FontWeight = FontWeight.SemiBold };

    private FunctionArgumentsDialog(EditingSession session, FunctionInfo function, IReadOnlyList<string> arguments)
        : base("Funktionsargumente", 600)
    {
        _session = session;
        _function = function;
        _maximum = FunctionRegistry.Find(function.Canonical)?.MaxArguments ?? function.Arguments.Count;
        _description.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _argument.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _description.Text = function.Description;

        var initial = Math.Max(Math.Min(function.Arguments.Count, _maximum), arguments.Count);
        if (function.RepeatsLast && arguments.Count >= initial && initial < _maximum)
        {
            initial++;
        }

        for (var index = 0; index < initial; index++)
        {
            AddField(index < arguments.Count ? arguments[index] : string.Empty);
        }

        var scroller = new ScrollViewer { Content = _fields, MaxHeight = 260, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = function.Name, FontWeight = FontWeight.SemiBold },
                scroller,
                _description,
                _argument,
                _result,
            },
        });
        Update();
        Opened += (_, _) => _boxes.FirstOrDefault()?.Focus();
    }

    /// <summary>The argument fields (tests).</summary>
    public IReadOnlyList<TextBox> ArgumentBoxes => _boxes;

    /// <summary>What each argument gives now, as shown beside its field (tests).</summary>
    public IReadOnlyList<string> Previews => [.. _previews.Select(preview => preview.Text ?? string.Empty)];

    /// <summary>"Formelergebnis = …" (tests).</summary>
    public string ResultText => _result.Text ?? string.Empty;

    /// <summary>The call as text without "=" (after OK): "SVERWEIS(A1;B:C;2;FALSCH)".</summary>
    public string? CallText { get; private set; }

    /// <summary>Shows the dialog for <paramref name="function"/>; the call's text after OK, else null.</summary>
    public static async Task<string?> ShowAsync(Window owner, EditingSession session, FunctionInfo function, IReadOnlyList<string>? arguments = null)
    {
        var dialog = new FunctionArgumentsDialog(session, function, arguments ?? []);
        return await dialog.ShowAsync(owner) ? dialog.CallText : null;
    }

    /// <summary>The dialog for snapshots and tests (not shown).</summary>
    internal static FunctionArgumentsDialog Create(EditingSession session, FunctionInfo function, IReadOnlyList<string>? arguments = null) =>
        new(session, function, arguments ?? []);

    private void AddField(string text)
    {
        var index = _boxes.Count;
        var name = _function.ArgumentName(index) ?? $"[Argument{index + 1}]";
        var optional = name.StartsWith('[');
        var label = new TextBlock
        {
            Text = name.Trim('[', ']'),
            FontWeight = optional ? FontWeight.Normal : FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new TextBox { Text = text, MinWidth = 240 };
        AutomationProperties.SetName(box, label.Text);
        var preview = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
        preview.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        box.TextChanged += (_, _) => Changed(index);
        box.GotFocus += (_, _) => ShowArgument(index);

        _fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(label, index);
        Grid.SetRow(box, index);
        Grid.SetColumn(box, 1);
        Grid.SetRow(preview, index);
        Grid.SetColumn(preview, 2);
        _fields.Children.Add(label);
        _fields.Children.Add(box);
        _fields.Children.Add(preview);
        _boxes.Add(box);
        _previews.Add(preview);
    }

    private void Changed(int index)
    {
        // A repeating argument (Zahl1, Zahl2 …): filling the last field offers the next one.
        if (_function.RepeatsLast && index == _boxes.Count - 1 && !string.IsNullOrWhiteSpace(_boxes[index].Text) && _boxes.Count < Math.Min(_maximum, MaximumFields))
        {
            AddField(string.Empty);
        }

        Update();
    }

    private void ShowArgument(int index) =>
        _argument.Text = _function.ArgumentName(index) is { } name ? $"{name.Trim('[', ']')}{(name.StartsWith('[') ? " (optional)" : string.Empty)}" : string.Empty;

    private void Update()
    {
        for (var index = 0; index < _boxes.Count; index++)
        {
            var text = _boxes[index].Text ?? string.Empty;
            _previews[index].Text = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : _session.PreviewExpression(text) is { } value ? "= " + value : "= ?";
        }

        var call = "=" + FunctionCallText.Join(_function.Name, _boxes.Select(box => box.Text ?? string.Empty), _session.Locale);
        _result.Text = "Formelergebnis = " + (_session.PreviewFormula(call) ?? string.Empty);
    }

    protected override bool Apply()
    {
        CallText = FunctionCallText.Join(_function.Name, _boxes.Select(box => box.Text ?? string.Empty), _session.Locale);
        return true;
    }
}

using MatDesktop;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Dialogs;

/// <summary>
/// A quick rule of Bedingte Formatierung, like Excel's small dialogs: "Zellen formatieren, die GRÖSSER SIND ALS: [100] mit
/// [Hellrote Füllung mit dunkelrotem Text]" (two values for Zwischen, none for Doppelte Werte).
/// </summary>
public sealed class ConditionalRuleDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly HighlightRule _rule;
    private readonly TextBox _first = new() { MinWidth = 160 };
    private readonly TextBox _second = new() { MinWidth = 160 };
    private readonly ComboBox _look = new() { ItemsSource = GridPalette.HighlightPresets.Select(preset => preset.Label).ToList(), SelectedIndex = 0, MinWidth = 300 };

    private ConditionalRuleDialog(CalcEditor editor, HighlightRule rule, string title, int values)
        : base(title, 520)
    {
        _editor = editor;
        _rule = rule;
        AutomationProperties.SetName(_first, "Wert");
        AutomationProperties.SetName(_look, "Format");
        _first.Text = rule is HighlightRule.Top10 or HighlightRule.Bottom10 ? "10" : null;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = Prompt(rule), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (values >= 1)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _first } };
            if (values == 2)
            {
                row.Children.Add(new TextBlock { Text = "und", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                row.Children.Add(_second);
            }

            body.Children.Add(row);
        }

        body.Children.Add(Field("mit", _look));
        SetBody(body);
        Opened += (_, _) => _first.Focus();
    }

    public TextBox FirstValue => _first;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, HighlightRule rule, string title, int values) =>
        new ConditionalRuleDialog(editor, rule, title, values).ShowAsync(owner);

    private static string Prompt(HighlightRule rule) => rule switch
    {
        HighlightRule.GreaterThan => "Zellen formatieren, die GRÖSSER SIND ALS:",
        HighlightRule.LessThan => "Zellen formatieren, die KLEINER SIND ALS:",
        HighlightRule.Between => "Zellen formatieren, die ZWISCHEN folgenden Werten liegen:",
        HighlightRule.EqualTo => "Zellen formatieren, die GLEICH folgendem Wert sind:",
        HighlightRule.TextContains => "Zellen formatieren, die folgenden Text enthalten:",
        HighlightRule.Duplicates => "Zellen formatieren, die doppelte Werte enthalten.",
        HighlightRule.Top10 => "Die obersten Elemente formatieren (Anzahl):",
        HighlightRule.Bottom10 => "Die untersten Elemente formatieren (Anzahl):",
        HighlightRule.AboveAverage => "Zellen formatieren, die ÜBER DEM DURCHSCHNITT liegen.",
        _ => "Zellen formatieren, die UNTER DEM DURCHSCHNITT liegen.",
    };

    protected override bool Apply()
    {
        var preset = GridPalette.HighlightPresets[Math.Max(0, _look.SelectedIndex)];
        return Run(() => _editor.Session.AddHighlightRule(_rule, _first.Text, _second.Text, CalcColor.FromHex(preset.Fill), CalcColor.FromHex(preset.Font)));
    }
}

/// <summary>Bedingte Formatierung › Regeln verwalten: the sheet's rules (what, where); Regel löschen set apart.</summary>
public sealed class RulesManagerDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly ListBox _rules = new() { Height = 220 };

    private RulesManagerDialog(CalcEditor editor)
        : base("Manager für Regeln zur bedingten Formatierung", 600)
    {
        _editor = editor;
        AutomationProperties.SetName(_rules, "Regeln");
        var delete = new Button { Content = new IconLabel { Icon = "trash", Text = "Regel löschen" } }.Themed("MatButton.Danger");
        delete.Click += (_, _) =>
        {
            if (_rules.SelectedIndex >= 0)
            {
                Run(() => _editor.Session.RemoveConditionalFormat(_rules.SelectedIndex));
                Refresh();
            }
        };
        SetBody(new StackPanel { Spacing = 10, Children = { _rules, new ActionBar { Children = { delete } } } });
        OkButton.Content = new IconLabel { Icon = "check", Text = "Schließen" };
        CancelButton.IsVisible = false;
        Refresh();
    }

    public ListBox Rules => _rules;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new RulesManagerDialog(editor).ShowAsync(owner);

    private void Refresh()
    {
        var session = _editor.Session;
        var entries = session.ActiveSheet.ConditionalFormats
            .Select(format => $"{string.Join(", ", format.Rules.Select(session.DescribeRule))}    gilt für {CellRange.FormatList(format.Ranges)}")
            .ToList();
        _rules.ItemsSource = entries.Count > 0 ? entries : ["Dieses Blatt hat keine Regeln."];
        _rules.IsEnabled = entries.Count > 0;
    }

    protected override bool Apply() => true;
}

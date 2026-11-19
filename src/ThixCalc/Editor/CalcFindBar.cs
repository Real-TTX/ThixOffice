using Avalonia.Controls;
using ThixCalc.Core.Editing;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Editor;

/// <summary>
/// Suchen / Ersetzen of ThixCalc on the shared bar (<see cref="FindBarBase"/>), with Excel's options: Innerhalb Blatt or
/// Arbeitsmappe, Suchen in Formeln or Werten, Groß-/Kleinschreibung, Gesamten Zellinhalt vergleichen; * and ? are wildcards.
/// The current hit is the active cell (another sheet becomes active when the hit is there).
/// </summary>
internal sealed class CalcFindBar : FindBarBase
{
    private readonly CalcEditor _editor;

    public CalcFindBar(CalcEditor editor)
        : this(editor,
            new ComboBox { ItemsSource = new[] { "Blatt", "Arbeitsmappe" }, SelectedIndex = 0, MinWidth = 130 },
            new ComboBox { ItemsSource = new[] { "Formeln", "Werte" }, SelectedIndex = 0, MinWidth = 110 },
            new CheckBox { Content = "Groß-/Kleinschreibung" },
            new CheckBox { Content = "Gesamten Zellinhalt vergleichen" })
    {
    }

    private CalcFindBar(CalcEditor editor, ComboBox within, ComboBox lookIn, CheckBox matchCase, CheckBox wholeCell)
        : base("Suchen nach", Labelled("Innerhalb:", within), Labelled("Suchen in:", lookIn), matchCase, wholeCell)
    {
        _editor = editor;
        Within = within;
        LookIn = lookIn;
        MatchCase = matchCase;
        WholeCell = wholeCell;
        Avalonia.Automation.AutomationProperties.SetName(within, "Innerhalb");
        Avalonia.Automation.AutomationProperties.SetName(lookIn, "Suchen in");
        within.SelectionChanged += (_, _) => SearchChanged();
        lookIn.SelectionChanged += (_, _) => SearchChanged();
        matchCase.IsCheckedChanged += (_, _) => SearchChanged();
        wholeCell.IsCheckedChanged += (_, _) => SearchChanged();
    }

    public ComboBox Within { get; }

    public ComboBox LookIn { get; }

    public CheckBox MatchCase { get; }

    public CheckBox WholeCell { get; }

    private static StackPanel Labelled(string label, Control control) => new()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        Spacing = 6,
        Children = { new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, control },
    };

    private FindOptions Options() => new(
        MatchCase?.IsChecked == true,
        WholeCell?.IsChecked == true,
        InFormulas: LookIn?.SelectedIndex != 1,
        AllSheets: Within?.SelectedIndex == 1);

    public override bool FindNext(bool forward = true)
    {
        if (SearchText is not { } text)
        {
            return false;
        }

        var found = _editor.Session.FindNext(text, Options(), backwards: !forward) is not null;
        _editor.ScrollIntoView();
        UpdateCount();
        return found;
    }

    public override void ReplaceOne()
    {
        if (SearchText is not { } text)
        {
            return;
        }

        _ = _editor.RunAsync("Ersetzen", () => _editor.Session.Replace(text, ReplacementText, Options()));
        _editor.ScrollIntoView();
        UpdateCount();
    }

    protected override int ReplaceEverything()
    {
        if (SearchText is not { } text)
        {
            return -1;
        }

        var count = 0;
        _ = _editor.RunAsync("Alle ersetzen", () => count = _editor.Session.ReplaceAll(text, ReplacementText, Options()));
        return count;
    }

    protected override string? SelectedSearchText() => null;

    protected override void FocusDocument() => _editor.FocusDocument();

    protected override void SearchChanged()
    {
        if (!IsVisible || SearchText is null)
        {
            UpdateCount();
            return;
        }

        var hits = _editor.Session.FindAll(SearchText, Options());
        var session = _editor.Session;
        if (!hits.Any(hit => hit.Sheet == session.ActiveSheet && hit.Cell == session.ActiveCell) && hits.Count > 0)
        {
            session.FindNext(SearchText, Options());
            _editor.ScrollIntoView();
        }

        UpdateCount();
    }

    protected override void UpdateCount()
    {
        if (SearchText is not { } text || _editor is null)
        {
            SetCount("");
            return;
        }

        var session = _editor.Session;
        var hits = session.FindAll(text, Options()).ToList();
        SetCount(CountOf(hits.Count, hits.FindIndex(hit => hit.Sheet == session.ActiveSheet && hit.Cell == session.ActiveCell)));
    }
}

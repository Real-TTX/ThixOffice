using MatDesktop;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Globalization;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// Funktion einfügen (fx, Umschalt+F3), like Excel's: a search over names and descriptions, the categories (Zuletzt
/// verwendet first, Alle), the list of functions, and for the chosen one its syntax with the argument names and what it does.
/// OK (or a double click) puts "=NAME(" into the cell; the argument tip then names each argument while it is typed.
/// </summary>
public sealed class InsertFunctionDialog : OfficeDialog
{
    private const string Recent = "Zuletzt verwendet";
    private const string All = "Alle";
    private static readonly List<string> RecentNames = ["SUM", "AVERAGE", "IF", "VLOOKUP", "COUNTIF", "SUMIF", "MAX", "MIN"];

    private readonly CalcLocale _locale;
    private readonly TextBox _search = new() { Watermark = "Beschreiben Sie, was Sie tun möchten, oder geben Sie einen Namen ein" };
    private readonly ComboBox _category = new() { MinWidth = 260 };
    private readonly ListBox _functions = new() { Height = 220 };
    private readonly TextBlock _signature = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<FunctionInfo> _shown = [];

    private InsertFunctionDialog(CalcLocale locale)
        : base("Funktion einfügen", 560)
    {
        _locale = locale;
        AutomationProperties.SetName(_search, "Funktion suchen");
        AutomationProperties.SetName(_functions, "Funktion auswählen");
        AutomationProperties.SetName(_category, "Kategorie");
        _category.ItemsSource = new[] { Recent, All }.Concat(FunctionHelp.Categories.Select(entry => entry.Name)).ToList();
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => ShowCategory();
        _search.TextChanged += (_, _) => ShowSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down)
            {
                _functions.Focus();
                _functions.SelectedIndex = Math.Max(0, _functions.SelectedIndex);
                e.Handled = true;
            }
        };
        _functions.SelectionChanged += (_, _) => ShowHelp();
        _functions.DoubleTapped += (_, _) => Confirm();
        _description.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Field("Funktion suchen", _search),
                Field("Kategorie auswählen", _category),
                Field("Funktion auswählen", _functions),
                _signature,
                _description,
            },
        });
        ShowCategory();
        Opened += (_, _) => _search.Focus();
    }

    /// <summary>The chosen function (after OK).</summary>
    public FunctionInfo? Chosen { get; private set; }

    public TextBox SearchBox => _search;

    public ListBox Functions => _functions;

    public ComboBox Category => _category;

    public static async Task<FunctionInfo?> ShowAsync(Window owner, CalcLocale locale)
    {
        var dialog = new InsertFunctionDialog(locale);
        return await dialog.ShowAsync(owner) ? dialog.Chosen : null;
    }

    /// <summary>The dialog for the snapshots and tests (not shown).</summary>
    internal static InsertFunctionDialog Create(CalcLocale locale) => new(locale);

    private void ShowCategory()
    {
        var name = _category.SelectedItem as string ?? Recent;
        _shown = name switch
        {
            Recent => RecentNames.Select(canonical => FunctionHelp.Find(canonical, CalcLocale.Invariant)).OfType<FunctionInfo>().Select(info => FunctionHelp.Find(info.Canonical, CalcLocale.Invariant) is { } found ? Localized(found) : info).ToList(),
            All => FunctionHelp.All(_locale),
            _ => FunctionHelp.All(_locale).Where(info => FunctionHelp.CategoryName(info.Category) == name).ToList(),
        };
        ShowFunctions();
    }

    private FunctionInfo Localized(FunctionInfo info) => FunctionHelp.Find(_locale.FunctionName(info.Canonical), _locale) ?? info;

    private void ShowSearch()
    {
        if (string.IsNullOrWhiteSpace(_search.Text))
        {
            ShowCategory();
            return;
        }

        _shown = FunctionHelp.Search(_search.Text, _locale);
        ShowFunctions();
    }

    private void ShowFunctions()
    {
        _functions.ItemsSource = _shown.Select(info => info.Name).ToList();
        _functions.SelectedIndex = _shown.Count > 0 ? 0 : -1;
        ShowHelp();
    }

    private void ShowHelp()
    {
        if (_functions.SelectedIndex < 0 || _functions.SelectedIndex >= _shown.Count)
        {
            _signature.Text = _search.Text is { Length: > 0 } ? "Keine Funktion gefunden." : string.Empty;
            _description.Text = string.Empty;
            return;
        }

        var info = _shown[_functions.SelectedIndex];
        _signature.Text = info.Signature(_locale);
        _description.Text = info.Description;
    }

    protected override bool Apply()
    {
        if (_functions.SelectedIndex < 0 || _functions.SelectedIndex >= _shown.Count)
        {
            return false;
        }

        Chosen = _shown[_functions.SelectedIndex];
        RecentNames.Remove(Chosen.Canonical);
        RecentNames.Insert(0, Chosen.Canonical);
        return true;
    }
}

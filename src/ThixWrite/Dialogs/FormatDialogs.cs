using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Start › Schriftart … (Strg+D): font, style, size, underline and the effects of the selection in one step, with a preview.
/// A value the selection has mixed stays as it is unless it is changed here.
/// </summary>
public sealed class FontDialog : WriteDialog
{
    private static readonly string[] FontStyles = ["Standard", "Kursiv", "Fett", "Fett Kursiv"];
    private static readonly (string Label, UnderlineStyle Style)[] Underlines =
    [
        ("(ohne)", UnderlineStyle.None), ("Einfach", UnderlineStyle.Single), ("Doppelt", UnderlineStyle.Double), ("Dick", UnderlineStyle.Thick),
        ("Punktiert", UnderlineStyle.Dotted), ("Gestrichelt", UnderlineStyle.Dash), ("Wellenlinie", UnderlineStyle.Wave), ("Nur Wörter", UnderlineStyle.Words),
    ];

    private readonly WriteEditor _editor;
    private readonly TextBlock _preview = new() { Text = "AaBbYyZz – Größe", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    public FontDialog(WriteEditor editor)
        : base("Schriftart", 520)
    {
        _editor = editor;
        var format = editor.Session.GetFormatting();
        Family = new ComboBox { ItemsSource = WriteFonts.Families, Width = 230, SelectedItem = format.FontFamily.IsMixed ? null : format.FontFamily.Value };
        StyleChoice = new ComboBox { ItemsSource = FontStyles, Width = 150 };
        if (!format.Bold.IsMixed && !format.Italic.IsMixed)
        {
            StyleChoice.SelectedIndex = (format.Bold.Value ? 2 : 0) + (format.Italic.Value ? 1 : 0);
        }

        SizeField = new NumericUpDown { Minimum = 1, Maximum = 1638, Increment = 0.5m, FormatString = "0.#", Width = 110, Value = format.FontSize.IsMixed ? null : (decimal)format.FontSize.Value };
        Underline = new ComboBox { ItemsSource = Underlines.Select(item => item.Label).ToList(), Width = 150 };
        if (!format.Underline.IsMixed)
        {
            Underline.SelectedIndex = Math.Max(0, Array.FindIndex(Underlines, item => item.Style == format.Underline.Value));
        }

        Strikethrough = Check("Durchgestrichen", format.Strikethrough);
        Superscript = Check("Hochgestellt", format.Superscript);
        Subscript = Check("Tiefgestellt", format.Subscript);
        Superscript.IsCheckedChanged += (_, _) => Subscript.IsChecked = Superscript.IsChecked == true ? false : Subscript.IsChecked;
        Subscript.IsCheckedChanged += (_, _) => Superscript.IsChecked = Subscript.IsChecked == true ? false : Superscript.IsChecked;
        AutomationProperties.SetName(Family, "Schriftart");
        AutomationProperties.SetName(StyleChoice, "Schriftschnitt");
        AutomationProperties.SetName(SizeField, "Schriftgrad");
        AutomationProperties.SetName(Underline, "Unterstreichung");

        var preview = new Border { Height = 64, BorderThickness = new Thickness(1), Child = _preview }.Use(Border.BorderBrushProperty, "Mat.Border");
        Family.SelectionChanged += (_, _) => Preview();
        StyleChoice.SelectionChanged += (_, _) => Preview();
        SizeField.ValueChanged += (_, _) => Preview();
        Preview();

        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Field("Schriftart", Family), Field("Schriftschnitt", StyleChoice), Field("Schriftgrad", SizeField) } },
                Field("Unterstreichung", Underline),
                Field("Effekte", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Strikethrough, Superscript, Subscript } }),
                Field("Vorschau", preview),
            },
        });
    }

    public ComboBox Family { get; }

    public ComboBox StyleChoice { get; }

    public NumericUpDown SizeField { get; }

    public ComboBox Underline { get; }

    public CheckBox Strikethrough { get; }

    public CheckBox Superscript { get; }

    public CheckBox Subscript { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new FontDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var family = Family.SelectedItem as string;
        var style = StyleChoice.SelectedIndex;
        var size = SizeField.Value is { } value ? (double)value : (double?)null;
        var underline = Underline.SelectedIndex >= 0 ? Underlines[Underline.SelectedIndex].Style : (UnderlineStyle?)null;
        var strike = Strikethrough.IsChecked;
        var position = Superscript.IsChecked == true ? VerticalPosition.Superscript : Subscript.IsChecked == true ? VerticalPosition.Subscript
            : Superscript.IsChecked == false && Subscript.IsChecked == false ? VerticalPosition.Baseline : (VerticalPosition?)null;
        _editor.Session.FormatCharacters("Schriftart", (_, props) =>
        {
            var result = family is null ? props : props.WithFont(family);
            if (style >= 0)
            {
                result = result with { Bold = style >= 2, Italic = style % 2 == 1 };
            }

            if (size is { } points)
            {
                result = result with { FontSize = Math.Round(points * 2) / 2 };
            }

            if (underline is { } line)
            {
                result = result with { Underline = line == UnderlineStyle.None ? null : line };
            }

            if (strike is { } struck)
            {
                result = result with { Strikethrough = struck ? true : null };
            }

            if (position is { } vertical)
            {
                result = result with { VerticalPosition = vertical == VerticalPosition.Baseline ? null : vertical };
            }

            return result;
        });
        return true;
    }

    private static CheckBox Check(string label, ThixWrite.Core.Editing.FormatState<bool> state) =>
        new() { Content = label, IsThreeState = state.IsMixed, IsChecked = state.IsMixed ? null : state.Value };

    private void Preview()
    {
        if (Family.SelectedItem is string family)
        {
            _preview.FontFamily = new FontFamily(family);
        }

        _preview.FontSize = Math.Clamp((double)(SizeField.Value ?? 11) * 4 / 3, 6, 40);
        _preview.FontWeight = StyleChoice.SelectedIndex >= 2 ? FontWeight.Bold : FontWeight.Normal;
        _preview.FontStyle = StyleChoice.SelectedIndex % 2 == 1 ? FontStyle.Italic : FontStyle.Normal;
    }
}

/// <summary>
/// Start › Absatz … : Einzüge und Abstände, Zeilen- und Seitenumbruch, Tabstopps – for the selected paragraphs in one step.
/// The values shown are the first paragraph's.
/// </summary>
public sealed class ParagraphDialog : WriteDialog
{
    public const int IndentsTab = 0;
    public const int BreaksTab = 1;
    public const int TabsTab = 2;

    private const double PointsPerCm = 72 / 2.54;
    private static readonly NumberFormatInfo German = CultureInfo.GetCultureInfo("de-DE").NumberFormat;
    private static readonly string[] Alignments = ["Links", "Zentriert", "Rechts", "Blocksatz"];
    private static readonly string[] Specials = ["(ohne)", "Erste Zeile", "Hängend"];
    private static readonly string[] Spacings = ["Einfach", "1,5 Zeilen", "Doppelt", "Mindestens", "Genau", "Mehrfach"];
    private static readonly (string Label, TabAlignment Alignment)[] TabKinds =
        [("Links", TabAlignment.Left), ("Zentriert", TabAlignment.Center), ("Rechts", TabAlignment.Right), ("Dezimal", TabAlignment.Decimal)];
    private static readonly (string Label, TabLeader Leader)[] Leaders =
        [("Ohne", TabLeader.None), ("Punkte ……", TabLeader.Dot), ("Striche ----", TabLeader.Hyphen), ("Linie ____", TabLeader.Underscore)];

    private readonly WriteEditor _editor;
    private readonly List<TabStop> _tabs;

    public ParagraphDialog(WriteEditor editor, int tab = IndentsTab)
        : base("Absatz", 560)
    {
        _editor = editor;
        var session = editor.Session;
        var paragraph = session.Document.ParagraphAt(session.Selection.Start.Block) ?? Paragraph.Empty();
        var format = session.Resolver.ResolveParagraph(paragraph);
        _tabs = [.. format.Tabs];

        Alignment = new ComboBox { ItemsSource = Alignments, SelectedIndex = (int)format.Alignment, Width = 180 };
        IndentLeft = Centimetres(format.IndentLeft);
        IndentRight = Centimetres(format.IndentRight);
        Special = new ComboBox { ItemsSource = Specials, Width = 160, SelectedIndex = format.FirstLineIndent > 0 ? 1 : format.FirstLineIndent < 0 ? 2 : 0 };
        SpecialBy = Centimetres(Math.Abs(format.FirstLineIndent));
        Before = Points(format.SpacingBefore);
        After = Points(format.SpacingAfter);
        var (spacingIndex, spacingValue) = format.LineSpacing switch
        {
            { Rule: LineSpacingRule.Multiple, Value: 1 } => (0, 1.0),
            { Rule: LineSpacingRule.Multiple, Value: 1.5 } => (1, 1.5),
            { Rule: LineSpacingRule.Multiple, Value: 2 } => (2, 2.0),
            { Rule: LineSpacingRule.AtLeast } spacing => (3, spacing.Value),
            { Rule: LineSpacingRule.Exact } spacing => (4, spacing.Value),
            var spacing => (5, spacing.Value),
        };
        LineSpacing = new ComboBox { ItemsSource = Spacings, SelectedIndex = spacingIndex, Width = 160 };
        LineSpacingValue = new NumericUpDown { Value = (decimal)Math.Round(spacingValue, 2), Minimum = 0.25m, Maximum = 1584, Increment = 0.25m, FormatString = "0.##", NumberFormat = German, Width = 110 };

        WidowControl = new CheckBox { Content = "Absatzkontrolle", IsChecked = format.WidowControl };
        KeepWithNext = new CheckBox { Content = "Nicht vom nächsten Absatz trennen", IsChecked = format.KeepWithNext };
        KeepTogether = new CheckBox { Content = "Diesen Absatz zusammenhalten", IsChecked = format.KeepLinesTogether };
        PageBreakBefore = new CheckBox { Content = "Seitenumbruch oberhalb", IsChecked = format.PageBreakBefore };

        TabList = new ListBox { Height = 120, Width = 200 };
        TabPosition = Centimetres(0, maximum: 55);
        TabKind = new ComboBox { ItemsSource = TabKinds.Select(item => item.Label).ToList(), SelectedIndex = 0, Width = 140 };
        TabLeaderChoice = new ComboBox { ItemsSource = Leaders.Select(item => item.Label).ToList(), SelectedIndex = 0, Width = 140 };
        var set = new Button { Content = new IconLabel { Icon = "plus", Text = "Festlegen" } };
        var remove = new Button { Content = new IconLabel { Icon = "minus", Text = "Löschen" } };
        var clear = new Button { Content = new IconLabel { Icon = "trash", Text = "Alle löschen" } };
        ActionBar.SetSeparated(clear, true);
        set.Click += (_, _) => SetTab();
        remove.Click += (_, _) => RemoveTab();
        clear.Click += (_, _) =>
        {
            _tabs.Clear();
            ShowTabs();
        };
        TabList.SelectionChanged += (_, _) =>
        {
            if (TabList.SelectedIndex >= 0 && TabList.SelectedIndex < _tabs.Count)
            {
                var stop = _tabs[TabList.SelectedIndex];
                TabPosition.Value = (decimal)Math.Round(stop.Position / PointsPerCm, 2);
                TabKind.SelectedIndex = Math.Max(0, Array.FindIndex(TabKinds, item => item.Alignment == stop.Alignment));
                TabLeaderChoice.SelectedIndex = Math.Max(0, Array.FindIndex(Leaders, item => item.Leader == stop.Leader));
            }
        };
        ShowTabs();

        var indents = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                Field("Ausrichtung", Alignment),
                Row(Field("Links (cm)", IndentLeft), Field("Rechts (cm)", IndentRight)),
                Row(Field("Sondereinzug", Special), Field("Um (cm)", SpecialBy)),
                Row(Field("Abstand vor (Pt.)", Before), Field("Abstand nach (Pt.)", After)),
                Row(Field("Zeilenabstand", LineSpacing), Field("Maß", LineSpacingValue)),
            },
        };
        var breaks = new StackPanel { Spacing = 10, Margin = new Thickness(0, 14, 0, 0), Children = { WidowControl, KeepWithNext, KeepTogether, PageBreakBefore } };
        var tabs = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                Row(Field("Tabstopps", TabList), new StackPanel { Spacing = 12, Children = { Field("Position (cm)", TabPosition), Field("Ausrichtung", TabKind), Field("Füllzeichen", TabLeaderChoice) } }),
                new ActionBar { Children = { set, remove, clear } },
            },
        };
        Tabs = new TabControl
        {
            SelectedIndex = tab,
            Items =
            {
                new TabItem { Header = "Einzüge und Abstände", Content = indents },
                new TabItem { Header = "Zeilen- und Seitenumbruch", Content = breaks },
                new TabItem { Header = "Tabstopps", Content = tabs },
            },
        };
        SetBody(Tabs);
    }

    public TabControl Tabs { get; }

    public ComboBox Alignment { get; }

    public NumericUpDown IndentLeft { get; }

    public NumericUpDown IndentRight { get; }

    public ComboBox Special { get; }

    public NumericUpDown SpecialBy { get; }

    public NumericUpDown Before { get; }

    public NumericUpDown After { get; }

    public ComboBox LineSpacing { get; }

    public NumericUpDown LineSpacingValue { get; }

    public CheckBox WidowControl { get; }

    public CheckBox KeepWithNext { get; }

    public CheckBox KeepTogether { get; }

    public CheckBox PageBreakBefore { get; }

    public ListBox TabList { get; }

    public NumericUpDown TabPosition { get; }

    public ComboBox TabKind { get; }

    public ComboBox TabLeaderChoice { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor, int tab = IndentsTab) => new ParagraphDialog(editor, tab).ShowAsync(owner);

    /// <summary>"Festlegen": a tab stop at the position (one already there is replaced).</summary>
    public void SetTab()
    {
        var position = (double)(TabPosition.Value ?? 0) * PointsPerCm;
        _tabs.RemoveAll(stop => Math.Abs(stop.Position - position) < 0.5);
        _tabs.Add(new TabStop(Math.Round(position, 2), TabKinds[Math.Max(0, TabKind.SelectedIndex)].Alignment, Leaders[Math.Max(0, TabLeaderChoice.SelectedIndex)].Leader));
        _tabs.Sort((left, right) => left.Position.CompareTo(right.Position));
        ShowTabs();
    }

    private void RemoveTab()
    {
        if (TabList.SelectedIndex >= 0 && TabList.SelectedIndex < _tabs.Count)
        {
            _tabs.RemoveAt(TabList.SelectedIndex);
            ShowTabs();
        }
    }

    private void ShowTabs() =>
        TabList.ItemsSource = _tabs.Select(stop => $"{(stop.Position / PointsPerCm).ToString("0.00", German)} cm – {TabKinds.FirstOrDefault(item => item.Alignment == stop.Alignment).Label}").ToList();

    protected override bool Apply()
    {
        var special = (double)(SpecialBy.Value ?? 0) * PointsPerCm;
        var firstLine = Special.SelectedIndex switch { 1 => special, 2 => -special, _ => 0 };
        var spacingValue = (double)(LineSpacingValue.Value ?? 1);
        LineSpacing spacing = LineSpacing.SelectedIndex switch
        {
            1 => new LineSpacing(LineSpacingRule.Multiple, 1.5),
            2 => new LineSpacing(LineSpacingRule.Multiple, 2),
            3 => new LineSpacing(LineSpacingRule.AtLeast, spacingValue),
            4 => new LineSpacing(LineSpacingRule.Exact, spacingValue),
            5 => new LineSpacing(LineSpacingRule.Multiple, spacingValue),
            _ => Core.Model.LineSpacing.Single,
        };
        var tabs = new EquatableArray<TabStop>(_tabs);
        _editor.Session.FormatParagraphs("Absatz", props => props with
        {
            Alignment = (ParagraphAlignment)Math.Max(0, Alignment.SelectedIndex),
            IndentLeft = (double)(IndentLeft.Value ?? 0) * PointsPerCm,
            IndentRight = (double)(IndentRight.Value ?? 0) * PointsPerCm,
            FirstLineIndent = firstLine,
            SpacingBefore = (double)(Before.Value ?? 0),
            SpacingAfter = (double)(After.Value ?? 0),
            LineSpacing = spacing,
            WidowControl = WidowControl.IsChecked == true,
            KeepWithNext = KeepWithNext.IsChecked == true,
            KeepLinesTogether = KeepTogether.IsChecked == true,
            PageBreakBefore = PageBreakBefore.IsChecked == true,
            Tabs = tabs,
        });
        return true;
    }

    private static NumericUpDown Centimetres(double points, double maximum = 30) => new()
    {
        Value = (decimal)Math.Round(points / PointsPerCm, 2),
        Minimum = 0,
        Maximum = (decimal)maximum,
        Increment = 0.1m,
        FormatString = "0.00",
        NumberFormat = German,
        Width = 140,
    };

    private static NumericUpDown Points(double points) => new()
    {
        Value = (decimal)Math.Round(points, 1),
        Minimum = 0,
        Maximum = 1584,
        Increment = 6,
        FormatString = "0.#",
        NumberFormat = German,
        Width = 140,
    };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        row.Children.AddRange(children);
        return row;
    }
}

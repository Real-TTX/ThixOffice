using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// "Formatvorlage ändern …" and "Neue Formatvorlage …" (Word's dialog in short): name, type, based on, the style of the next paragraph,
/// and the formatting – font, size, bold / italic / underline, colour, alignment, line spacing, spacing before / after, left indent – with
/// a preview. The fields show what the style gives (its chain resolved); only what is changed here goes into the style itself, so the
/// rest keeps following the style it is based on. One undoable step; every paragraph with the style follows.
/// </summary>
public sealed class StyleDialog : WriteDialog
{
    private const double PointsPerCm = 72 / 2.54;
    private const string NoStyle = "(keine Formatvorlage)";
    private static readonly NumberFormatInfo German = CultureInfo.GetCultureInfo("de-DE").NumberFormat;
    private static readonly string[] Alignments = ["Links", "Zentriert", "Rechts", "Blocksatz"];
    private static readonly (string Label, LineSpacing Spacing)[] Spacings =
    [
        ("Einfach", Core.Model.LineSpacing.Single), ("1,08", new LineSpacing(LineSpacingRule.Multiple, 259 / 240.0)), ("1,15", new LineSpacing(LineSpacingRule.Multiple, 1.15)),
        ("1,5 Zeilen", new LineSpacing(LineSpacingRule.Multiple, 1.5)), ("Doppelt", new LineSpacing(LineSpacingRule.Multiple, 2)),
    ];

    private readonly WriteEditor _editor;
    private readonly Style? _style;
    private readonly List<Style> _choices;
    private readonly EffectiveParagraphFormat _paragraph;
    private readonly EffectiveCharacterFormat _character;
    private readonly TextBlock _preview = new() { Text = "Beispieltext Beispieltext Beispieltext", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };

    private StyleDialog(WriteEditor editor, Style? style)
        : base(style is null ? "Neue Formatvorlage" : "Formatvorlage ändern", 600)
    {
        _editor = editor;
        _style = style;
        var session = editor.Session;
        _choices = [.. session.PaneStyles().Where(choice => choice.Type == StyleType.Paragraph && choice.Id != style?.Id)];
        var styleId = style?.Id ?? session.CurrentParagraphStyle?.Id;
        var probe = new Paragraph([], new ParagraphProperties { StyleId = style?.Type == StyleType.Character ? null : styleId });
        _paragraph = session.Resolver.ResolveParagraph(probe); // what the style gives (a paragraph in it without own formats)
        _character = session.Resolver.ResolveCharacter(_paragraph.StyleKey, style?.Type == StyleType.Character ? new CharacterProperties { StyleId = style.Id } : CharacterProperties.Empty);

        NameField = new TextBox { Text = style is null ? "" : BuiltInStyles.DisplayName(style), Width = 260, IsReadOnly = style is not null && !style.IsCustom };
        TypeChoice = new ComboBox { ItemsSource = new[] { "Absatz", "Zeichen" }, SelectedIndex = style?.Type == StyleType.Character ? 1 : 0, Width = 160, IsEnabled = style is null };
        BasedOn = new ComboBox { ItemsSource = new[] { NoStyle }.Concat(_choices.Select(BuiltInStyles.DisplayName)).ToList(), Width = 260 };
        var basedOnId = style is null ? styleId : style.BasedOn;
        BasedOn.SelectedIndex = basedOnId is null ? 0 : _choices.FindIndex(choice => choice.Id == basedOnId) + 1;
        NextStyle = new ComboBox { ItemsSource = new[] { "(diese Formatvorlage)" }.Concat(_choices.Select(BuiltInStyles.DisplayName)).ToList(), Width = 260 };
        NextStyle.SelectedIndex = style?.Next is { } next && next != style.Id ? _choices.FindIndex(choice => choice.Id == next) + 1 : 0;

        Family = new ComboBox { ItemsSource = WriteFonts.Families, SelectedItem = _character.FontFamily, Width = 200 };
        SizeField = new NumericUpDown { Minimum = 1, Maximum = 1638, Increment = 0.5m, FormatString = "0.#", NumberFormat = German, Width = 100, Value = (decimal)_character.FontSize };
        Bold = new ToggleButton { Content = new MatIcon { Icon = "bold", Size = 16 }, IsChecked = _character.Bold };
        Italic = new ToggleButton { Content = new MatIcon { Icon = "italic", Size = 16 }, IsChecked = _character.Italic };
        Underline = new ToggleButton { Content = new MatIcon { Icon = "underline", Size = 16 }, IsChecked = _character.Underline != UnderlineStyle.None };
        AutomationProperties.SetName(Bold, "Fett");
        AutomationProperties.SetName(Italic, "Kursiv");
        AutomationProperties.SetName(Underline, "Unterstrichen");
        ToolTip.SetTip(Bold, "Fett");
        ToolTip.SetTip(Italic, "Kursiv");
        ToolTip.SetTip(Underline, "Unterstrichen");
        var colors = new[] { "Automatisch" }.Concat(DocumentPalette.FontColors(session.Document.Theme)).Distinct().ToList();
        FontColor = new ComboBox { ItemsSource = colors, Width = 160, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((hex, _) => ColorItem(hex)) };
        var currentColor = _character.IsAutoColor ? "Automatisch" : "#" + _character.Color.ToUpperInvariant();
        if (!colors.Contains(currentColor))
        {
            colors.Add(currentColor);
            FontColor.ItemsSource = colors;
        }

        FontColor.SelectedItem = currentColor;

        Alignment = new ComboBox { ItemsSource = Alignments, SelectedIndex = (int)_paragraph.Alignment, Width = 160 };
        var spacingIndex = Array.FindIndex(Spacings, item => item.Spacing.Rule == _paragraph.LineSpacing.Rule && Math.Abs(item.Spacing.Value - _paragraph.LineSpacing.Value) < 0.005);
        LineSpacing = new ComboBox { ItemsSource = Spacings.Select(item => item.Label).ToList(), SelectedIndex = spacingIndex, Width = 160 };
        Before = Number(_paragraph.SpacingBefore, 6);
        After = Number(_paragraph.SpacingAfter, 6);
        IndentLeft = Number(Math.Round(_paragraph.IndentLeft / PointsPerCm, 2), 0.25);
        QuickFormat = new CheckBox { Content = "Zum Formatvorlagenkatalog hinzufügen", IsChecked = style?.QuickFormat ?? true };

        AutomationProperties.SetName(NameField, "Name");
        foreach (var control in new Control[] { Family, SizeField, Bold, Italic, Underline, FontColor, Alignment, LineSpacing, Before, After, IndentLeft, TypeChoice })
        {
            switch (control)
            {
                case ComboBox combo:
                    combo.SelectionChanged += (_, _) => Preview();
                    break;
                case NumericUpDown number:
                    number.ValueChanged += (_, _) => Preview();
                    break;
                case ToggleButton toggle:
                    toggle.IsCheckedChanged += (_, _) => Preview();
                    break;
            }
        }

        var preview = new Border { Height = 90, BorderThickness = new Thickness(1), Child = _preview }.Use(Border.BorderBrushProperty, "Mat.Border");
        Preview();
        SetBody(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Row(_nameField = Field("Name", NameField), Field("Formatvorlagentyp", TypeChoice)),
                Row(Field("Formatvorlage basiert auf", BasedOn), Field("Formatvorlage für Folgeabsatz", NextStyle)),
                Row(Field("Schriftart", Family), Field("Schriftgrad", SizeField),
                    Field("Auszeichnung", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Bold, Italic, Underline } }), Field("Farbe", FontColor)),
                Row(Field("Ausrichtung", Alignment), Field("Zeilenabstand", LineSpacing)),
                Row(Field("Abstand vor (Pt.)", Before), Field("Abstand nach (Pt.)", After), Field("Einzug links (cm)", IndentLeft)),
                Field("Vorschau", preview),
                QuickFormat,
            },
        });
    }

    private readonly MatFormField? _nameField;

    public TextBox NameField { get; }

    public ComboBox TypeChoice { get; }

    public ComboBox BasedOn { get; }

    public ComboBox NextStyle { get; }

    public ComboBox Family { get; }

    public NumericUpDown SizeField { get; }

    public ToggleButton Bold { get; }

    public ToggleButton Italic { get; }

    public ToggleButton Underline { get; }

    public ComboBox FontColor { get; }

    public ComboBox Alignment { get; }

    public ComboBox LineSpacing { get; }

    public NumericUpDown Before { get; }

    public NumericUpDown After { get; }

    public NumericUpDown IndentLeft { get; }

    public CheckBox QuickFormat { get; }

    /// <summary>The error shown at the name (tests).</summary>
    public string? NameError => _nameField?.Error;

    /// <summary>"Formatvorlage ändern …" for <paramref name="style"/>.</summary>
    public static Task<bool> ModifyAsync(Window owner, WriteEditor editor, Style style) => new StyleDialog(editor, style).ShowAsync(owner);

    /// <summary>"Neue Formatvorlage …" (based on the style of the caret's paragraph).</summary>
    public static Task<bool> CreateAsync(Window owner, WriteEditor editor) => new StyleDialog(editor, null).ShowAsync(owner);

    protected override bool Apply()
    {
        var session = _editor.Session;
        var name = NameField.Text?.Trim() ?? "";
        if (_style is null && session.StyleNameProblem(name) is { } problem)
        {
            _nameField!.Error = problem;
            return false;
        }

        var basedOn = BasedOn.SelectedIndex > 0 ? _choices[BasedOn.SelectedIndex - 1].Id : null;
        var next = NextStyle.SelectedIndex > 0 ? _choices[NextStyle.SelectedIndex - 1].Id : null;
        var character = ChangedCharacter(_style?.Character ?? CharacterProperties.Empty);
        var paragraph = ChangedParagraph(_style?.Paragraph ?? ParagraphProperties.Empty);
        if (_style is null)
        {
            var type = TypeChoice.SelectedIndex == 1 ? StyleType.Character : StyleType.Paragraph;
            session.CreateStyle(name, type, type == StyleType.Paragraph ? basedOn : BuiltInStyles.DefaultParagraphFont, next, paragraph, character, QuickFormat.IsChecked == true);
            return true;
        }

        session.ModifyStyle(_style with
        {
            Name = _style.IsCustom && name.Length > 0 ? name : _style.Name,
            BasedOn = _style.Type == StyleType.Paragraph ? basedOn : _style.BasedOn,
            Next = _style.Type == StyleType.Paragraph ? next ?? _style.Id : _style.Next,
            Character = character,
            Paragraph = _style.Type == StyleType.Paragraph ? paragraph : _style.Paragraph,
            QuickFormat = QuickFormat.IsChecked == true,
        });
        return true;
    }

    /// <summary>The style's character properties with what was changed here.</summary>
    private CharacterProperties ChangedCharacter(CharacterProperties properties)
    {
        var result = properties;
        if (Family.SelectedItem is string family && family != _character.FontFamily)
        {
            result = result.WithFont(family);
        }

        if (SizeField.Value is { } size && Math.Abs((double)size - _character.FontSize) > 0.01)
        {
            result = result with { FontSize = Math.Round((double)size * 2) / 2 };
        }

        if (Bold.IsChecked != _character.Bold)
        {
            result = result with { Bold = Bold.IsChecked == true };
        }

        if (Italic.IsChecked != _character.Italic)
        {
            result = result with { Italic = Italic.IsChecked == true };
        }

        if (Underline.IsChecked != (_character.Underline != UnderlineStyle.None))
        {
            result = result with { Underline = Underline.IsChecked == true ? UnderlineStyle.Single : UnderlineStyle.None };
        }

        var shownColor = _character.IsAutoColor ? "Automatisch" : "#" + _character.Color.ToUpperInvariant();
        if (FontColor.SelectedItem is string color && color != shownColor)
        {
            result = result with { Color = color == "Automatisch" ? TextColor.Auto : TextColor.FromHex(color) };
        }

        return result;
    }

    /// <summary>The style's paragraph properties with what was changed here.</summary>
    private ParagraphProperties ChangedParagraph(ParagraphProperties properties)
    {
        var result = properties;
        if (Alignment.SelectedIndex >= 0 && (ParagraphAlignment)Alignment.SelectedIndex != _paragraph.Alignment)
        {
            result = result with { Alignment = (ParagraphAlignment)Alignment.SelectedIndex };
        }

        if (LineSpacing.SelectedIndex >= 0 && Spacings[LineSpacing.SelectedIndex].Spacing != _paragraph.LineSpacing)
        {
            result = result with { LineSpacing = Spacings[LineSpacing.SelectedIndex].Spacing };
        }

        if (Before.Value is { } before && Math.Abs((double)before - _paragraph.SpacingBefore) > 0.01)
        {
            result = result with { SpacingBefore = (double)before };
        }

        if (After.Value is { } after && Math.Abs((double)after - _paragraph.SpacingAfter) > 0.01)
        {
            result = result with { SpacingAfter = (double)after };
        }

        if (IndentLeft.Value is { } indent && Math.Abs((double)indent * PointsPerCm - _paragraph.IndentLeft) > 0.1)
        {
            result = result with { IndentLeft = Math.Round((double)indent * PointsPerCm, 2) };
        }

        return result;
    }

    private void Preview()
    {
        if (Family.SelectedItem is string family)
        {
            _preview.FontFamily = new FontFamily(family);
        }

        _preview.FontSize = Math.Clamp((double)(SizeField.Value ?? 11) * 4 / 3, 6, 36);
        _preview.FontWeight = Bold.IsChecked == true ? FontWeight.Bold : FontWeight.Normal;
        _preview.FontStyle = Italic.IsChecked == true ? FontStyle.Italic : FontStyle.Normal;
        _preview.TextDecorations = Underline.IsChecked == true ? TextDecorations.Underline : null;
        _preview.TextAlignment = Alignment.SelectedIndex switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, 3 => TextAlignment.Justify, _ => TextAlignment.Left };
    }

    private static Control ColorItem(string? value)
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        if (value is null || !value.StartsWith('#'))
        {
            return text;
        }

        var swatch = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(1), Background = DocumentPalette.Swatch(value) }.Use(Border.BorderBrushProperty, "Mat.Border");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { swatch, text } };
    }

    private static NumericUpDown Number(double value, double increment) => new()
    {
        Value = (decimal)Math.Round(value, 2),
        Minimum = 0,
        Maximum = 1584,
        Increment = (decimal)increment,
        FormatString = "0.##",
        NumberFormat = German,
        Width = 120,
    };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        row.Children.AddRange(children);
        return row;
    }
}

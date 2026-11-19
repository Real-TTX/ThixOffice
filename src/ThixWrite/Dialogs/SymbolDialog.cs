using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using MatDesktop;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Einfügen › Symbol › Weitere Symbole …: a grid of the characters of a chosen range (Latin-1, punctuation, currency, arrows,
/// mathematics, Greek, shapes) in the font of the caret, and the character code (hex) – double click or OK inserts.
/// </summary>
public sealed class SymbolDialog : WriteDialog
{
    /// <summary>The ranges of the dialog: name, first and last code point.</summary>
    public static IReadOnlyList<(string Name, int First, int Last)> Ranges { get; } =
    [
        ("Lateinisch-1, Ergänzung", 0x00A1, 0x00FF),
        ("Lateinisch, erweitert-A", 0x0100, 0x017F),
        ("Allgemeine Interpunktion", 0x2010, 0x205E),
        ("Währungssymbole", 0x20A0, 0x20C0),
        ("Buchstabenähnliche Symbole", 0x2100, 0x214F),
        ("Pfeile", 0x2190, 0x21FF),
        ("Mathematische Operatoren", 0x2200, 0x22FF),
        ("Griechisch", 0x0391, 0x03C9),
        ("Geometrische Formen", 0x25A0, 0x25FF),
        ("Verschiedene Symbole", 0x2600, 0x26FF),
        ("Dingbats", 0x2700, 0x27BF),
    ];

    private readonly WriteEditor _editor;
    private readonly UniformGrid _grid = new() { Columns = 16 };

    public SymbolDialog(WriteEditor editor)
        : base("Symbol", 640)
    {
        _editor = editor;
        Range = new ComboBox { ItemsSource = Ranges.Select(range => range.Name).ToList(), SelectedIndex = 0, Width = 260 };
        Code = new TextBox { Width = 110, Text = "00A9" };
        AutomationProperties.SetName(Range, "Bereich");
        AutomationProperties.SetName(Code, "Zeichencode");
        Range.SelectionChanged += (_, _) => Fill();
        Fill();
        var scroller = new ScrollViewer { Height = 260, Content = _grid };
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Bereich", Range), Field("Zeichencode (hex)", Code) } },
                scroller,
            },
        });
    }

    public ComboBox Range { get; }

    public TextBox Code { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new SymbolDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (!int.TryParse((Code.Text ?? "").Trim().TrimStart('U', 'u', '+'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
            || code is < 0x20 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
        {
            return false;
        }

        _editor.Session.InsertText(char.ConvertFromUtf32(code));
        return true;
    }

    private void Fill()
    {
        _grid.Children.Clear();
        var (_, first, last) = Ranges[Math.Max(0, Range.SelectedIndex)];
        for (var code = first; code <= last; code++)
        {
            var text = char.ConvertFromUtf32(code);
            if (Rune.GetUnicodeCategory(new Rune(code)) is UnicodeCategory.OtherNotAssigned or UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            var button = new Button { Content = text, Width = 34, Height = 34, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 16 }.Themed("MatButton.Ghost");
            var hex = code.ToString("X4", CultureInfo.InvariantCulture);
            ToolTip.SetTip(button, $"U+{hex}");
            AutomationProperties.SetName(button, $"U+{hex}");
            button.Click += (_, _) => Code.Text = hex;
            button.DoubleTapped += (_, _) =>
            {
                Code.Text = hex;
                Confirm();
            };
            _grid.Children.Add(button);
        }
    }
}

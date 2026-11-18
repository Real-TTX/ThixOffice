using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;

namespace ThixWrite.Editor;

/// <summary>
/// PHASE 0 PLACEHOLDER of ThixWrite's editor ("Editor folgt"): a page on the desk with a note and a plain text box bound
/// to the <see cref="TextDocument"/>, so that the shell flow is real (typing makes the document unsaved, undo, save, open).
/// The native editor (layout, caret, formatting) replaces it in phase 1a – do not grow this into one.
/// </summary>
public sealed class PlaceholderEditor : OfficeEditor
{
    public const string WordsItem = "words";
    public const string CharactersItem = "characters";

    /// <summary>The text of the snapshots' sample document.</summary>
    public const string SampleText = """
        Projektbericht November

        Der neue Webshop ist seit dem 3. November online. In den ersten zwei Wochen kamen 412 Bestellungen, ein Viertel davon über das Smartphone.

        Nächste Schritte
        – Zahlungsarten erweitern (Rechnung, PayPal)
        – Versandkosten für Österreich und die Schweiz klären
        – Newsletter zum Weihnachtsgeschäft vorbereiten

        Anna Berger
        """;

    private readonly TextDocument _document;
    private readonly TextBox _textBox;
    private readonly Border _view;
    private readonly LayoutTransformControl _zoomed;
    private bool _isUpdating;

    public PlaceholderEditor(TextDocument document)
    {
        _document = document;
        _textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 420,
            Padding = new Thickness(14, 12),
            Text = document.Text,
            FontSize = 15,
        };
        _textBox.Use(TextBox.BackgroundProperty, "Mat.Surface");
        Avalonia.Automation.AutomationProperties.SetName(_textBox, "Dokumenttext");
        _textBox.TextChanged += (_, _) => OnTextChanged();
        _document.Changed += OnDocumentChanged;

        var note = new InfoBar { Margin = new Thickness(0, 0, 0, 18) };
        note.Show("Editor folgt: Der Textverarbeitungs-Editor von ThixWrite entsteht in der nächsten Phase. Bis dahin ist dies ein einfaches Textfeld – Neu, Öffnen, Speichern und „Zuletzt verwendet“ funktionieren schon (Textdateien, .txt).", InfoSeverity.Info);

        var sheet = new Border
        {
            Width = 794,
            MinHeight = 900,
            Padding = new Thickness(72, 64),
            Margin = new Thickness(0, 28, 0, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(MatTokens.Size.RadiusSm),
            Child = new StackPanel { Children = { note, _textBox } },
        }.Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");

        _zoomed = new LayoutTransformControl { Child = sheet };
        _view = new Border
        {
            Child = new ScrollViewer { Content = _zoomed, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
        }.Use(Border.BackgroundProperty, "Mat.Bg");
    }

    public override Control View => _view;

    /// <summary>The scale the page is drawn at (the zoom; tests).</summary>
    public double Scale => _zoomed.LayoutTransform is ScaleTransform scale ? scale.ScaleX : 1;

    /// <summary>The text box (tests type into it).</summary>
    public TextBox TextBox => _textBox;

    /// <summary>Where the caret is (the "Datum und Uhrzeit" command inserts there).</summary>
    public int CaretIndex => _textBox.CaretIndex;

    public override void FocusDocument() => _textBox.Focus();

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        var (words, characters) = _document.Count();
        statusBar.SetItem(WordsItem, words == 1 ? "1 Wort" : $"{words} Wörter");
        statusBar.SetItem(CharactersItem, characters == 1 ? "1 Zeichen" : $"{characters} Zeichen");
    }

    /// <summary>The placeholder draws the whole page larger or smaller (the native editor lays out at the zoom).</summary>
    protected override void OnZoomChanged(double zoom) =>
        _zoomed.LayoutTransform = zoom == 100 ? null : new ScaleTransform(zoom / 100, zoom / 100);

    public override void Detach() => _document.Changed -= OnDocumentChanged;

    /// <summary>Inserts text at the caret as one undo step and puts the caret after it.</summary>
    public void InsertAtCaret(string value)
    {
        var caret = _document.Insert(_textBox.CaretIndex, value);
        _textBox.CaretIndex = caret;
        _textBox.Focus();
    }

    private void OnTextChanged()
    {
        if (_isUpdating)
        {
            return;
        }

        _document.SetText(_textBox.Text ?? "", isTyping: true);
    }

    /// <summary>Undo, redo, an inserted text: the box shows the document again (the caret stays where it can).</summary>
    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (_textBox.Text == _document.Text)
        {
            return;
        }

        _isUpdating = true;
        var caret = _textBox.CaretIndex;
        _textBox.Text = _document.Text;
        _textBox.CaretIndex = Math.Min(caret, _document.Text.Length);
        _isUpdating = false;
    });
}

using Avalonia.Automation;
using Avalonia.Controls;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>Einfügen › Link (Strg+K): the text shown and the address (a web page or an e-mail address), Word's dialog in short.</summary>
public sealed class HyperlinkDialog : WriteDialog
{
    private readonly WriteEditor _editor;

    public HyperlinkDialog(WriteEditor editor)
        : base("Link einfügen", 480)
    {
        _editor = editor;
        var session = editor.Session;
        var existing = session.LinkAtCaret;
        DisplayText = new TextBox { Text = session.LinkRange(session.Selection.Start) is { } link && session.Selection.IsCollapsed ? Text(link.Range) : session.SelectedText ?? "" };
        Address = new TextBox { Text = existing?.Uri ?? "", Watermark = "https://www.beispiel.de oder name@beispiel.de" };
        AutomationProperties.SetName(DisplayText, "Anzuzeigender Text");
        AutomationProperties.SetName(Address, "Adresse");
        var address = Field("Adresse", Address);
        FormErrors.SetField(address, LinkService.AddressField);
        Bookmarks = new ComboBox { ItemsSource = new[] { "(keine)" }.Concat(session.Bookmarks().Select(bookmark => bookmark.Name)).ToList(), SelectedIndex = 0, Width = 260 };
        if (existing?.Anchor is { } anchor && Bookmarks.Items.Cast<string>().ToList().IndexOf(anchor) is > 0 and var index)
        {
            Bookmarks.SelectedIndex = index;
        }

        AutomationProperties.SetName(Bookmarks, "Textmarke in diesem Dokument");
        SetBody(new StackPanel { Spacing = 12, Children = { Field("Anzuzeigender Text", DisplayText), address, Field("Oder: Textmarke in diesem Dokument", Bookmarks) } });
        Opened += (_, _) => Address.Focus();

        string Text(ThixWrite.Core.Model.DocumentRange range) =>
            session.Document.ParagraphAt(range.Start.Block)?.Text[range.Start.Offset..range.End.Offset] ?? "";
    }

    public TextBox DisplayText { get; }

    public TextBox Address { get; }

    /// <summary>A bookmark of this document as the target (Einfügen › Lesezeichen), instead of an address.</summary>
    public ComboBox Bookmarks { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new HyperlinkDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (Bookmarks.SelectedIndex > 0 && Bookmarks.SelectedItem is string bookmark)
        {
            _editor.Session.SetHyperlink("", string.IsNullOrWhiteSpace(DisplayText.Text) ? null : DisplayText.Text, anchor: bookmark);
            return true;
        }

        try
        {
            var address = LinkService.Normalize(Address.Text);
            _editor.Session.SetHyperlink(address, string.IsNullOrWhiteSpace(DisplayText.Text) ? null : DisplayText.Text);
            return true;
        }
        catch (BusinessException exception)
        {
            FormErrors.ShowError(this, exception.Field, exception.Message);
            return false;
        }
    }
}

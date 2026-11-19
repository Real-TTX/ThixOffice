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
        SetBody(new StackPanel { Spacing = 12, Children = { Field("Anzuzeigender Text", DisplayText), address } });
        Opened += (_, _) => Address.Focus();

        string Text(ThixWrite.Core.Model.DocumentRange range) =>
            session.Document.ParagraphAt(range.Start.Block)?.Text[range.Start.Offset..range.End.Offset] ?? "";
    }

    public TextBox DisplayText { get; }

    public TextBox Address { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new HyperlinkDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
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

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>Bildformat › Alternativtext: one or two sentences that describe the picture (read by screen readers, kept in the docx).</summary>
public sealed class AltTextDialog : WriteDialog
{
    private readonly WriteEditor _editor;

    public AltTextDialog(WriteEditor editor)
        : base("Alternativtext", 460)
    {
        _editor = editor;
        Text = new TextBox
        {
            Text = editor.Session.SelectedImage?.Image.Description ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 96,
            Watermark = "Wie würden Sie dieses Bild jemandem beschreiben, der es nicht sieht?",
        };
        AutomationProperties.SetName(Text, "Alternativtext");
        SetBody(Field("Beschreibung", Text));
    }

    public TextBox Text { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new AltTextDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        _editor.Session.SetImageDescription(Text.Text);
        return true;
    }
}

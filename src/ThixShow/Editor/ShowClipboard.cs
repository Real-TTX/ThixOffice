using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// Kopieren, Ausschneiden, Einfügen of ThixShow: the own format (the fragment as a small pptx: slides, shapes or formatted text,
/// <see cref="ShowFragment"/>) plus its plain text for other programs. Paste order: the own format, a picture (a screenshot, a
/// picture from a browser), then text (into the edited text, else as a new text box) – what ThixWrite and ThixCalc put as text
/// arrives that way. In the notes pane the keys stay the text field's.
/// </summary>
internal sealed class ShowClipboard(ShowEditor editor)
{
    private static readonly DataFormat<byte[]> FragmentFormat = DataFormat.CreateBytesApplicationFormat("ThixShow.Fragment");

    private EditingSession Session => editor.Session;

    private IClipboard? Clipboard => TopLevel.GetTopLevel(editor.View)?.Clipboard ?? editor.Window.Clipboard;

    /// <summary>The selection is slides when the thumbnails or the sorter have the keyboard.</summary>
    private void PrepareSelection()
    {
        if (editor.FocusedArea == EditorArea.Slides || editor.CurrentView == ShowView.Sorter)
        {
            Session.SelectSlides(editor.SelectedSlideIds);
        }
    }

    public async Task CopyAsync(bool cut)
    {
        PrepareSelection();
        ShowFragment? fragment = null;
        var ok = await editor.RunAsync(cut ? "Ausschneiden" : "Kopieren", () => fragment = cut ? Session.Cut() : Session.Copy());
        if (!ok || fragment is null || Clipboard is not { } clipboard)
        {
            return;
        }

        var item = new DataTransferItem();
        item.Set(FragmentFormat, fragment.ToBytes());
        if (fragment.PlainText.Length > 0)
        {
            item.SetText(fragment.PlainText);
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    public async Task PasteAsync(bool textOnly = false)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        PrepareSelection();
        if (!textOnly && await clipboard.TryGetValueAsync(FragmentFormat) is { Length: > 0 } bytes && ShowFragment.FromBytes(bytes) is { } fragment)
        {
            await editor.RunAsync("Einfügen", () => Session.Paste(fragment));
            return;
        }

        if (!textOnly && await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using var png = new MemoryStream();
            bitmap.Save(png);
            var image = ImageData.From(png.ToArray());
            await editor.RunAsync("Bild einfügen", () => Session.InsertPicture(image));
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            await editor.RunAsync("Einfügen", () => Session.PasteText(text));
        }
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Referenzen in the real window: footnotes and endnotes through the keyboard, typed in place, clicked, saved.</summary>
public sealed class ReferenceUiTests
{
    [AvaloniaFact]
    public async Task Strg_Alt_F_inserts_a_footnote_whose_text_is_typed_in_place_at_the_foot_of_the_page()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Fußnoten.docx");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Ein Satz mit Quelle");
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.F, OperatingSystem.IsMacOS() ? CommandKey | RawInputModifiers.Alt | RawInputModifiers.Shift : CommandKey | RawInputModifiers.Alt);
        await SettleAsync();
        Assert.True(editor.IsEditingNote);
        Assert.False(editor.IsEditingHeaderFooter);
        await TypeAsync(window, "Die Quelle.");
        await SettleAsync();
        Assert.Equal("Ein Satz mit Quelle￼", TextOf(window)); // the body has the reference only

        // Esc puts the note's text into the document; the page shows it at its foot.
        await PressAsync(window, PhysicalKey.Escape);
        await SettleAsync();
        Assert.False(editor.IsEditingNote);
        var note = editor.Document.Content.ParagraphAt(0)!.Inlines.OfType<NoteInline>().Single();
        Assert.Equal("Die Quelle.", note.PlainText.Trim());
        var placed = Assert.Single(editor.Layout.Pages[0].Footnotes);
        Assert.Equal("1", placed.Label);

        // A click on the note's text opens it again; typing goes there; a click on the text of the page closes it.
        var line = placed.Lines[0];
        editor.Scroller.Offset = new Vector(0, editor.Surface.ToSurface(0, 0, line.Top, 0, 0).Y - 300);
        await SettleAsync();
        var inNote = editor.Surface.TranslatePoint(editor.Surface.ToSurface(0, line.Left + 120, line.Top + 4, 0, 0).TopLeft, window)!.Value;
        window.MouseDown(inNote, MouseButton.Left);
        window.MouseUp(inNote, MouseButton.Left);
        await SettleAsync();
        Assert.True(editor.IsEditingNote);
        await PressAsync(window, PhysicalKey.End);
        await TypeAsync(window, " Seite 4.");
        var body = editor.Surface.TranslatePoint(editor.Surface.ToSurface(0, 100, line.Top - 200, 0, 0).TopLeft, window)!.Value;
        window.MouseDown(body, MouseButton.Left);
        window.MouseUp(body, MouseButton.Left);
        await SettleAsync();
        Assert.False(editor.IsEditingNote);
        Assert.Equal("Die Quelle. Seite 4.", editor.Document.Content.ParagraphAt(0)!.Inlines.OfType<NoteInline>().Single().PlainText.Trim());

        // Saved as docx: the note is in footnotes.xml.
        await PressAsync(window, PhysicalKey.S, CommandKey);
        await SettleAsync();
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(target, false);
        Assert.Contains("Die Quelle. Seite 4.", package.MainDocumentPart!.FootnotesPart!.Footnotes!.InnerText);

        // Undo takes the edits back, then the note.
        editor.Session.Undo();
        editor.Session.Undo();
        editor.Session.Undo();
        Assert.Empty(editor.Document.Content.ParagraphAt(0)!.Inlines.OfType<NoteInline>());
    }

    [AvaloniaFact]
    public async Task Endnotes_come_after_the_text_and_Naechste_Fussnote_jumps_to_the_references()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Erstens");
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(WriteCommandIds.InsertEndnote);
        await SettleAsync();
        Assert.True(editor.IsEditingNote);
        await TypeAsync(window, "Ende.");
        await PressAsync(window, PhysicalKey.Escape);
        await TypeAsync(window, " und zweitens");
        await window.Commands.ExecuteAsync(WriteCommandIds.InsertFootnote);
        await PressAsync(window, PhysicalKey.Escape);
        await SettleAsync();

        var last = editor.Layout.Pages[^1];
        Assert.Equal("i", Assert.Single(last.Endnotes).Label);
        Assert.Single(editor.Layout.Pages[0].Footnotes);

        await PressAsync(window, PhysicalKey.Home, CommandKey);
        await window.Commands.ExecuteAsync($"{WriteCommandIds.NextFootnote}.endnote.next");
        Assert.Equal(new DocumentPosition(0, 8), editor.Session.Selection.Active);
        await window.Commands.ExecuteAsync($"{WriteCommandIds.NextFootnote}.footnote.next");
        Assert.Equal(new DocumentPosition(0, 22), editor.Session.Selection.Active);
    }
}

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Core.Business;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Überprüfen › Kommentare in the real window: Strg+Alt+K, typing into the card, the author from the settings, deleting, saving.</summary>
public sealed class CommentUiTests
{
    [AvaloniaFact]
    public async Task Strg_Alt_K_adds_a_comment_whose_text_is_typed_into_its_card_and_saved_in_comments_xml()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { UserName = "Anna Berger" });
        var target = Path.Combine(data.DocumentsFolder, "Kommentare.docx");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Der Umsatz stieg.");
        var editor = EditorOf(window);
        Assert.False(editor.ShowComments);

        editor.Session.Select(new DocumentPosition(0, 4), new DocumentPosition(0, 10));
        await PressAsync(window, PhysicalKey.K, CommandKey | RawInputModifiers.Alt);
        await SettleAsync();
        Assert.True(editor.ShowComments);
        var placed = Assert.Single(editor.Document.Session.Comments());
        Assert.Equal(("Anna Berger", "AB"), (placed.Comment.Author, placed.Comment.Initials));
        var field = editor.CommentsView.FieldOf(placed.Comment.Id)!;
        Assert.True(field.IsFocused);

        // Typed into the card; leaving the card puts the text into the document as one step.
        await TypeAsync(window, "Quelle?");
        editor.FocusDocument();
        await SettleAsync();
        Assert.Equal("Quelle?", editor.Document.Session.Comments().Single().Comment.Text);
        Assert.Equal("Der Umsatz stieg.", TextOf(window).Replace("￼", ""));

        // Saved: comments.xml has it with its author.
        await PressAsync(window, PhysicalKey.S, CommandKey);
        await SettleAsync();
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(target, false))
        {
            var comments = package.MainDocumentPart!.WordprocessingCommentsPart!.Comments!;
            Assert.Contains("Quelle?", comments.InnerText);
            Assert.Contains("Anna Berger", comments.OuterXml);
        }

        // Weiter selects the commented text; Löschen deletes the comment, the text stays.
        editor.Session.SetCaret(new DocumentPosition(0, 0));
        await window.Commands.ExecuteAsync(WriteCommandIds.NextComment);
        Assert.Equal("Umsatz", editor.Document.Content.ParagraphAt(0)!.Text[editor.Session.Selection.Start.Offset..editor.Session.Selection.End.Offset].Replace("￼", ""));
        await window.Commands.ExecuteAsync(WriteCommandIds.DeleteCommentOne);
        await SettleAsync();
        Assert.Empty(editor.Document.Session.Comments());
        Assert.Equal("Der Umsatz stieg.", TextOf(window));
        Assert.Empty(editor.CommentsView.Shown);
    }
}

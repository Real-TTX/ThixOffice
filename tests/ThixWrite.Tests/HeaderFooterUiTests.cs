using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Headers and footers in the real window: a double click opens one, typing goes there, Esc closes it, page numbers.</summary>
public sealed class HeaderFooterUiTests
{
    [AvaloniaFact]
    public async Task A_double_click_above_the_text_edits_the_header_in_place_and_Esc_puts_it_into_the_document()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Text");
        var editor = EditorOf(window);
        var page = editor.Surface.PageRect(0);
        var inHeader = editor.Surface.TranslatePoint(new Point(page.X + 200, page.Y + 20), window)!.Value;

        window.MouseDown(inHeader, MouseButton.Left);
        window.MouseUp(inHeader, MouseButton.Left);
        window.MouseDown(inHeader, MouseButton.Left);
        window.MouseUp(inHeader, MouseButton.Left);
        await SettleAsync();

        Assert.True(editor.IsEditingHeaderFooter);
        Assert.True(editor.Surface.DimBody);
        Assert.Contains(HeaderFooterCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.True(editor.StorySurface!.IsFocused);
        await TypeAsync(window, "Mein Kopf ");
        await window.Commands.ExecuteAsync(WriteCommandIds.HeaderFooterPage);
        await SettleAsync();
        Assert.Equal("Text", TextOf(window).Split('\n')[0]); // the body did not change

        await PressAsync(window, PhysicalKey.Escape);
        await SettleAsync();
        Assert.False(editor.IsEditingHeaderFooter);
        Assert.False(editor.Surface.DimBody);
        var header = editor.Document.Content.FinalSection.Headers.Default!;
        var paragraph = (Paragraph)header.Blocks[0];
        Assert.Equal("Mein Kopf ", paragraph.Inlines.OfType<TextRun>().Single().Text);
        Assert.Contains(paragraph.Inlines, inline => inline is FieldInline { Kind: FieldKind.Page });
        Assert.Equal(EditingSessionHeaderStyle, paragraph.Properties.StyleId);
        Assert.NotEmpty(editor.Layout.Pages[0].HeaderLines);

        editor.Session.Undo();
        Assert.Null(editor.Document.Content.FinalSection.Headers.Default);
    }

    private const string EditingSessionHeaderStyle = Core.Editing.EditingSession.HeaderStyle;

    [AvaloniaFact]
    public async Task Seitenzahl_at_the_bottom_numbers_every_page_and_saving_while_editing_keeps_the_footer()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Seiten.docx");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Eins");
        await PressAsync(window, PhysicalKey.Enter, TestData.CommandKey);
        await TypeAsync(window, "Zwei");
        var editor = EditorOf(window);

        await window.Commands.ExecuteAsync($"{WriteCommandIds.PageNumber}.bottom.right");
        await SettleAsync();
        Assert.Equal(2, editor.Layout.PageCount);
        Assert.All(editor.Layout.Pages, page => Assert.NotEmpty(page.FooterLines));
        Assert.Equal(ParagraphAlignment.Right, ((Paragraph)editor.Document.Content.FinalSection.Footers.Default!.Blocks[0]).Properties.Alignment);

        // The footer opened from the ribbon, typed into, saved without closing it: the file has it.
        await window.Commands.ExecuteAsync($"{WriteCommandIds.Footer}.edit");
        await SettleAsync();
        Assert.True(editor.IsEditingHeaderFooter);
        await PressAsync(window, PhysicalKey.Home);
        await TypeAsync(window, "Seite ");
        await PressAsync(window, PhysicalKey.S, TestData.CommandKey);
        await SettleAsync();

        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(target, false);
        Assert.Contains("Seite", package.MainDocumentPart!.FooterParts.Single().Footer!.InnerText);
        Assert.True(editor.IsEditingHeaderFooter); // still open
    }
}

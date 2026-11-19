using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>The spelling check in the real window: red wavy lines in the background, the context menu, F7, the setting, Sprache.</summary>
public sealed class SpellingUiTests
{
    /// <summary>Waits until the background check underlined what is visible (it runs on a worker thread).</summary>
    private static async Task<IReadOnlyList<DocumentRange>> UnderlinedAsync(WriteEditor editor, int expected)
    {
        for (var round = 0; round < 50; round++)
        {
            editor.Surface.InvalidateVisual();
            await SettleAsync();
            if (editor.Surface.UnderlinedWords.Count >= expected)
            {
                break;
            }

            await Task.Delay(20);
        }

        return editor.Surface.UnderlinedWords;
    }

    [AvaloniaFact]
    public async Task Unknown_words_get_a_wavy_line_and_the_context_menu_puts_a_suggestion_in()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "Ein Fehlr hier.");

        var underlined = Assert.Single(await UnderlinedAsync(editor, 1));
        Assert.Equal((new DocumentPosition(0, 4), new DocumentPosition(0, 9)), (underlined.Start, underlined.End));

        // The right click puts the caret into the word; the menu starts with the suggestions.
        editor.Session.SetCaret(new DocumentPosition(0, 6));
        var items = editor.SpellingMenuItems(editor.Session.Selection.Start);
        Assert.Equal(["Fehler", "Fehl", "Alle ignorieren", "Zum Wörterbuch hinzufügen"], items.OfType<MenuItem>().Select(item => item.Header as string));
        ((MenuItem)items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await SettleAsync();
        Assert.Equal("Ein Fehler hier.", TextOf(window));
        Assert.Empty(await UnderlinedAsync(editor, 0));
        editor.Session.Undo();
        Assert.Equal("Ein Fehlr hier.", TextOf(window));

        // Alle ignorieren: no line any more; the setting turns the lines off.
        Assert.Single(await UnderlinedAsync(editor, 1));
        editor.Session.SetCaret(new DocumentPosition(0, 6));
        ((MenuItem)editor.SpellingMenuItems(editor.Session.Selection.Start).OfType<MenuItem>().Single(item => item.Header as string == "Alle ignorieren")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(await UnderlinedAsync(editor, 0));
        await TypeAsync(window, " Tipfehler");
        await PressAsyncEnd(window);
        Assert.Single(await UnderlinedAsync(editor, 1));
        SettingsService.Update(settings => settings with { CheckSpellingAsYouType = false });
        Assert.Empty(await UnderlinedAsync(editor, 0));
    }

    private static async Task PressAsyncEnd(Avalonia.Controls.Window window) => await PressAsync((ThixOffice.Desktop.Shell.OfficeWindow)window, Avalonia.Input.PhysicalKey.Enter);

    [AvaloniaFact]
    public async Task F7_steps_through_the_unknown_words_and_Sprache_changes_the_language_checked()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "Ein Tipfehler und ein Fehlr.");
        await PressAsync(window, Avalonia.Input.PhysicalKey.Home);

        var dialog = new SpellingDialog(editor);
        Assert.Equal("Tipfehler", dialog.CurrentWord);
        Assert.Equal(["Tippfehler"], dialog.Suggestions);
        dialog.Confirm(); // Ändern
        Assert.Equal("Ein Tippfehler und ein Fehlr.", TextOf(window));
        Assert.Equal("Fehlr", dialog.CurrentWord);
        dialog.IgnoreOnce();
        Assert.Null(dialog.CurrentWord);
        Assert.False(dialog.OkButton.IsVisible);

        // Sprache: Englisch (USA) – the English check knows every word; saved with the document as its language.
        Assert.Equal("de-DE", editor.Document.Session.Language);
        Assert.True(window.Commands[$"{WriteCommandIds.Language}.de-DE"].Checked);
        await window.Commands.ExecuteAsync($"{WriteCommandIds.Language}.en-US");
        Assert.Equal("en-US", editor.Document.Session.Language);
        Assert.Empty(await UnderlinedAsync(editor, 0));
        editor.Session.Undo();
        Assert.Equal("de-DE", editor.Document.Session.Language);
        Assert.Single(await UnderlinedAsync(editor, 1));

        // Wörter zählen: of the selection ("Fehlr" is still selected), else of the document.
        Assert.Equal(1, new WordCountDialog(editor).Rows.Single(row => row.Label == "Wörter").Value);
        editor.Session.SetCaret(new DocumentPosition(0, 0));
        Assert.Equal(5, new WordCountDialog(editor).Rows.Single(row => row.Label == "Wörter").Value);
    }
}

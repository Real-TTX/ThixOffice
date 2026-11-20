using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatDesktop;
using ThixOffice.Desktop.Dialogs;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Formatvorlagen in the real window: the gallery, "Formatvorlage ändern …", a new quick style from the selection, the pane.</summary>
public sealed class StyleUiTests
{
    [AvaloniaFact]
    public async Task Formatvorlage_aendern_changes_the_style_and_every_paragraph_with_it()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Eins");
        await PressAsync(window, PhysicalKey.Enter);
        await TypeAsync(window, "Zwei");
        var session = EditorOf(window).Session;
        var gallery = (MatRibbonGallery)window.RibbonDefinition.Find(WriteCommandIds.Styles)!;
        Assert.Equal(17, gallery.Entries.Count); // Word's quick styles
        Assert.Contains(gallery.Menu, item => item.Id == WriteCommandIds.StyleModify);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.StyleModify);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<StyleDialog>().Single();
        Assert.Equal("Standard", dialog.NameField.Text);
        dialog.SizeField.Value = 14;
        dialog.Bold.IsChecked = true;
        dialog.Confirm();
        await SettleAsync();

        foreach (var paragraph in session.Document.Blocks.OfType<Paragraph>())
        {
            var format = session.Resolver.ResolveCharacter(paragraph, ((TextRun)paragraph.Inlines[0]).Properties);
            Assert.Equal(14, format.FontSize);
            Assert.True(format.Bold);
        }

        Assert.Equal(11, session.Document.Styles.CharacterDefaults.FontSize); // the style changed, not the defaults
    }

    [AvaloniaFact]
    public async Task A_new_quick_style_from_the_selection_joins_the_gallery_and_the_pane_shows_the_styles()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Merksatz");
        var editor = EditorOf(window);
        editor.Session.SelectAll();
        editor.Session.FormatCharacters("Farbe", (_, props) => props with { Italic = true });

        _ = window.Commands.ExecuteAsync(WriteCommandIds.StyleFromSelection);
        await SettleAsync();
        var ask = window.OwnedWindows.OfType<TextInputDialog>().Single();
        ask.Box.Text = "Merksatz";
        ask.Box.Focus();
        await PressAsync(window, PhysicalKey.Enter);
        await SettleAsync();
        var id = editor.Session.Document.ParagraphAt(0)!.Properties.StyleId;
        Assert.Equal("Merksatz", id);
        var gallery = (MatRibbonGallery)window.RibbonDefinition.Find(WriteCommandIds.Styles)!;
        Assert.Contains(gallery.Entries, entry => entry.Id == id);

        await PressAsync(window, PhysicalKey.S, CommandKey | RawInputModifiers.Alt | RawInputModifiers.Shift);
        await SettleAsync();
        Assert.True(editor.ShowStyles);
        Assert.Contains(editor.StylesView.Shown, style => style.Id == id);
    }
}

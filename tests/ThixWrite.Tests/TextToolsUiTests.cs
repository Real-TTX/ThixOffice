using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatDesktop;
using ThixOffice.Desktop;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Links, the dialogs Schriftart and Absatz, Format übertragen, Symbol and the ruler, through real input.</summary>
public sealed class TextToolsUiTests
{
    [AvaloniaFact]
    public async Task Ctrl_K_links_the_selected_text_and_Ctrl_click_opens_only_web_and_mail_addresses()
    {
        using var data = new TestData();
        var opened = new List<string>();
        Ui.OpenExternalForTests = opened.Add;
        var window = await NewWindowAsync();
        await TypeAsync(window, "Siehe Webseite hier");
        var session = EditorOf(window).Session;
        session.Select(new DocumentPosition(0, 6), new DocumentPosition(0, 14));

        await PressAsync(window, PhysicalKey.K, CommandKey);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<HyperlinkDialog>().Single();
        Assert.Equal("Webseite", dialog.DisplayText.Text);
        dialog.Address.Text = "javascript:alert(1)";
        dialog.Confirm();
        Assert.True(dialog.IsVisible); // refused at the field
        dialog.Address.Text = "www.thix.de";
        dialog.Confirm();
        await SettleAsync();

        var run = session.Document.ParagraphAt(0)!.Inlines.OfType<TextRun>().Single(item => item.Link is not null);
        Assert.Equal(("Webseite", "https://www.thix.de"), (run.Text, run.Link!.Uri));
        Assert.Equal(BuiltInStyles.HyperlinkStyle, run.Properties.StyleId);

        var point = PointOf(window, new DocumentPosition(0, 9));
        window.MouseDown(point, MouseButton.Left, CommandKey);
        window.MouseUp(point, MouseButton.Left, CommandKey);
        await SettleAsync();
        Assert.Equal(["https://www.thix.de"], opened);

        session.SetCaret(new DocumentPosition(0, 8));
        await window.Commands.ExecuteAsync(WriteCommandIds.HyperlinkRemove);
        Assert.DoesNotContain(session.Document.ParagraphAt(0)!.Inlines, inline => inline.Link is not null);
    }

    [AvaloniaFact]
    public async Task Schriftart_and_Absatz_change_the_selection_in_one_step_each()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Ein Absatz");
        var session = EditorOf(window).Session;
        session.SelectAll();

        await PressAsync(window, PhysicalKey.D, CommandKey);
        await SettleAsync();
        var font = window.OwnedWindows.OfType<FontDialog>().Single();
        font.StyleChoice.SelectedIndex = 3; // Fett Kursiv
        font.SizeField.Value = 14;
        font.Strikethrough.IsChecked = true;
        font.Confirm();
        await SettleAsync();
        var properties = session.Document.ParagraphAt(0)!.Inlines[0].Properties;
        Assert.Equal((true, true, 14.0, true), (properties.Bold, properties.Italic, properties.FontSize, properties.Strikethrough));

        _ = window.Commands.ExecuteAsync(WriteCommandIds.ParagraphDialog);
        await SettleAsync();
        var paragraph = window.OwnedWindows.OfType<ParagraphDialog>().Single();
        paragraph.IndentLeft.Value = 1;
        paragraph.Special.SelectedIndex = 2; // hängend
        paragraph.SpecialBy.Value = 0.5m;
        paragraph.TabPosition.Value = 5;
        paragraph.TabKind.SelectedIndex = 2; // rechts
        paragraph.SetTab();
        paragraph.Confirm();
        await SettleAsync();
        var format = session.Resolver.ResolveParagraph(session.Document.ParagraphAt(0)!);
        Assert.Equal(72 / 2.54, format.IndentLeft, 2);
        Assert.Equal(-0.5 * 72 / 2.54, format.FirstLineIndent, 2);
        Assert.Contains(format.Tabs, stop => Math.Abs(stop.Position - (5 * 72 / 2.54)) < 0.01 && stop.Alignment == TabAlignment.Right);

        session.Undo();
        Assert.Equal(0, session.Resolver.ResolveParagraph(session.Document.ParagraphAt(0)!).IndentLeft);
        Assert.True(session.Document.ParagraphAt(0)!.Inlines[0].Properties.Bold); // the font was its own step
    }

    [AvaloniaFact]
    public async Task Format_uebertragen_gives_the_next_selection_the_format()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "fett normal");
        var session = EditorOf(window).Session;
        session.Select(new DocumentPosition(0, 0), new DocumentPosition(0, 4));
        session.SetBold(true);

        await PressAsync(window, PhysicalKey.C, CommandKey | RawInputModifiers.Shift);
        Assert.True(EditorOf(window).IsPainting);
        var start = PointOf(window, new DocumentPosition(0, 5));
        var end = PointOf(window, new DocumentPosition(0, 11));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await SettleAsync();

        Assert.False(EditorOf(window).IsPainting);
        Assert.All(session.Document.ParagraphAt(0)!.Inlines.OfType<TextRun>().Where(item => item.Text.Contains("normal", StringComparison.Ordinal)), item => Assert.True(item.Properties.Bold));
    }

    [AvaloniaFact]
    public async Task HTML_of_another_program_pastes_with_its_formatting_in_one_step()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Vorher ");
        var editor = EditorOf(window);

        Assert.True(editor.PasteHtml("<p>Aus dem <b>Browser</b></p><ul><li>Punkt</li></ul><script>böse()</script>"));
        await SettleAsync();
        Assert.Equal("Vorher Aus dem Browser\nPunkt", TextOf(window));
        Assert.True(editor.Session.Document.ParagraphAt(0)!.Inlines.OfType<TextRun>().Single(run => run.Text == "Browser").Properties.Bold);
        editor.Session.Undo();
        Assert.Equal("Vorher ", TextOf(window));
    }

    [AvaloniaFact]
    public async Task Symbol_inserts_from_its_menu_and_the_dialog()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await window.Commands.ExecuteAsync(TextCommands.SymbolId("€"));
        _ = window.Commands.ExecuteAsync(WriteCommandIds.SymbolMore);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<SymbolDialog>().Single();
        dialog.Code.Text = "2192";
        dialog.Confirm();
        await SettleAsync();
        Assert.Equal("€→", TextOf(window));
    }

    [AvaloniaFact]
    public async Task The_ruler_shows_the_indents_and_a_click_on_it_sets_a_tab_stop()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Absatz");
        var editor = EditorOf(window);
        Assert.False(editor.ShowRuler);
        await window.Commands.ExecuteAsync(WriteCommandIds.Ruler);
        await SettleAsync();
        Assert.True(editor.RulerView.IsVisible);

        var ruler = editor.RulerView;
        var (pageX, scale, setup) = ruler.Geometry;
        var tabX = pageX + ((setup.MarginLeft + 100) * scale);
        var click = ruler.TranslatePoint(new Point(tabX, Ruler.RulerHeight / 2), window)!.Value;
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        await SettleAsync();
        var format = editor.Session.Resolver.ResolveParagraph(editor.Session.Document.ParagraphAt(0)!);
        Assert.InRange(Assert.Single(format.Tabs).Position, 99, 101); // snapped to a sixteenth of a centimetre

        // The first line marker 1 cm to the right.
        var first = ruler.Markers().Single(marker => marker.Kind == Ruler.MarkerKind.FirstLine);
        ruler.Move(first, 72 / 2.54);
        Assert.Equal(72 / 2.54, editor.Session.Resolver.ResolveParagraph(editor.Session.Document.ParagraphAt(0)!).FirstLineIndent, 1);
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatDesktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>The native editor driven through real key, text and pointer events: typing, caret movement, selection, formatting.</summary>
public sealed class EditorTests
{
    [AvaloniaFact]
    public async Task Typing_Enter_Backspace_and_word_movement_with_Shift_edit_the_document()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();

        await TypeAsync(window, "Hallo Welt\nZweiter Absatz");
        Assert.Equal("Hallo Welt\nZweiter Absatz", TextOf(window));

        await PressAsync(window, PhysicalKey.Backspace);
        await PressAsync(window, PhysicalKey.Backspace, WordKey); // the word before the caret
        Assert.Equal("Hallo Welt\nZweiter ", TextOf(window));

        await PressAsync(window, PhysicalKey.Home);
        await PressAsync(window, PhysicalKey.ArrowRight, WordKey | RawInputModifiers.Shift);
        var editor = EditorOf(window);
        Assert.Equal(new DocumentPosition(1, 0), editor.Session.Selection.Anchor);
        Assert.Equal(new DocumentPosition(1, 8), editor.Session.Selection.Active);

        await TypeAsync(window, "Dritter ");
        Assert.Equal("Hallo Welt\nDritter ", TextOf(window));

        await PressAsync(window, PhysicalKey.ArrowUp);
        Assert.Equal(0, editor.Session.Selection.Active.Block);
        await PressAsync(window, PhysicalKey.End, CommandKey);
        Assert.Equal(editor.Document.Content.End, editor.Session.Selection.Active);
        await PressAsync(window, PhysicalKey.Home, CommandKey | RawInputModifiers.Shift);
        Assert.Equal(new DocumentPosition(0, 0), editor.Session.Selection.Active);
        Assert.False(editor.Session.Selection.IsCollapsed);

        await PressAsync(window, PhysicalKey.Delete);
        Assert.Equal("", TextOf(window));
    }

    [AvaloniaFact]
    public async Task Shift_Enter_breaks_the_line_and_Ctrl_Enter_starts_a_new_page()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Zeile");
        await PressAsync(window, PhysicalKey.Enter, RawInputModifiers.Shift);
        await TypeAsync(window, "Weiter");
        Assert.Equal($"Zeile{SpecialCharacters.LineBreak}Weiter", TextOf(window));

        await PressAsync(window, PhysicalKey.Enter, CommandKey);
        await TypeAsync(window, "Seite zwei");
        var editor = EditorOf(window);
        Assert.Equal(2, editor.Layout.PageCount);
        Assert.Equal("Seite 2 von 2", window.StatusBar.TextOf(WriteEditor.PageItem));

        await PressAsync(window, PhysicalKey.Z, CommandKey);
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal(1, editor.Layout.PageCount);
        await PressAsync(window, PhysicalKey.Y, CommandKey);
        Assert.Equal(2, editor.Layout.PageCount);
    }

    [AvaloniaFact]
    public async Task Formatting_shortcuts_and_the_ribbon_follow_the_selection()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "normal ");
        await PressAsync(window, PhysicalKey.F, CommandKey | RawInputModifiers.Shift); // Strg+Umschalt+F: fett
        await TypeAsync(window, "fett");
        var bold = window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.Bold);
        Assert.True(bold.IsChecked);

        await PressAsync(window, PhysicalKey.Home);
        Assert.False(bold.IsChecked);

        // Strg+B is the alias of fett; Strg+E centres; the align toggles are one choice.
        await PressAsync(window, PhysicalKey.A, CommandKey);
        await PressAsync(window, PhysicalKey.B, CommandKey);
        Assert.True(EditorOf(window).Session.GetFormatting().Bold.Is(true));
        Assert.True(bold.IsChecked);
        await PressAsync(window, PhysicalKey.E, CommandKey);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.AlignCenter).IsChecked);
        Assert.False(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.AlignLeft).IsChecked);
        await PressAsync(window, PhysicalKey.I, CommandKey);
        await PressAsync(window, PhysicalKey.U, CommandKey | RawInputModifiers.Shift);
        var format = EditorOf(window).Session.GetFormatting();
        Assert.True(format.Italic.Is(true));
        Assert.True(format.Underline.Is(UnderlineStyle.Single));
    }

    [AvaloniaFact]
    public async Task The_value_items_of_the_ribbon_show_and_set_font_size_font_style_and_colour()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Überschrift\nText");
        var editor = EditorOf(window);
        var size = window.RibbonDefinition.Get<MatRibbonComboBox>(WriteCommandIds.FontSize);
        var font = window.RibbonDefinition.Get<MatRibbonComboBox>(WriteCommandIds.FontName);
        var styles = window.RibbonDefinition.Get<MatRibbonGallery>(WriteCommandIds.Styles);
        Assert.Equal("11", size.Text);
        Assert.Equal(WriteFonts.Default.Body, font.Text);
        Assert.Equal(BuiltInStyles.Normal, styles.SelectedId);

        await PressAsync(window, PhysicalKey.ArrowUp);
        window.RibbonDefinition.Choose(styles, BuiltInStyles.Heading1);
        await SettleAsync();
        Assert.Equal(BuiltInStyles.Heading1, editor.Session.GetFormatting().ParagraphStyleId.Value);
        Assert.Equal("16", size.Text);

        // Mixed: both paragraphs selected, two sizes – the field is empty.
        await PressAsync(window, PhysicalKey.A, CommandKey);
        Assert.Null(size.Text);
        Assert.Null(styles.SelectedId);
        window.RibbonDefinition.Commit(size, "10,5");
        await SettleAsync();
        Assert.Equal("10,5", size.Text);
        Assert.Equal(10.5, editor.Session.GetFormatting().FontSize.Value);

        var colour = window.RibbonDefinition.Get<MatRibbonColorPicker>(WriteCommandIds.FontColor);
        window.RibbonDefinition.ChooseColor(colour, DocumentPalette.StandardColors[7]);
        await SettleAsync();
        Assert.Equal(DocumentPalette.StandardColors[7].TrimStart('#'), editor.Session.GetFormatting().Color.Value, ignoreCase: true);
        var highlight = window.RibbonDefinition.Get<MatRibbonColorPicker>(WriteCommandIds.Highlight);
        window.RibbonDefinition.Invoke(highlight); // the button applies its colour (yellow)
        await SettleAsync();
        Assert.Equal(HighlightColor.Yellow, editor.Session.GetFormatting().Highlight.Value);
    }

    [AvaloniaFact]
    public async Task Lists_take_Tab_for_the_level_and_the_toggles_show_them()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await window.Commands[WriteCommandIds.Bullets].ExecuteAsync();
        await TypeAsync(window, "Eins\nZwei");
        await PressAsync(window, PhysicalKey.Home);
        await PressAsync(window, PhysicalKey.Tab);
        var editor = EditorOf(window);
        Assert.Equal(1, editor.Session.GetFormatting().ListLevel.Value);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.Bullets).IsChecked);
        await PressAsync(window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal(0, editor.Session.GetFormatting().ListLevel.Value);

        await window.Commands[WriteCommandIds.Numbering].ExecuteAsync();
        await SettleAsync();
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.Numbering).IsChecked);
        Assert.False(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.Bullets).IsChecked);
    }

    [AvaloniaFact]
    public async Task Click_double_click_triple_click_shift_click_and_drag_select_with_the_mouse()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Eins zwei drei\nVier fünf");
        var editor = EditorOf(window);

        var zwei = PointOf(window, new DocumentPosition(0, 6));
        window.MouseDown(zwei, MouseButton.Left);
        window.MouseUp(zwei, MouseButton.Left);
        await SettleAsync();
        Assert.Equal(new DocumentPosition(0, 6), editor.Session.Selection.Active);
        Assert.True(editor.Session.Selection.IsCollapsed);

        window.MouseDown(zwei, MouseButton.Left);
        window.MouseUp(zwei, MouseButton.Left);
        await SettleAsync();
        Assert.Equal(new DocumentRange(new DocumentPosition(0, 5), new DocumentPosition(0, 10)), editor.Session.Selection.Range); // "zwei " (double click)

        await Task.Delay(600); // a new click, not a triple click
        var vier = PointOf(window, new DocumentPosition(1, 2));
        window.MouseDown(vier, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(vier, MouseButton.Left, RawInputModifiers.Shift);
        await SettleAsync();
        Assert.Equal(new DocumentPosition(1, 2), editor.Session.Selection.Active);
        Assert.False(editor.Session.Selection.IsCollapsed);

        await Task.Delay(600);
        var start = PointOf(window, new DocumentPosition(0, 0));
        var end = PointOf(window, new DocumentPosition(0, 4));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
        await SettleAsync();
        Assert.Equal(new DocumentRange(new DocumentPosition(0, 0), new DocumentPosition(0, 4)), editor.Session.Selection.Range);
        Assert.Equal("1 von 5 Wörtern", window.StatusBar.TextOf(WriteEditor.WordsItem));
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_keep_the_formatting_and_Nur_Text_einfuegen_does_not()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await PressAsync(window, PhysicalKey.B, CommandKey);
        await TypeAsync(window, "Fett");
        await PressAsync(window, PhysicalKey.B, CommandKey);
        await PressAsync(window, PhysicalKey.A, CommandKey);
        await PressAsync(window, PhysicalKey.C, CommandKey);
        await PressAsync(window, PhysicalKey.End, CommandKey);
        await TypeAsync(window, "\n");
        await PressAsync(window, PhysicalKey.V, CommandKey);

        var editor = EditorOf(window);
        Assert.Equal("Fett\nFett", TextOf(window));
        Assert.True(((Paragraph)editor.Document.Content.Blocks[1]).Inlines.OfType<TextRun>().All(run => run.Properties.Bold == true));

        // Nur Text: the text takes the formatting of the place it goes to (here: not bold).
        await TypeAsync(window, "\n");
        await PressAsync(window, PhysicalKey.B, CommandKey);
        await PressAsync(window, PhysicalKey.V, CommandKey | RawInputModifiers.Shift);
        Assert.Equal("Fett\nFett\nFett", TextOf(window));
        Assert.DoesNotContain(((Paragraph)editor.Document.Content.Blocks[2]).Inlines.OfType<TextRun>(), run => run.Properties.Bold == true);

        await PressAsync(window, PhysicalKey.ArrowLeft, WordKey | RawInputModifiers.Shift);
        await PressAsync(window, PhysicalKey.X, CommandKey);
        Assert.Equal("Fett\nFett\n", TextOf(window));
    }

    [AvaloniaFact]
    public async Task Only_the_pages_in_view_are_drawn_and_typing_redraws_only_its_page()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        for (var page = 0; page < 12; page++)
        {
            editor.Session.InsertText($"Seite {page + 1}");
            editor.Session.InsertPageBreak();
        }

        await PressAsync(window, PhysicalKey.Home, CommandKey);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await SettleAsync();
        Assert.Equal(13, editor.Layout.PageCount);
        Assert.InRange(editor.Surface.DrawnPages.Count, 1, 3);
        Assert.Equal(0, editor.Surface.DrawnPages[0]);

        await PressAsync(window, PhysicalKey.End, CommandKey); // the caret is kept in view
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await SettleAsync();
        Assert.Contains(12, editor.Surface.DrawnPages);
        Assert.DoesNotContain(0, editor.Surface.DrawnPages);

        var cached = editor.Surface.Pictures.Count;
        await TypeAsync(window, "x");
        Assert.True(editor.Surface.Pictures.Count >= cached - 1);
    }

    [AvaloniaFact]
    public async Task The_input_method_sees_the_paragraph_the_caret_and_its_place()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Erste Zeile\nGrüße");
        var editor = EditorOf(window);
        var client = editor.Surface.InputClient;

        Assert.Equal("Grüße", client.SurroundingText);
        Assert.Equal(5, client.Selection.Start);
        Assert.True(client.CursorRectangle.Height > 10);
        Assert.Same(editor.Surface, client.TextViewVisual);

        client.SetPreeditText("に");
        Assert.Equal("に", editor.Surface.CaretLayer.Preedit);
        client.SetPreeditText(null);
        window.KeyTextInput("日本"); // the input method commits
        await SettleAsync();
        Assert.Equal("Erste Zeile\nGrüße日本", TextOf(window));

        client.Selection = new Avalonia.Input.TextInput.TextSelection(0, 5);
        Assert.Equal(new DocumentRange(new DocumentPosition(1, 0), new DocumentPosition(1, 5)), editor.Session.Selection.Range);
    }

    [AvaloniaFact]
    public async Task Ctrl_wheel_zooms_and_Seitenbreite_fits_the_page()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var middle = editor.Surface.TranslatePoint(new Point(300, 300), window)!.Value;

        window.MouseWheel(middle, new Vector(0, 1), RawInputModifiers.Control | (OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : 0));
        await SettleAsync();
        Assert.Equal(110, editor.Zoom);
        Assert.Equal(110, window.StatusBar.View.Zoom);

        await window.Commands[WriteCommandIds.ZoomPageWidth].ExecuteAsync();
        await SettleAsync();
        var page = editor.Surface.PageRect(0);
        Assert.InRange(editor.Scroller.Viewport.Width - page.Width, 2 * DocumentSurface.SideMargin - 4, 2 * DocumentSurface.SideMargin + 8);

        await window.Commands[WriteCommandIds.ZoomWholePage].ExecuteAsync();
        await SettleAsync();
        Assert.True(editor.Surface.PageRect(0).Height <= editor.Scroller.Viewport.Height);
    }
}

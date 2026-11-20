using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Pictures in the real window: Einfügen › Bilder, Bildformat, selecting by a click, sizing by a handle.</summary>
public sealed class PictureUiTests
{
    private static async Task<OfficeWindow> WindowWithPictureAsync(TestData data)
    {
        var path = Path.Combine(data.DocumentsFolder, "Diagramm.png");
        await File.WriteAllBytesAsync(path, SampleDocument.ChartPng());
        data.Dialogs.Pictures.Enqueue(path);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Vor dem Bild ");
        await window.Commands.ExecuteAsync(WriteCommandIds.InsertPicture);
        await SettleAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task Bilder_inserts_the_chosen_picture_selected_and_Bildformat_sizes_it_in_proportion()
    {
        using var data = new TestData();
        var window = await WindowWithPictureAsync(data);
        var session = EditorOf(window).Session;

        var (position, image) = session.SelectedImage!.Value;
        Assert.Equal(new DocumentPosition(0, 13), position);
        Assert.Equal((450.0, 225.0), (Math.Round(image.Width), Math.Round(image.Height))); // 600 × 300 pixels at 96 dpi
        Assert.Contains(PictureCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.StartsWith("15.8", ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[WriteCommandIds.PictureWidth]).Value);

        await ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[WriteCommandIds.PictureWidth]).ApplyAsync("10");
        image = session.SelectedImage!.Value.Image;
        Assert.Equal(10 * 72 / 2.54, image.Width, 1);
        Assert.Equal(image.Width / 2, image.Height, 1);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.PictureAltText);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<AltTextDialog>().Single();
        dialog.Text.Text = "Balkendiagramm der Bestellungen";
        dialog.Confirm();
        await SettleAsync();
        Assert.Equal("Balkendiagramm der Bestellungen", session.SelectedImage!.Value.Image.Description);

        // The caret elsewhere: Bildformat goes away.
        await PressAsync(window, PhysicalKey.Home);
        await SettleAsync();
        Assert.DoesNotContain(PictureCommands.Context, window.RibbonDefinition.ActiveContexts);
    }

    [AvaloniaFact]
    public async Task A_click_selects_the_picture_and_its_corner_handle_sizes_it_in_proportion()
    {
        using var data = new TestData();
        var window = await WindowWithPictureAsync(data);
        var editor = EditorOf(window);
        var session = editor.Session;
        var (position, before) = session.SelectedImage!.Value;
        editor.Zoom = 50; // the whole picture in the window
        await PressAsync(window, PhysicalKey.End, TestData.CommandKey);
        Assert.Null(session.SelectedImage);

        var rect = editor.Layout.GetObjectRect(position)!.Value;
        var middle = editor.Surface.TranslatePoint(editor.Surface.ToSurface(rect.Page, rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2), 0, 0).TopLeft, window)!.Value;
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        await SettleAsync();
        Assert.Equal(position, session.SelectedImage!.Value.Position);

        // The bottom right handle, 40 px to the left: the picture shrinks, its proportions stay (the view may have scrolled to it).
        rect = editor.Layout.GetObjectRect(position)!.Value;
        var corner = editor.Surface.TranslatePoint(editor.Surface.ToSurface(rect.Page, rect.X + rect.Width, rect.Y + rect.Height, 0, 0).TopLeft, window)!.Value;
        var target = corner + new Vector(-40, 0);
        window.MouseMove(corner);
        window.MouseDown(corner, MouseButton.Left);
        Assert.True(editor.Surface.IsSizingImage, $"no handle at {corner}");
        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        window.MouseUp(target, MouseButton.Left);
        await SettleAsync();

        var after = session.SelectedImage!.Value.Image;
        Assert.Equal(before.Width - (40 / editor.Surface.Scale), after.Width, 1);
        Assert.Equal(after.Width / 2, after.Height, 1);
        session.Undo();
        Assert.Equal(before.Width, session.SelectedImage!.Value.Image.Width, 1);
    }

    [AvaloniaFact]
    public async Task Textumbruch_Quadrat_lets_the_picture_float_where_it_was_and_dragging_moves_it()
    {
        using var data = new TestData();
        var window = await WindowWithPictureAsync(data);
        var editor = EditorOf(window);
        var session = editor.Session;
        editor.Zoom = 50;
        await ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[WriteCommandIds.PictureWidth]).ApplyAsync("6");
        await SettleAsync();
        var (position, _) = session.SelectedImage!.Value;
        var inLine = editor.Layout.GetObjectRect(position)!.Value;

        await window.Commands.ExecuteAsync($"{WriteCommandIds.PictureWrap}.square");
        await SettleAsync();
        Assert.Equal(PictureWrap.Square, session.SelectedImageWrap);
        Assert.True(window.Commands[$"{WriteCommandIds.PictureWrap}.square"].IsChecked?.Invoke());
        var floating = editor.Layout.GetObjectRect(position)!.Value;
        Assert.Equal(inLine.X, floating.X, 1); // it stays where it was shown
        Assert.Equal(inLine.Y, floating.Y, 1);

        // Dragged 60 px to the right and 30 px down: it moves there and stays floating (written as wp:anchor).
        var start = editor.Surface.TranslatePoint(editor.Surface.ToSurface(floating.Page, floating.X + 20, floating.Y + 20, 0, 0).TopLeft, window)!.Value;
        var end = start + new Vector(60, 30);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await SettleAsync();
        var moved = editor.Layout.GetObjectRect(position)!.Value;
        Assert.Equal(floating.X + (60 / editor.Surface.Scale), moved.X, 0);
        Assert.Equal(floating.Y + (30 / editor.Surface.Scale), moved.Y, 0);
        Assert.NotNull(session.SelectedImage!.Value.Image.Anchor);

        var bytes = new MemoryStream();
        ThixWrite.Core.Formats.Docx.DocxWriter.Write(session.Document, bytes);
        var reread = ThixWrite.Core.Formats.Docx.DocxReader.Read(new MemoryStream(bytes.ToArray()));
        var picture = reread.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<ImageInline>().Single();
        Assert.Equal(session.SelectedImage!.Value.Image.Anchor, picture.Anchor);

        // Mit Text in Zeile: back into the line; one undo step each.
        await window.Commands.ExecuteAsync($"{WriteCommandIds.PictureWrap}.inline");
        Assert.Null(session.SelectedImage!.Value.Image.Anchor);
        session.Undo();
        session.Undo();
        Assert.Equal(floating.X, editor.Layout.GetObjectRect(position)!.Value.X, 1);
    }

    [AvaloniaFact]
    public async Task A_file_that_is_no_picture_is_refused_with_a_message()
    {
        using var data = new TestData();
        var path = data.File("Bild.png", "kein Bild");
        data.Dialogs.Pictures.Enqueue(path);
        var window = await NewWindowAsync();

        _ = window.Commands.ExecuteAsync(WriteCommandIds.InsertPicture);
        await SettleAsync();

        Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.DoesNotContain(EditorOf(window).Session.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines), inline => inline is ImageInline);
    }
}

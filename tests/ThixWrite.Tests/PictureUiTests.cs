using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
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

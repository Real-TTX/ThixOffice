using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Core.Commands;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Einfügen › Formen / Textfeld in the real window: drawn by dragging, text typed into it, Formformat.</summary>
public sealed class ShapeUiTests
{
    [AvaloniaFact]
    public async Task A_text_box_is_drawn_by_dragging_takes_typed_text_and_Formformat_fills_it()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Ein Absatz.");
        var editor = EditorOf(window);
        editor.Zoom = 50;
        await SettleAsync();

        await window.Commands.ExecuteAsync(WriteCommandIds.InsertTextBox);
        Assert.True(editor.IsDrawingShape);
        var start = editor.Surface.TranslatePoint(editor.Surface.ToSurface(0, 120, 200, 0, 0).TopLeft, window)!.Value;
        var end = start + new Vector(100, 50);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await SettleAsync();
        Assert.False(editor.IsDrawingShape);
        Assert.True(editor.IsEditingShapeText); // a new text box opens for typing

        await TypeAsync(window, "Im Kasten");
        await PressAsync(window, PhysicalKey.Escape);
        await SettleAsync();
        var box = editor.Session.Document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<ShapeInline>().Single();
        Assert.True(box.IsTextBox);
        Assert.Equal("Im Kasten", ((Paragraph)box.Shape.Text!.Blocks[0]).Text);
        Assert.Equal(100 / editor.Surface.Scale, box.Width, 0);
        Assert.NotNull(box.Anchor);

        // Selected by a click: Formformat shows; Fülleffekt fills it.
        var rect = editor.Layout.GetObjectRect(new Core.Model.DocumentPosition(0, 0))!.Value;
        var inside = editor.Surface.TranslatePoint(editor.Surface.ToSurface(rect.Page, rect.X + 5, rect.Y + 5, 0, 0).TopLeft, window)!.Value;
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        await SettleAsync();
        Assert.Contains(ShapeCommands.Context, window.RibbonDefinition.ActiveContexts);
        await ((OfficeValueCommand)window.Commands[WriteCommandIds.ShapeFill]).ApplyAsync("#FFC000");
        Assert.Equal("FFC000", editor.Session.SelectedShape!.Shape.Fill!.Color!.Rgb);
    }
}

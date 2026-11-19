using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Core.Commands;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using ThixShow.Editor;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>The slide editor through real input: text in placeholders, shapes drawn, moved, sized, nudged, duplicated; the ribbon follows.</summary>
public sealed class EditorTests
{
    [AvaloniaFact]
    public async Task A_click_on_the_title_placeholder_and_typing_writes_the_title()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var slide = editor.Session.CurrentSlide!;
        var title = slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        var (left, top, right, bottom) = SlideHitTesting.BoundsOf(editor.Session.Presentation, slide, title.Id);

        await ClickAsync(window, SlidePoint(window, (left + right) / 2, (top + bottom) / 2));
        Assert.Equal(new TextTarget(title.Id), editor.Session.Selection.Text?.Target);
        await TypeAsync(window, "Hallo Welt");
        await PressAsync(window, PhysicalKey.Enter);
        await TypeAsync(window, "Zweite Zeile");
        await PressAsync(window, PhysicalKey.Backspace);

        var text = ((AutoShape)editor.Session.CurrentSlide!.FindShape(title.Id)!).Text!;
        Assert.Equal("Hallo Welt\nZweite Zeil", text.PlainText);
        Assert.NotNull(editor.Canvas.CaretRect);

        await PressAsync(window, PhysicalKey.Escape);
        Assert.Null(editor.Session.Selection.Text);
        Assert.Equal([title.Id], editor.Session.Selection.Shapes.ToList());
    }

    [AvaloniaFact]
    public async Task A_shape_is_drawn_by_dragging_then_moved_with_snapping_sized_nudged_and_duplicated()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await window.Commands[ShowCommandIds.NewSlide].ExecuteAsync();
        await editor.RunAsync("Layout", () => editor.Session.ChangeLayout([editor.Session.Selection.CurrentSlide!.Value], editor.Session.Presentation.Masters[0].Layouts.First(layout => layout.Type == "blank").Id));
        await SettleAsync();

        // Formen › Rechteck, then a drag on the slide draws it.
        await ((OfficeValueCommand)window.Commands[ShowCommandIds.Shapes]).ApplyAsync("rect");
        await DragAsync(window, SlidePoint(window, 100, 100), SlidePoint(window, 300, 200));
        var shape = editor.Session.CurrentSlide!.Shapes.OfType<AutoShape>().Single(item => item.Geometry?.Preset == "rect");
        Assert.Equal(100, Units.EmuToPoints(shape.Transform!.X), 0);
        Assert.Equal(200, Units.EmuToPoints(shape.Transform.Width), 0);
        Assert.Equal([shape.Id], editor.Session.Selection.Shapes.ToList());

        // Moved so its centre comes near the slide's centre: the smart guide snaps it there.
        var size = editor.Session.Presentation.SlideSize;
        var (centerX, centerY) = (size.WidthPoints / 2, size.HeightPoints / 2);
        await DragAsync(window, SlidePoint(window, 200, 150), SlidePoint(window, centerX + 2, centerY - 1));
        var moved = editor.Session.CurrentSlide!.FindShape(shape.Id)!.Transform!;
        Assert.Equal(centerX, Units.EmuToPoints(moved.X + (moved.Width / 2)), 0);
        Assert.Equal(centerY, Units.EmuToPoints(moved.Y + (moved.Height / 2)), 0);

        // The right handle widens it, the left side stays.
        var right = editor.Canvas.HandlesOf(shape.Id).First(handle => handle.Kind == HandleKind.Right);
        await DragAsync(window, SlidePoint(window, right.X, right.Y), SlidePoint(window, right.X + 50, right.Y), RawInputModifiers.Alt);
        var sized = editor.Session.CurrentSlide!.FindShape(shape.Id)!.Transform!;
        Assert.Equal(moved.X, sized.X);
        Assert.Equal(250, Units.EmuToPoints(sized.Width), 0);

        // An arrow key nudges by 0.2 cm; Strg+D duplicates.
        await PressAsync(window, PhysicalKey.ArrowRight);
        Assert.Equal(sized.X + 72000, editor.Session.CurrentSlide!.FindShape(shape.Id)!.Transform!.X);
        await PressAsync(window, PhysicalKey.D, CommandKey);
        Assert.Equal(2, editor.Session.CurrentSlide!.Shapes.OfType<AutoShape>().Count(item => item.Geometry?.Preset == "rect"));

        // Undo goes back step by step.
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Single(editor.Session.CurrentSlide!.Shapes.OfType<AutoShape>(), item => item.Geometry?.Preset == "rect");
    }

    [AvaloniaFact]
    public async Task Bold_follows_the_selection_and_the_contextual_tabs_show_for_shapes_and_tables()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        var session = editor.Session;
        session.GoToSlide(session.Presentation.Slides[1].Id);
        var body = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder is { IsTitle: false });
        session.SelectText(new TextTarget(body.Id), new TextPosition(0, 0), new TextPosition(0, 8));
        await SettleAsync();
        Assert.False(window.Commands[ShowCommandIds.Bold].Checked);

        await PressAsync(window, PhysicalKey.F, CommandKey | RawInputModifiers.Shift);
        Assert.True(window.Commands[ShowCommandIds.Bold].Checked);
        Assert.Contains(ShowContextMenus.ShapeContext, window.RibbonDefinition.ActiveContexts);

        session.GoToSlide(session.Presentation.Slides[4].Id);
        var table = session.CurrentSlide!.Shapes.OfType<GraphicFrame>().First(frame => frame.Table is not null);
        session.SelectShapes([table.Id]);
        await SettleAsync();
        Assert.Contains(ShowContextMenus.TableContext, window.RibbonDefinition.ActiveContexts);
        Assert.DoesNotContain(ShowContextMenus.ShapeContext, window.RibbonDefinition.ActiveContexts);
        await window.Commands[ShowContextMenus.RowBelow].ExecuteAsync();
        Assert.Equal(5, ((GraphicFrame)session.CurrentSlide!.FindShape(table.Id)!).Table!.RowCount);
    }

    [AvaloniaFact]
    public async Task A_marquee_selects_the_shapes_it_encloses_and_entf_deletes_them()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var session = editor.Session;
        await editor.RunAsync("Leer", () => session.ChangeLayout([session.Selection.CurrentSlide!.Value], session.Presentation.Masters[0].Layouts.First(layout => layout.Type == "blank").Id));
        session.InsertShape("ellipse", new EmuRect(Units.PointsToEmu(50), Units.PointsToEmu(50), Units.PointsToEmu(60), Units.PointsToEmu(60)));
        session.InsertShape("rect", new EmuRect(Units.PointsToEmu(150), Units.PointsToEmu(60), Units.PointsToEmu(60), Units.PointsToEmu(40)));
        session.SelectShapes([]);
        await SettleAsync();

        await DragAsync(window, SlidePoint(window, 20, 20), SlidePoint(window, 260, 140));
        Assert.Equal(2, session.Selection.Shapes.Count);
        await PressAsync(window, PhysicalKey.Delete);
        Assert.Empty(session.CurrentSlide!.Shapes);
    }

    [AvaloniaFact]
    public async Task A_chart_is_inserted_its_data_edited_and_its_type_changed()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await window.Commands[ShowChartCommands.Insert + ".column"].ExecuteAsync();
        await SettleAsync();
        var selected = ShowChartCommands.SelectedChart(editor);
        Assert.NotNull(selected);
        Assert.Contains(ShowChartCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.Equal(3, selected.Value.Chart.AllSeries.Count());

        var table = ChartDataDialog.ToTable(selected.Value.Chart);
        Assert.StartsWith("\tDatenreihe 1\tDatenreihe 2\tDatenreihe 3", table, StringComparison.Ordinal);
        Assert.Contains("Kategorie 1\t4,3\t2,4\t2", table, StringComparison.Ordinal);
        var specs = ChartDataDialog.FromTable("\tUmsatz\nNord\t12,5\nSüd\t7")!;
        await editor.RunAsync("Daten", () => editor.Session.SetChart(selected.Value.Frame.Id, ThixOffice.Charts.ChartFactory.Create(ThixOffice.Charts.ChartTemplates.Find("column")!, specs)));
        Assert.Equal(12.5, ShowChartCommands.SelectedChart(editor)!.Value.Chart.AllSeries.Single().Values.Cache![0].Number);

        await window.Commands[ShowChartCommands.ChangeType + ".pie"].ExecuteAsync();
        Assert.Equal(ThixOffice.Charts.ChartType.Pie, ShowChartCommands.SelectedChart(editor)!.Value.Chart.Plots[0].Type);
    }

    [AvaloniaFact]
    public async Task Kopf_und_Fusszeile_puts_footer_and_numbers_on_every_slide_but_the_title()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        var opening = window.Commands[ShowCommandIds.InsertSlideNumber].ExecuteAsync();
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<ThixShow.Dialogs.HeaderFooterDialog>());
        Assert.True(dialog.SlideNumber.IsChecked);
        dialog.Footer.IsChecked = true;
        dialog.FooterText.Text = "Nordlicht Reisen";
        dialog.NotOnTitle.IsChecked = true;
        dialog.Confirm();
        await opening;
        await SettleAsync();

        var slides = editor.Session.Presentation.Slides;
        Assert.DoesNotContain(slides[0].Shapes, shape => shape.Placeholder?.IsFooterKind == true);
        Assert.All(slides.Skip(1).Where(slide => editor.Session.Presentation.LayoutOf(slide).Type != "title"), slide =>
            Assert.Equal(2, slide.Shapes.Count(shape => shape.Placeholder?.IsFooterKind == true)));
        Assert.Equal("Nordlicht Reisen", editor.Session.HeaderFooterOf(slides[1]).FooterText);
    }

    [AvaloniaFact]
    public async Task Zuschneiden_crops_a_picture_and_Nach_Absatz_animates_each_paragraph()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        var session = editor.Session;
        session.GoToSlide(session.Presentation.Slides[3].Id);
        var picture = session.CurrentSlide!.Shapes.OfType<PictureShape>().Single();
        session.SelectShapes([picture.Id]);
        var before = SlideHitTesting.BoundsOf(session.Presentation, session.CurrentSlide!, picture.Id);
        await SettleAsync();
        Assert.Contains(ShowContextMenus.PictureContext, window.RibbonDefinition.ActiveContexts);

        var cropping = window.Commands[ShowContextMenus.Crop].ExecuteAsync();
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<ThixShow.Dialogs.CropDialog>());
        dialog.Left.Value = 10;
        dialog.Confirm();
        await cropping;
        await SettleAsync();
        var cropped = (PictureShape)session.CurrentSlide!.FindShape(picture.Id)!;
        Assert.Equal(10000, cropped.Picture.Crop.Left);
        var after = SlideHitTesting.BoundsOf(session.Presentation, session.CurrentSlide!, picture.Id);
        Assert.True(after.Left > before.Left + 1 && Math.Abs(after.Right - before.Right) < 0.5, $"{before} → {after}");

        session.GoToSlide(session.Presentation.Slides[1].Id);
        var body = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder is { IsTitle: false });
        session.SelectShapes([body.Id]);
        await SettleAsync();
        await window.Commands[ShowCommandIds.Animation + ".byParagraph"].ExecuteAsync();
        var effects = session.CurrentSlide!.Timing!.MainSequence;
        Assert.Equal(5, effects.Count);
        Assert.All(effects, effect => Assert.NotNull(effect.ParagraphStart));
        Assert.True(window.Commands[ShowCommandIds.Animation + ".byParagraph"].Checked);
    }
}

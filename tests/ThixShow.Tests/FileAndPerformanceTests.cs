using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Core.Business;
using ThixShow.Core.Documents;
using ThixShow.Core.Model;
using ThixShow.Editor;
using Xunit.Abstractions;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>Files through the window (pptx, pptm and its question), and a deck of 200 slides staying fluid.</summary>
public sealed class FileAndPerformanceTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task The_sample_saves_as_pptx_and_comes_back_with_everything()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var path = Path.Combine(data.DocumentsFolder, "Rückblick.pptx");
        data.Dialogs.SaveTargets.Enqueue(path);

        Assert.True(await window.SaveAsAsync());

        var reopened = (ShowDocument)DocumentService.Open(path, TestData.App.CreateDocument);
        var presentation = reopened.Presentation;
        Assert.Equal(6, presentation.SlideCount);
        Assert.Contains("Begrüßung", presentation.Slides[0].NotesText);
        Assert.Contains(ShapeTree.All(presentation.Slides[2].Shapes).OfType<GraphicFrame>(), frame => frame.Kind == GraphicKind.Chart && frame.ChartPartUri is not null);
        Assert.Contains(ShapeTree.All(presentation.Slides[3].Shapes), shape => shape is PictureShape);
        Assert.Equal(TransitionKind.Push, presentation.Slides[2].Transition?.Kind);
        Assert.Single(presentation.Slides[1].Timing!.MainSequence);
    }

    [AvaloniaFact]
    public async Task Als_PDF_speichern_writes_one_page_per_slide_and_the_presentation_keeps_its_file()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var before = window.Document.FilePath;
        var pdf = Path.Combine(data.DocumentsFolder, "Vortrag");
        data.Dialogs.SaveTargets.Enqueue(pdf);
        Assert.True(await window.Commands.ExecuteAsync(ThixOffice.Core.Commands.CommonCommandIds.SaveAsPdf));
        var bytes = File.ReadAllBytes(pdf + ".pdf");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count);
        Assert.Equal(before, window.Document.FilePath);
    }

    [AvaloniaFact]
    public async Task Exportieren_writes_every_slide_as_a_picture_or_the_current_one_as_JPEG()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        window.ShowBackstage(ThixShow.Pages.ShowExportPage.PageId);
        await SettleAsync();
        var page = Assert.IsType<ThixShow.Pages.ShowExportPage>(window.Backstage.CurrentPage);

        data.Dialogs.SaveTargets.Enqueue(Path.Combine(data.DocumentsFolder, "Vortrag.png"));
        Assert.True(await page.ExportPicturesAsync());
        var files = Directory.GetFiles(data.DocumentsFolder, "Vortrag – Folie *.png");
        Assert.Equal(6, files.Length);
        using (var picture = SkiaSharp.SKBitmap.Decode(files.Order().First()))
        {
            Assert.Equal(1920, picture.Width);
            Assert.Equal(1080, picture.Height);
        }

        EditorOf(window).Session.GoToSlide(EditorOf(window).Session.Presentation.Slides[2].Id);
        page.Scope.SelectedIndex = 1;
        page.Format.SelectedIndex = 1;
        page.PictureSize.SelectedIndex = 0;
        data.Dialogs.SaveTargets.Enqueue(Path.Combine(data.DocumentsFolder, "Diagramm"));
        Assert.True(await page.ExportPicturesAsync());
        var jpeg = File.ReadAllBytes(Path.Combine(data.DocumentsFolder, "Diagramm.jpg"));
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Contains("Diagramm.jpg", page.Message.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Saving_a_presentation_with_macros_as_pptx_asks_first()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        editor.Document.Load(editor.Session.Presentation with { IsMacroEnabled = true });
        await SettleAsync();
        Assert.Contains("Makros", editor.OpeningWarning);

        var asking = editor.ConfirmSaveAsync(Path.Combine(data.DocumentsFolder, "Ohne.pptx"));
        await SettleAsync();
        var question = Assert.Single(window.OwnedWindows.OfType<ThixOffice.Desktop.Dialogs.QuestionDialog>());
        Assert.Contains(".pptm", question.Message, StringComparison.Ordinal);
        question.Buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.False(await asking);
        Assert.True(await editor.ConfirmSaveAsync(Path.Combine(data.DocumentsFolder, "Mit.pptm")));
    }

    [AvaloniaFact]
    public async Task Two_hundred_slides_stay_fluid()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var presentation = editor.Session.Presentation;
        var layout = presentation.Masters[0].Layouts.First(item => item.Type == "obj");
        var slides = Enumerable.Range(0, 200).Select(index =>
        {
            var slide = MasterFactory.CreateSlide(layout, (uint)(256 + index));
            return slide with
            {
                Shapes = [.. slide.Shapes.Select(shape => shape is AutoShape { Placeholder: { } placeholder } auto
                    ? auto with { Text = TextBody.FromText(placeholder.IsTitle ? $"Folie {index + 1}" : "Erster Punkt\nZweiter Punkt mit mehr Text\nDritter Punkt") }
                    : shape)],
            };
        }).ToList();
        editor.Document.Load(presentation with { Slides = slides });
        await SettleAsync();

        // Switching slides with the keyboard in the pane: the slide is drawn anew each time.
        editor.Thumbnails.Focus();
        var switching = Stopwatch.StartNew();
        for (var step = 0; step < 20; step++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            TestData.Settle();
        }

        switching.Stop();
        Assert.Equal(20, editor.CurrentIndex);

        // Scrolling the pane through all 200 thumbnails: each is recorded once, frames draw only what is visible.
        var scroller = (Avalonia.Controls.ScrollViewer)editor.Thumbnails.Parent!;
        var scrolling = Stopwatch.StartNew();
        var frames = 0;
        for (var y = 0.0; y < editor.Thumbnails.Bounds.Height; y += 400)
        {
            scroller.Offset = new Vector(0, y);
            TestData.Settle();
            frames++;
        }

        scrolling.Stop();
        var perSwitch = switching.Elapsed.TotalMilliseconds / 20;
        var perFrame = scrolling.Elapsed.TotalMilliseconds / frames;
        output.WriteLine($"slide switch {perSwitch:0.0} ms, pane scroll {perFrame:0.0} ms per step ({frames} steps), {editor.Pictures.Recorded} thumbnails recorded");
        Assert.True(perSwitch < 250, $"a slide switch takes {perSwitch:0} ms");
        Assert.True(perFrame < 400, $"a scroll step takes {perFrame:0} ms");
        Assert.True(editor.Pictures.Recorded <= 230, "thumbnails are recorded once each");
    }
}

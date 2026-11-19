using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using ThixOffice.Charts;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;
using ThixShow.Core.Documents;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using ThixShow.Core.Show;
using ThixShow.Editor;

namespace ThixShow;

/// <summary>
/// ThixShow's part of <c>--self-test</c> (the trimmed, published program): a new presentation from a design in a real window, slides
/// added through the commands, text typed into a placeholder as text input, a shape, a picture, a table and a chart, a transition
/// and an animation, saved as pptx, opened again and compared (Open XML SDK after trimming), a slide drawn with Skia, the show engine
/// run through a slide with its build step, and the slides exported as PDF.
/// </summary>
internal static class ShowSelfTest
{
    public static async Task<string> RunAsync(App app)
    {
        var window = app.NewDocumentWindow();
        await Settle();
        var editor = window.Editor as ShowEditor ?? throw new InvalidOperationException("the window has no ShowEditor");
        var session = editor.Session;
        await (window.Commands.Find(ShowCommandIds.Design) is ThixOffice.Core.Commands.OfficeValueCommand design ? design.ApplyAsync(BuiltInThemes.Natur.Id) : Task.FromResult(false));
        Ensure(session.Presentation.Masters[0].Theme.Name == BuiltInThemes.Natur.Theme.Name, "the design did not apply");

        // The title typed into the title placeholder as a user types it.
        var title = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        session.SelectText(new TextTarget(title.Id), new TextPosition(0, 0), new TextPosition(0, 0));
        editor.Canvas.Focus();
        Type(editor, "Selbsttest Ä–ß");
        Press(editor, Key.Escape);
        await Settle();
        Ensure(session.CurrentSlide!.FindShape(title.Id) is AutoShape { Text.PlainText: "Selbsttest Ä–ß" }, "typed text not in the title");

        await window.Commands[ShowCommandIds.NewSlide].ExecuteAsync();
        await Settle();
        Ensure(session.Presentation.SlideCount == 2, "Neue Folie did not add a slide");
        session.InsertShape("roundRect", new EmuRect(914400, 1828800, 2743200, 1371600));
        session.InsertPicture(ImageData.From(ShowPalette.SamplePicture(320, 200)));
        var table = session.InsertTable(3, 3);
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [new SeriesSpec(ChartText.Of("Werte"), ChartDataRef.LiteralText(["A", "B", "C"]), ChartDataRef.Literal([1, 3, 2]))]);
        var chartId = session.InsertChart(chart);
        session.SelectShapes([chartId]);
        session.AddAnimation(AnimationPreset.Fade);
        session.SetTransition([session.Selection.CurrentSlide!.Value], new SlideTransition(TransitionKind.Push, "u"));
        await Settle();

        var folder = Path.Combine(AppPaths.DataDirectory, "Dokumente");
        Directory.CreateDirectory(folder);
        var path = DocumentService.SaveAs(window.Document, Path.Combine(folder, "Selbsttest.pptx"));
        var reopened = DocumentService.Open(path, app.CreateDocument) as ShowDocument ?? throw new InvalidOperationException("not a ShowDocument");
        var read = reopened.Presentation;
        Ensure(read.SlideCount == 2 && read.Slides[1].Transition?.Kind == TransitionKind.Push && read.Slides[1].Timing?.MainSequence.Count == 1, "the pptx read back lost slides, transition or animation");
        var shapes = ShapeTree.All(read.Slides[1].Shapes).ToList();
        Ensure(shapes.OfType<PictureShape>().Any() && shapes.OfType<GraphicFrame>().Any(frame => frame.Table is not null), "the picture or the table is lost in the pptx");
        var readChart = shapes.OfType<GraphicFrame>().FirstOrDefault(frame => frame.Kind == GraphicKind.Chart);
        Ensure(readChart is not null && SharedChartRenderer.ChartOf(readChart, read) is { } back && back.AllSeries.Count() == 1, "the chart is lost in the pptx");
        Ensure(table > 0, "no table id");

        // A slide drawn as the editor and the show draw it: the picture, the chart's labels and the shapes leave ink.
        var page = SlideRenderer.Render(read, read.Slides[1], OfficeFonts.Skia);
        Ensure(page.Items.OfType<ThixOffice.Text.Drawing.VectorPathItem>().Any(item => item.Fill is ThixOffice.Text.Drawing.ImagePaint), "the picture is not drawn");
        Ensure(page.Items.OfType<ThixOffice.Text.Drawing.GlyphRunItem>().Any(run => run.Text == "Werte"), "the chart is not drawn");
        var png = SkiaPageRenderer.RenderPng(page, 640, OfficeFonts.Skia);
        using var bitmap = SKBitmap.Decode(png);
        var coloured = 0;
        for (var y = 0; y < bitmap.Height; y += 3)
        {
            for (var x = 0; x < bitmap.Width; x += 3)
            {
                var pixel = bitmap.GetPixel(x, y);
                coloured += Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)) < 200 ? 1 : 0;
            }
        }

        Ensure(coloured > 1000, $"the slide shows {coloured} inked pixels");

        // The show engine through the second slide: the transition, then the click that fades the chart in.
        var show = new SlideShow(read, OfficeFonts.Skia, new SlideShowOptions(StartSlide: 1));
        show.Start(TimeSpan.Zero);
        show.GoTo(1, TimeSpan.Zero);
        var during = show.Frame(TimeSpan.FromMilliseconds(100));
        show.Update(TimeSpan.FromSeconds(3));
        show.Next(TimeSpan.FromSeconds(3));
        var after = show.Frame(TimeSpan.FromSeconds(10)).ToDisplayList();
        Ensure(during.Layers.Count > 0 && after.Items.Count > 0 && show.StepsStarted == 1, "the show engine did not run the slide");

        // The slides as PDF.
        using var pdf = new MemoryStream();
        PdfExport.Export(PrintLayouts.Pages(read, OfficeFonts.Skia, PrintKind.Handouts, 4), pdf, new PdfMetadata("Selbsttest", Creator: "ThixShow"), OfficeFonts.Skia);
        var bytes = pdf.ToArray();
        Ensure(bytes.Length > 1000 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F', "the PDF is not a PDF");

        // The sample deck through pptx.
        var sample = SampleDeck.Create();
        var samplePath = Path.Combine(folder, "Beispiel.pptx");
        var sampleDocument = new ShowDocument("Beispiel", sample);
        DocumentService.SaveAs(sampleDocument, samplePath);
        var sampleRead = DocumentService.Open(samplePath, app.CreateDocument) as ShowDocument ?? throw new InvalidOperationException("not a ShowDocument");
        Ensure(sampleRead.Presentation.SlideCount == sample.SlideCount, "the sample deck lost slides");

        window.Close();
        await Settle();
        return $"pptx {new FileInfo(path).Length} bytes, sample {new FileInfo(samplePath).Length} bytes, PDF {bytes.Length} bytes, slide {coloured} inked px, {OfficeFonts.Families.Count} font families";
    }

    private static void Type(ShowEditor editor, string text) =>
        editor.Canvas.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = editor.Canvas });

    private static void Press(ShowEditor editor, Key key) =>
        editor.Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = editor.Canvas });

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle()
    {
        for (var round = 0; round < 5; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40);
        }
    }
}

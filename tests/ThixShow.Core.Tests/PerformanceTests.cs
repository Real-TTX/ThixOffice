using System.Diagnostics;
using ThixOffice.Text.Skia;
using ThixShow.Core.Layout;
using ThixShow.Core.Tests.Formats;
using Xunit.Abstractions;

namespace ThixShow.Core.Tests;

public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void A_deck_of_200_slides_loads_saves_and_renders_its_thumbnails_quickly()
    {
        var bytes = PptxTestSupport.Write(TestDecks.Deck(200));
        PptxTestSupport.Read(bytes); // warm up

        var watch = Stopwatch.StartNew();
        var presentation = PptxTestSupport.Read(bytes);
        var load = watch.Elapsed;

        watch.Restart();
        foreach (var slide in presentation.Slides)
        {
            SlideRenderer.RenderThumbnail(presentation, slide, TestDecks.Measurer, 192);
        }

        var thumbnails = watch.Elapsed;

        watch.Restart();
        var edited = presentation with { Slides = presentation.Slides.SetItem(100, presentation.Slides[100] with { Hidden = true }) };
        PptxTestSupport.Write(edited);
        var save = watch.Elapsed;

        using var measurer = new SkiaTextMeasurer();
        watch.Restart();
        foreach (var slide in presentation.Slides.Take(20))
        {
            SkiaVectorRenderer.RenderPng(SlideRenderer.RenderThumbnail(presentation, slide, measurer, 192), 192, measurer);
        }

        var pixels = watch.Elapsed;
        output.WriteLine($"200 slides ({bytes.Length / 1024} KB): load {load.TotalMilliseconds:0} ms, 200 thumbnail display lists {thumbnails.TotalMilliseconds:0} ms, save after one change {save.TotalMilliseconds:0} ms, 20 Skia thumbnails (192 px, real fonts) {pixels.TotalMilliseconds:0} ms");
        Assert.Equal(200, presentation.SlideCount);
        Assert.True(load < TimeSpan.FromSeconds(10), $"load {load}");
        Assert.True(thumbnails < TimeSpan.FromSeconds(10), $"thumbnails {thumbnails}");
        Assert.True(save < TimeSpan.FromSeconds(10), $"save {save}");
    }
}

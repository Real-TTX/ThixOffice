using SkiaSharp;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Layout;

public class SkiaRenderingTests
{
    private static SKBitmap Pixels(Presentation presentation, int width = 960)
    {
        var page = SlideRenderer.Render(presentation, presentation.Slides[0], TestDecks.Measurer);
        var png = SkiaVectorRenderer.RenderPng(page, width);
        return SKBitmap.Decode(png);
    }

    [Fact]
    public void Shapes_land_where_the_model_puts_them()
    {
        var presentation = TestDecks.Blank(
            TestDecks.Rectangle(10, 1270000, 1270000, 1270000, 1270000) with { Fill = new SolidFill(ColorSpec.Rgb("FF0000")) },
            TestDecks.Rectangle(11, 5080000, 1270000, 1270000, 1270000, preset: "ellipse") with { Fill = new SolidFill(ColorSpec.Rgb("0000FF")) });
        using var bitmap = Pixels(presentation);
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(150, 150));
        Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(90, 90));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(450, 150));
        Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(403, 103)); // outside the ellipse, inside its box
    }

    [Fact]
    public void Rotated_shapes_and_gradients_render()
    {
        var gradient = new GradientFill([new GradientStop(0, ColorSpec.Rgb("000000")), new GradientStop(100000, ColorSpec.Rgb("FFFFFF"))], LinearAngle: 0);
        var presentation = TestDecks.Blank(
            TestDecks.Rectangle(10, 1270000, 2540000, 2540000, 254000) with
            {
                Fill = new SolidFill(ColorSpec.Rgb("00FF00")),
                Transform = new Transform2D(1270000, 2540000, 2540000, 254000, Rotation: 90 * 60000),
            },
            TestDecks.Rectangle(11, 6350000, 0, 2540000, 1270000) with { Fill = gradient });
        using var bitmap = Pixels(presentation);
        // The 200 × 20 pt bar at (100, 200) turned upright: its centre (200, 210) stays, it now spans y 110…310.
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(200, 120));
        Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(260, 210));
        var left = bitmap.GetPixel(505, 50);
        var right = bitmap.GetPixel(695, 50);
        Assert.True(left.Red < 30 && right.Red > 225, $"{left} {right}");
    }

    [Fact]
    public void Pictures_are_drawn_cropped()
    {
        var picture = new PictureShape
        {
            Id = 10,
            Name = "Bild 9",
            Transform = new Transform2D(0, 0, 1270000, 1270000),
            Picture = new PictureFill(ImageData.From(TestImages.Png(8, 8, SKColors.Red)), RelativeRect.Zero, StretchMode.Full),
        };
        using var bitmap = Pixels(TestDecks.Blank(picture));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(50, 50));
    }

    [Fact]
    public void Slides_handouts_and_notes_export_to_pdf()
    {
        var presentation = TestDecks.Deck(4);
        foreach (var kind in new[] { PrintKind.Slides, PrintKind.Handouts, PrintKind.NotesPages })
        {
            using var stream = new MemoryStream();
            SkiaVectorRenderer.ExportPdf(PrintLayouts.Pages(presentation, TestDecks.Measurer, kind, 4), stream, new PdfMetadata("Test", Creator: "ThixShow"));
            var bytes = stream.ToArray();
            Assert.True(bytes.Length > 1000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    [Fact]
    public void Transform_clip_and_opacity_scopes_nest()
    {
        var items = new List<DisplayItem>
        {
            new TransformBeginItem(Matrix2D.Translation(10, 10)),
            new OpacityBeginItem(0.5),
            new ClipPathBeginItem(VectorPath.Rectangle(0, 0, 5, 5)),
            new VectorPathItem(VectorPath.Rectangle(0, 0, 20, 20), new SolidPaint(new RgbColor(0, 0, 0))),
            ClipEndItem.Instance,
            OpacityEndItem.Instance,
            TransformEndItem.Instance,
            new RectangleItem(0, 0, 2, 2, new RgbColor(255, 0, 0)),
        };
        var png = SkiaVectorRenderer.RenderPng(new PageDisplayList(0, 40, 40, items), 40);
        using var bitmap = SKBitmap.Decode(png);
        var inside = bitmap.GetPixel(12, 12);
        Assert.InRange(inside.Red, 120, 135);
        Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel(18, 18));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(1, 1));
    }
}

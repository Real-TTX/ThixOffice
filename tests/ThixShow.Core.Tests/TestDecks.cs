using System.Globalization;
using System.Text;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests;

/// <summary>Presentations and helpers for the tests.</summary>
internal static class TestDecks
{
    public static readonly ITextMeasurer Measurer = new FixedWidthTextMeasurer();

    /// <summary>A new presentation whose only slide uses the layout <paramref name="layoutType"/> and holds <paramref name="shapes"/> (after its placeholders).</summary>
    public static Presentation With(string layoutType, params SlideElement[] shapes)
    {
        var presentation = MasterFactory.CreatePresentation();
        var layout = presentation.Masters[0].Layouts.First(item => item.Type == layoutType);
        var slide = MasterFactory.CreateSlide(layout, 256);
        return presentation with { Slides = [slide with { Shapes = slide.Shapes.AddRange(shapes) }] };
    }

    /// <summary>A blank slide with <paramref name="shapes"/>.</summary>
    public static Presentation Blank(params SlideElement[] shapes) => With("blank", shapes);

    /// <summary>A presentation of <paramref name="count"/> slides (title and content with text, a shape and a table every few).</summary>
    public static Presentation Deck(int count)
    {
        var presentation = MasterFactory.CreatePresentation();
        var layout = presentation.Masters[0].Layouts.First(item => item.Type == "obj");
        var slides = new List<Slide>();
        for (var index = 0; index < count; index++)
        {
            var slide = MasterFactory.CreateSlide(layout, (uint)(256 + index));
            var shapes = slide.Shapes.Select(shape => shape is AutoShape auto && auto.Placeholder is { } placeholder
                ? auto with
                {
                    Text = placeholder.IsTitle
                        ? TextBody.FromText($"Folie {index + 1}")
                        : TextBody.FromText("Erster Punkt mit etwas Text\nZweiter Punkt\nDritter Punkt mit einer längeren Zeile, die umbricht"),
                }
                : shape).ToList();
            shapes.Add(Rectangle(10 + index, 8000000, 5000000, 2000000, 1000000, "accent2"));
            slides.Add(slide with { Shapes = shapes });
        }

        return presentation with { Slides = slides };
    }

    public static AutoShape Rectangle(int id, long x, long y, long width, long height, string? scheme = null, string preset = "rect") => new()
    {
        Id = id,
        Name = $"Rechteck {id}",
        Transform = new Transform2D(x, y, width, height),
        Geometry = ShapeGeometry.Of(preset),
        Fill = scheme is null ? null : new SolidFill(ColorSpec.Scheme(scheme)),
    };

    public static AutoShape TextBox(int id, string text, long x = 914400, long y = 914400, long width = 3657600, long height = 914400) => new()
    {
        Id = id,
        Name = $"Textfeld {id}",
        IsTextBox = true,
        Transform = new Transform2D(x, y, width, height),
        Geometry = ShapeGeometry.Rectangle,
        Fill = NoFill.Instance,
        Text = TextBody.FromText(text, new BodyProperties(Wrap: true)),
    };

    /// <summary>A stable text form of display items (arrays by content), for determinism checks and readable asserts.</summary>
    public static string Describe(IEnumerable<DisplayItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.AppendLine(item switch
            {
                GlyphRunItem run => FormattableString.Invariant($"text {run.X:0.###},{run.Y:0.###} '{run.Text}' {run.Font} {run.Color} [{string.Join(",", run.CharacterX.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"),
                VectorPathItem path => $"path {path.Path} fill={Paint(path.Fill)} stroke={(path.Stroke is null ? "-" : FormattableString.Invariant($"{Paint(path.Stroke.Paint)}/{path.Stroke.Width:0.##}"))} shadow={path.Shadow is not null}",
                TransformBeginItem transform => FormattableString.Invariant($"transform {transform.Matrix.M11:0.###} {transform.Matrix.M12:0.###} {transform.Matrix.M21:0.###} {transform.Matrix.M22:0.###} {transform.Matrix.OffsetX:0.###} {transform.Matrix.OffsetY:0.###}"),
                _ => item.ToString(),
            });
        }

        return builder.ToString();
    }

    private static string Paint(Paint? paint) => paint switch
    {
        null => "-",
        SolidPaint solid => solid.Color.ToString(),
        LinearGradientPaint linear => $"linear({string.Join(",", linear.Stops.Select(stop => stop.Color))})",
        RadialGradientPaint radial => $"radial({string.Join(",", radial.Stops.Select(stop => stop.Color))})",
        ImagePaint image => $"image({image.Key[..8]})",
        _ => paint.ToString(),
    };
}

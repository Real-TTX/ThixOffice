using ThixShow.Core.Model;

namespace ThixShow.Core.Show;

/// <summary>
/// Splits a shape for paragraph animations without moving any text: the same text body is laid out in every part, but paragraphs
/// that belong to another part are drawn transparent (text, bullets, highlight), so each part shows exactly its own lines in their
/// places. The shape's fill, outline and shadow belong to the base part only.
/// </summary>
public static class ParagraphMask
{
    private static readonly ColorSpec Transparent = ColorSpec.Rgb("000000").Alpha(0);

    /// <summary>
    /// The shape with only the paragraphs <paramref name="visible"/> keeps showing; <paramref name="withShape"/> false also drops its
    /// fill, outline and shadow (a paragraph layer).
    /// </summary>
    public static SlideElement Keep(SlideElement shape, Func<int, bool> visible, bool withShape)
    {
        if (shape is not AutoShape { Text: { } text } auto)
        {
            return shape;
        }

        var paragraphs = text.Paragraphs.Select((paragraph, index) => visible(index) ? paragraph : Hide(paragraph)).ToList();
        var masked = auto with { Text = text with { Paragraphs = paragraphs } };
        return withShape ? masked : masked with { Fill = NoFill.Instance, Outline = new Outline(Fill: NoFill.Instance), Effects = ShapeEffects.Empty };
    }

    private static TextParagraph Hide(TextParagraph paragraph) => paragraph with
    {
        Properties = paragraph.Properties with { BulletColor = Transparent, BulletColorFollowsText = false },
        Runs = [.. paragraph.Runs.Select(Hide)],
        EndProperties = paragraph.EndProperties is null ? null : Hide(paragraph.EndProperties),
    };

    private static TextRun Hide(TextRun run) => run switch
    {
        TextSpan span => span with { RunProperties = Hide(span.RunProperties) },
        TextField field => field with { RunProperties = Hide(field.RunProperties) },
        LineBreak lineBreak => lineBreak with { RunProperties = Hide(lineBreak.RunProperties) },
        _ => run,
    };

    private static RunProperties Hide(RunProperties run) => run with { Fill = new SolidFill(Transparent), Highlight = null };
}

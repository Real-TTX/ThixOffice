using ThixShow.Core.Model;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpWriter
{
    /// <summary>
    /// What an .odp file of <paramref name="presentation"/> would not hold (German labels through <see cref="UnsupportedContent.PlainMessage"/>):
    /// charts, SmartArt, OLE objects, media, kept XML and formulas, effects other than the shadow, pattern fills, click actions, effects
    /// other than appear/fade, transitions ODF has no name for, sections, macros, and what a pptx kept (comments, custom XML, fonts, ink).
    /// Empty: saving as .odp loses nothing ThixShow knows of.
    /// </summary>
    public static IReadOnlyList<UnsupportedContent> Losses(Presentation presentation)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string kind, int count = 1)
        {
            if (count > 0)
            {
                counts[kind] = counts.GetValueOrDefault(kind) + count;
            }
        }

        var bases = presentation.Slides.Cast<SlideBase>()
            .Concat(presentation.Masters)
            .Concat(presentation.Masters.SelectMany(master => master.Layouts));
        foreach (var slide in bases)
        {
            foreach (var shape in ShapeTree.All(slide.Shapes))
            {
                CountShape(shape, Count);
            }
        }

        foreach (var slide in presentation.Slides)
        {
            if (slide.Timing is { } timing)
            {
                Count("animation", timing.MainSequence.Count(effect => !Writer.IsWritable(effect)));
                Count("interactive", timing.InteractiveSequences.Count);
            }

            if (slide.Transition is { Kind: TransitionKind.Zoom or TransitionKind.Morph or TransitionKind.Other })
            {
                Count("transition");
            }
        }

        Count("sections", presentation.Sections.Count);
        if (presentation.IsMacroEnabled)
        {
            Count("macros");
        }

        foreach (var kept in presentation.UnsupportedContent.Where(content => content.Kind is "comments" or "customXml" or "fonts" or "ink"))
        {
            Count(kept.Kind, kept.Count);
        }

        return counts.Select(pair => new UnsupportedContent(pair.Key, pair.Value)).ToList();
    }

    private static void CountShape(SlideElement shape, Action<string, int> count)
    {
        switch (shape)
        {
            case GraphicFrame { Kind: GraphicKind.Chart }:
                count("chart", 1);
                break;
            case GraphicFrame { Kind: GraphicKind.Diagram }:
                count("diagram", 1);
                break;
            case GraphicFrame { Kind: GraphicKind.OleObject }:
                count("ole", 1);
                break;
            case GraphicFrame { Kind: GraphicKind.Other }:
            case PreservedElement:
                count("element", 1);
                break;
            case PictureShape { Media: not MediaKind.None }:
                count("media", 1);
                break;
        }

        if (shape.Click is not null || shape.Hover is not null)
        {
            count("action", 1);
        }

        var effects = shape switch
        {
            AutoShape auto => auto.Effects,
            ConnectorShape connector => connector.Effects,
            PictureShape picture => picture.Effects,
            GroupShape group => group.Effects,
            _ => null,
        };
        if (effects is { } list && (list.Others is not null || list.IsDag))
        {
            count("effects", 1);
        }

        if (shape is AutoShape { Fill: PatternFill } or GroupShape { Fill: PatternFill })
        {
            count("pattern", 1);
        }

        List<TextBody> text = shape switch
        {
            AutoShape { Text: { } body } => [body],
            GraphicFrame { Table: { } table } => [.. table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Text)],
            _ => [],
        };
        count("math", text.SelectMany(body => body.Paragraphs).SelectMany(paragraph => paragraph.Runs).Count(run => run is PreservedRun));
    }
}

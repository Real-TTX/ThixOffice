using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>A smart guide to draw while moving: vertical (at x) or horizontal (at y) from <see cref="From"/> to <see cref="To"/> (EMU).</summary>
public sealed record SnapGuide(bool Vertical, long Position, long From, long To);

/// <summary>The correction to apply to a move and the guides that explain it.</summary>
public sealed record SnapResult(long Dx, long Dy, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Snapping while moving or resizing shapes, UI-free (EMU): the moving box's left/centre/right and top/middle/bottom edges snap to
/// the same edges of the other shapes and of the slide (smart guides) within a threshold, otherwise to a grid. The editor turns
/// pixels into a threshold (e.g. 6 px at its zoom) and draws the guides.
/// </summary>
public static class Snapping
{
    /// <summary>The default grid (0.2 cm, PowerPoint's).</summary>
    public const long DefaultGrid = 72000;

    public static SnapResult Snap(Presentation presentation, Slide slide, EmuRect moving, IReadOnlyCollection<int> exclude, long threshold, long? grid = null, bool smartGuides = true)
    {
        var others = smartGuides ? Targets(presentation, slide, exclude) : [];
        var size = presentation.SlideSize;
        var slideBox = new EmuRect(0, 0, size.Width, size.Height);
        var (dx, verticalGuide) = Axis(moving, others, slideBox, threshold, vertical: true);
        var (dy, horizontalGuide) = Axis(moving, others, slideBox, threshold, vertical: false);
        if (verticalGuide is null && grid is { } spacingX && spacingX > 0)
        {
            dx = (long)(Math.Round(moving.X / (double)spacingX) * spacingX) - moving.X;
        }

        if (horizontalGuide is null && grid is { } spacingY && spacingY > 0)
        {
            dy = (long)(Math.Round(moving.Y / (double)spacingY) * spacingY) - moving.Y;
        }

        var guides = new List<SnapGuide>();
        if (verticalGuide is { } vertical)
        {
            guides.Add(vertical with { From = Math.Min(vertical.From, moving.Y + dy), To = Math.Max(vertical.To, moving.Bottom + dy) });
        }

        if (horizontalGuide is { } horizontal)
        {
            guides.Add(horizontal with { From = Math.Min(horizontal.From, moving.X + dx), To = Math.Max(horizontal.To, moving.Right + dx) });
        }

        return new SnapResult(dx, dy, guides);
    }

    /// <summary>Snaps one point (a resize handle) to the edges and centres of the other shapes and the slide.</summary>
    public static (long X, long Y, IReadOnlyList<SnapGuide> Guides) SnapPoint(Presentation presentation, Slide slide, long x, long y, IReadOnlyCollection<int> exclude, long threshold)
    {
        var result = Snap(presentation, slide, new EmuRect(x, y, 0, 0), exclude, threshold);
        return (x + result.Dx, y + result.Dy, result.Guides);
    }

    private static List<EmuRect> Targets(Presentation presentation, Slide slide, IReadOnlyCollection<int> exclude)
    {
        var context = SlideContext.For(presentation, slide);
        return slide.Shapes
            .Where(shape => !exclude.Contains(shape.Id) && !shape.Hidden)
            .Select(shape => ShapeResolver.Resolve(context, shape).Transform)
            .Where(transform => transform.Width > 0 || transform.Height > 0)
            .Select(EditingSession.RotatedBounds)
            .ToList();
    }

    private static (long Delta, SnapGuide? Guide) Axis(EmuRect moving, List<EmuRect> others, EmuRect slide, long threshold, bool vertical)
    {
        long Start(EmuRect box) => vertical ? box.X : box.Y;
        long Middle(EmuRect box) => vertical ? box.CenterX : box.CenterY;
        long End(EmuRect box) => vertical ? box.Right : box.Bottom;
        long CrossStart(EmuRect box) => vertical ? box.Y : box.X;
        long CrossEnd(EmuRect box) => vertical ? box.Bottom : box.Right;

        var edges = new[] { Start(moving), Middle(moving), End(moving) };
        long? best = null;
        SnapGuide? guide = null;
        void Consider(long target, EmuRect owner)
        {
            foreach (var edge in edges)
            {
                var delta = target - edge;
                if (Math.Abs(delta) > threshold || (best is { } current && Math.Abs(current) <= Math.Abs(delta)))
                {
                    continue;
                }

                best = delta;
                guide = new SnapGuide(vertical, target, CrossStart(owner), CrossEnd(owner));
            }
        }

        foreach (var box in others)
        {
            Consider(Start(box), box);
            Consider(Middle(box), box);
            Consider(End(box), box);
        }

        Consider(Start(slide), slide);
        Consider(Middle(slide), slide);
        Consider(End(slide), slide);
        return (best ?? 0, guide);
    }
}

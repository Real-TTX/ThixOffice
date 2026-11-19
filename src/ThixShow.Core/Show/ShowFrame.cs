using ThixOffice.Text.Drawing;

namespace ThixShow.Core.Show;

/// <summary>A clip rectangle in a layer's own (slide) coordinates, points.</summary>
public readonly record struct ClipRect(double X, double Y, double Width, double Height);

/// <summary>
/// One drawable layer of a show frame: a display list (slide points) drawn with an opacity, through a transform (applied after the
/// items' own coordinates) and clipped to a rectangle in the items' coordinates. The UI draws layers in order; static content shares
/// one layer, every animated shape has its own.
/// </summary>
public sealed record ShowLayer(IReadOnlyList<DisplayItem> Items, double Opacity, Matrix2D Transform, ClipRect? Clip = null)
{
    public static ShowLayer Static(IReadOnlyList<DisplayItem> items) => new(items, 1, Matrix2D.Identity);
}

/// <summary>
/// What the screen shows at one moment of a slide show: the layers (black behind them), the slide (index into the presentation),
/// whether a transition is running, whether it is the end screen.
/// </summary>
public sealed record ShowFrame(double Width, double Height, IReadOnlyList<ShowLayer> Layers, int SlideIndex, bool InTransition, bool IsEnd)
{
    /// <summary>The frame as one display list (black background, each layer in transform/opacity/clip scopes) – drawn as it is.</summary>
    public PageDisplayList ToDisplayList()
    {
        var items = new List<DisplayItem> { new RectangleItem(0, 0, Width, Height, RgbColor.Black) };
        foreach (var layer in Layers)
        {
            if (layer.Opacity <= 0)
            {
                continue;
            }

            var transformed = !layer.Transform.IsIdentity;
            if (transformed)
            {
                items.Add(new TransformBeginItem(layer.Transform));
            }

            if (layer.Opacity < 1)
            {
                items.Add(new OpacityBeginItem(layer.Opacity));
            }

            if (layer.Clip is { } clip)
            {
                items.Add(new ClipBeginItem(clip.X, clip.Y, clip.Width, clip.Height));
            }

            items.AddRange(layer.Items);
            if (layer.Clip is not null)
            {
                items.Add(ClipEndItem.Instance);
            }

            if (layer.Opacity < 1)
            {
                items.Add(OpacityEndItem.Instance);
            }

            if (transformed)
            {
                items.Add(TransformEndItem.Instance);
            }
        }

        return new PageDisplayList(SlideIndex, Width, Height, items);
    }
}

/// <summary>What the presenter sees besides the slide: where the show is, the next slide, the notes, the time.</summary>
public sealed record PresenterView(
    int SlideNumber,
    int Position,
    int Count,
    int? NextSlideIndex,
    bool NextIsBuildStep,
    string Notes,
    TimeSpan Elapsed,
    DateTime Clock,
    int RemainingClicks);

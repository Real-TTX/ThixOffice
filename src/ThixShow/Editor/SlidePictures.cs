using SkiaSharp;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The slides of one window as recorded Skia pictures in points (vector: crisp at any zoom and screen scaling), made when first
/// drawn and kept per slide until it changes: thumbnails and the sorter without placeholder prompts, the editor's slide with them.
/// Pictures are never disposed (the render thread may still draw one the UI thread let go).
/// </summary>
internal sealed class SlidePictures
{
    private readonly Dictionary<uint, (Slide Slide, int Version, SKPicture Picture)> _thumbnails = [];
    private (Slide Slide, int Version, SKPicture Picture)? _editor;
    private int _version;

    /// <summary>How many thumbnails were recorded (tests and the performance numbers).</summary>
    public int Recorded { get; private set; }

    /// <summary>A slide as the thumbnail pane, the sorter and the show's navigator show it.</summary>
    public SKPicture Thumbnail(Presentation presentation, Slide slide)
    {
        if (_thumbnails.TryGetValue(slide.Id, out var entry) && ReferenceEquals(entry.Slide, slide) && entry.Version == _version)
        {
            return entry.Picture;
        }

        var picture = Record(SlideRenderer.Render(presentation, slide, OfficeFonts.Cached, new RenderOptions(Now: DateTime.Now)));
        _thumbnails[slide.Id] = (slide, _version, picture);
        Recorded++;
        return picture;
    }

    /// <summary>The slide of the editor (empty placeholders show their prompts and dashed frames).</summary>
    public SKPicture Editor(Presentation presentation, Slide slide)
    {
        if (_editor is { } entry && ReferenceEquals(entry.Slide, slide) && entry.Version == _version)
        {
            return entry.Picture;
        }

        var picture = Record(SlideRenderer.Render(presentation, slide, OfficeFonts.Cached, new RenderOptions(ShowPlaceholderPrompts: true, Now: DateTime.Now)));
        _editor = (slide, _version, picture);
        return picture;
    }

    /// <summary>
    /// The session changed the presentation: a changed slide is a new instance (its picture is made again by itself); the design,
    /// the slide size or the order (slide numbers in footers) make every picture again.
    /// </summary>
    public void OnPresentationChanged(PresentationChangedEventArgs e)
    {
        if (e.DesignChanged || e.SlideListChanged)
        {
            Clear();
        }
    }

    /// <summary>Forgets every picture (another presentation came into the window, the design changed).</summary>
    public void Clear()
    {
        _version++;
        _thumbnails.Clear();
        _editor = null;
    }

    /// <summary>Records a display list (points) into a picture.</summary>
    public static SKPicture Record(PageDisplayList page)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create((float)page.Width, (float)page.Height));
        SkiaPageRenderer.Draw(canvas, page, OfficeFonts.Skia);
        return recorder.EndRecording();
    }
}

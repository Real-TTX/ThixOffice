using SkiaSharp;
using ThixOffice.Text.Skia;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The pages as Skia pictures (vector recordings in points, drawn at any zoom and resolution, so text stays crisp): one per
/// page, recorded on first use from the layout's display list (plus the formatting marks when they show) and kept until the
/// layout reports that the page changed. Typing re-records only the page it happens on (and the ones after it when the
/// pagination moves). Pictures are immutable: the render thread may still draw one after it left the cache.
/// </summary>
internal sealed class PagePictures
{
    private readonly Dictionary<int, SKPicture> _pictures = [];
    private bool _showMarks;

    /// <summary>Whether the pictures include the formatting marks.</summary>
    public bool ShowMarks
    {
        get => _showMarks;
        set
        {
            if (_showMarks != value)
            {
                _showMarks = value;
                _pictures.Clear();
            }
        }
    }

    /// <summary>How many pictures are cached (tests).</summary>
    public int Count => _pictures.Count;

    /// <summary>The picture of page <paramref name="page"/> (recorded when it is not cached).</summary>
    public SKPicture Get(DocumentLayout layout, int page)
    {
        if (_pictures.TryGetValue(page, out var picture))
        {
            return picture;
        }

        var pageLayout = layout.Pages[page];
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create((float)pageLayout.Width, (float)pageLayout.Height));
        SkiaPageRenderer.Draw(canvas, layout.GetDisplayList(page), WriteFonts.Measurer);
        if (_showMarks)
        {
            FormattingMarks.Draw(canvas, pageLayout);
        }

        picture = recorder.EndRecording();
        _pictures[page] = picture;
        return picture;
    }

    /// <summary>Pages from <paramref name="firstPage"/> on changed.</summary>
    public void InvalidateFrom(int firstPage)
    {
        foreach (var page in _pictures.Keys.Where(page => page >= firstPage).ToList())
        {
            _pictures.Remove(page);
        }
    }

    public void Clear() => _pictures.Clear();
}

/// <summary>The formatting marks of a page (¶ at paragraph ends, · for spaces, → for tabs, ↵ for line breaks, the page break line).</summary>
internal static class FormattingMarks
{
    public static void Draw(SKCanvas canvas, PageLayout page)
    {
        using var paint = new SKPaint { Color = DocumentPalette.Marks, IsAntialias = true };
        using var line = new SKPaint { Color = DocumentPalette.Marks, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, PathEffect = SKPathEffect.CreateDash([1.5f, 1.5f], 0) };
        foreach (var placed in page.Lines)
        {
            if (placed.Paragraph.IsPreserved || placed.Paragraph.Block is not Paragraph paragraph)
            {
                continue;
            }

            var layout = placed.Line;
            var size = (float)Math.Max(6, (layout.Ascent + layout.Descent) * 0.72);
            using var font = new SKFont(SKTypeface.Default, size);
            var baseline = (float)(placed.Top + layout.Baseline);
            var text = paragraph.Text;
            for (var offset = layout.Start; offset < Math.Min(layout.End, text.Length); offset++)
            {
                var character = text[offset];
                if (character is not (' ' or '\t' or SpecialCharacters.NonBreakingSpace))
                {
                    continue;
                }

                var x1 = placed.Left + layout.XOf(offset);
                var x2 = placed.Left + layout.XOf(offset + 1);
                var mark = character == '\t' ? "→" : character == ' ' ? "·" : "°";
                var width = Measure(font, mark);
                canvas.DrawText(mark, (float)((x1 + x2) / 2) - (width / 2), baseline, font, paint);
            }

            var end = (float)(placed.Left + layout.StopX[^1]);
            switch (layout.Ending)
            {
                case LineEnding.Paragraph:
                    canvas.DrawText("¶", end + 1, baseline, font, paint);
                    break;
                case LineEnding.LineBreak:
                    canvas.DrawText("↵", end + 1, baseline, font, paint);
                    break;
                case LineEnding.PageBreak:
                    DrawPageBreak(canvas, placed, end, baseline, font, paint, line);
                    break;
            }
        }
    }

    private static void DrawPageBreak(SKCanvas canvas, PlacedLine placed, float x, float baseline, SKFont font, SKPaint paint, SKPaint line)
    {
        const string label = "Seitenumbruch";
        var right = (float)(placed.Left + placed.Paragraph.Width);
        var width = Measure(font, label);
        var middle = (x + right) / 2;
        var y = baseline - (font.Size / 3);
        canvas.DrawLine(x + 2, y, middle - (width / 2) - 4, y, line);
        canvas.DrawText(label, middle - (width / 2), baseline, font, paint);
        canvas.DrawLine(middle + (width / 2) + 4, y, right, y, line);
    }

    private static float Measure(SKFont font, string text)
    {
        using var paint = new SKPaint { Typeface = font.Typeface, TextSize = font.Size };
        return paint.MeasureText(text);
    }
}

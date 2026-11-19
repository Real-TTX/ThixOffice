using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ThixOffice.Desktop;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Ansicht › Lineal: the horizontal ruler over the pages, like Word's – the text area of the caret's page between its margins (in
/// cm), the indents of the caret's paragraph (first line ▽ above, hanging △ and left ▭ below, right △) and its tab stops (└ ┴ ┘).
/// Dragging a marker changes the indents or moves a tab stop (dragged off the ruler: removed), a click on the ruler sets a left tab
/// stop – all through the editing session (one undo step each).
/// </summary>
public sealed class Ruler : Control
{
    public const double RulerHeight = 24;
    private const double PointsPerCm = 72 / 2.54;
    private const double Grip = 5;

    private readonly WriteEditor _editor;
    private Marker? _drag;
    private double _dragX;

    public Ruler(WriteEditor editor)
    {
        _editor = editor;
        Height = RulerHeight;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>What can be dragged: an indent or a tab stop (its index among the paragraph's stops).</summary>
    public enum MarkerKind
    {
        FirstLine,
        Hanging,
        Left,
        Right,
        Tab,
    }

    public sealed record Marker(MarkerKind Kind, double X, int Tab = -1);

    /// <summary>The page geometry the ruler follows: the x of the page's left edge in this control and the scale (DIP per point).</summary>
    public (double PageX, double Scale, SectionProperties Setup) Geometry
    {
        get
        {
            var surface = _editor.Surface;
            var page = Math.Clamp(surface.CaretPage, 0, Math.Max(0, _editor.Layout.PageCount - 1));
            var rect = surface.PageRect(page);
            var origin = surface.TranslatePoint(rect.TopLeft, this) ?? rect.TopLeft;
            return (origin.X, surface.Scale, _editor.Layout.Pages[page].Section.Properties);
        }
    }

    /// <summary>The markers of the caret's paragraph, x in this control (DIP).</summary>
    public IReadOnlyList<Marker> Markers()
    {
        var (pageX, scale, setup) = Geometry;
        var session = _editor.Document.Session;
        if (session.Document.ParagraphAt(session.Selection.Active.Block) is not { } paragraph)
        {
            return [];
        }

        var format = session.Resolver.ResolveParagraph(paragraph);
        var textLeft = pageX + ((setup.MarginLeft + setup.Gutter) * scale);
        var textRight = pageX + ((setup.PageWidth - setup.MarginRight) * scale);
        var markers = new List<Marker>
        {
            new(MarkerKind.FirstLine, textLeft + ((format.IndentLeft + format.FirstLineIndent) * scale)),
            new(MarkerKind.Hanging, textLeft + (format.IndentLeft * scale)),
            new(MarkerKind.Left, textLeft + (format.IndentLeft * scale)),
            new(MarkerKind.Right, textRight - (format.IndentRight * scale)),
        };
        for (var index = 0; index < format.Tabs.Count; index++)
        {
            markers.Add(new Marker(MarkerKind.Tab, textLeft + (format.Tabs[index].Position * scale), index));
        }

        return markers;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Ui.Brush("Mat.Bg"), new Rect(Bounds.Size));
        if (_editor.Layout.PageCount == 0)
        {
            return;
        }

        var (pageX, scale, setup) = Geometry;
        var left = pageX + ((setup.MarginLeft + setup.Gutter) * scale);
        var right = pageX + ((setup.PageWidth - setup.MarginRight) * scale);
        var band = new Rect(pageX, 3, setup.PageWidth * scale, RulerHeight - 6);
        context.FillRectangle(Ui.Brush("Mat.NavBg"), band);
        context.FillRectangle(Ui.Brush("Mat.Surface"), new Rect(left, band.Y, Math.Max(0, right - left), band.Height));
        context.DrawRectangle(null, new Pen(Ui.Brush("Mat.Border"), 1), band);

        // Ticks every quarter centimetre from the left margin, numbers every centimetre.
        var muted = Ui.Brush("Mat.TextMuted");
        var pen = new Pen(muted, 1);
        var step = PointsPerCm / 4 * scale;
        var centimetre = step * 4;
        var numberEvery = centimetre >= 22 ? 1 : centimetre >= 11 ? 2 : 5; // numbers that do not run into each other at a small zoom
        for (var index = -(int)((left - pageX) / step); left + (index * step) <= pageX + (setup.PageWidth * scale); index++)
        {
            var x = Math.Round(left + (index * step)) + 0.5;
            if (index == 0)
            {
                continue;
            }

            if (index % 4 == 0)
            {
                if (index / 4 % numberEvery != 0)
                {
                    context.DrawLine(pen, new Point(x, (RulerHeight / 2) - 3), new Point(x, (RulerHeight / 2) + 3));
                    continue;
                }

                var number = new FormattedText(Math.Abs(index / 4).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, muted);
                context.DrawText(number, new Point(x - (number.Width / 2), (RulerHeight - number.Height) / 2));
                continue;
            }

            if (step < 3 && index % 2 != 0)
            {
                continue;
            }

            var length = index % 2 == 0 ? 5 : 2;
            context.DrawLine(pen, new Point(x, (RulerHeight / 2) - (length / 2.0)), new Point(x, (RulerHeight / 2) + (length / 2.0)));
        }

        var marker = Ui.Brush("Mat.TextMuted");
        var outline = new Pen(Ui.Brush("Mat.Text"), 0.75);
        foreach (var item in Markers())
        {
            var x = item.Kind == MarkerKind.Tab ? item.X : item.X;
            switch (item.Kind)
            {
                case MarkerKind.FirstLine:
                    context.DrawGeometry(marker, outline, Triangle(x, 3, down: true));
                    break;
                case MarkerKind.Hanging:
                    context.DrawGeometry(marker, outline, Triangle(x, RulerHeight - 9, down: false));
                    break;
                case MarkerKind.Left:
                    context.DrawRectangle(marker, outline, new Rect(x - 4, RulerHeight - 4, 8, 3));
                    break;
                case MarkerKind.Right:
                    context.DrawGeometry(marker, outline, Triangle(x, RulerHeight - 9, down: false));
                    break;
                case MarkerKind.Tab:
                    var tab = new Pen(Ui.Brush("Mat.Text"), 1.5);
                    context.DrawLine(tab, new Point(x, RulerHeight - 10), new Point(x, RulerHeight - 4));
                    context.DrawLine(tab, new Point(x, RulerHeight - 4), new Point(x + 5, RulerHeight - 4));
                    break;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        _drag = MarkerAt(point);
        _dragX = point.X;
        if (_drag is null)
        {
            AddTab(point.X);
        }
        else
        {
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        e.Pointer.Capture(null);
        var point = e.GetPosition(this);
        var (_, scale, _) = Geometry;
        var delta = (point.X - _dragX) / scale;
        var outside = point.Y < -RulerHeight || point.Y > RulerHeight * 2;
        Move(drag, delta, outside);
    }

    /// <summary>The marker within a few pixels of a point (tabs before indents, the lower markers below the middle).</summary>
    public Marker? MarkerAt(Point point)
    {
        var markers = Markers();
        var lower = point.Y > RulerHeight / 2;
        return markers.FirstOrDefault(item => item.Kind == MarkerKind.Tab && Math.Abs(item.X - point.X) <= Grip)
               ?? markers.FirstOrDefault(item => Math.Abs(item.X - point.X) <= Grip && item.Kind switch
               {
                   MarkerKind.FirstLine => !lower,
                   MarkerKind.Left => point.Y > RulerHeight - 6,
                   _ => lower,
               });
    }

    /// <summary>Applies a drag of <paramref name="delta"/> points (a tab stop dragged off the ruler is removed).</summary>
    public void Move(Marker marker, double delta, bool remove = false)
    {
        var session = _editor.Document.Session;
        if (Math.Abs(delta) < 0.25 && !remove)
        {
            return;
        }

        switch (marker.Kind)
        {
            case MarkerKind.FirstLine:
                session.FormatParagraphs("Einzug", props => props with { FirstLineIndent = Round(Effective().FirstLineIndent + delta) });
                break;
            case MarkerKind.Hanging:
                // The text moves, the first line stays where it is.
                var format = Effective();
                session.FormatParagraphs("Einzug", props => props with
                {
                    IndentLeft = Math.Max(0, Round(format.IndentLeft + delta)),
                    FirstLineIndent = Round(format.FirstLineIndent - delta),
                });
                break;
            case MarkerKind.Left:
                session.FormatParagraphs("Einzug", props => props with { IndentLeft = Math.Max(0, Round(Effective().IndentLeft + delta)) });
                break;
            case MarkerKind.Right:
                session.FormatParagraphs("Einzug", props => props with { IndentRight = Math.Max(0, Round(Effective().IndentRight - delta)) });
                break;
            case MarkerKind.Tab:
                var tabs = Effective().Tabs.ToList();
                if (marker.Tab < 0 || marker.Tab >= tabs.Count)
                {
                    return;
                }

                var stop = tabs[marker.Tab];
                tabs.RemoveAt(marker.Tab);
                if (!remove)
                {
                    tabs.Add(stop with { Position = Math.Max(0, Round(stop.Position + delta)) });
                }

                SetTabs(tabs);
                break;
        }

        InvalidateVisual();
    }

    /// <summary>A click on the ruler: a left tab stop there (in the text area).</summary>
    public void AddTab(double x)
    {
        var (pageX, scale, setup) = Geometry;
        var position = ((x - pageX) / scale) - setup.MarginLeft - setup.Gutter;
        if (position <= 0 || position >= setup.ContentWidth)
        {
            return;
        }

        var tabs = Effective().Tabs.Where(stop => Math.Abs(stop.Position - position) > 1).ToList();
        tabs.Add(new TabStop(Round(position)));
        SetTabs(tabs);
        InvalidateVisual();
    }

    private void SetTabs(List<TabStop> tabs)
    {
        tabs.Sort((left, right) => left.Position.CompareTo(right.Position));
        var array = new EquatableArray<TabStop>(tabs);
        _editor.Document.Session.FormatParagraphs("Tabstopps", props => props with { Tabs = array });
    }

    private Core.Formatting.EffectiveParagraphFormat Effective()
    {
        var session = _editor.Document.Session;
        var paragraph = session.Document.ParagraphAt(session.Selection.Active.Block) ?? Paragraph.Empty();
        return session.Resolver.ResolveParagraph(paragraph);
    }

    /// <summary>Positions snap to a sixteenth of a centimetre, like Word's ruler.</summary>
    private static double Round(double points) => Math.Round(points / (PointsPerCm / 16)) * (PointsPerCm / 16);

    private static StreamGeometry Triangle(double x, double top, bool down)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (down)
        {
            context.BeginFigure(new Point(x - 4.5, top), true);
            context.LineTo(new Point(x + 4.5, top));
            context.LineTo(new Point(x, top + 6));
        }
        else
        {
            context.BeginFigure(new Point(x, top), true);
            context.LineTo(new Point(x + 4.5, top + 6));
            context.LineTo(new Point(x - 4.5, top + 6));
        }

        context.EndFigure(true);
        return geometry;
    }
}

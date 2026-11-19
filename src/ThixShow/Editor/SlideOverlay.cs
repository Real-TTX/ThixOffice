using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixShow.Core.Layout;

namespace ThixShow.Editor;

/// <summary>
/// The selection layer above the slide (chrome, Mat tokens): frames and handles of the selected shapes (eight resize handles, the
/// rotation handle, the geometry's adjust handles, the ends of lines), the frame of an entered group, the dashed outlines of a move,
/// size or rotation in progress, the smart guides, the marquee, and while text is edited its frame, selection and blinking caret.
/// Drawing it never redraws the slide.
/// </summary>
internal sealed class SlideOverlay : Control
{
    /// <summary>The distance of the rotation handle above the top edge (device-independent pixels).</summary>
    public const double RotateDistance = 22;

    private readonly SlideCanvas _canvas;
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private bool _caretOn = true;

    public SlideOverlay(SlideCanvas canvas)
    {
        _canvas = canvas;
        IsHitTestVisible = false;
        ClipToBounds = true;
        _blink.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            InvalidateVisual();
        };
    }

    /// <summary>Starts the caret visible (after typing or a move) and keeps it blinking while text is edited.</summary>
    public void ResetBlink()
    {
        _caretOn = true;
        if (_canvas.Session.Selection.Text is not null && _canvas.IsFocused)
        {
            _blink.Stop();
            _blink.Start();
        }
        else
        {
            _blink.Stop();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_canvas.Slide is null)
        {
            return;
        }

        var session = _canvas.Session;
        var drag = _canvas.Drag;
        if (session.Selection.EnteredGroup is { } group)
        {
            SelectionHandles.DrawFrame(context, _canvas.CornersOf(group), dashed: true);
        }

        if (session.Selection.Text is { } text && _canvas.EditedText is { } layout)
        {
            DrawText(context, text, layout);
        }
        else if (drag is null || drag.Kind is DragKind.TextSelect or DragKind.Marquee)
        {
            foreach (var id in _canvas.SelectedIds)
            {
                DrawSelected(context, id, handles: _canvas.SelectedIds.Count <= 24);
            }
        }

        if (drag is not null)
        {
            DrawDrag(context, drag);
        }
    }

    /// <summary>A selected shape: its frame, the eight handles (or the ends of a line), the rotation handle and the adjust handles.</summary>
    private void DrawSelected(DrawingContext context, int id, bool handles)
    {
        var corners = _canvas.CornersOf(id);
        if (corners.Count < 4)
        {
            return;
        }

        var all = _canvas.HandlesOf(id);
        var isLine = all.Any(handle => handle.Kind is HandleKind.LineStart or HandleKind.LineEnd);
        if (!isLine)
        {
            SelectionHandles.DrawFrame(context, corners);
        }

        if (!handles)
        {
            return;
        }

        var points = all.Where(handle => handle.Kind is not (HandleKind.Rotate or HandleKind.Adjust or HandleKind.LineStart or HandleKind.LineEnd))
            .Select(handle => _canvas.ToView(handle.X, handle.Y));
        SelectionHandles.DrawHandles(context, points);
        SelectionHandles.DrawHandles(context, all.Where(handle => handle.Kind is HandleKind.LineStart or HandleKind.LineEnd).Select(handle => _canvas.ToView(handle.X, handle.Y)), round: true);
        if (_canvas.RotateHandleOf(id) is { } rotate)
        {
            var (top, _) = _canvas.TopMiddleOf(id);
            context.DrawLine(new Pen(Ui.Brush("Mat.Accent"), 1), top, rotate);
            SelectionHandles.DrawHandles(context, [rotate], round: true);
        }

        var adjust = Ui.Brush("Mat.Warning");
        var pen = new Pen(Ui.Brush("Mat.Border"), 1);
        foreach (var handle in all.Where(handle => handle.Kind == HandleKind.Adjust))
        {
            var center = _canvas.ToView(handle.X, handle.Y);
            context.DrawEllipse(adjust, pen, center, 4.5, 4.5);
        }
    }

    /// <summary>The edited text: a dashed frame around the shape, the selected stretches, the caret (blinking) and an IME composition.</summary>
    private void DrawText(DrawingContext context, Core.Editing.TextSelection text, PlacedTextLayout layout)
    {
        var corners = _canvas.CornersOf(text.Target.ShapeId);
        if (!text.Target.IsTableCell && corners.Count == 4)
        {
            SelectionHandles.DrawFrame(context, corners, dashed: true);
        }

        var brush = Ui.Brush("Mat.Selection");
        foreach (var quad in layout.SelectionQuads(text.Start, text.End))
        {
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(_canvas.ToView(quad[0].X, quad[0].Y), true);
                for (var index = 1; index < quad.Length; index++)
                {
                    stream.LineTo(_canvas.ToView(quad[index].X, quad[index].Y));
                }

                stream.EndFigure(true);
            }

            context.DrawGeometry(brush, null, geometry);
        }

        var (top, bottom) = layout.CaretLine(text.Active);
        var (from, to) = (_canvas.ToView(top.X, top.Y), _canvas.ToView(bottom.X, bottom.Y));
        if (_canvas.Preedit is { Length: > 0 } preedit)
        {
            var formatted = new FormattedText(preedit, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, Math.Max(10, to.Y - from.Y) * 0.8, Ui.Brush("Mat.Text"));
            var box = new Rect(from.X, from.Y, formatted.Width + 4, formatted.Height);
            context.FillRectangle(Ui.Brush("Mat.Surface"), box);
            context.DrawText(formatted, new Point(from.X + 2, from.Y));
            context.DrawLine(new Pen(Ui.Brush("Mat.Text"), 1), new Point(box.X, box.Bottom), new Point(box.Right, box.Bottom));
            return;
        }

        if (_caretOn && text.IsCollapsed)
        {
            context.DrawLine(new Pen(Ui.Brush("Mat.Text"), 1.5), from, to);
        }
    }

    /// <summary>What a drag shows: dashed outlines where the shapes go, the smart guides, or the marquee.</summary>
    private void DrawDrag(DrawingContext context, CanvasDrag drag)
    {
        switch (drag.Kind)
        {
            case DragKind.Marquee:
                var rect = new Rect(_canvas.ToView(drag.StartX, drag.StartY), _canvas.ToView(drag.CurrentX, drag.CurrentY)).Normalize();
                context.DrawRectangle(Ui.Brush("Mat.Selection"), new Pen(Ui.Brush("Mat.Accent"), 1), rect);
                break;
            case DragKind.TextSelect:
                break;
            default:
                foreach (var outline in _canvas.PreviewOutlines(drag))
                {
                    SelectionHandles.DrawFrame(context, outline, dashed: true);
                }

                break;
        }

        var guide = new Pen(Ui.Brush("Mat.Accent"), 1) { DashStyle = new DashStyle([5, 3], 0) };
        foreach (var line in drag.Guides)
        {
            var position = Core.Model.Units.EmuToPoints(line.Position);
            var (from, to) = (Core.Model.Units.EmuToPoints(line.From), Core.Model.Units.EmuToPoints(line.To));
            var (a, b) = line.Vertical ? (_canvas.ToView(position, from), _canvas.ToView(position, to)) : (_canvas.ToView(from, position), _canvas.ToView(to, position));
            context.DrawLine(guide, a, b);
        }
    }
}

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Platform.Storage;
using ThixOffice.Core.Business;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>What a drag on the slide does.</summary>
internal enum DragKind
{
    Move,
    Resize,
    Rotate,
    Adjust,
    LineEnd,
    Marquee,
    TextSelect,
    Draw,
}

/// <summary>A drag in progress: where it began and is (slide points), the shapes and handle, the guides the snapping shows.</summary>
internal sealed record CanvasDrag(DragKind Kind, double StartX, double StartY, IReadOnlyList<int> ShapeIds, int Handle = -1, bool StartsText = false)
{
    public double CurrentX { get; set; } = StartX;

    public double CurrentY { get; set; } = StartY;

    public bool Moved { get; set; }

    public bool Proportional { get; set; }

    public bool Duplicate { get; set; }

    public bool NoSnap { get; set; }

    /// <summary>The snapped offset of a move (points).</summary>
    public (double X, double Y) Offset { get; set; }

    public IReadOnlyList<SnapGuide> Guides { get; set; } = [];

    /// <summary>The text position a text drag started at.</summary>
    public TextPosition TextAnchor { get; init; }

    /// <summary>The shape a draw drag makes.</summary>
    public string? DrawPreset { get; init; }
}

internal sealed partial class SlideCanvas
{
    /// <summary>The band along a text shape's frame where a press moves the shape instead of placing the caret (DIP).</summary>
    private const double BorderBand = 6;

    /// <summary>The snap distance in device-independent pixels.</summary>
    private const double SnapDistance = 6;

    private string? _preedit;

    /// <summary>The drag in progress, or null.</summary>
    public CanvasDrag? Drag { get; private set; }

    /// <summary>
    /// The shape the next drag draws (a preset of the shapes gallery, "textBox" for Textfeld), or null. A click without dragging
    /// places it in its default size; Esc cancels.
    /// </summary>
    public string? PendingShape { get; private set; }

    /// <summary>Formen › a shape, Textfeld: the next drag on the slide draws it (the cursor becomes a cross).</summary>
    public void BeginDraw(string preset)
    {
        PendingShape = preset;
        Session.EndTextEditing();
        Cursor = new Cursor(StandardCursorType.Cross);
        Focus();
    }

    /// <summary>The IME composition shown at the caret, or null.</summary>
    public string? Preedit => _preedit;

    /// <summary>The handles of a shape (slide points).</summary>
    public IReadOnlyList<SelectionHandle> HandlesOf(int shapeId) =>
        Slide is { } slide ? SlideHitTesting.HandlesOf(Presentation, slide, shapeId) : [];

    /// <summary>The middle of a shape's top edge in this control and the unit vector pointing "up" from it.</summary>
    public (Point Top, Vector Up) TopMiddleOf(int shapeId)
    {
        var corners = CornersOf(shapeId);
        if (corners.Count < 4)
        {
            return (default, default);
        }

        var top = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
        var bottom = new Point((corners[3].X + corners[2].X) / 2, (corners[3].Y + corners[2].Y) / 2);
        var up = top - bottom;
        var length = Math.Sqrt((up.X * up.X) + (up.Y * up.Y));
        return (top, length < 1e-6 ? new Vector(0, -1) : up / length);
    }

    /// <summary>The rotation handle of a shape in this control (a fixed distance above its top edge), or null (lines, frames).</summary>
    public Point? RotateHandleOf(int shapeId)
    {
        if (!HandlesOf(shapeId).Any(handle => handle.Kind == HandleKind.Rotate))
        {
            return null;
        }

        var (top, up) = TopMiddleOf(shapeId);
        return top + (up * SlideOverlay.RotateDistance);
    }

    /// <summary>The rotation of a shape on the slide in degrees (its top edge's direction).</summary>
    private double RotationOf(int shapeId)
    {
        var corners = CornersOf(shapeId);
        return corners.Count < 4 ? 0 : Math.Atan2(corners[1].Y - corners[0].Y, corners[1].X - corners[0].X) * 180 / Math.PI;
    }

    // ===== Pointer =====

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Slide is not { } slide)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        var (x, y) = ToSlide(point.Position);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var toggle = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) || shift;
        e.Handled = true;

        if (point.Properties.IsRightButtonPressed)
        {
            PressRight(slide, x, y);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (PendingShape is { } preset)
        {
            Drag = new CanvasDrag(DragKind.Draw, x, y, []) { DrawPreset = preset };
            PendingShape = null;
            e.Pointer.Capture(this);
            return;
        }

        // A handle of a selected shape: size, rotate, adjust, move a line's end.
        if (Session.Selection.Text is null && HandleUnder(point.Position) is { } handle)
        {
            Drag = handle;
            e.Pointer.Capture(this);
            return;
        }

        // Text being edited: a click inside it places the caret (Umschalt extends), double = word, triple = paragraph.
        if (Session.Selection.Text is { } editing && EditedText is { } layout && HitsText(slide, editing.Target, x, y, point.Position))
        {
            var position = layout.HitTest(x, y);
            if (e.ClickCount == 2)
            {
                Session.SelectWord(editing.Target, position);
                return;
            }

            if (e.ClickCount >= 3)
            {
                Session.SelectParagraph(editing.Target, position.Paragraph);
                return;
            }

            Session.SetCaret(editing.Target, position, extend: shift);
            Drag = new CanvasDrag(DragKind.TextSelect, x, y, [editing.Target.ShapeId]) { TextAnchor = shift ? editing.Anchor : position };
            e.Pointer.Capture(this);
            return;
        }

        var hit = SlideHitTesting.HitTest(Presentation, slide, x, y);
        if (hit is null)
        {
            // The empty slide: a marquee selects what it encloses.
            if (Session.Selection.EnteredGroup is not null && !toggle)
            {
                Session.SelectShapes([]);
            }

            if (!toggle)
            {
                Session.SelectShapes([], Session.Selection.EnteredGroup);
            }

            Drag = new CanvasDrag(DragKind.Marquee, x, y, []);
            e.Pointer.Capture(this);
            return;
        }

        var entered = Session.Selection.EnteredGroup;
        var id = hit.SelectableId(entered);
        if (e.ClickCount == 2 && hit.GroupPath.Count > 0 && id != hit.ShapeId && entered != id)
        {
            // A double click on a group opens it: its shapes become selectable.
            Session.EnterGroup(id);
            Session.SelectShapes([hit.SelectableId(id)], id);
            return;
        }

        if (toggle)
        {
            Session.ToggleShapeSelection(id);
            return;
        }

        var target = id == hit.ShapeId ? TextGeometry.TargetAt(Presentation, slide, id, x, y, OfficeFonts.Cached) : null;
        var selected = SelectedIds.Contains(id);
        var onBorder = target is not null && NearFrame(id, point.Position);
        if (target is not null && !onBorder && (!selected || e.ClickCount >= 2 || target.IsTableCell))
        {
            // A press on text (or a cell): the caret goes there at once, like PowerPoint.
            BeginTextAt(slide, target, x, y);
            if (EditedText is { } placed)
            {
                Drag = new CanvasDrag(DragKind.TextSelect, x, y, [id]) { TextAnchor = placed.HitTest(x, y) };
                e.Pointer.Capture(this);
            }

            return;
        }

        if (!selected)
        {
            Session.SelectShapes([id], entered);
        }

        Drag = new CanvasDrag(DragKind.Move, x, y, SelectedIds, StartsText: target is not null && selected && !onBorder);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (Drag is not { } drag)
        {
            UpdateCursor(position);
            return;
        }

        var (x, y) = ToSlide(position);
        var start = ToView(drag.StartX, drag.StartY);
        if (!drag.Moved && Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 3)
        {
            return;
        }

        drag.Moved = true;
        drag.CurrentX = x;
        drag.CurrentY = y;
        drag.Proportional = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        drag.Duplicate = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control);
        drag.NoSnap = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Alt);
        switch (drag.Kind)
        {
            case DragKind.TextSelect when Session.Selection.Text is { } text && EditedText is { } layout:
                Session.SelectText(text.Target, drag.TextAnchor, layout.HitTest(x, y));
                break;
            case DragKind.Move:
                SnapMove(drag);
                break;
            case DragKind.Resize or DragKind.LineEnd or DragKind.Draw:
                SnapPointer(drag);
                break;
        }

        Overlay.InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Drag is not { } drag)
        {
            return;
        }

        Drag = null;
        e.Pointer.Capture(null);
        try
        {
            Finish(drag);
        }
        finally
        {
            Overlay.InvalidateVisual();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (Drag is not null)
        {
            Drag = null;
            Overlay.InvalidateVisual();
        }
    }

    /// <summary>A drag ends: the change goes to the session as one undo step (a click without moving changes nothing).</summary>
    private void Finish(CanvasDrag drag)
    {
        if (Slide is not { } slide)
        {
            return;
        }

        if (drag.Kind == DragKind.Draw)
        {
            FinishDraw(drag);
            return;
        }

        if (!drag.Moved)
        {
            if (drag.Kind == DragKind.Move && drag.StartsText && drag.ShapeIds.Count == 1
                && TextGeometry.TargetAt(Presentation, slide, drag.ShapeIds[0], drag.StartX, drag.StartY, OfficeFonts.Cached) is { } target)
            {
                BeginTextAt(slide, target, drag.StartX, drag.StartY);
            }

            return;
        }

        _ = _editor.RunAsync(LabelOf(drag), () =>
        {
            switch (drag.Kind)
            {
                case DragKind.Move:
                    FinishMove(drag);
                    break;
                case DragKind.Resize:
                    if (ResizedBounds(slide, drag) is { } bounds)
                    {
                        Session.SetBounds(drag.ShapeIds[0], bounds);
                    }

                    break;
                case DragKind.Rotate:
                    Session.Rotate(RotationAngle(drag), relative: false, [drag.ShapeIds[0]]);
                    break;
                case DragKind.Adjust when SlideHitTesting.Locate(Presentation, slide, drag.ShapeIds[0]) is { } placed:
                    var (localX, localY) = placed.Matrix.Invert().Apply(drag.CurrentX, drag.CurrentY);
                    Session.DragAdjustHandle(drag.ShapeIds[0], drag.Handle, Units.PointsToEmu(localX), Units.PointsToEmu(localY));
                    break;
                case DragKind.LineEnd:
                    if (LineBounds(slide, drag) is { } line)
                    {
                        Session.SetBounds(drag.ShapeIds[0], line);
                    }

                    break;
                case DragKind.Marquee:
                    SelectEnclosed(slide, drag);
                    break;
            }
        });
    }

    /// <summary>A drawn shape goes in with the dragged box (Umschalt: a square / 45° line); a click gives the default size there.</summary>
    private void FinishDraw(CanvasDrag drag)
    {
        Cursor = Cursor.Default;
        var preset = drag.DrawPreset ?? "rect";
        var (x1, y1) = (drag.StartX, drag.StartY);
        var (x2, y2) = (drag.CurrentX, drag.CurrentY);
        var isLine = ShapeCatalog.IsLine(preset);
        if (!drag.Moved)
        {
            (x2, y2) = preset == "textBox" ? (x1 + 216, y1 + 36) : isLine ? (x1 + 144, y1) : (x1 + 72, y1 + 72);
        }
        else if (drag.Proportional && !isLine)
        {
            var side = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
            (x2, y2) = (x1 + (Math.Sign(x2 - x1) * side), y1 + (Math.Sign(y2 - y1) * side));
        }

        var bounds = new EmuRect(Units.PointsToEmu(Math.Min(x1, x2)), Units.PointsToEmu(Math.Min(y1, y2)), Units.PointsToEmu(Math.Max(1, Math.Abs(x2 - x1))), Units.PointsToEmu(Math.Max(1, Math.Abs(y2 - y1))));
        _ = _editor.RunAsync(preset == "textBox" ? "Textfeld einfügen" : "Form einfügen", () =>
        {
            if (preset == "textBox")
            {
                var id = Session.InsertTextBox(bounds);
                EditTextOf(id);
            }
            else if (isLine)
            {
                Session.InsertLine(Units.PointsToEmu(x1), Units.PointsToEmu(y1), Units.PointsToEmu(x2), Units.PointsToEmu(y2), preset,
                    preset == "straightConnector1" ? new LineEnd(LineEndKind.Triangle) : null);
            }
            else
            {
                Session.InsertShape(preset, bounds);
            }
        });
    }

    private static string LabelOf(CanvasDrag drag) => drag.Kind switch
    {
        DragKind.Move => drag.Duplicate ? "Duplizieren" : "Verschieben",
        DragKind.Resize => "Größe ändern",
        DragKind.Rotate => "Drehen",
        DragKind.Adjust => "Form anpassen",
        DragKind.LineEnd => "Linie ändern",
        DragKind.Draw => "Form einfügen",
        _ => "Markieren",
    };

    private void FinishMove(CanvasDrag drag)
    {
        var (dx, dy) = (Units.PointsToEmu(drag.Offset.X), Units.PointsToEmu(drag.Offset.Y));
        if (!drag.Duplicate)
        {
            Session.MoveShapes(dx, dy, drag.ShapeIds);
            return;
        }

        // Strg+drag: a copy goes where the pointer let go, the originals stay.
        Session.Group("Duplizieren", () =>
        {
            Session.SelectShapes(drag.ShapeIds, Session.Selection.EnteredGroup);
            var copies = Session.DuplicateShapes();
            Session.MoveShapes(dx - EditingSession.DuplicateOffset, dy - EditingSession.DuplicateOffset, copies);
        });
    }

    /// <summary>The right button: the shape under it joins the selection (a selected one stays as it is), then its context menu.</summary>
    private void PressRight(Slide slide, double x, double y)
    {
        var hit = SlideHitTesting.HitTest(Presentation, slide, x, y);
        if (Session.Selection.Text is null)
        {
            if (hit is null)
            {
                Session.SelectShapes([], Session.Selection.EnteredGroup);
            }
            else if (!SelectedIds.Contains(hit.SelectableId(Session.Selection.EnteredGroup)))
            {
                Session.SelectShapes([hit.SelectableId(Session.Selection.EnteredGroup)], Session.Selection.EnteredGroup);
            }
        }

        ContextMenu = ShowContextMenus.ForSlideCanvas(_editor.Window, Session);
    }

    /// <summary>Starts editing the text of a shape or cell with the caret at a slide point.</summary>
    private void BeginTextAt(Slide slide, TextTarget target, double x, double y)
    {
        Session.SelectText(target, new TextPosition(0, 0), new TextPosition(0, 0));
        if (TextGeometry.Of(Presentation, slide, target, OfficeFonts.Cached, DateTime.Now) is { } layout)
        {
            var position = layout.HitTest(x, y);
            Session.SetCaret(target, position);
        }

        Overlay.ResetBlink();
    }

    /// <summary>True when a point is on the edited text's shape or cell (not on its frame band).</summary>
    private bool HitsText(Slide slide, TextTarget target, double x, double y, Point view)
    {
        var hit = SlideHitTesting.HitTest(Presentation, slide, x, y);
        if (hit?.ShapeId != target.ShapeId || (!target.IsTableCell && NearFrame(target.ShapeId, view)))
        {
            return false;
        }

        return !target.IsTableCell || TextGeometry.TargetAt(Presentation, slide, target.ShapeId, x, y, OfficeFonts.Cached) == target;
    }

    /// <summary>True when a point is within the border band of a shape's frame (inside the shape).</summary>
    private bool NearFrame(int shapeId, Point point)
    {
        var corners = CornersOf(shapeId);
        if (corners.Count < 4)
        {
            return false;
        }

        for (var index = 0; index < 4; index++)
        {
            if (DistanceToSegment(point, corners[index], corners[(index + 1) % 4]) <= BorderBand)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = (dx * dx) + (dy * dy);
        var t = length < 1e-9 ? 0 : Math.Clamp((((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / length, 0, 1);
        var (px, py) = (a.X + (t * dx), a.Y + (t * dy));
        return Math.Sqrt(((point.X - px) * (point.X - px)) + ((point.Y - py) * (point.Y - py)));
    }

    /// <summary>The handle under a point among the selected shapes' handles, as the drag it starts.</summary>
    private CanvasDrag? HandleUnder(Point point)
    {
        var (x, y) = ToSlide(point);
        foreach (var id in SelectedIds)
        {
            if (RotateHandleOf(id) is { } rotate && Distance(point, rotate) <= SelectionHandles.Size)
            {
                return new CanvasDrag(DragKind.Rotate, x, y, [id]);
            }

            foreach (var handle in HandlesOf(id).OrderBy(handle => handle.Kind == HandleKind.Adjust ? 0 : 1))
            {
                if (handle.Kind == HandleKind.Rotate || Distance(point, ToView(handle.X, handle.Y)) > SelectionHandles.Size * 0.75)
                {
                    continue;
                }

                return handle.Kind switch
                {
                    HandleKind.Adjust => new CanvasDrag(DragKind.Adjust, x, y, [id], handle.AdjustIndex),
                    HandleKind.LineStart => new CanvasDrag(DragKind.LineEnd, x, y, [id], 0),
                    HandleKind.LineEnd => new CanvasDrag(DragKind.LineEnd, x, y, [id], 1),
                    _ => new CanvasDrag(DragKind.Resize, x, y, [id], (int)handle.Kind),
                };
            }
        }

        return null;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private void UpdateCursor(Point position)
    {
        Cursor cursor;
        var (x, y) = ToSlide(position);
        if (Session.Selection.Text is null && HandleUnder(position) is { } handle)
        {
            cursor = handle.Kind switch
            {
                DragKind.Resize => SelectionHandles.CursorOf(handle.Handle, RotationOf(handle.ShapeIds[0])),
                DragKind.Rotate => new Cursor(StandardCursorType.Hand),
                _ => new Cursor(StandardCursorType.Cross),
            };
        }
        else if (Slide is { } slide && SlideHitTesting.HitTest(Presentation, slide, x, y) is { } hit)
        {
            var id = hit.SelectableId(Session.Selection.EnteredGroup);
            var text = id == hit.ShapeId && TextGeometry.TargetAt(Presentation, slide, id, x, y, OfficeFonts.Cached) is not null;
            cursor = text && !NearFrame(id, position) ? new Cursor(StandardCursorType.Ibeam) : new Cursor(StandardCursorType.SizeAll);
        }
        else
        {
            cursor = Cursor.Default;
        }

        Cursor = cursor;
    }

    // ===== Moves, sizes, rotations =====

    /// <summary>The move's offset with smart guides and the grid (Alt – ⌘ on the Mac – moves freely).</summary>
    private void SnapMove(CanvasDrag drag)
    {
        var (dx, dy) = (drag.CurrentX - drag.StartX, drag.CurrentY - drag.StartY);
        drag.Guides = [];
        if (Slide is not { } slide || drag.NoSnap || drag.ShapeIds.Count == 0)
        {
            drag.Offset = (dx, dy);
            return;
        }

        var bounds = drag.ShapeIds.Select(id => SlideHitTesting.BoundsOf(Presentation, slide, id)).ToList();
        var (left, top) = (bounds.Min(box => box.Left) + dx, bounds.Min(box => box.Top) + dy);
        var (right, bottom) = (bounds.Max(box => box.Right) + dx, bounds.Max(box => box.Bottom) + dy);
        var moving = new EmuRect(Units.PointsToEmu(left), Units.PointsToEmu(top), Units.PointsToEmu(right - left), Units.PointsToEmu(bottom - top));
        var threshold = Units.PointsToEmu(SnapDistance / Scale);
        var result = Snapping.Snap(Presentation, slide, moving, [.. drag.ShapeIds], threshold, _editor.SnapToGrid ? Snapping.DefaultGrid : null, _editor.SmartGuides);
        drag.Offset = (dx + Units.EmuToPoints(result.Dx), dy + Units.EmuToPoints(result.Dy));
        drag.Guides = result.Guides;
    }

    /// <summary>A handle's pointer snapped to the other shapes' edges and centres.</summary>
    private void SnapPointer(CanvasDrag drag)
    {
        drag.Guides = [];
        if (Slide is not { } slide || drag.NoSnap)
        {
            return;
        }

        var threshold = Units.PointsToEmu(SnapDistance / Scale);
        var (x, y, guides) = Snapping.SnapPoint(Presentation, slide, Units.PointsToEmu(drag.CurrentX), Units.PointsToEmu(drag.CurrentY), [.. drag.ShapeIds], threshold);
        drag.CurrentX = Units.EmuToPoints(x);
        drag.CurrentY = Units.EmuToPoints(y);
        drag.Guides = guides;
    }

    /// <summary>The new box of a shape sized by a handle: the opposite side stays, a rotated shape sizes along its own axes.</summary>
    internal EmuRect? ResizedBounds(Slide slide, CanvasDrag drag)
    {
        if (SlideHitTesting.Locate(Presentation, slide, drag.ShapeIds[0]) is not { } placed)
        {
            return null;
        }

        var transform = placed.Transform;
        var parent = SlideRenderer.ShapeMatrix(transform).Invert() * placed.Matrix;
        var (px, py) = parent.Invert().Apply(drag.CurrentX, drag.CurrentY);
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var (cx, cy) = (Units.EmuToPoints(transform.X) + (width / 2), Units.EmuToPoints(transform.Y) + (height / 2));
        var radians = transform.Rotation / 60000.0 * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        var (localX, localY) = (((px - cx) * cos) + ((py - cy) * sin), (-(px - cx) * sin) + ((py - cy) * cos));
        var (sx, sy) = SelectionHandles.SidesOf(drag.Handle);
        var (anchorX, anchorY) = (-sx * width / 2, -sy * height / 2);
        var newWidth = sx == 0 ? width : sx * (localX - anchorX);
        var newHeight = sy == 0 ? height : sy * (localY - anchorY);
        var proportional = drag.Proportional || placed.Element is PictureShape;
        if (sx != 0 && sy != 0 && proportional && width > 0 && height > 0)
        {
            var scale = Math.Max(Math.Abs(newWidth / width), Math.Abs(newHeight / height));
            (newWidth, newHeight) = (Math.Sign(newWidth == 0 ? 1 : newWidth) * width * scale, Math.Sign(newHeight == 0 ? 1 : newHeight) * height * scale);
        }

        newWidth = Math.Abs(newWidth) < 1 ? 1 : newWidth;
        newHeight = Math.Abs(newHeight) < 1 ? 1 : newHeight;
        var centerLocalX = sx == 0 ? 0 : anchorX + (sx * newWidth / 2);
        var centerLocalY = sy == 0 ? 0 : anchorY + (sy * newHeight / 2);
        var (newCx, newCy) = (cx + (centerLocalX * cos) - (centerLocalY * sin), cy + (centerLocalX * sin) + (centerLocalY * cos));
        var left = newWidth < 0 ? newCx + (Math.Abs(newWidth) / 2) : newCx - (newWidth / 2);
        var top = newHeight < 0 ? newCy + (Math.Abs(newHeight) / 2) : newCy - (newHeight / 2);
        return ToChildSpace(slide, drag.ShapeIds[0], new EmuRect(Units.PointsToEmu(left), Units.PointsToEmu(top), Units.PointsToEmu(newWidth), Units.PointsToEmu(newHeight)));
    }

    /// <summary>A line with one end dragged: the box from the end that stays to the pointer, its flips telling the direction.</summary>
    internal EmuRect? LineBounds(Slide slide, CanvasDrag drag)
    {
        if (SlideHitTesting.Locate(Presentation, slide, drag.ShapeIds[0]) is not { } placed)
        {
            return null;
        }

        var transform = placed.Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var parent = SlideRenderer.ShapeMatrix(transform).Invert() * placed.Matrix;
        var toParent = SlideRenderer.ShapeMatrix(transform);
        var fixedEnd = drag.Handle == 0 ? toParent.Apply(width, height) : toParent.Apply(0, 0);
        var moved = parent.Invert().Apply(drag.CurrentX, drag.CurrentY);
        var (start, end) = drag.Handle == 0 ? (moved, fixedEnd) : (fixedEnd, moved);
        var flipH = end.X < start.X;
        var flipV = end.Y < start.Y;
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var (boxWidth, boxHeight) = (Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

        // SetBounds turns a negative size into a flip of the current one.
        var signedWidth = flipH != transform.FlipHorizontal ? -boxWidth : boxWidth;
        var signedHeight = flipV != transform.FlipVertical ? -boxHeight : boxHeight;
        var x = signedWidth < 0 ? left + boxWidth : left;
        var y = signedHeight < 0 ? top + boxHeight : top;
        return ToChildSpace(slide, drag.ShapeIds[0], new EmuRect(Units.PointsToEmu(x), Units.PointsToEmu(y), Units.PointsToEmu(signedWidth), Units.PointsToEmu(signedHeight)));
    }

    /// <summary>A box in the slide's space mapped into the child space of the groups around a shape (their scale and offset).</summary>
    private static EmuRect ToChildSpace(Slide slide, int shapeId, EmuRect rect)
    {
        if (ShapeTree.PathTo(slide.Shapes, shapeId) is not { Count: > 1 } path)
        {
            return rect;
        }

        foreach (var groupId in path.Take(path.Count - 1))
        {
            if (slide.FindShape(groupId) is not GroupShape { Transform: { } group })
            {
                continue;
            }

            var scaleX = group.Width != 0 && group.ChildWidth != 0 ? group.ChildWidth / (double)group.Width : 1;
            var scaleY = group.Height != 0 && group.ChildHeight != 0 ? group.ChildHeight / (double)group.Height : 1;
            rect = new EmuRect(
                group.ChildX + (long)Math.Round((rect.X - group.X) * scaleX),
                group.ChildY + (long)Math.Round((rect.Y - group.Y) * scaleY),
                (long)Math.Round(rect.Width * scaleX),
                (long)Math.Round(rect.Height * scaleY));
        }

        return rect;
    }

    /// <summary>The rotation a rotate drag gives (degrees; Umschalt in steps of 15°).</summary>
    private double RotationAngle(CanvasDrag drag)
    {
        if (Slide is not { } slide)
        {
            return 0;
        }

        var (left, top, right, bottom) = SlideHitTesting.BoundsOf(Presentation, slide, drag.ShapeIds[0]);
        var (cx, cy) = ((left + right) / 2, (top + bottom) / 2);
        var angle = (Math.Atan2(drag.CurrentY - cy, drag.CurrentX - cx) * 180 / Math.PI) + 90;
        if (drag.Proportional)
        {
            angle = Math.Round(angle / 15) * 15;
        }

        return ((angle % 360) + 360) % 360;
    }

    /// <summary>The dashed outlines a drag shows (this control's coordinates).</summary>
    internal IEnumerable<IReadOnlyList<Point>> PreviewOutlines(CanvasDrag drag)
    {
        if (Slide is not { } slide || !drag.Moved)
        {
            yield break;
        }

        switch (drag.Kind)
        {
            case DragKind.Move:
                var offset = new Vector(drag.Offset.X * Scale, drag.Offset.Y * Scale);
                foreach (var id in drag.ShapeIds)
                {
                    yield return [.. CornersOf(id).Select(corner => corner + offset)];
                }

                break;
            case DragKind.Rotate:
                var corners = SlideHitTesting.CornersOf(Presentation, slide, drag.ShapeIds[0]);
                if (corners.Count == 4)
                {
                    var (cx, cy) = (corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
                    var turn = Matrix2D.Rotation(RotationAngle(drag) - RotationOf(drag.ShapeIds[0]), cx, cy);
                    yield return [.. corners.Select(corner => turn.Apply(corner.X, corner.Y)).Select(point => ToView(point.X, point.Y))];
                }

                break;
            case DragKind.Resize when ResizedBounds(slide, drag) is { } bounds:
                yield return OutlineOf(slide, drag.ShapeIds[0], bounds);
                break;
            case DragKind.LineEnd when SlideHitTesting.Locate(Presentation, slide, drag.ShapeIds[0]) is { } placed:
                var (width, height) = (Units.EmuToPoints(placed.Transform.Width), Units.EmuToPoints(placed.Transform.Height));
                var fixedEnd = drag.Handle == 0 ? placed.Matrix.Apply(width, height) : placed.Matrix.Apply(0, 0);
                yield return [ToView(fixedEnd.X, fixedEnd.Y), ToView(drag.CurrentX, drag.CurrentY)];
                break;
            case DragKind.Draw:
                var (a, b) = (ToView(drag.StartX, drag.StartY), ToView(drag.CurrentX, drag.CurrentY));
                if (ShapeCatalog.IsLine(drag.DrawPreset ?? ""))
                {
                    yield return [a, b];
                }
                else
                {
                    yield return [a, new Point(b.X, a.Y), b, new Point(a.X, b.Y)];
                }

                break;
            case DragKind.Adjust when SlideHitTesting.Locate(Presentation, slide, drag.ShapeIds[0]) is { } placed:
                var handle = ToView(drag.CurrentX, drag.CurrentY);
                yield return [handle + new Vector(-4, -4), handle + new Vector(4, -4), handle + new Vector(4, 4), handle + new Vector(-4, 4)];
                break;
        }
    }

    /// <summary>The corners a shape would have with another box (its rotation kept), in this control.</summary>
    private IReadOnlyList<Point> OutlineOf(Slide slide, int shapeId, EmuRect bounds)
    {
        if (SlideHitTesting.Locate(Presentation, slide, shapeId) is not { } placed)
        {
            return [];
        }

        var normalized = new EmuRect(Math.Min(bounds.X, bounds.X + bounds.Width), Math.Min(bounds.Y, bounds.Y + bounds.Height), Math.Abs(bounds.Width), Math.Abs(bounds.Height));
        var parentMatrix = SlideRenderer.ShapeMatrix(placed.Transform).Invert() * placed.Matrix;
        var path = ShapeTree.PathTo(slide.Shapes, shapeId);
        if (path is { Count: > 1 })
        {
            // A child of a group: show the box the slide space would get (approximately: the group's mapping undone).
            parentMatrix = Matrix2D.Identity;
            normalized = new EmuRect(Math.Min(bounds.X, bounds.X + bounds.Width), Math.Min(bounds.Y, bounds.Y + bounds.Height), Math.Abs(bounds.Width), Math.Abs(bounds.Height));
        }

        var transform = placed.Transform with { X = normalized.X, Y = normalized.Y, Width = normalized.Width, Height = normalized.Height };
        var matrix = SlideRenderer.ShapeMatrix(transform) * parentMatrix;
        var (width, height) = (Units.EmuToPoints(normalized.Width), Units.EmuToPoints(normalized.Height));
        return [.. new[] { matrix.Apply(0, 0), matrix.Apply(width, 0), matrix.Apply(width, height), matrix.Apply(0, height) }.Select(point => ToView(point.X, point.Y))];
    }

    /// <summary>The marquee selects the shapes it encloses completely (inside an entered group: its children).</summary>
    private void SelectEnclosed(Slide slide, CanvasDrag drag)
    {
        var (left, right) = (Math.Min(drag.StartX, drag.CurrentX), Math.Max(drag.StartX, drag.CurrentX));
        var (top, bottom) = (Math.Min(drag.StartY, drag.CurrentY), Math.Max(drag.StartY, drag.CurrentY));
        var entered = Session.Selection.EnteredGroup;
        IEnumerable<SlideElement> candidates = entered is { } group && slide.FindShape(group) is GroupShape opened ? opened.Children : slide.Shapes;
        var ids = candidates.Where(shape => !shape.Hidden)
            .Where(shape =>
            {
                var bounds = SlideHitTesting.BoundsOf(Presentation, slide, shape.Id);
                return bounds.Left >= left && bounds.Right <= right && bounds.Top >= top && bounds.Bottom <= bottom && bounds.Right > bounds.Left - 1;
            })
            .Select(shape => shape.Id)
            .ToList();
        Session.SelectShapes(ids, entered);
    }

    // ===== Pictures dropped on the slide =====

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } files && files.Any(file => file.TryGetLocalPath() is { } path && ImageFiles.IsPicture(path))
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    /// <summary>Picture files dropped on the slide go in where they were dropped.</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { Length: > 0 } files)
        {
            return;
        }

        var (x, y) = ToSlide(e.GetPosition(this));
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().Where(ImageFiles.IsPicture).ToList();
        _ = _editor.InsertPicturesAsync(paths, (x, y));
    }

    // ===== IME =====

    internal void SetPreedit(string? text)
    {
        _preedit = text;
        Overlay.InvalidateVisual();
    }
}

/// <summary>The input method's view of the edited text: the caret's rectangle, the caret's paragraph, the composition.</summary>
internal sealed class SlideInputClient(SlideCanvas canvas) : TextInputMethodClient
{
    public override Avalonia.Visual TextViewVisual => canvas;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => true;

    public override string SurroundingText
    {
        get
        {
            var session = canvas.Session;
            if (session.Selection.Text is not { } text || session.TextBodyOf(text.Target) is not { } body || text.Active.Paragraph >= body.Paragraphs.Count)
            {
                return "";
            }

            return body.Paragraphs[text.Active.Paragraph].Text;
        }
    }

    public override Rect CursorRectangle => canvas.CaretRect ?? default;

    public override Avalonia.Input.TextInput.TextSelection Selection
    {
        get
        {
            var text = canvas.Session.Selection.Text;
            if (text is null)
            {
                return default;
            }

            var anchor = text.Anchor.Paragraph == text.Active.Paragraph ? text.Anchor.Offset : text.Active.Offset;
            return new Avalonia.Input.TextInput.TextSelection(anchor, text.Active.Offset);
        }
        set
        {
            if (canvas.Session.Selection.Text is not { } text)
            {
                return;
            }

            var paragraph = text.Active.Paragraph;
            canvas.Session.SelectText(text.Target, new TextPosition(paragraph, value.Start), new TextPosition(paragraph, value.End));
        }
    }

    public override void SetPreeditText(string? preeditText) => canvas.SetPreedit(preeditText);

    public override void SetPreeditText(string? preeditText, int? cursorPos) => canvas.SetPreedit(preeditText);

    /// <summary>The caret, the selection or the text changed.</summary>
    public void NotifyChanged()
    {
        RaiseCursorRectangleChanged();
        RaiseSurroundingTextChanged();
        RaiseSelectionChanged();
    }
}

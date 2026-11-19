using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// The sizing handles of a selected object, shared by every app (ThixWrite's pictures, ThixCalc's charts, ThixShow's shapes): the
/// eight handles of a frame numbered clockwise from the top left (0 top left, 1 top, 2 top right, 3 right, 4 bottom right,
/// 5 bottom, 6 bottom left, 7 left), finding the one under the pointer, the frame a drag gives (corners keep the proportions on
/// request, the opposite side stays), the cursor of each handle and the drawing in the theme's accent.
/// </summary>
public static class SelectionHandles
{
    /// <summary>The size of a handle in device-independent pixels.</summary>
    public const double Size = 8;

    /// <summary>The eight handles of a frame, clockwise from the top left.</summary>
    public static IEnumerable<(int Handle, Point Center)> Of(Rect rect)
    {
        yield return (0, rect.TopLeft);
        yield return (1, new Point(rect.Center.X, rect.Top));
        yield return (2, rect.TopRight);
        yield return (3, new Point(rect.Right, rect.Center.Y));
        yield return (4, rect.BottomRight);
        yield return (5, new Point(rect.Center.X, rect.Bottom));
        yield return (6, rect.BottomLeft);
        yield return (7, new Point(rect.Left, rect.Center.Y));
    }

    /// <summary>The handle within <paramref name="reach"/> of <paramref name="point"/>, or null.</summary>
    public static int? At(Rect rect, Point point, double reach = Size * 0.75)
    {
        foreach (var (handle, center) in Of(rect))
        {
            if (Math.Abs(point.X - center.X) <= reach && Math.Abs(point.Y - center.Y) <= reach)
            {
                return handle;
            }
        }

        return null;
    }

    /// <summary>Which sides a handle moves: -1 the left/top side, 1 the right/bottom side, 0 neither.</summary>
    public static (int Horizontal, int Vertical) SidesOf(int handle) => (
        handle switch { 0 or 6 or 7 => -1, 2 or 3 or 4 => 1, _ => 0 },
        handle switch { 0 or 1 or 2 => -1, 4 or 5 or 6 => 1, _ => 0 });

    /// <summary>
    /// The frame after dragging <paramref name="handle"/> by (<paramref name="dx"/>, <paramref name="dy"/>): the sides the handle
    /// moves follow, the opposite ones stay; a corner with <paramref name="proportional"/> keeps the aspect ratio (the side that
    /// moved more, relatively, decides). Never smaller than <paramref name="minimum"/>.
    /// </summary>
    public static Rect Resize(Rect original, int handle, double dx, double dy, bool proportional, double minimum = 4)
    {
        var (horizontal, vertical) = SidesOf(handle);
        var width = Math.Max(minimum, original.Width + (horizontal * dx));
        var height = Math.Max(minimum, original.Height + (vertical * dy));
        if (horizontal != 0 && vertical != 0 && proportional && original.Width > 0 && original.Height > 0)
        {
            var (scaleX, scaleY) = (width / original.Width, height / original.Height);
            var scale = Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY;
            (width, height) = (Math.Max(minimum, original.Width * scale), Math.Max(minimum, original.Height * scale));
        }

        var x = horizontal < 0 ? original.Right - width : original.X;
        var y = vertical < 0 ? original.Bottom - height : original.Y;
        return new Rect(x, y, width, height);
    }

    /// <summary>The cursor over a handle (of a frame turned by <paramref name="rotation"/> degrees).</summary>
    public static Cursor CursorOf(int handle, double rotation = 0)
    {
        // The handle's direction from the centre, turned with the frame, picks one of the four resize cursors.
        var angle = (((((handle - 3) * 45) + rotation) % 180) + 180) % 180;
        var type = angle switch
        {
            < 22.5 or >= 157.5 => StandardCursorType.SizeWestEast,
            < 67.5 => StandardCursorType.TopLeftCorner,
            < 112.5 => StandardCursorType.SizeNorthSouth,
            _ => StandardCursorType.TopRightCorner,
        };
        return new Cursor(type);
    }

    /// <summary>Draws a frame's outline (any quadrilateral: a rotated frame too) in the accent.</summary>
    public static void DrawFrame(DrawingContext context, IReadOnlyList<Point> corners, bool dashed = false)
    {
        if (corners.Count < 2)
        {
            return;
        }

        var pen = new Pen(Ui.Brush("Mat.Accent"), 1) { DashStyle = dashed ? new DashStyle([4, 3], 0) : null };
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(corners[0], false);
            foreach (var corner in corners.Skip(1))
            {
                stream.LineTo(corner);
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>Draws handles: white (the surface token) with the accent's border; <paramref name="round"/> for rotation and line ends.</summary>
    public static void DrawHandles(DrawingContext context, IEnumerable<Point> centers, bool round = false)
    {
        var fill = Ui.Brush("Mat.Surface");
        var pen = new Pen(Ui.Brush("Mat.Accent"), 1);
        foreach (var center in centers)
        {
            var rect = new Rect(center.X - (Size / 2), center.Y - (Size / 2), Size, Size);
            if (round)
            {
                context.DrawEllipse(fill, pen, rect.Center, Size / 2 + 0.5, Size / 2 + 0.5);
            }
            else
            {
                context.DrawRectangle(fill, pen, rect, 1.5, 1.5);
            }
        }
    }

    /// <summary>Draws a frame with its eight handles (an unrotated object: a picture, a chart).</summary>
    public static void Draw(DrawingContext context, Rect rect)
    {
        DrawFrame(context, [rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft]);
        DrawHandles(context, Of(rect).Select(handle => handle.Center));
    }
}

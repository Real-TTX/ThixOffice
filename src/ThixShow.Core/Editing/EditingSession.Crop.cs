using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

public sealed partial class EditingSession
{
    /// <summary>
    /// Zuschneiden like PowerPoint: the picture's crop (1000ths of a percent cut off each side) changes and its frame with it, so the
    /// part that stays visible stays where it is on the slide (rotation kept: the frame moves along the picture's own axes).
    /// </summary>
    public void CropPicture(int shapeId, RelativeRect crop)
    {
        var slideId = RequireSlide();
        Apply("Zuschneiden", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return WithShape(presentation, slideId, shapeId, shape =>
            {
                if (shape is not PictureShape picture || (shape.Transform ?? ResolvedTransform(presentation, slide, shape)) is not { } transform)
                {
                    return shape;
                }

                var clamped = Clamp(crop);
                var old = picture.Picture.Crop;
                var (oldVisibleX, oldVisibleY) = (Visible(old.Left, old.Right), Visible(old.Top, old.Bottom));
                var (fullWidth, fullHeight) = (transform.Width / oldVisibleX, transform.Height / oldVisibleY);
                var width = fullWidth * Visible(clamped.Left, clamped.Right);
                var height = fullHeight * Visible(clamped.Top, clamped.Bottom);

                // The new frame's centre: the old one moved by the change of the cut sides, along the frame's own (rotated) axes.
                var shiftX = (fullWidth * ((clamped.Left - old.Left) - (clamped.Right - old.Right)) / 100000.0) / 2;
                var shiftY = (fullHeight * ((clamped.Top - old.Top) - (clamped.Bottom - old.Bottom)) / 100000.0) / 2;
                if (transform.FlipHorizontal)
                {
                    shiftX = -shiftX;
                }

                if (transform.FlipVertical)
                {
                    shiftY = -shiftY;
                }

                var radians = transform.Rotation / 60000.0 * Math.PI / 180;
                var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
                var centerX = transform.X + (transform.Width / 2.0) + (shiftX * cos) - (shiftY * sin);
                var centerY = transform.Y + (transform.Height / 2.0) + (shiftX * sin) + (shiftY * cos);
                var frame = transform with
                {
                    X = (long)Math.Round(centerX - (width / 2)),
                    Y = (long)Math.Round(centerY - (height / 2)),
                    Width = Math.Max(1, (long)Math.Round(width)),
                    Height = Math.Max(1, (long)Math.Round(height)),
                };
                return picture with { Transform = frame, Picture = picture.Picture with { Crop = clamped } };
            });
        });
    }

    private static double Visible(int first, int second) => Math.Max(0.01, 1 - ((first + second) / 100000.0));

    /// <summary>Each side between 0 and 99 %, and together less than all of the picture.</summary>
    private static RelativeRect Clamp(RelativeRect crop)
    {
        static int Side(int value) => Math.Clamp(value, 0, 99000);
        var (left, right) = (Side(crop.Left), Side(crop.Right));
        var (top, bottom) = (Side(crop.Top), Side(crop.Bottom));
        if (left + right > 99000)
        {
            right = 99000 - left;
        }

        if (top + bottom > 99000)
        {
            bottom = 99000 - top;
        }

        return new RelativeRect(left, top, right, bottom);
    }
}

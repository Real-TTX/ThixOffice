using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>"Textumbruch" of a picture: in the line of text, or floating with the text around it, above and below it, behind or in front of it.</summary>
public enum PictureWrap
{
    InLine,
    Square,
    TopAndBottom,
    BehindText,
    InFrontOfText,
}

/// <summary>The picture commands of the editing session (Einfügen › Bilder, Bildformat, resizing with the mouse).</summary>
public sealed partial class EditingSession
{
    /// <summary>The picture the selection is (exactly one picture selected, as after a click on it), or null.</summary>
    public (DocumentPosition Position, ImageInline Image)? SelectedImage
    {
        get
        {
            var range = _selection.Range;
            if (range.Start.Block != range.End.Block || range.End.Offset - range.Start.Offset != 1
                || Document.ParagraphAt(range.Start.Block)?.InlineAt(range.Start.Offset) is not ImageInline image)
            {
                return null;
            }

            return (range.Start, image);
        }
    }

    /// <summary>
    /// Inserts a picture over the selection, as large as its pixels at its resolution but never wider than the text (or the cell);
    /// the picture is selected afterwards, as in Word.
    /// </summary>
    public void InsertImage(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Run("Bild einfügen", EditKind.Other, null, () =>
        {
            ClearWholeCells();
            var (properties, link) = TypingFormat();
            var (width, height) = Fit(image.NaturalSize, AvailableWidth(_selection.Start.Block));
            var inserted = new Paragraph([new ImageInline(image, width, height, properties, Link: link)]);
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, [inserted]));
            var end = map.EndOfInserted;
            SetSelectionQuiet(new Selection(end with { Offset = end.Offset - 1 }, end));
        });
    }

    /// <summary>The size of the selected picture in points (Bildformat › Größe, the handles); nothing without a selected picture.</summary>
    public void SetImageSize(double width, double height) =>
        ChangeImage("Bildgröße", image => image.WithSize(Math.Clamp(width, 1, 22 * 72), Math.Clamp(height, 1, 22 * 72)));

    /// <summary>"Größe zurücksetzen": the picture's own size (fitted to the text width).</summary>
    public void ResetImageSize() =>
        ChangeImage("Größe zurücksetzen", image =>
        {
            var (width, height) = Fit(image.Image.NaturalSize, AvailableWidth(_selection.Start.Block));
            return image.WithSize(width, height);
        });

    /// <summary>The alternative text of the selected picture (null or empty removes it).</summary>
    public void SetImageDescription(string? description) =>
        ChangeImage("Alternativtext", image => image with { Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() });

    /// <summary>"Bild ändern": another picture in the place of the selected one, as wide as it was (the height follows the new picture).</summary>
    public void ReplaceImage(ImageData replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ChangeImage("Bild ändern", image =>
        {
            var (naturalWidth, naturalHeight) = replacement.NaturalSize;
            var height = naturalWidth > 0 ? image.Width * naturalHeight / naturalWidth : image.Height;
            return image with { Image = replacement, Height = Math.Max(1, height), SourceXml = null, SourcePart = null, Crop = null };
        });
    }

    /// <summary>"Textumbruch" of the selected picture (Bildformat › Textumbruch, Word's layout options).</summary>
    public PictureWrap? SelectedImageWrap => SelectedImage?.Image.Anchor switch
    {
        null when SelectedImage is null => null,
        null => PictureWrap.InLine,
        { Wrap: DrawingWrap.None, BehindText: true } => PictureWrap.BehindText,
        { Wrap: DrawingWrap.None } => PictureWrap.InFrontOfText,
        { Wrap: DrawingWrap.TopAndBottom } => PictureWrap.TopAndBottom,
        _ => PictureWrap.Square,
    };

    /// <summary>
    /// Sets the "Textumbruch" of the selected picture: in the line, or floating with square / top-and-bottom wrap, behind or in front of
    /// the text. A picture that starts to float is put at <paramref name="at"/> (offsets from its column and paragraph, where it was shown)
    /// so that it does not jump; one already floating keeps its place.
    /// </summary>
    public void SetImageWrap(PictureWrap wrap, (double X, double Y)? at = null) =>
        ChangeImage("Textumbruch", image =>
        {
            if (wrap == PictureWrap.InLine)
            {
                return image with { Anchor = null, IsFloating = false };
            }

            var anchor = image.Anchor ?? new DrawingAnchor(DrawingFrame.Column, at?.X ?? 0, null, DrawingFrame.Paragraph, at?.Y ?? 0, null, DrawingWrap.Square, false,
                9, 0, 9, 0, 251659264); // Word's distances to the text (0.32 cm left/right) and its first z order
            anchor = wrap switch
            {
                PictureWrap.TopAndBottom => anchor with { Wrap = DrawingWrap.TopAndBottom, BehindText = false },
                PictureWrap.BehindText => anchor with { Wrap = DrawingWrap.None, BehindText = true },
                PictureWrap.InFrontOfText => anchor with { Wrap = DrawingWrap.None, BehindText = false },
                _ => anchor with { Wrap = DrawingWrap.Square, BehindText = false },
            };
            return image with { Anchor = anchor, IsFloating = true };
        });

    /// <summary>Puts the selected floating picture at new offsets from its frames (dragged with the mouse); an alignment becomes an offset.</summary>
    public void MoveImage(double offsetX, double offsetY) =>
        ChangeImage("Bild verschieben", image => image.Anchor is { } anchor
            ? image with { Anchor = anchor with { OffsetX = Math.Round(offsetX, 2), OffsetY = Math.Round(offsetY, 2), AlignX = null, AlignY = null } }
            : image);

    /// <summary>A picture's natural size fitted to <paramref name="maximum"/> points of width (the aspect ratio kept).</summary>
    public static (double Width, double Height) Fit((double Width, double Height) size, double maximum)
    {
        var (width, height) = size;
        if (width <= maximum || width <= 0)
        {
            return (Math.Max(1, width), Math.Max(1, height));
        }

        return (maximum, Math.Max(1, height * maximum / width));
    }

    /// <summary>The width text has at block <paramref name="block"/>: the section's text width, in a table cell the cell's.</summary>
    public double AvailableWidth(int block)
    {
        var section = Document.SectionOf(block).Properties.ContentWidth;
        if (Document.ParagraphAt(block)?.Cell is not { } cell || cell.Table.Grid.Count == 0)
        {
            return section;
        }

        var table = cell.Table;
        var column = table.GridColumnOf(cell.Row, cell.Cell);
        var width = table.Grid.Skip(column).Take(Math.Max(1, table.CellAt(cell.Row, cell.Cell).GridSpan)).Sum();
        var margins = Resolver.ResolveTable(table).CellMargins ?? CellMargins.WordDefault;
        return Math.Max(12, width - (margins.Left ?? 0) - (margins.Right ?? 0));
    }

    private void ChangeImage(string label, Func<ImageInline, ImageInline> change)
    {
        if (SelectedImage is not { } selected)
        {
            return;
        }

        var (position, _) = selected;
        var selection = _selection;
        Run(label, EditKind.Other, null, () =>
        {
            ApplyOptional(ContentEdits.FormatParagraphs(Document, position.Block, position.Block, paragraph =>
            {
                var index = paragraph.InlineIndexAt(position.Offset);
                if (index < 0 || paragraph.Inlines[index] is not ImageInline image)
                {
                    return paragraph;
                }

                var inlines = paragraph.Inlines.ToArray();
                inlines[index] = change(image);
                return paragraph.WithInlines(inlines);
            }));
            SetSelectionQuiet(selection);
        });
    }
}

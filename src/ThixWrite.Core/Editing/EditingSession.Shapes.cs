using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// Shapes and text boxes (Einfügen › Formen / Textfeld, Formformat) and what pictures and shapes share: the selected object, its size,
/// place and wrap. A shape Word wrote (kept as XML) becomes an editable <see cref="ShapeInline"/> the first time it is changed.
/// </summary>
public sealed partial class EditingSession
{
    /// <summary>Word's look of a new shape: accent 1 filled, a darker accent 1 outline of 1 pt, white text.</summary>
    public static DrawingShape DefaultShape(string preset) => new(0, 0, 0, 0)
    {
        Preset = preset,
        Fill = new DrawingFill(new DrawingColor(null, "accent1", EquatableArray<(string, int?)>.Empty)),
        Line = new DrawingLine(new DrawingColor(null, "accent1", [("shade", 50000)]), 1),
        TextAnchor = DrawingTextAnchor.Center,
    };

    /// <summary>Word's new text box: white, a thin black outline, an empty paragraph to type into.</summary>
    public static DrawingShape DefaultTextBox() => new(0, 0, 0, 0)
    {
        Preset = "rect",
        Fill = new DrawingFill(DrawingColor.Of("FFFFFF")),
        Line = new DrawingLine(DrawingColor.Of("000000"), 0.5),
        Text = new Story([Paragraph.Empty()]),
    };

    /// <summary>The picture or shape the selection is (exactly that one object selected), or null.</summary>
    public (DocumentPosition Position, Inline Object)? SelectedObject
    {
        get
        {
            var range = _selection.Range;
            if (range.Start.Block != range.End.Block || range.End.Offset - range.Start.Offset != 1
                || Document.ParagraphAt(range.Start.Block)?.InlineAt(range.Start.Offset) is not { } inline
                || !(inline is ImageInline or ShapeInline || inline is OpaqueInline { Graphic.Elements: [DrawingShape] }))
            {
                return null;
            }

            return (range.Start, inline);
        }
    }

    /// <summary>The selected shape (a shape Word wrote as it would be when edited), or null.</summary>
    public ShapeInline? SelectedShape => SelectedObject?.Object switch
    {
        ShapeInline shape => shape,
        OpaqueInline opaque => ToShape(opaque),
        _ => null,
    };

    /// <summary>The size and anchor of an object (picture or shape).</summary>
    public static (double Width, double Height, DrawingAnchor? Anchor) Frame(Inline inline) => inline switch
    {
        ImageInline image => (image.Width, image.Height, image.Anchor),
        ShapeInline shape => (shape.Width, shape.Height, shape.Anchor),
        OpaqueInline { Graphic: { } graphic } => (graphic.Width, graphic.Height, graphic.Anchor),
        _ => (0, 0, null),
    };

    /// <summary>A shape Word wrote (one wps shape kept as XML) as an editable shape.</summary>
    public static ShapeInline? ToShape(OpaqueInline opaque) => opaque.Graphic is { Elements: [DrawingShape shape] } graphic
        ? new ShapeInline(shape with { X = 0, Y = 0, Width = 0, Height = 0 }, graphic.Width, graphic.Height, opaque.Properties, opaque.Link) { Anchor = graphic.Anchor, IsTextBox = shape.Text is not null && shape.Preset == "rect", Revision = opaque.Revision }
        : null;

    /// <summary>"Textumbruch" of the selected object.</summary>
    public PictureWrap? SelectedObjectWrap => SelectedObject is { } selected ? WrapOf(Frame(selected.Object).Anchor) : null;

    private static PictureWrap WrapOf(DrawingAnchor? anchor) => anchor switch
    {
        null => PictureWrap.InLine,
        { Wrap: DrawingWrap.None, BehindText: true } => PictureWrap.BehindText,
        { Wrap: DrawingWrap.None } => PictureWrap.InFrontOfText,
        { Wrap: DrawingWrap.TopAndBottom } => PictureWrap.TopAndBottom,
        _ => PictureWrap.Square,
    };

    /// <summary>
    /// Inserts a shape (or text box) at <paramref name="at"/> – floating at <paramref name="anchor"/> when it is given – and selects it.
    /// </summary>
    public void InsertShape(ShapeInline shape, DocumentPosition at)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Run(shape.IsTextBox ? "Textfeld einfügen" : "Form einfügen", EditKind.Other, null, () =>
        {
            var position = CaretStops.Snap(Document, at);
            var properties = Document.ParagraphAt(position.Block)?.TypingPropertiesAt(position.Offset) ?? CharacterProperties.Empty;
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, new DocumentRange(position, position), [new Paragraph([shape with { Properties = properties with { StyleId = null } }])]));
            var end = map.EndOfInserted;
            SetSelectionQuiet(new Selection(end with { Offset = end.Offset - 1 }, end));
        });
    }

    /// <summary>The size of the selected picture or shape in points (the handles, Höhe / Breite).</summary>
    public void SetObjectSize(double width, double height)
    {
        width = Math.Clamp(width, 1, 22 * 72);
        height = Math.Clamp(height, 1, 22 * 72);
        ChangeObject("Größe", inline => inline switch
        {
            ImageInline image => image.WithSize(width, height),
            ShapeInline shape => shape with { Width = width, Height = height },
            _ => inline,
        });
    }

    /// <summary>Puts the selected floating object at new offsets from its frames (an alignment becomes an offset).</summary>
    public void MoveObject(double offsetX, double offsetY) =>
        ChangeObject("Verschieben", inline =>
        {
            var anchor = Frame(inline).Anchor;
            if (anchor is null)
            {
                return inline;
            }

            var moved = anchor with { OffsetX = Math.Round(offsetX, 2), OffsetY = Math.Round(offsetY, 2), AlignX = null, AlignY = null };
            return inline switch
            {
                ImageInline image => image with { Anchor = moved },
                ShapeInline shape => shape with { Anchor = moved },
                _ => inline,
            };
        });

    /// <summary>"Textumbruch" of the selected object (see <see cref="SetImageWrap"/>).</summary>
    public void SetObjectWrap(PictureWrap wrap, (double X, double Y)? at = null) =>
        ChangeObject("Textumbruch", inline =>
        {
            var anchor = Wrapped(Frame(inline).Anchor, wrap, at);
            return inline switch
            {
                ImageInline image => image with { Anchor = anchor, IsFloating = anchor is not null },
                ShapeInline shape => shape with { Anchor = anchor },
                _ => inline,
            };
        });

    /// <summary>"Fülleffekt" of the selected shape (null: Keine Füllung).</summary>
    public void SetShapeFill(DrawingFill? fill) =>
        ChangeObject("Fülleffekt", inline => inline is ShapeInline shape ? shape with { Shape = shape.Shape with { Fill = fill } } : inline);

    /// <summary>"Formkontur" of the selected shape (null: Keine Kontur).</summary>
    public void SetShapeLine(DrawingLine? line) =>
        ChangeObject("Formkontur", inline => inline is ShapeInline shape ? shape with { Shape = shape.Shape with { Line = line } } : inline);

    /// <summary>The text of the shape at <paramref name="position"/> (its text edited in place); one step.</summary>
    public void SetShapeText(DocumentPosition position, Story text)
    {
        var selection = _selection;
        Run("Text in Form", EditKind.Other, null, () =>
        {
            ReplaceObject(position, inline => inline is ShapeInline shape ? shape with { Shape = shape.Shape with { Text = text } } : inline);
            SetSelectionQuiet(selection);
        });
    }

    private static DrawingAnchor? Wrapped(DrawingAnchor? anchor, PictureWrap wrap, (double X, double Y)? at)
    {
        if (wrap == PictureWrap.InLine)
        {
            return null;
        }

        var floating = anchor ?? new DrawingAnchor(DrawingFrame.Column, at?.X ?? 0, null, DrawingFrame.Paragraph, at?.Y ?? 0, null, DrawingWrap.Square, false,
            9, 0, 9, 0, 251659264);
        return wrap switch
        {
            PictureWrap.TopAndBottom => floating with { Wrap = DrawingWrap.TopAndBottom, BehindText = false },
            PictureWrap.BehindText => floating with { Wrap = DrawingWrap.None, BehindText = true },
            PictureWrap.InFrontOfText => floating with { Wrap = DrawingWrap.None, BehindText = false },
            _ => floating with { Wrap = DrawingWrap.Square, BehindText = false },
        };
    }

    /// <summary>Changes the selected object (a shape Word wrote becomes editable first); the selection stays on it.</summary>
    private void ChangeObject(string label, Func<Inline, Inline> change)
    {
        if (SelectedObject is not { } selected)
        {
            return;
        }

        var selection = _selection;
        Run(label, EditKind.Other, null, () =>
        {
            ReplaceObject(selected.Position, change);
            SetSelectionQuiet(selection);
        });
    }

    private void ReplaceObject(DocumentPosition position, Func<Inline, Inline> change) =>
        ApplyOptional(ContentEdits.FormatParagraphs(Document, position.Block, position.Block, paragraph =>
        {
            var index = paragraph.InlineIndexAt(position.Offset);
            if (index < 0)
            {
                return paragraph;
            }

            var inline = paragraph.Inlines[index] is OpaqueInline opaque && ToShape(opaque) is { } shape ? shape : paragraph.Inlines[index];
            var inlines = paragraph.Inlines.ToArray();
            inlines[index] = change(inline);
            return paragraph.WithInlines(inlines);
        }));
}

using ThixOffice.Text.Drawing.Geometry;

namespace ThixWrite.Core.Model;

/// <summary>What an anchored object's position is measured from (wp:positionH/V relativeFrom).</summary>
public enum DrawingFrame
{
    Page,
    Margin,
    Column,
    Paragraph,
    Line,
    Character,
    LeftMargin,
    RightMargin,
    TopMargin,
    BottomMargin,
    InsideMargin,
    OutsideMargin,
}

/// <summary>How text flows around an anchored object ("Textumbruch").</summary>
public enum DrawingWrap
{
    /// <summary>"Hinter den Text" / "Vor den Text": no effect on the text.</summary>
    None,

    /// <summary>"Quadrat": the text keeps to the sides of the object's rectangle.</summary>
    Square,

    /// <summary>"Passend" (drawn like Square: the wrap polygon is not followed).</summary>
    Tight,

    /// <summary>"Transparent" (drawn like Square).</summary>
    Through,

    /// <summary>"Oben und unten": no text beside the object.</summary>
    TopAndBottom,
}

/// <summary>
/// Where a floating object (wp:anchor) sits and how text flows around it: horizontal and vertical frame with an offset in points or
/// an alignment ("left", "center", "right", "inside", "outside"; "top", "center", "bottom"), the wrap, behind the text, the
/// distances to the text, the z order (relativeHeight). Read for the display; the XML stays with the object.
/// </summary>
public sealed record DrawingAnchor(
    DrawingFrame HorizontalFrame,
    double OffsetX,
    string? AlignX,
    DrawingFrame VerticalFrame,
    double OffsetY,
    string? AlignY,
    DrawingWrap Wrap,
    bool BehindText,
    double DistanceLeft = 9,
    double DistanceTop = 0,
    double DistanceRight = 9,
    double DistanceBottom = 0,
    long ZOrder = 0);

/// <summary>A colour of a drawing: sRGB "RRGGBB" or a scheme colour ("accent1", "tx1" …) with DrawingML transforms (lumMod 75000 …).</summary>
public sealed record DrawingColor(string? Rgb, string? Scheme, EquatableArray<(string Name, int? Value)> Modifiers)
{
    public static DrawingColor Of(string hex) => new(hex, null, EquatableArray<(string, int?)>.Empty);
}

/// <summary>A fill: one colour, or a gradient (stops at 0–1 along <see cref="Angle"/> degrees); null where a drawing has no fill.</summary>
public sealed record DrawingFill(DrawingColor? Color, EquatableArray<(double Position, DrawingColor Color)> Stops = default, double Angle = 90);

/// <summary>An outline: colour, width in points, dash (a:prstDash: "solid", "dash", "sysDot" …).</summary>
public sealed record DrawingLine(DrawingColor Color, double Width, string Dash = "solid");

/// <summary>A shadow outside a shape or picture (a:outerShdw): colour (with its alpha), blur and distance in points, direction in degrees (0 = right, 90 = down).</summary>
public sealed record DrawingShadow(DrawingColor Color, double Blur, double Distance, double Direction)
{
    /// <summary>The offset of the shadow in points.</summary>
    public (double X, double Y) Offset => (Distance * Math.Cos(Direction * Math.PI / 180), Distance * Math.Sin(Direction * Math.PI / 180));
}

/// <summary>Where the text of a shape starts vertically (wps:bodyPr anchor).</summary>
public enum DrawingTextAnchor
{
    Top,
    Center,
    Bottom,
}

/// <summary>One element of a drawing, in points relative to the drawing's frame; rotation in degrees (clockwise), flips.</summary>
public abstract record DrawingElement(double X, double Y, double Width, double Height)
{
    public double Rotation { get; init; }

    public bool FlipHorizontal { get; init; }

    public bool FlipVertical { get; init; }
}

/// <summary>
/// A shape (wps:wsp, a SmartArt's dsp:sp): a preset or custom geometry with adjust values, fill, outline and – for text boxes and
/// shapes with text – its text as a story (w:txbxContent, or DrawingML text turned into paragraphs), laid out inside the insets.
/// </summary>
public sealed record DrawingShape(double X, double Y, double Width, double Height) : DrawingElement(X, Y, Width, Height)
{
    public string Preset { get; init; } = "rect";

    public EquatableArray<GeometryAdjustment> Adjustments { get; init; } = EquatableArray<GeometryAdjustment>.Empty;

    /// <summary>a:custGeom as XML (the shared geometry engine evaluates it), or null.</summary>
    public string? CustomGeometryXml { get; init; }

    public DrawingFill? Fill { get; init; }

    public DrawingLine? Line { get; init; }

    /// <summary>"Schatten" (a:effectLst/a:outerShdw), or null.</summary>
    public DrawingShadow? Shadow { get; init; }

    public Story? Text { get; init; }

    /// <summary>Text insets left, top, right, bottom in points (Word's default 7.2 / 3.6).</summary>
    public (double Left, double Top, double Right, double Bottom) Insets { get; init; } = (7.2, 3.6, 7.2, 3.6);

    public DrawingTextAnchor TextAnchor { get; init; }

    /// <summary>True when the text box grows with its text (a:spAutoFit).</summary>
    public bool AutoFit { get; init; }
}

/// <summary>
/// Text stretched over its box (VML v:textpath with fitshape – Word's text watermark, old WordArt): <see cref="Text"/> in
/// <see cref="FontFamily"/>, filled with <see cref="Color"/> at <see cref="Opacity"/> (1 = solid, Word's "Halbtransparent" 0.5).
/// </summary>
public sealed record DrawingWordArt(double X, double Y, double Width, double Height, string Text, string FontFamily, DrawingColor Color, double Opacity)
    : DrawingElement(X, Y, Width, Height)
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }
}

/// <summary>A picture inside a drawing (pic:pic in a group or canvas).</summary>
public sealed record DrawingPicture(double X, double Y, double Width, double Height, ImageData Image) : DrawingElement(X, Y, Width, Height);

/// <summary>A chart (c:chart): the chart part's XML, drawn from its cached values through ThixOffice.Charts.</summary>
public sealed record DrawingChart(double X, double Y, double Width, double Height, string ChartXml) : DrawingElement(X, Y, Width, Height);

/// <summary>
/// What a drawing ThixWrite keeps as its XML shows: its size, the anchor when it floats, and its elements (a shape, a text box, the
/// shapes of a group with the group's transform applied, a chart, a SmartArt's pre-drawn shapes). Read for the display only – the
/// drawing is written back as read (<see cref="OpaqueInline.Xml"/>).
/// </summary>
public sealed record DrawingGraphic(double Width, double Height, DrawingAnchor? Anchor, EquatableArray<DrawingElement> Elements);

namespace ThixShow.Core.Model;

/// <summary>
/// a:xfrm / p:xfrm: position and size in EMU, rotation (60000ths of a degree, clockwise around the centre) and flips. A group
/// also maps its children's coordinate space (<see cref="ChildOffset"/>/<see cref="ChildExtent"/>) onto its own box.
/// </summary>
public sealed record Transform2D(
    long X,
    long Y,
    long Width,
    long Height,
    int Rotation = 0,
    bool FlipHorizontal = false,
    bool FlipVertical = false,
    long ChildX = 0,
    long ChildY = 0,
    long ChildWidth = 0,
    long ChildHeight = 0)
{
    public EmuRect Bounds => new(X, Y, Width, Height);

    public bool HasChildSpace => ChildWidth != 0 || ChildHeight != 0 || ChildX != 0 || ChildY != 0;

    public Transform2D WithBounds(EmuRect bounds) => this with { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height };

    public static Transform2D Of(EmuRect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}

/// <summary>The placeholder types of PresentationML (ST_PlaceholderType).</summary>
public enum PlaceholderType
{
    Title,
    Body,
    CenteredTitle,
    Subtitle,
    DateAndTime,
    SlideNumber,
    Footer,
    Header,
    Object,
    Chart,
    Table,
    ClipArt,
    Diagram,
    Media,
    SlideImage,
    Picture,
}

/// <summary>
/// p:ph: the placeholder a shape is (type, index; orientation "vert", size "half"/"quarter"). A slide's placeholder inherits
/// position, geometry, fill, line, body properties and list styles from the layout's placeholder with the same index (else the
/// same type) and that from the master's placeholder of the same type.
/// </summary>
public sealed record PlaceholderInfo(PlaceholderType Type = PlaceholderType.Object, uint? Index = null, string? Orientation = null, string? Size = null, bool HasCustomPrompt = false)
{
    /// <summary>The type the master matches: centred title → title, subtitle/object/chart/… → body.</summary>
    public PlaceholderType MasterType => Type switch
    {
        PlaceholderType.CenteredTitle => PlaceholderType.Title,
        PlaceholderType.Subtitle or PlaceholderType.Object or PlaceholderType.Chart or PlaceholderType.Table or PlaceholderType.ClipArt
            or PlaceholderType.Diagram or PlaceholderType.Media or PlaceholderType.Picture => PlaceholderType.Body,
        _ => Type,
    };

    /// <summary>True for the title kinds (they take the master's title style).</summary>
    public bool IsTitle => Type is PlaceholderType.Title or PlaceholderType.CenteredTitle;

    /// <summary>True for date, footer, slide number and header (the master's "other" placeholders).</summary>
    public bool IsFooterKind => Type is PlaceholderType.DateAndTime or PlaceholderType.Footer or PlaceholderType.SlideNumber or PlaceholderType.Header;
}

/// <summary>
/// A shape's geometry: a preset (a:prstGeom: "rect", "roundRect", "rightArrow" … with adjust values by name) or a custom geometry
/// (a:custGeom, kept as XML and evaluated by the same guide engine). <see cref="Drawing.GeometryEngine"/> turns it into paths.
/// </summary>
public sealed record ShapeGeometry(string? Preset, ValueList<GeometryAdjust> Adjustments = default, XmlFragment? Custom = null)
{
    public static ShapeGeometry Rectangle { get; } = new("rect");

    public static ShapeGeometry Of(string preset) => new(preset);

    public bool IsCustom => Custom is not null;

    /// <summary>The adjust value <paramref name="name"/> if the shape sets it.</summary>
    public long? AdjustOf(string name) => Adjustments.FirstOrDefault(adjust => adjust.Name == name)?.Value;

    public ShapeGeometry WithAdjust(string name, long value)
    {
        var index = Adjustments.FindIndex(adjust => adjust.Name == name);
        var adjust = new GeometryAdjust(name, value);
        return this with { Adjustments = index < 0 ? Adjustments.Add(adjust) : Adjustments.SetItem(index, adjust) };
    }
}

/// <summary>An adjust value of a preset (a:gd name="adj" fmla="val 16667").</summary>
public sealed record GeometryAdjust(string Name, long Value);

/// <summary>A connection of a connector's end to a shape's connection site (a:stCxn / a:endCxn).</summary>
public sealed record Connection(int ShapeId, int Site);

/// <summary>
/// Something on a slide (p:spTree child). Every element has the non-visual properties of its p:cNvPr (id unique on the slide,
/// name, alternative text = description and title, hidden, click/hover actions) and maybe a placeholder; <see cref="Source"/>
/// keeps the element's XML as read so that what ThixShow does not own survives (written back when the model is unchanged and
/// patched otherwise); <see cref="References"/> names the parts its relationship ids point at.
/// </summary>
public abstract record SlideElement
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary>The alternative text (cNvPr descr).</summary>
    public string? Description { get; init; }

    /// <summary>The title of the alternative text (cNvPr title).</summary>
    public string? AltTitle { get; init; }

    public bool Hidden { get; init; }

    /// <summary>The position; null for a placeholder that inherits it.</summary>
    public Transform2D? Transform { get; init; }

    public PlaceholderInfo? Placeholder { get; init; }

    /// <summary>The action of a click on the shape in the slide show (a:hlinkClick of cNvPr).</summary>
    public Hyperlink? Click { get; init; }

    public Hyperlink? Hover { get; init; }

    public XmlFragment? Source { get; init; }

    public ValueList<PartReference> References { get; init; }

    /// <summary>The German name of the kind ("Rechteck", "Bild", "Gruppe" …) for the selection pane.</summary>
    public abstract string KindLabel { get; }
}

/// <summary>
/// p:sp: an auto shape (preset or custom geometry) with fill, outline, effects, theme style and text, or a text box (txBox).
/// Null fill/outline/effects mean: from the style (p:style) or the placeholder it inherits from.
/// </summary>
public sealed record AutoShape : SlideElement
{
    public ShapeGeometry? Geometry { get; init; }

    public Fill? Fill { get; init; }

    public Outline? Outline { get; init; }

    public ShapeEffects? Effects { get; init; }

    public ShapeStyle? Style { get; init; }

    public TextBody? Text { get; init; }

    public bool IsTextBox { get; init; }

    /// <summary>Extra non-visual shape properties as read (p:cNvSpPr locks) – kept.</summary>
    public XmlFragment? ShapeLocks { get; init; }

    public override string KindLabel => Placeholder is not null ? "Platzhalter" : IsTextBox ? "Textfeld" : Geometry?.Preset switch
    {
        "rect" => "Rechteck",
        "roundRect" => "Abgerundetes Rechteck",
        "ellipse" => "Ellipse",
        "triangle" => "Dreieck",
        "line" => "Linie",
        null => "Freihandform",
        _ => "Form",
    };
}

/// <summary>p:cxnSp: a line or connector between shapes (its own geometry: "line", "straightConnector1", "bentConnector3" …).</summary>
public sealed record ConnectorShape : SlideElement
{
    public ShapeGeometry Geometry { get; init; } = ShapeGeometry.Of("line");

    public Fill? Fill { get; init; }

    public Outline? Outline { get; init; }

    public ShapeEffects? Effects { get; init; }

    public ShapeStyle? Style { get; init; }

    public Connection? Start { get; init; }

    public Connection? End { get; init; }

    public override string KindLabel => "Verbinder";
}

/// <summary>The media a picture stands for (p:nvPr's a:videoFile / a:audioFile / p14:media – kept, the poster frame shows).</summary>
public enum MediaKind
{
    None,
    Video,
    Audio,
}

/// <summary>
/// p:pic: a picture with its crop and fill mode (in <see cref="Picture"/>), clipped to its geometry (rect unless cropped to a
/// shape), with outline and effects. A video or audio is a picture whose poster frame is shown (<see cref="Media"/>).
/// </summary>
public sealed record PictureShape : SlideElement
{
    public required PictureFill Picture { get; init; }

    public ShapeGeometry Geometry { get; init; } = ShapeGeometry.Rectangle;

    public Outline? Outline { get; init; }

    public ShapeEffects? Effects { get; init; }

    public ShapeStyle? Style { get; init; }

    public MediaKind Media { get; init; }

    /// <summary>p:cNvPicPr's children as read (locks) – kept.</summary>
    public XmlFragment? PictureLocks { get; init; }

    public override string KindLabel => Media switch
    {
        MediaKind.Video => "Video",
        MediaKind.Audio => "Audio",
        _ => "Bild",
    };
}

/// <summary>p:grpSp: shapes grouped; the group's transform maps the children's space (nested groups multiply).</summary>
public sealed record GroupShape : SlideElement
{
    public ValueList<SlideElement> Children { get; init; }

    public Fill? Fill { get; init; }

    public ShapeEffects? Effects { get; init; }

    public override string KindLabel => "Gruppe";
}

/// <summary>What a graphic frame holds.</summary>
public enum GraphicKind
{
    Table,
    Chart,
    Diagram,
    OleObject,
    Other,
}

/// <summary>
/// p:graphicFrame: a table (modelled and editable), or a chart, SmartArt diagram, OLE object … kept as XML with its parts and
/// drawn as a placeholder – or by the chart seam (<see cref="Layout.IChartRenderer"/>) for charts. <see cref="ChartPartUri"/>
/// names the chart part a renderer reads.
/// </summary>
public sealed record GraphicFrame : SlideElement
{
    public GraphicKind Kind { get; init; }

    public Table? Table { get; init; }

    /// <summary>The graphic data's uri (a:graphicData uri) as read.</summary>
    public string? GraphicUri { get; init; }

    /// <summary>The chart part ("/ppt/charts/chart1.xml") of a chart frame.</summary>
    public string? ChartPartUri { get; init; }

    /// <summary>The fallback picture of an OLE object or other content (when the file has one) – drawn in its place.</summary>
    public ImageData? Fallback { get; init; }

    public override string KindLabel => Kind switch
    {
        GraphicKind.Table => "Tabelle",
        GraphicKind.Chart => "Diagramm",
        GraphicKind.Diagram => "SmartArt-Grafik",
        GraphicKind.OleObject => "Objekt",
        _ => "Grafik",
    };
}

/// <summary>
/// Something in the shape tree ThixShow does not know (mc:AlternateContent without a known choice, p:contentPart …): kept and
/// written back, drawn as nothing (or its transform's frame when it has one).
/// </summary>
public sealed record PreservedElement : SlideElement
{
    public override string KindLabel => "Unbekannter Inhalt";
}

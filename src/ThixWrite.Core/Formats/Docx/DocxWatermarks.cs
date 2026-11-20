using System.Globalization;
using System.Security;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Watermarks as Word writes them into the headers: a text watermark is a VML WordArt shape (v:shape id "PowerPlusWaterMarkObject…",
/// type _x0000_t136 with a v:textpath) – kept as an <see cref="OpaqueInline"/> whose graphic shows it; a picture watermark is a VML
/// picture (id "WordPictureWatermark…", v:imagedata with gain/blacklevel) – read into a washed-out floating <see cref="ImageInline"/>.
/// Both are centred on the text area, behind the text.
/// </summary>
internal static class DocxWatermarks
{
    private const string TextShapeId = "PowerPlusWaterMarkObject";
    private const string PictureShapeId = "WordPictureWatermark";
    private const string Namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:v=\"urn:schemas-microsoft-com:vml\" "
        + "xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w10=\"urn:schemas-microsoft-com:office:word\" "
        + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private const string Position =
        "position:absolute;margin-left:0;margin-top:0;width:{0}pt;height:{1}pt;{2}z-index:-251657216;mso-position-horizontal:center;"
        + "mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin";

    /// <summary>Word's shape type of WordArt text (o:spt 136).</summary>
    private const string TextShapeType =
        "<v:shapetype id=\"_x0000_t136\" coordsize=\"21600,21600\" o:spt=\"136\" adj=\"10800\" path=\"m@7,l@8,m@5,21600l@6,21600e\">"
        + "<v:formulas><v:f eqn=\"sum #0 0 10800\"/><v:f eqn=\"prod #0 2 1\"/><v:f eqn=\"sum 21600 0 @1\"/><v:f eqn=\"sum 0 0 @2\"/>"
        + "<v:f eqn=\"sum 21600 0 @3\"/><v:f eqn=\"if @0 @3 0\"/><v:f eqn=\"if @0 21600 @1\"/><v:f eqn=\"if @0 0 @2\"/><v:f eqn=\"if @0 @4 21600\"/>"
        + "<v:f eqn=\"mid @5 @6\"/><v:f eqn=\"mid @8 @5\"/><v:f eqn=\"mid @7 @8\"/><v:f eqn=\"mid @6 @7\"/><v:f eqn=\"sum @6 0 @5\"/></v:formulas>"
        + "<v:path textpathok=\"t\" o:connecttype=\"custom\" o:connectlocs=\"@9,0;@10,10800;@11,21600;@12,10800\" o:connectangles=\"270,180,90,0\"/>"
        + "<v:textpath on=\"t\" fitshape=\"t\"/><v:handles><v:h position=\"#0,bottomRight\" xrange=\"6629,14971\"/></v:handles>"
        + "<o:lock v:ext=\"edit\" text=\"t\" shapetype=\"t\"/></v:shapetype>";

    /// <summary>Word's shape type of a picture (o:spt 75).</summary>
    private const string PictureShapeType =
        "<v:shapetype id=\"_x0000_t75\" coordsize=\"21600,21600\" o:spt=\"75\" o:preferrelative=\"t\" path=\"m@4@5l@4@11@9@11@9@5xe\" filled=\"f\" stroked=\"f\">"
        + "<v:stroke joinstyle=\"miter\"/><v:formulas><v:f eqn=\"if lineDrawn pixelLineWidth 0\"/><v:f eqn=\"sum @0 1 0\"/><v:f eqn=\"sum 0 0 @1\"/>"
        + "<v:f eqn=\"prod @2 1 2\"/><v:f eqn=\"prod @3 21600 pixelWidth\"/><v:f eqn=\"prod @3 21600 pixelHeight\"/><v:f eqn=\"sum @0 0 1\"/>"
        + "<v:f eqn=\"prod @6 1 2\"/><v:f eqn=\"prod @7 21600 pixelWidth\"/><v:f eqn=\"sum @8 21600 0\"/><v:f eqn=\"prod @7 21600 pixelHeight\"/>"
        + "<v:f eqn=\"sum @10 21600 0\"/></v:formulas><v:path o:extrusionok=\"f\" gradientshapeok=\"t\" o:connecttype=\"rect\"/>"
        + "<o:lock v:ext=\"edit\" aspectratio=\"t\"/></v:shapetype>";

    /// <summary>True for Word's text watermark (kept as its XML).</summary>
    public static bool IsTextWatermark(Inline inline) => inline is OpaqueInline { Kind: PreservedKind.Drawing } opaque && opaque.Xml.Contains(TextShapeId, StringComparison.Ordinal);

    /// <summary>True for a text or picture watermark.</summary>
    public static bool IsWatermark(Inline inline) => IsTextWatermark(inline) || inline is ImageInline { IsWatermark: true };

    /// <summary>
    /// A text watermark for a text area of <paramref name="areaWidth"/>: Word's VML (number <paramref name="number"/> of the document,
    /// so shape ids stay unique) and what it shows – the same the reader makes of it.
    /// </summary>
    public static OpaqueInline TextInline(Watermark watermark, double areaWidth, int number)
    {
        var text = (watermark.Text ?? "").Trim();
        var ratio = Math.Max(1.2, text.Length * 0.37); // width : height of capitals in Word's watermark box
        var width = watermark.Diagonal ? 1.05 * areaWidth * Math.Sqrt(2) / (1 + 1 / ratio) : areaWidth;
        var height = width / ratio;
        string Number(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
        var style = string.Format(CultureInfo.InvariantCulture, Position, Number(width), Number(height), watermark.Diagonal ? "rotation:315;" : "");
        var fill = watermark.SemiTransparent ? "<v:fill opacity=\".5\"/>" : "";
        var xml = $"<w:pict {Namespaces}>{TextShapeType}"
            + $"<v:shape id=\"{TextShapeId}{number}\" o:spid=\"_x0000_s{2048 + number}\" type=\"#_x0000_t136\" style=\"{style}\" o:allowincell=\"f\" fillcolor=\"#{watermark.Color}\" stroked=\"f\">"
            + $"{fill}<v:textpath style=\"font-family:&quot;{SecurityElement.Escape(watermark.FontFamily)}&quot;;font-size:1pt\" string=\"{SecurityElement.Escape(text)}\"/>"
            + "<w10:wrap anchorx=\"margin\" anchory=\"margin\"/></v:shape></w:pict>";
        var element = Parse(xml);
        return new OpaqueInline(XmlFragments.Canonical(element), PreservedKind.Drawing, true, CharacterProperties.Empty) { Graphic = DocxDrawings.ReadVmlShape(element) };
    }

    /// <summary>A picture watermark: the picture washed out, centred behind the text, at its scale (0 = as large as fits the text area).</summary>
    public static ImageInline PictureInline(Watermark watermark, double areaWidth, double areaHeight)
    {
        var image = watermark.Picture!;
        var (naturalWidth, naturalHeight) = image.NaturalSize;
        var scale = watermark.Scale > 0 ? watermark.Scale : Math.Min(areaWidth / Math.Max(1, naturalWidth), areaHeight / Math.Max(1, naturalHeight));
        return new ImageInline(image, Math.Max(1, naturalWidth * scale), Math.Max(1, naturalHeight * scale), CharacterProperties.Empty)
        {
            Anchor = CenteredAnchor,
            Washout = watermark.Washout,
            IsWatermark = true,
        };
    }

    /// <summary>Where Word puts a watermark: centred on the text area, behind the text, no wrap.</summary>
    public static DrawingAnchor CenteredAnchor { get; } = new(DrawingFrame.Margin, 0, "center", DrawingFrame.Margin, 0, "center", DrawingWrap.None, true);

    /// <summary>A VML picture watermark (WordPictureWatermark) of a w:pict, or null.</summary>
    public static ImageInline? ReadPicture(W.Picture pict, OpenXmlPart part, CharacterProperties properties)
    {
        var shape = pict.ChildElements.FirstOrDefault(child => child.LocalName == "shape");
        if (shape is null || DocxValues.AttributeValue(shape, "id")?.StartsWith(PictureShapeId, StringComparison.Ordinal) != true
            || shape.ChildElements.FirstOrDefault(child => child.LocalName == "imagedata") is not { } data
            || data.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "id" && attribute.NamespaceUri == DocxValues.RelationshipNamespace).Value is not { } id)
        {
            return null;
        }

        try
        {
            if (part.GetPartById(id) is not ImagePart imagePart || ImageData.FromBytes(Bytes(imagePart)) is not { } image)
            {
                return null;
            }

            var style = DocxDrawings.Style(DocxValues.AttributeValue(shape, "style"));
            var width = DocxDrawings.Length(style.GetValueOrDefault("width"));
            var height = DocxDrawings.Length(style.GetValueOrDefault("height"));
            if (width <= 0 || height <= 0)
            {
                (width, height) = image.NaturalSize;
            }

            return new ImageInline(image, width, height, properties, SourceXml: XmlFragments.Canonical(pict), SourcePart: id, IsFloating: true)
            {
                Anchor = DocxDrawings.VmlAnchor(pict, style) ?? CenteredAnchor,
                IsWatermark = true,
                Washout = DocxValues.AttributeValue(data, "gain") is not null || DocxValues.AttributeValue(data, "blacklevel") is not null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>The w:pict of a new picture watermark (its picture in a media part of the header).</summary>
    public static OpenXmlElement WritePicture(ImageInline image, string relationshipId, uint number)
    {
        string Number(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
        var style = string.Format(CultureInfo.InvariantCulture, Position, Number(image.Width), Number(image.Height), "");
        var washout = image.Washout ? " gain=\"19661f\" blacklevel=\"22938f\"" : "";
        return Parse($"<w:pict {Namespaces}>{PictureShapeType}"
            + $"<v:shape id=\"{PictureShapeId}{number}\" o:spid=\"_x0000_s{2048 + number}\" type=\"#_x0000_t75\" style=\"{style}\" o:allowincell=\"f\">"
            + $"<v:imagedata r:id=\"{relationshipId}\" o:title=\"\"{washout}/></v:shape></w:pict>");
    }

    private static OpenXmlElement Parse(string xml) => XmlFragments.Parse<W.Run>(xml).Single();

    private static byte[] Bytes(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

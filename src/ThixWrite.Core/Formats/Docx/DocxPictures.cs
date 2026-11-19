using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using Dw = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Pictures in WordprocessingML: a w:drawing with one picture (pic:pic, a:blip r:embed → an image part) becomes an
/// <see cref="ImageInline"/> – inline (wp:inline) or, shown in the text flow, floating (wp:anchor). Writing puts the drawing as read
/// back while the picture is unchanged (cropping, effects and the position of a floating picture stay); a new or resized picture
/// gets a plain wp:inline and its media part (each picture once per file).
/// </summary>
internal static class DocxPictures
{
    private const double EmuPerPoint = 12700;
    private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>The picture of a w:drawing, or null when it is something else (a chart, a shape, a group, an EMF …).</summary>
    public static ImageInline? Read(W.Drawing drawing, OpenXmlPart main, CharacterProperties properties, Hyperlink? link)
    {
        var inline = drawing.GetFirstChild<Dw.Inline>();
        var anchor = drawing.GetFirstChild<Dw.Anchor>();
        OpenXmlCompositeElement? container = inline is not null ? inline : anchor;
        if (container is null)
        {
            return null;
        }

        var data = container.Descendants<A.GraphicData>().FirstOrDefault();
        var blips = container.Descendants<A.Blip>().ToList();
        if (data?.Uri?.Value != PictureUri || blips.Count != 1 || blips[0].Embed?.Value is not { } id)
        {
            return null;
        }

        ImagePart? part;
        try
        {
            part = main.GetPartById(id) as ImagePart;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        if (part is null || ImageData.FromBytes(ReadAll(part)) is not { } image)
        {
            return null;
        }

        var extent = inline?.Extent ?? anchor?.GetFirstChild<Dw.Extent>();
        var width = DocxValues.EmuToPoints(extent?.Cx?.Value ?? 0);
        var height = DocxValues.EmuToPoints(extent?.Cy?.Value ?? 0);
        if (width <= 0 || height <= 0)
        {
            (width, height) = image.NaturalSize;
        }

        var properties2 = container.GetFirstChild<Dw.DocProperties>();
        var description = properties2?.Description?.Value;
        return new ImageInline(image, width, height, properties, string.IsNullOrEmpty(description) ? null : description, link,
            XmlFragments.Canonical(drawing), id, IsFloating: inline is null);
    }

    /// <summary>The w:drawing of a picture (the one read, when it can stay; else a new wp:inline).</summary>
    public static IEnumerable<OpenXmlElement> Write(ImageInline image, PictureContext context)
    {
        if (image.SourceXml is { } xml && image.SourcePart is { } part && context.CanReuse(part, image.Image) && context.ClaimDocPr(xml))
        {
            return XmlFragments.Parse<W.Run>(xml);
        }

        return [Create(image, context.PartFor(image.Image), context.NextId())];
    }

    private static W.Drawing Create(ImageInline image, string relationshipId, uint id)
    {
        var cx = (long)Math.Round(image.Width * EmuPerPoint);
        var cy = (long)Math.Round(image.Height * EmuPerPoint);
        var name = $"Bild {id}";
        var description = image.Description ?? "";
        var picture = new Pic.Picture(
            new Pic.NonVisualPictureProperties(
                new Pic.NonVisualDrawingProperties { Id = 0U, Name = name + image.Image.Extension, Description = description },
                new Pic.NonVisualPictureDrawingProperties()),
            new Pic.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
            new Pic.ShapeProperties(
                new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        var inline = new Dw.Inline(
            new Dw.Extent { Cx = cx, Cy = cy },
            new Dw.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Dw.DocProperties { Id = id, Name = name, Description = description },
            new Dw.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(picture) { Uri = PictureUri }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };
        return new W.Drawing(inline);
    }

    private static byte[] ReadAll(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The media parts and drawing ids of one write: a picture's part is made once, ids never repeat.</summary>
    internal sealed class PictureContext(OpenXmlPart main)
    {
        private readonly Dictionary<string, string> _parts = new(StringComparer.Ordinal);
        private readonly HashSet<uint> _ids = [];
        private uint _next = 1000;

        /// <summary>True when relationship <paramref name="id"/> is an image part with exactly these bytes.</summary>
        public bool CanReuse(string id, ImageData image)
        {
            try
            {
                if (main.GetPartById(id) is not ImagePart part)
                {
                    return false;
                }

                if (_parts.TryGetValue(image.Key, out var known))
                {
                    return known == id;
                }

                var reusable = ImageData.FromBytes(ReadAll(part))?.Key == image.Key;
                if (reusable)
                {
                    _parts[image.Key] = id;
                }

                return reusable;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        /// <summary>Takes the wp:docPr id of a drawing written as read; false when another drawing of this write has it.</summary>
        public bool ClaimDocPr(string xml)
        {
            var drawing = XmlFragments.Parse<W.Run>(xml).FirstOrDefault();
            var id = drawing?.Descendants<Dw.DocProperties>().FirstOrDefault()?.Id?.Value;
            return id is null || _ids.Add(id.Value);
        }

        /// <summary>A drawing id no drawing of this write has.</summary>
        public uint NextId()
        {
            while (!_ids.Add(_next))
            {
                _next++;
            }

            return _next;
        }

        /// <summary>The relationship id of the media part with this picture (made on first use).</summary>
        public string PartFor(ImageData image)
        {
            if (_parts.TryGetValue(image.Key, out var id))
            {
                return id;
            }

            var part = main switch
            {
                HeaderPart header => header.AddImagePart(image.ContentType),
                FooterPart footer => footer.AddImagePart(image.ContentType),
                _ => ((MainDocumentPart)main).AddImagePart(image.ContentType),
            };
            using (var stream = new MemoryStream(image.Bytes, writable: false))
            {
                part.FeedData(stream);
            }

            id = main.GetIdOfPart(part);
            _parts[image.Key] = id;
            return id;
        }
    }
}

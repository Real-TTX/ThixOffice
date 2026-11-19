using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>
/// Reads .pptx/.pptm into the model with the Open XML SDK for the package and LINQ to XML for the parts: presentation (sizes,
/// default text style, sections), masters with their themes and layouts, slides with notes, transitions and timing, table
/// styles, document properties. The original bytes are kept (<see cref="Presentation.Package"/>) for the writer; what ThixShow
/// does not show is listed in German in <see cref="Presentation.UnsupportedContent"/>. Macros are never run.
/// </summary>
public static class PptxReader
{
    public static Presentation Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        using var package = PresentationDocument.Open(new MemoryStream(bytes), false);
        var presentationPart = package.PresentationPart ?? throw new InvalidDataException("Die Datei enthält keine Präsentation.");
        var context = new ReadContext(package);
        var root = Load(presentationPart);

        // Masters, layouts, themes.
        var masters = new List<SlideMaster>();
        var layoutIds = new Dictionary<OpenXmlPart, uint>();
        foreach (var masterId in root.Element(P + "sldMasterIdLst")?.Elements(P + "sldMasterId") ?? [])
        {
            if (masterId.Attribute(R + "id")?.Value is not { } relationshipId || presentationPart.GetPartById(relationshipId) is not SlideMasterPart masterPart)
            {
                continue;
            }

            masters.Add(ReadMaster(masterPart, masterId.UInt("id") ?? 0, context, layoutIds));
        }

        // Slides.
        var slideIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        var slideEntries = new List<(uint Id, SlidePart Part)>();
        foreach (var slideId in root.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? [])
        {
            if (slideId.Attribute(R + "id")?.Value is { } relationshipId && presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                var id = slideId.UInt("id") ?? 0;
                slideEntries.Add((id, slidePart));
                slideIds[slidePart.Uri.OriginalString] = id;
            }
        }

        context.SlideIds = slideIds;
        var slides = slideEntries.Select(entry => ReadSlide(entry.Part, entry.Id, context, layoutIds)).ToList();

        (ValueList<TableStyle> Styles, string? Default) tableStyles = presentationPart.TableStylesPart is { } stylesPart ? TableStyleXml.Read(Load(stylesPart)) : (ValueList<TableStyle>.Empty, null);
        var properties = package.PackageProperties;
        var presentation = new Presentation
        {
            SlideSize = new SlideSize(root.Element(P + "sldSz").Long("cx") ?? 12192000, root.Element(P + "sldSz").Long("cy") ?? 6858000, root.Element(P + "sldSz").Attr("type")),
            NotesSize = new EmuSize(root.Element(P + "notesSz").Long("cx") ?? 6858000, root.Element(P + "notesSz").Long("cy") ?? 9144000),
            DefaultTextStyle = DrawingXml.ReadListStyle(root.Element(P + "defaultTextStyle"), new PartLinks(presentationPart, context)),
            Masters = masters,
            Slides = slides,
            Sections = ReadSections(root),
            NotesMasterPartUri = presentationPart.NotesMasterPart?.Uri.OriginalString,
            TableStyles = tableStyles.Styles,
            DefaultTableStyleId = tableStyles.Default,
            Title = properties.Title,
            Author = properties.Creator,
            IsMacroEnabled = package.DocumentType == DocumentFormat.OpenXml.PresentationDocumentType.MacroEnabledPresentation || presentationPart.VbaProjectPart is not null,
            Source = XmlFragment.From(root),
            Package = new PreservedPackage(bytes),
            OriginalSlides = slides.ToDictionary(slide => slide.Id),
        };
        return presentation with { UnsupportedContent = Unsupported(package, presentation) };
    }

    internal static XElement Load(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace).Root ?? new XElement("empty");
    }

    private static SlideMaster ReadMaster(SlideMasterPart part, uint id, ReadContext context, Dictionary<OpenXmlPart, uint> layoutIds)
    {
        var root = Load(part);
        var links = new PartLinks(part, context);
        var theme = part.ThemePart is { } themePart ? ThemeXml.Read(Load(themePart), new PartLinks(themePart, context)) : BuiltInThemes.ThixShow.Theme;
        var layouts = new List<SlideLayout>();
        foreach (var layoutId in root.Element(P + "sldLayoutIdLst")?.Elements(P + "sldLayoutId") ?? [])
        {
            if (layoutId.Attribute(R + "id")?.Value is { } relationshipId && part.GetPartById(relationshipId) is SlideLayoutPart layoutPart)
            {
                var identifier = layoutId.UInt("id") ?? 0;
                layoutIds[layoutPart] = identifier;
                layouts.Add(ReadLayout(layoutPart, identifier, context));
            }
        }

        var styles = root.Element(P + "txStyles");
        var common = ReadCommon(root, links, context);
        return new SlideMaster
        {
            Id = id,
            Theme = theme,
            ColorMap = ReadColorMap(root.Element(P + "clrMap")) ?? ColorMap.Default,
            TextStyles = new MasterTextStyles(
                DrawingXml.ReadListStyle(styles?.Element(P + "titleStyle"), links),
                DrawingXml.ReadListStyle(styles?.Element(P + "bodyStyle"), links),
                DrawingXml.ReadListStyle(styles?.Element(P + "otherStyle"), links)),
            Layouts = layouts,
            HeaderFooter = ReadHeaderFooter(root.Element(P + "hf")),
            ThemePartUri = part.ThemePart?.Uri.OriginalString,
            Preserve = root.Bool("preserve") ?? false,
            Shapes = common.Shapes,
            Background = common.Background,
            Name = common.Name,
            Source = XmlFragment.From(root),
            PartUri = part.Uri.OriginalString,
            References = context.ReferencesOf(part, root),
        };
    }

    private static SlideLayout ReadLayout(SlideLayoutPart part, uint id, ReadContext context)
    {
        var root = Load(part);
        var common = ReadCommon(root, new PartLinks(part, context), context);
        return new SlideLayout
        {
            Id = id,
            Type = root.Attr("type") ?? "cust",
            MatchingName = root.Attr("matchingName"),
            ShowMasterShapes = root.Bool("showMasterSp") ?? true,
            ShowMasterPlaceholderAnimations = root.Bool("showMasterPhAnim") ?? false,
            Preserve = root.Bool("preserve") ?? false,
            UserDrawn = root.Bool("userDrawn") ?? false,
            ColorMapOverride = ReadColorMap(root.Element(P + "clrMapOvr")?.Element(A + "overrideClrMapping")),
            HeaderFooter = ReadHeaderFooter(root.Element(P + "hf")),
            Shapes = common.Shapes,
            Background = common.Background,
            Name = common.Name,
            Source = XmlFragment.From(root),
            PartUri = part.Uri.OriginalString,
            References = context.ReferencesOf(part, root),
        };
    }

    private static Slide ReadSlide(SlidePart part, uint id, ReadContext context, Dictionary<OpenXmlPart, uint> layoutIds)
    {
        var root = Load(part);
        var links = new PartLinks(part, context);
        var common = ReadCommon(root, links, context);
        NotesSlide? notes = null;
        if (part.NotesSlidePart is { } notesPart)
        {
            var notesRoot = Load(notesPart);
            var notesCommon = ReadCommon(notesRoot, new PartLinks(notesPart, context), context);
            notes = new NotesSlide
            {
                ColorMapOverride = ReadColorMap(notesRoot.Element(P + "clrMapOvr")?.Element(A + "overrideClrMapping")),
                Shapes = notesCommon.Shapes,
                Background = notesCommon.Background,
                Name = notesCommon.Name,
                Source = XmlFragment.From(notesRoot),
                PartUri = notesPart.Uri.OriginalString,
                References = context.ReferencesOf(notesPart, notesRoot),
            };
        }

        return new Slide
        {
            Id = id,
            LayoutId = part.SlideLayoutPart is { } layout && layoutIds.TryGetValue(layout, out var layoutId) ? layoutId : 0,
            Hidden = root.Bool("show") == false,
            ShowMasterShapes = root.Bool("showMasterSp") ?? true,
            ColorMapOverride = ReadColorMap(root.Element(P + "clrMapOvr")?.Element(A + "overrideClrMapping")),
            Transition = TimingXml.ReadTransition(root),
            Timing = TimingXml.ReadTiming(root.Element(P + "timing")),
            Notes = notes,
            Shapes = common.Shapes,
            Background = common.Background,
            Name = common.Name,
            Source = XmlFragment.From(root),
            PartUri = part.Uri.OriginalString,
            References = context.ReferencesOf(part, root),
        };
    }

    private sealed record Common(ValueList<SlideElement> Shapes, Background? Background, string? Name);

    private static Common ReadCommon(XElement root, PartLinks links, ReadContext context)
    {
        var data = root.Element(P + "cSld");
        var tree = data?.Element(P + "spTree");
        var shapes = tree is null ? ValueList<SlideElement>.Empty : ShapeXml.ReadShapes(tree, links, element => context.ReferencesOf(links.Part, element));
        return new Common(shapes, ReadBackground(data?.Element(P + "bg"), links), data.Attr("name"));
    }

    internal static Background? ReadBackground(XElement? background, IReadLinks links)
    {
        if (background is null)
        {
            return null;
        }

        if (background.Element(P + "bgPr") is { } properties)
        {
            var effects = DrawingXml.ReadEffects(properties);
            return new Background(DrawingXml.ReadFill(properties, links), null, properties.Bool("shadeToTitle") ?? false, effects == ShapeEffects.Empty ? null : effects);
        }

        if (background.Element(P + "bgRef") is { } reference)
        {
            return new Background(null, new StyleReference(reference.Int("idx") ?? 1001, DrawingXml.ReadColor(reference)));
        }

        return new Background();
    }

    internal static ColorMap? ReadColorMap(XElement? element) =>
        element is null ? null : ColorMap.FromValues(name => element.Attr(name));

    private static HeaderFooterFlags? ReadHeaderFooter(XElement? element) => element is null
        ? null
        : new HeaderFooterFlags(element.Bool("dt") ?? true, element.Bool("ftr") ?? true, element.Bool("hdr") ?? true, element.Bool("sldNum") ?? true);

    private static ValueList<SlideSection> ReadSections(XElement root)
    {
        var list = root.Element(P + "extLst")?.Elements(P + "ext")
            .FirstOrDefault(ext => ext.Attr("uri") == SectionsExtension)?
            .Element(P14 + "sectionLst");
        if (list is null)
        {
            return ValueList<SlideSection>.Empty;
        }

        return list.Elements(P14 + "section").Select(section => new SlideSection(
            section.Attr("name") ?? "",
            section.Attr("id") ?? Guid.NewGuid().ToString("B").ToUpperInvariant(),
            section.Element(P14 + "sldIdLst")?.Elements(P14 + "sldId").Select(slide => slide.UInt("id") ?? 0).ToList() ?? [])).ToList();
    }

    private static ValueList<UnsupportedContent> Unsupported(PresentationDocument package, Presentation presentation)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string kind, int count = 1)
        {
            if (count > 0)
            {
                counts[kind] = counts.GetValueOrDefault(kind) + count;
            }
        }

        foreach (var shape in presentation.Slides.SelectMany(slide => ShapeTree.All(slide.Shapes)))
        {
            switch (shape)
            {
                case GraphicFrame { Kind: GraphicKind.Chart }:
                    Count("chart");
                    break;
                case GraphicFrame { Kind: GraphicKind.Diagram }:
                    Count("diagram");
                    break;
                case GraphicFrame { Kind: GraphicKind.OleObject }:
                    Count("ole");
                    break;
                case PictureShape { Media: not MediaKind.None }:
                    Count("media");
                    break;
                case PictureShape { Picture.Image: { IsDrawable: false } }:
                    Count("picture");
                    break;
                case PreservedElement:
                    Count("element");
                    break;
                case AutoShape { Geometry.Preset: { } preset } when !PresetGeometries.IsKnown(preset):
                    Count("geometry");
                    break;
                case AutoShape { Text: { } text } when text.Paragraphs.Any(paragraph => paragraph.Runs.OfType<PreservedRun>().Any()):
                    Count("math");
                    break;
            }
        }

        foreach (var slide in presentation.Slides)
        {
            if (slide.Transition is { Kind: TransitionKind.Other or TransitionKind.Morph })
            {
                Count("transition");
            }

            if (slide.Timing is { } timing)
            {
                Count("interactive", timing.InteractiveSequences.Count);
                Count("animation", timing.MainSequence.Count(effect => effect.Behaviors.Any(behavior => behavior is PreservedBehavior)));
            }
        }

        var presentationPart = package.PresentationPart!;
        Count("comments", presentationPart.SlideParts.Sum(slide => slide.Parts.Count(pair => pair.OpenXmlPart.RelationshipType.Contains("comments", StringComparison.OrdinalIgnoreCase))));
        Count("macros", presentationPart.VbaProjectPart is null ? 0 : 1);
        Count("customXml", package.Parts.Count(pair => pair.OpenXmlPart is CustomXmlPart) + presentationPart.CustomXmlParts.Count());
        Count("fonts", presentationPart.FontParts.Count());
        return counts.Select(entry => new UnsupportedContent(entry.Key, entry.Value)).OrderBy(entry => entry.Kind, StringComparer.Ordinal).ToList();
    }

    /// <summary>What reading one package needs across parts: picture bytes by part (decoded once), slide ids by slide part.</summary>
    internal sealed class ReadContext(PresentationDocument package)
    {
        private readonly Dictionary<string, ImageData?> _images = new(StringComparer.Ordinal);

        public PresentationDocument Package { get; } = package;

        public IReadOnlyDictionary<string, uint> SlideIds { get; set; } = new Dictionary<string, uint>();

        public ImageData? ImageOf(OpenXmlPart part)
        {
            var uri = part.Uri.OriginalString;
            if (!_images.TryGetValue(uri, out var image))
            {
                using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                image = ImageData.From(buffer.ToArray(), part.ContentType);
                _images[uri] = image;
            }

            return image;
        }

        /// <summary>The parts the relationship ids inside <paramref name="element"/> point at (in document order, each id once).</summary>
        public ValueList<PartReference> ReferencesOf(OpenXmlPart part, XElement element)
        {
            var references = new List<PartReference>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in element.DescendantsAndSelf().SelectMany(node => node.Attributes()).Where(attribute => attribute.Name.Namespace == R))
            {
                var id = attribute.Value;
                if (id.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                if (Resolve(part, id) is { } reference)
                {
                    references.Add(reference);
                }
            }

            return references;
        }

        public static PartReference? Resolve(OpenXmlPart part, string id)
        {
            var pair = part.Parts.FirstOrDefault(item => item.RelationshipId == id);
            if (pair.OpenXmlPart is { } target)
            {
                return new PartReference(id, target.RelationshipType, target.Uri.OriginalString, null);
            }

            var external = part.ExternalRelationships.FirstOrDefault(item => item.Id == id);
            if (external is not null)
            {
                return new PartReference(id, external.RelationshipType, null, external.Uri.OriginalString);
            }

            var hyperlink = part.HyperlinkRelationships.FirstOrDefault(item => item.Id == id);
            if (hyperlink is not null)
            {
                return new PartReference(id, hyperlink.RelationshipType, null, hyperlink.Uri.OriginalString);
            }

            var data = part.DataPartReferenceRelationships.FirstOrDefault(item => item.Id == id);
            return data is null ? null : new PartReference(id, data.RelationshipType, data.DataPart.Uri.OriginalString, null);
        }
    }

    /// <summary>Links of one part while reading it.</summary>
    internal sealed class PartLinks(OpenXmlPart part, ReadContext context) : IReadLinks
    {
        public OpenXmlPart Part { get; } = part;

        public ImageData? ImageOf(string relationshipId)
        {
            var pair = Part.Parts.FirstOrDefault(item => item.RelationshipId == relationshipId);
            if (pair.OpenXmlPart is { } image)
            {
                return context.ImageOf(image);
            }

            var data = Part.DataPartReferenceRelationships.FirstOrDefault(item => item.Id == relationshipId);
            if (data is not null)
            {
                using var stream = data.DataPart.GetStream();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return ImageData.From(buffer.ToArray(), data.DataPart.ContentType);
            }

            return null;
        }

        public (string? Url, uint? SlideId) TargetOf(string relationshipId)
        {
            var reference = ReadContext.Resolve(Part, relationshipId);
            if (reference is null)
            {
                return (null, null);
            }

            if (reference.ExternalTarget is { } url)
            {
                return (url, null);
            }

            return reference.PartUri is { } uri && context.SlideIds.TryGetValue(uri, out var slideId) ? (null, slideId) : (reference.PartUri, null);
        }

        public string? PartUriOf(string relationshipId) => ReadContext.Resolve(Part, relationshipId)?.PartUri;
    }
}

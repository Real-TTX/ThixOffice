using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>
/// Writes the model as .pptx/.pptm. A presentation read from a file is written INTO A COPY OF ITS ORIGINAL PACKAGE: presentation.xml
/// gets its slide lists, sizes, default text style and sections anew (everything else in it stays), masters and layouts get their
/// shape trees, colour maps and text styles (the rest stays), themes are written only when changed, slides only when changed (an
/// unchanged slide's part is not touched), new slides and notes get new parts, deleted ones are removed. Inside a changed slide every
/// unchanged element is written as read, with its relationship ids pointed at the slide's parts. Charts, SmartArt, media, OLE,
/// comments, custom XML, VBA and fonts are parts ThixShow never rewrites, so they survive. A new presentation gets a complete package
/// (masters, layouts, themes, table styles, presentation and document properties).
/// </summary>
public static class PptxWriter
{
    private const string HyperlinkType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public static void Write(Presentation presentation, Stream stream)
    {
        using var buffer = presentation.Package?.Open() ?? new MemoryStream();
        if (presentation.Package is null)
        {
            using var created = PresentationDocument.Create(buffer, presentation.IsMacroEnabled ? PresentationDocumentType.MacroEnabledPresentation : PresentationDocumentType.Presentation);
            created.AddPresentationPart();
            new Writer(created, presentation, isNew: true).Run();
        }
        else
        {
            using var package = PresentationDocument.Open(buffer, true);
            new Writer(package, presentation, isNew: false).Run();
        }

        buffer.Position = 0;
        buffer.CopyTo(stream);
    }

    private sealed class Writer(PresentationDocument package, Presentation presentation, bool isNew)
    {
        private readonly PresentationDocument _package = package;
        private PresentationPart MainPart => _package.PresentationPart!;
        private readonly Dictionary<string, OpenXmlPart> _partsByUri = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImagePart> _imagesByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImageData> _imagesByUri = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, SlidePart> _slidePartsById = [];
        private readonly Dictionary<string, uint> _originalSlideIds = new(StringComparer.Ordinal);
        private bool _imagesIndexed;

        public void Run()
        {
            SetFlavour();
            IndexParts(MainPart);
            if (presentation.OriginalSlides is { } originals)
            {
                foreach (var slide in originals.Values.Where(slide => slide.PartUri is not null))
                {
                    _originalSlideIds[slide.PartUri!] = slide.Id;
                }
            }

            var masterParts = WriteMasters();
            var layoutParts = masterParts.SelectMany(entry => entry.Layouts).ToDictionary(entry => entry.Id, entry => entry.Part);
            var slideParts = AssignSlideParts(layoutParts);
            foreach (var (slide, part, write) in slideParts)
            {
                if (write)
                {
                    WriteSlide(slide, part);
                }

                WriteNotes(slide, part);
            }

            RemoveUnused(masterParts.Select(entry => entry.Part).ToList(), slideParts.Select(entry => entry.Part).ToList());
            WritePresentation(masterParts, slideParts.Select(entry => (entry.Slide.Id, entry.Part)).ToList());
            WriteTableStyles();
            WriteProperties();
        }

        /// <summary>.pptm keeps the VBA project; .pptx drops it (the content type follows).</summary>
        private void SetFlavour()
        {
            var wanted = presentation.IsMacroEnabled ? PresentationDocumentType.MacroEnabledPresentation : PresentationDocumentType.Presentation;
            if (!presentation.IsMacroEnabled && MainPart.VbaProjectPart is { } vba)
            {
                MainPart.DeletePart(vba);
            }

            if (_package.DocumentType != wanted)
            {
                _package.ChangeDocumentType(wanted);
            }
        }

        // ------------------------------------------------------------ parts and relationships

        private void IndexParts(OpenXmlPartContainer container)
        {
            foreach (var pair in container.Parts)
            {
                if (_partsByUri.TryAdd(pair.OpenXmlPart.Uri.OriginalString, pair.OpenXmlPart))
                {
                    IndexParts(pair.OpenXmlPart);
                }
            }
        }

        private OpenXmlPart? PartOf(string? uri) => uri is not null && _partsByUri.TryGetValue(uri, out var part) ? part : null;

        private void Register(OpenXmlPart part) => _partsByUri[part.Uri.OriginalString] = part;

        private static void Save(OpenXmlPart part, XElement root)
        {
            CleanNamespaces(root);
            using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(stream, SaveOptions.DisableFormatting);
        }

        /// <summary>Removes namespace declarations that repeat one in scope (kept fragments carry their own).</summary>
        private static void CleanNamespaces(XElement root)
        {
            foreach (var element in root.Descendants().Where(element => element.Name.Namespace != Mc).ToList())
            {
                foreach (var declaration in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToList())
                {
                    var parent = element.Parent!;
                    if (declaration.Name == "xmlns")
                    {
                        if (parent.GetPrefixOfNamespace(declaration.Value) is not null || parent.GetDefaultNamespace().NamespaceName == declaration.Value)
                        {
                            declaration.Remove();
                        }
                    }
                    else if (parent.GetNamespaceOfPrefix(declaration.Name.LocalName)?.NamespaceName == declaration.Value)
                    {
                        declaration.Remove();
                    }
                }
            }
        }

        private static XElement NewRoot(XName name) => new(name,
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName));

        private ImageData? ImageAt(string uri)
        {
            if (_imagesByUri.TryGetValue(uri, out var image))
            {
                return image;
            }

            if (PartOf(uri) is not { } part)
            {
                return null;
            }

            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            image = ImageData.From(copy.ToArray(), part.ContentType);
            _imagesByUri[uri] = image;
            return image;
        }

        private ImagePart? ExistingImage(ImageData image)
        {
            if (!_imagesIndexed)
            {
                _imagesIndexed = true;
                foreach (var part in _partsByUri.Values.OfType<ImagePart>())
                {
                    if (ImageAt(part.Uri.OriginalString) is { } data)
                    {
                        _imagesByKey.TryAdd(data.Key, part);
                    }
                }
            }

            return _imagesByKey.GetValueOrDefault(image.Key);
        }

        /// <summary>The writer of one part: relationships for new content, relinking for kept XML.</summary>
        private sealed class PartWriter(Writer writer, OpenXmlPart part) : IPartWriter
        {
            public OpenXmlPart Part { get; } = part;

            public string ImageId(ImageData image)
            {
                var target = writer.ExistingImage(image);
                if (target is null)
                {
                    target = Part.AddNewPart<ImagePart>(image.ContentType, null);
                    using (var stream = new MemoryStream(image.Bytes))
                    {
                        target.FeedData(stream);
                    }

                    writer._imagesByKey[image.Key] = target;
                    writer._imagesByUri[target.Uri.OriginalString] = image;
                    writer.Register(target);
                }

                return IdOf(target);
            }

            private string IdOf(OpenXmlPart target)
            {
                var existing = Part.Parts.FirstOrDefault(pair => pair.OpenXmlPart == target);
                return existing.OpenXmlPart is not null ? existing.RelationshipId : Part.CreateRelationshipToPart(target);
            }

            public string LinkId(Hyperlink link)
            {
                switch (link.Kind)
                {
                    case HyperlinkKind.Url when link.Target is { } address:
                        return External(HyperlinkType, address, null);
                    case HyperlinkKind.Slide when link.SlideId is { } slideId && writer._slidePartsById.TryGetValue(slideId, out var slidePart):
                        return IdOf(slidePart);
                    case HyperlinkKind.Other when link.Target is { } target && writer.PartOf(target) is { } targetPart:
                        return IdOf(targetPart);
                    default:
                        return "";
                }
            }

            private string External(string type, string target, string? preferredId)
            {
                if (type == HyperlinkType)
                {
                    var same = Part.HyperlinkRelationships.FirstOrDefault(item => item.Uri.OriginalString == target && (preferredId is null || item.Id == preferredId))
                               ?? Part.HyperlinkRelationships.FirstOrDefault(item => item.Uri.OriginalString == target);
                    return same?.Id ?? Part.AddHyperlinkRelationship(new Uri(target, UriKind.RelativeOrAbsolute), true).Id;
                }

                var external = Part.ExternalRelationships.FirstOrDefault(item => item.Uri.OriginalString == target && item.RelationshipType == type && (preferredId is null || item.Id == preferredId))
                               ?? Part.ExternalRelationships.FirstOrDefault(item => item.Uri.OriginalString == target && item.RelationshipType == type);
                return external?.Id ?? Part.AddExternalRelationship(type, new Uri(target, UriKind.RelativeOrAbsolute)).Id;
            }

            public XElement Relink(XElement element, ValueList<PartReference> references)
            {
                if (references.IsEmpty)
                {
                    return element;
                }

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var reference in references)
                {
                    map[reference.Id] = Target(reference) ?? reference.Id;
                }

                foreach (var attribute in element.DescendantsAndSelf().SelectMany(node => node.Attributes()).Where(attribute => attribute.Name.Namespace == R))
                {
                    if (map.TryGetValue(attribute.Value, out var mapped))
                    {
                        attribute.Value = mapped;
                    }
                }

                return element;
            }

            private string? Target(PartReference reference)
            {
                if (reference.ExternalTarget is { } external)
                {
                    return External(reference.RelationshipType, external, reference.Id);
                }

                if (writer.PartOf(reference.PartUri) is { } target)
                {
                    if (target is SlidePart && !writer._slidePartsById.ContainsValue((SlidePart)target))
                    {
                        return ""; // a link to a deleted slide
                    }

                    var same = Part.Parts.FirstOrDefault(pair => pair.RelationshipId == reference.Id && pair.OpenXmlPart == target);
                    return same.OpenXmlPart is not null ? reference.Id : IdOf(target);
                }

                var data = writer._package.DataParts.FirstOrDefault(item => item.Uri.OriginalString == reference.PartUri);
                if (data is MediaDataPart media)
                {
                    var existing = Part.DataPartReferenceRelationships.FirstOrDefault(item => item.DataPart == media && item.RelationshipType == reference.RelationshipType);
                    if (existing is not null)
                    {
                        return existing.Id;
                    }

                    return Part switch
                    {
                        SlidePart slide when reference.RelationshipType.EndsWith("/video", StringComparison.Ordinal) => slide.AddVideoReferenceRelationship(media).Id,
                        SlidePart slide when reference.RelationshipType.EndsWith("/audio", StringComparison.Ordinal) => slide.AddAudioReferenceRelationship(media).Id,
                        SlidePart slide => slide.AddMediaReferenceRelationship(media).Id,
                        _ => null,
                    };
                }

                return null;
            }
        }

        /// <summary>Reads kept XML back with the links of the parts it names (for the "unchanged" test).</summary>
        private sealed class ReferenceLinks(Writer writer, ValueList<PartReference> references) : IReadLinks
        {
            private PartReference? Find(string id) => references.FirstOrDefault(reference => reference.Id == id);

            public ImageData? ImageOf(string relationshipId) =>
                Find(relationshipId)?.PartUri is { } uri ? writer.ImageAt(uri) : null;

            public (string? Url, uint? SlideId) TargetOf(string relationshipId)
            {
                var reference = Find(relationshipId);
                if (reference?.ExternalTarget is { } url)
                {
                    return (url, null);
                }

                return reference?.PartUri is { } uri && writer._originalSlideIds.TryGetValue(uri, out var slideId) ? (null, slideId) : (reference?.PartUri, null);
            }

            public string? PartUriOf(string relationshipId) => Find(relationshipId)?.PartUri;
        }

        private bool Unchanged(SlideElement shape)
        {
            if (shape.Source is null)
            {
                return false;
            }

            var byId = shape.References.ToDictionary(reference => reference.Id, StringComparer.Ordinal);
            ValueList<PartReference> ReferencesOf(XElement element) => element.DescendantsAndSelf()
                .SelectMany(node => node.Attributes())
                .Where(attribute => attribute.Name.Namespace == R && attribute.Value.Length > 0)
                .Select(attribute => attribute.Value)
                .Distinct(StringComparer.Ordinal)
                .Select(id => byId.GetValueOrDefault(id))
                .OfType<PartReference>()
                .ToList();

            var read = ShapeXml.ReadElement(shape.Source.ToElement(), new ReferenceLinks(this, shape.References), ReferencesOf);
            return read == shape;
        }

        // ------------------------------------------------------------ masters and layouts

        private sealed record MasterEntry(uint Id, SlideMasterPart Part, List<(uint Id, SlideLayoutPart Part)> Layouts);

        private List<MasterEntry> WriteMasters()
        {
            var entries = new List<MasterEntry>();
            var usedParts = new HashSet<OpenXmlPart>();
            foreach (var master in presentation.Masters)
            {
                var part = PartOf(master.PartUri) as SlideMasterPart;
                if (part is null || !usedParts.Add(part))
                {
                    part = MainPart.AddNewPart<SlideMasterPart>();
                    usedParts.Add(part);
                    Register(part);
                }

                WriteTheme(master, part);
                var layouts = new List<(uint, SlideLayoutPart)>();
                foreach (var layout in master.Layouts)
                {
                    var layoutPart = PartOf(layout.PartUri) as SlideLayoutPart;
                    if (layoutPart is null || !usedParts.Add(layoutPart) || layoutPart.SlideMasterPart != part)
                    {
                        layoutPart = part.AddNewPart<SlideLayoutPart>();
                        layoutPart.AddPart(part);
                        usedParts.Add(layoutPart);
                        Register(layoutPart);
                    }

                    WriteLayout(layout, layoutPart);
                    layouts.Add((layout.Id, layoutPart));
                }

                WriteMaster(master, part, layouts);
                entries.Add(new MasterEntry(master.Id, part, layouts));
            }

            return entries;
        }

        private void WriteTheme(SlideMaster master, SlideMasterPart part)
        {
            var themePart = part.ThemePart;
            if (themePart is not null && master.Theme.Source is not null)
            {
                var current = ThemeXml.Read(PptxReader.Load(themePart), new PptxReader.PartLinks(themePart, new PptxReader.ReadContext(_package)));
                if (current == master.Theme)
                {
                    return;
                }
            }

            if (themePart is null)
            {
                themePart = part.AddNewPart<ThemePart>();
                Register(themePart);
            }

            Save(themePart, ThemeXml.Write(master.Theme));
        }

        private static readonly string[] MasterOrder = ["cSld", "clrMap", "sldLayoutIdLst", "transition", "timing", "hf", "txStyles", "extLst"];
        private static readonly string[] LayoutOrder = ["cSld", "clrMapOvr", "transition", "timing", "hf", "extLst"];
        private static readonly string[] SlideOrder = ["cSld", "clrMapOvr", "transition", "timing", "extLst"];
        private static readonly string[] CommonSlideOrder = ["bg", "spTree", "custDataLst", "controls", "extLst"];

        private void WriteCommon(XElement root, SlideBase slide, PartWriter writer, string[] order)
        {
            var data = root.Element(P + "cSld");
            if (data is null)
            {
                data = new XElement(P + "cSld");
                root.Place(data, order);
            }

            data.Set("name", slide.Name);
            data.Place(WriteBackground(slide.Background, writer), CommonSlideOrder, P + "bg");
            var tree = data.Element(P + "spTree");
            if (tree is null)
            {
                tree = new XElement(P + "spTree",
                    new XElement(P + "nvGrpSpPr", new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")), new XElement(P + "cNvGrpSpPr"), new XElement(P + "nvPr")),
                    new XElement(P + "grpSpPr", new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                        new XElement(A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                        new XElement(A + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))));
                data.Place(tree, CommonSlideOrder);
            }

            ShapeXml.WriteShapes(tree, slide.Shapes, writer, Unchanged);
        }

        private static XElement? WriteBackground(Background? background, IWriteLinks links)
        {
            if (background is null)
            {
                return null;
            }

            if (background.Fill is { } fill)
            {
                var properties = new XElement(P + "bgPr", DrawingXml.WriteFill(fill, links));
                properties.Set("shadeToTitle", background.ShadeToTitle ? true : null);
                properties.Add(background.Effects is { } effects ? DrawingXml.WriteEffects(effects) : new XElement(A + "effectLst"));
                return new XElement(P + "bg", properties);
            }

            var reference = background.Reference ?? new StyleReference(1001, ColorSpec.Scheme("bg1"));
            return new XElement(P + "bg", new XElement(P + "bgRef", new XAttribute("idx", Text(reference.Index)), DrawingXml.WriteColor(reference.Color ?? ColorSpec.Scheme("bg1"))));
        }

        private static XElement WriteColorMap(XName name, ColorMap map) =>
            new(name, ColorMap.AttributeNames.Select(attribute => new XAttribute(attribute, map.ValueOf(attribute))));

        private static XElement ColorMapOverride(ColorMap? map) =>
            new(P + "clrMapOvr", map is null ? new XElement(A + "masterClrMapping") : WriteColorMap(A + "overrideClrMapping", map));

        private static XElement? WriteHeaderFooter(HeaderFooterFlags? flags)
        {
            if (flags is null)
            {
                return null;
            }

            var element = new XElement(P + "hf");
            element.Set("sldNum", flags.SlideNumber ? null : false);
            element.Set("hdr", flags.Header ? null : false);
            element.Set("ftr", flags.Footer ? null : false);
            element.Set("dt", flags.Date ? null : false);
            return element;
        }

        private void WriteMaster(SlideMaster master, SlideMasterPart part, List<(uint Id, SlideLayoutPart Part)> layouts)
        {
            var root = master.Source?.ToElement() ?? NewRoot(P + "sldMaster");
            var writer = new PartWriter(this, part);
            root = writer.Relink(root, master.References);
            root.Set("preserve", master.Preserve ? true : null);
            WriteCommon(root, master, writer, MasterOrder);
            root.Place(WriteColorMap(P + "clrMap", master.ColorMap), MasterOrder);
            root.Place(new XElement(P + "sldLayoutIdLst", layouts.Select(layout =>
            {
                var existing = part.Parts.FirstOrDefault(pair => pair.OpenXmlPart == layout.Part);
                var id = existing.OpenXmlPart is not null ? existing.RelationshipId : part.CreateRelationshipToPart(layout.Part);
                return new XElement(P + "sldLayoutId", new XAttribute("id", Text(layout.Id)), new XAttribute(R + "id", id));
            })), MasterOrder);
            if (master.HeaderFooter is not null || root.Element(P + "hf") is not null)
            {
                root.Place(WriteHeaderFooter(master.HeaderFooter), MasterOrder, P + "hf");
            }

            root.Place(new XElement(P + "txStyles",
                DrawingXml.WriteListStyle(master.TextStyles.Title, P + "titleStyle", writer),
                DrawingXml.WriteListStyle(master.TextStyles.Body, P + "bodyStyle", writer),
                DrawingXml.WriteListStyle(master.TextStyles.Other, P + "otherStyle", writer)), MasterOrder);
            Save(part, root);
        }

        private void WriteLayout(SlideLayout layout, SlideLayoutPart part)
        {
            var root = layout.Source?.ToElement() ?? NewRoot(P + "sldLayout");
            var writer = new PartWriter(this, part);
            root = writer.Relink(root, layout.References);
            root.Set("showMasterSp", layout.ShowMasterShapes ? null : false);
            root.Set("showMasterPhAnim", layout.ShowMasterPlaceholderAnimations ? true : null);
            root.Set("type", layout.Type == "cust" ? null : layout.Type);
            root.Set("preserve", layout.Preserve ? true : null);
            root.Set("userDrawn", layout.UserDrawn ? true : null);
            root.Set("matchingName", layout.MatchingName);
            WriteCommon(root, layout, writer, LayoutOrder);
            root.Place(ColorMapOverride(layout.ColorMapOverride), LayoutOrder, P + "clrMapOvr");
            if (layout.HeaderFooter is not null || root.Element(P + "hf") is not null)
            {
                root.Place(WriteHeaderFooter(layout.HeaderFooter), LayoutOrder, P + "hf");
            }

            Save(part, root);
        }

        // ------------------------------------------------------------ slides

        private List<(Slide Slide, SlidePart Part, bool Write)> AssignSlideParts(Dictionary<uint, SlideLayoutPart> layoutParts)
        {
            var result = new List<(Slide, SlidePart, bool)>();
            var used = new HashSet<SlidePart>();
            var firstLayout = layoutParts.Values.First();
            foreach (var slide in presentation.Slides)
            {
                var part = PartOf(slide.PartUri) as SlidePart;
                var isNewPart = part is null || !used.Add(part);
                if (isNewPart)
                {
                    part = MainPart.AddNewPart<SlidePart>();
                    used.Add(part);
                    Register(part);
                }

                var layoutPart = layoutParts.GetValueOrDefault(slide.LayoutId) ?? firstLayout;
                var layoutChanged = part!.SlideLayoutPart != layoutPart;
                if (layoutChanged)
                {
                    if (part.SlideLayoutPart is { } old)
                    {
                        part.DeletePart(old);
                    }

                    part.AddPart(layoutPart);
                }

                var original = presentation.OriginalSlides?.GetValueOrDefault(slide.Id);
                var unchanged = !isNewPart && original is not null && (ReferenceEquals(original, slide) || original == slide);
                _slidePartsById[slide.Id] = part;
                result.Add((slide, part, !unchanged));
            }

            return result;
        }

        private void WriteSlide(Slide slide, SlidePart part)
        {
            var root = slide.Source?.ToElement() ?? NewRoot(P + "sld");
            var writer = new PartWriter(this, part);
            root = writer.Relink(root, slide.References);
            root.Set("show", slide.Hidden ? false : null);
            root.Set("showMasterSp", slide.ShowMasterShapes ? null : false);
            WriteCommon(root, slide, writer, SlideOrder);
            root.Place(ColorMapOverride(slide.ColorMapOverride), SlideOrder, P + "clrMapOvr");

            foreach (var old in root.Elements().Where(element => element.Name == P + "transition" || (element.Name == Mc + "AlternateContent" && element.Descendants(P + "transition").Any())).ToList())
            {
                old.Remove();
            }

            if (slide.Transition is { Kind: not TransitionKind.None } or { Source: not null } && slide.Transition is { } transition)
            {
                PlaceBefore(root, TimingXml.WriteTransition(transition), "timing", "extLst");
            }

            var textShapes = ShapeTree.All(slide.Shapes).OfType<AutoShape>().Select(shape => shape.Id).ToHashSet();
            var timing = slide.Timing is null ? null : TimingXml.WriteTiming(slide.Timing, textShapes.Contains);
            root.Place(timing, SlideOrder, P + "timing");
            if (timing is not null && root.Element(Mc + "AlternateContent") is { } alternate && alternate.Descendants(P + "transition").Any())
            {
                timing.Remove();
                alternate.AddAfterSelf(timing);
            }

            Save(part, root);
        }

        private static void PlaceBefore(XElement parent, XElement child, params string[] before)
        {
            var next = parent.Elements().FirstOrDefault(element => before.Contains(element.Name.LocalName) && element.Name.Namespace == P);
            if (next is null)
            {
                parent.Add(child);
            }
            else
            {
                next.AddBeforeSelf(child);
            }
        }

        private void WriteNotes(Slide slide, SlidePart part)
        {
            var notesPart = part.NotesSlidePart;
            if (slide.Notes is null)
            {
                if (notesPart is not null)
                {
                    part.DeletePart(notesPart);
                }

                return;
            }

            var originalNotes = presentation.OriginalSlides?.GetValueOrDefault(slide.Id)?.Notes;
            if (notesPart is not null && originalNotes is not null && originalNotes == slide.Notes && notesPart.Uri.OriginalString == slide.Notes.PartUri)
            {
                return;
            }

            if (notesPart is null)
            {
                notesPart = part.AddNewPart<NotesSlidePart>();
                notesPart.AddPart(part);
                notesPart.AddPart(EnsureNotesMaster());
                Register(notesPart);
            }

            var notes = slide.Notes;
            var root = notes.Source?.ToElement() ?? NewRoot(P + "notes");
            var writer = new PartWriter(this, notesPart);
            root = writer.Relink(root, notes.References);
            WriteCommon(root, notes, writer, ["cSld", "clrMapOvr", "extLst"]);
            root.Place(ColorMapOverride(notes.ColorMapOverride), ["cSld", "clrMapOvr", "extLst"], P + "clrMapOvr");
            Save(notesPart, root);
        }

        private NotesMasterPart EnsureNotesMaster()
        {
            if (MainPart.NotesMasterPart is { } existing)
            {
                return existing;
            }

            var part = MainPart.AddNewPart<NotesMasterPart>();
            var theme = part.AddNewPart<ThemePart>();
            Save(theme, ThemeXml.Write(presentation.Masters[0].Theme with { Source = null }));
            var size = presentation.NotesSize;
            var slideWidth = (long)(size.Width * 0.8);
            var slideHeight = (long)(slideWidth / presentation.SlideSize.AspectRatio);
            var image = new AutoShape
            {
                Id = 2,
                Name = "Folienbildplatzhalter 1",
                Placeholder = new PlaceholderInfo(PlaceholderType.SlideImage, 2),
                Transform = new Transform2D((size.Width - slideWidth) / 2, (long)(size.Height * 0.075), slideWidth, slideHeight),
                Geometry = ShapeGeometry.Rectangle,
                Fill = NoFill.Instance,
                Outline = new Outline(12700, new SolidFill(ColorSpec.Rgb("000000"))),
            };
            var body = new AutoShape
            {
                Id = 3,
                Name = "Notizenplatzhalter 2",
                Placeholder = new PlaceholderInfo(PlaceholderType.Body, 3, Size: "quarter"),
                Transform = new Transform2D(size.Width / 10, (long)(size.Height * 0.5), size.Width * 8 / 10, (long)(size.Height * 0.4)),
                Geometry = ShapeGeometry.Rectangle,
                Text = TextBody.FromText("Textmasterformat bearbeiten"),
            };
            var root = NewRoot(P + "notesMaster");
            var writer = new PartWriter(this, part);
            WriteCommon(root, new NotesSlide { Shapes = [image, body], Background = new Background(Reference: new StyleReference(1001, ColorSpec.Scheme("bg1"))) }, writer, ["cSld", "clrMap", "hf", "notesStyle", "extLst"]);
            root.Place(WriteColorMap(P + "clrMap", ColorMap.Default), ["cSld", "clrMap", "hf", "notesStyle", "extLst"]);
            var notesStyle = new ListStyle(null, Enumerable.Range(0, 9).Select(level => (ParagraphProperties?)new ParagraphProperties(
                LeftMargin: level * 457200,
                Alignment: TextAlignment.Left,
                DefaultRun: new RunProperties(Size: 1200, Fill: new SolidFill(ColorSpec.Scheme("tx1")), Latin: new TextFont("+mn-lt"), EastAsian: new TextFont("+mn-ea"), ComplexScript: new TextFont("+mn-cs")))).ToList());
            root.Place(DrawingXml.WriteListStyle(notesStyle, P + "notesStyle", writer), ["cSld", "clrMap", "hf", "notesStyle", "extLst"]);
            Save(part, root);
            Register(part);
            Register(theme);
            return part;
        }

        // ------------------------------------------------------------ presentation and package

        private void RemoveUnused(List<SlideMasterPart> masters, List<SlidePart> slides)
        {
            foreach (var slide in MainPart.SlideParts.Except(slides).ToList())
            {
                MainPart.DeletePart(slide);
            }

            foreach (var master in MainPart.SlideMasterParts.Except(masters).ToList())
            {
                foreach (var layout in master.SlideLayoutParts.ToList())
                {
                    master.DeletePart(layout);
                }

                MainPart.DeletePart(master);
            }

            foreach (var master in masters)
            {
                var listed = PptxReader.Load(master).Element(P + "sldLayoutIdLst")?.Elements(P + "sldLayoutId").Select(element => element.Attribute(R + "id")?.Value).ToHashSet() ?? [];
                foreach (var pair in master.Parts.Where(pair => pair.OpenXmlPart is SlideLayoutPart && !listed.Contains(pair.RelationshipId)).ToList())
                {
                    master.DeletePart(pair.RelationshipId);
                }
            }
        }

        private void WritePresentation(List<MasterEntry> masters, List<(uint Id, SlidePart Part)> slides)
        {
            string[] order = ["sldMasterIdLst", "notesMasterIdLst", "handoutMasterIdLst", "sldIdLst", "sldSz", "notesSz", "smartTags", "embeddedFontLst", "custShowLst", "photoAlbum", "custDataLst", "kinsoku", "defaultTextStyle", "modifyVerifier", "extLst"];
            var root = presentation.Source?.ToElement() ?? NewRoot(P + "presentation");
            if (presentation.Source is null)
            {
                root.SetAttributeValue("saveSubsetFonts", "1");
            }

            string IdOf(OpenXmlPart part)
            {
                var existing = MainPart.Parts.FirstOrDefault(pair => pair.OpenXmlPart == part);
                return existing.OpenXmlPart is not null ? existing.RelationshipId : MainPart.CreateRelationshipToPart(part);
            }

            root.Place(new XElement(P + "sldMasterIdLst", masters.Select(master =>
                new XElement(P + "sldMasterId", new XAttribute("id", Text(master.Id)), new XAttribute(R + "id", IdOf(master.Part))))), order);
            if (MainPart.NotesMasterPart is { } notesMaster)
            {
                root.Place(new XElement(P + "notesMasterIdLst", new XElement(P + "notesMasterId", new XAttribute(R + "id", IdOf(notesMaster)))), order);
            }

            root.Place(slides.Count == 0 ? null : new XElement(P + "sldIdLst", slides.Select(slide =>
                new XElement(P + "sldId", new XAttribute("id", Text(slide.Id)), new XAttribute(R + "id", IdOf(slide.Part))))), order, P + "sldIdLst");
            var size = new XElement(P + "sldSz", new XAttribute("cx", Text(presentation.SlideSize.Width)), new XAttribute("cy", Text(presentation.SlideSize.Height)));
            size.Set("type", presentation.SlideSize.Type);
            root.Place(size, order);
            root.Place(new XElement(P + "notesSz", new XAttribute("cx", Text(presentation.NotesSize.Width)), new XAttribute("cy", Text(presentation.NotesSize.Height))), order);
            root.Place(DrawingXml.WriteListStyle(presentation.DefaultTextStyle, P + "defaultTextStyle", new PartWriter(this, MainPart)), order);
            WriteSections(root, order);
            Save(MainPart, root);

            if (isNew && MainPart.PresentationPropertiesPart is null)
            {
                Save(MainPart.AddNewPart<PresentationPropertiesPart>(), NewRoot(P + "presentationPr"));
            }
        }

        private void WriteSections(XElement root, string[] order)
        {
            var extensions = root.Element(P + "extLst");
            extensions?.Elements(P + "ext").Where(ext => ext.Attr("uri") == SectionsExtension).Remove();
            if (presentation.Sections.IsEmpty)
            {
                if (extensions is { HasElements: false })
                {
                    extensions.Remove();
                }

                return;
            }

            if (extensions is null)
            {
                extensions = new XElement(P + "extLst");
                root.Place(extensions, order);
            }

            var list = new XElement(P14 + "sectionLst", new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                presentation.Sections.Select(section => new XElement(P14 + "section", new XAttribute("name", section.Name), new XAttribute("id", section.Id),
                    new XElement(P14 + "sldIdLst", section.SlideIds.Where(id => presentation.FindSlide(id) is not null).Select(id => new XElement(P14 + "sldId", new XAttribute("id", Text(id))))))));
            extensions.AddFirst(new XElement(P + "ext", new XAttribute("uri", SectionsExtension), list));
        }

        private void WriteTableStyles()
        {
            if (MainPart.TableStylesPart is not null)
            {
                return;
            }

            var styles = presentation.TableStyles.IsEmpty ? [TableRenderer.BuiltInStyle(TableRenderer.DefaultStyleId)] : presentation.TableStyles.ToList();
            Save(MainPart.AddNewPart<TableStylesPart>(), TableStyleXml.Write(styles, presentation.DefaultTableStyleId ?? TableRenderer.DefaultStyleId));
        }

        private void WriteProperties()
        {
            var properties = _package.PackageProperties;
            if (presentation.Title is not null)
            {
                properties.Title = presentation.Title;
            }

            if (isNew)
            {
                properties.Creator = presentation.Author ?? properties.Creator;
                properties.Created = DateTime.UtcNow;
            }

            properties.Modified = DateTime.UtcNow;
        }
    }
}

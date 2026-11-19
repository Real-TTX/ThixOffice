using System.Globalization;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

/// <summary>
/// Writes a presentation as OpenDocument Presentation (.odp) with the BCL only. What maps: the slide size; one style:master-page per
/// master (and per layout that brings its own look – placeholder text styles, shapes, background), its title/outline/subtitle text
/// styles as presentation styles and its placeholders and shapes; slides as draw:page with their shapes (preset geometries as
/// draw:custom-shape whose enhanced path the geometry engine computes, custom geometries the same, text boxes and placeholders as
/// frames with text, pictures with their crop, tables, groups, lines and connectors), fills (solid, gradients, pictures), outlines
/// (colour, width, dashes, ends), the shadow, rotation and flips, notes, footers (presentation:footer-decl), simple transitions and
/// the appear/fade effects. Theme colours and fonts are written as the colours and fonts they resolve to. What ODF cannot take –
/// charts, SmartArt, OLE objects, kept XML – is left out; <see cref="Losses"/> names it before a save.
/// </summary>
public static partial class OdpWriter
{
    public const string MimeType = OdpNames.MimeType;

    public static void Write(Presentation presentation, Stream stream)
    {
        var package = new OdfPackageWriter(MimeType);
        new Writer(presentation, package).Build();
        package.WriteTo(stream);
    }

    /// <summary>The model reduced to a page of a master page: a master and the layout whose own look it carries (null: the master alone).</summary>
    private sealed record MasterPage(string Name, string DisplayName, SlideMaster Master, SlideLayout? Layout, int Number);

    private sealed partial class Writer(Presentation presentation, OdfPackageWriter package)
    {
        private readonly StyleSink _content = new("");
        private readonly StyleSink _styles = new("M");
        private readonly List<MasterPage> _pages = [];
        private readonly Dictionary<string, string> _pictures = new(StringComparer.Ordinal);
        private readonly List<XElement> _decls = [];
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public void Build()
        {
            PlanMasterPages();
            var body = new XElement(OfficeNs + "presentation");
            var pages = presentation.Slides.Select((slide, index) => WriteSlide(slide, index)).ToList();
            body.Add(_decls, pages, new XElement(PresentationNs + "settings", new XAttribute(PresentationNs + "mouse-visible", "true")));
            var masterPages = _pages.Select(WriteMasterPage).ToList();
            var commonStyles = CommonStyles(); // also registers fonts, gradients and dashes the master styles use

            var content = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(OfficeNs + "document-content", AllDeclarations(),
                    new XAttribute(OfficeNs + "version", "1.3"),
                    FontFaces(),
                    new XElement(OfficeNs + "automatic-styles", _content.Styles),
                    new XElement(OfficeNs + "body", body)));
            var styles = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(OfficeNs + "document-styles", AllDeclarations(), new XAttribute(OfficeNs + "version", "1.3"),
                    FontFaces(),
                    new XElement(OfficeNs + "styles", _named, commonStyles),
                    new XElement(OfficeNs + "automatic-styles", PageLayout(), _styles.Styles),
                    new XElement(OfficeNs + "master-styles", masterPages)));
            package.AddXml("content.xml", content);
            package.AddXml("styles.xml", styles);
            package.AddXml("meta.xml", OdfPackageWriter.Meta("ThixShow"));
        }

        private XElement FontFaces() => new(OfficeNs + "font-face-decls",
            _fonts.Select(font => new XElement(StyleNs + "font-face", new XAttribute(StyleNs + "name", font),
                new XAttribute(SvgNs + "font-family", font.Contains(' ') ? $"'{font}'" : font))));

        private XElement PageLayout()
        {
            var size = presentation.SlideSize;
            return new XElement(StyleNs + "page-layout", new XAttribute(StyleNs + "name", "PM1"),
                new XElement(StyleNs + "page-layout-properties",
                    new XAttribute(FoNs + "margin-top", "0cm"), new XAttribute(FoNs + "margin-bottom", "0cm"),
                    new XAttribute(FoNs + "margin-left", "0cm"), new XAttribute(FoNs + "margin-right", "0cm"),
                    new XAttribute(FoNs + "page-width", OdpNames.Cm(size.Width)), new XAttribute(FoNs + "page-height", OdpNames.Cm(size.Height)),
                    new XAttribute(StyleNs + "print-orientation", size.Width >= size.Height ? "landscape" : "portrait")));
        }

        // ------------------------------------------------------------ master pages

        /// <summary>One master page per master, and one per layout that brings a look of its own (used by a slide).</summary>
        private void PlanMasterPages()
        {
            foreach (var master in presentation.Masters)
            {
                PageOf(master, null);
            }

            foreach (var slide in presentation.Slides)
            {
                var layout = presentation.LayoutOf(slide);
                if (Contributes(layout))
                {
                    PageOf(presentation.MasterOf(slide), layout);
                }
            }
        }

        /// <summary>
        /// True when a layout looks different from its master (own shapes, background, hidden master shapes, text styles of its title
        /// or content placeholders): then it gets a master page of its own. The subtitle's style of a title layout goes into the
        /// master's "-subtitle" style and does not count.
        /// </summary>
        private static bool Contributes(SlideLayout layout) =>
            layout.Background is not null ||
            !layout.ShowMasterShapes ||
            layout.Shapes.Any(shape => shape.Placeholder is null) ||
            layout.Shapes.OfType<AutoShape>().Any(shape => shape.Placeholder is { } placeholder && !placeholder.IsFooterKind && placeholder.Type != PlaceholderType.Subtitle &&
                (shape.Text?.ListStyle is { IsEmpty: false } || shape.Fill is not null || shape.Outline is not null));

        private MasterPage PageOf(SlideMaster master, SlideLayout? layout)
        {
            if (_pages.FirstOrDefault(page => page.Master.Id == master.Id && page.Layout?.Id == layout?.Id) is { } existing)
            {
                return existing;
            }

            var masterName = master.Name ?? master.Theme.Name;
            var display = layout is null ? masterName : $"{masterName} – {layout.Name ?? layout.Type}";
            var name = OdpNames.StyleName(display.Replace(" – ", "-", StringComparison.Ordinal).Replace(' ', '_'));
            var unique = name;
            for (var number = 2; !_names.Add(unique); number++)
            {
                unique = name + number.ToString(CultureInfo.InvariantCulture);
            }

            var page = new MasterPage(unique, display, master, layout, _pages.Count + 1);
            _pages.Add(page);
            return page;
        }

        private MasterPage PageOf(Slide slide)
        {
            var layout = presentation.LayoutOf(slide);
            var master = presentation.MasterOf(slide);
            return PageOf(master, Contributes(layout) ? layout : null);
        }

        private SlideContext ContextOf(MasterPage page) =>
            page.Layout is { } layout ? SlideContext.ForLayout(presentation, page.Master, layout) : SlideContext.ForMaster(presentation, page.Master);

        /// <summary>The placeholder of the page that stands for <paramref name="type"/> (the layout's, else the master's).</summary>
        private static SlideElement? PlaceholderOf(MasterPage page, Func<PlaceholderInfo, bool> match)
        {
            if (page.Layout is { } layout && layout.Shapes.FirstOrDefault(shape => shape.Placeholder is { } placeholder && match(placeholder)) is { } fromLayout)
            {
                return fromLayout;
            }

            return page.Master.Shapes.FirstOrDefault(shape => shape.Placeholder is { } placeholder && match(placeholder));
        }

        private XElement WriteMasterPage(MasterPage page)
        {
            var context = ContextOf(page);
            var colors = ColorsOf(context);
            var background = new XElement(StyleNs + "drawing-page-properties");
            AddFill(background, ShapeResolver.ResolveBackground(context), context.Colors);
            var element = new XElement(StyleNs + "master-page", new XAttribute(StyleNs + "name", page.Name), new XAttribute(StyleNs + "display-name", page.DisplayName),
                new XAttribute(StyleNs + "page-layout-name", "PM1"),
                new XAttribute(DrawNs + "style-name", _styles.Add("drawing-page", "dp", [background])!));
            var scope = new ShapeScope(context, colors, _styles, $"m{page.Number}_", page, IsMasterPage: true);

            // The master's own shapes (unless the layout hides them), then the layout's.
            var shapes = new List<SlideElement>();
            if (page.Layout?.ShowMasterShapes != false)
            {
                shapes.AddRange(page.Master.Shapes.Where(shape => shape.Placeholder is null));
            }

            if (page.Layout is { } layout)
            {
                shapes.AddRange(layout.Shapes.Where(shape => shape.Placeholder is null));
            }

            foreach (var shape in shapes)
            {
                if (WriteShape(scope, shape, Identity) is { } written)
                {
                    element.Add(written);
                }
            }

            foreach (var (cls, match) in MasterClasses)
            {
                if (PlaceholderOf(page, match) is not { } placeholder)
                {
                    continue;
                }

                var resolved = ShapeResolver.Resolve(context, placeholder);
                var frame = new XElement(DrawNs + "frame");
                if (cls is "title" or "outline")
                {
                    frame.Add(new XAttribute(PresentationNs + "style-name", $"{page.Name}-{(cls == "title" ? "title" : "outline1")}"));
                }
                else
                {
                    frame.Add(new XAttribute(DrawNs + "style-name", FooterStyle(scope, resolved)));
                }

                frame.Add(new XAttribute(DrawNs + "layer", "backgroundobjects"));
                AddPosition(frame, resolved.Transform);
                frame.Add(new XAttribute(PresentationNs + "class", cls), new XAttribute(PresentationNs + "placeholder", "true"));
                var box = new XElement(DrawNs + "text-box");
                if (cls is not ("title" or "outline"))
                {
                    box.Add(new XElement(TextNs + "p", cls switch
                    {
                        "page-number" => new XElement(TextNs + "page-number", "<Nummer>"),
                        "footer" => new XElement(PresentationNs + "footer"),
                        "date-time" => new XElement(PresentationNs + "date-time"),
                        _ => null,
                    }));
                }

                frame.Add(box);
                element.Add(frame);
            }

            return element;
        }

        private static readonly (string Class, Func<PlaceholderInfo, bool> Match)[] MasterClasses =
        [
            ("title", placeholder => placeholder.IsTitle),
            ("outline", placeholder => placeholder.MasterType == PlaceholderType.Body && placeholder.Type != PlaceholderType.Subtitle),
            ("date-time", placeholder => placeholder.Type == PlaceholderType.DateAndTime),
            ("footer", placeholder => placeholder.Type == PlaceholderType.Footer),
            ("page-number", placeholder => placeholder.Type == PlaceholderType.SlideNumber),
        ];

        /// <summary>The graphic style of a footer frame of a master page: its look and its text's format.</summary>
        private string FooterStyle(ShapeScope scope, ResolvedShape resolved)
        {
            var graphic = new XElement(StyleNs + "graphic-properties");
            AddFill(graphic, resolved.Fill ?? NoFill.Instance, resolved.Colors);
            AddStroke(graphic, resolved.Outline ?? Outline.None, resolved.Colors);
            AddBody(graphic, EffectiveBody(resolved.Body));
            var paragraph = Complete(ShapeResolver.ParagraphOf(resolved, TextParagraph.Empty));
            return scope.Sink.Add("graphic", "gr", [graphic, ParagraphProperties(Reduced(paragraph)), TextProperties(paragraph.DefaultRun, scope.Colors)])!;
        }

        /// <summary>The presentation styles of a master page: title, subtitle, outline levels 1–9 (with the bullets), notes.</summary>
        private IEnumerable<XElement> PresentationStyles(MasterPage page)
        {
            var context = ContextOf(page);
            var colors = ColorsOf(context);

            ResolvedShape ResolveOf(SlideElement? placeholder, PlaceholderType type) =>
                ShapeResolver.Resolve(context, placeholder ?? new AutoShape { Id = 1, Placeholder = new PlaceholderInfo(type), Text = TextBody.Empty });

            var title = ResolveOf(PlaceholderOf(page, placeholder => placeholder.IsTitle), PlaceholderType.Title);
            yield return PresentationStyle($"{page.Name}-title", null, title, ShapeResolver.ParagraphOf(title, TextParagraph.Empty), colors, graphicToo: true);

            var outline = ResolveOf(PlaceholderOf(page, placeholder => placeholder.MasterType == PlaceholderType.Body && placeholder.Type != PlaceholderType.Subtitle), PlaceholderType.Body);
            var levels = Enumerable.Range(0, 9).Select(level => ShapeResolver.ParagraphOf(outline, new TextParagraph(new ParagraphProperties(Level: level), []))).ToList();
            var first = PresentationStyle($"{page.Name}-outline1", null, outline, levels[0], colors, graphicToo: true);
            var listLevels = levels.Select((level, index) => ListLevel(index + 1, level, colors)).Where(level => level is not null).ToList();
            first.Element(StyleNs + "graphic-properties")!.Add(new XElement(TextNs + "list-style", new XAttribute(StyleNs + "name", $"{page.Name}-outline1"), listLevels));
            yield return first;
            for (var level = 1; level < 9; level++)
            {
                yield return PresentationStyle($"{page.Name}-outline{level + 1}", $"{page.Name}-outline{level}", outline, levels[level], colors, graphicToo: false);
            }

            // The subtitle: a title layout's subtitle placeholder (of this page's layout, else the master's first title layout).
            var titleLayout = page.Layout?.Type == "title" ? page.Layout : page.Master.Layouts.FirstOrDefault(layout => layout.Type == "title");
            var subtitleShape = titleLayout?.Shapes.FirstOrDefault(shape => shape.Placeholder?.Type == PlaceholderType.Subtitle);
            var subtitle = subtitleShape is not null && titleLayout is not null
                ? ShapeResolver.Resolve(SlideContext.ForLayout(presentation, page.Master, titleLayout), subtitleShape)
                : outline;
            var subtitleParagraph = ShapeResolver.ParagraphOf(subtitle, TextParagraph.Empty);
            var subtitleStyle = PresentationStyle($"{page.Name}-subtitle", null, subtitle, subtitleParagraph, colors, graphicToo: true);
            if (ListLevel(1, subtitleParagraph, colors) is { } subtitleBullet)
            {
                subtitleStyle.Element(StyleNs + "graphic-properties")!.Add(new XElement(TextNs + "list-style", new XAttribute(StyleNs + "name", $"{page.Name}-subtitle"), subtitleBullet));
            }

            yield return subtitleStyle;

            var notes = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", $"{page.Name}-notes"), new XAttribute(StyleNs + "family", "presentation"),
                new XElement(StyleNs + "paragraph-properties", new XAttribute(FoNs + "margin-left", "0cm"), new XAttribute(FoNs + "text-indent", "0cm")),
                TextProperties(new RunProperties(Size: 1200, Latin: new TextFont("+mn-lt")), colors));
            yield return notes;
        }

        private XElement PresentationStyle(string name, string? parent, ResolvedShape shape, ParagraphProperties paragraph, SlideContextColors colors, bool graphicToo)
        {
            var element = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "presentation"));
            if (parent is not null)
            {
                element.Add(new XAttribute(StyleNs + "parent-style-name", parent));
            }

            var graphic = new XElement(StyleNs + "graphic-properties");
            if (graphicToo)
            {
                AddFill(graphic, shape.Fill ?? NoFill.Instance, shape.Colors);
                AddStroke(graphic, shape.Outline ?? Outline.None, shape.Colors);
                AddBody(graphic, EffectiveBody(shape.Body));
            }

            paragraph = Complete(paragraph);
            element.Add(graphic, ParagraphProperties(Reduced(paragraph)), TextProperties(paragraph.DefaultRun, colors));
            return element;
        }

        /// <summary>Body properties with ThixShow's defaults filled in (insets, top anchoring, wrapping).</summary>
        private static BodyProperties EffectiveBody(BodyProperties body) => new BodyProperties(
            ShapeResolver.DefaultHorizontalInset, ShapeResolver.DefaultVerticalInset, ShapeResolver.DefaultHorizontalInset, ShapeResolver.DefaultVerticalInset,
            TextAnchor.Top, Wrap: true).Merge(body);

        /// <summary>
        /// A style's paragraph with every value it can carry set (alignment, indents, spacing; not bold, not italic …): a style read back
        /// is complete, so it says the same when written again over other styles.
        /// </summary>
        private static ParagraphProperties Complete(ParagraphProperties paragraph) => new ParagraphProperties(
            Alignment: TextAlignment.Left, LeftMargin: 0, RightMargin: 0, Indent: 0, LineSpacing: TextSpacing.Percent(100000), SpaceBefore: TextSpacing.Points(0), SpaceAfter: TextSpacing.Points(0),
            DefaultRun: new RunProperties(Bold: false, Italic: false, Underline: "none", Strike: "noStrike", Baseline: 0, Caps: "none", Spacing: 0)).Merge(paragraph);

        /// <summary>The paragraph properties ODF's paragraph-properties carry (bullets and run defaults are written elsewhere).</summary>
        private static ParagraphProperties Reduced(ParagraphProperties paragraph) => new(
            Alignment: paragraph.Alignment,
            LeftMargin: paragraph.LeftMargin,
            RightMargin: paragraph.RightMargin,
            Indent: paragraph.Indent,
            LineSpacing: paragraph.LineSpacing,
            SpaceBefore: paragraph.SpaceBefore,
            SpaceAfter: paragraph.SpaceAfter,
            DefaultRun: paragraph.DefaultRun is { Size: { } size } ? new RunProperties(Size: size) : null);

        /// <summary>office:styles: the base graphic style, every master page's presentation styles.</summary>
        private List<XElement> CommonStyles()
        {
            var styles = new List<XElement>();
            var master = presentation.Masters.Count > 0 ? presentation.Masters[0] : null;
            var context = master is null ? null : SlideContext.ForMaster(presentation, master);
            var graphic = new XElement(StyleNs + "graphic-properties",
                new XAttribute(DrawNs + "fill", "none"), new XAttribute(DrawNs + "stroke", "none"));
            AddBody(graphic, EffectiveBody(new BodyProperties(AutoFit: NoAutoFit.Instance)));
            var baseStyle = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", OdpNames.BaseStyleName), new XAttribute(StyleNs + "family", "graphic"), graphic);
            if (context is not null)
            {
                var resolved = ShapeResolver.Resolve(context, new AutoShape { Id = 1, Text = TextBody.Empty });
                var paragraph = Complete(ShapeResolver.ParagraphOf(resolved, TextParagraph.Empty));
                baseStyle.Add(ParagraphProperties(Reduced(paragraph)), TextProperties(paragraph.DefaultRun, ColorsOf(context)));
            }

            styles.Add(baseStyle);
            foreach (var page in _pages)
            {
                styles.AddRange(PresentationStyles(page));
            }

            return styles;
        }

        // ------------------------------------------------------------ slides

        private XElement WriteSlide(Slide slide, int index)
        {
            var page = PageOf(slide);
            var context = SlideContext.For(presentation, slide);
            var colors = ColorsOf(context);
            var element = new XElement(DrawNs + "page", new XAttribute(DrawNs + "name", slide.Name ?? $"page{index + 1}"));
            var scope = new ShapeScope(context, colors, _content, $"s{index + 1}_", page, IsMasterPage: false);

            var properties = new XElement(StyleNs + "drawing-page-properties");
            if (slide.Background is not null)
            {
                AddFill(properties, ShapeResolver.ResolveBackground(context), context.Colors);
            }

            properties.Add(new XAttribute(PresentationNs + "background-visible", "true"),
                new XAttribute(PresentationNs + "background-objects-visible", Boolean(slide.ShowMasterShapes)));
            var footers = slide.Shapes.OfType<AutoShape>().Where(shape => shape.Placeholder is { IsFooterKind: true }).ToList();
            var footer = footers.FirstOrDefault(shape => shape.Placeholder!.Type == PlaceholderType.Footer);
            var date = footers.FirstOrDefault(shape => shape.Placeholder!.Type == PlaceholderType.DateAndTime);
            var number = footers.Any(shape => shape.Placeholder!.Type == PlaceholderType.SlideNumber);
            properties.Add(new XAttribute(PresentationNs + "display-footer", Boolean(footer is not null)),
                new XAttribute(PresentationNs + "display-page-number", Boolean(number)),
                new XAttribute(PresentationNs + "display-date-time", Boolean(date is not null)));
            if (slide.Hidden)
            {
                properties.Add(new XAttribute(PresentationNs + "visibility", "hidden"));
            }

            var timingRoot = new XElement(AnimNs + "par", new XAttribute(PresentationNs + "node-type", "timing-root"));
            AddTransition(properties, timingRoot, slide.Transition);
            element.Add(new XAttribute(DrawNs + "style-name", _content.Add("drawing-page", "dp", [properties])!),
                new XAttribute(DrawNs + "master-page-name", page.Name));
            if (footer is not null)
            {
                var name = $"ftr{index + 1}";
                _decls.Add(new XElement(PresentationNs + "footer-decl", new XAttribute(PresentationNs + "name", name), footer.Text?.PlainText ?? ""));
                element.Add(new XAttribute(PresentationNs + "use-footer-name", name));
            }

            if (date is not null)
            {
                var name = $"dtd{index + 1}";
                var isField = date.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Any(run => run is TextField { Type: { } type } && type.StartsWith("datetime", StringComparison.Ordinal)) == true;
                _decls.Add(isField
                    ? new XElement(PresentationNs + "date-time-decl", new XAttribute(PresentationNs + "name", name), new XAttribute(PresentationNs + "source", "current-date"))
                    : new XElement(PresentationNs + "date-time-decl", new XAttribute(PresentationNs + "name", name), new XAttribute(PresentationNs + "source", "fixed"), date.Text?.PlainText ?? ""));
                element.Add(new XAttribute(PresentationNs + "use-date-time-name", name));
            }

            foreach (var shape in slide.Shapes)
            {
                if (shape.Placeholder is { IsFooterKind: true })
                {
                    continue;
                }

                if (WriteShape(scope, shape, Identity) is { } written)
                {
                    element.Add(written);
                }
            }

            AddAnimations(timingRoot, slide, scope);
            if (timingRoot.HasElements)
            {
                element.Add(timingRoot);
            }

            if (slide.Notes is { } notes && notes.Text.Length > 0)
            {
                element.Add(WriteNotes(slide, notes, index));
            }

            return element;
        }

        private XElement WriteNotes(Slide slide, NotesSlide notes, int index)
        {
            var context = SlideContext.ForNotes(presentation, slide, notes);
            var size = presentation.NotesSize;
            var page = PageOf(slide);
            var thumbnail = new XElement(DrawNs + "page-thumbnail", new XAttribute(DrawNs + "layer", "layout"),
                new XAttribute(SvgNs + "width", OdpNames.Cm(size.Width * 8 / 10)), new XAttribute(SvgNs + "height", OdpNames.Cm(size.Height * 4 / 10)),
                new XAttribute(SvgNs + "x", OdpNames.Cm(size.Width / 10)), new XAttribute(SvgNs + "y", OdpNames.Cm(size.Height / 20)),
                new XAttribute(DrawNs + "page-number", index + 1), new XAttribute(PresentationNs + "class", "page"));
            var body = notes.NotesBody!;
            var scope = new ShapeScope(context, ColorsOf(context), _content, $"n{index + 1}_", page, IsMasterPage: false);
            var resolved = ShapeResolver.Resolve(context, body);
            var frame = new XElement(DrawNs + "frame", new XAttribute(PresentationNs + "style-name", $"{page.Name}-notes"), new XAttribute(DrawNs + "layer", "layout"),
                new XAttribute(SvgNs + "width", OdpNames.Cm(size.Width * 8 / 10)), new XAttribute(SvgNs + "height", OdpNames.Cm(size.Height * 4 / 10)),
                new XAttribute(SvgNs + "x", OdpNames.Cm(size.Width / 10)), new XAttribute(SvgNs + "y", OdpNames.Cm(size.Height / 2)),
                new XAttribute(PresentationNs + "class", "notes"),
                new XElement(DrawNs + "text-box", WriteText(scope, body.Text!, resolved, outline: false)));
            return new XElement(PresentationNs + "notes", thumbnail, frame);
        }

        private SlideContextColors ColorsOf(SlideContext context) => new(context.Colors, context.Theme.Fonts);

        // ------------------------------------------------------------ pictures

        /// <summary>The package path of a picture (each content once).</summary>
        private string Picture(ImageData image)
        {
            if (_pictures.TryGetValue(image.Key, out var existing))
            {
                return existing;
            }

            var path = package.AddPicture(image.Bytes, image.Extension);
            _pictures[image.Key] = path;
            return path;
        }
    }
}

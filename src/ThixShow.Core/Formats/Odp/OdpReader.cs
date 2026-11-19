using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

/// <summary>
/// Reads an OpenDocument Presentation (.odp, .otp) into the model, tolerantly: unknown elements are skipped, what ThixShow does not
/// show (charts, OLE objects, media, 3D scenes, comments, effects other than appear/fade …) is counted in
/// <see cref="Presentation.UnsupportedContent"/> for the opening warning (it is not kept: an odp is written anew). Each
/// style:master-page becomes a master (title, outline and footer placeholders, the title/outline/subtitle presentation styles as text
/// styles, the background) with ThixShow's layouts Titelfolie, Titel und Inhalt, Zwei Inhalte, Nur Titel and Leer; a slide's layout
/// follows from its placeholder classes. A file that is no odp, or whose XML is broken, is an <see cref="InvalidDataException"/> with a
/// German message.
/// </summary>
public static partial class OdpReader
{
    public const string MimeType = OdpNames.MimeType;

    /// <summary>"Keine Formatvorlage, kein Raster": the table style of tables read from odp (their cells carry their own look).</summary>
    public const string NoStyleTableId = "{2D5ABB26-0587-4C30-8999-92F81FD0307C}";

    public static TableStyle NoStyleTable { get; } = new(NoStyleTableId, "Keine Formatvorlage, kein Raster", []);

    /// <summary>True when the stream is a ZIP whose mimetype says OpenDocument Presentation (or its template); the position is kept.</summary>
    public static bool IsOdp(Stream stream) => OdfPackage.HasMimeType(stream, OdpNames.MimeType, OdpNames.TemplateMimeType);

    public static Presentation Read(Stream stream)
    {
        try
        {
            return OdfPackage.Read(stream, OdpNames.InvalidMessage, package => new Reader(package).Read());
        }
        catch (InvalidDataException exception) when (exception.Message != OdpNames.InvalidMessage)
        {
            throw new InvalidDataException(OdpNames.InvalidMessage, exception);
        }
        catch (Exception exception) when (exception is not (InvalidDataException or OutOfMemoryException or OperationCanceledException))
        {
            throw new InvalidDataException(OdpNames.InvalidMessage, exception);
        }
    }

    /// <summary>A master read from a master page: its rectangles for title and content, the subtitle's text style.</summary>
    private sealed record MasterInfo(string PageName, SlideMaster Master, EmuRect TitleRect, EmuRect BodyRect, ListStyle Subtitle)
    {
        public SlideLayout Layout(string type) => Master.Layouts.FirstOrDefault(layout => layout.Type == type) ?? Master.Layouts[0];
    }

    /// <summary>Where shapes are read: the part their styles are in, their model ids, the master of the page.</summary>
    private sealed record ReadScope(Part Part, Dictionary<XElement, int> Ids, Dictionary<string, int> IdsByName, MasterInfo? Master);

    private sealed partial class Reader
    {
        private static readonly string[] ShapeNames = ["custom-shape", "rect", "ellipse", "circle", "line", "connector", "polyline", "polygon", "path", "regular-polygon", "frame", "g", "caption", "measure", "control", "page-thumbnail"];

        private readonly OdfPackage _package;
        private readonly XDocument _content;
        private readonly XDocument? _styles;
        private readonly StyleIndex _index;
        private readonly Dictionary<string, int> _losses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImageData?> _pictures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> _slideIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _footers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (bool Field, string Text)> _dates = new(StringComparer.Ordinal);
        private uint _nextMasterId = MasterFactory.FirstMasterId;
        private bool _hasTables;

        public Reader(OdfPackage package)
        {
            _package = package;
            _content = package.Xml("content.xml")!;
            _styles = package.Xml("styles.xml");
            _index = new StyleIndex(_content, _styles);
        }

        private void Lose(string kind, int count = 1)
        {
            if (count > 0)
            {
                _losses[kind] = _losses.GetValueOrDefault(kind) + count;
            }
        }

        public Presentation Read()
        {
            var body = _content.Root?.Element(OfficeNs + "body")?.Element(OfficeNs + "presentation") ?? throw new InvalidDataException(OdpNames.InvalidMessage);
            var masterPages = _styles?.Root?.Element(OfficeNs + "master-styles")?.Elements(StyleNs + "master-page").ToList() ?? [];
            var size = SlideSizeOf(masterPages.FirstOrDefault());
            var defaultText = _index.Base() is { } baseStyle
                ? new ListStyle(ParagraphFrom(baseStyle) ?? ParagraphProperties.Empty, ValueList<ParagraphProperties?>.Empty)
                : MasterFactory.DefaultTextStyle();

            var masters = new List<MasterInfo>();
            for (var index = 0; index < masterPages.Count; index++)
            {
                masters.Add(ReadMaster(masterPages[index], index, size));
            }

            if (masters.Count == 0)
            {
                var fallback = MasterFactory.CreateMaster(BuiltInThemes.ThixShow, size, _nextMasterId);
                masters.Add(Info("Standard", fallback, size));
            }

            foreach (var declaration in body.Elements(PresentationNs + "footer-decl"))
            {
                _footers[(string?)declaration.Attribute(PresentationNs + "name") ?? ""] = declaration.Value;
            }

            foreach (var declaration in body.Elements(PresentationNs + "date-time-decl"))
            {
                _dates[(string?)declaration.Attribute(PresentationNs + "name") ?? ""] = ((string?)declaration.Attribute(PresentationNs + "source") == "current-date", declaration.Value);
            }

            var pages = body.Elements(DrawNs + "page").ToList();
            for (var index = 0; index < pages.Count; index++)
            {
                var name = (string?)pages[index].Attribute(DrawNs + "name") ?? $"page{index + 1}";
                _slideIds.TryAdd(name, (uint)(256 + index));
            }

            var slides = pages.Select((page, index) => ReadSlide(page, index, masters)).ToList();
            if (slides.Count == 0)
            {
                slides.Add(MasterFactory.CreateSlide(masters[0].Layout("title"), 256));
            }

            Lose("animation", body.Descendants(PresentationNs + "animations").SelectMany(animations => animations.Elements()).Count());
            Lose("form", body.Descendants(OfficeNs + "forms").SelectMany(forms => forms.Elements()).Count());
            if (_package.Paths.Any(path => path.StartsWith("Basic/", StringComparison.Ordinal) || path.StartsWith("Scripts/", StringComparison.Ordinal)))
            {
                Lose("macros");
            }

            return new Presentation
            {
                SlideSize = size,
                DefaultTextStyle = defaultText,
                Masters = masters.Select(master => master.Master).ToList(),
                Slides = slides,
                TableStyles = _hasTables ? [NoStyleTable] : default,
                UnsupportedContent = _losses.Select(pair => new UnsupportedContent(pair.Key, pair.Value)).ToList(),
            };
        }

        private SlideSize SlideSizeOf(XElement? masterPage)
        {
            var layout = _index.PageLayout((string?)masterPage?.Attribute(StyleNs + "page-layout-name"))?.Element(StyleNs + "page-layout-properties");
            var width = OdpNames.Emu((string?)layout?.Attribute(FoNs + "page-width"));
            var height = OdpNames.Emu((string?)layout?.Attribute(FoNs + "page-height"));
            if (width is not > 0 || height is not > 0)
            {
                return SlideSize.Widescreen;
            }

            foreach (var known in new[] { SlideSize.Widescreen, SlideSize.Standard, SlideSize.OnScreen16x9 })
            {
                if (Math.Abs(known.Width - width.Value) <= 1 && Math.Abs(known.Height - height.Value) <= 1)
                {
                    return known;
                }
            }

            return new SlideSize(width.Value, height.Value);
        }

        // ------------------------------------------------------------ masters

        private MasterInfo ReadMaster(XElement page, int index, SlideSize size)
        {
            var name = (string?)page.Attribute(StyleNs + "name") ?? $"Master{index + 1}";
            var display = (string?)page.Attribute(StyleNs + "display-name") ?? name.Replace("_20_", " ", StringComparison.Ordinal);
            var scope = new ReadScope(Part.Styles, AssignIds(page, $"m{index + 1}_", out var byName), byName, null);
            var shapes = new List<SlideElement>();
            var placeholders = new List<SlideElement>();
            EmuRect? title = null, outline = null;
            foreach (var child in page.Elements())
            {
                var cls = (string?)child.Attribute(PresentationNs + "class");
                if (child.Name == DrawNs + "frame" && cls is "title" or "outline" or "date-time" or "footer" or "page-number" or "header")
                {
                    var placeholder = ReadMasterPlaceholder(child, cls, name, scope);
                    placeholders.Add(placeholder);
                    var rect = placeholder.Transform!.Bounds;
                    title ??= cls == "title" ? rect : null;
                    outline ??= cls == "outline" ? rect : null;
                    continue;
                }

                if (cls is not null)
                {
                    continue; // a subtitle, notes or handout placeholder of the master page: ThixShow's layouts bring their own
                }

                if (ReadShape(child, scope) is { } shape)
                {
                    shapes.Add(shape);
                }
            }

            var titleRect = title ?? Rect(size, 0.05, 0.053, 0.9, 0.193);
            var bodyRect = outline ?? Rect(size, 0.05, 0.267, 0.9, 0.635);
            var titleStyle = StyleOf("presentation", $"{name}-title");
            var outlineStyle = StyleOf("presentation", $"{name}-outline1");
            if (title is null)
            {
                placeholders.Insert(0, MasterPlaceholder(2, "Titelplatzhalter 1", new PlaceholderInfo(PlaceholderType.Title), titleRect, titleStyle));
            }

            if (outline is null)
            {
                placeholders.Insert(1, MasterPlaceholder(3, "Textplatzhalter 2", new PlaceholderInfo(PlaceholderType.Body, 1), bodyRect, outlineStyle));
            }

            var titleParagraph = (ParagraphFrom(titleStyle) ?? ParagraphProperties.Empty) with { Bullet = NoBullet.Instance };
            var listStyle = outlineStyle.ListStyle;
            var levels = new List<ParagraphProperties?>();
            for (var level = 1; level <= 9; level++)
            {
                var properties = ParagraphFrom(StyleOf("presentation", $"{name}-outline{level}")) ?? ParagraphProperties.Empty;
                levels.Add(WithBullet(properties, listStyle, level));
            }

            var subtitleStyle = StyleOf("presentation", $"{name}-subtitle");
            var subtitle = subtitleStyle.IsEmpty
                ? ListStyle.Empty
                : new ListStyle(null, [WithBullet(ParagraphFrom(subtitleStyle) ?? ParagraphProperties.Empty, subtitleStyle.ListStyle, 1)]);

            var majorFont = titleParagraph.DefaultRun?.Latin?.Typeface ?? BuiltInThemes.ThixShow.Theme.Fonts.Major.Latin;
            var baseFont = _index.Base() is { } baseStyle ? ReadRunProperties(baseStyle.Of("text-properties"))?.Latin?.Typeface : null;
            var minorFont = levels[0]?.DefaultRun?.Latin?.Typeface ?? baseFont ?? BuiltInThemes.ThixShow.Theme.Fonts.Minor.Latin;
            var theme = BuiltInThemes.ThixShow.Theme with
            {
                Name = display,
                Fonts = new FontScheme(display, new FontCollection(majorFont), new FontCollection(minorFont)),
                Formats = BuiltInThemes.Formats(display),
            };
            var background = ReadFill(StyleOf("drawing-page", (string?)page.Attribute(DrawNs + "style-name")).Of("drawing-page-properties"));
            var master = new SlideMaster
            {
                Id = _nextMasterId++,
                Name = display,
                Theme = theme,
                Shapes = [.. shapes, .. placeholders],
                Background = new Background(Fill: background is null or NoFill ? new SolidFill(ColorSpec.Rgb("FFFFFF")) : background),
                TextStyles = new MasterTextStyles(new ListStyle(null, [titleParagraph]), new ListStyle(null, levels), ListStyle.Empty),
            };
            return Info(name, master with { Layouts = Layouts(titleRect, bodyRect, subtitle) }, size);
        }

        private Props StyleOf(string family, string? name) => _index.Resolve(family, name, Part.Styles);

        private static EmuRect Rect(SlideSize size, double x, double y, double width, double height) =>
            new((long)Math.Round(size.Width * x), (long)Math.Round(size.Height * y), (long)Math.Round(size.Width * width), (long)Math.Round(size.Height * height));

        private static MasterInfo Info(string name, SlideMaster master, SlideSize size)
        {
            EmuRect RectOf(Func<PlaceholderInfo, bool> match, EmuRect fallback) =>
                master.Shapes.FirstOrDefault(shape => shape.Placeholder is { } placeholder && match(placeholder))?.Transform?.Bounds ?? fallback;
            return new MasterInfo(name, master,
                RectOf(placeholder => placeholder.IsTitle, Rect(size, 0.05, 0.053, 0.9, 0.193)),
                RectOf(placeholder => placeholder.Type == PlaceholderType.Body, Rect(size, 0.05, 0.267, 0.9, 0.635)),
                master.Layouts.FirstOrDefault(layout => layout.Type == "title")?.Shapes.OfType<AutoShape>().FirstOrDefault(shape => shape.Placeholder?.Type == PlaceholderType.Subtitle)?.Text?.ListStyle ?? ListStyle.Empty);
        }

        /// <summary>A paragraph's look from a style: paragraph properties and the run defaults of its text properties (null when it has neither).</summary>
        private ParagraphProperties? ParagraphFrom(Props props)
        {
            var paragraph = ReadParagraphProperties(props.Of("paragraph-properties"));
            var run = ReadRunProperties(props.Of("text-properties"));
            if (paragraph == ParagraphProperties.Empty && run is null)
            {
                return null;
            }

            return paragraph with { DefaultRun = run };
        }

        /// <summary>A level's properties with the bullet of the list style at that level (no bullet: NoBullet).</summary>
        private ParagraphProperties WithBullet(ParagraphProperties properties, XElement? listStyle, int level)
        {
            var (bullet, font, color, size) = ReadBullet(listStyle, level);
            return properties with { Bullet = bullet ?? NoBullet.Instance, BulletFont = font, BulletColor = color, BulletSize = size };
        }

        private static AutoShape MasterPlaceholder(int id, string name, PlaceholderInfo placeholder, EmuRect rect, Props style) => new()
        {
            Id = id,
            Name = name,
            Placeholder = placeholder,
            Transform = Transform2D.Of(rect),
            Text = new TextBody(ReadBody(style.Of("graphic-properties")), ListStyle.Empty, [TextParagraph.Empty]),
        };

        /// <summary>A placeholder of a master page: title and outline take their look from the presentation styles, footers from their own style.</summary>
        private AutoShape ReadMasterPlaceholder(XElement frame, string cls, string pageName, ReadScope scope)
        {
            var type = cls switch
            {
                "title" => PlaceholderType.Title,
                "outline" => PlaceholderType.Body,
                "date-time" => PlaceholderType.DateAndTime,
                "footer" => PlaceholderType.Footer,
                "page-number" => PlaceholderType.SlideNumber,
                _ => PlaceholderType.Header,
            };
            var info = type switch
            {
                PlaceholderType.Title => new PlaceholderInfo(PlaceholderType.Title),
                PlaceholderType.Body => new PlaceholderInfo(PlaceholderType.Body, 1),
                PlaceholderType.DateAndTime => new PlaceholderInfo(PlaceholderType.DateAndTime, 2, Size: "half"),
                PlaceholderType.Footer => new PlaceholderInfo(PlaceholderType.Footer, 3, Size: "quarter"),
                PlaceholderType.SlideNumber => new PlaceholderInfo(PlaceholderType.SlideNumber, 4, Size: "quarter"),
                _ => new PlaceholderInfo(PlaceholderType.Header, 5, Size: "quarter"),
            };
            var id = scope.Ids.GetValueOrDefault(frame, 2);
            var props = cls is "title" or "outline"
                ? _index.Resolve("presentation", (string?)frame.Attribute(PresentationNs + "style-name") ?? $"{pageName}-{(cls == "title" ? "title" : "outline1")}", Part.Styles)
                : _index.Resolve("graphic", (string?)frame.Attribute(DrawNs + "style-name"), Part.Styles);
            var graphic = props.Of("graphic-properties");
            var box = frame.Element(DrawNs + "text-box");
            var list = cls is "title" or "outline" ? ListStyle.Empty : ParagraphFrom(props) is { } own ? new ListStyle(own, ValueList<ParagraphProperties?>.Empty) : ListStyle.Empty;
            var paragraphs = cls is "title" or "outline" || box is null ? [TextParagraph.Empty] : ReadParagraphs(box.Elements(), scope, _ => null, null);
            return new AutoShape
            {
                Id = id,
                Name = (string?)frame.Attribute(DrawNs + "name") ?? MasterFactory.PlaceholderName(info, id),
                Placeholder = info,
                Transform = ReadPosition(frame),
                Fill = ReadFill(graphic),
                Outline = ReadOutline(graphic),
                Text = new TextBody(ReadBody(graphic), list, paragraphs),
            };
        }

        /// <summary>ThixShow's layouts for a master read from odp: placeholders inherit from the master (two contents split its body).</summary>
        private ValueList<SlideLayout> Layouts(EmuRect title, EmuRect body, ListStyle subtitle)
        {
            var layouts = new List<SlideLayout>();
            SlideLayout Layout(string type, string name, params SlideElement[] shapes) => new()
            {
                Id = _nextMasterId++,
                Type = type,
                Name = name,
                Shapes =
                [
                    .. shapes,
                    Prompt(10, "Datumsplatzhalter", new PlaceholderInfo(PlaceholderType.DateAndTime, 10, Size: "half"), null, ""),
                    Prompt(11, "Fußzeilenplatzhalter", new PlaceholderInfo(PlaceholderType.Footer, 11, Size: "quarter"), null, ""),
                    Prompt(12, "Foliennummernplatzhalter", new PlaceholderInfo(PlaceholderType.SlideNumber, 12, Size: "quarter"), null, ""),
                ],
            };

            var (left, right) = Halves(body);
            layouts.Add(Layout("title", "Titelfolie",
                Prompt(2, "Titel 1", new PlaceholderInfo(PlaceholderType.CenteredTitle), null, "Titel durch Klicken hinzufügen"),
                Prompt(3, "Untertitel 2", new PlaceholderInfo(PlaceholderType.Subtitle, 1), null, "Untertitel durch Klicken hinzufügen", subtitle)));
            layouts.Add(Layout("obj", "Titel und Inhalt",
                Prompt(2, "Titel 1", new PlaceholderInfo(PlaceholderType.Title), null, "Titel durch Klicken hinzufügen"),
                Prompt(3, "Inhaltsplatzhalter 2", new PlaceholderInfo(PlaceholderType.Object, 1), null, "Text durch Klicken hinzufügen")));
            layouts.Add(Layout("twoObj", "Zwei Inhalte",
                Prompt(2, "Titel 1", new PlaceholderInfo(PlaceholderType.Title), null, "Titel durch Klicken hinzufügen"),
                Prompt(3, "Inhaltsplatzhalter 2", new PlaceholderInfo(PlaceholderType.Object, 1, Size: "half"), left, "Text durch Klicken hinzufügen"),
                Prompt(4, "Inhaltsplatzhalter 3", new PlaceholderInfo(PlaceholderType.Object, 2, Size: "half"), right, "Text durch Klicken hinzufügen")));
            layouts.Add(Layout("titleOnly", "Nur Titel",
                Prompt(2, "Titel 1", new PlaceholderInfo(PlaceholderType.Title), null, "Titel durch Klicken hinzufügen")));
            layouts.Add(Layout("blank", "Leer"));
            _ = title;
            return layouts;
        }

        private static (EmuRect Left, EmuRect Right) Halves(EmuRect body)
        {
            var width = body.Width * 48 / 100;
            return (body with { Width = width }, body with { X = body.X + body.Width - width, Width = width });
        }

        private static AutoShape Prompt(int id, string name, PlaceholderInfo placeholder, EmuRect? rect, string prompt, ListStyle? list = null) => new()
        {
            Id = id,
            Name = name,
            Placeholder = placeholder with { HasCustomPrompt = prompt.Length > 0 && placeholder.Type is not (PlaceholderType.Title or PlaceholderType.Body) },
            Transform = rect is { } bounds ? Transform2D.Of(bounds) : null,
            Text = new TextBody(BodyProperties.Empty, list ?? ListStyle.Empty, [TextParagraph.Plain(prompt)]),
        };

        // ------------------------------------------------------------ ids

        /// <summary>
        /// The model ids of a page's shapes: "&lt;prefix&gt;&lt;n&gt;" ids (what ThixShow writes) keep their number, the others get the
        /// next free ones in document order. Notes are a page of their own.
        /// </summary>
        private static Dictionary<XElement, int> AssignIds(XElement page, string prefix, out Dictionary<string, int> byName)
        {
            var ids = new Dictionary<XElement, int>();
            byName = new Dictionary<string, int>(StringComparer.Ordinal);
            var shapes = Shapes(page).ToList();
            var used = new HashSet<int>();
            var pattern = new Regex("^" + Regex.Escape(prefix) + "(\\d{1,9})$", RegexOptions.CultureInvariant);
            foreach (var shape in shapes)
            {
                if (RawId(shape) is { } raw && pattern.Match(raw) is { Success: true } match && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) is var number && number > 1 && used.Add(number))
                {
                    ids[shape] = number;
                    byName[raw] = number;
                }
            }

            var next = used.DefaultIfEmpty(1).Max() + 1;
            foreach (var shape in shapes.Where(shape => !ids.ContainsKey(shape)))
            {
                ids[shape] = next;
                if (RawId(shape) is { } raw)
                {
                    byName.TryAdd(raw, next);
                }

                next++;
            }

            return ids;
        }

        private static string? RawId(XElement shape) => (string?)shape.Attribute(XNamespace.Xml + "id") ?? (string?)shape.Attribute(DrawNs + "id");

        /// <summary>The shape elements of a page (inside groups too), not those of its notes.</summary>
        private static IEnumerable<XElement> Shapes(XElement page)
        {
            foreach (var child in page.Elements())
            {
                if (child.Name.Namespace != DrawNs || !ShapeNames.Contains(child.Name.LocalName))
                {
                    if (child.Name == DrawNs + "a")
                    {
                        foreach (var inner in Shapes(child))
                        {
                            yield return inner;
                        }
                    }

                    continue;
                }

                yield return child;
                if (child.Name.LocalName == "g")
                {
                    foreach (var inner in Shapes(child))
                    {
                        yield return inner;
                    }
                }
            }
        }

        // ------------------------------------------------------------ pictures

        private ImageData? Picture(string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return null;
            }

            if (_pictures.TryGetValue(href, out var cached))
            {
                return cached;
            }

            var bytes = _package.Bytes(href);
            var image = bytes is { Length: > 0 } ? ImageData.From(bytes, OdfPackageWriter.MediaTypeOf(Path.GetExtension(href))) : null;
            _pictures[href] = image;
            return image;
        }
    }
}

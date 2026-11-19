using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpReader
{
    private sealed partial class Reader
    {
        private static readonly string[] ContentClasses = ["outline", "text", "graphic", "object", "chart", "table", "orgchart"];

        // ------------------------------------------------------------ slides

        private Slide ReadSlide(XElement page, int index, List<MasterInfo> masters)
        {
            var masterName = (string?)page.Attribute(DrawNs + "master-page-name");
            var master = masters.FirstOrDefault(item => item.PageName == masterName) ?? masters[0];
            var scope = new ReadScope(Part.Content, AssignIds(page, $"s{index + 1}_", out var byName), byName, master);

            // The layout follows from the placeholders the slide has.
            var classes = Shapes(page).Where(shape => shape.Parent == page).Select(shape => (string?)shape.Attribute(PresentationNs + "class")).OfType<string>().ToList();
            var contents = classes.Count(ContentClasses.Contains);
            var layoutType = classes.Contains("subtitle") ? "title" : contents >= 2 ? "twoObj" : contents == 1 ? "obj" : classes.Contains("title") ? "titleOnly" : "blank";
            var layout = master.Layout(layoutType);

            var shapes = new List<SlideElement>();
            var contentIndex = 0;
            foreach (var child in page.Elements())
            {
                var cls = (string?)child.Attribute(PresentationNs + "class");
                if (child.Name == DrawNs + "frame" && cls is not null && cls is not ("notes" or "page" or "handout"))
                {
                    if (ReadPlaceholder(child, cls, scope, layout, ref contentIndex) is { } placeholder)
                    {
                        shapes.Add(placeholder);
                    }

                    continue;
                }

                if (ReadShape(child, scope) is { } shape)
                {
                    shapes.Add(shape);
                }
            }

            var pageStyle = _index.Resolve("drawing-page", (string?)page.Attribute(DrawNs + "style-name"), Part.Content).Of("drawing-page-properties");
            AddFooters(shapes, page, pageStyle, layout);
            var fill = ReadFill(pageStyle);
            var name = (string?)page.Attribute(DrawNs + "name");
            return new Slide
            {
                Id = (uint)(256 + index),
                LayoutId = layout.Id,
                Name = name is null || Regex.IsMatch(name, "^page\\d+$", RegexOptions.CultureInvariant) ? null : name,
                Shapes = shapes,
                Background = fill is null or NoFill ? null : new Background(Fill: fill),
                Hidden = (string?)pageStyle?.Attribute(PresentationNs + "visibility") == "hidden",
                ShowMasterShapes = (string?)pageStyle?.Attribute(PresentationNs + "background-objects-visible") != "false",
                Transition = ReadTransition(pageStyle),
                Timing = ReadTiming(page, scope),
                Notes = ReadNotes(page.Element(PresentationNs + "notes"), index),
            };
        }

        /// <summary>The footer placeholders a slide shows (presentation:display-* with the footer and date declarations), as ThixShow makes them.</summary>
        private void AddFooters(List<SlideElement> shapes, XElement page, XElement? pageStyle, SlideLayout layout)
        {
            var nextId = shapes.Select(shape => ShapeTree.All([shape]).Max(item => item.Id)).DefaultIfEmpty(1).Max() + 1;
            void Add(PlaceholderType type, TextBody text)
            {
                if (layout.Shapes.FirstOrDefault(shape => shape.Placeholder?.Type == type)?.Placeholder is not { } placeholder)
                {
                    return;
                }

                var id = nextId++;
                shapes.Add(new AutoShape { Id = id, Name = MasterFactory.PlaceholderName(placeholder, id), Placeholder = placeholder with { HasCustomPrompt = false }, Text = text });
            }

            bool Shows(string name) => (string?)pageStyle?.Attribute(PresentationNs + name) == "true";
            if (Shows("display-date-time") && (string?)page.Attribute(PresentationNs + "use-date-time-name") is { } dateName && _dates.TryGetValue(dateName, out var date))
            {
                Add(PlaceholderType.DateAndTime, date.Field
                    ? new TextBody(BodyProperties.Empty, ListStyle.Empty, [new TextParagraph(ParagraphProperties.Empty, [new TextField(OdpNames.DateFieldId, "datetime1", DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")), RunProperties.Empty)])])
                    : TextBody.FromText(date.Text));
            }

            if (Shows("display-footer") && (string?)page.Attribute(PresentationNs + "use-footer-name") is { } footerName && _footers.TryGetValue(footerName, out var footer) && footer.Length > 0)
            {
                Add(PlaceholderType.Footer, TextBody.FromText(footer));
            }

            if (Shows("display-page-number"))
            {
                Add(PlaceholderType.SlideNumber, new TextBody(BodyProperties.Empty, ListStyle.Empty, [new TextParagraph(ParagraphProperties.Empty, [new TextField(OdpNames.SlideNumberFieldId, "slidenum", "‹Nr.›", RunProperties.Empty)])]));
            }
        }

        /// <summary>A placeholder frame of a slide: its type from the class (and the layout), its own look, its text.</summary>
        private SlideElement? ReadPlaceholder(XElement frame, string cls, ReadScope scope, SlideLayout layout, ref int contentIndex)
        {
            var master = scope.Master!;
            var twoContents = layout.Type == "twoObj";
            PlaceholderInfo info;
            EmuRect inherited;
            switch (cls)
            {
                case "title":
                    info = new PlaceholderInfo(layout.Type == "title" ? PlaceholderType.CenteredTitle : PlaceholderType.Title);
                    inherited = master.TitleRect;
                    break;
                case "subtitle":
                    info = new PlaceholderInfo(PlaceholderType.Subtitle, 1);
                    inherited = master.BodyRect;
                    break;
                case "date-time" or "footer" or "page-number" or "header":
                    info = new PlaceholderInfo(cls switch
                    {
                        "date-time" => PlaceholderType.DateAndTime,
                        "footer" => PlaceholderType.Footer,
                        "page-number" => PlaceholderType.SlideNumber,
                        _ => PlaceholderType.Header,
                    }, cls switch
                    {
                        "date-time" => 10u,
                        "footer" => 11u,
                        "page-number" => 12u,
                        _ => 13u,
                    }, Size: cls == "date-time" ? "half" : "quarter");
                    inherited = default;
                    break;
                default:
                    contentIndex++;
                    var type = cls switch
                    {
                        "text" => PlaceholderType.Body,
                        "graphic" => PlaceholderType.Picture,
                        "table" => PlaceholderType.Table,
                        "chart" => PlaceholderType.Chart,
                        "orgchart" => PlaceholderType.Diagram,
                        _ => PlaceholderType.Object,
                    };
                    info = new PlaceholderInfo(type, (uint)contentIndex, Size: twoContents ? "half" : null);
                    var (left, right) = Halves(master.BodyRect);
                    inherited = !twoContents ? master.BodyRect : contentIndex == 1 ? left : right;
                    break;
            }

            var id = scope.Ids.GetValueOrDefault(frame, 2);
            var position = ReadPosition(frame);
            var transform = position.Bounds == inherited && position.Rotation == 0 && !position.FlipHorizontal && !position.FlipVertical ? null : position;
            if (frame.Element(DrawNs + "image") is { } image)
            {
                return ReadPicture(frame, image, scope) is PictureShape picture ? picture with { Placeholder = info, Transform = transform ?? picture.Transform } : null;
            }

            if (frame.Element(TableNs + "table") is not null || frame.Element(DrawNs + "object") is not null || frame.Element(DrawNs + "object-ole") is not null)
            {
                return ReadShape(frame, scope) is { } content ? content with { Placeholder = info } : null;
            }

            var styleName = (string?)frame.Attribute(PresentationNs + "style-name");
            _index.Find("presentation", styleName, scope.Part, out var automatic);
            var props = automatic ? _index.Resolve("presentation", styleName, scope.Part, ownOnly: true) : Props.Empty;
            var graphic = props.Of("graphic-properties");
            var own = ParagraphFrom(props);
            var listStyle = own is null ? ListStyle.Empty : new ListStyle(own, ValueList<ParagraphProperties?>.Empty);
            var box = frame.Element(DrawNs + "text-box");
            Func<int, Bullet?> bullets = cls switch
            {
                "title" or "date-time" or "footer" or "page-number" or "header" => _ => null,
                "subtitle" => level => Visible((master.Master.TextStyles.Body.LevelOf(level) ?? ParagraphProperties.Empty).Merge(master.Subtitle.LevelOf(level)).Bullet),
                _ => level => Visible(master.Master.TextStyles.Body.LevelOf(level)?.Bullet),
            };
            var outlineList = cls is "title" or "date-time" or "footer" or "page-number" or "header" ? null
                : cls == "subtitle" ? StyleOf("presentation", $"{master.PageName}-subtitle").ListStyle
                : StyleOf("presentation", $"{master.PageName}-outline1").ListStyle;
            var placeholderEmpty = (string?)frame.Attribute(PresentationNs + "placeholder") == "true";
            var paragraphs = box is null || placeholderEmpty ? [] : ReadParagraphs(box.Elements(), scope, bullets, outlineList);
            if (paragraphs.Count == 0)
            {
                paragraphs = [TextParagraph.Empty];
            }

            return new AutoShape
            {
                Id = id,
                Name = (string?)frame.Attribute(DrawNs + "name") ?? MasterFactory.PlaceholderName(info, id),
                AltTitle = (string?)frame.Element(SvgNs + "title"),
                Description = (string?)frame.Element(SvgNs + "desc"),
                Placeholder = info,
                Transform = transform,
                Fill = ReadFill(graphic),
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
                Text = new TextBody(ReadBody(graphic), listStyle, paragraphs),
            };
        }

        private static Bullet? Visible(Bullet? bullet) => bullet is CharacterBullet or AutoNumberBullet or PictureBullet ? bullet : null;

        private NotesSlide? ReadNotes(XElement? notes, int index)
        {
            if (notes?.Elements(DrawNs + "frame").FirstOrDefault(frame => (string?)frame.Attribute(PresentationNs + "class") == "notes") is not { } frame)
            {
                return null;
            }

            var scope = new ReadScope(Part.Content, AssignIds(notes, $"n{index + 1}_", out var byName), byName, null);
            var paragraphs = frame.Element(DrawNs + "text-box") is { } box ? ReadParagraphs(box.Elements(), scope, _ => null, null) : [];
            if (paragraphs.All(paragraph => paragraph.Length == 0))
            {
                return null;
            }

            return new NotesSlide
            {
                Shapes =
                [
                    new AutoShape { Id = 2, Name = "Folienbildplatzhalter 1", Placeholder = new PlaceholderInfo(PlaceholderType.SlideImage) },
                    new AutoShape { Id = 3, Name = "Notizenplatzhalter 2", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = new TextBody(BodyProperties.Empty, ListStyle.Empty, paragraphs) },
                ],
            };
        }

        // ------------------------------------------------------------ shapes

        private SlideElement? ReadShape(XElement element, ReadScope scope)
        {
            if (element.Name.Namespace == DrawNs && element.Name.LocalName == "a")
            {
                Lose("action");
                return element.Elements().Select(child => ReadShape(child, scope)).FirstOrDefault(shape => shape is not null);
            }

            if (element.Name.Namespace != DrawNs)
            {
                if (element.Name.LocalName == "scene")
                {
                    Lose("3d");
                }

                return null;
            }

            var shape = element.Name.LocalName switch
            {
                "custom-shape" => ReadCustomShape(element, scope),
                "rect" or "ellipse" or "circle" => ReadBasicShape(element, scope),
                "line" or "connector" => ReadLine(element, scope),
                "polyline" or "polygon" or "path" or "regular-polygon" => ReadPathShape(element, scope),
                "frame" => ReadFrame(element, scope),
                "g" => ReadGroup(element, scope),
                "control" => Lost("form"),
                "page-thumbnail" or "caption" or "measure" => Lost("element"),
                _ => null,
            };
            if (shape is null)
            {
                return null;
            }

            return shape with
            {
                Id = scope.Ids.GetValueOrDefault(element, shape.Id),
                Name = (string?)element.Attribute(DrawNs + "name") ?? (shape.Name.Length > 0 ? shape.Name : $"{shape.KindLabel} {scope.Ids.GetValueOrDefault(element, 1) - 1}"),
                AltTitle = (string?)element.Element(SvgNs + "title"),
                Description = (string?)element.Element(SvgNs + "desc"),
            };
        }

        private SlideElement? Lost(string kind)
        {
            Lose(kind);
            return null;
        }

        /// <summary>The graphic style of a shape that is no placeholder (the chain up to ThixShow's base style, or with the default style).</summary>
        private Props GraphicStyle(XElement element, ReadScope scope) =>
            _index.Resolve("graphic", (string?)element.Attribute(DrawNs + "style-name"), scope.Part);

        /// <summary>The text defaults of a shape's style as its list style (Default), empty when it has none.</summary>
        private ListStyle ListOf(Props props) =>
            ParagraphFrom(props) is { } paragraph ? new ListStyle(paragraph, ValueList<ParagraphProperties?>.Empty) : ListStyle.Empty;

        private TextBody? TextOf(IEnumerable<XElement> children, Props props, ReadScope scope, bool always)
        {
            var paragraphs = ReadParagraphs(children, scope, _ => null, null);
            if (paragraphs.Count == 0 && !always)
            {
                return null;
            }

            return new TextBody(ReadBody(props.Of("graphic-properties")), ListOf(props), paragraphs.Count == 0 ? [TextParagraph.Empty] : paragraphs);
        }

        private SlideElement ReadCustomShape(XElement element, ReadScope scope)
        {
            var props = GraphicStyle(element, scope);
            var graphic = props.Of("graphic-properties");
            var position = ReadPosition(element);
            var geometry = OdpGeometry.Read(element.Element(DrawNs + "enhanced-geometry"), Units.EmuToPoints(position.Width), Units.EmuToPoints(position.Height), out var flipHorizontal, out var flipVertical, out var known);
            if (!known)
            {
                Lose("geometry");
            }

            return new AutoShape
            {
                Transform = position with { FlipHorizontal = flipHorizontal, FlipVertical = flipVertical },
                Geometry = geometry,
                Fill = ReadFill(graphic),
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
                Text = TextOf(element.Elements(), props, scope, always: false),
            };
        }

        private SlideElement ReadBasicShape(XElement element, ReadScope scope)
        {
            var props = GraphicStyle(element, scope);
            var graphic = props.Of("graphic-properties");
            var position = ReadPosition(element);
            var geometry = ShapeGeometry.Of(element.Name.LocalName == "rect" ? "rect" : "ellipse");
            if (element.Name.LocalName == "rect" && OdpNames.Emu((string?)element.Attribute(DrawNs + "corner-radius")) is > 0 and var radius && Math.Min(position.Width, position.Height) > 0)
            {
                geometry = new ShapeGeometry("roundRect", [new GeometryAdjust("adj", Math.Min(50000, radius * 100000 / Math.Min(position.Width, position.Height)))]);
            }

            return new AutoShape
            {
                Transform = position,
                Geometry = geometry,
                Fill = ReadFill(graphic),
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
                Text = TextOf(element.Elements(), props, scope, always: false),
            };
        }

        private SlideElement ReadPathShape(XElement element, ReadScope scope)
        {
            var props = GraphicStyle(element, scope);
            var graphic = props.Of("graphic-properties");
            var position = ReadPosition(element);
            var (width, height) = (Units.EmuToPoints(position.Width), Units.EmuToPoints(position.Height));
            var viewBox = (string?)element.Attribute(SvgNs + "viewBox");
            var geometry = element.Name.LocalName switch
            {
                "path" => OdpGeometry.SvgPath((string?)element.Attribute(SvgNs + "d"), viewBox, width, height),
                "polygon" => OdpGeometry.Polygon((string?)element.Attribute(DrawNs + "points"), viewBox, width, height, closed: true),
                "polyline" => OdpGeometry.Polygon((string?)element.Attribute(DrawNs + "points"), viewBox, width, height, closed: false),
                _ => RegularPolygon(element),
            };
            if (geometry is null)
            {
                Lose("geometry");
            }

            return new AutoShape
            {
                Transform = position,
                Geometry = geometry ?? ShapeGeometry.Rectangle,
                Fill = element.Name.LocalName == "polyline" ? NoFill.Instance : ReadFill(graphic),
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
                Text = TextOf(element.Elements(), props, scope, always: false),
            };
        }

        private static ShapeGeometry? RegularPolygon(XElement element)
        {
            var corners = Math.Clamp((int?)NumberOf((string?)element.Attribute(DrawNs + "corners")) ?? 5, 3, 64);
            var points = new List<string>();
            for (var corner = 0; corner < corners; corner++)
            {
                var angle = (-Math.PI / 2) + (corner * 2 * Math.PI / corners);
                points.Add(string.Create(CultureInfo.InvariantCulture, $"{10000 + (10000 * Math.Cos(angle)):0},{10000 + (10000 * Math.Sin(angle)):0}"));
            }

            return OdpGeometry.Polygon(string.Join(" ", points), "0 0 20000 20000", 1, 1, closed: true);
        }

        private SlideElement ReadLine(XElement element, ReadScope scope)
        {
            var graphic = GraphicStyle(element, scope).Of("graphic-properties");
            double Coordinate(string name) => OdpNames.Emu((string?)element.Attribute(SvgNs + name)) ?? 0;
            var (x1, y1, x2, y2) = (Coordinate("x1"), Coordinate("y1"), Coordinate("x2"), Coordinate("y2"));
            if ((string?)element.Attribute(DrawNs + "transform") is { } text && Matrix(text) is { } matrix)
            {
                (x1, y1) = matrix.Apply(x1, y1);
                (x2, y2) = matrix.Apply(x2, y2);
            }

            var transform = new Transform2D((long)Math.Round(Math.Min(x1, x2)), (long)Math.Round(Math.Min(y1, y2)), (long)Math.Round(Math.Abs(x2 - x1)), (long)Math.Round(Math.Abs(y2 - y1)),
                FlipHorizontal: x2 < x1, FlipVertical: y2 < y1);
            Connection? Connection(string end)
            {
                if ((string?)element.Attribute(DrawNs + end + "-shape") is not { } target || !scope.IdsByName.TryGetValue(target, out var shapeId))
                {
                    return null;
                }

                return new Connection(shapeId, (int?)NumberOf((string?)element.Attribute(DrawNs + end + "-glue-point")) ?? 0);
            }

            var connector = element.Name.LocalName == "connector";
            var preset = !connector ? "line" : (string?)element.Attribute(DrawNs + "type") switch
            {
                "standard" or "lines" => "bentConnector3",
                "curve" => "curvedConnector3",
                _ => "straightConnector1",
            };
            return new ConnectorShape
            {
                Transform = transform,
                Geometry = ShapeGeometry.Of(preset),
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
                Start = connector ? Connection("start") : null,
                End = connector ? Connection("end") : null,
            };
        }

        private SlideElement? ReadFrame(XElement frame, ReadScope scope)
        {
            if (frame.Element(TableNs + "table") is { } table)
            {
                return ReadTable(frame, table, scope);
            }

            if (frame.Element(DrawNs + "object") is { } embedded)
            {
                var chart = (string?)embedded.Attribute(DrawNs + "notify-on-update-of-ranges") is not null
                    || _package.Xml(((string?)embedded.Attribute(XLinkNs + "href") ?? "").TrimStart('.', '/').TrimEnd('/') + "/content.xml")?.Root?.Descendants(OfficeNs + "chart").Any() == true;
                Lose(chart ? "chart" : "ole");
                return frame.Element(DrawNs + "image") is { } replacement && ReadPicture(frame, replacement, scope) is PictureShape { Picture.Image.IsDrawable: true } picture ? picture : null;
            }

            if (frame.Element(DrawNs + "object-ole") is not null || frame.Element(DrawNs + "applet") is not null || frame.Element(DrawNs + "floating-frame") is not null)
            {
                Lose("ole");
                return null;
            }

            if (frame.Element(DrawNs + "plugin") is not null)
            {
                Lose("media");
                return frame.Element(DrawNs + "image") is { } poster ? ReadPicture(frame, poster, scope) : null;
            }

            if (frame.Element(DrawNs + "image") is { } image)
            {
                return ReadPicture(frame, image, scope);
            }

            if (frame.Element(DrawNs + "text-box") is { } box)
            {
                var props = GraphicStyle(frame, scope);
                var graphic = props.Of("graphic-properties");
                return new AutoShape
                {
                    Transform = ReadPosition(frame),
                    Geometry = ShapeGeometry.Rectangle,
                    IsTextBox = true,
                    Fill = ReadFill(graphic),
                    Outline = ReadOutline(graphic),
                    Effects = ReadShadow(graphic),
                    Text = TextOf(box.Elements(), props, scope, always: true),
                };
            }

            return null;
        }

        private SlideElement? ReadPicture(XElement frame, XElement image, ReadScope scope)
        {
            var href = (string?)image.Attribute(XLinkNs + "href");
            var data = href is not null && href.Contains("://", StringComparison.Ordinal) ? null : Picture(href);
            if (data is null && image.Element(OfficeNs + "binary-data") is { } binary)
            {
                try
                {
                    data = ImageData.From(Convert.FromBase64String(binary.Value));
                }
                catch (FormatException)
                {
                    data = null;
                }
            }

            var link = data is null && href is not null && href.Contains("://", StringComparison.Ordinal) ? href : null;
            if (data is null && link is null)
            {
                return null;
            }

            var graphic = GraphicStyle(frame, scope).Of("graphic-properties");
            var position = ReadPosition(frame);
            var mirror = (string?)graphic?.Attribute(StyleNs + "mirror") ?? "";
            var crop = RelativeRect.Zero;
            if (data is { PixelWidth: > 0, PixelHeight: > 0 } && (string?)graphic?.Attribute(FoNs + "clip") is { } clip)
            {
                var values = Regex.Match(clip, "rect\\s*\\(([^)]*)\\)").Groups[1].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(value => Points(value) ?? 0).ToList();
                if (values.Count == 4)
                {
                    var (width, height) = (data.PixelWidth * 72.0 / 96, data.PixelHeight * 72.0 / 96);
                    int Fraction(double length, double size) => (int)Math.Round(length / size * 100000);
                    crop = new RelativeRect(Fraction(values[3], width), Fraction(values[0], height), Fraction(values[1], width), Fraction(values[2], height));
                }
            }

            return new PictureShape
            {
                Transform = position with
                {
                    FlipHorizontal = mirror.Contains("horizontal", StringComparison.Ordinal),
                    FlipVertical = mirror.Contains("vertical", StringComparison.Ordinal),
                },
                Picture = new PictureFill(data, crop, StretchMode.Full, LinkTarget: link)
                {
                    Opacity = PercentOf((string?)graphic?.Attribute(DrawNs + "image-opacity")) ?? 1,
                },
                Outline = ReadOutline(graphic),
                Effects = ReadShadow(graphic),
            };
        }

        private SlideElement? ReadGroup(XElement group, ReadScope scope)
        {
            var children = group.Elements().Select(child => ReadShape(child, scope)).OfType<SlideElement>().ToList();
            if (children.Count == 0)
            {
                return null;
            }

            var boxes = children.Select(child => child.Transform?.Bounds ?? default).ToList();
            var bounds = boxes.Aggregate(EmuRect.Union);
            return new GroupShape
            {
                Transform = new Transform2D(bounds.X, bounds.Y, bounds.Width, bounds.Height, ChildX: bounds.X, ChildY: bounds.Y, ChildWidth: bounds.Width, ChildHeight: bounds.Height),
                Children = children,
            };
        }

        // ------------------------------------------------------------ tables

        private SlideElement ReadTable(XElement frame, XElement table, ReadScope scope)
        {
            _hasTables = true;
            var position = ReadPosition(frame);
            var widths = new List<long>();
            foreach (var column in table.Elements(TableNs + "table-column"))
            {
                var repeat = Math.Clamp((int?)NumberOf((string?)column.Attribute(TableNs + "number-columns-repeated")) ?? 1, 1, 64);
                var width = OdpNames.Emu(_index.Resolve("table-column", (string?)column.Attribute(TableNs + "style-name"), scope.Part).Attribute("table-column-properties", StyleNs + "column-width"));
                widths.AddRange(Enumerable.Repeat(width ?? 0, repeat));
            }

            var rows = table.Elements(TableNs + "table-row").Concat(table.Elements(TableNs + "table-rows").Elements(TableNs + "table-row")).ToList();
            var columnCount = Math.Max(widths.Count, rows.Select(row => row.Elements().Count(cell => cell.Name.LocalName is "table-cell" or "covered-table-cell")).DefaultIfEmpty(0).Max());
            while (widths.Count < columnCount)
            {
                widths.Add(0);
            }

            if (widths.Any(width => width <= 0))
            {
                var fallback = columnCount == 0 ? 0 : position.Width / columnCount;
                widths = widths.Select(width => width > 0 ? width : fallback).ToList();
            }

            var cells = rows.Select(_ => Enumerable.Repeat(TableCell.Empty, columnCount).ToArray()).ToList();
            var heights = new List<long>();
            for (var row = 0; row < rows.Count; row++)
            {
                var height = OdpNames.Emu(_index.Resolve("table-row", (string?)rows[row].Attribute(TableNs + "style-name"), scope.Part).Attribute("table-row-properties", StyleNs + "row-height"));
                heights.Add(height ?? (rows.Count == 0 ? 0 : position.Height / rows.Count));
                var column = 0;
                foreach (var cell in rows[row].Elements().Where(cell => cell.Name.LocalName is "table-cell" or "covered-table-cell"))
                {
                    if (column >= columnCount)
                    {
                        break;
                    }

                    if (cell.Name.LocalName == "table-cell")
                    {
                        var columnSpan = Math.Clamp((int?)NumberOf((string?)cell.Attribute(TableNs + "number-columns-spanned")) ?? 1, 1, columnCount - column);
                        var rowSpan = Math.Clamp((int?)NumberOf((string?)cell.Attribute(TableNs + "number-rows-spanned")) ?? 1, 1, rows.Count - row);
                        cells[row][column] = ReadCell(cell, scope) with { GridSpan = columnSpan, RowSpan = rowSpan };
                        for (var r = row; r < row + rowSpan; r++)
                        {
                            for (var c = column; c < column + columnSpan; c++)
                            {
                                if (r != row || c != column)
                                {
                                    cells[r][c] = cells[r][c] with { HorizontalMerge = c > column, VerticalMerge = r > row };
                                }
                            }
                        }
                    }

                    column++;
                }
            }

            bool Flag(string name) => (string?)table.Attribute(TableNs + name) == "true";
            var model = new Table(
                new TableProperties(NoStyleTableId, Flag("use-first-row-styles"), Flag("use-last-row-styles"), Flag("use-first-column-styles"), Flag("use-last-column-styles"),
                    Flag("use-banding-rows-styles"), Flag("use-banding-columns-styles")),
                widths,
                rows.Select((_, index) => new TableRow(heights[index], cells[index])).ToList());
            return new GraphicFrame { Kind = GraphicKind.Table, Table = model, Transform = position };
        }

        private TableCell ReadCell(XElement cell, ReadScope scope)
        {
            var props = _index.Resolve("table-cell", (string?)cell.Attribute(TableNs + "style-name"), scope.Part);
            var graphic = props.Of("graphic-properties");
            var cellProperties = props.Of("table-cell-properties");
            var body = ReadBody(graphic);
            var fill = ReadFill(graphic) ?? (Color((string?)cellProperties?.Attribute(FoNs + "background-color")) is { } background ? new SolidFill(background) : null);
            Outline? Border(string side)
            {
                var text = (string?)cellProperties?.Attribute(FoNs + "border-" + side) ?? (string?)cellProperties?.Attribute(FoNs + "border")
                    ?? (string?)props.Of("paragraph-properties")?.Attribute(FoNs + "border-" + side);
                if (text is null)
                {
                    return null;
                }

                if (text.Trim() == "none")
                {
                    return Outline.None;
                }

                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var width = parts.Select(OdpNames.Emu).FirstOrDefault(value => value is not null);
                var color = parts.Select(part => Color(part)).FirstOrDefault(value => value is not null);
                return new Outline(width, color is null ? null : new SolidFill(color));
            }

            var paragraphs = ReadParagraphs(cell.Elements(), scope, _ => null, null);
            var run = ReadRunProperties(props.Of("text-properties"));
            var list = run is null ? ListStyle.Empty : new ListStyle(new ParagraphProperties(DefaultRun: run), ValueList<ParagraphProperties?>.Empty);
            return new TableCell(
                new TextBody(BodyProperties.Empty, list, paragraphs.Count == 0 ? [TextParagraph.Empty] : paragraphs),
                new TableCellProperties(body.LeftInset, body.RightInset, body.TopInset, body.BottomInset, body.Anchor,
                    LeftBorder: Border("left"), RightBorder: Border("right"), TopBorder: Border("top"), BottomBorder: Border("bottom"), Fill: fill));
        }

        // ------------------------------------------------------------ positions

        /// <summary>The box of a shape: svg:x/y/width/height, or the rotation and translation of draw:transform.</summary>
        private static Transform2D ReadPosition(XElement element)
        {
            var width = OdpNames.Emu((string?)element.Attribute(SvgNs + "width")) ?? 0;
            var height = OdpNames.Emu((string?)element.Attribute(SvgNs + "height")) ?? 0;
            if ((string?)element.Attribute(DrawNs + "transform") is { } text && Matrix(text) is { } matrix)
            {
                var angle = Math.Atan2(-matrix.M12, matrix.M11);
                var (centerX, centerY) = matrix.Apply(width / 2.0, height / 2.0);
                var rotation = Units.NormalizeAngle((int)Math.Round(-angle * 180 / Math.PI * 60000));
                return new Transform2D((long)Math.Round(centerX - (width / 2.0)), (long)Math.Round(centerY - (height / 2.0)), width, height, rotation);
            }

            return new Transform2D(OdpNames.Emu((string?)element.Attribute(SvgNs + "x")) ?? 0, OdpNames.Emu((string?)element.Attribute(SvgNs + "y")) ?? 0, width, height);
        }

        /// <summary>
        /// A draw:transform as an affine map of EMU (x' = M11 x + M21 y + dx, y' = M12 x + M22 y + dy), the operations applied in the
        /// order they are listed; rotate(a) turns counter-clockwise.
        /// </summary>
        private static ThixOffice.Text.Drawing.Matrix2D? Matrix(string text)
        {
            var matrix = ThixOffice.Text.Drawing.Matrix2D.Identity;
            var any = false;
            foreach (Match operation in Regex.Matches(text, "([a-zA-Z]+)\\s*\\(([^)]*)\\)"))
            {
                var arguments = operation.Groups[2].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                double Number(int index) => index < arguments.Length ? NumberOf(arguments[index]) ?? 0 : 0;
                double Length(int index) => index < arguments.Length ? OdpNames.Emu(arguments[index]) ?? 0 : 0;
                ThixOffice.Text.Drawing.Matrix2D step;
                switch (operation.Groups[1].Value)
                {
                    case "rotate":
                        var angle = Number(0);
                        step = new ThixOffice.Text.Drawing.Matrix2D(Math.Cos(angle), -Math.Sin(angle), Math.Sin(angle), Math.Cos(angle), 0, 0);
                        break;
                    case "translate":
                        step = ThixOffice.Text.Drawing.Matrix2D.Translation(Length(0), Length(1));
                        break;
                    case "scale":
                        step = ThixOffice.Text.Drawing.Matrix2D.Scaling(Number(0), arguments.Length > 1 ? Number(1) : Number(0));
                        break;
                    case "skewX":
                        step = new ThixOffice.Text.Drawing.Matrix2D(1, 0, Math.Tan(Number(0)), 1, 0, 0);
                        break;
                    case "skewY":
                        step = new ThixOffice.Text.Drawing.Matrix2D(1, Math.Tan(Number(0)), 0, 1, 0, 0);
                        break;
                    case "matrix":
                        step = new ThixOffice.Text.Drawing.Matrix2D(Number(0), Number(1), Number(2), Number(3), Length(4), Length(5));
                        break;
                    default:
                        continue;
                }

                matrix *= step;
                any = true;
            }

            return any ? matrix : null;
        }
    }
}

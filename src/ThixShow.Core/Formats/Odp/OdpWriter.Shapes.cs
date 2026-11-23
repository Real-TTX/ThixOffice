using System.Globalization;
using System.Xml.Linq;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpWriter
{
    /// <summary>Where shapes are being written: the slide's context, the style sink of its part, the id prefix, the master page.</summary>
    private sealed record ShapeScope(SlideContext Context, SlideContextColors Colors, StyleSink Sink, string IdPrefix, MasterPage Page, bool IsMasterPage);

    private static readonly Func<Transform2D, Transform2D> Identity = transform => transform;

    private sealed partial class Writer
    {
        // ------------------------------------------------------------ shapes

        private XElement? WriteShape(ShapeScope scope, SlideElement shape, Func<Transform2D, Transform2D> map) => shape switch
        {
            AutoShape auto => WriteAutoShape(scope, auto, map),
            ConnectorShape connector => WriteConnector(scope, connector, map),
            PictureShape picture => WritePicture(scope, picture, picture.Picture, picture.Outline, map),
            GroupShape group => WriteGroup(scope, group, map),
            GraphicFrame { Table: { } table } frame => WriteTable(scope, frame, table, map),
            GraphicFrame { Fallback: { } fallback, Kind: not GraphicKind.Chart } frame => WritePicture(scope, frame, new PictureFill(fallback, RelativeRect.Zero, StretchMode.Full), null, map),
            _ => null,
        };

        private void AddIdentity(XElement element, SlideElement shape, ShapeScope scope)
        {
            if (shape.Name.Length > 0)
            {
                element.Add(new XAttribute(DrawNs + "name", shape.Name));
            }

            var id = scope.IdPrefix + shape.Id.ToString(CultureInfo.InvariantCulture);
            element.Add(new XAttribute(DrawNs + "id", id), new XAttribute(XNamespace.Xml + "id", id));
        }

        private static IEnumerable<XElement> TitleAndDescription(SlideElement shape)
        {
            if (!string.IsNullOrEmpty(shape.AltTitle))
            {
                yield return new XElement(SvgNs + "title", shape.AltTitle);
            }

            if (!string.IsNullOrEmpty(shape.Description))
            {
                yield return new XElement(SvgNs + "desc", shape.Description);
            }
        }

        /// <summary>svg:width/height and svg:x/y, or draw:transform for a rotated shape ("rotate(a) translate(x y)", a counter-clockwise).</summary>
        private static void AddPosition(XElement element, Transform2D transform)
        {
            element.Add(new XAttribute(SvgNs + "width", OdpNames.Cm(transform.Width)), new XAttribute(SvgNs + "height", OdpNames.Cm(transform.Height)));
            if (Units.NormalizeAngle(transform.Rotation) == 0)
            {
                element.Add(new XAttribute(SvgNs + "x", OdpNames.Cm(transform.X)), new XAttribute(SvgNs + "y", OdpNames.Cm(transform.Y)));
                return;
            }

            var theta = Units.NormalizeAngle(transform.Rotation) / 60000.0 * Math.PI / 180;
            var (halfWidth, halfHeight) = (transform.Width / 2.0, transform.Height / 2.0);
            var (centerX, centerY) = (transform.X + halfWidth, transform.Y + halfHeight);
            var translateX = centerX - ((halfWidth * Math.Cos(theta)) - (halfHeight * Math.Sin(theta)));
            var translateY = centerY - ((halfWidth * Math.Sin(theta)) + (halfHeight * Math.Cos(theta)));
            var angle = (2 * Math.PI) - theta;
            element.Add(new XAttribute(DrawNs + "transform",
                $"rotate ({angle.ToString("R", CultureInfo.InvariantCulture)}) translate ({OdpNames.Cm(translateX)} {OdpNames.Cm(translateY)})"));
        }

        /// <summary>The graphic properties of a shape's own look (null values: inherited).</summary>
        private XElement Graphic(Fill? fill, Outline? outline, ShapeEffects? effects, BodyProperties? body, ColorContext colors)
        {
            var graphic = new XElement(StyleNs + "graphic-properties");
            AddFill(graphic, fill, colors);
            AddStroke(graphic, outline, colors);
            AddShadow(graphic, effects, colors);
            AddBody(graphic, body is null || body == BodyProperties.Empty ? null : body);
            return graphic;
        }

        /// <summary>The paragraph and text defaults of a shape's own list style (default + first level), with the theme font of its style.</summary>
        private (XElement? Paragraph, XElement? Text) ShapeTextDefaults(TextBody? text, RunProperties? styleFont, SlideContextColors colors)
        {
            var list = text?.ListStyle;
            ParagraphProperties? paragraph = null;
            if (list is not null && (list.Default is not null || list.LevelOf(0) is not null))
            {
                paragraph = (list.Default ?? Model.ParagraphProperties.Empty).Merge(list.LevelOf(0));
            }

            var run = paragraph?.DefaultRun;
            if (styleFont is not null)
            {
                run = styleFont.Merge(run);
            }

            return (paragraph is null ? null : ParagraphProperties(Reduced(paragraph)), TextProperties(run is null || run == RunProperties.Empty ? null : run, colors));
        }

        private XElement WriteAutoShape(ShapeScope scope, AutoShape auto, Func<Transform2D, Transform2D> map)
        {
            var resolved = ShapeResolver.Resolve(scope.Context, auto);
            var transform = map(resolved.Transform);
            var styled = auto.Style is not null;
            var fill = auto.Fill ?? (styled ? resolved.Fill : null);
            var outline = styled ? resolved.Outline : auto.Outline;
            var effects = auto.Effects ?? (styled && resolved.Shadow is { } shadow ? new ShapeEffects(shadow) : null);
            var (paragraph, text) = ShapeTextDefaults(auto.Text, styled ? resolved.StyleFont : null, scope.Colors);
            var graphic = Graphic(fill, outline, effects, auto.Text?.Body, scope.Context.Colors);

            if (auto.Placeholder is { } placeholder && !scope.IsMasterPage && OdpNames.ClassOf(placeholder) is { } cls)
            {
                var parent = cls switch
                {
                    "title" => $"{scope.Page.Name}-title",
                    "subtitle" => $"{scope.Page.Name}-subtitle",
                    "notes" => $"{scope.Page.Name}-notes",
                    _ => $"{scope.Page.Name}-outline1",
                };
                var frame = new XElement(DrawNs + "frame", new XAttribute(PresentationNs + "style-name", scope.Sink.Add("presentation", "pr", [graphic, paragraph, text], parent)!));
                AddIdentity(frame, auto, scope);
                AddPosition(frame, transform);
                frame.Add(new XAttribute(PresentationNs + "class", cls));
                var empty = auto.Text is null || auto.Text.IsEmpty;
                if (empty)
                {
                    frame.Add(new XAttribute(PresentationNs + "placeholder", "true"));
                }

                if (auto.Placeholder.Type is not (PlaceholderType.Picture or PlaceholderType.ClipArt or PlaceholderType.Table or PlaceholderType.Chart or PlaceholderType.Diagram or PlaceholderType.Media) || !empty)
                {
                    frame.Add(new XElement(DrawNs + "text-box", empty ? null : WriteText(scope, auto.Text!, resolved, cls == "outline")));
                }

                frame.Add(TitleAndDescription(auto));
                return frame;
            }

            var style = scope.Sink.Add("graphic", "gr", [graphic, paragraph, text], OdpNames.BaseStyleName)!;
            if (auto.IsTextBox && auto.Geometry?.Preset is null or "rect" && auto.Geometry?.Custom is null)
            {
                var frame = new XElement(DrawNs + "frame", new XAttribute(DrawNs + "style-name", style));
                AddIdentity(frame, auto, scope);
                AddPosition(frame, transform);
                frame.Add(new XElement(DrawNs + "text-box", auto.Text is null ? null : WriteText(scope, auto.Text, resolved, outline: false)));
                frame.Add(TitleAndDescription(auto));
                return frame;
            }

            var shape = new XElement(DrawNs + "custom-shape", new XAttribute(DrawNs + "style-name", style));
            AddIdentity(shape, auto, scope);
            AddPosition(shape, transform);
            shape.Add(TitleAndDescription(auto));
            if (auto.Text is { } body)
            {
                shape.Add(WriteText(scope, body, resolved, outline: false));
            }

            shape.Add(OdpGeometry.Write(resolved.Geometry, transform.Width, transform.Height, transform.FlipHorizontal, transform.FlipVertical));
            return shape;
        }

        private XElement WriteConnector(ShapeScope scope, ConnectorShape connector, Func<Transform2D, Transform2D> map)
        {
            var resolved = ShapeResolver.Resolve(scope.Context, connector);
            var transform = map(resolved.Transform);
            var outline = connector.Style is not null ? resolved.Outline : connector.Outline;
            var effects = connector.Effects ?? (connector.Style is not null && resolved.Shadow is { } shadow ? new ShapeEffects(shadow) : null);
            var style = scope.Sink.Add("graphic", "gr", [Graphic(null, outline, effects, null, scope.Context.Colors)], OdpNames.BaseStyleName)!;

            double x1 = transform.FlipHorizontal ? transform.X + transform.Width : transform.X;
            double x2 = transform.FlipHorizontal ? transform.X : transform.X + transform.Width;
            double y1 = transform.FlipVertical ? transform.Y + transform.Height : transform.Y;
            double y2 = transform.FlipVertical ? transform.Y : transform.Y + transform.Height;
            if (Units.NormalizeAngle(transform.Rotation) != 0)
            {
                var theta = transform.Rotation / 60000.0 * Math.PI / 180;
                var (centerX, centerY) = (transform.X + (transform.Width / 2.0), transform.Y + (transform.Height / 2.0));
                (x1, y1) = Rotate(x1, y1, centerX, centerY, theta);
                (x2, y2) = Rotate(x2, y2, centerX, centerY, theta);
            }

            var connected = connector.Start is not null || connector.End is not null;
            var element = new XElement(DrawNs + (connected ? "connector" : "line"), new XAttribute(DrawNs + "style-name", style));
            AddIdentity(element, connector, scope);
            if (connected)
            {
                var preset = connector.Geometry.Preset ?? "";
                element.Add(new XAttribute(DrawNs + "type", preset.StartsWith("bent", StringComparison.Ordinal) ? "standard" : preset.StartsWith("curved", StringComparison.Ordinal) ? "curve" : "line"));
            }

            element.Add(new XAttribute(SvgNs + "x1", OdpNames.Cm(x1)), new XAttribute(SvgNs + "y1", OdpNames.Cm(y1)),
                new XAttribute(SvgNs + "x2", OdpNames.Cm(x2)), new XAttribute(SvgNs + "y2", OdpNames.Cm(y2)));
            if (connector.Start is { } start)
            {
                element.Add(new XAttribute(DrawNs + "start-shape", scope.IdPrefix + start.ShapeId.ToString(CultureInfo.InvariantCulture)), new XAttribute(DrawNs + "start-glue-point", start.Site));
            }

            if (connector.End is { } end)
            {
                element.Add(new XAttribute(DrawNs + "end-shape", scope.IdPrefix + end.ShapeId.ToString(CultureInfo.InvariantCulture)), new XAttribute(DrawNs + "end-glue-point", end.Site));
            }

            element.Add(TitleAndDescription(connector));
            return element;
        }

        private static (double X, double Y) Rotate(double x, double y, double centerX, double centerY, double theta)
        {
            var (dx, dy) = (x - centerX, y - centerY);
            return (centerX + (dx * Math.Cos(theta)) - (dy * Math.Sin(theta)), centerY + (dx * Math.Sin(theta)) + (dy * Math.Cos(theta)));
        }

        private XElement WritePicture(ShapeScope scope, SlideElement shape, PictureFill picture, Outline? outline, Func<Transform2D, Transform2D> map)
        {
            var transform = map(shape.Placeholder is not null ? ShapeResolver.Resolve(scope.Context, shape).Transform : shape.Transform ?? new Transform2D(0, 0, 0, 0));
            var graphic = Graphic(null, outline, (shape as PictureShape)?.Effects, null, scope.Context.Colors);
            graphic.Add(new XAttribute(DrawNs + "fill", "none"));
            if (picture.Image is { PixelWidth: > 0, PixelHeight: > 0 } image && !picture.Crop.IsZero)
            {
                var dpi = picture.Dpi is > 0 ? picture.Dpi.Value : 96;
                var (width, height) = (image.PixelWidth * 72.0 / dpi, image.PixelHeight * 72.0 / dpi);
                string Clip(int fraction, double size) => Length(fraction / 100000.0 * size);
                graphic.Add(new XAttribute(FoNs + "clip", $"rect({Clip(picture.Crop.Top, height)}, {Clip(picture.Crop.Right, width)}, {Clip(picture.Crop.Bottom, height)}, {Clip(picture.Crop.Left, width)})"));
            }

            if (transform.FlipHorizontal || transform.FlipVertical)
            {
                graphic.Add(new XAttribute(StyleNs + "mirror", (transform.FlipHorizontal, transform.FlipVertical) switch
                {
                    (true, true) => "horizontal vertical",
                    (true, false) => "horizontal",
                    _ => "vertical",
                }));
            }

            if (picture.Opacity < 1)
            {
                graphic.Add(new XAttribute(DrawNs + "image-opacity", Percent(picture.Opacity)));
            }

            var frame = new XElement(DrawNs + "frame", new XAttribute(DrawNs + "style-name", scope.Sink.Add("graphic", "gr", [graphic], OdpNames.BaseStyleName)!));
            AddIdentity(frame, shape, scope);
            AddPosition(frame, transform with { FlipHorizontal = false, FlipVertical = false });
            if (shape.Placeholder is not null)
            {
                frame.Add(new XAttribute(PresentationNs + "class", "graphic"));
            }

            var href = picture.Image is { } data ? Picture(data) : picture.LinkTarget ?? "";
            frame.Add(new XElement(DrawNs + "image", new XAttribute(XLinkNs + "href", href), new XAttribute(XLinkNs + "type", "simple"),
                new XAttribute(XLinkNs + "show", "embed"), new XAttribute(XLinkNs + "actuate", "onLoad"), new XElement(TextNs + "p")));
            frame.Add(TitleAndDescription(shape));
            return frame;
        }

        private XElement? WriteGroup(ShapeScope scope, GroupShape group, Func<Transform2D, Transform2D> map)
        {
            var own = group.Transform ?? new Transform2D(0, 0, 0, 0);
            Transform2D ChildMap(Transform2D child) => map(ToParent(child, own));
            var element = new XElement(DrawNs + "g");
            AddIdentity(element, group, scope);
            element.Add(TitleAndDescription(group));
            foreach (var child in group.Children)
            {
                if (WriteShape(scope, child, ChildMap) is { } written)
                {
                    element.Add(written);
                }
            }

            return element.Elements().Any(child => child.Name.Namespace == DrawNs) ? element : null;
        }

        /// <summary>A child's transform in its group's parent space: the child space scaled onto the group's box, then its flips and rotation.</summary>
        private static Transform2D ToParent(Transform2D child, Transform2D group)
        {
            var scaleX = group.ChildWidth != 0 ? group.Width / (double)group.ChildWidth : 1;
            var scaleY = group.ChildHeight != 0 ? group.Height / (double)group.ChildHeight : 1;
            var width = child.Width * scaleX;
            var height = child.Height * scaleY;
            var centerX = group.X + ((child.X - group.ChildX) * scaleX) + (width / 2);
            var centerY = group.Y + ((child.Y - group.ChildY) * scaleY) + (height / 2);
            var (groupX, groupY) = (group.X + (group.Width / 2.0), group.Y + (group.Height / 2.0));
            var rotation = child.Rotation;
            var (flipHorizontal, flipVertical) = (child.FlipHorizontal, child.FlipVertical);
            if (group.FlipHorizontal)
            {
                centerX = (2 * groupX) - centerX;
                flipHorizontal = !flipHorizontal;
                rotation = -rotation;
            }

            if (group.FlipVertical)
            {
                centerY = (2 * groupY) - centerY;
                flipVertical = !flipVertical;
                rotation = -rotation;
            }

            if (group.Rotation != 0)
            {
                (centerX, centerY) = Rotate(centerX, centerY, groupX, groupY, group.Rotation / 60000.0 * Math.PI / 180);
                rotation += group.Rotation;
            }

            return new Transform2D((long)Math.Round(centerX - (width / 2)), (long)Math.Round(centerY - (height / 2)), (long)Math.Round(width), (long)Math.Round(height),
                Units.NormalizeAngle(rotation), flipHorizontal, flipVertical);
        }

        // ------------------------------------------------------------ tables

        private XElement WriteTable(ShapeScope scope, GraphicFrame frame, Table table, Func<Transform2D, Transform2D> map)
        {
            var transform = map(frame.Transform ?? new Transform2D(0, 0, table.ColumnWidths.Sum(), table.Rows.Sum(row => row.Height)));
            var element = new XElement(DrawNs + "frame", new XAttribute(DrawNs + "style-name", scope.Sink.Add("graphic", "gr", [], OdpNames.BaseStyleName)!));
            AddIdentity(element, frame, scope);
            AddPosition(element, transform with { Rotation = 0 });
            var properties = table.Properties;
            var tableElement = new XElement(TableNs + "table");
            void Flag(string name, bool value)
            {
                if (value)
                {
                    tableElement.Add(new XAttribute(TableNs + name, "true"));
                }
            }

            Flag("use-first-row-styles", properties.FirstRow);
            Flag("use-last-row-styles", properties.LastRow);
            Flag("use-first-column-styles", properties.FirstColumn);
            Flag("use-last-column-styles", properties.LastColumn);
            Flag("use-banding-rows-styles", properties.BandRows);
            Flag("use-banding-columns-styles", properties.BandColumns);
            foreach (var width in table.ColumnWidths)
            {
                tableElement.Add(new XElement(TableNs + "table-column", new XAttribute(TableNs + "style-name",
                    scope.Sink.Add("table-column", "co", [new XElement(StyleNs + "table-column-properties", new XAttribute(StyleNs + "column-width", OdpNames.Cm(width)))])!)));
            }

            var style = TableRenderer.StyleOf(presentation, table);
            for (var row = 0; row < table.RowCount; row++)
            {
                var rowElement = new XElement(TableNs + "table-row", new XAttribute(TableNs + "style-name",
                    scope.Sink.Add("table-row", "ro", [new XElement(StyleNs + "table-row-properties", new XAttribute(StyleNs + "row-height", OdpNames.Cm(table.Rows[row].Height)))])!));
                for (var column = 0; column < table.ColumnCount; column++)
                {
                    var cell = column < table.Rows[row].Cells.Count ? table.Rows[row].Cells[column] : TableCell.Empty;
                    if (cell.IsCovered)
                    {
                        rowElement.Add(new XElement(TableNs + "covered-table-cell"));
                        continue;
                    }

                    rowElement.Add(WriteCell(scope, table, style, cell, row, column));
                }

                tableElement.Add(rowElement);
            }

            element.Add(tableElement, TitleAndDescription(frame));
            return element;
        }

        private XElement WriteCell(ShapeScope scope, Table table, TableStyle style, TableCell cell, int row, int column)
        {
            var look = TableRenderer.LookOf(table, style, row, column);
            var properties = cell.Properties;
            var lastRow = row + Math.Max(1, cell.RowSpan) >= table.RowCount;
            var lastColumn = column + Math.Max(1, cell.GridSpan) >= table.ColumnCount;
            var graphic = new XElement(StyleNs + "graphic-properties");
            AddFill(graphic, properties.Fill ?? look.Fill ?? table.Properties.Fill, scope.Context.Colors);
            AddBody(graphic, new BodyProperties(properties.LeftMargin, properties.TopMargin, properties.RightMargin, properties.BottomMargin, properties.Anchor));
            var borders = new XElement(StyleNs + "table-cell-properties");
            void Border(string side, Outline? outline)
            {
                if (outline is null)
                {
                    return;
                }

                borders.Add(new XAttribute(FoNs + "border-" + side, ColorOfFill(outline.Fill) is { } color && outline.Fill is not NoFill
                    ? $"{OdpNames.Cm(outline.Width ?? 12700)} solid {HexOf(color, scope.Context.Colors)}"
                    : "none"));
            }

            Border("top", properties.TopBorder ?? (row == 0 ? look.TopBorder : look.InsideHorizontalBorder ?? look.TopBorder));
            Border("bottom", properties.BottomBorder ?? (lastRow ? look.BottomBorder : look.InsideHorizontalBorder ?? look.BottomBorder));
            Border("left", properties.LeftBorder ?? (column == 0 ? look.LeftBorder : look.InsideVerticalBorder ?? look.LeftBorder));
            Border("right", properties.RightBorder ?? (lastColumn ? look.RightBorder : look.InsideVerticalBorder ?? look.RightBorder));
            var text = look.Bold is not null || look.Italic is not null || look.TextColor is not null
                ? TextProperties(new RunProperties(Bold: look.Bold, Italic: look.Italic, Fill: look.TextColor is null ? null : new SolidFill(look.TextColor)), scope.Colors)
                : null;
            var element = new XElement(TableNs + "table-cell");
            if (scope.Sink.Add("table-cell", "ce", [graphic, borders, text]) is { } cellStyle)
            {
                element.Add(new XAttribute(TableNs + "style-name", cellStyle));
            }
            if (cell.GridSpan > 1)
            {
                element.Add(new XAttribute(TableNs + "number-columns-spanned", cell.GridSpan));
            }

            if (cell.RowSpan > 1)
            {
                element.Add(new XAttribute(TableNs + "number-rows-spanned", cell.RowSpan));
            }

            var resolved = ShapeResolver.Resolve(scope.Context, new AutoShape { Id = 1, Text = cell.Text });
            element.Add(WriteText(scope, cell.Text, resolved, outline: false));
            return element;
        }
    }
}

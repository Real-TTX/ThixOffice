using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>How shapes line up: their edges or centres, against each other or the slide.</summary>
public enum AlignKind
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom,
}

/// <summary>Moves in the z-order.</summary>
public enum ZOrderMove
{
    BringToFront,
    BringForward,
    SendBackward,
    SendToBack,
}

public sealed partial class EditingSession
{
    /// <summary>The offset of a duplicate (Strg+D) and of pasted shapes on their own slide (0.42 cm).</summary>
    public const long DuplicateOffset = 152400;

    /// <summary>PowerPoint's default style for new shapes: accent 1 fill, a darker accent outline, light text.</summary>
    public static ShapeStyle DefaultShapeStyle { get; } = new(
        new StyleReference(2, ColorSpec.Scheme("accent1").Shade(15000)),
        new StyleReference(1, ColorSpec.Scheme("accent1")),
        new StyleReference(0, ColorSpec.Scheme("accent1")),
        new FontReference("minor", ColorSpec.Scheme("lt1")));

    /// <summary>The default style of new lines: accent 1 line of style 1.</summary>
    public static ShapeStyle DefaultLineStyle { get; } = new(
        new StyleReference(1, ColorSpec.Scheme("accent1")),
        new StyleReference(0, ColorSpec.Scheme("accent1")),
        new StyleReference(0, ColorSpec.Scheme("accent1")),
        new FontReference("minor", ColorSpec.Scheme("tx1")));

    private uint RequireSlide() => Selection.CurrentSlide ?? throw new InvalidOperationException("Es ist keine Folie ausgewählt.");

    /// <summary>The German shape name of a new shape ("Rechteck 4").</summary>
    private static string NameFor(string preset, int id) => preset switch
    {
        "rect" => $"Rechteck {id - 1}",
        "roundRect" => $"Rechteck: abgerundete Ecken {id - 1}",
        "ellipse" => $"Ellipse {id - 1}",
        "triangle" => $"Gleichschenkliges Dreieck {id - 1}",
        "rightArrow" => $"Pfeil: nach rechts {id - 1}",
        "line" or "straightConnector1" => $"Gerade Verbindung {id - 1}",
        _ => $"Form {id - 1}",
    };

    /// <summary>Inserts an auto shape of <paramref name="preset"/> (theme style, centred text) and selects it.</summary>
    public int InsertShape(string preset, EmuRect bounds)
    {
        var slideId = RequireSlide();
        var id = NextShapeId(CurrentSlide!);
        var shape = new AutoShape
        {
            Id = id,
            Name = NameFor(preset, id),
            Transform = Transform2D.Of(bounds),
            Geometry = ShapeGeometry.Of(preset),
            Style = DefaultShapeStyle,
            Text = new TextBody(new BodyProperties(Anchor: TextAnchor.Center, Wrap: true, RightToLeftColumns: false), ListStyle.Empty,
                [new TextParagraph(new ParagraphProperties(Alignment: TextAlignment.Center), [], new RunProperties(Language: "de-DE"))]),
        };
        AddShape("Form einfügen", slideId, shape);
        return id;
    }

    /// <summary>Inserts a text box (grows with its text, no fill) and puts the caret into it.</summary>
    public int InsertTextBox(EmuRect bounds, string text = "")
    {
        var slideId = RequireSlide();
        var id = NextShapeId(CurrentSlide!);
        var shape = new AutoShape
        {
            Id = id,
            Name = $"Textfeld {id - 1}",
            IsTextBox = true,
            Transform = Transform2D.Of(bounds),
            Geometry = ShapeGeometry.Rectangle,
            Fill = NoFill.Instance,
            Text = new TextBody(new BodyProperties(Wrap: true, AutoFit: ShapeAutoFit.Instance), ListStyle.Empty,
                text.Split('\n').Select(line => TextParagraph.Plain(line, new RunProperties(Language: "de-DE"))).ToList()),
        };
        AddShape("Textfeld einfügen", slideId, shape);
        var body = shape.Text!;
        SelectText(new TextTarget(id), TextEdits.End(body), TextEdits.End(body));
        return id;
    }

    /// <summary>Inserts a line or connector from (x1, y1) to (x2, y2) (flips encode the direction).</summary>
    public int InsertLine(long x1, long y1, long x2, long y2, string preset = "line", LineEnd? tail = null)
    {
        var slideId = RequireSlide();
        var id = NextShapeId(CurrentSlide!);
        var shape = new ConnectorShape
        {
            Id = id,
            Name = NameFor(preset, id),
            Transform = new Transform2D(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1), FlipHorizontal: x2 < x1, FlipVertical: y2 < y1),
            Geometry = ShapeGeometry.Of(preset),
            Style = DefaultLineStyle,
            Outline = tail is null ? null : new Outline(Tail: tail),
        };
        AddShape("Linie einfügen", slideId, shape);
        return id;
    }

    /// <summary>
    /// Inserts a picture: into the selected empty picture placeholder if there is one, else centred on the slide at its own size
    /// (96 dpi), shrunk to fit.
    /// </summary>
    public int InsertPicture(ImageData image, EmuRect? bounds = null, string? description = null)
    {
        var slideId = RequireSlide();
        var slide = CurrentSlide!;
        var id = NextShapeId(slide);
        var size = Presentation.SlideSize;
        var width = image.PixelWidth > 0 ? image.PixelWidth * Units.EmuPerInch / 96 : size.Width / 3;
        var height = image.PixelHeight > 0 ? image.PixelHeight * Units.EmuPerInch / 96 : size.Height / 3;
        var scale = Math.Min(1, Math.Min(size.Width * 0.8 / width, size.Height * 0.8 / height));
        width = (long)(width * scale);
        height = (long)(height * scale);
        var target = bounds ?? new EmuRect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        var placeholder = Selection.Shapes.Select(slide.FindShape).OfType<AutoShape>().FirstOrDefault(shape => shape.Placeholder?.Type is PlaceholderType.Picture or PlaceholderType.Object && (shape.Text?.IsEmpty ?? true));
        var picture = new PictureShape
        {
            Id = placeholder?.Id ?? id,
            Name = placeholder is null ? $"Grafik {id - 1}" : $"Bildplatzhalter {placeholder.Id - 1}",
            Description = description,
            Placeholder = placeholder?.Placeholder,
            Transform = placeholder is null ? Transform2D.Of(target) : null,
            Picture = new PictureFill(image, RelativeRect.Zero, StretchMode.Full),
            PictureLocks = new XmlFragment("<p:cNvPicPr xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><a:picLocks noChangeAspect=\"1\" /></p:cNvPicPr>"),
        };
        if (placeholder is not null)
        {
            Apply("Bild einfügen", presentation => WithShape(presentation, slideId, placeholder.Id, _ => picture), _ => Selection with { Shapes = [picture.Id], Text = null });
            return picture.Id;
        }

        AddShape("Bild einfügen", slideId, picture);
        return id;
    }

    /// <summary>Inserts a table of <paramref name="rows"/> × <paramref name="columns"/> with the default table style.</summary>
    public int InsertTable(int rows, int columns, EmuRect? bounds = null)
    {
        var slideId = RequireSlide();
        var id = NextShapeId(CurrentSlide!);
        var size = Presentation.SlideSize;
        var target = bounds ?? new EmuRect(size.Width / 10, size.Height / 4, size.Width * 8 / 10, 370840L * rows);
        var table = Table.Create(Math.Max(1, rows), Math.Max(1, columns), target.Width, target.Height / Math.Max(1, rows), Layout.TableRenderer.DefaultStyleId);
        AddShape("Tabelle einfügen", slideId, new GraphicFrame
        {
            Id = id,
            Name = $"Tabelle {id - 1}",
            Kind = GraphicKind.Table,
            Table = table,
            GraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/table",
            Transform = Transform2D.Of(target),
        });
        return id;
    }

    private void AddShape(string label, uint slideId, SlideElement shape) =>
        Apply(label, presentation => WithSlide(presentation, slideId, slide => slide with { Shapes = InsertIntoEnteredGroup(slide.Shapes, shape) }),
            _ => Selection with { Shapes = [shape.Id], Text = null });

    private ValueList<SlideElement> InsertIntoEnteredGroup(ValueList<SlideElement> shapes, SlideElement shape) => shapes.Add(shape);

    /// <summary>Deletes the selected shapes (a placeholder with content keeps its place empty, like PowerPoint's first Entf).</summary>
    public void DeleteShapes(IReadOnlyList<int>? shapeIds = null)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        if (ids.Count == 0)
        {
            return;
        }

        var slideId = RequireSlide();
        Apply("Löschen", presentation => WithSlide(ids.Aggregate(presentation, (current, id) => WithShape(current, slideId, id, shape =>
            shape is AutoShape { Placeholder: not null, Text: { IsEmpty: false } } placeholder ? placeholder with { Text = TextBody.Empty } : null)), slideId, WithoutDeadAnimations),
            _ => Selection with { Shapes = [], Text = null });
    }

    /// <summary>Duplicates the selected shapes (offset, on top) and selects the copies.</summary>
    public IReadOnlyList<int> DuplicateShapes()
    {
        var ids = SelectedShapeIds();
        var slideId = RequireSlide();
        var copies = new List<int>();
        Apply("Duplizieren", presentation => WithSlide(presentation, slideId, slide =>
        {
            var next = NextShapeId(slide);
            var added = new List<SlideElement>();
            foreach (var id in ids)
            {
                if (slide.FindShape(id) is not { } shape)
                {
                    continue;
                }

                var positioned = shape.Transform is null ? shape with { Transform = ResolvedTransform(presentation, slide, shape) } : shape;
                var copy = Renumber(Offset(positioned with { Placeholder = null }, DuplicateOffset, DuplicateOffset), ref next);
                copies.Add(copy.Id);
                added.Add(copy);
            }

            return slide with { Shapes = slide.Shapes.AddRange(added) };
        }), _ => Selection with { Shapes = copies, Text = null });
        return copies;
    }

    private static Transform2D? ResolvedTransform(Presentation presentation, Slide slide, SlideElement shape) =>
        ShapeResolver.Resolve(SlideContext.For(presentation, slide), shape).Transform;

    private static Transform2D Shift(Transform2D transform, long dx, long dy) => transform with { X = transform.X + dx, Y = transform.Y + dy };

    /// <summary>The element moved by (dx, dy) (a group moves its box; its children follow through the child space).</summary>
    internal static SlideElement Offset(SlideElement shape, long dx, long dy) =>
        shape.Transform is { } transform ? shape with { Transform = Shift(transform, dx, dy) } : shape;

    /// <summary>The element (and a group's children) with new ids from <paramref name="next"/> on; kept XML stays (its ids are rewritten on save).</summary>
    internal static SlideElement Renumber(SlideElement shape, ref int next)
    {
        var renumbered = shape with { Id = next++ };
        if (renumbered is GroupShape group)
        {
            var children = new List<SlideElement>();
            foreach (var child in group.Children)
            {
                children.Add(Renumber(child, ref next));
            }

            renumbered = group with { Id = renumbered.Id, Children = children };
        }

        return renumbered;
    }

    // ------------------------------------------------------------ transform

    /// <summary>Moves the selected shapes by (dx, dy) EMU (arrow keys group into one step while they continue).</summary>
    public void MoveShapes(long dx, long dy, IReadOnlyList<int>? shapeIds = null, bool nudge = false)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        if (ids.Count == 0 || (dx == 0 && dy == 0))
        {
            return;
        }

        var slideId = RequireSlide();
        Apply("Verschieben", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return ids.Aggregate(presentation, (current, id) => WithShape(current, slideId, id, shape =>
            {
                var transform = shape.Transform ?? ResolvedTransform(presentation, slide, shape);
                return transform is null ? shape : shape with { Transform = Shift(transform, dx, dy) };
            }));
        }, kind: nudge ? EditKind.Nudge : EditKind.Other);
    }

    /// <summary>Gives one shape a new box (resize with a handle): position and size in EMU; negative sizes flip.</summary>
    public void SetBounds(int shapeId, EmuRect bounds)
    {
        var slideId = RequireSlide();
        Apply("Größe ändern", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return WithShape(presentation, slideId, shapeId, shape =>
            {
                var transform = shape.Transform ?? ResolvedTransform(presentation, slide, shape) ?? Transform2D.Of(bounds);
                var flipH = bounds.Width < 0 ? !transform.FlipHorizontal : transform.FlipHorizontal;
                var flipV = bounds.Height < 0 ? !transform.FlipVertical : transform.FlipVertical;
                var normalized = new EmuRect(Math.Min(bounds.X, bounds.X + bounds.Width), Math.Min(bounds.Y, bounds.Y + bounds.Height), Math.Abs(bounds.Width), Math.Abs(bounds.Height));
                var changed = transform.WithBounds(normalized) with { FlipHorizontal = flipH, FlipVertical = flipV };
                return shape with { Transform = changed };
            });
        });
    }

    /// <summary>Sets the rotation (degrees, clockwise) of the selected shapes, or adds to it (<paramref name="relative"/>: "rotate 90°").</summary>
    public void Rotate(double degrees, bool relative = false, IReadOnlyList<int>? shapeIds = null)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        var slideId = RequireSlide();
        Apply("Drehen", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return ids.Aggregate(presentation, (current, id) => WithShape(current, slideId, id, shape =>
            {
                if (shape is GraphicFrame || (shape.Transform ?? ResolvedTransform(presentation, slide, shape)) is not { } transform)
                {
                    return shape;
                }

                var angle = Units.NormalizeAngle((relative ? transform.Rotation : 0) + Units.DegreesToAngle(degrees));
                return shape with { Transform = transform with { Rotation = angle } };
            }));
        });
    }

    /// <summary>Flips the selected shapes horizontally or vertically.</summary>
    public void Flip(bool horizontal, IReadOnlyList<int>? shapeIds = null)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        var slideId = RequireSlide();
        Apply(horizontal ? "Horizontal kippen" : "Vertikal kippen", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return ids.Aggregate(presentation, (current, id) => WithShape(current, slideId, id, shape =>
            {
                if (shape is GraphicFrame || (shape.Transform ?? ResolvedTransform(presentation, slide, shape)) is not { } transform)
                {
                    return shape;
                }

                return shape with
                {
                    Transform = horizontal
                        ? transform with { FlipHorizontal = !transform.FlipHorizontal, Rotation = Units.NormalizeAngle(-transform.Rotation) }
                        : transform with { FlipVertical = !transform.FlipVertical, Rotation = Units.NormalizeAngle(-transform.Rotation) },
                };
            }));
        });
    }

    /// <summary>The axis-aligned box of a shape on the current slide (EMU, its rotation ignored), or null.</summary>
    public EmuRect? BoundsOf(int shapeId)
    {
        if (CurrentSlide is not { } slide || slide.FindShape(shapeId) is not { } shape)
        {
            return null;
        }

        return ResolvedTransform(Presentation, slide, shape)?.Bounds;
    }

    /// <summary>Aligns the selected shapes to each other (two or more) or to the slide.</summary>
    public void Align(AlignKind kind, bool toSlide = false)
    {
        var ids = SelectedShapeIds();
        if (ids.Count == 0 || (ids.Count == 1 && !toSlide))
        {
            toSlide = true;
        }

        var boxes = ids.Select(id => (Id: id, Box: BoundsOf(id))).Where(entry => entry.Box is not null).Select(entry => (entry.Id, Box: entry.Box!.Value)).ToList();
        if (boxes.Count == 0)
        {
            return;
        }

        var size = Presentation.SlideSize;
        var area = toSlide ? new EmuRect(0, 0, size.Width, size.Height) : boxes.Select(entry => entry.Box).Aggregate(EmuRect.Union);
        Group("Ausrichten", () =>
        {
            foreach (var (id, box) in boxes)
            {
                var (dx, dy) = kind switch
                {
                    AlignKind.Left => (area.X - box.X, 0L),
                    AlignKind.Center => (area.CenterX - box.CenterX, 0L),
                    AlignKind.Right => (area.Right - box.Right, 0L),
                    AlignKind.Top => (0L, area.Y - box.Y),
                    AlignKind.Middle => (0L, area.CenterY - box.CenterY),
                    _ => (0L, area.Bottom - box.Bottom),
                };
                MoveShapes(dx, dy, [id]);
            }
        });
    }

    /// <summary>Distributes the selected shapes evenly (equal gaps) horizontally or vertically (to the slide with fewer than three).</summary>
    public void Distribute(bool horizontal, bool toSlide = false)
    {
        var boxes = SelectedShapeIds().Select(id => (Id: id, Box: BoundsOf(id))).Where(entry => entry.Box is not null).Select(entry => (entry.Id, Box: entry.Box!.Value)).ToList();
        if (boxes.Count < 2 && !toSlide)
        {
            return;
        }

        toSlide |= boxes.Count < 3;
        var ordered = horizontal ? boxes.OrderBy(entry => entry.Box.X).ToList() : boxes.OrderBy(entry => entry.Box.Y).ToList();
        var size = Presentation.SlideSize;
        var start = toSlide ? 0 : horizontal ? ordered[0].Box.X : ordered[0].Box.Y;
        var end = toSlide ? (horizontal ? size.Width : size.Height) : horizontal ? ordered[^1].Box.Right : ordered[^1].Box.Bottom;
        var total = ordered.Sum(entry => horizontal ? entry.Box.Width : entry.Box.Height);
        var gap = ordered.Count > 1 ? (end - start - total) / (double)(toSlide ? ordered.Count + 1 : ordered.Count - 1) : 0;
        Group("Verteilen", () =>
        {
            var position = start + (toSlide ? gap : 0);
            foreach (var (id, box) in ordered)
            {
                var target = (long)Math.Round(position);
                MoveShapes(horizontal ? target - box.X : 0, horizontal ? 0 : target - box.Y, [id]);
                position += (horizontal ? box.Width : box.Height) + gap;
            }
        });
    }

    /// <summary>Changes the z-order of the selected shapes (within their group).</summary>
    public void Arrange(ZOrderMove move)
    {
        var ids = SelectedShapeIds().ToHashSet();
        var slideId = RequireSlide();
        Apply("Anordnen", presentation => WithSlide(presentation, slideId, slide =>
        {
            ValueList<SlideElement> Reorder(ValueList<SlideElement> shapes)
            {
                var list = shapes.Select(shape => shape is GroupShape group ? group with { Children = Reorder(group.Children) } : shape).ToList();
                if (!list.Any(shape => ids.Contains(shape.Id)))
                {
                    return shapes.Zip(list).All(pair => pair.First == pair.Second) ? shapes : list;
                }

                switch (move)
                {
                    case ZOrderMove.BringToFront:
                        list = [.. list.Where(shape => !ids.Contains(shape.Id)), .. list.Where(shape => ids.Contains(shape.Id))];
                        break;
                    case ZOrderMove.SendToBack:
                        list = [.. list.Where(shape => ids.Contains(shape.Id)), .. list.Where(shape => !ids.Contains(shape.Id))];
                        break;
                    case ZOrderMove.BringForward:
                        for (var index = list.Count - 2; index >= 0; index--)
                        {
                            if (ids.Contains(list[index].Id) && !ids.Contains(list[index + 1].Id))
                            {
                                (list[index], list[index + 1]) = (list[index + 1], list[index]);
                            }
                        }

                        break;
                    default:
                        for (var index = 1; index < list.Count; index++)
                        {
                            if (ids.Contains(list[index].Id) && !ids.Contains(list[index - 1].Id))
                            {
                                (list[index], list[index - 1]) = (list[index - 1], list[index]);
                            }
                        }

                        break;
                }

                return list;
            }

            var reordered = Reorder(slide.Shapes);
            return reordered == slide.Shapes ? slide : slide with { Shapes = reordered };
        }));
    }

    /// <summary>Groups the selected top-level shapes (two or more): the group's child space is the slide's, so nothing moves.</summary>
    public int? GroupShapes()
    {
        var slide = CurrentSlide;
        var ids = SelectedShapeIds().Where(id => slide?.Shapes.Any(shape => shape.Id == id) == true).ToList();
        if (slide is null || ids.Count < 2)
        {
            return null;
        }

        var slideId = slide.Id;
        var groupId = NextShapeId(slide);
        Apply("Gruppieren", presentation => WithSlide(presentation, slideId, current =>
        {
            var members = current.Shapes.Where(shape => ids.Contains(shape.Id))
                .Select(shape => shape.Placeholder is null && shape.Transform is not null ? shape : shape with { Placeholder = null, Transform = ResolvedTransform(presentation, current, shape) })
                .ToList();
            var bounds = members.Select(shape => RotatedBounds(shape.Transform!)).Aggregate(EmuRect.Union);
            var group = new GroupShape
            {
                Id = groupId,
                Name = $"Gruppieren {groupId - 1}",
                Transform = new Transform2D(bounds.X, bounds.Y, bounds.Width, bounds.Height, ChildX: bounds.X, ChildY: bounds.Y, ChildWidth: bounds.Width, ChildHeight: bounds.Height),
                Children = members,
            };
            var last = current.Shapes.Select((shape, index) => (shape, index)).Where(entry => ids.Contains(entry.shape.Id)).Max(entry => entry.index);
            var shapes = current.Shapes.ToList();
            shapes.Insert(last + 1, group);
            shapes.RemoveAll(shape => ids.Contains(shape.Id));
            return current with { Shapes = shapes };
        }), _ => Selection with { Shapes = [groupId], Text = null, EnteredGroup = null });
        return groupId;
    }

    /// <summary>The axis-aligned box of a rotated transform.</summary>
    internal static EmuRect RotatedBounds(Transform2D transform)
    {
        if (transform.Rotation == 0)
        {
            return transform.Bounds;
        }

        var radians = transform.Rotation / 60000.0 * Math.PI / 180;
        var (cos, sin) = (Math.Abs(Math.Cos(radians)), Math.Abs(Math.Sin(radians)));
        var width = (transform.Width * cos) + (transform.Height * sin);
        var height = (transform.Width * sin) + (transform.Height * cos);
        var (centerX, centerY) = (transform.X + (transform.Width / 2.0), transform.Y + (transform.Height / 2.0));
        return new EmuRect((long)Math.Round(centerX - (width / 2)), (long)Math.Round(centerY - (height / 2)), (long)Math.Round(width), (long)Math.Round(height));
    }

    /// <summary>Ungroups the selected groups: children take the group's mapping (scale, offset, rotation) into their own transforms.</summary>
    public IReadOnlyList<int> Ungroup()
    {
        var slide = CurrentSlide;
        var groups = SelectedShapeIds().Select(id => slide?.Shapes.FirstOrDefault(shape => shape.Id == id)).OfType<GroupShape>().ToList();
        if (slide is null || groups.Count == 0)
        {
            return [];
        }

        var released = groups.SelectMany(group => group.Children.Select(child => child.Id)).ToList();
        Apply("Gruppierung aufheben", presentation => WithSlide(presentation, slide.Id, current =>
        {
            var shapes = new List<SlideElement>();
            foreach (var shape in current.Shapes)
            {
                if (shape is GroupShape group && groups.Any(item => item.Id == group.Id) && group.Transform is { } transform)
                {
                    shapes.AddRange(group.Children.Select(child => MapOutOfGroup(child, transform)));
                    continue;
                }

                shapes.Add(shape);
            }

            return current with { Shapes = shapes };
        }), _ => Selection with { Shapes = released, Text = null, EnteredGroup = null });
        return released;
    }

    /// <summary>A child's transform in its group's parent space.</summary>
    internal static SlideElement MapOutOfGroup(SlideElement child, Transform2D group)
    {
        if (child.Transform is not { } transform)
        {
            return child;
        }

        var box = Layout.SlideRenderer.MapChild(transform, group);
        var matrix = Layout.SlideRenderer.GroupMatrix(group);
        var centerX = Units.EmuToPoints(box.X) + (Units.EmuToPoints(box.Width) / 2);
        var centerY = Units.EmuToPoints(box.Y) + (Units.EmuToPoints(box.Height) / 2);
        var (mappedX, mappedY) = matrix.Apply(centerX, centerY);
        var (width, height) = (box.Width, box.Height);
        var flipH = transform.FlipHorizontal ^ group.FlipHorizontal;
        var flipV = transform.FlipVertical ^ group.FlipVertical;
        var rotation = transform.Rotation * (group.FlipHorizontal ^ group.FlipVertical ? -1 : 1) + group.Rotation;
        var mapped = transform with
        {
            X = Units.PointsToEmu(mappedX) - (width / 2),
            Y = Units.PointsToEmu(mappedY) - (height / 2),
            Width = width,
            Height = height,
            Rotation = Units.NormalizeAngle(rotation),
            FlipHorizontal = flipH,
            FlipVertical = flipV,
        };
        if (child is GroupShape inner)
        {
            mapped = mapped with
            {
                ChildX = transform.ChildX,
                ChildY = transform.ChildY,
                ChildWidth = transform.ChildWidth,
                ChildHeight = transform.ChildHeight,
            };
        }

        return child with { Transform = mapped };
    }

    /// <summary>Opens a group: clicks select its children (Doppelklick on a group).</summary>
    public void EnterGroup(int groupId) => SelectShapes([], groupId);

    // ------------------------------------------------------------ format

    /// <summary>Sets the fill of the selected shapes (null: from the style again).</summary>
    public void SetFill(Fill? fill, IReadOnlyList<int>? shapeIds = null) =>
        FormatShapes("Füllung", shapeIds, shape => shape switch
        {
            AutoShape auto => auto with { Fill = fill },
            ConnectorShape connector => connector with { Fill = fill },
            GroupShape group => group with { Fill = fill },
            _ => shape,
        });

    /// <summary>Changes the outline of the selected shapes (width, colour, dash, arrowheads …).</summary>
    public void SetOutline(Func<Outline, Outline> change, IReadOnlyList<int>? shapeIds = null) =>
        FormatShapes("Kontur", shapeIds, shape => shape switch
        {
            AutoShape auto => auto with { Outline = change(auto.Outline ?? new Outline()) },
            ConnectorShape connector => connector with { Outline = change(connector.Outline ?? new Outline()) },
            PictureShape picture => picture with { Outline = change(picture.Outline ?? new Outline()) },
            _ => shape,
        });

    /// <summary>Sets or removes the shadow of the selected shapes (other effects stay).</summary>
    public void SetShadow(OuterShadow? shadow, IReadOnlyList<int>? shapeIds = null)
    {
        ShapeEffects Change(ShapeEffects? effects) => (effects ?? ShapeEffects.Empty) with { Shadow = shadow };
        FormatShapes("Schatten", shapeIds, shape => shape switch
        {
            AutoShape auto => auto with { Effects = Change(auto.Effects) },
            ConnectorShape connector => connector with { Effects = Change(connector.Effects) },
            PictureShape picture => picture with { Effects = Change(picture.Effects) },
            GroupShape group => group with { Effects = Change(group.Effects) },
            _ => shape,
        });
    }

    /// <summary>Changes the geometry of the selected auto shapes ("Form ändern").</summary>
    public void SetGeometry(string preset, IReadOnlyList<int>? shapeIds = null) =>
        FormatShapes("Form ändern", shapeIds, shape => shape is AutoShape auto ? auto with { Geometry = ShapeGeometry.Of(preset) } : shape);

    /// <summary>Drags adjust handle <paramref name="handleIndex"/> of a shape to a point in its box (EMU, relative to its top-left, unrotated).</summary>
    public void DragAdjustHandle(int shapeId, int handleIndex, long x, long y)
    {
        var slideId = RequireSlide();
        Apply("Form anpassen", presentation =>
        {
            var slide = presentation.FindSlide(slideId)!;
            return WithShape(presentation, slideId, shapeId, shape =>
            {
                var resolved = ShapeResolver.Resolve(SlideContext.For(presentation, slide), shape);
                var (width, height) = (Units.EmuToPoints(resolved.Transform.Width), Units.EmuToPoints(resolved.Transform.Height));
                var adjusts = GeometryEngine.AdjustForHandle(resolved.Geometry, handleIndex, width, height, Units.EmuToPoints(x), Units.EmuToPoints(y));
                var geometry = adjusts.Aggregate(resolved.Geometry, (current, adjust) => current.WithAdjust(adjust.Name, adjust.Value));
                return shape switch
                {
                    AutoShape auto => auto with { Geometry = geometry },
                    ConnectorShape connector => connector with { Geometry = geometry },
                    _ => shape,
                };
            });
        });
    }

    /// <summary>Sets the alternative text of a shape.</summary>
    public void SetAlternativeText(int shapeId, string? description, string? title = null) =>
        FormatShapes("Alternativtext", [shapeId], shape => shape with { Description = string.IsNullOrEmpty(description) ? null : description, AltTitle = string.IsNullOrEmpty(title) ? null : title });

    /// <summary>Sets the crop of a picture (fractions in 1000ths of a percent from each side).</summary>
    public void SetCrop(int shapeId, RelativeRect crop) =>
        FormatShapes("Zuschneiden", [shapeId], shape => shape is PictureShape picture ? picture with { Picture = picture.Picture with { Crop = crop } } : shape);

    /// <summary>Sets the click action of the selected shapes.</summary>
    public void SetShapeAction(Hyperlink? action, IReadOnlyList<int>? shapeIds = null) =>
        FormatShapes("Aktion", shapeIds, shape => shape with { Click = action });

    private void FormatShapes(string label, IReadOnlyList<int>? shapeIds, Func<SlideElement, SlideElement> change)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        if (ids.Count == 0)
        {
            return;
        }

        var slideId = RequireSlide();
        Apply(label, presentation => ids.Aggregate(presentation, (current, id) => WithShape(current, slideId, id, shape =>
        {
            var changed = change(shape);
            return changed == shape ? shape : changed;
        })));
    }
}

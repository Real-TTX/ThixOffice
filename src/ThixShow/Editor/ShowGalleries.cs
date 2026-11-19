using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

using RenderOptions = ThixShow.Core.Layout.RenderOptions;

namespace ThixShow.Editor;

/// <summary>
/// The galleries with pictures (flyouts of <see cref="RibbonFlyoutButton"/>): the layouts of the presentation's master (Neue Folie,
/// Layout), the designs (Entwurf › Designs, each as its title slide), the shapes grouped like PowerPoint's (Formen). Pictures are
/// rendered by the core and drawn by Skia; chrome around them in Mat tokens.
/// </summary>
internal static class ShowGalleries
{
    public const string NewSlideGallery = "show.gallery.newSlide";
    public const string LayoutGallery = "show.gallery.layout";
    public const string ShapesGallery = "show.gallery.shapes";
    public const string DesignGallery = "show.gallery.designs";
    public const string TablePicker = "show.gallery.table";

    /// <summary>The ribbon's own controls of ThixShow by item id, or null.</summary>
    public static Control? Create(OfficeWindow window, string id) => id switch
    {
        ShapesGallery + ".change" => new RibbonFlyoutButton(window, ShowCommandIds.ChangeShape, "Form ändern", "shapes", button => Shapes(window, button, ShowCommandIds.ChangeShape), tooltip: "Form ändern – gibt den markierten Formen eine andere Form"),
        _ when id.EndsWith(".insert", StringComparison.Ordinal) || id.EndsWith(".format", StringComparison.Ordinal) => Create(window, id[..id.LastIndexOf('.')]),
        NewSlideGallery => new RibbonFlyoutButton(window, ShowCommandIds.NewSlide, "Neue Folie", "slide-add", button => Layouts(window, button, newSlide: true), tooltip: "Neue Folie – mit einem Layout Ihrer Wahl"),
        LayoutGallery => new RibbonFlyoutButton(window, ShowCommandIds.Layout, "Layout", "template", button => Layouts(window, button, newSlide: false), large: false),
        ShapesGallery => new RibbonFlyoutButton(window, ShowCommandIds.Shapes, "Formen", "shapes", button => Shapes(window, button, ShowCommandIds.Shapes), tooltip: "Formen – wählen Sie eine Form und ziehen Sie sie auf der Folie auf"),
        DesignGallery => new DesignStrip(window),
        TablePicker => new TablePicker(window, ShowCommandIds.InsertTable, (rows, columns) => Apply(window, ShowCommandIds.InsertTable, $"{rows.ToString(CultureInfo.InvariantCulture)}x{columns.ToString(CultureInfo.InvariantCulture)}")),
        _ => null,
    };

    /// <summary>Runs a value command of the window with a value.</summary>
    private static void Apply(OfficeWindow window, string commandId, string value)
    {
        if (window.Commands.Find(commandId) is ThixOffice.Core.Commands.OfficeValueCommand command)
        {
            _ = command.ApplyAsync(value);
        }
    }

    /// <summary>The layouts of the current slide's master as pictures with their names.</summary>
    private static Control Layouts(OfficeWindow window, RibbonFlyoutButton button, bool newSlide)
    {
        if (window.Editor is not ShowEditor editor || editor.Session.Presentation.Masters.Count == 0)
        {
            return new TextBlock { Text = "Keine Layouts" };
        }

        var presentation = editor.Session.Presentation;
        var master = editor.Session.CurrentSlide is { } slide ? presentation.MasterOf(slide) : presentation.Masters[0];
        var current = editor.Session.CurrentSlide?.LayoutId;
        var grid = new WrapPanel { MaxWidth = 3 * 168, Margin = new Thickness(8) };
        foreach (var layout in master.Layouts)
        {
            var picture = SlidePictures.Record(SlideRenderer.RenderLayout(presentation, master, layout, OfficeFonts.Cached));
            var name = layout.Name ?? layout.Type;
            var entry = Tile(new PictureView(picture, presentation.SlideSize.WidthPoints) { Width = 144, Height = 144 / presentation.SlideSize.AspectRatio }, name, !newSlide && layout.Id == current);
            var id = layout.Id.ToString(CultureInfo.InvariantCulture);
            entry.Click += (_, _) =>
            {
                button.Close();
                Apply(window, newSlide ? ShowCommandIds.NewSlideLayout : ShowCommandIds.Layout, id);
            };
            grid.Children.Add(entry);
        }

        var panel = new StackPanel { Children = { Heading(master.Theme.Name), grid } };
        if (newSlide)
        {
            var duplicate = new Button { Content = new IconLabel { Icon = "copy", Text = "Ausgewählte Folien duplizieren" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 8, 8) };
            duplicate.Click += async (_, _) =>
            {
                button.Close();
                if (window.Commands.Find(ShowCommandIds.DuplicateSlide) is { } command)
                {
                    await OfficeWindow.RunCommandAsync(window, command);
                }
            };
            panel.Children.Add(duplicate);
        }

        return new ScrollViewer { Content = panel, MaxHeight = 560 };
    }

    /// <summary>A design's picture: its title slide ("Aa" in the title font) in a slide size.</summary>
    public static SKPicture DesignPicture(ThemeDesign design, SlideSize size)
    {
        var sample = MasterFactory.CreatePresentation(design, size);
        var slide = MasterFactory.CreateSlide(sample.Masters[0].Layouts[0], 256);
        var title = slide.Shapes.OfType<AutoShape>().FirstOrDefault(shape => shape.Placeholder?.IsTitle == true);
        if (title is not null)
        {
            slide = slide with { Shapes = ShapeTree.Replace(slide.Shapes, title.Id, shape => ((AutoShape)shape) with { Text = TextBody.FromText("Aa") }) };
        }

        sample = sample with { Slides = [slide] };
        var page = SlideRenderer.Render(sample, slide, OfficeFonts.Cached);

        // The design's six accents as a strip at the foot (PowerPoint's designs show their colours the same way).
        var master = sample.Masters[0];
        var colors = new ColorContext(master.Theme.Colors, master.ColorMap);
        var items = page.Items.ToList();
        var (width, height) = (page.Width, page.Height);
        var swatch = width / 6;
        for (var index = 0; index < 6; index++)
        {
            var color = ColorResolver.Resolve(ColorSpec.Scheme($"accent{index + 1}"), colors);
            items.Add(new RectangleItem(index * swatch, height * 0.9, swatch, height * 0.1, color));
        }

        return SlidePictures.Record(page with { Items = items });
    }

    /// <summary>The internal applies a value command (the design strip).</summary>
    internal static void ApplyValue(OfficeWindow window, string commandId, string value) => Apply(window, commandId, value);

    /// <summary>The shapes by category; a choice starts drawing it on the slide (or changes the selected shapes' form).</summary>
    public static Control Shapes(OfficeWindow window, RibbonFlyoutButton? button, string commandId)
    {
        var panel = new StackPanel { Margin = new Thickness(8), Spacing = 2 };
        foreach (var category in ShapeCatalog.Categories)
        {
            if (commandId == ShowCommandIds.ChangeShape && category.Presets.All(ShapeCatalog.IsLine))
            {
                continue;
            }

            panel.Children.Add(Heading(category.Name));
            var row = new WrapPanel { MaxWidth = 12 * 30 };
            foreach (var preset in category.Presets)
            {
                var shape = new Button { Width = 28, Height = 28, Padding = new Thickness(3), Content = new ShapePreview(preset) { Width = 20, Height = 20 } }.Themed("MatButton.Icon");
                var name = ShapeCatalog.NameOf(preset);
                AutomationProperties.SetName(shape, name);
                ToolTip.SetTip(shape, name);
                var chosen = preset;
                shape.Click += (_, _) =>
                {
                    button?.Close();
                    Apply(window, commandId, chosen);
                };
                row.Children.Add(shape);
            }

            panel.Children.Add(row);
        }

        return new ScrollViewer { Content = panel, MaxHeight = 520 };
    }

    private static TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 8, 8, 4) };
        heading.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        return heading;
    }

    /// <summary>A gallery entry: the picture with a frame (the accent when it is the current one) and its name below.</summary>
    private static Button Tile(Control picture, string name, bool current)
    {
        var frame = new Border { Child = picture, BorderThickness = new Thickness(current ? 2 : 1) };
        frame.Use(Border.BorderBrushProperty, current ? "Mat.Accent" : "Mat.Border");
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = picture.Width, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var tile = new Button { Content = new StackPanel { Children = { frame, label } }, Padding = new Thickness(4), Margin = new Thickness(2) }.Themed("MatButton.Icon");
        AutomationProperties.SetName(tile, name);
        ToolTip.SetTip(tile, name);
        return tile;
    }
}

/// <summary>A recorded slide picture (points) drawn fitted into the control.</summary>
internal sealed class PictureView(SKPicture picture, double widthPoints) : Control
{
    public override void Render(DrawingContext context)
    {
        var scale = Bounds.Width / Math.Max(1, widthPoints);
        var size = SKRect.Create((float)Bounds.Width, (float)Bounds.Height);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            canvas.Save();
            canvas.ClipRect(size);
            canvas.DrawColor(ShowPalette.EmptySlide);
            canvas.Scale((float)scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }));
    }
}

/// <summary>A preset shape drawn as an outline in the text colour (the shapes gallery).</summary>
internal sealed class ShapePreview(string preset) : Control
{
    public override void Render(DrawingContext context)
    {
        var (width, height) = (Math.Max(4, Bounds.Width - 2), Math.Max(4, Bounds.Height - 2));
        var pen = new Pen(Ui.Brush("Mat.Text"), 1.2);
        if (ShapeCatalog.IsLine(preset))
        {
            var (start, end) = (new Point(1, height), new Point(width, 1));
            switch (preset)
            {
                case "bentConnector3":
                    context.DrawLine(pen, start, new Point(width / 2, height));
                    context.DrawLine(pen, new Point(width / 2, height), new Point(width / 2, 1));
                    context.DrawLine(pen, new Point(width / 2, 1), end);
                    break;
                case "curvedConnector3":
                    var curve = new StreamGeometry();
                    using (var sink = curve.Open())
                    {
                        sink.BeginFigure(start, false);
                        sink.CubicBezierTo(new Point(width / 2, height), new Point(width / 2, 1), end);
                        sink.EndFigure(false);
                    }

                    context.DrawGeometry(null, pen, curve);
                    break;
                default:
                    context.DrawLine(pen, start, end);
                    if (preset == "straightConnector1")
                    {
                        context.DrawLine(pen, end, new Point(end.X - 5, end.Y + 1));
                        context.DrawLine(pen, end, new Point(end.X - 1, end.Y + 5));
                    }

                    break;
            }

            return;
        }

        var geometry = GeometryEngine.Evaluate(ShapeGeometry.Of(preset), width, height);
        foreach (var path in geometry.Paths)
        {
            var stream = new StreamGeometry();
            using (var sink = stream.Open())
            {
                var open = false;
                path.Path.Visit((verb, points) =>
                {
                    switch (verb)
                    {
                        case PathVerb.MoveTo:
                            if (open)
                            {
                                sink.EndFigure(false);
                            }

                            sink.BeginFigure(new Point(points[0] + 1, points[1] + 1), true);
                            open = true;
                            break;
                        case PathVerb.LineTo when open:
                            sink.LineTo(new Point(points[0] + 1, points[1] + 1));
                            break;
                        case PathVerb.QuadTo when open:
                            sink.QuadraticBezierTo(new Point(points[0] + 1, points[1] + 1), new Point(points[2] + 1, points[3] + 1));
                            break;
                        case PathVerb.CubicTo when open:
                            sink.CubicBezierTo(new Point(points[0] + 1, points[1] + 1), new Point(points[2] + 1, points[3] + 1), new Point(points[4] + 1, points[5] + 1));
                            break;
                        case PathVerb.Close when open:
                            sink.EndFigure(true);
                            open = false;
                            break;
                    }
                });
                if (open)
                {
                    sink.EndFigure(false);
                }
            }

            context.DrawGeometry(null, path.Stroke ? pen : null, stream);
        }
    }
}

/// <summary>
/// Entwurf › Designs like PowerPoint: the designs side by side in the ribbon, each as its title slide ("Aa" in its fonts and
/// colours), the current one framed in the accent; a click gives the presentation that design.
/// </summary>
internal sealed class DesignStrip : StackPanel
{
    private readonly OfficeWindow _window;
    private readonly List<(string Id, Border Frame)> _tiles = [];

    public DesignStrip(OfficeWindow window)
    {
        _window = window;
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        Margin = new Thickness(2, 4, 2, 0);
        var size = SlideSize.Widescreen;
        foreach (var design in BuiltInThemes.All)
        {
            var picture = ShowGalleries.DesignPicture(design, size);
            var frame = new Border { Child = new PictureView(picture, size.WidthPoints) { Width = 96, Height = 54 }, BorderThickness = new Thickness(2) };
            var tile = new Button { Content = frame, Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Top }.Themed("MatRibbon.Item");
            AutomationProperties.SetName(tile, design.Name);
            ToolTip.SetTip(tile, $"Design „{design.Name}“");
            var id = design.Id;
            tile.Click += (_, _) => ShowGalleries.ApplyValue(window, ShowCommandIds.Design, id);
            _tiles.Add((id, frame));
            Children.Add(tile);
        }

        window.Commands.StateChanged += (_, _) => Ui.Post(Sync);
        Sync();
    }

    /// <summary>The design tiles (tests click them).</summary>
    public IEnumerable<Button> Tiles => Children.OfType<Button>();

    private void Sync()
    {
        var command = _window.Commands.Find(ShowCommandIds.Design) as ThixOffice.Core.Commands.OfficeValueCommand;
        IsEnabled = command?.IsEnabled ?? false;
        var current = command?.Value;
        foreach (var (id, frame) in _tiles)
        {
            frame.Use(Border.BorderBrushProperty, id == current ? "Mat.Accent" : "Mat.Border");
        }
    }
}

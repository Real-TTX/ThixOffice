using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>What is printed or exported: the slides, handouts with N slides per page, or notes pages.</summary>
public enum PrintKind
{
    Slides,
    Handouts,
    NotesPages,
}

/// <summary>
/// Pages for PDF export and printing, as display lists in points: one slide per page (the slide size), handouts (1, 2, 3 – with
/// lines for notes –, 4, 6 or 9 slides per A4 portrait page, framed, with the page number) and notes pages (the slide above its
/// notes text, the notes size). Hidden slides are left out unless asked for.
/// </summary>
public static class PrintLayouts
{
    public const double A4Width = 595.28;
    public const double A4Height = 841.89;

    public static IReadOnlyList<PageDisplayList> Pages(Presentation presentation, ITextMeasurer measurer, PrintKind kind, int slidesPerPage = 6, bool includeHidden = false, RenderOptions? options = null) =>
        kind switch
        {
            PrintKind.Handouts => Handouts(presentation, measurer, slidesPerPage, includeHidden, options),
            PrintKind.NotesPages => NotesPages(presentation, measurer, includeHidden, options),
            _ => Slides(presentation, measurer, includeHidden, options),
        };

    public static IReadOnlyList<PageDisplayList> Slides(Presentation presentation, ITextMeasurer measurer, bool includeHidden = false, RenderOptions? options = null) =>
        Printed(presentation, includeHidden)
            .Select((slide, index) => SlideRenderer.Render(presentation, slide, measurer, options) with { PageIndex = index })
            .ToList();

    /// <summary>The handout grid of <paramref name="perPage"/> (1, 2, 3, 4, 6, 9): rows and columns.</summary>
    public static (int Columns, int Rows) Grid(int perPage) => perPage switch
    {
        1 => (1, 1),
        2 => (1, 2),
        3 => (1, 3),
        4 => (2, 2),
        9 => (3, 3),
        _ => (2, 3),
    };

    public static IReadOnlyList<PageDisplayList> Handouts(Presentation presentation, ITextMeasurer measurer, int perPage, bool includeHidden = false, RenderOptions? options = null)
    {
        perPage = perPage is 1 or 2 or 3 or 4 or 6 or 9 ? perPage : 6;
        var slides = Printed(presentation, includeHidden);
        var (columns, rows) = Grid(perPage);
        var pages = new List<PageDisplayList>();
        const double margin = 42;
        const double gap = 18;
        var areaWidth = A4Width - (2 * margin);
        var areaHeight = A4Height - (2 * margin) - 20;
        var cellWidth = perPage == 3 ? areaWidth * 0.48 : (areaWidth - ((columns - 1) * gap)) / columns;
        var cellHeight = (areaHeight - ((rows - 1) * gap)) / rows;
        var aspect = presentation.SlideSize.AspectRatio;
        for (var start = 0; start < slides.Count; start += perPage)
        {
            var items = new List<DisplayItem> { new RectangleItem(0, 0, A4Width, A4Height, RgbColor.White) };
            for (var index = 0; index < perPage && start + index < slides.Count; index++)
            {
                var (column, row) = (index % columns, index / columns);
                var x = margin + (column * (cellWidth + gap));
                var y = margin + (row * (cellHeight + gap));
                var width = Math.Min(cellWidth, cellHeight * aspect);
                var height = width / aspect;
                x += perPage == 3 ? 0 : (cellWidth - width) / 2;
                y += (cellHeight - height) / 2;
                AddSlide(items, presentation, slides[start + index], measurer, x, y, width, height, options);
                if (perPage == 3)
                {
                    var lineLeft = margin + (areaWidth * 0.54);
                    for (var line = 1; line <= 8; line++)
                    {
                        var lineY = y + (height * line / 9);
                        items.Add(new LineItem(lineLeft, lineY, A4Width - margin, lineY, new RgbColor(0xA6, 0xA6, 0xA6), 0.5));
                    }
                }
            }

            AddPageNumber(items, A4Width, A4Height, pages.Count + 1, measurer);
            pages.Add(new PageDisplayList(pages.Count, A4Width, A4Height, items));
        }

        return pages;
    }

    public static IReadOnlyList<PageDisplayList> NotesPages(Presentation presentation, ITextMeasurer measurer, bool includeHidden = false, RenderOptions? options = null)
    {
        var pageWidth = Units.EmuToPoints(presentation.NotesSize.Width);
        var pageHeight = Units.EmuToPoints(presentation.NotesSize.Height);
        var pages = new List<PageDisplayList>();
        foreach (var slide in Printed(presentation, includeHidden))
        {
            var items = new List<DisplayItem> { new RectangleItem(0, 0, pageWidth, pageHeight, RgbColor.White) };
            var width = pageWidth * 0.8;
            var height = width / presentation.SlideSize.AspectRatio;
            var maxHeight = pageHeight * 0.4;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * presentation.SlideSize.AspectRatio;
            }

            AddSlide(items, presentation, slide, measurer, (pageWidth - width) / 2, pageHeight * 0.075, width, height, options);
            var notesTop = (pageHeight * 0.075) + height + (pageHeight * 0.04);
            var notesBox = new Transform2D(
                Units.PointsToEmu(pageWidth * 0.1),
                Units.PointsToEmu(notesTop),
                Units.PointsToEmu(pageWidth * 0.8),
                Units.PointsToEmu((pageHeight * 0.9) - notesTop));
            AddNotesText(items, presentation, slide, measurer, notesBox);
            AddPageNumber(items, pageWidth, pageHeight, pages.Count + 1, measurer);
            pages.Add(new PageDisplayList(pages.Count, pageWidth, pageHeight, items));
        }

        return pages;
    }

    private static List<Slide> Printed(Presentation presentation, bool includeHidden) =>
        presentation.Slides.Where(slide => includeHidden || !slide.Hidden).ToList();

    /// <summary>A slide scaled into a framed rectangle.</summary>
    internal static void AddSlide(List<DisplayItem> items, Presentation presentation, Slide slide, ITextMeasurer measurer, double x, double y, double width, double height, RenderOptions? options)
    {
        var scale = width / presentation.SlideSize.WidthPoints;
        var page = SlideRenderer.Render(presentation, slide, measurer, options);
        items.Add(new ClipBeginItem(x, y, width, height));
        items.Add(new TransformBeginItem(Matrix2D.Scaling(scale, scale) * Matrix2D.Translation(x, y)));
        items.AddRange(page.Items);
        items.Add(TransformEndItem.Instance);
        items.Add(ClipEndItem.Instance);
        items.Add(new RectangleItem(x, y, width, height, null, new RgbColor(0x80, 0x80, 0x80), 0.5));
    }

    private static void AddNotesText(List<DisplayItem> items, Presentation presentation, Slide slide, ITextMeasurer measurer, Transform2D box)
    {
        var text = slide.Notes?.NotesBody?.Text;
        if (text is null || text.IsEmpty)
        {
            return;
        }

        var notes = slide.Notes!;
        var context = SlideContext.ForNotes(presentation, slide, notes);
        var shape = new AutoShape { Id = 0, Transform = box, Text = text };
        var resolved = ShapeResolver.Resolve(context, shape) with
        {
            TextStyles = [presentation.DefaultTextStyle, new ListStyle(new ParagraphProperties(DefaultRun: new RunProperties(Size: 1200)), ValueList<ParagraphProperties?>.Empty), text.ListStyle],
            Body = BodyProperties.Empty,
        };
        var layout = TextBodyLayouter.Layout(resolved, text, context, measurer, Units.EmuToPoints(box.X), Units.EmuToPoints(box.Y), Units.EmuToPoints(box.Width), Units.EmuToPoints(box.Height));
        items.AddRange(layout.Items);
    }

    private static void AddPageNumber(List<DisplayItem> items, double width, double height, int number, ITextMeasurer measurer)
    {
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var font = new FontSpec("Arial", 9);
        var shaped = measurer.Shape(text, font);
        TextBodyLayouter.AddText(items, width - 42 - shaped.Width, height - 24, shaped, 0, text.Length, font, new RgbColor(0x59, 0x59, 0x59), 0);
    }
}

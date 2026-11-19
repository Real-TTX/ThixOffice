namespace ThixShow.Core.Model;

/// <summary>
/// Makes ThixShow's own masters and layouts for a design and slide size (positions as fractions of the slide, so 16:9 and 4:3 both
/// look right) and new presentations. Layouts: Titelfolie, Titel und Inhalt, Abschnittsüberschrift, Zwei Inhalte, Vergleich, Nur
/// Titel, Leer, Bild mit Überschrift.
/// </summary>
public static class MasterFactory
{
    public const uint FirstMasterId = 2147483648;

    /// <summary>A new presentation: the design's master and layouts and one title slide.</summary>
    public static Presentation CreatePresentation(ThemeDesign? design = null, SlideSize? size = null)
    {
        design ??= BuiltInThemes.ThixShow;
        size ??= SlideSize.Widescreen;
        var master = CreateMaster(design, size, FirstMasterId);
        var presentation = new Presentation
        {
            SlideSize = size,
            DefaultTextStyle = DefaultTextStyle(),
            Masters = [master],
        };
        var titleLayout = master.Layouts.First(layout => layout.Type == "title");
        return presentation with { Slides = [CreateSlide(titleLayout, 256)] };
    }

    /// <summary>A slide of <paramref name="layout"/> with empty placeholders for its content placeholders (no footers).</summary>
    public static Slide CreateSlide(SlideLayout layout, uint id)
    {
        var shapes = new List<SlideElement>();
        var nextId = 2;
        foreach (var placeholder in layout.Shapes.Where(shape => shape.Placeholder is { IsFooterKind: false }))
        {
            shapes.Add(new AutoShape
            {
                Id = nextId++,
                Name = PlaceholderName(placeholder.Placeholder!, nextId - 1),
                Placeholder = placeholder.Placeholder! with { HasCustomPrompt = false },
                Text = new TextBody(BodyProperties.Empty, ListStyle.Empty, [TextParagraph.Empty]),
            });
        }

        return new Slide { Id = id, LayoutId = layout.Id, Shapes = shapes };
    }

    /// <summary>The German shape name of a placeholder ("Titel 1").</summary>
    public static string PlaceholderName(PlaceholderInfo placeholder, int id) => placeholder.Type switch
    {
        PlaceholderType.Title or PlaceholderType.CenteredTitle => $"Titel {id}",
        PlaceholderType.Subtitle => $"Untertitel {id}",
        PlaceholderType.Picture => $"Bildplatzhalter {id}",
        PlaceholderType.DateAndTime => $"Datumsplatzhalter {id}",
        PlaceholderType.Footer => $"Fußzeilenplatzhalter {id}",
        PlaceholderType.SlideNumber => $"Foliennummernplatzhalter {id}",
        PlaceholderType.Body => $"Textplatzhalter {id}",
        _ => $"Inhaltsplatzhalter {id}",
    };

    /// <summary>The presentation's default text style: 18 pt in the body font and text colour, levels indented by 0.5".</summary>
    public static ListStyle DefaultTextStyle()
    {
        var levels = Enumerable.Range(0, 9)
            .Select(level => (ParagraphProperties?)new ParagraphProperties(
                LeftMargin: level * 457200,
                Alignment: TextAlignment.Left,
                DefaultTabSize: 914400,
                RightToLeft: false,
                DefaultRun: new RunProperties(Size: 1800, Fill: new SolidFill(ColorSpec.Scheme("tx1")), Latin: new TextFont("+mn-lt"), EastAsian: new TextFont("+mn-ea"), ComplexScript: new TextFont("+mn-cs"), Kerning: 1200)))
            .ToList();
        return new ListStyle(new ParagraphProperties(DefaultRun: new RunProperties(Language: "de-DE")), levels);
    }

    /// <summary>A master for <paramref name="design"/> with its layouts (ids following <paramref name="id"/>).</summary>
    public static SlideMaster CreateMaster(ThemeDesign design, SlideSize size, uint id)
    {
        var frame = new Frame(size);
        var footerRun = new RunProperties(Size: 1200, Fill: new SolidFill(ColorSpec.Scheme("tx1").LumMod(65000).LumOff(35000)));
        var shapes = new List<SlideElement>
        {
            Placeholder(2, "Titelplatzhalter 1", new PlaceholderInfo(PlaceholderType.Title), frame.Rect(0.05, 0.053, 0.9, 0.193),
                new BodyProperties(Anchor: TextAnchor.Center, AutoFit: new NormalAutoFit()), "Titelmasterformat durch Klicken bearbeiten"),
            Placeholder(3, "Textplatzhalter 2", new PlaceholderInfo(PlaceholderType.Body, 1), frame.Rect(0.05, 0.267, 0.9, 0.635),
                new BodyProperties(AutoFit: new NormalAutoFit()), "Textmasterformat bearbeiten\nZweite Ebene\nDritte Ebene\nVierte Ebene\nFünfte Ebene", levels: true),
            Placeholder(4, "Datumsplatzhalter 3", new PlaceholderInfo(PlaceholderType.DateAndTime, 2, Size: "half"), frame.Rect(0.05, 0.927, 0.225, 0.053),
                new BodyProperties(Anchor: TextAnchor.Center), "", listStyle: Aligned(TextAlignment.Left, footerRun)),
            Placeholder(5, "Fußzeilenplatzhalter 4", new PlaceholderInfo(PlaceholderType.Footer, 3, Size: "quarter"), frame.Rect(0.331, 0.927, 0.338, 0.053),
                new BodyProperties(Anchor: TextAnchor.Center), "", listStyle: Aligned(TextAlignment.Center, footerRun)),
            Placeholder(6, "Foliennummernplatzhalter 5", new PlaceholderInfo(PlaceholderType.SlideNumber, 4, Size: "quarter"), frame.Rect(0.725, 0.927, 0.225, 0.053),
                new BodyProperties(Anchor: TextAnchor.Center), "‹Nr.›", listStyle: Aligned(TextAlignment.Right, footerRun), slideNumber: true),
        };

        var background = new Background(Reference: new StyleReference(1001, ColorSpec.Scheme("bg1")));
        var layouts = CreateLayouts(frame, id + 1);
        return new SlideMaster
        {
            Id = id,
            Theme = design.Theme,
            ColorMap = design.IsDark ? new ColorMap("dk1", "lt1", "dk2", "lt2") : ColorMap.Default,
            Shapes = shapes,
            Background = background,
            TextStyles = MasterTextStyles(),
            Layouts = layouts,
        };
    }

    /// <summary>The master's title, body and other text styles (headings in the major font, body text with bullets per level).</summary>
    public static MasterTextStyles MasterTextStyles()
    {
        var title = new ListStyle(null, [
            new ParagraphProperties(
                Alignment: TextAlignment.Left,
                DefaultTabSize: 914400,
                RightToLeft: false,
                LineSpacing: TextSpacing.Percent(90000),
                SpaceBefore: TextSpacing.Percent(0),
                Bullet: NoBullet.Instance,
                DefaultRun: new RunProperties(Size: 4400, Kerning: 1200, Fill: new SolidFill(ColorSpec.Scheme("tx1")), Latin: new TextFont("+mj-lt"), EastAsian: new TextFont("+mj-ea"), ComplexScript: new TextFont("+mj-cs"))),
        ]);
        string[] bullets = ["•", "–", "•", "–", "»", "•", "•", "•", "•"];
        int[] sizes = [2800, 2400, 2000, 1800, 1800, 1800, 1800, 1800, 1800];
        var bodyLevels = Enumerable.Range(0, 9).Select(level => (ParagraphProperties?)new ParagraphProperties(
            LeftMargin: 228600 + (level * 457200),
            Indent: -228600,
            Alignment: TextAlignment.Left,
            DefaultTabSize: 914400,
            RightToLeft: false,
            LineSpacing: TextSpacing.Percent(90000),
            SpaceBefore: TextSpacing.Points(level == 0 ? 10 : 5),
            BulletFont: new TextFont("Arial", PitchFamily: "34", CharacterSet: "0"),
            Bullet: new CharacterBullet(bullets[level]),
            DefaultRun: new RunProperties(Size: sizes[level], Kerning: 1200, Fill: new SolidFill(ColorSpec.Scheme("tx1")), Latin: new TextFont("+mn-lt"), EastAsian: new TextFont("+mn-ea"), ComplexScript: new TextFont("+mn-cs"))))
            .ToList();
        var body = new ListStyle(null, bodyLevels);
        var otherLevels = Enumerable.Range(0, 9).Select(level => (ParagraphProperties?)new ParagraphProperties(
            LeftMargin: level * 457200,
            Alignment: TextAlignment.Left,
            DefaultTabSize: 914400,
            RightToLeft: false,
            DefaultRun: new RunProperties(Size: 1800, Kerning: 1200, Fill: new SolidFill(ColorSpec.Scheme("tx1")), Latin: new TextFont("+mn-lt"), EastAsian: new TextFont("+mn-ea"), ComplexScript: new TextFont("+mn-cs"))))
            .ToList();
        var other = new ListStyle(new ParagraphProperties(DefaultRun: new RunProperties(Language: "de-DE")), otherLevels);
        return new MasterTextStyles(title, body, other);
    }

    private static ValueList<SlideLayout> CreateLayouts(Frame frame, uint firstId)
    {
        var id = firstId;
        var layouts = new List<SlideLayout>();

        SlideLayout Layout(string type, string name, params SlideElement[] shapes) => new()
        {
            Id = id++,
            Type = type,
            Name = name,
            Shapes = [.. shapes, .. Footers(10)],
        };

        var noBullets = new ListStyle(null, [new ParagraphProperties(LeftMargin: 0, Indent: 0, Bullet: NoBullet.Instance, Alignment: TextAlignment.Center, DefaultRun: new RunProperties(Size: 2400))]);
        var title = new PlaceholderInfo(PlaceholderType.Title);
        layouts.Add(Layout("title", "Titelfolie",
            Placeholder(2, "Titel 1", new PlaceholderInfo(PlaceholderType.CenteredTitle), frame.Rect(0.125, 0.227, 0.75, 0.33),
                new BodyProperties(Anchor: TextAnchor.Bottom), "Titel durch Klicken hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(Alignment: TextAlignment.Center, DefaultRun: new RunProperties(Size: 6000))])),
            Placeholder(3, "Untertitel 2", new PlaceholderInfo(PlaceholderType.Subtitle, 1), frame.Rect(0.125, 0.587, 0.75, 0.2),
                null, "Untertitel durch Klicken hinzufügen", listStyle: noBullets)));
        layouts.Add(Layout("obj", "Titel und Inhalt",
            Placeholder(2, "Titel 1", title, null, null, "Titel durch Klicken hinzufügen"),
            Placeholder(3, "Inhaltsplatzhalter 2", new PlaceholderInfo(PlaceholderType.Object, 1), null, null, "Text durch Klicken hinzufügen")));
        layouts.Add(Layout("secHead", "Abschnittsüberschrift",
            Placeholder(2, "Titel 1", title, frame.Rect(0.07, 0.3, 0.86, 0.32), new BodyProperties(Anchor: TextAnchor.Bottom), "Titel durch Klicken hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(DefaultRun: new RunProperties(Size: 5400))])),
            Placeholder(3, "Textplatzhalter 2", new PlaceholderInfo(PlaceholderType.Body, 1), frame.Rect(0.07, 0.64, 0.86, 0.16), null, "Text durch Klicken hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(LeftMargin: 0, Indent: 0, Bullet: NoBullet.Instance, DefaultRun: new RunProperties(Size: 2400, Fill: new SolidFill(ColorSpec.Scheme("tx1").Tint(82000))))]))));
        layouts.Add(Layout("twoObj", "Zwei Inhalte",
            Placeholder(2, "Titel 1", title, null, null, "Titel durch Klicken hinzufügen"),
            Placeholder(3, "Inhaltsplatzhalter 2", new PlaceholderInfo(PlaceholderType.Object, 1, Size: "half"), frame.Rect(0.05, 0.267, 0.44, 0.635), null, "Text durch Klicken hinzufügen"),
            Placeholder(4, "Inhaltsplatzhalter 3", new PlaceholderInfo(PlaceholderType.Object, 2, Size: "half"), frame.Rect(0.51, 0.267, 0.44, 0.635), null, "Text durch Klicken hinzufügen")));
        var heading = new ListStyle(null, [new ParagraphProperties(LeftMargin: 0, Indent: 0, Bullet: NoBullet.Instance, DefaultRun: new RunProperties(Size: 2400, Bold: true))]);
        layouts.Add(Layout("twoTxTwoObj", "Vergleich",
            Placeholder(2, "Titel 1", title, null, null, "Titel durch Klicken hinzufügen"),
            Placeholder(3, "Textplatzhalter 2", new PlaceholderInfo(PlaceholderType.Body, 1), frame.Rect(0.05, 0.267, 0.44, 0.09), new BodyProperties(Anchor: TextAnchor.Bottom), "Text durch Klicken hinzufügen", listStyle: heading),
            Placeholder(4, "Inhaltsplatzhalter 3", new PlaceholderInfo(PlaceholderType.Object, 2, Size: "half"), frame.Rect(0.05, 0.37, 0.44, 0.532), null, "Text durch Klicken hinzufügen"),
            Placeholder(5, "Textplatzhalter 4", new PlaceholderInfo(PlaceholderType.Body, 3), frame.Rect(0.51, 0.267, 0.44, 0.09), new BodyProperties(Anchor: TextAnchor.Bottom), "Text durch Klicken hinzufügen", listStyle: heading),
            Placeholder(6, "Inhaltsplatzhalter 5", new PlaceholderInfo(PlaceholderType.Object, 4, Size: "half"), frame.Rect(0.51, 0.37, 0.44, 0.532), null, "Text durch Klicken hinzufügen")));
        layouts.Add(Layout("titleOnly", "Nur Titel",
            Placeholder(2, "Titel 1", title, null, null, "Titel durch Klicken hinzufügen")));
        layouts.Add(Layout("blank", "Leer"));
        layouts.Add(Layout("picTx", "Bild mit Überschrift",
            Placeholder(2, "Titel 1", title, frame.Rect(0.05, 0.1, 0.33, 0.2), new BodyProperties(Anchor: TextAnchor.Bottom), "Titel durch Klicken hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(DefaultRun: new RunProperties(Size: 3200))])),
            Placeholder(3, "Bildplatzhalter 2", new PlaceholderInfo(PlaceholderType.Picture, 1), frame.Rect(0.42, 0.1, 0.53, 0.8), null, "Bild durch Klicken auf Symbol hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(LeftMargin: 0, Indent: 0, Bullet: NoBullet.Instance, DefaultRun: new RunProperties(Size: 3200))])),
            Placeholder(4, "Textplatzhalter 3", new PlaceholderInfo(PlaceholderType.Body, 2, Size: "half"), frame.Rect(0.05, 0.32, 0.33, 0.58), null, "Text durch Klicken hinzufügen",
                listStyle: new ListStyle(null, [new ParagraphProperties(LeftMargin: 0, Indent: 0, Bullet: NoBullet.Instance, DefaultRun: new RunProperties(Size: 1600))]))));
        return layouts;
    }

    private static SlideElement[] Footers(int firstId) =>
    [
        Placeholder(firstId, "Datumsplatzhalter", new PlaceholderInfo(PlaceholderType.DateAndTime, 10, Size: "half"), null, null, ""),
        Placeholder(firstId + 1, "Fußzeilenplatzhalter", new PlaceholderInfo(PlaceholderType.Footer, 11, Size: "quarter"), null, null, ""),
        Placeholder(firstId + 2, "Foliennummernplatzhalter", new PlaceholderInfo(PlaceholderType.SlideNumber, 12, Size: "quarter"), null, null, "‹Nr.›", slideNumber: true),
    ];

    private static ListStyle Aligned(TextAlignment alignment, RunProperties run) =>
        new(null, [new ParagraphProperties(Alignment: alignment, DefaultRun: run)]);

    private static AutoShape Placeholder(
        int id,
        string name,
        PlaceholderInfo placeholder,
        Transform2D? transform,
        BodyProperties? body,
        string prompt,
        ListStyle? listStyle = null,
        bool levels = false,
        bool slideNumber = false)
    {
        var paragraphs = new List<TextParagraph>();
        var lines = prompt.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var paragraphProperties = levels && index > 0 ? new ParagraphProperties(Level: index) : ParagraphProperties.Empty;
            if (slideNumber)
            {
                paragraphs.Add(new TextParagraph(paragraphProperties, [new TextField("{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}", "slidenum", lines[index], new RunProperties(Language: "de-DE"))]));
                continue;
            }

            paragraphs.Add(TextParagraph.Plain(lines[index], new RunProperties(Language: "de-DE"), paragraphProperties));
        }

        return new AutoShape
        {
            Id = id,
            Name = name,
            Placeholder = placeholder with { HasCustomPrompt = prompt.Length > 0 && !slideNumber && placeholder.Type != PlaceholderType.Body && placeholder.Type != PlaceholderType.Title },
            Transform = transform,
            Text = new TextBody(body ?? BodyProperties.Empty, listStyle ?? ListStyle.Empty, paragraphs),
        };
    }

    /// <summary>Positions as fractions of the slide.</summary>
    private sealed class Frame(SlideSize size)
    {
        public Transform2D Rect(double x, double y, double width, double height) =>
            new((long)Math.Round(size.Width * x), (long)Math.Round(size.Height * y), (long)Math.Round(size.Width * width), (long)Math.Round(size.Height * height));
    }
}

namespace ThixShow.Core.Model;

/// <summary>The slide size: width and height in EMU and the p:sldSz type ("screen16x9", "screen4x3", "custom" …).</summary>
public sealed record SlideSize(long Width, long Height, string? Type = null)
{
    /// <summary>16:9 widescreen, 33.867 × 19.05 cm (PowerPoint's default since 2013).</summary>
    public static SlideSize Widescreen { get; } = new(12192000, 6858000);

    /// <summary>4:3, 25.4 × 19.05 cm.</summary>
    public static SlideSize Standard { get; } = new(9144000, 6858000, "screen4x3");

    /// <summary>16:9 as PowerPoint 2010 made it, 25.4 × 14.288 cm.</summary>
    public static SlideSize OnScreen16x9 { get; } = new(9144000, 5143500, "screen16x9");

    public double WidthPoints => Units.EmuToPoints(Width);

    public double HeightPoints => Units.EmuToPoints(Height);

    public double AspectRatio => Height == 0 ? 1 : Width / (double)Height;
}

/// <summary>
/// What an opened file had that ThixShow keeps but cannot show or edit fully (plan §8: never lose content silently): the UI
/// warns with <see cref="Message"/>; everything listed is written back on save.
/// </summary>
public sealed record UnsupportedContent(string Kind, int Count)
{
    /// <summary>"2 × SmartArt-Grafik (wird erhalten, als Platzhalter angezeigt)" – German, for the warning.</summary>
    public string Message => $"{Count} × {Label(Kind)}";

    public static string Label(string kind) => kind switch
    {
        "chart" => "Diagramm (wird erhalten, als Platzhalter angezeigt)",
        "diagram" => "SmartArt-Grafik (wird erhalten, als Platzhalter angezeigt)",
        "ole" => "Eingebettetes Objekt (wird erhalten, als Bild angezeigt)",
        "media" => "Video oder Audio (wird erhalten, das Vorschaubild wird angezeigt)",
        "comments" => "Kommentare (werden erhalten, nicht angezeigt)",
        "macros" => "Makros (werden erhalten, aber nie ausgeführt)",
        "customXml" => "Benutzerdefinierte XML-Daten (werden erhalten)",
        "fonts" => "Eingebettete Schriftarten (werden erhalten, nicht verwendet)",
        "ink" => "Freihandeingabe (wird erhalten, nicht angezeigt)",
        "math" => "Formel (wird erhalten, als Platzhalter angezeigt)",
        "interactive" => "Interaktive Animation per Auslöser (wird erhalten, in der Bildschirmpräsentation nicht abgespielt)",
        "animation" => "Animationseffekt, den ThixShow nicht abspielt (wird erhalten, als Verblassen gezeigt)",
        "transition" => "Folienübergang, den ThixShow nicht zeigt (wird erhalten, als Verblassen gezeigt)",
        "geometry" => "Unbekannte Form (wird erhalten, als Rechteck angezeigt)",
        "picture" => "Bildformat, das ThixShow nicht anzeigt (EMF/WMF/SVG/TIFF; wird erhalten)",
        "element" => "Unbekanntes Element (wird erhalten, nicht angezeigt)",
        _ => "Unbekannter Inhalt (wird erhalten, nicht angezeigt)",
    };
}

/// <summary>
/// The original package of an opened presentation. The writer starts from a copy of it and replaces only what ThixShow owns
/// (presentation.xml's slide lists and sizes, the slides, layouts, masters, themes, notes it changed), so SmartArt, charts, media,
/// OLE objects, comments, custom XML, VBA, embedded fonts and every relationship of kept XML survive.
/// </summary>
public sealed class PreservedPackage(byte[] bytes)
{
    private readonly byte[] _bytes = bytes;

    public MemoryStream Open()
    {
        var stream = new MemoryStream();
        stream.Write(_bytes);
        stream.Position = 0;
        return stream;
    }

    public int Size => _bytes.Length;
}

/// <summary>
/// A presentation: slide size, default text style, masters (each with its theme and layouts), slides in order, sections, the
/// notes master (kept), document properties. Immutable – the editing session swaps whole instances, so undo is exact and an
/// unchanged slide keeps its instance (views redraw only what changed; the writer leaves its part alone).
/// </summary>
public sealed record Presentation
{
    public SlideSize SlideSize { get; init; } = SlideSize.Widescreen;

    /// <summary>The notes page size (p:notesSz), EMU; portrait A4-ish by default.</summary>
    public EmuSize NotesSize { get; init; } = new(6858000, 9144000);

    public ListStyle DefaultTextStyle { get; init; } = ListStyle.Empty;

    public ValueList<SlideMaster> Masters { get; init; }

    public ValueList<Slide> Slides { get; init; }

    public ValueList<SlideSection> Sections { get; init; }

    /// <summary>p:notesMaster as read (kept; notes pages are drawn with a layout of ThixShow's).</summary>
    public string? NotesMasterPartUri { get; init; }

    /// <summary>Table styles of tableStyles.xml (the default style id first).</summary>
    public ValueList<TableStyle> TableStyles { get; init; }

    public string? DefaultTableStyleId { get; init; }

    public string? Title { get; init; }

    public string? Author { get; init; }

    /// <summary>The pptm flavour (a VBA project inside; macros never run).</summary>
    public bool IsMacroEnabled { get; init; }

    /// <summary>presentation.xml as read (p:extLst, p:custShowLst, p:photoAlbum, p:kinsoku, p:modifyVerifier …), written around the owned parts.</summary>
    public XmlFragment? Source { get; init; }

    public PreservedPackage? Package { get; init; }

    public ValueList<UnsupportedContent> UnsupportedContent { get; init; }

    /// <summary>The slides as they were read, by id (not part of equality: the writer leaves a slide's part alone while it is unchanged).</summary>
    public IReadOnlyDictionary<uint, Slide>? OriginalSlides { get; init; }

    public int SlideCount => Slides.Count;

    public Slide? FindSlide(uint id) => Slides.FirstOrDefault(slide => slide.Id == id);

    public int IndexOf(uint slideId) => Slides.FindIndex(slide => slide.Id == slideId);

    public SlideLayout? FindLayout(uint layoutId) =>
        Masters.SelectMany(master => master.Layouts).FirstOrDefault(layout => layout.Id == layoutId);

    public SlideMaster? MasterOf(uint layoutId) =>
        Masters.FirstOrDefault(master => master.Layouts.Any(layout => layout.Id == layoutId));

    /// <summary>The layout of <paramref name="slide"/> (the first layout when its id is unknown).</summary>
    public SlideLayout LayoutOf(Slide slide) => FindLayout(slide.LayoutId) ?? Masters[0].Layouts[0];

    public SlideMaster MasterOf(Slide slide) => MasterOf(slide.LayoutId) ?? Masters[0];

    /// <summary>The section a slide is in (null when the presentation has no sections).</summary>
    public SlideSection? SectionOf(uint slideId) => Sections.FirstOrDefault(section => section.SlideIds.Contains(slideId));

    /// <summary>The next free slide id (≥ 256).</summary>
    public uint NextSlideId => Slides.Select(slide => slide.Id).DefaultIfEmpty(255u).Max() + 1;

    /// <summary>The next free master/layout id (≥ 2147483648).</summary>
    public uint NextMasterOrLayoutId => Masters
        .SelectMany(master => master.Layouts.Select(layout => layout.Id).Append(master.Id))
        .DefaultIfEmpty(2147483647u)
        .Max() + 1;

    public bool Equals(Presentation? other) =>
        other is not null &&
        SlideSize == other.SlideSize &&
        NotesSize == other.NotesSize &&
        DefaultTextStyle == other.DefaultTextStyle &&
        Masters == other.Masters &&
        Slides == other.Slides &&
        Sections == other.Sections &&
        NotesMasterPartUri == other.NotesMasterPartUri &&
        TableStyles == other.TableStyles &&
        DefaultTableStyleId == other.DefaultTableStyleId &&
        Title == other.Title &&
        Author == other.Author &&
        IsMacroEnabled == other.IsMacroEnabled &&
        Source == other.Source;

    public override int GetHashCode() => HashCode.Combine(SlideSize, Slides.Count, Masters.Count);
}

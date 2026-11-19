namespace ThixShow.Core.Model;

/// <summary>
/// p:bg: a background fill (p:bgPr) or a reference to the theme's background styles (p:bgRef idx 1001+ with a colour).
/// </summary>
public sealed record Background(Fill? Fill = null, StyleReference? Reference = null, bool ShadeToTitle = false, ShapeEffects? Effects = null);

/// <summary>
/// The text styles of a master (p:txStyles): titles, body placeholders, and everything else (other placeholders and shapes).
/// </summary>
public sealed record MasterTextStyles(ListStyle Title, ListStyle Body, ListStyle Other)
{
    public static MasterTextStyles Empty { get; } = new(ListStyle.Empty, ListStyle.Empty, ListStyle.Empty);
}

/// <summary>Which footer placeholders a master/layout/slide shows (p:hf: date, footer, header, slide number).</summary>
public sealed record HeaderFooterFlags(bool Date = true, bool Footer = true, bool Header = true, bool SlideNumber = true);

/// <summary>
/// What every kind of slide has: the shape tree, a background, a name (p:cSld name) and the XML of the part as read
/// (<see cref="Source"/>: extensions, custom data, p:extLst – written back around the owned elements). <see cref="PartUri"/> names
/// the part it was read from (null for new ones); <see cref="References"/> the parts of relationship ids in kept XML.
/// </summary>
public abstract record SlideBase
{
    public ValueList<SlideElement> Shapes { get; init; }

    public Background? Background { get; init; }

    public string? Name { get; init; }

    public XmlFragment? Source { get; init; }

    public string? PartUri { get; init; }

    public ValueList<PartReference> References { get; init; }

    /// <summary>The shape with <paramref name="id"/> anywhere in the tree (inside groups too), or null.</summary>
    public SlideElement? FindShape(int id) => ShapeTree.Find(Shapes, id);

    /// <summary>The highest shape id in the tree (new shapes take the next one).</summary>
    public int MaxShapeId => ShapeTree.All(Shapes).Select(shape => shape.Id).DefaultIfEmpty(1).Max();
}

/// <summary>
/// p:sldMaster: the theme, the colour map, the text styles, the master's own shapes and placeholders, and its layouts.
/// <see cref="Id"/> is the p:sldMasterId id (≥ 2147483648, unique with the layout ids).
/// </summary>
public sealed record SlideMaster : SlideBase
{
    public uint Id { get; init; }

    public required Theme Theme { get; init; }

    public ColorMap ColorMap { get; init; } = ColorMap.Default;

    public MasterTextStyles TextStyles { get; init; } = MasterTextStyles.Empty;

    public ValueList<SlideLayout> Layouts { get; init; }

    public HeaderFooterFlags? HeaderFooter { get; init; }

    /// <summary>The theme part's uri as read.</summary>
    public string? ThemePartUri { get; init; }

    public bool Preserve { get; init; }

    public SlideLayout? FindLayout(uint id) => Layouts.FirstOrDefault(layout => layout.Id == id);
}

/// <summary>
/// p:sldLayout: a layout of a master (its type "title", "obj", "twoObj", "secHead", "titleOnly", "blank" …, its name and
/// placeholders). <see cref="Id"/> is its p:sldLayoutId id, unique in the presentation.
/// </summary>
public sealed record SlideLayout : SlideBase
{
    public uint Id { get; init; }

    public string Type { get; init; } = "cust";

    public string? MatchingName { get; init; }

    /// <summary>False: the master's own shapes (not its placeholders) are not drawn under this layout's slides.</summary>
    public bool ShowMasterShapes { get; init; } = true;

    public bool ShowMasterPlaceholderAnimations { get; init; }

    public bool Preserve { get; init; }

    public bool UserDrawn { get; init; }

    public ColorMap? ColorMapOverride { get; init; }

    public HeaderFooterFlags? HeaderFooter { get; init; }
}

/// <summary>
/// p:notes: the notes page of a slide (its slide image and notes text placeholders). <see cref="Text"/> is the body placeholder's
/// text.
/// </summary>
public sealed record NotesSlide : SlideBase
{
    public ColorMap? ColorMapOverride { get; init; }

    /// <summary>The notes text (paragraphs separated by "\n"), "" when there is none.</summary>
    public string Text => NotesBody?.Text?.PlainText ?? "";

    /// <summary>The body placeholder that holds the notes.</summary>
    public AutoShape? NotesBody =>
        ShapeTree.All(Shapes).OfType<AutoShape>().FirstOrDefault(shape => shape.Placeholder?.Type == PlaceholderType.Body);
}

/// <summary>
/// p:sld: a slide – its layout (by layout id), shapes, background, transition, timing, notes. <see cref="Id"/> is its p:sldId id
/// (≥ 256, unique in the presentation, stable across edits: selections and links name slides by it).
/// </summary>
public sealed record Slide : SlideBase
{
    public uint Id { get; init; }

    public uint LayoutId { get; init; }

    public bool Hidden { get; init; }

    public bool ShowMasterShapes { get; init; } = true;

    public ColorMap? ColorMapOverride { get; init; }

    public SlideTransition? Transition { get; init; }

    public SlideTiming? Timing { get; init; }

    public NotesSlide? Notes { get; init; }

    public string NotesText => Notes?.Text ?? "";
}

/// <summary>p14:section: a named group of slides (by slide id).</summary>
public sealed record SlideSection(string Name, string Id, ValueList<uint> SlideIds);

/// <summary>Helpers for the shape tree (groups nest).</summary>
public static class ShapeTree
{
    /// <summary>Every element in drawing order, groups before their children.</summary>
    public static IEnumerable<SlideElement> All(IEnumerable<SlideElement> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            if (shape is GroupShape group)
            {
                foreach (var child in All(group.Children))
                {
                    yield return child;
                }
            }
        }
    }

    public static SlideElement? Find(IEnumerable<SlideElement> shapes, int id) => All(shapes).FirstOrDefault(shape => shape.Id == id);

    /// <summary>The ids of the groups from the top down to <paramref name="id"/> (empty for a top-level shape, null when not found).</summary>
    public static IReadOnlyList<int>? PathTo(IEnumerable<SlideElement> shapes, int id)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == id)
            {
                return [];
            }

            if (shape is GroupShape group && PathTo(group.Children, id) is { } inner)
            {
                return [group.Id, .. inner];
            }
        }

        return null;
    }

    /// <summary>The tree with the element of <paramref name="id"/> replaced by <paramref name="change"/>'s result (null removes it).</summary>
    public static ValueList<SlideElement> Replace(ValueList<SlideElement> shapes, int id, Func<SlideElement, SlideElement?> change)
    {
        for (var index = 0; index < shapes.Count; index++)
        {
            var shape = shapes[index];
            if (shape.Id == id)
            {
                return change(shape) is { } changed ? shapes.SetItem(index, changed) : shapes.RemoveAt(index);
            }

            if (shape is GroupShape group)
            {
                var children = Replace(group.Children, id, change);
                if (!children.IsSameInstance(group.Children))
                {
                    return shapes.SetItem(index, group with { Children = children });
                }
            }
        }

        return shapes;
    }
}

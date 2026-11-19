using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Formatting;

/// <summary>Which kind of slide a shape is on (the inheritance stops at the kind: a master shape inherits nothing).</summary>
public enum SlideKind
{
    Slide,
    Layout,
    Master,
    Notes,
}

/// <summary>
/// Everything a shape on one slide resolves against: the presentation, the slide (or layout / master), its layout and master,
/// the theme and the colour map in effect. Built once per slide render (<see cref="For(Presentation, Slide)"/> …).
/// </summary>
public sealed class SlideContext
{
    private SlideContext(Presentation presentation, SlideBase slide, SlideKind kind, SlideLayout? layout, SlideMaster master)
    {
        Presentation = presentation;
        Slide = slide;
        Kind = kind;
        Layout = layout;
        Master = master;
        var map = master.ColorMap;
        if (kind == SlideKind.Layout && layout?.ColorMapOverride is { } layoutMap)
        {
            map = layoutMap;
        }
        else if (kind == SlideKind.Slide)
        {
            map = (slide as Slide)?.ColorMapOverride ?? layout?.ColorMapOverride ?? map;
        }
        else if (kind == SlideKind.Notes)
        {
            map = (slide as NotesSlide)?.ColorMapOverride ?? map;
        }

        Colors = new ColorContext(master.Theme.Colors, map);
    }

    public Presentation Presentation { get; }

    public SlideBase Slide { get; }

    public SlideKind Kind { get; }

    public SlideLayout? Layout { get; }

    public SlideMaster Master { get; }

    public Theme Theme => Master.Theme;

    public ColorContext Colors { get; }

    public static SlideContext For(Presentation presentation, Slide slide) =>
        new(presentation, slide, SlideKind.Slide, presentation.LayoutOf(slide), presentation.MasterOf(slide));

    public static SlideContext ForLayout(Presentation presentation, SlideMaster master, SlideLayout layout) =>
        new(presentation, layout, SlideKind.Layout, layout, master);

    public static SlideContext ForMaster(Presentation presentation, SlideMaster master) =>
        new(presentation, master, SlideKind.Master, null, master);

    public static SlideContext ForNotes(Presentation presentation, Slide slide, NotesSlide notes) =>
        new(presentation, notes, SlideKind.Notes, null, presentation.MasterOf(slide));

    /// <summary>The placeholder of the layout this shape's placeholder inherits from (slides only).</summary>
    public SlideElement? LayoutPlaceholder(PlaceholderInfo placeholder) =>
        Kind == SlideKind.Slide && Layout is not null ? PlaceholderMatching.InLayout(Layout.Shapes, placeholder) : null;

    /// <summary>The placeholder of the master this shape's placeholder (or its layout's) inherits from.</summary>
    public SlideElement? MasterPlaceholder(PlaceholderInfo placeholder) =>
        Kind is SlideKind.Slide or SlideKind.Layout ? PlaceholderMatching.InMaster(Master.Shapes, placeholder) : null;
}

/// <summary>How a placeholder finds the one it inherits from (ECMA-376 Part 1, 19.3.1.36).</summary>
public static class PlaceholderMatching
{
    /// <summary>
    /// On the layout: the placeholder with the same index (no index = 0); else the first of the same type (title kinds match each
    /// other, body-like kinds the body).
    /// </summary>
    public static SlideElement? InLayout(IEnumerable<SlideElement> shapes, PlaceholderInfo placeholder)
    {
        var candidates = ShapeTree.All(shapes).Where(shape => shape.Placeholder is not null).ToList();
        var index = placeholder.Index ?? 0;
        var byIndex = candidates.FirstOrDefault(shape => (shape.Placeholder!.Index ?? 0) == index && Compatible(shape.Placeholder, placeholder));
        if (byIndex is not null)
        {
            return byIndex;
        }

        return candidates.FirstOrDefault(shape => shape.Placeholder!.Type == placeholder.Type)
               ?? candidates.FirstOrDefault(shape => shape.Placeholder!.MasterType == placeholder.MasterType && !placeholder.IsFooterKind);
    }

    /// <summary>On the master: the placeholder of the same master type (title, body, date, footer, slide number …).</summary>
    public static SlideElement? InMaster(IEnumerable<SlideElement> shapes, PlaceholderInfo placeholder) =>
        ShapeTree.All(shapes).FirstOrDefault(shape => shape.Placeholder is { } master && master.MasterType == placeholder.MasterType);

    private static bool Compatible(PlaceholderInfo candidate, PlaceholderInfo placeholder) =>
        candidate.Type == placeholder.Type ||
        candidate.MasterType == placeholder.MasterType ||
        (placeholder.Type == PlaceholderType.Object && !candidate.IsTitle && !candidate.IsFooterKind) ||
        (candidate.Type == PlaceholderType.Object && !placeholder.IsTitle && !placeholder.IsFooterKind);
}

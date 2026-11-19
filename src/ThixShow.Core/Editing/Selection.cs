using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>What is selected: nothing, slides (in the slide sorter / thumbnails), shapes on the current slide, or text inside a shape.</summary>
public enum SelectionKind
{
    None,
    Slides,
    Shapes,
    Text,
}

/// <summary>A position in a text body: paragraph index and offset in its text (a line break is one character).</summary>
public readonly record struct TextPosition(int Paragraph, int Offset) : IComparable<TextPosition>
{
    public int CompareTo(TextPosition other) => Paragraph != other.Paragraph ? Paragraph.CompareTo(other.Paragraph) : Offset.CompareTo(other.Offset);

    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;

    public static TextPosition Min(TextPosition first, TextPosition second) => first <= second ? first : second;

    public static TextPosition Max(TextPosition first, TextPosition second) => first >= second ? first : second;
}

/// <summary>The text being edited: a shape's body, or a table cell's (<see cref="Row"/>/<see cref="Column"/>).</summary>
public sealed record TextTarget(int ShapeId, int? Row = null, int? Column = null)
{
    public bool IsTableCell => Row is not null && Column is not null;
}

/// <summary>A text selection (caret when collapsed) inside one text body.</summary>
public sealed record TextSelection(TextTarget Target, TextPosition Anchor, TextPosition Active)
{
    public TextPosition Start => TextPosition.Min(Anchor, Active);

    public TextPosition End => TextPosition.Max(Anchor, Active);

    public bool IsCollapsed => Anchor == Active;

    public static TextSelection Caret(TextTarget target, TextPosition position) => new(target, position, position);
}

/// <summary>
/// The selection of an editing session: the slide shown in the editor (<see cref="CurrentSlide"/>), the slides selected in the
/// sorter, the selected shapes of the current slide (an entered group's children when <see cref="EnteredGroup"/> is set) and the
/// text selection when editing text.
/// </summary>
public sealed record ShowSelection(
    uint? CurrentSlide,
    ValueList<uint> Slides = default,
    ValueList<int> Shapes = default,
    int? EnteredGroup = null,
    TextSelection? Text = null)
{
    public static ShowSelection Empty { get; } = new((uint?)null);

    public SelectionKind Kind => Text is not null ? SelectionKind.Text
        : !Shapes.IsEmpty ? SelectionKind.Shapes
        : !Slides.IsEmpty ? SelectionKind.Slides
        : SelectionKind.None;

    public static ShowSelection OnSlide(uint slideId) => new(slideId, [slideId]);
}

namespace ThixWrite.Core.Model;

/// <summary>
/// A shape or text box ThixWrite edits (Einfügen › Formen / Textfeld, Formformat): one <see cref="DrawingShape"/> (preset geometry, fill,
/// outline, shadow, text as a story) of <see cref="Width"/> × <see cref="Height"/> points, in the line or floating
/// (<see cref="Anchor"/>). Written as a w:drawing with a wps:wsp. A shape read from a file keeps its XML (<see cref="SourceXml"/>) and
/// is written back exactly as read while it is unchanged (<see cref="ReadAs"/>); a shape Word wrote inside mc:AlternateContent becomes
/// one of these only when it is edited.
/// </summary>
public sealed record ShapeInline(DrawingShape Shape, double Width, double Height, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    /// <summary>Where a floating shape sits and how text flows around it; null in the line.</summary>
    public DrawingAnchor? Anchor { get; init; }

    /// <summary>The w:drawing as read, or null.</summary>
    public string? SourceXml { get; init; }

    /// <summary>The shape as it was read (with <see cref="SourceXml"/>): while it is equal, the XML is written back.</summary>
    public ShapeInline? ReadAs { get; init; }

    /// <summary>A text box (wps:cNvSpPr txBox="1").</summary>
    public bool IsTextBox { get; init; }

    /// <summary>What the layout draws: the shape filling the frame.</summary>
    public DrawingGraphic Graphic => new(Width, Height, Anchor, [Shape with { X = 0, Y = 0, Width = Width, Height = Height }]);

    /// <summary>True while nothing differs from how the shape was read.</summary>
    public bool IsUnchanged => ReadAs is { } read && Shape == read.Shape && Math.Abs(Width - read.Width) < 0.01 && Math.Abs(Height - read.Height) < 0.01
                               && Anchor == read.Anchor && IsTextBox == read.IsTextBox && Properties == read.Properties;

    public bool Equals(ShapeInline? other) =>
        other is not null && Shape == other.Shape && Math.Abs(Width - other.Width) < 0.01 && Math.Abs(Height - other.Height) < 0.01
        && Anchor == other.Anchor && IsTextBox == other.IsTextBox && Properties == other.Properties && Link == other.Link && Revision == other.Revision;

    public override int GetHashCode() => HashCode.Combine(Shape.Preset, Math.Round(Width), Math.Round(Height), Anchor);
}

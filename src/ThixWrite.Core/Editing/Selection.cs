using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// Which line a caret belongs to when its position is both the end of one line and the start of the next (a soft wrap):
/// <see cref="Downstream"/> (default) = the start of the next line, <see cref="Upstream"/> = the end of the line before
/// (after End, or a click behind the end of a wrapped line).
/// </summary>
public enum CaretAffinity
{
    Downstream,
    Upstream,
}

/// <summary>
/// The selection: <see cref="Anchor"/> stays where the selection began, <see cref="Active"/> moves (the caret). Collapsed when
/// both are equal. Immutable.
/// </summary>
public sealed record Selection(DocumentPosition Anchor, DocumentPosition Active, CaretAffinity Affinity = CaretAffinity.Downstream)
{
    public static Selection Caret(DocumentPosition position, CaretAffinity affinity = CaretAffinity.Downstream) => new(position, position, affinity);

    public DocumentPosition Start => DocumentPosition.Min(Anchor, Active);

    public DocumentPosition End => DocumentPosition.Max(Anchor, Active);

    public bool IsCollapsed => Anchor == Active;

    /// <summary>True when the caret (active end) is before the anchor (selected backwards).</summary>
    public bool IsBackward => Active < Anchor;

    public DocumentRange Range => new(Anchor, Active);

    public override string ToString() => IsCollapsed ? $"Caret {Active}" : $"Selection {Anchor} → {Active}";
}

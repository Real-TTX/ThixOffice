namespace ThixWrite.Core.Model;

/// <summary>
/// A place in the document: before the character <see cref="Offset"/> of block <see cref="Block"/> (0 = start of the paragraph,
/// Length = before its paragraph mark). Positions are values; to keep one valid across edits, map it through the edits
/// (Editing.PositionMap) – the editing session does that for the selection and for <c>TrackedPosition</c>s.
/// </summary>
public readonly record struct DocumentPosition(int Block, int Offset) : IComparable<DocumentPosition>
{
    public static readonly DocumentPosition Start = new(0, 0);

    public int CompareTo(DocumentPosition other) => Block != other.Block ? Block.CompareTo(other.Block) : Offset.CompareTo(other.Offset);

    public static bool operator <(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) < 0;

    public static bool operator >(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) > 0;

    public static bool operator <=(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) <= 0;

    public static bool operator >=(DocumentPosition left, DocumentPosition right) => left.CompareTo(right) >= 0;

    public static DocumentPosition Min(DocumentPosition first, DocumentPosition second) => first <= second ? first : second;

    public static DocumentPosition Max(DocumentPosition first, DocumentPosition second) => first >= second ? first : second;

    public override string ToString() => $"({Block}:{Offset})";
}

/// <summary>A range between two positions, <see cref="Start"/> ≤ <see cref="End"/> (the constructor orders them).</summary>
public readonly record struct DocumentRange
{
    public DocumentRange(DocumentPosition start, DocumentPosition end)
    {
        Start = DocumentPosition.Min(start, end);
        End = DocumentPosition.Max(start, end);
    }

    public DocumentPosition Start { get; }

    public DocumentPosition End { get; }

    public bool IsEmpty => Start == End;

    /// <summary>True when the range spans more than one block.</summary>
    public bool IsMultiBlock => Start.Block != End.Block;

    public bool Contains(DocumentPosition position) => position >= Start && position <= End;

    public static DocumentRange At(DocumentPosition position) => new(position, position);

    public override string ToString() => $"[{Start}–{End}]";
}

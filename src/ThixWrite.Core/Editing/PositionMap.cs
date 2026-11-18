using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Where a position at the edge of an insertion goes: before it (<see cref="Left"/>) or after it (<see cref="Right"/>).</summary>
public enum Bias
{
    Left,
    Right,
}

/// <summary>
/// Maps positions from before an edit to after it, so that positions survive edits (selection, find results, bookmarks of
/// the views). Every step of the editing engine has one.
/// </summary>
public abstract class PositionMap
{
    public static readonly PositionMap Identity = new IdentityMap();

    public abstract DocumentPosition Map(DocumentPosition position, Bias bias = Bias.Right);

    /// <summary>Maps a range so that content inserted at its edges stays outside it.</summary>
    public DocumentRange Map(DocumentRange range)
    {
        var start = Map(range.Start, Bias.Right);
        var end = Map(range.End, Bias.Left);
        return new DocumentRange(start, end < start ? start : end);
    }

    private sealed class IdentityMap : PositionMap
    {
        public override DocumentPosition Map(DocumentPosition position, Bias bias = Bias.Right) => position;
    }
}

/// <summary>
/// The map of a replacement: the range [<see cref="From"/>, <see cref="To"/>] was replaced by content of
/// <see cref="InsertedBlocks"/> blocks (1 = inline content); <see cref="EndOfInserted"/> is where the inserted content ends.
/// Positions before the range stay, positions after it shift, positions inside it collapse to its start (left bias) or the
/// end of the inserted content (right bias).
/// </summary>
public sealed class ReplaceMap(DocumentPosition from, DocumentPosition to, int insertedBlocks, int lastInsertedLength) : PositionMap
{
    public DocumentPosition From { get; } = from;

    public DocumentPosition To { get; } = to;

    public int InsertedBlocks { get; } = insertedBlocks;

    /// <summary>The position right after the inserted content.</summary>
    public DocumentPosition EndOfInserted { get; } = insertedBlocks == 1
        ? new DocumentPosition(from.Block, from.Offset + lastInsertedLength)
        : new DocumentPosition(from.Block + insertedBlocks - 1, lastInsertedLength);

    public override DocumentPosition Map(DocumentPosition position, Bias bias = Bias.Right)
    {
        if (position < From)
        {
            return position;
        }

        if (position > To)
        {
            if (position.Block == To.Block)
            {
                return new DocumentPosition(EndOfInserted.Block, EndOfInserted.Offset + position.Offset - To.Offset);
            }

            return new DocumentPosition(position.Block - (To.Block - From.Block) + (InsertedBlocks - 1), position.Offset);
        }

        if (From == To)
        {
            return bias == Bias.Left ? From : EndOfInserted;
        }

        if (position == To)
        {
            return EndOfInserted;
        }

        return bias == Bias.Left ? From : EndOfInserted;
    }
}

/// <summary>The map of a whole-block replacement (undo of a step): positions inside replaced blocks clamp into the new ones.</summary>
public sealed class BlockReplaceMap(int start, int removedCount, IReadOnlyList<Block> inserted) : PositionMap
{
    public override DocumentPosition Map(DocumentPosition position, Bias bias = Bias.Right)
    {
        if (position.Block < start)
        {
            return position;
        }

        if (position.Block >= start + removedCount)
        {
            return position with { Block = position.Block - removedCount + inserted.Count };
        }

        if (inserted.Count == 0)
        {
            return new DocumentPosition(Math.Max(0, start - 1), 0);
        }

        var index = Math.Min(position.Block - start, inserted.Count - 1);
        return new DocumentPosition(start + index, Math.Min(position.Offset, inserted[index].Length));
    }
}

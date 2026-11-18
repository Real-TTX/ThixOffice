using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// The smallest reversible change of a document. Because blocks, style sheets and list definitions are immutable, a step keeps
/// the old and the new instances: applying swaps them in, the inverse swaps them back – undo/redo is exact by construction.
/// </summary>
internal abstract class EditStep
{
    /// <summary>Applies the step and returns how positions move.</summary>
    public abstract PositionMap Apply(Document document);

    /// <summary>The step that undoes this one.</summary>
    public abstract EditStep Invert();
}

/// <summary>Replaces blocks [<see cref="Start"/>, + Removed.Count) with <see cref="Inserted"/>.</summary>
internal sealed class BlocksStep(int start, IReadOnlyList<Block> removed, IReadOnlyList<Block> inserted, PositionMap map) : EditStep
{
    public int Start { get; } = start;

    public IReadOnlyList<Block> Removed { get; } = removed;

    public IReadOnlyList<Block> Inserted { get; } = inserted;

    public PositionMap Map { get; } = map;

    public override PositionMap Apply(Document document)
    {
        for (var index = 0; index < Removed.Count; index++)
        {
            if (!ReferenceEquals(document.Blocks[Start + index], Removed[index]))
            {
                throw new InvalidOperationException($"The document changed under an edit step (block {Start + index}).");
            }
        }

        document.ReplaceBlocks(Start, Removed.Count, Inserted);
        return Map;
    }

    public override EditStep Invert() => new BlocksStep(Start, Inserted, Removed, new BlockReplaceMap(Start, Inserted.Count, Removed));
}

/// <summary>Swaps a document-wide value (style sheet, list definitions, final section, settings).</summary>
internal sealed class DocumentValueStep<T>(T before, T after, Action<Document, T> set) : EditStep
    where T : class
{
    public T Before { get; } = before;

    public T After { get; } = after;

    public override PositionMap Apply(Document document)
    {
        set(document, After);
        return PositionMap.Identity;
    }

    public override EditStep Invert() => new DocumentValueStep<T>(After, Before, set);
}

/// <summary>Factories for the document-wide steps.</summary>
internal static class DocumentSteps
{
    public static EditStep Styles(Document document, StyleSheet styles) =>
        new DocumentValueStep<StyleSheet>(document.Styles, styles, (target, value) => target.SetStyles(value));

    public static EditStep Numbering(Document document, NumberingDefinitions numbering) =>
        new DocumentValueStep<NumberingDefinitions>(document.Numbering, numbering, (target, value) => target.SetNumbering(value));

    public static EditStep FinalSection(Document document, SectionProperties section) =>
        new DocumentValueStep<SectionProperties>(document.FinalSection, section, (target, value) => target.SetFinalSection(value));

    public static EditStep Settings(Document document, DocumentSettings settings) =>
        new DocumentValueStep<DocumentSettings>(document.Settings, settings, (target, value) => target.SetSettings(value));
}

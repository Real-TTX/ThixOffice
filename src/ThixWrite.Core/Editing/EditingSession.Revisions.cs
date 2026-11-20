using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// "Änderungen nachverfolgen": while it is on (<see cref="DocumentSettings.TrackRevisions"/>), typed and pasted text is an insertion and
/// deleted text stays as a deletion (text another insertion put there goes for good), each with the author and the time; Annehmen /
/// Ablehnen one by one, Vorherige / Nächste. Paragraph marks and format changes are not recorded.
/// </summary>
public sealed partial class EditingSession
{
    /// <summary>The name recorded with new changes (the app sets it from its settings).</summary>
    public string RevisionAuthor { get; set; } = "Unbekannt";

    /// <summary>True while edits are recorded as tracked changes.</summary>
    public bool TrackChanges => Document.Settings.TrackRevisions;

    /// <summary>"Änderungen nachverfolgen" on or off (saved with the document, one undo step).</summary>
    public void SetTrackChanges(bool on)
    {
        if (on == TrackChanges)
        {
            return;
        }

        Run(on ? "Änderungen nachverfolgen" : "Nachverfolgung aus", EditKind.Other, null, () => Apply(DocumentSteps.Settings(Document, Document.Settings with { TrackRevisions = on })));
    }

    /// <summary>A change by the author now (to the minute, so that one sitting's typing is one change).</summary>
    private Revision NewRevision(RevisionKind kind)
    {
        var now = _clock();
        return new Revision(kind, RevisionAuthor, new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind));
    }

    /// <summary>
    /// A deletion while changes are tracked: the text of <paramref name="range"/> is marked deleted (text that was itself inserted goes);
    /// returns the start and the end of what is left of the range.
    /// </summary>
    private (DocumentPosition Start, DocumentPosition End) TrackDelete(DocumentRange range)
    {
        if (range.IsEmpty)
        {
            return (range.Start, range.Start);
        }

        var deletion = NewRevision(RevisionKind.Deletion);
        var endOffset = range.End.Offset;
        for (var block = range.Start.Block; block <= range.End.Block; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var from = block == range.Start.Block ? range.Start.Offset : 0;
            var to = block == range.End.Block ? range.End.Offset : paragraph.Length;
            var kept = paragraph.Slice(from, to)
                .Where(inline => inline.Revision is not { Kind: RevisionKind.Insertion or RevisionKind.MoveTo })
                .Select(inline => inline.Revision is null ? inline with { Revision = deletion } : inline)
                .ToList();
            if (block == range.End.Block)
            {
                endOffset = from + kept.Sum(inline => inline.Length);
            }

            var changed = paragraph.WithInlines([.. paragraph.Slice(0, from), .. kept, .. paragraph.Slice(to, paragraph.Length)]);
            ApplyOptional(ContentEdits.FormatParagraphs(Document, block, block, _ => changed));
        }

        return (range.Start, new DocumentPosition(range.End.Block, endOffset));
    }

    /// <summary>Marks what was just put in between <paramref name="from"/> and <paramref name="to"/> as an insertion.</summary>
    private void MarkInserted(DocumentPosition from, DocumentPosition to)
    {
        if (from >= to)
        {
            return;
        }

        var insertion = NewRevision(RevisionKind.Insertion);
        ApplyOptional(ContentEdits.MapInlines(Document, new DocumentRange(from, to), inline => inline.Revision is null && !inline.IsMarker ? inline with { Revision = insertion } : inline));
    }

    /// <summary>The tracked change at <paramref name="position"/> (the run of content of one change around it), or null.</summary>
    public (DocumentRange Range, Revision Revision)? RevisionAt(DocumentPosition position)
    {
        if (Document.ParagraphAt(position.Block) is not { } paragraph || paragraph.Length == 0)
        {
            return null;
        }

        var offset = Math.Clamp(position.Offset, 0, paragraph.Length - 1);
        var revision = paragraph.InlineAt(offset)?.Revision ?? (offset > 0 ? paragraph.InlineAt(offset - 1)?.Revision : null);
        if (revision is null)
        {
            return null;
        }

        var index = paragraph.InlineAt(offset)?.Revision == revision ? paragraph.InlineIndexAt(offset) : paragraph.InlineIndexAt(offset - 1);
        var first = index;
        while (first > 0 && paragraph.Inlines[first - 1].Revision == revision)
        {
            first--;
        }

        var last = index;
        while (last + 1 < paragraph.Inlines.Length && paragraph.Inlines[last + 1].Revision == revision)
        {
            last++;
        }

        return (new DocumentRange(new DocumentPosition(position.Block, paragraph.StartOf(first)), new DocumentPosition(position.Block, paragraph.StartOf(last + 1))), revision);
    }

    /// <summary>"Nächste" / "Vorherige": selects the next (previous) tracked change; false when there is none that way.</summary>
    public bool NextRevision(bool forward)
    {
        var from = forward ? _selection.End : _selection.Start;
        var blocks = Document.Blocks.Count;
        for (var step = 0; step < blocks; step++)
        {
            var block = forward ? from.Block + step : from.Block - step;
            if (block < 0 || block >= blocks || Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var indices = Enumerable.Range(0, paragraph.Inlines.Length);
            foreach (var index in forward ? indices : indices.Reverse())
            {
                var start = paragraph.StartOf(index);
                if (paragraph.Inlines[index].Revision is null
                    || (step == 0 && (forward ? start < from.Offset : start >= from.Offset)))
                {
                    continue;
                }

                if (RevisionAt(new DocumentPosition(block, start)) is { } found && (step > 0 || (forward ? found.Range.Start >= from : found.Range.End <= from)))
                {
                    Select(found.Range.Start, found.Range.End);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// "Annehmen" / "Ablehnen" the changes in the selection (or the change at the caret): accepting keeps insertions and removes deletions,
    /// rejecting the other way round. One step.
    /// </summary>
    public void ResolveRevision(bool accept)
    {
        var range = _selection.IsCollapsed ? RevisionAt(_selection.Start)?.Range : _selection.Range;
        if (range is not { } target)
        {
            return;
        }

        Run(accept ? "Änderung annehmen" : "Änderung ablehnen", EditKind.Other, null, () =>
        {
            for (var block = target.Start.Block; block <= target.End.Block; block++)
            {
                if (Document.Blocks[block] is not Paragraph paragraph)
                {
                    continue;
                }

                var from = block == target.Start.Block ? target.Start.Offset : 0;
                var to = block == target.End.Block ? target.End.Offset : paragraph.Length;
                var middle = paragraph.Slice(from, to)
                    .Where(inline => inline.Revision is null || inline.Revision.IsRemoval != accept)
                    .Select(inline => inline.Revision is null ? inline : inline with { Revision = null });
                var changed = paragraph.WithInlines([.. paragraph.Slice(0, from), .. middle, .. paragraph.Slice(to, paragraph.Length)]);
                ApplyOptional(ContentEdits.FormatParagraphs(Document, block, block, _ => changed));
            }

            SetSelectionQuiet(Selection.Caret(CaretStops.Snap(Document, target.Start)));
        });
    }
}

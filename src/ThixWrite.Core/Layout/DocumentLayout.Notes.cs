using System.Collections.Immutable;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// Footnotes and endnotes. Footnotes go to the foot of the page their reference is on, under a short separator line: a line with
/// references is placed only when it and (the start of) its notes fit; what of a long note does not fit continues on the next page
/// under a separator across the whole text width. Endnotes follow the last paragraph, under a separator, and flow over pages like
/// text. Notes are numbered in document order per kind; a paragraph whose note numbers change is laid out again.
/// </summary>
public sealed partial class DocumentLayout
{
    /// <summary>The height of the footnote separator (the gap above the notes with the line in its middle).</summary>
    public const double NoteSeparatorHeight = 14;

    /// <summary>The length of the separator line above footnotes (Word: about a third of the text width).</summary>
    public const double NoteSeparatorWidth = 144;

    private readonly Dictionary<(Block Block, string Label, double Width), ParagraphLayout> _noteLayouts = [];
    private (int Footnote, int Endnote)[] _noteStarts = [];

    /// <summary>The number a note's own mark (<see cref="NoteMarkInline"/>) shows in this layout's paragraphs (the editor of a note sets it).</summary>
    public string? NoteMarkText
    {
        get => _context.NoteMarkText;
        set
        {
            _context.NoteMarkText = value;
            _clearAll = true;
        }
    }

    /// <summary>The footnote or endnote placed at a point of a page (its text, not its reference), or null.</summary>
    public PlacedNote? NoteAt(int page, double x, double y)
    {
        Update();
        if (page < 0 || page >= _pages.Count)
        {
            return null;
        }

        foreach (var note in _pages[page].Footnotes.Concat(_pages[page].Endnotes))
        {
            if (note.Lines.Count == 0)
            {
                continue;
            }

            var top = note.Lines[0].Top;
            var bottom = note.Lines[^1].Top + note.Lines[^1].Line.Height;
            if (y >= top && y < bottom && x >= note.Lines[0].Left - 4 && x <= note.Lines[0].Left + note.Lines[0].Paragraph.Width + 4)
            {
                return note;
            }
        }

        return null;
    }

    /// <summary>Where the text of the note referenced at <paramref name="reference"/> starts (its first placed part), or null.</summary>
    public (int Page, PlacedNote Note)? FindNote(DocumentPosition reference)
    {
        Update();
        for (var page = 0; page < _pages.Count; page++)
        {
            foreach (var note in _pages[page].Footnotes.Concat(_pages[page].Endnotes))
            {
                if (note.Reference == reference && !note.IsContinuation)
                {
                    return (page, note);
                }
            }
        }

        return null;
    }

    /// <summary>The label of note <paramref name="reference"/> as the text shows it ("3", "iv"), or null when there is no note there.</summary>
    public string? NoteLabel(DocumentPosition reference)
    {
        Update();
        if (Document.ParagraphAt(reference.Block) is not { } paragraph || paragraph.InlineAt(reference.Offset) is not NoteInline note)
        {
            return null;
        }

        (int Footnote, int Endnote) start = reference.Block < _noteStarts.Length ? _noteStarts[reference.Block] : (1, 1);
        var number = note.Kind == NoteKind.Footnote ? start.Footnote : start.Endnote;
        for (var index = 0; index < paragraph.Inlines.Length && paragraph.StartOf(index) < reference.Offset; index++)
        {
            if (paragraph.Inlines[index] is NoteInline other && other.Kind == note.Kind)
            {
                number++;
            }
        }

        return NoteNumbering.Format(note.Kind, number);
    }

    /// <summary>The numbers of the first footnote and endnote of every paragraph that has notes ((0, 0) for the others).</summary>
    private (int, int)[] ComputeNoteStarts()
    {
        var starts = new (int, int)[Document.Blocks.Count];
        var footnotes = 1;
        var endnotes = 1;
        for (var block = 0; block < starts.Length; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var (start, foot, end) = ((footnotes, endnotes), 0, 0);
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is NoteInline note)
                {
                    if (note.Kind == NoteKind.Footnote)
                    {
                        foot++;
                    }
                    else
                    {
                        end++;
                    }
                }
            }

            if (foot + end == 0)
            {
                continue;
            }

            starts[block] = start;
            footnotes += foot;
            endnotes += end;
        }

        return starts;
    }

    /// <summary>The notes referenced on a line of a block, with their labels.</summary>
    private List<(DocumentPosition Reference, NoteInline Note, string Label)> NotesIn(int block, LineLayout line, NoteKind kind = NoteKind.Footnote)
    {
        var result = new List<(DocumentPosition, NoteInline, string)>();
        if (block >= _noteStarts.Length || _noteStarts[block] == (0, 0) || Document.Blocks[block] is not Paragraph paragraph)
        {
            return result;
        }

        var number = kind == NoteKind.Footnote ? _noteStarts[block].Footnote : _noteStarts[block].Endnote;
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            if (paragraph.Inlines[index] is not NoteInline note || note.Kind != kind)
            {
                continue;
            }

            var offset = paragraph.StartOf(index);
            if (offset >= line.Start && offset < line.End)
            {
                result.Add((new DocumentPosition(block, offset), note, NoteNumbering.Format(kind, number)));
            }

            number++;
        }

        return result;
    }

    /// <summary>A note's text laid out at a width: its lines with the space above each.</summary>
    private NoteContent LayoutNote(NoteInline note, string label, double width)
    {
        if (_noteLayouts.Count > 2000)
        {
            _noteLayouts.Clear();
        }

        var lines = new List<NoteLine>();
        var previous = _context.NoteMarkText;
        _context.NoteMarkText = label;
        try
        {
            var blocks = note.Content.Blocks;
            for (var block = 0; block < blocks.Count; block++)
            {
                if (blocks[block] is not Paragraph paragraph)
                {
                    continue;
                }

                var key = (blocks[block], label, width);
                if (!_noteLayouts.TryGetValue(key, out var layout))
                {
                    layout = ParagraphLayouter.Layout(paragraph, _context.Resolver.ResolveParagraph(paragraph), null, width, _context);
                    _noteLayouts[key] = layout;
                }

                var before = block == 0 ? 0 : layout.Format.SpacingBefore + (lines.Count > 0 ? lines[^1].Paragraph.Format.SpacingAfter : 0);
                for (var index = 0; index < layout.Lines.Count; index++)
                {
                    lines.Add(new NoteLine(block, index, index == 0 ? before : 0, layout.Lines[index], layout));
                }
            }
        }
        finally
        {
            _context.NoteMarkText = previous;
        }

        return new NoteContent(lines);
    }

    /// <summary>Turns the footnote slices of the pages from <paramref name="firstPage"/> on into placed notes at the foot of each page.</summary>
    private void PlaceFootnoteAreas(int firstPage)
    {
        for (var index = Math.Max(0, firstPage); index < _pages.Count; index++)
        {
            var page = _pages[index];
            var slices = page.MutableFootnoteSlices;
            if (slices.Count == 0)
            {
                page.Footnotes = [];
                page.FootnoteSeparator = null;
                continue;
            }

            var setup = page.Section.Properties;
            var left = setup.MarginLeft + setup.Gutter;
            var y = setup.PageHeight - setup.MarginBottom - page.FootnoteHeight;
            page.FootnoteSeparator = new NoteSeparator(left, y + (NoteSeparatorHeight / 2), slices[0].From > 0 ? setup.ContentWidth : Math.Min(NoteSeparatorWidth, setup.ContentWidth));
            y += NoteSeparatorHeight;
            var placed = new List<PlacedNote>();
            foreach (var slice in slices)
            {
                var lines = new List<PlacedLine>();
                for (var line = slice.From; line < slice.To; line++)
                {
                    var item = slice.Content.Lines[line];
                    y += line == slice.From ? 0 : item.Before;
                    lines.Add(new PlacedLine(item.Block, item.LineIndex, y, left, item.Line, item.Paragraph));
                    y += item.Line.Height;
                }

                placed.Add(new PlacedNote(NoteKind.Footnote, slice.Reference, slice.Label, slice.Note, lines, slice.From > 0));
            }

            page.Footnotes = placed;
        }
    }

    /// <summary>A note's text laid out: its lines in order.</summary>
    internal sealed class NoteContent(IReadOnlyList<NoteLine> lines)
    {
        public IReadOnlyList<NoteLine> Lines { get; } = lines;

        /// <summary>The height of lines [<paramref name="from"/>, <paramref name="to"/>) (the space above the first one left out).</summary>
        public double Height(int from, int to)
        {
            var height = 0.0;
            for (var index = from; index < to && index < Lines.Count; index++)
            {
                height += Lines[index].Line.Height + (index == from ? 0 : Lines[index].Before);
            }

            return height;
        }
    }

    /// <summary>A line of a note's text: its story block and line, the space above it (between paragraphs), the line itself.</summary>
    internal readonly record struct NoteLine(int Block, int LineIndex, double Before, LineLayout Line, ParagraphLayout Paragraph);

    /// <summary>
    /// The part of a footnote on one page: lines [<see cref="From"/>, <see cref="To"/>) of its text, referenced from line
    /// <see cref="RefLine"/> of block <see cref="RefBlock"/> (re-pagination from a block removes the slices of its references).
    /// </summary>
    internal sealed record NoteSlice(int RefBlock, int RefLine, DocumentPosition Reference, string Label, NoteInline Note, NoteContent Content, int From, int To)
    {
        public double Height => Content.Height(From, To);
    }

    private sealed partial class Flow
    {
        private ImmutableList<NoteSlice> _carry = start.Carry ?? ImmutableList<NoteSlice>.Empty;

        /// <summary>The space a line's footnotes need on this page before the line may go here: short notes whole, long ones their first two lines.</summary>
        private double NoteReserve(List<(DocumentPosition Reference, NoteInline Note, string Label)> notes, double width)
        {
            if (notes.Count == 0 || !_carry.IsEmpty)
            {
                return 0;
            }

            var reserve = Page.MutableFootnoteSlices.Count == 0 ? NoteSeparatorHeight : 0;
            foreach (var (_, note, label) in notes)
            {
                var content = owner.LayoutNote(note, label, width);
                reserve += content.Height(0, Math.Min(2, content.Lines.Count));
            }

            return reserve;
        }

        /// <summary>Puts a placed line's footnotes on this page – as much of them as fits under <paramref name="lineBottom"/>; the rest continues on the next page.</summary>
        private void AddFootnotes(int block, int lineIndex, List<(DocumentPosition Reference, NoteInline Note, string Label)> notes, double lineBottom)
        {
            var width = Setup.ContentWidth;
            foreach (var (reference, note, label) in notes)
            {
                var content = owner.LayoutNote(note, label, width);
                if (content.Lines.Count == 0)
                {
                    continue;
                }

                var slice = new NoteSlice(block, lineIndex, reference, label, note, content, 0, content.Lines.Count);
                if (!_carry.IsEmpty)
                {
                    _carry = _carry.Add(slice);
                    continue;
                }

                var fitting = Fitting(slice, lineBottom);
                if (fitting == 0)
                {
                    _carry = _carry.Add(slice);
                    continue;
                }

                Page.AddFootnote(slice with { To = fitting });
                if (fitting < content.Lines.Count)
                {
                    _carry = _carry.Add(slice with { From = fitting });
                }
            }
        }

        /// <summary>How many lines of a slice fit on this page under <paramref name="lineBottom"/> (counted from its first line).</summary>
        private int Fitting(NoteSlice slice, double lineBottom)
        {
            var available = Setup.PageHeight - Setup.MarginBottom - lineBottom - Page.FootnoteHeight - (Page.MutableFootnoteSlices.Count == 0 ? NoteSeparatorHeight : 0);
            var count = slice.From;
            while (count < slice.To && slice.Content.Height(slice.From, count + 1) <= available + Epsilon)
            {
                count++;
            }

            return count;
        }

        /// <summary>A new page takes the footnote text carried over from the page before first.</summary>
        private void PlaceCarry()
        {
            if (_carry.IsEmpty)
            {
                return;
            }

            var carried = _carry;
            _carry = ImmutableList<NoteSlice>.Empty;
            foreach (var slice in carried)
            {
                if (!_carry.IsEmpty)
                {
                    _carry = _carry.Add(slice);
                    continue;
                }

                var fitting = Fitting(slice, _y);
                if (fitting == slice.From)
                {
                    fitting = Math.Min(slice.To, slice.From + 1); // at least a line on an empty page, or it never ends
                }

                Page.AddFootnote(slice with { To = fitting });
                if (fitting < slice.To)
                {
                    _carry = _carry.Add(slice with { From = fitting });
                }
            }
        }

        /// <summary>Drops the footnotes of lines the widow control takes back from this page.</summary>
        private void RemoveFootnotesFrom(int block, int lineIndex)
        {
            Page.RemoveFootnotes(slice => slice.RefBlock == block && slice.RefLine >= lineIndex);
            _carry = _carry.RemoveAll(slice => slice.RefBlock == block && slice.RefLine >= lineIndex);
        }

        /// <summary>After the last block: the rest of long footnotes on pages of their own, then the endnotes.</summary>
        public void Finish()
        {
            var guard = 0;
            while (!_carry.IsEmpty && guard++ < 1000)
            {
                NewPage(hard: false);
            }

            PlaceEndnotes();
        }

        private void PlaceEndnotes()
        {
            var document = owner.Document;
            var endnotes = new List<(DocumentPosition Reference, NoteInline Note, string Label)>();
            for (var block = 0; block < document.Blocks.Count; block++)
            {
                if (owner._noteStarts.Length > block && owner._noteStarts[block] != (0, 0) && document.Blocks[block] is Paragraph paragraph)
                {
                    var number = owner._noteStarts[block].Endnote;
                    for (var index = 0; index < paragraph.Inlines.Length; index++)
                    {
                        if (paragraph.Inlines[index] is NoteInline { Kind: NoteKind.Endnote } note)
                        {
                            endnotes.Add((new DocumentPosition(block, paragraph.StartOf(index)), note, NoteNumbering.Format(NoteKind.Endnote, number++)));
                        }
                    }
                }
            }

            if (endnotes.Count == 0)
            {
                return;
            }

            if (_y + NoteSeparatorHeight + 14 > Bottom)
            {
                NewPage(hard: false);
            }

            Page.EndnoteSeparator = new NoteSeparator(Setup.MarginLeft + Setup.Gutter, _y + (NoteSeparatorHeight / 2), Math.Min(NoteSeparatorWidth, Setup.ContentWidth));
            _y += NoteSeparatorHeight;
            foreach (var (reference, note, label) in endnotes)
            {
                var content = owner.LayoutNote(note, label, Setup.ContentWidth);
                var lines = new List<PlacedLine>();
                var continuation = false;
                for (var index = 0; index < content.Lines.Count; index++)
                {
                    var item = content.Lines[index];
                    var gap = lines.Count == 0 && index == 0 ? 0 : item.Before;
                    if (_y + gap + item.Line.Height > Bottom + Epsilon && (lines.Count > 0 || Page.Endnotes.Count > 0 || Page.Lines.Count > 0))
                    {
                        if (lines.Count > 0)
                        {
                            Page.MutableEndnotes.Add(new PlacedNote(NoteKind.Endnote, reference, label, note, lines, continuation));
                            lines = [];
                            continuation = true;
                        }

                        NewPage(hard: false);
                        gap = 0;
                    }

                    _y += gap;
                    lines.Add(new PlacedLine(item.Block, item.LineIndex, _y, Setup.MarginLeft + Setup.Gutter, item.Line, item.Paragraph));
                    _y += item.Line.Height;
                }

                if (lines.Count > 0)
                {
                    Page.MutableEndnotes.Add(new PlacedNote(NoteKind.Endnote, reference, label, note, lines, continuation));
                }

                _y += content.Lines.Count > 0 ? content.Lines[^1].Paragraph.Format.SpacingAfter : 0;
            }
        }
    }
}

/// <summary>
/// The text of a footnote or endnote (or the part of it) placed on a page: its kind, its reference in the document, the label it
/// shows, the note and its lines (block numbers of the note's story). <see cref="IsContinuation"/>: the text continues from the page before.
/// </summary>
public sealed record PlacedNote(NoteKind Kind, DocumentPosition Reference, string Label, NoteInline Note, IReadOnlyList<PlacedLine> Lines, bool IsContinuation);

/// <summary>The separator line above notes: its left end, its y and its length (page points).</summary>
public readonly record struct NoteSeparator(double X, double Y, double Width);

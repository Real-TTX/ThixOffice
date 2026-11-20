using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Links (Einfügen › Link, Strg+K): adding, changing and removing them on text.</summary>
public sealed partial class EditingSession
{
    /// <summary>The link at the caret (or the start of the selection), or null.</summary>
    public Hyperlink? LinkAtCaret => LinkRange(_selection.Start) is { } found ? found.Link : null;

    /// <summary>The text of the selection in one paragraph (the text a link dialog shows), or null over paragraphs.</summary>
    public string? SelectedText
    {
        get
        {
            var range = _selection.Range;
            return range.Start.Block == range.End.Block && Document.ParagraphAt(range.Start.Block) is { } paragraph
                ? paragraph.Text[range.Start.Offset..range.End.Offset].Replace(SpecialCharacters.Object.ToString(), "")
                : null;
        }
    }

    /// <summary>The whole link around a position (the inlines next to each other with the same link) and the link, or null.</summary>
    public (DocumentRange Range, Hyperlink Link)? LinkRange(DocumentPosition position)
    {
        if (Document.ParagraphAt(position.Block) is not { } paragraph)
        {
            return null;
        }

        var index = paragraph.InlineIndexAt(position.Offset);
        if ((index < 0 || paragraph.Inlines[index].Link is null) && position.Offset > 0)
        {
            index = paragraph.InlineIndexAt(position.Offset - 1);
        }

        if (index < 0 || paragraph.Inlines[index].Link is not { } link)
        {
            return null;
        }

        var first = index;
        while (first > 0 && paragraph.Inlines[first - 1].Link == link)
        {
            first--;
        }

        var last = index;
        while (last + 1 < paragraph.Inlines.Length && paragraph.Inlines[last + 1].Link == link)
        {
            last++;
        }

        var start = paragraph.StartOf(first);
        var end = paragraph.StartOf(last) + paragraph.Inlines[last].Length;
        return (new DocumentRange(position with { Offset = start }, position with { Offset = end }), link);
    }

    /// <summary>
    /// Makes the selection (or the link at the caret) a link to <paramref name="address"/>; with <paramref name="text"/> the text shown
    /// becomes that (a caret: the text is inserted). The text gets the character style "Link" (blue, underlined).
    /// </summary>
    public void SetHyperlink(string address, string? text = null, string? tooltip = null, string? anchor = null)
    {
        var link = anchor is not null
            ? new Hyperlink(null, anchor, string.IsNullOrWhiteSpace(tooltip) ? null : tooltip)
            : new Hyperlink(address, Tooltip: string.IsNullOrWhiteSpace(tooltip) ? null : tooltip);
        var range = _selection.IsCollapsed && LinkRange(_selection.Start) is { } existing ? existing.Range : _selection.Range;
        var shown = string.IsNullOrEmpty(text) ? null : text;
        Run("Link", EditKind.Other, null, () =>
        {
            EnsureStyle(BuiltInStyles.HyperlinkStyle);
            var currentText = range.Start.Block == range.End.Block ? Document.ParagraphAt(range.Start.Block)?.Text[range.Start.Offset..range.End.Offset] : null;
            if (range.IsEmpty || (shown is not null && shown != currentText))
            {
                var (properties, _) = TypingFormat();
                var run = new TextRun(shown ?? anchor ?? address, properties with { StyleId = BuiltInStyles.HyperlinkStyle }, link);
                var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, range, [new Paragraph([run])]));
                SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
                return;
            }

            ApplyOptional(ContentEdits.MapInlines(Document, range, inline => inline with
            {
                Link = link,
                Properties = inline.Properties with { StyleId = BuiltInStyles.HyperlinkStyle },
            }));
        });
    }

    /// <summary>"Link entfernen": the text of the selection (or of the link at the caret) is plain text again.</summary>
    public void RemoveHyperlink()
    {
        var range = _selection.IsCollapsed && LinkRange(_selection.Start) is { } existing ? existing.Range : _selection.Range;
        if (range.IsEmpty)
        {
            return;
        }

        Run("Link entfernen", EditKind.Other, null, () =>
            ApplyOptional(ContentEdits.MapInlines(Document, range, inline => inline with
            {
                Link = null,
                Properties = inline.Properties.StyleId == BuiltInStyles.HyperlinkStyle ? inline.Properties with { StyleId = null } : inline.Properties,
            })));
    }
}

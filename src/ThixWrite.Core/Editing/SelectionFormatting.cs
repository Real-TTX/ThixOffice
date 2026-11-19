using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>A formatting value of a selection: one <see cref="Value"/>, or <see cref="IsMixed"/> when the selection has several.</summary>
public readonly record struct FormatState<T>(T Value, bool IsMixed)
{
    public static FormatState<T> Mixed => new(default!, true);

    /// <summary>True when the whole selection has exactly <paramref name="value"/>.</summary>
    public bool Is(T value) => !IsMixed && EqualityComparer<T>.Default.Equals(Value, value);

    public override string ToString() => IsMixed ? "(gemischt)" : $"{Value}";
}

/// <summary>
/// What the ribbon shows for the current selection (asked on every selection change; cached by the session until the
/// document, the selection or the pending formatting changes). Paragraph values cover the paragraphs the selection touches,
/// character values the selected text – at a caret, the formatting typing would get.
/// </summary>
public sealed record SelectionFormatting
{
    public required FormatState<string> FontFamily { get; init; }

    public required FormatState<double> FontSize { get; init; }

    public required FormatState<bool> Bold { get; init; }

    public required FormatState<bool> Italic { get; init; }

    public required FormatState<UnderlineStyle> Underline { get; init; }

    public required FormatState<bool> Strikethrough { get; init; }

    public required FormatState<bool> Superscript { get; init; }

    public required FormatState<bool> Subscript { get; init; }

    /// <summary>"RRGGBB" or "auto".</summary>
    public required FormatState<string> Color { get; init; }

    public required FormatState<HighlightColor> Highlight { get; init; }

    /// <summary>The character style id ("" = none).</summary>
    public required FormatState<string> CharacterStyleId { get; init; }

    public required FormatState<string> ParagraphStyleId { get; init; }

    /// <summary>The German name of the paragraph style ("Überschrift 1"); null when mixed.</summary>
    public string? ParagraphStyleName { get; init; }

    public required FormatState<ParagraphAlignment> Alignment { get; init; }

    public required FormatState<LineSpacing> LineSpacing { get; init; }

    public required FormatState<double> SpacingBefore { get; init; }

    public required FormatState<double> SpacingAfter { get; init; }

    public required FormatState<double> IndentLeft { get; init; }

    public required FormatState<double> IndentRight { get; init; }

    public required FormatState<double> FirstLineIndent { get; init; }

    /// <summary>The kind of list (null = no list).</summary>
    public required FormatState<ListKind?> List { get; init; }

    /// <summary>The list level 0–8 (-1 = no list).</summary>
    public required FormatState<int> ListLevel { get; init; }

    public bool HasSelection { get; init; }

    public bool CanUndo { get; init; }

    public bool CanRedo { get; init; }

    /// <summary>"Rückgängig: Eingabe" – the label of the next undo step, or null.</summary>
    public string? UndoLabel { get; init; }

    public string? RedoLabel { get; init; }
}

/// <summary>Builds <see cref="SelectionFormatting"/>.</summary>
internal static class SelectionFormattingBuilder
{
    public static SelectionFormatting Build(Document document, StyleResolver resolver, Selection selection, CharacterProperties? pending)
    {
        var characters = new List<EffectiveCharacterFormat>();
        var paragraphs = new List<(Paragraph Paragraph, EffectiveParagraphFormat Format)>();
        var start = selection.Start;
        var end = selection.End;

        for (var index = start.Block; index <= end.Block; index++)
        {
            if (document.Blocks[index] is not Paragraph paragraph)
            {
                continue;
            }

            paragraphs.Add((paragraph, resolver.ResolveParagraph(paragraph)));
            if (selection.IsCollapsed)
            {
                var typing = pending ?? paragraph.TypingPropertiesAt(start.Offset);
                characters.Add(resolver.ResolveCharacter(paragraph, typing));
                continue;
            }

            var from = index == start.Block ? start.Offset : 0;
            var to = index == end.Block ? end.Offset : paragraph.Length;
            AddCharacters(resolver, paragraph, from, to, characters);
        }

        if (characters.Count == 0 && paragraphs.Count > 0)
        {
            var first = paragraphs[0].Paragraph;
            characters.Add(resolver.ResolveCharacter(first, first.TypingPropertiesAt(start.Offset)));
        }

        var styleState = Combine(paragraphs, item => item.Format.StyleId);
        return new SelectionFormatting
        {
            FontFamily = Combine(characters, format => format.FontFamily),
            FontSize = Combine(characters, format => format.FontSize),
            Bold = Combine(characters, format => format.Bold),
            Italic = Combine(characters, format => format.Italic),
            Underline = Combine(characters, format => format.Underline),
            Strikethrough = Combine(characters, format => format.Strikethrough),
            Superscript = Combine(characters, format => format.VerticalPosition == VerticalPosition.Superscript),
            Subscript = Combine(characters, format => format.VerticalPosition == VerticalPosition.Subscript),
            Color = Combine(characters, format => format.Color),
            Highlight = Combine(characters, format => format.Highlight),
            CharacterStyleId = Combine(characters, format => format.CharacterStyleId ?? ""),
            ParagraphStyleId = styleState,
            ParagraphStyleName = styleState.IsMixed || document.Styles.Find(styleState.Value) is not { } style ? null : BuiltInStyles.DisplayName(style),
            Alignment = Combine(paragraphs, item => item.Format.Alignment),
            LineSpacing = Combine(paragraphs, item => item.Format.LineSpacing),
            SpacingBefore = Combine(paragraphs, item => item.Format.SpacingBefore),
            SpacingAfter = Combine(paragraphs, item => item.Format.SpacingAfter),
            IndentLeft = Combine(paragraphs, item => item.Format.IndentLeft),
            IndentRight = Combine(paragraphs, item => item.Format.IndentRight),
            FirstLineIndent = Combine(paragraphs, item => item.Format.FirstLineIndent),
            List = Combine(paragraphs, item => ListKindOf(document, item.Format)),
            ListLevel = Combine(paragraphs, item => item.Format.Numbering?.Level ?? -1),
            HasSelection = !selection.IsCollapsed,
        };
    }

    /// <summary>The kind of list a resolved paragraph is in, or null.</summary>
    public static ListKind? ListKindOf(Document document, EffectiveParagraphFormat format)
    {
        if (format.Numbering is not { } reference || document.Numbering.Instance(reference.NumberingId) is not { } instance)
        {
            return null;
        }

        var level = document.Numbering.ResolveLevel(reference.NumberingId, reference.Level);
        if (level is not null)
        {
            return level.Format == NumberFormat.Bullet ? ListKind.Bullet : ListKind.Numbered;
        }

        return document.Numbering.Definition(instance.AbstractId) is { } definition ? BuiltInLists.KindOf(definition) : null;
    }

    private static void AddCharacters(StyleResolver resolver, Paragraph paragraph, int from, int to, List<EffectiveCharacterFormat> characters)
    {
        if (from == to)
        {
            return;
        }

        var styleId = resolver.ParagraphStyleId(paragraph);
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            var inline = paragraph.Inlines[index];
            var start = paragraph.StartOf(index);
            if (start >= to || start + inline.Length <= from || inline is OpaqueInline { IsMarker: true })
            {
                continue;
            }

            var format = resolver.ResolveCharacter(styleId, inline.Properties);
            if (characters.Count == 0 || !ReferenceEquals(characters[^1], format))
            {
                characters.Add(format);
            }
        }
    }

    private static FormatState<TValue> Combine<TItem, TValue>(List<TItem> items, Func<TItem, TValue> select)
    {
        if (items.Count == 0)
        {
            return FormatState<TValue>.Mixed;
        }

        var first = select(items[0]);
        for (var index = 1; index < items.Count; index++)
        {
            if (!EqualityComparer<TValue>.Default.Equals(first, select(items[index])))
            {
                return FormatState<TValue>.Mixed;
            }
        }

        return new FormatState<TValue>(first, false);
    }
}

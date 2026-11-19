using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>
/// Pure edits of a text body (the session wraps them in undoable steps): insert, delete across paragraphs, split, format runs and
/// paragraphs, and the run that formats a position. Runs split at edit boundaries and merge again when their properties are equal,
/// so the body stays small; fields and kept runs are atomic (a deletion inside one removes it whole).
/// </summary>
public static class TextEdits
{
    /// <summary>The end position of a body.</summary>
    public static TextPosition End(TextBody body) => new(body.Paragraphs.Count - 1, body.Paragraphs[^1].Length);

    /// <summary>A position clamped into the body.</summary>
    public static TextPosition Clamp(TextBody body, TextPosition position)
    {
        var paragraph = Math.Clamp(position.Paragraph, 0, body.Paragraphs.Count - 1);
        return new TextPosition(paragraph, Math.Clamp(position.Offset, 0, body.Paragraphs[paragraph].Length));
    }

    /// <summary>The properties text typed at <paramref name="position"/> gets: the run before it (or after it at a paragraph start).</summary>
    public static RunProperties PropertiesAt(TextBody body, TextPosition position)
    {
        var paragraph = body.Paragraphs[position.Paragraph];
        var offset = 0;
        TextRun? previous = null;
        foreach (var run in paragraph.Runs)
        {
            var end = offset + run.Text.Length;
            if (position.Offset <= end && position.Offset > offset)
            {
                return Plain(run.Properties);
            }

            if (position.Offset == offset && previous is null)
            {
                return Plain(run.Properties);
            }

            previous = run;
            offset = end;
        }

        return Plain(previous?.Properties ?? paragraph.EndProperties ?? RunProperties.Empty);
    }

    /// <summary>Properties without file bookkeeping (dirty, spelling error) – typed text gets fresh ones.</summary>
    private static RunProperties Plain(RunProperties properties) => properties with { Dirty = null, SpellingError = null };

    /// <summary>Inserts <paramref name="text"/> ("\n" splits paragraphs, "\v" is a line break) at <paramref name="position"/>.</summary>
    public static (TextBody Body, TextPosition End) Insert(TextBody body, TextPosition position, string text, RunProperties? properties = null)
    {
        position = Clamp(body, position);
        var format = properties ?? PropertiesAt(body, position);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var current = body;
        var at = position;
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                current = Split(current, at);
                at = new TextPosition(at.Paragraph + 1, 0);
            }

            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            var paragraph = current.Paragraphs[at.Paragraph];
            var (before, after) = SplitRuns(paragraph.Runs, at.Offset);
            var inserted = new List<TextRun>();
            var segment = 0;
            for (var character = 0; character <= line.Length; character++)
            {
                if (character < line.Length && line[character] != LineBreak.Character)
                {
                    continue;
                }

                if (character > segment)
                {
                    inserted.Add(new TextSpan(line[segment..character], format));
                }

                if (character < line.Length)
                {
                    inserted.Add(new LineBreak(format));
                }

                segment = character + 1;
            }

            var runs = Merge([.. before, .. inserted, .. after]);
            current = current with { Paragraphs = current.Paragraphs.SetItem(at.Paragraph, paragraph with { Runs = runs }) };
            at = new TextPosition(at.Paragraph, at.Offset + line.Length);
        }

        return (current, at);
    }

    /// <summary>Splits the paragraph at <paramref name="position"/> (Eingabe): both halves keep the paragraph's properties.</summary>
    public static TextBody Split(TextBody body, TextPosition position)
    {
        position = Clamp(body, position);
        var paragraph = body.Paragraphs[position.Paragraph];
        var (before, after) = SplitRuns(paragraph.Runs, position.Offset);
        var end = paragraph.EndProperties ?? (before.Count > 0 ? Plain(before[^1].Properties) : null);
        var first = paragraph with { Runs = before, EndProperties = after.Count == 0 && before.Count == 0 ? paragraph.EndProperties : end };
        var second = paragraph with { Runs = after, EndProperties = end };
        return body with { Paragraphs = body.Paragraphs.SetItem(position.Paragraph, first).Insert(position.Paragraph + 1, second) };
    }

    /// <summary>Deletes [<paramref name="start"/>, <paramref name="end"/>); paragraphs in between are joined into the first.</summary>
    public static TextBody Delete(TextBody body, TextPosition start, TextPosition end)
    {
        start = Clamp(body, start);
        end = Clamp(body, end);
        if (end <= start)
        {
            return body;
        }

        var first = body.Paragraphs[start.Paragraph];
        var last = body.Paragraphs[end.Paragraph];
        var (keep, _) = SplitRuns(first.Runs, start.Offset);
        var (_, rest) = SplitRuns(last.Runs, end.Offset);
        var joined = first with
        {
            Runs = Merge([.. keep, .. rest]),
            EndProperties = last.EndProperties ?? first.EndProperties,
        };
        return body with
        {
            Paragraphs = body.Paragraphs.RemoveRange(start.Paragraph, end.Paragraph - start.Paragraph + 1).Insert(start.Paragraph, joined),
        };
    }

    /// <summary>Applies <paramref name="change"/> to the runs in [<paramref name="start"/>, <paramref name="end"/>) (and the end properties of fully covered paragraphs).</summary>
    public static TextBody FormatRuns(TextBody body, TextPosition start, TextPosition end, Func<RunProperties, RunProperties> change)
    {
        start = Clamp(body, start);
        end = Clamp(body, end);
        var paragraphs = body.Paragraphs.ToList();
        for (var index = start.Paragraph; index <= end.Paragraph; index++)
        {
            var paragraph = paragraphs[index];
            var from = index == start.Paragraph ? start.Offset : 0;
            var to = index == end.Paragraph ? end.Offset : paragraph.Length;
            var (before, rest) = SplitRuns(paragraph.Runs, from);
            var (middle, after) = SplitRuns(rest, to - from);
            var changed = middle.Select(run => WithProperties(run, change(run.Properties))).ToList();
            var coversEnd = to >= paragraph.Length;
            paragraphs[index] = paragraph with
            {
                Runs = Merge([.. before, .. changed, .. after]),
                EndProperties = coversEnd && (from == 0 || index > start.Paragraph || paragraph.Length == 0) ? change(paragraph.EndProperties ?? RunProperties.Empty) : paragraph.EndProperties,
            };
        }

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>Applies <paramref name="change"/> to the paragraph properties of paragraphs <paramref name="first"/>..<paramref name="last"/>.</summary>
    public static TextBody FormatParagraphs(TextBody body, int first, int last, Func<ParagraphProperties, ParagraphProperties> change)
    {
        var paragraphs = body.Paragraphs.ToList();
        for (var index = Math.Max(0, first); index <= Math.Min(last, paragraphs.Count - 1); index++)
        {
            paragraphs[index] = paragraphs[index] with { Properties = change(paragraphs[index].Properties) };
        }

        return body with { Paragraphs = paragraphs };
    }

    /// <summary>The runs of [<paramref name="start"/>, <paramref name="end"/>) (split at the bounds).</summary>
    public static IReadOnlyList<TextRun> RunsIn(TextBody body, TextPosition start, TextPosition end)
    {
        var result = new List<TextRun>();
        for (var index = start.Paragraph; index <= end.Paragraph && index < body.Paragraphs.Count; index++)
        {
            var paragraph = body.Paragraphs[index];
            var from = index == start.Paragraph ? start.Offset : 0;
            var to = index == end.Paragraph ? end.Offset : paragraph.Length;
            var (_, rest) = SplitRuns(paragraph.Runs, from);
            var (middle, _) = SplitRuns(rest, to - from);
            result.AddRange(middle);
        }

        return result;
    }

    /// <summary>The text of [<paramref name="start"/>, <paramref name="end"/>) ("\n" between paragraphs).</summary>
    public static string TextIn(TextBody body, TextPosition start, TextPosition end)
    {
        var parts = new List<string>();
        for (var index = start.Paragraph; index <= end.Paragraph && index < body.Paragraphs.Count; index++)
        {
            var text = body.Paragraphs[index].Text;
            var from = index == start.Paragraph ? Math.Min(start.Offset, text.Length) : 0;
            var to = index == end.Paragraph ? Math.Min(end.Offset, text.Length) : text.Length;
            parts.Add(text[from..Math.Max(from, to)]);
        }

        return string.Join("\n", parts);
    }

    /// <summary>A copy of [<paramref name="start"/>, <paramref name="end"/>) as its own paragraphs (clipboard).</summary>
    public static IReadOnlyList<TextParagraph> Slice(TextBody body, TextPosition start, TextPosition end)
    {
        var result = new List<TextParagraph>();
        for (var index = start.Paragraph; index <= end.Paragraph && index < body.Paragraphs.Count; index++)
        {
            var paragraph = body.Paragraphs[index];
            var from = index == start.Paragraph ? start.Offset : 0;
            var to = index == end.Paragraph ? end.Offset : paragraph.Length;
            var (_, rest) = SplitRuns(paragraph.Runs, from);
            var (middle, _) = SplitRuns(rest, to - from);
            result.Add(paragraph with { Runs = middle });
        }

        return result;
    }

    /// <summary>Inserts whole paragraphs at a position (paste of formatted text): the first joins the paragraph at the position.</summary>
    public static (TextBody Body, TextPosition End) InsertParagraphs(TextBody body, TextPosition position, IReadOnlyList<TextParagraph> paragraphs)
    {
        if (paragraphs.Count == 0)
        {
            return (body, position);
        }

        position = Clamp(body, position);
        var current = body;
        var at = position;
        for (var index = 0; index < paragraphs.Count; index++)
        {
            if (index > 0)
            {
                current = Split(current, at);
                at = new TextPosition(at.Paragraph + 1, 0);
                current = FormatParagraphs(current, at.Paragraph, at.Paragraph, _ => paragraphs[index].Properties);
            }

            var paragraph = current.Paragraphs[at.Paragraph];
            var (before, after) = SplitRuns(paragraph.Runs, at.Offset);
            current = current with { Paragraphs = current.Paragraphs.SetItem(at.Paragraph, paragraph with { Runs = Merge([.. before, .. paragraphs[index].Runs, .. after]) }) };
            at = new TextPosition(at.Paragraph, at.Offset + paragraphs[index].Length);
        }

        return (current, at);
    }

    private static TextRun WithProperties(TextRun run, RunProperties properties) => run switch
    {
        TextSpan span => span with { RunProperties = properties },
        LineBreak lineBreak => lineBreak with { RunProperties = properties },
        TextField field => field with { RunProperties = properties },
        _ => run,
    };

    /// <summary>Splits runs at a character offset (fields and kept runs are not split: they go to the side their start is on).</summary>
    private static (List<TextRun> Before, List<TextRun> After) SplitRuns(IEnumerable<TextRun> runs, int offset)
    {
        var before = new List<TextRun>();
        var after = new List<TextRun>();
        var position = 0;
        foreach (var run in runs)
        {
            var length = run.Text.Length;
            if (position + length <= offset)
            {
                before.Add(run);
            }
            else if (position >= offset)
            {
                after.Add(run);
            }
            else if (run is TextSpan span)
            {
                var cut = offset - position;
                before.Add(span with { Content = span.Content[..cut] });
                after.Add(span with { Content = span.Content[cut..] });
            }
            else
            {
                before.Add(run);
            }

            position += length;
        }

        return (before, after);
    }

    /// <summary>Joins neighbouring text spans with equal properties and drops empty ones.</summary>
    private static ValueList<TextRun> Merge(IEnumerable<TextRun> runs)
    {
        var result = new List<TextRun>();
        foreach (var run in runs)
        {
            if (run is TextSpan { Content.Length: 0 })
            {
                continue;
            }

            if (run is TextSpan span && result.Count > 0 && result[^1] is TextSpan previous && previous.RunProperties == span.RunProperties)
            {
                result[^1] = previous with { Content = previous.Content + span.Content };
                continue;
            }

            result.Add(run);
        }

        return result;
    }
}

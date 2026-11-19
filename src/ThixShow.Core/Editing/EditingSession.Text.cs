using ThixOffice.Text.Unicode;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>Caret movements the core knows without a layout (lines are the editor's: it hit-tests the text layout).</summary>
public enum CaretMovement
{
    Left,
    Right,
    WordLeft,
    WordRight,
    ParagraphStart,
    ParagraphEnd,
    TextStart,
    TextEnd,
}

/// <summary>A value that is the same across the selection, or mixed (<see cref="IsMixed"/>).</summary>
public readonly record struct Mixed<T>(T? Value, bool IsMixed)
{
    public bool Is(T value) => !IsMixed && EqualityComparer<T?>.Default.Equals(Value, value);

    public static Mixed<T> Of(IEnumerable<T?> values)
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count switch
        {
            0 => new Mixed<T>(default, false),
            1 => new Mixed<T>(distinct[0], false),
            _ => new Mixed<T>(default, true),
        };
    }
}

/// <summary>The effective formatting of the selection for the ribbon (mixed values are <see cref="Mixed{T}.IsMixed"/>).</summary>
public sealed record SelectionFormatting(
    Mixed<string> FontFamily,
    Mixed<double> FontSize,
    Mixed<bool> Bold,
    Mixed<bool> Italic,
    Mixed<bool> Underline,
    Mixed<bool> Strikethrough,
    Mixed<string> TextColor,
    Mixed<TextAlignment> Alignment,
    Mixed<string> BulletKind,
    Mixed<int> Level,
    Mixed<TextAnchor> Anchor);

/// <summary>Options of find and replace.</summary>
public sealed record FindOptions(string Text, bool MatchCase = false, bool WholeWords = false, bool IncludeNotes = false);

public sealed partial class EditingSession
{
    // ------------------------------------------------------------ typing

    /// <summary>Types <paramref name="text"/> at the caret (replacing a selection); typing continues one undo step.</summary>
    public void InsertText(string text)
    {
        if (Selection.Text is not { } selection || text.Length == 0)
        {
            return;
        }

        var slideId = RequireSlide();
        var pending = PendingFormat;
        Apply("Eingabe", presentation =>
        {
            var result = presentation;
            result = WithText(result, slideId, selection.Target, body =>
            {
                var withoutSelection = TextEdits.Delete(body, selection.Start, selection.End);
                var format = pending ?? TextEdits.PropertiesAt(withoutSelection, selection.Start);
                return TextEdits.Insert(withoutSelection, selection.Start, text, format).Body;
            });
            return result;
        }, after =>
        {
            var body = TextBodyOf(after, slideId, selection.Target)!;
            var end = Advance(body, selection.Start, text);
            return Selection with { Text = TextSelection.Caret(selection.Target, end) };
        }, EditKind.Typing);
        PendingFormat = null;
    }

    private static TextPosition Advance(TextBody body, TextPosition start, string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return lines.Length == 1
            ? TextEdits.Clamp(body, new TextPosition(start.Paragraph, start.Offset + text.Length))
            : TextEdits.Clamp(body, new TextPosition(start.Paragraph + lines.Length - 1, lines[^1].Length));
    }

    /// <summary>Eingabe: a new paragraph (with the same properties).</summary>
    public void InsertParagraphBreak()
    {
        if (Selection.Text is not { } selection)
        {
            return;
        }

        var slideId = RequireSlide();
        Apply("Eingabe", presentation => WithText(presentation, slideId, selection.Target, body =>
                TextEdits.Split(TextEdits.Delete(body, selection.Start, selection.End), selection.Start)),
            _ => Selection with { Text = TextSelection.Caret(selection.Target, new TextPosition(selection.Start.Paragraph + 1, 0)) }, EditKind.Typing);
    }

    /// <summary>Umschalt+Eingabe: a line break inside the paragraph.</summary>
    public void InsertLineBreak() => InsertText(LineBreak.Character.ToString());

    /// <summary>Rücktaste: deletes the selection or the character (word) before the caret; at a paragraph start joins it to the one before.</summary>
    public void DeleteBackward(bool word = false)
    {
        if (Selection.Text is not { } selection || TextBodyOf(selection.Target) is not { } body)
        {
            return;
        }

        var (start, end) = (selection.Start, selection.End);
        if (selection.IsCollapsed)
        {
            if (start.Offset == 0 && start.Paragraph == 0)
            {
                return;
            }

            start = word ? Moved(body, start, CaretMovement.WordLeft) : Moved(body, start, CaretMovement.Left);
        }

        DeleteRange(selection.Target, start, end, EditKind.DeleteBackward);
    }

    /// <summary>Entf: deletes the selection or the character (word) after the caret; at a paragraph end joins the next one.</summary>
    public void DeleteForward(bool word = false)
    {
        if (Selection.Text is not { } selection || TextBodyOf(selection.Target) is not { } body)
        {
            return;
        }

        var (start, end) = (selection.Start, selection.End);
        if (selection.IsCollapsed)
        {
            end = word ? Moved(body, end, CaretMovement.WordRight) : Moved(body, end, CaretMovement.Right);
            if (end == start)
            {
                return;
            }
        }

        DeleteRange(selection.Target, start, end, EditKind.DeleteForward);
    }

    private void DeleteRange(TextTarget target, TextPosition start, TextPosition end, EditKind kind)
    {
        var slideId = RequireSlide();
        Apply("Löschen", presentation => WithText(presentation, slideId, target, body => TextEdits.Delete(body, start, end)),
            _ => Selection with { Text = TextSelection.Caret(target, start) }, kind);
    }

    // ------------------------------------------------------------ caret

    /// <summary>Moves the caret (or extends the selection with Umschalt).</summary>
    public void MoveCaret(CaretMovement movement, bool extend = false)
    {
        if (Selection.Text is not { } selection || TextBodyOf(selection.Target) is not { } body)
        {
            return;
        }

        CloseOpenStep();
        TextPosition target;
        if (!extend && !selection.IsCollapsed && movement is CaretMovement.Left or CaretMovement.Right)
        {
            target = movement == CaretMovement.Left ? selection.Start : selection.End;
        }
        else
        {
            target = Moved(body, selection.Active, movement);
        }

        SelectText(selection.Target, extend ? selection.Anchor : target, target);
    }

    /// <summary>Puts the caret at a position (the editor's click / line movement after hit-testing its layout).</summary>
    public void SetCaret(TextTarget target, TextPosition position, bool extend = false)
    {
        CloseOpenStep();
        var anchor = extend && Selection.Text is { } current && current.Target == target ? current.Anchor : position;
        SelectText(target, anchor, position);
    }

    /// <summary>Selects the word at a position (double click).</summary>
    public void SelectWord(TextTarget target, TextPosition position)
    {
        if (TextBodyOf(target) is not { } body)
        {
            return;
        }

        var text = body.Paragraphs[Math.Clamp(position.Paragraph, 0, body.Paragraphs.Count - 1)].Text;
        var (start, end) = TextBoundaries.WordAt(text, Math.Clamp(position.Offset, 0, text.Length));
        SelectText(target, new TextPosition(position.Paragraph, start), new TextPosition(position.Paragraph, end));
    }

    /// <summary>Selects a whole paragraph (triple click).</summary>
    public void SelectParagraph(TextTarget target, int paragraph)
    {
        if (TextBodyOf(target) is not { } body)
        {
            return;
        }

        paragraph = Math.Clamp(paragraph, 0, body.Paragraphs.Count - 1);
        SelectText(target, new TextPosition(paragraph, 0), new TextPosition(paragraph, body.Paragraphs[paragraph].Length));
    }

    /// <summary>Selects all text of the shape being edited (Strg+A while editing).</summary>
    public void SelectAllText()
    {
        if (Selection.Text is { } selection && TextBodyOf(selection.Target) is { } body)
        {
            SelectText(selection.Target, new TextPosition(0, 0), TextEdits.End(body));
        }
    }

    private static TextPosition Moved(TextBody body, TextPosition position, CaretMovement movement)
    {
        var paragraph = body.Paragraphs[position.Paragraph];
        var text = paragraph.Text;
        switch (movement)
        {
            case CaretMovement.Left when position.Offset > 0:
                return position with { Offset = TextBoundaries.PreviousGrapheme(text, position.Offset) };
            case CaretMovement.Left when position.Paragraph > 0:
                return new TextPosition(position.Paragraph - 1, body.Paragraphs[position.Paragraph - 1].Length);
            case CaretMovement.Right when position.Offset < text.Length:
                return position with { Offset = TextBoundaries.NextGrapheme(text, position.Offset) };
            case CaretMovement.Right when position.Paragraph < body.Paragraphs.Count - 1:
                return new TextPosition(position.Paragraph + 1, 0);
            case CaretMovement.WordLeft when position.Offset > 0:
                return position with { Offset = TextBoundaries.PreviousWordStart(text, position.Offset) };
            case CaretMovement.WordLeft:
                return Moved(body, position, CaretMovement.Left);
            case CaretMovement.WordRight when position.Offset < text.Length:
                return position with { Offset = TextBoundaries.NextWordStart(text, position.Offset) };
            case CaretMovement.WordRight:
                return Moved(body, position, CaretMovement.Right);
            case CaretMovement.ParagraphStart:
                return position with { Offset = 0 };
            case CaretMovement.ParagraphEnd:
                return position with { Offset = text.Length };
            case CaretMovement.TextStart:
                return new TextPosition(0, 0);
            case CaretMovement.TextEnd:
                return TextEdits.End(body);
            default:
                return position;
        }
    }

    // ------------------------------------------------------------ character formatting

    public void ToggleBold() => SetBold(!GetFormatting().Bold.Is(true));

    public void ToggleItalic() => SetItalic(!GetFormatting().Italic.Is(true));

    public void ToggleUnderline() => FormatRuns("Unterstrichen", run => run with { Underline = GetFormatting().Underline.Is(true) ? "none" : "sng" });

    public void ToggleStrikethrough() => FormatRuns("Durchgestrichen", run => run with { Strike = GetFormatting().Strikethrough.Is(true) ? "noStrike" : "sngStrike" });

    public void SetBold(bool bold) => FormatRuns("Fett", run => run with { Bold = bold });

    public void SetItalic(bool italic) => FormatRuns("Kursiv", run => run with { Italic = italic });

    public void SetFontFamily(string family) => FormatRuns("Schriftart", run => run with { Latin = new TextFont(family), EastAsian = run.EastAsian is null ? null : new TextFont(family), ComplexScript = run.ComplexScript is null ? null : new TextFont(family) });

    public void SetFontSize(double points) => FormatRuns("Schriftgrad", run => run with { Size = (int)Math.Round(Math.Clamp(points, 1, 4000) * 100) });

    /// <summary>Schrift vergrößern / verkleinern: to the next size of the list (by every run's own effective size).</summary>
    public void GrowFont(bool grow = true)
    {
        double[] sizes = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96];
        var current = GetFormatting().FontSize.Value;
        if (current <= 0)
        {
            current = 18;
        }

        var next = grow ? sizes.FirstOrDefault(size => size > current + 0.01, current + 4) : sizes.LastOrDefault(size => size < current - 0.01, Math.Max(1, current - 4));
        SetFontSize(next);
    }

    public void SetTextColor(ColorSpec? color) => FormatRuns("Schriftfarbe", run => run with { Fill = color is null ? null : new SolidFill(color) });

    public void SetHighlight(ColorSpec? color) => FormatRuns("Texthervorhebungsfarbe", run => run with { Highlight = color });

    public void SetBaseline(int baseline) => FormatRuns(baseline > 0 ? "Hochgestellt" : baseline < 0 ? "Tiefgestellt" : "Grundlinie", run => run with { Baseline = baseline == 0 ? null : baseline });

    public void SetCharacterSpacing(double points) => FormatRuns("Zeichenabstand", run => run with { Spacing = (int)Math.Round(points * 100) });

    /// <summary>Formatierung löschen: the runs keep only their language and links.</summary>
    public void ClearCharacterFormatting() => FormatRuns("Formatierung löschen", run => new RunProperties(Language: run.Language, Click: run.Click, Hover: run.Hover));

    /// <summary>Sets a link on the selected text (or the shapes' click action when shapes are selected).</summary>
    public void SetTextLink(Hyperlink? link)
    {
        if (Selection.Text is null)
        {
            SetShapeAction(link);
            return;
        }

        FormatRuns(link is null ? "Link entfernen" : "Link", run => run with { Click = link });
    }

    /// <summary>
    /// Formats the selected text's runs, or all text of the selected shapes; a caret alone sets the format the next typing gets (and
    /// an empty paragraph's end).
    /// </summary>
    public void FormatRuns(string label, Func<RunProperties, RunProperties> change)
    {
        var slideId = RequireSlide();
        if (Selection.Text is { } selection)
        {
            if (selection.IsCollapsed)
            {
                var body = TextBodyOf(selection.Target)!;
                PendingFormat = change(PendingFormat ?? TextEdits.PropertiesAt(body, selection.Start));
                if (body.Paragraphs[selection.Start.Paragraph].Length == 0)
                {
                    Apply(label, presentation => WithText(presentation, slideId, selection.Target, text => TextEdits.FormatRuns(text, selection.Start, selection.End, change)));
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            Apply(label, presentation => WithText(presentation, slideId, selection.Target, text => TextEdits.FormatRuns(text, selection.Start, selection.End, change)));
            return;
        }

        var targets = TextTargetsOfSelection();
        Apply(label, presentation => targets.Aggregate(presentation, (current, target) => WithText(current, slideId, target, text => TextEdits.FormatRuns(text, new TextPosition(0, 0), TextEdits.End(text), change))));
    }

    /// <summary>The text bodies of the selected shapes (a table: every cell).</summary>
    private List<TextTarget> TextTargetsOfSelection()
    {
        var targets = new List<TextTarget>();
        if (CurrentSlide is not { } slide)
        {
            return targets;
        }

        foreach (var id in SelectedShapeIds())
        {
            switch (slide.FindShape(id))
            {
                case AutoShape:
                    targets.Add(new TextTarget(id));
                    break;
                case GraphicFrame { Table: { } table }:
                    for (var row = 0; row < table.RowCount; row++)
                    {
                        for (var column = 0; column < table.ColumnCount; column++)
                        {
                            if (!table.CellAt(row, column).IsCovered)
                            {
                                targets.Add(new TextTarget(id, row, column));
                            }
                        }
                    }

                    break;
                case GroupShape group:
                    targets.AddRange(ShapeTree.All(group.Children).OfType<AutoShape>().Select(child => new TextTarget(child.Id)));
                    break;
            }
        }

        return targets;
    }

    // ------------------------------------------------------------ paragraph formatting

    public void SetAlignment(TextAlignment alignment) => FormatParagraphs("Ausrichtung", paragraph => paragraph with { Alignment = alignment });

    public void SetLineSpacing(TextSpacing spacing) => FormatParagraphs("Zeilenabstand", paragraph => paragraph with { LineSpacing = spacing });

    public void SetParagraphSpacing(TextSpacing? before, TextSpacing? after) =>
        FormatParagraphs("Absatzabstand", paragraph => paragraph with { SpaceBefore = before ?? paragraph.SpaceBefore, SpaceAfter = after ?? paragraph.SpaceAfter });

    /// <summary>Listenebene erhöhen (Tab at a paragraph start): one level deeper, up to level 9.</summary>
    public void IncreaseLevel() => FormatParagraphs("Listenebene erhöhen", paragraph => paragraph with { Level = Math.Min(8, (paragraph.Level ?? 0) + 1) });

    /// <summary>Listenebene verringern (Umschalt+Tab).</summary>
    public void DecreaseLevel() => FormatParagraphs("Listenebene verringern", paragraph =>
    {
        var level = Math.Max(0, (paragraph.Level ?? 0) - 1);
        return paragraph with { Level = level == 0 ? null : level };
    });

    /// <summary>Tab while editing: a level deeper at a paragraph start or with several paragraphs selected, else a tab character.</summary>
    public void HandleTab(bool shift)
    {
        if (Selection.Text is not { } selection)
        {
            return;
        }

        if (selection.Start.Offset == 0 || selection.Start.Paragraph != selection.End.Paragraph)
        {
            if (shift)
            {
                DecreaseLevel();
            }
            else
            {
                IncreaseLevel();
            }

            return;
        }

        if (!shift)
        {
            InsertText("\t");
        }
    }

    /// <summary>Aufzählungszeichen: on (a bullet "•") or off for the selected paragraphs.</summary>
    public void ToggleBullets()
    {
        var on = GetFormatting().BulletKind.Is("bullet");
        FormatParagraphs("Aufzählungszeichen", paragraph => on
            ? paragraph with { Bullet = NoBullet.Instance, Indent = null, LeftMargin = null }
            : paragraph with { Bullet = new CharacterBullet("•"), BulletFont = new TextFont("Arial", PitchFamily: "34", CharacterSet: "0"), LeftMargin = paragraph.LeftMargin ?? (228600 + ((paragraph.Level ?? 0) * 457200)), Indent = paragraph.Indent ?? -228600 });
    }

    /// <summary>Nummerierung: on (<paramref name="scheme"/>) or off for the selected paragraphs.</summary>
    public void ToggleNumbering(string scheme = "arabicPeriod")
    {
        var on = GetFormatting().BulletKind.Is("number");
        FormatParagraphs("Nummerierung", paragraph => on
            ? paragraph with { Bullet = NoBullet.Instance, Indent = null, LeftMargin = null }
            : paragraph with { Bullet = new AutoNumberBullet(scheme), BulletFont = null, LeftMargin = paragraph.LeftMargin ?? (342900 + ((paragraph.Level ?? 0) * 457200)), Indent = paragraph.Indent ?? -342900 });
    }

    /// <summary>Formats the selected paragraphs (or all paragraphs of the selected shapes).</summary>
    public void FormatParagraphs(string label, Func<ParagraphProperties, ParagraphProperties> change)
    {
        var slideId = RequireSlide();
        if (Selection.Text is { } selection)
        {
            Apply(label, presentation => WithText(presentation, slideId, selection.Target, text => TextEdits.FormatParagraphs(text, selection.Start.Paragraph, selection.End.Paragraph, change)));
            return;
        }

        var targets = TextTargetsOfSelection();
        Apply(label, presentation => targets.Aggregate(presentation, (current, target) => WithText(current, slideId, target, text => TextEdits.FormatParagraphs(text, 0, text.Paragraphs.Count - 1, change))));
    }

    /// <summary>Changes the body properties (anchor, autofit, insets, columns, direction) of the edited or selected shapes.</summary>
    public void SetBodyProperties(string label, Func<BodyProperties, BodyProperties> change)
    {
        var slideId = RequireSlide();
        var targets = Selection.Text is { } selection ? [selection.Target] : TextTargetsOfSelection().Where(target => !target.IsTableCell).ToList();
        Apply(label, presentation => targets.Aggregate(presentation, (current, target) => WithText(current, slideId, target, text => text with { Body = change(text.Body) })));
    }

    // ------------------------------------------------------------ formatting query

    private SelectionFormatting? _formatting;
    private (Presentation, ShowSelection, RunProperties?)? _formattingKey;

    /// <summary>The effective formatting of the selected text (or of all text in the selected shapes); cached until something changes.</summary>
    public SelectionFormatting GetFormatting()
    {
        var key = (Presentation, Selection, PendingFormat);
        if (_formatting is not null && _formattingKey is { } cached && ReferenceEquals(cached.Item1, key.Presentation) && cached.Item2 == key.Selection && cached.Item3 == key.PendingFormat)
        {
            return _formatting;
        }

        _formattingKey = key;
        _formatting = ComputeFormatting();
        return _formatting;
    }

    private SelectionFormatting ComputeFormatting()
    {
        var runs = new List<EffectiveRun>();
        var paragraphs = new List<ParagraphProperties>();
        var anchors = new List<TextAnchor>();
        if (CurrentSlide is not { } slide)
        {
            return Format(runs, paragraphs, anchors);
        }

        var context = SlideContext.For(Presentation, slide);
        void Collect(TextTarget target, TextPosition? start, TextPosition? end, bool caret)
        {
            if (TextBodyOf(target) is not { } body || slide.FindShape(target.ShapeId) is not { } shape)
            {
                return;
            }

            var resolved = target.IsTableCell
                ? new ResolvedShape(shape, new Transform2D(0, 0, 0, 0), ShapeGeometry.Rectangle, null, null, null, context.Colors, body.Body, [Presentation.DefaultTextStyle, body.ListStyle], null, body)
                : ShapeResolver.Resolve(context, shape);
            anchors.Add(resolved.Body.Anchor ?? TextAnchor.Top);
            var from = start ?? new TextPosition(0, 0);
            var to = end ?? TextEdits.End(body);
            for (var index = from.Paragraph; index <= to.Paragraph && index < body.Paragraphs.Count; index++)
            {
                var paragraph = ShapeResolver.ParagraphOf(resolved, body.Paragraphs[index]);
                paragraphs.Add(paragraph);
                if (caret)
                {
                    var properties = PendingFormat ?? TextEdits.PropertiesAt(body, from);
                    runs.Add(ShapeResolver.Effective(ShapeResolver.RunOf(paragraph, properties), context));
                    continue;
                }

                var inParagraph = TextEdits.RunsIn(body, index == from.Paragraph ? from : new TextPosition(index, 0), index == to.Paragraph ? to : new TextPosition(index, body.Paragraphs[index].Length))
                    .Where(run => run is not LineBreak).ToList();
                if (inParagraph.Count == 0)
                {
                    runs.Add(ShapeResolver.Effective(ShapeResolver.RunOf(paragraph, body.Paragraphs[index].EndProperties ?? RunProperties.Empty), context));
                }

                runs.AddRange(inParagraph.Select(run => ShapeResolver.Effective(ShapeResolver.RunOf(paragraph, run.Properties), context)));
            }
        }

        if (Selection.Text is { } selection)
        {
            Collect(selection.Target, selection.Start, selection.End, selection.IsCollapsed);
        }
        else
        {
            foreach (var target in TextTargetsOfSelection())
            {
                Collect(target, null, null, false);
            }
        }

        return Format(runs, paragraphs, anchors);
    }

    private static SelectionFormatting Format(List<EffectiveRun> runs, List<ParagraphProperties> paragraphs, List<TextAnchor> anchors) => new(
        Mixed<string>.Of(runs.Select(run => run.Family)),
        Mixed<double>.Of(runs.Select(run => Math.Round(run.Size, 1))),
        Mixed<bool>.Of(runs.Select(run => run.Bold)),
        Mixed<bool>.Of(runs.Select(run => run.Italic)),
        Mixed<bool>.Of(runs.Select(run => run.Underline)),
        Mixed<bool>.Of(runs.Select(run => run.Strike)),
        Mixed<string>.Of(runs.Select(run => run.Color.ToHex())),
        Mixed<TextAlignment>.Of(paragraphs.Select(paragraph => paragraph.Alignment ?? TextAlignment.Left)),
        Mixed<string>.Of(paragraphs.Select(paragraph => paragraph.Bullet switch
        {
            CharacterBullet or PictureBullet => "bullet",
            AutoNumberBullet => "number",
            _ => "none",
        })),
        Mixed<int>.Of(paragraphs.Select(paragraph => paragraph.Level ?? 0)),
        Mixed<TextAnchor>.Of(anchors));

    // ------------------------------------------------------------ find and replace

    /// <summary>
    /// Finds the next match after the selection (forward or back) across the slides' shapes, table cells and – with the option – notes,
    /// wrapping around; selects it and returns true, or false when there is none.
    /// </summary>
    public bool FindNext(FindOptions options, bool forward = true)
    {
        var matches = AllMatches(options);
        if (matches.Count == 0)
        {
            return false;
        }

        var current = CurrentMatchKey();
        var ordered = forward ? matches : Enumerable.Reverse(matches).ToList();
        var next = ordered.FirstOrDefault(match => forward ? Compare(match.Key, current) > 0 : Compare(match.Key, current) < 0) ?? ordered[0];
        SetSelection(new ShowSelection(next.SlideId, [next.SlideId], [next.Target.ShapeId], null, new TextSelection(next.Target, next.Start, next.End)));
        return true;
    }

    /// <summary>Replaces the selected match (if the selection is one) and finds the next.</summary>
    public bool Replace(FindOptions options, string replacement)
    {
        if (Selection.Text is { IsCollapsed: false } selection && Matches(TextEdits.TextIn(TextBodyOf(selection.Target)!, selection.Start, selection.End), options))
        {
            InsertText(replacement);
            CloseOpenStep();
        }

        return FindNext(options);
    }

    /// <summary>Replaces every match in the presentation as one step; returns how many.</summary>
    public int ReplaceAll(FindOptions options, string replacement)
    {
        var matches = AllMatches(options);
        if (matches.Count == 0)
        {
            return 0;
        }

        Apply("Alle ersetzen", presentation =>
        {
            var result = presentation;
            foreach (var match in Enumerable.Reverse(matches))
            {
                result = match.InNotes
                    ? WithSlide(result, match.SlideId, slide => slide with { Notes = ReplaceInNotes(slide.Notes!, match, replacement) })
                    : WithText(result, match.SlideId, match.Target, body =>
                    {
                        var format = TextEdits.PropertiesAt(body, new TextPosition(match.Start.Paragraph, match.Start.Offset + 1));
                        return TextEdits.Insert(TextEdits.Delete(body, match.Start, match.End), match.Start, replacement, format).Body;
                    });
            }

            return result;
        }, _ => Selection with { Text = null });
        return matches.Count;
    }

    private static NotesSlide ReplaceInNotes(NotesSlide notes, Match match, string replacement) => notes with
    {
        Shapes = ShapeTree.Replace(notes.Shapes, match.Target.ShapeId, shape => shape is AutoShape { Text: { } text } auto
            ? auto with { Text = TextEdits.Insert(TextEdits.Delete(text, match.Start, match.End), match.Start, replacement, TextEdits.PropertiesAt(text, match.Start)).Body }
            : shape),
    };

    private sealed record Match(uint SlideId, TextTarget Target, TextPosition Start, TextPosition End, bool InNotes, (int Slide, int Shape, int Row, int Column, int Paragraph, int Offset) Key);

    private List<Match> AllMatches(FindOptions options)
    {
        var matches = new List<Match>();
        if (options.Text.Length == 0)
        {
            return matches;
        }

        for (var slideIndex = 0; slideIndex < Presentation.Slides.Count; slideIndex++)
        {
            var slide = Presentation.Slides[slideIndex];
            var shapeIndex = 0;
            foreach (var shape in ShapeTree.All(slide.Shapes))
            {
                shapeIndex++;
                if (shape is AutoShape { Text: { } text })
                {
                    AddMatches(matches, slide.Id, new TextTarget(shape.Id), text, options, false, (slideIndex, shapeIndex, 0, 0));
                }
                else if (shape is GraphicFrame { Table: { } table })
                {
                    for (var row = 0; row < table.RowCount; row++)
                    {
                        for (var column = 0; column < table.ColumnCount; column++)
                        {
                            AddMatches(matches, slide.Id, new TextTarget(shape.Id, row, column), table.CellAt(row, column).Text, options, false, (slideIndex, shapeIndex, row, column));
                        }
                    }
                }
            }

            if (options.IncludeNotes && slide.Notes?.NotesBody is { Text: { } notes } body)
            {
                AddMatches(matches, slide.Id, new TextTarget(body.Id), notes, options, true, (slideIndex, int.MaxValue, 0, 0));
            }
        }

        return matches;
    }

    private static void AddMatches(List<Match> matches, uint slideId, TextTarget target, TextBody body, FindOptions options, bool notes, (int Slide, int Shape, int Row, int Column) key)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var paragraph = 0; paragraph < body.Paragraphs.Count; paragraph++)
        {
            var text = body.Paragraphs[paragraph].Text;
            var index = 0;
            while ((index = text.IndexOf(options.Text, index, comparison)) >= 0)
            {
                var end = index + options.Text.Length;
                if (!options.WholeWords || IsWholeWord(text, index, end))
                {
                    matches.Add(new Match(slideId, target, new TextPosition(paragraph, index), new TextPosition(paragraph, end), notes, (key.Slide, key.Shape, key.Row, key.Column, paragraph, index)));
                }

                index = end;
            }
        }
    }

    private static bool IsWholeWord(string text, int start, int end) =>
        (start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (end >= text.Length || !char.IsLetterOrDigit(text[end]));

    private static bool Matches(string text, FindOptions options) =>
        string.Equals(text, options.Text, options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private (int, int, int, int, int, int) CurrentMatchKey()
    {
        var slideIndex = Selection.CurrentSlide is { } id ? Presentation.IndexOf(id) : -1;
        if (Selection.Text is not { } text || CurrentSlide is not { } slide)
        {
            return (slideIndex, -1, 0, 0, 0, -1);
        }

        var shapeIndex = ShapeTree.All(slide.Shapes).Select((shape, index) => (shape, index)).FirstOrDefault(entry => entry.shape.Id == text.Target.ShapeId).index + 1;
        return (slideIndex, shapeIndex, text.Target.Row ?? 0, text.Target.Column ?? 0, text.Start.Paragraph, text.Start.Offset);
    }

    private static int Compare((int, int, int, int, int, int) first, (int, int, int, int, int, int) second) =>
        Comparer<(int, int, int, int, int, int)>.Default.Compare(first, second);
}

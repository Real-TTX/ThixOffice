using ThixOffice.Text.Unicode;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>How far Backspace / Delete reach.</summary>
public enum DeleteUnit
{
    Character,
    Word,
}

/// <summary>Which sections a page setup change applies to.</summary>
public enum PageSetupScope
{
    WholeDocument,
    CurrentSection,
}

/// <summary>A position the session keeps valid across edits (IME composition start, bookmarks of a view …).</summary>
public sealed class TrackedPosition(DocumentPosition position, Bias bias)
{
    public DocumentPosition Position { get; internal set; } = position;

    public Bias Bias { get; } = bias;
}

/// <summary>
/// The editing engine of one document: selection, every editing command as a reversible transaction, undo/redo (typing and
/// deleting group into one step while they continue), pending formatting at the caret, clipboard, find/replace and the cheap
/// formatting query of the ribbon. UI-free; the editor control forwards keys and mouse to it and asks the layout for geometry.
/// <para>
/// Events: <see cref="Document"/>.Changed for every block change (the layout re-lays only those), <see cref="ContentChanged"/>
/// once per finished command / undo / redo, <see cref="SelectionChanged"/>, and <see cref="StateChanged"/> whenever anything
/// the ribbon shows may have changed (then call <see cref="GetFormatting"/>).
/// </para>
/// </summary>
public sealed class EditingSession
{
    private readonly UndoHistory _history = new();
    private readonly List<TrackedPosition> _tracked = [];
    private readonly Func<DateTime> _clock;
    private EditTransaction? _open;
    private Selection _selection;
    private double? _goalX;
    private SelectionFormatting? _formatting;
    private (long Version, Selection Selection, CharacterProperties? Pending) _formattingKey;

    public EditingSession(Document document, Func<DateTime>? clock = null)
    {
        Document = document;
        Resolver = new StyleResolver(document);
        _clock = clock ?? (() => DateTime.UtcNow);
        _selection = Selection.Caret(CaretStops.FirstPosition(document));
    }

    /// <summary>Raised once after every command, undo and redo that changed the document.</summary>
    public event EventHandler? ContentChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>Anything the ribbon shows may have changed (selection, content, pending formatting, undo stack).</summary>
    public event EventHandler? StateChanged;

    public Document Document { get; }

    public StyleResolver Resolver { get; }

    public Selection Selection => _selection;

    /// <summary>The formatting the next typed text gets (set by formatting commands at a caret), or null = from the text.</summary>
    public CharacterProperties? PendingFormat { get; private set; }

    /// <summary>Answers line and page movements; set by the editor to its layout.</summary>
    public ICaretNavigator? Navigator { get; set; }

    /// <summary>Typing after a longer pause starts a new undo step.</summary>
    public TimeSpan TypingGroupPause { get; set; } = TimeSpan.FromSeconds(2);

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    // ---------------------------------------------------------------- selection

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="active"/> (snapped to caret stops).</summary>
    public void Select(DocumentPosition anchor, DocumentPosition active, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        _goalX = null;
        SetSelection(new Selection(CaretStops.Snap(Document, anchor), CaretStops.Snap(Document, active), affinity));
    }

    public void SetCaret(DocumentPosition position, CaretAffinity affinity = CaretAffinity.Downstream) => Select(position, position, affinity);

    /// <summary>Extends the selection from its anchor to <paramref name="active"/> (Shift+click, drag).</summary>
    public void ExtendTo(DocumentPosition active, CaretAffinity affinity = CaretAffinity.Downstream) => Select(_selection.Anchor, active, affinity);

    public void SelectAll() => SetSelection(new Selection(new DocumentPosition(0, 0), Document.End));

    /// <summary>Double click: the word at <paramref name="position"/> with the spaces after it.</summary>
    public void SelectWord(DocumentPosition position)
    {
        var at = CaretStops.Snap(Document, position);
        var text = ((Paragraph)Document.Blocks[at.Block]).Text;
        var (start, end) = TextBoundaries.WordAt(text, at.Offset);
        Select(at with { Offset = start }, at with { Offset = end });
    }

    /// <summary>Triple click: the paragraph at <paramref name="position"/> (with its mark, so formatting reaches it).</summary>
    public void SelectParagraph(DocumentPosition position)
    {
        var at = CaretStops.Snap(Document, position);
        var next = CaretStops.NextParagraphStart(Document, at.Block);
        Select(at with { Offset = 0 }, next ?? at with { Offset = Document.Blocks[at.Block].Length });
    }

    /// <summary>Selects a whole preserved block (a click on a table placeholder; Backspace / Delete next to one).</summary>
    public void SelectBlock(int block)
    {
        if (block < 0 || block >= Document.Blocks.Count || Document.Blocks[block] is Paragraph)
        {
            return;
        }

        var after = ContentEdits.NextParagraph(Document, block + 1);
        SetSelection(new Selection(new DocumentPosition(block, 0), new DocumentPosition(after, 0)));
    }

    /// <summary>Moves the caret (or the active end with <paramref name="extend"/>).</summary>
    public void Move(CaretMovement movement, bool extend = false)
    {
        var vertical = movement is CaretMovement.LineUp or CaretMovement.LineDown or CaretMovement.PageUp or CaretMovement.PageDown;
        if (!vertical)
        {
            _goalX = null;
        }

        if (!extend && !_selection.IsCollapsed && movement is CaretMovement.CharacterLeft or CaretMovement.CharacterRight)
        {
            var edge = movement == CaretMovement.CharacterLeft ? _selection.Start : _selection.End;
            SetSelection(Selection.Caret(edge));
            return;
        }

        var place = Target(movement, new CaretPlace(_selection.Active, _selection.Affinity));
        var anchor = extend ? _selection.Anchor : place.Position;
        SetSelection(new Selection(anchor, place.Position, place.Affinity), keepGoalX: vertical);
    }

    /// <summary>Keeps <paramref name="position"/> valid across edits until <see cref="Untrack"/>.</summary>
    public TrackedPosition Track(DocumentPosition position, Bias bias = Bias.Right)
    {
        var tracked = new TrackedPosition(position, bias);
        _tracked.Add(tracked);
        return tracked;
    }

    public void Untrack(TrackedPosition tracked) => _tracked.Remove(tracked);

    // ---------------------------------------------------------------- text

    /// <summary>Types <paramref name="text"/> over the selection (line breaks in it become paragraphs, like pasting).</summary>
    public void InsertText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            PasteText(text);
            return;
        }

        var continues = ContinuesTyping(EditKind.Typing);
        Run("Eingabe", EditKind.Typing, continues, () =>
        {
            var (properties, link) = TypingFormat();
            var map = Apply(ContentEdits.InsertText(Document, _selection.Range, text, properties, link));
            SetSelectionQuiet(Selection.Caret(((ReplaceMap)map).EndOfInserted));
        });
    }

    /// <summary>Enter: splits the paragraph (in an empty list item, ends the list instead).</summary>
    public void InsertParagraphBreak()
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block);
        if (_selection.IsCollapsed && paragraph is { Length: 0 } && Resolver.ResolveParagraph(paragraph).Numbering is not null)
        {
            RemoveList();
            return;
        }

        Run("Absatzmarke", EditKind.Other, null, () =>
        {
            var atEnd = paragraph is not null && _selection.End.Block == _selection.Start.Block && _selection.End.Offset == paragraph.Length;
            var map = (ReplaceMap)Apply(ContentEdits.SplitParagraph(Document, _selection.Range));
            var caret = map.EndOfInserted;
            if (atEnd && paragraph is not null)
            {
                var style = Document.Styles.Find(Resolver.ParagraphStyleId(paragraph));
                if (style?.Next is { } next && next != style.Id && Document.Styles.Find(next) is not null)
                {
                    var styleId = Document.Styles.Find(next)!.IsDefault ? null : next;
                    ApplyOptional(ContentEdits.FormatParagraphs(Document, caret.Block, caret.Block,
                        item => item.WithProperties(item.Properties with { StyleId = styleId })));
                }
            }

            SetSelectionQuiet(Selection.Caret(caret));
        });
    }

    /// <summary>Shift+Enter: a manual line break.</summary>
    public void InsertLineBreak() => InsertSpecial(SpecialCharacters.LineBreak.ToString(), "Zeilenumbruch");

    /// <summary>Tab in text.</summary>
    public void InsertTab() => InsertSpecial(SpecialCharacters.Tab.ToString(), "Tabstopp");

    /// <summary>Ctrl+Enter: a page break; the text after it starts a new paragraph on the next page.</summary>
    public void InsertPageBreak()
    {
        Run("Seitenumbruch", EditKind.Other, null, () =>
        {
            var (properties, _) = TypingFormat();
            var map = (ReplaceMap)Apply(ContentEdits.InsertText(Document, _selection.Range, SpecialCharacters.PageBreak.ToString(), properties));
            var split = (ReplaceMap)Apply(ContentEdits.SplitParagraph(Document, DocumentRange.At(map.EndOfInserted)));
            SetSelectionQuiet(Selection.Caret(split.EndOfInserted));
        });
    }

    /// <summary>Backspace.</summary>
    public void DeleteBackward(DeleteUnit unit = DeleteUnit.Character)
    {
        if (!_selection.IsCollapsed)
        {
            DeleteSelection();
            return;
        }

        var caret = _selection.Active;
        if (caret.Offset == 0)
        {
            DeleteBackwardAtParagraphStart(caret);
            return;
        }

        var start = unit == DeleteUnit.Word ? CaretStops.PreviousWord(Document, caret) : CaretStops.Previous(Document, caret);
        DeleteRange(new DocumentRange(start, caret), "Löschen", EditKind.DeleteBackward);
    }

    /// <summary>Delete (Entf).</summary>
    public void DeleteForward(DeleteUnit unit = DeleteUnit.Character)
    {
        if (!_selection.IsCollapsed)
        {
            DeleteSelection();
            return;
        }

        var caret = _selection.Active;
        var paragraph = (Paragraph)Document.Blocks[caret.Block];
        if (caret.Offset >= paragraph.Length)
        {
            var nextBlock = caret.Block + 1;
            if (nextBlock >= Document.Blocks.Count)
            {
                return;
            }

            if (Document.Blocks[nextBlock] is OpaqueBlock)
            {
                // A preserved block (table) is selected first, deleted by the next key press.
                SelectBlock(nextBlock);
                return;
            }

            DeleteRange(new DocumentRange(caret, new DocumentPosition(nextBlock, 0)), "Löschen", EditKind.DeleteForward);
            return;
        }

        var end = unit == DeleteUnit.Word
            ? caret with { Offset = TextBoundaries.NextWordStart(paragraph.Text, caret.Offset) }
            : CaretStops.Next(Document, caret);
        DeleteRange(new DocumentRange(caret, end), "Löschen", EditKind.DeleteForward);
    }

    /// <summary>Deletes the selection (nothing at a caret).</summary>
    public void DeleteSelection()
    {
        if (_selection.IsCollapsed)
        {
            return;
        }

        DeleteRange(_selection.Range, "Löschen", EditKind.Other);
    }

    // ---------------------------------------------------------------- character formatting

    public void ToggleBold() => SetBold(!Formatting().Bold.Is(true));

    public void ToggleItalic() => SetItalic(!Formatting().Italic.Is(true));

    public void ToggleUnderline() => SetUnderline(Formatting().Underline.Is(UnderlineStyle.None) ? UnderlineStyle.Single : UnderlineStyle.None);

    public void ToggleStrikethrough() =>
        FormatCharacters("Durchgestrichen", Flag(props => props.Strikethrough, (props, value) => props with { Strikethrough = value }, !Formatting().Strikethrough.Is(true)));

    public void ToggleSuperscript() =>
        SetVerticalPosition(Formatting().Superscript.Is(true) ? VerticalPosition.Baseline : VerticalPosition.Superscript);

    public void ToggleSubscript() =>
        SetVerticalPosition(Formatting().Subscript.Is(true) ? VerticalPosition.Baseline : VerticalPosition.Subscript);

    public void SetBold(bool bold) =>
        FormatCharacters("Fett", Flag(props => props.Bold, (props, value) => props with { Bold = value }, bold));

    public void SetItalic(bool italic) =>
        FormatCharacters("Kursiv", Flag(props => props.Italic, (props, value) => props with { Italic = value }, italic));

    public void SetUnderline(UnderlineStyle underline) =>
        FormatCharacters("Unterstrichen", Value(props => props.Underline, (props, value) => props with { Underline = value }, underline, UnderlineStyle.None));

    public void SetVerticalPosition(VerticalPosition position) =>
        FormatCharacters(position == VerticalPosition.Subscript ? "Tiefgestellt" : "Hochgestellt",
            Value(props => props.VerticalPosition, (props, value) => props with { VerticalPosition = value }, position, VerticalPosition.Baseline));

    public void SetFontFamily(string family) => FormatCharacters("Schriftart", (_, props) => props.WithFont(family));

    public void SetFontSize(double size) =>
        FormatCharacters("Schriftgrad", (_, props) => props with { FontSize = Math.Clamp(Math.Round(size * 2) / 2, 1, 1638) });

    /// <summary>Ctrl+> / Ctrl+&lt;: the next size of Word's size list.</summary>
    public void GrowFont(bool grow = true)
    {

        FormatCharacters(grow ? "Schrift vergrößern" : "Schrift verkleinern", (paragraph, props) =>
        {
            var current = Resolver.ResolveCharacter(paragraph, props).FontSize;
            return props with { FontSize = grow ? FontSizes.Next(current) : FontSizes.Previous(current) };
        });

    }

    /// <summary>A text colour "RRGGBB", or null for automatic.</summary>
    public void SetTextColor(string? color) =>
        FormatCharacters("Schriftfarbe", (_, props) => props with { Color = color is null ? null : TextColor.FromRgb(color) });

    public void SetHighlight(HighlightColor highlight) =>
        FormatCharacters("Texthervorhebungsfarbe", (_, props) => props with { Highlight = highlight == HighlightColor.None ? null : highlight });

    /// <summary>Any character formatting change (the general command behind the ribbon's font dialog).</summary>
    public void FormatCharacters(string label, Func<Paragraph, CharacterProperties, CharacterProperties> change)
    {
        if (!_selection.IsCollapsed)
        {
            Run(label, EditKind.Other, null, () => ApplyOptional(ContentEdits.FormatCharacters(Document, _selection.Range, change)));
            return;
        }

        var caret = _selection.Active;
        var paragraph = (Paragraph)Document.Blocks[caret.Block];
        var (wordStart, _) = TextBoundaries.WordAt(paragraph.Text, caret.Offset);
        var insideWord = paragraph.Length > 0 && caret.Offset > wordStart && caret.Offset < TextBoundaries.WordEnd(paragraph.Text, wordStart)
            && TextBoundaries.KindAt(paragraph.Text, caret.Offset) == WordCharKind.Word;
        if (insideWord)
        {
            // Like Word: a format at a caret inside a word formats the word.
            var range = new DocumentRange(caret with { Offset = wordStart }, caret with { Offset = TextBoundaries.WordEnd(paragraph.Text, wordStart) });
            Run(label, EditKind.Other, null, () => ApplyOptional(ContentEdits.FormatCharacters(Document, range, change)));

            return;
        }

        PendingFormat = change(paragraph, PendingFormat ?? paragraph.TypingPropertiesAt(caret.Offset));
        if (paragraph.Length == 0)
        {
            // The mark of an empty paragraph carries the formatting (its height follows it).
            Run(label, EditKind.Other, null, () => ApplyOptional(ContentEdits.FormatParagraphs(Document, caret.Block, caret.Block,
                item => item.WithMarkProperties(change(item, item.MarkProperties)))), keepPending: true);
            return;
        }

        RaiseState();
    }

    /// <summary>Ctrl+Space: removes the direct character formatting (keeps language and other unmodelled properties).</summary>
    public void ClearCharacterFormatting() =>
        FormatCharacters("Zeichenformatierung löschen", (_, props) => new CharacterProperties { UnknownXml = props.UnknownXml });

    /// <summary>"Alle Formatierungen löschen": character and paragraph formatting and the style go back to "Standard".</summary>
    public void ClearFormatting()
    {
        var (first, last) = SelectedBlocks();
        Run("Formatierung löschen", EditKind.Other, null, () =>
        {
            if (!_selection.IsCollapsed)
            {
                ApplyOptional(ContentEdits.FormatCharacters(Document, _selection.Range, (_, props) => new CharacterProperties { UnknownXml = props.UnknownXml }));
            }

            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item => item.WithProperties(ParagraphProperties.Empty)));
        });
        PendingFormat = null;
    }

    // ---------------------------------------------------------------- paragraph formatting

    public void SetAlignment(ParagraphAlignment alignment) =>
        FormatParagraphs("Ausrichtung", props => props with { Alignment = alignment });

    public void SetLineSpacing(LineSpacing spacing) => FormatParagraphs("Zeilenabstand", props => props with { LineSpacing = spacing });

    public void SetSpacing(double? before, double? after) =>
        FormatParagraphs("Abstand", props => props with { SpacingBefore = before ?? props.SpacingBefore, SpacingAfter = after ?? props.SpacingAfter });

    /// <summary>Indents in points (null = unchanged); <paramref name="firstLine"/> negative = hanging.</summary>
    public void SetIndents(double? left, double? right, double? firstLine) =>
        FormatParagraphs("Einzug", props => props with
        {
            IndentLeft = left ?? props.IndentLeft,
            IndentRight = right ?? props.IndentRight,
            FirstLineIndent = firstLine ?? props.FirstLineIndent,
        });

    public void SetKeepWithNext(bool value) => FormatParagraphs("Absatzkontrolle", props => props with { KeepWithNext = value });

    public void SetKeepLinesTogether(bool value) => FormatParagraphs("Absatzkontrolle", props => props with { KeepLinesTogether = value });

    public void SetPageBreakBefore(bool value) => FormatParagraphs("Seitenumbruch oberhalb", props => props with { PageBreakBefore = value });

    public void SetWidowControl(bool value) => FormatParagraphs("Absatzkontrolle", props => props with { WidowControl = value });

    /// <summary>Any paragraph formatting change for the paragraphs of the selection.</summary>
    public void FormatParagraphs(string label, Func<ParagraphProperties, ParagraphProperties> change)
    {
        var (first, last) = SelectedBlocks();
        Run(label, EditKind.Other, null, () =>
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item => item.WithProperties(change(item.Properties)))));
    }

    /// <summary>"Einzug vergrößern" / Tab in a list: list items one level deeper, other paragraphs 36 pt (to the next step).</summary>
    public void IncreaseIndent() => ChangeIndent(1);

    /// <summary>"Einzug verkleinern" / Shift+Tab in a list.</summary>
    public void DecreaseIndent() => ChangeIndent(-1);

    /// <summary>
    /// The Tab key: at the start of a list item or with several paragraphs selected it changes the level (Shift: back);
    /// otherwise it types a tab.
    /// </summary>
    public void HandleTab(bool shift)
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block);
        var multiple = _selection.Start.Block != _selection.End.Block;
        var atListStart = paragraph is not null && _selection.IsCollapsed && _selection.Start.Offset == 0
            && Resolver.ResolveParagraph(paragraph).Numbering is not null;
        if (multiple || atListStart)
        {
            ChangeIndent(shift ? -1 : 1);
            return;
        }

        if (!shift)
        {
            InsertTab();
        }
    }

    // ---------------------------------------------------------------- styles

    /// <summary>Applies a paragraph style to the selected paragraphs or a character style to the selected text (adds a missing built-in style).</summary>
    public void ApplyStyle(string styleId)
    {
        var style = Document.Styles.Find(styleId) ?? BuiltInStyles.Definition(styleId);
        if (style is null)
        {
            return;
        }

        if (style.Type == StyleType.Character)
        {
            var characterStyle = style.IsDefault ? null : style.Id;
            Run($"Formatvorlage {BuiltInStyles.DisplayName(style)}", EditKind.Other, null, () =>
            {
                EnsureStyle(style.Id);
                if (!_selection.IsCollapsed)
                {
                    ApplyOptional(ContentEdits.FormatCharacters(Document, _selection.Range, (_, props) => props with { StyleId = characterStyle }));
                }
            });
            if (_selection.IsCollapsed)
            {
                PendingFormat = (PendingFormat ?? TypingFormat().Properties) with { StyleId = characterStyle };
                RaiseState();
            }

            return;
        }

        if (style.Type != StyleType.Paragraph)
        {
            return;
        }

        var (first, last) = SelectedBlocks();
        Run($"Formatvorlage {BuiltInStyles.DisplayName(style)}", EditKind.Other, null, () =>
        {
            EnsureStyle(style.Id);
            var id = Document.Styles.Find(style.Id)!.IsDefault ? null : style.Id;
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item => item.WithProperties(item.Properties with { StyleId = id })));
        });
    }

    // ---------------------------------------------------------------- lists

    /// <summary>Bullets / numbering on (continuing an adjacent list of the same kind) or off when all selected paragraphs have it.</summary>
    public void ToggleList(ListKind kind)
    {
        var (first, last) = SelectedBlocks();
        var paragraphs = ParagraphsIn(first, last).ToList();
        var allInKind = paragraphs.Count > 0 && paragraphs.All(item => KindOf(item.Paragraph) == kind);
        if (allInKind)
        {
            RemoveList();
            return;
        }

        Run(kind == ListKind.Bullet ? "Aufzählungszeichen" : "Nummerierung", EditKind.Other, null, () =>
        {
            var numberingId = AdjacentList(first, last, kind) ?? CreateList(kind);
            EnsureStyle(BuiltInStyles.ListParagraph);
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item =>
            {
                var level = Resolver.ResolveParagraph(item).Numbering?.Level ?? 0;
                var styleId = item.Properties.StyleId is null or BuiltInStyles.Normal ? BuiltInStyles.ListParagraph : item.Properties.StyleId;
                return item.WithProperties(item.Properties with { Numbering = new NumberingReference(numberingId, level), StyleId = styleId });
            }));
        });
    }

    /// <summary>Takes the selected paragraphs out of their list.</summary>
    public void RemoveList()
    {
        var (first, last) = SelectedBlocks();
        Run("Liste entfernen", EditKind.Other, null, () =>
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item =>
            {
                var styleGivesList = Document.Styles.Chain(Resolver.ParagraphStyleId(item)).Any(style => style.Paragraph.Numbering is { IsNone: false });
                var styleId = item.Properties.StyleId == BuiltInStyles.ListParagraph ? null : item.Properties.StyleId;
                return item.WithProperties(item.Properties with { Numbering = styleGivesList ? NumberingReference.None : null, StyleId = styleId });
            })));
    }

    /// <summary>Sets the list level (0–8) of the selected list items.</summary>
    public void SetListLevel(int level)
    {
        var (first, last) = SelectedBlocks();
        Run("Listenebene", EditKind.Other, null, () =>
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item =>
                Resolver.ResolveParagraph(item).Numbering is { } numbering
                    ? item.WithProperties(item.Properties with { Numbering = numbering with { Level = Math.Clamp(level, 0, 8) } })
                    : item)));
    }

    /// <summary>"Neu beginnen mit 1": the list continues from the caret's paragraph as a new instance starting again.</summary>
    public void RestartNumbering()
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block);
        if (paragraph is null || Resolver.ResolveParagraph(paragraph).Numbering is not { } numbering
            || Document.Numbering.Instance(numbering.NumberingId) is not { } instance)
        {
            return;
        }

        var start = _selection.Start.Block;
        Run("Nummerierung neu beginnen", EditKind.Other, null, () =>
        {
            var definition = Document.Numbering.Definition(instance.AbstractId);
            var startValue = definition?.LevelAt(numbering.Level)?.Start ?? 1;
            var restarted = new NumberingInstance
            {
                Id = Document.Numbering.NextInstanceId(),
                AbstractId = instance.AbstractId,
                Overrides = [new LevelOverride(numbering.Level, startValue)],
            };
            Apply(DocumentSteps.Numbering(Document, Document.Numbering.WithInstance(restarted)));
            ApplyOptional(ContentEdits.FormatParagraphs(Document, start, Document.Blocks.Count - 1, item =>
                Resolver.ResolveParagraph(item).Numbering is { } current && current.NumberingId == numbering.NumberingId
                    ? item.WithProperties(item.Properties with { Numbering = current with { NumberingId = restarted.Id } })
                    : item));
        });
    }

    // ---------------------------------------------------------------- page setup

    /// <summary>"Seite einrichten": changes the page setup of the whole document or of the caret's section.</summary>
    public void SetPageSetup(Func<SectionProperties, SectionProperties> change, PageSetupScope scope = PageSetupScope.WholeDocument)
    {
        var section = Document.SectionOf(_selection.Start.Block);
        var isFinal = section.Index == Document.Sections.Count - 1;
        Run("Seite einrichten", EditKind.Other, null, () =>
        {
            if (scope == PageSetupScope.WholeDocument || isFinal)
            {
                var updated = change(Document.FinalSection);
                if (updated != Document.FinalSection)
                {
                    Apply(DocumentSteps.FinalSection(Document, updated));
                }
            }

            var (from, to) = scope == PageSetupScope.WholeDocument ? (0, Document.Blocks.Count - 1) : (section.EndBlock, section.EndBlock);
            ApplyOptional(ContentEdits.FormatParagraphs(Document, from, to, item =>
                item.SectionBreak is { } properties ? item.WithSectionBreak(change(properties)) : item));
        });
    }

    // ---------------------------------------------------------------- clipboard

    /// <summary>The selected content for the clipboard (null at a caret).</summary>
    public DocumentFragment? Copy() => _selection.IsCollapsed ? null : DocumentFragment.Copy(Document, _selection.Range);

    /// <summary>Copies and deletes the selection.</summary>
    public DocumentFragment? Cut()
    {
        var fragment = Copy();
        if (fragment is not null)
        {
            DeleteRange(_selection.Range, "Ausschneiden", EditKind.Other);
        }

        return fragment;
    }

    /// <summary>
    /// Pastes rich content over the selection. From another document, missing styles come along, lists get their own
    /// definitions and preserved content that refers to parts of the other file (images …) is left out.
    /// </summary>
    public void Paste(DocumentFragment fragment)
    {
        Run("Einfügen", EditKind.Other, null, () =>
        {
            var blocks = fragment.SourceDocumentId == Document.Id ? fragment.Blocks : Adopt(fragment);
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, blocks));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    /// <summary>Pastes plain text with the formatting of the place it goes to ("Nur den Text übernehmen").</summary>
    public void PasteText(string text)
    {
        var lines = DocumentFragment.FromPlainText(text).Blocks.Cast<Paragraph>().ToList();
        Run("Einfügen", EditKind.Other, null, () =>
        {
            var (properties, _) = TypingFormat();
            var targetBlock = ContentEdits.NextParagraph(Document, ContentEdits.Normalize(Document, _selection.Range).From.Block);
            var target = (Paragraph)Document.Blocks[targetBlock];
            var blocks = lines.Select(line => (Block)new Paragraph(line.Text.Length == 0 ? [] : [new TextRun(line.Text, properties)], target.Properties, target.MarkProperties)).ToList();
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, blocks));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    // ---------------------------------------------------------------- find / replace

    /// <summary>Selects the next match after the selection (or before it, backwards); false when there is none.</summary>
    public bool FindNext(FindOptions options, bool forward = true)
    {
        var from = forward ? _selection.End : _selection.Start;
        var match = TextSearch.Find(Document, options, from, forward);
        if (match is not { } range)
        {
            return false;
        }

        Select(range.Start, range.End);
        return true;
    }

    /// <summary>Replaces the selection when it is a match, then selects the next match; false when no match remains.</summary>
    public bool Replace(FindOptions options, string replacement)
    {
        if (TextSearch.IsMatch(Document, _selection.Range, options))
        {
            var range = _selection.Range;
            Run("Ersetzen", EditKind.Other, null, () =>
            {
                var properties = ((Paragraph)Document.Blocks[range.Start.Block]).InlineAt(range.Start.Offset)?.Properties ?? CharacterProperties.Empty;
                var map = (ReplaceMap)Apply(ContentEdits.InsertText(Document, range, replacement, properties));
                SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
            });
        }

        return FindNext(options);
    }

    /// <summary>Replaces every match as ONE undo step; returns how many.</summary>
    public int ReplaceAll(FindOptions options, string replacement)
    {
        var matches = TextSearch.FindAll(Document, options);
        if (matches.Count == 0)
        {
            return 0;
        }

        Run("Alle ersetzen", EditKind.Other, null, () =>
        {
            for (var index = matches.Count - 1; index >= 0; index--)
            {
                var range = matches[index];
                var properties = ((Paragraph)Document.Blocks[range.Start.Block]).InlineAt(range.Start.Offset)?.Properties ?? CharacterProperties.Empty;
                Apply(ContentEdits.InsertText(Document, range, replacement, properties));
            }
        });
        return matches.Count;
    }

    // ---------------------------------------------------------------- undo

    public void Undo()
    {
        var transaction = _history.PopUndo();
        if (transaction is null)
        {
            return;
        }

        for (var index = transaction.Steps.Count - 1; index >= 0; index--)
        {
            MapTracked(transaction.Steps[index].Invert().Apply(Document));
        }

        PendingFormat = null;
        SetSelectionQuiet(Clamp(transaction.Before));
        Completed();
    }

    public void Redo()
    {
        var transaction = _history.PopRedo();
        if (transaction is null)
        {
            return;
        }

        foreach (var step in transaction.Steps)
        {
            MapTracked(step.Apply(Document));
        }

        PendingFormat = null;
        SetSelectionQuiet(Clamp(transaction.After));
        Completed();
    }

    /// <summary>Forgets the undo history (after loading a document).</summary>
    public void ClearHistory()
    {
        _history.Clear();
        RaiseState();
    }

    // ---------------------------------------------------------------- queries

    /// <summary>The formatting of the selection for the ribbon (cached until something changes).</summary>
    public SelectionFormatting GetFormatting() => Formatting();

    /// <summary>Words, characters, paragraphs of the document; <paramref name="pages"/> from the layout.</summary>
    public DocumentStatistics GetStatistics(int pages = 0) => StatisticsCounter.Count(Document, pages);

    /// <summary>The counts of the selection ("12 von 340 Wörtern").</summary>
    public DocumentStatistics GetSelectionStatistics() => StatisticsCounter.Count(Document, _selection.Range);

    // ---------------------------------------------------------------- internals

    private SelectionFormatting Formatting()
    {
        var key = (Document.Version, _selection, PendingFormat);
        if (_formatting is not null && _formattingKey == key)
        {
            return _formatting;
        }

        var built = SelectionFormattingBuilder.Build(Document, Resolver, _selection, PendingFormat) with
        {
            CanUndo = _history.CanUndo,
            CanRedo = _history.CanRedo,
            UndoLabel = _history.UndoLabel,
            RedoLabel = _history.RedoLabel,
        };
        _formatting = built;
        _formattingKey = key;
        return built;
    }

    private void InsertSpecial(string text, string label)
    {
        Run(label, EditKind.Other, null, () =>
        {
            var (properties, link) = TypingFormat();
            var map = (ReplaceMap)Apply(ContentEdits.InsertText(Document, _selection.Range, text, properties, link));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    private void DeleteBackwardAtParagraphStart(DocumentPosition caret)
    {
        var paragraph = (Paragraph)Document.Blocks[caret.Block];
        if (paragraph.Properties.Numbering is { IsNone: false } || (paragraph.Properties.Numbering is null && Resolver.ResolveParagraph(paragraph).Numbering is not null))
        {
            // Like Word: Backspace at the start of a list item removes its number first.
            RemoveList();
            return;
        }

        var previous = caret.Block - 1;
        if (previous < 0)
        {
            return;
        }

        if (Document.Blocks[previous] is OpaqueBlock)
        {
            SelectBlock(previous);
            return;
        }

        var end = new DocumentPosition(previous, Document.Blocks[previous].Length);
        DeleteRange(new DocumentRange(end, caret), "Löschen", EditKind.DeleteBackward);
    }

    private void DeleteRange(DocumentRange range, string label, EditKind kind)
    {
        if (range.IsEmpty)
        {
            return;
        }

        var continues = kind is EditKind.DeleteBackward or EditKind.DeleteForward ? ContinuesTyping(kind) : null;
        Run(label, kind, continues, () =>
        {
            var map = (ReplaceMap)Apply(ContentEdits.Delete(Document, range));
            SetSelectionQuiet(Selection.Caret(CaretStops.Snap(Document, map.From)));
        });
    }

    private void ChangeIndent(int direction)
    {
        var (first, last) = SelectedBlocks();
        var defaultStop = Document.Settings.DefaultTabStop;
        Run(direction > 0 ? "Einzug vergrößern" : "Einzug verkleinern", EditKind.Other, null, () =>
            ApplyOptional(ContentEdits.FormatParagraphs(Document, first, last, item =>
            {
                var format = Resolver.ResolveParagraph(item);
                if (format.Numbering is { } numbering)
                {
                    var level = Math.Clamp(numbering.Level + direction, 0, 8);
                    return item.WithProperties(item.Properties with { Numbering = numbering with { Level = level } });
                }

                var step = Math.Max(defaultStop, 1);
                var indent = Math.Max(0, (Math.Floor(format.IndentLeft / step + 0.001) + direction) * step);
                return item.WithProperties(item.Properties with { IndentLeft = indent });
            })));
    }

    private (CharacterProperties Properties, Hyperlink? Link) TypingFormat()
    {
        var start = _selection.Start;
        var paragraph = Document.ParagraphAt(start.Block) ?? (Paragraph)Document.Blocks[ContentEdits.NextParagraph(Document, start.Block)];
        if (PendingFormat is not null && _selection.IsCollapsed)
        {
            return (PendingFormat, null);
        }

        var properties = _selection.IsCollapsed || start.Offset >= paragraph.Length
            ? paragraph.TypingPropertiesAt(start.Offset)
            : paragraph.InlineAt(start.Offset)?.Properties ?? paragraph.MarkProperties;

        // A link continues only when typing happens inside it.
        Hyperlink? link = null;
        if (start.Offset > 0 && start.Offset < paragraph.Length)
        {
            var before = paragraph.InlineAt(start.Offset - 1)?.Link;
            var after = paragraph.InlineAt(start.Offset)?.Link;
            link = before is not null && before == after ? before : null;
        }

        return (properties, link);
    }

    private EditTransaction? ContinuesTyping(EditKind kind)
    {
        var last = _history.Last;
        if (last is null || last.Kind != kind || _history.CanRedo || last.After != _selection || !_selection.IsCollapsed)
        {
            return null;
        }

        if (last.Block != _selection.Active.Block || _clock() - last.LastEdit > TypingGroupPause || PendingFormat is not null && kind == EditKind.Typing)
        {
            return null;
        }

        return last;
    }

    private Func<Paragraph, CharacterProperties, CharacterProperties> Flag(
        Func<CharacterProperties, bool?> get,
        Func<CharacterProperties, bool?, CharacterProperties> set,
        bool value) =>
        (paragraph, props) =>
        {
            var inherited = get(Resolver.InheritedCharacter(Resolver.ParagraphStyleId(paragraph), props.StyleId)) ?? false;
            return set(props, inherited == value ? null : value);
        };

    private Func<Paragraph, CharacterProperties, CharacterProperties> Value<T>(
        Func<CharacterProperties, T?> get,
        Func<CharacterProperties, T?, CharacterProperties> set,
        T value,
        T defaultValue)
        where T : struct =>
        (paragraph, props) =>
        {
            var inherited = get(Resolver.InheritedCharacter(Resolver.ParagraphStyleId(paragraph), props.StyleId)) ?? defaultValue;
            return set(props, EqualityComparer<T>.Default.Equals(inherited, value) ? null : value);
        };

    private (int First, int Last) SelectedBlocks()
    {
        var start = _selection.Start;
        var end = _selection.End;
        var last = end.Block;
        if (end.Offset == 0 && end.Block > start.Block)
        {
            // A selection that ends at the start of a paragraph does not format that paragraph (triple click).
            last--;
        }

        return (start.Block, last);
    }

    private IEnumerable<(int Block, Paragraph Paragraph)> ParagraphsIn(int first, int last)
    {
        for (var index = first; index <= last; index++)
        {
            if (Document.Blocks[index] is Paragraph paragraph)
            {
                yield return (index, paragraph);
            }
        }
    }

    private ListKind? KindOf(Paragraph paragraph) => SelectionFormattingBuilder.ListKindOf(Document, Resolver.ResolveParagraph(paragraph));

    private int? AdjacentList(int first, int last, ListKind kind)
    {
        var before = ContentEdits.PreviousParagraph(Document, first - 1);
        if (before >= 0 && Document.Blocks[before] is Paragraph previous && KindOf(previous) == kind)
        {
            return Resolver.ResolveParagraph(previous).Numbering!.NumberingId;
        }

        var after = last + 1 < Document.Blocks.Count ? ContentEdits.NextParagraph(Document, last + 1) : -1;
        if (after > last && Document.Blocks[after] is Paragraph next && KindOf(next) == kind)
        {
            return Resolver.ResolveParagraph(next).Numbering!.NumberingId;
        }

        return null;
    }

    private int CreateList(ListKind kind)
    {
        var definition = BuiltInLists.Create(kind, Document.Numbering.NextDefinitionId());
        var numbering = Document.Numbering.WithDefinition(definition);
        var instance = new NumberingInstance { Id = numbering.NextInstanceId(), AbstractId = definition.Id };
        Apply(DocumentSteps.Numbering(Document, numbering.WithInstance(instance)));
        return instance.Id;
    }

    private void EnsureStyle(string styleId)
    {
        var styles = BuiltInStyles.Ensure(Document.Styles, styleId);
        if (!ReferenceEquals(styles, Document.Styles))
        {
            Apply(DocumentSteps.Styles(Document, styles));
        }
    }

    /// <summary>The fragment's blocks made fit for this document: styles added, lists re-created, foreign part references dropped.</summary>
    private IReadOnlyList<Block> Adopt(DocumentFragment fragment)
    {
        if (fragment.Styles is { } sourceStyles)
        {
            var styles = Document.Styles;
            foreach (var id in UsedStyles(fragment.Blocks))
            {
                foreach (var style in sourceStyles.Chain(id).Reverse())
                {
                    if (styles.Find(style.Id) is null)
                    {
                        styles = styles.With(style with { IsDefault = false });
                    }
                }
            }

            if (!ReferenceEquals(styles, Document.Styles))
            {
                Apply(DocumentSteps.Styles(Document, styles));
            }
        }

        var remap = new Dictionary<int, int>();
        if (fragment.Numbering is { } sourceNumbering)
        {
            var numbering = Document.Numbering;
            foreach (var numberingId in fragment.Blocks.OfType<Paragraph>().Select(item => item.Properties.Numbering?.NumberingId ?? 0).Where(id => id != 0).Distinct())
            {
                if (sourceNumbering.Instance(numberingId) is not { } instance || sourceNumbering.Definition(instance.AbstractId) is not { } definition)
                {
                    continue;
                }

                var copied = definition with { Id = numbering.NextDefinitionId() };
                numbering = numbering.WithDefinition(copied);
                var newInstance = instance with { Id = numbering.NextInstanceId(), AbstractId = copied.Id };
                numbering = numbering.WithInstance(newInstance);
                remap[numberingId] = newInstance.Id;
            }

            if (!ReferenceEquals(numbering, Document.Numbering))
            {
                Apply(DocumentSteps.Numbering(Document, numbering));
            }
        }

        var blocks = new List<Block>();
        foreach (var block in fragment.Blocks)
        {
            switch (block)
            {
                case OpaqueBlock opaque when ReferencesParts(opaque.Xml):
                    continue;
                case Paragraph paragraph:
                    var inlines = paragraph.Inlines.Where(inline => inline is not OpaqueInline opaque || !ReferencesParts(opaque.Xml));
                    var properties = paragraph.Properties;
                    if (properties.Numbering is { IsNone: false } numbering)
                    {
                        properties = properties with { Numbering = remap.TryGetValue(numbering.NumberingId, out var id) ? numbering with { NumberingId = id } : null };
                    }

                    blocks.Add(new Paragraph(inlines, properties, paragraph.MarkProperties, paragraph.SectionBreak));
                    break;
                default:
                    blocks.Add(block);
                    break;
            }
        }

        return blocks;
    }

    private static IEnumerable<string> UsedStyles(IEnumerable<Block> blocks)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paragraph in blocks.OfType<Paragraph>())
        {
            if (paragraph.Properties.StyleId is { } paragraphStyle)
            {
                ids.Add(paragraphStyle);
            }

            foreach (var inline in paragraph.Inlines)
            {
                if (inline.Properties.StyleId is { } characterStyle)
                {
                    ids.Add(characterStyle);
                }
            }
        }

        return ids;
    }

    private static bool ReferencesParts(string xml) =>
        xml.Contains("http://schemas.openxmlformats.org/officeDocument/2006/relationships", StringComparison.Ordinal);

    private Selection Clamp(Selection selection) =>
        new(CaretStops.Snap(Document, selection.Anchor), CaretStops.Snap(Document, selection.Active), selection.Affinity);

    private CaretPlace Target(CaretMovement movement, CaretPlace place)
    {
        var position = place.Position;
        switch (movement)
        {
            case CaretMovement.CharacterLeft:
                return new CaretPlace(CaretStops.Previous(Document, position));
            case CaretMovement.CharacterRight:
                return new CaretPlace(CaretStops.Next(Document, position));
            case CaretMovement.WordLeft:
                return new CaretPlace(CaretStops.PreviousWord(Document, position));
            case CaretMovement.WordRight:
                return new CaretPlace(CaretStops.NextWord(Document, position));
            case CaretMovement.ParagraphUp:
                return new CaretPlace(position.Offset > 0 ? position with { Offset = 0 } : StartOfPreviousParagraph(position));
            case CaretMovement.ParagraphDown:
                return new CaretPlace(CaretStops.NextParagraphStart(Document, position.Block) ?? Document.End);
            case CaretMovement.DocumentStart:
                return new CaretPlace(CaretStops.FirstPosition(Document));
            case CaretMovement.DocumentEnd:
                return new CaretPlace(Document.End);
            case CaretMovement.LineStart:
                return Navigator?.LineStart(place) ?? new CaretPlace(position with { Offset = 0 });
            case CaretMovement.LineEnd:
                return Navigator?.LineEnd(place) ?? new CaretPlace(position with { Offset = Document.Blocks[position.Block].Length });
            case CaretMovement.LineUp:
            case CaretMovement.LineDown:
            {
                var direction = movement == CaretMovement.LineUp ? -1 : 1;
                if (Navigator is null)
                {
                    return new CaretPlace(direction < 0 ? StartOfPreviousParagraph(position) : CaretStops.NextParagraphStart(Document, position.Block) ?? Document.End);
                }

                _goalX ??= Navigator.CaretX(place);
                return Navigator.MoveByLine(place, direction, _goalX.Value)
                    ?? new CaretPlace(direction < 0 ? CaretStops.FirstPosition(Document) : Document.End);
            }

            case CaretMovement.PageUp:
            case CaretMovement.PageDown:
            {
                var direction = movement == CaretMovement.PageUp ? -1 : 1;
                if (Navigator is null)
                {
                    return new CaretPlace(direction < 0 ? CaretStops.FirstPosition(Document) : Document.End);
                }

                _goalX ??= Navigator.CaretX(place);
                return Navigator.MoveByPage(place, direction, _goalX.Value);
            }

            default:
                return place;
        }
    }

    private DocumentPosition StartOfPreviousParagraph(DocumentPosition position)
    {
        var previous = ContentEdits.PreviousParagraph(Document, position.Block - 1);
        return previous < 0 ? CaretStops.FirstPosition(Document) : new DocumentPosition(previous, 0);
    }

    /// <summary>Runs a command as one transaction (or as more of <paramref name="continueWith"/>, the typing just before).</summary>
    private void Run(string label, EditKind kind, EditTransaction? continueWith, Action build, bool keepPending = false)
    {
        if (_open is not null)
        {
            build();
            return;
        }

        var transaction = continueWith ?? new EditTransaction(label, kind, _selection);
        var stepsBefore = transaction.Steps.Count;
        var selectionBefore = _selection;
        _open = transaction;
        try
        {
            build();
        }
        finally
        {
            _open = null;
        }

        if (transaction.Steps.Count == stepsBefore)
        {
            if (_selection != selectionBefore)
            {
                RaiseSelection();
            }

            return;
        }

        transaction.After = _selection;
        transaction.LastEdit = _clock();
        transaction.Block = _selection.Active.Block;
        if (continueWith is null)
        {
            _history.Push(transaction);
        }

        if (!keepPending)
        {
            PendingFormat = null;
        }

        Completed();
    }

    private PositionMap Apply(EditStep step)
    {
        var map = step.Apply(Document);
        _open?.Steps.Add(step);
        MapTracked(map);
        if (_open is not null)
        {
            _selection = new Selection(
                CaretStops.Snap(Document, map.Map(_selection.Anchor, Bias.Left)),
                CaretStops.Snap(Document, map.Map(_selection.Active, Bias.Right)),
                _selection.Affinity);
        }

        return map;
    }

    private void ApplyOptional(EditStep? step)
    {
        if (step is not null)
        {
            Apply(step);
        }
    }

    private void MapTracked(PositionMap map)
    {
        foreach (var tracked in _tracked)
        {
            tracked.Position = ContentEdits.Clamp(Document, map.Map(tracked.Position, tracked.Bias));
        }
    }

    private void SetSelection(Selection selection, bool keepGoalX = false)
    {
        if (!keepGoalX)
        {
            _goalX = null;
        }

        if (selection == _selection)
        {
            return;
        }

        _selection = selection;
        PendingFormat = null;
        RaiseSelection();
    }

    private void SetSelectionQuiet(Selection selection) => _selection = selection;

    private void Completed()
    {
        ContentChanged?.Invoke(this, EventArgs.Empty);
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        RaiseState();
    }

    private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Word's list of font sizes (the ribbon's size box, Ctrl+> / Ctrl+&lt;).</summary>
public static class FontSizes
{
    public static IReadOnlyList<double> Standard { get; } = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    public static double Next(double size)
    {
        foreach (var candidate in Standard)
        {
            if (candidate > size + 0.01)
            {
                return candidate;
            }
        }

        return Math.Min(1638, Math.Floor(size / 10 + 1) * 10);
    }

    public static double Previous(double size)
    {
        for (var index = Standard.Count - 1; index >= 0; index--)
        {
            if (Standard[index] < size - 0.01)
            {
                return Standard[index];
            }
        }

        return Math.Max(1, size - 1);
    }
}

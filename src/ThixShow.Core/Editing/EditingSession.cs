using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>What kind of edit a step is (typing and deleting group into one undo step while they continue).</summary>
public enum EditKind
{
    Other,
    Typing,
    DeleteBackward,
    DeleteForward,
    Nudge,
}

/// <summary>
/// What changed in the presentation: the slides whose content changed (redraw their thumbnail and view), the shapes per slide
/// (redraw little), whether the slide list changed (order, insert, delete, sections), and whether masters/layouts/themes or the
/// slide size changed (everything redraws).
/// </summary>
public sealed class PresentationChangedEventArgs(IReadOnlyList<uint> slides, IReadOnlyDictionary<uint, IReadOnlyList<int>> shapes, bool slideListChanged, bool designChanged) : EventArgs
{
    public IReadOnlyList<uint> ChangedSlides { get; } = slides;

    public IReadOnlyDictionary<uint, IReadOnlyList<int>> ChangedShapes { get; } = shapes;

    public bool SlideListChanged { get; } = slideListChanged;

    public bool DesignChanged { get; } = designChanged;
}

/// <summary>
/// The editing engine of a presentation, UI-free like ThixWrite's and ThixCalc's: the current <see cref="Presentation"/> (immutable),
/// the <see cref="Selection"/>, every command as one undoable step (a step keeps the presentation before and after it – undo is
/// exact by construction and cheap, unchanged slides share their instances), typing and nudging group while they continue, and
/// change events per slide and shape so views redraw little. The partial files hold the commands: slides and sections, shapes
/// (insert, transform, arrange, group, format), text, tables, design (themes, size, transitions, animations, notes), clipboard,
/// find/replace.
/// </summary>
public sealed partial class EditingSession
{
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private readonly Func<DateTime> _clock;
    private Step? _open;
    private int _groupDepth;
    private Presentation? _groupBefore;
    private ShowSelection? _groupSelection;
    private string? _groupLabel;

    public const int UndoLimit = 500;

    public EditingSession(Presentation presentation, Func<DateTime>? clock = null)
    {
        Presentation = presentation;
        _clock = clock ?? (() => DateTime.UtcNow);
        var first = presentation.Slides.FirstOrDefault()?.Id;
        Selection = first is { } id ? ShowSelection.OnSlide(id) : ShowSelection.Empty;
    }

    private sealed class Step(string label, EditKind kind, Presentation before, ShowSelection selectionBefore)
    {
        public string Label { get; } = label;

        public EditKind Kind { get; } = kind;

        public Presentation Before { get; } = before;

        public ShowSelection SelectionBefore { get; } = selectionBefore;

        public Presentation After { get; set; } = before;

        public ShowSelection SelectionAfter { get; set; } = selectionBefore;

        public DateTime LastEdit { get; set; }

        public TextTarget? Target { get; set; }
    }

    /// <summary>Raised after every change of the presentation (commands, undo, redo).</summary>
    public event EventHandler<PresentationChangedEventArgs>? PresentationChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>Raised when undo/redo availability or formatting queries may have changed (ribbon sync).</summary>
    public event EventHandler? StateChanged;

    public Presentation Presentation { get; private set; }

    public ShowSelection Selection { get; private set; }

    /// <summary>Formatting for the next typed character when the caret has no text around it (Strg+F on an empty caret).</summary>
    public RunProperties? PendingFormat { get; private set; }

    /// <summary>The pause after which typing starts a new undo step.</summary>
    public TimeSpan TypingGroupPause { get; set; } = TimeSpan.FromSeconds(2);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>The slide shown in the editor.</summary>
    public Slide? CurrentSlide => Selection.CurrentSlide is { } id ? Presentation.FindSlide(id) : null;

    /// <summary>The kind of the selection (for the ribbon's contextual tabs).</summary>
    public SelectionKind SelectionKind => Selection.Kind;

    // ------------------------------------------------------------ undo / redo

    public void Undo()
    {
        CloseOpenStep();
        if (_undo.Count == 0)
        {
            return;
        }

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        Replace(step.Before, step.SelectionBefore);
    }

    public void Redo()
    {
        CloseOpenStep();
        if (_redo.Count == 0)
        {
            return;
        }

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        Replace(step.After, step.SelectionAfter);
    }

    /// <summary>Ends the typing group: the next typing starts a new undo step.</summary>
    public void CloseOpenStep() => _open = null;

    /// <summary>
    /// Runs <paramref name="action"/> as ONE undo step labelled <paramref name="label"/> (several commands, e.g. a dialog's changes).
    /// Nested groups join the outermost.
    /// </summary>
    public void Group(string label, Action action)
    {
        if (_groupDepth++ == 0)
        {
            CloseOpenStep();
            _groupBefore = Presentation;
            _groupSelection = Selection;
            _groupLabel = label;
        }

        try
        {
            action();
        }
        finally
        {
            if (--_groupDepth == 0)
            {
                var before = _groupBefore!;
                _groupBefore = null;
                if (!ReferenceEquals(before, Presentation))
                {
                    Push(new Step(_groupLabel!, EditKind.Other, before, _groupSelection!) { After = Presentation, SelectionAfter = Selection });
                }

                CloseOpenStep();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Applies a change as an undo step. <paramref name="change"/> returns the new presentation (the same instance: nothing to do);
    /// <paramref name="selection"/> computes the selection after it. Typing/deleting/nudging of the same kind continue the open
    /// step while it is recent and on the same text.
    /// </summary>
    internal void Apply(string label, Func<Presentation, Presentation> change, Func<Presentation, ShowSelection>? selection = null, EditKind kind = EditKind.Other)
    {
        var before = Presentation;
        var after = change(before);
        if (ReferenceEquals(after, before))
        {
            return;
        }

        var selectionAfter = selection?.Invoke(after) ?? Validate(Selection, after);
        if (_groupDepth > 0)
        {
            Replace(after, selectionAfter);
            return;
        }

        var now = _clock();
        var target = selectionAfter.Text?.Target;
        if (kind != EditKind.Other && _open is { } open && open.Kind == kind && _undo.Count > 0 && ReferenceEquals(_undo[^1], open)
            && now - open.LastEdit <= TypingGroupPause && open.Target == target && ReferenceEquals(open.After, before))
        {
            open.After = after;
            open.SelectionAfter = selectionAfter;
            open.LastEdit = now;
            Replace(after, selectionAfter);
            return;
        }

        var step = new Step(label, kind, before, Selection) { After = after, SelectionAfter = selectionAfter, LastEdit = now, Target = target };
        Push(step);
        _open = kind == EditKind.Other ? null : step;
        Replace(after, selectionAfter);
    }

    private void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>Puts a presentation in without an undo step (loading, the document's own replacement).</summary>
    public void Load(Presentation presentation)
    {
        _undo.Clear();
        _redo.Clear();
        _open = null;
        var first = presentation.Slides.FirstOrDefault()?.Id;
        Replace(presentation, first is { } id ? ShowSelection.OnSlide(id) : ShowSelection.Empty);
    }

    private void Replace(Presentation presentation, ShowSelection selection)
    {
        var old = Presentation;
        Presentation = presentation;
        var selectionChanged = selection != Selection;
        Selection = Validate(selection, presentation);
        if (!ReferenceEquals(old, presentation))
        {
            PresentationChanged?.Invoke(this, Diff(old, presentation));
        }

        if (selectionChanged)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The slides and shapes that differ between two presentations (by instance: unchanged parts share theirs).</summary>
    public static PresentationChangedEventArgs Diff(Presentation before, Presentation after)
    {
        var designChanged = !before.Masters.IsSameInstance(after.Masters) || before.SlideSize != after.SlideSize || before.DefaultTextStyle != after.DefaultTextStyle;
        var listChanged = before.Slides.Count != after.Slides.Count
                          || !before.Slides.Select(slide => slide.Id).SequenceEqual(after.Slides.Select(slide => slide.Id))
                          || !before.Sections.IsSameInstance(after.Sections);
        var oldSlides = before.Slides.ToDictionary(slide => slide.Id);
        var changed = new List<uint>();
        var shapes = new Dictionary<uint, IReadOnlyList<int>>();
        foreach (var slide in after.Slides)
        {
            if (!oldSlides.TryGetValue(slide.Id, out var old))
            {
                changed.Add(slide.Id);
                continue;
            }

            if (ReferenceEquals(old, slide))
            {
                continue;
            }

            changed.Add(slide.Id);
            var oldShapes = old.Shapes.ToDictionary(shape => shape.Id);
            var changedShapes = slide.Shapes.Where(shape => !oldShapes.TryGetValue(shape.Id, out var previous) || !ReferenceEquals(previous, shape)).Select(shape => shape.Id)
                .Concat(old.Shapes.Where(shape => slide.Shapes.All(current => current.Id != shape.Id)).Select(shape => shape.Id))
                .ToList();
            shapes[slide.Id] = changedShapes;
        }

        return new PresentationChangedEventArgs(changed, shapes, listChanged, designChanged);
    }

    // ------------------------------------------------------------ selection

    /// <summary>Shows a slide in the editor (selects it in the sorter, clears shape and text selection).</summary>
    public void GoToSlide(uint slideId)
    {
        CloseOpenStep();
        SetSelection(ShowSelection.OnSlide(slideId));
    }

    /// <summary>Selects slides in the sorter (the first becomes current unless the current one is among them).</summary>
    public void SelectSlides(IEnumerable<uint> slideIds)
    {
        var ids = slideIds.Where(id => Presentation.FindSlide(id) is not null).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var current = Selection.CurrentSlide is { } id && ids.Contains(id) ? id : ids[0];
        SetSelection(new ShowSelection(current, ids));
    }

    /// <summary>Selects shapes on the current slide (an empty list clears the shape selection).</summary>
    public void SelectShapes(IEnumerable<int> shapeIds, int? enteredGroup = null)
    {
        CloseOpenStep();
        if (Selection.CurrentSlide is not { } slideId)
        {
            return;
        }

        SetSelection(Selection with { Shapes = shapeIds.Distinct().ToList(), EnteredGroup = enteredGroup, Text = null, Slides = [slideId] });
    }

    /// <summary>Adds or removes one shape from the selection (Umschalt/Strg+Klick).</summary>
    public void ToggleShapeSelection(int shapeId)
    {
        var shapes = Selection.Shapes.Contains(shapeId) ? Selection.Shapes.RemoveAll(id => id == shapeId) : Selection.Shapes.Add(shapeId);
        SelectShapes(shapes, Selection.EnteredGroup);
    }

    /// <summary>Selects every shape of the current slide (in the entered group: its children).</summary>
    public void SelectAllShapes()
    {
        if (CurrentSlide is not { } slide)
        {
            return;
        }

        var shapes = Selection.EnteredGroup is { } group && slide.FindShape(group) is GroupShape entered ? entered.Children : slide.Shapes;
        SelectShapes(shapes.Select(shape => shape.Id), Selection.EnteredGroup);
    }

    /// <summary>Puts a text selection (or the caret) into a shape's text.</summary>
    public void SelectText(TextTarget target, TextPosition anchor, TextPosition active)
    {
        if (TextBodyOf(target) is not { } body)
        {
            return;
        }

        if (_open is { } open && open.Target != target)
        {
            CloseOpenStep();
        }

        PendingFormat = null;
        SetSelection(Selection with { Shapes = [target.ShapeId], Text = new TextSelection(target, TextEdits.Clamp(body, anchor), TextEdits.Clamp(body, active)) });
    }

    /// <summary>Leaves text editing (the shape stays selected).</summary>
    public void EndTextEditing()
    {
        CloseOpenStep();
        if (Selection.Text is { } text)
        {
            SetSelection(Selection with { Text = null, Shapes = [text.Target.ShapeId] });
        }
    }

    private void SetSelection(ShowSelection selection)
    {
        selection = Validate(selection, Presentation);
        if (selection == Selection)
        {
            return;
        }

        Selection = selection;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A selection that only names what exists in <paramref name="presentation"/>.</summary>
    private static ShowSelection Validate(ShowSelection selection, Presentation presentation)
    {
        var current = selection.CurrentSlide is { } id && presentation.FindSlide(id) is not null ? id : presentation.Slides.FirstOrDefault()?.Id;
        if (current is null)
        {
            return ShowSelection.Empty;
        }

        var slide = presentation.FindSlide(current.Value)!;
        var slides = selection.Slides.Where(slideId => presentation.FindSlide(slideId) is not null).ToList();
        if (slides.Count == 0)
        {
            slides.Add(current.Value);
        }

        var shapes = selection.CurrentSlide == current ? selection.Shapes.Where(shapeId => slide.FindShape(shapeId) is not null).ToList() : [];
        var text = selection.Text;
        if (text is not null && (selection.CurrentSlide != current || TextBodyOf(presentation, current.Value, text.Target) is not { } body))
        {
            text = null;
        }
        else if (text is not null)
        {
            var textBody = TextBodyOf(presentation, current.Value, text.Target)!;
            text = text with { Anchor = TextEdits.Clamp(textBody, text.Anchor), Active = TextEdits.Clamp(textBody, text.Active) };
        }

        var entered = selection.EnteredGroup is { } group && slide.FindShape(group) is GroupShape ? selection.EnteredGroup : null;
        return new ShowSelection(current, slides, shapes, entered, text);
    }

    // ------------------------------------------------------------ helpers for the partial files

    /// <summary>The text body a target names on the current slide (a shape's or a table cell's), or null.</summary>
    public TextBody? TextBodyOf(TextTarget target) =>
        Selection.CurrentSlide is { } slideId ? TextBodyOf(Presentation, slideId, target) : null;

    internal static TextBody? TextBodyOf(Presentation presentation, uint slideId, TextTarget target)
    {
        var shape = presentation.FindSlide(slideId)?.FindShape(target.ShapeId);
        if (target.IsTableCell)
        {
            return shape is GraphicFrame { Table: { } table } && target.Row < table.RowCount && target.Column < table.ColumnCount
                ? table.CellAt(target.Row!.Value, target.Column!.Value).Text
                : null;
        }

        return shape switch
        {
            AutoShape { Text: { } text } => text,
            AutoShape auto => TextBody.Empty,
            _ => null,
        };
    }

    /// <summary>The presentation with slide <paramref name="slideId"/> changed.</summary>
    internal static Presentation WithSlide(Presentation presentation, uint slideId, Func<Slide, Slide> change)
    {
        var index = presentation.IndexOf(slideId);
        if (index < 0)
        {
            return presentation;
        }

        var slide = presentation.Slides[index];
        var changed = change(slide);
        return ReferenceEquals(changed, slide) ? presentation : presentation with { Slides = presentation.Slides.SetItem(index, changed) };
    }

    /// <summary>The presentation with shape <paramref name="shapeId"/> on slide <paramref name="slideId"/> changed (inside groups too).</summary>
    internal static Presentation WithShape(Presentation presentation, uint slideId, int shapeId, Func<SlideElement, SlideElement?> change) =>
        WithSlide(presentation, slideId, slide =>
        {
            var shapes = ShapeTree.Replace(slide.Shapes, shapeId, change);
            return shapes.IsSameInstance(slide.Shapes) ? slide : slide with { Shapes = shapes };
        });

    /// <summary>The presentation with the text body of <paramref name="target"/> on the current slide replaced.</summary>
    internal static Presentation WithText(Presentation presentation, uint slideId, TextTarget target, Func<TextBody, TextBody> change) =>
        WithShape(presentation, slideId, target.ShapeId, shape =>
        {
            if (target.IsTableCell && shape is GraphicFrame { Table: { } table } frame)
            {
                var cell = table.CellAt(target.Row!.Value, target.Column!.Value);
                return frame with { Table = table.WithCell(target.Row.Value, target.Column.Value, cell with { Text = change(cell.Text) }) };
            }

            return shape is AutoShape auto ? auto with { Text = change(auto.Text ?? TextBody.Empty) } : shape;
        });

    /// <summary>The ids of the selected shapes that exist on the current slide (in z-order).</summary>
    internal IReadOnlyList<int> SelectedShapeIds()
    {
        if (CurrentSlide is not { } slide)
        {
            return [];
        }

        var selected = Selection.Shapes.ToHashSet();
        return ShapeTree.All(slide.Shapes).Where(shape => selected.Contains(shape.Id)).Select(shape => shape.Id).ToList();
    }

    /// <summary>A new shape id for the current slide.</summary>
    internal static int NextShapeId(Slide slide) => slide.MaxShapeId + 1;
}

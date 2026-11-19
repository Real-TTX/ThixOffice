using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Editor;

/// <summary>How editing a cell starts.</summary>
internal enum EditStart : byte
{
    /// <summary>A character typed into the grid: the content is replaced (Eingeben).</summary>
    Typing,

    /// <summary>Rücktaste: the content is cleared and the cell is edited (Eingeben).</summary>
    Empty,

    /// <summary>F2 or a double click: the content is edited with the caret at its end (Bearbeiten).</summary>
    Edit,

    /// <summary>A click into the formula bar (Bearbeiten there).</summary>
    FormulaBar,
}

/// <summary>Excel's modes of the status bar while a cell is edited.</summary>
internal enum EditMode : byte
{
    /// <summary>Eingeben: arrows end the input and move (or point in a formula).</summary>
    Enter,

    /// <summary>Bearbeiten: arrows move the caret.</summary>
    Edit,

    /// <summary>Zeigen: a reference is being pointed into the formula.</summary>
    Point,
}

/// <summary>
/// The editing of one cell, shared by its two fields – the one over the cell and the formula bar's – which always show the same
/// text. Implements Excel's behaviour: typing replaces (Eingeben), F2 / a double click / the formula bar edit (Bearbeiten), F2
/// switches; in a formula where a reference may go (after =, an operator, "(" or ";") arrows and clicks point references in
/// (Zeigen), Umschalt extends them, F4 cycles $; every reference is coloured in the text and framed on the grid in the same
/// colour; a list of functions follows the typed name (Tab or a double click takes one) and a tip shows the arguments of the
/// function around the caret. Eingabe / Tab / Umschalt+… end the input and move within the selection; Strg+Eingabe fills
/// every selected cell; Strg+Umschalt+Eingabe enters an array formula; Esc cancels. An entry the core refuses (a formula with
/// an error, a value against the validation) is explained and the cell stays open.
/// </summary>
internal sealed class CellEditing
{
    private readonly CalcEditor _editor;
    private (int Start, int Length)? _pointed;
    private CellAddress _pointAnchor;
    private CellAddress _pointCell;
    private bool _syncing;
    private string _original = string.Empty;
    private int _editSheetIndex;

    public CellEditing(CalcEditor editor, FormulaTextBox cellBox, FormulaTextBox barBox)
    {
        _editor = editor;
        CellBox = cellBox;
        BarBox = barBox;
        foreach (var box in new[] { cellBox, barBox })
        {
            box.TextEdited += (sender, _) => OnTextEdited((FormulaTextBox)sender!);
            box.CaretMoved += (sender, _) => OnCaretMoved((FormulaTextBox)sender!);
            box.KeyPressing += (sender, e) => OnKey((FormulaTextBox)sender!, e);
        }

        barBox.Activated += (_, _) =>
        {
            if (!IsEditing)
            {
                Begin(EditStart.FormulaBar);
            }
            else
            {
                IsInFormulaBar = true;
                CellBox.IsActive = false;
                BarBox.IsActive = true;
            }
        };
        CellBox.IsVisible = false;
    }

    /// <summary>The field over the cell.</summary>
    public FormulaTextBox CellBox { get; }

    /// <summary>The formula bar's field.</summary>
    public FormulaTextBox BarBox { get; }

    public bool IsEditing { get; private set; }

    public EditMode Mode { get; private set; }

    /// <summary>The keyboard is in the formula bar (else in the grid, which forwards to the cell's field).</summary>
    public bool IsInFormulaBar { get; private set; }

    /// <summary>The cell being edited.</summary>
    public CellAddress Cell { get; private set; }

    /// <summary>The frames of the edited formula's references on the shown sheet, in the colours of their tokens.</summary>
    public IReadOnlyList<RangeHighlight> Highlights { get; private set; } = [];

    /// <summary>The functions that match the name being typed (the list under the caret), empty when none shows.</summary>
    public IReadOnlyList<FunctionInfo> Suggestions { get; private set; } = [];

    public int SuggestionIndex { get; private set; }

    /// <summary>The function and argument around the caret (the tip), or null.</summary>
    public (FunctionInfo Function, int Argument)? ArgumentTip { get; private set; }

    /// <summary>Raised when the mode, the highlights, the suggestions or the tip changed (status bar, grid, assist).</summary>
    public event EventHandler? Changed;

    private EditingSession Session => _editor.Session;

    private FormulaTextBox ActiveBox => IsInFormulaBar ? BarBox : CellBox;

    /// <summary>Whether a click or an arrow would point a reference in now.</summary>
    public bool CanPoint => IsEditing && (Mode != EditMode.Edit || _pointed is not null) &&
                            (_pointed is not null || FormulaTokens.CanInsertReference(ActiveBox.Text, ActiveBox.CaretIndex, Session.Locale));

    /// <summary>The status bar's word for the mode: Bereit, Eingeben, Bearbeiten, Zeigen.</summary>
    public string ModeText => !IsEditing ? "Bereit" : Mode switch
    {
        EditMode.Enter => "Eingeben",
        EditMode.Edit => "Bearbeiten",
        _ => "Zeigen",
    };

    /// <summary>Starts editing the active cell.</summary>
    public void Begin(EditStart start, Point? caretAt = null)
    {
        if (IsEditing)
        {
            return;
        }

        var sheet = Session.ActiveSheet;
        Cell = Session.ActiveCell;
        _editSheetIndex = sheet.Index;
        _original = Session.FormulaBarText;
        if (_original.StartsWith("{=", StringComparison.Ordinal) && _original.EndsWith('}'))
        {
            _original = _original[1..^1];
        }

        var text = start is EditStart.Typing or EditStart.Empty ? string.Empty : _original;
        Mode = start is EditStart.Typing or EditStart.Empty ? EditMode.Enter : EditMode.Edit;
        IsInFormulaBar = start == EditStart.FormulaBar;
        IsEditing = true;
        _pointed = null;

        StyleCellBox(sheet);
        _syncing = true;
        CellBox.SetText(text, text.Length);
        BarBox.SetText(text, text.Length);
        _syncing = false;
        CellBox.IsVisible = true;
        CellBox.IsActive = !IsInFormulaBar;
        BarBox.IsActive = IsInFormulaBar;
        PositionCellBox();
        if (caretAt is { } point && !IsInFormulaBar)
        {
            CellBox.Measure(Size.Infinity);
            CellBox.Arrange(new Rect(CellBox.DesiredSize));
            var local = point - new Point(Canvas.GetLeft(CellBox), Canvas.GetTop(CellBox));
            CellBox.Select(PositionAt(CellBox, local), PositionAt(CellBox, local));
        }

        if (IsInFormulaBar)
        {
            BarBox.Focus();
        }

        Refresh();
    }

    private static int PositionAt(FormulaTextBox box, Point point)
    {
        var layout = new TextLayout(box.Text.Length == 0 ? " " : box.Text, box.Typeface, box.FontSize, box.Foreground);
        return Math.Clamp(layout.HitTestPoint(new Point(point.X - box.Padding.Left, point.Y - box.Padding.Top)).TextPosition, 0, box.Text.Length);
    }

    /// <summary>Ends the input (Eingabe and friends): true when the cell took it (or nothing changed), false when it stays open.</summary>
    public bool Commit(bool allSelected = false, bool array = false)
    {
        if (!IsEditing)
        {
            return true;
        }

        var text = CloseParentheses(CellBox.Text);
        if (Session.ActiveSheet.Index != _editSheetIndex)
        {
            Session.ActivateSheet(_editSheetIndex);
        }

        if (Session.ActiveCell != Cell && !allSelected && !array)
        {
            Session.Select(Cell);
        }

        try
        {
            if (array)
            {
                Session.EnterArrayFormula(text);
            }
            else if (text != _original || allSelected)
            {
                Session.EnterInput(text, allSelected);
            }
        }
        catch (BusinessException exception)
        {
            _ = ExplainAsync(exception);
            return false;
        }

        End();
        return true;
    }

    /// <summary>Esc: the cell keeps what it had.</summary>
    public void Cancel()
    {
        if (!IsEditing)
        {
            return;
        }

        if (Session.ActiveSheet.Index != _editSheetIndex)
        {
            Session.ActivateSheet(_editSheetIndex);
            Session.Select(Cell);
        }

        End();
    }

    private void End()
    {
        IsEditing = false;
        _pointed = null;
        CellBox.IsVisible = false;
        CellBox.IsActive = false;
        BarBox.IsActive = false;
        IsInFormulaBar = false;
        Highlights = [];
        Suggestions = [];
        ArgumentTip = null;
        _syncing = true;
        BarBox.SetText(Session.FormulaBarText, 0);
        _syncing = false;
        _editor.FocusDocument();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Excel closes the parentheses a formula left open.</summary>
    private string CloseParentheses(string text)
    {
        if (!text.StartsWith('='))
        {
            return text;
        }

        var open = 0;
        foreach (var token in FormulaTokens.Scan(text, Session.Locale))
        {
            open += token.Kind switch
            {
                FormulaTokenKind.OpenParenthesis => 1,
                FormulaTokenKind.CloseParenthesis => -1,
                _ => 0,
            };
        }

        return open > 0 ? text + new string(')', open) : text;
    }

    /// <summary>Why the cell did not take the entry: a formula error (with its place selected) or the validation's message.</summary>
    private async Task ExplainAsync(BusinessException exception)
    {
        if (TopLevel.GetTopLevel(_editor.View) is not Window window)
        {
            return;
        }

        if (exception.Field == "formula")
        {
            await QuestionDialog.ShowMessageAsync(window, "ThixCalc", exception.Message);
            ActiveBox.Focus();
            return;
        }

        var choice = await QuestionDialog.AskAsync(window, "Ungültige Eingabe", exception.Message,
            new DialogChoice("Wiederholen", "refresh", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "x", IsCancel: true));
        if (choice != 0)
        {
            Cancel();
            return;
        }

        CellBox.Select(0, CellBox.Text.Length);
        BarBox.Select(0, BarBox.Text.Length);
    }

    // ===== Keys =====

    private void OnKey(FormulaTextBox box, KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        if (Suggestions.Count > 0 && HandleSuggestionKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                return; // a line break in the cell (the field inserts it)
            case Key.Enter:
                e.Handled = true;
                if (Commit(allSelected: command && !shift, array: command && shift) && !command)
                {
                    Session.MoveWithinSelection(shift, byRows: false);
                    _editor.ScrollIntoView();
                }

                return;
            case Key.Tab:
                e.Handled = true;
                if (Commit())
                {
                    Session.MoveWithinSelection(shift, byRows: true);
                    _editor.ScrollIntoView();
                }

                return;
            case Key.Escape:
                e.Handled = true;
                Cancel();
                return;
            case Key.F2:
                e.Handled = true;
                Mode = Mode == EditMode.Edit ? EditMode.Enter : EditMode.Edit;
                _pointed = null;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            case Key.F4:
                e.Handled = true;
                if (FormulaTokens.CycleReference(box.Text, box.CaretIndex, Session.Locale) is { } cycled)
                {
                    box.Replace(0, box.Text.Length, cycled.Text);
                    box.Select(cycled.Caret, cycled.Caret);
                    if (_pointed is not null)
                    {
                        _pointed = (cycled.Start, cycled.Length);
                    }
                }

                return;
            case Key.Left or Key.Right or Key.Up or Key.Down when Mode != EditMode.Edit:
                if (CanPoint)
                {
                    e.Handled = true;
                    PointByKey(e.Key, shift, command);
                    return;
                }

                if (Mode == EditMode.Enter && !IsInFormulaBar)
                {
                    e.Handled = true;
                    if (Commit())
                    {
                        Session.Move(e.Key switch
                        {
                            Key.Left => Direction.Left,
                            Key.Right => Direction.Right,
                            Key.Up => Direction.Up,
                            _ => Direction.Down,
                        });
                        _editor.ScrollIntoView();
                    }
                }

                return;
        }
    }

    private bool HandleSuggestionKey(Key key)
    {
        switch (key)
        {
            case Key.Down:
                SuggestionIndex = Math.Min(Suggestions.Count - 1, SuggestionIndex + 1);
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.Up:
                SuggestionIndex = Math.Max(0, SuggestionIndex - 1);
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.Tab:
                AcceptSuggestion(SuggestionIndex);
                return true;
            case Key.Escape:
                Suggestions = [];
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Takes a function of the list: its name and "(" replace the typed part.</summary>
    public void AcceptSuggestion(int index)
    {
        if (index < 0 || index >= Suggestions.Count || FormulaTokens.NameBeforeCaret(ActiveBox.Text, ActiveBox.CaretIndex) is not { } typed)
        {
            return;
        }

        var name = Suggestions[index].Name;
        ActiveBox.Replace(typed.Start, ActiveBox.CaretIndex - typed.Start, name + "(");
        Suggestions = [];
        Refresh();
    }

    /// <summary>Arrows in point mode: the pointer cell moves (Umschalt extends from where pointing started, Strg jumps to the data edge).</summary>
    private void PointByKey(Key key, bool extend, bool toEdge)
    {
        var sheet = Session.ActiveSheet;
        if (_pointed is null)
        {
            _pointCell = _pointAnchor = Session.ActiveSheet.Index == _editSheetIndex ? Cell : Session.ActiveCell;
        }

        var direction = key switch
        {
            Key.Left => Direction.Left,
            Key.Right => Direction.Right,
            Key.Up => Direction.Up,
            _ => Direction.Down,
        };
        _pointCell = toEdge ? Navigation.Edge(sheet, _pointCell, direction) : Navigation.Step(sheet, _pointCell, direction);
        if (!extend)
        {
            _pointAnchor = _pointCell;
        }

        Insert(new CellRange(_pointAnchor, _pointCell));
        _editor.ScrollIntoView(_pointCell);
    }

    /// <summary>A click (or drag) on the grid while a formula is edited: the range goes into the formula at the caret.</summary>
    public void PointAt(CellRange range, bool extend, bool replaceDrag = false)
    {
        if (extend && _pointed is not null)
        {
            range = new CellRange(_pointAnchor, range.Last);
        }
        else if (!replaceDrag)
        {
            _pointed = null;
            _pointAnchor = range.First;
        }

        _pointCell = range.Last;
        Insert(range);
    }

    private void Insert(CellRange range)
    {
        var sheet = Session.ActiveSheet;
        var text = FormulaTokens.ReferenceText(range, sheet.Index == _editSheetIndex ? null : sheet.Name);
        var box = ActiveBox;
        if (_pointed is { } pointed)
        {
            box.Replace(pointed.Start, pointed.Length, text);
            _pointed = (pointed.Start, text.Length);
        }
        else
        {
            var start = box.SelectionStart;
            box.InsertText(text);
            _pointed = (start, text.Length);
        }

        Mode = EditMode.Point;
        Refresh();
    }

    // ===== Text =====

    private void OnTextEdited(FormulaTextBox box)
    {
        if (_syncing)
        {
            return;
        }

        if (!IsEditing)
        {
            // Typing into the formula bar without a click (the keyboard came by Tab).
            Begin(box == BarBox ? EditStart.FormulaBar : EditStart.Typing);
        }

        var other = box == CellBox ? BarBox : CellBox;
        _syncing = true;
        other.SetText(box.Text, box.CaretIndex, box.SelectionStart == box.CaretIndex ? box.SelectionEnd : box.SelectionStart);
        _syncing = false;
        if (Mode == EditMode.Point && !IsPointing(box))
        {
            Mode = EditMode.Enter;
        }

        if (!IsPointing(box))
        {
            _pointed = null;
        }

        UpdateSuggestions(box);
        Refresh();
        PositionCellBox();
    }

    /// <summary>The text change was the pointed reference itself (still being pointed).</summary>
    private bool IsPointing(FormulaTextBox box) => _pointed is { } pointed && box.CaretIndex == pointed.Start + pointed.Length;

    private void OnCaretMoved(FormulaTextBox box)
    {
        if (_syncing || !IsEditing)
        {
            return;
        }

        if (_pointed is not null && !IsPointing(box))
        {
            _pointed = null;
            if (Mode == EditMode.Point)
            {
                Mode = EditMode.Enter;
            }
        }

        UpdateTip(box);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSuggestions(FormulaTextBox box)
    {
        var typed = FormulaTokens.NameBeforeCaret(box.Text, box.CaretIndex);
        Suggestions = typed is { } name ? FunctionHelp.StartingWith(name.Prefix, Session.Locale) : [];
        SuggestionIndex = 0;
    }

    private void UpdateTip(FormulaTextBox box)
    {
        ArgumentTip = FormulaTokens.ArgumentAt(box.Text, box.CaretIndex, Session.Locale) is { } position && FunctionHelp.Find(position.Name, Session.Locale) is { } info
            ? (info, position.Index)
            : null;
    }

    /// <summary>Colours of the reference tokens (both fields) and their frames on the grid.</summary>
    private void Refresh()
    {
        var box = ActiveBox;
        var text = box.Text;
        var colors = new List<(int Start, int Length, IBrush Brush)>();
        var highlights = new List<RangeHighlight>();
        if (text.StartsWith('='))
        {
            var palette = GridPalette.ReferenceColors;
            var byText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var shown = Session.ActiveSheet;
            foreach (var token in FormulaTokens.Scan(text, Session.Locale))
            {
                if (token.Kind != FormulaTokenKind.Reference || token.Range is not { } range)
                {
                    continue;
                }

                var key = text.Substring(token.Start, token.Length).Replace("$", "", StringComparison.Ordinal);
                if (!byText.TryGetValue(key, out var index))
                {
                    index = byText.Count;
                    byText[key] = index;
                }

                var color = palette[index % palette.Count];
                colors.Add((token.Start, token.Length, new SolidColorBrush(GridPalette.ToColor(color))));
                var sheet = token.Sheet is null ? Session.Workbook.Sheets[_editSheetIndex] : Session.Workbook.FindSheet(token.Sheet);
                if (sheet == shown)
                {
                    highlights.Add(new RangeHighlight(shown.Merges.Expand(range), color));
                }
            }
        }

        CellBox.Colors = colors;
        BarBox.Colors = colors;
        CellBox.SetPreedit(CellBox.Preedit);
        BarBox.SetPreedit(BarBox.Preedit);
        Highlights = highlights;
        UpdateTip(box);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ===== The field over the cell =====

    /// <summary>The cell's font, scaled by the zoom (the field shows the text as the cell will).</summary>
    private void StyleCellBox(Worksheet sheet)
    {
        var style = sheet.GetStyle(Cell.Row, Cell.Column);
        var zoom = _editor.Zoom / 100;
        CellBox.Typeface = new Typeface(style.Font.Name, style.Font.Italic ? FontStyle.Italic : FontStyle.Normal, style.Font.Bold ? FontWeight.Bold : FontWeight.Normal);
        CellBox.FontSize = Math.Max(4, style.Font.Size * 96 / 72 * zoom);
        CellBox.Wrap = style.Alignment.WrapText;
    }

    /// <summary>Puts the cell's field over the edited cell (it grows to the right with its text, like Excel's).</summary>
    public void PositionCellBox()
    {
        if (!IsEditing || Session.ActiveSheet.Index != _editSheetIndex)
        {
            CellBox.IsVisible = IsEditing && Session.ActiveSheet.Index == _editSheetIndex;
            return;
        }

        CellBox.IsVisible = true;
        var surface = _editor.Surface;
        var viewport = surface.Viewport();
        var rect = viewport.CellRect(Cell);
        var scaling = surface.Scale / Math.Max(0.01, _editor.Zoom / 100);
        var left = rect.X / scaling;
        var top = rect.Y / scaling;
        Canvas.SetLeft(CellBox, left);
        Canvas.SetTop(CellBox, top);
        CellBox.MinimumWidth = Math.Max(8, rect.Width / scaling);
        CellBox.MinHeight = Math.Max(8, rect.Height / scaling);
        CellBox.MaximumWidth = Math.Max(CellBox.MinimumWidth, surface.Bounds.Width - left - 2);
        CellBox.InvalidateMeasure();
    }
}

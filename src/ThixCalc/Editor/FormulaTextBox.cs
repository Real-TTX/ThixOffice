using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Editor;

/// <summary>
/// The text field of a cell's content – twice per window: in the cell (over the grid) and in the formula bar. Unlike a TextBox
/// it colours the references of a formula (each in the colour of its frame on the grid), shows the caret while it is the
/// active one even without the keyboard (the grid forwards typing to the cell's field, so an input method composes in place),
/// grows with its text in the cell and wraps in the formula bar. Editing keys: arrows (by words with Strg / ⌥), Pos1/Ende,
/// Umschalt to select, Rücktaste/Entf, Strg+A/C/X/V, Alt+Eingabe (⌃⌥↩ on macOS) for a line break; everything else goes to
/// <see cref="KeyPressing"/> first (the cell editing decides Enter, Tab, Esc, F2, F4 and the arrows of point mode).
/// </summary>
internal sealed class FormulaTextBox : Control, ITextField
{
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly Stack<(string Text, int Caret, int Anchor)> _undo = new();
    private readonly Stack<(string Text, int Caret, int Anchor)> _redo = new();
    private string _text = string.Empty;
    private int _caret;
    private int _anchor;
    private bool _typing;
    private string? _preedit;
    private TextLayout? _layout;
    private double _offsetX;
    private double _offsetY;
    private bool _caretOn = true;
    private bool _isActive;
    private bool _selecting;

    public FormulaTextBox()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ClipToBounds = true;
        InputClient = new FormulaInputClient(this);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputClient);
        _blink.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            InvalidateVisual();
        };
    }

    /// <summary>A key before the field acts on it: set Handled to take it (Enter, Tab, Esc, F2, F4, point mode).</summary>
    public event EventHandler<KeyEventArgs>? KeyPressing;

    /// <summary>The text changed by the user (typing, deleting, pasting, the input method).</summary>
    public event EventHandler? TextEdited;

    /// <summary>The caret or the selection moved.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>The field got the keyboard by a click (the formula bar starts editing).</summary>
    public event EventHandler? Activated;

    public FormulaInputClient InputClient { get; }

    /// <summary>The font: the cell's (scaled by the zoom) or the formula bar's.</summary>
    public Typeface Typeface { get; set; } = new("Calibri");

    public double FontSize { get; set; } = 14.67;

    /// <summary>The text colour (a styled property, so the formula bar can follow the theme's Mat.Text).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundBrushProperty = AvaloniaProperty.Register<FormulaTextBox, IBrush?>(nameof(Foreground));

    public IBrush Foreground
    {
        get => GetValue(ForegroundBrushProperty) ?? InkBrush;
        set => SetValue(ForegroundBrushProperty, value);
    }

    private static readonly IBrush InkBrush = new SolidColorBrush(GridPalette.InkColor);

    public IBrush? FieldBackground { get; set; }

    /// <summary>The selection (null: the theme's Mat.Selection).</summary>
    public IBrush? SelectionBrush { get; set; }

    /// <summary>The caret (null: the text colour).</summary>
    public IBrush? CaretBrush { get; set; }

    public Thickness Padding { get; set; } = new(3, 1);

    /// <summary>
    /// Where the baseline of a single line goes, from the field's top (the cell's field: where the grid draws the cell's text,
    /// by its vertical alignment); null: the text starts at <see cref="Padding"/>'s top.
    /// </summary>
    public double? Baseline { get; set; }

    /// <summary>The baseline of the first line when the text has several (it starts at the top, as in Excel); null: <see cref="Baseline"/>.</summary>
    public double? TopBaseline { get; set; }

    /// <summary>A frame drawn inside the field's edges (the cell's field: the active cell's border around the edit area).</summary>
    public Thickness Frame { get; set; }

    public IBrush? FrameBrush { get; set; }

    /// <summary>The height of one line of text in the field's font.</summary>
    public double LineHeight => new TextLayout(" ", Typeface, FontSize, Foreground).Height;

    /// <summary>Wrap at the field's width (the formula bar, a cell with Zeilenumbruch); otherwise one line that scrolls.</summary>
    public bool Wrap { get; set; }

    /// <summary>The cell's field grows to the right up to <see cref="MaximumWidth"/>, then wraps (like Excel).</summary>
    public bool GrowsWithText { get; set; }

    public double MinimumWidth { get; set; }

    public double MaximumWidth { get; set; } = double.PositiveInfinity;

    /// <summary>Colours of parts of the text (the references of a formula), as (start, length, colour).</summary>
    public IReadOnlyList<(int Start, int Length, IBrush Brush)> Colors { get; set; } = [];

    /// <summary>The field shows its caret (the active one of the two, also while the grid has the keyboard).</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            RestartBlink();
            InvalidateVisual();
        }
    }

    public bool IsEditingText => IsActive;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caret = Math.Min(_caret, _text.Length);
            _anchor = Math.Min(_anchor, _text.Length);
            Changed(edited: false);
        }
    }

    public int CaretIndex => _caret;

    public int SelectionStart => Math.Min(_caret, _anchor);

    public int SelectionEnd => Math.Max(_caret, _anchor);

    public string SelectedText => _text[SelectionStart..SelectionEnd];

    /// <summary>The composition of an input method shown at the caret (null: none).</summary>
    public string? Preedit => _preedit;

    /// <summary>Sets the text and puts the caret (and the anchor) somewhere, without raising <see cref="TextEdited"/>.</summary>
    public void SetText(string text, int caret, int? anchor = null)
    {
        _undo.Clear();
        _redo.Clear();
        _typing = false;
        _text = text ?? string.Empty;
        _caret = Math.Clamp(caret, 0, _text.Length);
        _anchor = Math.Clamp(anchor ?? _caret, 0, _text.Length);
        _preedit = null;
        Changed(edited: false);
    }

    public void Select(int anchor, int caret)
    {
        _anchor = Math.Clamp(anchor, 0, _text.Length);
        _caret = Math.Clamp(caret, 0, _text.Length);
        Moved();
    }

    /// <summary>Replaces the selection with <paramref name="text"/> (typing, pasting, the input method's result).</summary>
    public void InsertText(string text)
    {
        _preedit = null;
        Remember(typing: text.Length == 1 && SelectionStart == SelectionEnd && !char.IsWhiteSpace(text[0]));
        var start = SelectionStart;
        _text = _text[..start] + text + _text[SelectionEnd..];
        _caret = _anchor = start + text.Length;
        Changed(edited: true);
    }

    /// <summary>Replaces <paramref name="length"/> characters at <paramref name="start"/> (point mode, F4, autocomplete) and puts the caret after it.</summary>
    public void Replace(int start, int length, string text)
    {
        start = Math.Clamp(start, 0, _text.Length);
        length = Math.Clamp(length, 0, _text.Length - start);
        Remember(typing: false);
        _text = _text[..start] + text + _text[(start + length)..];
        _caret = _anchor = start + text.Length;
        Changed(edited: true);
    }

    public void SetPreedit(string? text)
    {
        _preedit = string.IsNullOrEmpty(text) ? null : text;
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>The caret's rectangle in this control (the input method's candidate window, the function list).</summary>
    public Rect CaretRect()
    {
        var layout = Layout();
        var rect = layout.HitTestTextPosition(Math.Min(_caret + (_preedit?.Length ?? 0), DisplayText.Length));
        return new Rect(rect.X + Padding.Left - _offsetX, rect.Y + TextTop(layout) - _offsetY, 1, Math.Max(rect.Height, FontSize * 1.2));
    }

    /// <summary>Where the text layout's top goes in the field: on <see cref="Baseline"/> / <see cref="TopBaseline"/>, else the padding.</summary>
    private double TextTop(TextLayout layout)
    {
        if (Baseline is not { } single || layout.TextLines.Count == 0)
        {
            return Padding.Top;
        }

        var baseline = layout.TextLines.Count > 1 ? TopBaseline ?? single : single;
        return baseline - layout.TextLines[0].Baseline;
    }

    /// <summary>The position in the text under a point of the field.</summary>
    public int TextPositionAt(Point point) => PositionAt(point);

    /// <summary>The origin of the text (its layout's top left) in the field, for tests and placement.</summary>
    public Point TextOrigin
    {
        get
        {
            var layout = Layout();
            return new Point(Padding.Left - _offsetX, TextTop(layout) - _offsetY);
        }
    }

    /// <summary>The baseline of the first line, from the field's top.</summary>
    public double FirstBaseline
    {
        get
        {
            var layout = Layout();
            return TextTop(layout) - _offsetY + (layout.TextLines.Count > 0 ? layout.TextLines[0].Baseline : 0);
        }
    }

    // ===== Keys =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            e.Handled = ProcessKey(e);
        }
    }

    /// <summary>A key for the field (also forwarded from the grid while the cell is edited); true when it was used.</summary>
    public bool ProcessKey(KeyEventArgs e)
    {
        KeyPressing?.Invoke(this, e);
        if (e.Handled)
        {
            return true;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var word = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Left:
                MoveCaret(word ? WordStart(_caret) : SelectionCollapsedOr(shift, SelectionStart, _caret - 1), shift);
                return true;
            case Key.Right:
                MoveCaret(word ? WordEnd(_caret) : SelectionCollapsedOr(shift, SelectionEnd, _caret + 1), shift);
                return true;
            case Key.Home:
                MoveCaret(command ? 0 : LineStart(_caret), shift);
                return true;
            case Key.End:
                MoveCaret(command ? _text.Length : LineEnd(_caret), shift);
                return true;
            case Key.Up or Key.Down when _text.Contains('\n') || Wrap:
                MoveVertically(e.Key == Key.Up ? -1 : 1, shift);
                return true;
            case Key.Back:
                if (SelectionStart == SelectionEnd && _caret > 0)
                {
                    _anchor = word ? WordStart(_caret) : PreviousGrapheme(_caret);
                }

                InsertText(string.Empty);
                return true;
            case Key.Delete:
                if (SelectionStart == SelectionEnd && _caret < _text.Length)
                {
                    _anchor = word ? WordEnd(_caret) : NextGrapheme(_caret);
                }

                InsertText(string.Empty);
                return true;
            case Key.A when command:
                Select(0, _text.Length);
                return true;
            case Key.Z when command && !shift:
                Undo();
                return true;
            case Key.Z when command && shift:
            case Key.Y when command && !OperatingSystem.IsMacOS():
                Redo();
                return true;
            case Key.C when command:
                _ = CopyAsync(cut: false);
                return true;
            case Key.X when command:
                _ = CopyAsync(cut: true);
                return true;
            case Key.V when command:
                _ = PasteAsync();
                return true;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                InsertText("\n");
                return true;
            default:
                return false;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var text = new string(e.Text.Where(character => !char.IsControl(character)).ToArray());
        if (text.Length > 0)
        {
            InsertText(text);
            e.Handled = true;
        }
    }

    /// <summary>Whether the edit has something to undo (Rückgängig inside the cell before the input ends).</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Rückgängig while the cell is edited: the text before the last edit (a run of typed characters is one step).</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push((_text, _caret, _anchor));
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push((_text, _caret, _anchor));
        Restore(_redo.Pop());
    }

    private void Restore((string Text, int Caret, int Anchor) state)
    {
        _typing = false;
        _preedit = null;
        _text = state.Text;
        _caret = Math.Clamp(state.Caret, 0, _text.Length);
        _anchor = Math.Clamp(state.Anchor, 0, _text.Length);
        Changed(edited: true);
    }

    /// <summary>Keeps the state before an edit; typed characters in a row share one state.</summary>
    private void Remember(bool typing)
    {
        if (!(typing && _typing))
        {
            _undo.Push((_text, _caret, _anchor));
            if (_undo.Count > 200)
            {
                var kept = _undo.Reverse().Skip(_undo.Count - 200).ToList();
                _undo.Clear();
                foreach (var state in kept)
                {
                    _undo.Push(state);
                }
            }
        }

        _typing = typing;
        _redo.Clear();
    }

    private int SelectionCollapsedOr(bool shift, int collapsed, int moved) => !shift && SelectionStart != SelectionEnd ? collapsed : Math.Clamp(moved, 0, _text.Length);

    private void MoveCaret(int position, bool extend)
    {
        _caret = Math.Clamp(position, 0, _text.Length);
        if (!extend)
        {
            _anchor = _caret;
        }

        Moved();
    }

    private void MoveVertically(int lines, bool extend)
    {
        var layout = Layout();
        var rect = layout.HitTestTextPosition(_caret);
        var target = layout.HitTestPoint(new Point(rect.X, rect.Y + rect.Height / 2 + lines * rect.Height));
        MoveCaret(target.TextPosition, extend);
    }

    private int LineStart(int position)
    {
        var index = _text.LastIndexOf('\n', Math.Max(0, position - 1));
        return position == 0 ? 0 : index < 0 ? 0 : index + 1;
    }

    private int LineEnd(int position)
    {
        var index = _text.IndexOf('\n', position);
        return index < 0 ? _text.Length : index;
    }

    private int WordStart(int position)
    {
        position = Math.Max(0, position - 1);
        while (position > 0 && !char.IsLetterOrDigit(_text[position]))
        {
            position--;
        }

        while (position > 0 && char.IsLetterOrDigit(_text[position - 1]))
        {
            position--;
        }

        return position;
    }

    private int WordEnd(int position)
    {
        while (position < _text.Length && !char.IsLetterOrDigit(_text[position]))
        {
            position++;
        }

        while (position < _text.Length && char.IsLetterOrDigit(_text[position]))
        {
            position++;
        }

        return position;
    }

    private int PreviousGrapheme(int position) => position >= 2 && char.IsLowSurrogate(_text[position - 1]) ? position - 2 : position - 1;

    private int NextGrapheme(int position) => position + 1 < _text.Length && char.IsHighSurrogate(_text[position]) ? position + 2 : position + 1;

    private async Task CopyAsync(bool cut)
    {
        if (SelectionStart == SelectionEnd || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(SelectedText);
        if (cut)
        {
            InsertText(string.Empty);
        }
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            InsertText(text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
        }
    }

    // ===== Mouse =====

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!IsFocused)
        {
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
        }

        var position = PositionAt(point.Position);
        if (e.ClickCount == 2)
        {
            Select(WordStart(Math.Min(_text.Length, position + 1)), WordEnd(position));
        }
        else
        {
            MoveCaret(position, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _selecting = true;
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_selecting)
        {
            MoveCaret(PositionAt(e.GetPosition(this)), extend: true);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _selecting = false;
        e.Pointer.Capture(null);
    }

    private int PositionAt(Point point)
    {
        var layout = Layout();
        var hit = layout.HitTestPoint(new Point(point.X - Padding.Left + _offsetX, point.Y - TextTop(layout) + _offsetY));
        return Math.Clamp(hit.TextPosition, 0, _text.Length);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _selecting = false;
        RestartBlink();
    }

    // ===== Layout and drawing =====

    private string DisplayText => _preedit is null ? _text : _text.Insert(Math.Min(_caret, _text.Length), _preedit);

    private TextLayout Layout()
    {
        if (_layout is not null)
        {
            return _layout;
        }

        var overrides = new List<ValueSpan<TextRunProperties>>();
        var preeditAt = _preedit is null ? int.MaxValue : _caret;
        var shift = _preedit?.Length ?? 0;
        foreach (var (start, length, brush) in Colors)
        {
            if (start < 0 || start + length > _text.Length || length <= 0)
            {
                continue;
            }

            var displayStart = start >= preeditAt ? start + shift : start;
            overrides.Add(new ValueSpan<TextRunProperties>(displayStart, length, new GenericTextRunProperties(Typeface, FontSize, null, brush)));
        }

        if (_preedit is not null)
        {
            overrides.Add(new ValueSpan<TextRunProperties>(_caret, _preedit.Length, new GenericTextRunProperties(Typeface, FontSize, TextDecorations.Underline, Foreground)));
            overrides.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        var width = WrapWidth();
        _layout = new TextLayout(DisplayText.Length == 0 ? " " : DisplayText, Typeface, FontSize, Foreground, TextAlignment.Left,
            double.IsInfinity(width) ? TextWrapping.NoWrap : TextWrapping.Wrap, null, null, FlowDirection.LeftToRight, width, double.PositiveInfinity,
            double.NaN, 0, 0, overrides.Count > 0 ? overrides : null);
        return _layout;
    }

    private double WrapWidth()
    {
        if (GrowsWithText)
        {
            if (Wrap)
            {
                // A cell with Zeilenumbruch: the text wraps at the cell's width and the field grows down (Excel).
                return Math.Max(10, MinimumWidth - Padding.Left - Padding.Right);
            }

            var natural = NaturalWidth();
            return natural + Padding.Left + Padding.Right <= MaximumWidth ? double.PositiveInfinity : Math.Max(10, MaximumWidth - Padding.Left - Padding.Right);
        }

        return Wrap && Bounds.Width > 0 ? Math.Max(10, Bounds.Width - Padding.Left - Padding.Right) : double.PositiveInfinity;
    }

    private double NaturalWidth()
    {
        var single = new TextLayout(DisplayText.Length == 0 ? " " : DisplayText, Typeface, FontSize, Foreground);
        return single.WidthIncludingTrailingWhitespace;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!GrowsWithText)
        {
            return base.MeasureOverride(availableSize);
        }

        _layout = null;
        var layout = Layout();
        // A wrapping field keeps the cell's width (wrapped lines end in their spaces, which must not widen it); a growing one
        // keeps room for the caret after the last character (the text area ends at the frame).
        const double caretRoom = 2;
        var width = Wrap ? MinimumWidth : Math.Clamp(layout.WidthIncludingTrailingWhitespace + Padding.Left + Padding.Right + caretRoom, MinimumWidth, Math.Max(MinimumWidth, MaximumWidth));
        // One line sits on the cell's baseline inside the cell; several lines start at the top and the field grows down.
        var bottom = Baseline is not null && layout.TextLines.Count <= 1 ? 0 : Padding.Bottom;
        var height = Math.Max(MinHeight, TextTop(layout) + layout.Height + bottom);
        return new Size(SnapUp(width), SnapUp(height));
    }

    /// <summary>The screen's device pixels per DIP (1 before the field is shown).</summary>
    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>A length rounded up to whole device pixels (the field's edges and frame stay sharp at 125 % and 150 %).</summary>
    private double SnapUp(double length)
    {
        var scaling = Scaling;
        return Math.Ceiling(length * scaling - 0.01) / scaling;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundBrushProperty)
        {
            _layout = null;
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _layout = null;
        KeepCaretVisible();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (FieldBackground is { } background)
        {
            context.FillRectangle(background, bounds);
        }

        var layout = Layout();
        var origin = new Point(Padding.Left - _offsetX, TextTop(layout) - _offsetY);
        var inner = bounds.Deflate(Frame);
        if (FrameBrush is { } frame && Frame != default)
        {
            // Four bands inside the edges: exactly the active cell's border (whole device pixels, given by the cell editing).
            context.FillRectangle(frame, new Rect(0, 0, bounds.Width, Frame.Top));
            context.FillRectangle(frame, new Rect(0, bounds.Height - Frame.Bottom, bounds.Width, Frame.Bottom));
            context.FillRectangle(frame, new Rect(0, 0, Frame.Left, bounds.Height));
            context.FillRectangle(frame, new Rect(bounds.Width - Frame.Right, 0, Frame.Right, bounds.Height));
        }

        using (context.PushClip(inner))
        {
            if (SelectionStart != SelectionEnd && (IsActive || IsFocused))
            {
                foreach (var rect in layout.HitTestTextRange(SelectionStart, SelectionEnd - SelectionStart))
                {
                    context.FillRectangle(SelectionBrush ?? ThixOffice.Desktop.Ui.Brush("Mat.Selection"), rect.Translate(origin));
                }
            }

            layout.Draw(context, origin);
            if ((IsActive || IsFocused) && _caretOn && SelectionStart == SelectionEnd)
            {
                var caret = CaretRect();
                var scaling = Scaling;
                var caretWidth = Math.Max(1, Math.Round(scaling)) / scaling;
                context.FillRectangle(CaretBrush ?? Foreground, new Rect(Math.Round(caret.X * scaling) / scaling, caret.Y, caretWidth, caret.Height));
            }
        }
    }

    private void Changed(bool edited)
    {
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
        KeepCaretVisible();
        RestartBlink();
        InputClient.NotifyChanged();
        if (edited)
        {
            TextEdited?.Invoke(this, EventArgs.Empty);
        }

        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void Moved()
    {
        InvalidateVisual();
        KeepCaretVisible();
        RestartBlink();
        InputClient.NotifyChanged();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls a one-line field sideways (and a wrapped one up/down) so that the caret shows.</summary>
    private void KeepCaretVisible()
    {
        if (Bounds.Width <= 0)
        {
            return;
        }

        var layout = Layout();
        var caret = layout.HitTestTextPosition(Math.Min(_caret, DisplayText.Length));
        var width = Bounds.Width - Padding.Left - Padding.Right;
        var height = Bounds.Height - TextTop(layout) - Padding.Bottom;
        if (Baseline is not null)
        {
            // The cell's field grows with its lines: its text never scrolls up and down.
            height = double.PositiveInfinity;
        }

        if (double.IsInfinity(WrapWidth()) && !GrowsWithText)
        {
            if (caret.X - _offsetX > width - 2)
            {
                _offsetX = caret.X - width + 2;
            }
            else if (caret.X < _offsetX)
            {
                _offsetX = Math.Max(0, caret.X - 10);
            }
        }
        else
        {
            _offsetX = 0;
        }

        if (caret.Bottom - _offsetY > height)
        {
            _offsetY = caret.Bottom - height;
        }
        else if (caret.Y < _offsetY)
        {
            _offsetY = caret.Y;
        }

        if (layout.Height <= height)
        {
            _offsetY = 0;
        }
    }

    private void RestartBlink()
    {
        _caretOn = true;
        _blink.Stop();
        if (IsActive || IsFocused)
        {
            _blink.Start();
        }

        InvalidateVisual();
    }
}

/// <summary>
/// What an input method sees of a <see cref="FormulaTextBox"/>: its text, the selection and the caret rectangle; the
/// composition shows at the caret (underlined), the finished text arrives as text input.
/// </summary>
internal sealed class FormulaInputClient(FormulaTextBox box) : TextInputMethodClient
{
    public override Visual TextViewVisual => box;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => true;

    public override string SurroundingText => box.Text;

    public override Rect CursorRectangle => box.CaretRect();

    public override TextSelection Selection
    {
        get => new(box.SelectionStart, box.SelectionEnd);
        set => box.Select(value.Start, value.End);
    }

    public override void SetPreeditText(string? preeditText) => box.SetPreedit(preeditText);

    public override void SetPreeditText(string? preeditText, int? cursorPos) => box.SetPreedit(preeditText);

    public void NotifyChanged()
    {
        RaiseCursorRectangleChanged();
        RaiseSurroundingTextChanged();
        RaiseSelectionChanged();
    }
}

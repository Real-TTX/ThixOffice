using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Text.Unicode;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The pages of one document, stacked under each other on the workspace, and everything the user does on them: the paper with
/// a subtle border and shadow, the page pictures (<see cref="PagePictures"/>, drawn through Avalonia's Skia lease – only the
/// pages in the viewport), the selection in the system's selection colour, the blinking caret and the IME's composition
/// (<see cref="CaretLayer"/>), keyboard (caret movement with Shift, deleting, Enter, Tab), text input and the input method
/// (<see cref="WriteInputClient"/>), mouse (click, Shift+click, drag with auto-scroll, double click = word, triple click =
/// paragraph). It sits in the editor's ScrollViewer, which scrolls it.
/// <para>Coordinates: the layout works in points per page; this control in device-independent pixels; 1 pt = 96/72 DIP at 100 %.</para>
/// </summary>
internal sealed class DocumentSurface : Control
{
    /// <summary>Device-independent pixels per point at 100 % (Word shows A4 at 96 DPI).</summary>
    public const double PointsToDips = 96.0 / 72.0;

    /// <summary>The workspace left and right of the widest page and above the first / below the last page (DIP).</summary>
    public const double SideMargin = 28;

    public const double TopMargin = 22;

    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);
    private readonly CaretLayer _caretLayer = new();
    private readonly Cursor _ibeam = new(StandardCursorType.Ibeam);
    private readonly DispatcherTimer _blink;
    private readonly DispatcherTimer _autoScroll;
    private readonly IBrush _paper = new SolidColorBrush(DocumentPalette.Paper);
    private Rect _viewport;
    private bool _refreshPending;
    private bool _dragging;
    private DocumentRange? _wordAnchor;
    private Point _pointer;
    private double _scale = PointsToDips;

    public DocumentSurface(EditingSession session, DocumentLayout layout)
    {
        Session = session;
        Layout = layout;
        InputClient = new WriteInputClient(this);
        Focusable = true;
        ClipToBounds = false;
        Cursor = _ibeam;
        VisualChildren.Add(_caretLayer);

        _blink = new DispatcherTimer { Interval = BlinkInterval };
        _blink.Tick += (_, _) => _caretLayer.IsBlinkedOff = !_caretLayer.IsBlinkedOff;
        _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _autoScroll.Tick += (_, _) => AutoScroll();

        Layout.Changed += OnLayoutChanged;
        Session.SelectionChanged += OnSessionChanged;
        Session.ContentChanged += OnSessionChanged;
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            Layout.ViewportHeight = _viewport.Height > 0 ? _viewport.Height / _scale : null;
            InvalidateVisual();
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        AddHandler(InputElement.TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputClient);
    }

    public EditingSession Session { get; }

    public DocumentLayout Layout { get; }

    /// <summary>The page pictures (the formatting marks switch them).</summary>
    public PagePictures Pictures { get; } = new();

    /// <summary>The input method's view of the document.</summary>
    public WriteInputClient InputClient { get; }

    /// <summary>The caret and the composition (tests).</summary>
    public CaretLayer CaretLayer => _caretLayer;

    /// <summary>The visible part of this control (DIP), as the ScrollViewer reports it.</summary>
    public Rect Viewport => _viewport;

    /// <summary>DIP per point: the zoom × 96/72.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            if (Math.Abs(_scale - value) < 1e-9)
            {
                return;
            }

            _scale = value;
            InvalidateMeasure();
            InvalidateVisual();
            ScheduleRefresh();
        }
    }

    /// <summary>Scrolls the ScrollViewer (auto-scroll while dragging a selection); set by the editor.</summary>
    public Action<double>? ScrollBy { get; set; }

    /// <summary>Raised after the view has caught up with a change of the selection or the content (status bar, find bar).</summary>
    public event EventHandler? Refreshed;

    /// <summary>The page pictures drawn by the last render (tests: only the visible pages are drawn).</summary>
    public IReadOnlyList<int> DrawnPages { get; private set; } = [];

    // ===== Geometry =====

    /// <summary>The rectangle of page <paramref name="page"/> in this control (DIP).</summary>
    public Rect PageRect(int page)
    {
        var layout = Layout.Pages[page];
        var width = layout.Width * _scale;
        var x = Math.Max(SideMargin, (Bounds.Width - width) / 2);
        return new Rect(x, TopMargin + (Layout.PageTop(page) * _scale), width, layout.Height * _scale);
    }

    /// <summary>A rectangle of a page (points) in this control (DIP).</summary>
    public Rect ToSurface(int page, double x, double y, double width, double height)
    {
        var origin = PageRect(page).TopLeft;
        return new Rect(origin.X + (x * _scale), origin.Y + (y * _scale), width * _scale, height * _scale);
    }

    /// <summary>The caret place nearest to a point of this control.</summary>
    public HitResult HitAt(Point point)
    {
        var (page, y) = Layout.PageAt((point.Y - TopMargin) / _scale);
        var rect = PageRect(page);
        return Layout.HitTest(page, (point.X - rect.X) / _scale, y);
    }

    /// <summary>The page under a point, or -1 on the workspace.</summary>
    public int PageUnder(Point point)
    {
        var (page, _) = Layout.PageAt((point.Y - TopMargin) / _scale);
        return PageRect(page).Contains(point) ? page : -1;
    }

    /// <summary>Where the caret is drawn (DIP), from the active end of the selection.</summary>
    public Rect CaretRect()
    {
        var caret = Layout.GetCaret(new CaretPlace(Session.Selection.Active, Session.Selection.Affinity));
        var width = Math.Max(1, _scale * 0.75);
        var rect = ToSurface(caret.Page, caret.X, caret.Top, 0, caret.Height);
        return rect.WithWidth(width).WithX(rect.X - (width / 2));
    }

    /// <summary>The page the caret is on.</summary>
    public int CaretPage => Layout.GetCaret(new CaretPlace(Session.Selection.Active, Session.Selection.Affinity)).Page;

    // ===== Layout and drawing =====

    protected override Size MeasureOverride(Size availableSize)
    {
        var pages = Layout.Pages;
        var width = pages.Count == 0 ? 0 : pages.Max(page => page.Width) * _scale;
        _caretLayer.Measure(availableSize);
        return new Size(width + (2 * SideMargin), (Layout.StackedHeight * _scale) + (2 * TopMargin));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _caretLayer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var visible = _viewport.Width > 0 && _viewport.Height > 0 ? _viewport : new Rect(Bounds.Size);
        var border = new Pen(Ui.Brush("Mat.Border"), 1);
        var shadow = ShadowColor();
        var shadows = new BoxShadows(new BoxShadow { OffsetY = 1, Blur = 6, Color = shadow });
        var pages = new List<(Rect Rect, SKPicture Picture)>();
        var drawn = new List<int>();
        if (Layout.PageCount > 0)
        {
            var (first, _) = Layout.PageAt(Math.Max(0, (visible.Top - TopMargin) / _scale));
            for (var page = first; page < Layout.PageCount; page++)
            {
                var rect = PageRect(page);
                if (rect.Top > visible.Bottom)
                {
                    break;
                }

                if (rect.Bottom < visible.Top)
                {
                    continue;
                }

                context.DrawRectangle(_paper, border, new RoundedRect(rect.Inflate(0.5)), shadows);
                pages.Add((rect, Pictures.Get(Layout, page)));
                drawn.Add(page);
            }
        }

        DrawnPages = drawn;
        if (pages.Count > 0)
        {
            context.Custom(new PageDrawOperation(visible.Inflate(2), pages, _scale));
        }

        DrawSelection(context, visible);
    }

    private void DrawSelection(DrawingContext context, Rect visible)
    {
        var selection = Session.Selection;
        if (selection.IsCollapsed)
        {
            return;
        }

        var brush = Ui.Brush("Mat.Selection");
        foreach (var rect in Layout.GetSelectionRects(selection.Range))
        {
            var surface = ToSurface(rect.Page, rect.X, rect.Y, rect.Width, rect.Height);
            if (surface.Intersects(visible))
            {
                context.FillRectangle(brush, surface);
            }
        }
    }

    /// <summary>The page shadow: the overlay token of the theme, much lighter.</summary>
    private static Color ShadowColor()
    {
        var overlay = Ui.Brush("Mat.Overlay") is ISolidColorBrush solid ? solid.Color : default;
        return new Color((byte)(overlay.A * 0.32), overlay.R, overlay.G, overlay.B);
    }

    // ===== Following the session =====

    private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
    {
        Pictures.InvalidateFrom(e.FirstChangedPage);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => ScheduleRefresh();

    /// <summary>Catches the view up with the session once the current input is handled (several changes, one refresh).</summary>
    public void ScheduleRefresh()
    {
        if (_refreshPending)
        {
            return;
        }

        _refreshPending = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Normal);
    }

    /// <summary>Brings layout, caret, selection and the input method up to date and keeps the caret in view.</summary>
    public void Refresh()
    {
        _refreshPending = false;
        Layout.Update();
        UpdateCaret();
        InvalidateVisual();
        if (_caretLayer.CaretRect is { } caret)
        {
            this.BringIntoView(caret.Inflate(new Thickness(24, 16)));
        }

        InputClient.NotifyChanged();
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCaret()
    {
        _caretLayer.CaretRect = CaretRect();
        _caretLayer.ShowsCaret = Session.Selection.IsCollapsed && IsFocused;
        RestartBlink();
    }

    /// <summary>The caret shows steadily while the user types or moves; it blinks again after a pause.</summary>
    private void RestartBlink()
    {
        _caretLayer.IsBlinkedOff = false;
        _blink.Stop();
        if (IsFocused)
        {
            _blink.Start();
        }
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateCaret();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _blink.Stop();
        _caretLayer.ShowsCaret = false;
        SetPreedit(null);
    }

    /// <summary>The composition of the input method at the caret (null: none).</summary>
    public void SetPreedit(string? text)
    {
        _caretLayer.Preedit = string.IsNullOrEmpty(text) ? null : text;
        _caretLayer.PreeditSize = Math.Max(8, (Session.GetFormatting().FontSize.IsMixed ? 11 : Session.GetFormatting().FontSize.Value) * _scale);
        _caretLayer.PreeditFont = Session.GetFormatting().FontFamily is { IsMixed: false } family ? family.Value : null;
        RestartBlink();
    }

    // ===== Keyboard =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        e.Handled = HandleKey(e.Key, e.KeyModifiers);
        if (e.Handled)
        {
            RestartBlink();
        }
    }

    /// <summary>The keys of the document: caret movement (with Shift), deleting, Enter, Tab. Windows and macOS conventions.</summary>
    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var isMac = OperatingSystem.IsMacOS();
        var word = isMac ? modifiers.HasFlag(KeyModifiers.Alt) : modifiers.HasFlag(KeyModifiers.Control);
        var command = isMac && modifiers.HasFlag(KeyModifiers.Meta);
        var documentKey = modifiers.HasFlag(isMac ? KeyModifiers.Meta : KeyModifiers.Control);
        CaretMovement? movement = key switch
        {
            Key.Left when command => CaretMovement.LineStart,
            Key.Right when command => CaretMovement.LineEnd,
            Key.Left => word ? CaretMovement.WordLeft : CaretMovement.CharacterLeft,
            Key.Right => word ? CaretMovement.WordRight : CaretMovement.CharacterRight,
            Key.Up when command => CaretMovement.DocumentStart,
            Key.Down when command => CaretMovement.DocumentEnd,
            Key.Up => word ? CaretMovement.ParagraphUp : CaretMovement.LineUp,
            Key.Down => word ? CaretMovement.ParagraphDown : CaretMovement.LineDown,
            Key.Home => documentKey ? CaretMovement.DocumentStart : CaretMovement.LineStart,
            Key.End => documentKey ? CaretMovement.DocumentEnd : CaretMovement.LineEnd,
            Key.PageUp => CaretMovement.PageUp,
            Key.PageDown => CaretMovement.PageDown,
            _ => null,
        };
        if (movement is { } move)
        {
            Session.Move(move, shift);
            return true;
        }

        switch (key)
        {
            case Key.Back:
                Session.DeleteBackward(word ? DeleteUnit.Word : DeleteUnit.Character);
                return true;
            case Key.Delete:
                Session.DeleteForward(word ? DeleteUnit.Word : DeleteUnit.Character);
                return true;
            case Key.Enter when shift:
                Session.InsertLineBreak();
                return true;
            case Key.Enter when documentKey:
                Session.InsertPageBreak();
                return true;
            case Key.Enter:
                Session.InsertParagraphBreak();
                return true;
            case Key.Tab when !documentKey:
                Session.HandleTab(shift);
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
        if (text.Length == 0)
        {
            return;
        }

        _caretLayer.Preedit = null;
        Session.InsertText(text);
        e.Handled = true;
        RestartBlink();
    }

    // ===== Mouse =====

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var hit = HitAt(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            // Right click outside the selection: the caret goes there first (the context menu acts on it).
            var selection = Session.Selection;
            if (selection.IsCollapsed || !selection.Range.Contains(hit.Position))
            {
                Session.SetCaret(hit.Position, hit.Place.Affinity);
            }

            Focus();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        if (hit.PreservedBlock is { } block)
        {
            Session.SelectBlock(block);
        }
        else if (e.ClickCount == 2)
        {
            Session.SelectWord(hit.Position);
        }
        else if (e.ClickCount >= 3)
        {
            Session.SelectParagraph(hit.Position);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Session.ExtendTo(hit.Position, hit.Place.Affinity);
        }
        else
        {
            Session.SetCaret(hit.Position, hit.Place.Affinity);
        }

        // Dragging after a double click extends by whole words (like Word); the word clicked stays selected.
        _dragging = e.ClickCount <= 2 && hit.PreservedBlock is null;
        _wordAnchor = e.ClickCount == 2 ? Session.Selection.Range : null;
        _pointer = point.Position;
        if (_dragging)
        {
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var cursor = _dragging || PageUnder(position) >= 0 ? _ibeam : Cursor.Default;
        if (!ReferenceEquals(Cursor, cursor))
        {
            Cursor = cursor;
        }

        if (!_dragging)
        {
            return;
        }

        _pointer = position;
        ExtendToPointer();
        var outside = position.Y < _viewport.Top || position.Y > _viewport.Bottom;
        if (outside && !_autoScroll.IsEnabled)
        {
            _autoScroll.Start();
        }
        else if (!outside)
        {
            _autoScroll.Stop();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        _autoScroll.Stop();
    }

    private void ExtendToPointer()
    {
        var hit = HitAt(_pointer);
        if (_wordAnchor is not { } word || Session.Document.ParagraphAt(hit.Position.Block) is not { } paragraph)
        {
            Session.ExtendTo(hit.Position, hit.Place.Affinity);
            return;
        }

        var (start, end) = TextBoundaries.WordAt(paragraph.Text, Math.Min(hit.Position.Offset, paragraph.Text.Length));
        if (hit.Position < word.Start)
        {
            Session.Select(word.End, hit.Position with { Offset = start });
        }
        else
        {
            Session.Select(word.Start, DocumentPosition.Max(word.End, hit.Position with { Offset = end }));
        }
    }

    /// <summary>The pointer is above or below the viewport while dragging: scroll toward it (faster the farther) and extend.</summary>
    private void AutoScroll()
    {
        if (!_dragging || ScrollBy is null)
        {
            _autoScroll.Stop();
            return;
        }

        var distance = _pointer.Y < _viewport.Top ? _pointer.Y - _viewport.Top : _pointer.Y > _viewport.Bottom ? _pointer.Y - _viewport.Bottom : 0;
        if (distance == 0)
        {
            _autoScroll.Stop();
            return;
        }

        var step = Math.Clamp(distance / 2, -60, 60);
        ScrollBy(step);
        _pointer = _pointer.WithY(_pointer.Y + step);
        ExtendToPointer();
    }

    /// <summary>Stops listening to the session (the window let go of the editor).</summary>
    public void Detach()
    {
        _blink.Stop();
        _autoScroll.Stop();
        Layout.Changed -= OnLayoutChanged;
        Session.SelectionChanged -= OnSessionChanged;
        Session.ContentChanged -= OnSessionChanged;
    }
}

/// <summary>
/// The caret (a thin line in ink colour, blinking) and the composition of an input method (the text being composed, underlined,
/// at the caret) over the pages. A layer of its own: blinking redraws only this, never the pages.
/// </summary>
internal sealed class CaretLayer : Control
{
    private Rect? _caretRect;
    private bool _showsCaret;
    private bool _isBlinkedOff;
    private string? _preedit;

    public CaretLayer()
    {
        IsHitTestVisible = false;
    }

    public Rect? CaretRect
    {
        get => _caretRect;
        set
        {
            _caretRect = value;
            InvalidateVisual();
        }
    }

    public bool ShowsCaret
    {
        get => _showsCaret;
        set
        {
            _showsCaret = value;
            InvalidateVisual();
        }
    }

    public bool IsBlinkedOff
    {
        get => _isBlinkedOff;
        set
        {
            _isBlinkedOff = value;
            InvalidateVisual();
        }
    }

    /// <summary>The text an input method is composing, or null.</summary>
    public string? Preedit
    {
        get => _preedit;
        set
        {
            _preedit = value;
            InvalidateVisual();
        }
    }

    public double PreeditSize { get; set; } = 14;

    public string? PreeditFont { get; set; }

    /// <summary>True while the caret is drawn now (shown and not in the off phase of blinking).</summary>
    public bool IsCaretVisible => _showsCaret && !_isBlinkedOff && _caretRect is not null;

    public override void Render(DrawingContext context)
    {
        if (_caretRect is not { } caret)
        {
            return;
        }

        var ink = new SolidColorBrush(DocumentPalette.Ink);
        var x = caret.X;
        if (_preedit is { Length: > 0 } preedit)
        {
            var typeface = new Typeface(PreeditFont is null ? FontFamily.Default : new FontFamily(PreeditFont));
            var text = new FormattedText(preedit, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, PreeditSize, ink);
            var top = caret.Bottom - text.Height;
            var area = new Rect(caret.X, top, text.WidthIncludingTrailingWhitespace, text.Height);
            context.FillRectangle(new SolidColorBrush(DocumentPalette.Paper), area);
            context.DrawText(text, area.TopLeft);
            context.DrawLine(new Pen(ink, 1), area.BottomLeft, area.BottomRight);
            x = area.Right;
        }

        if (IsCaretVisible)
        {
            context.FillRectangle(ink, caret.WithX(x));
        }
    }
}

/// <summary>
/// What an input method (IME, dead keys on macOS, the emoji panel, CJK composition) sees of the document: the paragraph around
/// the caret as surrounding text, the selection in it, and the caret rectangle (where the candidate window opens). The
/// composed text is shown at the caret (<see cref="CaretLayer"/>); the finished text arrives as text input.
/// </summary>
internal sealed class WriteInputClient(DocumentSurface surface) : TextInputMethodClient
{
    public override Visual TextViewVisual => surface;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => true;

    public override string SurroundingText => surface.Session.Document.ParagraphAt(surface.Session.Selection.Active.Block)?.Text ?? "";

    public override Rect CursorRectangle => surface.CaretLayer.CaretRect ?? default;

    public override TextSelection Selection
    {
        get
        {
            var selection = surface.Session.Selection;
            var active = selection.Active;
            var anchor = selection.Anchor.Block == active.Block ? selection.Anchor.Offset : active.Offset;
            return new TextSelection(anchor, active.Offset);
        }
        set
        {
            var block = surface.Session.Selection.Active.Block;
            if (surface.Session.Document.ParagraphAt(block) is not { } paragraph)
            {
                return;
            }

            var start = Math.Clamp(value.Start, 0, paragraph.Length);
            var end = Math.Clamp(value.End, 0, paragraph.Length);
            surface.Session.Select(new DocumentPosition(block, start), new DocumentPosition(block, end));
        }
    }

    public override void SetPreeditText(string? preeditText) => surface.SetPreedit(preeditText);

    public override void SetPreeditText(string? preeditText, int? cursorPos) => surface.SetPreedit(preeditText);

    /// <summary>The caret, the selection or the text changed.</summary>
    public void NotifyChanged()
    {
        RaiseCursorRectangleChanged();
        RaiseSurroundingTextChanged();
        RaiseSelectionChanged();
    }
}

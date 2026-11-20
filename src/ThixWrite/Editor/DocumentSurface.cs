using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
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
    private readonly Cursor _columnCursor = new(StandardCursorType.SizeWestEast);
    private readonly Cursor _moveCursor = new(StandardCursorType.SizeAll);
    private ColumnDrag? _columnDrag;
    private ImageDrag? _imageDrag;
    private bool _dimBody;
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
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && Layout.PageCount > 0)
            {
                // The pages are centred: a new width (a pane opened beside them) moves them, the caret goes along.
                _caretLayer.CaretRect = CaretRect();
            }
        };
        AddHandler(InputElement.TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputClient);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>Inserts a picture file dropped at a position (the editor reads it through the business layer); set by the editor.</summary>
    public Func<string, DocumentPosition, Task>? DropPicture { get; set; }

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

    /// <summary>
    /// A header or footer being edited: the surface shows only the story's one page, from its top left, as tall as its text (at
    /// least <see cref="MinimumHeight"/> points), on paper without a frame – laid over the page of the document.
    /// </summary>
    public bool IsStory { get; init; }

    /// <summary>The least height of a story surface (points): the header or footer area it covers.</summary>
    public double MinimumHeight { get; set; }

    /// <summary>While a header or footer is edited the body of every page is dimmed and the areas are marked (as in Word).</summary>
    public bool DimBody
    {
        get => _dimBody;
        set
        {
            _dimBody = value;
            InvalidateVisual();
        }
    }

    /// <summary>A double click in the header (false) or footer (true) area of a page: the editor opens it.</summary>
    public event EventHandler<(int Page, bool Footer)>? HeaderFooterRequested;

    /// <summary>A click or a drag of the pointer made a selection (Format übertragen applies to it).</summary>
    public event EventHandler? SelectedByPointer;

    /// <summary>Strg+Klick (⌘ on the Mac) on a link: its address (the editor opens it through the business layer).</summary>
    public event EventHandler<Hyperlink>? LinkClicked;

    /// <summary>A double click on the body while a header or footer is edited: the editor closes it.</summary>
    public event EventHandler? BodyRequested;

    /// <summary>A click on the text of a footnote or endnote (or a double click on its reference): the note and the point (page points).</summary>
    public event EventHandler<(PlacedNote Note, Point At)>? NoteRequested;

    /// <summary>The height of the story's text (points).</summary>
    public double StoryHeight
    {
        get
        {
            var lines = Layout.Pages.Count > 0 ? Layout.Pages[0].Lines : [];
            var bottom = lines.Count == 0 ? 0 : lines.Max(line => line.Top + line.Line.Height);
            return Math.Max(MinimumHeight, bottom + 2);
        }
    }

    private double Side => IsStory ? 0 : SideMargin;

    private double Top => IsStory ? 0 : TopMargin;

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
        if (IsStory)
        {
            return new Rect(0, 0, width, StoryHeight * _scale);
        }

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
        var (page, y) = Layout.PageAt((point.Y - Top) / _scale);
        var rect = PageRect(page);
        return Layout.HitTest(page, (point.X - rect.X) / _scale, y);
    }

    /// <summary>The page under a point, or -1 on the workspace.</summary>
    public int PageUnder(Point point)
    {
        var (page, _) = Layout.PageAt((point.Y - Top) / _scale);
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
        if (IsStory)
        {
            return new Size(width, StoryHeight * _scale);
        }

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
        if (IsStory)
        {
            // A story lies over a page: it draws (and is hit) only where it is, not over the whole viewport.
            visible = visible.Intersect(new Rect(Bounds.Size));
        }
        var border = new Pen(Ui.Brush("Mat.Border"), 1);
        var shadow = ShadowColor();
        var shadows = new BoxShadows(new BoxShadow { OffsetY = 1, Blur = 6, Color = shadow });
        var pages = new List<(Rect Rect, SKPicture Picture)>();
        var drawn = new List<int>();
        if (Layout.PageCount > 0)
        {
            var (first, _) = Layout.PageAt(Math.Max(0, (visible.Top - Top) / _scale));
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

                if (IsStory)
                {
                    context.FillRectangle(_paper, rect);
                }
                else
                {
                    context.DrawRectangle(_paper, border, new RoundedRect(rect.Inflate(0.5)), shadows);
                }

                pages.Add((rect, Pictures.Get(Layout, page)));
                drawn.Add(page);
            }
        }

        DrawnPages = drawn;
        if (pages.Count > 0)
        {
            var scale = _scale;
            context.Custom(new ThixOffice.Desktop.Rendering.SkiaDrawOperation(visible.Inflate(2), canvas => ThixOffice.Desktop.Rendering.SkiaDrawOperation.DrawPictures(canvas, pages, scale)));
        }

        if (_dimBody)
        {
            DrawHeaderFooterAreas(context, drawn);
        }

        DrawShading(context, visible);
        if (!IsStory)
        {
            DrawMisspelled(context, visible, drawn);
        }

        DrawSelection(context, visible);
    }

    /// <summary>While a header or footer is edited: the bodies dimmed, the areas bounded by a dashed line with their names.</summary>
    private void DrawHeaderFooterAreas(DrawingContext context, IReadOnlyList<int> pages)
    {
        var dim = new SolidColorBrush(DocumentPalette.DimmedBody);
        var line = new Pen(Ui.Brush("Mat.Accent"), 1, new DashStyle([4, 3], 0));
        var label = Ui.Brush("Mat.Accent");
        foreach (var page in pages)
        {
            var setup = Layout.Pages[page].Section.Properties;
            var rect = PageRect(page);
            var top = rect.Y + (setup.MarginTop * _scale);
            var bottom = rect.Y + ((setup.PageHeight - setup.MarginBottom) * _scale);
            context.FillRectangle(dim, new Rect(rect.X, top, rect.Width, Math.Max(0, bottom - top)));
            context.DrawLine(line, new Point(rect.X, top), new Point(rect.Right, top));
            context.DrawLine(line, new Point(rect.X, bottom), new Point(rect.Right, bottom));
            DrawAreaLabel(context, "Kopfzeile", new Point(rect.X + 6, top + 2), label);
            DrawAreaLabel(context, "Fußzeile", new Point(rect.X + 6, bottom - 16), label);
        }
    }

    private static void DrawAreaLabel(DrawingContext context, string text, Point at, IBrush brush)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);
        context.DrawText(formatted, at);
    }

    /// <summary>The text of comments to shade (the active one stronger); set by the editor, asked on every render.</summary>
    public Func<IReadOnlyList<(DocumentRange Range, bool Active)>>? Shading { get; set; }

    /// <summary>Over the page pictures (no page is drawn again): the text comments are about.</summary>
    private void DrawShading(DrawingContext context, Rect visible)
    {
        if (Shading?.Invoke() is not { Count: > 0 } ranges)
        {
            return;
        }

        var normal = new SolidColorBrush(DocumentPalette.CommentRange);
        var active = new SolidColorBrush(DocumentPalette.CommentRangeActive);
        foreach (var (range, isActive) in ranges)
        {
            foreach (var rect in Layout.GetSelectionRects(range))
            {
                var surface = ToSurface(rect.Page, rect.X, rect.Y, rect.Width, rect.Height);
                if (surface.Intersects(visible))
                {
                    context.FillRectangle(isActive ? active : normal, surface);
                }
            }
        }
    }

    /// <summary>The unknown words on the pages drawn (the spelling check of the editor), asked on every render; null: none.</summary>
    public Func<IReadOnlyList<int>, IReadOnlyList<DocumentRange>>? Misspelled { get; set; }

    /// <summary>The ranges the last render underlined (tests).</summary>
    public IReadOnlyList<DocumentRange> UnderlinedWords { get; private set; } = [];

    /// <summary>Red wavy lines under unknown words, over the page pictures (no page is drawn again).</summary>
    private void DrawMisspelled(DrawingContext context, Rect visible, IReadOnlyList<int> pages)
    {
        UnderlinedWords = Misspelled?.Invoke(pages) ?? [];
        if (UnderlinedWords.Count == 0)
        {
            return;
        }

        var pen = new Pen(new SolidColorBrush(DocumentPalette.Misspelled), Math.Max(1, _scale * 0.6));
        var step = Math.Max(2, _scale * 1.6);
        foreach (var range in UnderlinedWords)
        {
            var caret = Layout.GetCaret(new CaretPlace(range.Start));
            foreach (var rect in Layout.GetSelectionRects(range))
            {
                var area = ToSurface(rect.Page, rect.X, caret.Top + caret.Height, rect.Width, 0);
                if (!area.Intersects(visible.Inflate(4)))
                {
                    continue;
                }

                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    var x = area.X;
                    var up = true;
                    path.BeginFigure(new Point(x, area.Y), false);
                    while (x < area.Right)
                    {
                        x = Math.Min(area.Right, x + step);
                        path.LineTo(new Point(x, area.Y + (up ? -step / 2 : 0)));
                        up = !up;
                    }

                    path.EndFigure(false);
                }

                context.DrawGeometry(null, pen, geometry);
            }
        }
    }

    private void DrawSelection(DrawingContext context, Rect visible)
    {
        var selection = Session.Selection;
        if (selection.IsCollapsed)
        {
            return;
        }

        if (SelectedImageRect() is { } image)
        {
            DrawImageFrame(context, _imageDrag is { } drag ? Resized(drag, image) : image);
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

    /// <summary>A selected picture: a frame in the accent with eight handles (Word's sizing handles).</summary>
    private static void DrawImageFrame(DrawingContext context, Rect rect) => SelectionHandles.Draw(context, rect);

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
        if (!IsStory && e.ClickCount == 2 && AreaAt(point.Position) is { } area && (area.Footer is not null || _dimBody))
        {
            // A double click in the margin above or below the text: the header or the footer of that page (and back to the body).
            e.Handled = true;
            if (area.Footer is { } footer)
            {
                HeaderFooterRequested?.Invoke(this, (area.Page, footer));
            }
            else if (_dimBody)
            {
                BodyRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (!IsStory && !_dimBody && PageUnder(point.Position) is >= 0 and var notePage)
        {
            var origin = PageRect(notePage);
            var local = new Point((point.Position.X - origin.X) / _scale, (point.Position.Y - origin.Y) / _scale);
            if (Layout.NoteAt(notePage, local.X, local.Y) is { } note)
            {
                e.Handled = true;
                NoteRequested?.Invoke(this, (note, local));
                return;
            }

            if (e.ClickCount == 2 && NoteReferenceAt(hit.Position) is { } reference && Layout.FindNote(reference) is { } found)
            {
                e.Handled = true;
                NoteRequested?.Invoke(this, (found.Note, new Point(found.Note.Lines[0].Left, found.Note.Lines[0].Top)));
                return;
            }
        }

        if (SelectedImageRect() is { } selected && HandleAt(selected, point.Position) is { } handle)
        {
            // A handle of the selected picture: dragging sizes it (corners keep its proportions unless Umschalt is held).
            _imageDrag = new ImageDrag(handle, point.Position, point.Position, selected);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var follow = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(follow) && LinkUnder(point.Position) is { } link)
        {
            e.Handled = true;
            LinkClicked?.Invoke(this, link);
            return;
        }

        if (ImageUnder(point.Position) is { } picture && e.ClickCount == 1 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Session.Select(picture, picture with { Offset = picture.Offset + 1 });
            if (Session.SelectedImage?.Image.Anchor is not null && SelectedImageRect() is { } floating)
            {
                // A floating picture: dragging it moves it (Word's four-way arrow).
                _imageDrag = new ImageDrag(MoveHandle, point.Position, point.Position, floating);
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (ColumnBorderAt(point.Position) is { } border)
        {
            // A column border of a table: dragging it changes the widths of the columns beside it.
            _columnDrag = border;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

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
        if (_imageDrag is { } drag)
        {
            _imageDrag = drag with { Current = position };
            if (drag.Handle == MoveHandle && !ReferenceEquals(Cursor, _moveCursor))
            {
                Cursor = _moveCursor;
            }

            InvalidateVisual();
            return;
        }

        ShowLinkTip(position);
        var handle = !_dragging && SelectedImageRect() is { } selected ? HandleAt(selected, position) : null;
        var cursor = handle is { } index ? HandleCursor(index)
            : _columnDrag is not null || (!_dragging && ColumnBorderAt(position) is not null) ? _columnCursor
            : _imageDrag is { Handle: MoveHandle } ? _moveCursor
            : !_dragging && ImageUnder(position) is { } under ? Session.Document.ParagraphAt(under.Block)?.InlineAt(under.Offset) is ImageInline { Anchor: not null } ? _moveCursor : Cursor.Default
            : _dragging || PageUnder(position) >= 0 ? _ibeam : Cursor.Default;
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
        if (_columnDrag is { } drag)
        {
            _columnDrag = null;
            ResizeColumn(drag, e.GetPosition(this));
        }

        if (_imageDrag is { Handle: MoveHandle } moving && Session.SelectedImage is { } floating && SelectedImageRect() is { } from)
        {
            _imageDrag = null;
            var to = Resized(moving with { Current = e.GetPosition(this) }, from);
            var page = PageUnder(from.Center);
            if ((Math.Abs(to.X - from.X) > 0.5 || Math.Abs(to.Y - from.Y) > 0.5) && page >= 0)
            {
                var pageRect = PageRect(page);
                if (Layout.AnchorOffsetsFor(floating.Position, (to.X - pageRect.X) / _scale, (to.Y - pageRect.Y) / _scale) is { } offsets)
                {
                    Session.MoveImage(offsets.OffsetX, offsets.OffsetY);
                }
            }

            InvalidateVisual();
        }
        else if (_imageDrag is { } sizing && SelectedImageRect() is { } before)
        {
            _imageDrag = null;
            var after = Resized(sizing with { Current = e.GetPosition(this), Free = e.KeyModifiers.HasFlag(KeyModifiers.Shift) }, before);
            if (Math.Abs(after.Width - before.Width) > 0.5 || Math.Abs(after.Height - before.Height) > 0.5)
            {
                Session.SetImageSize(after.Width / _scale, after.Height / _scale);
            }

            InvalidateVisual();
        }

        var selected = _dragging;
        EndDrag();
        e.Pointer.Capture(null);
        if (selected)
        {
            SelectedByPointer?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        _columnDrag = null;
        _imageDrag = null;
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

    private const double HandleSize = 7;

    private string? _tipLink;

    /// <summary>The link under a point (on its text, not beside it), or null.</summary>
    private Hyperlink? LinkUnder(Point point)
    {
        if (PageUnder(point) < 0)
        {
            return null;
        }

        var position = HitAt(point).Position;
        if (Session.LinkRange(position) is not { } link || position.Offset >= link.Range.End.Offset)
        {
            return null;
        }

        return link.Link;
    }

    /// <summary>The address of a link under the pointer as its tooltip (and how to follow it).</summary>
    private void ShowLinkTip(Point point)
    {
        var link = _dragging ? null : LinkUnder(point);
        var address = link is null ? null : string.IsNullOrEmpty(link.Uri) ? (link.Anchor is null ? null : "Zur Überschrift springen") : link.Uri;
        if (address == _tipLink)
        {
            return;
        }

        _tipLink = address;
        var key = OperatingSystem.IsMacOS() ? "⌘" : "Strg";
        ToolTip.SetTip(this, address is null ? null : $"{address}\n{key}+Klicken, um dem Link zu folgen");
    }

    /// <summary>The reference of a note at or just before a position (a double click lands beside the small number), or null.</summary>
    private DocumentPosition? NoteReferenceAt(DocumentPosition position)
    {
        if (Session.NoteAt(position) is not null)
        {
            return position;
        }

        var before = position with { Offset = position.Offset - 1 };
        return position.Offset > 0 && Session.NoteAt(before) is not null ? before : null;
    }

    /// <summary>Where a point is on a page: the header area (Footer false), the footer area (true) or the body (null).</summary>
    private (int Page, bool? Footer)? AreaAt(Point point)
    {
        var page = PageUnder(point);
        if (page < 0)
        {
            return null;
        }

        var setup = Layout.Pages[page].Section.Properties;
        var y = (point.Y - PageRect(page).Y) / _scale;
        bool? footer = y < setup.MarginTop ? false : y > setup.PageHeight - setup.MarginBottom ? true : null;
        return (page, footer);
    }

    /// <summary>True while a handle of the selected picture is dragged (tests).</summary>
    internal bool IsSizingImage => _imageDrag is not null;

    /// <summary>A handle of the selected picture being dragged: which (0 = top left, clockwise), where the drag began and is, the picture's rectangle (DIP).</summary>
    private sealed record ImageDrag(int Handle, Point Start, Point Current, Rect Original, bool Free = false);

    /// <summary>The rectangle (DIP) of the selected picture, or null.</summary>
    private Rect? SelectedImageRect()
    {
        if (Session.SelectedImage is not { } selected || Layout.GetObjectRect(selected.Position) is not { } rect)
        {
            return null;
        }

        return ToSurface(rect.Page, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static int? HandleAt(Rect rect, Point point) => SelectionHandles.At(rect, point, HandleSize);

    private static Cursor HandleCursor(int handle) => SelectionHandles.CursorOf(handle);

    /// <summary>The "handle" of moving a floating picture (the picture itself is dragged).</summary>
    private const int MoveHandle = -1;

    /// <summary>The frame a handle drag gives: corners scale both sides in proportion (Umschalt frees them), side handles one side; moving shifts it.</summary>
    private static Rect Resized(ImageDrag drag, Rect original)
    {
        if (drag.Handle == MoveHandle)
        {
            return original.Translate(new Vector(drag.Current.X - drag.Start.X, drag.Current.Y - drag.Start.Y));
        }

        var size = SelectionHandles.Resize(original, drag.Handle, drag.Current.X - drag.Start.X, drag.Current.Y - drag.Start.Y, proportional: !drag.Free);
        return new Rect(original.X, original.Bottom - size.Height, size.Width, size.Height);
    }

    /// <summary>The picture under a point of this control, or null.</summary>
    private DocumentPosition? ImageUnder(Point point)
    {
        var page = PageUnder(point);
        if (page < 0)
        {
            return null;
        }

        var rect = PageRect(page);
        return Layout.ImageAt(page, (point.X - rect.X) / _scale, (point.Y - rect.Y) / _scale);
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } files && files.Any(file => file.TryGetLocalPath() is { } path && ImageFiles.IsPicture(path))
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    /// <summary>Picture files dropped on the page go in where they were dropped (one after the other).</summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DropPicture is null || e.DataTransfer.TryGetFiles() is not { Length: > 0 } files)
        {
            return;
        }

        e.Handled = true;
        var position = HitAt(e.GetPosition(this)).Position;
        foreach (var path in files.Select(file => file.TryGetLocalPath()).OfType<string>().Where(ImageFiles.IsPicture))
        {
            await DropPicture(path, position);
            position = Session.Selection.End;
        }
    }

    /// <summary>A table column border being dragged: the table, the grid boundary (1 … columns), where the drag began (DIP).</summary>
    private sealed record ColumnDrag(int TableStart, int Boundary, double StartX);

    /// <summary>The vertical cell border within a few pixels of <paramref name="point"/> (the left edge of a table excepted), or null.</summary>
    private ColumnDrag? ColumnBorderAt(Point point)
    {
        var page = PageUnder(point);
        if (page < 0)
        {
            return null;
        }

        var rect = PageRect(page);
        var x = (point.X - rect.X) / _scale;
        var y = (point.Y - rect.Y) / _scale;
        var tolerance = 3 / _scale;
        foreach (var cell in Layout.Pages[page].Cells)
        {
            if (cell.IsRepeat || y < cell.Y || y > cell.Y + cell.Height || Math.Abs(x - (cell.X + cell.Width)) > tolerance)
            {
                continue;
            }

            if (TableSpan.At(Session.Document, cell.TableStart) is not { } span)
            {
                continue;
            }

            var table = span.Table;
            var boundary = table.GridColumnOf(cell.Row, cell.Cell) + Math.Max(1, table.CellAt(cell.Row, cell.Cell).GridSpan);
            return new ColumnDrag(cell.TableStart, boundary, point.X);
        }

        return null;
    }

    /// <summary>Applies a column drag: the column left of the border grows by the distance, the one right of it shrinks (the last border widens the table).</summary>
    private void ResizeColumn(ColumnDrag drag, Point end)
    {
        var delta = (end.X - drag.StartX) / _scale;
        if (Math.Abs(delta) < 0.5 || TableSpan.At(Session.Document, drag.TableStart) is not { } span)
        {
            return;
        }

        var grid = span.Table.Grid.ToList();
        if (grid.Count != span.Table.ColumnCount || drag.Boundary < 1)
        {
            return;
        }

        var left = drag.Boundary - 1;
        if (drag.Boundary < grid.Count)
        {
            delta = Math.Clamp(delta, TableEditsMinimum - grid[left], grid[drag.Boundary] - TableEditsMinimum);
            grid[drag.Boundary] -= delta;
        }
        else
        {
            delta = Math.Max(delta, TableEditsMinimum - grid[left]);
        }

        grid[left] += delta;
        Session.SetTableColumnWidths(drag.TableStart, grid);
    }

    private const double TableEditsMinimum = 12;

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

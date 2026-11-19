using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using PixelRect = ThixCalc.Core.Layout.PixelRect;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixCalc.Editor;

/// <summary>
/// The grid of the active sheet: drawn from <see cref="GridRenderer"/>'s display list (recorded once into an SKPicture per
/// change of content, scroll, size or zoom) and <see cref="SelectionRenderer"/>'s layer through the Skia lease, in DEVICE
/// pixels – the viewport is laid out at zoom × screen scaling and the canvas is reset to whole pixels, so grid lines stay
/// one sharp pixel at every zoom and DPI. Scrolls by pixels (smoothly, also partly visible rows); only visible rows and
/// columns are ever laid out, so a sheet of a million rows costs what a screenful costs.
/// <para>
/// Input while no cell is edited: the keys of Excel's Bereit mode (arrows, Strg+arrows to the data edge, Umschalt to extend,
/// Pos1, Strg+Pos1/Ende, Bild auf/ab, Alt+Bild, Tab/Eingabe within the selection, F2, Entf, Rücktaste, Strg/Umschalt+
/// Leertaste, Strg+A), typing starts Eingeben mode; the mouse selects cells, ranges (Umschalt), several ranges (Strg), rows
/// and columns by their headers, resizes rows and columns (double click: optimal), drags the fill handle (AutoAusfüllen),
/// scrolls while dragging outside. While a formula is edited, clicking and dragging point references into it.
/// </para>
/// </summary>
internal sealed partial class GridSurface : Control, ITextField
{
    private const double WheelRows = 3;

    private readonly CalcEditor _editor;
    private readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _marchingAnts = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private GridViewport? _viewport;
    private SKPicture? _picture;
    private bool _pictureValid;
    private double _scrollX;
    private double _scrollY;
    private double _scale = 1;
    private int _sheetId = -1;
    private Drag _drag;
    private Point _lastPointer;
    private int _antsPhase;

    public GridSurface(CalcEditor editor)
    {
        _editor = editor;
        Focusable = true;
        ClipToBounds = true;
        InputClient = new GridInputClient(this, editor);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputClient);
        _autoScroll.Tick += (_, _) => AutoScrollTick();
        _marchingAnts.Tick += (_, _) =>
        {
            _antsPhase = (_antsPhase + 1) % 8;
            InvalidateVisual();
        };
    }

    /// <summary>The scroll position changed (the scrollbars follow).</summary>
    public event EventHandler? ScrollChanged;

    public GridInputClient InputClient { get; }

    /// <summary>While a cell is edited the keys and the typing go to its field (the window's undo/copy keys yield).</summary>
    public bool IsEditingText => _editor.Editing.IsEditing;

    private EditingSession Session => _editor.Session;

    /// <summary>The screen's scaling × the zoom: device pixels per pixel of the sheet at 100 %.</summary>
    public double Scale => _scale;

    /// <summary>The scroll position in device-independent pixels (the scrollbars').</summary>
    public Point ScrollOffset => new(_scrollX / RenderScaling, _scrollY / RenderScaling);

    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>The frame time of the last full redraw (layout + display list + picture), for the performance numbers.</summary>
    public TimeSpan LastRenderTime { get; private set; }

    /// <summary>The copied (or cut) range whose frame "marches", or null.</summary>
    public CellRange? CopiedRange
    {
        get => _copied;
        set
        {
            _copied = value;
            if (value is null)
            {
                _marchingAnts.Stop();
            }
            else
            {
                _marchingAnts.Start();
            }

            InvalidateVisual();
        }
    }

    private CellRange? _copied;

    /// <summary>The viewport of the current size, scroll and zoom (device pixels).</summary>
    public GridViewport Viewport()
    {
        if (_viewport is not null)
        {
            return _viewport;
        }

        SyncSheet();
        var layout = _editor.LayoutOf(Session.ActiveSheet);
        var scaling = RenderScaling;
        _viewport = layout.Viewport(Math.Max(1, Bounds.Width * scaling), Math.Max(1, Bounds.Height * scaling), _scale, _scrollX, _scrollY);
        _scrollX = _viewport.ScrollX;
        _scrollY = _viewport.ScrollY;
        return _viewport;
    }

    /// <summary>Everything must be drawn anew (content, structure, theme).</summary>
    public void InvalidateGrid()
    {
        _viewport = null;
        _pictureValid = false;
        InvalidateVisual();
    }

    /// <summary>Cells changed: redrawn when a changed region is on screen (or the structure changed).</summary>
    public void OnCellsChanged(WorkbookChangedEventArgs e)
    {
        if (e.StructureChanged || e.SheetsChanged || _viewport is not { } viewport)
        {
            InvalidateGrid();
            return;
        }

        var sheet = Session.ActiveSheet;
        var visible = new CellRange(
            Math.Min(viewport.FrozenRows.Count > 0 ? viewport.FrozenRows[0].Index : int.MaxValue, viewport.Rows.Count > 0 ? viewport.Rows[0].Index : 0),
            0,
            viewport.Rows.Count > 0 ? viewport.Rows[^1].Index : SheetLimits.LastRow,
            SheetLimits.LastColumn);
        foreach (var region in e.Regions)
        {
            // Overflowing text and conditional formats reach beyond a cell: any change in the visible rows redraws.
            if (region.Sheet == sheet && region.Range.Intersects(visible) || sheet.ConditionalFormats.Count > 0)
            {
                InvalidateGrid();
                return;
            }
        }
    }

    /// <summary>The zoom (percent) or the screen's scaling changed: keep the first visible cell where it is.</summary>
    public void UpdateScale(double zoomPercent)
    {
        var scale = zoomPercent / 100 * RenderScaling;
        if (Math.Abs(scale - _scale) < 1e-9)
        {
            return;
        }

        var first = _viewport is { } viewport ? new CellAddress(viewport.FirstWholeRow, viewport.FirstWholeColumn) : Session.ActiveSheet.View.TopLeft;
        var layout = _editor.LayoutOf(Session.ActiveSheet);
        _scale = scale;
        _scrollX = layout.ColumnLeft(first.Column, scale);
        _scrollY = layout.RowTop(first.Row, scale);
        InvalidateGrid();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // ===== Scrolling =====

    /// <summary>Scrolls to a position in device-independent pixels (the scrollbars).</summary>
    public void ScrollTo(double x, double y)
    {
        var scaling = RenderScaling;
        SetScroll(x * scaling, y * scaling);
    }

    /// <summary>Scrolls by device-independent pixels.</summary>
    public void ScrollBy(double dx, double dy) => SetScroll(_scrollX + dx * RenderScaling, _scrollY + dy * RenderScaling);

    private void SetScroll(double x, double y)
    {
        var layout = _editor.LayoutOf(Session.ActiveSheet);
        var (minimumX, minimumY) = layout.MinimumScroll(_scale);
        x = Math.Clamp(Math.Round(x), minimumX, Math.Max(minimumX, layout.TotalWidth(_scale) - 50));
        y = Math.Clamp(Math.Round(y), minimumY, Math.Max(minimumY, layout.TotalHeight(_scale) - 50));
        if (x == _scrollX && y == _scrollY)
        {
            return;
        }

        _scrollX = x;
        _scrollY = y;
        _viewport = null;
        _pictureValid = false;
        var viewport = Viewport();
        Session.ScrollTo(new CellAddress(viewport.FirstWholeRow, viewport.FirstWholeColumn));
        InvalidateVisual();
        _editor.Editing.PositionCellBox();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls as little as possible so that <paramref name="cell"/> shows whole.</summary>
    public void ScrollIntoView(CellAddress cell)
    {
        if (Bounds.Width <= 0)
        {
            return;
        }

        var viewport = Viewport();
        var (x, y) = viewport.Layout.ScrollToShow(viewport, cell);
        SetScroll(x, y);
    }

    /// <summary>The size of the scrollable area in DIPs, like Excel's: the used range and the view, plus some room.</summary>
    public Size ScrollExtent()
    {
        var sheet = Session.ActiveSheet;
        var layout = _editor.LayoutOf(sheet);
        var used = sheet.UsedRange ?? CellRange.Cell(0, 0);
        var viewport = Viewport();
        var lastRow = Math.Max(Math.Max(used.LastRow, Session.ActiveCell.Row), viewport.Rows.Count > 0 ? viewport.Rows[^1].Index : 0);
        var lastColumn = Math.Max(Math.Max(used.LastColumn, Session.ActiveCell.Column), viewport.Columns.Count > 0 ? viewport.Columns[^1].Index : 0);
        var scaling = RenderScaling;
        var height = layout.RowTop(Math.Min(SheetLimits.MaxRows, lastRow + 20), _scale) + viewport.Height / 3;
        var width = layout.ColumnLeft(Math.Min(SheetLimits.MaxColumns, lastColumn + 5), _scale) + viewport.Width / 3;
        return new Size(width / scaling, height / scaling);
    }

    private void SyncSheet()
    {
        var sheet = Session.ActiveSheet;
        if (sheet.Id == _sheetId)
        {
            return;
        }

        _sheetId = sheet.Id;
        var layout = _editor.LayoutOf(sheet);
        var view = sheet.View;
        _scale = _editor.Zoom / 100 * RenderScaling;
        _scrollX = layout.ColumnLeft(Math.Max(view.TopLeft.Column, view.FrozenColumns), _scale);
        _scrollY = layout.RowTop(Math.Max(view.TopLeft.Row, view.FrozenRows), _scale);
        _pictureValid = false;
        Dispatcher.UIThread.Post(() => ScrollChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The scroll position comes from the sheet's view again (another sheet, a file opened, frozen panes changed).</summary>
    public void ResetSheet()
    {
        _sheetId = -1;
        InvalidateGrid();
    }

    // ===== Drawing =====

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateGrid();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var viewport = Viewport();
        if (!_pictureValid || _picture is null)
        {
            var watch = Stopwatch.StartNew();
            _picture = Record(viewport);
            _pictureValid = true;
            LastRenderTime = watch.Elapsed;
        }

        var picture = _picture;
        var selection = Session.SelectedChart is null ? SelectionRenderer.Render(viewport, Look()) : [];
        var marquee = _copied is { } copied ? viewport.RangeRect(copied) : (PixelRect?)null;
        var phase = _antsPhase;
        var antsColor = GridPalette.Token("Mat.Accent");
        var resizeLine = ResizeLine(viewport);
        var measurer = CalcFonts.Skia;
        var chartFrame = _chartDrag is null ? SelectedChartRect(viewport) : null;
        var chartOutline = _chartDrag?.Current;
        var scaling = RenderScaling;
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            canvas.DrawPicture(picture);
            SkiaPageRenderer.Draw(canvas, selection, measurer);
            DrawChartSelection(canvas, viewport, chartFrame, chartOutline, scaling);
            if (marquee is { } rect)
            {
                DrawMarchingAnts(canvas, rect, viewport, phase, antsColor);
            }

            if (resizeLine is { } line)
            {
                using var paint = new SKPaint { Color = new SKColor(antsColor.R, antsColor.G, antsColor.B), StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash([3, 3], 0) };
                canvas.DrawLine((float)line.X1, (float)line.Y1, (float)line.X2, (float)line.Y2, paint);
            }
        }, devicePixels: true));
    }

    /// <summary>The grid's display list of the viewport, recorded once (vector, replayed on every frame until something changes).</summary>
    private SKPicture Record(GridViewport viewport)
    {
        var list = GridRenderer.Render(viewport, _editor.Metrics, GridPalette.RenderOptions(Session.SelectedChart is null ? Session.SelectedRanges : []));
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create((float)viewport.Width, (float)viewport.Height));
        SkiaPageRenderer.Draw(canvas, list, CalcFonts.Skia);
        return recorder.EndRecording();
    }

    /// <summary>The dashed line where a dragged row or column border would go (Excel shows it across the grid).</summary>
    private (double X1, double Y1, double X2, double Y2)? ResizeLine(GridViewport viewport)
    {
        switch (_drag.Kind)
        {
            case DragKind.ResizeColumn:
                var x = Math.Round(viewport.ColumnX(_drag.Index) + _drag.Size) - 0.5;
                return (x, 0, x, viewport.Height);
            case DragKind.ResizeRow:
                var y = Math.Round(viewport.RowY(_drag.Index) + _drag.Size) - 0.5;
                return (0, y, viewport.Width, y);
            default:
                return null;
        }
    }

    private SelectionLook Look()
    {
        var editing = _editor.Editing;
        var look = GridPalette.SelectionLook(Session.SelectedRanges, Session.ActiveCell) with
        {
            ShowFillHandle = !editing.IsEditing,
            Highlights = editing.Highlights,
            FillPreview = _drag.Kind == DragKind.Fill ? _drag.FillTarget : null,
        };
        return look;
    }

    private static void DrawMarchingAnts(SKCanvas canvas, PixelRect rect, GridViewport viewport, int phase, RgbColor color)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(color.R, color.G, color.B),
            PathEffect = SKPathEffect.CreateDash([4, 4], phase),
            IsAntialias = false,
        };
        canvas.Save();
        canvas.ClipRect(SKRect.Create((float)viewport.HeaderWidth, (float)viewport.HeaderHeight, (float)(viewport.Width - viewport.HeaderWidth), (float)(viewport.Height - viewport.HeaderHeight)));
        canvas.DrawRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width - 1, (float)rect.Height - 1), paint);
        canvas.Restore();
    }

    // ===== Keyboard =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (_editor.Editing.IsEditing)
        {
            e.Handled = _editor.Editing.CellBox.ProcessKey(e);
            return;
        }

        e.Handled = HandleReadyKey(e.Key, e.KeyModifiers);
    }

    /// <summary>The keys of Bereit mode; true when one was used.</summary>
    internal bool HandleReadyKey(Key key, KeyModifiers modifiers)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var command = modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        if (HandleChartKey(key, modifiers))
        {
            return true;
        }

        switch (key)
        {
            case Key.Down when alt:
                _editor.ShowValidationList();
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                Session.Move(DirectionOf(key), shift, command);
                break;
            case Key.Home when command:
                var frozen = Session.ActiveSheet.View;
                MoveOrExtend(new CellAddress(frozen.FrozenRows, frozen.FrozenColumns), shift);
                break;
            case Key.End when command:
                var used = Session.ActiveSheet.UsedRange ?? CellRange.Cell(0, 0);
                MoveOrExtend(used.Last, shift);
                break;
            case Key.Home:
                MoveOrExtend(new CellAddress(Session.ActiveCell.Row, Session.ActiveSheet.View.FrozenColumns), shift);
                break;
            case Key.PageDown or Key.PageUp:
                Page(key == Key.PageDown ? 1 : -1, horizontal: alt, shift);
                break;
            case Key.Tab:
                Session.MoveWithinSelection(shift, byRows: true);
                break;
            case Key.Enter:
                Session.MoveWithinSelection(shift, byRows: false);
                break;
            case Key.F2:
                _editor.Editing.Begin(EditStart.Edit);
                break;
            case Key.Delete:
                _ = _editor.RunAsync("Inhalte löschen", Session.ClearContents);
                break;
            case Key.Back:
                _editor.Editing.Begin(EditStart.Empty);
                break;
            case Key.Space when command && !shift:
                Session.SelectColumns(Session.Selection.FirstColumn, Session.Selection.LastColumn);
                break;
            case Key.Space when shift && !command:
                Session.SelectRows(Session.Selection.FirstRow, Session.Selection.LastRow);
                break;
            case Key.Escape when _copied is not null:
                CopiedRange = null;
                break;
            default:
                return false;
        }

        ScrollIntoView(ExtensionEnd());
        return true;
    }

    /// <summary>The cell the keyboard moved to: the active cell, or – while extending – the far corner.</summary>
    private CellAddress ExtensionEnd()
    {
        var range = Session.Selection;
        var active = Session.ActiveCell;
        var row = range.FirstRow == active.Row ? range.LastRow : range.FirstRow;
        var column = range.FirstColumn == active.Column ? range.LastColumn : range.FirstColumn;
        return range.IsWholeColumns || range.IsWholeRows ? active : new CellAddress(row, column);
    }

    private void MoveOrExtend(CellAddress cell, bool extend)
    {
        if (extend)
        {
            Session.ExtendTo(cell);
        }
        else
        {
            Session.Select(cell);
        }
    }

    /// <summary>Bild auf/ab (Alt: sideways): a screenful, the active cell moves along.</summary>
    private void Page(int direction, bool horizontal, bool extend)
    {
        var viewport = Viewport();
        var active = Session.ActiveCell;
        if (horizontal)
        {
            var columns = Math.Max(1, viewport.Columns.Count - 1);
            var target = new CellAddress(active.Row, Math.Clamp(active.Column + direction * columns, 0, SheetLimits.LastColumn));
            SetScroll(_scrollX + direction * (viewport.Width - viewport.ScrollLeft), _scrollY);
            MoveOrExtend(target, extend);
            return;
        }

        var rows = Math.Max(1, viewport.Rows.Count - 1);
        var row = Math.Clamp(active.Row + direction * rows, 0, SheetLimits.LastRow);
        SetScroll(_scrollX, _scrollY + direction * (viewport.Height - viewport.ScrollTop));
        MoveOrExtend(new CellAddress(row, active.Column), extend);
    }

    private static Direction DirectionOf(Key key) => key switch
    {
        Key.Left => Direction.Left,
        Key.Right => Direction.Right,
        Key.Up => Direction.Up,
        _ => Direction.Down,
    };

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var text = new string(e.Text.Where(character => !char.IsControl(character)).ToArray());
        if (text.Length == 0 || Session.SelectedChart is not null && !_editor.Editing.IsEditing)
        {
            return;
        }

        e.Handled = true;
        if (!_editor.Editing.IsEditing)
        {
            _editor.Editing.Begin(EditStart.Typing);
        }

        _editor.Editing.CellBox.InsertText(text);
    }

    // ===== Mouse =====

    private enum DragKind : byte
    {
        None,
        Cells,
        Rows,
        Columns,
        ResizeColumn,
        ResizeRow,
        Fill,
        Point,
    }

    private record struct Drag(DragKind Kind, CellAddress Anchor = default, int Index = -1, double Start = 0, double Size = 0, CellRange Source = default, CellRange? FillTarget = null);

    private Point DevicePoint(Point point) => new(point.X * RenderScaling, point.Y * RenderScaling);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var device = DevicePoint(point.Position);
        var viewport = Viewport();
        var editing = _editor.Editing;
        var hit = viewport.HitTest(device.X, device.Y, editing.IsEditing || Session.SelectedRanges.Count > 1 ? null : Session.Selection);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var add = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        _lastPointer = point.Position;
        if (PressChart(e, point, device, viewport))
        {
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (editing.IsEditing)
            {
                return;
            }

            SelectForContextMenu(hit);
            ContextMenu = _editor.ContextMenuFor(hit.Kind);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (editing.IsEditing && hit.Kind is HitKind.Cell or HitKind.RowHeader or HitKind.ColumnHeader && editing.CanPoint)
        {
            var range = hit.Kind switch
            {
                HitKind.RowHeader => CellRange.Rows(hit.Index, hit.Index),
                HitKind.ColumnHeader => CellRange.Columns(hit.Index, hit.Index),
                _ => Session.ActiveSheet.Merges.Find(hit.Cell.Row, hit.Cell.Column) ?? CellRange.Cell(hit.Cell),
            };
            editing.PointAt(range, extend: shift);
            _drag = new Drag(DragKind.Point, hit.Kind == HitKind.Cell ? hit.Cell : range.First);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!editing.IsEditing && hit.Kind == HitKind.Cell && Session.ActiveSheet.PivotTables.Count > 0 && GridRenderer.PivotFilterFieldAt(Session.ActiveSheet, hit.Cell) is { } pivotFilter
            && GridRenderer.FilterButtonRect(viewport, hit.Cell) is { } pivotButton && pivotButton.Contains(device.X, device.Y))
        {
            Session.Select(hit.Cell);
            OpenPivotFilter(pivotFilter.Pivot, pivotFilter.Field);
            e.Handled = true;
            return;
        }

        if (!editing.IsEditing && hit.Kind == HitKind.Cell && FilterColumnAt(viewport, hit.Cell, device) is { } filterColumn)
        {
            Session.Select(hit.Cell);
            OpenFilter(filterColumn);
            e.Handled = true;
            return;
        }

        if (!editing.IsEditing && add && hit.Kind == HitKind.Cell && Session.ActiveSheet.Hyperlinks.FirstOrDefault(link => link.Range.Contains(hit.Cell)) is { } hyperlink)
        {
            _ = _editor.OpenLinkAsync(hyperlink);
            e.Handled = true;
            return;
        }

        if (editing.IsEditing && !editing.Commit())
        {
            return; // an invalid entry keeps the cell open
        }

        Focus();
        if (e.ClickCount == 2)
        {
            switch (hit.Kind)
            {
                case HitKind.Cell when Session.ActiveSheet.PivotAt(hit.Cell.Row, hit.Cell.Column) is { } pivot:
                    // A double click on a value shows its records (Details anzeigen); pivot cells are not edited in place.
                    Session.Select(hit.Cell);
                    if (Session.ActiveSheet.OutputOf(pivot)?.EntryAt(hit.Cell) is not null)
                    {
                        _ = _editor.RunAsync("Details anzeigen", () => Session.ShowPivotDetails(hit.Cell));
                    }

                    e.Handled = true;
                    return;
                case HitKind.Cell:
                    Session.Select(hit.Cell);
                    editing.Begin(EditStart.Edit, caretAt: point.Position);
                    e.Handled = true;
                    return;
                case HitKind.ColumnResize:
                    _ = _editor.RunAsync("Optimale Breite", () => Session.AutoFitColumns(hit.Index, hit.Index, _editor.Metrics));
                    e.Handled = true;
                    return;
                case HitKind.RowResize:
                    _ = _editor.RunAsync("Optimale Höhe", () => Session.SetRowHeight(hit.Index, hit.Index, Session.ActiveSheet.Rows.DefaultSize));
                    e.Handled = true;
                    return;
            }
        }

        switch (hit.Kind)
        {
            case HitKind.Cell:
                if (shift)
                {
                    Session.ExtendTo(hit.Cell);
                }
                else if (add)
                {
                    Session.AddRange(CellRange.Cell(hit.Cell));
                }
                else
                {
                    Session.Select(hit.Cell);
                }

                _drag = new Drag(DragKind.Cells, Session.ActiveCell);
                break;
            case HitKind.RowHeader:
                var rowAnchor = shift ? Session.Selection.FirstRow : hit.Index;
                SelectRows(rowAnchor, hit.Index, add);
                _drag = new Drag(DragKind.Rows, Index: rowAnchor);
                break;
            case HitKind.ColumnHeader:
                var columnAnchor = shift ? Session.Selection.FirstColumn : hit.Index;
                SelectColumns(columnAnchor, hit.Index, add);
                _drag = new Drag(DragKind.Columns, Index: columnAnchor);
                break;
            case HitKind.Corner:
                Session.Select(CellRange.Whole, Session.ActiveCell);
                _drag = default;
                break;
            case HitKind.ColumnResize:
                _drag = new Drag(DragKind.ResizeColumn, Index: hit.Index, Start: device.X, Size: viewport.Layout.ColumnWidth(hit.Index, viewport.Zoom));
                break;
            case HitKind.RowResize:
                _drag = new Drag(DragKind.ResizeRow, Index: hit.Index, Start: device.Y, Size: viewport.Layout.RowHeight(hit.Index, viewport.Zoom));
                break;
            case HitKind.FillHandle:
                _drag = new Drag(DragKind.Fill, Source: Session.Selection);
                break;
            default:
                return;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>The autofilter column whose arrow is at <paramref name="device"/>, or null.</summary>
    private int? FilterColumnAt(GridViewport viewport, CellAddress cell, Point device) =>
        GridRenderer.FilterColumnAt(Session.ActiveSheet, cell) is { } offset && GridRenderer.FilterButtonRect(viewport, cell) is { } button && button.Contains(device.X, device.Y)
            ? offset
            : null;

    /// <summary>The list behind a filter arrow (the last one opened, for tests).</summary>
    internal FilterMenu? FilterMenu { get; private set; }

    /// <summary>The list behind a PivotTable field's arrow (the last one opened, for tests).</summary>
    internal PivotFilterMenu? PivotFilterMenu { get; private set; }

    internal void OpenPivotFilter(Core.Model.Pivot.PivotTable pivot, int field)
    {
        var flyout = new Flyout { Placement = PlacementMode.Pointer };
        PivotFilterMenu = new PivotFilterMenu(_editor, pivot, field, () => flyout.Hide());
        flyout.Content = PivotFilterMenu;
        flyout.ShowAt(this, showAtPointer: true);
    }

    /// <summary>Opens the filter list of an autofilter column under the pointer.</summary>
    internal void OpenFilter(int columnOffset)
    {
        var flyout = new Flyout { Placement = PlacementMode.Pointer };
        FilterMenu = new FilterMenu(_editor, columnOffset, () => flyout.Hide());
        flyout.Content = FilterMenu;
        flyout.ShowAt(this, showAtPointer: true);
    }

    private void SelectRows(int anchor, int row, bool add)
    {
        var range = CellRange.Rows(Math.Min(anchor, row), Math.Max(anchor, row));
        if (add)
        {
            Session.AddRange(range);
        }
        else
        {
            Session.Select(range, new CellAddress(anchor, Session.ActiveSheet.View.TopLeft.Column));
        }
    }

    private void SelectColumns(int anchor, int column, bool add)
    {
        var range = CellRange.Columns(Math.Min(anchor, column), Math.Max(anchor, column));
        if (add)
        {
            Session.AddRange(range);
        }
        else
        {
            Session.Select(range, new CellAddress(Session.ActiveSheet.View.TopLeft.Row, anchor));
        }
    }

    /// <summary>A right click outside the selection selects what was clicked first (the menu acts on it), as in Excel.</summary>
    private void SelectForContextMenu(HitResult hit)
    {
        var selection = Session.SelectedRanges;
        switch (hit.Kind)
        {
            case HitKind.Cell when !selection.Any(range => range.Contains(hit.Cell)):
                Session.Select(hit.Cell);
                break;
            case HitKind.RowHeader when !selection.Any(range => range.IsWholeRows && hit.Index >= range.FirstRow && hit.Index <= range.LastRow):
                Session.SelectRows(hit.Index, hit.Index);
                break;
            case HitKind.ColumnHeader when !selection.Any(range => range.IsWholeColumns && hit.Index >= range.FirstColumn && hit.Index <= range.LastColumn):
                Session.SelectColumns(hit.Index, hit.Index);
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _lastPointer = position;
        if (DragChart(DevicePoint(position), e.KeyModifiers))
        {
            return;
        }

        if (_drag.Kind == DragKind.None)
        {
            UpdateCursor(position);
            return;
        }

        DragTo(position);
        var outside = position.X < 0 || position.Y < 0 || position.X > Bounds.Width || position.Y > Bounds.Height;
        if (outside && _drag.Kind is DragKind.Cells or DragKind.Rows or DragKind.Columns or DragKind.Fill or DragKind.Point)
        {
            _autoScroll.Start();
        }
        else
        {
            _autoScroll.Stop();
        }
    }

    private void DragTo(Point position)
    {
        var viewport = Viewport();
        var device = DevicePoint(position);
        var x = Math.Clamp(device.X, viewport.ScrollLeft, viewport.Width - 1);
        var y = Math.Clamp(device.Y, viewport.ScrollTop, viewport.Height - 1);
        var hit = viewport.HitTest(x, y);
        var cell = hit.Kind == HitKind.Cell ? hit.Cell : new CellAddress(Math.Max(0, hit.Index), Math.Max(0, hit.Index));
        switch (_drag.Kind)
        {
            case DragKind.Cells when hit.Kind == HitKind.Cell:
                Session.ExtendTo(hit.Cell);
                break;
            case DragKind.Rows:
                var row = viewport.HitTest(viewport.HeaderWidth > 0 ? 1 : 0, y) is { Kind: HitKind.RowHeader } rowHit ? rowHit.Index : hit.Cell.Row;
                SelectRows(_drag.Index, row, add: false);
                break;
            case DragKind.Columns:
                var column = viewport.HitTest(x, viewport.HeaderHeight > 0 ? 1 : 0) is { Kind: HitKind.ColumnHeader } columnHit ? columnHit.Index : hit.Cell.Column;
                SelectColumns(_drag.Index, column, add: false);
                break;
            case DragKind.ResizeColumn:
                _drag = _drag with { Size = Math.Max(0, viewport.Layout.ColumnWidth(_drag.Index, viewport.Zoom) + device.X - _drag.Start) };
                ToolTip.SetTip(this, ColumnWidthTip(_drag.Size / viewport.Zoom));
                ToolTip.SetIsOpen(this, true);
                InvalidateVisual();
                break;
            case DragKind.ResizeRow:
                _drag = _drag with { Size = Math.Max(0, viewport.Layout.RowHeight(_drag.Index, viewport.Zoom) + device.Y - _drag.Start) };
                ToolTip.SetTip(this, $"Höhe: {_drag.Size / viewport.Zoom * 0.75:0.##} ({_drag.Size / viewport.Zoom:0} Pixel)");
                ToolTip.SetIsOpen(this, true);
                InvalidateVisual();
                break;
            case DragKind.Fill when hit.Kind == HitKind.Cell:
                _drag = _drag with { FillTarget = FillTarget(_drag.Source, hit.Cell) };
                InvalidateVisual();
                break;
            case DragKind.Point when hit.Kind == HitKind.Cell:
                _editor.Editing.PointAt(new CellRange(_drag.Anchor, cell), extend: false, replaceDrag: true);
                break;
        }
    }

    private string ColumnWidthTip(double pixels) =>
        $"Breite: {_editor.Metrics.WidthInCharactersFromPixels(pixels):0.##} ({pixels:0} Pixel)";

    /// <summary>The AutoFill target: the source stretched to the pointer's row or column, whichever it left farther.</summary>
    private static CellRange? FillTarget(CellRange source, CellAddress cell)
    {
        var below = cell.Row - source.LastRow;
        var above = source.FirstRow - cell.Row;
        var right = cell.Column - source.LastColumn;
        var left = source.FirstColumn - cell.Column;
        var vertical = Math.Max(below, above);
        var horizontal = Math.Max(right, left);
        if (vertical <= 0 && horizontal <= 0)
        {
            return null;
        }

        if (vertical >= horizontal)
        {
            return below > 0
                ? new CellRange(source.FirstRow, source.FirstColumn, cell.Row, source.LastColumn)
                : new CellRange(cell.Row, source.FirstColumn, source.LastRow, source.LastColumn);
        }

        return right > 0
            ? new CellRange(source.FirstRow, source.FirstColumn, source.LastRow, cell.Column)
            : new CellRange(source.FirstRow, cell.Column, source.LastRow, source.LastColumn);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _autoScroll.Stop();
        if (ReleaseChart())
        {
            e.Pointer.Capture(null);
            return;
        }

        var drag = _drag;
        _drag = default;
        e.Pointer.Capture(null);
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
        var viewport = Viewport();
        switch (drag.Kind)
        {
            case DragKind.ResizeColumn:
                var characters = _editor.Metrics.WidthInCharactersFromPixels(drag.Size / viewport.Zoom);
                var columns = ResizeTargets(drag.Index, columnsNotRows: true);
                _ = _editor.RunAsync("Spaltenbreite", () => Session.SetColumnWidth(columns.First, columns.Last, Math.Max(0, characters)));
                break;
            case DragKind.ResizeRow:
                var points = drag.Size / viewport.Zoom * 0.75;
                var rows = ResizeTargets(drag.Index, columnsNotRows: false);
                _ = _editor.RunAsync("Zeilenhöhe", () => Session.SetRowHeight(rows.First, rows.Last, Math.Max(0, points)));
                break;
            case DragKind.Cells or DragKind.Rows or DragKind.Columns:
                _editor.OnPointerSelection();
                break;
            case DragKind.Fill when drag.FillTarget is { } target:
                var increment = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control);
                _ = _editor.RunAsync("AutoAusfüllen", () =>
                {
                    Session.AutoFill(drag.Source, target, increment);
                    Session.Select(target, Session.ActiveCell);
                });
                break;
        }

        InvalidateVisual();
    }

    /// <summary>Resizing a selected column resizes every selected one (as in Excel); otherwise just the one.</summary>
    private (int First, int Last) ResizeTargets(int index, bool columnsNotRows)
    {
        foreach (var range in Session.SelectedRanges)
        {
            if (columnsNotRows && range.IsWholeColumns && index >= range.FirstColumn && index <= range.LastColumn)
            {
                return (range.FirstColumn, range.LastColumn);
            }

            if (!columnsNotRows && range.IsWholeRows && index >= range.FirstRow && index <= range.LastRow)
            {
                return (range.FirstRow, range.LastRow);
            }
        }

        return (index, index);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _autoScroll.Stop();
        if (_chartDrag is not null)
        {
            _chartDrag = null;
            InvalidateVisual();
        }

        if (_drag.Kind is DragKind.Fill or DragKind.ResizeColumn or DragKind.ResizeRow)
        {
            _drag = default;
            InvalidateVisual();
        }
    }

    private void AutoScrollTick()
    {
        var dx = _lastPointer.X < 0 ? _lastPointer.X : _lastPointer.X > Bounds.Width ? _lastPointer.X - Bounds.Width : 0;
        var dy = _lastPointer.Y < 0 ? _lastPointer.Y : _lastPointer.Y > Bounds.Height ? _lastPointer.Y - Bounds.Height : 0;
        ScrollBy(Math.Clamp(dx, -60, 60), Math.Clamp(dy, -60, 60));
        DragTo(_lastPointer);
    }

    private void UpdateCursor(Point position)
    {
        var device = DevicePoint(position);
        var viewport = Viewport();
        var hit = viewport.HitTest(device.X, device.Y, _editor.Editing.IsEditing || Session.SelectedRanges.Count > 1 ? null : Session.Selection);
        var cursor = hit.Kind switch
        {
            HitKind.ColumnResize => StandardCursorType.SizeWestEast,
            HitKind.RowResize => StandardCursorType.SizeNorthSouth,
            HitKind.FillHandle => StandardCursorType.Cross,
            HitKind.ColumnHeader or HitKind.RowHeader or HitKind.Corner => StandardCursorType.Hand,
            _ => StandardCursorType.Arrow,
        };
        Cursor = ChartCursor(viewport, device) ?? new Cursor(cursor);
        ShowNote(hit);
    }

    private bool _showsNote;

    /// <summary>Hovering a cell with a note shows the note (Excel shows it beside the cell).</summary>
    private void ShowNote(HitResult hit)
    {
        if (hit.Kind == HitKind.Cell && Session.ActiveSheet.Comments.TryGetValue(hit.Cell, out var note))
        {
            ToolTip.SetTip(this, note.Author.Length > 0 ? note.Author + ":\n" + note.Text : note.Text);
            ToolTip.SetIsOpen(this, true);
            _showsNote = true;
            return;
        }

        if (_showsNote)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
            _showsNote = false;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled)
        {
            return;
        }

        var viewport = Viewport();
        var rowHeight = viewport.Layout.RowHeight(Math.Max(0, viewport.FirstWholeRow), viewport.Zoom) / RenderScaling;
        var delta = e.Delta;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0)
        {
            delta = new Vector(delta.Y, 0);
        }

        // A notched wheel moves three rows; a touchpad scrolls by its pixels.
        ScrollBy(-delta.X * rowHeight * WheelRows * 2, -delta.Y * rowHeight * WheelRows);
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _editor.Editing.CellBox.IsActive = _editor.Editing.IsEditing && !_editor.Editing.IsInFormulaBar;
    }

    /// <summary>The input method's view of the grid: the cell's field while it is edited; a composition starts editing.</summary>
    internal sealed class GridInputClient(GridSurface surface, CalcEditor editor) : TextInputMethodClient
    {
        public override Visual TextViewVisual => surface;

        public override bool SupportsPreedit => true;

        public override bool SupportsSurroundingText => editor.Editing.IsEditing;

        public override string SurroundingText => editor.Editing.IsEditing ? editor.Editing.CellBox.Text : string.Empty;

        public override Rect CursorRectangle
        {
            get
            {
                var box = editor.Editing.CellBox;
                if (editor.Editing.IsEditing && box.TranslatePoint(box.CaretRect().Position, surface) is { } point)
                {
                    return new Rect(point, box.CaretRect().Size);
                }

                var viewport = surface.Viewport();
                var rect = viewport.CellRect(editor.Session.ActiveCell);
                var scaling = surface.RenderScaling;
                return new Rect(rect.X / scaling, rect.Y / scaling, 1, rect.Height / scaling);
            }
        }

        public override TextSelection Selection
        {
            get => editor.Editing.IsEditing ? new TextSelection(editor.Editing.CellBox.SelectionStart, editor.Editing.CellBox.SelectionEnd) : default;
            set
            {
                if (editor.Editing.IsEditing)
                {
                    editor.Editing.CellBox.Select(value.Start, value.End);
                }
            }
        }

        public override void SetPreeditText(string? preeditText)
        {
            if (!string.IsNullOrEmpty(preeditText) && !editor.Editing.IsEditing)
            {
                editor.Editing.Begin(EditStart.Typing);
            }

            if (editor.Editing.IsEditing)
            {
                editor.Editing.CellBox.SetPreedit(preeditText);
            }
        }

        public void NotifyChanged()
        {
            RaiseCursorRectangleChanged();
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
        }
    }
}

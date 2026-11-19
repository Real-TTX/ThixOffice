using MatDesktop;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// ThixCalc's editor of one <see cref="CalcDocument"/> in a window, from top to bottom: the formula bar (<see cref="FormulaBar"/>),
/// the grid (<see cref="GridSurface"/>) with its scrollbars and the field over an edited cell, the sheet tabs
/// (<see cref="SheetTabBar"/>) beside the horizontal scrollbar. The status bar shows the mode (Bereit, Eingeben, Bearbeiten,
/// Zeigen) and the selection's Mittelwert, Anzahl and Summe. Everything that changes the workbook goes through the session;
/// the views follow its events (the ribbon once per dispatcher round, however many changes came).
/// </summary>
public sealed class CalcEditor : OfficeEditor
{
    public const string ModeItem = "mode";
    public const string AverageItem = "average";
    public const string CountItem = "count";
    public const string SumItem = "sum";

    private readonly OfficeWindow _window;
    private readonly Grid _view;
    private readonly Canvas _overlay = new();
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, AllowAutoHide = false };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, AllowAutoHide = false };
    private readonly Dictionary<Worksheet, SheetLayout> _layouts = [];
    private readonly FormulaAssist _assist;
    private bool _statePending;
    private bool _syncingBars;
    private int _shownSheet = -1;

    public CalcEditor(OfficeWindow window, CalcDocument document)
    {
        _window = window;
        Document = document;
        Metrics = new GridMetrics(CalcFonts.Grid, document.Workbook, document.Locale);
        Surface = new GridSurface(this);
        Avalonia.Automation.AutomationProperties.SetName(Surface, "Tabelle");

        var cellBox = new FormulaTextBox { GrowsWithText = true, Padding = new Thickness(2, 0, 2, 0) };
        cellBox.FieldBackground = new Avalonia.Media.SolidColorBrush(GridPalette.SheetColor);
        Avalonia.Automation.AutomationProperties.SetName(cellBox, "Zelle bearbeiten");
        var barBox = new FormulaTextBox();
        Editing = new CellEditing(this, cellBox, barBox);
        Editing.Changed += (_, _) => OnEditingChanged();
        FormulaBar = new FormulaBar(this, barBox);
        Tabs = new SheetTabBar(this);
        _assist = new FormulaAssist(this) { IsVisible = false };
        _overlay.Children.Add(cellBox);
        _overlay.Children.Add(_assist);

        var gridArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*") };
        var host = new Panel { Children = { Surface, _overlay }, ClipToBounds = true };
        Grid.SetColumn(_vertical, 1);
        gridArea.Children.Add(host);
        gridArea.Children.Add(_vertical);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*,Auto") };
        bottom.Use(Panel.BackgroundProperty, "Mat.Bg");
        Grid.SetColumn(_horizontal, 1);
        _horizontal.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        bottom.Children.Add(Tabs);
        bottom.Children.Add(_horizontal);

        FindBar = new CalcFindBar(this) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(0, 10, 26, 0), IsVisible = false };
        Grid.SetRow(FindBar, 1);
        Grid.SetRow(gridArea, 1);
        Grid.SetRow(bottom, 2);
        _view = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { FormulaBar, gridArea, bottom, FindBar } };
        _view.Use(Panel.BackgroundProperty, "Mat.Surface");

        Surface.ScrollChanged += (_, _) => SyncScrollBars();
        _vertical.Scroll += (_, _) => OnScrollBar();
        _horizontal.Scroll += (_, _) => OnScrollBar();
        _view.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        _view.ActualThemeVariantChanged += (_, _) => Surface.InvalidateGrid();

        Attach();
        document.ContentReplaced += OnContentReplaced;
        Zoom = Session.ActiveSheet.View.Zoom;
    }

    public CalcDocument Document { get; }

    public EditingSession Session => Document.Session;

    public GridMetrics Metrics { get; private set; }

    internal GridSurface Surface { get; }

    internal CellEditing Editing { get; }

    internal FormulaBar FormulaBar { get; }

    internal SheetTabBar Tabs { get; }

    internal CalcFindBar FindBar { get; }

    internal OfficeWindow Window => _window;

    public override Control View => _view;

    /// <summary>The colour Schriftfarbe's button applies (the last one chosen; null = Automatisch).</summary>
    public string? FontColor { get; set; } = GridPalette.DefaultFontColor;

    /// <summary>The colour Füllfarbe's button applies (the last one chosen; null = Keine Füllung).</summary>
    public string? FillColor { get; set; } = GridPalette.DefaultFillColor;

    /// <summary>Which aggregates the status bar shows (its context menu; Excel's default: Mittelwert, Anzahl, Summe).</summary>
    public HashSet<string> StatusAggregates { get; } = [AverageItem, CountItem, SumItem];

    public override string? OpeningWarning
    {
        get
        {
            var workbook = Document.Workbook;
            var notes = workbook.UnsupportedContent.Select(note => "• " + note).ToList();
            var macros = workbook.HasMacros ? "Diese Arbeitsmappe enthält Makros. Makros werden nicht ausgeführt, bleiben aber erhalten." : null;
            if (notes.Count == 0)
            {
                return macros;
            }

            var text = $"Diese Arbeitsmappe enthält Inhalte, die ThixCalc noch nicht anzeigt oder bearbeitet. Sie bleiben beim Speichern als Excel-Arbeitsmappe erhalten:{Environment.NewLine}{string.Join(Environment.NewLine, notes)}";
            return macros is null ? text : macros + Environment.NewLine + text;
        }
    }

    /// <summary>The layout of a sheet (cached per sheet; the column widths per zoom).</summary>
    public SheetLayout LayoutOf(Worksheet sheet)
    {
        if (!_layouts.TryGetValue(sheet, out var layout))
        {
            layout = new SheetLayout(sheet, Metrics);
            _layouts[sheet] = layout;
        }

        return layout;
    }

    public override void FocusDocument()
    {
        if (Editing.IsEditing && Editing.IsInFormulaBar)
        {
            FormulaBar.Field.Focus();
            return;
        }

        Surface.Focus();
    }

    /// <summary>Scrolls the active cell (or <paramref name="cell"/>) into view.</summary>
    public void ScrollIntoView(CellAddress? cell = null) => Surface.ScrollIntoView(cell ?? Session.ActiveCell);

    public override void RequestZoomLevel() => _ = Dialogs.ZoomDialog.ShowAsync(_window, this);

    protected override void OnZoomChanged(double zoom)
    {
        Surface.UpdateScale(zoom);
        if (Session.ActiveSheet.View.Zoom != (int)zoom)
        {
            Session.SetZoom((int)zoom);
        }

        Editing.PositionCellBox();
    }

    /// <summary>"Auswahl": the zoom that fits the selection into the window.</summary>
    public void ZoomToSelection()
    {
        var layout = LayoutOf(Session.ActiveSheet);
        var range = Session.Selection;
        var width = layout.ColumnLeft(range.LastColumn + 1, 1) - layout.ColumnLeft(range.FirstColumn, 1);
        var height = layout.RowTop(range.LastRow + 1, 1) - layout.RowTop(range.FirstRow, 1);
        var viewport = Surface.Viewport();
        var scaling = Surface.Scale / (Zoom / 100);
        var available = (viewport.Width / scaling - viewport.HeaderWidth / scaling - 20, viewport.Height / scaling - viewport.HeaderHeight / scaling - 20);
        Zoom = Math.Floor(Math.Min(available.Item1 / Math.Max(1, width), available.Item2 / Math.Max(1, height)) * 100);
        Session.ScrollTo(range.First);
        Surface.ResetSheet();
    }

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        statusBar.SetItem(ModeItem, Editing.ModeText);
        var statistics = Session.Statistics;
        var many = Session.SelectedRanges.Count > 1 || !Session.Selection.IsSingleCell;
        var format = Session.ActiveStyle.NumberFormat;
        string Number(double value) => NumberFormatter.Format(CellValue.FromNumber(value), NumberFormat.Parse(format).IsText ? "General" : format, Session.Locale, Session.Workbook.Date1904).Text;
        void Item(string id, string? text)
        {
            if (text is null || !StatusAggregates.Contains(id))
            {
                statusBar.RemoveItem(id);
            }
            else
            {
                statusBar.SetItem(id, text, StatusBarSide.Right);
            }
        }

        var showsNumbers = many && statistics.NumericCount > 0;
        Item(AverageItem, showsNumbers && statistics.Average is { } average ? $"Mittelwert: {Number(average)}" : null);
        Item(CountItem, many && statistics.Count > 1 ? $"Anzahl: {statistics.Count.ToString(CultureInfo.CurrentCulture)}" : null);
        Item(SumItem, showsNumbers ? $"Summe: {Number(statistics.Sum)}" : null);
    }

    public override void Detach()
    {
        Session.CellsChanged -= OnCellsChanged;
        Session.SelectionChanged -= OnSelectionChanged;
        Session.StateChanged -= OnStateChanged;
        Document.ContentReplaced -= OnContentReplaced;
    }

    // ===== Running =====

    /// <summary>Runs a session command; a refusal of the core (BusinessException) is shown as a message. False when refused.</summary>
    public Task<bool> RunAsync(string label, Action action) => OfficeWindow.RunAsync(_window, label, () =>
    {
        action();
        return Task.FromResult(true);
    });

    /// <summary>Namenfeld / Gehe zu: a cell, a range, "Tabelle2!B4", a defined name; a new valid name is defined for the selection.</summary>
    public void GoTo(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var workbook = Session.Workbook;
        var sheetName = (string?)null;
        var target = text;
        var bang = text.LastIndexOf('!');
        if (bang > 0)
        {
            sheetName = text[..bang].Trim('\'');
            target = text[(bang + 1)..];
        }

        if (CellRange.TryParse(target) is { } range)
        {
            if (sheetName is not null)
            {
                var sheet = workbook.FindSheet(sheetName) ?? throw new BusinessException($"Das Blatt „{sheetName}“ gibt es nicht.");
                Session.ActivateSheet(sheet.Index);
            }

            Session.Select(range);
            ScrollIntoView(range.First);
            return;
        }

        var name = workbook.Names.FirstOrDefault(candidate => string.Equals(candidate.Name, text, StringComparison.OrdinalIgnoreCase) && !candidate.IsBuiltIn);
        if (name is not null)
        {
            var refersTo = Session.NameFormulaText(name).TrimStart('=');
            GoTo(refersTo.Replace("$", "", StringComparison.Ordinal));
            return;
        }

        // Excel: a new name in the Namenfeld defines it for the selection.
        var selection = Session.Selection;
        var reference = "=" + Core.Formulas.FormulaTokens.QuoteSheet(Session.ActiveSheet.Name) + "!" + Absolute(selection);
        Session.DefineName(text, reference);
    }

    private static string Absolute(CellRange range) => range.IsSingleCell
        ? $"${A1.ColumnName(range.FirstColumn)}${range.FirstRow + 1}"
        : $"${A1.ColumnName(range.FirstColumn)}${range.FirstRow + 1}:${A1.ColumnName(range.LastColumn)}${range.LastRow + 1}";

    /// <summary>Löschen of a sheet: asks first when it has content (it cannot be undone in Excel; here it can, but the question stays).</summary>
    public async Task DeleteSheetAsync(int index)
    {
        var sheet = Session.Workbook.Sheets[index];
        if (sheet.CellCount > 0 && !await QuestionDialog.ConfirmAsync(_window, "Blatt löschen", $"Das Blatt „{sheet.Name}“ enthält Daten. Möchten Sie es löschen?", "Löschen", isDestructive: true))
        {
            return;
        }

        await RunAsync("Blatt löschen", () => Session.DeleteSheet(index));
    }

    /// <summary>Verschieben oder kopieren: before which sheet, a copy or not.</summary>
    public async Task MoveOrCopySheetAsync(int index)
    {
        if (await Dialogs.MoveSheetDialog.ShowAsync(_window, this, index) is not { } choice)
        {
            return;
        }

        await RunAsync(choice.Copy ? "Blatt kopieren" : "Blatt verschieben", () =>
        {
            if (choice.Copy)
            {
                Session.CopySheet(index, choice.Before);
            }
            else
            {
                Session.MoveSheet(index, choice.Before > index ? choice.Before - 1 : choice.Before);
            }
        });
    }

    /// <summary>fx / Formeln › Funktion einfügen: the dialog; the chosen function goes into the cell being edited (or starts editing).</summary>
    public async Task InsertFunctionAsync()
    {
        var chosen = await Dialogs.InsertFunctionDialog.ShowAsync(_window, Session.Locale);
        if (chosen is null)
        {
            return;
        }

        if (!Editing.IsEditing)
        {
            Editing.Begin(EditStart.Typing);
        }

        var box = Editing.IsInFormulaBar ? Editing.BarBox : Editing.CellBox;
        if (!box.Text.StartsWith('='))
        {
            box.SetText("=", 1);
        }

        box.InsertText(chosen.Name + "(");
        FocusDocument();
    }

    /// <summary>A function of the library menus: "=NAME(" into the cell (or at the caret of the edit).</summary>
    public void InsertFunctionByName(string name)
    {
        if (!Editing.IsEditing)
        {
            Editing.Begin(EditStart.Typing);
        }

        var box = Editing.IsInFormulaBar ? Editing.BarBox : Editing.CellBox;
        if (!box.Text.StartsWith('='))
        {
            box.InsertText("=");
        }

        box.InsertText(name + "(");
        FocusDocument();
    }

    /// <summary>The context menu of the grid for what was right-clicked (cells, row headers, column headers).</summary>
    internal ContextMenu ContextMenuFor(HitKind kind) => CalcContextMenus.For(_window, kind);

    /// <summary>Kopieren, Ausschneiden, Einfügen.</summary>
    internal CalcClipboard Clipboard => _clipboard ??= new CalcClipboard(this);

    private CalcClipboard? _clipboard;
    private CellStyle? _painted;

    /// <summary>True while Format übertragen waits for the selection that gets the format.</summary>
    public bool IsPainting => _painted is not null;

    /// <summary>Format übertragen: takes the active cell's format; the next selection made with the mouse gets it.</summary>
    public void ToggleFormatPainter()
    {
        _painted = _painted is null ? Session.ActiveStyle : null;
        ScheduleState();
    }

    /// <summary>The mouse finished a selection: Format übertragen applies its format there.</summary>
    internal void OnPointerSelection()
    {
        if (_painted is not { } style)
        {
            return;
        }

        _painted = null;
        _ = RunAsync("Format übertragen", () => Session.ApplyStyle(_ => style, "Format übertragen"));
    }

    /// <summary>AutoSumme: on one cell the proposed formula to confirm (Excel's way), on a range the totals below it.</summary>
    public void AutoSum(string function)
    {
        if (!Editing.Commit())
        {
            return;
        }

        if (Session.Selection.IsSingleCell && Session.SelectedRanges.Count <= 1)
        {
            var formula = Session.AutoSumFormula(function);
            Editing.Begin(EditStart.Typing);
            Editing.CellBox.InsertText(formula);
            var open = formula.IndexOf('(') + 1;
            Editing.CellBox.Select(open, formula.Length - 1);
            return;
        }

        _ = RunAsync("AutoSumme", () => Session.AutoSum(function));
    }

    /// <summary>Suchen / Ersetzen: the bar at the top right.</summary>
    public void OpenFind(bool replace)
    {
        if (Editing.Commit())
        {
            FindBar.Open(replace);
        }
    }

    /// <summary>Gehe zu (F5): the keyboard into the Namenfeld.</summary>
    public void FocusNameBox()
    {
        if (!Editing.Commit())
        {
            return;
        }

        FormulaBar.IsVisible = true;
        FormulaBar.NameBox.Focus();
        FormulaBar.NameBox.SelectAll();
    }

    /// <summary>Changes the active sheet's view options (Gitternetzlinien, Überschriften, Formeln anzeigen).</summary>
    public void SetView(Func<SheetView, SheetView> change) => Session.ChangeView(change);

    /// <summary>Ansicht › Bearbeitungsleiste.</summary>
    public void ToggleFormulaBar()
    {
        FormulaBar.IsVisible = !FormulaBar.IsVisible;
        ScheduleState();
    }

    // ===== Events =====

    private void Attach()
    {
        Session.CellsChanged += OnCellsChanged;
        Session.SelectionChanged += OnSelectionChanged;
        Session.StateChanged += OnStateChanged;
        _shownSheet = Session.ActiveSheet.Id;
        Tabs.Refresh();
        FormulaBar.Refresh();
        Editing.BarBox.SetText(Session.FormulaBarText, 0);
    }

    private void OnContentReplaced(object? sender, EventArgs e)
    {
        Metrics = new GridMetrics(CalcFonts.Grid, Document.Workbook, Document.Locale);
        _layouts.Clear();
        Attach();
        Surface.ResetSheet();
        Zoom = Session.ActiveSheet.View.Zoom;
        ScheduleState();
    }

    private void OnCellsChanged(object? sender, WorkbookChangedEventArgs e)
    {
        if (e.StructureChanged)
        {
            foreach (var layout in _layouts.Keys.Where(sheet => sheet.Workbook != Document.Workbook || sheet.Index < 0).ToList())
            {
                _layouts.Remove(layout);
            }
        }

        if (e.SheetsChanged)
        {
            Tabs.Refresh();
        }

        CheckSheet();
        Surface.OnCellsChanged(e);
        ScheduleState();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        CheckSheet();
        Surface.InvalidateGrid();
        if (!Editing.IsEditing)
        {
            Editing.BarBox.SetText(Session.FormulaBarText, 0);
        }

        ScheduleState();
    }

    /// <summary>Another sheet became active (tab, undo, Strg+Bild): its scroll position, zoom, tabs.</summary>
    private void CheckSheet()
    {
        var sheet = Session.ActiveSheet;
        if (sheet.Id == _shownSheet)
        {
            return;
        }

        _shownSheet = sheet.Id;
        Surface.ResetSheet();
        Tabs.Refresh();
        Zoom = sheet.View.Zoom;
        Editing.PositionCellBox();
    }

    private void OnStateChanged(object? sender, EventArgs e) => ScheduleState();

    private void OnEditingChanged()
    {
        Surface.InvalidateVisual();
        _assist.Update();
        ScheduleState();
    }

    /// <summary>The ribbon, the formula bar and the status bar follow the session, once per dispatcher round.</summary>
    internal void ScheduleState()
    {
        if (_statePending)
        {
            return;
        }

        _statePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _statePending = false;
            FormulaBar.Refresh();
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
            SyncScrollBars();
        }, DispatcherPriority.Background);
    }

    // ===== Scrollbars and wheel =====

    private void SyncScrollBars()
    {
        if (Surface.Bounds.Width <= 0)
        {
            return;
        }

        _syncingBars = true;
        var extent = Surface.ScrollExtent();
        var offset = Surface.ScrollOffset;
        var viewport = Surface.Bounds.Size;
        _vertical.Minimum = 0;
        _vertical.Maximum = Math.Max(0, extent.Height - viewport.Height);
        _vertical.ViewportSize = viewport.Height;
        _vertical.SmallChange = 20;
        _vertical.LargeChange = viewport.Height * 0.9;
        _vertical.Value = Math.Min(offset.Y, _vertical.Maximum);
        _horizontal.Minimum = 0;
        _horizontal.Maximum = Math.Max(0, extent.Width - viewport.Width);
        _horizontal.ViewportSize = viewport.Width;
        _horizontal.SmallChange = 40;
        _horizontal.LargeChange = viewport.Width * 0.9;
        _horizontal.Value = Math.Min(offset.X, _horizontal.Maximum);
        _syncingBars = false;
    }

    private void OnScrollBar()
    {
        if (_syncingBars)
        {
            return;
        }

        Surface.ScrollTo(_horizontal.Value, _vertical.Value);
    }

    /// <summary>Strg + mouse wheel zooms in steps of 10 % (⌘ on the Mac).</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(modifier) || e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        Zoom = e.Delta.Y > 0 ? Math.Floor(Zoom / 10 + 1) * 10 : Math.Ceiling(Zoom / 10 - 1) * 10;
    }
}

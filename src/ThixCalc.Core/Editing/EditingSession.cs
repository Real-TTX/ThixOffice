using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>The aggregates the status bar shows for the selection (Summe, Mittelwert, Anzahl, Numerische Anzahl, Min, Max).</summary>
public sealed record SelectionStatistics(double Sum, double? Average, int Count, int NumericCount, double? Minimum, double? Maximum);

/// <summary>
/// The editing engine of one workbook window, UI-free: the active sheet and its selection, every command as an undoable
/// transaction (typed input through the locale's parser, clearing, styles, rows/columns/cells, clipboard, fill and series,
/// sort, filter, find/replace, merges, freeze panes, sheets, names), recalculation after each command, and the cheap queries
/// of the grid, formula bar, name box and status bar.
/// <para>
/// Events: <see cref="CellsChanged"/> with the changed regions once per command/undo/redo/recalculation (the grid redraws
/// them), <see cref="SelectionChanged"/>, and <see cref="StateChanged"/> whenever what the ribbon shows may have changed.
/// Commands that the user cannot do throw <see cref="BusinessException"/> with a German message.
/// </para>
/// </summary>
public sealed partial class EditingSession
{
    private readonly UndoHistory _history = new();
    private EditTransaction? _open;
    private Dictionary<(Worksheet, int, int), (int Row, int Column, Cell Before, Cell After)>? _cellChanges;
    private CellAddress _extensionEnd;

    public EditingSession(Workbook workbook)
    {
        Workbook = workbook;
        if (workbook.Sheets.Count == 0)
        {
            workbook.AddSheet();
        }

        workbook.ActiveSheetIndex = Math.Clamp(workbook.ActiveSheetIndex, 0, workbook.Sheets.Count - 1);
        workbook.Changed += (_, args) => CellsChanged?.Invoke(this, args);
        _extensionEnd = ActiveCell;
    }

    /// <summary>Cells, rows/columns or sheets changed (once per command, undo, redo).</summary>
    public event EventHandler<WorkbookChangedEventArgs>? CellsChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>Anything the ribbon, the formula bar or the status bar shows may have changed.</summary>
    public event EventHandler? StateChanged;

    public Workbook Workbook { get; }

    /// <summary>The user's locale: function names, separators, number entry and display (German by default).</summary>
    public CalcLocale Locale
    {
        get => Workbook.Calculation.Locale;
        set
        {
            Workbook.Calculation.Locale = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A1 or R1C1 (Z1S1) references in the formula bar.</summary>
    public ReferenceStyle ReferenceStyle { get; set; }

    public FormulaSyntax Syntax => new(Locale, ReferenceStyle);

    public Worksheet ActiveSheet => Workbook.ActiveSheet ?? Workbook.Sheets[0];

    public CellAddress ActiveCell => ActiveSheet.View.ActiveCell;

    public IReadOnlyList<CellRange> SelectedRanges => ActiveSheet.View.Selection;

    /// <summary>The range the active cell is in (the last of a multiple selection).</summary>
    public CellRange Selection => SelectedRanges.Count > 0 ? SelectedRanges[^1] : CellRange.Cell(ActiveCell);

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    // ---------------------------------------------------------------- sheets and selection

    public void ActivateSheet(int index)
    {
        if (index < 0 || index >= Workbook.Sheets.Count || index == Workbook.ActiveSheetIndex)
        {
            return;
        }

        Workbook.ActiveSheetIndex = index;
        _selectedChart = null;
        _extensionEnd = ActiveCell;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects one cell (a merged area as a whole).</summary>
    public void Select(CellAddress cell)
    {
        var range = ActiveSheet.Merges.Find(cell.Row, cell.Column) ?? CellRange.Cell(cell);
        var active = range.First;
        SetSelection(active, [range]);
        _extensionEnd = cell;
    }

    /// <summary>Selects a range with the active cell in it (its first cell by default).</summary>
    public void Select(CellRange range, CellAddress? active = null)
    {
        range = ActiveSheet.Merges.Expand(range);
        var cell = active is { } given && range.Contains(given) ? given : range.First;
        SetSelection(cell, [range]);
        _extensionEnd = range.Last == cell ? range.First : range.Last;
    }

    /// <summary>Umschalt+Klick / Umschalt+Pfeil: from the active cell to <paramref name="cell"/>.</summary>
    public void ExtendTo(CellAddress cell)
    {
        var range = ActiveSheet.Merges.Expand(new CellRange(ActiveCell, cell));
        var ranges = SelectedRanges.Take(SelectedRanges.Count - 1).Append(range).ToList();
        SetSelection(ActiveCell, ranges);
        _extensionEnd = cell;
    }

    /// <summary>Strg+Klick: adds a range to the selection; it becomes the active one.</summary>
    public void AddRange(CellRange range)
    {
        range = ActiveSheet.Merges.Expand(range);
        SetSelection(range.First, [.. SelectedRanges, range]);
        _extensionEnd = range.Last;
    }

    public void SelectRows(int first, int last) => Select(CellRange.Rows(Math.Min(first, last), Math.Max(first, last)), new CellAddress(first, ActiveSheet.View.TopLeft.Column));

    public void SelectColumns(int first, int last) => Select(CellRange.Columns(Math.Min(first, last), Math.Max(first, last)), new CellAddress(ActiveSheet.View.TopLeft.Row, first));

    /// <summary>Strg+A: the current region first, the whole sheet when that is already selected (or empty).</summary>
    public void SelectAll()
    {
        var region = Navigation.CurrentRegion(ActiveSheet, ActiveCell);
        if (region.IsSingleCell || Selection == region)
        {
            Select(CellRange.Whole, ActiveCell);
            return;
        }

        Select(region, ActiveCell);
    }

    /// <summary>The arrow keys: one cell or to the data edge (Strg), extending the selection with Umschalt.</summary>
    public void Move(Direction direction, bool extend = false, bool toEdge = false)
    {
        var sheet = ActiveSheet;
        var from = extend ? _extensionEnd : ActiveCell;
        var target = toEdge ? Navigation.Edge(sheet, from, direction) : Navigation.Step(sheet, from, direction);
        if (extend)
        {
            ExtendTo(target);
            return;
        }

        Select(target);
    }

    /// <summary>Eingabe/Tab: the next cell, inside a multi-cell selection without leaving it (wrapping).</summary>
    public void MoveWithinSelection(bool backwards, bool byRows)
    {
        var range = Selection;
        if (range.IsSingleCell || SelectedRanges.Count > 1)
        {
            Move(byRows ? (backwards ? Direction.Left : Direction.Right) : (backwards ? Direction.Up : Direction.Down));
            return;
        }

        var cell = ActiveCell;
        var step = backwards ? -1 : 1;
        if (byRows)
        {
            var column = cell.Column + step;
            var row = cell.Row;
            if (column > range.LastColumn)
            {
                (column, row) = (range.FirstColumn, row + 1 > range.LastRow ? range.FirstRow : row + 1);
            }
            else if (column < range.FirstColumn)
            {
                (column, row) = (range.LastColumn, row - 1 < range.FirstRow ? range.LastRow : row - 1);
            }

            SetSelection(new CellAddress(row, column), SelectedRanges);
        }
        else
        {
            var row = cell.Row + step;
            var column = cell.Column;
            if (row > range.LastRow)
            {
                (row, column) = (range.FirstRow, column + 1 > range.LastColumn ? range.FirstColumn : column + 1);
            }
            else if (row < range.FirstRow)
            {
                (row, column) = (range.LastRow, column - 1 < range.FirstColumn ? range.LastColumn : column - 1);
            }

            SetSelection(new CellAddress(row, column), SelectedRanges);
        }
    }

    private void SetSelection(CellAddress active, IReadOnlyList<CellRange> ranges)
    {
        var sheet = ActiveSheet;
        _selectedChart = null;
        sheet.View = sheet.View with { ActiveCell = active, Selection = ranges };
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
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
            transaction.Steps[index].Undo(Workbook);
        }

        Finish(transaction.IsStructural, transaction.Before);
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
            step.Redo(Workbook);
        }

        Finish(transaction.IsStructural, transaction.After);
    }

    private void Finish(bool structural, SelectionState selection)
    {
        if (structural)
        {
            Workbook.Calculation.Rebuild();
            Workbook.NoteStructureChanged();
            Workbook.NoteSheetsChanged();
        }

        Workbook.ActiveSheetIndex = Math.Clamp(selection.SheetIndex, 0, Workbook.Sheets.Count - 1);
        ActiveSheet.View = selection.View;
        _extensionEnd = ActiveCell;
        Recalculate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recalculates (unless calculation is manual) and tells the views what changed.</summary>
    public void Recalculate(bool force = false)
    {
        if (force || !Workbook.CalculationSettings.Manual)
        {
            Workbook.Calculation.Recalculate();
        }

        Workbook.PublishChanges();
    }

    /// <summary>Runs <paramref name="action"/> as one undo step labelled <paramref name="label"/>.</summary>
    internal void Execute(string label, Action action)
    {
        if (_open is not null)
        {
            action();
            return;
        }

        _open = new EditTransaction(label, CaptureSelection());
        _cellChanges = [];
        try
        {
            action();
        }
        catch
        {
            Rollback();
            throw;
        }

        FlushCells();
        var transaction = _open;
        _open = null;
        _cellChanges = null;
        transaction.After = CaptureSelection();
        if (transaction.Steps.Count > 0)
        {
            _history.Push(transaction);
        }

        if (transaction.IsStructural)
        {
            Workbook.Calculation.Rebuild();
            Workbook.NoteStructureChanged();
        }

        Recalculate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rollback()
    {
        FlushCells();
        var transaction = _open!;
        _open = null;
        _cellChanges = null;
        for (var index = transaction.Steps.Count - 1; index >= 0; index--)
        {
            transaction.Steps[index].Undo(Workbook);
        }

        if (transaction.IsStructural)
        {
            Workbook.Calculation.Rebuild();
        }

        Workbook.PublishChanges();
    }

    private SelectionState CaptureSelection() => new(Workbook.ActiveSheetIndex, ActiveSheet.View);

    internal void Record(EditStep step)
    {
        FlushCells();
        _open?.Steps.Add(step);
    }

    /// <summary>Changes a cell inside the open command (recorded for undo).</summary>
    internal void SetCell(Worksheet sheet, int row, int column, Cell after)
    {
        var key = (sheet, row, column);
        var before = _cellChanges!.TryGetValue(key, out var existing) ? existing.Before : sheet.GetCell(row, column);
        if (before.IsSpilled)
        {
            before = Cell.Empty with { StyleId = before.StyleId };
        }

        _cellChanges[key] = (row, column, before, after);
        sheet.SetCell(row, column, after);
    }

    private void FlushCells()
    {
        if (_cellChanges is null || _cellChanges.Count == 0 || _open is null)
        {
            return;
        }

        foreach (var group in _cellChanges.GroupBy(entry => entry.Key.Item1))
        {
            _open.Steps.Add(new CellStep(group.Key, group.Select(entry => entry.Value).ToList()));
        }

        _cellChanges.Clear();
    }

    // ---------------------------------------------------------------- input

    /// <summary>
    /// What the user typed into the active cell (or, with <paramref name="allSelected"/> = Strg+Eingabe, every selected
    /// cell): a number, date, time, percent, text, boolean, error or formula in the session's locale. Excel's number format
    /// for the input (percent, currency, date …) is applied to cells formatted "Standard".
    /// </summary>
    public void EnterInput(string text, bool allSelected = false)
    {
        var sheet = ActiveSheet;
        var targets = allSelected ? SelectedCells(limit: 1_000_000).ToList() : [ActiveCell];
        Execute("Eingabe", () =>
        {
            foreach (var cell in targets)
            {
                EnterInto(sheet, cell, text);
            }
        });
    }

    /// <summary>
    /// Strg+Umschalt+Eingabe: <paramref name="text"/> (a formula in the locale) as a legacy array formula over the selection
    /// (the active range); a formula that was such an array before keeps its range.
    /// </summary>
    public void EnterArrayFormula(string text)
    {
        var sheet = ActiveSheet;
        var range = Selection;
        if (sheet.FindArrayAnchor(ActiveCell.Row, ActiveCell.Column) is { } anchor && sheet.GetCell(anchor).ArrayRange is { } existing)
        {
            range = existing;
        }

        if (range.CellCount > 1_000_000)
        {
            throw new BusinessException("Die Auswahl ist für eine Matrixformel zu groß.");
        }

        var body = text.TrimStart().TrimStart('{').TrimEnd().TrimEnd('}');
        if (!body.StartsWith('=') && !body.StartsWith('+'))
        {
            EnterInput(text);
            return;
        }

        Formula formula;
        try
        {
            formula = FormulaParser.Parse(body, Workbook, sheet, range.First, Syntax);
        }
        catch (FormulaParseException exception)
        {
            throw new BusinessException(exception.Message, "formula");
        }

        Execute("Matrixformel", () =>
        {
            GuardArrays(sheet, range);
            foreach (var entry in sheet.GetCells(range).ToList())
            {
                SetCell(sheet, entry.Row, entry.Column, Cell.Empty with { StyleId = entry.Cell.StyleId });
            }

            SetCell(sheet, range.FirstRow, range.FirstColumn, new Cell(CellValue.Blank, formula, sheet.GetStyleId(range.FirstRow, range.FirstColumn), range));
        });
    }

    /// <summary>Puts input into one cell (inside the open command).</summary>
    internal void EnterInto(Worksheet sheet, CellAddress cell, string text)
    {
        GuardArrays(sheet, CellRange.Cell(cell));
        var parsed = ValueParser.ParseInput(text, Locale, Workbook.Date1904, () => Workbook.Calculation.Clock());
        var style = sheet.GetStyleId(cell.Row, cell.Column);
        var current = sheet.GetStyle(cell.Row, cell.Column);
        switch (parsed.Kind)
        {
            case InputKind.Empty:
                SetCell(sheet, cell.Row, cell.Column, Cell.Empty with { StyleId = style });
                return;
            case InputKind.Formula:
                Formula formula;
                try
                {
                    formula = FormulaParser.Parse(parsed.FormulaText!, Workbook, sheet, cell, Syntax);
                }
                catch (FormulaParseException exception)
                {
                    throw new BusinessException(exception.Message, "formula");
                }

                SetCell(sheet, cell.Row, cell.Column, new Cell(CellValue.Blank, formula, style, IsDynamicArray: true));
                return;
        }

        var value = parsed.Value;
        Validation.Check(sheet, cell, value, Locale);
        if (parsed.NumberFormat is { } format && NumberFormat.Parse(current.NumberFormat).IsGeneral)
        {
            style = Workbook.Styles.Intern(current with { NumberFormat = format });
        }

        if (text.StartsWith('\'') != current.QuotePrefix)
        {
            style = Workbook.Styles.Intern(Workbook.Styles[style] with { QuotePrefix = text.StartsWith('\'') });
        }

        SetCell(sheet, cell.Row, cell.Column, new Cell(value, null, style));
    }

    /// <summary>Puts a constant into a cell (API for the app and tests; one undo step).</summary>
    public void SetValue(CellAddress cell, CellValue value) =>
        Execute("Eingabe", () => SetCell(ActiveSheet, cell.Row, cell.Column, new Cell(value, null, ActiveSheet.GetStyleId(cell.Row, cell.Column))));

    /// <summary>Ein Teil eines Arrays kann nicht geändert werden.</summary>
    private static void GuardArrays(Worksheet sheet, CellRange range)
    {
        foreach (var anchor in sheet.Workbook.Calculation.AnchorsIn(sheet, range))
        {
            if (anchor.ArrayRange is { } array && !range.Contains(array))
            {
                throw new BusinessException("Teile eines Arrays können nicht geändert werden.");
            }
        }
    }

    /// <summary>The cells of the selection (merged areas only by their first cell).</summary>
    internal IEnumerable<CellAddress> SelectedCells(long limit)
    {
        long count = 0;
        foreach (var range in SelectedRanges)
        {
            if (range.CellCount > limit)
            {
                throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
            }

            for (var row = range.FirstRow; row <= range.LastRow; row++)
            {
                for (var column = range.FirstColumn; column <= range.LastColumn; column++)
                {
                    if (ActiveSheet.Merges.Find(row, column) is { } merge && (merge.FirstRow != row || merge.FirstColumn != column))
                    {
                        continue;
                    }

                    if (++count > limit)
                    {
                        throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
                    }

                    yield return new CellAddress(row, column);
                }
            }
        }
    }

    // ---------------------------------------------------------------- clearing

    public void ClearContents() => Clear("Inhalte löschen", contents: true, formats: false);

    public void ClearFormats() => Clear("Formate löschen", contents: false, formats: true);

    /// <summary>Alles löschen: contents, formats, notes and links.</summary>
    public void ClearAll() => Clear("Alles löschen", contents: true, formats: true, extras: true);

    private void Clear(string label, bool contents, bool formats, bool extras = false)
    {
        var sheet = ActiveSheet;
        Execute(label, () =>
        {
            foreach (var range in SelectedRanges)
            {
                GuardArrays(sheet, range);
                foreach (var entry in sheet.GetCells(range).ToList())
                {
                    if (entry.Cell.IsSpilled && !formats)
                    {
                        continue;
                    }

                    var after = contents ? Cell.Empty with { StyleId = entry.Cell.StyleId } : entry.Cell with { IsSpilled = false };
                    if (formats)
                    {
                        after = after with { StyleId = 0 };
                    }

                    if (after != entry.Cell)
                    {
                        SetCell(sheet, entry.Row, entry.Column, after);
                    }
                }

                if (extras)
                {
                    ChangeSheetState(sheet, () =>
                    {
                        foreach (var address in sheet.Comments.Keys.Where(range.Contains).ToList())
                        {
                            sheet.Comments.Remove(address);
                        }

                        sheet.Hyperlinks.RemoveAll(link => range.Contains(link.Range));
                    });
                }
            }
        });
    }

    /// <summary>Runs a change of a sheet's lists (merges, notes, links …) as an undoable step.</summary>
    internal void ChangeSheetState(Worksheet sheet, Action change)
    {
        var before = new WorkbookSnapshot.SheetState(sheet);
        change();
        var after = new WorkbookSnapshot.SheetState(sheet);
        Record(new ActionStep(before.Restore, after.Restore));
        Workbook.NoteStructureChanged();
    }

    // ---------------------------------------------------------------- styles

    /// <summary>
    /// Changes the style of the selection: every cell of normal ranges; whole rows/columns get the style as their row/column
    /// style and every stored cell in them changes too (as Excel does).
    /// </summary>
    public void ApplyStyle(Func<CellStyle, CellStyle> change, string label = "Format")
    {
        var sheet = ActiveSheet;
        Execute(label, () =>
        {
            foreach (var range in SelectedRanges)
            {
                if (range.IsWholeColumns || range.IsWholeRows)
                {
                    StyleAxes(sheet, range, change);
                    foreach (var entry in sheet.GetCells(range).ToList())
                    {
                        SetCell(sheet, entry.Row, entry.Column, entry.Cell with { StyleId = Workbook.Styles.Intern(change(Workbook.Styles[entry.Cell.StyleId])), IsSpilled = false });
                    }

                    continue;
                }

                if (range.CellCount > 5_000_000)
                {
                    throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
                }

                for (var row = range.FirstRow; row <= range.LastRow; row++)
                {
                    for (var column = range.FirstColumn; column <= range.LastColumn; column++)
                    {
                        var cell = sheet.GetCell(row, column);
                        var style = Workbook.Styles.Intern(change(sheet.GetStyle(row, column)));
                        if (style != cell.StyleId || cell.StyleId == 0 && style != 0)
                        {
                            SetCell(sheet, row, column, cell.IsSpilled ? Cell.Empty with { StyleId = style } : cell with { StyleId = style });
                        }
                    }
                }
            }
        });
    }

    private void StyleAxes(Worksheet sheet, CellRange range, Func<CellStyle, CellStyle> change)
    {
        var rows = range.IsWholeRows && !range.IsWholeColumns;
        var axis = rows ? sheet.Rows : sheet.Columns;
        var first = rows ? range.FirstRow : range.FirstColumn;
        var last = rows ? range.LastRow : range.LastColumn;
        if (last - first > 20_000)
        {
            return;
        }

        var before = axis.Snapshot();
        var defaultSize = axis.DefaultSize;
        axis.Update(first, last, info => info with { StyleId = Workbook.Styles.Intern(change(Workbook.Styles[info.StyleId ?? 0])) });
        var after = axis.Snapshot();
        Record(new ActionStep(() => axis.Restore(before, defaultSize), () => axis.Restore(after, defaultSize)));
        Workbook.NoteStructureChanged();
    }

    public void SetNumberFormat(string canonicalCode) => ApplyStyle(style => style with { NumberFormat = canonicalCode }, "Zahlenformat");

    /// <summary>The number format typed in the locale ("#.##0,00 €", "TT.MM.JJJJ").</summary>
    public void SetLocalNumberFormat(string code) => SetNumberFormat(FormatCodes.ToCanonical(code, Locale));

    public void SetBold(bool bold) => ApplyStyle(style => style with { Font = style.Font with { Bold = bold } }, "Fett");

    public void SetItalic(bool italic) => ApplyStyle(style => style with { Font = style.Font with { Italic = italic } }, "Kursiv");

    public void SetUnderline(UnderlineKind underline) => ApplyStyle(style => style with { Font = style.Font with { Underline = underline } }, "Unterstrichen");

    public void SetFont(string name, double size) => ApplyStyle(style => style with { Font = style.Font with { Name = name, Size = size } }, "Schriftart");

    public void SetFontColor(CalcColor? color) => ApplyStyle(style => style with { Font = style.Font with { Color = color } }, "Schriftfarbe");

    public void SetFill(CalcColor? color) => ApplyStyle(style => style with { Fill = color is { } fill ? CellFill.Solid(fill) : CellFill.None }, "Füllfarbe");

    public void SetHorizontalAlignment(HorizontalAlignment alignment) => ApplyStyle(style => style with { Alignment = style.Alignment with { Horizontal = alignment } }, "Ausrichtung");

    public void SetVerticalAlignment(VerticalAlignment alignment) => ApplyStyle(style => style with { Alignment = style.Alignment with { Vertical = alignment } }, "Ausrichtung");

    public void SetWrapText(bool wrap) => ApplyStyle(style => style with { Alignment = style.Alignment with { WrapText = wrap } }, "Textumbruch");

    /// <summary>Rahmen: all edges of every selected cell, or only the outline of each range.</summary>
    public void SetBorders(BorderStyle border, CalcColor? color = null, bool outlineOnly = false)
    {
        var sheet = ActiveSheet;
        var edge = new BorderEdge(border, color);
        Execute("Rahmen", () =>
        {
            foreach (var range in SelectedRanges)
            {
                if (range.CellCount > 1_000_000)
                {
                    throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
                }

                for (var row = range.FirstRow; row <= range.LastRow; row++)
                {
                    for (var column = range.FirstColumn; column <= range.LastColumn; column++)
                    {
                        var style = sheet.GetStyle(row, column);
                        var borders = style.Borders;
                        if (!outlineOnly || column == range.FirstColumn)
                        {
                            borders = borders with { Left = edge };
                        }

                        if (!outlineOnly || column == range.LastColumn)
                        {
                            borders = borders with { Right = edge };
                        }

                        if (!outlineOnly || row == range.FirstRow)
                        {
                            borders = borders with { Top = edge };
                        }

                        if (!outlineOnly || row == range.LastRow)
                        {
                            borders = borders with { Bottom = edge };
                        }

                        var cell = sheet.GetCell(row, column);
                        SetCell(sheet, row, column, (cell.IsSpilled ? Cell.Empty : cell) with { StyleId = Workbook.Styles.Intern(style with { Borders = borders }) });
                    }
                }
            }
        });
    }

    /// <summary>The style of the active cell (the ribbon's state).</summary>
    public CellStyle ActiveStyle => ActiveSheet.GetStyle(ActiveCell.Row, ActiveCell.Column);

    // ---------------------------------------------------------------- merges and panes

    /// <summary>Verbinden (und zentrieren): keeps the first cell's content (the others are cleared, as Excel warns).</summary>
    public void Merge(bool center = false, bool across = false)
    {
        var sheet = ActiveSheet;
        Execute(center ? "Verbinden und zentrieren" : "Zellen verbinden", () =>
        {
            foreach (var range in SelectedRanges)
            {
                if (range.IsSingleCell)
                {
                    continue;
                }

                GuardArrays(sheet, range);
                var parts = across ? Enumerable.Range(range.FirstRow, range.RowCount).Select(row => new CellRange(row, range.FirstColumn, row, range.LastColumn)).ToList() : [range];
                foreach (var part in parts)
                {
                    foreach (var entry in sheet.GetCells(part).ToList())
                    {
                        if (entry.Address != part.First && !entry.Cell.Value.IsBlank)
                        {
                            SetCell(sheet, entry.Row, entry.Column, Cell.Empty with { StyleId = entry.Cell.StyleId });
                        }
                    }

                    ChangeSheetState(sheet, () => sheet.Merges.Add(part));
                }

                if (center)
                {
                    var first = range.First;
                    var style = sheet.GetStyle(first.Row, first.Column);
                    SetCell(sheet, first.Row, first.Column, sheet.GetCell(first.Row, first.Column) with
                    {
                        StyleId = Workbook.Styles.Intern(style with { Alignment = style.Alignment with { Horizontal = HorizontalAlignment.Center } }),
                    });
                }
            }
        });
    }

    public void Unmerge()
    {
        var sheet = ActiveSheet;
        Execute("Zellverbund aufheben", () =>
        {
            foreach (var range in SelectedRanges)
            {
                ChangeSheetState(sheet, () => sheet.Merges.Remove(range));
            }
        });
    }

    /// <summary>Fenster fixieren: rows above and columns left of <paramref name="at"/> stay (A1 unfreezes).</summary>
    public void FreezePanes(CellAddress at)
    {
        var sheet = ActiveSheet;
        sheet.View = sheet.View with { FrozenRows = at.Row, FrozenColumns = at.Column, TopLeft = new CellAddress(Math.Max(at.Row, sheet.View.TopLeft.Row), Math.Max(at.Column, sheet.View.TopLeft.Column)) };
        Workbook.NoteStructureChanged();
        Workbook.PublishChanges();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Unfreeze() => FreezePanes(default);

    /// <summary>Formeln › Berechnungsoptionen: automatic (recalculates at once) or manual (F9).</summary>
    public void SetCalculation(bool manual)
    {
        Workbook.CalculationSettings = Workbook.CalculationSettings with { Manual = manual };
        Recalculate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ansicht options of the active sheet (Gitternetzlinien, Überschriften, Formeln anzeigen …): not an undo step.</summary>
    public void ChangeView(Func<SheetView, SheetView> change)
    {
        var sheet = ActiveSheet;
        sheet.View = change(sheet.View);
        Workbook.NoteStructureChanged();
        Workbook.PublishChanges();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The scroll position of the scrolling pane (the grid sets it; not an undo step).</summary>
    public void ScrollTo(CellAddress topLeft)
    {
        ActiveSheet.View = ActiveSheet.View with { TopLeft = topLeft };
    }

    public void SetZoom(int percent)
    {
        ActiveSheet.View = ActiveSheet.View with { Zoom = Math.Clamp(percent, 10, 400) };
        Workbook.NoteStructureChanged();
        Workbook.PublishChanges();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- queries

    /// <summary>The formula bar: "=SUMME(A1:A3)" for a formula (in the locale), the value as typed for a constant.</summary>
    public string FormulaBarText
    {
        get
        {
            var sheet = ActiveSheet;
            var cell = ActiveCell;
            if (sheet.FindArrayAnchor(cell.Row, cell.Column) is { } anchor && anchor != cell && sheet.GetCell(anchor).ArrayRange is not null)
            {
                cell = anchor;
            }

            var content = sheet.GetCell(cell);
            if (content.Formula is { } formula)
            {
                var text = "=" + FormulaFormatter.Format(formula, Workbook, cell, Syntax);
                return content.ArrayRange is not null ? "{" + text + "}" : text;
            }

            if (content.IsSpilled && sheet.FindArrayAnchor(cell.Row, cell.Column) is { } spillAnchor && sheet.GetFormula(spillAnchor.Row, spillAnchor.Column) is { } spilled)
            {
                return "=" + FormulaFormatter.Format(spilled, Workbook, spillAnchor, Syntax);
            }

            return EditText(content.Value, sheet.GetStyle(cell.Row, cell.Column));
        }
    }

    /// <summary>A constant as the formula bar (and edit mode) shows it: dates in the short date format, percent with %.</summary>
    public string EditText(CellValue value, CellStyle style)
    {
        switch (value.Kind)
        {
            case CellValueKind.Text:
                return style.QuotePrefix ? "'" + value.Text : value.Text;
            case CellValueKind.Number:
                var format = NumberFormat.Parse(BuiltInFormats.Localize(style.NumberFormat, Locale));
                if (format.IsDateTime)
                {
                    var hasTime = value.Number != Math.Floor(value.Number);
                    var hasDate = value.Number >= 1;
                    var code = hasDate && hasTime ? Locale.ShortDateFormat + " hh:mm:ss" : hasDate ? Locale.ShortDateFormat : "hh:mm:ss";
                    return NumberFormatter.Format(value, code, Locale, Workbook.Date1904).Text;
                }

                if (format.IsPercent)
                {
                    return NumberText.ToText(value.Number * 100, Locale) + "%";
                }

                return NumberText.ToText(value.Number, Locale);
            default:
                return NumberFormatter.Format(value, "General", Locale).Text;
        }
    }

    /// <summary>The name box: a defined name that covers exactly the selection, else the active cell (or R×C while extending).</summary>
    public string NameBoxText
    {
        get
        {
            var sheet = ActiveSheet;
            var selection = Selection;
            foreach (var name in Workbook.Names)
            {
                if (name.IsBuiltIn || name.Hidden || (name.SheetId is { } id && id != sheet.Id))
                {
                    continue;
                }

                if (Workbook.Calculation.ParsedName(name) is ReferenceNode { Sheet: { } reference } node && reference.FirstSheetId == sheet.Id && node.Area.Resolve(default) == selection)
                {
                    return name.Name;
                }
            }

            return ActiveCell.ToString();
        }
    }

    /// <summary>Summe, Mittelwert, Anzahl … of the selection (numbers only for sum/average/min/max, as Excel's status bar).</summary>
    public SelectionStatistics Statistics
    {
        get
        {
            var sheet = ActiveSheet;
            double sum = 0;
            var count = 0;
            var numbers = 0;
            double? minimum = null;
            double? maximum = null;
            var seen = new HashSet<CellAddress>();
            foreach (var range in SelectedRanges)
            {
                foreach (var entry in sheet.GetCells(range))
                {
                    var value = entry.Cell.Value;
                    if (value.IsBlank && entry.Cell.Formula is null || !seen.Add(entry.Address) || sheet.Rows.IsHidden(entry.Row))
                    {
                        continue;
                    }

                    count++;
                    if (!value.IsNumber)
                    {
                        continue;
                    }

                    numbers++;
                    sum += value.Number;
                    minimum = Math.Min(minimum ?? value.Number, value.Number);
                    maximum = Math.Max(maximum ?? value.Number, value.Number);
                }
            }

            return new SelectionStatistics(sum, numbers > 0 ? sum / numbers : null, count, numbers, minimum, maximum);
        }
    }
}

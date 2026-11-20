using System.Text.RegularExpressions;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>How inserted or deleted cells make room.</summary>
public enum CellShift : byte
{
    Down,
    Right,
    Up,
    Left,
}

public sealed partial class EditingSession
{
    private static readonly Regex NamePattern = new(@"^[\p{L}_\\][\p{L}\p{N}_.\\?]*$", RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- rows and columns

    /// <summary>Zeilen einfügen: <paramref name="count"/> rows before <paramref name="at"/>; references follow, ranges spanning the point grow.</summary>
    public void InsertRows(int at, int count) => Shift("Zeilen einfügen", ActiveSheet, rows: true, at, count, null);

    public void DeleteRows(int at, int count) => Shift("Zeilen löschen", ActiveSheet, rows: true, at, -count, null);

    public void InsertColumns(int at, int count) => Shift("Spalten einfügen", ActiveSheet, rows: false, at, count, null);

    public void DeleteColumns(int at, int count) => Shift("Spalten löschen", ActiveSheet, rows: false, at, -count, null);

    /// <summary>Zellen einfügen with Zellen nach unten / nach rechts verschieben.</summary>
    public void InsertCells(CellRange range, CellShift shift)
    {
        if (shift == CellShift.Down)
        {
            Shift("Zellen einfügen", ActiveSheet, rows: true, range.FirstRow, range.RowCount, (range.FirstColumn, range.LastColumn));
        }
        else
        {
            Shift("Zellen einfügen", ActiveSheet, rows: false, range.FirstColumn, range.ColumnCount, (range.FirstRow, range.LastRow));
        }
    }

    /// <summary>Zellen löschen with Zellen nach oben / nach links verschieben.</summary>
    public void DeleteCells(CellRange range, CellShift shift)
    {
        if (shift == CellShift.Up)
        {
            Shift("Zellen löschen", ActiveSheet, rows: true, range.FirstRow, -range.RowCount, (range.FirstColumn, range.LastColumn));
        }
        else
        {
            Shift("Zellen löschen", ActiveSheet, rows: false, range.FirstColumn, -range.ColumnCount, (range.FirstRow, range.LastRow));
        }
    }

    /// <summary>
    /// Inserts (count &gt; 0) or deletes rows/columns, optionally only in a band (cells shifted). Undo moves the cells back and
    /// restores every formula, name and sheet list from a snapshot, so reference rewriting is undone exactly.
    /// </summary>
    private void Shift(string label, Worksheet sheet, bool rows, int at, int count, (int First, int Last)? band)
    {
        GuardAxes(sheet, rows, structural: true, deleting: count < 0);
        var limit = rows ? SheetLimits.MaxRows : SheetLimits.MaxColumns;
        var bandRange = band is { } b
            ? (rows ? new CellRange(at, b.First, SheetLimits.LastRow, b.Last) : new CellRange(b.First, at, b.Last, SheetLimits.LastColumn))
            : (rows ? CellRange.Rows(at, SheetLimits.LastRow) : CellRange.Columns(at, SheetLimits.LastColumn));
        if (count > 0 && sheet.UsedRange is { } used && (rows ? used.LastRow : used.LastColumn) + count >= limit && sheet.GetCells(bandRange).Any())
        {
            var lastUsed = rows ? used.LastRow : used.LastColumn;
            if (lastUsed + count >= limit)
            {
                throw new BusinessException("Es können keine Zellen eingefügt werden, weil sonst Zellen mit Inhalt aus dem Tabellenblatt geschoben würden.");
            }
        }

        if (band is not null)
        {
            foreach (var merge in sheet.Merges.Ranges)
            {
                var crosses = rows
                    ? merge.FirstColumn < bandRange.FirstColumn && merge.LastColumn >= bandRange.FirstColumn || merge.FirstColumn <= bandRange.LastColumn && merge.LastColumn > bandRange.LastColumn
                    : merge.FirstRow < bandRange.FirstRow && merge.LastRow >= bandRange.FirstRow || merge.FirstRow <= bandRange.LastRow && merge.LastRow > bandRange.LastRow;
                if (crosses && merge.Intersects(bandRange))
                {
                    throw new BusinessException("Diese Operation ist bei verbundenen Zellen nicht möglich.");
                }
            }
        }

        var removedRange = count < 0
            ? (rows ? new CellRange(at, band?.First ?? 0, at - count - 1, band?.Last ?? SheetLimits.LastColumn) : new CellRange(band?.First ?? 0, at, band?.Last ?? SheetLimits.LastRow, at - count - 1))
            : (CellRange?)null;

        Execute(label, () =>
        {
            var snapshot = new WorkbookSnapshot(Workbook);
            List<(int Row, int Column, Cell Cell)> removed = [];
            void Forward()
            {
                removed = removedRange is { } region ? CaptureCells(sheet, region) : [];
                ReferenceRewriter.ApplyToWorkbook(Workbook, new AxisShift(sheet.Id, rows, at, count, band?.First ?? 0, band?.Last ?? int.MaxValue));
                MoveStore(sheet, rows, at, count, band);
                if (band is null)
                {
                    var axis = rows ? sheet.Rows : sheet.Columns;
                    if (count > 0)
                    {
                        axis.Insert(at, count);
                    }
                    else
                    {
                        axis.Delete(at, -count);
                    }
                }

                Workbook.Calculation.Rebuild();
            }

            void Backward()
            {
                MoveStore(sheet, rows, at, -count, band);
                RestoreCells(sheet, removed);
                snapshot.Restore(Workbook);
                Workbook.Calculation.Rebuild();
            }

            Forward();
            Record(new ActionStep(Backward, Forward, structural: true));
            Workbook.NoteStructureChanged();
        });
    }

    /// <summary>The cells of a range as snapshots (value or formula, style).</summary>
    internal static List<(int Row, int Column, Cell Cell)> CaptureCells(Worksheet sheet, CellRange range) =>
        sheet.GetCells(range).Where(entry => !entry.Cell.IsEmpty).Select(entry => (entry.Row, entry.Column, entry.Cell)).ToList();

    /// <summary>Puts snapshots back (spilled values come back from their formulas when recalculated).</summary>
    internal static void RestoreCells(Worksheet sheet, List<(int Row, int Column, Cell Cell)> cells)
    {
        foreach (var (row, column, cell) in cells)
        {
            sheet.SetCell(row, column, cell.IsSpilled ? Cell.Empty with { StyleId = cell.StyleId } : cell);
        }
    }

    /// <summary>Moves the stored cells (no reference rewriting): whole rows/columns, or a band of them.</summary>
    private static void MoveStore(Worksheet sheet, bool rows, int at, int count, (int First, int Last)? band)
    {
        var store = sheet.Store;
        if (band is null)
        {
            if (rows)
            {
                if (count > 0)
                {
                    store.InsertRows(at, count);
                }
                else
                {
                    store.DeleteRows(at, -count);
                }
            }
            else if (count > 0)
            {
                store.InsertColumns(at, count);
            }
            else
            {
                store.DeleteColumns(at, -count);
            }

            return;
        }

        var (first, last) = band.Value;
        var source = rows ? new CellRange(at, first, SheetLimits.LastRow, last) : new CellRange(first, at, last, SheetLimits.LastColumn);
        var cells = store.Enumerate(source).ToList();
        foreach (var (row, column, _) in cells)
        {
            store.Remove(row, column);
        }

        foreach (var (row, column, record) in cells)
        {
            var targetRow = rows ? row + count : row;
            var targetColumn = rows ? column : column + count;
            var deleted = count < 0 && (rows ? row < at - count : column < at - count);
            if (deleted || targetRow < 0 || targetColumn < 0 || targetRow > SheetLimits.LastRow || targetColumn > SheetLimits.LastColumn)
            {
                continue;
            }

            store.Set(targetRow, targetColumn, record);
        }
    }

    public void SetRowHeight(int first, int last, double points) =>
        ChangeAxis("Zeilenhöhe", ActiveSheet.Rows, first, last, info => info with { Size = Math.Clamp(points, 0, 409), CustomSize = true, Hidden = points == 0 });

    public void SetColumnWidth(int first, int last, double characters) =>
        ChangeAxis("Spaltenbreite", ActiveSheet.Columns, first, last, info => info with { Size = Math.Clamp(characters, 0, 255), CustomSize = true, Hidden = characters == 0 });

    public void HideRows(int first, int last, bool hidden = true) => ChangeAxis(hidden ? "Zeilen ausblenden" : "Zeilen einblenden", ActiveSheet.Rows, first, last, info => info with { Hidden = hidden });

    public void HideColumns(int first, int last, bool hidden = true) => ChangeAxis(hidden ? "Spalten ausblenden" : "Spalten einblenden", ActiveSheet.Columns, first, last, info => info with { Hidden = hidden });

    /// <summary>Gruppieren (Gliederung): raises or lowers the outline level (0–7).</summary>
    public void Group(bool rows, int first, int last, bool group = true) =>
        ChangeAxis(group ? "Gruppieren" : "Gruppierung aufheben", rows ? ActiveSheet.Rows : ActiveSheet.Columns, first, last,
            info => info with { OutlineLevel = Math.Clamp(info.OutlineLevel + (group ? 1 : -1), 0, 7) });

    private void ChangeAxis(string label, AxisCollection axis, int first, int last, Func<AxisInfo, AxisInfo> change)
    {
        GuardAxes(ActiveSheet, ReferenceEquals(axis, ActiveSheet.Rows), structural: false);
        if (last - first > 100_000)
        {
            throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
        }

        Execute(label, () =>
        {
            var before = axis.Snapshot();
            var size = axis.DefaultSize;
            axis.Update(Math.Min(first, last), Math.Max(first, last), change);
            var after = axis.Snapshot();
            Record(new ActionStep(() => axis.Restore(before, size), () => axis.Restore(after, size)));
            Workbook.NoteStructureChanged();
        });
    }

    /// <summary>Optimale Breite: the widest displayed text of each column (measured by the grid's measurer).</summary>
    public void AutoFitColumns(int first, int last, Layout.GridMetrics metrics)
    {
        var sheet = ActiveSheet;
        var widths = new Dictionary<int, double>();
        for (var column = first; column <= last; column++)
        {
            var widest = 0.0;
            foreach (var entry in sheet.GetCells(CellRange.Columns(column, column)))
            {
                if (sheet.Merges.Find(entry.Row, entry.Column) is { IsSingleCell: false } || entry.Cell.Value.IsBlank)
                {
                    continue;
                }

                widest = Math.Max(widest, metrics.TextWidth(sheet, entry.Row, entry.Column));
            }

            if (widest > 0)
            {
                widths[column] = metrics.WidthInCharacters(widest);
            }
        }

        Execute("Optimale Breite", () =>
        {
            var axis = sheet.Columns;
            var before = axis.Snapshot();
            foreach (var (column, width) in widths)
            {
                axis.Set(column, axis[column] with { Size = width, CustomSize = true, BestFit = true });
            }

            var after = axis.Snapshot();
            var size = axis.DefaultSize;
            Record(new ActionStep(() => axis.Restore(before, size), () => axis.Restore(after, size)));
            Workbook.NoteStructureChanged();
        });
    }

    // ---------------------------------------------------------------- sheets

    public Worksheet AddSheet(string? name = null, int? index = null)
    {
        GuardStructure();
        Worksheet? added = null;
        var position = index ?? Workbook.ActiveSheetIndex + 1;
        Execute("Blatt einfügen", () =>
        {
            added = Workbook.AddSheet(name ?? Workbook.NextSheetName(Locale.SheetBaseName), position);
            var sheet = added;
            Record(new ActionStep(() => RemoveSheetRaw(sheet), () => Workbook.InsertExistingSheet(sheet, position), structural: true));
            Workbook.ActiveSheetIndex = Workbook.IndexOf(sheet);
        });
        return added!;
    }

    public void DeleteSheet(int index)
    {
        GuardStructure();
        if (Workbook.Sheets.Count(sheet => sheet.Visibility == SheetVisibility.Visible) <= 1 && Workbook.Sheets[index].Visibility == SheetVisibility.Visible)
        {
            throw new BusinessException("Eine Arbeitsmappe muss mindestens ein sichtbares Blatt enthalten.");
        }

        var sheet = Workbook.Sheets[index];
        Execute("Blatt löschen", () =>
        {
            var snapshot = new WorkbookSnapshot(Workbook);
            var active = Workbook.ActiveSheetIndex;
            Workbook.RemoveSheet(sheet);
            Record(new ActionStep(
                () =>
                {
                    Workbook.InsertExistingSheet(sheet, index);
                    snapshot.Restore(Workbook);
                    Workbook.ActiveSheetIndex = active;
                },
                () => Workbook.RemoveSheet(sheet),
                structural: true));
        });
    }

    public void RenameSheet(int index, string name)
    {
        GuardStructure();
        var sheet = Workbook.Sheets[index];
        var old = sheet.Name;
        if (old == name)
        {
            return;
        }

        Execute("Blatt umbenennen", () =>
        {
            Workbook.RenameSheet(sheet, name.Trim());
            Record(new ActionStep(() => Workbook.RenameSheet(sheet, old), () => Workbook.RenameSheet(sheet, name.Trim()), structural: true));
        });
    }

    public void MoveSheet(int from, int to)
    {
        GuardStructure();
        var sheet = Workbook.Sheets[from];
        Execute("Blatt verschieben", () =>
        {
            var active = ActiveSheet;
            Workbook.MoveSheet(sheet, to);
            Workbook.ActiveSheetIndex = Workbook.IndexOf(active);
            Record(new ActionStep(() => Workbook.MoveSheet(sheet, from), () => Workbook.MoveSheet(sheet, to), structural: true));
        });
    }

    public void SetSheetVisibility(int index, SheetVisibility visibility)
    {
        GuardStructure();
        var sheet = Workbook.Sheets[index];
        var old = sheet.Visibility;
        if (visibility != SheetVisibility.Visible && Workbook.Sheets.Count(other => other.Visibility == SheetVisibility.Visible && other != sheet) == 0)
        {
            throw new BusinessException("Eine Arbeitsmappe muss mindestens ein sichtbares Blatt enthalten.");
        }

        Execute(visibility == SheetVisibility.Visible ? "Blatt einblenden" : "Blatt ausblenden", () =>
        {
            sheet.Visibility = visibility;
            Workbook.NoteSheetsChanged();
            Record(new ActionStep(() => sheet.Visibility = old, () => sheet.Visibility = visibility));
        });
    }

    public void SetTabColor(int index, CalcColor? color)
    {
        var sheet = Workbook.Sheets[index];
        var old = sheet.TabColor;
        Execute("Registerfarbe", () =>
        {
            sheet.TabColor = color;
            Workbook.NoteSheetsChanged();
            Record(new ActionStep(() => sheet.TabColor = old, () => sheet.TabColor = color));
        });
    }

    /// <summary>Blatt kopieren: a copy with cells, formats, sizes and the sheet's features, named "Name (2)".</summary>
    public Worksheet CopySheet(int index, int? target = null)
    {
        GuardStructure();
        var source = Workbook.Sheets[index];
        var name = source.Name;
        for (var number = 2; ; number++)
        {
            var candidate = $"{source.Name} ({number})";
            if (candidate.Length > 31)
            {
                candidate = $"{source.Name[..(31 - $" ({number})".Length)]} ({number})";
            }

            if (Workbook.FindSheet(candidate) is null)
            {
                name = candidate;
                break;
            }
        }

        var copy = AddSheet(name, target ?? index + 1);
        Execute("Blatt kopieren", () =>
        {
            foreach (var entry in source.GetCells())
            {
                if (!entry.Cell.IsSpilled)
                {
                    copy.SetCell(entry.Row, entry.Column, entry.Cell);
                }
            }

            copy.Rows.Restore(source.Rows.Snapshot(), source.Rows.DefaultSize);
            copy.Columns.Restore(source.Columns.Snapshot(), source.Columns.DefaultSize);
            copy.Merges.Replace(source.Merges.Ranges);
            copy.ConditionalFormats.AddRange(source.ConditionalFormats);
            copy.Validations.AddRange(source.Validations);
            copy.Hyperlinks.AddRange(source.Hyperlinks);
            foreach (var (address, comment) in source.Comments)
            {
                copy.Comments[address] = comment;
            }

            copy.View = source.View with { TabSelected = false };
            copy.TabColor = source.TabColor;

            // Charts come along and show the copy's cells (Excel's way).
            ChangeSheetState(copy, () => copy.Charts.AddRange(source.Charts.Select(chart => new SheetChart(Workbook.NextChartId(), chart.Anchor,
                ThixOffice.Charts.ChartEdits.MapReferences(chart.Chart, formula => ChartReferences.Rename(formula, source.Name, copy.Name))) { Name = chart.Name })));
            Record(new ActionStep(() => { }, () => { }, structural: true));
        });
        return copy;
    }

    private void RemoveSheetRaw(Worksheet sheet)
    {
        ReferenceRewriter.ApplyToWorkbook(Workbook, new SheetDeletion(sheet.Id, Workbook));
        Workbook.RemoveSheetWithoutChecks(sheet);
    }

    // ---------------------------------------------------------------- names

    /// <summary>Namen definieren: <paramref name="refersTo"/> as typed in the locale ("=Tabelle1!$A$1:$A$10").</summary>
    public void DefineName(string name, string refersTo, Worksheet? scope = null, string? comment = null)
    {
        name = name.Trim();
        ValidateName(name, scope);
        string canonical;
        try
        {
            var node = FormulaParser.ParseNode(refersTo, Workbook, default, Syntax);
            canonical = FormulaFormatter.Format(node, Workbook, default);
        }
        catch (FormulaParseException exception)
        {
            throw new BusinessException(exception.Message, "refersTo");
        }

        Execute("Namen definieren", () =>
        {
            var before = Workbook.Names.ToList();
            Workbook.Names.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) && existing.SheetId == scope?.Id);
            Workbook.Names.Add(new DefinedName(name, canonical, scope?.Id, Comment: comment));
            var after = Workbook.Names.ToList();
            Record(new ActionStep(() => ReplaceNames(before), () => ReplaceNames(after), structural: true));
        });
    }

    public void DeleteName(string name, Worksheet? scope = null)
    {
        Execute("Namen löschen", () =>
        {
            var before = Workbook.Names.ToList();
            Workbook.Names.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) && existing.SheetId == scope?.Id);
            var after = Workbook.Names.ToList();
            Record(new ActionStep(() => ReplaceNames(before), () => ReplaceNames(after), structural: true));
        });
    }

    /// <summary>The text a name refers to, in the locale ("=Tabelle1!$A$1").</summary>
    public string NameFormulaText(DefinedName name)
    {
        try
        {
            return "=" + FormulaFormatter.Format(FormulaParser.ParseNode(name.Formula, Workbook, default), Workbook, default, Syntax);
        }
        catch (FormulaParseException)
        {
            return "=" + name.Formula;
        }
    }

    private void ReplaceNames(List<DefinedName> names)
    {
        Workbook.Names.Clear();
        Workbook.Names.AddRange(names);
    }

    private void ValidateName(string name, Worksheet? scope)
    {
        _ = scope;
        if (name.Length is 0 or > 255 || !NamePattern.IsMatch(name) || CellAddress.TryParse(name) is not null
            || Regex.IsMatch(name, "^(?:[RZ][0-9]*)?(?:[CS][0-9]*)?$", RegexOptions.IgnoreCase) || string.Equals(name, Locale.TrueName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Locale.FalseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Der eingegebene Name ist ungültig. Namen beginnen mit einem Buchstaben oder Unterstrich, enthalten keine Leerzeichen und dürfen keinem Zellbezug gleichen.", "name");
        }
    }
}

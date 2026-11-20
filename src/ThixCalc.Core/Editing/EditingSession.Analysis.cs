using System.Globalization;
using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>Inhalte auswählen (Gehe zu › Inhalte): which cells of the selection (or the used range) to select.</summary>
public enum SpecialCells : byte
{
    Blanks,
    Constants,
    Formulas,
    Errors,
    VisibleCells,
    CurrentRegion,
    LastCell,
    Comments,
}

/// <summary>A Teilergebnis function (TEILERGEBNIS's codes 1–11).</summary>
public enum SubtotalFunction : byte
{
    Average = 1,
    Count = 2,
    CountA = 3,
    Max = 4,
    Min = 5,
    Product = 6,
    StdDev = 7,
    StdDevP = 8,
    Sum = 9,
    Var = 10,
    VarP = 11,
}

/// <summary>An arrow of Spur zum Vorgänger/Nachfolger: from a cell or range to a cell (another sheet: <see cref="OtherSheet"/>).</summary>
public sealed record TraceArrow(CellRange From, CellAddress To, bool OtherSheet = false);

/// <summary>
/// Daten › Gliederung › Teilergebnis and Detail ein-/ausblenden, Was-wäre-wenn › Zielwertsuche, Formelüberwachung (Spur zum
/// Vorgänger/Nachfolger, Fehlerüberprüfung, Formelauswertung), Namen aus Auswahl erstellen, Inhalte auswählen. Changes are undoable;
/// the trace arrows are a view state.
/// </summary>
public sealed partial class EditingSession
{
    private readonly List<TraceArrow> _arrows = [];

    // ---------------------------------------------------------------- Teilergebnis

    /// <summary>
    /// Teilergebnis: under each run of equal values in <paramref name="groupColumn"/> (a column offset of the list, header in its first
    /// row) a row "… Ergebnis" with TEILERGEBNIS(function; …) for <paramref name="columns"/>, a "Gesamtergebnis" row at the end, and the
    /// rows grouped (details at level 2) – like Excel; <paramref name="replace"/> removes earlier Teilergebnisse first.
    /// </summary>
    public CellRange AddSubtotals(CellRange list, int groupColumn, SubtotalFunction function, IReadOnlyList<int> columns, bool replace = true)
    {
        var sheet = ActiveSheet;
        if (list.RowCount < 2 || columns.Count == 0 || groupColumn < 0 || groupColumn >= list.ColumnCount || columns.Any(column => column < 0 || column >= list.ColumnCount))
        {
            throw new BusinessException("Markieren Sie eine Liste mit Überschrift und wählen Sie mindestens eine Spalte für das Teilergebnis.");
        }

        var result = list;
        Execute("Teilergebnis", () =>
        {
            if (replace)
            {
                list = RemoveSubtotalsIn(sheet, list);
            }

            var groupAt = list.FirstColumn + groupColumn;
            var label = FunctionLabel(function);
            var row = list.FirstRow + 1;
            var groupStart = row;
            var end = list.LastRow;
            var code = ((int)function).ToString(CultureInfo.InvariantCulture);
            while (row <= end + 1)
            {
                var value = row <= end ? sheet.GetValue(row, groupAt) : CellValue.FromText("\u0001end");
                var previous = sheet.GetValue(groupStart, groupAt);
                if (row > groupStart && (row > end || ValueComparer.Compare(value, previous) != 0))
                {
                    // A subtotal row below the group.
                    Shift("Teilergebnis", sheet, rows: true, row, 1, null);
                    end++;
                    SetCell(sheet, row, groupAt, new Cell(CellValue.FromText($"{PivotLabel(previous)} {label}"), null, BoldStyle(sheet, row, groupAt)));
                    foreach (var column in columns)
                    {
                        var at = list.FirstColumn + column;
                        var formula = FormulaParser.Parse($"=SUBTOTAL({code},{new CellRange(groupStart, at, row - 1, at)})", Workbook, sheet, new CellAddress(row, at));
                        SetCell(sheet, row, at, new Cell(CellValue.Blank, formula, BoldStyle(sheet, row, at)));
                    }

                    GroupRows(sheet, groupStart, row - 1, 2);
                    GroupRows(sheet, row, row, 1);
                    row++;
                    groupStart = row;
                    continue;
                }

                row++;
            }

            // Gesamtergebnis below everything (its TEILERGEBNIS skips the subtotals above).
            var total = end + 1;
            Shift("Teilergebnis", sheet, rows: true, total, 1, null);
            SetCell(sheet, total, groupAt, new Cell(CellValue.FromText($"Gesamt{label.ToLowerInvariant()}"), null, BoldStyle(sheet, total, groupAt)));
            foreach (var column in columns)
            {
                var at = list.FirstColumn + column;
                var formula = FormulaParser.Parse($"=SUBTOTAL({code},{new CellRange(list.FirstRow + 1, at, total - 1, at)})", Workbook, sheet, new CellAddress(total, at));
                SetCell(sheet, total, at, new Cell(CellValue.Blank, formula, BoldStyle(sheet, total, at)));
            }

            GroupRows(sheet, list.FirstRow + 1, total - 1, 1, keepDeeper: true);
            result = new CellRange(list.FirstRow, list.FirstColumn, total, list.LastColumn);
        });
        return result;
    }

    /// <summary>Teilergebnis › Alle entfernen: the rows holding TEILERGEBNIS go, the outline too.</summary>
    public void RemoveSubtotals(CellRange list)
    {
        var sheet = ActiveSheet;
        Execute("Teilergebnisse entfernen", () => RemoveSubtotalsIn(sheet, list));
    }

    private CellRange RemoveSubtotalsIn(Worksheet sheet, CellRange list)
    {
        var last = list.LastRow;
        for (var row = list.LastRow; row > list.FirstRow; row--)
        {
            var isSubtotal = Enumerable.Range(list.FirstColumn, list.ColumnCount)
                .Any(column => sheet.GetFormula(row, column)?.Root is FunctionNode { Name: "SUBTOTAL" });
            if (isSubtotal)
            {
                Shift("Teilergebnisse entfernen", sheet, rows: true, row, -1, null);
                last--;
            }
        }

        var before = sheet.Rows.Snapshot();
        var size = sheet.Rows.DefaultSize;
        sheet.Rows.Update(list.FirstRow, Math.Max(list.FirstRow, last + 1), info => info with { OutlineLevel = 0, Collapsed = false, Hidden = false });
        var after = sheet.Rows.Snapshot();
        Record(new ActionStep(() => sheet.Rows.Restore(before, size), () => sheet.Rows.Restore(after, size)));
        return new CellRange(list.FirstRow, list.FirstColumn, last, list.LastColumn);
    }

    private void GroupRows(Worksheet sheet, int first, int last, int level, bool keepDeeper = false)
    {
        var before = sheet.Rows.Snapshot();
        var size = sheet.Rows.DefaultSize;
        sheet.Rows.Update(first, last, info => info with { OutlineLevel = keepDeeper ? Math.Max(info.OutlineLevel, level) : level });
        var after = sheet.Rows.Snapshot();
        Record(new ActionStep(() => sheet.Rows.Restore(before, size), () => sheet.Rows.Restore(after, size)));
    }

    private int BoldStyle(Worksheet sheet, int row, int column) =>
        Workbook.Styles.Intern(sheet.GetStyle(row, column) is var style ? style with { Font = style.Font with { Bold = true } } : CellStyle.Default);

    private static string FunctionLabel(SubtotalFunction function) => function switch
    {
        SubtotalFunction.Sum => "Ergebnis",
        SubtotalFunction.Count or SubtotalFunction.CountA => "Anzahl",
        SubtotalFunction.Average => "Mittelwert",
        SubtotalFunction.Max => "Maximum",
        SubtotalFunction.Min => "Minimum",
        SubtotalFunction.Product => "Produkt",
        _ => "Ergebnis",
    };

    private string PivotLabel(CellValue value) => value.Kind switch
    {
        CellValueKind.Text => value.Text,
        CellValueKind.Number => NumberText.ToText(value.Number, Locale),
        CellValueKind.Blank => "(Leer)",
        _ => Coercion.ToText(value, Locale),
    };

    // ---------------------------------------------------------------- Gliederung

    /// <summary>Detail ausblenden / einblenden: the rows (or columns) of the outline group around the index.</summary>
    public void ShowDetail(bool rows, int index, bool show)
    {
        var axis = rows ? ActiveSheet.Rows : ActiveSheet.Columns;
        var level = axis[index].OutlineLevel;
        if (level == 0)
        {
            throw new BusinessException("Zum Ein- oder Ausblenden von Details markieren Sie eine Zelle in einer Gruppe.");
        }

        var first = index;
        while (first > 0 && axis[first - 1].OutlineLevel >= level)
        {
            first--;
        }

        var last = index;
        while (last < (rows ? SheetLimits.LastRow : SheetLimits.LastColumn) && axis[last + 1].OutlineLevel >= level && last - first < 1_000_000)
        {
            last++;
        }

        ChangeAxis(show ? "Detail einblenden" : "Detail ausblenden", axis, first, last, info => info with { Hidden = !show && info.OutlineLevel >= level });
    }

    /// <summary>The outline level buttons 1, 2, 3 …: rows deeper than <paramref name="level"/> hidden, the others shown.</summary>
    public void ShowOutlineLevel(bool rows, int level)
    {
        var axis = rows ? ActiveSheet.Rows : ActiveSheet.Columns;
        var used = ActiveSheet.UsedRange ?? new CellRange(0, 0, 0, 0);
        ChangeAxis($"Ebene {level}", axis, 0, rows ? used.LastRow + 1 : used.LastColumn + 1, info => info.OutlineLevel == 0 ? info : info with { Hidden = info.OutlineLevel >= level });
    }

    // ---------------------------------------------------------------- Zielwertsuche

    /// <summary>
    /// Zielwertsuche: changes <paramref name="changing"/> (a constant) until <paramref name="target"/> (a formula) gives <paramref name="value"/>
    /// (secant steps, then bisection; Excel's 100 iterations, 0,001). One undo step; false when no solution was found (the cell stays).
    /// </summary>
    public (bool Found, double Input, double Result) GoalSeek(CellAddress target, double value, CellAddress changing)
    {
        var sheet = ActiveSheet;
        if (sheet.GetFormula(target.Row, target.Column) is null)
        {
            throw new BusinessException("Die Zielzelle muss eine Formel enthalten.", "target");
        }

        if (sheet.GetFormula(changing.Row, changing.Column) is not null)
        {
            throw new BusinessException("Die veränderbare Zelle muss einen Wert enthalten, keine Formel.", "changing");
        }

        var original = sheet.GetCell(changing);
        double Evaluate(double input)
        {
            sheet.SetValue(changing.Row, changing.Column, input);
            Workbook.Calculation.Recalculate();
            var result = sheet.GetValue(target);
            return result.IsNumber ? result.Number - value : double.NaN;
        }

        var x0 = original.Value.IsNumber ? original.Value.Number : 0;
        var x1 = x0 == 0 ? 1 : x0 * 1.01;
        var f0 = Evaluate(x0);
        var f1 = Evaluate(x1);
        var found = false;
        var best = x0;
        for (var step = 0; step < 100 && !double.IsNaN(f0) && !double.IsNaN(f1); step++)
        {
            if (Math.Abs(f1) <= 0.001)
            {
                best = x1;
                found = true;
                break;
            }

            if (f1 == f0)
            {
                x1 += Math.Max(1, Math.Abs(x1)) * 0.1;
                f1 = Evaluate(x1);
                continue;
            }

            var next = x1 - f1 * (x1 - x0) / (f1 - f0);
            (x0, f0) = (x1, f1);
            x1 = double.IsFinite(next) ? next : x1 * 2;
            f1 = Evaluate(x1);
            best = x1;
        }

        // Back to the original, then the found value as one undo step.
        sheet.SetCell(changing.Row, changing.Column, original);
        Workbook.Calculation.Recalculate();
        if (!found)
        {
            Recalculate();
            return (false, original.Value.Number, sheet.GetValue(target).Number);
        }

        Execute("Zielwertsuche", () => SetCell(sheet, changing.Row, changing.Column, new Cell(CellValue.FromNumber(best), null, original.StyleId)));
        return (true, best, sheet.GetValue(target).Number);
    }

    // ---------------------------------------------------------------- Formelüberwachung

    /// <summary>The arrows of Spur zum Vorgänger/Nachfolger on the active sheet (a view state).</summary>
    public IReadOnlyList<TraceArrow> TraceArrows => _arrows;

    /// <summary>Spur zum Vorgänger: arrows from the cells the active cell's formula refers to.</summary>
    public int TracePrecedents()
    {
        var cell = ActiveCell;
        var formula = ActiveSheet.GetFormula(cell.Row, cell.Column);
        if (formula is null)
        {
            throw new BusinessException("Die aktive Zelle enthält keine Formel, deren Vorgänger angezeigt werden könnten.");
        }

        var added = 0;
        foreach (var (sheetId, range) in References(formula.Root, cell))
        {
            var arrow = new TraceArrow(range, cell, sheetId is { } id && id != ActiveSheet.Id);
            if (!_arrows.Contains(arrow))
            {
                _arrows.Add(arrow);
                added++;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Spur zum Nachfolger: arrows to the formulas on the sheet that refer to the active cell.</summary>
    public int TraceDependents()
    {
        var cell = ActiveCell;
        var added = 0;
        foreach (var entry in ActiveSheet.GetCells().Where(entry => entry.Cell.Formula is not null).ToList())
        {
            if (References(entry.Cell.Formula!.Root, entry.Address).Any(reference => (reference.SheetId is null || reference.SheetId == ActiveSheet.Id) && reference.Range.Contains(cell)))
            {
                var arrow = new TraceArrow(CellRange.Cell(cell), entry.Address);
                if (!_arrows.Contains(arrow))
                {
                    _arrows.Add(arrow);
                    added++;
                }
            }
        }

        if (added == 0)
        {
            throw new BusinessException("Es gibt keine Formeln auf diesem Blatt, die sich auf die aktive Zelle beziehen.");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Pfeile entfernen.</summary>
    public void RemoveArrows()
    {
        _arrows.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<(int? SheetId, CellRange Range)> References(FormulaNode node, CellAddress origin, int depth = 0)
    {
        switch (node)
        {
            case NameNode name when depth < 8 && Workbook.FindName(name.Name, ActiveSheet) is { } defined:
                FormulaNode? parsed;
                try
                {
                    parsed = FormulaParser.ParseNode(defined.Formula, Workbook, default);
                }
                catch (FormulaParseException)
                {
                    parsed = null;
                }

                if (parsed is not null)
                {
                    foreach (var found in References(parsed, origin, depth + 1))
                    {
                        yield return found;
                    }
                }

                break;
            case ReferenceNode reference:
                if (reference.Area.Resolve(origin) is { } range)
                {
                    yield return (reference.Sheet?.FirstSheetId, range);
                }

                break;
            case FunctionNode function:
                foreach (var argument in function.Arguments)
                {
                    foreach (var found in References(argument, origin))
                    {
                        yield return found;
                    }
                }

                break;
            case BinaryNode binary:
                foreach (var found in References(binary.Left, origin).Concat(References(binary.Right, origin)))
                {
                    yield return found;
                }

                break;
            case UnaryNode unary:
                foreach (var found in References(unary.Operand, origin))
                {
                    yield return found;
                }

                break;
            case PercentNode percent:
                foreach (var found in References(percent.Operand, origin))
                {
                    yield return found;
                }

                break;
            case ParenthesesNode parentheses:
                foreach (var found in References(parentheses.Inner, origin))
                {
                    yield return found;
                }

                break;
        }
    }

    /// <summary>Fehlerüberprüfung: the next cell after the active one (row by row, wrapping) whose value is an error, or null.</summary>
    public CellAddress? NextError()
    {
        var cells = ActiveSheet.GetCells().Where(entry => entry.Cell.Value.IsError).Select(entry => entry.Address).ToList();
        if (cells.Count == 0)
        {
            return null;
        }

        var active = ActiveCell;
        var next = cells.FirstOrDefault(address => address.Row > active.Row || address.Row == active.Row && address.Column > active.Column, cells[0]);
        Select(next);
        return next;
    }

    /// <summary>What an error means (Fehlerüberprüfung's explanation, German).</summary>
    public static string ExplainError(CellError error) => error switch
    {
        CellError.DivisionByZero => "Fehler „Division durch 0“: Die Formel teilt durch 0 oder eine leere Zelle.",
        CellError.NotAvailable => "Fehler „Wert nicht verfügbar“: Ein gesuchter Wert wurde nicht gefunden (z. B. von SVERWEIS oder VERGLEICH).",
        CellError.Name => "Fehler „Ungültiger Name“: Die Formel enthält einen Namen oder eine Funktion, die es nicht gibt.",
        CellError.Reference => "Fehler „Ungültiger Zellbezug“: Die Formel bezieht sich auf gelöschte Zellen.",
        CellError.Value => "Fehler „Wert“: Ein Argument hat den falschen Datentyp (z. B. Text statt einer Zahl).",
        CellError.Number => "Fehler „Ungültige Zahl“: Das Ergebnis ist zu groß, zu klein oder nicht definiert.",
        CellError.Null => "Fehler „Leere Schnittmenge“: Zwei Bereiche schneiden sich nicht.",
        CellError.Spill => "Fehler „Überlauf“: Der Bereich für das Ergebnis ist nicht leer.",
        CellError.Calc => "Fehler „Berechnung“: Das Ergebnis ist ein leeres Array oder lässt sich nicht berechnen.",
        _ => "Die Formel ergibt einen Fehler.",
    };

    /// <summary>
    /// Formelauswertung: the steps from the formula to its result – each reference replaced by its value, then each function call
    /// (innermost first) by its result, as Excel's dialog walks them.
    /// </summary>
    public IReadOnlyList<string> EvaluateFormulaSteps(CellAddress cell)
    {
        var sheet = ActiveSheet;
        var formula = sheet.GetFormula(cell.Row, cell.Column) ?? throw new BusinessException("Die Zelle enthält keine Formel.");
        var steps = new List<string>();
        var text = "=" + FormulaFormatter.Format(formula, Workbook, cell, Syntax);
        steps.Add(text);
        var node = formula.Root;
        for (var guard = 0; guard < 64; guard++)
        {
            var next = Reduce(node, cell, out var changed);
            if (!changed)
            {
                break;
            }

            node = next;
            steps.Add("=" + FormulaFormatter.Format(new Formula(node), Workbook, cell, Syntax));
        }

        var result = sheet.GetValue(cell);
        steps.Add(PreviewOf(result));
        return steps;
    }

    private string PreviewOf(CellValue value) => value.Kind switch
    {
        CellValueKind.Text => "\"" + value.Text + "\"",
        CellValueKind.Number => NumberText.ToText(value.Number, Locale),
        CellValueKind.Boolean => Locale.BooleanName(value.Boolean),
        CellValueKind.Error => Locale.ErrorName(value.Error),
        _ => "0",
    };

    /// <summary>One step of Formelauswertung: the first reference becomes its value, else the innermost call its result.</summary>
    private FormulaNode Reduce(FormulaNode node, CellAddress origin, out bool changed)
    {
        changed = false;
        if (node is ReferenceNode { Area.Kind: AreaKind.Cell } reference)
        {
            changed = true;
            return Literal(Workbook.Calculation.Evaluate(new Formula(node), ActiveSheet, origin));
        }

        switch (node)
        {
            case FunctionNode function:
                for (var index = 0; index < function.Arguments.Length; index++)
                {
                    var reduced = Reduce(function.Arguments[index], origin, out changed);
                    if (changed)
                    {
                        return function with { Arguments = function.Arguments.SetItem(index, reduced) };
                    }
                }

                changed = true;
                return Literal(Workbook.Calculation.Evaluate(new Formula(function), ActiveSheet, origin));
            case BinaryNode binary:
                var left = Reduce(binary.Left, origin, out changed);
                if (changed)
                {
                    return binary with { Left = left };
                }

                var right = Reduce(binary.Right, origin, out changed);
                if (changed)
                {
                    return binary with { Right = right };
                }

                if (binary.Left is not ReferenceNode && binary.Right is not ReferenceNode)
                {
                    changed = true;
                    return Literal(Workbook.Calculation.Evaluate(new Formula(binary), ActiveSheet, origin));
                }

                return node;
            case ParenthesesNode parentheses:
                var inner = Reduce(parentheses.Inner, origin, out changed);
                return changed ? parentheses with { Inner = inner } : node;
            case UnaryNode unary:
                var operand = Reduce(unary.Operand, origin, out changed);
                return changed ? unary with { Operand = operand } : node;
            default:
                return node;
        }
    }

    private static FormulaNode Literal(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => new NumberNode(value.Number),
        CellValueKind.Text => new TextNode(value.Text),
        CellValueKind.Boolean => new BooleanNode(value.Boolean),
        CellValueKind.Error => new ErrorNode(value.Error),
        _ => new NumberNode(0),
    };

    // ---------------------------------------------------------------- Namen aus Auswahl erstellen

    /// <summary>
    /// Aus Auswahl erstellen: names from the selection's oberste Zeile / linke Spalte / unterste Zeile / rechte Spalte for the cells
    /// beside them (spaces become "_", invalid characters go, a leading digit gets "_"). Returns the names created.
    /// </summary>
    public IReadOnlyList<string> CreateNamesFromSelection(bool top, bool left, bool bottom = false, bool right = false)
    {
        var range = Selection;
        var sheet = ActiveSheet;
        var created = new List<(string Name, CellRange Range)>();
        void Add(CellValue label, CellRange target)
        {
            if (Sanitize(label) is { } name && target.CellCount > 0)
            {
                created.Add((name, target));
            }
        }

        var firstRow = range.FirstRow + (top ? 1 : 0);
        var lastRow = range.LastRow - (bottom ? 1 : 0);
        var firstColumn = range.FirstColumn + (left ? 1 : 0);
        var lastColumn = range.LastColumn - (right ? 1 : 0);
        if (firstRow > lastRow || firstColumn > lastColumn)
        {
            throw new BusinessException("Die Auswahl ist für Namen aus Auswahl zu klein.");
        }

        for (var column = firstColumn; column <= lastColumn; column++)
        {
            if (top)
            {
                Add(sheet.GetValue(range.FirstRow, column), new CellRange(firstRow, column, lastRow, column));
            }

            if (bottom)
            {
                Add(sheet.GetValue(range.LastRow, column), new CellRange(firstRow, column, lastRow, column));
            }
        }

        for (var row = firstRow; row <= lastRow; row++)
        {
            if (left)
            {
                Add(sheet.GetValue(row, range.FirstColumn), new CellRange(row, firstColumn, row, lastColumn));
            }

            if (right)
            {
                Add(sheet.GetValue(row, range.LastColumn), new CellRange(row, firstColumn, row, lastColumn));
            }
        }

        if (created.Count == 0)
        {
            throw new BusinessException("In der Auswahl stehen keine Beschriftungen, aus denen Namen werden könnten.");
        }

        var prefix = sheet.Name.All(character => char.IsLetterOrDigit(character) || character == '_') ? sheet.Name : $"'{sheet.Name.Replace("'", "''", StringComparison.Ordinal)}'";
        Execute("Namen aus Auswahl erstellen", () =>
        {
            foreach (var (name, target) in created)
            {
                DefineName(name, $"={prefix}!{target.ToAbsoluteString()}");
            }
        });
        return [.. created.Select(entry => entry.Name)];
    }

    private string? Sanitize(CellValue label)
    {
        if (label.IsBlank || label.IsError)
        {
            return null;
        }

        var text = PivotLabel(label).Trim();
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '.' ? character : character == ' ' ? '_' : '\0');
        }

        var name = builder.ToString().Replace("\0", string.Empty, StringComparison.Ordinal);
        if (name.Length == 0)
        {
            return null;
        }

        if (char.IsDigit(name[0]) || CellRange.TryParse(name) is not null || name.Length <= 2 && name.All(char.IsLetter) && name.Length == 1 && "RCrcZSzs".Contains(name[0]))
        {
            name = "_" + name;
        }

        return name;
    }

    // ---------------------------------------------------------------- Inhalte auswählen

    /// <summary>Inhalte auswählen: the cells of the kind within the selection (the used range when one cell is selected).</summary>
    public IReadOnlyList<CellRange> SelectSpecial(SpecialCells kind)
    {
        var sheet = ActiveSheet;
        var used = sheet.UsedRange ?? CellRange.Cell(ActiveCell);
        var scope = Selection.IsSingleCell ? used : Selection.Intersect(used) ?? Selection;
        List<CellRange> ranges;
        switch (kind)
        {
            case SpecialCells.CurrentRegion:
                ranges = [Navigation.CurrentRegion(sheet, ActiveCell)];
                break;
            case SpecialCells.LastCell:
                ranges = [CellRange.Cell(used.Last)];
                break;
            case SpecialCells.VisibleCells:
                ranges = VisibleBlocks(sheet, Selection.IsSingleCell ? used : Selection);
                break;
            default:
                var cells = new List<CellAddress>();
                for (var row = scope.FirstRow; row <= scope.LastRow && cells.Count < 1_000_000; row++)
                {
                    for (var column = scope.FirstColumn; column <= scope.LastColumn; column++)
                    {
                        var cell = sheet.GetCell(row, column);
                        var take = kind switch
                        {
                            SpecialCells.Blanks => cell.Value.IsBlank && cell.Formula is null && !cell.IsSpilled,
                            SpecialCells.Constants => cell.Formula is null && !cell.IsSpilled && !cell.Value.IsBlank,
                            SpecialCells.Formulas => cell.Formula is not null,
                            SpecialCells.Errors => cell.Value.IsError,
                            SpecialCells.Comments => sheet.Comments.ContainsKey(new CellAddress(row, column)),
                            _ => false,
                        };
                        if (take)
                        {
                            cells.Add(new CellAddress(row, column));
                        }
                    }
                }

                ranges = Merge(cells);
                break;
        }

        if (ranges.Count == 0)
        {
            throw new BusinessException("Es wurden keine Zellen gefunden.");
        }

        SetSelection(ranges[0].First, ranges);
        return ranges;
    }

    /// <summary>Nur sichtbare Zellen (Alt+;): the selection without hidden rows and columns, as blocks.</summary>
    private static List<CellRange> VisibleBlocks(Worksheet sheet, CellRange range)
    {
        var rows = Runs(range.FirstRow, Math.Min(range.LastRow, (sheet.UsedRange?.LastRow ?? range.FirstRow) + 1), index => !sheet.Rows.IsHidden(index));
        var columns = Runs(range.FirstColumn, Math.Min(range.LastColumn, (sheet.UsedRange?.LastColumn ?? range.FirstColumn) + 1), index => !sheet.Columns.IsHidden(index));
        return [.. rows.SelectMany(row => columns.Select(column => new CellRange(row.First, column.First, row.Last, column.Last)))];
    }

    private static List<(int First, int Last)> Runs(int first, int last, Func<int, bool> visible)
    {
        var runs = new List<(int, int)>();
        int? start = null;
        for (var index = first; index <= last + 1; index++)
        {
            var shown = index <= last && visible(index);
            if (shown && start is null)
            {
                start = index;
            }
            else if (!shown && start is { } begin)
            {
                runs.Add((begin, index - 1));
                start = null;
            }
        }

        return runs;
    }

    /// <summary>Cells to ranges: horizontal runs per row, then equal runs of consecutive rows joined.</summary>
    private static List<CellRange> Merge(List<CellAddress> cells)
    {
        var runs = new List<CellRange>();
        foreach (var row in cells.GroupBy(cell => cell.Row))
        {
            var columns = row.Select(cell => cell.Column).OrderBy(column => column).ToList();
            var start = columns[0];
            for (var index = 1; index <= columns.Count; index++)
            {
                if (index == columns.Count || columns[index] != columns[index - 1] + 1)
                {
                    runs.Add(new CellRange(row.Key, start, row.Key, columns[index - 1]));
                    if (index < columns.Count)
                    {
                        start = columns[index];
                    }
                }
            }
        }

        var merged = new List<CellRange>();
        foreach (var run in runs.OrderBy(run => run.FirstColumn).ThenBy(run => run.LastColumn).ThenBy(run => run.FirstRow))
        {
            var last = merged.Count - 1;
            if (last >= 0 && merged[last].FirstColumn == run.FirstColumn && merged[last].LastColumn == run.LastColumn && merged[last].LastRow + 1 == run.FirstRow)
            {
                merged[last] = merged[last].Union(run);
            }
            else
            {
                merged.Add(run);
            }
        }

        return merged;
    }
}

using System.Collections.Immutable;
using System.Globalization;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>
/// Tables (Einfügen › Tabelle, Strg+T): creating one over a range (with or without a header row), its options and style,
/// the total row, renaming (formulas follow), resizing, converting back to a range (the look stays as cell formats), and the
/// automatic growth when the user types right below or beside a table. Every change is one undo step.
/// </summary>
public sealed partial class EditingSession
{
    /// <summary>The table around the active cell, or null (Tabellenentwurf shows while there is one).</summary>
    public TableDefinition? ActiveTable => ActiveSheet.Tables.FirstOrDefault(table => table.Range.Contains(ActiveCell));

    /// <summary>The range "Tabelle erstellen" proposes: the selection, or the current region of a single cell.</summary>
    public CellRange ProposedTableRange() => Selection.IsSingleCell ? Navigation.CurrentRegion(ActiveSheet, ActiveCell) : Selection;

    /// <summary>
    /// Makes <paramref name="range"/> a table. Without headers a header row "Spalte1, Spalte2 …" goes above the data (the cells
    /// below move down, as in Excel). Texts of the header row become the column names (blanks and doubles get numbers).
    /// </summary>
    public TableDefinition CreateTable(CellRange range, bool hasHeaders, string? style = null)
    {
        var sheet = ActiveSheet;
        if (range.IsWholeColumns || range.IsWholeRows || range.CellCount > 10_000_000)
        {
            throw new BusinessException("Der Bereich ist für eine Tabelle zu groß.", "range");
        }

        if (sheet.Tables.Any(table => table.Range.Intersects(range)))
        {
            throw new BusinessException("Tabellen dürfen sich nicht überschneiden.", "range");
        }

        if (sheet.Merges.Intersecting(range).Any())
        {
            throw new BusinessException("Eine Tabelle kann keine verbundenen Zellen enthalten. Heben Sie den Zellverbund zuerst auf.", "range");
        }

        var name = NextTableName();
        TableDefinition? created = null;
        Execute("Tabelle erstellen", () =>
        {
            var area = range;
            if (!hasHeaders)
            {
                InsertCells(new CellRange(range.FirstRow, range.FirstColumn, range.FirstRow, range.LastColumn), CellShift.Down);
                area = new CellRange(range.FirstRow, range.FirstColumn, range.LastRow + 1, range.LastColumn);
            }

            if (area.RowCount < 2)
            {
                area = new CellRange(area.FirstRow, area.FirstColumn, area.FirstRow + 1, area.LastColumn);
            }

            var columns = HeaderNames(sheet, area);
            for (var index = 0; index < columns.Count; index++)
            {
                var cell = sheet.GetCell(area.FirstRow, area.FirstColumn + index);
                if (cell.Formula is not null || cell.Value.Kind != CellValueKind.Text || cell.Value.Text != columns[index])
                {
                    SetCell(sheet, area.FirstRow, area.FirstColumn + index, new Cell(CellValue.FromText(columns[index]), null, cell.StyleId));
                }
            }

            created = new TableDefinition(name, area, columns) { Style = style ?? TableStyles.DefaultStyle };
            ChangeSheetState(sheet, () =>
            {
                if (sheet.AutoFilter is { } filter && filter.Range.Intersects(area))
                {
                    sheet.AutoFilter = null;
                }

                sheet.Tables.Add(created);
            });
            TablesChanged();
        });
        Select(created!.Range.First);
        return created;
    }

    /// <summary>"Tabelle1", "Tabelle2" … the first name no table and no defined name has.</summary>
    public string NextTableName()
    {
        for (var number = 1; ; number++)
        {
            var name = "Tabelle" + number.ToString(CultureInfo.InvariantCulture);
            if (!NameTaken(name))
            {
                return name;
            }
        }
    }

    private bool NameTaken(string name) =>
        Workbook.Sheets.SelectMany(sheet => sheet.Tables).Any(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
        || Workbook.Names.Any(defined => string.Equals(defined.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The column names of a header row: its texts; blanks become "Spalte n", doubles get a number.</summary>
    private static List<string> HeaderNames(Worksheet sheet, CellRange area)
    {
        var names = new List<string>();
        for (var column = area.FirstColumn; column <= area.LastColumn; column++)
        {
            var value = sheet.GetValue(area.FirstRow, column);
            var text = value.IsBlank ? string.Empty : value.Kind == CellValueKind.Text ? value.Text.Trim() : value.ToString().Trim();
            if (text.Length == 0)
            {
                text = "Spalte" + (column - area.FirstColumn + 1).ToString(CultureInfo.InvariantCulture);
            }

            var unique = text;
            for (var number = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); number++)
            {
                unique = text + number.ToString(CultureInfo.InvariantCulture);
            }

            names.Add(unique);
        }

        return names;
    }

    /// <summary>Changes a table's options or style (Tabellenentwurf).</summary>
    public void SetTableOptions(string name, Func<TableDefinition, TableDefinition> change, string label = "Tabellenformatoptionen")
    {
        var (sheet, table) = FindTable(name);
        var changed = change(table);
        if (changed == table)
        {
            return;
        }

        Execute(label, () =>
        {
            if (table.ShowFilterButton && !changed.ShowFilterButton && table.Filter is { Columns.Count: > 0 })
            {
                ShowAllRows(sheet, table.FilterRange);
                changed = changed with { Filter = null };
            }

            ReplaceTable(sheet, table, changed);
        });
    }

    /// <summary>
    /// Ergebniszeile on: a row below the data (cells below move down when it is not empty) with "Ergebnis" in the first column and
    /// TEILERGEBNIS(109;[Spalte]) – the sum of the visible rows – in the last; off: the row goes.
    /// </summary>
    public void SetTotalsRow(string name, bool visible)
    {
        var (sheet, table) = FindTable(name);
        if (table.HasTotals == visible)
        {
            return;
        }

        Execute("Ergebniszeile", () =>
        {
            if (!visible)
            {
                var row = table.Range.LastRow;
                for (var column = table.Range.FirstColumn; column <= table.Range.LastColumn; column++)
                {
                    SetCell(sheet, row, column, Cell.Empty with { StyleId = sheet.GetCell(row, column).StyleId });
                }

                ReplaceTable(sheet, table, table with
                {
                    Range = WithLastRow(table.Range, row - 1),
                    TotalsRowCount = 0,
                    TotalsFunctions = [],
                    TotalsLabels = [],
                });
                return;
            }

            var below = new CellRange(table.Range.LastRow + 1, table.Range.FirstColumn, table.Range.LastRow + 1, table.Range.LastColumn);
            if (sheet.GetCells(below).Any(entry => !entry.Cell.Value.IsBlank || entry.Cell.HasFormula))
            {
                InsertCells(below, CellShift.Down);
                (sheet, table) = FindTable(name);
            }

            var totalsRow = table.Range.LastRow + 1;
            var last = table.Columns.Count - 1;
            var data = new CellRange(table.DataRange.FirstRow, table.Range.LastColumn, table.DataRange.LastRow, table.Range.LastColumn);
            var numeric = sheet.GetCells(data).Any(entry => entry.Cell.Value.IsNumber);
            var functions = Enumerable.Repeat<string?>(null, table.Columns.Count).ToArray();
            var labels = Enumerable.Repeat<string?>(null, table.Columns.Count).ToArray();
            labels[0] = "Ergebnis";
            if (last > 0)
            {
                functions[last] = numeric ? "sum" : "count";
            }

            // The table first: the total row's formula names its column without the table ([Preis]).
            ReplaceTable(sheet, table, table with
            {
                Range = WithLastRow(table.Range, totalsRow),
                TotalsRowCount = 1,
                TotalsFunctions = functions,
                TotalsLabels = labels,
            });
            if (last > 0)
            {
                var formula = FormulaParser.Parse($"SUBTOTAL({(numeric ? 109 : 103)},[{EscapeColumn(table.Columns[last])}])", Workbook, sheet, new CellAddress(totalsRow, table.Range.LastColumn));
                SetCell(sheet, totalsRow, table.Range.LastColumn, new Cell(CellValue.Blank, formula, sheet.GetStyleId(totalsRow, table.Range.LastColumn)));
            }

            SetCell(sheet, totalsRow, table.Range.FirstColumn, new Cell(CellValue.FromText("Ergebnis"), null, sheet.GetStyleId(totalsRow, table.Range.FirstColumn)));
        });
    }

    /// <summary>Tabellenname: checked like Excel (a letter or _ first, no spaces, no cell reference, unique); formulas follow.</summary>
    public void RenameTable(string name, string newName)
    {
        newName = newName.Trim();
        var (sheet, table) = FindTable(name);
        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return;
        }

        CheckTableName(newName, table);
        Execute("Tabelle umbenennen", () =>
        {
            RewriteStructuredReferences((node, _, _) => node.TableName is { } tableName && string.Equals(tableName, name, StringComparison.OrdinalIgnoreCase) ? node with { TableName = newName } : node);
            ReplaceTable(sheet, table, table with { Name = newName });
        });
    }

    private void CheckTableName(string name, TableDefinition? self)
    {
        if (name.Length == 0 || name.Length > 255 || !(char.IsLetter(name[0]) || name[0] is '_' or '\\') || name.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '.' or '\\')))
        {
            throw new BusinessException("Ein Tabellenname beginnt mit einem Buchstaben oder _ und enthält nur Buchstaben, Ziffern, _ und Punkte – keine Leerzeichen.", "name");
        }

        var index = 0;
        if (A1.TryReadCell(name, ref index, out _, out _, out _) && index == name.Length || name is "R" or "C" or "r" or "c" or "Z" or "S" or "z" or "s")
        {
            throw new BusinessException("Ein Tabellenname darf kein Zellbezug sein.", "name");
        }

        if (Workbook.Sheets.SelectMany(sheet => sheet.Tables).Any(table => table != self && string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
            || Workbook.Names.Any(defined => string.Equals(defined.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException($"Der Name „{name}“ ist bereits vergeben.", "name");
        }
    }

    /// <summary>Tabellengröße ändern: the same header row, more or fewer rows and columns.</summary>
    public void ResizeTable(string name, CellRange range)
    {
        var (sheet, table) = FindTable(name);
        if (range.FirstRow != table.Range.FirstRow)
        {
            throw new BusinessException("Die Überschriften müssen in derselben Zeile bleiben, und der neue Bereich muss die Tabelle überlappen.", "range");
        }

        if (sheet.Tables.Any(other => other != table && other.Range.Intersects(range)))
        {
            throw new BusinessException("Tabellen dürfen sich nicht überschneiden.", "range");
        }

        Execute("Tabellengröße ändern", () =>
        {
            var area = WithLastRow(range, Math.Max(range.LastRow, range.FirstRow + 1 + table.TotalsRowCount));
            var columns = HeaderNames(sheet, area);
            for (var index = 0; index < columns.Count; index++)
            {
                var cell = sheet.GetCell(area.FirstRow, area.FirstColumn + index);
                if (cell.Value.Kind != CellValueKind.Text)
                {
                    SetCell(sheet, area.FirstRow, area.FirstColumn + index, new Cell(CellValue.FromText(columns[index]), null, cell.StyleId));
                }
            }

            ReplaceTable(sheet, table, table with { Range = area, Columns = columns, TotalsFunctions = [], TotalsLabels = [] });
        });
    }

    /// <summary>In Bereich konvertieren: the table goes; its look stays as the cells' own formats, filtered rows show again.</summary>
    public void ConvertTableToRange(string name)
    {
        var (sheet, table) = FindTable(name);
        Execute("In Bereich konvertieren", () =>
        {
            for (var row = table.Range.FirstRow; row <= table.Range.LastRow; row++)
            {
                for (var column = table.Range.FirstColumn; column <= table.Range.LastColumn; column++)
                {
                    if (TableStyles.LookAt(sheet, row, column) is not { } look)
                    {
                        continue;
                    }

                    var cell = sheet.GetCell(row, column);
                    var style = sheet.GetStyle(row, column);
                    var baked = style with
                    {
                        Fill = style.Fill.Pattern == FillPattern.None && look.Fill is { } fill ? CellFill.Solid(CalcColor.FromHex(fill.ToHex())) : style.Fill,
                        Font = style.Font with
                        {
                            Bold = style.Font.Bold || look.Bold,
                            Color = style.Font.Color ?? (look.Text is { } text ? CalcColor.FromHex(text.ToHex()) : null),
                        },
                    };
                    if (baked != style)
                    {
                        SetCell(sheet, row, column, (cell.IsSpilled ? Cell.Empty : cell) with { StyleId = Workbook.Styles.Intern(baked) });
                    }
                }
            }

            if (table.Filter is { Columns.Count: > 0 })
            {
                ShowAllRows(sheet, table.FilterRange);
            }

            // Structured references to it become plain ones (Excel's way).
            var removed = table;
            RewriteStructuredReferences((node, formulaSheet, cell) =>
            {
                if (!RefersTo(node, removed, formulaSheet == sheet ? cell : null)
                    || Calculation.StructuredReferences.ResolveRange(removed, node.Specifier, cell.Row) is not { } target)
                {
                    return node;
                }

                return new ReferenceNode(SheetReference.Single(sheet.Id), new AreaReference(
                    new CellReference(target.FirstRow, target.FirstColumn, true, true),
                    new CellReference(target.LastRow, target.LastColumn, true, true),
                    target.IsSingleCell ? AreaKind.Cell : AreaKind.Range));
            });
            ChangeSheetState(sheet, () => sheet.Tables.Remove(removed));
            TablesChanged();
        });
    }

    /// <summary>
    /// After typing into <paramref name="cell"/>: a table right above it (without total row) grows by that row, a table at its
    /// left by that column (a header typed there names the column). Inside the open command.
    /// </summary>
    internal void GrowTableFor(Worksheet sheet, CellAddress cell, string text)
    {
        if (sheet.Tables.Count == 0 || text.Length == 0)
        {
            return;
        }

        foreach (var table in sheet.Tables.ToList())
        {
            var range = table.Range;
            if (!table.HasTotals && cell.Row == range.LastRow + 1 && cell.Column >= range.FirstColumn && cell.Column <= range.LastColumn)
            {
                ReplaceTable(sheet, table, table with { Range = WithLastRow(range, cell.Row) });
                return;
            }

            if (cell.Column == range.LastColumn + 1 && cell.Row >= range.FirstRow && cell.Row <= range.LastRow
                && !sheet.Tables.Any(other => other != table && other.Range.Contains(cell)))
            {
                var name = table.HasHeader && cell.Row == range.FirstRow ? text.Trim() : "Spalte" + (table.Columns.Count + 1).ToString(CultureInfo.InvariantCulture);
                for (var number = 2; table.Columns.Contains(name, StringComparer.OrdinalIgnoreCase); number++)
                {
                    name = "Spalte" + (table.Columns.Count + number).ToString(CultureInfo.InvariantCulture);
                }

                if (table.HasHeader && cell.Row != range.FirstRow)
                {
                    SetCell(sheet, range.FirstRow, cell.Column, new Cell(CellValue.FromText(name), null, sheet.GetStyleId(range.FirstRow, cell.Column)));
                }

                ReplaceTable(sheet, table, table with
                {
                    Range = new CellRange(range.FirstRow, range.FirstColumn, range.LastRow, cell.Column),
                    Columns = [.. table.Columns, name],
                    TotalsFunctions = table.TotalsFunctions.Count > 0 ? [.. table.TotalsFunctions, null] : [],
                    TotalsLabels = table.TotalsLabels.Count > 0 ? [.. table.TotalsLabels, null] : [],
                });
                return;
            }
        }
    }

    /// <summary>A header cell typed over: the column takes the new name (formulas that name the old one follow).</summary>
    internal void RenameColumnFor(Worksheet sheet, CellAddress cell, string text)
    {
        foreach (var table in sheet.Tables.ToList())
        {
            if (!table.HasHeader || cell.Row != table.Range.FirstRow || cell.Column < table.Range.FirstColumn || cell.Column > table.Range.LastColumn)
            {
                continue;
            }

            var index = cell.Column - table.Range.FirstColumn;
            var old = table.Columns[index];
            var name = text.Trim().Length == 0 ? "Spalte" + (index + 1).ToString(CultureInfo.InvariantCulture) : text.Trim();
            if (string.Equals(old, name, StringComparison.Ordinal))
            {
                return;
            }

            var columns = table.Columns.ToList();
            columns[index] = name;
            RewriteStructuredReferences((node, formulaSheet, formulaCell) => RefersTo(node, table, formulaSheet == sheet ? formulaCell : null)
                ? node with { Specifier = ReplaceColumn(node.Specifier, old, name) }
                : node);
            ReplaceTable(sheet, table, table with { Columns = columns });
            return;
        }
    }

    private static string ReplaceColumn(string specifier, string old, string name)
    {
        var escapedOld = EscapeColumn(old);
        var escapedNew = EscapeColumn(name);
        if (string.Equals(specifier, "[" + escapedOld + "]", StringComparison.OrdinalIgnoreCase))
        {
            return "[" + escapedNew + "]";
        }

        return specifier.Replace("[" + escapedOld + "]", "[" + escapedNew + "]", StringComparison.OrdinalIgnoreCase)
            .Replace("@" + escapedOld + "]", "@" + escapedNew + "]", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A column name inside brackets: ' escapes [ ] # '.</summary>
    internal static string EscapeColumn(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (character is '[' or ']' or '#' or '\'')
            {
                builder.Append('\'');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Whether a structured reference names <paramref name="table"/> (by name, or without one from a cell inside it).</summary>
    private static bool RefersTo(StructuredReferenceNode node, TableDefinition table, CellAddress? cell) =>
        node.TableName is { } name ? string.Equals(name, table.Name, StringComparison.OrdinalIgnoreCase) : cell is { } inside && table.Range.Contains(inside);

    /// <summary>Rewrites the structured references of every formula (one undo step with the open command); the map gets the formula's sheet and cell.</summary>
    private void RewriteStructuredReferences(Func<StructuredReferenceNode, Worksheet, CellAddress, FormulaNode> map)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            foreach (var entry in sheet.GetCells().Where(entry => entry.Cell.Formula is not null && !entry.Cell.IsSpilled).ToList())
            {
                var root = entry.Cell.Formula!.Root;
                var formulaSheet = sheet;
                var address = entry.Address;
                var rewritten = StructuredRewrite(root, node => map(node, formulaSheet, address));
                if (!ReferenceEquals(root, rewritten))
                {
                    SetCell(sheet, entry.Row, entry.Column, entry.Cell with { Formula = new Formula(rewritten) });
                }
            }
        }
    }

    private static FormulaNode StructuredRewrite(FormulaNode node, Func<StructuredReferenceNode, FormulaNode> map)
    {
        switch (node)
        {
            case StructuredReferenceNode structured:
                var mapped = map(structured);
                return mapped.Equals(structured) ? structured : mapped;
            case FunctionNode function:
                var arguments = function.Arguments.Select(argument => StructuredRewrite(argument, map)).ToImmutableArray();
                var callee = function.Callee is { } inner ? StructuredRewrite(inner, map) : null;
                return arguments.AsEnumerable().SequenceEqual(function.Arguments, ReferenceEqualityComparer.Instance) && ReferenceEquals(callee, function.Callee)
                    ? function
                    : function with { Arguments = arguments, Callee = callee };
            case BinaryNode binary:
                var left = StructuredRewrite(binary.Left, map);
                var right = StructuredRewrite(binary.Right, map);
                return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right) ? binary : binary with { Left = left, Right = right };
            case UnaryNode unary:
                var operand = StructuredRewrite(unary.Operand, map);
                return ReferenceEquals(operand, unary.Operand) ? unary : unary with { Operand = operand };
            case PercentNode percent:
                var percentOperand = StructuredRewrite(percent.Operand, map);
                return ReferenceEquals(percentOperand, percent.Operand) ? percent : percent with { Operand = percentOperand };
            case ParenthesesNode parentheses:
                var parenthesesInner = StructuredRewrite(parentheses.Inner, map);
                return ReferenceEquals(parenthesesInner, parentheses.Inner) ? parentheses : parentheses with { Inner = parenthesesInner };
            default:
                return node;
        }
    }

    private (Worksheet Sheet, TableDefinition Table) FindTable(string name)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            if (sheet.Tables.FirstOrDefault(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase)) is { } table)
            {
                return (sheet, table);
            }
        }

        throw new BusinessException($"Die Tabelle „{name}“ gibt es nicht.");
    }

    private void ReplaceTable(Worksheet sheet, TableDefinition table, TableDefinition changed)
    {
        ChangeSheetState(sheet, () =>
        {
            var index = sheet.Tables.FindIndex(entry => string.Equals(entry.Name, table.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                sheet.Tables[index] = changed;
            }
        });
        TablesChanged();
    }

    /// <summary>Structured references depend on the tables' ranges: the engine rebuilds its dependencies after the command (and its undo).</summary>
    private void TablesChanged() => Record(new ActionStep(static () => { }, static () => { }, structural: true));

    private static CellRange WithLastRow(CellRange range, int lastRow) => new(range.FirstRow, range.FirstColumn, lastRow, range.LastColumn);
}

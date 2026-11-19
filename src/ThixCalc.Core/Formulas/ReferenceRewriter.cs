using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formulas;

/// <summary>The outcome of transforming a referenced area: the new range (null = deleted → #REF!) and maybe a new sheet.</summary>
internal readonly record struct AreaResult(CellRange? Range, int? SheetId = null)
{
    public static AreaResult Deleted => new(null);
}

/// <summary>A structural change as it affects references: where areas and formula cells go.</summary>
internal abstract class ReferenceTransform
{
    /// <summary>Where a cell (a formula's own position) goes, or null when it is removed.</summary>
    public virtual (int SheetId, CellAddress Address)? MovePosition(int sheetId, CellAddress position) => (sheetId, position);

    public abstract AreaResult TransformArea(int sheetId, CellRange range, AreaKind kind);

    /// <summary>The sheet part of a reference after the change, or null when it is gone (#REF!).</summary>
    public virtual SheetReference? TransformSheet(SheetReference reference) => reference;
}

/// <summary>
/// Rows (or columns) inserted (count &gt; 0) or deleted (count &lt; 0) at <c>at</c> on one sheet, optionally only in a band of
/// columns (rows) – inserting/deleting cells with shift down/up (right/left). Excel's rules: references after the point move,
/// ranges that span it grow or shrink, references into deleted cells become #REF!.
/// </summary>
internal sealed class AxisShift(int sheetId, bool rows, int at, int count, int bandFirst = 0, int bandLast = int.MaxValue) : ReferenceTransform
{
    public int SheetId { get; } = sheetId;

    public bool Rows { get; } = rows;

    public int At { get; } = at;

    public int Count { get; } = count;

    public override (int SheetId, CellAddress Address)? MovePosition(int sheetId, CellAddress position)
    {
        if (sheetId != SheetId || !InBand(Rows ? position.Column : position.Row, Rows ? position.Column : position.Row))
        {
            return (sheetId, position);
        }

        var index = Rows ? position.Row : position.Column;
        var moved = Shift(index);
        if (moved is null)
        {
            return null;
        }

        return (sheetId, Rows ? position with { Row = moved.Value } : position with { Column = moved.Value });
    }

    public override AreaResult TransformArea(int sheetId, CellRange range, AreaKind kind)
    {
        if (sheetId != SheetId || (Rows && kind == AreaKind.Columns) || (!Rows && kind == AreaKind.Rows))
        {
            return new AreaResult(range);
        }

        var crossFirst = Rows ? range.FirstColumn : range.FirstRow;
        var crossLast = Rows ? range.LastColumn : range.LastRow;
        if (!InBand(crossFirst, crossLast))
        {
            return new AreaResult(range);
        }

        var first = Rows ? range.FirstRow : range.FirstColumn;
        var last = Rows ? range.LastRow : range.LastColumn;
        var limit = Rows ? SheetLimits.LastRow : SheetLimits.LastColumn;
        int newFirst, newLast;
        if (Count > 0)
        {
            // A range reaching the sheet's end stays there (A1:A1048576 does not move).
            if (first >= At && first + Count > limit)
            {
                return AreaResult.Deleted;
            }

            newFirst = first >= At ? first + Count : first;
            newLast = last == limit ? limit : last >= At ? Math.Min(last + Count, limit) : last;
        }
        else
        {
            var removed = -Count;
            var end = At + removed;
            newFirst = first < At ? first : first >= end ? first - removed : At;
            newLast = last == limit && first < At ? limit : last < At ? last : last >= end ? last - removed : At - 1;
            if (newFirst > newLast)
            {
                return AreaResult.Deleted;
            }
        }

        return new AreaResult(Rows
            ? new CellRange(newFirst, range.FirstColumn, newLast, range.LastColumn)
            : new CellRange(range.FirstRow, newFirst, range.LastRow, newLast));
    }

    private bool InBand(int first, int last) => first >= bandFirst && last <= bandLast;

    private int? Shift(int index)
    {
        if (Count > 0)
        {
            return index >= At ? index + Count : index;
        }

        var removed = -Count;
        if (index < At)
        {
            return index;
        }

        return index >= At + removed ? index - removed : null;
    }
}

/// <summary>A sheet is deleted: references to it become #REF!, 3D spans that start or end there shrink.</summary>
internal sealed class SheetDeletion(int sheetId, Workbook workbook) : ReferenceTransform
{
    public int SheetId { get; } = sheetId;

    public override (int SheetId, CellAddress Address)? MovePosition(int sheetId, CellAddress position) =>
        sheetId == SheetId ? null : (sheetId, position);

    public override AreaResult TransformArea(int sheetId, CellRange range, AreaKind kind) =>
        sheetId == SheetId ? AreaResult.Deleted : new AreaResult(range);

    public override SheetReference? TransformSheet(SheetReference reference)
    {
        if (!reference.Is3D)
        {
            return reference.FirstSheetId == SheetId ? null : reference;
        }

        var ids = workbook.Sheets.Select(sheet => sheet.Id).ToList();
        var start = ids.IndexOf(reference.FirstSheetId);
        var end = ids.IndexOf(reference.LastSheetId);
        if (start < 0 || end < 0)
        {
            return reference;
        }

        if (reference.FirstSheetId == SheetId)
        {
            return start + 1 <= end ? reference with { FirstSheetId = ids[start + 1] } : null;
        }

        if (reference.LastSheetId == SheetId)
        {
            return end - 1 >= start ? reference with { LastSheetId = ids[end - 1] } : null;
        }

        return reference;
    }
}

/// <summary>
/// Cells cut and pasted (moved): references to cells inside the source go with them; references to single cells that the
/// move overwrites become #REF!; formulas inside the source move and keep pointing to the same cells.
/// </summary>
internal sealed class RangeMove(int sourceSheetId, CellRange source, int targetSheetId, CellAddress target) : ReferenceTransform
{
    private readonly int _rows = target.Row - source.FirstRow;
    private readonly int _columns = target.Column - source.FirstColumn;

    public CellRange Target => source.Offset(_rows, _columns);

    public override (int SheetId, CellAddress Address)? MovePosition(int sheetId, CellAddress position)
    {
        if (sheetId == sourceSheetId && source.Contains(position))
        {
            return (targetSheetId, position.Offset(_rows, _columns));
        }

        return (sheetId, position);
    }

    public override AreaResult TransformArea(int sheetId, CellRange range, AreaKind kind)
    {
        if (kind is AreaKind.Rows or AreaKind.Columns)
        {
            return new AreaResult(range);
        }

        if (sheetId == sourceSheetId && source.Contains(range))
        {
            return new AreaResult(range.Offset(_rows, _columns), targetSheetId);
        }

        if (sheetId == targetSheetId && range.IsSingleCell && Target.Contains(range) && !(sheetId == sourceSheetId && source.Contains(range)))
        {
            return AreaResult.Deleted;
        }

        return new AreaResult(range);
    }
}

/// <summary>
/// Applies a structural change to every formula of a workbook (cells, names, conditional formats, validation) and to the
/// ranges the sheets carry (merges, conditional formats, validation, hyperlinks, notes, autofilter, tables). Unchanged
/// formulas keep their (shared) instances.
/// </summary>
internal static class ReferenceRewriter
{
    public static FormulaNode Rewrite(FormulaNode node, ReferenceTransform transform, int sheetId, CellAddress oldOrigin, int newSheetId, CellAddress newOrigin)
    {
        var context = new Context(transform, sheetId, oldOrigin, newSheetId, newOrigin);
        return context.Visit(node);
    }

    /// <summary>Rewrites a formula text (canonical) stored relative to <paramref name="oldOrigin"/>; null when it cannot be parsed.</summary>
    public static string? RewriteText(string text, Workbook workbook, ReferenceTransform transform, int sheetId, CellAddress oldOrigin, CellAddress newOrigin)
    {
        try
        {
            var node = FormulaParser.ParseNode(text, workbook, oldOrigin);
            var rewritten = Rewrite(node, transform, sheetId, oldOrigin, sheetId, newOrigin);
            return ReferenceEquals(node, rewritten) && oldOrigin == newOrigin ? text : FormulaFormatter.Format(rewritten, workbook, newOrigin);
        }
        catch (FormulaParseException)
        {
            return null;
        }
    }

    public static void ApplyToWorkbook(Workbook workbook, ReferenceTransform transform)
    {
        foreach (var sheet in workbook.Sheets)
        {
            RewriteCells(sheet, transform);
        }

        for (var index = 0; index < workbook.Names.Count; index++)
        {
            var name = workbook.Names[index];
            var scopeSheet = name.SheetId ?? workbook.Sheets.FirstOrDefault()?.Id ?? 0;
            var text = RewriteText(name.Formula, workbook, transform, scopeSheet, default, default);
            if (text is not null && text != name.Formula)
            {
                workbook.Names[index] = name with { Formula = text };
            }
        }

        foreach (var sheet in workbook.Sheets)
        {
            RewriteSheetRanges(sheet, transform);
        }
    }

    private static void RewriteCells(Worksheet sheet, ReferenceTransform transform)
    {
        var store = sheet.Store;
        for (var rowIndex = 0; rowIndex < store.RowCount; rowIndex++)
        {
            var row = store.RowKeyAt(rowIndex);
            var data = store.RowAt(rowIndex);
            for (var position = 0; position < data.Count; position++)
            {
                if (data.CellAt(position).Payload is not FormulaCell formula)
                {
                    continue;
                }

                var origin = new CellAddress(row, data.ColumnAt(position));
                var moved = transform.MovePosition(sheet.Id, origin);
                if (moved is null)
                {
                    continue;
                }

                var root = Rewrite(formula.Formula.Root, transform, sheet.Id, origin, moved.Value.SheetId, moved.Value.Address);
                if (!ReferenceEquals(root, formula.Formula.Root))
                {
                    formula.Formula = new Formula(root);
                }

                if (formula.ArrayRange is { } array)
                {
                    var result = transform.TransformArea(sheet.Id, array, AreaKind.Range);
                    formula.ArrayRange = result.Range;
                }
            }
        }
    }

    private static void RewriteSheetRanges(Worksheet sheet, ReferenceTransform transform)
    {
        CellRange? Map(CellRange range) => transform.TransformArea(sheet.Id, range, AreaKind.Range) is { Range: { } mapped, SheetId: null or _ } result
            && (result.SheetId is null || result.SheetId == sheet.Id) ? mapped : null;

        IReadOnlyList<CellRange> MapAll(IEnumerable<CellRange> ranges) => ranges.Select(Map).Where(range => range is not null).Select(range => range!.Value).ToList();

        var merges = MapAll(sheet.Merges.Ranges).Where(range => !range.IsSingleCell).ToList();
        sheet.Merges.Replace(merges);

        for (var index = sheet.ConditionalFormats.Count - 1; index >= 0; index--)
        {
            var format = sheet.ConditionalFormats[index];
            var ranges = MapAll(format.Ranges);
            if (ranges.Count == 0)
            {
                sheet.ConditionalFormats.RemoveAt(index);
                continue;
            }

            var newOrigin = ranges[0].First;
            var rules = format.Rules.Select(rule => rule.Formulas.Count == 0 ? rule : rule with
            {
                Formulas = rule.Formulas.Select(formula => RewriteText(formula, sheet.Workbook, transform, sheet.Id, format.Origin, newOrigin) ?? formula).ToList(),
                Xml = null,
            }).ToList();
            sheet.ConditionalFormats[index] = new ConditionalFormat(ranges, rules);
        }

        for (var index = sheet.Validations.Count - 1; index >= 0; index--)
        {
            var validation = sheet.Validations[index];
            var ranges = MapAll(validation.Ranges);
            if (ranges.Count == 0)
            {
                sheet.Validations.RemoveAt(index);
                continue;
            }

            var newOrigin = ranges[0].First;
            sheet.Validations[index] = validation with
            {
                Ranges = ranges,
                Formula1 = validation.Formula1 is { } first ? RewriteText(first, sheet.Workbook, transform, sheet.Id, validation.Origin, newOrigin) ?? first : null,
                Formula2 = validation.Formula2 is { } second ? RewriteText(second, sheet.Workbook, transform, sheet.Id, validation.Origin, newOrigin) ?? second : null,
            };
        }

        for (var index = sheet.Hyperlinks.Count - 1; index >= 0; index--)
        {
            if (Map(sheet.Hyperlinks[index].Range) is { } range)
            {
                sheet.Hyperlinks[index] = sheet.Hyperlinks[index] with { Range = range };
            }
            else
            {
                sheet.Hyperlinks.RemoveAt(index);
            }
        }

        if (sheet.Comments.Count > 0)
        {
            var comments = sheet.Comments.ToList();
            sheet.Comments.Clear();
            foreach (var (address, comment) in comments)
            {
                if (transform.MovePosition(sheet.Id, address) is { } moved && moved.SheetId == sheet.Id)
                {
                    sheet.Comments[moved.Address] = comment;
                }
            }
        }

        if (sheet.AutoFilter is { } filter)
        {
            sheet.AutoFilter = Map(filter.Range) is { } range ? filter with { Range = range } : null;
        }

        for (var index = 0; index < sheet.Tables.Count; index++)
        {
            if (Map(sheet.Tables[index].Range) is { } range)
            {
                sheet.Tables[index] = sheet.Tables[index] with { Range = range };
            }
        }
    }

    private sealed class Context(ReferenceTransform transform, int sheetId, CellAddress oldOrigin, int newSheetId, CellAddress newOrigin)
    {
        public FormulaNode Visit(FormulaNode node)
        {
            switch (node)
            {
                case ReferenceNode reference:
                    return VisitReference(reference);
                case ReferenceErrorNode error when error.Sheet is { } errorSheet:
                    var errorTarget = transform.TransformSheet(errorSheet);
                    return errorTarget == errorSheet ? node : new ReferenceErrorNode(errorTarget);
                case NameNode { SheetId: { } nameSheet } name:
                    var check = transform.TransformSheet(SheetReference.Single(nameSheet));
                    return check is null ? new ErrorNode(CellError.Reference) : name;
                case FunctionNode function:
                {
                    var arguments = function.Arguments;
                    var builder = arguments.ToBuilder();
                    var changed = false;
                    for (var index = 0; index < arguments.Length; index++)
                    {
                        var visited = Visit(arguments[index]);
                        if (!ReferenceEquals(visited, arguments[index]))
                        {
                            builder[index] = visited;
                            changed = true;
                        }
                    }

                    var callee = function.Callee is null ? null : Visit(function.Callee);
                    if (!changed && ReferenceEquals(callee, function.Callee))
                    {
                        return node;
                    }

                    return function with { Arguments = builder.ToImmutable(), Callee = callee };
                }

                case UnaryNode unary:
                {
                    var operand = Visit(unary.Operand);
                    return ReferenceEquals(operand, unary.Operand) ? node : unary with { Operand = operand };
                }

                case PercentNode percent:
                {
                    var operand = Visit(percent.Operand);
                    return ReferenceEquals(operand, percent.Operand) ? node : percent with { Operand = operand };
                }

                case ParenthesesNode parentheses:
                {
                    var inner = Visit(parentheses.Inner);
                    return ReferenceEquals(inner, parentheses.Inner) ? node : parentheses with { Inner = inner };
                }

                case BinaryNode binary:
                {
                    var left = Visit(binary.Left);
                    var right = Visit(binary.Right);
                    return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right) ? node : binary with { Left = left, Right = right };
                }

                default:
                    return node;
            }
        }

        private FormulaNode VisitReference(ReferenceNode reference)
        {
            var sheetReference = reference.Sheet;
            if (sheetReference is { IsMissing: true })
            {
                return reference;
            }

            if (sheetReference is not null)
            {
                var transformed = transform.TransformSheet(sheetReference);
                if (transformed is null)
                {
                    return new ReferenceErrorNode(null);
                }

                if (transformed.Is3D)
                {
                    return transformed == sheetReference ? reference : reference with { Sheet = transformed };
                }

                sheetReference = transformed;
            }

            var targetSheet = sheetReference?.FirstSheetId ?? sheetId;
            var range = reference.Area.Resolve(oldOrigin);
            if (range is null)
            {
                return reference;
            }

            var result = transform.TransformArea(targetSheet, range.Value, reference.Area.Kind);
            if (result.Range is not { } mapped)
            {
                return new ReferenceErrorNode(reference.Sheet);
            }

            var resultSheet = result.SheetId ?? targetSheet;
            SheetReference? newSheetReference = sheetReference;
            if (sheetReference is null && resultSheet != newSheetId)
            {
                newSheetReference = SheetReference.Single(resultSheet);
            }
            else if (sheetReference is not null && resultSheet != sheetReference.FirstSheetId)
            {
                newSheetReference = resultSheet == newSheetId ? null : SheetReference.Single(resultSheet);
            }

            var area = Encode(reference.Area, mapped);
            var rewritten = new ReferenceNode(newSheetReference, area);
            return rewritten == reference ? reference : rewritten;
        }

        private AreaReference Encode(AreaReference area, CellRange range)
        {
            switch (area.Kind)
            {
                case AreaKind.Rows:
                    return new AreaReference(
                        new CellReference(area.First.RowAbsolute ? range.FirstRow : range.FirstRow - newOrigin.Row, 0, area.First.RowAbsolute, true),
                        new CellReference(area.Last.RowAbsolute ? range.LastRow : range.LastRow - newOrigin.Row, SheetLimits.LastColumn, area.Last.RowAbsolute, true),
                        AreaKind.Rows);
                case AreaKind.Columns:
                    return new AreaReference(
                        new CellReference(0, area.First.ColumnAbsolute ? range.FirstColumn : range.FirstColumn - newOrigin.Column, true, area.First.ColumnAbsolute),
                        new CellReference(SheetLimits.LastRow, area.Last.ColumnAbsolute ? range.LastColumn : range.LastColumn - newOrigin.Column, true, area.Last.ColumnAbsolute),
                        AreaKind.Columns);
                case AreaKind.Cell:
                    return AreaReference.Cell(CellReference.To(range.First, newOrigin, area.First.RowAbsolute, area.First.ColumnAbsolute));
                default:
                    return new AreaReference(
                        CellReference.To(range.First, newOrigin, area.First.RowAbsolute, area.First.ColumnAbsolute),
                        CellReference.To(range.Last, newOrigin, area.Last.RowAbsolute, area.Last.ColumnAbsolute),
                        AreaKind.Range);
            }
        }
    }
}

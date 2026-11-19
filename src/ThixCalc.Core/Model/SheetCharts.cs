using ThixCalc.Core.Formulas;
using ThixOffice.Charts;

namespace ThixCalc.Core.Model;

/// <summary>
/// Where a chart floats over the sheet, as Excel anchors it (xdr:twoCellAnchor): its top-left corner in cell
/// <see cref="From"/> plus an offset, its bottom-right corner in <see cref="To"/> plus an offset (points). It moves and
/// grows with the cells under it.
/// </summary>
public sealed record ChartAnchor(CellAddress From, double FromX, double FromY, CellAddress To, double ToX, double ToY)
{
    /// <summary>The cells the chart covers (printing, the used range).</summary>
    public CellRange Cells => new(From.Row, From.Column, To.Row, To.Column);
}

/// <summary>
/// A chart on a sheet: an id unique in the workbook session, its anchor, the chart, and its name ("Diagramm 1"). What it was
/// read from (the chart part, the chart as read) lets the writer keep an unchanged chart's part byte for byte.
/// </summary>
public sealed record SheetChart(int Id, ChartAnchor Anchor, Chart Chart)
{
    public string Name { get; init; } = "Diagramm " + Id;

    /// <summary>The chart part it was read from (relative to the drawing part), or null for a new chart.</summary>
    internal string? PartUri { get; init; }

    /// <summary>The chart as read: equal (same instance) = the part is written back unchanged.</summary>
    internal Chart? Loaded { get; init; }

    /// <summary>The anchor element as read (a chart ThixCalc cannot read – chartex – is written back with it).</summary>
    internal string? AnchorXml { get; init; }

    /// <summary>The drawing object id (cNvPr id) as read.</summary>
    internal int DrawingId { get; init; }
}

/// <summary>Chart references in the sheet's terms: resolving a reference text to cells, and following a sheet rename.</summary>
public static class ChartReferences
{
    /// <summary>The cells a reference text ("Tabelle1!$B$2:$B$13", "(Tabelle1!$A$1,Tabelle1!$A$3)", a name) points to, in order.</summary>
    public static IReadOnlyList<(Worksheet Sheet, CellRange Range)> Resolve(Workbook workbook, string formula)
    {
        var result = new List<(Worksheet, CellRange)>();
        try
        {
            var node = FormulaParser.ParseNode(formula.TrimStart('='), workbook, default);
            Collect(workbook, node, result, depth: 0);
        }
        catch (FormulaParseException)
        {
            // An unreadable reference shows the cached values.
        }

        return result;
    }

    private static void Collect(Workbook workbook, FormulaNode node, List<(Worksheet, CellRange)> result, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        switch (node)
        {
            case ReferenceNode { Sheet: { } sheetReference } reference when workbook.GetSheetById(sheetReference.FirstSheetId) is { } sheet && reference.Area.Resolve(default) is { } range:
                result.Add((sheet, range));
                break;
            case ParenthesesNode parentheses:
                Collect(workbook, parentheses.Inner, result, depth + 1);
                break;
            case BinaryNode { Operator: BinaryOperator.Union } union:
                Collect(workbook, union.Left, result, depth + 1);
                Collect(workbook, union.Right, result, depth + 1);
                break;
            case BinaryNode { Operator: BinaryOperator.Range } span:
                var parts = new List<(Worksheet, CellRange)>();
                Collect(workbook, span.Left, parts, depth + 1);
                Collect(workbook, span.Right, parts, depth + 1);
                if (parts.Count == 2 && parts[0].Item1 == parts[1].Item1)
                {
                    result.Add((parts[0].Item1, parts[0].Item2.Union(parts[1].Item2)));
                }

                break;
            case NameNode name when workbook.FindName(name.Name, name.SheetId is { } id ? workbook.GetSheetById(id) : null) is { } defined:
                try
                {
                    Collect(workbook, FormulaParser.ParseNode(defined.Formula.TrimStart('='), workbook, default), result, depth + 1);
                }
                catch (FormulaParseException)
                {
                    // Not a reference.
                }

                break;
        }
    }

    /// <summary>The reference text of a range ("Tabelle1!$B$2:$B$13").</summary>
    public static string Of(Worksheet sheet, CellRange range) => FormulaFormatter.FormatRange(sheet, range);

    /// <summary>A reference text after a sheet was renamed: every "Alt!" (quoted as needed) becomes "Neu!".</summary>
    public static string Rename(string formula, string oldName, string newName)
    {
        var oldPrefix = FormulaFormatter.QuoteSheetName(oldName) + "!";
        var newPrefix = FormulaFormatter.QuoteSheetName(newName) + "!";
        if (!formula.Contains(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return formula;
        }

        var builder = new System.Text.StringBuilder(formula.Length + 8);
        var index = 0;
        while (index < formula.Length)
        {
            var atBoundary = index == 0 || formula[index - 1] is '(' or ',' or ' ' or '=';
            if (atBoundary && string.Compare(formula, index, oldPrefix, 0, oldPrefix.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                builder.Append(newPrefix);
                index += oldPrefix.Length;
                continue;
            }

            builder.Append(formula[index]);
            index++;
        }

        return builder.ToString();
    }
}

using System.Text;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// Writes a formula as text for a cell position in a syntax: canonical (stored), the file format (with _xlfn./_xlpm.
/// prefixes) or a user's locale (SUMME(A1;B1), decimal comma, Z1S1). Without the leading "=".
/// </summary>
public static class FormulaFormatter
{
    public static string Format(Formula formula, Workbook? workbook, CellAddress origin, FormulaSyntax? syntax = null) =>
        Format(formula.Root, workbook, origin, syntax);

    public static string Format(FormulaNode node, Workbook? workbook, CellAddress origin, FormulaSyntax? syntax = null)
    {
        var writer = new Writer(workbook, origin, syntax ?? FormulaSyntax.Canonical);
        writer.Write(node, 0);
        return writer.ToString();
    }

    /// <summary>A sheet name as a reference writes it ('Mein Blatt' quoted when needed).</summary>
    public static string QuoteSheetName(string name) => NeedsQuotes(name) ? "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'" : name;

    /// <summary>"Tabelle1!$A$1:$B$3" – a range with its sheet, absolute, for names and dialogs.</summary>
    public static string FormatRange(Worksheet sheet, CellRange range, bool absolute = true)
    {
        var reference = new ReferenceNode(
            SheetReference.Single(sheet.Id),
            new AreaReference(
                new CellReference(range.FirstRow, range.FirstColumn, absolute, absolute),
                new CellReference(range.LastRow, range.LastColumn, absolute, absolute),
                range.IsSingleCell ? AreaKind.Cell : range.IsWholeColumns && !range.IsWholeRows ? AreaKind.Columns : range.IsWholeRows && !range.IsWholeColumns ? AreaKind.Rows : AreaKind.Range));
        return Format(reference, sheet.Workbook, default);
    }

    private static bool NeedsQuotes(string name)
    {
        if (name.Length == 0 || char.IsAsciiDigit(name[0]))
        {
            return true;
        }

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character is not '_' and not '.')
            {
                return true;
            }
        }

        var index = 0;
        if (A1.TryReadCell(name, ref index, out _, out _, out _) && index == name.Length)
        {
            return true;
        }

        // R, C, R1, C1, R1C1 (and the German Z/S forms) would read as R1C1 references.
        return R1C1Like.IsMatch(name);
    }

    private static readonly System.Text.RegularExpressions.Regex R1C1Like =
        new("^(?:[RZ][0-9]*)?(?:[CS][0-9]*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static int Precedence(FormulaNode node) => node switch
    {
        BinaryNode binary => binary.Operator switch
        {
            BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.Less or BinaryOperator.LessOrEqual
                or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => 1,
            BinaryOperator.Concatenate => 2,
            BinaryOperator.Add or BinaryOperator.Subtract => 3,
            BinaryOperator.Multiply or BinaryOperator.Divide => 4,
            BinaryOperator.Power => 5,
            BinaryOperator.Union => 8,
            BinaryOperator.Intersect => 9,
            _ => 10,
        },
        UnaryNode { Operator: UnaryOperator.ImplicitIntersection } => 11,
        UnaryNode => 6,
        PercentNode => 7,
        _ => 12,
    };

    private sealed class Writer(Workbook? workbook, CellAddress origin, FormulaSyntax syntax)
    {
        private readonly StringBuilder _builder = new();
        private readonly CalcLocale _locale = syntax.Locale;
        private readonly HashSet<string> _bound = new(StringComparer.OrdinalIgnoreCase);

        public override string ToString() => _builder.ToString();

        public void Write(FormulaNode node, int parentPrecedence)
        {
            var own = Precedence(node);
            var wrap = own < parentPrecedence && node is BinaryNode && ((BinaryNode)node).Operator != BinaryOperator.Union;
            if (node is BinaryNode { Operator: BinaryOperator.Union } && parentPrecedence > 0)
            {
                wrap = true;
            }

            if (wrap)
            {
                _builder.Append('(');
            }

            WriteNode(node, own);
            if (wrap)
            {
                _builder.Append(')');
            }
        }

        private void WriteNode(FormulaNode node, int own)
        {
            switch (node)
            {
                case NumberNode number:
                    _builder.Append(_locale.FormatNumberLiteral(number.Value));
                    break;
                case TextNode text:
                    WriteString(text.Value);
                    break;
                case BooleanNode boolean:
                    _builder.Append(_locale.BooleanName(boolean.Value));
                    break;
                case ErrorNode error:
                    _builder.Append(_locale.ErrorName(error.Error));
                    break;
                case MissingArgumentNode:
                    break;
                case ReferenceNode reference:
                    WriteSheet(reference.Sheet);
                    WriteArea(reference.Area);
                    break;
                case ReferenceErrorNode referenceError:
                    WriteSheet(referenceError.Sheet);
                    _builder.Append(_locale.ErrorName(CellError.Reference));
                    break;
                case NameNode name:
                    if (name.SheetId is { } sheetId)
                    {
                        WriteSheet(SheetReference.Single(sheetId));
                    }

                    if (syntax.IsFileFormat && _bound.Contains(name.Name))
                    {
                        _builder.Append("_xlpm.");
                    }

                    _builder.Append(name.Name);
                    break;
                case FunctionNode function:
                    WriteFunction(function);
                    break;
                case UnaryNode unary:
                    _builder.Append(unary.Operator switch
                    {
                        UnaryOperator.Minus => '-',
                        UnaryOperator.Plus => '+',
                        _ => '@',
                    });
                    Write(unary.Operand, own);
                    break;
                case PercentNode percent:
                    Write(percent.Operand, own);
                    _builder.Append('%');
                    break;
                case BinaryNode binary:
                    WriteBinary(binary, own);
                    break;
                case ParenthesesNode parentheses:
                    _builder.Append('(');
                    Write(parentheses.Inner, 0);
                    _builder.Append(')');
                    break;
                case ArrayNode array:
                    WriteArray(array);
                    break;
                case StructuredReferenceNode structured:
                    _builder.Append(structured.TableName).Append(structured.Specifier);
                    break;
                case ExternalReferenceNode external:
                    _builder.Append(external.Text);
                    break;
            }
        }

        private void WriteBinary(BinaryNode binary, int own)
        {
            // Left-associative: the right operand needs parentheses at equal precedence.
            Write(binary.Left, binary.Operator == BinaryOperator.Union ? 0 : own);
            _builder.Append(binary.Operator switch
            {
                BinaryOperator.Add => "+",
                BinaryOperator.Subtract => "-",
                BinaryOperator.Multiply => "*",
                BinaryOperator.Divide => "/",
                BinaryOperator.Power => "^",
                BinaryOperator.Concatenate => "&",
                BinaryOperator.Equal => "=",
                BinaryOperator.NotEqual => "<>",
                BinaryOperator.Less => "<",
                BinaryOperator.LessOrEqual => "<=",
                BinaryOperator.Greater => ">",
                BinaryOperator.GreaterOrEqual => ">=",
                BinaryOperator.Range => ":",
                BinaryOperator.Intersect => " ",
                _ => _locale.ListSeparator.ToString(),
            });
            Write(binary.Right, binary.Operator == BinaryOperator.Union ? 0 : own + 1);
        }

        private void WriteFunction(FunctionNode function)
        {
            if (!syntax.IsFileFormat && function.Function?.Name == "ANCHORARRAY" && function.Arguments is [ReferenceNode anchor])
            {
                Write(anchor, 12);
                _builder.Append('#');
                return;
            }

            if (function.Callee is not null)
            {
                Write(function.Callee, 12);
            }
            else if (function.Function is { } definition)
            {
                if (syntax.IsFileFormat && definition.FilePrefix is { } prefix)
                {
                    _builder.Append(prefix);
                }

                _builder.Append(syntax.IsFileFormat ? definition.Name : _locale.FunctionName(definition.Name));
            }
            else
            {
                var name = function.Name;
                if (!syntax.IsFileFormat && name.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[6..];
                }

                _builder.Append(name);
            }

            var binding = BoundNames(function);
            foreach (var name in binding)
            {
                _bound.Add(name);
            }

            _builder.Append('(');
            for (var index = 0; index < function.Arguments.Length; index++)
            {
                if (index > 0)
                {
                    _builder.Append(_locale.ListSeparator);
                }

                Write(function.Arguments[index], 0);
            }

            _builder.Append(')');
            foreach (var name in binding)
            {
                _bound.Remove(name);
            }
        }

        private static List<string> BoundNames(FunctionNode function)
        {
            var names = new List<string>();
            if (function.Function?.Name == "LET")
            {
                for (var index = 0; index + 1 < function.Arguments.Length; index += 2)
                {
                    if (function.Arguments[index] is NameNode name)
                    {
                        names.Add(name.Name);
                    }
                }
            }
            else if (function.Function?.Name == "LAMBDA")
            {
                for (var index = 0; index + 1 < function.Arguments.Length; index++)
                {
                    if (function.Arguments[index] is NameNode name)
                    {
                        names.Add(name.Name);
                    }
                }
            }

            return names;
        }

        private void WriteString(string value)
        {
            _builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        private void WriteArray(ArrayNode array)
        {
            _builder.Append('{');
            for (var row = 0; row < array.Rows; row++)
            {
                if (row > 0)
                {
                    _builder.Append(_locale.ArrayRowSeparator);
                }

                for (var column = 0; column < array.Columns; column++)
                {
                    if (column > 0)
                    {
                        _builder.Append(_locale.ArrayColumnSeparator);
                    }

                    var value = array[row, column];
                    switch (value.Kind)
                    {
                        case CellValueKind.Number:
                            _builder.Append(_locale.FormatNumberLiteral(value.Number));
                            break;
                        case CellValueKind.Text:
                            WriteString(value.Text);
                            break;
                        case CellValueKind.Boolean:
                            _builder.Append(_locale.BooleanName(value.Boolean));
                            break;
                        case CellValueKind.Error:
                            _builder.Append(_locale.ErrorName(value.Error));
                            break;
                    }
                }
            }

            _builder.Append('}');
        }

        private void WriteSheet(SheetReference? sheet)
        {
            if (sheet is null)
            {
                return;
            }

            if (sheet.MissingName is { } missing)
            {
                _builder.Append(QuoteSheetName(missing)).Append('!');
                return;
            }

            var first = workbook?.GetSheetById(sheet.FirstSheetId)?.Name ?? "#REF";
            if (!sheet.Is3D)
            {
                _builder.Append(QuoteSheetName(first)).Append('!');
                return;
            }

            var last = workbook?.GetSheetById(sheet.LastSheetId)?.Name ?? "#REF";
            if (NeedsQuotes(first) || NeedsQuotes(last))
            {
                _builder.Append('\'').Append(first.Replace("'", "''", StringComparison.Ordinal)).Append(':')
                    .Append(last.Replace("'", "''", StringComparison.Ordinal)).Append("'!");
            }
            else
            {
                _builder.Append(first).Append(':').Append(last).Append('!');
            }
        }

        private void WriteArea(AreaReference area)
        {
            if (syntax.Style == ReferenceStyle.R1C1)
            {
                WriteR1C1Area(area);
                return;
            }

            switch (area.Kind)
            {
                case AreaKind.Cell:
                    WriteCell(area.First);
                    break;
                case AreaKind.Range:
                    WriteCell(area.First);
                    _builder.Append(':');
                    WriteCell(area.Last);
                    break;
                case AreaKind.Rows:
                    WriteRow(area.First);
                    _builder.Append(':');
                    WriteRow(area.Last);
                    break;
                case AreaKind.Columns:
                    WriteColumn(area.First);
                    _builder.Append(':');
                    WriteColumn(area.Last);
                    break;
            }
        }

        private void WriteCell(CellReference reference)
        {
            var row = reference.RowAt(origin.Row);
            var column = reference.ColumnAt(origin.Column);
            if (row is < 0 or >= SheetLimits.MaxRows || column is < 0 or >= SheetLimits.MaxColumns)
            {
                _builder.Append(_locale.ErrorName(CellError.Reference));
                return;
            }

            WriteColumn(reference);
            WriteRow(reference);
        }

        private void WriteRow(CellReference reference)
        {
            if (reference.RowAbsolute)
            {
                _builder.Append('$');
            }

            _builder.Append(reference.RowAt(origin.Row) + 1);
        }

        private void WriteColumn(CellReference reference)
        {
            if (reference.ColumnAbsolute)
            {
                _builder.Append('$');
            }

            _builder.Append(A1.ColumnName(Math.Clamp(reference.ColumnAt(origin.Column), 0, SheetLimits.LastColumn)));
        }

        private void WriteR1C1Area(AreaReference area)
        {
            switch (area.Kind)
            {
                case AreaKind.Cell:
                    WriteR1C1Part(_locale.RowLetter, area.First.Row, area.First.RowAbsolute);
                    WriteR1C1Part(_locale.ColumnLetter, area.First.Column, area.First.ColumnAbsolute);
                    break;
                case AreaKind.Range:
                    WriteR1C1Part(_locale.RowLetter, area.First.Row, area.First.RowAbsolute);
                    WriteR1C1Part(_locale.ColumnLetter, area.First.Column, area.First.ColumnAbsolute);
                    _builder.Append(':');
                    WriteR1C1Part(_locale.RowLetter, area.Last.Row, area.Last.RowAbsolute);
                    WriteR1C1Part(_locale.ColumnLetter, area.Last.Column, area.Last.ColumnAbsolute);
                    break;
                case AreaKind.Rows:
                    WriteR1C1Part(_locale.RowLetter, area.First.Row, area.First.RowAbsolute);
                    if (area.First.Row != area.Last.Row || area.First.RowAbsolute != area.Last.RowAbsolute)
                    {
                        _builder.Append(':');
                        WriteR1C1Part(_locale.RowLetter, area.Last.Row, area.Last.RowAbsolute);
                    }

                    break;
                case AreaKind.Columns:
                    WriteR1C1Part(_locale.ColumnLetter, area.First.Column, area.First.ColumnAbsolute);
                    if (area.First.Column != area.Last.Column || area.First.ColumnAbsolute != area.Last.ColumnAbsolute)
                    {
                        _builder.Append(':');
                        WriteR1C1Part(_locale.ColumnLetter, area.Last.Column, area.Last.ColumnAbsolute);
                    }

                    break;
            }
        }

        private void WriteR1C1Part(char letter, int value, bool absolute)
        {
            _builder.Append(letter);
            if (absolute)
            {
                _builder.Append(value + 1);
            }
            else if (value != 0)
            {
                _builder.Append('[').Append(value).Append(']');
            }
        }
    }
}

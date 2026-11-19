using System.Globalization;
using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>
/// Formulas between ThixCalc's model and OpenFormula (ODFF, "of:=SUM([.A1:.B2])"): references in brackets with the sheet before a
/// dot ([$Tabelle2.A1:.B3], [.A:.A], [.1:.3]), ";" between arguments, "~" (union) and "!" (intersection), array constants
/// {1;2|3;4}, TRUE()/FALSE(). Functions keep their English names; Excel's newer ones (written "_xlfn." in xlsx) are
/// "COM.MICROSOFT.…" unless OpenFormula has them itself, the old distributions "LEGACY.…". Writing works on the parsed formula
/// (exact); reading translates the text into the canonical form and parses that – a formula it cannot translate (LibreOffice-only
/// functions, external references, unions) gives null, and the cell keeps its value.
/// </summary>
internal static class OdsFormulas
{
    /// <summary>Excel's newer functions that OpenFormula has under the same name (no COM.MICROSOFT. prefix).</summary>
    private static readonly HashSet<string> OpenFormulaNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACOT", "ACOTH", "ARABIC", "BASE", "BITAND", "BITLSHIFT", "BITOR", "BITRSHIFT", "BITXOR", "COMBINA", "COT", "COTH", "CSC", "CSCH",
        "DAYS", "DECIMAL", "GAMMA", "GAUSS", "IFNA", "ISFORMULA", "ISOWEEKNUM", "NUMBERVALUE", "PDURATION", "PERMUTATIONA", "PHI",
        "RRI", "SEC", "SECH", "SHEET", "SHEETS", "UNICHAR", "UNICODE", "XOR",
    };

    /// <summary>Names that differ (Excel → OpenFormula).</summary>
    private static readonly Dictionary<string, string> ToOpenFormulaName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FORMULATEXT"] = "FORMULA",
        ["CHIDIST"] = "LEGACY.CHIDIST",
        ["CHIINV"] = "LEGACY.CHIINV",
        ["CHITEST"] = "LEGACY.CHITEST",
        ["FDIST"] = "LEGACY.FDIST",
        ["FINV"] = "LEGACY.FINV",
        ["NORMSDIST"] = "LEGACY.NORMSDIST",
        ["NORMSINV"] = "LEGACY.NORMSINV",
        ["TDIST"] = "LEGACY.TDIST",
        ["CEILING"] = "COM.MICROSOFT.CEILING",
        ["FLOOR"] = "COM.MICROSOFT.FLOOR",
    };

    private static readonly Dictionary<string, string> FromOpenFormulaName =
        ToOpenFormulaName.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- model → OpenFormula

    /// <summary>"of:=…" of a formula at <paramref name="origin"/> on <paramref name="sheet"/>, or null when OpenFormula cannot express it.</summary>
    public static string? Write(Formula formula, Worksheet sheet, CellAddress origin) =>
        Expression(formula.Root, sheet.Workbook, sheet, origin) is { } text ? "of:=" + text : null;

    /// <summary>The OpenFormula expression (without "of:=") of a node, or null.</summary>
    public static string? Expression(FormulaNode node, Workbook workbook, Worksheet? sheet, CellAddress origin)
    {
        var writer = new Writer(workbook, sheet, origin);
        return writer.Write(node, 0) ? writer.ToString() : null;
    }

    /// <summary>The OpenFormula expression of a canonical formula text (a name, a rule), or null.</summary>
    public static string? Expression(string canonical, Workbook workbook, Worksheet? sheet, CellAddress origin)
    {
        try
        {
            return Expression(FormulaParser.ParseNode(canonical, workbook, origin), workbook, sheet, origin);
        }
        catch (FormulaParseException)
        {
            return null;
        }
    }

    /// <summary>"Tabelle1.A1:Tabelle1.D10" (a target range address of ODF), sheet names quoted when needed.</summary>
    public static string RangeAddress(Worksheet sheet, CellRange range)
    {
        var name = QuoteSheet(sheet.Name);
        return range.IsSingleCell ? $"{name}.{range.First}" : $"{name}.{range.First}:{name}.{range.Last}";
    }

    /// <summary>An ODF sheet name in a reference: quoted with ' when it has anything but letters, digits and "_".</summary>
    public static string QuoteSheet(string name) =>
        name.Length > 0 && name.All(character => char.IsLetterOrDigit(character) || character == '_') && !char.IsAsciiDigit(name[0])
            ? name
            : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";

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
        UnaryNode => 6,
        PercentNode => 7,
        _ => 12,
    };

    private sealed class Writer(Workbook workbook, Worksheet? sheet, CellAddress origin)
    {
        private readonly StringBuilder _builder = new();

        public override string ToString() => _builder.ToString();

        public bool Write(FormulaNode node, int parentPrecedence)
        {
            var own = Precedence(node);
            var wrap = node is BinaryNode binary && (binary.Operator == BinaryOperator.Union ? parentPrecedence > 0 : own < parentPrecedence);
            if (wrap)
            {
                _builder.Append('(');
            }

            var written = WriteNode(node, own);
            if (wrap)
            {
                _builder.Append(')');
            }

            return written;
        }

        private bool WriteNode(FormulaNode node, int own)
        {
            switch (node)
            {
                case NumberNode number:
                    _builder.Append(number.Value.ToString("R", CultureInfo.InvariantCulture));
                    return true;
                case TextNode text:
                    WriteString(text.Value);
                    return true;
                case BooleanNode boolean:
                    _builder.Append(boolean.Value ? "TRUE()" : "FALSE()");
                    return true;
                case ErrorNode error:
                    _builder.Append(CellErrors.NameOf(error.Error));
                    return true;
                case MissingArgumentNode:
                    return true;
                case ReferenceNode reference:
                    return WriteReference(reference);
                case ReferenceErrorNode:
                    _builder.Append("#REF!");
                    return true;
                case NameNode name:
                    _builder.Append(name.Name);
                    return true;
                case FunctionNode function:
                    return WriteFunction(function);
                case UnaryNode { Operator: UnaryOperator.ImplicitIntersection } unary:
                    return Write(unary.Operand, own);
                case UnaryNode unary:
                    _builder.Append(unary.Operator == UnaryOperator.Minus ? '-' : '+');
                    return Write(unary.Operand, own);
                case PercentNode percent:
                    var written = Write(percent.Operand, own);
                    _builder.Append('%');
                    return written;
                case BinaryNode binary:
                    if (!Write(binary.Left, binary.Operator == BinaryOperator.Union ? 0 : own))
                    {
                        return false;
                    }

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
                        BinaryOperator.Intersect => "!",
                        _ => "~",
                    });
                    return Write(binary.Right, binary.Operator == BinaryOperator.Union ? 0 : own + 1);
                case ParenthesesNode parentheses:
                    _builder.Append('(');
                    var inner = Write(parentheses.Inner, 0);
                    _builder.Append(')');
                    return inner;
                case ArrayNode array:
                    WriteArray(array);
                    return true;
                case StructuredReferenceNode structured:
                    return WriteStructured(structured);
                default:
                    // External references, anything new: OpenFormula has no place for it here.
                    return false;
            }
        }

        private bool WriteFunction(FunctionNode function)
        {
            if (function.Callee is not null || function.Function?.Name == "ANCHORARRAY")
            {
                return false;
            }

            var name = function.Function?.Name ?? function.Name;
            if (name.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase))
            {
                name = name[6..];
            }

            if (name.StartsWith("_xlws.", StringComparison.OrdinalIgnoreCase))
            {
                name = name[6..];
            }

            if (ToOpenFormulaName.TryGetValue(name, out var mapped))
            {
                name = mapped;
            }
            else if (function.Function?.FilePrefix is not null && !OpenFormulaNames.Contains(name))
            {
                name = "COM.MICROSOFT." + name;
            }

            _builder.Append(name).Append('(');
            for (var index = 0; index < function.Arguments.Length; index++)
            {
                if (index > 0)
                {
                    _builder.Append(';');
                }

                if (!Write(function.Arguments[index], 0))
                {
                    return false;
                }
            }

            _builder.Append(')');
            return true;
        }

        private bool WriteReference(ReferenceNode reference)
        {
            string? firstSheet = null;
            string? lastSheet = null;
            if (reference.Sheet is { } sheetReference)
            {
                if (sheetReference.MissingName is { } missing)
                {
                    firstSheet = missing;
                }
                else
                {
                    firstSheet = workbook.GetSheetById(sheetReference.FirstSheetId)?.Name;
                    lastSheet = sheetReference.Is3D ? workbook.GetSheetById(sheetReference.LastSheetId)?.Name : null;
                    if (firstSheet is null || (sheetReference.Is3D && lastSheet is null))
                    {
                        _builder.Append("#REF!");
                        return true;
                    }
                }
            }

            var area = reference.Area;
            if ((area.Kind is AreaKind.Cell or AreaKind.Range && !InSheet(area.First)) || (area.Kind == AreaKind.Range && !InSheet(area.Last)))
            {
                _builder.Append("#REF!");
                return true;
            }

            _builder.Append('[');
            AppendSheet(firstSheet);
            switch (area.Kind)
            {
                case AreaKind.Cell:
                    AppendCell(area.First);
                    if (lastSheet is not null)
                    {
                        _builder.Append(':');
                        AppendSheet(lastSheet);
                        AppendCell(area.First);
                    }

                    break;
                case AreaKind.Range:
                    AppendCell(area.First);
                    _builder.Append(':');
                    AppendSheet(lastSheet);
                    AppendCell(area.Last);
                    break;
                case AreaKind.Rows:
                    AppendRow(area.First);
                    _builder.Append(':');
                    AppendSheet(lastSheet);
                    AppendRow(area.Last);
                    break;
                case AreaKind.Columns:
                    AppendColumn(area.First);
                    _builder.Append(':');
                    AppendSheet(lastSheet);
                    AppendColumn(area.Last);
                    break;
            }

            _builder.Append(']');
            return true;
        }

        private bool InSheet(CellReference reference)
        {
            var row = reference.RowAt(origin.Row);
            var column = reference.ColumnAt(origin.Column);
            return row is >= 0 and < SheetLimits.MaxRows && column is >= 0 and < SheetLimits.MaxColumns;
        }

        private void AppendSheet(string? name)
        {
            if (name is not null)
            {
                _builder.Append('$').Append(QuoteSheet(name));
            }

            _builder.Append('.');
        }

        private void AppendCell(CellReference reference)
        {
            AppendColumn(reference);
            AppendRow(reference);
        }

        private void AppendColumn(CellReference reference)
        {
            if (reference.ColumnAbsolute)
            {
                _builder.Append('$');
            }

            _builder.Append(A1.ColumnName(Math.Clamp(reference.ColumnAt(origin.Column), 0, SheetLimits.LastColumn)));
        }

        private void AppendRow(CellReference reference)
        {
            if (reference.RowAbsolute)
            {
                _builder.Append('$');
            }

            _builder.Append((Math.Clamp(reference.RowAt(origin.Row), 0, SheetLimits.MaxRows - 1) + 1).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>A structured reference is written as the range it names now (OpenFormula has no tables).</summary>
        private bool WriteStructured(StructuredReferenceNode structured)
        {
            (Worksheet Sheet, TableDefinition Table)? found = structured.TableName is { } name
                ? StructuredReferences.FindTable(workbook, name)
                : sheet?.Tables.FirstOrDefault(table => table.Range.Contains(origin.Row, origin.Column)) is { } around ? (sheet, around) : null;
            if (found is not { } table || StructuredReferences.ResolveRange(table.Table, structured.Specifier, origin.Row) is not { } range)
            {
                return false;
            }

            var reference = new ReferenceNode(
                table.Sheet == sheet ? null : SheetReference.Single(table.Sheet.Id),
                new AreaReference(
                    new CellReference(range.FirstRow, range.FirstColumn, true, true),
                    new CellReference(range.LastRow, range.LastColumn, true, true),
                    range.IsSingleCell ? AreaKind.Cell : AreaKind.Range));
            return WriteReference(reference);
        }

        private void WriteString(string value) =>
            _builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');

        private void WriteArray(ArrayNode array)
        {
            _builder.Append('{');
            for (var row = 0; row < array.Rows; row++)
            {
                if (row > 0)
                {
                    _builder.Append('|');
                }

                for (var column = 0; column < array.Columns; column++)
                {
                    if (column > 0)
                    {
                        _builder.Append(';');
                    }

                    var value = array[row, column];
                    switch (value.Kind)
                    {
                        case CellValueKind.Number:
                            _builder.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                            break;
                        case CellValueKind.Text:
                            WriteString(value.Text);
                            break;
                        case CellValueKind.Boolean:
                            _builder.Append(value.Boolean ? "TRUE()" : "FALSE()");
                            break;
                        case CellValueKind.Error:
                            _builder.Append(CellErrors.NameOf(value.Error));
                            break;
                    }
                }
            }

            _builder.Append('}');
        }
    }

    // ---------------------------------------------------------------- OpenFormula → canonical

    /// <summary>
    /// The canonical text (English, "," between arguments, A1, without "=") of an ODF formula ("of:=…", "oooc:=…", "=…"), or null
    /// when it cannot be translated (another syntax, LibreOffice-only functions, external references, unions).
    /// </summary>
    public static string? ToCanonical(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        if (colon >= 0 && equals == colon + 1)
        {
            var prefix = text[..colon];
            if (prefix is not ("of" or "oooc"))
            {
                // msoxl:= (Excel syntax inside ODF) and other grammars are not translated.
                return prefix == "msoxl" ? text[(equals + 1)..] : null;
            }

            text = text[(equals + 1)..];
        }
        else if (equals == 0)
        {
            text = text[1..];
        }

        return new Reader(text).Translate();
    }

    private sealed class Reader(string text)
    {
        private readonly StringBuilder _builder = new();
        private int _index;
        private int _arrayDepth;

        public string? Translate()
        {
            while (_index < text.Length)
            {
                var character = text[_index];
                switch (character)
                {
                    case '"':
                        CopyString();
                        continue;
                    case '[':
                        if (!TranslateReference())
                        {
                            return null;
                        }

                        continue;
                    case '{':
                        _arrayDepth++;
                        _builder.Append('{');
                        _index++;
                        continue;
                    case '}':
                        _arrayDepth = Math.Max(0, _arrayDepth - 1);
                        _builder.Append('}');
                        _index++;
                        continue;
                    case ';':
                        _builder.Append(',');
                        _index++;
                        continue;
                    case '|' when _arrayDepth > 0:
                        _builder.Append(';');
                        _index++;
                        continue;
                    case '~':
                        return null;
                    case '!':
                        _builder.Append(' ');
                        _index++;
                        continue;
                    case '#':
                        if (!CopyError())
                        {
                            return null;
                        }

                        continue;
                    case '\'':
                        return null;
                }

                if (char.IsLetter(character) || character == '_' || character == '\\')
                {
                    if (!TranslateName())
                    {
                        return null;
                    }

                    continue;
                }

                if (char.IsAsciiDigit(character) || (character == '.' && _index + 1 < text.Length && char.IsAsciiDigit(text[_index + 1])))
                {
                    CopyNumber();
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    // White space is no intersection in OpenFormula ("!" is).
                    _index++;
                    continue;
                }

                _builder.Append(character);
                _index++;
            }

            return _builder.ToString();
        }

        private void CopyString()
        {
            _builder.Append('"');
            _index++;
            while (_index < text.Length)
            {
                if (text[_index] == '"')
                {
                    if (_index + 1 < text.Length && text[_index + 1] == '"')
                    {
                        _builder.Append("\"\"");
                        _index += 2;
                        continue;
                    }

                    _index++;
                    break;
                }

                _builder.Append(text[_index]);
                _index++;
            }

            _builder.Append('"');
        }

        private bool CopyError()
        {
            foreach (var error in CellErrors.All.Select(CellErrors.NameOf).Append("#NAME!").OrderByDescending(name => name.Length))
            {
                if (string.Compare(text, _index, error, 0, error.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    _builder.Append(error == "#NAME!" ? "#NAME?" : error);
                    _index += error.Length;
                    return true;
                }
            }

            return false;
        }

        private void CopyNumber()
        {
            var start = _index;
            while (_index < text.Length && (char.IsAsciiDigit(text[_index]) || text[_index] == '.'))
            {
                _index++;
            }

            if (_index < text.Length && text[_index] is 'e' or 'E')
            {
                var next = _index + 1;
                if (next < text.Length && text[next] is '+' or '-')
                {
                    next++;
                }

                if (next < text.Length && char.IsAsciiDigit(text[next]))
                {
                    _index = next;
                    while (_index < text.Length && char.IsAsciiDigit(text[_index]))
                    {
                        _index++;
                    }
                }
            }

            _builder.Append(text, start, _index - start);
        }

        private bool TranslateName()
        {
            var start = _index;
            while (_index < text.Length && (char.IsLetterOrDigit(text[_index]) || text[_index] is '_' or '.' or '\\'))
            {
                _index++;
            }

            var name = text[start.._index];
            var next = _index;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }

            if (next >= text.Length || text[next] != '(')
            {
                // A named range, a LET variable.
                _builder.Append(name);
                return true;
            }

            var upper = name.ToUpperInvariant();
            if (upper is "TRUE" or "FALSE")
            {
                var close = next + 1;
                while (close < text.Length && char.IsWhiteSpace(text[close]))
                {
                    close++;
                }

                if (close < text.Length && text[close] == ')')
                {
                    _builder.Append(upper);
                    _index = close + 1;
                    return true;
                }
            }

            if (FromOpenFormulaName.TryGetValue(upper, out var mapped))
            {
                upper = mapped;
            }
            else if (upper.StartsWith("COM.MICROSOFT.", StringComparison.Ordinal))
            {
                upper = upper["COM.MICROSOFT.".Length..];
            }
            else if (upper.StartsWith("_XLFN.", StringComparison.Ordinal))
            {
                upper = upper[6..];
            }

            if (FunctionRegistry.Find(upper) is null)
            {
                // LibreOffice's own functions (ORG.OPENOFFICE.…, MULTIPLE.OPERATIONS …) have no counterpart.
                return false;
            }

            _builder.Append(upper);
            _index = next;
            return true;
        }

        /// <summary>[.A1], [$Tabelle2.$A$1:.B3], [Tabelle1.A1:Tabelle3.B2], [.A:.C], [.1:.3], [.#REF!] → canonical.</summary>
        private bool TranslateReference()
        {
            var end = _index + 1;
            var quoted = false;
            while (end < text.Length && (quoted || text[end] != ']'))
            {
                if (text[end] == '\'')
                {
                    quoted = !quoted;
                }

                end++;
            }

            if (end >= text.Length)
            {
                return false;
            }

            var content = text[(_index + 1)..end];
            _index = end + 1;
            if (content.Contains('#', StringComparison.Ordinal) && content.Contains("#REF!", StringComparison.OrdinalIgnoreCase))
            {
                _builder.Append("#REF!");
                return true;
            }

            if (content.Contains('#', StringComparison.Ordinal) || content.Contains("://", StringComparison.Ordinal))
            {
                return false; // another document
            }

            var parts = SplitRange(content);
            if (parts.Count is 0 or > 2)
            {
                return false;
            }

            var first = ParsePart(parts[0]);
            var last = parts.Count > 1 ? ParsePart(parts[1]) : null;
            if (first is null || (parts.Count > 1 && last is null))
            {
                return false;
            }

            var firstSheet = first.Value.Sheet;
            var lastSheet = last?.Sheet;
            if (firstSheet is not null)
            {
                if (lastSheet is not null && !string.Equals(lastSheet, firstSheet, StringComparison.Ordinal))
                {
                    var plain = FormulaFormatter.QuoteSheetName(firstSheet) == firstSheet && FormulaFormatter.QuoteSheetName(lastSheet) == lastSheet;
                    _builder.Append(plain
                        ? $"{firstSheet}:{lastSheet}!"
                        : $"'{firstSheet.Replace("'", "''", StringComparison.Ordinal)}:{lastSheet.Replace("'", "''", StringComparison.Ordinal)}'!");
                }
                else
                {
                    _builder.Append(FormulaFormatter.QuoteSheetName(firstSheet)).Append('!');
                }
            }

            _builder.Append(first.Value.Address);
            if (last is { } second && !(string.Equals(second.Address, first.Value.Address, StringComparison.Ordinal) && second.Address.Any(char.IsAsciiLetter) && second.Address.Any(char.IsAsciiDigit)))
            {
                _builder.Append(':').Append(second.Address);
            }

            return true;
        }

        private static List<string> SplitRange(string content)
        {
            var parts = new List<string>();
            var quoted = false;
            var start = 0;
            for (var index = 0; index < content.Length; index++)
            {
                if (content[index] == '\'')
                {
                    quoted = !quoted;
                }
                else if (content[index] == ':' && !quoted)
                {
                    parts.Add(content[start..index]);
                    start = index + 1;
                }
            }

            parts.Add(content[start..]);
            return parts;
        }

        /// <summary>"$'Mein Blatt'.$A$1" → (Mein Blatt, $A$1); ".A1" → (null, A1).</summary>
        private static (string? Sheet, string Address)? ParsePart(string part)
        {
            part = part.Trim();
            string? sheet = null;
            var index = 0;
            if (index < part.Length && part[index] == '$')
            {
                index++;
            }

            if (index < part.Length && part[index] == '\'')
            {
                var builder = new StringBuilder();
                index++;
                while (index < part.Length)
                {
                    if (part[index] == '\'')
                    {
                        if (index + 1 < part.Length && part[index + 1] == '\'')
                        {
                            builder.Append('\'');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    builder.Append(part[index]);
                    index++;
                }

                sheet = builder.ToString();
            }
            else
            {
                var dot = part.LastIndexOf('.');
                if (dot < 0)
                {
                    return null;
                }

                if (dot > index)
                {
                    sheet = part[index..dot];
                }

                index = dot;
            }

            if (index >= part.Length || part[index] != '.')
            {
                return null;
            }

            var address = part[(index + 1)..];
            return IsAddress(address) ? (sheet, address) : null;
        }

        /// <summary>$A$1, A1, $A, 1, $1 (a cell, a column or a row).</summary>
        private static bool IsAddress(string address)
        {
            var index = 0;
            if (index < address.Length && address[index] == '$')
            {
                index++;
            }

            var letters = 0;
            while (index < address.Length && char.IsAsciiLetter(address[index]))
            {
                index++;
                letters++;
            }

            if (letters > 3)
            {
                return false;
            }

            if (index < address.Length && address[index] == '$')
            {
                index++;
            }

            var digits = 0;
            while (index < address.Length && char.IsAsciiDigit(address[index]))
            {
                index++;
                digits++;
            }

            return index == address.Length && (letters > 0 || digits > 0) && !(letters == 0 && address.EndsWith('$'));
        }
    }
}
